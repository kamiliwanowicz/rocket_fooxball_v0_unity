using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Visual-only player feedback. Sole owner of camera pose, FOV, shake, and goal orbit.</summary>
    public sealed class PlayerCameraFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerMotor player;
        [SerializeField] private Camera targetCamera;

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

        private Transform cameraTransform;
        private Vector3 neutralLocalPosition;
        private Quaternion neutralLocalRotation = Quaternion.identity;
        private float shakeRemaining;
        private float shakeStrength;
        private float shakeElapsed;
        private float shakePhase;

        private bool goalCelebrationActive;
        private float goalCelebrationElapsed;
        private float goalCelebrationDuration;
        private float goalCelebrationStartAngle;
        private Transform goalCelebrationOriginalParent;
        private Vector3 goalCelebrationOriginalLocalPosition;
        private Quaternion goalCelebrationOriginalLocalRotation;
        private float goalCelebrationOriginalFov;
        private int goalCelebrationOriginalCullingMask;
        private GameObject goalCelebrationViewmodels;
        private GameObject goalCelebrationCrosshair;
        private bool goalCelebrationViewmodelsWasActive;
        private bool goalCelebrationCrosshairWasActive;
        private bool goalCelebrationStateCaptured;

        public PlayerMotor Player => player;
        public Camera TargetCamera => targetCamera;
        public float CurrentFov => targetCamera != null ? targetCamera.fieldOfView : baseFov;
        public Vector3 NeutralLocalPosition => neutralLocalPosition;
        public bool IsGoalCelebrating => goalCelebrationActive;

        private void Awake()
        {
            CacheReferences();
            CacheNeutralState();
        }

        private void OnEnable()
        {
            CacheReferences();
            CacheNeutralState();
            ResetFeedback();
        }

        private void OnDisable()
        {
            ResetFeedback();
        }

        private void LateUpdate()
        {
            if (targetCamera == null)
            {
                return;
            }

            if (goalCelebrationActive)
            {
                UpdateGoalCelebrationOrbit(Time.unscaledDeltaTime);
                return;
            }

            var speedT = 0f;
            if (player != null)
            {
                speedT = Mathf.InverseLerp(player.BaseSpeed, player.HardCap, player.HorizontalSpeed);
            }

            targetCamera.fieldOfView = Mathf.Lerp(baseFov, Mathf.Max(baseFov, maxFov), speedT);
            ApplyShake(Time.unscaledDeltaTime);
        }

        /// <summary>Detaches and orbits the camera around the frozen player for one goal celebration.</summary>
        public void BeginGoalCelebration(float duration)
        {
            CacheReferences();
            if (goalCelebrationActive || targetCamera == null || cameraTransform == null || player == null)
            {
                return;
            }

            shakeRemaining = 0f;
            shakeStrength = 0f;
            shakeElapsed = 0f;
            shakePhase = 0f;
            cameraTransform.localPosition = neutralLocalPosition;
            cameraTransform.localRotation = neutralLocalRotation;

            goalCelebrationOriginalParent = cameraTransform.parent;
            goalCelebrationOriginalLocalPosition = cameraTransform.localPosition;
            goalCelebrationOriginalLocalRotation = cameraTransform.localRotation;
            goalCelebrationOriginalFov = targetCamera.fieldOfView;
            goalCelebrationOriginalCullingMask = targetCamera.cullingMask;
            goalCelebrationViewmodels = FindDescendant("Viewmodels");
            goalCelebrationCrosshair = FindDescendant("CrosshairCanvas");
            goalCelebrationViewmodelsWasActive = goalCelebrationViewmodels != null && goalCelebrationViewmodels.activeSelf;
            goalCelebrationCrosshairWasActive = goalCelebrationCrosshair != null && goalCelebrationCrosshair.activeSelf;
            goalCelebrationStateCaptured = true;

            if (goalCelebrationViewmodels != null)
            {
                goalCelebrationViewmodels.SetActive(false);
            }
            if (goalCelebrationCrosshair != null)
            {
                goalCelebrationCrosshair.SetActive(false);
            }

            var hiddenLayer = LayerMask.NameToLayer("LocalPlayerHidden");
            if (hiddenLayer >= 0)
            {
                targetCamera.cullingMask |= 1 << hiddenLayer;
            }

            var flatForward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
            if (flatForward.sqrMagnitude <= 0.000001f)
            {
                flatForward = Vector3.forward;
            }
            flatForward.Normalize();
            var startOffset = -flatForward * Mathf.Max(celebrationOrbitRadius, 0.1f);
            goalCelebrationStartAngle = Mathf.Atan2(startOffset.z, startOffset.x);
            goalCelebrationElapsed = 0f;
            goalCelebrationDuration = Mathf.Max(duration, 0.1f);
            goalCelebrationActive = true;

            // Detach once, then this component owns world pose until EndGoalCelebration.
            cameraTransform.SetParent(null, true);
            targetCamera.fieldOfView = Mathf.Max(celebrationFov, 1f);
            UpdateGoalCelebrationOrbit(0f);
        }

        /// <summary>Restores parent, local pose, culling, and first-person overlays after celebration.</summary>
        public void EndGoalCelebration()
        {
            if (!goalCelebrationStateCaptured)
            {
                goalCelebrationActive = false;
                return;
            }

            goalCelebrationActive = false;
            if (cameraTransform != null)
            {
                cameraTransform.SetParent(goalCelebrationOriginalParent, false);
                cameraTransform.localPosition = goalCelebrationOriginalLocalPosition;
                cameraTransform.localRotation = goalCelebrationOriginalLocalRotation;
            }
            if (targetCamera != null)
            {
                targetCamera.fieldOfView = goalCelebrationOriginalFov;
                targetCamera.cullingMask = goalCelebrationOriginalCullingMask;
            }
            if (goalCelebrationViewmodels != null)
            {
                goalCelebrationViewmodels.SetActive(goalCelebrationViewmodelsWasActive);
            }
            if (goalCelebrationCrosshair != null)
            {
                goalCelebrationCrosshair.SetActive(goalCelebrationCrosshairWasActive);
            }

            goalCelebrationElapsed = 0f;
            goalCelebrationDuration = 0f;
            goalCelebrationStartAngle = 0f;
            goalCelebrationOriginalParent = null;
            goalCelebrationViewmodels = null;
            goalCelebrationCrosshair = null;
            goalCelebrationStateCaptured = false;
        }

        /// <summary>Requests deterministic, decaying positional shake. Rotation and aim remain unchanged.</summary>
        public void RequestBlastShake(float normalizedStrength)
        {
            if (shakeAmplitude <= 0f || shakeDuration <= 0f)
            {
                return;
            }

            var strength = Mathf.Clamp01(normalizedStrength);
            if (strength <= 0f)
            {
                return;
            }

            shakeStrength = Mathf.Clamp01(Mathf.Max(shakeStrength, strength));
            shakeRemaining = Mathf.Max(shakeRemaining, shakeDuration);
            shakePhase = Mathf.Repeat(shakePhase + 1.234567f, 1000f);
        }

        /// <summary>Compatibility alias for explosion feedback owners.</summary>
        public void RequestShake(float normalizedStrength)
        {
            RequestBlastShake(normalizedStrength);
        }

        /// <summary>Ends any celebration orbit, restores neutral camera pose/FOV, and clears pending shake.</summary>
        public void ResetFeedback()
        {
            EndGoalCelebration();
            shakeRemaining = 0f;
            shakeStrength = 0f;
            shakeElapsed = 0f;
            shakePhase = 0f;

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

        /// <summary>Compatibility alias for match reset owners.</summary>
        public void ResetState()
        {
            ResetFeedback();
        }

        private void CacheReferences()
        {
            if (player == null)
            {
                player = GetComponent<PlayerMotor>();
            }
            if (targetCamera == null)
            {
                targetCamera = GetComponentInChildren<Camera>(true);
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

        private void UpdateGoalCelebrationOrbit(float deltaTime)
        {
            if (!goalCelebrationActive || targetCamera == null || cameraTransform == null || player == null)
            {
                return;
            }

            goalCelebrationElapsed = Mathf.Min(goalCelebrationElapsed + Mathf.Max(deltaTime, 0f), goalCelebrationDuration);
            var progress = Mathf.Clamp01(goalCelebrationElapsed / Mathf.Max(goalCelebrationDuration, 0.0001f));
            var angle = goalCelebrationStartAngle + progress * celebrationOrbitDegrees * Mathf.Deg2Rad;
            var orbitCenter = player.transform.position + Vector3.up * Mathf.Max(celebrationLookHeight, 0f);
            var horizontalOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Mathf.Max(celebrationOrbitRadius, 0.1f);
            var cameraPosition = player.transform.position + horizontalOffset + Vector3.up * Mathf.Max(celebrationOrbitHeight, 0f);
            var lookDirection = orbitCenter - cameraPosition;
            if (lookDirection.sqrMagnitude <= 0.000001f)
            {
                lookDirection = Vector3.forward;
            }

            cameraTransform.position = cameraPosition;
            cameraTransform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            targetCamera.fieldOfView = Mathf.Max(celebrationFov, 1f);
        }

        private GameObject FindDescendant(string childName)
        {
            var children = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                {
                    return children[i].gameObject;
                }
            }

            return null;
        }

        private void ApplyShake(float deltaTime)
        {
            if (cameraTransform == null)
            {
                return;
            }

            var safeDeltaTime = Mathf.Max(deltaTime, 0f);
            shakeElapsed += safeDeltaTime;
            shakeRemaining = Mathf.Max(shakeRemaining - safeDeltaTime, 0f);

            if (shakeRemaining <= 0f || shakeStrength <= 0f)
            {
                shakeStrength = 0f;
                cameraTransform.localPosition = neutralLocalPosition;
                return;
            }

            var envelope = Mathf.Clamp01(shakeRemaining / Mathf.Max(shakeDuration, 0.0001f));
            var phase = shakeElapsed * shakeFrequency + shakePhase;
            var offset = new Vector3(
                Mathf.Sin(phase) * 0.75f,
                Mathf.Cos(phase * 1.31f) * 0.55f,
                Mathf.Sin(phase * 1.73f + 0.9f) * 0.65f);
            cameraTransform.localPosition = neutralLocalPosition + offset * (shakeAmplitude * shakeStrength * envelope);
        }
    }
}
