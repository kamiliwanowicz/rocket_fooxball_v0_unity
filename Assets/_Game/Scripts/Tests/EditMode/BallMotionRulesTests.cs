using System.Collections.Generic;
using NUnit.Framework;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Physics;
using UnityEngine;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BallMotionRulesTests
    {
        private static readonly Vector3 HeadOnNormal = Vector3.left;

        [Test]
        public void StationaryHeadOnContactRetainsPlayerMomentumAndTransfersClosingSpeed()
        {
            var incoming = Vector3.right * 10f;

            var resolved = BallMotionRules.ResolvePlayerCollision(incoming, HeadOnNormal, Vector3.zero, true);
            var assist = BallMotionRules.ComputeContactAssist(incoming, Vector3.zero, HeadOnNormal);

            Assert.That(resolved, Is.EqualTo(Vector3.right * 3.5f).Using(Vector3Comparer));
            Assert.That(assist, Is.EqualTo(Vector3.right * 6.5f).Using(Vector3Comparer));
        }

        [Test]
        public void ContactConstantsExposeRequestedRetentionTransferAndCaps()
        {
            Assert.That(GamePhysicsSettings.PlayerCollisionRetentionFraction, Is.EqualTo(0.35f));
            Assert.That(GamePhysicsSettings.PlayerCollisionTransferFraction, Is.EqualTo(0.65f));
            Assert.That(GamePhysicsSettings.BallContactAssistPerContactCap, Is.EqualTo(12f));
            Assert.That(GamePhysicsSettings.BallContactAssistAggregateCap, Is.EqualTo(12f));
        }

        [Test]
        public void GlancingContactOnlyRemovesInwardComponent()
        {
            var resolved = BallMotionRules.ResolvePlayerCollision(new Vector3(10f, 4f, 0f), HeadOnNormal, Vector3.zero, true);
            var assist = BallMotionRules.ComputeContactAssist(new Vector3(10f, 4f, 0f), Vector3.zero, HeadOnNormal);

            Assert.That(resolved, Is.EqualTo(new Vector3(3.5f, 4f, 0f)).Using(Vector3Comparer));
            Assert.That(assist, Is.EqualTo(Vector3.right * 6.5f).Using(Vector3Comparer));
        }

        [Test]
        public void FollowingBallHasNoClosingTransfer()
        {
            var incoming = Vector3.right * 10f;
            var ballVelocity = Vector3.right * 12f;

            Assert.That(BallMotionRules.ResolvePlayerCollision(incoming, HeadOnNormal, ballVelocity, true), Is.EqualTo(incoming).Using(Vector3Comparer));
            Assert.That(BallMotionRules.ComputeContactAssist(incoming, ballVelocity, HeadOnNormal), Is.EqualTo(Vector3.zero).Using(Vector3Comparer));
        }

        [Test]
        public void OpposingBallMotionCannotRemoveMoreThanPlayerInwardSpeed()
        {
            var incoming = Vector3.right * 10f;
            var ballVelocity = Vector3.left * 8f;

            Assert.That(BallMotionRules.ResolvePlayerCollision(incoming, HeadOnNormal, ballVelocity, true), Is.EqualTo(Vector3.right * 3.5f).Using(Vector3Comparer));
            Assert.That(BallMotionRules.ComputeContactAssist(incoming, ballVelocity, HeadOnNormal), Is.EqualTo(Vector3.right * 11.7f).Using(Vector3Comparer));
        }

        [Test]
        public void SeparatingPlayerMotionHasNoRemovalOrAssist()
        {
            var incoming = Vector3.left * 10f;

            Assert.That(BallMotionRules.ResolvePlayerCollision(incoming, HeadOnNormal, Vector3.zero, true), Is.EqualTo(incoming).Using(Vector3Comparer));
            Assert.That(BallMotionRules.ComputeContactAssist(incoming, Vector3.zero, HeadOnNormal), Is.EqualTo(Vector3.zero).Using(Vector3Comparer));
        }

        [Test]
        public void VerticalContactUsesTheSameInwardRule()
        {
            var incoming = Vector3.up * 10f;
            var normal = Vector3.down;

            Assert.That(BallMotionRules.ResolvePlayerCollision(incoming, normal, Vector3.zero, true), Is.EqualTo(Vector3.up * 3.5f).Using(Vector3Comparer));
            Assert.That(BallMotionRules.ComputeContactAssist(incoming, Vector3.zero, normal), Is.EqualTo(Vector3.up * 6.5f).Using(Vector3Comparer));
        }

        [Test]
        public void StaticContactStillClipsAllInwardMotion()
        {
            var incoming = Vector3.right * 10f;

            Assert.That(BallMotionRules.ResolvePlayerCollision(incoming, HeadOnNormal, Vector3.zero, false), Is.EqualTo(Vector3.zero).Using(Vector3Comparer));
        }

        [Test]
        public void InvalidInputsNeverProduceInvalidMotion()
        {
            var incoming = new Vector3(float.NaN, 0f, 0f);
            var invalidNormal = new Vector3(float.PositiveInfinity, 0f, 0f);

            Assert.That(BallMotionRules.ResolvePlayerCollision(incoming, HeadOnNormal, Vector3.zero, true), Is.EqualTo(Vector3.zero).Using(Vector3Comparer));
            Assert.That(BallMotionRules.ResolvePlayerCollision(Vector3.right, invalidNormal, Vector3.zero, true), Is.EqualTo(Vector3.right).Using(Vector3Comparer));
            Assert.That(BallMotionRules.ComputeContactAssist(Vector3.right, incoming, HeadOnNormal), Is.EqualTo(Vector3.zero).Using(Vector3Comparer));
            Assert.That(BallMotionRules.AccumulateContactAssist(Vector3.right, incoming), Is.EqualTo(Vector3.right).Using(Vector3Comparer));
        }

        [Test]
        public void HorizontalLookDoesNotTurnUpwardPlayerVelocityIntoVerticalKick()
        {
            var result = BallMotionRules.ApplyKick(
                Vector3.zero,
                Vector3.forward,
                Vector3.up * 20f,
                30f,
                0.5f,
                0.20f);

            Assert.That(result.z, Is.GreaterThan(0f));
            Assert.That(result.y, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void ContactTransferHasPerContactAndAggregateCaps()
        {
            var candidate = BallMotionRules.ComputeContactAssist(Vector3.right * 30f, Vector3.zero, HeadOnNormal);
            var aggregate = BallMotionRules.AccumulateContactAssist(Vector3.right * 10f, candidate);

            Assert.That(candidate.magnitude, Is.EqualTo(GamePhysicsSettings.BallContactAssistPerContactCap).Within(0.0001f));
            Assert.That(aggregate.magnitude, Is.EqualTo(GamePhysicsSettings.BallContactAssistAggregateCap).Within(0.0001f));
        }

        [Test]
        public void RepeatedParticipantCallbackIsAcceptedOnlyOnceUntilCleared()
        {
            var accepted = new HashSet<ulong>();

            Assert.That(BallMotionRules.ShouldAcceptParticipantContact(accepted, 7UL), Is.True);
            Assert.That(BallMotionRules.ShouldAcceptParticipantContact(accepted, 7UL), Is.False);
            accepted.Clear();
            Assert.That(BallMotionRules.ShouldAcceptParticipantContact(accepted, 7UL), Is.True);
            Assert.That(BallMotionRules.ShouldAcceptParticipantContact(accepted, 0UL), Is.False);
        }

        [Test]
        public void DashContributionIsResolvedAsDifferenceOfTotalAndBase()
        {
            var incoming = Vector3.right * 24f;
            var dashContribution = Vector3.right * 14f;
            var baseIncoming = incoming - dashContribution;
            var resolvedTotal = BallMotionRules.ResolvePlayerCollision(incoming, HeadOnNormal, Vector3.zero, true);
            var resolvedBase = BallMotionRules.ResolvePlayerCollision(baseIncoming, HeadOnNormal, Vector3.zero, true);

            var resolvedDash = BallMotionRules.ResolveDashContributionAfterCollision(
                incoming,
                dashContribution,
                HeadOnNormal,
                Vector3.zero,
                true);

            Assert.That(resolvedTotal, Is.EqualTo(Vector3.right * 8.4f).Using(Vector3Comparer));
            Assert.That(resolvedBase, Is.EqualTo(Vector3.right * 3.5f).Using(Vector3Comparer));
            Assert.That(resolvedDash, Is.EqualTo(Vector3.right * 4.9f).Using(Vector3Comparer));
            Assert.That(resolvedDash, Is.EqualTo(resolvedTotal - resolvedBase).Using(Vector3Comparer));
        }

        private static readonly IEqualityComparer<Vector3> Vector3Comparer = new ApproximateVector3Comparer();

        private sealed class ApproximateVector3Comparer : IEqualityComparer<Vector3>
        {
            public bool Equals(Vector3 left, Vector3 right)
            {
                return (left - right).sqrMagnitude <= 0.0000001f;
            }

            public int GetHashCode(Vector3 value)
            {
                return value.GetHashCode();
            }
        }
    }
}
