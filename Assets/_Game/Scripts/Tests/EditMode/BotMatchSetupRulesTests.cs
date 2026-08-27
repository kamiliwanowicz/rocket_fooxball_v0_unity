using NUnit.Framework;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Hud;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotMatchSetupRulesTests
    {
        [Test]
        public void SupportedDifficultyValuesAreExactlyTheThreeTiers()
        {
            Assert.That(MatchRules.IsSupportedDifficulty(BotDifficulty.Low), Is.True);
            Assert.That(MatchRules.IsSupportedDifficulty(BotDifficulty.Medium), Is.True);
            Assert.That(MatchRules.IsSupportedDifficulty(BotDifficulty.High), Is.True);
            Assert.That(MatchRules.IsSupportedDifficulty((BotDifficulty)99), Is.False);
        }

        [Test]
        public void SetupSelectionAndStartRequireUnlockedSupportedTier()
        {
            Assert.That(MatchRules.CanSelectEnemyDifficulty(MatchRules.MatchState.Setup, false, BotDifficulty.High), Is.True);
            Assert.That(MatchRules.CanSelectEnemyDifficulty(MatchRules.MatchState.Playing, false, BotDifficulty.High), Is.False);
            Assert.That(MatchRules.CanSelectEnemyDifficulty(MatchRules.MatchState.Setup, true, BotDifficulty.High), Is.False);
            Assert.That(MatchRules.CanSelectEnemyDifficulty(MatchRules.MatchState.Setup, false, (BotDifficulty)99), Is.False);
            Assert.That(MatchRules.CanStartConfiguredMatch(MatchRules.MatchState.Setup, false, BotDifficulty.Medium), Is.True);
            Assert.That(MatchRules.CanStartConfiguredMatch(MatchRules.MatchState.Setup, true, BotDifficulty.Medium), Is.False);
        }

        [Test]
        public void BotSetupSelectionLocksAndControlsDifficultyRequirement()
        {
            Assert.That(MatchRules.CanSelectBotsEnabled(MatchRules.MatchState.Setup, false), Is.True);
            Assert.That(MatchRules.CanSelectBotsEnabled(MatchRules.MatchState.Setup, true), Is.False);
            Assert.That(MatchRules.CanSelectBotsEnabled(MatchRules.MatchState.Playing, false), Is.False);

            Assert.That(MatchRules.CanSelectEnemyDifficulty(MatchRules.MatchState.Setup, false, false, BotDifficulty.High), Is.False);
            Assert.That(MatchRules.CanSelectEnemyDifficulty(MatchRules.MatchState.Setup, false, true, BotDifficulty.High), Is.True);
            Assert.That(MatchRules.CanStartConfiguredMatch(MatchRules.MatchState.Setup, false, false, (BotDifficulty)99), Is.True);
            Assert.That(MatchRules.CanStartConfiguredMatch(MatchRules.MatchState.Setup, false, true, (BotDifficulty)99), Is.False);
            Assert.That(MatchRules.CanStartConfiguredMatch(MatchRules.MatchState.Setup, true, false, BotDifficulty.Medium), Is.False);
        }

        [Test]
        public void PauseAndResumeRequireTheExpectedStateAndLiveClock()
        {
            Assert.That(MatchRules.CanPauseMatch(MatchRules.MatchState.Playing, 0.01f), Is.True);
            Assert.That(MatchRules.CanPauseMatch(MatchRules.MatchState.Playing, 0f), Is.False);
            Assert.That(MatchRules.CanPauseMatch(MatchRules.MatchState.Paused, 10f), Is.False);
            Assert.That(MatchRules.CanResumeMatch(MatchRules.MatchState.Paused), Is.True);
            Assert.That(MatchRules.CanResumeMatch(MatchRules.MatchState.Playing), Is.False);
            Assert.That(MatchRules.CanStartRematch(MatchRules.MatchState.Final), Is.True);
            Assert.That(MatchRules.CanStartRematch(MatchRules.MatchState.Setup), Is.False);
        }

        [Test]
        public void DifficultyResolvesToMediumForAlliesAndInvalidQueries()
        {
            Assert.That(
                MatchRules.ResolveBotDifficulty(ParticipantTeam.Red, ParticipantTeam.Blue, BotDifficulty.High),
                Is.EqualTo(BotDifficulty.High));
            Assert.That(
                MatchRules.ResolveBotDifficulty(ParticipantTeam.Blue, ParticipantTeam.Blue, BotDifficulty.High),
                Is.EqualTo(BotDifficulty.Medium));
            Assert.That(
                MatchRules.ResolveBotDifficulty(ParticipantTeam.Red, ParticipantTeam.Blue, (BotDifficulty)99),
                Is.EqualTo(BotDifficulty.Medium));
        }

        [Test]
        public void SetupAndPausedScreensPrecedeDeathTableAndGoFlags()
        {
            Assert.That(
                MatchHudScreenPolicy.Resolve(MatchController.MatchState.Setup, false, true, true),
                Is.EqualTo(MatchHudScreen.Setup));
            Assert.That(
                MatchHudScreenPolicy.Resolve(MatchController.MatchState.Paused, false, true, true),
                Is.EqualTo(MatchHudScreen.Paused));
        }

        [Test]
        public void BotFreeTablesOnlyDisplayTheLocalParticipant()
        {
            Assert.That(MatchHudScreenPolicy.ShouldDisplayParticipantRow(true, false), Is.True);
            Assert.That(MatchHudScreenPolicy.ShouldDisplayParticipantRow(true, true), Is.True);
            Assert.That(MatchHudScreenPolicy.ShouldDisplayParticipantRow(false, false), Is.False);
            Assert.That(MatchHudScreenPolicy.ShouldDisplayParticipantRow(false, true), Is.True);
        }
    }
}
