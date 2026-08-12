using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Public feedback facade and sole writer of camera Transform and FOV state.</summary>
    [MovedFrom("RocketFooxball")]
    public sealed class PlayerCameraFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerMotor player;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private GameObject viewmodels;
        [SerializeField] private GameObject crosshairCanvas;
        [SerializeField] private ParticipantState participant;

        [Header("Speed FOV")]
        [SerializeField, Min(1f)] private float baseFov = 75f;
        [SerializeField, Min(1f)] private float maxFov = 84f;

        [Header("Blast Shake")]
        [SerializeField, Min(0f)] private float shakeAmplitude = 0.06f;
        [SerializeField, Min(0.01f)] private float shakeDuration = 0.18f;
        [SerializeField, Min(0f)] private float shakeFrequency = 28f;

        [Header("Goal Celebration Orbit")]
        [SerializeField, Min(0.1f)] private float celebrationOrbitRadius = 5.5f;
        [SerializeField, Min(0f)] private float celebrationOrbitHeight = 2.5f;
        [SerializeField, Min(0f)] private float celebrationLookHeight = 1.05f;
        [SerializeField, Min(0f)] private float celebrationOrbitDegrees = 360f;
        [SerializeField, Min(1f)] private float celebrationFov = 60f;

        [Header("Spectator")]
        [SerializeField] private Vector3 spectatorOffset = new Vector3(0f, 2.5f, -5f);
        [SerializeField, Min(1f)] private float spectatorFov = 70f;

        private readonly CameraShakeModel shakeModel = new CameraShakeModel();
        private readonly GoalOrbitModel orbitModel = new GoalOrbitModel();
        private Transform cameraTransform;
        private Vector3 neutralLocalPosition;
        private Quaternion neutralLocalRotation = Quaternion.identity;
        private Transform celebrationOriginalParent;
        private Vector3 celebrationOriginalLocalPosition;
        private Quaternion celebrationOriginalLocalRotation;
        private float celebrationOriginalFov;
        private int celebrationOriginalCullingMask;
        private bool viewmodelsWasActive;
        private bool crosshairWasActive;
        private bool celebrationStateCaptured;
        private Transform spectatorTarget;
        private bool spectatorTargetRelative;
        private Transform spectatorOriginalParent;
        private Vector3 spectatorOriginalLocalPosition;
        private Quaternion spectatorOriginalLocalRotation;
        private bool spectatorStateCaptured;
        private bool spectatorViewmodelsWasActive;
        private bool spectatorCrosshairWasActive;

        public PlayerMotor Player => player;
        public ParticipantState Participant => participant;
        public Camera TargetCamera => targetCamera;
        public float CurrentFov => targetCamera != null ? targetCamera.fieldOfView : baseFov;
        public Vector3 NeutralLocalPosition => neutralLocalPosition;
        public bool IsGoalCelebrating => orbitModel.IsActive;
        public bool IsSpectating => spectatorTarget != null;
        public Transform SpectatorTarget => spectatorTarget;

        private void Awake()
        {
            CacheCameraTransform();
            CacheNeutralState();
        }

        private void OnEnable()
        {
            CacheCameraTransform();
            CacheNeutralState();
            ResetFeedback();
        }

        private void OnDisable()
        {
            ResetFeedback();
        }

        private void LateUpdate()
        {
            if (targetCamera == null || cameraTransform == null)
            {
                return;
            }

            if (orbitModel.IsActive)
            {
                WriteGoalOrbit(Time.unscaledDeltaTime);
                return;
            }

            if (spectatorTarget != null)
            {
                WriteSpectatorFollow();
                return;
            }

            var horizontalSpeed = player != null ? player.HorizontalSpeed : 0f;
            var baseSpeed = player != null ? player.BaseSpeed : 0f;
            var hardCap = player != null ? player.HardCap : 0f;
            targetCamera.fieldOfView = SpeedFovModel.Evaluate(baseFov, maxFov, baseSpeed, hardCap, horizontalSpeed);
            cameraTransform.localPosition = neutralLocalPosition + shakeModel.Step(Time.unscaledDeltaTime, shakeDuration, shakeAmplitude, shakeFrequency);
        }

        /// <summary>Detaches and orbits camera around frozen player for one goal celebration.</summary>
        public void BeginGoalCelebration(float duration)
        {
            CacheCameraTransform();
            ExitSpectator();
            if (orbitModel.IsActive || targetCamera == null || cameraTransform == null || player == null)
            {
                return;
            }

            shakeModel.Reset();
            cameraTransform.localPosition = neutralLocalPosition;
            cameraTransform.localRotation = neutralLocalRotation;
            celebrationOriginalParent = cameraTransform.parent;
            celebrationOriginalLocalPosition = cameraTransform.localPosition;
            celebrationOriginalLocalRotation = cameraTransform.localRotation;
            celebrationOriginalFov = targetCamera.fieldOfView;
            celebrationOriginalCullingMask = targetCamera.cullingMask;
            viewmodelsWasActive = viewmodels != null && viewmodels.activeSelf;
            crosshairWasActive = crosshairCanvas != null && crosshairCanvas.activeSelf;
            celebrationStateCaptured = true;

            if (viewmodels != null)
            {
                viewmodels.SetActive(false);
            }
            if (crosshairCanvas != null)
            {
                crosshairCanvas.SetActive(false);
            }

            var hiddenLayer = LayerMask.NameToLayer("LocalPlayerHidden");
            if (hiddenLayer >= 0)
            {
                targetCamera.cullingMask |= 1 << hiddenLayer;
            }

            orbitModel.Begin(player.transform.forward, celebrationOrbitRadius, duration);
            cameraTransform.SetParent(null, true);
            targetCamera.fieldOfView = Mathf.Max(celebrationFov, 1f);
            WriteGoalOrbit(0f);
        }

        /// <summary>Restores camera parent, local pose, culling, and explicit first-person overlays.</summary>
        public void EndGoalCelebration()
        {
            if (!celebrationStateCaptured)
            {
                orbitModel.Reset();
                return;
            }

            orbitModel.Reset();
            if (cameraTransform != null)
            {
                cameraTransform.SetParent(celebrationOriginalParent, false);
                cameraTransform.localPosition = celebrationOriginalLocalPosition;
                cameraTransform.localRotation = celebrationOriginalLocalRotation;
            }
            if (targetCamera != null)
            {
                targetCamera.fieldOfView = celebrationOriginalFov;
                targetCamera.cullingMask = celebrationOriginalCullingMask;
            }
            if (viewmodels != null)
            {
                viewmodels.SetActive(viewmodelsWasActive);
            }
            if (crosshairCanvas != null)
            {
                crosshairCanvas.SetActive(crosshairWasActive);
            }

            celebrationOriginalParent = null;
            celebrationStateCaptured = false;
        }

        /// <summary>Follows a living ally using target-relative behind-follow.</summary>
        public void SetSpectatorTarget(Transform target)
        {
            SetSpectatorTarget(target, true);
        }

        /// <summary>Follows a spectator target with optional target-relative offset.</summary>
        public void SetSpectatorTarget(Transform target, bool targetRelative)
        {
            if (target == spectatorTarget && targetRelative == spectatorTargetRelative)
            {
                return;
            }

            if (target == null)
            {
                ExitSpectator();
                return;
            }

            CacheCameraTransform();
            if (targetCamera == null || cameraTransform == null)
            {
                return;
            }

            if (!spectatorStateCaptured)
            {
                spectatorOriginalParent = cameraTransform.parent;
                spectatorOriginalLocalPosition = cameraTransform.localPosition;
                spectatorOriginalLocalRotation = cameraTransform.localRotation;
                spectatorViewmodelsWasActive = viewmodels != null && viewmodels.activeSelf;
                spectatorCrosshairWasActive = crosshairCanvas != null && crosshairCanvas.activeSelf;
                spectatorStateCaptured = true;
            }

            spectatorTarget = target;
            spectatorTargetRelative = targetRelative;
            cameraTransform.SetParent(null, true);
            if (viewmodels != null)
            {
                viewmodels.SetActive(false);
            }
            if (crosshairCanvas != null)
            {
                crosshairCanvas.SetActive(false);
            }
            targetCamera.fieldOfView = Mathf.Max(spectatorFov, 1f);
            WriteSpectatorFollow();
        }

        /// <summary>Follows a world-space target without inheriting its rotation.</summary>
        public void SetSpectatorWorldTarget(Transform target) => SetSpectatorTarget(target, false);

        public void BeginSpectator(Transform target) => SetSpectatorTarget(target);

        public void BeginSpectator(Transform target, bool targetRelative) => SetSpectatorTarget(target, targetRelative);

        /// <summary>Restores local camera parent and first-person overlays after respawn/reset.</summary>
        public void ExitSpectator()
        {
            spectatorTarget = null;
            spectatorTargetRelative = false;
            if (!spectatorStateCaptured)
            {
                return;
            }

            if (cameraTransform != null)
            {
                cameraTransform.SetParent(spectatorOriginalParent, false);
                cameraTransform.localPosition = spectatorOriginalLocalPosition;
                cameraTransform.localRotation = spectatorOriginalLocalRotation;
            }
            if (viewmodels != null)
            {
                viewmodels.SetActive(spectatorViewmodelsWasActive);
            }
            if (crosshairCanvas != null)
            {
                crosshairCanvas.SetActive(spectatorCrosshairWasActive);
            }
            spectatorOriginalParent = null;
            spectatorStateCaptured = false;
        }

        /// <summary>Requests deterministic decaying positional shake. Rotation and aim remain unchanged.</summary>
        public void RequestBlastShake(float normalizedStrength)
        {
            if (shakeAmplitude <= 0f || shakeDuration <= 0f)
            {
                return;
            }

            var strength = Mathf.Clamp01(normalizedStrength);
            if (strength > 0f)
            {
                shakeModel.Request(strength, shakeDuration);
            }
        }

        /// <summary>Ends celebration, restores neutral pose/FOV, and clears pending shake.</summary>
        public void ResetFeedback()
        {
            EndGoalCelebration();
            ExitSpectator();
            shakeModel.Reset();
            if (cameraTransform != null)
            {
                cameraTransform.localPosition = neutralLocalPosition;
                cameraTransform.localRotation = neutralLocalRotation;
            }
            if (targetCamera != null)
            {
                targetCamera.fieldOfView = baseFov;
            }
        }

        private void CacheCameraTransform()
        {
            if (participant == null)
            {
                participant = GetComponent<ParticipantState>();
            }
            cameraTransform = targetCamera != null ? targetCamera.transform : null;
        }

        private void CacheNeutralState()
        {
            if (cameraTransform != null)
            {
                neutralLocalPosition = cameraTransform.localPosition;
                neutralLocalRotation = cameraTransform.localRotation;
            }
        }

        private void WriteGoalOrbit(float deltaTime)
        {
            if (!orbitModel.IsActive || targetCamera == null || cameraTransform == null || player == null)
            {
                return;
            }

            orbitModel.Step(deltaTime, player.transform.position, celebrationOrbitRadius, celebrationOrbitHeight, celebrationLookHeight, celebrationOrbitDegrees, out var position, out var rotation);
            cameraTransform.position = position;
            cameraTransform.rotation = rotation;
            targetCamera.fieldOfView = Mathf.Max(celebrationFov, 1f);
        }

        private void WriteSpectatorFollow()
        {
            if (spectatorTarget == null || cameraTransform == null || targetCamera == null)
            {
                return;
            }

            var targetPosition = spectatorTarget.position;
            var offset = spectatorTargetRelative ? spectatorTarget.rotation * spectatorOffset : spectatorOffset;
            var position = targetPosition + offset;
            var lookPoint = targetPosition + Vector3.up * Mathf.Max(celebrationLookHeight, 0.5f);
            cameraTransform.position = position;
            cameraTransform.rotation = Quaternion.LookRotation((lookPoint - position).normalized, Vector3.up);
            targetCamera.fieldOfView = Mathf.Max(spectatorFov, 1f);
        }
    }
}
