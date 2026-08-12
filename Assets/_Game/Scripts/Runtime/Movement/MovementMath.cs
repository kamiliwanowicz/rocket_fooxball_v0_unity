using UnityEngine;

namespace RocketFooxball.Runtime.Movement
{
    /// <summary>Pure, fixed-step movement helpers. Inputs and physics state stay in the caller.</summary>
    public static class MovementMath
    {
        private const float Epsilon = 0.000001f;

        public static Vector3 Accelerate(Vector3 horizontal, Vector3 wishDirection, float wishSpeed, float acceleration, float deltaTime)
        {
            if (wishDirection.sqrMagnitude <= Epsilon || wishSpeed <= 0f || acceleration <= 0f || deltaTime <= 0f)
            {
                return horizontal;
            }

            var direction = wishDirection.normalized;
            var addSpeed = wishSpeed - Vector3.Dot(horizontal, direction);
            if (addSpeed <= 0f)
            {
                return horizontal;
            }

            return horizontal + direction * Mathf.Min(acceleration * deltaTime, addSpeed);
        }

        public static Vector3 SteerToward(Vector3 horizontal, Vector3 wishDirection, float turnRateRadians, float deltaTime)
        {
            var speed = horizontal.magnitude;
            if (speed <= Epsilon || wishDirection.sqrMagnitude <= Epsilon || turnRateRadians <= 0f || deltaTime <= 0f)
            {
                return horizontal;
            }

            var currentDirection = horizontal / speed;
            var targetDirection = wishDirection.normalized;
            var angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(currentDirection, targetDirection), -1f, 1f));
            var maxAngle = turnRateRadians * deltaTime;
            if (angle <= maxAngle)
            {
                return targetDirection * speed;
            }

            var axis = Vector3.Cross(currentDirection, targetDirection);
            if (axis.sqrMagnitude <= Epsilon)
            {
                axis = Vector3.up;
            }
            else
            {
                axis.Normalize();
            }

            return Quaternion.AngleAxis(maxAngle * Mathf.Rad2Deg, axis) * currentDirection * speed;
        }

        public static Vector3 ApplyFriction(Vector3 horizontal, float targetSpeed, float deceleration, float deltaTime)
        {
            var speed = horizontal.magnitude;
            var target = Mathf.Max(targetSpeed, 0f);
            if (speed <= target || speed <= Epsilon || deceleration <= 0f || deltaTime <= 0f)
            {
                return horizontal;
            }

            return horizontal.normalized * Mathf.Max(speed - deceleration * deltaTime, target);
        }

        public static Vector3 TurnScrub(Vector3 horizontal, Vector3 wishDirection, float baseSpeed, float scrubRate, float overspeedPenalty, float deltaTime)
        {
            var speed = horizontal.magnitude;
            if (speed <= 0.001f || wishDirection.sqrMagnitude <= Epsilon)
            {
                return horizontal;
            }

            var direction = wishDirection.normalized;
            var turn = (1f - Vector3.Dot(horizontal.normalized, direction)) * 0.5f;
            var safeBaseSpeed = Mathf.Max(baseSpeed, 0.0001f);
            var overspeed = Mathf.Max(speed / safeBaseSpeed, 1f);
            var penalty = 1f + (overspeed - 1f) * overspeedPenalty;
            var loss = Mathf.Max(scrubRate, 0f) * turn * penalty * Mathf.Max(deltaTime, 0f);
            return Vector3.ClampMagnitude(horizontal, Mathf.Max(speed - loss, 0f));
        }

        public static float AirAccelerationScale(float speed, float softCap, float hardCap)
        {
            if (speed <= softCap)
            {
                return 1f;
            }
            if (speed >= hardCap || hardCap <= softCap)
            {
                return 0f;
            }

            return Mathf.Clamp01((hardCap - speed) / (hardCap - softCap));
        }

        public static Vector3 ClampHorizontal(Vector3 velocity, float hardCap)
        {
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            var speed = horizontal.magnitude;
            if (speed <= Epsilon)
            {
                return velocity;
            }
            if (hardCap <= 0f)
            {
                return new Vector3(0f, velocity.y, 0f);
            }
            if (speed <= hardCap)
            {
                return velocity;
            }

            var limited = horizontal.normalized * hardCap;
            return new Vector3(limited.x, velocity.y, limited.z);
        }

        /// <summary>Combines a dash burst with existing momentum under a strict full-vector cap.</summary>
        public static Vector3 ComposeDashVelocity(Vector3 currentVelocity, Vector3 direction, float burstSpeed, float speedCap)
        {
            if (!IsFinite(currentVelocity) || !IsFinite(direction) || direction.sqrMagnitude <= Epsilon || burstSpeed <= 0f || speedCap <= 0f)
            {
                return currentVelocity;
            }

            return Vector3.ClampMagnitude(currentVelocity + direction.normalized * burstSpeed, speedCap);
        }

        /// <summary>Turns a contribution without changing its magnitude when aim and step inputs are valid.</summary>
        public static Vector3 SteerContribution(Vector3 contribution, Vector3 aimDirection, float turnRateDegrees, float deltaTime)
        {
            if (!IsFinite(contribution) || !IsFinite(aimDirection) || contribution.sqrMagnitude <= Epsilon || aimDirection.sqrMagnitude <= Epsilon || turnRateDegrees <= 0f || deltaTime <= 0f)
            {
                return contribution;
            }

            return Vector3.RotateTowards(
                contribution,
                aimDirection.normalized * contribution.magnitude,
                turnRateDegrees * Mathf.Deg2Rad * deltaTime,
                0f);
        }

        /// <summary>Removes a tracked contribution without reversing movement along that contribution.</summary>
        public static Vector3 RemoveContributionWithoutReversal(Vector3 velocity, Vector3 contribution, float retainedFraction)
        {
            if (!IsFinite(velocity) || !IsFinite(contribution) || contribution.sqrMagnitude <= Epsilon)
            {
                return velocity;
            }

            var removal = contribution * (1f - Mathf.Clamp01(retainedFraction));
            var removalMagnitude = removal.magnitude;
            if (removalMagnitude <= Epsilon)
            {
                return velocity;
            }

            var removalDirection = removal / removalMagnitude;
            var safeRemoval = Mathf.Min(removalMagnitude, Mathf.Max(Vector3.Dot(velocity, removalDirection), 0f));
            return velocity - removalDirection * safeRemoval;
        }

        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        /// <summary>Returns true when a contact normal is within a walkable slope limit.</summary>
        public static bool IsWalkableNormal(Vector3 normal, float slopeLimitDegrees)
        {
            if (normal.sqrMagnitude <= Epsilon || slopeLimitDegrees <= 0f)
            {
                return false;
            }

            var clampedSlope = Mathf.Clamp(slopeLimitDegrees, 0f, 89.9f);
            return Vector3.Dot(normal.normalized, Vector3.up) >= Mathf.Cos(clampedSlope * Mathf.Deg2Rad);
        }

        /// <summary>Projects direction onto a plane and keeps direction-only semantics.</summary>
        public static Vector3 ProjectDirectionOnPlane(Vector3 direction, Vector3 planeNormal)
        {
            if (direction.sqrMagnitude <= Epsilon || planeNormal.sqrMagnitude <= Epsilon)
            {
                return Vector3.zero;
            }

            var projected = Vector3.ProjectOnPlane(direction, planeNormal.normalized);
            return projected.sqrMagnitude <= Epsilon ? Vector3.zero : projected.normalized;
        }

        /// <summary>Projects a vector onto a plane while retaining its original magnitude.</summary>
        public static Vector3 ProjectOnPlanePreserveMagnitude(Vector3 vector, Vector3 planeNormal)
        {
            if (vector.sqrMagnitude <= Epsilon || planeNormal.sqrMagnitude <= Epsilon)
            {
                return vector;
            }

            var projected = Vector3.ProjectOnPlane(vector, planeNormal.normalized);
            if (projected.sqrMagnitude <= Epsilon)
            {
                return Vector3.zero;
            }

            return projected.normalized * vector.magnitude;
        }

        /// <summary>Alias for callers that describe ground motion as a tangent projection.</summary>
        public static Vector3 ProjectVelocityAlongGround(Vector3 velocity, Vector3 groundNormal)
        {
            return ProjectOnPlanePreserveMagnitude(velocity, groundNormal);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
