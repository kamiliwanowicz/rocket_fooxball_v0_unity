using UnityEngine;

namespace RocketFooxball
{
    public sealed class MovementDebugHud : MonoBehaviour
    {
        [SerializeField] private PlayerMotor player;

        private GUIStyle labelStyle;

        private void OnGUI()
        {
            if (player == null)
            {
                return;
            }

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            }

            var velocity = player.Velocity;
            var speed = new Vector2(velocity.x, velocity.z).magnitude;
            var text = $"FPS: {Mathf.RoundToInt(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f))}\n" +
                       $"H Speed: {speed:F1} m/s ({speed / player.HardCap * 100f:F0}% cap)\n" +
                       $"V Speed: {velocity.y:F1} m/s\n" +
                       $"Grounded: {(player.GetComponent<CharacterController>().isGrounded ? "YES" : "NO")}\n" +
                       $"Air Jump: {(player.IsAirJumpAvailable ? "READY" : "USED")}\n" +
                       $"Caps: base {player.BaseSpeed:F0} | soft {player.SoftCap:F0} | hard {player.HardCap:F0}\n\n" +
                       "WASD Move | Mouse Look | Space Jump/Double-Jump | Esc Release Mouse";
            GUI.Label(new Rect(16f, 16f, 550f, 180f), text, labelStyle);
        }
    }
}
