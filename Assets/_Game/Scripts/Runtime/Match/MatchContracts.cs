using System;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Match
{
    /// <summary>Reason for a coordinated match-owned reset.</summary>
    public enum MatchResetReason
    {
        MatchStart,
        Goal,
        Rematch,
        Manual
    }

    /// <summary>Final match result. InProgress is used until the clock expires.</summary>
    public enum MatchOutcome
    {
        InProgress,
        BlueWin,
        RedWin,
        Draw
    }

    /// <summary>Rule that decided a finished match.</summary>
    public enum MatchDecisionRule
    {
        None,
        Goals,
        TeamFrags,
        Draw
    }

    /// <summary>Immutable participant score row owned by MatchController.</summary>
    public readonly struct MatchParticipantStats
    {
        public MatchParticipantStats(int slotId, string displayName, ParticipantTeam team, int goals, int frags, int deaths)
        {
            SlotId = slotId;
            DisplayName = displayName ?? string.Empty;
            Team = team;
            Goals = goals;
            Frags = frags;
            Deaths = deaths;
        }

        public int SlotId { get; }
        public int Id => SlotId;
        public string DisplayName { get; }
        public ParticipantTeam Team { get; }
        public int Goals { get; }
        public int GoalCount => Goals;
        public int Frags { get; }
        public int Deaths { get; }

        public MatchParticipantStats WithDelta(int goalsDelta, int fragsDelta, int deathsDelta)
        {
            return new MatchParticipantStats(SlotId, DisplayName, Team, Goals + goalsDelta, Frags + fragsDelta, Deaths + deathsDelta);
        }
    }

    /// <summary>Immutable summary captured at goal time for summary/HUD consumers.</summary>
    public readonly struct MatchGoalSummary
    {
        public MatchGoalSummary(
            ParticipantTeam scoringTeam,
            ParticipantTeam defendingTeam,
            bool ownGoal,
            int scorerSlotId,
            string scorerName,
            int responsibleSlotId,
            string responsibleName,
            int blueGoals,
            int redGoals,
            int blueTeamFrags,
            int redTeamFrags)
        {
            ScoringTeam = scoringTeam;
            DefendingTeam = defendingTeam;
            IsOwnGoal = ownGoal;
            ScorerSlotId = scorerSlotId;
            ScorerName = scorerName ?? string.Empty;
            ResponsibleSlotId = responsibleSlotId;
            ResponsibleName = responsibleName ?? string.Empty;
            BlueGoals = blueGoals;
            RedGoals = redGoals;
            BlueTeamFrags = blueTeamFrags;
            RedTeamFrags = redTeamFrags;
        }

        public ParticipantTeam ScoringTeam { get; }
        public ParticipantTeam DefendingTeam { get; }
        public bool IsOwnGoal { get; }
        public bool HasScorer => ScorerSlotId >= 0;
        public int ScorerSlotId { get; }
        public string ScorerName { get; }
        public bool HasResponsibleParticipant => ResponsibleSlotId >= 0;
        public int ResponsibleSlotId { get; }
        public string ResponsibleName { get; }
        public int BlueGoals { get; }
        public int RedGoals { get; }
        public int BlueTeamFrags { get; }
        public int RedTeamFrags { get; }

        // Short aliases keep the read contract convenient for summary consumers.
        public ParticipantTeam Team => ScoringTeam;
        public int Goals => ScoringTeam == ParticipantTeam.Blue ? BlueGoals : RedGoals;
    }

    /// <summary>Immutable flow transition published by MatchController.</summary>
    public readonly struct MatchFlowChange
    {
        public MatchFlowChange(MatchRules.MatchState previous, MatchRules.MatchState current)
        {
            Previous = previous;
            Current = current;
        }

        public MatchRules.MatchState Previous { get; }
        public MatchRules.MatchState Current { get; }
        public MatchRules.MatchState State => Current;
    }

    /// <summary>Pure clock result; no Unity or mutable match state.</summary>
    public readonly struct MatchClockAdvance
    {
        public MatchClockAdvance(float remaining, float elapsed, bool reachedZero)
        {
            Remaining = remaining;
            Elapsed = elapsed;
            ReachedZero = reachedZero;
        }

        public float Remaining { get; }
        public float Elapsed { get; }
        public bool ReachedZero { get; }
    }

    /// <summary>Pure goal scoring delta used by MatchController.</summary>
    public readonly struct MatchGoalDelta
    {
        public MatchGoalDelta(
            bool accepted,
            ParticipantTeam scoringTeam,
            bool ownGoal,
            int scorerSlotId,
            int responsibleSlotId,
            int blueGoalsDelta,
            int redGoalsDelta)
        {
            Accepted = accepted;
            ScoringTeam = scoringTeam;
            IsOwnGoal = ownGoal;
            ScorerSlotId = scorerSlotId;
            ResponsibleSlotId = responsibleSlotId;
            BlueGoalsDelta = blueGoalsDelta;
            RedGoalsDelta = redGoalsDelta;
        }

        public bool Accepted { get; }
        public ParticipantTeam ScoringTeam { get; }
        public bool IsOwnGoal { get; }
        public int ScorerSlotId { get; }
        public int ResponsibleSlotId { get; }
        public int BlueGoalsDelta { get; }
        public int RedGoalsDelta { get; }
    }

    /// <summary>Pure death scoring delta used by MatchController.</summary>
    public readonly struct MatchDeathDelta
    {
        public MatchDeathDelta(bool accepted, int victimSlotId, int killerSlotId, int victimDeathsDelta, int victimFragsDelta, int killerFragsDelta)
        {
            Accepted = accepted;
            VictimSlotId = victimSlotId;
            KillerSlotId = killerSlotId;
            VictimDeathsDelta = victimDeathsDelta;
            VictimFragsDelta = victimFragsDelta;
            KillerFragsDelta = killerFragsDelta;
        }

        public bool Accepted { get; }
        public int VictimSlotId { get; }
        public int KillerSlotId { get; }
        public int VictimDeathsDelta { get; }
        public int VictimFragsDelta { get; }
        public int KillerFragsDelta { get; }
    }
}
