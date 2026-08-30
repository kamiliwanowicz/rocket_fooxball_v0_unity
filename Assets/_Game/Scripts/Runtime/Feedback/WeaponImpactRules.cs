using UnityEngine;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Pure calculations for orienting weapon impact marks.</summary>
    public static class WeaponImpactRules
    {
        private const float Epsilon = 0.000001f;

        public static bool TryResolveMarkRotation(Vector3 normal, out Quaternion rotation)
        {
            if (!IsFinite(normal) || normal.sqrMagnitude <= Epsilon)
            {
                rotation = Quaternion.identity;
                return false;
            }

            rotation = Quaternion.FromToRotation(Vector3.forward, normal.normalized);
            return true;
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
