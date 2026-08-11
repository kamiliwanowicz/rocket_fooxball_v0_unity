using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Participants;

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

        [Header("Participant Damage")]
        [SerializeField, Min(0f)] private float directRocketDamage = 50f;
        [SerializeField, Range(0f, 1f)] private float enemyRocketImpulseMultiplier = 0.5f;

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
        public float DirectRocketDamage => directRocketDamage;
        public float EnemyRocketImpulseMultiplier => enemyRocketImpulseMultiplier;

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

                var participant = collider.GetComponentInParent<ParticipantState>();
                if (participant != null)
                {
                    targets.AddPlayer(participant, collider, origin);
                    continue;
                }

                var ball = collider.GetComponentInParent<BallMotor>();
                if (ball != null)
                {
                    targets.AddBall(ball, collider, origin);
                }
            }

            DispatchPlayers(origin, impactCollider, source != null ? source.OwnerParticipant : null);
            DispatchBalls(origin, impactCollider);
        }

        private void AddImpactTarget(Collider impactCollider, Vector3 origin)
        {
            if (impactCollider == null)
            {
                return;
            }

            var participant = impactCollider.GetComponentInParent<ParticipantState>();
            if (participant != null)
            {
                targets.AddPlayer(participant, impactCollider, origin, true);
                return;
            }

            var ball = impactCollider.GetComponentInParent<BallMotor>();
            if (ball != null)
            {
                targets.AddBall(ball, impactCollider, origin, true);
            }
        }

        private void DispatchPlayers(Vector3 origin, Collider impactCollider, ParticipantState sourceParticipant)
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
                // Legacy direct calls without a source projectile remain force-only.
                var relationship = GetRelationship(target, sourceParticipant);
                if (relationship == ParticipantRelationship.Friendly || relationship == ParticipantRelationship.Immune)
                {
                    continue;
                }

                var impulseScale = relationship == ParticipantRelationship.Enemy ? enemyRocketImpulseMultiplier : 1f;
                var impulse = ComputePlayerImpulse(target.Motor, origin, playerImpulseStrength * strength * impulseScale);
                target.Motor?.AddExternalImpulse(impulse);
                target.CameraFeedback?.RequestBlastShake(Mathf.Clamp01(strength * cameraFeedbackScale));

                if (relationship == ParticipantRelationship.Enemy && target.IsAlive && !target.IsImmune)
                {
                    var damage = directRocketDamage * strength;
                    target.TryApplyDamage(sourceParticipant, damage, ParticipantDamageCause.Rocket, "Rocket Launcher");
                }
            }
        }

        private enum ParticipantRelationship
        {
            Unattributed,
            Own,
            Friendly,
            Enemy,
            Immune
        }

        private static ParticipantRelationship GetRelationship(ParticipantState target, ParticipantState sourceParticipant)
        {
            if (target == null)
            {
                return ParticipantRelationship.Immune;
            }
            if (!target.IsAlive || target.IsImmune)
            {
                return ParticipantRelationship.Immune;
            }
            if (sourceParticipant == null)
            {
                return ParticipantRelationship.Unattributed;
            }
            if (target == sourceParticipant)
            {
                return ParticipantRelationship.Own;
            }
            return target.Team == sourceParticipant.Team ? ParticipantRelationship.Friendly : ParticipantRelationship.Enemy;
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
            if (target == null)
            {
                return Vector3.zero;
            }

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
