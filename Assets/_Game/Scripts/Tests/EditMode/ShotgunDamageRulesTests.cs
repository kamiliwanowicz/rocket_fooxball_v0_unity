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
            Assert.That(Falloff(23f), Is.EqualTo(0.475f).Within(0.0001f));
            Assert.That(Falloff(30f), Is.EqualTo(0.40f).Within(0.0001f));
            Assert.That(Falloff(30.01f), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void DefaultCloseShotAndTwoShotsMatchStartingLethalityTarget()
        {
            var perPellet = ShotgunDamageRules.CalculatePelletDamage(
                ShotgunDamageRules.DefaultPelletDamage,
                Falloff(4f));
            var closeShot = perPellet * ShotgunDamageRules.DefaultPelletCount;

            Assert.That(closeShot, Is.EqualTo(108.8f).Within(0.0001f));
            Assert.That(closeShot * 2f, Is.EqualTo(217.6f).Within(0.0001f));
        }

        [Test]
        public void DefaultDamageScalesAtMediumAndFarRanges()
        {
            Assert.That(
                ShotgunDamageRules.CalculatePelletDamage(ShotgunDamageRules.DefaultPelletDamage, Falloff(16f)) *
                ShotgunDamageRules.DefaultPelletCount,
                Is.EqualTo(59.84f).Within(0.0001f));
            Assert.That(
                ShotgunDamageRules.CalculatePelletDamage(ShotgunDamageRules.DefaultPelletDamage, Falloff(23f)) *
                ShotgunDamageRules.DefaultPelletCount,
                Is.EqualTo(51.68f).Within(0.0001f));
            Assert.That(
                ShotgunDamageRules.CalculatePelletDamage(ShotgunDamageRules.DefaultPelletDamage, Falloff(30f)) *
                ShotgunDamageRules.DefaultPelletCount,
                Is.EqualTo(43.52f).Within(0.0001f));
        }

        [Test]
        public void DefaultFalloffIsMonotonicAndClampedOutsideConfiguredRange()
        {
            var previous = Falloff(0f);
            for (var distance = 1f; distance <= ShotgunDamageRules.DefaultMaxRange; distance += 1f)
            {
                var current = Falloff(distance);
                Assert.That(current, Is.LessThanOrEqualTo(previous + 0.0001f));
                Assert.That(current, Is.InRange(0f, 1f));
                previous = current;
            }

            Assert.That(Falloff(-1f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(Falloff(ShotgunDamageRules.DefaultMaxRange + 0.01f), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void DefaultBallImpulseUsesSinglePelletStrengthAndNewCap()
        {
            Assert.That(
                ShotgunDamageRules.CalculateBallImpulse(
                    1f,
                    ShotgunDamageRules.DefaultPerPelletBallImpulse,
                    ShotgunDamageRules.DefaultBallImpulseCap),
                Is.EqualTo(3.4f).Within(0.0001f));
            Assert.That(
                ShotgunDamageRules.CalculateBallImpulse(
                    ShotgunDamageRules.DefaultPelletCount,
                    ShotgunDamageRules.DefaultPerPelletBallImpulse,
                    ShotgunDamageRules.DefaultBallImpulseCap),
                Is.EqualTo(20.4f).Within(0.0001f));
        }

        [Test]
        public void InvalidRangesAndNonFiniteValuesReturnZero()
        {
            Assert.That(ShotgunDamageRules.EvaluateFalloff(2f, 6f, 4f, 30f, 0.55f, 0.2f), Is.EqualTo(0f));
            Assert.That(ShotgunDamageRules.EvaluateFalloff(2f, 6f, 16f, 30f, 1.1f, 0.2f), Is.EqualTo(0f));
            Assert.That(ShotgunDamageRules.EvaluateFalloff(-1f, 6f, 16f, 30f, 0.55f, 0.2f), Is.EqualTo(0f));
            Assert.That(ShotgunDamageRules.EvaluateFalloff(float.NaN, 6f, 16f, 30f, 0.55f, 0.2f), Is.EqualTo(0f));
            Assert.That(
                ShotgunDamageRules.CalculatePelletDamage(ShotgunDamageRules.DefaultPelletDamage, float.PositiveInfinity),
                Is.EqualTo(0f));
            Assert.That(
                ShotgunDamageRules.CalculateBallImpulse(
                    -1f,
                    ShotgunDamageRules.DefaultPerPelletBallImpulse,
                    ShotgunDamageRules.DefaultBallImpulseCap),
                Is.EqualTo(0f));
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
