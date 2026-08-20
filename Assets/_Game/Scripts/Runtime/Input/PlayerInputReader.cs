using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Scripting.APIUpdating;

namespace RocketFooxball.Runtime.Input
{
    [MovedFrom("RocketFooxball")]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;

        private bool jumpPressed;
        private bool kickPressed;
        private bool shotgunPressed;
        private bool fireHeld;
        private bool gameplayInputEnabled = true;
        private bool releaseCursorRequested;
        private bool captureCursorRequested;
        private bool suppressFireUntilRelease;
        private bool anyButtonPressLatched;
        private IDisposable anyButtonPressSubscription;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction jumpAction;
        private InputAction fireAction;
        private InputAction shotgunFireAction;
        private InputAction kickAction;
        private InputAction releaseCursorAction;
        private InputAction captureCursorAction;
        private InputAction matchTableAction;

        public Vector2 Move => gameplayInputEnabled && moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        public Vector2 Look => gameplayInputEnabled && lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;
        public bool FireHeld => gameplayInputEnabled && fireHeld;
        /// <summary>Returns held match-table intent while this reader/action is active.</summary>
        public bool MatchTableHeld => isActiveAndEnabled && matchTableAction != null && matchTableAction.enabled && matchTableAction.IsPressed();
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
            shotgunFireAction = actions.FindAction("Player/ShotgunFire", true);
            kickAction = actions.FindAction("Player/Kick", true);
            releaseCursorAction = actions.FindAction("Player/ReleaseCursor", true);
            captureCursorAction = actions.FindAction("Player/CaptureCursor", true);
            matchTableAction = actions.FindAction("Player/MatchTable", true);
        }

        private void OnEnable()
        {
            Enable(moveAction);
            Enable(lookAction);
            Enable(jumpAction);
            Enable(fireAction);
            Enable(shotgunFireAction);
            Enable(kickAction);
            Enable(releaseCursorAction);
            Enable(captureCursorAction);
            Enable(matchTableAction);
            if (jumpAction != null)
            {
                jumpAction.started += OnJumpStarted;
            }
            if (kickAction != null)
            {
                kickAction.started += OnKickStarted;
            }
            if (shotgunFireAction != null)
            {
                shotgunFireAction.started += OnShotgunFireStarted;
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
            if (shotgunFireAction != null)
            {
                shotgunFireAction.started -= OnShotgunFireStarted;
            }
            if (releaseCursorAction != null)
            {
                releaseCursorAction.started -= OnReleaseCursorStarted;
            }
            if (captureCursorAction != null)
            {
                captureCursorAction.started -= OnCaptureCursorStarted;
            }
            CancelAnyButtonPress();
            Disable(moveAction);
            Disable(lookAction);
            Disable(jumpAction);
            Disable(fireAction);
            Disable(shotgunFireAction);
            Disable(kickAction);
            Disable(releaseCursorAction);
            Disable(captureCursorAction);
            Disable(matchTableAction);
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

        public bool ConsumeShotgunPressed()
        {
            var result = shotgunPressed;
            shotgunPressed = false;
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

        /// <summary>Arms one fresh global button press for dismissing a goal celebration.</summary>
        public void ArmAnyButtonPress()
        {
            CancelAnyButtonPress();
            anyButtonPressSubscription = InputSystem.onAnyButtonPress.CallOnce(OnAnyButtonPress);
        }

        /// <summary>Consumes the one latched goal-celebration dismissal request.</summary>
        public bool ConsumeAnyButtonPress()
        {
            var result = anyButtonPressLatched;
            anyButtonPressLatched = false;
            return result;
        }

        /// <summary>Disarms and clears the goal-celebration dismissal request.</summary>
        public void CancelAnyButtonPress()
        {
            anyButtonPressSubscription?.Dispose();
            anyButtonPressSubscription = null;
            anyButtonPressLatched = false;
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
            shotgunPressed = false;
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

        private void OnShotgunFireStarted(InputAction.CallbackContext _)
        {
            if (gameplayInputEnabled && CursorCaptured)
            {
                shotgunPressed = true;
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

        private void OnAnyButtonPress(InputControl _)
        {
            if (anyButtonPressLatched)
            {
                return;
            }

            anyButtonPressLatched = true;
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
