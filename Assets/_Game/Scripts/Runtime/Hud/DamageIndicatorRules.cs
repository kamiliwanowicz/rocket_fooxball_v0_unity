using UnityEngine;

namespace RocketFooxball.Runtime.Hud
{
    /// <summary>Cardinal directions used by the incoming-damage HUD indicator.</summary>
    public enum DamageIndicatorSector
    {
        Front,
        Right,
        Back,
        Left
    }

    /// <summary>Pure direction and timing rules for the incoming-damage indicator.</summary>
    public static class DamageIndicatorRules
    {
        public const float VisibleDuration = 0.75f;
        public const float FullOpacityDuration = 0.5f;
        public const float FadeDuration = 0.25f;

        /// <summary>
        /// Resolves a victim-to-source direction against the camera's right and forward axes.
        /// The camera axes are normalized so callers can provide transform-like directions.
        /// </summary>
        public static bool TryResolveSector(
            Vector3 victimToSource,
            Vector3 cameraRight,
            Vector3 cameraForward,
            out DamageIndicatorSector sector)
        {
            sector = DamageIndicatorSector.Front;
            if (!IsFinite(victimToSource) || !IsFinite(cameraRight) || !IsFinite(cameraForward))
            {
                return false;
            }

            if (cameraRight.sqrMagnitude <= Mathf.Epsilon || cameraForward.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            cameraRight.Normalize();
            cameraForward.Normalize();
            var rightProjection = Vector3.Dot(victimToSource, cameraRight);
            var forwardProjection = Vector3.Dot(victimToSource, cameraForward);
            if (!IsFinite(rightProjection) || !IsFinite(forwardProjection) ||
                (Mathf.Abs(rightProjection) <= Mathf.Epsilon && Mathf.Abs(forwardProjection) <= Mathf.Epsilon))
            {
                return false;
            }

            var angle = Mathf.Atan2(rightProjection, forwardProjection) * Mathf.Rad2Deg;
            if (!IsFinite(angle))
            {
                return false;
            }

            if (angle < 0f)
            {
                angle += 360f;
            }

            // Adding half a sector makes exact boundaries choose the clockwise sector.
            var sectorIndex = Mathf.FloorToInt((angle + 45f) / 90f) % 4;
            sector = (DamageIndicatorSector)sectorIndex;
            return true;
        }

        /// <summary>Resolves positions directly by constructing victim-to-source direction.</summary>
        public static bool TryResolveSector(
            Vector3 victimPosition,
            Vector3 sourcePosition,
            Vector3 cameraRight,
            Vector3 cameraForward,
            out DamageIndicatorSector sector)
        {
            return TryResolveSector(sourcePosition - victimPosition, cameraRight, cameraForward, out sector);
        }

        /// <summary>Alias for callers that prefer a shorter pure resolver name.</summary>
        public static bool TryResolve(
            Vector3 victimToSource,
            Vector3 cameraRight,
            Vector3 cameraForward,
            out DamageIndicatorSector sector)
        {
            return TryResolveSector(victimToSource, cameraRight, cameraForward, out sector);
        }

        /// <summary>Returns indicator opacity for a remaining unscaled lifetime.</summary>
        public static float EvaluateOpacity(float remaining)
        {
            if (!IsFinite(remaining) || remaining <= 0f)
            {
                return 0f;
            }

            if (remaining >= FadeDuration)
            {
                return 1f;
            }

            return Mathf.Clamp01(remaining / FadeDuration);
        }

        /// <summary>Returns indicator opacity for elapsed unscaled time since the hit.</summary>
        public static float EvaluateOpacityAtElapsed(float elapsed)
        {
            if (!IsFinite(elapsed) || elapsed < 0f)
            {
                return 0f;
            }

            if (elapsed <= FullOpacityDuration)
            {
                return 1f;
            }

            if (elapsed >= VisibleDuration)
            {
                return 0f;
            }

            return Mathf.Clamp01(1f - (elapsed - FullOpacityDuration) / FadeDuration);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
