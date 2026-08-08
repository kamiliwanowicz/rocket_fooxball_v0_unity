using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Constant-velocity projectile. Collision only requests one explosion; it never applies direct impact force.</summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public sealed class RocketProjectile : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float speed = 48f;
        [SerializeField, Min(0.1f)] private float lifetime = 8f;
        [SerializeField] private Rigidbody body;
        [SerializeField] private Collider projectileCollider;
        [SerializeField] private ExplosionResolver explosionResolver;

        private Transform ownerRoot;
        private RocketLauncher launcher;
        private Vector3 flightDirection = Vector3.forward;
        private float lifeRemaining;
        private bool detonated;
        private bool simulationEnabled = true;

        public Rigidbody Body => body;
        public Collider ProjectileCollider => projectileCollider;
        public Transform OwnerRoot => ownerRoot;
        public Vector3 Velocity => flightDirection * speed;
        public float Speed => speed;
        public bool IsDetonated => detonated;

        private void Awake()
        {
            CacheReferences();
            ConfigureBody();
        }

        private void FixedUpdate()
        {
            if (!simulationEnabled || detonated || body == null)
            {
                return;
            }

            var deltaTime = Time.fixedDeltaTime;
            lifeRemaining -= deltaTime;
            if (lifeRemaining <= 0f)
            {
                TryDetonate(null, body.position);
                return;
            }

            var distance = speed * deltaTime;
            if (distance > 0f && Physics.Raycast(body.position, flightDirection, out var hit, distance + ColliderRadius(), ~0, QueryTriggerInteraction.Ignore))
            {
                if (!ShouldIgnore(hit.collider))
                {
                    TryDetonate(hit.collider, hit.point);
                    return;
                }
            }

            body.MovePosition(body.position + flightDirection * distance);
            body.linearVelocity = Vector3.zero;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.collider == null)
            {
                return;
            }

            var point = collision.contactCount > 0 ? collision.GetContact(0).point : body.position;
            TryDetonate(collision.collider, point);
        }

        private void OnTriggerEnter(Collider other)
        {
            TryDetonate(other, other != null ? other.ClosestPoint(body != null ? body.position : transform.position) : transform.position);
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
            detonated = false;
            simulationEnabled = true;
            ConfigureBody();
            IgnoreOwnerCollisions();
        }

        /// <summary>Registers a collision pair that must not detonate this rocket.</summary>
        public void IgnoreCollisionWith(Collider other)
        {
            if (projectileCollider != null && other != null && projectileCollider != other)
            {
                Physics.IgnoreCollision(projectileCollider, other, true);
            }
        }

        /// <summary>Stops flight without changing transform; launcher normally destroys rockets on freeze.</summary>
        public void SetSimulationEnabled(bool enabled)
        {
            simulationEnabled = enabled;
        }

        /// <summary>Requests one explosion and unregisters projectile before destruction.</summary>
        public bool TryDetonate(Collider hitCollider, Vector3 hitPoint)
        {
            if (detonated)
            {
                return false;
            }
            if (hitCollider != null && ShouldIgnore(hitCollider))
            {
                return false;
            }

            detonated = true;
            simulationEnabled = false;
            var explosionPosition = hitPoint != Vector3.zero ? hitPoint : (body != null ? body.position : transform.position);
            launcher?.UnregisterProjectile(this);
            explosionResolver?.ResolveExplosion(explosionPosition, this, hitCollider);
            Destroy(gameObject);
            return true;
        }

        private void OnDestroy()
        {
            launcher?.UnregisterProjectile(this);
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
    }
}
