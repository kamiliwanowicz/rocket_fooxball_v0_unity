using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Match
{
    /// <summary>Single owner for match state, roster-wide gates, reset timing, collisions, and spectator selection.</summary>
    [MovedFrom("RocketFooxball")]
    public sealed class MatchController : MonoBehaviour
    {
        [MovedFrom(false, "RocketFooxball", "RocketFooxball.Runtime", "MatchController/MatchState")]
        public enum MatchState
        {
            Playing,
            GoalFreeze,
            Reset
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

        [Header("Reset")]
        [SerializeField, Min(0.1f)] private float goalFreezeDuration = 5f;
        [SerializeField] private Vector3 ballResetPosition = new Vector3(0f, 2.16f, 0f);
        [SerializeField] private Vector3 resetLookTarget = Vector3.zero;

        private MatchRules.MatchState state = MatchRules.MatchState.Playing;
        private float freezeRemaining;
        private int northScore;
        private int southScore;

        public MatchState State => (MatchState)state;
        public float FreezeRemaining => Mathf.Max(freezeRemaining, 0f);
        public int NorthScore => northScore;
        public int SouthScore => southScore;
        public bool GameplayEnabled => state == MatchRules.MatchState.Playing;
        public IReadOnlyList<ParticipantState> Participants => participants;
        public ParticipantState LocalParticipant => localParticipant;
        public ParticipantSpawnSet SpawnSet => spawnSet;
        public BallMotor Ball => ball;

        private void Awake()
        {
            ValidateComposition();
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
            if (state != MatchRules.MatchState.GoalFreeze)
            {
                return;
            }

            freezeRemaining = MatchRules.AdvanceGoalFreeze(freezeRemaining, Time.unscaledDeltaTime);
            if (freezeRemaining <= 0f)
            {
                ResetMatch();
            }
        }

        private void OnGoalCrossed(GoalTrigger goal)
        {
            if (goal == null)
            {
                return;
            }

            var transition = MatchRules.BeginGoal(state, northScore, southScore, goal.Side, goalFreezeDuration);
            if (!transition.EnteredGoalFreeze)
            {
                return;
            }

            state = transition.State;
            freezeRemaining = transition.FreezeRemaining;
            northScore = transition.NorthScore;
            southScore = transition.SouthScore;
            ApplyGameplayGate(false);
            DestroyAllProjectiles();
            GetCameraFeedback()?.BeginGoalCelebration(freezeRemaining);
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

        /// <summary>Immediate reset command; normal flow calls after unscaled freeze.</summary>
        public void ResetMatch()
        {
            state = MatchRules.MatchState.Reset;
            ball?.ResetState(ballResetPosition, Quaternion.identity);

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

            GetCameraFeedback()?.ResetFeedback();
            northGoal?.Rearm();
            southGoal?.Rearm();
            DestroyAllProjectiles();
            freezeRemaining = 0f;
            state = MatchRules.CompleteReset();
            ApplyGameplayGate(true);
            ReconcileParticipantCollisions();
        }

        /// <summary>Clears score and resets current frame without changing gameplay contract.</summary>
        public void ResetMatchAndScore()
        {
            northScore = 0;
            southScore = 0;
            ResetMatch();
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
