using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Weapons;

namespace RocketFooxball.Runtime.Match
{
    /// <summary>Single owner for score, goal freeze, gameplay gate, celebration camera, and reset timing.</summary>
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

        [Header("Owners")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerMotor player;
        [SerializeField] private PlayerLook playerLook;
        [SerializeField] private PlayerCameraFeedback cameraFeedback;
        [SerializeField] private BallMotor ball;
        [SerializeField] private RocketLauncher launcher;
        [SerializeField] private BallKick kick;
        [SerializeField] private GoalTrigger northGoal;
        [SerializeField] private GoalTrigger southGoal;

        [Header("Reset")]
        [SerializeField, Min(0.1f)] private float goalFreezeDuration = 5f;
        [SerializeField] private Vector3 ballResetPosition = new Vector3(0f, 2.16f, 0f);
        [SerializeField] private Vector3 playerResetPosition = new Vector3(3f, 0f, 0f);
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

        private void Awake()
        {
            if (!ValidateComposition())
            {
                return;
            }
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
            launcher?.DestroyAllProjectiles();
            cameraFeedback?.BeginGoalCelebration(freezeRemaining);
        }

        private void ApplyGameplayGate(bool enabled)
        {
            if (input != null)
            {
                input.SetGameplayInputEnabled(enabled);
            }
            player?.SetSimulationEnabled(enabled);
            ball?.SetSimulationEnabled(enabled);
            launcher?.SetSimulationEnabled(enabled);
            kick?.SetSimulationEnabled(enabled);
        }

        /// <summary>Immediate reset command; normal flow calls after unscaled freeze.</summary>
        public void ResetMatch()
        {
            state = MatchRules.MatchState.Reset;

            if (ball != null)
            {
                ball.ResetState(ballResetPosition, Quaternion.identity);
            }

            var lookDirection = resetLookTarget - playerResetPosition;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude <= 0.000001f)
            {
                lookDirection = Vector3.forward;
            }
            var playerRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            player?.ResetState(playerResetPosition, playerRotation);
            playerLook?.ResetView(lookDirection);
            cameraFeedback?.ResetFeedback();
            launcher?.ResetState();
            kick?.ResetState();
            northGoal?.Rearm();
            southGoal?.Rearm();

            if (input != null)
            {
                input.ResetInputState();
            }
            freezeRemaining = 0f;
            state = MatchRules.CompleteReset();
            ApplyGameplayGate(true);
        }

        /// <summary>Clears score and resets current frame without changing gameplay contract.</summary>
        public void ResetMatchAndScore()
        {
            northScore = 0;
            southScore = 0;
            ResetMatch();
        }

        private bool ValidateComposition()
        {
            if (input == null || player == null || playerLook == null || cameraFeedback == null || ball == null || launcher == null || kick == null || northGoal == null || southGoal == null)
            {
                Debug.LogError("MatchController requires serialized references: input, player, playerLook, cameraFeedback, ball, launcher, kick, northGoal, southGoal.", this);
                enabled = false;
                return false;
            }

            return true;
        }
    }
}
