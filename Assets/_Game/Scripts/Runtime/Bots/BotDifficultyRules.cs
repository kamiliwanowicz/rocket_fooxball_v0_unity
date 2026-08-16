using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    /// <summary>Pure, deterministic bot difficulty parameters and schedule sampling.</summary>
    public static class BotDifficultyRules
    {
        public const float MinimumDelaySeconds = 0.01f;

        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        private static readonly BotDifficultyParameters LowParameters = new BotDifficultyParameters(
            0.45f,
            0.35f,
            0.12f,
            7f,
            0.35f);

        private static readonly BotDifficultyParameters MediumParameters = new BotDifficultyParameters(
            0.22f,
            0.20f,
            0.06f,
            3f,
            0.15f);

        private static readonly BotDifficultyParameters HighParameters = new BotDifficultyParameters(
            0.10f,
            0.12f,
            0.03f,
            1.5f,
            0.06f);

        /// <summary>Returns the fixed tier tuple. Unknown enum values intentionally use Medium.</summary>
        public static BotDifficultyParameters GetParameters(BotDifficulty difficulty)
        {
            switch (difficulty)
            {
                case BotDifficulty.Low:
                    return LowParameters;
                case BotDifficulty.High:
                    return HighParameters;
                case BotDifficulty.Medium:
                default:
                    return MediumParameters;
            }
        }

        /// <summary>
        /// Hashes the bot slot, schedule ordinal, and sample channel with unchecked FNV-1a.
        /// Explicit casts preserve the two's-complement bits of negative identifiers.
        /// </summary>
        public static uint StableHash(int slotId, int ordinal, BotSampleChannel channel)
        {
            unchecked
            {
                var hash = FnvOffsetBasis;
                hash = (hash ^ (uint)slotId) * FnvPrime;
                hash = (hash ^ (uint)ordinal) * FnvPrime;
                hash = (hash ^ (uint)channel) * FnvPrime;
                return hash;
            }
        }

        public static uint StableHash(int slotId, int ordinal, int channel)
        {
            return StableHash(slotId, ordinal, (BotSampleChannel)channel);
        }

        /// <summary>Returns a deterministic sample in the half-open interval [0, 1).</summary>
        public static float Sample01(int slotId, int ordinal, BotSampleChannel channel)
        {
            return Sample01(StableHash(slotId, ordinal, channel));
        }

        public static float Sample01(int slotId, int ordinal, int channel)
        {
            return Sample01(slotId, ordinal, (BotSampleChannel)channel);
        }

        public static float Sample01(uint hash)
        {
            return (hash & 0x00ffffffu) / 16777216f;
        }

        /// <summary>Returns a deterministic sample in the interval [-1, 1).</summary>
        public static float SignedSample(int slotId, int ordinal, BotSampleChannel channel)
        {
            return SignedSample(StableHash(slotId, ordinal, channel));
        }

        public static float SignedSample(int slotId, int ordinal, int channel)
        {
            return SignedSample(slotId, ordinal, (BotSampleChannel)channel);
        }

        public static float SignedSample(uint hash)
        {
            return 2f * Sample01(hash) - 1f;
        }

        /// <summary>Samples a reaction schedule without a second aim timer.</summary>
        public static float GetReactionDelay(BotDifficulty difficulty, int slotId, int ordinal)
        {
            var parameters = GetParameters(difficulty);
            return GetDelay(parameters.ReactionSeconds, parameters.ScheduleJitterSeconds, slotId, ordinal, BotSampleChannel.ReactionDelay);
        }

        public static float GetReactionDelay(int slotId, int ordinal, BotDifficulty difficulty)
        {
            return GetReactionDelay(difficulty, slotId, ordinal);
        }

        /// <summary>Samples a decision schedule without a second aim timer.</summary>
        public static float GetDecisionDelay(BotDifficulty difficulty, int slotId, int ordinal)
        {
            var parameters = GetParameters(difficulty);
            return GetDelay(parameters.DecisionSeconds, parameters.ScheduleJitterSeconds, slotId, ordinal, BotSampleChannel.DecisionDelay);
        }

        public static float GetDecisionDelay(int slotId, int ordinal, BotDifficulty difficulty)
        {
            return GetDecisionDelay(difficulty, slotId, ordinal);
        }

        public static float ReactionDelay(BotDifficulty difficulty, int slotId, int ordinal)
        {
            return GetReactionDelay(difficulty, slotId, ordinal);
        }

        public static float DecisionDelay(BotDifficulty difficulty, int slotId, int ordinal)
        {
            return GetDecisionDelay(difficulty, slotId, ordinal);
        }

        /// <summary>Applies a clamped signed deterministic jitter to a base schedule.</summary>
        public static float GetDelay(
            float baseSeconds,
            float jitterSeconds,
            int slotId,
            int ordinal,
            BotSampleChannel channel)
        {
            var safeBase = IsFinite(baseSeconds) ? baseSeconds : MinimumDelaySeconds;
            var safeJitter = IsFinite(jitterSeconds) ? Mathf.Max(jitterSeconds, 0f) : 0f;
            var delay = safeBase + SignedSample(slotId, ordinal, channel) * safeJitter;
            return IsFinite(delay) ? Mathf.Max(MinimumDelaySeconds, delay) : MinimumDelaySeconds;
        }

        public static float GetDelay(
            float baseSeconds,
            float jitterSeconds,
            int slotId,
            int ordinal,
            int channel)
        {
            return GetDelay(baseSeconds, jitterSeconds, slotId, ordinal, (BotSampleChannel)channel);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
