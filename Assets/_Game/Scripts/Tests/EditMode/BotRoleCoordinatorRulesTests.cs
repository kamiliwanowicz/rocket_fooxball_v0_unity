using NUnit.Framework;
using UnityEngine;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotRoleCoordinatorRulesTests
    {
        [Test]
        public void ScheduleInvalidIntervalEvaluatesImmediately()
        {
            Assert.That(BotCoordinatorScheduleRules.ShouldEvaluate(0f, 0f, false, false), Is.True);
            Assert.That(BotCoordinatorScheduleRules.ShouldEvaluate(-1f, 0.5f, false, false), Is.False);
        }

        [Test]
        public void PickupEtiquetteYieldsShotgunToNearbyLocalTeammate()
        {
            var observer = Participant(2, ParticipantTeam.Blue, false, true, Vector3.zero, 100f, 100f, false);
            var human = Participant(0, ParticipantTeam.Blue, true, true, new Vector3(12f, 0f, 0f), 100f, 100f, false);

            Assert.That(BotTargetRules.ShouldYieldShotgun(observer, human), Is.True);
        }

        [Test]
        public void PickupEtiquetteYieldsHealthToMoreCriticalNearbyTeammate()
        {
            var observer = Participant(1, ParticipantTeam.Red, false, true, Vector3.zero, 80f, 100f, false);
            var teammate = Participant(2, ParticipantTeam.Red, false, true, new Vector3(10f, 0f, 0f), 30f, 100f, false);

            Assert.That(BotTargetRules.ShouldYieldHealth(observer, teammate), Is.True);
        }

        private static BotParticipantObservation Participant(
            int slotId,
            ParticipantTeam team,
            bool local,
            bool alive,
            Vector3 position,
            float health,
            float maxHealth,
            bool hasShotgun)
        {
            return new BotParticipantObservation(
                true,
                true,
                slotId,
                team,
                local,
                alive,
                position,
                Vector3.zero,
                health,
                maxHealth,
                hasShotgun,
                0,
                16,
                0f);
        }
    }
}
