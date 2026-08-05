using UnityEngine;
using UnityEngine.InputSystem;

namespace RocketFooxball
{
    public sealed class PlayerLook : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Transform head;
        [SerializeField, Range(0.0001f, 0.02f)] private float mouseSensitivity = 0.0025f;
        [SerializeField, Range(1f, 89.9f)] private float maxPitchDegrees = 89f;

        private float pitch;

        private void Awake()
        {
            SetCursorCapture(true);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetCursorCapture(false);
            }
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorCapture(true);
            }

            if (Cursor.lockState != CursorLockMode.Locked || input == null || head == null)
            {
                return;
            }

            var look = input.Look;
            transform.Rotate(Vector3.up, look.x * mouseSensitivity * Mathf.Rad2Deg, Space.World);
            pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity * Mathf.Rad2Deg, -maxPitchDegrees, maxPitchDegrees);
            head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private static void SetCursorCapture(bool captured)
        {
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }
    }
}
