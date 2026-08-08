using UnityEngine;

namespace RocketFooxball
{
    public sealed class MovementDebugHud : MonoBehaviour
    {
        [SerializeField] private PlayerMotor player;
        [SerializeField] private BallMotor ball;
        [SerializeField] private RocketLauncher launcher;
        [SerializeField] private BallKick kick;
        [SerializeField] private MatchController match;

        private GUIStyle labelStyle;

        private void Awake()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerMotor>();
            }
            if (ball == null)
            {
                ball = FindAnyObjectByType<BallMotor>();
            }
            if (launcher == null && player != null)
            {
                launcher = player.GetComponent<RocketLauncher>();
            }
            if (kick == null && player != null)
            {
                kick = player.GetComponent<BallKick>();
            }
            if (match == null)
            {
                match = FindAnyObjectByType<MatchController>();
            }
        }

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
            var controller = player.GetComponent<CharacterController>();
            var text = $"FPS: {Mathf.RoundToInt(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f))}\n" +
                       $"H Speed: {speed:F1} m/s ({speed / Mathf.Max(player.HardCap, 0.001f) * 100f:F0}% cap)\n" +
                       $"V Speed: {velocity.y:F1} m/s\n" +
                       $"Grounded: {(controller != null && controller.isGrounded ? "YES" : "NO")}\n" +
                       $"Air Jump: {(player.IsAirJumpAvailable ? "READY" : "USED")}\n" +
                       $"Caps: base {player.BaseSpeed:F0} | soft {player.SoftCap:F0} | hard {player.HardCap:F0}\n";

            if (ball != null)
            {
                text += $"Ball: {ball.Speed:F1} m/s ({ball.Speed / Mathf.Max(ball.HardCap, 0.001f) * 100f:F0}% cap)\n";
            }
            if (launcher != null)
            {
                text += $"Fire: {(launcher.CooldownRemaining <= 0f ? "READY" : launcher.CooldownRemaining.ToString("F2") + "s")} | Rockets {launcher.ActiveProjectileCount}\n";
            }
            if (kick != null)
            {
                text += $"Kick: {(kick.CooldownRemaining <= 0f ? "READY" : kick.CooldownRemaining.ToString("F2") + "s")}" +
                        (kick.AttemptPending ? $" | Buffer {kick.BufferRemaining:F2}s" : "") + "\n";
            }
            if (match != null)
            {
                text += $"Score North {match.NorthScore} - South {match.SouthScore} | {match.State}";
                if (match.State == MatchController.MatchState.GoalFreeze)
                {
                    text += $" ({match.FreezeRemaining:F1}s)";
                }
                text += "\n";
            }

            text += "WASD Move | Mouse Look | Space Jump/Double-Jump | LMB Fire | RMB Kick | Esc Release Mouse";
            GUI.Label(new Rect(16f, 16f, 700f, 260f), text, labelStyle);
        }
    }
}
