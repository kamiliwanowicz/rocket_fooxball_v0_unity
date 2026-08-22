using System;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Match
{
    /// <summary>Pure match transitions and score deltas. MatchController owns mutable state and Unity side effects.</summary>
    public static class MatchRules
    {
        // Keep serialized enum ordinals stable. New phases append only.
        public enum MatchState
        {
            Playing = 0,
            GoalFreeze = 1,
            Reset = 2,
            OpeningCountdown = 3,
            KickoffCountdown = 4,
            Final = 5,
            Setup = 6,
            Paused = 7
        }

        /// <summary>Legacy goal result retained for diagnostics and older callers.</summary>
        public readonly struct GoalTransition
        {
            public GoalTransition(MatchState state, float freezeRemaining, int northScore, int southScore, bool enteredGoalFreeze)
            {
                State = state;
                FreezeRemaining = freezeRemaining;
                NorthScore = northScore;
                SouthScore = southScore;
                EnteredGoalFreeze = enteredGoalFreeze;
            }

            public MatchState State { get; }
            public float FreezeRemaining { get; }
            public int NorthScore { get; }
            public int SouthScore { get; }
            public bool EnteredGoalFreeze { get; }
        }

        /// <summary>Pure old geometry-keyed transition. NorthScore remains Red and SouthScore remains Blue.</summary>
        public static GoalTransition BeginGoal(MatchState state, int northScore, int southScore, GoalTrigger.GoalSide crossedGoal, float goalFreezeDuration)
        {
            if (state != MatchState.Playing)
            {
                return new GoalTransition(state, 0f, northScore, southScore, false);
            }

            if (crossedGoal == GoalTrigger.GoalSide.North)
            {
                southScore++;
            }
            else
            {
                northScore++;
            }

            // GoalFreeze is dismissed by a fresh input press, not by a timer. Keep the
            // legacy argument for callers that still use this compatibility helper.
            _ = goalFreezeDuration;
            return new GoalTransition(MatchState.GoalFreeze, 0f, northScore, southScore, true);
        }

        /// <summary>Advances an unscaled phase timer and clamps at zero.</summary>
        public static float AdvanceGoalFreeze(float freezeRemaining, float unscaledDeltaTime)
        {
            return ClampRemaining(freezeRemaining, unscaledDeltaTime);
        }

        public static float AdvancePhase(float phaseRemaining, float unscaledDeltaTime)
        {
            return ClampRemaining(phaseRemaining, unscaledDeltaTime);
        }

        /// <summary>Advances live clock only when caller supplies a Playing phase.</summary>
        public static MatchClockAdvance AdvanceClock(float remaining, float deltaTime)
        {
            var safeRemaining = Math.Max(remaining, 0f);
            var safeDelta = Math.Max(deltaTime, 0f);
            var next = Math.Max(safeRemaining - safeDelta, 0f);
            return new MatchClockAdvance(next, safeRemaining - next, next <= 0f);
        }

        public static bool IsGameplayEnabled(MatchState state)
        {
            return state == MatchState.Playing;
        }

        public static bool IsSupportedDifficulty(BotDifficulty difficulty)
        {
            return difficulty == BotDifficulty.Low ||
                   difficulty == BotDifficulty.Medium ||
                   difficulty == BotDifficulty.High;
        }

        public static bool CanSelectBotsEnabled(MatchState state, bool configurationLocked)
        {
            return state == MatchState.Setup && !configurationLocked;
        }

        public static bool CanSelectEnemyDifficulty(
            MatchState state,
            bool configurationLocked,
            bool botsEnabled,
            BotDifficulty difficulty)
        {
            return state == MatchState.Setup &&
                   !configurationLocked &&
                   botsEnabled &&
                   IsSupportedDifficulty(difficulty);
        }

        public static bool CanSelectEnemyDifficulty(MatchState state, bool difficultyLocked, BotDifficulty difficulty)
        {
            return CanSelectEnemyDifficulty(state, difficultyLocked, true, difficulty);
        }

        public static bool CanStartConfiguredMatch(
            MatchState state,
            bool configurationLocked,
            bool botsEnabled,
            BotDifficulty difficulty)
        {
            return state == MatchState.Setup &&
                   !configurationLocked &&
                   (!botsEnabled || IsSupportedDifficulty(difficulty));
        }

        public static bool CanStartConfiguredMatch(MatchState state, bool difficultyLocked, BotDifficulty difficulty)
        {
            return CanStartConfiguredMatch(state, difficultyLocked, true, difficulty);
        }

        public static bool CanPauseMatch(MatchState state, float matchTimeRemaining)
        {
            return state == MatchState.Playing && matchTimeRemaining > 0f;
        }

        public static bool CanResumeMatch(MatchState state)
        {
            return state == MatchState.Paused;
        }

        public static bool CanStartRematch(MatchState state)
        {
            return state == MatchState.Final;
        }

        public static bool ShouldAdvancePausedTimer(MatchState state)
        {
            return state != MatchState.Paused;
        }

        public static BotDifficulty ResolveBotDifficulty(
            ParticipantTeam participantTeam,
            ParticipantTeam localTeam,
            BotDifficulty lockedEnemyDifficulty)
        {
            if ((participantTeam != ParticipantTeam.Blue && participantTeam != ParticipantTeam.Red) ||
                (localTeam != ParticipantTeam.Blue && localTeam != ParticipantTeam.Red) ||
                participantTeam == localTeam ||
                !IsSupportedDifficulty(lockedEnemyDifficulty))
            {
                return BotDifficulty.Medium;
            }

            return lockedEnemyDifficulty;
        }

        public static int CountdownNumber(MatchState state, float phaseRemaining)
        {
            if (state != MatchState.OpeningCountdown && state != MatchState.KickoffCountdown)
            {
                return 0;
            }

            var number = (int)Math.Ceiling(Math.Max(phaseRemaining, 0f));
            return Math.Max(1, Math.Min(3, number));
        }

        /// <summary>Maps defending goal to scoring team; teams are globally Blue/Red.</summary>
        public static ParticipantTeam OppositeTeam(ParticipantTeam team)
        {
            return team == ParticipantTeam.Blue ? ParticipantTeam.Red : ParticipantTeam.Blue;
        }

        /// <summary>Resolves regular, own, or unattributed goal without mutating aggregate state.</summary>
        public static MatchGoalDelta ResolveGoal(
            MatchState state,
            ParticipantTeam defendingTeam,
            bool hasLastTouch,
            ParticipantTeam lastTouchTeam,
            int lastTouchSlotId)
        {
            if (state != MatchState.Playing)
            {
                return new MatchGoalDelta(false, OppositeTeam(defendingTeam), false, -1, -1, 0, 0);
            }

            var scoringTeam = OppositeTeam(defendingTeam);
            var ownGoal = hasLastTouch && lastTouchTeam == defendingTeam;
            var scorerSlotId = hasLastTouch && !ownGoal && lastTouchTeam == scoringTeam ? lastTouchSlotId : -1;
            var responsibleSlotId = ownGoal ? lastTouchSlotId : -1;
            return new MatchGoalDelta(
                true,
                scoringTeam,
                ownGoal,
                scorerSlotId,
                responsibleSlotId,
                scoringTeam == ParticipantTeam.Blue ? 1 : 0,
                scoringTeam == ParticipantTeam.Red ? 1 : 0);
        }

        /// <summary>Overload for callers with no physical touch.</summary>
        public static MatchGoalDelta ResolveGoal(MatchState state, ParticipantTeam defendingTeam)
        {
            return ResolveGoal(state, defendingTeam, false, default(ParticipantTeam), -1);
        }

        /// <summary>Resolves one accepted death. Unknown/friendly killers grant no frag.</summary>
        public static MatchDeathDelta ResolveDeath(
            MatchState state,
            int victimSlotId,
            ParticipantTeam victimTeam,
            int killerSlotId,
            ParticipantTeam? killerTeam,
            ParticipantDeathCause cause)
        {
            if (state != MatchState.Playing || victimSlotId < 0)
            {
                return new MatchDeathDelta(false, victimSlotId, killerSlotId, 0, 0, 0);
            }

            var victimFragsDelta = 0;
            var killerFragsDelta = 0;
            if (cause == ParticipantDeathCause.Self || cause == ParticipantDeathCause.Arena || killerSlotId == victimSlotId)
            {
                victimFragsDelta = -1;
            }
            else if (killerSlotId >= 0 && killerTeam.HasValue && killerTeam.Value != victimTeam)
            {
                killerFragsDelta = 1;
            }

            return new MatchDeathDelta(true, victimSlotId, killerSlotId, 1, victimFragsDelta, killerFragsDelta);
        }

        public static MatchOutcome ResolveOutcome(int blueGoals, int redGoals, int blueTeamFrags, int redTeamFrags, out MatchDecisionRule decisionRule)
        {
            if (blueGoals > redGoals)
            {
                decisionRule = MatchDecisionRule.Goals;
                return MatchOutcome.BlueWin;
            }
            if (redGoals > blueGoals)
            {
                decisionRule = MatchDecisionRule.Goals;
                return MatchOutcome.RedWin;
            }
            if (blueTeamFrags > redTeamFrags)
            {
                decisionRule = MatchDecisionRule.TeamFrags;
                return MatchOutcome.BlueWin;
            }
            if (redTeamFrags > blueTeamFrags)
            {
                decisionRule = MatchDecisionRule.TeamFrags;
                return MatchOutcome.RedWin;
            }

            decisionRule = MatchDecisionRule.Draw;
            return MatchOutcome.Draw;
        }

        public static MatchOutcome ResolveOutcome(int blueGoals, int redGoals, int blueTeamFrags, int redTeamFrags)
        {
            MatchDecisionRule ignored;
            return ResolveOutcome(blueGoals, redGoals, blueTeamFrags, redTeamFrags, out ignored);
        }

        /// <summary>Legacy reset result. New aggregate flow enters a countdown after owner reset.</summary>
        public static MatchState CompleteReset()
        {
            return MatchState.Playing;
        }

        private static float ClampRemaining(float remaining, float delta)
        {
            return Math.Max(Math.Max(remaining, 0f) - Math.Max(delta, 0f), 0f);
        }
    }
}
