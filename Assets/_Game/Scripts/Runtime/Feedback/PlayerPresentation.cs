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
        [SerializeField] private PlayerCameraFeedback cameraFeedback;
        [SerializeField] private RocketLauncher launcher;
        [SerializeField] private ShotgunWeapon shotgun;
        [SerializeField] private Animator worldAnimator;
        [SerializeField] private Animator fpsKickAnimator;
        [SerializeField] private Transform weaponVisual;
        [SerializeField] private Transform fpsShotgunVisual;
        [SerializeField] private Transform worldShotgunVisual;
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
        private const float ShotgunRecoilDuration = 0.10f;
        private const float ShotgunPumpDuration = 0.28f;
        private const float ShotgunReturnDuration = 0.47f;
        private static readonly Vector3 ShotgunRecoilOffset = new Vector3(0f, 0f, -0.07f);
        private static readonly Vector3 ShotgunPumpOffset = new Vector3(0f, 0f, -0.11f);
        private static readonly Vector3 ShotgunRecoilEuler = new Vector3(6f, 0f, 0f);
        private static readonly Vector3 ShotgunPumpEuler = new Vector3(-4f, 0f, 0f);

        private Vector3 neutralLocalPosition;
        private Quaternion neutralLocalRotation;
        private float recoilElapsed;
        private bool neutralPoseCached;
        private bool recoilActive;
        private Vector3 neutralShotgunLocalPosition;
        private Quaternion neutralShotgunLocalRotation;
        private float shotgunCycleElapsed;
        private bool shotgunCycleActive;
        private bool shotgunNeutralPoseCached;
        private bool kickSubscribed;
        private bool launcherSubscribed;
        private bool shotgunSubscribed;
        private bool alive = true;
        private bool localMode;
        private bool shotgunOwned = true;
        private Transform[] worldLayerTransforms;
        private int[] worldLayerValues;

        public ParticipantState Participant => participant;
        public ParticipantTeam Team => participant != null ? participant.Team : ParticipantTeam.Blue;

        private void OnEnable()
        {
            CacheReferences();
            CacheNeutralPose();
            CacheShotgunNeutralPose();
            localMode = participant != null && participant.IsLocalParticipant;
            alive = participant == null || participant.IsAlive;
            RefreshShotgunVisibility();
            if (participant != null)
            {
                ConfigureSlot(participant);
            }

            if (kick != null && !kickSubscribed)
            {
                kick.DashStarted += OnDashStarted;
                kickSubscribed = true;
            }

            if (launcher != null && !launcherSubscribed)
            {
                launcher.RocketLaunched += OnRocketLaunched;
                launcherSubscribed = true;
            }

            if (shotgun != null && !shotgunSubscribed)
            {
                shotgun.ShotFired += OnShotgunFired;
                shotgunSubscribed = true;
            }
        }

        private void OnDisable()
        {
            if (kick != null && kickSubscribed)
            {
                kick.DashStarted -= OnDashStarted;
                kickSubscribed = false;
            }

            if (launcher != null && launcherSubscribed)
            {
                launcher.RocketLaunched -= OnRocketLaunched;
                launcherSubscribed = false;
            }

            if (shotgun != null && shotgunSubscribed)
            {
                shotgun.ShotFired -= OnShotgunFired;
                shotgunSubscribed = false;
            }

            recoilActive = false;
            recoilElapsed = 0f;
            RestoreNeutralPose();
            shotgunCycleActive = false;
            shotgunCycleElapsed = 0f;
            RestoreShotgunNeutralPose();
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
            UpdateShotgunCycle();
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

        private void OnDashStarted()
        {
            if (worldAnimator != null && worldAnimator.isActiveAndEnabled)
            {
                worldAnimator.SetTrigger(KickTrigger);
            }

            if (fpsKickAnimator != null && fpsKickAnimator.isActiveAndEnabled)
            {
                fpsKickAnimator.SetTrigger(KickTrigger);
            }

            cameraFeedback?.RequestDashKickImpulse(1f);
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

        private void OnShotgunFired()
        {
            if (fpsShotgunVisual == null)
            {
                return;
            }

            if (!shotgunNeutralPoseCached)
            {
                CacheShotgunNeutralPose();
            }

            RestoreShotgunNeutralPose();
            shotgunCycleElapsed = 0f;
            shotgunCycleActive = true;
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
            if (cameraFeedback == null)
            {
                cameraFeedback = GetComponent<PlayerCameraFeedback>();
            }
            if (launcher == null)
            {
                launcher = GetComponent<RocketLauncher>();
            }
            if (shotgun == null)
            {
                shotgun = GetComponent<ShotgunWeapon>();
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
            localMode = owner != null && owner.IsLocalParticipant;
            shotgunOwned = owner == null || owner.HasShotgun;
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
            RefreshShotgunVisibility();
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
            this.alive = alive;
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
                shotgunCycleActive = false;
                shotgunCycleElapsed = 0f;
                RestoreShotgunNeutralPose();
            }

            RefreshShotgunVisibility();
        }

        /// <summary>Shows or hides both shotgun presentations without changing the player body.</summary>
        public void SetShotgunOwned(bool owned)
        {
            shotgunOwned = owned;
            if (!owned)
            {
                shotgunCycleActive = false;
                shotgunCycleElapsed = 0f;
                RestoreShotgunNeutralPose();
            }
            RefreshShotgunVisibility();
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
            localMode = local;
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
                RefreshShotgunVisibility();
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

            RefreshShotgunVisibility();
        }

        private void RefreshShotgunVisibility()
        {
            if (fpsShotgunVisual != null)
            {
                fpsShotgunVisual.gameObject.SetActive(localMode && alive && shotgunOwned);
            }
            if (worldShotgunVisual != null)
            {
                worldShotgunVisual.gameObject.SetActive(alive && shotgunOwned);
            }
        }

        private void UpdateShotgunCycle()
        {
            if (!shotgunNeutralPoseCached || fpsShotgunVisual == null)
            {
                return;
            }

            if (!shotgunCycleActive)
            {
                RestoreShotgunNeutralPose();
                return;
            }

            shotgunCycleElapsed += Time.unscaledDeltaTime;
            var recoilPosePosition = neutralShotgunLocalPosition + ShotgunRecoilOffset;
            var recoilPoseRotation = neutralShotgunLocalRotation * Quaternion.Euler(ShotgunRecoilEuler);
            var pumpPosePosition = neutralShotgunLocalPosition + ShotgunPumpOffset;
            var pumpPoseRotation = neutralShotgunLocalRotation * Quaternion.Euler(ShotgunPumpEuler);

            if (shotgunCycleElapsed <= ShotgunRecoilDuration)
            {
                var t = Mathf.Clamp01(shotgunCycleElapsed / ShotgunRecoilDuration);
                fpsShotgunVisual.localPosition = Vector3.Lerp(neutralShotgunLocalPosition, recoilPosePosition, t);
                fpsShotgunVisual.localRotation = Quaternion.Slerp(neutralShotgunLocalRotation, recoilPoseRotation, t);
                return;
            }

            if (shotgunCycleElapsed <= ShotgunRecoilDuration + ShotgunPumpDuration)
            {
                var t = Mathf.Clamp01((shotgunCycleElapsed - ShotgunRecoilDuration) / ShotgunPumpDuration);
                fpsShotgunVisual.localPosition = Vector3.Lerp(recoilPosePosition, pumpPosePosition, t);
                fpsShotgunVisual.localRotation = Quaternion.Slerp(recoilPoseRotation, pumpPoseRotation, t);
                return;
            }

            if (shotgunCycleElapsed <= ShotgunRecoilDuration + ShotgunPumpDuration + ShotgunReturnDuration)
            {
                var t = Mathf.Clamp01((shotgunCycleElapsed - ShotgunRecoilDuration - ShotgunPumpDuration) / ShotgunReturnDuration);
                fpsShotgunVisual.localPosition = Vector3.Lerp(pumpPosePosition, neutralShotgunLocalPosition, t);
                fpsShotgunVisual.localRotation = Quaternion.Slerp(pumpPoseRotation, neutralShotgunLocalRotation, t);
                return;
            }

            shotgunCycleActive = false;
            shotgunCycleElapsed = 0f;
            RestoreShotgunNeutralPose();
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

        private void CacheShotgunNeutralPose()
        {
            if (fpsShotgunVisual == null)
            {
                shotgunNeutralPoseCached = false;
                return;
            }

            neutralShotgunLocalPosition = fpsShotgunVisual.localPosition;
            neutralShotgunLocalRotation = fpsShotgunVisual.localRotation;
            shotgunNeutralPoseCached = true;
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

        private void RestoreShotgunNeutralPose()
        {
            if (!shotgunNeutralPoseCached || fpsShotgunVisual == null)
            {
                return;
            }

            fpsShotgunVisual.localPosition = neutralShotgunLocalPosition;
            fpsShotgunVisual.localRotation = neutralShotgunLocalRotation;
        }
    }
}
