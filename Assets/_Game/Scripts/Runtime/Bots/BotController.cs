using UnityEngine;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Weapons;

namespace RocketFooxball.Runtime.Bots
{
    /// <summary>Owns one non-local participant's fixed-step bot intent lifecycle.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed class BotController : MonoBehaviour
    {
        private const float ExpectedMaxPitchDegrees = 89f;
        private const float ExpectedJumpProbeDistance = 4f;
        private const int MaxPickupCandidates = 5;
        private const string CompositionError =
            "BotController requires a valid non-local participant and serialized match, motor, Head, kick, launcher, shotgun, navigator, perception, role coordinator, combat mask, and exact 89-degree/4-meter tuning.";

        [Header("References")]
        [SerializeField] private ParticipantState participant;
        [SerializeField] private MatchController match;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private Transform head;
        [SerializeField] private BallKick kick;
        [SerializeField] private RocketLauncher launcher;
        [SerializeField] private ShotgunWeapon shotgun;
        [SerializeField] private BotNavigator navigator;
        [SerializeField] private BotPerception perception;
        [SerializeField] private BotTeamRoleCoordinator roleCoordinator;

        [Header("Combat and Aim")]
        [SerializeField] private LayerMask combatObstacleMask = ~0;
        [SerializeField, Range(0f, 89f)] private float maxPitchDegrees = ExpectedMaxPitchDegrees;
        [SerializeField, Min(0f)] private float jumpProbeDistance = ExpectedJumpProbeDistance;

        private BotDifficulty difficulty = BotDifficulty.Medium;
        private bool simulationEnabled = true;
        private bool paused;
        private bool compositionValid;

        private int reactionOrdinal;
        private int decisionOrdinal;
        private float nextReactionTime;
        private float nextDecisionTime;
        private bool reactionScheduled;
        private bool decisionScheduled;
        private BotReactedSnapshot reactedSnapshot;

        // These owners are populated by the decision/aim/action phase in the next slice.
        private BotTargetSelection activeTarget = BotTargetSelection.None;
        private BotNavigationSteeringResult navigationSteering = BotNavigationSteeringResult.Invalid;
        private BotAimSolution ballAim = BotAimSolution.Invalid;
        private BotAimSolution enemyAim = BotAimSolution.Invalid;
        private BotCombatResult lastCombatResult = BotCombatResult.None;
        private Vector3 storedAimDirection;
        private Vector3 storedSteeringDirection;
        private bool hasAim;
        private bool hasRoute;

        public ParticipantState Participant => participant;
        public BotDifficulty Difficulty => difficulty;
        public bool SimulationEnabled => simulationEnabled;

        private void OnEnable()
        {
            compositionValid = ValidateComposition();
            ClearState();
            if (!compositionValid)
            {
                enabled = false;
                return;
            }

            // Match difficulty is the source of the initial tier. Later false->true transitions
            // refresh it again; ResetState deliberately preserves the current tier.
            difficulty = match.GetBotDifficulty(participant.Team);
            ArmSchedule();
        }

        private void OnDisable()
        {
            ClearState();
        }

        private void FixedUpdate()
        {
            if (!compositionValid || !simulationEnabled || paused)
            {
                return;
            }

            var gameplayTime = GetGameplayTime();
            if (!IsFinite(gameplayTime))
            {
                return;
            }

            if (reactionScheduled && gameplayTime >= nextReactionTime)
            {
                RunReaction(gameplayTime);
            }

            if (decisionScheduled && gameplayTime >= nextDecisionTime)
            {
                RunDecision(gameplayTime);
            }
        }

        public bool SetDifficulty(BotDifficulty nextDifficulty)
        {
            if (!IsSupportedDifficulty(nextDifficulty))
            {
                FailComposition();
                return false;
            }

            difficulty = nextDifficulty;
            return true;
        }

        public void SetSimulationEnabled(bool enabledState)
        {
            if (!enabledState)
            {
                simulationEnabled = false;
                ClearState();
                return;
            }

            if (simulationEnabled)
            {
                return;
            }

            simulationEnabled = true;
            if (!compositionValid)
            {
                return;
            }

            difficulty = match.GetBotDifficulty(participant.Team);
            ClearState();
            ArmSchedule();
        }

        public void SetPaused(bool pausedState)
        {
            if (paused == pausedState)
            {
                return;
            }

            paused = pausedState;
        }

        public void ResetState()
        {
            ClearState();
            if (simulationEnabled && compositionValid)
            {
                ArmSchedule();
            }
        }

        private void ArmSchedule()
        {
            var gameplayTime = GetGameplayTime();
            if (!IsFinite(gameplayTime))
            {
                gameplayTime = 0f;
            }

            var delay = BotDifficultyRules.GetReactionDelay(difficulty, participant.SlotId, reactionOrdinal);
            nextReactionTime = gameplayTime + delay;
            reactionScheduled = IsFinite(nextReactionTime);
            decisionScheduled = false;
            nextDecisionTime = 0f;
        }

        private void RunReaction(float gameplayTime)
        {
            reactedSnapshot = CaptureReaction();
            reactionOrdinal++;
            nextReactionTime = reactedSnapshot.GameplayTime +
                BotDifficultyRules.GetReactionDelay(difficulty, participant.SlotId, reactionOrdinal);
            reactionScheduled = IsFinite(nextReactionTime);

            // The first decision for a copied snapshot is immediate. Subsequent decisions
            // are scheduled independently from reaction timing.
            nextDecisionTime = reactedSnapshot.GameplayTime;
            decisionScheduled = reactedSnapshot.HasData && IsFinite(nextDecisionTime);
        }

        private void RunDecision(float gameplayTime)
        {
            if (!reactedSnapshot.HasData)
            {
                decisionScheduled = false;
                return;
            }

            // Decision ownership is intentionally a no-op in this slice. It still advances
            // its own deterministic schedule so later phases cannot double-run a slot.
            decisionOrdinal++;
            nextDecisionTime = gameplayTime +
                BotDifficultyRules.GetDecisionDelay(difficulty, participant.SlotId, decisionOrdinal);
            decisionScheduled = IsFinite(nextDecisionTime);
        }

        private BotReactedSnapshot CaptureReaction()
        {
            var gameplayTime = perception.GameplayTime;
            var ownGoalPosition = perception.OwnGoalPosition;
            var enemyGoalPosition = perception.EnemyGoalPosition;

            var slot0 = default(BotParticipantObservation);
            var slot1 = default(BotParticipantObservation);
            var slot2 = default(BotParticipantObservation);
            var slot3 = default(BotParticipantObservation);
            var slot4 = default(BotParticipantObservation);
            var slot5 = default(BotParticipantObservation);
            perception.TryGetParticipantObservation(0, out slot0);
            perception.TryGetParticipantObservation(1, out slot1);
            perception.TryGetParticipantObservation(2, out slot2);
            perception.TryGetParticipantObservation(3, out slot3);
            perception.TryGetParticipantObservation(4, out slot4);
            perception.TryGetParticipantObservation(5, out slot5);

            var selfObservation = GetSlotObservation(participant.SlotId, slot0, slot1, slot2, slot3, slot4, slot5);
            var ballObservation = default(BotBallObservation);
            perception.TryGetBallObservation(out ballObservation);

            var role = BotRole.Attacker;
            if (!roleCoordinator.TryGetAssignedRole(participant.SlotId, out role))
            {
                role = BotRole.Attacker;
            }

            var allyA = default(BotParticipantObservation);
            var allyB = default(BotParticipantObservation);
            var enemyA = default(BotParticipantObservation);
            var enemyB = default(BotParticipantObservation);
            var enemyC = default(BotParticipantObservation);
            if (participant.Team == ParticipantTeam.Blue)
            {
                AssignTeamObservations(
                    participant.SlotId,
                    role,
                    slot0,
                    slot1,
                    slot2,
                    slot3,
                    slot4,
                    slot5,
                    out allyA,
                    out allyB,
                    out enemyA,
                    out enemyB,
                    out enemyC);
            }
            else
            {
                AssignTeamObservations(
                    participant.SlotId,
                    role,
                    slot3,
                    slot4,
                    slot5,
                    slot0,
                    slot1,
                    slot2,
                    out allyA,
                    out allyB,
                    out enemyA,
                    out enemyB,
                    out enemyC);
            }

            var pickup0 = GetPickupCandidate(0);
            var pickup1 = GetPickupCandidate(1);
            var pickup2 = GetPickupCandidate(2);
            var pickup3 = GetPickupCandidate(3);
            var pickup4 = GetPickupCandidate(4);

            return new BotReactedSnapshot(
                gameplayTime,
                selfObservation,
                ballObservation,
                allyA,
                allyB,
                enemyA,
                enemyB,
                enemyC,
                pickup0,
                pickup1,
                pickup2,
                pickup3,
                pickup4,
                role,
                ownGoalPosition,
                enemyGoalPosition);
        }

        private void AssignTeamObservations(
            int selfSlotId,
            BotRole role,
            BotParticipantObservation ownA,
            BotParticipantObservation ownB,
            BotParticipantObservation ownC,
            BotParticipantObservation enemyAObservation,
            BotParticipantObservation enemyBObservation,
            BotParticipantObservation enemyCObservation,
            out BotParticipantObservation allyA,
            out BotParticipantObservation allyB,
            out BotParticipantObservation enemyA,
            out BotParticipantObservation enemyB,
            out BotParticipantObservation enemyC)
        {
            allyA = default(BotParticipantObservation);
            allyB = default(BotParticipantObservation);
            enemyA = enemyAObservation;
            enemyB = enemyBObservation;
            enemyC = enemyCObservation;

            var ownFirst = ownA;
            var ownSecond = ownB;
            var ownThird = ownC;
            if (ownFirst.SlotId == selfSlotId)
            {
                ownFirst = ownSecond;
                ownSecond = ownThird;
                ownThird = default(BotParticipantObservation);
            }
            else if (ownSecond.SlotId == selfSlotId)
            {
                ownSecond = ownThird;
                ownThird = default(BotParticipantObservation);
            }
            else if (ownThird.SlotId == selfSlotId)
            {
                ownThird = default(BotParticipantObservation);
            }

            if (role == BotRole.Support)
            {
                var attacker = FindLivingAssignedAttacker(ownFirst, ownSecond, ownThird);
                if (attacker.HasObservation)
                {
                    allyA = attacker;
                    if (ownFirst.SlotId == attacker.SlotId)
                    {
                        ownFirst = ownSecond;
                        ownSecond = ownThird;
                        ownThird = default(BotParticipantObservation);
                    }
                    else if (ownSecond.SlotId == attacker.SlotId)
                    {
                        ownSecond = ownThird;
                        ownThird = default(BotParticipantObservation);
                    }
                    else if (ownThird.SlotId == attacker.SlotId)
                    {
                        ownThird = default(BotParticipantObservation);
                    }
                }
            }

            if (!allyA.HasObservation)
            {
                allyA = ownFirst;
            }
            if (!allyB.HasObservation)
            {
                allyB = ownSecond;
            }
        }

        private BotParticipantObservation FindLivingAssignedAttacker(
            BotParticipantObservation first,
            BotParticipantObservation second,
            BotParticipantObservation third)
        {
            if (IsLivingAssignedAttacker(first))
            {
                return first;
            }
            if (IsLivingAssignedAttacker(second))
            {
                return second;
            }
            if (IsLivingAssignedAttacker(third))
            {
                return third;
            }

            return default(BotParticipantObservation);
        }

        private bool IsLivingAssignedAttacker(BotParticipantObservation observation)
        {
            return observation.HasObservation && observation.IsAlive &&
                roleCoordinator.TryGetAssignedRole(observation.SlotId, out var role) &&
                role == BotRole.Attacker;
        }

        private BotTargetCandidate GetPickupCandidate(int index)
        {
            if (index < 0 || index >= MaxPickupCandidates ||
                !roleCoordinator.TryGetPickupCandidate(participant.SlotId, index, out var candidate))
            {
                return default(BotTargetCandidate);
            }

            return candidate;
        }

        private static BotParticipantObservation GetSlotObservation(
            int slotId,
            BotParticipantObservation slot0,
            BotParticipantObservation slot1,
            BotParticipantObservation slot2,
            BotParticipantObservation slot3,
            BotParticipantObservation slot4,
            BotParticipantObservation slot5)
        {
            if (slot0.SlotId == slotId)
            {
                return slot0;
            }
            if (slot1.SlotId == slotId)
            {
                return slot1;
            }
            if (slot2.SlotId == slotId)
            {
                return slot2;
            }
            if (slot3.SlotId == slotId)
            {
                return slot3;
            }
            if (slot4.SlotId == slotId)
            {
                return slot4;
            }
            if (slot5.SlotId == slotId)
            {
                return slot5;
            }

            return default(BotParticipantObservation);
        }

        private float GetGameplayTime()
        {
            return perception != null ? perception.GameplayTime : 0f;
        }

        private void ClearState()
        {
            paused = false;
            reactionOrdinal = 0;
            decisionOrdinal = 0;
            nextReactionTime = 0f;
            nextDecisionTime = 0f;
            reactionScheduled = false;
            decisionScheduled = false;
            reactedSnapshot = default(BotReactedSnapshot);

            activeTarget = BotTargetSelection.None;
            navigationSteering = BotNavigationSteeringResult.Invalid;
            ballAim = BotAimSolution.Invalid;
            enemyAim = BotAimSolution.Invalid;
            lastCombatResult = BotCombatResult.None;
            storedAimDirection = Vector3.zero;
            storedSteeringDirection = Vector3.zero;
            hasAim = false;
            hasRoute = false;
        }

        private bool ValidateComposition()
        {
            if (participant == null || participant.IsLocalParticipant || match == null || motor == null ||
                head == null || kick == null || launcher == null || shotgun == null || navigator == null ||
                perception == null || roleCoordinator == null ||
                participant.Motor != motor || participant.Kick != kick || participant.Launcher != launcher ||
                participant.Shotgun != shotgun || perception.ObserverSlotId != participant.SlotId ||
                !IsSupportedDifficulty(difficulty) || !IsFinite(maxPitchDegrees) ||
                !Mathf.Approximately(maxPitchDegrees, ExpectedMaxPitchDegrees) ||
                !IsFinite(jumpProbeDistance) ||
                !Mathf.Approximately(jumpProbeDistance, ExpectedJumpProbeDistance))
            {
                LogCompositionError();
                return false;
            }

            return true;
        }

        private void FailComposition()
        {
            LogCompositionError();
            compositionValid = false;
            enabled = false;
        }

        private void LogCompositionError()
        {
            Debug.LogError(CompositionError, this);
        }

        private static bool IsSupportedDifficulty(BotDifficulty value)
        {
            return value == BotDifficulty.Low || value == BotDifficulty.Medium || value == BotDifficulty.High;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct BotReactedSnapshot
        {
            public BotReactedSnapshot(
                float gameplayTime,
                BotParticipantObservation self,
                BotBallObservation ball,
                BotParticipantObservation allyA,
                BotParticipantObservation allyB,
                BotParticipantObservation enemyA,
                BotParticipantObservation enemyB,
                BotParticipantObservation enemyC,
                BotTargetCandidate pickup0,
                BotTargetCandidate pickup1,
                BotTargetCandidate pickup2,
                BotTargetCandidate pickup3,
                BotTargetCandidate pickup4,
                BotRole role,
                Vector3 ownGoalPosition,
                Vector3 enemyGoalPosition)
            {
                HasData = true;
                GameplayTime = gameplayTime;
                Self = self;
                Ball = ball;
                AllyA = allyA;
                AllyB = allyB;
                EnemyA = enemyA;
                EnemyB = enemyB;
                EnemyC = enemyC;
                Pickup0 = pickup0;
                Pickup1 = pickup1;
                Pickup2 = pickup2;
                Pickup3 = pickup3;
                Pickup4 = pickup4;
                Role = role;
                OwnGoalPosition = ownGoalPosition;
                EnemyGoalPosition = enemyGoalPosition;
            }

            public bool HasData { get; }
            public float GameplayTime { get; }
            public BotParticipantObservation Self { get; }
            public BotBallObservation Ball { get; }
            public BotParticipantObservation AllyA { get; }
            public BotParticipantObservation AllyB { get; }
            public BotParticipantObservation EnemyA { get; }
            public BotParticipantObservation EnemyB { get; }
            public BotParticipantObservation EnemyC { get; }
            public BotTargetCandidate Pickup0 { get; }
            public BotTargetCandidate Pickup1 { get; }
            public BotTargetCandidate Pickup2 { get; }
            public BotTargetCandidate Pickup3 { get; }
            public BotTargetCandidate Pickup4 { get; }
            public BotRole Role { get; }
            public Vector3 OwnGoalPosition { get; }
            public Vector3 EnemyGoalPosition { get; }
        }
    }
}
