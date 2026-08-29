using NUnit.Framework;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Weapons;
using UnityEngine;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BlastMathTests
    {
        [Test]
        public void EnlargedBlastRadiusFallsOffAtItsBoundary()
        {
            Assert.That(BlastMath.ComputeFalloff(0f, 11.7f), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(BlastMath.ComputeFalloff(11.7f, 11.7f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(BlastMath.ComputeFalloff(11.7001f, 11.7f), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void EnemyRocketMultiplierPreservesFullRequestedForce()
        {
            Assert.That(ParticipantRelationshipPolicy.RocketImpulseScale(ParticipantRelationship.Enemy, 1f), Is.EqualTo(1f));
            Assert.That(ParticipantRelationshipPolicy.RocketImpulseScale(ParticipantRelationship.Self, 1f), Is.EqualTo(1f));
            Assert.That(ParticipantRelationshipPolicy.RocketImpulseScale(ParticipantRelationship.Friendly, 1f), Is.EqualTo(0f));
        }

        [Test]
        public void ExplosionScaleUsesOneReferenceRadiusWithoutCompounding()
        {
            Assert.That(ExplosionVfx.ReferenceVisualRadius, Is.EqualTo(4.5f));
            Assert.That(ExplosionVfx.ComputeVisualScale(11.7f), Is.EqualTo(2.6f).Within(0.0001f));
            Assert.That(ExplosionVfx.ComputeVisualScale(0f), Is.EqualTo(0.01f / 4.5f).Within(0.0001f));
        }

        [Test]
        public void ExplosionRadiusCueMapsAuthoredDiameterToRequestedRadius()
        {
            Assert.That(ExplosionVfx.ReferenceVisualDiameter, Is.EqualTo(9f));
            Assert.That(ExplosionVfx.ComputeVisualRadius(9f, 11.7f), Is.EqualTo(11.7f).Within(0.0001f));
            Assert.That(ExplosionVfx.ComputeVisualRadius(9f, 0f), Is.EqualTo(0.01f).Within(0.0001f));
            Assert.That(ExplosionVfx.ComputeVisualRadius(9f, -2f), Is.EqualTo(0.01f).Within(0.0001f));
            Assert.That(ExplosionVfx.ComputeVisualRadius(0f, 11.7f), Is.EqualTo(0f));
            Assert.That(ExplosionVfx.ComputeVisualRadius(-1f, 11.7f), Is.EqualTo(0f));
            Assert.That(ExplosionVfx.ComputeVisualRadius(float.NaN, 11.7f), Is.EqualTo(0f));
            Assert.That(ExplosionVfx.ComputeVisualRadius(9f, float.PositiveInfinity), Is.EqualTo(0f));
        }

        [Test]
        public void PlanarDirectionMirrorsForwardAndBackwardIntent()
        {
            var forward = BlastMath.ResolvePlanarDirection(Vector3.forward, Vector2.up);
            var backward = BlastMath.ResolvePlanarDirection(Vector3.forward, Vector2.down);

            Assert.That(forward, Is.EqualTo(Vector3.forward));
            Assert.That(backward, Is.EqualTo(Vector3.back));
            Assert.That(forward, Is.EqualTo(-backward));
        }

        [Test]
        public void PlanarDirectionNormalizesDiagonalIntentAndFallsBackToFacing()
        {
            var diagonal = BlastMath.ResolvePlanarDirection(Vector3.forward, new Vector2(1f, 1f));
            var fallback = BlastMath.ResolvePlanarDirection(new Vector3(0f, 4f, 2f), Vector2.zero);
            var invalidFallback = BlastMath.ResolvePlanarDirection(Vector3.forward, new Vector2(float.NaN, 0f));

            Assert.That(diagonal.x, Is.EqualTo(0.70710677f).Within(0.0001f));
            Assert.That(diagonal.z, Is.EqualTo(0.70710677f).Within(0.0001f));
            Assert.That(diagonal.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(fallback, Is.EqualTo(Vector3.forward));
            Assert.That(invalidFallback, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void UnderfootRedirectPreservesLowSpeedLiftAndConvertsForwardAtSoftCap()
        {
            var lowSpeed = BlastMath.ComputePlayerImpulse(
                Vector3.zero,
                Vector3.down,
                24f,
                0.18f,
                true,
                Vector3.forward,
                10f,
                10f,
                25f,
                0.5625f,
                1f,
                1f);
            var highSpeed = BlastMath.ComputePlayerImpulse(
                Vector3.zero,
                Vector3.down,
                24f,
                0.18f,
                true,
                Vector3.forward,
                25f,
                10f,
                25f,
                0.5625f,
                1f,
                1f);
            var backward = BlastMath.ComputePlayerImpulse(
                Vector3.zero,
                Vector3.down,
                24f,
                0.18f,
                true,
                Vector3.back,
                10f,
                10f,
                25f,
                0.5625f,
                1f,
                1f);

            Assert.That(lowSpeed.z, Is.EqualTo(13.5f).Within(0.0001f));
            Assert.That(lowSpeed.y, Is.EqualTo(24f).Within(0.0001f));
            Assert.That(highSpeed.z, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(highSpeed.y, Is.EqualTo(37.5f).Within(0.0001f));
            Assert.That(backward.z, Is.EqualTo(-lowSpeed.z).Within(0.0001f));
            Assert.That(backward.y, Is.EqualTo(lowSpeed.y).Within(0.0001f));
        }
    }
}
