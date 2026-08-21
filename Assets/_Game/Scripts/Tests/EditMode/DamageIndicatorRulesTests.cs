using NUnit.Framework;
using UnityEngine;
using RocketFooxball.Runtime.Hud;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class DamageIndicatorRulesTests
    {
        [Test]
        public void CardinalDirectionsResolveAgainstCameraAxes()
        {
            AssertSector(Vector3.forward, DamageIndicatorSector.Front);
            AssertSector(Vector3.right, DamageIndicatorSector.Right);
            AssertSector(Vector3.back, DamageIndicatorSector.Back);
            AssertSector(Vector3.left, DamageIndicatorSector.Left);
        }

        [Test]
        public void SourcePositionOverloadUsesVictimToSourceDirection()
        {
            Assert.That(
                DamageIndicatorRules.TryResolveSector(
                    new Vector3(4f, 3f, 2f),
                    new Vector3(4f, 3f, 8f),
                    Vector3.right,
                    Vector3.forward,
                    out var sector),
                Is.True);
            Assert.That(sector, Is.EqualTo(DamageIndicatorSector.Front));
        }

        [Test]
        public void ProjectionKeepsFullThreeDimensionalDirection()
        {
            var pitchedForward = new Vector3(0f, 1f, 1f).normalized;
            Assert.That(
                DamageIndicatorRules.TryResolveSector(
                    Vector3.up,
                    Vector3.right,
                    pitchedForward,
                    out var sector),
                Is.True);
            Assert.That(sector, Is.EqualTo(DamageIndicatorSector.Front));
        }

        [Test]
        public void ExactFortyFiveDegreeBoundariesChooseClockwiseSector()
        {
            AssertSector(new Vector3(1f, 0f, 1f), DamageIndicatorSector.Right);
            AssertSector(new Vector3(1f, 0f, -1f), DamageIndicatorSector.Back);
            AssertSector(new Vector3(-1f, 0f, -1f), DamageIndicatorSector.Left);
            AssertSector(new Vector3(-1f, 0f, 1f), DamageIndicatorSector.Front);
        }

        [Test]
        public void ZeroOrNonfiniteProjectionIsSuppressed()
        {
            Assert.That(
                DamageIndicatorRules.TryResolveSector(
                    Vector3.zero,
                    Vector3.right,
                    Vector3.forward,
                    out _),
                Is.False);
            Assert.That(
                DamageIndicatorRules.TryResolveSector(
                    new Vector3(float.NaN, 0f, 1f),
                    Vector3.right,
                    Vector3.forward,
                    out _),
                Is.False);
            Assert.That(
                DamageIndicatorRules.TryResolveSector(
                    Vector3.forward,
                    new Vector3(float.PositiveInfinity, 0f, 0f),
                    Vector3.forward,
                    out _),
                Is.False);
            Assert.That(
                DamageIndicatorRules.TryResolveSector(
                    Vector3.forward,
                    Vector3.right,
                    Vector3.zero,
                    out _),
                Is.False);
        }

        [Test]
        public void RemainingLifetimeIsFullyOpaqueThenFadesLinearly()
        {
            Assert.That(DamageIndicatorRules.EvaluateOpacity(0.75f), Is.EqualTo(1f));
            Assert.That(DamageIndicatorRules.EvaluateOpacity(0.5f), Is.EqualTo(1f));
            Assert.That(DamageIndicatorRules.EvaluateOpacity(0.375f), Is.EqualTo(1f));
            Assert.That(DamageIndicatorRules.EvaluateOpacity(0.25f), Is.EqualTo(1f));
            Assert.That(DamageIndicatorRules.EvaluateOpacity(0.125f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(DamageIndicatorRules.EvaluateOpacity(0f), Is.EqualTo(0f));
        }

        [Test]
        public void ElapsedLifetimeExpiresAtThreeQuartersSecond()
        {
            Assert.That(DamageIndicatorRules.EvaluateOpacityAtElapsed(0f), Is.EqualTo(1f));
            Assert.That(DamageIndicatorRules.EvaluateOpacityAtElapsed(0.5f), Is.EqualTo(1f));
            Assert.That(DamageIndicatorRules.EvaluateOpacityAtElapsed(0.625f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(DamageIndicatorRules.EvaluateOpacityAtElapsed(0.75f), Is.EqualTo(0f));
            Assert.That(DamageIndicatorRules.EvaluateOpacityAtElapsed(float.NaN), Is.EqualTo(0f));
        }

        private static void AssertSector(Vector3 victimToSource, DamageIndicatorSector expected)
        {
            Assert.That(
                DamageIndicatorRules.TryResolveSector(
                    victimToSource,
                    Vector3.right,
                    Vector3.forward,
                    out var sector),
                Is.True);
            Assert.That(sector, Is.EqualTo(expected));
        }
    }
}
