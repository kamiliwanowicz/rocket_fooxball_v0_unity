using UnityEngine;
using UnityEngine.InputSystem;

namespace RocketFooxball
{
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;

        private bool jumpPressed;
        private bool kickPressed;
        private bool fireHeld;
        private bool gameplayInputEnabled = true;
        private bool releaseCursorRequested;
        private bool captureCursorRequested;
        private bool suppressFireUntilRelease;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction jumpAction;
        private InputAction fireAction;
        private InputAction kickAction;
        private InputAction releaseCursorAction;
        private InputAction captureCursorAction;

        public Vector2 Move => gameplayInputEnabled && moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        public Vector2 Look => gameplayInputEnabled && lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;
        public bool FireHeld => gameplayInputEnabled && fireHeld;
        public bool CursorCaptured => Cursor.lockState == CursorLockMode.Locked;
        public bool GameplayInputEnabled => gameplayInputEnabled;

        private void Awake()
        {
            if (actions == null)
            {
                Debug.LogError("PlayerInputReader requires InputSystem_Actions.inputactions.", this);
                return;
            }

            moveAction = actions.FindAction("Player/Move", true);
            lookAction = actions.FindAction("Player/Look", true);
            jumpAction = actions.FindAction("Player/Jump", true);
            fireAction = actions.FindAction("Player/Fire", true);
            kickAction = actions.FindAction("Player/Kick", true);
            releaseCursorAction = actions.FindAction("Player/ReleaseCursor", true);
            captureCursorAction = actions.FindAction("Player/CaptureCursor", true);
        }

        private void OnEnable()
        {
            Enable(moveAction);
            Enable(lookAction);
            Enable(jumpAction);
            Enable(fireAction);
            Enable(kickAction);
            Enable(releaseCursorAction);
            Enable(captureCursorAction);
            if (jumpAction != null)
            {
                jumpAction.started += OnJumpStarted;
            }
            if (kickAction != null)
            {
                kickAction.started += OnKickStarted;
            }
            if (releaseCursorAction != null)
            {
                releaseCursorAction.started += OnReleaseCursorStarted;
            }
            if (captureCursorAction != null)
            {
                captureCursorAction.started += OnCaptureCursorStarted;
            }
        }

        private void OnDisable()
        {
            if (jumpAction != null)
            {
                jumpAction.started -= OnJumpStarted;
            }
            if (kickAction != null)
            {
                kickAction.started -= OnKickStarted;
            }
            if (releaseCursorAction != null)
            {
                releaseCursorAction.started -= OnReleaseCursorStarted;
            }
            if (captureCursorAction != null)
            {
                captureCursorAction.started -= OnCaptureCursorStarted;
            }
            Disable(moveAction);
            Disable(lookAction);
            Disable(jumpAction);
            Disable(fireAction);
            Disable(kickAction);
            Disable(releaseCursorAction);
            Disable(captureCursorAction);
            ClearGameplayState();
            releaseCursorRequested = false;
            captureCursorRequested = false;
        }

        private void Update()
        {
            if (!gameplayInputEnabled || !CursorCaptured || fireAction == null)
            {
                fireHeld = false;
                return;
            }

            if (suppressFireUntilRelease)
            {
                if (!fireAction.IsPressed())
                {
                    suppressFireUntilRelease = false;
                }
                fireHeld = false;
                return;
            }

            fireHeld = fireAction.IsPressed();
        }

        public bool ConsumeJumpPressed()
        {
            var result = jumpPressed;
            jumpPressed = false;
            return result;
        }

        public bool ConsumeKickPressed()
        {
            var result = kickPressed;
            kickPressed = false;
            return result;
        }

        public bool ConsumeReleaseCursorRequested()
        {
            var result = releaseCursorRequested;
            releaseCursorRequested = false;
            return result;
        }

        public bool ConsumeCaptureCursorRequested()
        {
            var result = captureCursorRequested;
            captureCursorRequested = false;
            return result;
        }

        public void SetGameplayInputEnabled(bool enabled)
        {
            if (gameplayInputEnabled == enabled)
            {
                return;
            }

            gameplayInputEnabled = enabled;
            if (!enabled)
            {
                ClearGameplayState();
            }
        }

        public void ClearGameplayState()
        {
            jumpPressed = false;
            kickPressed = false;
            fireHeld = false;
            suppressFireUntilRelease = fireAction != null && fireAction.IsPressed();
        }

        /// <summary>Clears all gameplay intents without changing cursor ownership.</summary>
        public void ResetInputState()
        {
            ClearGameplayState();
            releaseCursorRequested = false;
            captureCursorRequested = false;
        }

        /// <summary>Compatibility alias for reset owners.</summary>
        public void ClearInputState()
        {
            ResetInputState();
        }

        private void OnJumpStarted(InputAction.CallbackContext _)
        {
            if (gameplayInputEnabled)
            {
                jumpPressed = true;
            }
        }

        private void OnKickStarted(InputAction.CallbackContext _)
        {
            if (gameplayInputEnabled)
            {
                kickPressed = true;
            }
        }

        private void OnReleaseCursorStarted(InputAction.CallbackContext _)
        {
            releaseCursorRequested = true;
            fireHeld = false;
        }

        private void OnCaptureCursorStarted(InputAction.CallbackContext _)
        {
            if (CursorCaptured)
            {
                return;
            }

            captureCursorRequested = true;
            fireHeld = false;
            suppressFireUntilRelease = true;
        }

        private static void Enable(InputAction action)
        {
            if (action != null)
            {
                action.Enable();
            }
        }

        private static void Disable(InputAction action)
        {
            if (action != null)
            {
                action.Disable();
            }
        }
    }
}
