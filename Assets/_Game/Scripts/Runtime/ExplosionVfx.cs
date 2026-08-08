using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Owns one-shot particle playback and lifetime cleanup for an explosion.</summary>
    public sealed class ExplosionVfx : MonoBehaviour
    {
        private const float MaxVisualLifetime = 1.25f;

        [SerializeField] private ParticleSystem[] particleSystems;

        private bool played;

        /// <summary>Plays every configured system once and schedules the effect root for cleanup.</summary>
        public void Play()
        {
            if (played)
            {
                return;
            }

            played = true;
            var systems = particleSystems;
            if (systems == null || systems.Length == 0)
            {
                systems = GetComponentsInChildren<ParticleSystem>(true);
            }

            var maximumLifetime = 0f;
            for (var i = 0; i < systems.Length; i++)
            {
                var particleSystem = systems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                var main = particleSystem.main;
                maximumLifetime = Mathf.Max(maximumLifetime, main.duration + main.startLifetime.constantMax);
                particleSystem.Play(false);
            }

            var cleanupDelay = Mathf.Clamp(maximumLifetime, 0.01f, MaxVisualLifetime);
            Destroy(gameObject, cleanupDelay);
        }
    }
}
