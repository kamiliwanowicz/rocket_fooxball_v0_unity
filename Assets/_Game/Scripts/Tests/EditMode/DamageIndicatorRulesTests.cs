using NUnit.Framework;
using UnityEngine;
using RocketFooxball.Runtime.Hud;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class DamageIndicatorRulesTests
    {
        [Test]
        public void CardinalDirectionsResolveToClockwiseDegrees()
        {
            AssertAngle(Vector3.forward, 0f);
            AssertAngle(Vector3.right, 90f);
            AssertAngle(Vector3.back, 180f);
            AssertAngle(Vector3.left, 270f);
        }

        [Test]
        public void SourcePositionOverloadUsesVictimToSourceDirection()
        {
            Assert.That(
                DamageIndicatorRules.TryResolveAngle(
                    new Vector3(4f, 3f, 2f),
                    new Vector3(4f, 3f, 8f),
                    Vector3.right,
                    Vector3.forward,
                    out var angle),
                Is.True);
            Assert.That(angle, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void DiagonalDirectionsRemainContinuous()
        {
            AssertAngle(new Vector3(1f, 0f, 1f), 45f);
            AssertAngle(new Vector3(-1f, 0f, 1f), 315f);
            AssertAngle(new Vector3(-1f, 0f, -1f), 225f);
            AssertAngle(new Vector3(1f, 0f, -1f), 135f);
        }

        [Test]
        public void PitchedCameraAxesUseProjectedDirection()
        {
            var pitchedForward = new Vector3(0f, 0.6f, 0.8f).normalized;
            Assert.That(
                DamageIndicatorRules.TryResolveAngle(
                    Vector3.up,
                    Vector3.right,
                    pitchedForward,
                    out var angle),
                Is.True);
            Assert.That(angle, Is.EqualTo(0f).Within(0.0001f));

            Assert.That(
                DamageIndicatorRules.TryResolveAngle(
                    Vector3.right + pitchedForward,
                    Vector3.right,
                    pitchedForward,
                    out angle),
                Is.True);
            Assert.That(angle, Is.EqualTo(45f).Within(0.0001f));
        }

        [Test]
        public void RepeatWrapsNegativeClockwiseAngleToLeft()
        {
            AssertAngle(Vector3.left, 270f);
            AssertAngle(new Vector3(-1f, 0f, 1f), 315f);
        }

        [Test]
        public void ZeroOrNonfiniteProjectionIsSuppressed()
        {
            Assert.That(
                DamageIndicatorRules.TryResolveAngle(
                    Vector3.zero,
                    Vector3.right,
                    Vector3.forward,
                    out _),
                Is.False);
            Assert.That(
                DamageIndicatorRules.TryResolveAngle(
                    Vector3.up,
                    Vector3.right,
                    Vector3.forward,
                    out _),
                Is.False);
            Assert.That(
                DamageIndicatorRules.TryResolveAngle(
                    new Vector3(float.NaN, 0f, 1f),
                    Vector3.right,
                    Vector3.forward,
                    out _),
                Is.False);
            Assert.That(
                DamageIndicatorRules.TryResolveAngle(
                    Vector3.forward,
                    new Vector3(float.PositiveInfinity, 0f, 0f),
                    Vector3.forward,
                    out _),
                Is.False);
            Assert.That(
                DamageIndicatorRules.TryResolveAngle(
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
            Assert.That(DamageIndicatorRules.EvaluateOpacityAtElapsed(-0.1f), Is.EqualTo(0f));
            Assert.That(DamageIndicatorRules.EvaluateOpacityAtElapsed(float.NaN), Is.EqualTo(0f));
        }

        private static void AssertAngle(Vector3 victimToSource, float expected)
        {
            Assert.That(
                DamageIndicatorRules.TryResolveAngle(
                    victimToSource,
                    Vector3.right,
                    Vector3.forward,
                    out var angle),
                Is.True);
            Assert.That(angle, Is.EqualTo(expected).Within(0.0001f));
        }
    }
}
