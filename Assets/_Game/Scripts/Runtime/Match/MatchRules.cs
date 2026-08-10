namespace RocketFooxball.Runtime.Match
{
    /// <summary>Pure score and timer transitions. MatchController owns Unity side effects.</summary>
    public static class MatchRules
    {
        public enum MatchState
        {
            Playing,
            GoalFreeze,
            Reset
        }

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

            return new GoalTransition(MatchState.GoalFreeze, System.Math.Max(goalFreezeDuration, 0.1f), northScore, southScore, true);
        }

        public static float AdvanceGoalFreeze(float freezeRemaining, float unscaledDeltaTime)
        {
            return System.Math.Max(freezeRemaining - System.Math.Max(unscaledDeltaTime, 0f), 0f);
        }

        public static MatchState CompleteReset()
        {
            return MatchState.Playing;
        }
    }
}
