using NUnit.Framework;
using RocketFooxball.Runtime.Bots;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotDifficultyRulesTests
    {
        [Test]
        public void DifficultyTuplesAreExactAndUnknownFallsBackToMedium()
        {
            AssertParameters(BotDifficulty.Low, 0.45f, 0.35f, 0.12f, 7f, 0.35f, 0.70f, 8f);
            AssertParameters(BotDifficulty.Medium, 0.22f, 0.20f, 0.06f, 3f, 0.15f, 0.50f, 7f);
            AssertParameters(BotDifficulty.High, 0.10f, 0.12f, 0.03f, 1.5f, 0.06f, 0.30f, 6f);
            AssertParameters((BotDifficulty)99, 0.22f, 0.20f, 0.06f, 3f, 0.15f, 0.50f, 7f);
        }

        [Test]
        public void AerialMissRollUsesItsOwnStableChannel()
        {
            var first = BotDifficultyRules.ShouldMissAerialBall(BotDifficulty.Low, 4, 8);
            var second = BotDifficultyRules.ShouldMissAerialBall(BotDifficulty.Low, 4, 8);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(BotDifficultyRules.StableHash(4, 8, BotSampleChannel.AerialMissRoll),
                Is.Not.EqualTo(BotDifficultyRules.StableHash(4, 8, BotSampleChannel.AerialMissAzimuth)));
        }

        [Test]
        public void StableHashPinsUseAllThreeInputs()
        {
            Assert.That(BotDifficultyRules.StableHash(0, 0, 0), Is.EqualTo(1253111735u));
            Assert.That(BotDifficultyRules.StableHash(1, 0, 0), Is.EqualTo(197805164u));
            Assert.That(BotDifficultyRules.StableHash(1, 2, 3), Is.EqualTo(1456420779u));
            Assert.That(BotDifficultyRules.StableHash(-1, 7, 4), Is.EqualTo(3160143677u));
        }

        [Test]
        public void DelaysUseTheirOwnChannelsAndMinimumFloor()
        {
            var parameters = BotDifficultyRules.GetParameters(BotDifficulty.Medium);
            var expectedReaction = UnityEngine.Mathf.Max(
                0.01f,
                parameters.ReactionSeconds +
                BotDifficultyRules.SignedSample(4, 2, BotSampleChannel.ReactionDelay) * parameters.ScheduleJitterSeconds);
            var expectedDecision = UnityEngine.Mathf.Max(
                0.01f,
                parameters.DecisionSeconds +
                BotDifficultyRules.SignedSample(4, 2, BotSampleChannel.DecisionDelay) * parameters.ScheduleJitterSeconds);

            Assert.That(BotDifficultyRules.GetReactionDelay(BotDifficulty.Medium, 4, 2), Is.EqualTo(expectedReaction));
            Assert.That(BotDifficultyRules.GetDecisionDelay(BotDifficulty.Medium, 4, 2), Is.EqualTo(expectedDecision));
            Assert.That(BotDifficultyRules.GetDelay(float.NaN, float.NaN, 0, 0, BotSampleChannel.ReactionDelay), Is.EqualTo(0.01f));
        }

        private static void AssertParameters(
            BotDifficulty difficulty,
            float reaction,
            float decision,
            float jitter,
            float noise,
            float prediction,
            float aerialChance,
            float aerialMagnitude)
        {
            var actual = BotDifficultyRules.GetParameters(difficulty);
            Assert.That(actual.ReactionSeconds, Is.EqualTo(reaction));
            Assert.That(actual.DecisionSeconds, Is.EqualTo(decision));
            Assert.That(actual.ScheduleJitterSeconds, Is.EqualTo(jitter));
            Assert.That(actual.AimNoiseDegrees, Is.EqualTo(noise));
            Assert.That(actual.PredictionErrorFraction, Is.EqualTo(prediction));
            Assert.That(actual.AerialMissChance, Is.EqualTo(aerialChance));
            Assert.That(actual.AerialMissMagnitude, Is.EqualTo(aerialMagnitude));
        }
    }
}
