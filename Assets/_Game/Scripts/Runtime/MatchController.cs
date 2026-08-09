using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Single owner for score, goal freeze, gameplay gate, celebration camera, and reset timing.</summary>
    public sealed class MatchController : MonoBehaviour
    {
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

        private MatchState state = MatchState.Playing;
        private float freezeRemaining;
        private int northScore;
        private int southScore;

        public MatchState State => state;
        public float FreezeRemaining => Mathf.Max(freezeRemaining, 0f);
        public int NorthScore => northScore;
        public int SouthScore => southScore;
        public bool GameplayEnabled => state == MatchState.Playing;

        private void Awake()
        {
            CacheReferences();
            if (northGoal != null)
            {
                northGoal.SetMatch(this);
            }
            if (southGoal != null)
            {
                southGoal.SetMatch(this);
            }
        }

        private void Update()
        {
            if (state != MatchState.GoalFreeze)
            {
                return;
            }

            freezeRemaining = Mathf.Max(freezeRemaining - Time.unscaledDeltaTime, 0f);
            if (freezeRemaining <= 0f)
            {
                ResetMatch();
            }
        }

        /// <summary>Receives one goal event and starts unscaled five-second freeze.</summary>
        public void NotifyGoal(GoalTrigger goal)
        {
            if (goal != null)
            {
                NotifyGoal(goal.Side);
            }
        }

        /// <summary>Scores opposing side; own goals remain valid physics outcomes.</summary>
        public void NotifyGoal(GoalTrigger.GoalSide goalSide)
        {
            if (state != MatchState.Playing)
            {
                return;
            }

            if (goalSide == GoalTrigger.GoalSide.North)
            {
                southScore++;
            }
            else
            {
                northScore++;
            }

            state = MatchState.GoalFreeze;
            freezeRemaining = Mathf.Max(goalFreezeDuration, 0.1f);
            SetGameplayEnabled(false);
            launcher?.DestroyAllProjectiles();
            cameraFeedback?.BeginGoalCelebration(freezeRemaining);
        }

        /// <summary>Compatibility alias for goal owners.</summary>
        public void RegisterGoal(GoalTrigger.GoalSide goalSide)
        {
            NotifyGoal(goalSide);
        }

        /// <summary>Explicit gameplay gate used by external reset tooling.</summary>
        public void SetGameplayEnabled(bool enabled)
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
            cameraFeedback?.EndGoalCelebration();
            state = MatchState.Reset;
            SetGameplayEnabled(false);
            launcher?.DestroyAllProjectiles();

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
            playerLook?.ResetAim(lookDirection);
            cameraFeedback?.ResetFeedback();
            launcher?.ResetState();
            kick?.ResetState();
            northGoal?.Rearm();
            southGoal?.Rearm();

            if (input != null)
            {
                input.ResetInputState();
            }
            SetGameplayEnabled(true);
            freezeRemaining = 0f;
            state = MatchState.Playing;
        }

        /// <summary>Clears score and resets current frame without changing gameplay contract.</summary>
        public void ResetMatchAndScore()
        {
            northScore = 0;
            southScore = 0;
            ResetMatch();
        }

        private void CacheReferences()
        {
            if (input == null && player != null)
            {
                input = player.GetComponent<PlayerInputReader>();
            }
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerMotor>();
            }
            if (input == null && player != null)
            {
                input = player.GetComponent<PlayerInputReader>();
            }
            if (playerLook == null && player != null)
            {
                playerLook = player.GetComponent<PlayerLook>();
            }
            if (cameraFeedback == null && player != null)
            {
                cameraFeedback = player.GetComponent<PlayerCameraFeedback>();
            }
            if (ball == null)
            {
                ball = FindAnyObjectByType<BallMotor>();
            }
            if (launcher == null && player != null)
            {
                launcher = player.GetComponent<RocketLauncher>();
            }
            if (kick == null && player != null)
            {
                kick = player.GetComponent<BallKick>();
            }
            if (northGoal == null || southGoal == null)
            {
                var goals = FindObjectsByType<GoalTrigger>();
                for (var i = 0; i < goals.Length; i++)
                {
                    if (goals[i].Side == GoalTrigger.GoalSide.North)
                    {
                        northGoal = goals[i];
                    }
                    else
                    {
                        southGoal = goals[i];
                    }
                }
            }
        }
    }
}
