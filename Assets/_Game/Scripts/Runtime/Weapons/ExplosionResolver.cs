using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Movement;

namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Unity physics adapter: queries, occlusion checks, and gameplay dispatch for one blast.</summary>
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
        [SerializeField] private GoalShieldSet goalShieldSet;

        [Header("Feedback")]
        [SerializeField, Range(0f, 1f)] private float cameraFeedbackScale = 0.8f;

        [Header("Presentation")]
        [SerializeField] private ExplosionVfxSpawner explosionVfxSpawner;

        private readonly Collider[] overlapBuffer = new Collider[128];
        private readonly ExplosionTargetCollector targets = new ExplosionTargetCollector(16, 8);

        public float BlastRadius => blastRadius;
        public float OccludedForce => occludedForce;

        /// <summary>Resolves one accepted rocket blast. Impact-owned gameplay targets remain eligible.</summary>
        public void ResolveExplosion(Vector3 origin, RocketProjectile source = null, Collider impactCollider = null)
        {
            explosionVfxSpawner?.Play(origin);
            targets.Clear();

            var overlapCount = UnityEngine.Physics.OverlapSphereNonAlloc(origin, blastRadius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            AddImpactTarget(impactCollider, origin);
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
                    targets.AddPlayer(player, collider, origin);
                    continue;
                }

                var ball = collider.GetComponentInParent<BallMotor>();
                if (ball != null)
                {
                    targets.AddBall(ball, collider, origin);
                }
            }

            DispatchPlayers(origin, impactCollider);
            DispatchBalls(origin, impactCollider);
        }

        private void AddImpactTarget(Collider impactCollider, Vector3 origin)
        {
            if (impactCollider == null)
            {
                return;
            }

            var player = impactCollider.GetComponentInParent<PlayerMotor>();
            if (player != null)
            {
                targets.AddPlayer(player, impactCollider, origin, true);
                return;
            }

            var ball = impactCollider.GetComponentInParent<BallMotor>();
            if (ball != null)
            {
                targets.AddBall(ball, impactCollider, origin, true);
            }
        }

        private void DispatchPlayers(Vector3 origin, Collider impactCollider)
        {
            for (var i = 0; i < targets.PlayerCount; i++)
            {
                var targetCollider = targets.GetPlayerCollider(i);
                var falloff = BlastMath.ComputeFalloff(targets.GetPlayerDistance(i), blastRadius);
                if (falloff <= 0f)
                {
                    continue;
                }

                var strength = falloff * (IsOccluded(origin, targetCollider, targetCollider.ClosestPoint(origin), false, impactCollider) ? occludedForce : 1f);
                var target = targets.GetPlayer(i);
                var impulse = ComputePlayerImpulse(target, origin, playerImpulseStrength * strength);
                target.AddExternalImpulse(impulse);
                target.GetComponent<PlayerCameraFeedback>()?.RequestBlastShake(Mathf.Clamp01(strength * cameraFeedbackScale));
            }
        }

        private void DispatchBalls(Vector3 origin, Collider impactCollider)
        {
            for (var i = 0; i < targets.BallCount; i++)
            {
                var targetCollider = targets.GetBallCollider(i);
                var falloff = BlastMath.ComputeFalloff(targets.GetBallDistance(i), blastRadius);
                if (falloff <= 0f)
                {
                    continue;
                }

                var closestPoint = targetCollider.ClosestPoint(origin);
                var strength = falloff * (IsOccluded(origin, targetCollider, closestPoint, true, impactCollider) ? occludedForce : 1f);
                var target = targets.GetBall(i);
                if (BlastMath.TryGetBallDirection(target.transform.position, closestPoint, origin, out var direction))
                {
                    target.QueueImpulse(direction * (ballImpulseStrength * strength));
                }
            }
        }

        private Vector3 ComputePlayerImpulse(PlayerMotor target, Vector3 origin, float strength)
        {
            var controller = target.GetComponent<CharacterController>();
            var facing = Vector3.zero;
            var isUnderfoot = controller != null && BlastMath.TryGetUnderfootFacing(
                target.transform.forward,
                controller.bounds,
                controller.radius,
                origin,
                out facing);
            return BlastMath.ComputePlayerImpulse(
                target.transform.position,
                origin,
                strength,
                playerUpBias,
                isUnderfoot,
                facing,
                target.HorizontalSpeed,
                target.BaseSpeed,
                target.SoftCap,
                underfootForwardImpulseScale,
                underfootUpwardImpulseScale,
                underfootHighSpeedVerticalRedirect);
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
            if (hit.collider == impactCollider || hit.collider == targetCollider || hit.collider.transform.IsChildOf(targetCollider.transform))
            {
                return false;
            }
            return !(ballTarget && goalShieldSet != null && goalShieldSet.Contains(hit.collider));
        }
    }
}
