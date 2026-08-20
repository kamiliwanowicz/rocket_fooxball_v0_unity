using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    public readonly struct BotTargetCandidate
    {
        public BotTargetCandidate(
            BotTargetKey key,
            float score,
            Vector3 navigationPosition,
            Vector3 actionPosition,
            bool suppressParticipantCombat,
            bool preferBallActions,
            bool reachable,
            float routeCost,
            float expiresAt)
        {
            Key = key;
            Score = score;
            NavigationPosition = navigationPosition;
            ActionPosition = actionPosition;
            SuppressParticipantCombat = suppressParticipantCombat;
            PreferBallActions = preferBallActions;
            Reachable = reachable;
            RouteCost = routeCost;
            ExpiresAt = expiresAt;
        }

        public BotTargetKey Key { get; }
        public float Score { get; }
        public Vector3 NavigationPosition { get; }
        public Vector3 ActionPosition { get; }
        public bool SuppressParticipantCombat { get; }
        public bool PreferBallActions { get; }
        public bool Reachable { get; }
        public float RouteCost { get; }
        public float ExpiresAt { get; }
    }

    public readonly struct BotTargetSelection
    {
        public BotTargetSelection(
            bool hasTarget,
            BotTargetKey key,
            float score,
            Vector3 navigationPosition,
            Vector3 actionPosition,
            bool suppressParticipantCombat,
            bool preferBallActions,
            float routeCost,
            float expiresAt)
        {
            HasTarget = hasTarget;
            Key = key;
            Score = score;
            NavigationPosition = navigationPosition;
            ActionPosition = actionPosition;
            SuppressParticipantCombat = suppressParticipantCombat;
            PreferBallActions = preferBallActions;
            RouteCost = routeCost;
            ExpiresAt = expiresAt;
        }

        public bool HasTarget { get; }
        public BotTargetKey Key { get; }
        public float Score { get; }
        public Vector3 NavigationPosition { get; }
        public Vector3 ActionPosition { get; }
        public bool SuppressParticipantCombat { get; }
        public bool PreferBallActions { get; }
        public float RouteCost { get; }
        public float ExpiresAt { get; }

        public static BotTargetSelection None => new BotTargetSelection(
            false,
            new BotTargetKey(BotTargetKind.None, 0),
            0f,
            Vector3.zero,
            Vector3.zero,
            false,
            false,
            0f,
            0f);
    }

    public static class BotTargetRules
    {
        public const float OwnGoalEmergencyScore = 100f;
        public const float DefenderCoverageScore = 90f;
        public const float AttackerBallScore = 70f;
        public const float SupportLaneScore = 55f;
        public const float HealthPickupScore = 35f;
        public const float ShotgunPickupScore = 25f;
        public const float AmmoPickupScore = 25f;
        public const float VisibleEnemyScore = 20f;
        public const float RecentEnemyScore = 15f;
        public const float BallFallbackScore = 0f;

        public const float HumanShotgunYieldDistance = 12f;
        public const float HealthYieldDistance = 10f;
        public const float CriticalHealthRatio = 0.30f;

        public static BotTargetSelection Consider(BotTargetSelection current, BotTargetCandidate candidate)
        {
            if (!IsValid(candidate))
                return current;

            if (!current.HasTarget || !IsValid(current))
                return ToSelection(candidate);

            if (IsHigherPriority(candidate, current))
                return ToSelection(candidate);

            return current;
        }

        public static bool IsValid(BotTargetCandidate candidate)
        {
            return IsValidKey(candidate.Key) &&
                IsFinite(candidate.Score) &&
                IsFinite(candidate.NavigationPosition) &&
                IsFinite(candidate.ActionPosition) &&
                candidate.Reachable &&
                IsFinite(candidate.RouteCost) && candidate.RouteCost >= 0f &&
                IsFinite(candidate.ExpiresAt) && candidate.ExpiresAt >= 0f;
        }

        public static bool IsValid(BotTargetSelection selection)
        {
            return selection.HasTarget &&
                IsValidKey(selection.Key) &&
                IsFinite(selection.Score) &&
                IsFinite(selection.NavigationPosition) &&
                IsFinite(selection.ActionPosition) &&
                IsFinite(selection.RouteCost) && selection.RouteCost >= 0f &&
                IsFinite(selection.ExpiresAt) && selection.ExpiresAt >= 0f;
        }

        public static float ScoreFor(BotTargetKind kind)
        {
            switch (kind)
            {
                case BotTargetKind.OwnGoalEmergency:
                    return OwnGoalEmergencyScore;
                case BotTargetKind.DefenderCoverage:
                    return DefenderCoverageScore;
                case BotTargetKind.AttackerBall:
                    return AttackerBallScore;
                case BotTargetKind.SupportLane:
                    return SupportLaneScore;
                case BotTargetKind.HealthPickup:
                    return HealthPickupScore;
                case BotTargetKind.ShotgunPickup:
                    return ShotgunPickupScore;
                case BotTargetKind.AmmoPickup:
                    return AmmoPickupScore;
                case BotTargetKind.EnemyOpportunity:
                    return VisibleEnemyScore;
                case BotTargetKind.BallFallback:
                    return BallFallbackScore;
                default:
                    return float.NaN;
            }
        }

        public static float ScoreFor(BotTargetKind kind, bool visible)
        {
            return kind == BotTargetKind.EnemyOpportunity ? ScoreForEnemy(visible) : ScoreFor(kind);
        }

        public static float ScoreForEnemy(bool visible)
        {
            return visible ? VisibleEnemyScore : RecentEnemyScore;
        }

        public static void GetActionFlags(
            BotTargetKind kind,
            bool hasValidBallInOwnHalf,
            out bool suppressParticipantCombat,
            out bool preferBallActions)
        {
            var ballPriority = kind == BotTargetKind.OwnGoalEmergency ||
                kind == BotTargetKind.AttackerBall ||
                kind == BotTargetKind.BallFallback ||
                (kind == BotTargetKind.DefenderCoverage && hasValidBallInOwnHalf);
            suppressParticipantCombat = ballPriority;
            preferBallActions = ballPriority;
        }

        public static bool ShouldYieldShotgun(BotParticipantObservation observer, BotParticipantObservation teammate)
        {
            if (!observer.HasObservation || !observer.IsAlive || !teammate.HasObservation || !teammate.IsAlive)
                return false;
            if (observer.SlotId == teammate.SlotId || observer.Team != teammate.Team || !teammate.IsLocalParticipant || teammate.HasShotgun)
                return false;
            return IsFinite(observer.Position) && IsFinite(teammate.Position) &&
                Vector3.Distance(observer.Position, teammate.Position) <= HumanShotgunYieldDistance;
        }

        public static bool ShouldYieldHealth(BotParticipantObservation observer, BotParticipantObservation teammate)
        {
            if (!observer.HasObservation || !observer.IsAlive || !teammate.HasObservation || !teammate.IsAlive)
                return false;
            if (observer.SlotId == teammate.SlotId || observer.Team != teammate.Team ||
                !IsFinite(observer.Position) || !IsFinite(teammate.Position) ||
                Vector3.Distance(observer.Position, teammate.Position) > HealthYieldDistance)
                return false;
            if (!TryGetHealthRatio(observer, out var observerRatio) || !TryGetHealthRatio(teammate, out var teammateRatio))
                return false;

            if (teammateRatio < observerRatio)
                return teammateRatio <= CriticalHealthRatio;
            return teammateRatio == observerRatio && teammate.SlotId < observer.SlotId &&
                teammateRatio <= CriticalHealthRatio;
        }

        private static BotTargetSelection ToSelection(BotTargetCandidate candidate)
        {
            return new BotTargetSelection(
                true,
                candidate.Key,
                candidate.Score,
                candidate.NavigationPosition,
                candidate.ActionPosition,
                candidate.SuppressParticipantCombat,
                candidate.PreferBallActions,
                candidate.RouteCost,
                candidate.ExpiresAt);
        }

        private static bool IsHigherPriority(BotTargetCandidate candidate, BotTargetSelection current)
        {
            if (candidate.Score != current.Score)
                return candidate.Score > current.Score;
            if (candidate.RouteCost != current.RouteCost)
                return candidate.RouteCost < current.RouteCost;
            var candidateKind = (int)candidate.Key.Kind;
            var currentKind = (int)current.Key.Kind;
            if (candidateKind != currentKind)
                return candidateKind < currentKind;
            return candidate.Key.SubjectId < current.Key.SubjectId;
        }

        private static bool IsValidKey(BotTargetKey key)
        {
            return key.Kind >= BotTargetKind.OwnGoalEmergency && key.Kind <= BotTargetKind.BallFallback;
        }

        private static bool TryGetHealthRatio(BotParticipantObservation participant, out float ratio)
        {
            ratio = 0f;
            if (!IsFinite(participant.Health) || participant.Health < 0f ||
                !IsFinite(participant.MaxHealth) || participant.MaxHealth <= 0f)
                return false;
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
