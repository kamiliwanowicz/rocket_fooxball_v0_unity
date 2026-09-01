using UnityEngine;

namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Pure shotgun damage falloff and ball impulse calculations.</summary>
    public static class ShotgunDamageRules
    {
        public const int DefaultPelletCount = 8;
        public const float DefaultPelletDamage = 13.6f;
        public const float DefaultFullDamageRange = 6f;
        public const float DefaultMediumRange = 16f;
        public const float DefaultMaxRange = 30f;
        public const float DefaultMediumMultiplier = 0.55f;
        public const float DefaultFarMultiplier = 0.40f;
        public const float DefaultPerPelletBallImpulse = 3.4f;
        public const float DefaultBallImpulseCap = 20.4f;
        public const float DefaultSpreadAngleDegrees = 7f;
        public const float DefaultPumpDelay = 0.85f;

        /// <summary>Clamps a requested pellet count to the fixed spread's usable range.</summary>
        public static int ClampPelletCount(int requested)
        {
            if (requested < 1)
            {
                return 1;
            }

            return requested > ShotgunSpreadPattern.Count ? ShotgunSpreadPattern.Count : requested;
        }

        /// <summary>
        /// Evaluates the piecewise linear shotgun falloff. The max-range knot retains the
        /// far multiplier; only distances beyond that knot are out of range.
        /// </summary>
        public static float EvaluateFalloff(
            float distance,
            float fullDamageRange,
            float mediumRange,
            float maxRange,
            float mediumMultiplier,
            float farMultiplier)
        {
            if (!IsValidRange(distance, fullDamageRange, mediumRange, maxRange, mediumMultiplier, farMultiplier))
            {
                return 0f;
            }

            if (distance <= fullDamageRange)
            {
                return 1f;
            }

            if (distance <= mediumRange)
            {
                var t = (distance - fullDamageRange) / (mediumRange - fullDamageRange);
                return Mathf.Lerp(1f, mediumMultiplier, t);
            }

            if (distance <= maxRange)
            {
                var t = (distance - mediumRange) / (maxRange - mediumRange);
                return Mathf.Lerp(mediumMultiplier, farMultiplier, t);
            }

            return 0f;
        }

        /// <summary>Applies a normalized falloff to one pellet's nonnegative damage.</summary>
        public static float CalculatePelletDamage(float perPelletDamage, float falloff)
        {
            if (!IsFinite(perPelletDamage) || perPelletDamage < 0f || !IsFinite(falloff))
            {
                return 0f;
            }

            return perPelletDamage * Mathf.Clamp01(falloff);
        }

        /// <summary>Calculates capped scalar ball impulse from the contacted pellets.</summary>
        public static float CalculateBallImpulse(float accumulatedFalloff, float perPelletImpulse, float cap)
        {
            if (!IsFinite(accumulatedFalloff) || accumulatedFalloff < 0f ||
                !IsFinite(perPelletImpulse) || perPelletImpulse < 0f ||
                !IsFinite(cap) || cap < 0f)
            {
                return 0f;
            }

            var requested = accumulatedFalloff * perPelletImpulse;
            if (float.IsNaN(requested))
            {
                return 0f;
            }

            return Mathf.Min(requested, cap);
        }

        private static bool IsValidRange(
            float distance,
            float fullDamageRange,
            float mediumRange,
            float maxRange,
            float mediumMultiplier,
            float farMultiplier)
        {
            return IsFinite(distance) && distance >= 0f &&
                   IsFinite(fullDamageRange) && fullDamageRange >= 0f &&
                   IsFinite(mediumRange) && mediumRange > fullDamageRange &&
                   IsFinite(maxRange) && maxRange > mediumRange &&
                   IsFinite(mediumMultiplier) && mediumMultiplier >= 0f && mediumMultiplier <= 1f &&
                   IsFinite(farMultiplier) && farMultiplier >= 0f && farMultiplier <= mediumMultiplier;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
