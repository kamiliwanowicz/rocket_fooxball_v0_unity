using UnityEngine;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Pickups
{
    /// <summary>Recurring shell pack that delegates capped ammo accounting to the participant owner.</summary>
    [DisallowMultipleComponent]
    public sealed class AmmoPickup : ArenaPickup
    {
        [SerializeField, Min(1)] private int grant = 8;

        public int Grant => grant;

        protected override bool TryApplyToParticipant(ParticipantState participant)
        {
            if (participant == null || !participant.TryCollectShotgunAmmo(grant))
            {
                return false;
            }

            participant.CancelImmunity();
            return true;
        }
    }
}
