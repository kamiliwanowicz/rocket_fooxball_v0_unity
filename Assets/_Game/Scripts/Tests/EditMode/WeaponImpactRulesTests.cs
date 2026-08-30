using NUnit.Framework;
using RocketFooxball.Runtime.Feedback;
using UnityEngine;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class WeaponImpactRulesTests
    {
        [Test]
        public void CardinalVerticalAndRampNormalsOrientMarkForwardAxis()
        {
            AssertNormal(Vector3.forward);
            AssertNormal(Vector3.back);
            AssertNormal(Vector3.left);
            AssertNormal(Vector3.right);
            AssertNormal(Vector3.up);
            AssertNormal(Vector3.down);
            AssertNormal(new Vector3(0.35f, 0.8f, 0.48f));
        }

        [Test]
        public void ZeroOrNonfiniteNormalsAreRejectedWithIdentityRotation()
        {
            AssertInvalid(Vector3.zero);
            AssertInvalid(new Vector3(0.001f, 0f, 0f));
            AssertInvalid(new Vector3(float.NaN, 0f, 1f));
            AssertInvalid(new Vector3(0f, float.PositiveInfinity, 0f));
            AssertInvalid(new Vector3(0f, 0f, float.NegativeInfinity));
        }

        private static void AssertNormal(Vector3 normal)
        {
            Assert.That(
                WeaponImpactRules.TryResolveMarkRotation(normal, out var rotation),
                Is.True);

            var expected = normal.normalized;
            var actual = rotation * Vector3.forward;
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }

        private static void AssertInvalid(Vector3 normal)
        {
            Assert.That(
                WeaponImpactRules.TryResolveMarkRotation(normal, out var rotation),
                Is.False);
            Assert.That(rotation, Is.EqualTo(Quaternion.identity));
        }
    }
}
