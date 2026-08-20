using NUnit.Framework;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Weapons;

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
    }
}
