using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Physics;
using RocketFooxball.Runtime.Weapons;

namespace RocketFooxball.Runtime.Ball
{
    /// <summary>Single owner for ball Rigidbody velocity, impulses, contact assist, and reset state.</summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    [DefaultExecutionOrder(100)]
    [MovedFrom("RocketFooxball")]
    public sealed class BallMotor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody body;
        [SerializeField] private Collider ballCollider;
        [SerializeField] private ParticipantState[] participants = new ParticipantState[6];
        [SerializeField] private GoalShieldSet goalShieldSet;

        [Header("Speed and Surface")]
        [SerializeField, Min(1f)] private float baseSpeedReference = 10f;
        [SerializeField, Min(1f)] private float speedCapMultiplier = 4f;
        [SerializeField, Min(0f)] private float rollingResistance = 1.25f;
        [SerializeField, Min(0f)] private float restSpeed = 0.08f;
        [SerializeField, Min(0f)] private float contactAssistStrength = 0.35f;
        [SerializeField, Min(0f)] private float contactAssistImpulseCap = 5f;
        [SerializeField, Min(0f)] private float meaningfulContactSpeedThreshold = 1f;

        private const float Epsilon = 0.000001f;
        private const float AdditionalGravityFraction = 0.2f;
        private Vector3 queuedContactAssistImpulse;
        private Vector3 queuedExternalImpulse;
        private readonly HashSet<ulong> contactAssistParticipantIds = new HashSet<ulong>();
        private bool groundedContact;
        private bool simulationEnabled = true;
        private bool freezeStored;
        private bool preFreezeKinematic;
        private Vector3 preFreezeVelocity;
        private Vector3 preFreezeAngularVelocity;
        private bool paused;
        private bool pauseStored;
        private bool prePauseKinematic;
        private Vector3 prePauseVelocity;
        private Vector3 prePauseAngularVelocity;
        private Action<ControllerColliderHit>[] collisionHandlers;
        private Action<PlayerCollisionResolution>[] dynamicCollisionHandlers;
        private Action[] kickHandlers;
        private ParticipantState lastTouchParticipant;

        public Rigidbody Body => body;
        public Rigidbody Rigidbody => body;
        public Collider BallCollider => ballCollider;
        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;
        public float Speed => Velocity.magnitude;
        public float HardCap => Mathf.Max(1f, baseSpeedReference) * speedCapMultiplier;
        public bool IsGrounded => groundedContact;
        public bool SimulationEnabled => simulationEnabled;
        public float MeaningfulContactSpeedThreshold => meaningfulContactSpeedThreshold;
        public IReadOnlyList<ParticipantState> Participants => participants;
        /// <summary>Last valid roster participant to physically touch or kick the ball.</summary>
        public ParticipantState LastTouchParticipant => lastTouchParticipant;

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

            UnsubscribeParticipantHandlers();
            if (participants != null)
            {
                collisionHandlers = new Action<ControllerColliderHit>[participants.Length];
                dynamicCollisionHandlers = new Action<PlayerCollisionResolution>[participants.Length];
                kickHandlers = new Action[participants.Length];
                for (var i = 0; i < participants.Length; i++)
                {
                    var participant = participants[i];
                    if (participant == null)
                    {
                        continue;
                    }

                    var capturedParticipant = participant;
                    if (participant.Motor != null)
                    {
                        Action<ControllerColliderHit> handler = hit => OnPlayerCollisionHit(capturedParticipant, hit);
                        collisionHandlers[i] = handler;
                        participant.Motor.CollisionHit += handler;

                        Action<PlayerCollisionResolution> dynamicHandler = resolution => OnPlayerDynamicCollisionResolved(capturedParticipant, resolution);
                        dynamicCollisionHandlers[i] = dynamicHandler;
                        participant.Motor.DynamicCollisionResolved += dynamicHandler;
                    }
                    if (participant.Kick != null)
                    {
                        Action kickHandler = () => OnKickSucceeded(capturedParticipant);
                        kickHandlers[i] = kickHandler;
                        participant.Kick.KickSucceeded += kickHandler;
                    }
                }
            }
        }

        private void OnDisable()
        {
            SetPaused(false);
            UnsubscribeParticipantHandlers();
            ClearQueuedState();
        }

        private void FixedUpdate()
        {
            if (paused || body == null || !simulationEnabled)
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
            contactAssistParticipantIds.Clear();

            // Supported balls keep normal sleep behaviour instead of being woken by a force every step.
            if (!grounded && !body.IsSleeping())
            {
                body.AddForce(Vector3.down * (GamePhysicsSettings.GravityMagnitude * AdditionalGravityFraction), ForceMode.Acceleration);
            }

            if (grounded)
            {
                ApplyRollingResistance(Time.fixedDeltaTime);
            }

            ClampVelocity();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (paused)
            {
                return;
            }

            RecordGroundContact(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (paused)
            {
                return;
            }

            RecordGroundContact(collision);
        }

        /// <summary>Queues an additive impulse for next fixed-step ball simulation and reports acceptance.</summary>
        public bool QueueImpulse(Vector3 impulse)
        {
            if (paused || !simulationEnabled || !BallMotionRules.IsFinite(impulse) || impulse.sqrMagnitude <= Epsilon)
            {
                return false;
            }

            queuedExternalImpulse += impulse;
            return true;
        }

        /// <summary>Applies aimed kick velocity while preserving useful incoming momentum.</summary>
        public bool ApplyKick(Vector3 aimDirection, Vector3 playerVelocity, float speedFraction = 0.91f, float playerMomentumShare = 0.20f)
        {
            if (paused || body == null || !simulationEnabled || !BallMotionRules.IsFinite(aimDirection) || aimDirection.sqrMagnitude <= Epsilon)
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
            if (!enabled && paused)
            {
                SetPaused(false);
            }

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
                RestoreBodyState(body, preFreezeKinematic, preFreezeVelocity, preFreezeAngularVelocity);
                freezeStored = false;
                preFreezeVelocity = Vector3.zero;
                preFreezeAngularVelocity = Vector3.zero;
            }
        }

        /// <summary>Freezes the Rigidbody while preserving queued impulses, contacts, transform, and velocity.</summary>
        public void SetPaused(bool pausedState)
        {
            if (paused == pausedState)
            {
                return;
            }

            paused = pausedState;
            if (body == null)
            {
                pauseStored = false;
                return;
            }

            if (paused)
            {
                pauseStored = true;
                prePauseKinematic = body.isKinematic;
                prePauseVelocity = body.linearVelocity;
                prePauseAngularVelocity = body.angularVelocity;
                body.isKinematic = true;
                return;
            }

            if (!pauseStored)
            {
                return;
            }

            RestoreBodyState(body, prePauseKinematic, prePauseVelocity, prePauseAngularVelocity);
            pauseStored = false;
            prePauseVelocity = Vector3.zero;
            prePauseAngularVelocity = Vector3.zero;
        }

        /// <summary>Clears pending impulses and contact state without moving the body.</summary>
        public void ClearQueuedState()
        {
            queuedContactAssistImpulse = Vector3.zero;
            queuedExternalImpulse = Vector3.zero;
            contactAssistParticipantIds.Clear();
            groundedContact = false;
        }

        /// <summary>Resets body position and both velocity channels.</summary>
        public void ResetState(Vector3 worldPosition, Quaternion worldRotation)
        {
            SetPaused(false);
            lastTouchParticipant = null;
            if (body == null)
            {
                transform.SetPositionAndRotation(worldPosition, worldRotation);
                return;
            }

            body.position = worldPosition;
            body.rotation = worldRotation;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.Sleep();
            // A reset invalidates the motion captured by a preceding freeze, but
            // keeps the stored kinematic mode so ordinary freeze/unfreeze still
            // restores its body configuration. Re-enable therefore starts from
            // this reset frame instead of replaying scoring-frame velocity.
            preFreezeVelocity = Vector3.zero;
            preFreezeAngularVelocity = Vector3.zero;
            prePauseVelocity = Vector3.zero;
            prePauseAngularVelocity = Vector3.zero;
            ClearQueuedState();
        }

        /// <summary>Records a valid roster participant as ball touch owner for goal attribution.</summary>
        public bool RecordParticipantTouch(ParticipantState participant)
        {
            if (paused || participant == null || !IsRosterParticipant(participant))
            {
                return false;
            }

            lastTouchParticipant = participant;
            return true;
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
            if (body == null || ballCollider == null || participants == null || participants.Length == 0 || goalShieldSet == null || goalShieldSet.Colliders == null)
            {
                Debug.LogError("BallMotor requires serialized references: body, ballCollider, participants, goalShieldSet.", this);
                enabled = false;
                return false;
            }

            for (var i = 0; i < goalShieldSet.Colliders.Length; i++)
            {
                if (goalShieldSet.Colliders[i] == null)
                {
                    Debug.LogError("BallMotor requires serialized references: body, ballCollider, participants, goalShieldSet.", this);
                    enabled = false;
                    return false;
                }
            }

            for (var i = 0; i < participants.Length; i++)
            {
                if (participants[i] == null || participants[i].Motor == null)
                {
                    Debug.LogError("BallMotor requires serialized references: each participants entry and its motor.", this);
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

        private void OnPlayerCollisionHit(ParticipantState participant, ControllerColliderHit hit)
        {
            if (hit == null || hit.collider == null)
            {
                return;
            }
            if (hit.collider != ballCollider && hit.collider.attachedRigidbody != body)
            {
                return;
            }

            RecordParticipantTouch(participant);
        }

        private void OnPlayerDynamicCollisionResolved(ParticipantState participant, PlayerCollisionResolution resolution)
        {
            var hit = resolution.Hit;
            if (hit == null || hit.collider == null)
            {
                return;
            }
            if (hit.collider != ballCollider && hit.collider.attachedRigidbody != body)
            {
                return;
            }

            // Attribution must happen even when the contact has no usable impulse.
            RecordParticipantTouch(participant);
            if (paused || !simulationEnabled || body == null || resolution.WasDashing)
            {
                return;
            }

            if (participant == null || participant.Motor == null)
            {
                return;
            }

            var entityId = participant.GetEntityId();
            var participantId = EntityId.ToULong(entityId);
            if (!entityId.IsValid())
            {
                participantId = unchecked((ulong)(uint)participant.GetInstanceID());
            }
            if (!BallMotionRules.ShouldAcceptParticipantContact(contactAssistParticipantIds, participantId))
            {
                return;
            }

            var effectiveBallVelocity = body.linearVelocity + queuedContactAssistImpulse;
            var candidate = BallMotionRules.ComputeContactAssist(
                resolution.IncomingPlayerVelocity,
                effectiveBallVelocity,
                hit.normal,
                GamePhysicsSettings.BallContactAssistPerContactCap,
                GamePhysicsSettings.PlayerCollisionTransferFraction);
            queuedContactAssistImpulse = BallMotionRules.AccumulateContactAssist(
                queuedContactAssistImpulse,
                candidate,
                GamePhysicsSettings.BallContactAssistAggregateCap);

            var relativeSpeed = (resolution.IncomingPlayerVelocity - effectiveBallVelocity).magnitude;
            if (relativeSpeed >= meaningfulContactSpeedThreshold)
            {
                participant.NotifyMeaningfulBallContact();
            }
        }

        private void OnKickSucceeded(ParticipantState participant)
        {
            if (paused)
            {
                return;
            }

            RecordParticipantTouch(participant);
        }

        private bool IsRosterParticipant(ParticipantState participant)
        {
            if (participants == null)
            {
                return false;
            }

            for (var i = 0; i < participants.Length; i++)
            {
                if (participants[i] == participant)
                {
                    return true;
                }
            }
            return false;
        }

        private void UnsubscribeParticipantHandlers()
        {
            if (participants == null)
            {
                collisionHandlers = null;
                dynamicCollisionHandlers = null;
                kickHandlers = null;
                return;
            }

            for (var i = 0; i < participants.Length; i++)
            {
                var participant = participants[i];
                if (participant == null)
                {
                    continue;
                }

                if (collisionHandlers != null && i < collisionHandlers.Length && collisionHandlers[i] != null && participant.Motor != null)
                {
                    participant.Motor.CollisionHit -= collisionHandlers[i];
                }
                if (dynamicCollisionHandlers != null && i < dynamicCollisionHandlers.Length && dynamicCollisionHandlers[i] != null && participant.Motor != null)
                {
                    participant.Motor.DynamicCollisionResolved -= dynamicCollisionHandlers[i];
                }
                if (kickHandlers != null && i < kickHandlers.Length && kickHandlers[i] != null && participant.Kick != null)
                {
                    participant.Kick.KickSucceeded -= kickHandlers[i];
                }
            }

            collisionHandlers = null;
            dynamicCollisionHandlers = null;
            kickHandlers = null;
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

        private static void RestoreBodyState(Rigidbody rigidbody, bool kinematic, Vector3 velocity, Vector3 angularVelocity)
        {
            rigidbody.isKinematic = kinematic;
            if (!rigidbody.isKinematic)
            {
                rigidbody.linearVelocity = velocity;
                rigidbody.angularVelocity = angularVelocity;
            }
        }
    }
}
