using System;
using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    /// <summary>Pure deterministic direct aim, intercept, prediction-error, and aim-noise rules.</summary>
    public static class BotAimRules
    {
        public const float DefaultMinPitchDegrees = -80f;
        public const float DefaultMaxPitchDegrees = 80f;

        private const float Epsilon = 0.000001f;
        private const double QuadraticEpsilon = 0.000001d;

        /// <summary>
        /// Solves the constant-speed intercept equation using double precision. Outputs remain
        /// finite-zero on every failure so callers cannot accidentally fire a fallback shot.
        /// </summary>
        public static bool TrySolveIntercept(
            Vector3 origin,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float projectileSpeed,
            out float interceptTime,
            out Vector3 interceptPoint)
        {
            interceptTime = 0f;
            interceptPoint = Vector3.zero;

            if (!IsFinite(origin) || !IsFinite(targetPosition) || !IsFinite(targetVelocity) ||
                !IsFinite(projectileSpeed) || projectileSpeed <= 0f)
            {
                return false;
            }

            var rx = (double)targetPosition.x - origin.x;
            var ry = (double)targetPosition.y - origin.y;
            var rz = (double)targetPosition.z - origin.z;
            var vx = (double)targetVelocity.x;
            var vy = (double)targetVelocity.y;
            var vz = (double)targetVelocity.z;
            var speed = (double)projectileSpeed;
            var a = vx * vx + vy * vy + vz * vz - speed * speed;
            var b = 2d * (rx * vx + ry * vy + rz * vz);
            var c = rx * rx + ry * ry + rz * rz;

            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
            {
                return false;
            }

            if (c <= QuadraticEpsilon)
            {
                interceptPoint = targetPosition;
                return true;
            }

            double candidate;
            if (Math.Abs(a) <= QuadraticEpsilon)
            {
                if (Math.Abs(b) <= QuadraticEpsilon)
                {
                    return false;
                }

                candidate = -c / b;
                if (!IsFinite(candidate) || candidate < 0d)
                {
                    return false;
                }
            }
            else
            {
                var discriminant = b * b - 4d * a * c;
                if (!IsFinite(discriminant) || discriminant < 0d)
                {
                    return false;
                }

                var root = Math.Sqrt(discriminant);
                var first = (-b - root) / (2d * a);
                var second = (-b + root) / (2d * a);
                candidate = double.PositiveInfinity;
                if (IsFinite(first) && first >= 0d)
                {
                    candidate = first;
                }

                if (IsFinite(second) && second >= 0d && second < candidate)
                {
                    candidate = second;
                }

                if (!IsFinite(candidate))
                {
                    return false;
                }
            }

            if (candidate > float.MaxValue)
            {
                return false;
            }

            interceptTime = (float)candidate;
            if (!IsFinite(interceptTime))
            {
                interceptTime = 0f;
                return false;
            }

            interceptPoint = targetPosition + targetVelocity * interceptTime;
            if (!IsFinite(interceptPoint))
            {
                interceptTime = 0f;
                interceptPoint = Vector3.zero;
                return false;
            }

            return true;
        }

        /// <summary>Applies one deterministic prediction-error sample to an ideal time.</summary>
        public static float ApplyPredictionError(float idealTime, float maxFraction, int slotId, int ordinal)
        {
            if (!IsFinite(idealTime) || idealTime <= 0f)
            {
                return 0f;
            }

            var safeFraction = IsFinite(maxFraction) ? Mathf.Clamp01(maxFraction) : 0f;
            var scale = 1f + BotDifficultyRules.SignedSample(slotId, ordinal, BotSampleChannel.PredictionError) * safeFraction;
            var perturbed = idealTime * scale;
            return IsFinite(perturbed) && perturbed > 0f ? perturbed : 0f;
        }

        public static float ApplyPredictionError(float idealTime, int slotId, int ordinal, float maxFraction)
        {
            return ApplyPredictionError(idealTime, maxFraction, slotId, ordinal);
        }

        public static float ApplyPredictionError(
            float idealTime,
            BotDifficulty difficulty,
            int slotId,
            int ordinal)
        {
            return ApplyPredictionError(
                idealTime,
                BotDifficultyRules.GetParameters(difficulty).PredictionErrorFraction,
                slotId,
                ordinal);
        }

        /// <summary>
        /// Applies one deterministic cone sample around a normalized direction. The reference
        /// basis is fixed to world up except when the direction is nearly parallel to it.
        /// </summary>
        public static Vector3 ApplyAimNoise(Vector3 direction, float maxDegrees, int slotId, int ordinal)
        {
            if (!IsFinite(direction) || direction.sqrMagnitude <= Epsilon)
            {
                return Vector3.zero;
            }

            var forward = direction.normalized;
            if (!IsFinite(forward) || forward.sqrMagnitude <= Epsilon)
            {
                return Vector3.zero;
            }
            var reference = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.999f
                ? Vector3.up
                : Vector3.right;
            var right = Vector3.Cross(reference, forward).normalized;
            var up = Vector3.Cross(forward, right);
            if (!IsFinite(right) || !IsFinite(up) || right.sqrMagnitude <= Epsilon || up.sqrMagnitude <= Epsilon)
            {
                return Vector3.zero;
            }

            var safeDegrees = IsFinite(maxDegrees) ? Mathf.Clamp(maxDegrees, 0f, 89f) : 0f;
            var coneRadians = Mathf.Sqrt(BotDifficultyRules.Sample01(slotId, ordinal, BotSampleChannel.AimConeRadius)) *
                              safeDegrees * Mathf.Deg2Rad;
            var azimuth = 2f * Mathf.PI * BotDifficultyRules.Sample01(slotId, ordinal, BotSampleChannel.AimConeAzimuth);
            var radial = Mathf.Cos(azimuth) * right + Mathf.Sin(azimuth) * up;
            var noisy = forward * Mathf.Cos(coneRadians) + radial * Mathf.Sin(coneRadians);
            if (!IsFinite(noisy) || noisy.sqrMagnitude <= Epsilon)
            {
                return Vector3.zero;
            }

            return noisy.normalized;
        }

        public static Vector3 ApplyAimNoise(
            Vector3 direction,
            BotDifficulty difficulty,
            int slotId,
            int ordinal)
        {
            return ApplyAimNoise(
                direction,
                BotDifficultyRules.GetParameters(difficulty).AimNoiseDegrees,
                slotId,
                ordinal);
        }

        /// <summary>Clamps a direction's pitch to the default [-80, 80] degree range.</summary>
        public static Vector3 ClampPitch(Vector3 direction)
        {
            return ClampPitch(direction, DefaultMinPitchDegrees, DefaultMaxPitchDegrees);
        }

        /// <summary>Clamps a direction's pitch while preserving its yaw.</summary>
        public static Vector3 ClampPitch(Vector3 direction, float minPitchDegrees, float maxPitchDegrees)
        {
            if (!IsFinite(direction) || direction.sqrMagnitude <= Epsilon)
            {
                return Vector3.zero;
            }

            var normalized = direction.normalized;
            if (!IsFinite(normalized) || normalized.sqrMagnitude <= Epsilon)
            {
                return Vector3.zero;
            }
            var minPitch = IsFinite(minPitchDegrees) ? minPitchDegrees : DefaultMinPitchDegrees;
            var maxPitch = IsFinite(maxPitchDegrees) ? maxPitchDegrees : DefaultMaxPitchDegrees;
            if (minPitch > maxPitch)
            {
                var swap = minPitch;
                minPitch = maxPitch;
                maxPitch = swap;
            }

            var horizontal = new Vector3(normalized.x, 0f, normalized.z);
            var horizontalMagnitude = horizontal.magnitude;
            if (horizontalMagnitude <= Epsilon)
            {
                horizontal = Vector3.forward;
            }
            else
            {
                horizontal /= horizontalMagnitude;
            }

            var pitch = Mathf.Atan2(normalized.y, horizontalMagnitude) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch) * Mathf.Deg2Rad;
            var clamped = horizontal * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            return IsFinite(clamped) && clamped.sqrMagnitude > Epsilon ? clamped.normalized : Vector3.zero;
        }

        /// <summary>Clamps to a symmetric pitch range.</summary>
        public static Vector3 ClampPitch(Vector3 direction, float maxPitchDegrees)
        {
            var safeMax = IsFinite(maxPitchDegrees) ? Mathf.Abs(maxPitchDegrees) : DefaultMaxPitchDegrees;
            return ClampPitch(direction, -safeMax, safeMax);
        }

        /// <summary>Solves direct ball/enemy aim with no intercept prediction.</summary>
        public static BotAimSolution SolveDirectAim(
            Vector3 origin,
            Vector3 targetPosition,
            float maxNoiseDegrees,
            int slotId,
            int ordinal)
        {
            return BuildAimSolution(origin, targetPosition, maxNoiseDegrees, slotId, ordinal, false, 0f);
        }

        public static BotAimSolution SolveDirectAim(
            Vector3 origin,
            Vector3 targetPosition,
            BotDifficultyParameters parameters,
            int slotId,
            int ordinal)
        {
            return SolveDirectAim(origin, targetPosition, parameters.AimNoiseDegrees, slotId, ordinal);
        }

        public static BotAimSolution SolveDirectAim(
            Vector3 origin,
            Vector3 targetPosition,
            BotDifficulty difficulty,
            int slotId,
            int ordinal)
        {
            return SolveDirectAim(origin, targetPosition, BotDifficultyRules.GetParameters(difficulty), slotId, ordinal);
        }

        /// <summary>Solves direct aim when the target kind is known.</summary>
        public static BotAimSolution SolveDirectAim(
            Vector3 origin,
            Vector3 targetPosition,
            BotCombatTarget target,
            float maxNoiseDegrees,
            int slotId,
            int ordinal)
        {
            return IsBallOrEnemy(target)
                ? SolveDirectAim(origin, targetPosition, maxNoiseDegrees, slotId, ordinal)
                : BotAimSolution.Invalid;
        }

        /// <summary>Allows only dash-kick and shotgun direct targets.</summary>
        public static BotAimSolution SolveDirectAim(
            Vector3 origin,
            Vector3 targetPosition,
            BotCombatAction action,
            BotCombatTarget target,
            float maxNoiseDegrees,
            int slotId,
            int ordinal)
        {
            return IsDirectAction(action) && IsBallOrEnemy(target)
                ? SolveDirectAim(origin, targetPosition, maxNoiseDegrees, slotId, ordinal)
                : BotAimSolution.Invalid;
        }

        public static BotAimSolution SolveDirectAim(
            Vector3 origin,
            Vector3 targetPosition,
            BotCombatTarget target,
            BotCombatAction action,
            float maxNoiseDegrees,
            int slotId,
            int ordinal)
        {
            return SolveDirectAim(origin, targetPosition, action, target, maxNoiseDegrees, slotId, ordinal);
        }

        public static BotAimSolution SolveDirectAim(
            Vector3 origin,
            Vector3 targetPosition,
            BotCombatTarget target,
            BotDifficultyParameters parameters,
            int slotId,
            int ordinal)
        {
            return SolveDirectAim(origin, targetPosition, target, parameters.AimNoiseDegrees, slotId, ordinal);
        }

        public static BotAimSolution SolveDirectAim(
            Vector3 origin,
            Vector3 targetPosition,
            BotCombatAction action,
            BotCombatTarget target,
            BotDifficultyParameters parameters,
            int slotId,
            int ordinal)
        {
            return SolveDirectAim(origin, targetPosition, action, target, parameters.AimNoiseDegrees, slotId, ordinal);
        }

        public static BotAimSolution SolveDirectAim(
            Vector3 origin,
            Vector3 targetPosition,
            BotCombatTarget target,
            BotDifficulty difficulty,
            int slotId,
            int ordinal)
        {
            return SolveDirectAim(origin, targetPosition, target, BotDifficultyRules.GetParameters(difficulty), slotId, ordinal);
        }

        /// <summary>Solves a rocket target using constant-speed intercept and one error sample.</summary>
        public static BotAimSolution SolveInterceptAim(
            Vector3 origin,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float projectileSpeed,
            float maxPredictionErrorFraction,
            float maxNoiseDegrees,
            int slotId,
            int ordinal)
        {
            float idealTime;
            if (!TrySolveIntercept(origin, targetPosition, targetVelocity, projectileSpeed, out idealTime, out _))
            {
                return BotAimSolution.Invalid;
            }

            var perturbedTime = ApplyPredictionError(idealTime, maxPredictionErrorFraction, slotId, ordinal);
            var predictedPoint = targetPosition + targetVelocity * perturbedTime;
            if (!IsFinite(predictedPoint))
            {
                return BotAimSolution.Invalid;
            }

            return BuildAimSolution(origin, predictedPoint, maxNoiseDegrees, slotId, ordinal, true, perturbedTime);
        }

        public static BotAimSolution SolveInterceptAim(
            Vector3 origin,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float projectileSpeed,
            BotDifficultyParameters parameters,
            int slotId,
            int ordinal)
        {
            return SolveInterceptAim(
                origin,
                targetPosition,
                targetVelocity,
                projectileSpeed,
                parameters.PredictionErrorFraction,
                parameters.AimNoiseDegrees,
                slotId,
                ordinal);
        }

        public static BotAimSolution SolveInterceptAim(
            Vector3 origin,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float projectileSpeed,
            BotDifficulty difficulty,
            int slotId,
            int ordinal)
        {
            return SolveInterceptAim(
                origin,
                targetPosition,
                targetVelocity,
                projectileSpeed,
                BotDifficultyRules.GetParameters(difficulty),
                slotId,
                ordinal);
        }

        /// <summary>Allows only rocket ball/enemy targets through intercept aim.</summary>
        public static BotAimSolution SolveInterceptAim(
            Vector3 origin,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float projectileSpeed,
            BotCombatAction action,
            BotCombatTarget target,
            float maxPredictionErrorFraction,
            float maxNoiseDegrees,
            int slotId,
            int ordinal)
        {
            return action == BotCombatAction.FireRocket && IsBallOrEnemy(target)
                ? SolveInterceptAim(
                    origin,
                    targetPosition,
                    targetVelocity,
                    projectileSpeed,
                    maxPredictionErrorFraction,
                    maxNoiseDegrees,
                    slotId,
                    ordinal)
                : BotAimSolution.Invalid;
        }

        public static BotAimSolution SolveInterceptAim(
            Vector3 origin,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float projectileSpeed,
            BotCombatTarget target,
            BotCombatAction action,
            float maxPredictionErrorFraction,
            float maxNoiseDegrees,
            int slotId,
            int ordinal)
        {
            return SolveInterceptAim(
                origin,
                targetPosition,
                targetVelocity,
                projectileSpeed,
                action,
                target,
                maxPredictionErrorFraction,
                maxNoiseDegrees,
                slotId,
                ordinal);
        }

        public static BotAimSolution SolveInterceptAim(
            Vector3 origin,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float projectileSpeed,
            BotCombatTarget target,
            BotDifficultyParameters parameters,
            int slotId,
            int ordinal)
        {
            return IsBallOrEnemy(target)
                ? SolveInterceptAim(origin, targetPosition, targetVelocity, projectileSpeed, parameters, slotId, ordinal)
                : BotAimSolution.Invalid;
        }

        /// <summary>Allows only rocket ball/enemy targets through intercept aim.</summary>
        public static BotAimSolution SolveInterceptAim(
            Vector3 origin,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float projectileSpeed,
            BotCombatAction action,
            BotCombatTarget target,
            BotDifficultyParameters parameters,
            int slotId,
            int ordinal)
        {
            return action == BotCombatAction.FireRocket && IsBallOrEnemy(target)
                ? SolveInterceptAim(origin, targetPosition, targetVelocity, projectileSpeed, parameters, slotId, ordinal)
                : BotAimSolution.Invalid;
        }

        private static BotAimSolution BuildAimSolution(
            Vector3 origin,
            Vector3 predictedPoint,
            float maxNoiseDegrees,
            int slotId,
            int ordinal,
            bool usedIntercept,
            float interceptTime)
        {
            if (!IsFinite(origin) || !IsFinite(predictedPoint))
            {
                return BotAimSolution.Invalid;
            }

            var offset = predictedPoint - origin;
            if (!IsFinite(offset) || offset.sqrMagnitude <= Epsilon)
            {
                return BotAimSolution.Invalid;
            }

            var distance = offset.magnitude;
            if (!IsFinite(distance))
            {
                return BotAimSolution.Invalid;
            }

            var direction = offset.normalized;
            if (!IsFinite(direction) || direction.sqrMagnitude <= Epsilon)
            {
                return BotAimSolution.Invalid;
            }
            var noisyDirection = ApplyAimNoise(direction, maxNoiseDegrees, slotId, ordinal);
            if (!IsFinite(noisyDirection) || noisyDirection.sqrMagnitude <= Epsilon)
            {
                return BotAimSolution.Invalid;
            }

            var finalDirection = ClampPitch(noisyDirection);
            if (!IsFinite(finalDirection) || finalDirection.sqrMagnitude <= Epsilon)
            {
                return BotAimSolution.Invalid;
            }

            var aimPoint = origin + finalDirection * Mathf.Max(0.01f, distance);
            if (!IsFinite(aimPoint))
            {
                return BotAimSolution.Invalid;
            }

            return new BotAimSolution(true, usedIntercept, aimPoint, finalDirection, Mathf.Max(interceptTime, 0f));
        }

        private static bool IsDirectAction(BotCombatAction action)
        {
            return action == BotCombatAction.DashKick || action == BotCombatAction.FireShotgun;
        }

        private static bool IsBallOrEnemy(BotCombatTarget target)
        {
            return target == BotCombatTarget.Ball || target == BotCombatTarget.Enemy;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
