using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Held-fire owner. Tracks every active rocket and owns launcher cooldown/cleanup.</summary>
    [MovedFrom("RocketFooxball")]
    public sealed class RocketLauncher : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerLook look;
        [SerializeField] private Camera aimCamera;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private RocketProjectile projectilePrefab;
        [SerializeField] private ExplosionResolver explosionResolver;
        [SerializeField] private WeaponImpactFeedback impactFeedback;
        [SerializeField] private ParticipantState ownerParticipant;

        [Header("Firing")]
        [SerializeField, Min(0.05f)] private float firingInterval = 0.90f;
        [SerializeField, Min(0f)] private float spawnOffset = 0.35f;

        private readonly List<RocketProjectile> activeProjectiles = new List<RocketProjectile>(16);
        private float cooldownRemaining;
        private bool simulationEnabled = true;
        private bool paused;
        private bool requestPending;
        private Vector3 requestedLaunchPosition;
        private Vector3 requestedDirection;

        public float CooldownRemaining => Mathf.Max(cooldownRemaining, 0f);
        public float FiringInterval => firingInterval;
        public int ActiveProjectileCount => activeProjectiles.Count;
        public bool SimulationEnabled => simulationEnabled;
        public bool CanFire => !paused && simulationEnabled && cooldownRemaining <= 0f && projectilePrefab != null;
        public float ProjectileSpeed => projectilePrefab != null && IsFinite(projectilePrefab.Speed) && projectilePrefab.Speed > 0f
            ? projectilePrefab.Speed
            : 0f;
        public ParticipantState OwnerParticipant => ownerParticipant;

        /// <summary>Raised once after a projectile is initialized, registered, and cooldown is assigned.</summary>
        public event System.Action RocketLaunched;

        private void Awake()
        {
            CacheReferences();
        }

        private void OnDisable()
        {
            SetPaused(false);
            ClearProgrammaticRequest();
        }

        private void FixedUpdate()
        {
            if (paused)
            {
                return;
            }

            if (!simulationEnabled)
            {
                ClearProgrammaticRequest();
                return;
            }

            cooldownRemaining = Mathf.Max(cooldownRemaining - Time.fixedDeltaTime, 0f);
            var useProgrammaticRequest = requestPending;
            var launchPosition = requestedLaunchPosition;
            var direction = requestedDirection;
            ClearProgrammaticRequest();

            if (useProgrammaticRequest)
            {
                TryLaunchRocket(launchPosition, direction);
            }
            else if (input != null && input.FireHeld)
            {
                LaunchRocket();
            }
        }

        /// <summary>Spawns one crosshair-authoritative rocket when cooldown permits.</summary>
        public bool LaunchRocket()
        {
            if (paused)
            {
                return false;
            }

            var origin = GetAimOrigin();
            var direction = GetAimDirection();
            if (!TryGetLaunchPosition(origin, direction, out var launchPosition))
            {
                return false;
            }

            return TryLaunchRocket(launchPosition, direction.normalized);
        }

        /// <summary>Queues the latest valid one-step rocket launch pose.</summary>
        public bool RequestFire(Vector3 launchPosition, Vector3 direction)
        {
            if (paused || !isActiveAndEnabled || !simulationEnabled || !IsFinite(launchPosition) ||
                !IsFinite(direction) || direction.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            requestedLaunchPosition = launchPosition;
            requestedDirection = direction.normalized;
            requestPending = true;
            return true;
        }

        /// <summary>Resolves the exact world launch position from a fallback aim origin and direction.</summary>
        public bool TryGetLaunchPosition(Vector3 fallbackOrigin, Vector3 direction, out Vector3 launchPosition)
        {
            launchPosition = Vector3.zero;
            if (paused || !isActiveAndEnabled || !simulationEnabled || !IsFinite(fallbackOrigin) ||
                !IsFinite(direction) || direction.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            var basePosition = spawnPoint != null ? spawnPoint.position : fallbackOrigin;
            if (!IsFinite(basePosition))
            {
                return false;
            }

            var offset = IsFinite(spawnOffset) ? Mathf.Max(spawnOffset, 0f) : 0f;
            launchPosition = basePosition + direction.normalized * offset;
            return IsFinite(launchPosition);
        }

        private bool TryLaunchRocket(Vector3 launchPosition, Vector3 direction)
        {
            if (!CanFire || !IsFinite(launchPosition) || !IsFinite(direction) || direction.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            var projectile = Object.Instantiate(projectilePrefab, launchPosition, Quaternion.LookRotation(direction, Vector3.up));
            projectile.Initialize(ownerParticipant, transform, this, explosionResolver, impactFeedback, direction);
            RegisterProjectile(projectile);
            cooldownRemaining = Mathf.Max(firingInterval, 0.01f);
            ownerParticipant?.CancelImmunity();
            RocketLaunched?.Invoke();
            return true;
        }

        /// <summary>Stops fixed-step launches and leaves active projectile cleanup to match reset.</summary>
        public void SetSimulationEnabled(bool enabled)
        {
            if (!enabled && paused)
            {
                SetPaused(false);
            }

            simulationEnabled = enabled;
            if (!enabled)
            {
                ClearProgrammaticRequest();
            }
        }

        /// <summary>Freezes launcher fire processing and forwards the pause to all current and future rockets.</summary>
        public void SetPaused(bool pausedState)
        {
            if (paused == pausedState)
            {
                return;
            }

            paused = pausedState;
            if (paused)
            {
                ClearProgrammaticRequest();
            }

            for (var i = 0; i < activeProjectiles.Count; i++)
            {
                activeProjectiles[i]?.SetPaused(pausedState);
            }
        }

        /// <summary>Clears cooldown and destroys all tracked rockets.</summary>
        public void ResetState()
        {
            SetPaused(false);
            cooldownRemaining = 0f;
            ClearProgrammaticRequest();
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
                    projectile.Cancel();
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
                    UnityEngine.Physics.IgnoreCollision(first, second, true);
                }
            }
            activeProjectiles.Add(projectile);
            projectile.SetPaused(paused);
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
            if (ownerParticipant == null)
            {
                ownerParticipant = GetComponent<ParticipantState>();
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

        private void ClearProgrammaticRequest()
        {
            requestPending = false;
            requestedLaunchPosition = Vector3.zero;
            requestedDirection = Vector3.zero;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
