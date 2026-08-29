using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Physics;

namespace RocketFooxball.Runtime.Movement
{
    /// <summary>Immutable collision snapshot emitted after player velocity resolution.</summary>
    public readonly struct PlayerCollisionResolution
    {
        public PlayerCollisionResolution(
            ControllerColliderHit hit,
            Vector3 incomingPlayerVelocity,
            Vector3 resolvedPlayerVelocity,
            Vector3 attachedBodyVelocity,
            bool wasDashing)
        {
            Hit = hit;
            IncomingPlayerVelocity = incomingPlayerVelocity;
            ResolvedPlayerVelocity = resolvedPlayerVelocity;
            AttachedBodyVelocity = attachedBodyVelocity;
            WasDashing = wasDashing;
        }

        public ControllerColliderHit Hit { get; }
        public Vector3 IncomingPlayerVelocity { get; }
        public Vector3 ResolvedPlayerVelocity { get; }
        public Vector3 AttachedBodyVelocity { get; }
        public bool WasDashing { get; }

        public Vector3 IncomingVelocity => IncomingPlayerVelocity;
        public Vector3 ResolvedVelocity => ResolvedPlayerVelocity;
        public Vector3 BodyVelocity => AttachedBodyVelocity;
    }

    public enum DashEndReason
    {
        Duration,
        EnemyContact,
        Wall,
        SimulationDisabled,
        Reset
    }

    [RequireComponent(typeof(CharacterController))]
    [MovedFrom("RocketFooxball")]
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
        [SerializeField, Min(1f)] private float bhopSoftCapMultiplier = PlayerMotorDefaults.BhopSoftCapMultiplier;
        [SerializeField, Min(1f)] private float hardCapMultiplier = 3f;

        [Header("Jump")]
        [SerializeField, Min(0f)] private float jumpVelocity = 4.80f;
        [SerializeField, Min(1)] private int jumpsToHardCap = PlayerMotorDefaults.JumpsToHardCap;
        [SerializeField, Min(0f)] private float coyoteTime = 0.08f;
        [SerializeField, Min(0f)] private float jumpBufferTime = 0.10f;
        [SerializeField] private PlayerInputReader input;

        [Header("Dash Kick")]
        [SerializeField, Min(0f)] private float dashBurstSpeed = PlayerMotorDefaults.DashBurstSpeed;
        [SerializeField, Min(0.01f)] private float dashDuration = PlayerMotorDefaults.DashDuration;
        [SerializeField, Min(0f)] private float dashSteerRateDegrees = PlayerMotorDefaults.DashSteerRateDegrees;
        [SerializeField, Min(1f)] private float dashSpeedCap = PlayerMotorDefaults.DashSpeedCap;

        private const float MaxGroundedFallVelocity = -0.1f;
        private const float Epsilon = 0.000001f;
        private CharacterController controller;
        private Vector3 velocity;
        private Vector3 queuedExternalImpulse;
        private Vector2 requestedMove;
        private Vector2 currentEffectiveMoveIntent;
        private bool moveIntentPending;
        private bool jumpRequestPending;
        private Vector3 groundNormal = Vector3.up;
        private Vector3 groundNormalThisStep = Vector3.up;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private bool hasGroundContact;
        private bool groundContactThisStep;
        private bool simulationEnabled = true;
        private bool dashActive;
        private float dashRemaining;
        private float dashElapsed;
        private Vector3 dashDirection;
        private Vector3 dashContribution;
        private Vector3 dashAim;
        private bool airDashAvailable = true;
        private bool paused;

        /// <summary>Raised during CharacterController collision dispatch with actual contact data.</summary>
        public event Action<ControllerColliderHit> CollisionHit;

        /// <summary>Raised after a dynamic non-kinematic collision resolves player velocity.</summary>
        public event Action<PlayerCollisionResolution> DynamicCollisionResolved;

        public Vector3 Velocity => velocity;
        public Vector2 CurrentEffectiveMoveIntent => currentEffectiveMoveIntent;
        public float BaseSpeed => baseSpeed;
        public float SoftCap => baseSpeed * bhopSoftCapMultiplier;
        public float HardCap => baseSpeed * hardCapMultiplier;
        public float HorizontalSpeed => new Vector3(velocity.x, 0f, velocity.z).magnitude;
        public bool IsGrounded => controller != null && controller.isGrounded;
        public bool HasGroundContact => hasGroundContact;
        public Vector3 GroundNormal => hasGroundContact ? groundNormal : Vector3.up;
        public bool SimulationEnabled => simulationEnabled;
        public bool IsDashing => dashActive;
        public float DashRemaining => Mathf.Max(dashRemaining, 0f);
        public float DashElapsed => Mathf.Max(dashElapsed, 0f);
        public Vector3 DashDirection => dashDirection;
        public bool AirDashAvailable => airDashAvailable;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void OnDisable()
        {
            paused = false;
            ClearProgrammaticInput();
            ClearEffectiveMoveIntent();
        }

        private void FixedUpdate()
        {
            if (paused)
            {
                return;
            }

            if (controller == null)
            {
                ClearProgrammaticInput();
                return;
            }

            if (!simulationEnabled)
            {
                ClearProgrammaticInput();
                ClearEffectiveMoveIntent();
                input?.ClearGameplayState();
                queuedExternalImpulse = Vector3.zero;
                return;
            }

            var hasProgrammaticMove = moveIntentPending;
            var programmaticMove = requestedMove;
            var programmaticJump = jumpRequestPending;
            var deviceMove = input != null ? input.Move : Vector2.zero;
            var effectiveMove = hasProgrammaticMove ? programmaticMove : SanitizeMoveIntent(deviceMove);
            currentEffectiveMoveIntent = effectiveMove;
            ClearProgrammaticInput();

            var deltaTime = Time.fixedDeltaTime;
            var grounded = controller.isGrounded || hasGroundContact;
            var activeGroundNormal = hasGroundContact ? groundNormal : Vector3.up;

            groundContactThisStep = false;
            groundNormalThisStep = Vector3.up;

            if (grounded)
            {
                airDashAvailable = true;
            }

            if (dashActive)
            {
                ApplyDashMovement(grounded, activeGroundNormal, deltaTime);
                controller.Move(velocity * deltaTime);
                ResolveGroundContactAfterMove();
                dashElapsed += deltaTime;
                dashRemaining = Mathf.Max(dashRemaining - deltaTime, 0f);
                if (dashRemaining <= 0f)
                {
                    EndDash(DashEndReason.Duration, 1f);
                }
                return;
            }

            var deviceJump = input != null && input.ConsumeJumpPressed();
            if (programmaticJump || deviceJump)
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

            var move = effectiveMove;
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
            if (paused || hit == null)
            {
                return;
            }

            var incomingVelocity = velocity;
            var attachedBody = hit.collider != null ? hit.collider.attachedRigidbody : null;
            var dynamicBody = attachedBody != null && !attachedBody.isKinematic;
            var wasDashing = dashActive;
            if (dynamicBody)
            {
                var attachedBodyVelocity = attachedBody.linearVelocity;
                var resolvedVelocity = BallMotionRules.ResolvePlayerCollision(
                    incomingVelocity,
                    hit.normal,
                    attachedBodyVelocity,
                    true,
                    GamePhysicsSettings.PlayerCollisionTransferFraction);
                if (wasDashing)
                {
                    dashContribution = BallMotionRules.ResolveDashContributionAfterCollision(
                        incomingVelocity,
                        dashContribution,
                        hit.normal,
                        attachedBodyVelocity,
                        true,
                        GamePhysicsSettings.PlayerCollisionTransferFraction);
                }

                velocity = resolvedVelocity;
                DynamicCollisionResolved?.Invoke(new PlayerCollisionResolution(
                    hit,
                    incomingVelocity,
                    resolvedVelocity,
                    attachedBodyVelocity,
                    wasDashing));
            }
            else
            {
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
                    var resolvedVelocity = BallMotionRules.ResolvePlayerCollision(
                        incomingVelocity,
                        hit.normal,
                        Vector3.zero,
                        false,
                        GamePhysicsSettings.PlayerCollisionTransferFraction);
                    if (wasDashing)
                    {
                        dashContribution = BallMotionRules.ResolveDashContributionAfterCollision(
                            incomingVelocity,
                            dashContribution,
                            hit.normal,
                            Vector3.zero,
                            false,
                            GamePhysicsSettings.PlayerCollisionTransferFraction);
                    }

                    velocity = resolvedVelocity;
                }
            }

            CollisionHit?.Invoke(hit);
        }

        /// <summary>Queues an additive fixed-step impulse. Invalid or gated impulses are ignored.</summary>
        public void AddExternalImpulse(Vector3 impulse)
        {
            if (paused || !simulationEnabled || !IsFinite(impulse) || impulse.sqrMagnitude <= Epsilon)
            {
                return;
            }

            queuedExternalImpulse += impulse;
        }

        /// <summary>Queues the latest valid one-step movement intent.</summary>
        public bool SetMoveIntent(Vector2 move)
        {
            if (paused || !isActiveAndEnabled || !simulationEnabled || !IsFinite(move))
            {
                return false;
            }

            requestedMove = Vector2.ClampMagnitude(move, 1f);
            currentEffectiveMoveIntent = requestedMove;
            moveIntentPending = true;
            return true;
        }

        /// <summary>Queues one jump request for the next enabled fixed step.</summary>
        public bool RequestJump()
        {
            if (paused || !isActiveAndEnabled || !simulationEnabled)
            {
                return false;
            }

            jumpRequestPending = true;
            return true;
        }

        /// <summary>Starts a bounded dash without replacing existing velocity.</summary>
        public bool TryStartDash(Vector3 aim)
        {
            if (paused || !simulationEnabled || dashActive || !MovementMath.IsFinite(aim) || aim.sqrMagnitude <= Epsilon)
            {
                return false;
            }

            var grounded = controller != null && (controller.isGrounded || hasGroundContact);
            if (!grounded && !airDashAvailable)
            {
                return false;
            }

            var activeGroundNormal = hasGroundContact ? groundNormal : Vector3.up;
            var direction = ResolveDashDirection(aim, grounded, activeGroundNormal);
            if (direction.sqrMagnitude <= Epsilon)
            {
                return false;
            }

            if (!grounded)
            {
                airDashAvailable = false;
            }

            var nextVelocity = MovementMath.ComposeDashVelocity(velocity, direction, dashBurstSpeed, dashSpeedCap);
            dashContribution = nextVelocity - velocity;
            velocity = nextVelocity;
            dashDirection = dashContribution.sqrMagnitude > Epsilon ? dashContribution.normalized : direction;
            dashAim = aim.normalized;
            dashRemaining = dashDuration;
            dashElapsed = 0f;
            dashActive = true;
            return true;
        }

        /// <summary>Supplies current camera aim for fixed-step dash steering.</summary>
        public void SetDashAim(Vector3 aim)
        {
            if (paused || !dashActive || !MovementMath.IsFinite(aim) || aim.sqrMagnitude <= Epsilon)
            {
                return;
            }

            dashAim = aim.normalized;
        }

        /// <summary>Ends active dash and removes only requested tracked contribution.</summary>
        public void EndDash(DashEndReason reason, float retainedContributionFraction)
        {
            if (paused || !dashActive)
            {
                return;
            }

            velocity = MovementMath.RemoveContributionWithoutReversal(velocity, dashContribution, retainedContributionFraction);
            ClearDashState();
        }

        /// <summary>Enables or freezes fixed-step player simulation without changing transform or velocity.</summary>
        public void SetSimulationEnabled(bool enabled)
        {
            simulationEnabled = enabled;
            if (!enabled)
            {
                paused = false;
                EndDash(DashEndReason.SimulationDisabled, 1f);
                queuedExternalImpulse = Vector3.zero;
                ClearProgrammaticInput();
                ClearEffectiveMoveIntent();
                coyoteTimer = 0f;
                jumpBufferTimer = 0f;
                input?.ClearGameplayState();
            }
        }

        /// <summary>Freezes fixed-step movement while preserving velocity, dash, timers, impulse, and ground state.</summary>
        public void SetPaused(bool pausedState)
        {
            if (paused == pausedState)
            {
                return;
            }

            paused = pausedState;
            if (paused)
            {
                ClearProgrammaticInput();
                input?.ClearGameplayState();
            }
        }

        /// <summary>Clears movement state without moving the player.</summary>
        public void ClearQueuedState()
        {
            EndDash(DashEndReason.SimulationDisabled, 1f);
            queuedExternalImpulse = Vector3.zero;
            ClearProgrammaticInput();
            ClearEffectiveMoveIntent();
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
            paused = false;
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
            ClearDashState();
            airDashAvailable = true;
            input?.ClearGameplayState();
        }

        private void ApplyDashMovement(bool grounded, Vector3 activeGroundNormal, float deltaTime)
        {
            var baseVelocity = velocity - dashContribution;
            if (grounded)
            {
                if (!hasGroundContact || activeGroundNormal.y >= 0.9999f)
                {
                    baseVelocity.y = Mathf.Max(baseVelocity.y, MaxGroundedFallVelocity);
                }
            }
            else
            {
                baseVelocity.y -= GamePhysicsSettings.GravityMagnitude * deltaTime;
            }

            if (queuedExternalImpulse.sqrMagnitude > Epsilon)
            {
                baseVelocity += queuedExternalImpulse;
                queuedExternalImpulse = Vector3.zero;
            }

            var aimDirection = ResolveDashDirection(dashAim, grounded, activeGroundNormal);
            var steeredContribution = MovementMath.SteerContribution(dashContribution, aimDirection, dashSteerRateDegrees, deltaTime);
            velocity = Vector3.ClampMagnitude(baseVelocity + steeredContribution, dashSpeedCap);
            dashContribution = velocity - baseVelocity;
            if (dashContribution.sqrMagnitude > Epsilon)
            {
                dashDirection = dashContribution.normalized;
            }
        }

        private Vector3 ResolveDashDirection(Vector3 aim, bool grounded, Vector3 activeGroundNormal)
        {
            var direction = aim;
            if (grounded)
            {
                direction = MovementMath.ProjectDirectionOnPlane(aim, activeGroundNormal);
                if (direction.sqrMagnitude <= Epsilon)
                {
                    direction = MovementMath.ProjectDirectionOnPlane(transform.forward, activeGroundNormal);
                }
            }

            return direction.sqrMagnitude > Epsilon ? direction.normalized : Vector3.zero;
        }

        private void ClearDashState()
        {
            dashActive = false;
            dashRemaining = 0f;
            dashElapsed = 0f;
            dashDirection = Vector3.zero;
            dashContribution = Vector3.zero;
            dashAim = Vector3.zero;
        }

        private void ClearProgrammaticInput()
        {
            requestedMove = Vector2.zero;
            moveIntentPending = false;
            jumpRequestPending = false;
        }

        private void ClearEffectiveMoveIntent()
        {
            currentEffectiveMoveIntent = Vector2.zero;
        }

        private static Vector2 SanitizeMoveIntent(Vector2 move)
        {
            if (!IsFinite(move))
            {
                return Vector2.zero;
            }

            return Vector2.ClampMagnitude(move, 1f);
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

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
