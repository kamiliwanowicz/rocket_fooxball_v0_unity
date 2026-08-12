using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Pickups
{
    /// <summary>Pure eligibility and capped restore math for health pickups.</summary>
    public static class HealthPickupRules
    {
        public static bool TryCalculateRestore(ParticipantReadModel participant, float fraction, out float amount)
        {
            amount = 0f;
            if (!participant.IsAlive || !IsFinite(participant.MaxHealth) || participant.MaxHealth <= 0f ||
                !IsFinite(participant.Health) || participant.Health >= participant.MaxHealth ||
                !IsFinite(fraction) || fraction <= 0f)
            {
                return false;
            }

            var boundedFraction = fraction > 1f ? 1f : fraction;
            var missing = participant.MaxHealth - participant.Health;
            var requested = participant.MaxHealth * boundedFraction;
            amount = requested < missing ? requested : missing;
            if (!IsFinite(amount) || amount <= 0f)
            {
                amount = 0f;
                return false;
            }

            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
