using UnityEngine;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Pickups
{
    /// <summary>Health pickup that delegates eligibility and health mutation to participant owners.</summary>
    [DisallowMultipleComponent]
    public sealed class HealthPickup : ArenaPickup
    {
        [SerializeField, Range(0f, 1f)] private float restoreFraction = 0.33f;

        public float RestoreFraction => restoreFraction;

        protected override bool TryApplyToParticipant(ParticipantState participant)
        {
            if (participant == null || !HealthPickupRules.TryCalculateRestore(participant.ReadModel, restoreFraction, out var amount))
            {
                return false;
            }

            return participant.TryRestoreHealth(amount);
        }
    }
}
