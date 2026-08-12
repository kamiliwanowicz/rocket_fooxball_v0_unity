using System;

namespace RocketFooxball.Runtime.Pickups
{
    /// <summary>Pure availability and fixed-step respawn state owned by one pickup.</summary>
    public sealed class PickupRespawnState
    {
        private bool available = true;
        private float remaining;

        public bool IsAvailable => available;
        public float Remaining => remaining;

        /// <summary>Consumes an available pickup and starts its finite, nonnegative respawn delay.</summary>
        public bool TryConsume(float respawnDelay)
        {
            if (!available || !IsFinite(respawnDelay) || respawnDelay < 0f)
            {
                return false;
            }

            available = false;
            remaining = respawnDelay;
            return true;
        }

        /// <summary>Advances an unavailable pickup by a finite positive fixed-step delta.</summary>
        public bool Advance(float fixedDelta)
        {
            if (available || !IsFinite(fixedDelta) || fixedDelta <= 0f)
            {
                return false;
            }

            if (remaining > fixedDelta)
            {
                remaining -= fixedDelta;
                return false;
            }

            remaining = 0f;
            available = true;
            return true;
        }

        /// <summary>Restores availability immediately, cancelling any remaining delay.</summary>
        public void Reset()
        {
            available = true;
            remaining = 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
