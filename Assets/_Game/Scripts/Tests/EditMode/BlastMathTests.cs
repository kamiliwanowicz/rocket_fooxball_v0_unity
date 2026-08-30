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
        public void PlanarTravelDirectionFollowsForwardAndBackwardVelocity()
        {
            var forward = BlastMath.ResolvePlanarTravelDirection(new Vector3(0f, 4f, 10f), Vector3.back);
            var backward = BlastMath.ResolvePlanarTravelDirection(new Vector3(0f, -4f, -10f), Vector3.forward);

            Assert.That(forward, Is.EqualTo(Vector3.forward));
            Assert.That(backward, Is.EqualTo(Vector3.back));
            Assert.That(forward, Is.EqualTo(-backward));
        }

        [Test]
        public void PlanarTravelDirectionNormalizesDiagonalVelocityAndFallsBackToFacing()
        {
            var diagonal = BlastMath.ResolvePlanarTravelDirection(new Vector3(3f, 4f, 4f), Vector3.back);
            var fallback = BlastMath.ResolvePlanarTravelDirection(Vector3.zero, new Vector3(0f, 4f, 2f));
            var invalidVelocity = BlastMath.ResolvePlanarTravelDirection(
                new Vector3(float.NaN, 0f, 2f),
                new Vector3(2f, 0f, 0f));
            var invalidFallback = BlastMath.ResolvePlanarTravelDirection(Vector3.zero, new Vector3(float.NaN, 0f, 0f));

            Assert.That(diagonal.x, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(diagonal.z, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(diagonal.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(fallback, Is.EqualTo(Vector3.forward));
            Assert.That(invalidVelocity, Is.EqualTo(Vector3.right));
            Assert.That(invalidFallback, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void UnderfootRedirectProducesExpectedPostImpulseTravelAtFullStrength()
        {
            var speeds = new[] { 10f, 17.5f, 25f, 30f };
            var expectedPlanar = new[] { 23.5f, 17.5f, 11.5f, 16.5f };
            var expectedUpward = new[] { 24f, 30.75f, 37.5f, 37.5f };

            for (var i = 0; i < speeds.Length; i++)
            {
                var startVelocity = Vector3.forward * speeds[i];
                var impulse = BlastMath.ComputePlayerImpulse(
                    Vector3.zero,
                    Vector3.down,
                    24f,
                    0.18f,
                    true,
                    BlastMath.ResolvePlanarTravelDirection(startVelocity, Vector3.right),
                    speeds[i],
                    10f,
                    25f,
                    0.5625f,
                    1f,
                    1f);
                var postImpulse = startVelocity + impulse;

                Assert.That(postImpulse.z, Is.EqualTo(expectedPlanar[i]).Within(0.0001f));
                Assert.That(postImpulse.y, Is.EqualTo(expectedUpward[i]).Within(0.0001f));
            }
        }

        [Test]
        public void UnderfootRedirectMirrorsBackwardTravelAndBrakesWithoutReversal()
        {
            var startVelocity = Vector3.back * 25f;
            var impulse = BlastMath.ComputePlayerImpulse(
                Vector3.zero,
                Vector3.down,
                24f,
                0.18f,
                true,
                BlastMath.ResolvePlanarTravelDirection(startVelocity, Vector3.forward),
                25f,
                10f,
                25f,
                0.5625f,
                1f,
                1f);

            var postImpulse = startVelocity + impulse;
            Assert.That(postImpulse.z, Is.EqualTo(-11.5f).Within(0.0001f));
            Assert.That(postImpulse.y, Is.EqualTo(37.5f).Within(0.0001f));
            Assert.That(Mathf.Sign(postImpulse.z), Is.EqualTo(Mathf.Sign(startVelocity.z)));
        }

        [Test]
        public void UnderfootRedirectBrakingScalesWithAvailableImpulseStrength()
        {
            var startVelocity = Vector3.forward * 25f;
            var impulse = BlastMath.ComputePlayerImpulse(
                Vector3.zero,
                Vector3.down,
                12f,
                0.18f,
                true,
                Vector3.forward,
                25f,
                10f,
                25f,
                0.5625f,
                1f,
                1f);

            var postImpulse = startVelocity + impulse;
            Assert.That(postImpulse.z, Is.EqualTo(18.25f).Within(0.0001f));
            Assert.That(postImpulse.y, Is.EqualTo(18.75f).Within(0.0001f));
        }

        [Test]
        public void UnderfootRedirectPreservesDiagonalTravelDirection()
        {
            var startVelocity = new Vector3(10f, 0f, 10f);
            var travel = BlastMath.ResolvePlanarTravelDirection(startVelocity, Vector3.back);
            var impulse = BlastMath.ComputePlayerImpulse(
                Vector3.zero,
                Vector3.down,
                24f,
                0.18f,
                true,
                travel,
                startVelocity.magnitude,
                10f,
                25f,
                0.5625f,
                1f,
                1f);

            var planarPostImpulse = new Vector3(startVelocity.x + impulse.x, 0f, startVelocity.z + impulse.z);
            Assert.That(Vector3.Dot(planarPostImpulse, travel), Is.GreaterThan(0f));
            var normalizedPostImpulse = planarPostImpulse.normalized;
            Assert.That(normalizedPostImpulse.x, Is.EqualTo(travel.x).Within(0.0001f));
            Assert.That(normalizedPostImpulse.z, Is.EqualTo(travel.z).Within(0.0001f));
        }

        [Test]
        public void ComputePlayerImpulseRejectsInvalidPositionsAndStrength()
        {
            var invalidPosition = BlastMath.ComputePlayerImpulse(
                new Vector3(float.NaN, 0f, 0f),
                Vector3.zero,
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
            var invalidStrength = BlastMath.ComputePlayerImpulse(
                Vector3.zero,
                Vector3.zero,
                float.PositiveInfinity,
                0.18f,
                true,
                Vector3.forward,
                10f,
                10f,
                25f,
                0.5625f,
                1f,
                1f);

            Assert.That(invalidPosition, Is.EqualTo(Vector3.zero));
            Assert.That(invalidStrength, Is.EqualTo(Vector3.zero));
        }
    }
}
