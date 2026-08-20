using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    /// <summary>Pure pickup-memory state kept separately for each bot observer.</summary>
    public readonly struct BotPickupMemoryState
    {
        public BotPickupMemoryState(
            bool hasPriorVisibleSample,
            bool priorVisibleAvailable,
            bool hasWitnessedRespawn,
            float remaining)
        {
            HasPriorVisibleSample = hasPriorVisibleSample;
            PriorVisibleAvailable = priorVisibleAvailable;
            HasWitnessedRespawn = hasWitnessedRespawn;
            Remaining = remaining;
        }

        public bool HasPriorVisibleSample { get; }
        public bool PriorVisibleAvailable { get; }
        public bool HasWitnessedRespawn { get; }
        public float Remaining { get; }
    }

    /// <summary>Deterministic transitions and publication for bounded pickup knowledge.</summary>
    public static class BotPerceptionMemory
    {
        /// <summary>
        /// Updates one pickup's private memory. A countdown begins only when an observer
        /// sees the same pickup change from available to unavailable.
        /// </summary>
        public static BotPickupMemoryState UpdatePickup(
            BotPickupMemoryState prior,
            bool isVisible,
            bool isAvailable,
            float respawnDelay,
            float gameplayDelta)
        {
            var validDelay = IsFinite(respawnDelay) && respawnDelay >= 0f;
            var delta = IsFinite(gameplayDelta) && gameplayDelta > 0f ? gameplayDelta : 0f;

            if (isVisible)
            {
                if (isAvailable)
                {
                    return new BotPickupMemoryState(true, true, false, 0f);
                }

                if (!validDelay)
                {
                    return new BotPickupMemoryState(true, false, false, 0f);
                }

                if (prior.HasWitnessedRespawn)
                {
                    var remaining = ClampRemaining(prior.Remaining - delta);
                    return new BotPickupMemoryState(true, false, remaining > 0f, remaining);
                }

                if (prior.HasPriorVisibleSample && prior.PriorVisibleAvailable)
                {
                    // The transition is observed now; do not spend a fixed-step slice twice.
                    return new BotPickupMemoryState(true, false, true, respawnDelay);
                }

                // A first-seen unavailable pickup gives no respawn knowledge.
                return new BotPickupMemoryState(true, false, false, 0f);
            }

            if (!validDelay)
            {
                return new BotPickupMemoryState(
                    false,
                    false,
                    false,
                    0f);
            }

            if (!prior.HasWitnessedRespawn)
            {
                // An unseen sample breaks consecutive-visible transition eligibility.
                return new BotPickupMemoryState(false, false, false, 0f);
            }

            // At zero seconds the bot may believe the pickup has returned, but it must
            // retain the fact that this was a witnessed timer until it sees the pickup again.
            return new BotPickupMemoryState(
                prior.HasPriorVisibleSample,
                prior.PriorVisibleAvailable,
                true,
                ClampRemaining(prior.Remaining - delta));
        }

        /// <summary>Publishes only knowledge available to one observer.</summary>
        public static BotPickupObservation PublishPickup(
            int stableId,
            BotPickupKind kind,
            Vector3 position,
            BotPickupMemoryState state,
            bool isVisible,
            bool isAvailable,
            float ageSeconds)
        {
            var remaining = state.HasWitnessedRespawn ? ClampRemaining(state.Remaining) : 0f;
            var age = IsFinite(ageSeconds) && ageSeconds >= 0f ? ageSeconds : 0f;

            if (isVisible)
            {
                return new BotPickupObservation(
                    stableId,
                    kind,
                    position,
                    true,
                    true,
                    isAvailable,
                    isAvailable ? false : state.HasWitnessedRespawn,
                    isAvailable ? 0f : remaining,
                    age);
            }

            if (!state.HasWitnessedRespawn)
            {
                return default(BotPickupObservation);
            }

            var believedAvailable = remaining <= 0f;
            return new BotPickupObservation(
                stableId,
                kind,
                position,
                true,
                false,
                believedAvailable,
                true,
                remaining,
                age);
        }

        private static float ClampRemaining(float value)
        {
            return IsFinite(value) && value > 0f ? value : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>Pure cadence gate used by the team role coordinator.</summary>
    public static class BotCoordinatorScheduleRules
    {
        public static bool ShouldEvaluate(
            float elapsed,
            float interval,
            bool first,
            bool aliveSetChanged)
        {
            if (first || aliveSetChanged)
            {
                return true;
            }

            if (!IsFinite(elapsed) || elapsed < 0f)
            {
                return false;
            }

            if (!IsFinite(interval) || interval <= 0f)
            {
                return true;
            }

            return elapsed >= interval;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
