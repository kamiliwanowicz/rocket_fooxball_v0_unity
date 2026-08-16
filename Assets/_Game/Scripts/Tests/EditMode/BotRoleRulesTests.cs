using NUnit.Framework;
using UnityEngine;
using RocketFooxball.Runtime.Bots;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotRoleRulesTests
    {
        private static readonly Vector3 OwnGoal = new Vector3(-50f, 0f, 0f);
        private static readonly Vector3 EnemyGoal = new Vector3(50f, 0f, 0f);

        [Test]
        public void NoLivingBotsProducesEmptyAssignments()
        {
            var result = Assign(new BotRoleCandidate(0, true, true, Vector3.zero, false, BotRole.Attacker, 0f),
                new BotRoleCandidate(1, false, false, Vector3.zero, false, BotRole.Attacker, 0f),
                new BotRoleCandidate(2, true, false, Vector3.zero, false, BotRole.Attacker, 0f));

            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void OneLivingBotIsDefender()
        {
            var result = Assign(Candidate(4, new Vector3(0f, 0f, 0f)));

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.TryGetAssignment(4, out var assignment), Is.True);
            Assert.That(assignment.Role, Is.EqualTo(BotRole.Defender));
        }

        [Test]
        public void TwoLivingBotsReceiveAttackerAndDefender()
        {
            var result = Assign(Candidate(4, Vector3.zero), Candidate(2, Vector3.zero));

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.TryGetAssignment(2, out var lowSlot), Is.True);
            Assert.That(result.TryGetAssignment(4, out var highSlot), Is.True);
            Assert.That(lowSlot.Role, Is.EqualTo(BotRole.Attacker));
            Assert.That(highSlot.Role, Is.EqualTo(BotRole.Defender));
        }

        [Test]
        public void ThreeLivingBotsReceiveUniqueSortedRoles()
        {
            var result = Assign(Candidate(5, Vector3.zero), Candidate(1, Vector3.zero), Candidate(3, Vector3.zero));

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result.TryGetAssignment(1, out var first), Is.True);
            Assert.That(result.TryGetAssignment(3, out var second), Is.True);
            Assert.That(result.TryGetAssignment(5, out var third), Is.True);
            Assert.That(first.Role, Is.EqualTo(BotRole.Attacker));
            Assert.That(second.Role, Is.EqualTo(BotRole.Support));
            Assert.That(third.Role, Is.EqualTo(BotRole.Defender));
        }

        [Test]
        public void DeadBotIsRemovedAndRemainingBotsAreReassigned()
        {
            var result = Assign(Candidate(1, OwnGoal),
                new BotRoleCandidate(2, false, false, Vector3.zero, true, BotRole.Attacker, 20f),
                Candidate(3, Vector3.zero));

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.TryGetAssignment(2, out _), Is.False);
            Assert.That(result.TryGetAssignment(1, out var first), Is.True);
            Assert.That(result.TryGetAssignment(3, out var second), Is.True);
            Assert.That(first.Role, Is.EqualTo(BotRole.Defender));
            Assert.That(second.Role, Is.EqualTo(BotRole.Attacker));
        }

        [Test]
        public void CurrentRolesHoldBeforeTwoSecondsAndChangeAtInclusiveBoundary()
        {
            var held = AssignWithoutAliveSetChange(
                Candidate(1, OwnGoal, true, BotRole.Attacker, 1.99f),
                Candidate(2, Vector3.zero, true, BotRole.Defender, 1.99f));
            Assert.That(held.TryGetAssignment(1, out var heldFirst), Is.True);
            Assert.That(heldFirst.Role, Is.EqualTo(BotRole.Attacker));

            var switched = AssignWithoutAliveSetChange(
                Candidate(1, OwnGoal, true, BotRole.Attacker, 2f),
                Candidate(2, Vector3.zero, true, BotRole.Defender, 2f));
            Assert.That(switched.TryGetAssignment(1, out var switchedFirst), Is.True);
            Assert.That(switchedFirst.Role, Is.EqualTo(BotRole.Defender));
        }

        [Test]
        public void AliveSetChangeBypassesRoleHold()
        {
            var context = new BotRoleContext(false, Vector3.zero, OwnGoal, EnemyGoal, true);
            var result = BotRoleRules.Assign(context,
                Candidate(1, OwnGoal, true, BotRole.Attacker, 0f),
                Candidate(2, Vector3.zero, true, BotRole.Defender, 0f),
                new BotRoleCandidate(3, true, false, Vector3.zero, false, BotRole.Attacker, 0f));

            Assert.That(result.TryGetAssignment(1, out var first), Is.True);
            Assert.That(first.Role, Is.EqualTo(BotRole.Defender));
        }

        [Test]
        public void EqualTotalsUseRoleOrdinalsInAscendingSlotOrder()
        {
            var context = new BotRoleContext(true, Vector3.zero, Vector3.zero, Vector3.zero, true);
            var result = BotRoleRules.Assign(context,
                Candidate(9, Vector3.zero),
                Candidate(3, Vector3.zero),
                Candidate(6, Vector3.zero));

            Assert.That(result.TryGetAssignment(3, out var first), Is.True);
            Assert.That(result.TryGetAssignment(6, out var second), Is.True);
            Assert.That(result.TryGetAssignment(9, out var third), Is.True);
            Assert.That(first.Role, Is.EqualTo(BotRole.Attacker));
            Assert.That(second.Role, Is.EqualTo(BotRole.Support));
            Assert.That(third.Role, Is.EqualTo(BotRole.Defender));
        }

        [Test]
        public void InvalidGoalsReturnEmptyEvenWhenBotsAreAlive()
        {
            var result = BotRoleRules.Assign(
                new BotRoleContext(false, Vector3.zero, new Vector3(float.NaN, 0f, 0f), EnemyGoal, false),
                Candidate(1, Vector3.zero),
                Candidate(2, Vector3.zero),
                Candidate(3, Vector3.zero));

            Assert.That(result.Count, Is.EqualTo(0));
        }

        private static BotRoleAssignmentSet Assign(BotRoleCandidate first, BotRoleCandidate second = default(BotRoleCandidate), BotRoleCandidate third = default(BotRoleCandidate))
        {
            return BotRoleRules.Assign(
                new BotRoleContext(true, Vector3.zero, OwnGoal, EnemyGoal, true),
                first,
                second,
                third);
        }

        private static BotRoleAssignmentSet AssignWithoutAliveSetChange(BotRoleCandidate first, BotRoleCandidate second)
        {
            return BotRoleRules.Assign(
                new BotRoleContext(true, Vector3.zero, OwnGoal, EnemyGoal, false),
                first,
                second,
                default(BotRoleCandidate));
        }

        private static BotRoleCandidate Candidate(int slotId, Vector3 position, bool hasCurrentRole = false, BotRole currentRole = BotRole.Attacker, float heldSeconds = 0f)
        {
            return new BotRoleCandidate(slotId, false, true, position, hasCurrentRole, currentRole, heldSeconds);
        }
    }
}
