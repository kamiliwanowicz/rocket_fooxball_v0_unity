using UnityEngine;
using UnityEngine.InputSystem;

namespace RocketFooxball
{
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;

        private bool jumpPressed;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction jumpAction;

        public Vector2 Move => moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        public Vector2 Look => lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

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
        }

        private void OnEnable()
        {
            Enable(moveAction);
            Enable(lookAction);
            Enable(jumpAction);
            if (jumpAction != null)
            {
                jumpAction.started += OnJumpStarted;
            }
        }

        private void OnDisable()
        {
            if (jumpAction != null)
            {
                jumpAction.started -= OnJumpStarted;
            }
            Disable(moveAction);
            Disable(lookAction);
            Disable(jumpAction);
            jumpPressed = false;
        }

        public bool ConsumeJumpPressed()
        {
            var result = jumpPressed;
            jumpPressed = false;
            return result;
        }

        private void OnJumpStarted(InputAction.CallbackContext _) => jumpPressed = true;

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
