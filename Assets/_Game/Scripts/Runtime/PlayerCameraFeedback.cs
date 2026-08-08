using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Visual-only player feedback. Owns camera local position and field of view.</summary>
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

        private Transform cameraTransform;
        private Vector3 neutralLocalPosition;
        private float shakeRemaining;
        private float shakeStrength;
        private float shakeElapsed;
        private float shakePhase;

        public PlayerMotor Player => player;
        public Camera TargetCamera => targetCamera;
        public float CurrentFov => targetCamera != null ? targetCamera.fieldOfView : baseFov;
        public Vector3 NeutralLocalPosition => neutralLocalPosition;

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

            var speedT = 0f;
            if (player != null)
            {
                speedT = Mathf.InverseLerp(player.BaseSpeed, player.HardCap, player.HorizontalSpeed);
            }

            targetCamera.fieldOfView = Mathf.Lerp(baseFov, Mathf.Max(baseFov, maxFov), speedT);
            ApplyShake(Time.unscaledDeltaTime);
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

        /// <summary>Restores neutral camera position/FOV and clears pending shake.</summary>
        public void ResetFeedback()
        {
            shakeRemaining = 0f;
            shakeStrength = 0f;
            shakeElapsed = 0f;
            shakePhase = 0f;

            if (cameraTransform != null)
            {
                cameraTransform.localPosition = neutralLocalPosition;
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
            }
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
