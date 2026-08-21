using NUnit.Framework;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Participants;
using UnityEngine;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotCombatEnemyRulesTests
    {
        [Test]
        public void SelectsNearestVisibleLivingEnemyByXZDistance()
        {
            var selected = BotCombatEnemyRules.TrySelectVisibleEnemy(
                Vector3.zero,
                Enemy(7, true, true, new Vector3(0f, 100f, 3f)),
                Enemy(4, true, true, new Vector3(2f, -100f, 0f)),
                Enemy(2, false, true, Vector3.one),
                out var result);

            Assert.That(selected, Is.True);
            Assert.That(result.SlotId, Is.EqualTo(4));
        }

        [Test]
        public void EqualXZDistancesUseLowestSlotAndIgnoreMemory()
        {
            var selected = BotCombatEnemyRules.TrySelectVisibleEnemy(
                Vector3.zero,
                Enemy(9, true, true, new Vector3(3f, 5f, 0f)),
                Enemy(3, true, true, new Vector3(0f, -5f, 3f)),
                Enemy(1, false, true, new Vector3(0.1f, 0f, 0.1f)),
                out var result);

            Assert.That(selected, Is.True);
            Assert.That(result.SlotId, Is.EqualTo(3));
        }

        [Test]
        public void InvalidOrNonVisibleCandidatesAreRejected()
        {
            Assert.That(BotCombatEnemyRules.TrySelectVisibleEnemy(
                Vector3.zero,
                Enemy(1, false, true, Vector3.one),
                Enemy(2, true, false, Vector3.one),
                Enemy(3, true, true, new Vector3(float.NaN, 0f, 0f)),
                out _), Is.False);
        }

        private static BotParticipantObservation Enemy(int slotId, bool visible, bool alive, Vector3 position)
        {
            return new BotParticipantObservation(
                true,
                visible,
                slotId,
                ParticipantTeam.Red,
                false,
                alive,
                position,
                Vector3.zero,
                100f,
                100f,
                false,
                0,
                2,
                0f);
        }
    }
}
