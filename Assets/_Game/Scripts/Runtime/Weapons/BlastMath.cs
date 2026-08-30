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

        /// <summary>Resolves actual player travel into a normalized XZ direction, with a facing fallback.</summary>
        public static Vector3 ResolvePlanarTravelDirection(Vector3 playerVelocity, Vector3 fallbackForward)
        {
            var travel = new Vector3(playerVelocity.x, 0f, playerVelocity.z);
            if (IsFinite(travel) && travel.sqrMagnitude > Epsilon)
            {
                travel.Normalize();
                return travel;
            }

            var fallback = new Vector3(fallbackForward.x, 0f, fallbackForward.z);
            if (IsFinite(fallback) && fallback.sqrMagnitude > Epsilon)
            {
                fallback.Normalize();
                return fallback;
            }

            return Vector3.forward;
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

            var normalizedTravel = new Vector3(underfootFacing.x, 0f, underfootFacing.z);
            if (!IsFinite(normalizedTravel) || normalizedTravel.sqrMagnitude <= Epsilon)
            {
                if (radialDirection.y < 0.25f)
                {
                    radialDirection = (radialDirection + Vector3.up * playerUpBias).normalized;
                }

                return radialDirection * strength;
            }

            normalizedTravel.Normalize();
            var speedT = Mathf.Clamp01((horizontalSpeed - baseSpeed) / Mathf.Max(softCap - baseSpeed, Epsilon));
            var redirectT = speedT * Mathf.Clamp01(underfootHighSpeedVerticalRedirect);
            var forwardBoost = strength * underfootForwardImpulseScale * (1f - redirectT);
            var redirectBudget = Mathf.Max(strength, 0f) * underfootForwardImpulseScale * redirectT;
            var excessSpeed = Mathf.Max(horizontalSpeed - baseSpeed, 0f);
            var brake = Mathf.Min(excessSpeed, redirectBudget);
            var planarImpulse = normalizedTravel * (forwardBoost - brake);
            var upwardImpulse = Vector3.up * strength *
                (underfootUpwardImpulseScale + underfootForwardImpulseScale * redirectT);
            return planarImpulse + upwardImpulse;
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
