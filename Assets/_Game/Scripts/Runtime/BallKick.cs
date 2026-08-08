using System;
using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Fresh-press kick buffer and eligibility owner. BallMotor remains sole velocity owner.</summary>
    public sealed class BallKick : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerMotor player;
        [SerializeField] private PlayerLook look;
        [SerializeField] private Camera aimCamera;
        [SerializeField] private BallMotor ball;

        [Header("Kick")]
        // Ball diameter is now 3x the prior POC size. Keep aim/contact reach
        // forgiving enough for that larger surface without adding aim assist.
        [SerializeField, Min(0.1f)] private float kickRange = 4.50f;
        [SerializeField, Min(0f)] private float contactReachPadding = 1.00f;
        [SerializeField, Min(1f)] private float contactReachScale = 1.50f;
        [SerializeField, Range(1f, 89f)] private float coneTotalDegrees = 35f;
        [SerializeField, Min(0.01f)] private float cooldown = 0.40f;
        [SerializeField, Min(0.01f)] private float inputBuffer = 0.50f;
        [SerializeField, Range(0f, 1f)] private float speedFraction = 0.91f;
        [SerializeField, Range(0f, 1f)] private float playerMomentumShare = 0.20f;

        private float cooldownRemaining;
        private float bufferRemaining;
        private bool attemptPending;
        private bool simulationEnabled = true;
        private const float Epsilon = 0.000001f;

        public float CooldownRemaining => Mathf.Max(cooldownRemaining, 0f);
        public float BufferRemaining => Mathf.Max(bufferRemaining, 0f);
        public bool AttemptPending => attemptPending;
        public bool SimulationEnabled => simulationEnabled;

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

            var deltaTime = Time.fixedDeltaTime;
            cooldownRemaining = Mathf.Max(cooldownRemaining - deltaTime, 0f);

            if (input != null && input.ConsumeKickPressed())
            {
                // Animation represents the kick input itself. Contact remains
                // conditional in TryKickNow and BallMotor.ApplyKick.
                KickAttempted?.Invoke();
                if (!attemptPending && cooldownRemaining <= 0f)
                {
                    attemptPending = true;
                    bufferRemaining = inputBuffer;
                    cooldownRemaining = cooldown;
                }
            }

            if (!attemptPending)
            {
                return;
            }

            if (TryKickNow())
            {
                attemptPending = false;
                bufferRemaining = 0f;
                return;
            }

            bufferRemaining = Mathf.Max(bufferRemaining - deltaTime, 0f);
            if (bufferRemaining <= 0f)
            {
                attemptPending = false;
            }
        }

        /// <summary>Enables or freezes kick state; match reset clears pending attempts separately.</summary>
        public void SetSimulationEnabled(bool enabled)
        {
            simulationEnabled = enabled;
            if (!enabled)
            {
                attemptPending = false;
                bufferRemaining = 0f;
            }
        }

        /// <summary>Compatibility alias for match freeze owners.</summary>
        public void SetSimulationFrozen(bool frozen)
        {
            SetSimulationEnabled(!frozen);
        }

        /// <summary>Clears cooldown and buffered attempt.</summary>
        public void ResetState()
        {
            cooldownRemaining = 0f;
            bufferRemaining = 0f;
            attemptPending = false;
        }

        /// <summary>Attempts immediate eligibility check; useful for deterministic integration probes.</summary>
        public bool TryKickNow()
        {
            if (!simulationEnabled || ball == null || ball.BallCollider == null)
            {
                return false;
            }

            var origin = GetAimOrigin();
            var direction = GetAimDirection();
            var toBall = ball.transform.position - origin;
            var distance = toBall.magnitude;
            if (distance <= 0.0001f || !IsWithinPlayerReach())
            {
                return false;
            }

            // The camera is above and behind the physical player contact point.
            // Gate the aim ray by ball-surface distance, then gate physical reach
            // separately so ordinary grounded contact plus a small extension is
            // eligible without adding aim assistance.
            var ballSurfacePoint = ball.BallCollider.ClosestPoint(origin);
            if (Vector3.Distance(origin, ballSurfacePoint) > kickRange)
            {
                return false;
            }

            var coneHalfAngle = Mathf.Max(coneTotalDegrees * 0.5f, 0.5f);
            if (Vector3.Angle(direction, toBall) > coneHalfAngle)
            {
                return false;
            }

            // Unity raycasts do not report a collider that already contains the
            // ray origin. At high player speed the camera can enter this large
            // ball briefly, so treat that overlap as ball contact instead of
            // leaving the buffered kick pending until the camera exits again.
            var aimOriginInsideBall = (ballSurfacePoint - origin).sqrMagnitude <= Epsilon;
            if (!aimOriginInsideBall && Physics.Raycast(origin, direction, out var hit, kickRange, ~0, QueryTriggerInteraction.Ignore))
            {
                var hitBall = hit.collider.GetComponentInParent<BallMotor>();
                if (hitBall != ball)
                {
                    return false;
                }
            }
            else if (!aimOriginInsideBall)
            {
                return false;
            }

            var succeeded = ball.ApplyKick(direction, player != null ? player.Velocity : Vector3.zero, speedFraction, playerMomentumShare);
            if (succeeded)
            {
                KickSucceeded?.Invoke();
            }

            return succeeded;
        }

        /// <summary>Raised once when a kick attempt successfully applies ball velocity.</summary>
        public event Action KickSucceeded;

        /// <summary>Raised for every fresh kick input, before contact eligibility is checked.</summary>
        public event Action KickAttempted;

        private bool IsWithinPlayerReach()
        {
            if (player == null || ball == null || ball.BallCollider == null)
            {
                return false;
            }

            var controller = player.GetComponent<CharacterController>();
            var playerCenter = controller != null
                ? controller.transform.TransformPoint(controller.center)
                : player.transform.position;
            var playerRadius = controller != null ? controller.radius : 0.4f;
            var ballBounds = ball.BallCollider.bounds;
            var ballRadius = Mathf.Max(ballBounds.extents.x, ballBounds.extents.y, ballBounds.extents.z);
            var baseCenterDistance = playerRadius + ballRadius + Mathf.Max(contactReachPadding, 0f);
            var maximumCenterDistance = baseCenterDistance * Mathf.Max(contactReachScale, 1f);
            return Vector3.Distance(playerCenter, ball.transform.position) <= maximumCenterDistance;
        }

        private void CacheReferences()
        {
            if (input == null)
            {
                input = GetComponent<PlayerInputReader>();
            }
            if (player == null)
            {
                player = GetComponent<PlayerMotor>();
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
