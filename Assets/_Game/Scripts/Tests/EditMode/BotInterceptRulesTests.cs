using NUnit.Framework;
using RocketFooxball.Runtime.Bots;
using UnityEngine;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotInterceptRulesTests
    {
        [Test]
        public void StationaryAndLateralTargetsReturnSmallestNonnegativeRoot()
        {
            float time;
            Vector3 point;
            Assert.That(BotAimRules.TrySolveIntercept(
                Vector3.zero,
                new Vector3(0f, 0f, 10f),
                Vector3.zero,
                5f,
                out time,
                out point), Is.True);
            Assert.That(time, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(point, Is.EqualTo(new Vector3(0f, 0f, 10f)));

            Assert.That(BotAimRules.TrySolveIntercept(
                Vector3.zero,
                new Vector3(0f, 0f, 10f),
                new Vector3(2f, 0f, 0f),
                5f,
                out time,
                out point), Is.True);
            Assert.That(time, Is.EqualTo(2.1828206f).Within(0.0001f));
            Assert.That(point.x, Is.EqualTo(4.365641f).Within(0.0002f));
        }

        [Test]
        public void LinearCoincidentAndNoRootCasesAreExplicit()
        {
            float time;
            Vector3 point;
            Assert.That(BotAimRules.TrySolveIntercept(
                Vector3.zero,
                Vector3.zero,
                new Vector3(2f, 0f, 0f),
                5f,
                out time,
                out point), Is.True);
            Assert.That(time, Is.EqualTo(0f));
            Assert.That(point, Is.EqualTo(Vector3.zero));

            Assert.That(BotAimRules.TrySolveIntercept(
                Vector3.zero,
                new Vector3(0f, 0f, 10f),
                new Vector3(10f, 0f, 0f),
                1f,
                out time,
                out point), Is.False);
            Assert.That(time, Is.EqualTo(0f));
            Assert.That(point, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void InterceptAimUsesPerturbedPointOnceAndRejectsInvalidMath()
        {
            var solution = BotAimRules.SolveInterceptAim(
                Vector3.zero,
                new Vector3(0f, 0f, 10f),
                new Vector3(1f, 0f, 0f),
                5f,
                BotDifficultyRules.GetParameters(BotDifficulty.Medium),
                1,
                2);
            Assert.That(solution.IsValid, Is.True);
            Assert.That(solution.UsedIntercept, Is.True);
            Assert.That(solution.Direction.sqrMagnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(Vector3.Distance(solution.AimPoint, solution.Direction * solution.AimPoint.magnitude), Is.LessThan(0.0001f));

            var invalid = BotAimRules.SolveInterceptAim(
                Vector3.zero,
                new Vector3(0f, 0f, 10f),
                Vector3.zero,
                float.NaN,
                0.15f,
                3f,
                1,
                2);
            Assert.That(invalid.IsValid, Is.False);
            Assert.That(invalid.AimPoint, Is.EqualTo(Vector3.zero));
            Assert.That(invalid.Direction, Is.EqualTo(Vector3.zero));
        }
    }
}
