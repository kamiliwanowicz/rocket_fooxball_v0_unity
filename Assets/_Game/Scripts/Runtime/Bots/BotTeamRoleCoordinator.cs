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
        private const float ExpectedEvaluationInterval = 0.5f;
        private const float ExpectedRoleHoldSeconds = 2f;
        private const float ExpectedSwitchMargin = 0.15f;
        private const float ExpectedHumanShotgunYieldDistance = 12f;
        private const float ExpectedHealthYieldDistance = 10f;
        private const float ExpectedCriticalHealthRatio = 0.30f;
        private const int MaxParticipantSlotId = 6;

        [Header("Team")]
        [SerializeField] private ParticipantTeam team = ParticipantTeam.Blue;
        [SerializeField] private MatchController match;
        [SerializeField] private ParticipantState[] participants = new ParticipantState[TeamRosterSize];
        [SerializeField] private BotPerception[] perceptions = new BotPerception[TeamRosterSize];

        [Header("Role and Pickup Policy")]
        [SerializeField, Min(0f)] private float evaluationInterval = ExpectedEvaluationInterval;
        [SerializeField, Min(0f)] private float roleHoldSeconds = ExpectedRoleHoldSeconds;
        [SerializeField] private float switchMargin = ExpectedSwitchMargin;
        [SerializeField, Min(0f)] private float humanShotgunYieldDistance = ExpectedHumanShotgunYieldDistance;
        [SerializeField, Min(0f)] private float healthYieldDistance = ExpectedHealthYieldDistance;
        [SerializeField, Range(0f, 1f)] private float criticalHealthRatio = ExpectedCriticalHealthRatio;

        private readonly float[] roleHeldSeconds = new float[MaxParticipantSlotId];
        private BotTargetCandidate[][] pickupCandidates = new BotTargetCandidate[0][];
        private BotRoleAssignmentSet assignments = BotRoleAssignmentSet.Empty;
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
            if (match == null || participants == null || participants.Length != TeamRosterSize ||
                perceptions == null || perceptions.Length != expectedPerceptionCount)
            {
                Debug.LogError("BotTeamRoleCoordinator requires serialized match, exact three participants, and exact non-local perceptions.", this);
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

            if (!IsFinite(evaluationInterval) || !Mathf.Approximately(evaluationInterval, ExpectedEvaluationInterval) ||
                !IsFinite(roleHoldSeconds) || !Mathf.Approximately(roleHoldSeconds, ExpectedRoleHoldSeconds) ||
                !IsFinite(switchMargin) || !Mathf.Approximately(switchMargin, ExpectedSwitchMargin) ||
                !IsFinite(humanShotgunYieldDistance) || !Mathf.Approximately(humanShotgunYieldDistance, ExpectedHumanShotgunYieldDistance) ||
                !IsFinite(healthYieldDistance) || !Mathf.Approximately(healthYieldDistance, ExpectedHealthYieldDistance) ||
                !IsFinite(criticalHealthRatio) || !Mathf.Approximately(criticalHealthRatio, ExpectedCriticalHealthRatio))
            {
                Debug.LogError("BotTeamRoleCoordinator requires tuning 0.5s/2s/0.15/12m/10m/0.30.", this);
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
