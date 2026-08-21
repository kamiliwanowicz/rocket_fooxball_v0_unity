using UnityEngine;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Bots
{
    public enum BotDifficulty
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    public enum BotRole
    {
        Attacker = 0,
        Support = 1,
        Defender = 2
    }

    public enum BotSampleChannel
    {
        ReactionDelay = 0,
        DecisionDelay = 1,
        PredictionError = 2,
        AimConeRadius = 3,
        AimConeAzimuth = 4,
        AerialMissRoll = 5,
        AerialMissAzimuth = 6
    }

    public enum BotPickupKind
    {
        Health = 0,
        Shotgun = 1,
        Ammo = 2
    }

    public enum BotTargetKind
    {
        None = 0,
        OwnGoalEmergency = 1,
        DefenderCoverage = 2,
        AttackerBall = 3,
        SupportLane = 4,
        HealthPickup = 5,
        ShotgunPickup = 6,
        AmmoPickup = 7,
        EnemyOpportunity = 8,
        BallFallback = 9
    }

    public enum BotCombatAction
    {
        None = 0,
        DashKick = 1,
        FireShotgun = 2,
        FireRocket = 3,
        RocketJump = 4
    }

    public enum BotCombatTarget
    {
        None = 0,
        Ball = 1,
        Enemy = 2,
        SelfImpact = 3
    }

    public readonly struct BotDifficultyParameters
    {
        public BotDifficultyParameters(
            float reactionSeconds,
            float decisionSeconds,
            float scheduleJitterSeconds,
            float aimNoiseDegrees,
            float predictionErrorFraction,
            float aerialMissChance = 0f,
            float aerialMissMagnitude = 0f)
        {
            ReactionSeconds = reactionSeconds;
            DecisionSeconds = decisionSeconds;
            ScheduleJitterSeconds = scheduleJitterSeconds;
            AimNoiseDegrees = aimNoiseDegrees;
            PredictionErrorFraction = predictionErrorFraction;
            AerialMissChance = aerialMissChance;
            AerialMissMagnitude = aerialMissMagnitude;
        }

        public float ReactionSeconds { get; }
        public float DecisionSeconds { get; }
        public float ScheduleJitterSeconds { get; }
        public float AimNoiseDegrees { get; }
        public float PredictionErrorFraction { get; }
        public float AerialMissChance { get; }
        public float AerialMissMagnitude { get; }
        public float AerialMissProbability => AerialMissChance;
        public float AerialMissOffsetMagnitude => AerialMissMagnitude;
    }

    public readonly struct BotBallObservation
    {
        public BotBallObservation(
            bool hasObservation,
            bool isVisible,
            bool isGrounded,
            Vector3 position,
            Vector3 velocity,
            float ageSeconds)
        {
            HasObservation = hasObservation;
            IsVisible = isVisible;
            IsGrounded = isGrounded;
            Position = position;
            Velocity = velocity;
            AgeSeconds = ageSeconds;
        }

        public bool HasObservation { get; }
        public bool IsVisible { get; }
        public bool IsGrounded { get; }
        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public float AgeSeconds { get; }
    }

    public readonly struct BotParticipantObservation
    {
        public BotParticipantObservation(
            bool hasObservation,
            bool isVisible,
            int slotId,
            ParticipantTeam team,
            bool isLocalParticipant,
            bool isAlive,
            Vector3 position,
            Vector3 velocity,
            float health,
            float maxHealth,
            bool hasShotgun,
            int shotgunShells,
            int shotgunShellCapacity,
            float ageSeconds)
        {
            HasObservation = hasObservation;
            IsVisible = isVisible;
            SlotId = slotId;
            Team = team;
            IsLocalParticipant = isLocalParticipant;
            IsAlive = isAlive;
            Position = position;
            Velocity = velocity;
            Health = health;
            MaxHealth = maxHealth;
            HasShotgun = hasShotgun;
            ShotgunShells = shotgunShells;
            ShotgunShellCapacity = shotgunShellCapacity;
            AgeSeconds = ageSeconds;
        }

        public bool HasObservation { get; }
        public bool IsVisible { get; }
        public int SlotId { get; }
        public ParticipantTeam Team { get; }
        public bool IsLocalParticipant { get; }
        public bool IsAlive { get; }
        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public float Health { get; }
        public float MaxHealth { get; }
        public bool HasShotgun { get; }
        public int ShotgunShells { get; }
        public int ShotgunShellCapacity { get; }
        public float AgeSeconds { get; }
    }

    public readonly struct BotPickupObservation
    {
        public BotPickupObservation(
            int stableId,
            BotPickupKind kind,
            Vector3 position,
            bool hasObservation,
            bool isVisible,
            bool isAvailable,
            bool hasWitnessedRespawn,
            float estimatedRespawnRemaining,
            float ageSeconds)
        {
            StableId = stableId;
            Kind = kind;
            Position = position;
            HasObservation = hasObservation;
            IsVisible = isVisible;
            IsAvailable = isAvailable;
            HasWitnessedRespawn = hasWitnessedRespawn;
            EstimatedRespawnRemaining = estimatedRespawnRemaining;
            AgeSeconds = ageSeconds;
        }

        public int StableId { get; }
        public BotPickupKind Kind { get; }
        public Vector3 Position { get; }
        public bool HasObservation { get; }
        public bool IsVisible { get; }
        public bool IsAvailable { get; }
        public bool HasWitnessedRespawn { get; }
        public float EstimatedRespawnRemaining { get; }
        public float AgeSeconds { get; }
    }

    public readonly struct BotAimSolution
    {
        public BotAimSolution(
            bool isValid,
            bool usedIntercept,
            Vector3 aimPoint,
            Vector3 direction,
            float interceptTime)
        {
            IsValid = isValid;
            UsedIntercept = usedIntercept;
            AimPoint = aimPoint;
            Direction = direction;
            InterceptTime = interceptTime;
        }

        public bool IsValid { get; }
        public bool UsedIntercept { get; }
        public Vector3 AimPoint { get; }
        public Vector3 Direction { get; }
        public float InterceptTime { get; }

        public static BotAimSolution Invalid => new BotAimSolution(
            false,
            false,
            Vector3.zero,
            Vector3.zero,
            0f);
    }

    public readonly struct BotTargetKey
    {
        public BotTargetKey(BotTargetKind kind, int subjectId)
        {
            Kind = kind;
            SubjectId = subjectId;
        }

        public BotTargetKind Kind { get; }
        public int SubjectId { get; }
    }

    public readonly struct BotCombatInput
    {
        /// <summary>
        /// Creates combat input without the legacy target score. Enemy combat eligibility is
        /// governed by current visibility and the normal action gates, never by target score.
        /// </summary>
        public BotCombatInput(
            BotDifficulty difficulty,
            BotRole role,
            BotTargetKind activeTargetKind,
            bool suppressParticipantCombat,
            bool preferBallActions,
            Vector3 actionOrigin,
            Vector3 ballRocketLaunchPosition,
            Vector3 enemyRocketLaunchPosition,
            Vector3 rocketJumpLaunchPosition,
            Vector3 routeForwardXZ,
            BotBallObservation ball,
            BotParticipantObservation enemy,
            BotParticipantObservation allyA,
            BotParticipantObservation allyB,
            bool ballRouteReachable,
            bool enemyRouteReachable,
            bool dashReady,
            bool shotgunReady,
            bool launcherReady,
            BotAimSolution ballDirectAim,
            BotAimSolution ballRocketAim,
            BotAimSolution enemyDirectAim,
            BotAimSolution enemyRocketAim,
            bool rocketLineClearToBall,
            bool rocketLineClearToEnemy,
            bool isGrounded,
            bool upwardTransitionRequired,
            Vector3 rocketJumpAimPoint,
            bool rocketJumpLineClear)
            : this(
                difficulty,
                role,
                activeTargetKind,
                0f,
                suppressParticipantCombat,
                preferBallActions,
                actionOrigin,
                ballRocketLaunchPosition,
                enemyRocketLaunchPosition,
                rocketJumpLaunchPosition,
                routeForwardXZ,
                ball,
                enemy,
                allyA,
                allyB,
                ballRouteReachable,
                enemyRouteReachable,
                dashReady,
                shotgunReady,
                launcherReady,
                ballDirectAim,
                ballRocketAim,
                enemyDirectAim,
                enemyRocketAim,
                rocketLineClearToBall,
                rocketLineClearToEnemy,
                isGrounded,
                upwardTransitionRequired,
                rocketJumpAimPoint,
                rocketJumpLineClear)
        {
        }

        /// <summary>
        /// Legacy overload retained for source compatibility while callers migrate away from
        /// target-score gating. The score is intentionally ignored by combat rules.
        /// </summary>
        public BotCombatInput(
            BotDifficulty difficulty,
            BotRole role,
            BotTargetKind activeTargetKind,
            float legacyActiveTargetScore,
            bool suppressParticipantCombat,
            bool preferBallActions,
            Vector3 actionOrigin,
            Vector3 ballRocketLaunchPosition,
            Vector3 enemyRocketLaunchPosition,
            Vector3 rocketJumpLaunchPosition,
            Vector3 routeForwardXZ,
            BotBallObservation ball,
            BotParticipantObservation enemy,
            BotParticipantObservation allyA,
            BotParticipantObservation allyB,
            bool ballRouteReachable,
            bool enemyRouteReachable,
            bool dashReady,
            bool shotgunReady,
            bool launcherReady,
            BotAimSolution ballDirectAim,
            BotAimSolution ballRocketAim,
            BotAimSolution enemyDirectAim,
            BotAimSolution enemyRocketAim,
            bool rocketLineClearToBall,
            bool rocketLineClearToEnemy,
            bool isGrounded,
            bool upwardTransitionRequired,
            Vector3 rocketJumpAimPoint,
            bool rocketJumpLineClear)
        {
            Difficulty = difficulty;
            Role = role;
            ActiveTargetKind = activeTargetKind;
            SuppressParticipantCombat = suppressParticipantCombat;
            PreferBallActions = preferBallActions;
            ActionOrigin = actionOrigin;
            BallRocketLaunchPosition = ballRocketLaunchPosition;
            EnemyRocketLaunchPosition = enemyRocketLaunchPosition;
            RocketJumpLaunchPosition = rocketJumpLaunchPosition;
            RouteForwardXZ = routeForwardXZ;
            Ball = ball;
            Enemy = enemy;
            AllyA = allyA;
            AllyB = allyB;
            BallRouteReachable = ballRouteReachable;
            EnemyRouteReachable = enemyRouteReachable;
            DashReady = dashReady;
            ShotgunReady = shotgunReady;
            LauncherReady = launcherReady;
            BallDirectAim = ballDirectAim;
            BallRocketAim = ballRocketAim;
            EnemyDirectAim = enemyDirectAim;
            EnemyRocketAim = enemyRocketAim;
            RocketLineClearToBall = rocketLineClearToBall;
            RocketLineClearToEnemy = rocketLineClearToEnemy;
            IsGrounded = isGrounded;
            UpwardTransitionRequired = upwardTransitionRequired;
            RocketJumpAimPoint = rocketJumpAimPoint;
            RocketJumpLineClear = rocketJumpLineClear;
        }

        public BotDifficulty Difficulty { get; }
        public BotRole Role { get; }
        public BotTargetKind ActiveTargetKind { get; }
        public bool SuppressParticipantCombat { get; }
        public bool PreferBallActions { get; }
        public Vector3 ActionOrigin { get; }
        public Vector3 BallRocketLaunchPosition { get; }
        public Vector3 EnemyRocketLaunchPosition { get; }
        public Vector3 RocketJumpLaunchPosition { get; }
        public Vector3 RouteForwardXZ { get; }
        public BotBallObservation Ball { get; }
        public BotParticipantObservation Enemy { get; }
        public BotParticipantObservation AllyA { get; }
        public BotParticipantObservation AllyB { get; }
        public bool BallRouteReachable { get; }
        public bool EnemyRouteReachable { get; }
        public bool DashReady { get; }
        public bool ShotgunReady { get; }
        public bool LauncherReady { get; }
        public BotAimSolution BallDirectAim { get; }
        public BotAimSolution BallRocketAim { get; }
        public BotAimSolution EnemyDirectAim { get; }
        public BotAimSolution EnemyRocketAim { get; }
        public bool RocketLineClearToBall { get; }
        public bool RocketLineClearToEnemy { get; }
        public bool IsGrounded { get; }
        public bool UpwardTransitionRequired { get; }
        public Vector3 RocketJumpAimPoint { get; }
        public bool RocketJumpLineClear { get; }
    }

    public readonly struct BotCombatResult
    {
        public BotCombatResult(
            BotCombatAction action,
            BotCombatTarget target,
            Vector3 aimPoint,
            Vector3 fireAimDirection,
            Vector3 bodyFacingDirection,
            Vector3 launchPosition)
        {
            Action = action;
            Target = target;
            AimPoint = aimPoint;
            FireAimDirection = fireAimDirection;
            BodyFacingDirection = bodyFacingDirection;
            LaunchPosition = launchPosition;
        }

        /// <summary>
        /// Legacy constructor maps one direction to both fire and body facing. New actions should
        /// use the six-argument constructor so rocket jumps can fire down/back while facing route.
        /// </summary>
        public BotCombatResult(
            BotCombatAction action,
            BotCombatTarget target,
            Vector3 aimPoint,
            Vector3 aimDirection,
            Vector3 launchPosition)
            : this(action, target, aimPoint, aimDirection, aimDirection, launchPosition)
        {
        }

        public BotCombatAction Action { get; }
        public BotCombatTarget Target { get; }
        public Vector3 AimPoint { get; }
        public Vector3 FireAimDirection { get; }
        public Vector3 BodyFacingDirection { get; }
        public Vector3 AimDirection => FireAimDirection;
        public Vector3 LaunchPosition { get; }
        public bool HasAction => Action != BotCombatAction.None;

        public static BotCombatResult None => new BotCombatResult(
            BotCombatAction.None,
            BotCombatTarget.None,
            Vector3.zero,
            Vector3.zero,
            Vector3.zero);
    }
}
