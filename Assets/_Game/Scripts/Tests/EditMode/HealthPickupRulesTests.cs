using NUnit.Framework;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Pickups;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class HealthPickupRulesTests
    {
        [Test]
        public void AliveHalfHealthRestoresConfiguredFraction()
        {
            var model = Model(50f, 100f, ParticipantLifecycle.Alive);

            Assert.That(HealthPickupRules.TryCalculateRestore(model, 0.33f, out var amount), Is.True);
            Assert.That(amount, Is.EqualTo(33f).Within(0.0001f));
        }

        [Test]
        public void NearFullHealthRestoresOnlyMissingHealth()
        {
            var model = Model(90f, 100f, ParticipantLifecycle.Alive);

            Assert.That(HealthPickupRules.TryCalculateRestore(model, 0.33f, out var amount), Is.True);
            Assert.That(amount, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void FullHealthCannotConsume()
        {
            var model = Model(100f, 100f, ParticipantLifecycle.Alive);

            Assert.That(HealthPickupRules.TryCalculateRestore(model, 0.33f, out var amount), Is.False);
            Assert.That(amount, Is.EqualTo(0f));
        }

        [Test]
        public void FailedEligibilityDoesNotConsumeRespawnState()
        {
            var state = new PickupRespawnState();
            var model = Model(100f, 100f, ParticipantLifecycle.Alive);

            var applied = HealthPickupRules.TryCalculateRestore(model, 0.33f, out _) && state.TryConsume(15f);

            Assert.That(applied, Is.False);
            Assert.That(state.IsAvailable, Is.True);
            Assert.That(state.Remaining, Is.EqualTo(0f));
        }

        [Test]
        public void DeadAndRespawningParticipantsCannotConsume()
        {
            var dead = Model(50f, 100f, ParticipantLifecycle.Dead);
            var respawning = Model(50f, 100f, ParticipantLifecycle.Respawning);

            Assert.That(HealthPickupRules.TryCalculateRestore(dead, 0.33f, out _), Is.False);
            Assert.That(HealthPickupRules.TryCalculateRestore(respawning, 0.33f, out _), Is.False);
        }

        [Test]
        public void InvalidHealthMaxAndFractionInputsReject()
        {
            Assert.That(HealthPickupRules.TryCalculateRestore(Model(50f, 0f, ParticipantLifecycle.Alive), 0.33f, out _), Is.False);
            Assert.That(HealthPickupRules.TryCalculateRestore(Model(float.NaN, 100f, ParticipantLifecycle.Alive), 0.33f, out _), Is.False);
            Assert.That(HealthPickupRules.TryCalculateRestore(Model(50f, float.PositiveInfinity, ParticipantLifecycle.Alive), 0.33f, out _), Is.False);
            Assert.That(HealthPickupRules.TryCalculateRestore(Model(50f, 100f, ParticipantLifecycle.Alive), 0f, out _), Is.False);
            Assert.That(HealthPickupRules.TryCalculateRestore(Model(50f, 100f, ParticipantLifecycle.Alive), -0.1f, out _), Is.False);
            Assert.That(HealthPickupRules.TryCalculateRestore(Model(50f, 100f, ParticipantLifecycle.Alive), float.NaN, out _), Is.False);
            Assert.That(HealthPickupRules.TryCalculateRestore(Model(50f, 100f, ParticipantLifecycle.Alive), float.PositiveInfinity, out _), Is.False);
        }

        [Test]
        public void FractionAboveOneStillCapsAtMissingHealth()
        {
            var model = Model(20f, 100f, ParticipantLifecycle.Alive);

            Assert.That(HealthPickupRules.TryCalculateRestore(model, 2f, out var amount), Is.True);
            Assert.That(amount, Is.EqualTo(80f).Within(0.0001f));
        }

        [Test]
        public void RespawnStateConsumesOnceAndReturnsAtFifteenSecondBoundary()
        {
            var state = new PickupRespawnState();

            Assert.That(state.IsAvailable, Is.True);
            Assert.That(state.TryConsume(15f), Is.True);
            Assert.That(state.TryConsume(15f), Is.False);
            Assert.That(state.IsAvailable, Is.False);
            Assert.That(state.Remaining, Is.EqualTo(15f).Within(0.0001f));
            Assert.That(state.Advance(14f), Is.False);
            Assert.That(state.IsAvailable, Is.False);
            Assert.That(state.Remaining, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(state.Advance(1f), Is.True);
            Assert.That(state.IsAvailable, Is.True);
            Assert.That(state.Remaining, Is.EqualTo(0f));
        }

        [Test]
        public void RespawnStateClampsOvershootAndResetRestoresEarly()
        {
            var state = new PickupRespawnState();

            Assert.That(state.TryConsume(15f), Is.True);
            Assert.That(state.Advance(20f), Is.True);
            Assert.That(state.Remaining, Is.EqualTo(0f));
            Assert.That(state.TryConsume(15f), Is.True);
            state.Reset();
            Assert.That(state.IsAvailable, Is.True);
            Assert.That(state.Remaining, Is.EqualTo(0f));
        }

        [Test]
        public void RespawnStateRejectsInvalidOrNonpositiveFixedDelta()
        {
            var state = new PickupRespawnState();
            Assert.That(state.TryConsume(15f), Is.True);

            Assert.That(state.Advance(0f), Is.False);
            Assert.That(state.Advance(-1f), Is.False);
            Assert.That(state.Advance(float.NaN), Is.False);
            Assert.That(state.Advance(float.PositiveInfinity), Is.False);
            Assert.That(state.IsAvailable, Is.False);
            Assert.That(state.Remaining, Is.EqualTo(15f).Within(0.0001f));
        }

        [Test]
        public void RespawnStateRejectsInvalidConsumeDelayWithoutChangingAvailability()
        {
            var state = new PickupRespawnState();

            Assert.That(state.TryConsume(-1f), Is.False);
            Assert.That(state.TryConsume(float.NaN), Is.False);
            Assert.That(state.TryConsume(float.PositiveInfinity), Is.False);
            Assert.That(state.IsAvailable, Is.True);
            Assert.That(state.Remaining, Is.EqualTo(0f));
        }

        private static ParticipantReadModel Model(float health, float maxHealth, ParticipantLifecycle lifecycle)
        {
            return new ParticipantReadModel(
                1,
                "Test",
                ParticipantTeam.Blue,
                true,
                health,
                maxHealth,
                lifecycle,
                0f,
                0f);
        }
    }
}
