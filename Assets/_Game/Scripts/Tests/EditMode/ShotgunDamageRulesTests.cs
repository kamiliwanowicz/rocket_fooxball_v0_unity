using NUnit.Framework;
using RocketFooxball.Runtime.Weapons;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class ShotgunDamageRulesTests
    {
        [Test]
        public void DefaultFalloffHitsConfiguredKnotsAndFiniteRange()
        {
            Assert.That(Falloff(0f), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(Falloff(6f), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(Falloff(11f), Is.EqualTo(0.775f).Within(0.0001f));
            Assert.That(Falloff(16f), Is.EqualTo(0.55f).Within(0.0001f));
            Assert.That(Falloff(23f), Is.EqualTo(0.375f).Within(0.0001f));
            Assert.That(Falloff(30f), Is.EqualTo(0.20f).Within(0.0001f));
            Assert.That(Falloff(30.01f), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void DefaultCloseShotAndTwoShotsMatchStartingLethalityTarget()
        {
            var perPellet = ShotgunDamageRules.CalculatePelletDamage(8f, Falloff(4f));
            var closeShot = perPellet * ShotgunDamageRules.DefaultPelletCount;

            Assert.That(closeShot, Is.EqualTo(64f).Within(0.0001f));
            Assert.That(closeShot * 2f, Is.EqualTo(128f).Within(0.0001f));
        }

        [Test]
        public void BallImpulseAggregatesPelletsAndCapsAtTwelve()
        {
            Assert.That(ShotgunDamageRules.CalculateBallImpulse(3f, 2f, 12f), Is.EqualTo(6f).Within(0.0001f));
            Assert.That(ShotgunDamageRules.CalculateBallImpulse(8f, 2f, 12f), Is.EqualTo(12f).Within(0.0001f));
        }

        [Test]
        public void InvalidRangesAndNonFiniteValuesReturnZero()
        {
            Assert.That(ShotgunDamageRules.EvaluateFalloff(2f, 6f, 4f, 30f, 0.55f, 0.2f), Is.EqualTo(0f));
            Assert.That(ShotgunDamageRules.EvaluateFalloff(2f, 6f, 16f, 30f, 1.1f, 0.2f), Is.EqualTo(0f));
            Assert.That(ShotgunDamageRules.EvaluateFalloff(-1f, 6f, 16f, 30f, 0.55f, 0.2f), Is.EqualTo(0f));
            Assert.That(ShotgunDamageRules.EvaluateFalloff(float.NaN, 6f, 16f, 30f, 0.55f, 0.2f), Is.EqualTo(0f));
            Assert.That(ShotgunDamageRules.CalculatePelletDamage(8f, float.PositiveInfinity), Is.EqualTo(0f));
            Assert.That(ShotgunDamageRules.CalculateBallImpulse(-1f, 2f, 12f), Is.EqualTo(0f));
        }

        [Test]
        public void PelletCountClampStaysWithinFixedPattern()
        {
            Assert.That(ShotgunDamageRules.ClampPelletCount(-4), Is.EqualTo(1));
            Assert.That(ShotgunDamageRules.ClampPelletCount(0), Is.EqualTo(1));
            Assert.That(ShotgunDamageRules.ClampPelletCount(8), Is.EqualTo(8));
            Assert.That(ShotgunDamageRules.ClampPelletCount(50), Is.EqualTo(ShotgunSpreadPattern.Count));
        }

        private static float Falloff(float distance)
        {
            return ShotgunDamageRules.EvaluateFalloff(
                distance,
                ShotgunDamageRules.DefaultFullDamageRange,
                ShotgunDamageRules.DefaultMediumRange,
                ShotgunDamageRules.DefaultMaxRange,
                ShotgunDamageRules.DefaultMediumMultiplier,
                ShotgunDamageRules.DefaultFarMultiplier);
        }

    }
}
