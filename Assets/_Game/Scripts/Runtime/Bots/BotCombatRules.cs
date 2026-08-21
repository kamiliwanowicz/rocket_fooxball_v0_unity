using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    /// <summary>Pure football-first arbitration for one bot combat decision.</summary>
    public static class BotCombatRules
    {
        public const float DashRange = 2.2f;
        public const float BallShotgunRange = 20f;
        public const float EnemyShotgunRange = 16f;
        public const float RocketCorridorRadius = 1.5f;

        private const float Epsilon = 0.000001f;

        /// <summary>Chooses at most one action without querying or mutating any Unity object.</summary>
        public static BotCombatResult Evaluate(in BotCombatInput input)
        {
            if (input.PreferBallActions)
            {
                var ballResult = EvaluateBallActions(input);
                if (ballResult.HasAction)
                {
                    return ballResult;
                }
            }

            if (input.SuppressParticipantCombat)
            {
                return BotCombatResult.None;
            }

            if (CanRocketJump(input) && !HasEligibleBallAction(input))
            {
                return new BotCombatResult(
                    BotCombatAction.RocketJump,
                    BotCombatTarget.SelfImpact,
                    input.RocketJumpAimPoint,
                    GetRocketJumpDirection(input.RouteForwardXZ),
                    GetBodyFacingDirection(input.RouteForwardXZ),
                    input.RocketJumpLaunchPosition);
            }

            return EvaluateEnemyActions(input);
        }

        /// <summary>
        /// Returns the normalized underfoot blast direction for a rocket jump. A route with no
        /// finite, non-zero direction cannot produce a safe jump impulse.
        /// </summary>
        public static Vector3 GetRocketJumpDirection(Vector3 routeForwardXZ)
        {
            if (!IsFinite(routeForwardXZ))
            {
                return Vector3.zero;
            }

            routeForwardXZ.y = 0f;
            var routeMagnitudeSquared = routeForwardXZ.sqrMagnitude;
            if (!IsFinite(routeMagnitudeSquared) || routeMagnitudeSquared <= Epsilon)
            {
                return Vector3.zero;
            }

            var direction = Vector3.down - routeForwardXZ.normalized * 0.25f;
            var directionMagnitudeSquared = direction.sqrMagnitude;
            if (!IsFinite(direction) || !IsFinite(directionMagnitudeSquared) || directionMagnitudeSquared <= Epsilon)
            {
                return Vector3.zero;
            }

            return direction.normalized;
        }

        private static BotCombatResult EvaluateBallActions(in BotCombatInput input)
        {
            if (!IsVisibleBall(input) || !input.BallRouteReachable)
            {
                return BotCombatResult.None;
            }

            var distance = DistanceFromActionOrigin(input.ActionOrigin, input.Ball.Position);
            if (!IsFinite(distance))
            {
                return BotCombatResult.None;
            }

            if (input.DashReady && distance <= DashRange && IsUsableDirectAim(input.BallDirectAim))
            {
                return DirectResult(BotCombatAction.DashKick, BotCombatTarget.Ball, input.BallDirectAim);
            }

            if (input.ShotgunReady && distance <= BallShotgunRange && IsUsableDirectAim(input.BallDirectAim))
            {
                return DirectResult(BotCombatAction.FireShotgun, BotCombatTarget.Ball, input.BallDirectAim);
            }

            if (IsEligibleBallRocket(input))
            {
                return RocketResult(BotCombatTarget.Ball, input.BallRocketAim, input.BallRocketLaunchPosition);
            }

            return BotCombatResult.None;
        }

        private static BotCombatResult EvaluateEnemyActions(in BotCombatInput input)
        {
            if (!IsEligibleEnemy(input))
            {
                return BotCombatResult.None;
            }

            var distance = DistanceFromActionOrigin(input.ActionOrigin, input.Enemy.Position);
            if (!IsFinite(distance))
            {
                return BotCombatResult.None;
            }

            if (input.DashReady && distance <= DashRange && IsUsableDirectAim(input.EnemyDirectAim))
            {
                return DirectResult(BotCombatAction.DashKick, BotCombatTarget.Enemy, input.EnemyDirectAim);
            }

            if (input.ShotgunReady && distance <= EnemyShotgunRange && IsUsableDirectAim(input.EnemyDirectAim))
            {
                return DirectResult(BotCombatAction.FireShotgun, BotCombatTarget.Enemy, input.EnemyDirectAim);
            }

            if (IsEligibleEnemyRocket(input))
            {
                return RocketResult(BotCombatTarget.Enemy, input.EnemyRocketAim, input.EnemyRocketLaunchPosition);
            }

            return BotCombatResult.None;
        }

        private static bool HasEligibleBallAction(in BotCombatInput input)
        {
            if (!IsVisibleBall(input) || !input.BallRouteReachable)
            {
                return false;
            }

            var distance = DistanceFromActionOrigin(input.ActionOrigin, input.Ball.Position);
            if (!IsFinite(distance))
            {
                return false;
            }

            return (input.DashReady && distance <= DashRange && IsUsableDirectAim(input.BallDirectAim)) ||
                   (input.ShotgunReady && distance <= BallShotgunRange && IsUsableDirectAim(input.BallDirectAim)) ||
                   IsEligibleBallRocket(input);
        }

        private static bool IsVisibleBall(in BotCombatInput input)
        {
            return input.Ball.HasObservation && input.Ball.IsVisible && IsFinite(input.Ball.Position);
        }

        private static bool IsEligibleEnemy(in BotCombatInput input)
        {
            return BotCombatEnemyRules.IsEligibleVisibleEnemy(input.Enemy);
        }

        private static bool IsEligibleBallRocket(in BotCombatInput input)
        {
            return input.LauncherReady && input.BallRouteReachable && input.RocketLineClearToBall &&
                   IsUsableRocketAim(input.BallRocketAim) &&
                   IsCorridorClear(input.BallRocketLaunchPosition, input.BallRocketAim.AimPoint, input.AllyA, input.AllyB);
        }

        private static bool IsEligibleEnemyRocket(in BotCombatInput input)
        {
            return input.LauncherReady && input.EnemyRouteReachable && input.RocketLineClearToEnemy &&
                   IsUsableRocketAim(input.EnemyRocketAim) &&
                   IsCorridorClear(input.EnemyRocketLaunchPosition, input.EnemyRocketAim.AimPoint, input.AllyA, input.AllyB);
        }

        private static bool CanRocketJump(in BotCombatInput input)
        {
            var direction = GetRocketJumpDirection(input.RouteForwardXZ);
            return input.Difficulty == BotDifficulty.High && input.IsGrounded &&
                   input.UpwardTransitionRequired && input.LauncherReady && input.RocketJumpLineClear &&
                   direction.sqrMagnitude > Epsilon && IsFinite(input.RocketJumpLaunchPosition) &&
                   IsFinite(input.RocketJumpAimPoint) &&
                   GetBodyFacingDirection(input.RouteForwardXZ).sqrMagnitude > Epsilon &&
                   IsCorridorClear(input.RocketJumpLaunchPosition, input.RocketJumpAimPoint, input.AllyA, input.AllyB);
        }

        private static BotCombatResult DirectResult(BotCombatAction action, BotCombatTarget target, BotAimSolution aim)
        {
            return new BotCombatResult(
                action,
                target,
                aim.AimPoint,
                aim.Direction,
                aim.Direction,
                Vector3.zero);
        }

        private static BotCombatResult RocketResult(BotCombatTarget target, BotAimSolution aim, Vector3 launchPosition)
        {
            return new BotCombatResult(
                BotCombatAction.FireRocket,
                target,
                aim.AimPoint,
                aim.Direction,
                aim.Direction,
                launchPosition);
        }

        /// <summary>Returns a finite normalized horizontal route direction for root facing.</summary>
        public static Vector3 GetBodyFacingDirection(Vector3 routeForwardXZ)
        {
            if (!IsFinite(routeForwardXZ))
            {
                return Vector3.zero;
            }

            routeForwardXZ.y = 0f;
            var magnitudeSquared = routeForwardXZ.sqrMagnitude;
            if (!IsFinite(magnitudeSquared) || magnitudeSquared <= Epsilon)
            {
                return Vector3.zero;
            }

            var direction = routeForwardXZ.normalized;
            return IsFinite(direction) && direction.sqrMagnitude > Epsilon
                ? direction
                : Vector3.zero;
        }

        private static bool IsUsableDirectAim(BotAimSolution aim)
        {
            return aim.IsValid && IsFinite(aim.AimPoint) && IsFinite(aim.Direction) &&
                   aim.Direction.sqrMagnitude > Epsilon;
        }

        private static bool IsUsableRocketAim(BotAimSolution aim)
        {
            return aim.UsedIntercept && IsUsableDirectAim(aim) && IsFinite(aim.InterceptTime) && aim.InterceptTime >= 0f;
        }

        private static bool IsCorridorClear(Vector3 start, Vector3 end, BotParticipantObservation allyA, BotParticipantObservation allyB)
        {
            if (!IsFinite(start) || !IsFinite(end))
            {
                return false;
            }

            return IsAllyClear(start, end, allyA) && IsAllyClear(start, end, allyB);
        }

        private static bool IsAllyClear(Vector3 start, Vector3 end, BotParticipantObservation ally)
        {
            if (!ally.IsAlive)
            {
                return true;
            }

            // A live ally without a complete finite observation is known-live but unsafe to
            // reason about. Refuse the blast rather than guessing that the corridor is empty.
            if (!ally.HasObservation || !IsFinite(ally.Position))
            {
                return false;
            }

            var distanceSquared = DistanceToSegmentSquared(ally.Position, start, end);
            return IsFinite(distanceSquared) && distanceSquared > RocketCorridorRadius * RocketCorridorRadius;
        }

        private static float DistanceFromActionOrigin(Vector3 origin, Vector3 target)
        {
            if (!IsFinite(origin) || !IsFinite(target))
            {
                return float.NaN;
            }

            var distance = Vector3.Distance(origin, target);
            return IsFinite(distance) ? distance : float.NaN;
        }

        private static float DistanceToSegmentSquared(Vector3 point, Vector3 start, Vector3 end)
        {
            if (!IsFinite(point) || !IsFinite(start) || !IsFinite(end))
            {
                return float.NaN;
            }

            var segment = end - start;
            var lengthSquared = segment.sqrMagnitude;
            if (!IsFinite(segment) || !IsFinite(lengthSquared))
            {
                return float.NaN;
            }

            if (lengthSquared <= Epsilon)
            {
                var offsetSquared = (point - start).sqrMagnitude;
                return IsFinite(offsetSquared) ? offsetSquared : float.NaN;
            }

            var offset = point - start;
            var dot = Vector3.Dot(offset, segment);
            var t = dot / lengthSquared;
            if (!IsFinite(dot) || !IsFinite(t))
            {
                return float.NaN;
            }

            t = Mathf.Clamp01(t);
            var closest = start + segment * t;
            var distanceSquared = (point - closest).sqrMagnitude;
            return IsFinite(distanceSquared) ? distanceSquared : float.NaN;
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
