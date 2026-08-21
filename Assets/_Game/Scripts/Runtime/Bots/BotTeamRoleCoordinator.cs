using System;
using System.Collections.Generic;
using UnityEngine;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Bots
{
    /// <summary>Owns one team's deterministic role map and pickup etiquette candidates.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-250)]
    public sealed class BotTeamRoleCoordinator : MonoBehaviour
    {
        private const int TeamRosterSize = 3;
        public const float ExpectedEvaluationInterval = 0.5f;
        public const float ExpectedRoleHoldSeconds = 2f;
        public const float ExpectedSwitchMargin = 0.15f;
        private const int MaxParticipantSlotId = 6;

        [Header("Team")]
        [SerializeField] private ParticipantTeam team = ParticipantTeam.Blue;
        [SerializeField] private MatchController match;
        [SerializeField] private BotNavigationGraph navigationGraph;
        [SerializeField] private ParticipantState[] participants = new ParticipantState[TeamRosterSize];
        [SerializeField] private BotPerception[] perceptions = new BotPerception[TeamRosterSize];

        [Header("Role and Pickup Policy")]
        [SerializeField, Min(0f)] private float evaluationInterval = ExpectedEvaluationInterval;
        [SerializeField, Min(0f)] private float roleHoldSeconds = ExpectedRoleHoldSeconds;
        [SerializeField] private float switchMargin = ExpectedSwitchMargin;
        [SerializeField, Min(0f)] private float humanShotgunYieldDistance = BotTargetRules.HumanShotgunYieldDistance;
        [SerializeField, Min(0f)] private float healthYieldDistance = BotTargetRules.HealthYieldDistance;
        [SerializeField, Range(0f, 1f)] private float criticalHealthRatio = BotTargetRules.CriticalHealthRatio;

        private readonly float[] roleHeldSeconds = new float[MaxParticipantSlotId];
        private BotTargetCandidate[][] pickupCandidates = new BotTargetCandidate[0][];
        private BotRoleAssignmentSet assignments = BotRoleAssignmentSet.Empty;
        private BotCornerState cornerState = BotCornerState.Inactive;
        private BotCornerAssignmentSet cornerAssignments = BotCornerAssignmentSet.Empty;
        private Vector3 previousCornerBallPosition;
        private readonly Vector3[] previousCornerParticipantPositions = new Vector3[TeamRosterSize];
        private int previousCornerObserverSlot = -1;
        private bool hasCornerBall;
        private bool hasCornerParticipantPositions;
        private float elapsedSinceEvaluation;
        private int previousAliveMask;
        private bool hasAliveMask;
        private bool firstEvaluation = true;
        private bool compositionValid;
        private bool resetEventSubscribed;

        private void OnEnable()
        {
            compositionValid = ValidateComposition();
            ClearState();
            if (!compositionValid)
            {
                enabled = false;
                return;
            }

            if (!resetEventSubscribed)
            {
                match.CoordinatedResetRequested += OnCoordinatedResetRequested;
                resetEventSubscribed = true;
            }

            EvaluateRoles(true);
        }

        private void OnDisable()
        {
            if (resetEventSubscribed && match != null)
            {
                match.CoordinatedResetRequested -= OnCoordinatedResetRequested;
            }

            resetEventSubscribed = false;
            ClearState();
        }

        private void FixedUpdate()
        {
            if (!compositionValid)
            {
                return;
            }

            var aliveMask = BuildAliveMask();
            var aliveSetChanged = hasAliveMask && aliveMask != previousAliveMask;
            previousAliveMask = aliveMask;
            hasAliveMask = true;

            if (IsPlaying())
            {
                var delta = SafeFixedDeltaTime();
                elapsedSinceEvaluation += delta;
                AdvanceRoleHolds(delta);
                UpdateCornerState(delta, aliveSetChanged);
            }

            if (BotCoordinatorScheduleRules.ShouldEvaluate(
                    elapsedSinceEvaluation,
                    evaluationInterval,
                    firstEvaluation,
                    aliveSetChanged))
            {
                EvaluateRoles(aliveSetChanged);
            }
        }

        public bool TryGetAssignedRole(int botSlotId, out BotRole role)
        {
            if (assignments.TryGetAssignment(botSlotId, out var assignment))
            {
                role = assignment.Role;
                return true;
            }

            role = BotRole.Attacker;
            return false;
        }

        public bool TryGetCornerAssignment(int botSlotId, out BotCornerAssignment assignment)
        {
            return cornerAssignments.TryGetAssignment(botSlotId, out assignment);
        }

        public int GetPickupCandidateCount(int observerSlotId)
        {
            var perception = GetPerception(observerSlotId);
            if (perception == null)
            {
                return 0;
            }

            var candidates = BuildPickupCandidates(perception);
            return candidates.Length;
        }

        public bool TryGetPickupCandidate(int observerSlotId, int candidateIndex, out BotTargetCandidate candidate)
        {
            var perception = GetPerception(observerSlotId);
            if (perception == null || candidateIndex < 0)
            {
                candidate = default(BotTargetCandidate);
                return false;
            }

            var candidates = BuildPickupCandidates(perception);
            if (candidateIndex >= candidates.Length)
            {
                candidate = default(BotTargetCandidate);
                return false;
            }

            candidate = candidates[candidateIndex];
            return true;
        }

        private void EvaluateRoles(bool aliveSetChanged)
        {
            var previous = assignments;
            var perception = GetFirstPerception();
            var ownGoalPosition = perception != null ? perception.OwnGoalPosition : Vector3.zero;
            var enemyGoalPosition = perception != null ? perception.EnemyGoalPosition : Vector3.zero;
            var hasBallObservation = false;
            var ballPosition = Vector3.zero;
            if (perception != null && perception.TryGetBallObservation(out var ballObservation))
            {
                hasBallObservation = ballObservation.HasObservation;
                ballPosition = ballObservation.Position;
            }

            var candidates = new BotRoleCandidate[TeamRosterSize];
            for (var i = 0; i < participants.Length; i++)
            {
                var participant = participants[i];
                var current = default(BotRoleAssignment);
                var hasCurrentRole = participant != null && previous.TryGetAssignment(participant.SlotId, out current);
                var currentRole = hasCurrentRole ? current.Role : BotRole.Attacker;
                var held = participant != null ? GetHeldSeconds(participant.SlotId) : 0f;
                candidates[i] = participant == null
                    ? default(BotRoleCandidate)
                    : new BotRoleCandidate(
                        participant.SlotId,
                        participant.IsLocalParticipant,
                        participant.IsAlive,
                        participant.transform.position,
                        hasCurrentRole,
                        currentRole,
                        held);
            }

            assignments = BotRoleRules.Assign(
                new BotRoleContext(
                    hasBallObservation,
                    ballPosition,
                    ownGoalPosition,
                    enemyGoalPosition,
                    aliveSetChanged),
                candidates[0],
                candidates[1],
                candidates[2]);

            for (var i = 0; i < participants.Length; i++)
            {
                var participant = participants[i];
                if (participant == null || participant.SlotId < 0 || participant.SlotId >= roleHeldSeconds.Length)
                {
                    continue;
                }

                if (!assignments.TryGetAssignment(participant.SlotId, out var next))
                {
                    roleHeldSeconds[participant.SlotId] = 0f;
                    continue;
                }

                var old = default(BotRoleAssignment);
                var retained = !aliveSetChanged && previous.TryGetAssignment(participant.SlotId, out old) && old.Role == next.Role;
                if (!retained)
                {
                    roleHeldSeconds[participant.SlotId] = 0f;
                }
            }

            elapsedSinceEvaluation = 0f;
            firstEvaluation = false;
            RefreshCandidateCache();
        }

        private void UpdateCornerState(float deltaSeconds, bool aliveSetChanged)
        {
            if (navigationGraph == null || navigationGraph.ArenaBounds == null || perceptions == null)
            {
                cornerState = BotCornerState.Inactive;
                cornerAssignments = BotCornerAssignmentSet.Empty;
                hasCornerBall = false;
                previousCornerObserverSlot = -1;
                return;
            }

            var observations = new BotCornerBallObservation[perceptions.Length];
            for (var i = 0; i < perceptions.Length; i++)
            {
                var perception = perceptions[i];
                var observation = default(BotBallObservation);
                if (perception != null)
                {
                    perception.TryGetBallObservation(out observation);
                }

                observations[i] = new BotCornerBallObservation(
                    perception != null ? perception.ObserverSlotId : -1,
                    observation);
            }

            var hadPreviousState = cornerState.Active || cornerState.DwellSeconds > 0f;
            var previousState = cornerState;
            cornerState = BotCornerRules.Advance(cornerState, navigationGraph.ArenaBounds, deltaSeconds, observations);

            var hasFreshBall = BotCornerRules.TrySelectFreshestBall(observations, out var selectedBall);
            var selectedPosition = hasFreshBall ? selectedBall.Observation.Position : Vector3.zero;
            var selectedObserverSlot = hasFreshBall ? selectedBall.ObserverSlotId : -1;
            var ballChanged = hasFreshBall != hasCornerBall ||
                selectedObserverSlot != previousCornerObserverSlot ||
                (hasFreshBall && (!IsFinite(previousCornerBallPosition) ||
                    Vector3.Distance(previousCornerBallPosition, selectedPosition) > 0.0001f));
            var stateChanged = previousState.Active != cornerState.Active ||
                Mathf.Abs(previousState.DwellSeconds - cornerState.DwellSeconds) > 0.0001f ||
                (previousState.Active && (previousState.Corner != cornerState.Corner ||
                    previousState.ReleaseDirection != cornerState.ReleaseDirection));
            var participantPositionsChanged = HaveCornerParticipantPositionsChanged();

            previousCornerBallPosition = selectedPosition;
            previousCornerObserverSlot = selectedObserverSlot;
            hasCornerBall = hasFreshBall;

            if (!cornerState.Active || !hasFreshBall)
            {
                cornerAssignments = BotCornerAssignmentSet.Empty;
                return;
            }

            if (aliveSetChanged || stateChanged || ballChanged || participantPositionsChanged || !hadPreviousState)
            {
                var enemyGoalPosition = GetEnemyGoalPosition(selectedBall.ObserverSlotId);
                cornerAssignments = BotCornerRules.Assign(
                    cornerState,
                    navigationGraph.ArenaBounds,
                    selectedBall,
                    enemyGoalPosition,
                    BuildCornerParticipants());
            }
        }

        private Vector3 GetEnemyGoalPosition(int observerSlotId)
        {
            if (perceptions == null)
            {
                return Vector3.zero;
            }

            for (var i = 0; i < perceptions.Length; i++)
            {
                if (perceptions[i] != null && perceptions[i].ObserverSlotId == observerSlotId &&
                    IsFinite(perceptions[i].EnemyGoalPosition))
                {
                    return perceptions[i].EnemyGoalPosition;
                }
            }

            for (var i = 0; i < perceptions.Length; i++)
            {
                if (perceptions[i] != null && IsFinite(perceptions[i].EnemyGoalPosition))
                {
                    return perceptions[i].EnemyGoalPosition;
                }
            }

            return Vector3.zero;
        }

        private BotCornerParticipant[] BuildCornerParticipants()
        {
            var result = new BotCornerParticipant[participants != null ? participants.Length : 0];
            for (var i = 0; i < result.Length; i++)
            {
                var participant = participants[i];
                result[i] = participant == null
                    ? default(BotCornerParticipant)
                    : new BotCornerParticipant(
                        participant.SlotId,
                        participant.IsLocalParticipant,
                        participant.IsAlive,
                        participant.transform.position);
            }

            return result;
        }

        private bool HaveCornerParticipantPositionsChanged()
        {
            var changed = !hasCornerParticipantPositions;
            for (var i = 0; i < previousCornerParticipantPositions.Length; i++)
            {
                var participant = participants != null && i < participants.Length ? participants[i] : null;
                var position = participant != null && IsFinite(participant.transform.position)
                    ? participant.transform.position
                    : Vector3.zero;
                if (!hasCornerParticipantPositions ||
                    Vector3.Distance(previousCornerParticipantPositions[i], position) > 0.0001f)
                {
                    changed = true;
                }

                previousCornerParticipantPositions[i] = position;
            }

            hasCornerParticipantPositions = true;
            return changed;
        }

        private void RefreshCandidateCache()
        {
            pickupCandidates = new BotTargetCandidate[perceptions.Length][];
            for (var i = 0; i < perceptions.Length; i++)
            {
                pickupCandidates[i] = perceptions[i] == null
                    ? new BotTargetCandidate[0]
                    : BuildPickupCandidates(perceptions[i]);
            }
        }

        private BotTargetCandidate[] BuildPickupCandidates(BotPerception perception)
        {
            if (perception == null || !perception.TryGetParticipantObservation(perception.ObserverSlotId, out var observer) ||
                !observer.HasObservation || !observer.IsAlive)
            {
                return new BotTargetCandidate[0];
            }

            var candidates = new List<BotTargetCandidate>(5);
            for (var stableId = 0; stableId < 5; stableId++)
            {
                if (!perception.TryGetPickupObservation(stableId, out var pickup) ||
                    !pickup.HasObservation || !pickup.IsAvailable || !CanUsePickup(observer, pickup))
                {
                    continue;
                }

                if (pickup.Kind == BotPickupKind.Health && ShouldYieldHealth(perception, observer))
                {
                    continue;
                }

                if (pickup.Kind == BotPickupKind.Shotgun && ShouldYieldShotgun(perception, observer))
                {
                    continue;
                }

                var key = GetPickupKey(pickup);
                if (key.Kind == BotTargetKind.None || !IsFinite(pickup.Position))
                {
                    continue;
                }

                candidates.Add(new BotTargetCandidate(
                    key,
                    BotTargetRules.ScoreFor(key.Kind),
                    pickup.Position,
                    pickup.Position,
                    false,
                    false,
                    true,
                    0f,
                    Mathf.Max(perception.GameplayTime + evaluationInterval, 0f)));
            }

            return candidates.ToArray();
        }

        private bool CanUsePickup(BotParticipantObservation observer, BotPickupObservation pickup)
        {
            if (pickup.Kind == BotPickupKind.Health)
            {
                return IsFinite(observer.Health) && IsFinite(observer.MaxHealth) &&
                    observer.MaxHealth > 0f && observer.Health < observer.MaxHealth;
            }

            if (pickup.Kind == BotPickupKind.Shotgun)
            {
                return observer.ShotgunShellCapacity > 0 &&
                    (!observer.HasShotgun || observer.ShotgunShells < observer.ShotgunShellCapacity);
            }

            return observer.HasShotgun && observer.ShotgunShellCapacity > 0 &&
                observer.ShotgunShells < observer.ShotgunShellCapacity;
        }

        private bool ShouldYieldShotgun(BotPerception perception, BotParticipantObservation observer)
        {
            for (var i = 0; i < participants.Length; i++)
            {
                var teammate = participants[i];
                if (teammate == null || teammate.SlotId == observer.SlotId || !teammate.IsLocalParticipant)
                {
                    continue;
                }

                if (!perception.TryGetParticipantObservation(teammate.SlotId, out var teammateObservation))
                {
                    continue;
                }

                if (BotTargetRules.ShouldYieldShotgun(observer, teammateObservation) &&
                    DistanceAtMost(observer.Position, teammateObservation.Position, humanShotgunYieldDistance))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldYieldHealth(BotPerception perception, BotParticipantObservation observer)
        {
            for (var i = 0; i < participants.Length; i++)
            {
                var teammate = participants[i];
                if (teammate == null || teammate.SlotId == observer.SlotId)
                {
                    continue;
                }

                if (!perception.TryGetParticipantObservation(teammate.SlotId, out var teammateObservation))
                {
                    continue;
                }

                if (!BotTargetRules.ShouldYieldHealth(observer, teammateObservation) ||
                    !DistanceAtMost(observer.Position, teammateObservation.Position, healthYieldDistance))
                {
                    continue;
                }

                if (TryGetHealthRatio(teammateObservation, out var teammateRatio) && teammateRatio <= criticalHealthRatio)
                {
                    return true;
                }
            }

            return false;
        }

        private static BotTargetKey GetPickupKey(BotPickupObservation pickup)
        {
            switch (pickup.Kind)
            {
                case BotPickupKind.Health:
                    return pickup.StableId >= 0 && pickup.StableId <= 1
                        ? new BotTargetKey(BotTargetKind.HealthPickup, pickup.StableId)
                        : new BotTargetKey(BotTargetKind.None, 0);
                case BotPickupKind.Shotgun:
                    return pickup.StableId == 2
                        ? new BotTargetKey(BotTargetKind.ShotgunPickup, 0)
                        : new BotTargetKey(BotTargetKind.None, 0);
                case BotPickupKind.Ammo:
                    return pickup.StableId >= 3 && pickup.StableId <= 4
                        ? new BotTargetKey(BotTargetKind.AmmoPickup, pickup.StableId - 3)
                        : new BotTargetKey(BotTargetKind.None, 0);
                default:
                    return new BotTargetKey(BotTargetKind.None, 0);
            }
        }

        private BotPerception GetFirstPerception()
        {
            if (perceptions == null)
            {
                return null;
            }

            BotPerception first = null;
            for (var i = 0; i < perceptions.Length; i++)
            {
                var perception = perceptions[i];
                if (perception == null)
                {
                    continue;
                }

                if (first == null || perception.ObserverSlotId < first.ObserverSlotId)
                {
                    first = perception;
                }
            }

            return first;
        }

        private BotPerception GetPerception(int observerSlotId)
        {
            if (perceptions == null)
            {
                return null;
            }

            for (var i = 0; i < perceptions.Length; i++)
            {
                if (perceptions[i] != null && perceptions[i].ObserverSlotId == observerSlotId)
                {
                    return perceptions[i];
                }
            }

            return null;
        }

        private int BuildAliveMask()
        {
            var mask = 0;
            for (var i = 0; i < participants.Length; i++)
            {
                var participant = participants[i];
                if (participant == null || participant.IsLocalParticipant || !participant.IsAlive)
                {
                    continue;
                }

                if (participant.SlotId >= 0 && participant.SlotId < 30)
                {
                    mask |= 1 << participant.SlotId;
                }
            }

            return mask;
        }

        private void AdvanceRoleHolds(float delta)
        {
            if (delta <= 0f)
            {
                return;
            }

            for (var i = 0; i < participants.Length; i++)
            {
                var participant = participants[i];
                if (participant == null || participant.SlotId < 0 || participant.SlotId >= roleHeldSeconds.Length ||
                    !assignments.TryGetAssignment(participant.SlotId, out _))
                {
                    continue;
                }

                roleHeldSeconds[participant.SlotId] = Mathf.Max(roleHeldSeconds[participant.SlotId] + delta, 0f);
            }
        }

        private float GetHeldSeconds(int slotId)
        {
            return slotId >= 0 && slotId < roleHeldSeconds.Length ? roleHeldSeconds[slotId] : 0f;
        }

        private bool ValidateComposition()
        {
            var expectedPerceptionCount = team == ParticipantTeam.Blue ? 2 : 3;
            if (match == null || navigationGraph == null || navigationGraph.ArenaBounds == null ||
                !navigationGraph.ArenaBounds.IsValid || participants == null || participants.Length != TeamRosterSize ||
                perceptions == null || perceptions.Length != expectedPerceptionCount)
            {
                Debug.LogError("BotTeamRoleCoordinator requires serialized match, navigation graph, exact three participants, and exact non-local perceptions.", this);
                return false;
            }

            var participantSet = new HashSet<ParticipantState>();
            var slotSet = new HashSet<int>();
            var expectedFirstSlot = team == ParticipantTeam.Blue ? 0 : 3;
            for (var i = 0; i < participants.Length; i++)
            {
                var participant = participants[i];
                var expectedSlot = expectedFirstSlot + i;
                if (participant == null || !participantSet.Add(participant) || !slotSet.Add(participant.SlotId) ||
                    participant.Team != team || participant.SlotId != expectedSlot)
                {
                    Debug.LogError("BotTeamRoleCoordinator requires the team's exact three ordered participant slots.", this);
                    return false;
                }

                var expectedLocal = team == ParticipantTeam.Blue && i == 0;
                if (participant.IsLocalParticipant != expectedLocal)
                {
                    Debug.LogError("BotTeamRoleCoordinator requires only Blue slot 0 to be local; all role candidates must be non-local.", this);
                    return false;
                }
            }

            var perceptionSlots = new HashSet<int>();
            for (var i = 0; i < perceptions.Length; i++)
            {
                var perception = perceptions[i];
                var expectedSlot = expectedFirstSlot + (team == ParticipantTeam.Blue ? 1 : 0) + i;
                if (perception == null || !perceptionSlots.Add(perception.ObserverSlotId) ||
                    perception.ObserverSlotId != expectedSlot)
                {
                    Debug.LogError("BotTeamRoleCoordinator requires one non-local perception per bot slot in team order.", this);
                    return false;
                }
            }

            if (!IsFinite(evaluationInterval) || evaluationInterval <= 0f ||
                !IsFinite(roleHoldSeconds) || roleHoldSeconds < 0f ||
                !IsFinite(switchMargin) || switchMargin < 0f ||
                !IsFinite(humanShotgunYieldDistance) || humanShotgunYieldDistance <= 0f ||
                !IsFinite(healthYieldDistance) || healthYieldDistance <= 0f ||
                !IsFinite(criticalHealthRatio) || criticalHealthRatio <= 0f || criticalHealthRatio > 1f)
            {
                Debug.LogError("BotTeamRoleCoordinator requires positive finite tuning values, a non-negative switch margin, and a critical health ratio in (0, 1]; check prefab serialization.", this);
                return false;
            }

            return true;
        }

        private void OnCoordinatedResetRequested(MatchResetReason reason)
        {
            ClearState();
        }

        private void ClearState()
        {
            assignments = BotRoleAssignmentSet.Empty;
            cornerState = BotCornerState.Inactive;
            cornerAssignments = BotCornerAssignmentSet.Empty;
            previousCornerBallPosition = Vector3.zero;
            previousCornerObserverSlot = -1;
            hasCornerBall = false;
            hasCornerParticipantPositions = false;
            for (var i = 0; i < previousCornerParticipantPositions.Length; i++)
            {
                previousCornerParticipantPositions[i] = Vector3.zero;
            }
            elapsedSinceEvaluation = 0f;
            previousAliveMask = 0;
            hasAliveMask = false;
            firstEvaluation = true;
            for (var i = 0; i < roleHeldSeconds.Length; i++)
            {
                roleHeldSeconds[i] = 0f;
            }

            pickupCandidates = new BotTargetCandidate[perceptions != null ? perceptions.Length : 0][];
        }

        private bool IsPlaying()
        {
            return match != null && match.State == MatchController.MatchState.Playing;
        }

        private static float SafeFixedDeltaTime()
        {
            return IsFinite(Time.fixedDeltaTime) && Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0f;
        }

        private static bool DistanceAtMost(Vector3 first, Vector3 second, float limit)
        {
            return IsFinite(first) && IsFinite(second) && IsFinite(limit) && limit >= 0f &&
                Vector3.Distance(first, second) <= limit;
        }

        private static bool TryGetHealthRatio(BotParticipantObservation participant, out float ratio)
        {
            ratio = 0f;
            if (!IsFinite(participant.Health) || participant.Health < 0f ||
                !IsFinite(participant.MaxHealth) || participant.MaxHealth <= 0f)
            {
                return false;
            }

            ratio = participant.Health / participant.MaxHealth;
            return IsFinite(ratio);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
