using UnityEngine;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Owns one VFX prefab reference and one-shot visual spawning.</summary>
    public sealed class ExplosionVfxSpawner : MonoBehaviour
    {
        [SerializeField] private ExplosionVfx explosionVfxPrefab;

        public void Play(Vector3 origin)
        {
            Play(origin, ExplosionVfx.ReferenceVisualRadius, null);
        }

        public void Play(Vector3 origin, float radius)
        {
            Play(origin, radius, null);
        }

        public void Play(Vector3 origin, float radius, ParticipantTeam? team)
        {
            if (explosionVfxPrefab == null)
            {
                return;
            }

            var explosionVfx = Instantiate(explosionVfxPrefab, origin, Quaternion.identity);
            explosionVfx?.Play(radius, team);
        }
    }
}
