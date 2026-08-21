using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Owns one-shot particle playback and lifetime cleanup for an explosion.</summary>
    [MovedFrom("RocketFooxball")]
    public sealed class ExplosionVfx : MonoBehaviour
    {
        public const float ReferenceVisualRadius = 4.5f;
        public const float ReferenceVisualDiameter = ReferenceVisualRadius * 2f;
        public static readonly Color BlueFlashColor = new Color(0.22f, 0.62f, 1f, 1f);
        public static readonly Color RedFlashColor = new Color(1f, 0.24f, 0.20f, 1f);
        public static readonly Color NeutralFlashColor = new Color(1f, 0.55f, 0.18f, 1f);

        [SerializeField] private ParticleSystem[] particleSystems;

        private bool played;

        /// <summary>Returns the pure uniform scale needed to represent one blast radius.</summary>
        public static float ComputeVisualScale(float radius)
        {
            return Mathf.Max(radius, 0.01f) / ReferenceVisualRadius;
        }

        /// <summary>Compatibility alias for callers that describe the result as a scale.</summary>
        public static float ComputeScale(float radius)
        {
            return ComputeVisualScale(radius);
        }

        /// <summary>Returns the world-space radius represented by an authored particle diameter.</summary>
        public static float ComputeVisualRadius(float authoredDiameter, float requestedRadius)
        {
            if (!IsFinite(authoredDiameter) || !IsFinite(requestedRadius) || authoredDiameter <= 0f)
            {
                return 0f;
            }

            return authoredDiameter * 0.5f * ComputeVisualScale(requestedRadius);
        }

        /// <summary>Plays a neutral reference-sized effect for legacy callers.</summary>
        public void Play()
        {
            Play(ReferenceVisualRadius, null);
        }

        public void Play(float radius)
        {
            Play(radius, null);
        }

        /// <summary>Plays every configured system once at the immutable firing team's blast radius.</summary>
        public void Play(float radius, ParticipantTeam? team)
        {
            if (played)
            {
                return;
            }

            played = true;
            transform.localScale = Vector3.one * ComputeVisualScale(radius);
            var systems = particleSystems;
            if (systems == null || systems.Length == 0)
            {
                systems = GetComponentsInChildren<ParticleSystem>(true);
            }

            var maximumLifetime = 0f;
            ParticleSystem flash = null;
            ParticleSystem blastRadiusCue = null;
            for (var i = 0; i < systems.Length; i++)
            {
                var particleSystem = systems[i];
                if (particleSystem != null && particleSystem.name == "Flash" && flash == null)
                {
                    flash = particleSystem;
                }
                else if (particleSystem != null && particleSystem.name == "BlastRadiusCue" && blastRadiusCue == null)
                {
                    blastRadiusCue = particleSystem;
                }
            }
            if (flash == null && systems.Length > 0)
            {
                flash = systems[0];
            }
            var flashColor = GetFlashColor(team);
            if (flash != null)
            {
                var flashMain = flash.main;
                flashMain.startColor = flashColor;
            }
            if (blastRadiusCue != null)
            {
                var cueMain = blastRadiusCue.main;
                cueMain.startColor = flashColor;
            }

            for (var i = 0; i < systems.Length; i++)
            {
                var particleSystem = systems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                var main = particleSystem.main;
                if (!main.loop)
                {
                    maximumLifetime = Mathf.Max(
                        maximumLifetime,
                        main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
                }

                particleSystem.Play(false);
            }

            Destroy(gameObject, Mathf.Max(maximumLifetime, 0.01f));
        }

        public static Color GetFlashColor(ParticipantTeam? team)
        {
            if (team == ParticipantTeam.Blue)
            {
                return BlueFlashColor;
            }

            if (team == ParticipantTeam.Red)
            {
                return RedFlashColor;
            }

            return NeutralFlashColor;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
