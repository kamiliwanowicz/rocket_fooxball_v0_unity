using UnityEngine;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Stateful but Unity-object-free deterministic positional shake model.</summary>
    public sealed class CameraShakeModel
    {
        private float remaining;
        private float strength;
        private float elapsed;
        private float phase;

        public void Request(float normalizedStrength, float duration)
        {
            strength = Mathf.Clamp01(Mathf.Max(strength, normalizedStrength));
            remaining = Mathf.Max(remaining, duration);
            phase = Mathf.Repeat(phase + 1.234567f, 1000f);
        }

        public Vector3 Step(float deltaTime, float duration, float amplitude, float frequency)
        {
            var safeDeltaTime = Mathf.Max(deltaTime, 0f);
            elapsed += safeDeltaTime;
            remaining = Mathf.Max(remaining - safeDeltaTime, 0f);
            if (remaining <= 0f || strength <= 0f)
            {
                strength = 0f;
                return Vector3.zero;
            }

            var envelope = Mathf.Clamp01(remaining / Mathf.Max(duration, 0.0001f));
            var samplePhase = elapsed * frequency + phase;
            var offset = new Vector3(
                Mathf.Sin(samplePhase) * 0.75f,
                Mathf.Cos(samplePhase * 1.31f) * 0.55f,
                Mathf.Sin(samplePhase * 1.73f + 0.9f) * 0.65f);
            return offset * (amplitude * strength * envelope);
        }

        public void Reset()
        {
            remaining = 0f;
            strength = 0f;
            elapsed = 0f;
            phase = 0f;
        }
    }
}
