using NUnit.Framework;
using UnityEngine;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotTargetRulesTests
    {
        [Test]
        public void FootballScoreLadderIsExact()
        {
            Assert.That(BotTargetRules.ScoreFor(BotTargetKind.OwnGoalEmergency), Is.EqualTo(100f));
            Assert.That(BotTargetRules.ScoreFor(BotTargetKind.DefenderCoverage), Is.EqualTo(90f));
            Assert.That(BotTargetRules.ScoreFor(BotTargetKind.AttackerBall), Is.EqualTo(70f));
            Assert.That(BotTargetRules.ScoreFor(BotTargetKind.SupportLane), Is.EqualTo(55f));
            Assert.That(BotTargetRules.ScoreFor(BotTargetKind.HealthPickup), Is.EqualTo(35f));
            Assert.That(BotTargetRules.ScoreFor(BotTargetKind.ShotgunPickup), Is.EqualTo(25f));
            Assert.That(BotTargetRules.ScoreFor(BotTargetKind.AmmoPickup), Is.EqualTo(25f));
            Assert.That(BotTargetRules.ScoreForEnemy(true), Is.EqualTo(20f));
            Assert.That(BotTargetRules.ScoreForEnemy(false), Is.EqualTo(15f));
            Assert.That(BotTargetRules.ScoreFor(BotTargetKind.BallFallback), Is.EqualTo(0f));
        }

        [Test]
        public void ConsiderOrdersScoreThenRouteThenKindThenSubject()
        {
            var current = Candidate(BotTargetKind.HealthPickup, 0, 35f, 4f);
            var emergency = Candidate(BotTargetKind.OwnGoalEmergency, 0, 100f, 50f);
            var selected = BotTargetRules.Consider(ToSelection(current), emergency);
            Assert.That(selected.Key.Kind, Is.EqualTo(BotTargetKind.OwnGoalEmergency));

            var cheaper = Candidate(BotTargetKind.HealthPickup, 0, 35f, 3f);
            selected = BotTargetRules.Consider(ToSelection(current), cheaper);
            Assert.That(selected.Key.SubjectId, Is.EqualTo(0));
            Assert.That(selected.RouteCost, Is.EqualTo(3f));

            var lowerKind = Candidate(BotTargetKind.HealthPickup, 3, 35f, 3f);
            var higherKind = Candidate(BotTargetKind.ShotgunPickup, 1, 35f, 3f);
            selected = BotTargetRules.Consider(ToSelection(lowerKind), higherKind);
            Assert.That(selected.Key.Kind, Is.EqualTo(BotTargetKind.HealthPickup));

            var sameKindLowerSubject = Candidate(BotTargetKind.HealthPickup, 1, 35f, 3f);
            selected = BotTargetRules.Consider(ToSelection(lowerKind), sameKindLowerSubject);
            Assert.That(selected.Key.SubjectId, Is.EqualTo(1));
        }

        [Test]
        public void EqualCandidatesRetainCurrentSelection()
        {
            var candidate = Candidate(BotTargetKind.AttackerBall, 2, 70f, 4f);
            var current = ToSelection(candidate);

            var selected = BotTargetRules.Consider(current, candidate);

            Assert.That(selected.Key.Kind, Is.EqualTo(BotTargetKind.AttackerBall));
            Assert.That(selected.Key.SubjectId, Is.EqualTo(2));
            Assert.That(selected.NavigationPosition, Is.EqualTo(current.NavigationPosition));
        }

        [Test]
        public void InvalidCandidatesAreRejected()
        {
            var current = ToSelection(Candidate(BotTargetKind.AttackerBall, 0, 70f, 1f));
            AssertRejected(current, new BotTargetCandidate(new BotTargetKey(BotTargetKind.AttackerBall, 0), 70f, Vector3.zero, Vector3.zero, false, true, false, 1f, 0f));
            AssertRejected(current, new BotTargetCandidate(new BotTargetKey(BotTargetKind.AttackerBall, 0), float.NaN, Vector3.zero, Vector3.zero, false, true, true, 1f, 0f));
            AssertRejected(current, new BotTargetCandidate(new BotTargetKey(BotTargetKind.AttackerBall, 0), 70f, new Vector3(float.PositiveInfinity, 0f, 0f), Vector3.zero, false, true, true, 1f, 0f));
            AssertRejected(current, new BotTargetCandidate(new BotTargetKey(BotTargetKind.AttackerBall, 0), 70f, Vector3.zero, Vector3.zero, false, true, true, -0.1f, 0f));
            AssertRejected(current, new BotTargetCandidate(new BotTargetKey(BotTargetKind.AttackerBall, 0), 70f, Vector3.zero, Vector3.zero, false, true, true, 1f, float.NaN));
            AssertRejected(current, new BotTargetCandidate(new BotTargetKey(BotTargetKind.None, 0), 1000f, Vector3.zero, Vector3.zero, true, true, true, 0f, 0f));
        }

        [Test]
        public void ActionFlagsFollowFootballPriority()
        {
            BotTargetRules.GetActionFlags(BotTargetKind.OwnGoalEmergency, false, out var suppress, out var prefer);
            Assert.That(suppress, Is.True);
            Assert.That(prefer, Is.True);

            BotTargetRules.GetActionFlags(BotTargetKind.DefenderCoverage, true, out suppress, out prefer);
            Assert.That(suppress, Is.True);
            Assert.That(prefer, Is.True);
            BotTargetRules.GetActionFlags(BotTargetKind.DefenderCoverage, false, out suppress, out prefer);
            Assert.That(suppress, Is.False);
            Assert.That(prefer, Is.False);

            BotTargetRules.GetActionFlags(BotTargetKind.HealthPickup, true, out suppress, out prefer);
            Assert.That(suppress, Is.False);
            Assert.That(prefer, Is.False);
        }

        [Test]
        public void ShotgunYieldUsesSameTeamLivingLocalHumanAndInclusiveRange()
        {
            var observer = Participant(2, false, true, new Vector3(0f, 0f, 0f), 100f, 100f);
            var human = Participant(0, true, true, new Vector3(12f, 0f, 0f), 100f, 100f, false);

            Assert.That(BotTargetRules.ShouldYieldShotgun(observer, human), Is.True);
            Assert.That(BotTargetRules.ShouldYieldShotgun(observer, Participant(0, true, true, new Vector3(12.01f, 0f, 0f), 100f, 100f, false)), Is.False);
            Assert.That(BotTargetRules.ShouldYieldShotgun(observer, Participant(0, true, false, new Vector3(1f, 0f, 0f), 100f, 100f, false)), Is.False);
            Assert.That(BotTargetRules.ShouldYieldShotgun(observer, Participant(5, false, true, new Vector3(1f, 0f, 0f), 100f, 100f, false)), Is.False);
            Assert.That(BotTargetRules.ShouldYieldShotgun(observer, Participant(0, true, true, new Vector3(1f, 0f, 0f), 100f, 100f, true)), Is.False);
        }

        [Test]
        public void HealthYieldUsesCriticalLowerRatioAndLowerSlotTieBreak()
        {
            var observer = Participant(3, false, true, Vector3.zero, 80f, 100f);
            var critical = Participant(2, false, true, new Vector3(10f, 0f, 0f), 30f, 100f);
            Assert.That(BotTargetRules.ShouldYieldHealth(observer, critical), Is.True);
            Assert.That(BotTargetRules.ShouldYieldHealth(observer, Participant(2, false, true, new Vector3(10.01f, 0f, 0f), 30f, 100f)), Is.False);

            var equalLowerSlot = Participant(2, false, true, new Vector3(1f, 0f, 0f), 30f, 100f);
            var equalHigherSlot = Participant(4, false, true, new Vector3(1f, 0f, 0f), 30f, 100f);
            Assert.That(BotTargetRules.ShouldYieldHealth(Participant(3, false, true, Vector3.zero, 30f, 100f), equalLowerSlot), Is.True);
            Assert.That(BotTargetRules.ShouldYieldHealth(Participant(3, false, true, Vector3.zero, 30f, 100f), equalHigherSlot), Is.False);
            Assert.That(BotTargetRules.ShouldYieldHealth(observer, Participant(2, false, true, Vector3.one, 31f, 100f)), Is.False);
        }

        private static BotTargetCandidate Candidate(BotTargetKind kind, int subjectId, float score, float routeCost)
        {
            return new BotTargetCandidate(
                new BotTargetKey(kind, subjectId),
                score,
                Vector3.zero,
                Vector3.zero,
                false,
                false,
                true,
                routeCost,
                0f);
        }

        private static BotTargetSelection ToSelection(BotTargetCandidate candidate)
        {
            return new BotTargetSelection(
                true,
                candidate.Key,
                candidate.Score,
                candidate.NavigationPosition,
                candidate.ActionPosition,
                candidate.SuppressParticipantCombat,
                candidate.PreferBallActions,
                candidate.RouteCost,
                candidate.ExpiresAt);
        }

        private static void AssertRejected(BotTargetSelection current, BotTargetCandidate candidate)
        {
            var result = BotTargetRules.Consider(current, candidate);
            Assert.That(result.Key.Kind, Is.EqualTo(current.Key.Kind));
            Assert.That(result.Key.SubjectId, Is.EqualTo(current.Key.SubjectId));
        }

        private static BotParticipantObservation Participant(
            int slotId,
            bool isLocal,
            bool isAlive,
            Vector3 position,
            float health,
            float maxHealth,
            bool hasShotgun = false)
        {
            return new BotParticipantObservation(
                true,
                true,
                slotId,
                ParticipantTeam.Blue,
                isLocal,
                isAlive,
                position,
                Vector3.zero,
                health,
                maxHealth,
                hasShotgun,
                0,
                2,
                0f);
        }
    }
}
