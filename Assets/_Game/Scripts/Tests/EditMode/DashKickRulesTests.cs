using NUnit.Framework;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Movement;
using UnityEngine;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class DashKickRulesTests
    {
        [Test]
        public void AcceptedActivationChargesExactThreeSecondCooldownWithoutBallPolicy()
        {
            Assert.That(DashKickRules.CanActivate(true, Vector3.forward, false, 0f, true, false), Is.True);
            Assert.That(DashKickRules.TickCooldown(3f, 0f), Is.EqualTo(3f));
            Assert.That(DashKickRules.TickCooldown(3f, 1.25f), Is.EqualTo(1.75f));
            Assert.That(DashKickRules.TickCooldown(1.75f, 1.75f), Is.EqualTo(0f));
        }

        [Test]
        public void CooldownAndAirUseRejectWithoutAcceptance()
        {
            Assert.That(DashKickRules.CanActivate(true, Vector3.forward, false, 0.01f, true, true), Is.False);
            Assert.That(DashKickRules.CanActivate(true, Vector3.forward, false, 0f, false, false), Is.False);
            Assert.That(DashKickRules.CanActivate(true, Vector3.forward, false, 0f, false, true), Is.True);
            Assert.That(DashKickRules.CanActivate(true, Vector3.forward, false, 0f, true, false), Is.True);
        }

        [Test]
        public void InvalidOrActiveDashNeverAccepts()
        {
            Assert.That(DashKickRules.CanActivate(true, Vector3.zero, false, 0f, true, true), Is.False);
            Assert.That(DashKickRules.CanActivate(true, Vector3.forward, true, 0f, true, true), Is.False);
            Assert.That(DashKickRules.CanActivate(false, Vector3.forward, false, 0f, true, true), Is.False);
        }

        [Test]
        public void KickDirectionPrefersFullPitchedLookOverPlanarDashDirection()
        {
            var lookDirection = new Vector3(0.2f, 0.6f, 0.8f).normalized;

            var resolved = DashKickRules.ResolveKickDirection(lookDirection, Vector3.forward);

            Assert.That(Vector3.Distance(resolved, lookDirection), Is.LessThan(0.0001f));
            Assert.That(resolved.y, Is.GreaterThan(0f));
        }

        [Test]
        public void KickDirectionKeepsHorizontalLookHorizontal()
        {
            var resolved = DashKickRules.ResolveKickDirection(new Vector3(2f, 0f, 1f), Vector3.up);

            Assert.That(resolved.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(resolved.sqrMagnitude, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void KickDirectionFallsBackToDashDirectionWhenLookIsInvalid()
        {
            var fallback = new Vector3(1f, 0f, 2f).normalized;

            Assert.That(
                Vector3.Distance(
                    DashKickRules.ResolveKickDirection(new Vector3(float.NaN, 0f, 1f), fallback),
                    fallback),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Distance(DashKickRules.ResolveKickDirection(Vector3.zero, fallback), fallback),
                Is.LessThan(0.0001f));
        }

        [Test]
        public void KickDirectionRejectsInvalidLookAndFallback()
        {
            Assert.That(
                DashKickRules.ResolveKickDirection(Vector3.zero, new Vector3(float.PositiveInfinity, 0f, 0f)),
                Is.EqualTo(Vector3.zero));
            Assert.That(
                DashKickRules.ResolveKickDirection(new Vector3(0f, 0f, float.NaN), Vector3.zero),
                Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void MissingLaterProgrammaticAimRetainsAcceptedAimAsFallback()
        {
            var acceptedAim = new Vector3(0f, 0.5f, 1f).normalized;

            Assert.That(
                Vector3.Distance(DashKickRules.ResolveKickDirection(Vector3.zero, acceptedAim), acceptedAim),
                Is.LessThan(0.0001f));
        }

        [Test]
        public void AirDashExhaustionRejectsOnlyAirborneActivation()
        {
            Assert.That(DashKickRules.CanActivate(true, Vector3.forward, false, 0f, false, false), Is.False);
            Assert.That(DashKickRules.CanActivate(true, Vector3.forward, false, 0f, false, true), Is.True);
        }

        [Test]
        public void GroundedDashDoesNotRequireAirChargeAfterLanding()
        {
            Assert.That(DashKickRules.CanActivate(true, Vector3.forward, false, 0f, true, false), Is.True);
        }

        [Test]
        public void DashCompositionPreservesMomentumUnderStrictCap()
        {
            var composed = MovementMath.ComposeDashVelocity(new Vector3(10f, 0f, 0f), Vector3.forward, 12f, 30f);

            Assert.That(composed, Is.EqualTo(new Vector3(10f, 0f, 12f)));
            Assert.That(composed.magnitude, Is.LessThanOrEqualTo(30f));
        }

        [Test]
        public void AirDashPreservesNormalizedDownwardAimAsFullThreeDimensionalImpulse()
        {
            var direction = new Vector3(0f, -1f, 1f).normalized;
            var composed = MovementMath.ComposeDashVelocity(Vector3.zero, direction, 12f, 30f);

            Assert.That(composed.y, Is.LessThan(0f));
            Assert.That(composed.z, Is.GreaterThan(0f));
            Assert.That(composed.magnitude, Is.EqualTo(12f).Within(0.0001f));
        }

        [Test]
        public void DefaultDashBurstUsesTwentyFourUnitsWithoutExceedingThirtyUnitCap()
        {
            var direction = new Vector3(0f, -1f, 1f).normalized;
            var composed = MovementMath.ComposeDashVelocity(Vector3.zero, direction, PlayerMotorDefaults.DashBurstSpeed, PlayerMotorDefaults.DashSpeedCap);

            Assert.That(PlayerMotorDefaults.DashBurstSpeed, Is.EqualTo(24f));
            Assert.That(composed.magnitude, Is.EqualTo(24f).Within(0.0001f));
            Assert.That(composed.y, Is.LessThan(0f));
            Assert.That(composed.z, Is.GreaterThan(0f));
            Assert.That(composed.magnitude, Is.LessThanOrEqualTo(PlayerMotorDefaults.DashSpeedCap));
        }

        [Test]
        public void DashCompositionCapsTheFullVectorIncludingVerticalMomentum()
        {
            var composed = MovementMath.ComposeDashVelocity(new Vector3(20f, 20f, 0f), Vector3.up, 12f, 30f);

            Assert.That(composed.magnitude, Is.EqualTo(30f).Within(0.0001f));
            Assert.That(composed.y, Is.GreaterThan(0f));
        }

        [Test]
        public void HorizontalAndUpwardAimKeepTheirVerticalComponents()
        {
            var horizontal = MovementMath.ComposeDashVelocity(Vector3.zero, Vector3.right, 12f, 30f);
            var upward = MovementMath.ComposeDashVelocity(Vector3.zero, Vector3.up, 12f, 30f);

            Assert.That(horizontal, Is.EqualTo(Vector3.right * 12f));
            Assert.That(upward, Is.EqualTo(Vector3.up * 12f));
            Assert.That(horizontal.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(upward.y, Is.GreaterThan(0f));
        }

        [Test]
        public void DashCompositionClampsAlreadyAtCap()
        {
            var composed = MovementMath.ComposeDashVelocity(Vector3.right * 30f, Vector3.forward, 12f, 30f);

            Assert.That(composed.magnitude, Is.EqualTo(30f).Within(0.0001f));
        }

        [Test]
        public void ContributionSteeringRespectsAngleBoundAndCap()
        {
            var steered = MovementMath.SteerContribution(Vector3.forward * 12f, Vector3.right, 180f, 0.1f);
            var recomposed = Vector3.ClampMagnitude(Vector3.right * 25f + steered, 30f);

            Assert.That(Vector3.Angle(Vector3.forward, steered), Is.EqualTo(18f).Within(0.001f));
            Assert.That(steered.magnitude, Is.EqualTo(12f).Within(0.0001f));
            Assert.That(recomposed.magnitude, Is.LessThanOrEqualTo(30f));
        }

        [Test]
        public void DurationRetainsDashContributionAsMomentum()
        {
            var velocity = new Vector3(20f, 2f, 0f);
            var result = MovementMath.RemoveContributionWithoutReversal(velocity, Vector3.right * 12f, 1f);

            Assert.That(result, Is.EqualTo(velocity));
        }

        [Test]
        public void EnemyRetentionKeepsTwentyPercentOfTrackedContribution()
        {
            var result = MovementMath.RemoveContributionWithoutReversal(Vector3.right * 20f, Vector3.right * 12f, 0.20f);

            Assert.That(result.x, Is.EqualTo(10.4f).Within(0.0001f));
        }

        [Test]
        public void WallRemovalCannotReverseCurrentMovement()
        {
            var result = MovementMath.RemoveContributionWithoutReversal(Vector3.right * 2f, Vector3.right * 12f, 0f);

            Assert.That(result.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(Vector3.Dot(result, Vector3.right), Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void ContactDelayAndOrderingAreDeterministic()
        {
            Assert.That(DashKickRules.IsContactActive(0.099f, 0.10f), Is.False);
            Assert.That(DashKickRules.IsContactActive(0.10f, 0.10f), Is.True);
            Assert.That(DashKickRules.IsForward(Vector3.zero, Vector3.forward, Vector3.forward), Is.True);
            Assert.That(DashKickRules.IsForward(Vector3.zero, Vector3.forward, Vector3.back), Is.False);
            Assert.That(DashKickRules.IsBetterContactCandidate(1f, EntityId.FromULong(20UL), 2f, EntityId.FromULong(10UL)), Is.True);
            Assert.That(DashKickRules.IsBetterContactCandidate(2f, EntityId.FromULong(5UL), 2f, EntityId.FromULong(10UL)), Is.True);
            Assert.That(DashKickRules.IsBetterContactCandidate(2f, EntityId.FromULong(15UL), 2f, EntityId.FromULong(10UL)), Is.False);
            Assert.That(DashKickRules.ShouldProcessContact(false), Is.True);
            Assert.That(DashKickRules.ShouldProcessContact(true), Is.False);
        }
    }
}
