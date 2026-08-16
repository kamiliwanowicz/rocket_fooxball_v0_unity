using UnityEngine;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Pickups
{
    /// <summary>Neutral recurring pickup that arms a participant and grants its standard shell pack.</summary>
    [DisallowMultipleComponent]
    public sealed class ShotgunPickup : ArenaPickup
    {
        [SerializeField, Min(1)] private int grant = 8;

        public int Grant => grant;

        protected override bool TryApplyToParticipant(ParticipantState participant)
        {
            if (participant == null || !participant.TryCollectShotgun(grant))
            {
                return false;
            }

            participant.CancelImmunity();
            return true;
        }
    }
}
