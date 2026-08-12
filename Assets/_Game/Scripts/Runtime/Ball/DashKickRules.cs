using UnityEngine;

namespace RocketFooxball.Runtime.Ball
{
    /// <summary>Pure dash-kick eligibility, timer, and deterministic contact ordering rules.</summary>
    public static class DashKickRules
    {
        private const float Epsilon = 0.000001f;

        public static float TickCooldown(float remaining, float deltaTime)
        {
            return Mathf.Max(remaining - Mathf.Max(deltaTime, 0f), 0f);
        }

        public static bool CanActivate(bool simulationEnabled, Vector3 aim, bool isDashing, float cooldownRemaining, bool isGrounded, bool airDashAvailable)
        {
            return simulationEnabled &&
                   IsFinite(aim) &&
                   aim.sqrMagnitude > Epsilon &&
                   !isDashing &&
                   cooldownRemaining <= 0f &&
                   (isGrounded || airDashAvailable);
        }

        public static bool IsContactActive(float dashElapsed, float startDelay)
        {
            return dashElapsed >= Mathf.Max(startDelay, 0f);
        }

        public static bool ShouldProcessContact(bool alreadyProcessed)
        {
            return !alreadyProcessed;
        }

        public static bool IsForward(Vector3 origin, Vector3 direction, Vector3 candidate)
        {
            if (!IsFinite(origin) || !IsFinite(direction) || !IsFinite(candidate) || direction.sqrMagnitude <= Epsilon)
            {
                return false;
            }

            return Vector3.Dot(candidate - origin, direction.normalized) > 0f;
        }

        public static bool IsBetterContactCandidate(float candidateDistance, int candidateInstanceId, float bestDistance, int bestInstanceId)
        {
            if (candidateDistance < bestDistance - Epsilon)
            {
                return true;
            }

            return Mathf.Abs(candidateDistance - bestDistance) <= Epsilon &&
                   (bestInstanceId == 0 || candidateInstanceId < bestInstanceId);
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
