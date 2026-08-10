using UnityEngine;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Stateful celebration orbit calculation without Transform or Camera writes.</summary>
    public sealed class GoalOrbitModel
    {
        private float elapsed;
        private float duration;
        private float startAngle;

        public bool IsActive { get; private set; }

        public void Begin(Vector3 playerForward, float orbitRadius, float requestedDuration)
        {
            var flatForward = Vector3.ProjectOnPlane(playerForward, Vector3.up);
            if (flatForward.sqrMagnitude <= 0.000001f)
            {
                flatForward = Vector3.forward;
            }
            flatForward.Normalize();

            var startOffset = -flatForward * Mathf.Max(orbitRadius, 0.1f);
            startAngle = Mathf.Atan2(startOffset.z, startOffset.x);
            elapsed = 0f;
            duration = Mathf.Max(requestedDuration, 0.1f);
            IsActive = true;
        }

        public void Step(float deltaTime, Vector3 playerPosition, float orbitRadius, float orbitHeight, float lookHeight, float orbitDegrees, out Vector3 position, out Quaternion rotation)
        {
            elapsed = Mathf.Min(elapsed + Mathf.Max(deltaTime, 0f), duration);
            var progress = Mathf.Clamp01(elapsed / Mathf.Max(duration, 0.0001f));
            var angle = startAngle + progress * orbitDegrees * Mathf.Deg2Rad;
            var orbitCenter = playerPosition + Vector3.up * Mathf.Max(lookHeight, 0f);
            var horizontalOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Mathf.Max(orbitRadius, 0.1f);
            position = playerPosition + horizontalOffset + Vector3.up * Mathf.Max(orbitHeight, 0f);
            var lookDirection = orbitCenter - position;
            rotation = Quaternion.LookRotation(lookDirection.sqrMagnitude <= 0.000001f ? Vector3.forward : lookDirection.normalized, Vector3.up);
        }

        public void Reset()
        {
            elapsed = 0f;
            duration = 0f;
            startAngle = 0f;
            IsActive = false;
        }
    }
}
