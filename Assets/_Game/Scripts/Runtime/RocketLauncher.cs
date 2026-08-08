using System.Collections.Generic;
using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Held-fire owner. Tracks every active rocket and owns launcher cooldown/cleanup.</summary>
    public sealed class RocketLauncher : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerLook look;
        [SerializeField] private Camera aimCamera;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private RocketProjectile projectilePrefab;
        [SerializeField] private ExplosionResolver explosionResolver;

        [Header("Firing")]
        [SerializeField, Min(0.05f)] private float firingInterval = 0.70f;
        [SerializeField, Min(0f)] private float spawnOffset = 0.35f;

        private readonly List<RocketProjectile> activeProjectiles = new List<RocketProjectile>(16);
        private float cooldownRemaining;
        private bool simulationEnabled = true;

        public float CooldownRemaining => Mathf.Max(cooldownRemaining, 0f);
        public float FiringInterval => firingInterval;
        public int ActiveProjectileCount => activeProjectiles.Count;
        public bool SimulationEnabled => simulationEnabled;
        public bool CanFire => simulationEnabled && cooldownRemaining <= 0f && projectilePrefab != null;

        private void Awake()
        {
            CacheReferences();
        }

        private void FixedUpdate()
        {
            if (!simulationEnabled)
            {
                return;
            }

            cooldownRemaining = Mathf.Max(cooldownRemaining - Time.fixedDeltaTime, 0f);
            if (input != null && input.FireHeld && cooldownRemaining <= 0f)
            {
                LaunchRocket();
            }
        }

        /// <summary>Spawns one crosshair-authoritative rocket when cooldown permits.</summary>
        public bool LaunchRocket()
        {
            if (!CanFire)
            {
                return false;
            }

            var origin = GetAimOrigin();
            var direction = GetAimDirection();
            var spawnPosition = spawnPoint != null ? spawnPoint.position : origin;
            spawnPosition += direction * spawnOffset;
            var projectile = Object.Instantiate(projectilePrefab, spawnPosition, Quaternion.LookRotation(direction, Vector3.up));
            projectile.Initialize(transform, this, explosionResolver, direction);
            RegisterProjectile(projectile);
            cooldownRemaining = Mathf.Max(firingInterval, 0.01f);
            return true;
        }

        /// <summary>Compatibility alias for callers that use fire terminology.</summary>
        public bool Fire()
        {
            return LaunchRocket();
        }

        /// <summary>Stops fixed-step launches and leaves active projectile cleanup to match reset.</summary>
        public void SetSimulationEnabled(bool enabled)
        {
            simulationEnabled = enabled;
        }

        /// <summary>Compatibility alias for match freeze owners.</summary>
        public void SetSimulationFrozen(bool frozen)
        {
            SetSimulationEnabled(!frozen);
        }

        /// <summary>Clears cooldown and destroys all tracked rockets.</summary>
        public void ResetState()
        {
            cooldownRemaining = 0f;
            DestroyAllProjectiles();
        }

        /// <summary>Destroys active rockets immediately and clears tracking.</summary>
        public void DestroyAllProjectiles()
        {
            for (var i = activeProjectiles.Count - 1; i >= 0; i--)
            {
                var projectile = activeProjectiles[i];
                if (projectile != null)
                {
                    projectile.SetSimulationEnabled(false);
                    Object.Destroy(projectile.gameObject);
                }
            }
            activeProjectiles.Clear();
        }

        /// <summary>Registers projectile and ignores collision against every existing rocket.</summary>
        public void RegisterProjectile(RocketProjectile projectile)
        {
            if (projectile == null || activeProjectiles.Contains(projectile))
            {
                return;
            }

            for (var i = 0; i < activeProjectiles.Count; i++)
            {
                var other = activeProjectiles[i];
                if (other == null)
                {
                    continue;
                }
                var first = projectile.ProjectileCollider;
                var second = other.ProjectileCollider;
                if (first != null && second != null)
                {
                    Physics.IgnoreCollision(first, second, true);
                }
            }
            activeProjectiles.Add(projectile);
        }

        /// <summary>Removes projectile after detonation or external destruction.</summary>
        public void UnregisterProjectile(RocketProjectile projectile)
        {
            if (projectile != null)
            {
                activeProjectiles.Remove(projectile);
            }
        }

        public IReadOnlyList<RocketProjectile> ActiveProjectiles => activeProjectiles;

        private void CacheReferences()
        {
            if (input == null)
            {
                input = GetComponent<PlayerInputReader>();
            }
            if (look == null)
            {
                look = GetComponent<PlayerLook>();
            }
            if (aimCamera == null)
            {
                aimCamera = GetComponentInChildren<Camera>(true);
            }
        }

        private Vector3 GetAimOrigin()
        {
            if (aimCamera != null)
            {
                return aimCamera.transform.position;
            }
            if (look != null && look.Head != null)
            {
                return look.Head.position;
            }
            return transform.position + Vector3.up;
        }

        private Vector3 GetAimDirection()
        {
            if (aimCamera != null)
            {
                return aimCamera.transform.forward;
            }
            if (look != null && look.Head != null)
            {
                return look.Head.forward;
            }
            return transform.forward;
        }
    }
}
