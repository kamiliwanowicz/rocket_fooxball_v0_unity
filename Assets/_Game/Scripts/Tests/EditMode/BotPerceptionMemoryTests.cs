using NUnit.Framework;
using UnityEngine;
using RocketFooxball.Runtime.Bots;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotPerceptionMemoryTests
    {
        [Test]
        public void ConsecutiveVisibleAvailableToUnavailableStartsWitnessedTimer()
        {
            var prior = new BotPickupMemoryState(true, true, false, 0f);
            var next = BotPerceptionMemory.UpdatePickup(prior, true, false, 15f, 0.02f);

            Assert.That(next.HasPriorVisibleSample, Is.True);
            Assert.That(next.PriorVisibleAvailable, Is.False);
            Assert.That(next.HasWitnessedRespawn, Is.True);
            Assert.That(next.Remaining, Is.EqualTo(15f));
        }

        [Test]
        public void UnseenWitnessedTimerPublishesUnavailableThenAvailableBelief()
        {
            var state = new BotPickupMemoryState(true, false, true, 1f);
            var beforeDue = BotPerceptionMemory.UpdatePickup(state, false, false, 15f, 0.25f);
            var before = BotPerceptionMemory.PublishPickup(0, BotPickupKind.Health, Vector3.zero, beforeDue, false, false, 0.25f);

            Assert.That(before.HasObservation, Is.True);
            Assert.That(before.IsVisible, Is.False);
            Assert.That(before.IsAvailable, Is.False);
            Assert.That(before.HasWitnessedRespawn, Is.True);
            Assert.That(before.EstimatedRespawnRemaining, Is.EqualTo(0.75f).Within(0.0001f));

            var due = BotPerceptionMemory.UpdatePickup(beforeDue, false, false, 15f, 1f);
            var after = BotPerceptionMemory.PublishPickup(0, BotPickupKind.Health, Vector3.zero, due, false, false, 1.25f);
            Assert.That(after.HasObservation, Is.True);
            Assert.That(after.IsAvailable, Is.True);
            Assert.That(after.EstimatedRespawnRemaining, Is.EqualTo(0f));
            Assert.That(after.HasWitnessedRespawn, Is.True);
        }

        [Test]
        public void FirstSeenUnavailableAndUnseenGapDoNotInventRespawnKnowledge()
        {
            var firstUnavailable = BotPerceptionMemory.UpdatePickup(
                default(BotPickupMemoryState), true, false, 15f, 0f);
            var visible = BotPerceptionMemory.PublishPickup(
                1, BotPickupKind.Health, Vector3.one, firstUnavailable, true, false, 0f);
            var unseen = BotPerceptionMemory.PublishPickup(
                1, BotPickupKind.Health, Vector3.one, firstUnavailable, false, false, 1f);

            Assert.That(visible.HasObservation, Is.True);
            Assert.That(visible.HasWitnessedRespawn, Is.False);
            Assert.That(unseen.HasObservation, Is.False);
        }

        [Test]
        public void UnseenGapBreaksVisibleTransitionEligibility()
        {
            var available = BotPerceptionMemory.UpdatePickup(
                default(BotPickupMemoryState), true, true, 15f, 0f);
            var unseenState = BotPerceptionMemory.UpdatePickup(
                available, false, false, 15f, 0.02f);
            var unseen = BotPerceptionMemory.PublishPickup(
                1, BotPickupKind.Health, Vector3.zero, unseenState, false, false, 0.02f);

            Assert.That(unseenState.HasWitnessedRespawn, Is.False);
            Assert.That(unseen.HasObservation, Is.False);

            var unavailable = BotPerceptionMemory.UpdatePickup(
                unseenState, true, false, 15f, 0f);
            Assert.That(unavailable.HasWitnessedRespawn, Is.False);
            Assert.That(unavailable.Remaining, Is.EqualTo(0f));
        }

        [Test]
        public void VisibleAvailableClearsTimerAndVisibleUnavailableAfterDueClearsWitness()
        {
            var witnessed = new BotPickupMemoryState(true, false, true, 0f);
            var available = BotPerceptionMemory.UpdatePickup(witnessed, true, true, 15f, 0f);
            Assert.That(available.HasWitnessedRespawn, Is.False);
            Assert.That(available.Remaining, Is.EqualTo(0f));

            var unavailable = BotPerceptionMemory.UpdatePickup(witnessed, true, false, 15f, 0f);
            Assert.That(unavailable.HasWitnessedRespawn, Is.False);
            Assert.That(unavailable.Remaining, Is.EqualTo(0f));
        }

        [Test]
        public void InvalidDelayNeverCreatesTimer()
        {
            var prior = new BotPickupMemoryState(true, true, false, 0f);
            var next = BotPerceptionMemory.UpdatePickup(prior, true, false, float.NaN, 0f);
            var observation = BotPerceptionMemory.PublishPickup(
                2, BotPickupKind.Shotgun, Vector3.zero, next, false, false, 0f);

            Assert.That(next.HasWitnessedRespawn, Is.False);
            Assert.That(observation.HasObservation, Is.False);
        }

        [Test]
        public void ScheduleRunsImmediatelyOnFirstOrAliveChangeAndAtInclusiveCadence()
        {
            Assert.That(BotCoordinatorScheduleRules.ShouldEvaluate(0f, 0.5f, true, false), Is.True);
            Assert.That(BotCoordinatorScheduleRules.ShouldEvaluate(0f, 0.5f, false, true), Is.True);
            Assert.That(BotCoordinatorScheduleRules.ShouldEvaluate(0.4999f, 0.5f, false, false), Is.False);
            Assert.That(BotCoordinatorScheduleRules.ShouldEvaluate(0.5f, 0.5f, false, false), Is.True);
        }
    }
}
