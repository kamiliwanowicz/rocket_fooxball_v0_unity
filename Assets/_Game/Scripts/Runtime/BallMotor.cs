using System;
using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Single owner for ball Rigidbody velocity, impulses, contact assist, and reset state.</summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public sealed class BallMotor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody body;
        [SerializeField] private Collider ballCollider;
        [SerializeField] private PlayerMotor player;
        [SerializeField] private Collider[] goalShieldColliders;

        [Header("Speed and Surface")]
        [SerializeField, Min(1f)] private float baseSpeedReference = 10f;
        [SerializeField, Min(1f)] private float speedCapMultiplier = 4f;
        [SerializeField, Min(0f)] private float rollingResistance = 1.25f;
        [SerializeField, Min(0f)] private float restSpeed = 0.08f;
        [SerializeField, Min(0f)] private float contactAssistStrength = 0.35f;
        [SerializeField, Min(0f)] private float contactAssistImpulseCap = 5f;

        private const float Epsilon = 0.000001f;
        private Vector3 queuedImpulse;
        private bool groundedContact;
        private bool simulationEnabled = true;
        private bool freezeStored;
        private bool preFreezeKinematic;
        private Vector3 preFreezeVelocity;
        private Vector3 preFreezeAngularVelocity;

        public Rigidbody Body => body;
        public Rigidbody Rigidbody => body;
        public Collider BallCollider => ballCollider;
        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;
        public float Speed => Velocity.magnitude;
        public float HardCap => Mathf.Max(1f, player != null ? player.BaseSpeed : baseSpeedReference) * speedCapMultiplier;
        public bool IsGrounded => groundedContact;
        public bool SimulationEnabled => simulationEnabled;

        private void Awake()
        {
            CacheReferences();
            ConfigureBody();
            IgnoreShieldCollisions();
        }

        private void OnEnable()
        {
            CacheReferences();
            if (player != null)
            {
                player.CollisionHit += OnPlayerCollisionHit;
            }
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.CollisionHit -= OnPlayerCollisionHit;
            }
        }

        private void FixedUpdate()
        {
            if (body == null || !simulationEnabled)
            {
                return;
            }

            var grounded = groundedContact;
            groundedContact = false;

            if (queuedImpulse.sqrMagnitude > Epsilon)
            {
                body.AddForce(queuedImpulse, ForceMode.Impulse);
                queuedImpulse = Vector3.zero;
            }

            if (grounded)
            {
                ApplyRollingResistance(Time.fixedDeltaTime);
            }

            ClampVelocity();
        }

        private void OnCollisionEnter(Collision collision)
        {
            RecordGroundContact(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            RecordGroundContact(collision);
        }

        /// <summary>Queues an additive impulse for next fixed-step ball simulation.</summary>
        public void QueueImpulse(Vector3 impulse)
        {
            if (!simulationEnabled || !IsFinite(impulse) || impulse.sqrMagnitude <= Epsilon)
            {
                return;
            }

            queuedImpulse += impulse;
        }

        /// <summary>Compatibility alias for explosion and kick owners.</summary>
        public void AddExternalImpulse(Vector3 impulse)
        {
            QueueImpulse(impulse);
        }

        /// <summary>Applies aimed kick velocity while preserving useful incoming momentum.</summary>
        public bool ApplyKick(Vector3 aimDirection, Vector3 playerVelocity, float speedFraction = 0.91f, float playerMomentumShare = 0.20f)
        {
            if (body == null || !simulationEnabled || !IsFinite(aimDirection) || aimDirection.sqrMagnitude <= Epsilon)
            {
                return false;
            }

            var direction = aimDirection.normalized;
            var current = body.linearVelocity;
            var opposing = Vector3.Dot(current, direction);
            if (opposing < 0f)
            {
                current -= direction * opposing * 0.65f;
            }

            var momentum = Mathf.Clamp(Vector3.Dot(playerVelocity, direction), 0f, HardCap * 0.25f) * Mathf.Clamp01(playerMomentumShare);
            var kickVelocity = HardCap * Mathf.Clamp(speedFraction, 0f, 1f);
            // A successful kick supersedes contact-assist impulses accumulated while
            // the player was touching the ball. Letting those fire one step later
            // made high-speed kicks feel delayed and unpredictable.
            queuedImpulse = Vector3.zero;
            body.linearVelocity = current + direction * (kickVelocity + momentum);
            ClampVelocity();
            return true;
        }

        /// <summary>Enables or freezes body simulation while retaining scoring-frame transform and velocity.</summary>
        public void SetSimulationEnabled(bool enabled)
        {
            if (simulationEnabled == enabled)
            {
                return;
            }

            simulationEnabled = enabled;
            if (body == null)
            {
                return;
            }

            if (!enabled)
            {
                freezeStored = true;
                preFreezeKinematic = body.isKinematic;
                preFreezeVelocity = body.linearVelocity;
                preFreezeAngularVelocity = body.angularVelocity;
                body.isKinematic = true;
                queuedImpulse = Vector3.zero;
            }
            else if (freezeStored)
            {
                body.isKinematic = preFreezeKinematic;
                body.linearVelocity = preFreezeVelocity;
                body.angularVelocity = preFreezeAngularVelocity;
                freezeStored = false;
            }
        }

        /// <summary>Compatibility alias for match freeze owners.</summary>
        public void SetSimulationFrozen(bool frozen)
        {
            SetSimulationEnabled(!frozen);
        }

        /// <summary>Clears pending impulses and contact state without moving the body.</summary>
        public void ClearQueuedState()
        {
            queuedImpulse = Vector3.zero;
            groundedContact = false;
        }

        /// <summary>Resets body position and both velocity channels.</summary>
        public void ResetState(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (body == null)
            {
                transform.SetPositionAndRotation(worldPosition, worldRotation);
                return;
            }

            body.position = worldPosition;
            body.rotation = worldRotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();
            // A reset invalidates the motion captured by a preceding freeze, but
            // keeps the stored kinematic mode so ordinary freeze/unfreeze still
            // restores its body configuration. Re-enable therefore starts from
            // this reset frame instead of replaying scoring-frame velocity.
            preFreezeVelocity = Vector3.zero;
            preFreezeAngularVelocity = Vector3.zero;
            ClearQueuedState();
        }

        /// <summary>Compatibility alias for reset owners.</summary>
        public void ResetBall(Vector3 worldPosition)
        {
            ResetState(worldPosition, Quaternion.identity);
        }

        /// <summary>Sets shield colliders ignored by ball physics and ball-directed blast occlusion.</summary>
        public void SetShieldColliders(Collider[] shields)
        {
            goalShieldColliders = shields;
            IgnoreShieldCollisions();
        }

        private void CacheReferences()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }
            if (ballCollider == null)
            {
                ballCollider = GetComponent<Collider>();
            }
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerMotor>();
            }
        }

        private void ConfigureBody()
        {
            if (body == null)
            {
                return;
            }

            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void IgnoreShieldCollisions()
        {
            if (ballCollider == null || goalShieldColliders == null)
            {
                return;
            }

            for (var i = 0; i < goalShieldColliders.Length; i++)
            {
                var shield = goalShieldColliders[i];
                if (shield != null && shield != ballCollider)
                {
                    Physics.IgnoreCollision(ballCollider, shield, true);
                }
            }
        }

        private void OnPlayerCollisionHit(ControllerColliderHit hit)
        {
            if (!simulationEnabled || hit == null || hit.collider == null || body == null)
            {
                return;
            }
            if (hit.collider != ballCollider && hit.collider.attachedRigidbody != body)
            {
                return;
            }

            var playerVelocity = player != null ? player.Velocity : Vector3.zero;
            var speed = new Vector3(playerVelocity.x, 0f, playerVelocity.z).magnitude;
            if (speed <= Epsilon)
            {
                return;
            }

            var direction = playerVelocity.normalized;
            var impulse = Mathf.Min(contactAssistImpulseCap, speed * contactAssistStrength);
            QueueImpulse(direction * impulse);
        }

        private void RecordGroundContact(Collision collision)
        {
            if (collision == null || collision.contactCount == 0)
            {
                return;
            }

            for (var i = 0; i < collision.contactCount; i++)
            {
                var normal = collision.GetContact(i).normal;
                if (normal.y > 0.25f)
                {
                    groundedContact = true;
                    return;
                }
            }
        }

        private void ApplyRollingResistance(float deltaTime)
        {
            var velocity = body.linearVelocity;
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            var nextMagnitude = Mathf.MoveTowards(horizontal.magnitude, 0f, rollingResistance * Mathf.Max(deltaTime, 0f));
            if (nextMagnitude <= restSpeed)
            {
                horizontal = Vector3.zero;
            }
            else if (horizontal.sqrMagnitude > Epsilon)
            {
                horizontal = horizontal.normalized * nextMagnitude;
            }
            body.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
        }

        private void ClampVelocity()
        {
            if (body == null)
            {
                return;
            }

            body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, HardCap);
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
