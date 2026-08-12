using NUnit.Framework;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class MatchRulesTests
    {
        [Test]
        public void MatchStateOrdinalsRemainStable()
        {
            Assert.That((int)MatchRules.MatchState.Playing, Is.EqualTo(0));
            Assert.That((int)MatchRules.MatchState.GoalFreeze, Is.EqualTo(1));
            Assert.That((int)MatchRules.MatchState.Reset, Is.EqualTo(2));
            Assert.That((int)MatchRules.MatchState.OpeningCountdown, Is.EqualTo(3));
            Assert.That((int)MatchRules.MatchState.KickoffCountdown, Is.EqualTo(4));
            Assert.That((int)MatchRules.MatchState.Final, Is.EqualTo(5));
        }

        [Test]
        public void ClockRunsOnlyWhenCallerUsesPlayingAndClampsAtZero()
        {
            var running = MatchRules.AdvanceClock(1f, 0.25f);
            Assert.That(running.Remaining, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(running.ReachedZero, Is.False);

            var stopped = MatchRules.AdvancePhase(3f, 10f);
            Assert.That(stopped, Is.EqualTo(0f));

            var final = MatchRules.AdvanceClock(0.1f, 0.2f);
            Assert.That(final.Remaining, Is.EqualTo(0f));
            Assert.That(final.ReachedZero, Is.True);
        }

        [Test]
        public void GameplayGateAndCountdownPhaseAreDeterministic()
        {
            Assert.That(MatchRules.IsGameplayEnabled(MatchRules.MatchState.Playing), Is.True);
            Assert.That(MatchRules.IsGameplayEnabled(MatchRules.MatchState.GoalFreeze), Is.False);
            Assert.That(MatchRules.IsGameplayEnabled(MatchRules.MatchState.Reset), Is.False);
            Assert.That(MatchRules.CountdownNumber(MatchRules.MatchState.OpeningCountdown, 2.4f), Is.EqualTo(3));
            Assert.That(MatchRules.CountdownNumber(MatchRules.MatchState.KickoffCountdown, 0.2f), Is.EqualTo(1));
            Assert.That(MatchRules.CountdownNumber(MatchRules.MatchState.Final, 2f), Is.EqualTo(0));
        }

        [Test]
        public void RegularGoalCreditsAttackingToucher()
        {
            var delta = MatchRules.ResolveGoal(MatchRules.MatchState.Playing, ParticipantTeam.Red, true, ParticipantTeam.Blue, 4);
            Assert.That(delta.Accepted, Is.True);
            Assert.That(delta.ScoringTeam, Is.EqualTo(ParticipantTeam.Blue));
            Assert.That(delta.IsOwnGoal, Is.False);
            Assert.That(delta.ScorerSlotId, Is.EqualTo(4));
            Assert.That(delta.BlueGoalsDelta, Is.EqualTo(1));
            Assert.That(delta.RedGoalsDelta, Is.EqualTo(0));
        }

        [Test]
        public void OwnGoalCreditsOpposingTeamWithoutPersonalGoal()
        {
            var delta = MatchRules.ResolveGoal(MatchRules.MatchState.Playing, ParticipantTeam.Blue, true, ParticipantTeam.Blue, 2);
            Assert.That(delta.Accepted, Is.True);
            Assert.That(delta.ScoringTeam, Is.EqualTo(ParticipantTeam.Red));
            Assert.That(delta.IsOwnGoal, Is.True);
            Assert.That(delta.ScorerSlotId, Is.EqualTo(-1));
            Assert.That(delta.ResponsibleSlotId, Is.EqualTo(2));
            Assert.That(delta.RedGoalsDelta, Is.EqualTo(1));
        }

        [Test]
        public void UnattributedGoalCreditsTeamOnly()
        {
            var delta = MatchRules.ResolveGoal(MatchRules.MatchState.Playing, ParticipantTeam.Red);
            Assert.That(delta.Accepted, Is.True);
            Assert.That(delta.BlueGoalsDelta, Is.EqualTo(1));
            Assert.That(delta.ScorerSlotId, Is.EqualTo(-1));
            Assert.That(delta.ResponsibleSlotId, Is.EqualTo(-1));
        }

        [Test]
        public void EnemyKillAddsKillerFragAndDeath()
        {
            var delta = MatchRules.ResolveDeath(MatchRules.MatchState.Playing, 1, ParticipantTeam.Blue, 5, ParticipantTeam.Red, ParticipantDeathCause.Rocket);
            Assert.That(delta.Accepted, Is.True);
            Assert.That(delta.VictimDeathsDelta, Is.EqualTo(1));
            Assert.That(delta.VictimFragsDelta, Is.EqualTo(0));
            Assert.That(delta.KillerFragsDelta, Is.EqualTo(1));
        }

        [Test]
        public void SelfArenaAndSameSlotDeathsDecrementVictimOnly()
        {
            var self = MatchRules.ResolveDeath(MatchRules.MatchState.Playing, 1, ParticipantTeam.Blue, -1, null, ParticipantDeathCause.Self);
            var arena = MatchRules.ResolveDeath(MatchRules.MatchState.Playing, 1, ParticipantTeam.Blue, -1, null, ParticipantDeathCause.Arena);
            var same = MatchRules.ResolveDeath(MatchRules.MatchState.Playing, 1, ParticipantTeam.Blue, 1, ParticipantTeam.Blue, ParticipantDeathCause.Rocket);
            Assert.That(self.VictimFragsDelta, Is.EqualTo(-1));
            Assert.That(arena.VictimFragsDelta, Is.EqualTo(-1));
            Assert.That(same.VictimFragsDelta, Is.EqualTo(-1));
            Assert.That(self.KillerFragsDelta, Is.EqualTo(0));
        }

        [Test]
        public void FriendlyOrUnknownKillAddsNoFragButDeathStillCounts()
        {
            var friendly = MatchRules.ResolveDeath(MatchRules.MatchState.Playing, 1, ParticipantTeam.Blue, 2, ParticipantTeam.Blue, ParticipantDeathCause.Rocket);
            var unknown = MatchRules.ResolveDeath(MatchRules.MatchState.Playing, 1, ParticipantTeam.Blue, -1, null, ParticipantDeathCause.Unknown);
            Assert.That(friendly.VictimDeathsDelta, Is.EqualTo(1));
            Assert.That(friendly.VictimFragsDelta, Is.EqualTo(0));
            Assert.That(friendly.KillerFragsDelta, Is.EqualTo(0));
            Assert.That(unknown.VictimDeathsDelta, Is.EqualTo(1));
        }

        [Test]
        public void NegativeTeamTotalsArePreserved()
        {
            var result = MatchRules.ResolveOutcome(0, 0, -2, -3, out var rule);
            Assert.That(result, Is.EqualTo(MatchOutcome.BlueWin));
            Assert.That(rule, Is.EqualTo(MatchDecisionRule.TeamFrags));
        }

        [Test]
        public void GoalsWinBeforeFragTiebreak()
        {
            var result = MatchRules.ResolveOutcome(2, 1, -4, 9, out var rule);
            Assert.That(result, Is.EqualTo(MatchOutcome.BlueWin));
            Assert.That(rule, Is.EqualTo(MatchDecisionRule.Goals));
        }

        [Test]
        public void EqualGoalsUseTeamFragsThenDraw()
        {
            var fragResult = MatchRules.ResolveOutcome(2, 2, 1, 3, out var fragRule);
            var drawResult = MatchRules.ResolveOutcome(2, 2, 3, 3, out var drawRule);
            Assert.That(fragResult, Is.EqualTo(MatchOutcome.RedWin));
            Assert.That(fragRule, Is.EqualTo(MatchDecisionRule.TeamFrags));
            Assert.That(drawResult, Is.EqualTo(MatchOutcome.Draw));
            Assert.That(drawRule, Is.EqualTo(MatchDecisionRule.Draw));
        }
    }
}
