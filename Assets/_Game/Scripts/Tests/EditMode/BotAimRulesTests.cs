using NUnit.Framework;
using RocketFooxball.Runtime.Bots;
using UnityEngine;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotAimRulesTests
    {
        [Test]
        public void PredictionErrorIsAppliedOnceAndClamped()
        {
            var signed = BotDifficultyRules.SignedSample(1, 4, BotSampleChannel.PredictionError);
            var expected = 2f * (1f + signed * 0.25f);
            Assert.That(BotAimRules.ApplyPredictionError(2f, 0.25f, 1, 4), Is.EqualTo(expected));
            Assert.That(BotAimRules.ApplyPredictionError(float.NaN, 0.25f, 1, 4), Is.EqualTo(0f));
            Assert.That(BotAimRules.ApplyPredictionError(-1f, 0.25f, 1, 4), Is.EqualTo(0f));
        }

        [Test]
        public void AimNoiseIsDeterministicNormalizedAndUsesZeroConeExactly()
        {
            var input = new Vector3(0.31f, -0.42f, 0.85f).normalized;
            var first = BotAimRules.ApplyAimNoise(input, 5f, 2, 7);
            var second = BotAimRules.ApplyAimNoise(input, 5f, 2, 7);
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.sqrMagnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(BotAimRules.ApplyAimNoise(input, 0f, 2, 7), Is.EqualTo(input));
            Assert.That(BotAimRules.ApplyAimNoise(Vector3.zero, 5f, 2, 7), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void AimNoiseNormalizesNonUnitDirectionWhenConeIsZero()
        {
            var result = BotAimRules.ApplyAimNoise(new Vector3(0f, 0f, 2f), 0f, 2, 7);

            Assert.That(result, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void PitchClampKeepsYawAndLimitsVerticalAngle()
        {
            var clamped = BotAimRules.ClampPitch(new Vector3(0.4f, 10f, 0.2f));
            var pitch = Mathf.Atan2(clamped.y, new Vector2(clamped.x, clamped.z).magnitude) * Mathf.Rad2Deg;
            Assert.That(pitch, Is.EqualTo(80f).Within(0.0001f));
            Assert.That(clamped.sqrMagnitude, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void DirectAimMapsBallAndEnemyTargetsAndKeepsPointCollinear()
        {
            var solution = BotAimRules.SolveDirectAim(
                Vector3.zero,
                new Vector3(0f, 0f, 10f),
                BotCombatAction.FireShotgun,
                BotCombatTarget.Ball,
                3f,
                0,
                0);
            Assert.That(solution.IsValid, Is.True);
            Assert.That(solution.UsedIntercept, Is.False);
            Assert.That(solution.Direction.sqrMagnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(Vector3.Distance(solution.AimPoint.normalized, solution.Direction), Is.LessThan(0.0001f));
            Assert.That(BotAimRules.SolveDirectAim(
                Vector3.zero,
                Vector3.forward,
                BotCombatAction.FireRocket,
                BotCombatTarget.Ball,
                3f,
                0,
                0).IsValid, Is.False);
        }
    }
}
