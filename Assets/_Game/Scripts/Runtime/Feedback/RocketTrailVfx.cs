using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Detaches a projectile's world-space trail on impact and lets emitted particles fade out.</summary>
    [MovedFrom("RocketFooxball")]
    public sealed class RocketTrailVfx : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] particleSystems;
        [SerializeField] private TrailRenderer[] trailRenderers;
        [SerializeField] private GameObject blueImpactAccent;
        [SerializeField] private GameObject redImpactAccent;
        [SerializeField] private Material blueTrailMaterial;
        [SerializeField] private Material redTrailMaterial;
        [SerializeField] private Color blueTrailColor = new Color(0.08f, 0.35f, 1f, 1f);
        [SerializeField] private Color redTrailColor = new Color(1f, 0.12f, 0.1f, 1f);

        private bool detached;

        public ParticipantTeam Team { get; private set; } = ParticipantTeam.Blue;

        /// <summary>Configures team color and shape-coded impact endpoint before flight.</summary>
        public void ConfigureTeam(ParticipantTeam team)
        {
            Team = team;
            var isBlue = team == ParticipantTeam.Blue;
            blueImpactAccent?.SetActive(false);
            redImpactAccent?.SetActive(false);
            var color = isBlue ? blueTrailColor : redTrailColor;
            if (trailRenderers != null)
            {
                for (var i = 0; i < trailRenderers.Length; i++)
                {
                    var trail = trailRenderers[i];
                    if (trail == null)
                    {
                        continue;
                    }

                    trail.startColor = color;
                    trail.endColor = new Color(color.r, color.g, color.b, 0f);
                }
            }

            var systems = particleSystems;
            if (systems == null || systems.Length == 0)
            {
                systems = GetComponentsInChildren<ParticleSystem>(true);
            }
            var trailMaterial = isBlue ? blueTrailMaterial : redTrailMaterial;
            for (var i = 0; i < systems.Length; i++)
            {
                var particleSystem = systems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                var main = particleSystem.main;
                main.startColor = color;
                var colorOverLifetime = particleSystem.colorOverLifetime;
                if (colorOverLifetime.enabled)
                {
                    var gradient = new Gradient();
                    gradient.SetKeys(
                        new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                        new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
                    colorOverLifetime.color = gradient;
                }

                var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                if (renderer != null && trailMaterial != null)
                {
                    renderer.sharedMaterial = trailMaterial;
                }
            }
        }

        /// <summary>Stops new emission, preserves world pose, and schedules this effect root for cleanup.</summary>
        public void DetachAndFade()
        {
            if (detached)
            {
                return;
            }

            detached = true;
            blueImpactAccent?.SetActive(Team == ParticipantTeam.Blue);
            redImpactAccent?.SetActive(Team == ParticipantTeam.Red);
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
