using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Weapons;

namespace RocketFooxball.Runtime.Ball
{
    /// <summary>Single owner for ball Rigidbody velocity, impulses, contact assist, and reset state.</summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    [MovedFrom("RocketFooxball")]
    public sealed class BallMotor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody body;
        [SerializeField] private Collider ballCollider;
        [SerializeField] private PlayerMotor player;
        [SerializeField] private GoalShieldSet goalShieldSet;

        [Header("Speed and Surface")]
        [SerializeField, Min(1f)] private float baseSpeedReference = 10f;
        [SerializeField, Min(1f)] private float speedCapMultiplier = 4f;
        [SerializeField, Min(0f)] private float rollingResistance = 1.25f;
        [SerializeField, Min(0f)] private float restSpeed = 0.08f;
        [SerializeField, Min(0f)] private float contactAssistStrength = 0.35f;
        [SerializeField, Min(0f)] private float contactAssistImpulseCap = 5f;

        private const float Epsilon = 0.000001f;
        private Vector3 queuedContactAssistImpulse;
        private Vector3 queuedExternalImpulse;
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
            if (!ValidateComposition())
            {
                return;
            }

            ConfigureBody();
            IgnoreShieldCollisions();
        }

        private void OnEnable()
        {
            CacheReferences();
            if (!ValidateComposition())
            {
                return;
            }

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

            var queuedImpulse = queuedContactAssistImpulse + queuedExternalImpulse;
            queuedContactAssistImpulse = Vector3.zero;
            queuedExternalImpulse = Vector3.zero;
            if (queuedImpulse.sqrMagnitude > Epsilon)
            {
                body.AddForce(queuedImpulse, ForceMode.Impulse);
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
            if (!simulationEnabled || !BallMotionRules.IsFinite(impulse) || impulse.sqrMagnitude <= Epsilon)
            {
                return;
            }

            queuedExternalImpulse += impulse;
        }

        /// <summary>Applies aimed kick velocity while preserving useful incoming momentum.</summary>
        public bool ApplyKick(Vector3 aimDirection, Vector3 playerVelocity, float speedFraction = 0.91f, float playerMomentumShare = 0.20f)
        {
            if (body == null || !simulationEnabled || !BallMotionRules.IsFinite(aimDirection) || aimDirection.sqrMagnitude <= Epsilon)
            {
                return false;
            }

            // A successful kick supersedes contact-assist impulses accumulated while
            // the player was touching the ball, never external blast impulses.
            queuedContactAssistImpulse = Vector3.zero;
            body.linearVelocity = BallMotionRules.ApplyKick(body.linearVelocity, aimDirection, playerVelocity, HardCap, speedFraction, playerMomentumShare);
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
                ClearQueuedState();
            }
            else if (freezeStored)
            {
                body.isKinematic = preFreezeKinematic;
                body.linearVelocity = preFreezeVelocity;
                body.angularVelocity = preFreezeAngularVelocity;
                freezeStored = false;
            }
        }

        /// <summary>Clears pending impulses and contact state without moving the body.</summary>
        public void ClearQueuedState()
        {
            queuedContactAssistImpulse = Vector3.zero;
            queuedExternalImpulse = Vector3.zero;
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
        }

        private bool ValidateComposition()
        {
            if (body == null || ballCollider == null || player == null || goalShieldSet == null || goalShieldSet.Colliders == null)
            {
                Debug.LogError("BallMotor requires serialized references: body, ballCollider, player, goalShieldSet.", this);
                enabled = false;
                return false;
            }

            for (var i = 0; i < goalShieldSet.Colliders.Length; i++)
            {
                if (goalShieldSet.Colliders[i] == null)
                {
                    Debug.LogError("BallMotor requires serialized references: body, ballCollider, player, goalShieldSet.", this);
                    enabled = false;
                    return false;
                }
            }

            return true;
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
            if (ballCollider == null || goalShieldSet == null || goalShieldSet.Colliders == null)
            {
                return;
            }

            for (var i = 0; i < goalShieldSet.Colliders.Length; i++)
            {
                var shield = goalShieldSet.Colliders[i];
                if (shield != null && shield != ballCollider)
                {
                    UnityEngine.Physics.IgnoreCollision(ballCollider, shield, true);
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
            queuedContactAssistImpulse += direction * impulse;
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
            body.linearVelocity = BallMotionRules.ApplyRollingResistance(body.linearVelocity, rollingResistance, restSpeed, deltaTime);
        }

        private void ClampVelocity()
        {
            if (body == null)
            {
                return;
            }

            body.linearVelocity = BallMotionRules.ClampVelocity(body.linearVelocity, HardCap);
        }
    }
}
