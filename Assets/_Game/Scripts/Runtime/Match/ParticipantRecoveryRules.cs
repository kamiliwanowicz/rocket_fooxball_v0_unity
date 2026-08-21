using UnityEngine;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Match
{
    /// <summary>Pure eligibility and position rules for below-arena participant recovery.</summary>
    public static class ParticipantRecoveryRules
    {
        public static bool ShouldRecover(
            MatchRules.MatchState matchState,
            ParticipantLifecycle lifecycle,
            Vector3 position,
            float threshold)
        {
            if (matchState != MatchRules.MatchState.Playing || lifecycle != ParticipantLifecycle.Alive)
            {
                return false;
            }

            return !IsFinite(position) || (IsFinite(threshold) && position.y < threshold);
        }

        public static bool IsValidDestination(Vector3 position, float threshold)
        {
            return IsFinite(position) && IsFinite(threshold) && position.y >= threshold;
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
