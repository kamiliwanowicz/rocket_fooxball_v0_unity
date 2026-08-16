using NUnit.Framework;
using RocketFooxball.Runtime.Weapons;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class ParticipantRelationshipTests
    {
        [TestCase(false, false, false, false, false, false, ParticipantRelationship.Invalid)]
        [TestCase(true, false, false, true, false, false, ParticipantRelationship.Immune)]
        [TestCase(true, true, true, true, false, false, ParticipantRelationship.Immune)]
        [TestCase(true, true, false, false, false, false, ParticipantRelationship.Unattributed)]
        [TestCase(true, true, false, true, true, true, ParticipantRelationship.Self)]
        [TestCase(true, true, false, true, false, true, ParticipantRelationship.Friendly)]
        [TestCase(true, true, false, true, false, false, ParticipantRelationship.Enemy)]
        public void ClassifyFactsReturnsExpectedRelationship(
            bool targetValid,
            bool targetAlive,
            bool targetImmune,
            bool sourceValid,
            bool self,
            bool sameTeam,
            ParticipantRelationship expected)
        {
            var facts = new ParticipantRelationshipFacts(targetValid, targetAlive, targetImmune, sourceValid, self, sameTeam);
            Assert.That(ParticipantRelationshipPolicy.Classify(facts), Is.EqualTo(expected));
        }

        [Test]
        public void RocketDispatchScaleAndEligibilityMatchRelationshipPolicy()
        {
            Assert.That(ParticipantRelationshipPolicy.CanReceiveRocketForce(ParticipantRelationship.Invalid), Is.False);
            Assert.That(ParticipantRelationshipPolicy.CanReceiveRocketForce(ParticipantRelationship.Immune), Is.False);
            Assert.That(ParticipantRelationshipPolicy.CanReceiveRocketForce(ParticipantRelationship.Friendly), Is.False);
            Assert.That(ParticipantRelationshipPolicy.CanReceiveRocketForce(ParticipantRelationship.Self), Is.True);
            Assert.That(ParticipantRelationshipPolicy.CanReceiveRocketForce(ParticipantRelationship.Enemy), Is.True);
            Assert.That(ParticipantRelationshipPolicy.CanReceiveRocketForce(ParticipantRelationship.Unattributed), Is.True);

            Assert.That(ParticipantRelationshipPolicy.CanReceiveRocketDamage(ParticipantRelationship.Enemy), Is.True);
            Assert.That(ParticipantRelationshipPolicy.CanReceiveRocketDamage(ParticipantRelationship.Self), Is.False);
            Assert.That(ParticipantRelationshipPolicy.CanReceiveRocketDamage(ParticipantRelationship.Unattributed), Is.False);
            Assert.That(ParticipantRelationshipPolicy.RocketImpulseScale(ParticipantRelationship.Self, 0.5f), Is.EqualTo(1f));
            Assert.That(ParticipantRelationshipPolicy.RocketImpulseScale(ParticipantRelationship.Unattributed, 0.5f), Is.EqualTo(1f));
            Assert.That(ParticipantRelationshipPolicy.RocketImpulseScale(ParticipantRelationship.Enemy, 0.5f), Is.EqualTo(0.5f));
            Assert.That(ParticipantRelationshipPolicy.RocketImpulseScale(ParticipantRelationship.Friendly, 0.5f), Is.EqualTo(0f));
        }
    }
}
