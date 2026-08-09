using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Movement;

namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Resolves radial blast falloff, geometry occlusion, and additive player/ball impulses.</summary>
    [MovedFrom("RocketFooxball")]
    public sealed class ExplosionResolver : MonoBehaviour
    {
        [Header("Blast")]
        [SerializeField, Min(0.1f)] private float blastRadius = 5.85f;
        [SerializeField, Min(0f)] private float playerImpulseStrength = 24f;
        [SerializeField, Min(0f)] private float ballImpulseStrength = 16f;
        [SerializeField, Range(0f, 1f)] private float occludedForce = 0.25f;
        [SerializeField, Range(0f, 1f)] private float playerUpBias = 0.18f;

        [Header("Rocket Jump")]
        [SerializeField, Min(0f)] private float underfootForwardImpulseScale = 0.5625f;
        [SerializeField, Min(0f)] private float underfootUpwardImpulseScale = 1f;
        [SerializeField, Range(0f, 1f)] private float underfootHighSpeedVerticalRedirect = 1f;
        [SerializeField] private Collider[] goalShieldColliders;

        [Header("Feedback")]
        [SerializeField, Range(0f, 1f)] private float cameraFeedbackScale = 0.8f;

        [Header("Presentation")]
        [SerializeField] private ExplosionVfx explosionVfxPrefab;

        private readonly Collider[] overlapBuffer = new Collider[128];
        private readonly PlayerMotor[] playerTargets = new PlayerMotor[16];
        private readonly Collider[] playerTargetColliders = new Collider[16];
        private readonly float[] playerTargetDistances = new float[16];
        private readonly BallMotor[] ballTargets = new BallMotor[8];
        private readonly Collider[] ballTargetColliders = new Collider[8];
        private readonly float[] ballTargetDistances = new float[8];

        public float BlastRadius => blastRadius;
        public float OccludedForce => occludedForce;

        /// <summary>Resolves one rocket blast at origin. Impact-owned gameplay targets still receive the blast.</summary>
        public void ResolveExplosion(Vector3 origin, RocketProjectile source = null, Collider impactCollider = null)
        {
            if (explosionVfxPrefab != null)
            {
                var explosionVfx = Instantiate(explosionVfxPrefab, origin, Quaternion.identity);
                explosionVfx?.Play();
            }

            var overlapCount = UnityEngine.Physics.OverlapSphereNonAlloc(origin, blastRadius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            var playerCount = 0;
            var ballCount = 0;

            // A direct hit can place the impact collider on the overlap boundary,
            // or leave it out of the overlap query entirely. Add its gameplay owner
            // explicitly so direct hits receive one normal maximum/surface-falloff
            // blast. Non-gameplay impact colliders remain excluded from targeting.
            AddImpactTarget(impactCollider, origin, ref playerCount, ref ballCount);

            for (var i = 0; i < overlapCount; i++)
            {
                var collider = overlapBuffer[i];
                if (collider == null || (source != null && collider == source.ProjectileCollider))
                {
                    continue;
                }

                var player = collider.GetComponentInParent<PlayerMotor>();
                if (player != null)
                {
                    AddPlayerTarget(player, collider, origin, ref playerCount);
                    continue;
                }

                var ball = collider.GetComponentInParent<BallMotor>();
                if (ball != null)
                {
                    AddBallTarget(ball, collider, origin, ref ballCount);
                }
            }

            for (var i = 0; i < playerCount; i++)
            {
                var targetCollider = playerTargetColliders[i];
                var distance = playerTargetDistances[i];
                var falloff = ComputeFalloff(distance);
                if (falloff <= 0f)
                {
                    continue;
                }

                var strength = falloff * (IsOccluded(origin, targetCollider, targetCollider.ClosestPoint(origin), false, impactCollider) ? occludedForce : 1f);
                var target = playerTargets[i];
                var impulse = ComputePlayerImpulse(target, origin, playerImpulseStrength * strength);
                target.AddExternalImpulse(impulse);
                var feedback = target.GetComponent<PlayerCameraFeedback>();
                feedback?.RequestBlastShake(Mathf.Clamp01(strength * cameraFeedbackScale));
            }

            for (var i = 0; i < ballCount; i++)
            {
                var targetCollider = ballTargetColliders[i];
                var distance = ballTargetDistances[i];
                var falloff = ComputeFalloff(distance);
                if (falloff <= 0f)
                {
                    continue;
                }

                var closestPoint = targetCollider.ClosestPoint(origin);
                var strength = falloff * (IsOccluded(origin, targetCollider, closestPoint, true, impactCollider) ? occludedForce : 1f);
                var target = ballTargets[i];
                var direction = target.transform.position - origin;
                if (direction.sqrMagnitude <= 0.000001f)
                {
                    direction = closestPoint - origin;
                }
                if (direction.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }
                target.QueueImpulse(direction.normalized * (ballImpulseStrength * strength));
            }
        }

        private Vector3 ComputePlayerImpulse(PlayerMotor target, Vector3 origin, float strength)
        {
            var radialDirection = target.transform.position - origin;
            if (radialDirection.sqrMagnitude <= 0.000001f)
            {
                radialDirection = Vector3.up;
            }
            else
            {
                radialDirection.Normalize();
            }

            if (!TryGetUnderfootFacing(target, origin, out var facing))
            {
                if (radialDirection.y < 0.25f)
                {
                    radialDirection = (radialDirection + Vector3.up * playerUpBias).normalized;
                }
                return radialDirection * strength;
            }

            // Floor/leg blasts preserve impulse magnitude while redirecting
            // high-speed forward force upward. Camera pitch never controls
            // rocket-jump direction.
            var speedT = Mathf.Clamp01(
                (target.HorizontalSpeed - target.BaseSpeed) /
                Mathf.Max(target.SoftCap - target.BaseSpeed, 0.000001f));
            var redirectT = speedT * underfootHighSpeedVerticalRedirect;
            var forwardScale = underfootForwardImpulseScale * (1f - redirectT);
            var impulseScaleSqr =
                underfootForwardImpulseScale * underfootForwardImpulseScale +
                underfootUpwardImpulseScale * underfootUpwardImpulseScale;
            var upwardScale = Mathf.Sqrt(Mathf.Max(
                impulseScaleSqr - forwardScale * forwardScale,
                0f));
            var underfootImpulse = facing * forwardScale + Vector3.up * upwardScale;
            if (underfootImpulse.sqrMagnitude <= 0.000001f)
            {
                return Vector3.up * strength;
            }
            return underfootImpulse * strength;
        }

        private bool TryGetUnderfootFacing(PlayerMotor target, Vector3 origin, out Vector3 facing)
        {
            facing = Vector3.ProjectOnPlane(target.transform.forward, Vector3.up);
            if (facing.sqrMagnitude <= 0.000001f)
            {
                return false;
            }
            facing.Normalize();

            var controller = target.GetComponent<CharacterController>();
            if (controller == null)
            {
                return false;
            }

            var bounds = controller.bounds;
            var horizontalOffset = Vector3.ProjectOnPlane(origin - bounds.center, Vector3.up);
            var maxHorizontalDistance = Mathf.Max(controller.radius * 1.5f, 0.05f);
            if (horizontalOffset.sqrMagnitude > maxHorizontalDistance * maxHorizontalDistance)
            {
                return false;
            }

            // Foot capsule zone plus a small below-feet margin defines
            // underfoot. Side/upper-body blasts retain radial response.
            var feetY = bounds.min.y;
            var minY = feetY - controller.radius;
            var maxY = feetY + controller.radius;
            return origin.y >= minY && origin.y <= maxY;
        }

        /// <summary>Updates shield list used for ball-transparent blast occlusion.</summary>
        public void SetShieldColliders(Collider[] shields)
        {
            goalShieldColliders = shields;
        }

        private void AddImpactTarget(Collider impactCollider, Vector3 origin, ref int playerCount, ref int ballCount)
        {
            if (impactCollider == null)
            {
                return;
            }

            var player = impactCollider.GetComponentInParent<PlayerMotor>();
            if (player != null)
            {
                AddPlayerTarget(player, impactCollider, origin, ref playerCount, true);
                return;
            }

            var ball = impactCollider.GetComponentInParent<BallMotor>();
            if (ball != null)
            {
                AddBallTarget(ball, impactCollider, origin, ref ballCount, true);
            }
        }

        private void AddPlayerTarget(PlayerMotor target, Collider collider, Vector3 origin, ref int count, bool directImpact = false)
        {
            for (var i = 0; i < count; i++)
            {
                if (playerTargets[i] != target)
                {
                    continue;
                }

                var distance = directImpact ? 0f : SurfaceDistance(origin, collider);
                if (distance < playerTargetDistances[i])
                {
                    playerTargetDistances[i] = distance;
                    playerTargetColliders[i] = collider;
                }
                return;
            }

            if (count >= playerTargets.Length)
            {
                return;
            }
            playerTargets[count] = target;
            playerTargetColliders[count] = collider;
            playerTargetDistances[count] = directImpact ? 0f : SurfaceDistance(origin, collider);
            count++;
        }

        private void AddBallTarget(BallMotor target, Collider collider, Vector3 origin, ref int count, bool directImpact = false)
        {
            for (var i = 0; i < count; i++)
            {
                if (ballTargets[i] != target)
                {
                    continue;
                }

                var distance = directImpact ? 0f : SurfaceDistance(origin, collider);
                if (distance < ballTargetDistances[i])
                {
                    ballTargetDistances[i] = distance;
                    ballTargetColliders[i] = collider;
                }
                return;
            }

            if (count >= ballTargets.Length)
            {
                return;
            }
            ballTargets[count] = target;
            ballTargetColliders[count] = collider;
            ballTargetDistances[count] = directImpact ? 0f : SurfaceDistance(origin, collider);
            count++;
        }

        private float ComputeFalloff(float surfaceDistance)
        {
            var normalized = Mathf.Clamp01(1f - surfaceDistance / Mathf.Max(blastRadius, 0.0001f));
            return normalized * normalized * (3f - 2f * normalized);
        }

        private float SurfaceDistance(Vector3 origin, Collider collider)
        {
            if (collider == null)
            {
                return blastRadius;
            }
            return Vector3.Distance(origin, collider.ClosestPoint(origin));
        }

        private bool IsOccluded(Vector3 origin, Collider targetCollider, Vector3 targetPoint, bool ballTarget, Collider impactCollider)
        {
            var offset = targetPoint - origin;
            var distance = offset.magnitude;
            if (distance <= 0.0001f)
            {
                return false;
            }

            var direction = offset / distance;
            if (!UnityEngine.Physics.Raycast(origin, direction, out var hit, Mathf.Max(distance - 0.01f, 0f), ~0, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            // The collider that received the rocket hit is at the blast origin.
            // Treating it as a blocker would incorrectly reduce force to nearby
            // targets; it is excluded only from occlusion, never from targeting.
            if (hit.collider == impactCollider)
            {
                return false;
            }
            if (hit.collider == targetCollider || hit.collider.transform.IsChildOf(targetCollider.transform))
            {
                return false;
            }
            if (ballTarget && IsGoalShield(hit.collider))
            {
                return false;
            }
            return true;
        }

        private bool IsGoalShield(Collider collider)
        {
            if (collider == null || goalShieldColliders == null)
            {
                return false;
            }
            for (var i = 0; i < goalShieldColliders.Length; i++)
            {
                if (goalShieldColliders[i] == collider)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
