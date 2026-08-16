using UnityEngine;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Physics;
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
        private const float ObservationMemorySeconds = 1.5f;
        private const float EmergencyCrossingWindowSeconds = 2.5f;
        private const float EmergencyTowardGoalSpeed = 0.5f;
        private const float EmergencyCrossingLateralLimit = 18f;
        private const float EmergencyCrossingHeightLimit = 7f;
        private const float EmergencyCloseProgressLimit = 12f;
        private const float EmergencyCloseLateralLimit = 22f;
        private const float EmergencyCloseHeightLimit = 10f;
        private const float EmergencyFallbackDistance = 12f;
        private const float EmergencyFallbackLane = 28f;
        private const float SupportProgressOffset = 12f;
        private const float SupportLateralLane = 28f;
        private const float EnemyOpportunityDistance = 30f;
        private const float PredictionLookaheadSeconds = 0.25f;
        private const float DirectionEpsilon = 0.000001f;
        private const int MaxPickupCandidates = 5;
        private const float CombatTargetDistanceEpsilon = 0.05f;
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
        private readonly RaycastHit[] combatRaycastHits = new RaycastHit[8];

        private BotTargetSelection activeTarget = BotTargetSelection.None;
        private BotNavigationSteeringResult navigationSteering = BotNavigationSteeringResult.Invalid;
        private BotAimSolution ballAim = BotAimSolution.Invalid;
        private BotAimSolution enemyAim = BotAimSolution.Invalid;
        private BotCombatResult lastCombatResult = BotCombatResult.None;
        private BotParticipantObservation combatEnemy;
        private Vector3 ballRocketLaunchPosition;
        private Vector3 enemyRocketLaunchPosition;
        private Vector3 rocketJumpLaunchPosition;
        private Vector3 rocketJumpAimPoint;
        private bool rocketLineClearToBall;
        private bool rocketLineClearToEnemy;
        private bool rocketJumpLineClear;
        private Vector3 storedAimDirection;
        private Vector3 storedSteeringDirection;
        private Vector3 storedActionFacing;
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

            var decisionRan = false;
            if (reactionScheduled && gameplayTime >= nextReactionTime)
            {
                RunReaction(gameplayTime);
            }

            if (decisionScheduled && gameplayTime >= nextDecisionTime)
            {
                RunDecision(gameplayTime);
                decisionRan = true;
            }

            UpdateNavigationAndMovement(gameplayTime);
            if (decisionRan)
            {
                EvaluateCombatDecision(gameplayTime);
            }

            ApplyFinalPoseAndMove();
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

            // The first decision for a copied snapshot is immediate. Once armed, its
            // deadline is independent from later reaction snapshots.
            if (!decisionScheduled && reactedSnapshot.HasData && IsFinite(reactedSnapshot.GameplayTime))
            {
                nextDecisionTime = reactedSnapshot.GameplayTime;
                decisionScheduled = true;
            }
        }

        private void RunDecision(float gameplayTime)
        {
            ClearCombatState();
            if (!reactedSnapshot.HasData)
            {
                decisionScheduled = false;
                activeTarget = BotTargetSelection.None;
                ClearMotion();
                return;
            }

            decisionOrdinal++;
            nextDecisionTime = gameplayTime +
                BotDifficultyRules.GetDecisionDelay(difficulty, participant.SlotId, decisionOrdinal);
            decisionScheduled = IsFinite(nextDecisionTime);

            if (!decisionScheduled || !IsFinite(gameplayTime))
            {
                activeTarget = BotTargetSelection.None;
                ClearMotion();
                return;
            }

            BuildTargetSelection(gameplayTime);
        }

        private void BuildTargetSelection(float decisionTime)
        {
            var selection = BotTargetSelection.None;
            if (!TryGetFieldBasis(reactedSnapshot, out var fieldForward, out var fieldLateral, out var fieldLength))
            {
                activeTarget = selection;
                storedActionFacing = Vector3.zero;
                ClearMotion();
                return;
            }

            var hasBall = TryGetBallEstimate(
                reactedSnapshot,
                decisionTime,
                0f,
                out var ballPosition,
                out var ballEffectiveAge,
                out var ballExpiry);
            var hasValidBallInOwnHalf = hasBall && IsBallInOwnHalf(
                ballPosition,
                reactedSnapshot.OwnGoalPosition,
                fieldForward,
                fieldLength);

            TryAddEmergencyTarget(
                ref selection,
                decisionTime,
                fieldForward,
                fieldLateral,
                fieldLength,
                hasBall,
                ballPosition,
                ballEffectiveAge,
                ballExpiry,
                hasValidBallInOwnHalf);
            TryAddRoleTarget(
                ref selection,
                decisionTime,
                fieldForward,
                fieldLateral,
                fieldLength,
                hasBall,
                ballPosition,
                ballEffectiveAge,
                ballExpiry,
                hasValidBallInOwnHalf);
            TryAddPickupTargets(ref selection, decisionTime, hasValidBallInOwnHalf);
            TryAddEnemyTargets(ref selection, decisionTime);

            if (!selection.HasTarget && hasBall && !reactedSnapshot.Ball.IsVisible)
            {
                var fallbackPosition = PredictBall(
                    reactedSnapshot.Ball,
                    decisionTime,
                    reactedSnapshot.GameplayTime,
                    0.35f);
                ProbeAndConsider(
                    ref selection,
                    new BotTargetKey(BotTargetKind.BallFallback, 0),
                    BotTargetRules.BallFallbackScore,
                    fallbackPosition,
                    fallbackPosition,
                    MinExpiry(ObservationExpiry(decisionTime, ballEffectiveAge), nextDecisionTime),
                    hasValidBallInOwnHalf);
            }

            activeTarget = selection;
            if (activeTarget.HasTarget)
            {
                storedActionFacing = GetFiniteFacing(
                    activeTarget.ActionPosition - GetCurrentPosition(),
                    activeTarget.NavigationPosition - GetCurrentPosition());
            }
            else
            {
                storedActionFacing = GetFiniteFacing(
                    reactedSnapshot.EnemyGoalPosition - GetCurrentPosition(),
                    transform.forward);
            }

            ClearMotion();
        }

        private bool TryGetFieldBasis(
            BotReactedSnapshot snapshot,
            out Vector3 fieldForward,
            out Vector3 fieldLateral,
            out float fieldLength)
        {
            fieldForward = Vector3.zero;
            fieldLateral = Vector3.zero;
            fieldLength = 0f;
            var goalDelta = snapshot.EnemyGoalPosition - snapshot.OwnGoalPosition;
            if (!IsFinite(goalDelta) || goalDelta.sqrMagnitude <= DirectionEpsilon)
            {
                return false;
            }

            fieldLength = goalDelta.magnitude;
            fieldForward = goalDelta / fieldLength;
            fieldLateral = Vector3.Cross(Vector3.up, fieldForward);
            if (!IsFinite(fieldLateral) || fieldLateral.sqrMagnitude <= DirectionEpsilon)
            {
                return false;
            }

            fieldLateral.Normalize();
            return IsFinite(fieldForward) && IsFinite(fieldLength) && fieldLength > 0f;
        }

        private bool TryGetBallEstimate(
            BotReactedSnapshot snapshot,
            float decisionTime,
            float horizon,
            out Vector3 predictedPosition,
            out float effectiveAge,
            out float expiresAt)
        {
            predictedPosition = Vector3.zero;
            effectiveAge = 0f;
            expiresAt = 0f;
            var observation = snapshot.Ball;
            if (!observation.HasObservation || !IsFinite(observation.Position))
            {
                return false;
            }

            effectiveAge = GetEffectiveAge(observation.AgeSeconds, decisionTime, snapshot.GameplayTime);
            if (!IsFinite(effectiveAge) || effectiveAge > ObservationMemorySeconds)
            {
                return false;
            }

            expiresAt = decisionTime + Mathf.Max(ObservationMemorySeconds - effectiveAge, 0f);
            predictedPosition = PredictBall(observation, decisionTime, snapshot.GameplayTime, horizon);
            return IsFinite(predictedPosition) && IsFinite(expiresAt) && expiresAt >= decisionTime;
        }

        private Vector3 PredictBall(
            BotBallObservation observation,
            float decisionTime,
            float snapshotTime,
            float horizon)
        {
            if (!IsFinite(observation.Position))
            {
                return Vector3.zero;
            }

            var effectiveAge = GetEffectiveAge(observation.AgeSeconds, decisionTime, snapshotTime);
            var safeHorizon = IsFinite(horizon) ? Mathf.Max(horizon, 0f) : 0f;
            var age = effectiveAge + safeHorizon;
            if (!IsFinite(effectiveAge) || !IsFinite(age) || age < 0f || !IsFinite(observation.Velocity))
            {
                return observation.Position;
            }

            var velocity = observation.Velocity;
            if (observation.IsGrounded)
            {
                velocity.y = 0f;
                var groundedPosition = observation.Position + velocity * age;
                groundedPosition.y = observation.Position.y;
                return IsFinite(groundedPosition) ? groundedPosition : observation.Position;
            }

            var airbornePosition = observation.Position + velocity * age +
                Vector3.down * (GamePhysicsSettings.GravityMagnitude * age * age * 0.5f);
            return IsFinite(airbornePosition) ? airbornePosition : observation.Position;
        }

        private Vector3 PredictBallVelocity(
            BotBallObservation observation,
            float decisionTime,
            float snapshotTime)
        {
            if (!IsFinite(observation.Velocity))
            {
                return Vector3.zero;
            }

            var effectiveAge = GetEffectiveAge(observation.AgeSeconds, decisionTime, snapshotTime);
            var velocity = observation.Velocity;
            if (observation.IsGrounded)
            {
                velocity.y = 0f;
                return velocity;
            }

            velocity += Vector3.down * (GamePhysicsSettings.GravityMagnitude * effectiveAge);
            return IsFinite(velocity) ? velocity : Vector3.zero;
        }

        private static float GetEffectiveAge(float observationAge, float decisionTime, float snapshotTime)
        {
            var age = IsFinite(observationAge) && observationAge >= 0f ? observationAge : 0f;
            var elapsed = decisionTime - snapshotTime;
            if (!IsFinite(elapsed) || elapsed < 0f)
            {
                elapsed = 0f;
            }

            var effectiveAge = age + elapsed;
            return IsFinite(effectiveAge) && effectiveAge >= 0f ? effectiveAge : float.PositiveInfinity;
        }

        private static bool IsBallInOwnHalf(
            Vector3 ballPosition,
            Vector3 ownGoalPosition,
            Vector3 fieldForward,
            float fieldLength)
        {
            if (!IsFinite(ballPosition) || !IsFinite(ownGoalPosition) || !IsFinite(fieldForward) ||
                !IsFinite(fieldLength) || fieldLength <= 0f)
            {
                return false;
            }

            var progress = Vector3.Dot(ballPosition - ownGoalPosition, fieldForward);
            return IsFinite(progress) && progress >= 0f && progress <= fieldLength * 0.5f;
        }

        private static float ObservationExpiry(float decisionTime, float effectiveAge)
        {
            if (!IsFinite(decisionTime) || !IsFinite(effectiveAge))
            {
                return -1f;
            }

            return decisionTime + Mathf.Max(ObservationMemorySeconds - effectiveAge, 0f);
        }

        private static float MinExpiry(float sourceExpiry, float nextDecision)
        {
            if (!IsFinite(sourceExpiry) || !IsFinite(nextDecision))
            {
                return -1f;
            }

            return Mathf.Min(sourceExpiry, nextDecision);
        }

        private void TryAddEmergencyTarget(
            ref BotTargetSelection selection,
            float decisionTime,
            Vector3 fieldForward,
            Vector3 fieldLateral,
            float fieldLength,
            bool hasBall,
            Vector3 ballPosition,
            float ballEffectiveAge,
            float ballExpiry,
            bool hasValidBallInOwnHalf)
        {
            if (!hasBall)
            {
                return;
            }

            var ballVelocity = PredictBallVelocity(
                reactedSnapshot.Ball,
                decisionTime,
                reactedSnapshot.GameplayTime);
            var progress = Vector3.Dot(
                ballPosition - reactedSnapshot.OwnGoalPosition,
                fieldForward);
            var lateral = Vector3.Dot(
                ballPosition - reactedSnapshot.OwnGoalPosition,
                fieldLateral);
            var towardOwnGoalSpeed = Vector3.Dot(ballVelocity, -fieldForward);
            var height = ballPosition.y;

            var hasCrossingDanger = IsFinite(progress) && IsFinite(lateral) && IsFinite(height) &&
                IsFinite(towardOwnGoalSpeed) && towardOwnGoalSpeed >= EmergencyTowardGoalSpeed &&
                progress >= 0f && progress / towardOwnGoalSpeed >= 0f &&
                progress / towardOwnGoalSpeed <= EmergencyCrossingWindowSeconds &&
                Mathf.Abs(lateral) <= EmergencyCrossingLateralLimit &&
                height >= 0f && height <= EmergencyCrossingHeightLimit;
            var hasCloseDanger = IsFinite(progress) && IsFinite(lateral) && IsFinite(height) &&
                progress >= 0f && progress <= EmergencyCloseProgressLimit &&
                Mathf.Abs(lateral) <= EmergencyCloseLateralLimit &&
                height >= 0f && height <= EmergencyCloseHeightLimit;
            if (!hasCrossingDanger && !hasCloseDanger)
            {
                return;
            }

            var crossingTime = hasCrossingDanger ? progress / towardOwnGoalSpeed : 0f;
            var horizon = hasCrossingDanger
                ? Mathf.Clamp(crossingTime - 0.20f, 0f, 0.75f)
                : 0.35f;
            var emergencyPosition = PredictBall(
                reactedSnapshot.Ball,
                decisionTime,
                reactedSnapshot.GameplayTime,
                horizon);
            var expiry = MinExpiry(ballExpiry, nextDecisionTime);
            if (!IsFinite(expiry) || decisionTime > expiry)
            {
                return;
            }

            var key = new BotTargetKey(BotTargetKind.OwnGoalEmergency, 0);
            if (ProbeAndConsider(
                ref selection,
                key,
                BotTargetRules.OwnGoalEmergencyScore,
                emergencyPosition,
                emergencyPosition,
                expiry,
                hasValidBallInOwnHalf))
            {
                return;
            }

            var fallbackLane = GetNearestFallbackLane(lateral);
            var fallbackPosition = reactedSnapshot.OwnGoalPosition +
                fieldForward * EmergencyFallbackDistance +
                fieldLateral * fallbackLane;
            ProbeAndConsider(
                ref selection,
                key,
                BotTargetRules.OwnGoalEmergencyScore,
                fallbackPosition,
                fallbackPosition,
                expiry,
                hasValidBallInOwnHalf);
        }

        private void TryAddRoleTarget(
            ref BotTargetSelection selection,
            float decisionTime,
            Vector3 fieldForward,
            Vector3 fieldLateral,
            float fieldLength,
            bool hasBall,
            Vector3 ballPosition,
            float ballEffectiveAge,
            float ballExpiry,
            bool hasValidBallInOwnHalf)
        {
            switch (reactedSnapshot.Role)
            {
                case BotRole.Defender:
                    TryAddDefenderTarget(
                        ref selection,
                        decisionTime,
                        fieldForward,
                        fieldLateral,
                        fieldLength,
                        hasBall,
                        ballPosition,
                        ballExpiry,
                        hasValidBallInOwnHalf);
                    break;
                case BotRole.Attacker:
                    if (hasBall)
                    {
                        var attackerPosition = PredictBall(
                            reactedSnapshot.Ball,
                            decisionTime,
                            reactedSnapshot.GameplayTime,
                            0.50f);
                        ProbeAndConsider(
                            ref selection,
                            new BotTargetKey(BotTargetKind.AttackerBall, 0),
                            BotTargetRules.AttackerBallScore,
                            attackerPosition,
                            attackerPosition,
                            MinExpiry(ballExpiry, nextDecisionTime),
                            hasValidBallInOwnHalf);
                    }
                    break;
                case BotRole.Support:
                    TryAddSupportTarget(
                        ref selection,
                        decisionTime,
                        fieldForward,
                        fieldLateral,
                        fieldLength,
                        hasBall,
                        ballPosition,
                        ballExpiry,
                        hasValidBallInOwnHalf);
                    break;
            }
        }

        private void TryAddDefenderTarget(
            ref BotTargetSelection selection,
            float decisionTime,
            Vector3 fieldForward,
            Vector3 fieldLateral,
            float fieldLength,
            bool hasBall,
            Vector3 ballPosition,
            float ballExpiry,
            bool hasValidBallInOwnHalf)
        {
            var lane = 0f;
            if (hasBall)
            {
                lane = Mathf.Clamp(
                    Vector3.Dot(ballPosition - reactedSnapshot.OwnGoalPosition, fieldLateral),
                    -SupportLateralLane,
                    SupportLateralLane);
            }

            var coveragePosition = reactedSnapshot.OwnGoalPosition +
                fieldForward * Mathf.Min(EmergencyFallbackDistance, Mathf.Max(fieldLength, 0f)) +
                fieldLateral * lane;
            var expiry = hasBall ? MinExpiry(ballExpiry, nextDecisionTime) : nextDecisionTime;
            ProbeAndConsider(
                ref selection,
                new BotTargetKey(BotTargetKind.DefenderCoverage, 0),
                BotTargetRules.DefenderCoverageScore,
                coveragePosition,
                coveragePosition,
                expiry,
                hasValidBallInOwnHalf);
        }

        private void TryAddSupportTarget(
            ref BotTargetSelection selection,
            float decisionTime,
            Vector3 fieldForward,
            Vector3 fieldLateral,
            float fieldLength,
            bool hasBall,
            Vector3 ballPosition,
            float ballExpiry,
            bool hasValidBallInOwnHalf)
        {
            var allyA = reactedSnapshot.AllyA;
            if (!hasBall || !allyA.HasObservation || !allyA.IsAlive || !IsFinite(allyA.Position) ||
                !IsFinite(fieldLength) || fieldLength <= 0f)
            {
                return;
            }

            var ballProgress = Vector3.Dot(
                ballPosition - reactedSnapshot.OwnGoalPosition,
                fieldForward);
            var minProgress = Mathf.Min(24f, fieldLength * 0.5f);
            var maxProgress = Mathf.Max(minProgress, fieldLength - 24f);
            var supportProgress = Mathf.Clamp(ballProgress - SupportProgressOffset, minProgress, maxProgress);
            var attackerLateral = Vector3.Dot(
                allyA.Position - reactedSnapshot.OwnGoalPosition,
                fieldLateral);
            var supportLane = 0f;
            if (IsFinite(attackerLateral) && Mathf.Abs(attackerLateral) > DirectionEpsilon)
            {
                supportLane = Mathf.Clamp(-attackerLateral, -SupportLateralLane, SupportLateralLane);
            }
            else
            {
                supportLane = participant.SlotId % 2 == 0 ? SupportLateralLane : -SupportLateralLane;
            }

            var supportPosition = reactedSnapshot.OwnGoalPosition +
                fieldForward * supportProgress + fieldLateral * supportLane;
            ProbeAndConsider(
                ref selection,
                new BotTargetKey(BotTargetKind.SupportLane, 0),
                BotTargetRules.SupportLaneScore,
                supportPosition,
                supportPosition,
                MinExpiry(ballExpiry, nextDecisionTime),
                hasValidBallInOwnHalf);
        }

        private static float GetNearestFallbackLane(float lateral)
        {
            var bestLane = -EmergencyFallbackLane;
            var bestDistance = float.PositiveInfinity;
            for (var i = -1; i <= 1; i++)
            {
                var lane = i * EmergencyFallbackLane;
                var distance = Mathf.Abs(lateral - lane);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestLane = lane;
                }
            }

            return bestLane;
        }

        private void TryAddPickupTargets(
            ref BotTargetSelection selection,
            float decisionTime,
            bool hasValidBallInOwnHalf)
        {
            TryAddPickupTarget(ref selection, reactedSnapshot.Pickup0, decisionTime, hasValidBallInOwnHalf);
            TryAddPickupTarget(ref selection, reactedSnapshot.Pickup1, decisionTime, hasValidBallInOwnHalf);
            TryAddPickupTarget(ref selection, reactedSnapshot.Pickup2, decisionTime, hasValidBallInOwnHalf);
            TryAddPickupTarget(ref selection, reactedSnapshot.Pickup3, decisionTime, hasValidBallInOwnHalf);
            TryAddPickupTarget(ref selection, reactedSnapshot.Pickup4, decisionTime, hasValidBallInOwnHalf);
        }

        private void TryAddPickupTarget(
            ref BotTargetSelection selection,
            BotTargetCandidate pickup,
            float decisionTime,
            bool hasValidBallInOwnHalf)
        {
            if (!BotTargetRules.IsValid(pickup) || decisionTime > pickup.ExpiresAt)
            {
                return;
            }

            var expiry = MinExpiry(pickup.ExpiresAt, nextDecisionTime);
            if (!IsFinite(expiry) || decisionTime > expiry)
            {
                return;
            }

            ProbeAndConsider(
                ref selection,
                pickup.Key,
                pickup.Score,
                pickup.NavigationPosition,
                pickup.ActionPosition,
                expiry,
                hasValidBallInOwnHalf);
        }

        private void TryAddEnemyTargets(ref BotTargetSelection selection, float decisionTime)
        {
            var visibleCount = 0;
            var recentCount = 0;
            var visibleA = default(EnemyOpportunityCandidate);
            var visibleB = default(EnemyOpportunityCandidate);
            var visibleC = default(EnemyOpportunityCandidate);
            var recentA = default(EnemyOpportunityCandidate);
            var recentB = default(EnemyOpportunityCandidate);
            var recentC = default(EnemyOpportunityCandidate);
            var origin = reactedSnapshot.Self.HasObservation && IsFinite(reactedSnapshot.Self.Position)
                ? reactedSnapshot.Self.Position
                : GetCurrentPosition();

            ConsiderEnemy(
                reactedSnapshot.EnemyA,
                decisionTime,
                origin,
                ref visibleCount,
                ref visibleA,
                ref visibleB,
                ref visibleC,
                ref recentCount,
                ref recentA,
                ref recentB,
                ref recentC);
            ConsiderEnemy(
                reactedSnapshot.EnemyB,
                decisionTime,
                origin,
                ref visibleCount,
                ref visibleA,
                ref visibleB,
                ref visibleC,
                ref recentCount,
                ref recentA,
                ref recentB,
                ref recentC);
            ConsiderEnemy(
                reactedSnapshot.EnemyC,
                decisionTime,
                origin,
                ref visibleCount,
                ref visibleA,
                ref visibleB,
                ref visibleC,
                ref recentCount,
                ref recentA,
                ref recentB,
                ref recentC);

            if (visibleCount > 0)
            {
                TryAddEnemyCandidate(ref selection, visibleA.Observation, visibleA.PredictedPosition, decisionTime);
            }
            if (visibleCount > 1)
            {
                TryAddEnemyCandidate(ref selection, visibleB.Observation, visibleB.PredictedPosition, decisionTime);
            }
            if (visibleCount > 2)
            {
                TryAddEnemyCandidate(ref selection, visibleC.Observation, visibleC.PredictedPosition, decisionTime);
            }
            if (recentCount > 0)
            {
                TryAddEnemyCandidate(ref selection, recentA.Observation, recentA.PredictedPosition, decisionTime);
            }
            if (recentCount > 1)
            {
                TryAddEnemyCandidate(ref selection, recentB.Observation, recentB.PredictedPosition, decisionTime);
            }
            if (recentCount > 2)
            {
                TryAddEnemyCandidate(ref selection, recentC.Observation, recentC.PredictedPosition, decisionTime);
            }
        }

        private void ConsiderEnemy(
            BotParticipantObservation observation,
            float decisionTime,
            Vector3 origin,
            ref int visibleCount,
            ref EnemyOpportunityCandidate visibleA,
            ref EnemyOpportunityCandidate visibleB,
            ref EnemyOpportunityCandidate visibleC,
            ref int recentCount,
            ref EnemyOpportunityCandidate recentA,
            ref EnemyOpportunityCandidate recentB,
            ref EnemyOpportunityCandidate recentC)
        {
            if (!observation.HasObservation || !observation.IsAlive ||
                !IsFinite(observation.Position) || !IsFinite(observation.Velocity))
            {
                return;
            }

            var effectiveAge = GetEffectiveAge(
                observation.AgeSeconds,
                decisionTime,
                reactedSnapshot.GameplayTime);
            if (!IsFinite(effectiveAge) || effectiveAge > ObservationMemorySeconds)
            {
                return;
            }

            var predictedPosition = observation.Position + observation.Velocity *
                (effectiveAge + PredictionLookaheadSeconds);
            if (!IsFinite(predictedPosition))
            {
                predictedPosition = observation.Position;
            }

            var distance = Vector3.Distance(origin, predictedPosition);
            if (!IsFinite(distance) || distance > EnemyOpportunityDistance)
            {
                return;
            }

            var candidate = new EnemyOpportunityCandidate(observation, predictedPosition, distance);
            if (observation.IsVisible)
            {
                InsertEnemyCandidate(
                    ref visibleCount,
                    ref visibleA,
                    ref visibleB,
                    ref visibleC,
                    candidate);
            }
            else
            {
                InsertEnemyCandidate(
                    ref recentCount,
                    ref recentA,
                    ref recentB,
                    ref recentC,
                    candidate);
            }
        }

        private static void InsertEnemyCandidate(
            ref int count,
            ref EnemyOpportunityCandidate first,
            ref EnemyOpportunityCandidate second,
            ref EnemyOpportunityCandidate third,
            EnemyOpportunityCandidate candidate)
        {
            if (count <= 0)
            {
                first = candidate;
                count = 1;
                return;
            }

            if (count == 1)
            {
                if (IsEarlierEnemy(candidate, first))
                {
                    second = first;
                    first = candidate;
                }
                else
                {
                    second = candidate;
                }
                count = 2;
                return;
            }

            if (count == 2)
            {
                if (IsEarlierEnemy(candidate, first))
                {
                    third = second;
                    second = first;
                    first = candidate;
                }
                else if (IsEarlierEnemy(candidate, second))
                {
                    third = second;
                    second = candidate;
                }
                else
                {
                    third = candidate;
                }
                count = 3;
                return;
            }

            if (!IsEarlierEnemy(candidate, third))
            {
                return;
            }

            if (IsEarlierEnemy(candidate, first))
            {
                third = second;
                second = first;
                first = candidate;
            }
            else if (IsEarlierEnemy(candidate, second))
            {
                third = second;
                second = candidate;
            }
            else
            {
                third = candidate;
            }
        }

        private static bool IsEarlierEnemy(
            EnemyOpportunityCandidate candidate,
            EnemyOpportunityCandidate current)
        {
            if (candidate.Distance != current.Distance)
            {
                return candidate.Distance < current.Distance;
            }

            return candidate.Observation.SlotId < current.Observation.SlotId;
        }

        private void TryAddEnemyCandidate(
            ref BotTargetSelection selection,
            BotParticipantObservation observation,
            Vector3 predictedPosition,
            float decisionTime)
        {
            var effectiveAge = GetEffectiveAge(
                observation.AgeSeconds,
                decisionTime,
                reactedSnapshot.GameplayTime);
            var expiry = MinExpiry(ObservationExpiry(decisionTime, effectiveAge), nextDecisionTime);
            if (!IsFinite(expiry) || decisionTime > expiry)
            {
                return;
            }

            ProbeAndConsider(
                ref selection,
                new BotTargetKey(BotTargetKind.EnemyOpportunity, observation.SlotId),
                BotTargetRules.ScoreForEnemy(observation.IsVisible),
                predictedPosition,
                predictedPosition,
                expiry,
                false);
        }

        private bool ProbeAndConsider(
            ref BotTargetSelection selection,
            BotTargetKey key,
            float score,
            Vector3 navigationPosition,
            Vector3 actionPosition,
            float expiresAt,
            bool hasValidBallInOwnHalf)
        {
            if (navigator == null || !IsFinite(navigationPosition) || !IsFinite(actionPosition) ||
                !IsFinite(score) || !IsFinite(expiresAt) || expiresAt < 0f)
            {
                return false;
            }

            if (!navigator.TryProbeTarget(navigationPosition, out var projectedTarget, out var routeCost) ||
                !IsFinite(projectedTarget) || !IsFinite(routeCost) || routeCost < 0f)
            {
                return false;
            }

            BotTargetRules.GetActionFlags(
                key.Kind,
                hasValidBallInOwnHalf,
                out var suppressParticipantCombat,
                out var preferBallActions);
            var candidate = new BotTargetCandidate(
                key,
                score,
                projectedTarget,
                actionPosition,
                suppressParticipantCombat,
                preferBallActions,
                true,
                routeCost,
                expiresAt);
            selection = BotTargetRules.Consider(selection, candidate);
            return true;
        }

        private void UpdateNavigationAndMovement(float gameplayTime)
        {
            if (!compositionValid || !simulationEnabled || paused)
            {
                return;
            }

            if (!activeTarget.HasTarget || !BotTargetRules.IsValid(activeTarget) ||
                !IsFinite(gameplayTime) || gameplayTime > activeTarget.ExpiresAt)
            {
                activeTarget = BotTargetSelection.None;
                storedActionFacing = reactedSnapshot.HasData
                    ? GetFiniteFacing(reactedSnapshot.EnemyGoalPosition - GetCurrentPosition(), transform.forward)
                    : GetFiniteFacing(transform.forward, Vector3.forward);
                ClearMotion();
                return;
            }

            if (navigator == null || !IsFinite(activeTarget.NavigationPosition))
            {
                ClearMotion();
                return;
            }

            navigationSteering = navigator.EvaluateSteering(activeTarget.NavigationPosition);
            if (navigationSteering.Status == BotNavigationStatus.Invalid ||
                navigationSteering.Status == BotNavigationStatus.Unreachable)
            {
                ClearMotion();
                return;
            }

            var worldSteering = navigationSteering.WorldDirection;
            var flatSteering = NormalizeHorizontal(worldSteering);
            if (navigationSteering.Status == BotNavigationStatus.Following &&
                IsFinite(flatSteering) && flatSteering.sqrMagnitude > DirectionEpsilon)
            {
                storedSteeringDirection = IsFinite(worldSteering) ? worldSteering : flatSteering;
                hasRoute = true;
                return;
            }

            storedSteeringDirection = Vector3.zero;
            hasRoute = navigationSteering.Status == BotNavigationStatus.Following;
        }

        private void EvaluateCombatDecision(float decisionTime)
        {
            if (!compositionValid || !simulationEnabled || paused || !reactedSnapshot.HasData ||
                head == null || launcher == null || motor == null || kick == null || shotgun == null ||
                navigator == null || participant == null)
            {
                return;
            }

            var actionOrigin = head.position;
            if (!IsFinite(actionOrigin))
            {
                return;
            }

            var parameters = BotDifficultyRules.GetParameters(difficulty);
            var decisionBall = BuildDecisionBall(decisionTime);
            var hasDecisionEnemy = TryGetDecisionEnemy(decisionTime, out var decisionEnemy);
            combatEnemy = hasDecisionEnemy ? decisionEnemy : default(BotParticipantObservation);

            var ballRouteReachable = decisionBall.HasObservation && IsFinite(decisionBall.Position) &&
                navigator.TryProbeTarget(decisionBall.Position, out _, out _);
            var enemyRouteReachable = hasDecisionEnemy && IsFinite(decisionEnemy.Position) &&
                navigator.TryProbeTarget(decisionEnemy.Position, out _, out _);

            ballAim = BotAimRules.SolveDirectAim(
                actionOrigin,
                decisionBall.Position,
                BotCombatTarget.Ball,
                parameters,
                participant.SlotId,
                decisionOrdinal);
            enemyAim = BotAimRules.SolveDirectAim(
                actionOrigin,
                decisionEnemy.Position,
                BotCombatTarget.Enemy,
                parameters,
                participant.SlotId,
                decisionOrdinal);

            var projectileSpeed = launcher.ProjectileSpeed;
            var ballRocketAim = BotAimRules.SolveInterceptAim(
                actionOrigin,
                decisionBall.Position,
                decisionBall.Velocity,
                projectileSpeed,
                BotCombatAction.FireRocket,
                BotCombatTarget.Ball,
                parameters,
                participant.SlotId,
                decisionOrdinal);
            var enemyRocketAim = BotAimRules.SolveInterceptAim(
                actionOrigin,
                decisionEnemy.Position,
                decisionEnemy.Velocity,
                projectileSpeed,
                BotCombatAction.FireRocket,
                BotCombatTarget.Enemy,
                parameters,
                participant.SlotId,
                decisionOrdinal);

            if (ballRocketAim.IsValid &&
                launcher.TryGetLaunchPosition(actionOrigin, ballRocketAim.Direction, out ballRocketLaunchPosition))
            {
                rocketLineClearToBall = HasCombatLineOfSight(
                    ballRocketLaunchPosition,
                    ballRocketAim.AimPoint,
                    true);
            }

            if (enemyRocketAim.IsValid &&
                launcher.TryGetLaunchPosition(actionOrigin, enemyRocketAim.Direction, out enemyRocketLaunchPosition))
            {
                rocketLineClearToEnemy = HasCombatLineOfSight(
                    enemyRocketLaunchPosition,
                    enemyRocketAim.AimPoint,
                    false);
            }

            var routeForwardXZ = navigationSteering.HasSteering
                ? NormalizeHorizontal(navigationSteering.WorldDirection)
                : Vector3.zero;
            var rocketJumpDirection = BotCombatRules.GetRocketJumpDirection(routeForwardXZ);
            if (rocketJumpDirection.sqrMagnitude > DirectionEpsilon &&
                launcher.TryGetLaunchPosition(actionOrigin, rocketJumpDirection, out rocketJumpLaunchPosition))
            {
                rocketJumpLineClear = TryGetRocketJumpAimPoint(
                    rocketJumpLaunchPosition,
                    rocketJumpDirection,
                    out rocketJumpAimPoint);
            }

            var isGrounded = motor.IsGrounded;
            var hasGroundContact = motor.HasGroundContact;
            var dashReady = kick.SimulationEnabled && kick.CooldownRemaining <= 0f &&
                !motor.IsDashing && (isGrounded || hasGroundContact || motor.AirDashAvailable);
            var input = new BotCombatInput(
                difficulty,
                reactedSnapshot.Role,
                activeTarget.HasTarget ? activeTarget.Key.Kind : BotTargetKind.None,
                activeTarget.HasTarget ? activeTarget.Score : 0f,
                activeTarget.HasTarget && activeTarget.SuppressParticipantCombat,
                activeTarget.HasTarget && activeTarget.PreferBallActions,
                actionOrigin,
                ballRocketLaunchPosition,
                enemyRocketLaunchPosition,
                rocketJumpLaunchPosition,
                routeForwardXZ,
                decisionBall,
                combatEnemy,
                reactedSnapshot.AllyA,
                reactedSnapshot.AllyB,
                ballRouteReachable,
                enemyRouteReachable,
                dashReady,
                shotgun.CanFire,
                launcher.CanFire,
                ballAim,
                ballRocketAim,
                enemyAim,
                enemyRocketAim,
                rocketLineClearToBall,
                rocketLineClearToEnemy,
                isGrounded,
                navigationSteering.VerticalRoute == BotVerticalRoute.Ascend,
                rocketJumpAimPoint,
                rocketJumpLineClear);

            lastCombatResult = BotCombatRules.Evaluate(input);
            if (!lastCombatResult.HasAction)
            {
                storedAimDirection = Vector3.zero;
                hasAim = false;
                return;
            }

            storedAimDirection = lastCombatResult.AimDirection;
            hasAim = IsFinite(storedAimDirection) && storedAimDirection.sqrMagnitude > DirectionEpsilon;
            if (hasAim)
            {
                TranslateCombatResult(lastCombatResult, actionOrigin);
            }
        }

        private BotBallObservation BuildDecisionBall(float decisionTime)
        {
            if (!TryGetBallEstimate(
                    reactedSnapshot,
                    decisionTime,
                    0f,
                    out var position,
                    out var effectiveAge,
                    out _))
            {
                return default(BotBallObservation);
            }

            var velocity = IsFinite(reactedSnapshot.Ball.Velocity)
                ? reactedSnapshot.Ball.Velocity
                : Vector3.zero;
            if (reactedSnapshot.Ball.IsGrounded)
            {
                velocity.y = 0f;
            }
            else if (IsFinite(effectiveAge))
            {
                velocity += Vector3.down * (GamePhysicsSettings.GravityMagnitude * effectiveAge);
            }

            if (!IsFinite(velocity))
            {
                velocity = Vector3.zero;
            }

            return new BotBallObservation(
                true,
                reactedSnapshot.Ball.IsVisible,
                reactedSnapshot.Ball.IsGrounded,
                position,
                velocity,
                effectiveAge);
        }

        private void ApplyFinalPoseAndMove()
        {
            var facingDirection = storedActionFacing;
            if (hasAim && lastCombatResult.HasAction && IsFinite(storedAimDirection) &&
                storedAimDirection.sqrMagnitude > DirectionEpsilon)
            {
                facingDirection = storedAimDirection;
            }
            else if (hasRoute && IsFinite(storedSteeringDirection) &&
                storedSteeringDirection.sqrMagnitude > DirectionEpsilon)
            {
                facingDirection = storedSteeringDirection;
            }

            ApplyFacing(facingDirection);
            ApplyMoveIntent(hasRoute ? storedSteeringDirection : Vector3.zero);
        }

        private bool TryGetDecisionEnemy(float decisionTime, out BotParticipantObservation enemy)
        {
            enemy = default(BotParticipantObservation);
            if (!activeTarget.HasTarget || activeTarget.Key.Kind != BotTargetKind.EnemyOpportunity ||
                !TryGetEnemyObservation(activeTarget.Key.SubjectId, out var observation) ||
                !observation.HasObservation || !observation.IsAlive ||
                !IsFinite(observation.Position) || !IsFinite(observation.Velocity))
            {
                return false;
            }

            var effectiveAge = GetEffectiveAge(
                observation.AgeSeconds,
                decisionTime,
                reactedSnapshot.GameplayTime);
            if (!IsFinite(effectiveAge) || effectiveAge > ObservationMemorySeconds)
            {
                return false;
            }

            var predictedPosition = observation.Position + observation.Velocity *
                (effectiveAge + PredictionLookaheadSeconds);
            if (!IsFinite(predictedPosition))
            {
                predictedPosition = observation.Position;
            }

            enemy = new BotParticipantObservation(
                true,
                observation.IsVisible,
                observation.SlotId,
                observation.Team,
                observation.IsLocalParticipant,
                observation.IsAlive,
                predictedPosition,
                observation.Velocity,
                observation.Health,
                observation.MaxHealth,
                observation.HasShotgun,
                observation.ShotgunShells,
                observation.ShotgunShellCapacity,
                effectiveAge);
            return true;
        }

        private bool TryGetEnemyObservation(int slotId, out BotParticipantObservation observation)
        {
            if (reactedSnapshot.EnemyA.HasObservation && reactedSnapshot.EnemyA.SlotId == slotId)
            {
                observation = reactedSnapshot.EnemyA;
                return true;
            }
            if (reactedSnapshot.EnemyB.HasObservation && reactedSnapshot.EnemyB.SlotId == slotId)
            {
                observation = reactedSnapshot.EnemyB;
                return true;
            }
            if (reactedSnapshot.EnemyC.HasObservation && reactedSnapshot.EnemyC.SlotId == slotId)
            {
                observation = reactedSnapshot.EnemyC;
                return true;
            }

            observation = default(BotParticipantObservation);
            return false;
        }

        private bool HasCombatLineOfSight(Vector3 origin, Vector3 aimPoint, bool ballTarget)
        {
            var offset = aimPoint - origin;
            var distance = offset.magnitude;
            if (!IsFinite(origin) || !IsFinite(aimPoint) || !IsFinite(offset) ||
                !IsFinite(distance) || distance <= DirectionEpsilon)
            {
                return false;
            }

            var direction = offset / distance;
            var hitCount = UnityEngine.Physics.RaycastNonAlloc(
                origin,
                direction,
                combatRaycastHits,
                distance,
                EffectiveCombatObstacleMask(),
                QueryTriggerInteraction.Ignore);
            if (hitCount <= 0)
            {
                return true;
            }

            var ballBody = ballTarget && match != null && match.Ball != null
                ? match.Ball.Rigidbody
                : null;
            var blockingDistance = distance - CombatTargetDistanceEpsilon;
            var nearestBlockingDistance = float.PositiveInfinity;
            for (var i = 0; i < hitCount; i++)
            {
                var collider = combatRaycastHits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                if (ballTarget && ballBody != null && collider.attachedRigidbody == ballBody)
                {
                    continue;
                }

                if (IsFinite(combatRaycastHits[i].distance) &&
                    combatRaycastHits[i].distance < nearestBlockingDistance)
                {
                    nearestBlockingDistance = combatRaycastHits[i].distance;
                }
            }

            return nearestBlockingDistance >= blockingDistance;
        }

        private bool TryGetRocketJumpAimPoint(
            Vector3 launchPosition,
            Vector3 direction,
            out Vector3 aimPoint)
        {
            aimPoint = Vector3.zero;
            var hitCount = UnityEngine.Physics.RaycastNonAlloc(
                launchPosition,
                direction,
                combatRaycastHits,
                jumpProbeDistance,
                EffectiveCombatObstacleMask(),
                QueryTriggerInteraction.Ignore);
            if (hitCount <= 0 || participant.CharacterController == null)
            {
                return false;
            }

            var nearestHit = default(RaycastHit);
            var nearestDistance = float.PositiveInfinity;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = combatRaycastHits[i];
                if (hit.collider == null || !IsFinite(hit.point) || !IsFinite(hit.distance) ||
                    hit.distance < 0f || hit.distance >= nearestDistance)
                {
                    continue;
                }

                nearestHit = hit;
                nearestDistance = hit.distance;
            }

            if (nearestHit.collider == null ||
                !BlastMath.TryGetUnderfootFacing(
                    transform.forward,
                    participant.CharacterController.bounds,
                    participant.CharacterController.radius,
                    nearestHit.point,
                    out _))
            {
                return false;
            }

            aimPoint = nearestHit.point;
            return IsFinite(aimPoint);
        }

        private int EffectiveCombatObstacleMask()
        {
            var mask = combatObstacleMask.value;
            RemoveNamedLayer(ref mask, "Participants");
            RemoveNamedLayer(ref mask, "Projectiles");
            return mask;
        }

        private static void RemoveNamedLayer(ref int mask, string layerName)
        {
            var layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                mask &= ~(1 << layer);
            }
        }

        private void TranslateCombatResult(BotCombatResult result, Vector3 actionOrigin)
        {
            switch (result.Action)
            {
                case BotCombatAction.DashKick:
                    kick.RequestKick(result.AimDirection);
                    break;
                case BotCombatAction.FireShotgun:
                    shotgun.RequestFire(actionOrigin, result.AimDirection);
                    break;
                case BotCombatAction.FireRocket:
                    launcher.RequestFire(result.LaunchPosition, result.AimDirection);
                    break;
                case BotCombatAction.RocketJump:
                    motor.RequestJump();
                    launcher.RequestFire(result.LaunchPosition, result.AimDirection);
                    break;
            }
        }

        private void ApplyMoveIntent(Vector3 worldSteering)
        {
            if (motor == null || !IsFinite(worldSteering))
            {
                return;
            }

            var normalized = NormalizeHorizontal(worldSteering);
            if (!IsFinite(normalized) || normalized.sqrMagnitude <= DirectionEpsilon)
            {
                motor.SetMoveIntent(Vector2.zero);
                return;
            }

            normalized.Normalize();
            var move = new Vector2(
                Vector3.Dot(transform.right, normalized),
                Vector3.Dot(transform.forward, normalized));
            if (!IsFinite(move))
            {
                motor.SetMoveIntent(Vector2.zero);
                return;
            }

            motor.SetMoveIntent(Vector2.ClampMagnitude(move, 1f));
        }

        private void ApplyFacing(Vector3 steeringDirection)
        {
            var worldDirection = steeringDirection;
            if (!IsFinite(worldDirection) || worldDirection.sqrMagnitude <= DirectionEpsilon)
            {
                worldDirection = storedActionFacing;
            }
            if (!IsFinite(worldDirection) || worldDirection.sqrMagnitude <= DirectionEpsilon)
            {
                worldDirection = reactedSnapshot.HasData
                    ? reactedSnapshot.EnemyGoalPosition - GetCurrentPosition()
                    : transform.forward;
            }

            var horizontal = new Vector3(worldDirection.x, 0f, worldDirection.z);
            if (!IsFinite(horizontal) || horizontal.sqrMagnitude <= DirectionEpsilon)
            {
                var storedHorizontal = new Vector3(storedActionFacing.x, 0f, storedActionFacing.z);
                if (IsFinite(storedHorizontal) && storedHorizontal.sqrMagnitude > DirectionEpsilon)
                {
                    horizontal = storedHorizontal;
                }
            }

            if (IsFinite(horizontal) && horizontal.sqrMagnitude > DirectionEpsilon)
            {
                transform.rotation = Quaternion.LookRotation(horizontal.normalized, Vector3.up);
            }

            if (head == null)
            {
                return;
            }

            var horizontalMagnitude = Mathf.Sqrt(worldDirection.x * worldDirection.x + worldDirection.z * worldDirection.z);
            var pitch = 0f;
            if (IsFinite(worldDirection) && IsFinite(horizontalMagnitude))
            {
                pitch = Mathf.Clamp(
                    -Mathf.Atan2(worldDirection.y, horizontalMagnitude) * Mathf.Rad2Deg,
                    -maxPitchDegrees,
                    maxPitchDegrees);
            }

            if (!IsFinite(pitch))
            {
                pitch = 0f;
            }
            head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void ClearMotion()
        {
            if (navigator != null)
            {
                navigator.ClearRoute();
            }

            navigationSteering = BotNavigationSteeringResult.Invalid;
            storedSteeringDirection = Vector3.zero;
            hasRoute = false;
            if (motor != null)
            {
                motor.SetMoveIntent(Vector2.zero);
            }
        }

        private Vector3 GetCurrentPosition()
        {
            return IsFinite(transform.position) ? transform.position : Vector3.zero;
        }

        private static Vector3 NormalizeHorizontal(Vector3 value)
        {
            if (!IsFinite(value))
            {
                return Vector3.zero;
            }

            var horizontal = new Vector3(value.x, 0f, value.z);
            if (horizontal.sqrMagnitude <= DirectionEpsilon)
            {
                return Vector3.zero;
            }

            horizontal.Normalize();
            return horizontal;
        }

        private static Vector3 GetFiniteFacing(Vector3 preferred, Vector3 fallback)
        {
            if (IsFinite(preferred) && preferred.sqrMagnitude > DirectionEpsilon)
            {
                return preferred.normalized;
            }
            if (IsFinite(fallback) && fallback.sqrMagnitude > DirectionEpsilon)
            {
                return fallback.normalized;
            }
            return Vector3.forward;
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
            storedAimDirection = Vector3.zero;
            storedSteeringDirection = Vector3.zero;
            storedActionFacing = Vector3.zero;
            hasAim = false;
            hasRoute = false;
            ClearCombatState();
            ClearMotion();
        }

        private void ClearCombatState()
        {
            ballAim = BotAimSolution.Invalid;
            enemyAim = BotAimSolution.Invalid;
            lastCombatResult = BotCombatResult.None;
            combatEnemy = default(BotParticipantObservation);
            ballRocketLaunchPosition = Vector3.zero;
            enemyRocketLaunchPosition = Vector3.zero;
            rocketJumpLaunchPosition = Vector3.zero;
            rocketJumpAimPoint = Vector3.zero;
            rocketLineClearToBall = false;
            rocketLineClearToEnemy = false;
            rocketJumpLineClear = false;
        }

        private bool ValidateComposition()
        {
            if (participant == null || participant.IsLocalParticipant || match == null || motor == null ||
                head == null || kick == null || launcher == null || shotgun == null || navigator == null ||
                perception == null || roleCoordinator == null ||
                participant.Motor != motor || participant.Kick != kick || participant.Launcher != launcher ||
                participant.Shotgun != shotgun || participant.CharacterController == null ||
                perception.ObserverSlotId != participant.SlotId || ContainsForbiddenLayer(combatObstacleMask.value) ||
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

        private static bool ContainsForbiddenLayer(int mask)
        {
            return ContainsLayer(mask, "Participants") || ContainsLayer(mask, "Projectiles");
        }

        private static bool ContainsLayer(int mask, string layerName)
        {
            var layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 && (mask & (1 << layer)) != 0;
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

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct EnemyOpportunityCandidate
        {
            public EnemyOpportunityCandidate(
                BotParticipantObservation observation,
                Vector3 predictedPosition,
                float distance)
            {
                Observation = observation;
                PredictedPosition = predictedPosition;
                Distance = distance;
            }

            public BotParticipantObservation Observation { get; }
            public Vector3 PredictedPosition { get; }
            public float Distance { get; }
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
