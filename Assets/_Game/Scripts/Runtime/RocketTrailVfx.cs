using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Detaches a projectile's world-space trail on impact and lets emitted particles fade out.</summary>
    public sealed class RocketTrailVfx : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] particleSystems;

        private bool detached;

        /// <summary>Stops new emission, preserves world pose, and schedules this effect root for cleanup.</summary>
        public void DetachAndFade()
        {
            if (detached)
            {
                return;
            }

            detached = true;
            var effectRoot = transform;
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
                maximumLifetime = Mathf.Max(maximumLifetime, main.startLifetime.constantMax);
            }

            effectRoot.SetParent(null, true);
            for (var i = 0; i < systems.Length; i++)
            {
                var particleSystem = systems[i];
                if (particleSystem != null)
                {
                    particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            Destroy(effectRoot.gameObject, Mathf.Max(maximumLifetime + 0.1f, 0.1f));
        }
    }
}
