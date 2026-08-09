using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Feedback;

namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Constant-velocity projectile. Collision only requests one explosion; it never applies direct impact force.</summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    [MovedFrom("RocketFooxball")]
    public sealed class RocketProjectile : MonoBehaviour
    {
        public enum ProjectileState
        {
            Flying,
            Detonated,
            Cancelled
        }

        [SerializeField, Min(1f)] private float speed = 48f;
        [SerializeField, Min(0.1f)] private float lifetime = 8f;
        [SerializeField] private Rigidbody body;
        [SerializeField] private Collider projectileCollider;
        [SerializeField] private ExplosionResolver explosionResolver;
        [SerializeField] private RocketTrailVfx trailVfx;

        private Transform ownerRoot;
        private RocketLauncher launcher;
        private Vector3 flightDirection = Vector3.forward;
        private float lifeRemaining;
        private ProjectileState state;
        private bool simulationEnabled = true;
        private bool unregistered;

        public Rigidbody Body => body;
        public Collider ProjectileCollider => projectileCollider;
        public Transform OwnerRoot => ownerRoot;
        public Vector3 Velocity => flightDirection * speed;
        public float Speed => speed;
        public bool IsDetonated => state == ProjectileState.Detonated;
        public ProjectileState State => state;

        private void Awake()
        {
            CacheReferences();
            ConfigureBody();
        }

        private void FixedUpdate()
        {
            if (!simulationEnabled || state != ProjectileState.Flying || body == null)
            {
                return;
            }

            var deltaTime = Time.fixedDeltaTime;
            lifeRemaining -= deltaTime;
            if (lifeRemaining <= 0f)
            {
                TryDetonate(null, default, false);
                return;
            }

            var distance = speed * deltaTime;
            if (distance > 0f && UnityEngine.Physics.Raycast(body.position, flightDirection, out var hit, distance + ColliderRadius(), ~0, QueryTriggerInteraction.Ignore))
            {
                if (!ShouldIgnore(hit.collider))
                {
                    TryDetonate(hit.collider, hit.point, true);
                    return;
                }
            }

            body.MovePosition(body.position + flightDirection * distance);
            body.linearVelocity = Vector3.zero;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (state != ProjectileState.Flying || collision == null || collision.collider == null)
            {
                return;
            }

            var point = collision.contactCount > 0 ? collision.GetContact(0).point : body.position;
            TryDetonate(collision.collider, point, true);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (state != ProjectileState.Flying)
            {
                return;
            }

            TryDetonate(other, other != null ? other.ClosestPoint(body != null ? body.position : transform.position) : default, other != null);
        }

        /// <summary>Initializes owner collision filters, world direction, and explosion callback.</summary>
        public void Initialize(Transform owner, RocketLauncher sourceLauncher, ExplosionResolver resolver, Vector3 direction)
        {
            CacheReferences();
            ownerRoot = owner;
            launcher = sourceLauncher;
            explosionResolver = resolver != null ? resolver : explosionResolver;
            flightDirection = direction.sqrMagnitude > 0.000001f ? direction.normalized : transform.forward;
            lifeRemaining = lifetime;
            state = ProjectileState.Flying;
            simulationEnabled = true;
            unregistered = false;
            ConfigureBody();
            IgnoreOwnerCollisions();
        }

        /// <summary>Registers a collision pair that must not detonate this rocket.</summary>
        public void IgnoreCollisionWith(Collider other)
        {
            if (projectileCollider != null && other != null && projectileCollider != other)
            {
                UnityEngine.Physics.IgnoreCollision(projectileCollider, other, true);
            }
        }

        /// <summary>Stops flight without changing transform; launcher normally destroys rockets on freeze.</summary>
        public void SetSimulationEnabled(bool enabled)
        {
            if (state == ProjectileState.Flying)
            {
                simulationEnabled = enabled;
            }
        }

        /// <summary>Requests one explosion and unregisters projectile before destruction.</summary>
        public bool TryDetonate(Collider hitCollider, Vector3 hitPoint, bool hasHitPoint)
        {
            if (state != ProjectileState.Flying)
            {
                return false;
            }
            if (hitCollider != null && ShouldIgnore(hitCollider))
            {
                return false;
            }

            state = ProjectileState.Detonated;
            simulationEnabled = false;
            var explosionPosition = hasHitPoint ? hitPoint : (body != null ? body.position : transform.position);
            UnregisterOnce();
            trailVfx?.DetachAndFade();
            explosionResolver?.ResolveExplosion(explosionPosition, this, hitCollider);
            Destroy(gameObject);
            return true;
        }

        /// <summary>Cancels terminal flight before deferred destruction; cancelled rockets cannot detonate.</summary>
        public void Cancel()
        {
            if (state != ProjectileState.Flying)
            {
                return;
            }

            state = ProjectileState.Cancelled;
            simulationEnabled = false;
            UnregisterOnce();
        }

        private void OnDestroy()
        {
            UnregisterOnce();
        }

        private void CacheReferences()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }
            if (projectileCollider == null)
            {
                projectileCollider = GetComponent<Collider>();
            }
            if (trailVfx == null)
            {
                trailVfx = GetComponentInChildren<RocketTrailVfx>(true);
            }
        }

        private void ConfigureBody()
        {
            if (body == null)
            {
                return;
            }

            body.useGravity = false;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = Vector3.zero;
        }

        private void IgnoreOwnerCollisions()
        {
            if (ownerRoot == null || projectileCollider == null)
            {
                return;
            }

            var ownerColliders = ownerRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < ownerColliders.Length; i++)
            {
                IgnoreCollisionWith(ownerColliders[i]);
            }
        }

        private bool ShouldIgnore(Collider other)
        {
            if (other == null || other == projectileCollider)
            {
                return true;
            }
            if (ownerRoot != null && other.transform.IsChildOf(ownerRoot))
            {
                return true;
            }
            return other.GetComponentInParent<RocketProjectile>() != null;
        }

        private float ColliderRadius()
        {
            if (projectileCollider == null)
            {
                return 0f;
            }

            var extents = projectileCollider.bounds.extents;
            return Mathf.Max(extents.x, extents.y, extents.z);
        }

        private void UnregisterOnce()
        {
            if (unregistered)
            {
                return;
            }

            unregistered = true;
            launcher?.UnregisterProjectile(this);
        }
    }
}
