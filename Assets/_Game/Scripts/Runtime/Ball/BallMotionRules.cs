using UnityEngine;

namespace RocketFooxball.Runtime.Ball
{
    /// <summary>Pure ball motion calculations. BallMotor applies all Rigidbody writes.</summary>
    public static class BallMotionRules
    {
        private const float Epsilon = 0.000001f;

        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        public static Vector3 ApplyKick(Vector3 currentVelocity, Vector3 aimDirection, Vector3 playerVelocity, float hardCap, float speedFraction, float playerMomentumShare)
        {
            var direction = aimDirection.normalized;
            var opposing = Vector3.Dot(currentVelocity, direction);
            if (opposing < 0f)
            {
                currentVelocity -= direction * opposing * 0.65f;
            }

            var momentum = Mathf.Clamp(Vector3.Dot(playerVelocity, direction), 0f, hardCap * 0.25f) * Mathf.Clamp01(playerMomentumShare);
            var kickVelocity = hardCap * Mathf.Clamp(speedFraction, 0f, 1f);
            return ClampVelocity(currentVelocity + direction * (kickVelocity + momentum), hardCap);
        }

        public static Vector3 ApplyRollingResistance(Vector3 velocity, float rollingResistance, float restSpeed, float deltaTime)
        {
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            var nextMagnitude = Mathf.MoveTowards(horizontal.magnitude, 0f, rollingResistance * Mathf.Max(deltaTime, 0f));
            if (nextMagnitude <= restSpeed)
            {
                horizontal = Vector3.zero;
            }
            else if (horizontal.sqrMagnitude > Epsilon)
            {
                horizontal = horizontal.normalized * nextMagnitude;
            }

            return new Vector3(horizontal.x, velocity.y, horizontal.z);
        }

        public static Vector3 ClampVelocity(Vector3 velocity, float hardCap)
        {
            return Vector3.ClampMagnitude(velocity, hardCap);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
