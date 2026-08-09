using System;
using UnityEngine;

namespace RocketFooxball
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Ground Movement")]
        [SerializeField, Min(1f)] private float baseSpeed = 10f;
        [SerializeField, Min(1f)] private float groundAcceleration = 80f;
        [SerializeField, Min(1f)] private float groundDeceleration = 50f;
        [SerializeField, Min(0f)] private float overspeedFriction = 20f;
        [SerializeField, Min(0f)] private float turnScrubRate = 18f;
        [SerializeField, Min(0f)] private float overspeedTurnPenalty = 1.5f;
        [SerializeField, Min(1f)] private float groundStrafePower = 1.5f;
        [SerializeField, Min(0f)] private float groundSteerRateDegrees = 540f;

        [Header("Air Movement")]
        [SerializeField, Min(0f)] private float airAcceleration = 60f;
        [SerializeField, Min(0f)] private float airStrafeWishSpeed = 3f;
        [SerializeField, Min(1f)] private float airStrafePower = 2f;
        [SerializeField, Min(0f)] private float airSteerRateDegrees = 420f;
        [SerializeField, Range(0f, 1f)] private float airForwardScale = 0.35f;
        [SerializeField, Min(1f)] private float bhopSoftCapMultiplier = 2.5f;
        [SerializeField, Min(1f)] private float hardCapMultiplier = 3f;

        [Header("Jump")]
        [SerializeField, Min(0f)] private float jumpVelocity = 4.80f;
        [SerializeField, Min(1)] private int jumpsToHardCap = 4;
        [SerializeField, Min(0f)] private float coyoteTime = 0.08f;
        [SerializeField, Min(0f)] private float jumpBufferTime = 0.10f;
        [SerializeField] private PlayerInputReader input;

        private const float MaxGroundedFallVelocity = -0.1f;
        private const float Epsilon = 0.000001f;
        private CharacterController controller;
        private Vector3 velocity;
        private Vector3 queuedExternalImpulse;
        private Vector3 groundNormal = Vector3.up;
        private Vector3 groundNormalThisStep = Vector3.up;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private bool hasGroundContact;
        private bool groundContactThisStep;
        private bool simulationEnabled = true;

        /// <summary>Raised during CharacterController collision dispatch with actual contact data.</summary>
        public event Action<ControllerColliderHit> CollisionHit;

        public Vector3 Velocity => velocity;
        public float BaseSpeed => baseSpeed;
        public float SoftCap => baseSpeed * bhopSoftCapMultiplier;
        public float HardCap => baseSpeed * hardCapMultiplier;
        public float HorizontalSpeed => new Vector3(velocity.x, 0f, velocity.z).magnitude;
        public bool IsGrounded => controller != null && controller.isGrounded;
        public bool HasGroundContact => hasGroundContact;
        public Vector3 GroundNormal => hasGroundContact ? groundNormal : Vector3.up;
        public bool SimulationEnabled => simulationEnabled;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void FixedUpdate()
        {
            if (controller == null)
            {
                return;
            }

            if (!simulationEnabled)
            {
                input?.ClearGameplayState();
                queuedExternalImpulse = Vector3.zero;
                return;
            }

            var deltaTime = Time.fixedDeltaTime;
            var grounded = controller.isGrounded || hasGroundContact;
            var activeGroundNormal = hasGroundContact ? groundNormal : Vector3.up;

            groundContactThisStep = false;
            groundNormalThisStep = Vector3.up;

            if (input != null && input.ConsumeJumpPressed())
            {
                jumpBufferTimer = jumpBufferTime;
            }

            if (grounded)
            {
                coyoteTimer = coyoteTime;
            }
            else
            {
                coyoteTimer = Mathf.Max(coyoteTimer - deltaTime, 0f);
            }
            jumpBufferTimer = Mathf.Max(jumpBufferTimer - deltaTime, 0f);

            var move = input != null ? input.Move : Vector2.zero;
            var strafeDirection = Mathf.Abs(move.x) > 0.001f ? transform.right * Mathf.Sign(move.x) : Vector3.zero;
            var forwardDirection = Mathf.Abs(move.y) > 0.001f ? transform.forward * Mathf.Sign(move.y) : Vector3.zero;
            var jumpedThisStep = TryConsumeJump(grounded, strafeDirection + forwardDirection);

            if (grounded && !jumpedThisStep)
            {
                ApplyGroundMovement(strafeDirection, forwardDirection, activeGroundNormal, deltaTime);
                if (!hasGroundContact || activeGroundNormal.y >= 0.9999f)
                {
                    velocity.y = Mathf.Max(velocity.y, MaxGroundedFallVelocity);
                }
            }
            else
            {
                ApplyAirMovement(strafeDirection, forwardDirection, deltaTime);
                velocity.y -= GamePhysicsSettings.GravityMagnitude * deltaTime;
            }

            ApplyQueuedExternalImpulse();
            velocity = MovementMath.ClampHorizontal(velocity, HardCap);
            controller.Move(velocity * deltaTime);
            ResolveGroundContactAfterMove();
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit == null)
            {
                return;
            }

            var walkable = controller != null && MovementMath.IsWalkableNormal(hit.normal, controller.slopeLimit);
            if (walkable)
            {
                if (!groundContactThisStep || hit.normal.y > groundNormalThisStep.y)
                {
                    groundNormalThisStep = hit.normal.normalized;
                }
                groundContactThisStep = true;
            }
            else
            {
                var intoSurface = Vector3.Dot(velocity, hit.normal);
                if (intoSurface < 0f)
                {
                    velocity -= hit.normal * intoSurface;
                }
            }

            CollisionHit?.Invoke(hit);
        }

        /// <summary>Queues an additive fixed-step impulse. Invalid or gated impulses are ignored.</summary>
        public void AddExternalImpulse(Vector3 impulse)
        {
            if (!simulationEnabled || !IsFinite(impulse) || impulse.sqrMagnitude <= Epsilon)
            {
                return;
            }

            queuedExternalImpulse += impulse;
        }

        /// <summary>Compatibility alias for gameplay owners requesting a rocket/blast impulse.</summary>
        public void QueueExternalImpulse(Vector3 impulse)
        {
            AddExternalImpulse(impulse);
        }

        /// <summary>Enables or freezes fixed-step player simulation without changing transform or velocity.</summary>
        public void SetSimulationEnabled(bool enabled)
        {
            simulationEnabled = enabled;
            if (!enabled)
            {
                queuedExternalImpulse = Vector3.zero;
                coyoteTimer = 0f;
                jumpBufferTimer = 0f;
                input?.ClearGameplayState();
            }
        }

        /// <summary>Compatibility alias for match freeze owners.</summary>
        public void SetSimulationFrozen(bool frozen)
        {
            SetSimulationEnabled(!frozen);
        }

        /// <summary>Clears movement state without moving the player.</summary>
        public void ClearQueuedState()
        {
            queuedExternalImpulse = Vector3.zero;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            groundContactThisStep = false;
            hasGroundContact = false;
            groundNormal = Vector3.up;
            groundNormalThisStep = Vector3.up;
        }

        /// <summary>Resets position, facing, velocity, jump buffers, impulses, and collision state.</summary>
        public void ResetState(Vector3 worldPosition, Quaternion worldRotation)
        {
            var wasControllerEnabled = controller != null && controller.enabled;
            if (controller != null && wasControllerEnabled)
            {
                controller.enabled = false;
            }

            transform.SetPositionAndRotation(worldPosition, worldRotation);

            if (controller != null && wasControllerEnabled)
            {
                controller.enabled = true;
            }

            velocity = Vector3.zero;
            ClearQueuedState();
            input?.ClearGameplayState();
        }

        /// <summary>Compatibility alias for reset owners.</summary>
        public void ResetMotion(Vector3 worldPosition, Quaternion worldRotation)
        {
            ResetState(worldPosition, worldRotation);
        }

        private void ApplyGroundMovement(Vector3 strafeDirection, Vector3 forwardDirection, Vector3 activeGroundNormal, float deltaTime)
        {
            var hasRampNormal = hasGroundContact && activeGroundNormal.y < 0.9999f;
            var horizontal = hasRampNormal
                ? MovementMath.ProjectOnPlanePreserveMagnitude(velocity, activeGroundNormal)
                : HorizontalVelocity();
            var wishDirection = strafeDirection + forwardDirection;
            var hasStrafe = strafeDirection.sqrMagnitude > Epsilon;
            var strafePower = hasStrafe ? groundStrafePower : 1f;

            if (hasRampNormal)
            {
                wishDirection = MovementMath.ProjectDirectionOnPlane(wishDirection, activeGroundNormal);
            }

            if (wishDirection.sqrMagnitude <= Epsilon)
            {
                horizontal = MovementMath.ApplyFriction(horizontal, 0f, groundDeceleration, deltaTime);
            }
            else
            {
                wishDirection.Normalize();
                horizontal = MovementMath.TurnScrub(horizontal, wishDirection, baseSpeed, turnScrubRate, overspeedTurnPenalty, deltaTime);
                horizontal = MovementMath.ApplyFriction(horizontal, baseSpeed, overspeedFriction, deltaTime);
                horizontal = MovementMath.Accelerate(horizontal, wishDirection, baseSpeed, groundAcceleration * strafePower, deltaTime);
                horizontal = MovementMath.SteerToward(horizontal, wishDirection, groundSteerRateDegrees * strafePower * Mathf.Deg2Rad, deltaTime);
            }

            if (hasRampNormal)
            {
                velocity = horizontal;
            }
            else
            {
                SetHorizontalVelocity(horizontal);
            }
        }

        private void ApplyAirMovement(Vector3 strafeDirection, Vector3 forwardDirection, float deltaTime)
        {
            var horizontal = HorizontalVelocity();
            var scale = MovementMath.AirAccelerationScale(horizontal.magnitude, SoftCap, HardCap);
            var hasStrafe = strafeDirection.sqrMagnitude > Epsilon;
            var strafePower = hasStrafe ? airStrafePower : 1f;
            if (hasStrafe)
            {
                horizontal = MovementMath.Accelerate(horizontal, strafeDirection, airStrafeWishSpeed * strafePower, airAcceleration * strafePower * scale, deltaTime);
            }
            if (forwardDirection.sqrMagnitude > Epsilon)
            {
                horizontal = MovementMath.Accelerate(horizontal, forwardDirection, airStrafeWishSpeed * airForwardScale, airAcceleration * airForwardScale * scale, deltaTime);
            }

            var wishDirection = strafeDirection + forwardDirection;
            if (wishDirection.sqrMagnitude > Epsilon)
            {
                wishDirection.Normalize();
                horizontal = MovementMath.SteerToward(horizontal, wishDirection, airSteerRateDegrees * strafePower * Mathf.Deg2Rad, deltaTime);
            }
            SetHorizontalVelocity(horizontal);
        }

        private bool TryConsumeJump(bool grounded, Vector3 wishDirection)
        {
            if (jumpBufferTimer <= 0f)
            {
                return false;
            }
            if (grounded || coyoteTimer > 0f)
            {
                velocity.y = jumpVelocity;
                ApplyJumpForwardBoost(wishDirection);
                jumpBufferTimer = 0f;
                coyoteTimer = 0f;
                return true;
            }
            return false;
        }

        /// <summary>Adds one quarter-cap forward gain so four uninterrupted hops can reach maximum speed.</summary>
        private void ApplyJumpForwardBoost(Vector3 wishDirection)
        {
            var horizontal = HorizontalVelocity();
            var boostDirection = wishDirection;
            if (boostDirection.sqrMagnitude <= Epsilon)
            {
                boostDirection = horizontal.sqrMagnitude > Epsilon ? horizontal : transform.forward;
            }

            var speedGain = HardCap / Mathf.Max(jumpsToHardCap, 1);
            SetHorizontalVelocity(horizontal + boostDirection.normalized * speedGain);
        }

        private void ApplyQueuedExternalImpulse()
        {
            if (queuedExternalImpulse.sqrMagnitude <= Epsilon)
            {
                return;
            }

            velocity += queuedExternalImpulse;
            queuedExternalImpulse = Vector3.zero;
        }

        private void ResolveGroundContactAfterMove()
        {
            if (groundContactThisStep)
            {
                groundNormal = groundNormalThisStep;
                hasGroundContact = true;
                return;
            }

            if (!controller.isGrounded)
            {
                groundNormal = Vector3.up;
                hasGroundContact = false;
            }
            else if (!hasGroundContact)
            {
                groundNormal = Vector3.up;
                hasGroundContact = true;
            }
        }

        private Vector3 HorizontalVelocity() => new Vector3(velocity.x, 0f, velocity.z);
        private void SetHorizontalVelocity(Vector3 horizontal) => velocity = new Vector3(horizontal.x, velocity.y, horizontal.z);

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
