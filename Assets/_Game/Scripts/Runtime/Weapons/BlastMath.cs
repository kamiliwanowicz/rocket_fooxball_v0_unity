using UnityEngine;

namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Pure blast falloff and impulse rules. This type never writes Unity component state.</summary>
    public static class BlastMath
    {
        public const float Epsilon = 0.000001f;

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        public static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        /// <summary>Resolves local movement intent into a normalized XZ direction relative to player facing.</summary>
        public static Vector3 ResolvePlanarDirection(Vector3 playerForward, Vector2 moveIntent)
        {
            var forward = new Vector3(playerForward.x, 0f, playerForward.z);
            if (!IsFinite(forward) || forward.sqrMagnitude <= Epsilon)
            {
                forward = Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            if (!IsFinite(moveIntent) || moveIntent.sqrMagnitude <= Epsilon)
            {
                return forward;
            }

            var right = Vector3.Cross(Vector3.up, forward);
            var direction = right * moveIntent.x + forward * moveIntent.y;
            if (!IsFinite(direction) || direction.sqrMagnitude <= Epsilon)
            {
                return forward;
            }

            return direction.normalized;
        }

        public static float ComputeFalloff(float surfaceDistance, float blastRadius)
        {
            if (!IsFinite(surfaceDistance) || !IsFinite(blastRadius))
            {
                return 0f;
            }

            var normalized = Mathf.Clamp01(1f - surfaceDistance / Mathf.Max(blastRadius, 0.0001f));
            return normalized * normalized * (3f - 2f * normalized);
        }

        public static Vector3 ComputePlayerImpulse(
            Vector3 playerPosition,
            Vector3 origin,
            float strength,
            float playerUpBias,
            bool isUnderfoot,
            Vector3 underfootFacing,
            float horizontalSpeed,
            float baseSpeed,
            float softCap,
            float underfootForwardImpulseScale,
            float underfootUpwardImpulseScale,
            float underfootHighSpeedVerticalRedirect)
        {
            if (!IsFinite(playerPosition) || !IsFinite(origin) || !IsFinite(strength))
            {
                return Vector3.zero;
            }

            var radialDirection = playerPosition - origin;
            if (radialDirection.sqrMagnitude <= Epsilon)
            {
                radialDirection = Vector3.up;
            }
            else
            {
                radialDirection.Normalize();
            }

            if (!isUnderfoot || !IsFinite(underfootFacing) || underfootFacing.sqrMagnitude <= Epsilon)
            {
                if (radialDirection.y < 0.25f)
                {
                    radialDirection = (radialDirection + Vector3.up * playerUpBias).normalized;
                }

                return radialDirection * strength;
            }

            var speedT = Mathf.Clamp01((horizontalSpeed - baseSpeed) / Mathf.Max(softCap - baseSpeed, Epsilon));
            var redirectT = speedT * Mathf.Clamp01(underfootHighSpeedVerticalRedirect);
            var baseForwardScale = underfootForwardImpulseScale;
            var baseUpwardScale = underfootUpwardImpulseScale;
            var forwardScale = baseForwardScale * (1f - redirectT);
            var upwardScale = baseUpwardScale + baseForwardScale * redirectT;
            var underfootImpulse = underfootFacing.normalized * forwardScale + Vector3.up * upwardScale;
            return underfootImpulse.sqrMagnitude <= Epsilon ? Vector3.up * strength : underfootImpulse * strength;
        }

        public static bool TryGetUnderfootFacing(
            Vector3 playerForward,
            Bounds controllerBounds,
            float controllerRadius,
            Vector3 origin,
            out Vector3 facing)
        {
            facing = Vector3.ProjectOnPlane(playerForward, Vector3.up);
            if (!IsFinite(facing) || facing.sqrMagnitude <= Epsilon || !IsFinite(origin))
            {
                return false;
            }

            facing.Normalize();
            var horizontalOffset = Vector3.ProjectOnPlane(origin - controllerBounds.center, Vector3.up);
            var maxHorizontalDistance = Mathf.Max(controllerRadius * 1.5f, 0.05f);
            if (horizontalOffset.sqrMagnitude > maxHorizontalDistance * maxHorizontalDistance)
            {
                return false;
            }

            var feetY = controllerBounds.min.y;
            return origin.y >= feetY - controllerRadius && origin.y <= feetY + controllerRadius;
        }

        public static bool TryGetBallDirection(Vector3 ballPosition, Vector3 closestPoint, Vector3 origin, out Vector3 direction)
        {
            direction = ballPosition - origin;
            if (direction.sqrMagnitude <= Epsilon)
            {
                direction = closestPoint - origin;
            }

            if (!IsFinite(direction) || direction.sqrMagnitude <= Epsilon)
            {
                direction = Vector3.zero;
                return false;
            }

            direction.Normalize();
            return true;
        }
    }
}
