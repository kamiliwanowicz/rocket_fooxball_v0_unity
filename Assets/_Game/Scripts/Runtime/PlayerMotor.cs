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
        [SerializeField, Min(1f)] private float bhopSoftCapMultiplier = 2f;
        [SerializeField, Min(1f)] private float hardCapMultiplier = 3f;

        [Header("Jump")]
        [SerializeField, Min(0f)] private float jumpVelocity = 6.75f;
        [SerializeField, Min(0f)] private float coyoteTime = 0.08f;
        [SerializeField, Min(0f)] private float jumpBufferTime = 0.10f;
        [SerializeField] private bool airJumpEnabled = true;
        [SerializeField, Min(0f)] private float airJumpVelocity = 6.75f;
        [SerializeField, Min(0f)] private float airJumpHorizontalImpulse = 2f;
        [SerializeField, Min(0f)] private float gravity = 16.875f;
        [SerializeField] private PlayerInputReader input;

        private const float MaxGroundedFallVelocity = -0.1f;
        private CharacterController controller;
        private Vector3 velocity;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private bool airJumpAvailable;

        public Vector3 Velocity => velocity;
        public float BaseSpeed => baseSpeed;
        public float SoftCap => baseSpeed * bhopSoftCapMultiplier;
        public float HardCap => baseSpeed * hardCapMultiplier;
        public bool IsAirJumpAvailable => airJumpAvailable;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void FixedUpdate()
        {
            if (input != null && input.ConsumeJumpPressed())
            {
                jumpBufferTimer = jumpBufferTime;
            }

            var deltaTime = Time.fixedDeltaTime;
            var grounded = controller.isGrounded;
            if (grounded)
            {
                airJumpAvailable = true;
                coyoteTimer = coyoteTime;
            }
            else
            {
                coyoteTimer = Mathf.Max(coyoteTimer - deltaTime, 0f);
            }
            jumpBufferTimer = Mathf.Max(jumpBufferTimer - deltaTime, 0f);

            var jumpedThisStep = TryConsumeJump(grounded);
            var move = input != null ? input.Move : Vector2.zero;
            var strafeDirection = Mathf.Abs(move.x) > 0.001f ? transform.right * Mathf.Sign(move.x) : Vector3.zero;
            var forwardDirection = Mathf.Abs(move.y) > 0.001f ? transform.forward * Mathf.Sign(move.y) : Vector3.zero;

            if (grounded && !jumpedThisStep)
            {
                ApplyGroundMovement(strafeDirection, forwardDirection, deltaTime);
                velocity.y = Mathf.Max(velocity.y, MaxGroundedFallVelocity);
            }
            else
            {
                ApplyAirMovement(strafeDirection, forwardDirection, deltaTime);
                velocity.y -= gravity * deltaTime;
            }

            velocity = MovementMath.ClampHorizontal(velocity, HardCap);
            controller.Move(velocity * deltaTime);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > 0.5f)
            {
                return;
            }

            var intoSurface = Vector3.Dot(velocity, hit.normal);
            if (intoSurface < 0f)
            {
                velocity -= hit.normal * intoSurface;
            }
        }

        private void ApplyGroundMovement(Vector3 strafeDirection, Vector3 forwardDirection, float deltaTime)
        {
            var horizontal = HorizontalVelocity();
            var wishDirection = strafeDirection + forwardDirection;
            var hasStrafe = strafeDirection.sqrMagnitude > 0.000001f;
            var strafePower = hasStrafe ? groundStrafePower : 1f;
            if (wishDirection.sqrMagnitude <= 0.000001f)
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
            SetHorizontalVelocity(horizontal);
        }

        private void ApplyAirMovement(Vector3 strafeDirection, Vector3 forwardDirection, float deltaTime)
        {
            var horizontal = HorizontalVelocity();
            var scale = MovementMath.AirAccelerationScale(horizontal.magnitude, SoftCap, HardCap);
            var hasStrafe = strafeDirection.sqrMagnitude > 0.000001f;
            var strafePower = hasStrafe ? airStrafePower : 1f;
            if (hasStrafe)
            {
                horizontal = MovementMath.Accelerate(horizontal, strafeDirection, airStrafeWishSpeed * strafePower, airAcceleration * strafePower * scale, deltaTime);
            }
            if (forwardDirection.sqrMagnitude > 0.000001f)
            {
                horizontal = MovementMath.Accelerate(horizontal, forwardDirection, airStrafeWishSpeed * airForwardScale, airAcceleration * airForwardScale * scale, deltaTime);
            }

            var wishDirection = strafeDirection + forwardDirection;
            if (wishDirection.sqrMagnitude > 0.000001f)
            {
                wishDirection.Normalize();
                horizontal = MovementMath.SteerToward(horizontal, wishDirection, airSteerRateDegrees * strafePower * Mathf.Deg2Rad, deltaTime);
            }
            SetHorizontalVelocity(horizontal);
        }

        private bool TryConsumeJump(bool grounded)
        {
            if (jumpBufferTimer <= 0f)
            {
                return false;
            }
            if (grounded || coyoteTimer > 0f)
            {
                velocity.y = jumpVelocity;
                jumpBufferTimer = 0f;
                coyoteTimer = 0f;
                return true;
            }
            if (!airJumpEnabled || !airJumpAvailable)
            {
                return false;
            }

            airJumpAvailable = false;
            jumpBufferTimer = 0f;
            velocity.y += airJumpVelocity;
            var horizontal = HorizontalVelocity();
            if (horizontal.sqrMagnitude > 0.000001f && airJumpHorizontalImpulse > 0f)
            {
                SetHorizontalVelocity(horizontal + horizontal.normalized * airJumpHorizontalImpulse);
            }
            return true;
        }

        private Vector3 HorizontalVelocity() => new Vector3(velocity.x, 0f, velocity.z);
        private void SetHorizontalVelocity(Vector3 horizontal) => velocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
    }
}
