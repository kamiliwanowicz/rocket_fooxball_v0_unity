using UnityEngine;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Owns one VFX prefab reference and one-shot visual spawning.</summary>
    public sealed class ExplosionVfxSpawner : MonoBehaviour
    {
        [SerializeField] private ExplosionVfx explosionVfxPrefab;

        public void Play(Vector3 origin)
        {
            if (explosionVfxPrefab == null)
            {
                return;
            }

            var explosionVfx = Instantiate(explosionVfxPrefab, origin, Quaternion.identity);
            explosionVfx?.Play();
        }
    }
}
