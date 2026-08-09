using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Input;

namespace RocketFooxball.Runtime.Movement
{
    [MovedFrom("RocketFooxball")]
    public sealed class PlayerLook : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Transform head;
        [SerializeField, Range(0.0001f, 0.02f)] private float mouseSensitivity = 0.0025f;
        [SerializeField, Range(1f, 89.9f)] private float maxPitchDegrees = 89f;

        private float pitch;

        public float PitchDegrees => pitch;
        public Transform Head => head;

        private void Awake()
        {
            SetCursorCapture(true);
        }

        private void Update()
        {
            if (input == null || head == null)
            {
                return;
            }

            if (input.ConsumeReleaseCursorRequested())
            {
                SetCursorCapture(false);
            }
            if (input.ConsumeCaptureCursorRequested())
            {
                SetCursorCapture(true);
            }

            if (!input.CursorCaptured)
            {
                return;
            }

            var look = input.Look;
            transform.Rotate(Vector3.up, look.x * mouseSensitivity * Mathf.Rad2Deg, Space.World);
            pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity * Mathf.Rad2Deg, -maxPitchDegrees, maxPitchDegrees);
            head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>Resets pitch and keeps current horizontal facing.</summary>
        public void ResetView()
        {
            ResetView(transform.forward);
        }

        /// <summary>Resets pitch and faces a supplied world-space direction without changing camera aim mechanically.</summary>
        public void ResetView(Vector3 worldForward)
        {
            var flatForward = new Vector3(worldForward.x, 0f, worldForward.z);
            if (flatForward.sqrMagnitude > 0.000001f)
            {
                transform.rotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            }

            pitch = 0f;
            if (head != null)
            {
                head.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Compatibility alias used by match reset owners.</summary>
        public void ResetAim(Vector3 worldForward)
        {
            ResetView(worldForward);
        }

        private static void SetCursorCapture(bool captured)
        {
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }
    }
}
