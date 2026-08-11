using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Weapons;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Bridges gameplay state to world/FPS presentation without owning simulation.</summary>
    [MovedFrom("RocketFooxball")]
    public sealed class PlayerPresentation : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BallKick kick;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private RocketLauncher launcher;
        [SerializeField] private Animator worldAnimator;
        [SerializeField] private Animator fpsKickAnimator;
        [SerializeField] private Transform weaponVisual;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private AudioListener audioListener;
        [SerializeField] private ParticipantState participant;
        [SerializeField] private Renderer[] teamTintRenderers;
        [SerializeField] private GameObject blueTeamCue;
        [SerializeField] private GameObject redTeamCue;
        [SerializeField] private GameObject immunityShield;
        [SerializeField] private GameObject blueImmunityShield;
        [SerializeField] private GameObject redImmunityShield;
        [SerializeField] private GameObject worldVisual;
        [SerializeField] private GameObject fpsVisual;
        [SerializeField] private Color blueTeamColor = new Color(0.08f, 0.35f, 1f, 1f);
        [SerializeField] private Color redTeamColor = new Color(1f, 0.12f, 0.1f, 1f);

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int GroundedParameter = Animator.StringToHash("Grounded");
        private static readonly int VerticalSpeedParameter = Animator.StringToHash("VerticalSpeed");
        private static readonly int KickTrigger = Animator.StringToHash("Kick");
        private static readonly Vector3 RecoilOffset = new Vector3(0f, 0.025f, -0.08f);
        private static readonly Vector3 RecoilEuler = new Vector3(-6f, 0f, 1.5f);
        private const float RecoilDuration = 0.16f;

        private Vector3 neutralLocalPosition;
        private Quaternion neutralLocalRotation;
        private float recoilElapsed;
        private bool neutralPoseCached;
        private bool recoilActive;
        private bool kickSubscribed;
        private bool launcherSubscribed;
        private Transform[] worldLayerTransforms;
        private int[] worldLayerValues;

        public ParticipantState Participant => participant;
        public ParticipantTeam Team => participant != null ? participant.Team : ParticipantTeam.Blue;

        private void OnEnable()
        {
            CacheReferences();
            CacheNeutralPose();
            if (participant != null)
            {
                ConfigureSlot(participant);
            }

            if (kick != null && !kickSubscribed)
            {
                kick.KickAttempted += OnKickAttempted;
                kickSubscribed = true;
            }

            if (launcher != null && !launcherSubscribed)
            {
                launcher.RocketLaunched += OnRocketLaunched;
                launcherSubscribed = true;
            }
        }

        private void OnDisable()
        {
            if (kick != null && kickSubscribed)
            {
                kick.KickAttempted -= OnKickAttempted;
                kickSubscribed = false;
            }

            if (launcher != null && launcherSubscribed)
            {
                launcher.RocketLaunched -= OnRocketLaunched;
                launcherSubscribed = false;
            }

            recoilActive = false;
            recoilElapsed = 0f;
            RestoreNeutralPose();
        }

        private void Update()
        {
            if (worldAnimator == null || !worldAnimator.isActiveAndEnabled || motor == null || (participant != null && !participant.IsAlive))
            {
                return;
            }

            worldAnimator.SetFloat(SpeedParameter, motor.HorizontalSpeed);
            worldAnimator.SetBool(GroundedParameter, motor.IsGrounded || motor.HasGroundContact);
            worldAnimator.SetFloat(VerticalSpeedParameter, motor.Velocity.y);
        }

        private void LateUpdate()
        {
            if (!neutralPoseCached || weaponVisual == null)
            {
                return;
            }

            if (!recoilActive)
            {
                RestoreNeutralPose();
                return;
            }

            recoilElapsed += Time.unscaledDeltaTime;
            var normalized = Mathf.Clamp01(recoilElapsed / RecoilDuration);
            var envelope = Mathf.Sin(normalized * Mathf.PI);
            weaponVisual.localPosition = neutralLocalPosition + RecoilOffset * envelope;
            weaponVisual.localRotation = neutralLocalRotation * Quaternion.Euler(RecoilEuler * envelope);

            if (normalized >= 1f)
            {
                recoilActive = false;
                RestoreNeutralPose();
            }
        }

        private void OnKickAttempted()
        {
            if (worldAnimator != null && worldAnimator.isActiveAndEnabled)
            {
                worldAnimator.SetTrigger(KickTrigger);
            }

            if (fpsKickAnimator != null && fpsKickAnimator.isActiveAndEnabled)
            {
                fpsKickAnimator.SetTrigger(KickTrigger);
            }
        }

        private void OnRocketLaunched()
        {
            if (weaponVisual == null)
            {
                return;
            }

            if (!neutralPoseCached)
            {
                CacheNeutralPose();
            }

            // Restart from the exact neutral transform; this prevents rapid-fire
            // events from adding offsets or rotations on top of one another.
            RestoreNeutralPose();
            recoilElapsed = 0f;
            recoilActive = true;
        }

        private void CacheReferences()
        {
            if (kick == null)
            {
                kick = GetComponent<BallKick>();
            }
            if (motor == null)
            {
                motor = GetComponent<PlayerMotor>();
            }
            if (launcher == null)
            {
                launcher = GetComponent<RocketLauncher>();
            }
            if (participant == null)
            {
                participant = GetComponent<ParticipantState>();
            }
        }

        /// <summary>Applies serialized team palette and shape cue to this participant.</summary>
        public void ConfigureSlot(ParticipantState owner)
        {
            participant = owner;
            var isBlue = owner == null || owner.Team == ParticipantTeam.Blue;
            if (blueTeamCue != null)
            {
                blueTeamCue.SetActive(isBlue);
            }
            if (redTeamCue != null)
            {
                redTeamCue.SetActive(!isBlue);
            }

            var color = isBlue ? blueTeamColor : redTeamColor;
            if (teamTintRenderers != null)
            {
                for (var i = 0; i < teamTintRenderers.Length; i++)
                {
                    var renderer = teamTintRenderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    var propertyBlock = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(propertyBlock);
                    propertyBlock.SetColor("_BaseColor", color);
                    propertyBlock.SetColor("_Color", color);
                    renderer.SetPropertyBlock(propertyBlock);
                }
            }
        }

        public void ConfigureTeam(ParticipantTeam configuredTeam)
        {
            ConfigureSlot(participant);
            var isBlue = configuredTeam == ParticipantTeam.Blue;
            blueTeamCue?.SetActive(isBlue);
            redTeamCue?.SetActive(!isBlue);
        }

        public void SetAlive(bool alive)
        {
            if (worldVisual != null)
            {
                worldVisual.SetActive(alive);
            }
            if (fpsVisual != null)
            {
                fpsVisual.SetActive(alive && participant != null && participant.IsLocalParticipant);
            }
            if (worldAnimator != null)
            {
                worldAnimator.enabled = alive;
            }
            if (!alive)
            {
                recoilActive = false;
                RestoreNeutralPose();
            }
        }

        public void SetImmune(bool immune)
        {
            if (immunityShield != null)
            {
                immunityShield.SetActive(immune);
            }
            var isBlue = participant == null || participant.Team == ParticipantTeam.Blue;
            blueImmunityShield?.SetActive(immune && isBlue);
            redImmunityShield?.SetActive(immune && !isBlue);
        }

        public void SetLocalMode(bool local)
        {
            if (gameplayCamera != null)
            {
                gameplayCamera.enabled = local;
            }
            if (audioListener != null)
            {
                audioListener.enabled = local;
            }
            if (weaponVisual != null)
            {
                weaponVisual.gameObject.SetActive(local);
            }
            if (worldVisual == null)
            {
                return;
            }

            CacheWorldLayers();
            var hiddenLayer = LayerMask.NameToLayer("LocalPlayerHidden");
            for (var i = 0; i < worldLayerTransforms.Length; i++)
            {
                if (worldLayerTransforms[i] == null)
                {
                    continue;
                }
                worldLayerTransforms[i].gameObject.layer = local && hiddenLayer >= 0 ? hiddenLayer : worldLayerValues[i];
            }
        }

        private void CacheWorldLayers()
        {
            if (worldLayerTransforms != null || worldVisual == null)
            {
                return;
            }

            worldLayerTransforms = worldVisual.GetComponentsInChildren<Transform>(true);
            worldLayerValues = new int[worldLayerTransforms.Length];
            for (var i = 0; i < worldLayerTransforms.Length; i++)
            {
                worldLayerValues[i] = worldLayerTransforms[i] != null ? worldLayerTransforms[i].gameObject.layer : 0;
            }
        }

        private void CacheNeutralPose()
        {
            if (weaponVisual == null)
            {
                neutralPoseCached = false;
                return;
            }

            neutralLocalPosition = weaponVisual.localPosition;
            neutralLocalRotation = weaponVisual.localRotation;
            neutralPoseCached = true;
        }

        private void RestoreNeutralPose()
        {
            if (!neutralPoseCached || weaponVisual == null)
            {
                return;
            }

            weaponVisual.localPosition = neutralLocalPosition;
            weaponVisual.localRotation = neutralLocalRotation;
        }
    }
}
