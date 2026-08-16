using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Match
{
    /// <summary>Single mutable owner for match flow, clock, score, stats, gates, and coordinated reset.</summary>
    [MovedFrom("RocketFooxball")]
    public sealed class MatchController : MonoBehaviour
    {
        [MovedFrom(false, "RocketFooxball", "RocketFooxball.Runtime", "MatchController/MatchState")]
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

        [Header("Roster")]
        [SerializeField] private ParticipantState[] participants = new ParticipantState[6];
        [SerializeField] private ParticipantState localParticipant;
        [SerializeField] private ParticipantSpawnSet spawnSet;

        [Header("Match Owners")]
        [SerializeField] private BallMotor ball;
        [SerializeField] private PlayerCameraFeedback cameraFeedback;
        [SerializeField] private GoalTrigger northGoal;
        [SerializeField] private GoalTrigger southGoal;

        [Header("Match Timing")]
        [SerializeField, Min(0.1f)] private float matchDuration = 300f;
        [FormerlySerializedAs("goalFreezeDuration")]
        [SerializeField, Min(0f)] private float goalSummaryDuration = 3f;
        [SerializeField, Min(0f)] private float kickoffCountdownDuration = 3f;

        [Header("Reset")]
        [SerializeField] private Vector3 ballResetPosition = new Vector3(0f, 2.16f, 0f);
        [SerializeField] private Vector3 resetLookTarget = Vector3.zero;

        private MatchRules.MatchState state = MatchRules.MatchState.Setup;
        private float matchTimeRemaining;
        private float phaseRemaining;
        private MatchParticipantStats[] stats = Array.Empty<MatchParticipantStats>();
        private IReadOnlyList<MatchParticipantStats> readOnlyStats = Array.Empty<MatchParticipantStats>();
        private Dictionary<int, int> slotIndices = new Dictionary<int, int>();
        private MatchGoalSummary lastGoalSummary;
        private bool hasLastGoalSummary;
        private MatchOutcome outcome = MatchOutcome.InProgress;
        private MatchDecisionRule decisionRule = MatchDecisionRule.None;
        private bool compositionValid;
        private BotDifficulty selectedEnemyDifficulty = BotDifficulty.Medium;
        private BotDifficulty lockedEnemyDifficulty = BotDifficulty.Medium;
        private bool difficultyLocked;

        public MatchState State => (MatchState)state;
        public bool GameplayEnabled => MatchRules.IsGameplayEnabled(state);
        public float MatchTimeRemaining => Mathf.Max(matchTimeRemaining, 0f);
        public float PhaseRemaining => Mathf.Max(phaseRemaining, 0f);
        public int CountdownNumber => MatchRules.CountdownNumber(state, phaseRemaining);
        public int BlueGoals { get; private set; }
        public int RedGoals { get; private set; }
        public int BlueTeamFrags => SumTeamFrags(ParticipantTeam.Blue);
        public int RedTeamFrags => SumTeamFrags(ParticipantTeam.Red);
        public IReadOnlyList<MatchParticipantStats> ParticipantStats => readOnlyStats;
        public MatchGoalSummary LastGoalSummary => lastGoalSummary;
        public bool HasLastGoalSummary => hasLastGoalSummary;
        public MatchOutcome Outcome => outcome;
        public MatchDecisionRule DecisionRule => decisionRule;
        public BotDifficulty SelectedEnemyDifficulty => selectedEnemyDifficulty;
        public BotDifficulty LockedEnemyDifficulty => lockedEnemyDifficulty;
        public bool DifficultyLocked => difficultyLocked;

        // Compatibility surface used by the existing diagnostics HUD.
        public float FreezeRemaining => state == MatchRules.MatchState.GoalFreeze ? PhaseRemaining : 0f;
        public int NorthScore => RedGoals;
        public int SouthScore => BlueGoals;
        public IReadOnlyList<ParticipantState> Participants => participants;
        public ParticipantState LocalParticipant => localParticipant;
        public ParticipantSpawnSet SpawnSet => spawnSet;
        public BallMotor Ball => ball;

        public event Action<MatchState> FlowChanged;
        public event Action StatsChanged;
        public event Action<MatchResetReason> CoordinatedResetRequested;
        public event Action ExitRequested;
        public event Action<bool> PauseChanged;

        private void Awake()
        {
            matchTimeRemaining = Mathf.Max(matchDuration, 0f);
            phaseRemaining = 0f;
            state = MatchRules.MatchState.Setup;
            selectedEnemyDifficulty = BotDifficulty.Medium;
            lockedEnemyDifficulty = BotDifficulty.Medium;
            difficultyLocked = false;
            compositionValid = ValidateComposition();
            if (!compositionValid)
            {
                return;
            }

            BuildStats();
            ApplyGameplayGate(false);
        }

        private void Start()
        {
            if (!compositionValid)
            {
                return;
            }

            EnterSetup(false);
        }

        private void OnEnable()
        {
            if (northGoal != null)
            {
                northGoal.GoalCrossed += OnGoalCrossed;
            }
            if (southGoal != null)
            {
                southGoal.GoalCrossed += OnGoalCrossed;
            }

            SubscribeParticipants();
            ReconcileParticipantCollisions();
        }

        private void OnDisable()
        {
            if (northGoal != null)
            {
                northGoal.GoalCrossed -= OnGoalCrossed;
            }
            if (southGoal != null)
            {
                southGoal.GoalCrossed -= OnGoalCrossed;
            }

            UnsubscribeParticipants();
        }

        private void Update()
        {
            if (!compositionValid)
            {
                return;
            }

            switch (state)
            {
                case MatchRules.MatchState.Playing:
                {
                    var clock = MatchRules.AdvanceClock(matchTimeRemaining, Time.deltaTime);
                    matchTimeRemaining = clock.Remaining;
                    if (clock.ReachedZero)
                    {
                        EnterFinal();
                    }
                    break;
                }
                case MatchRules.MatchState.GoalFreeze:
                    phaseRemaining = MatchRules.AdvancePhase(phaseRemaining, Time.unscaledDeltaTime);
                    if (phaseRemaining <= 0f)
                    {
                        CompleteGoalSummary();
                    }
                    break;
                case MatchRules.MatchState.OpeningCountdown:
                case MatchRules.MatchState.KickoffCountdown:
                    phaseRemaining = MatchRules.AdvancePhase(phaseRemaining, Time.unscaledDeltaTime);
                    if (phaseRemaining <= 0f)
                    {
                        EnterPlaying();
                    }
                    break;
                case MatchRules.MatchState.Setup:
                case MatchRules.MatchState.Paused:
                    break;
            }
        }

        private void OnGoalCrossed(GoalTrigger goal)
        {
            if (goal == null || state != MatchRules.MatchState.Playing || matchTimeRemaining <= 0f)
            {
                return;
            }

            var touch = ball != null ? ball.LastTouchParticipant : null;
            var hasTouch = IsRosterParticipant(touch);
            var delta = MatchRules.ResolveGoal(
                state,
                goal.DefendingTeam,
                hasTouch,
                hasTouch ? touch.Team : default(ParticipantTeam),
                hasTouch ? touch.SlotId : -1);
            if (!delta.Accepted)
            {
                return;
            }

            BlueGoals += delta.BlueGoalsDelta;
            RedGoals += delta.RedGoalsDelta;
            if (delta.ScorerSlotId >= 0)
            {
                AddStatsDelta(delta.ScorerSlotId, 1, 0, 0);
            }

            var scorerName = delta.ScorerSlotId >= 0 && TryGetParticipantStats(delta.ScorerSlotId, out var scorerStats)
                ? scorerStats.DisplayName
                : string.Empty;
            var responsibleName = delta.ResponsibleSlotId >= 0 && TryGetParticipantStats(delta.ResponsibleSlotId, out var responsibleStats)
                ? responsibleStats.DisplayName
                : string.Empty;
            lastGoalSummary = new MatchGoalSummary(
                delta.ScoringTeam,
                goal.DefendingTeam,
                delta.IsOwnGoal,
                delta.ScorerSlotId,
                scorerName,
                delta.ResponsibleSlotId,
                responsibleName,
                BlueGoals,
                RedGoals,
                BlueTeamFrags,
                RedTeamFrags);
            hasLastGoalSummary = true;
            StatsChanged?.Invoke();

            SetState(MatchRules.MatchState.GoalFreeze);
            phaseRemaining = Mathf.Max(goalSummaryDuration, 0f);
            ApplyGameplayGate(false);
            DestroyAllProjectiles();
            GetCameraFeedback()?.BeginGoalCelebration(phaseRemaining);
        }

        private void CompleteGoalSummary()
        {
            PerformCoordinatedReset(MatchResetReason.Goal);
            SetCountdown(MatchRules.MatchState.KickoffCountdown);
        }

        private void EnterPlaying()
        {
            phaseRemaining = 0f;
            SetState(MatchRules.MatchState.Playing);
            ApplyGameplayGate(true);
            ReconcileParticipantCollisions();
        }

        private void EnterFinal()
        {
            if (state == MatchRules.MatchState.Final)
            {
                return;
            }

            matchTimeRemaining = 0f;
            phaseRemaining = 0f;
            outcome = MatchRules.ResolveOutcome(BlueGoals, RedGoals, BlueTeamFrags, RedTeamFrags, out decisionRule);
            SetState(MatchRules.MatchState.Final);
            ApplyGameplayGate(false);
            DestroyAllProjectiles();
            StatsChanged?.Invoke();
        }

        private void EnterSetup(bool clearPreviousMatch)
        {
            selectedEnemyDifficulty = BotDifficulty.Medium;
            lockedEnemyDifficulty = BotDifficulty.Medium;
            difficultyLocked = false;
            matchTimeRemaining = Mathf.Max(matchDuration, 0f);
            phaseRemaining = 0f;

            if (clearPreviousMatch)
            {
                BlueGoals = 0;
                RedGoals = 0;
                ClearStats();
                hasLastGoalSummary = false;
                lastGoalSummary = default(MatchGoalSummary);
                outcome = MatchOutcome.InProgress;
                decisionRule = MatchDecisionRule.None;
            }

            SetState(MatchRules.MatchState.Setup);
            ApplyGameplayGate(false);
        }

        private void BeginNewMatch(MatchResetReason reason)
        {
            matchTimeRemaining = Mathf.Max(matchDuration, 0f);
            BlueGoals = 0;
            RedGoals = 0;
            ClearStats();
            hasLastGoalSummary = false;
            lastGoalSummary = default(MatchGoalSummary);
            outcome = MatchOutcome.InProgress;
            decisionRule = MatchDecisionRule.None;
            PerformCoordinatedReset(reason);
            SetCountdown(MatchRules.MatchState.OpeningCountdown);
            StatsChanged?.Invoke();
        }

        private void SetCountdown(MatchRules.MatchState countdownState)
        {
            SetState(countdownState);
            phaseRemaining = Mathf.Max(kickoffCountdownDuration, 0f);
            ApplyGameplayGate(false);
        }

        private void SetState(MatchRules.MatchState next, bool notifyWhenUnchanged = false)
        {
            if (state == next)
            {
                if (notifyWhenUnchanged)
                {
                    FlowChanged?.Invoke((MatchState)next);
                }
                return;
            }

            state = next;
            FlowChanged?.Invoke((MatchState)next);
        }

        public bool TrySelectEnemyDifficulty(BotDifficulty difficulty)
        {
            if (!compositionValid || !MatchRules.CanSelectEnemyDifficulty(state, difficultyLocked, difficulty))
            {
                return false;
            }

            selectedEnemyDifficulty = difficulty;
            return true;
        }

        public bool TryStartConfiguredMatch()
        {
            if (!compositionValid || !MatchRules.CanStartConfiguredMatch(state, difficultyLocked, selectedEnemyDifficulty))
            {
                return false;
            }

            lockedEnemyDifficulty = selectedEnemyDifficulty;
            difficultyLocked = true;
            BeginNewMatch(MatchResetReason.MatchStart);
            return true;
        }

        public bool TryPauseMatch()
        {
            if (!compositionValid || !MatchRules.CanPauseMatch(state, matchTimeRemaining))
            {
                return false;
            }

            SetState(MatchRules.MatchState.Paused);
            SetGoalPollingEnabled(false);
            if (participants != null)
            {
                for (var i = 0; i < participants.Length; i++)
                {
                    participants[i]?.SetMatchPaused(true);
                }
            }
            ball?.SetPaused(true);
            PauseChanged?.Invoke(true);
            return true;
        }

        public bool TryResumeMatch()
        {
            if (!compositionValid || !MatchRules.CanResumeMatch(state))
            {
                return false;
            }

            SetGoalPollingEnabled(true);
            ball?.SetPaused(false);
            if (participants != null)
            {
                for (var i = 0; i < participants.Length; i++)
                {
                    participants[i]?.SetMatchPaused(false);
                }
            }
            SetState(MatchRules.MatchState.Playing);
            ReconcileParticipantCollisions();
            PauseChanged?.Invoke(false);
            return true;
        }

        public BotDifficulty GetBotDifficulty(ParticipantTeam participantTeam)
        {
            if (!difficultyLocked || localParticipant == null)
            {
                return BotDifficulty.Medium;
            }

            return MatchRules.ResolveBotDifficulty(participantTeam, localParticipant.Team, lockedEnemyDifficulty);
        }

        /// <summary>Manual coordinated reset followed by kickoff countdown.</summary>
        public void ResetMatch()
        {
            if (!compositionValid || state == MatchRules.MatchState.Setup || state == MatchRules.MatchState.Paused || state == MatchRules.MatchState.Final)
            {
                return;
            }

            PerformCoordinatedReset(MatchResetReason.Manual);
            SetCountdown(MatchRules.MatchState.KickoffCountdown);
        }

        /// <summary>Starts a fresh match from active legacy flow states; setup, pause, and final are explicit flows.</summary>
        public void ResetMatchAndScore()
        {
            if (!compositionValid || state == MatchRules.MatchState.Setup || state == MatchRules.MatchState.Paused || state == MatchRules.MatchState.Final)
            {
                return;
            }

            BeginNewMatch(MatchResetReason.Manual);
        }

        public bool TryStartRematch()
        {
            if (!compositionValid || !MatchRules.CanStartRematch(state))
            {
                return false;
            }

            EnterSetup(true);
            return true;
        }

        public bool RequestExit()
        {
            if (state != MatchRules.MatchState.Final)
            {
                return false;
            }

            ExitRequested?.Invoke();
            Application.Quit();
            return true;
        }

        public bool TryGetParticipantStats(int slotId, out MatchParticipantStats participantStats)
        {
            if (slotIndices.TryGetValue(slotId, out var index) && index >= 0 && index < stats.Length)
            {
                participantStats = stats[index];
                return true;
            }

            participantStats = default(MatchParticipantStats);
            return false;
        }

        private void PerformCoordinatedReset(MatchResetReason reason)
        {
            ClearMatchPause();
            SetState(MatchRules.MatchState.Reset, true);
            phaseRemaining = 0f;
            ApplyGameplayGate(false);
            DestroyAllProjectiles();

            if (participants != null)
            {
                for (var i = 0; i < participants.Length; i++)
                {
                    var participant = participants[i];
                    if (participant == null)
                    {
                        continue;
                    }

                    var spawn = spawnSet != null ? spawnSet.GetKickoffSpawn(participant, i) : null;
                    if (spawn != null)
                    {
                        var lookDirection = resetLookTarget - spawn.position;
                        lookDirection.y = 0f;
                        if (lookDirection.sqrMagnitude <= 0.000001f)
                        {
                            lookDirection = spawn.forward;
                        }
                        participant.ResetForKickoff(spawn.position, Quaternion.LookRotation(lookDirection.normalized, Vector3.up));
                    }
                    else
                    {
                        participant.ResetForKickoff();
                    }
                }
            }

            ball?.ResetState(ballResetPosition, Quaternion.identity);
            GetCameraFeedback()?.ResetFeedback();
            northGoal?.Rearm();
            southGoal?.Rearm();
            CoordinatedResetRequested?.Invoke(reason);
            ReconcileParticipantCollisions();
        }

        private void ApplyGameplayGate(bool enabled)
        {
            if (participants != null)
            {
                for (var i = 0; i < participants.Length; i++)
                {
                    participants[i]?.SetMatchSimulationEnabled(enabled);
                }
            }
            ball?.SetSimulationEnabled(enabled);
        }

        private void ClearMatchPause()
        {
            SetGoalPollingEnabled(true);
            if (participants != null)
            {
                for (var i = 0; i < participants.Length; i++)
                {
                    participants[i]?.SetMatchPaused(false);
                }
            }
            ball?.SetPaused(false);
        }

        private void SetGoalPollingEnabled(bool enabled)
        {
            if (northGoal != null)
            {
                northGoal.enabled = enabled;
            }
            if (southGoal != null)
            {
                southGoal.enabled = enabled;
            }
        }

        private void DestroyAllProjectiles()
        {
            if (participants == null)
            {
                return;
            }

            for (var i = 0; i < participants.Length; i++)
            {
                participants[i]?.Launcher?.DestroyAllProjectiles();
            }
        }

        private void SubscribeParticipants()
        {
            if (participants == null)
            {
                return;
            }

            for (var i = 0; i < participants.Length; i++)
            {
                var participant = participants[i];
                if (participant == null)
                {
                    continue;
                }

                participant.Died += OnParticipantDied;
                participant.RespawnRequested += OnRespawnRequested;
                participant.LifecycleChanged += OnParticipantLifecycleChanged;
                participant.CollisionStateChanged += OnParticipantCollisionStateChanged;
            }
        }

        private void UnsubscribeParticipants()
        {
            if (participants == null)
            {
                return;
            }

            for (var i = 0; i < participants.Length; i++)
            {
                var participant = participants[i];
                if (participant == null)
                {
                    continue;
                }

                participant.Died -= OnParticipantDied;
                participant.RespawnRequested -= OnRespawnRequested;
                participant.LifecycleChanged -= OnParticipantLifecycleChanged;
                participant.CollisionStateChanged -= OnParticipantCollisionStateChanged;
            }
        }

        private void OnParticipantDied(ParticipantDeathEvent death)
        {
            ReconcileParticipantCollisions();
            if (death.Victim == localParticipant)
            {
                SetLocalSpectatorTarget();
            }

            if (state != MatchRules.MatchState.Playing || death.Victim == null || !IsRosterParticipant(death.Victim))
            {
                return;
            }

            var killer = death.Killer;
            var killerTeam = IsRosterParticipant(killer) ? (ParticipantTeam?)killer.Team : null;
            var delta = MatchRules.ResolveDeath(
                state,
                death.Victim.SlotId,
                death.Victim.Team,
                IsRosterParticipant(killer) ? killer.SlotId : -1,
                killerTeam,
                death.Cause);
            if (!delta.Accepted)
            {
                return;
            }

            AddStatsDelta(delta.VictimSlotId, 0, delta.VictimFragsDelta, delta.VictimDeathsDelta);
            if (delta.KillerFragsDelta != 0)
            {
                AddStatsDelta(delta.KillerSlotId, 0, delta.KillerFragsDelta, 0);
            }
            StatsChanged?.Invoke();
        }

        private void OnParticipantLifecycleChanged(ParticipantLifecycleEvent _)
        {
            ReconcileParticipantCollisions();
            if (localParticipant != null && localParticipant.IsAlive)
            {
                GetCameraFeedback()?.ExitSpectator();
            }
            else if (localParticipant != null && localParticipant.IsDead)
            {
                SetLocalSpectatorTarget();
            }
        }

        private void OnParticipantCollisionStateChanged(ParticipantState _)
        {
            ReconcileParticipantCollisions();
        }

        private void OnRespawnRequested(ParticipantState participant)
        {
            if (participant == null || state != MatchRules.MatchState.Playing || spawnSet == null)
            {
                return;
            }

            var spawn = spawnSet.SelectSafestSpawn(participant, ball, participants);
            if (spawn == null)
            {
                return;
            }

            var lookDirection = resetLookTarget - spawn.position;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude <= 0.000001f)
            {
                lookDirection = spawn.forward;
            }
            participant.RespawnAt(spawn.position, Quaternion.LookRotation(lookDirection.normalized, Vector3.up));
        }

        private void SetLocalSpectatorTarget()
        {
            var feedback = GetCameraFeedback();
            if (feedback == null)
            {
                return;
            }

            ParticipantState bestAlly = null;
            if (participants != null && localParticipant != null)
            {
                for (var i = 0; i < participants.Length; i++)
                {
                    var candidate = participants[i];
                    if (candidate == null || candidate == localParticipant || !candidate.IsAlive || candidate.Team != localParticipant.Team)
                    {
                        continue;
                    }
                    if (bestAlly == null || candidate.SlotId < bestAlly.SlotId)
                    {
                        bestAlly = candidate;
                    }
                }
            }

            if (bestAlly != null)
            {
                feedback.SetSpectatorTarget(bestAlly.transform);
            }
            else if (ball != null)
            {
                feedback.SetSpectatorWorldTarget(ball.transform);
            }
            else
            {
                feedback.SetSpectatorTarget(null);
            }
        }

        private void ReconcileParticipantCollisions()
        {
            if (participants == null)
            {
                return;
            }

            for (var i = 0; i < participants.Length; i++)
            {
                var first = participants[i];
                var firstController = first != null ? first.CharacterController : null;
                if (firstController == null)
                {
                    continue;
                }

                for (var j = i + 1; j < participants.Length; j++)
                {
                    var second = participants[j];
                    var secondController = second != null ? second.CharacterController : null;
                    if (secondController == null || first == null || second == null)
                    {
                        continue;
                    }

                    var ignore = !first.IsAlive || !second.IsAlive || first.IsImmune || second.IsImmune;
                    UnityEngine.Physics.IgnoreCollision(firstController, secondController, ignore);
                }
            }
        }

        private PlayerCameraFeedback GetCameraFeedback()
        {
            return cameraFeedback != null ? cameraFeedback : localParticipant != null ? localParticipant.CameraFeedback : null;
        }

        private void BuildStats()
        {
            stats = new MatchParticipantStats[participants.Length];
            slotIndices = new Dictionary<int, int>(participants.Length);
            for (var i = 0; i < participants.Length; i++)
            {
                var participant = participants[i];
                stats[i] = new MatchParticipantStats(participant.SlotId, participant.DisplayName, participant.Team, 0, 0, 0);
                slotIndices[participant.SlotId] = i;
            }
            readOnlyStats = Array.AsReadOnly(stats);
        }

        private void ClearStats()
        {
            if (stats == null || participants == null || stats.Length != participants.Length)
            {
                return;
            }

            for (var i = 0; i < stats.Length; i++)
            {
                var participant = participants[i];
                stats[i] = new MatchParticipantStats(participant.SlotId, participant.DisplayName, participant.Team, 0, 0, 0);
            }
        }

        private void AddStatsDelta(int slotId, int goalsDelta, int fragsDelta, int deathsDelta)
        {
            if (!slotIndices.TryGetValue(slotId, out var index) || index < 0 || index >= stats.Length)
            {
                return;
            }

            stats[index] = stats[index].WithDelta(goalsDelta, fragsDelta, deathsDelta);
        }

        private int SumTeamFrags(ParticipantTeam team)
        {
            var total = 0;
            for (var i = 0; i < stats.Length; i++)
            {
                if (stats[i].Team == team)
                {
                    total += stats[i].Frags;
                }
            }
            return total;
        }

        private bool IsRosterParticipant(ParticipantState participant)
        {
            if (participant == null || participants == null)
            {
                return false;
            }

            for (var i = 0; i < participants.Length; i++)
            {
                if (participants[i] == participant)
                {
                    return true;
                }
            }
            return false;
        }

        private bool ValidateComposition()
        {
            if (participants == null || participants.Length != 6 || localParticipant == null || spawnSet == null || ball == null || northGoal == null || southGoal == null)
            {
                Debug.LogError("MatchController requires serialized references: six participants, localParticipant, spawnSet, ball, northGoal, southGoal.", this);
                enabled = false;
                return false;
            }

            var ids = new HashSet<int>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var blueCount = 0;
            var redCount = 0;
            var localCount = 0;
            for (var i = 0; i < participants.Length; i++)
            {
                var participant = participants[i];
                if (participant == null || !ids.Add(participant.SlotId) || !names.Add(participant.DisplayName))
                {
                    Debug.LogError("MatchController requires six non-null participants with unique slot IDs and display names.", this);
                    enabled = false;
                    return false;
                }

                if (participant.Team == ParticipantTeam.Blue)
                {
                    blueCount++;
                }
                else
                {
                    redCount++;
                }
                if (participant.IsLocalParticipant)
                {
                    localCount++;
                }
            }

            if (!Array.Exists(participants, participant => participant == localParticipant) || localCount != 1 || localParticipant.Team != ParticipantTeam.Blue || blueCount != 3 || redCount != 3)
            {
                Debug.LogError("MatchController requires one local Blue participant and exactly three participants per team.", this);
                enabled = false;
                return false;
            }

            return true;
        }
    }
}
