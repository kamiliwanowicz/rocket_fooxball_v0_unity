using System;
using System.Collections.Generic;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Participants;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace RocketFooxball.Runtime.Ball
{
    /// <summary>Owns accepted dash-kick input, cooldown, and contact effects. PlayerMotor owns dash movement.</summary>
    [DefaultExecutionOrder(-100)]
    [MovedFrom("RocketFooxball")]
    public sealed class BallKick : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerMotor player;
        [SerializeField] private PlayerLook look;
        [SerializeField] private Camera aimCamera;
        [SerializeField] private BallMotor ball;
        [SerializeField] private ParticipantState ownerParticipant;

        [Header("Dash Contact")]
        [SerializeField, Min(0f)] private float dashContactStartDelay = 0.10f;
        [SerializeField, Min(0.1f)] private float dashContactReach = 2f;
        [SerializeField, Min(0f)] private float dashContactRadiusPadding = 0.35f;
        [SerializeField, Min(0.01f)] private float cooldown = 3f;
        [SerializeField, Range(0f, 1f)] private float speedFraction = 0.91f;
        [SerializeField, Range(0f, 1f)] private float playerMomentumShare = 0.20f;
        [SerializeField, Min(0f)] private float enemyContactDamage = 20f;
        [SerializeField, Min(0f)] private float enemyShoveImpulse = 6f;
        [SerializeField, Range(0f, 1f)] private float enemyDashRetention = 0.20f;

        private const float Epsilon = 0.000001f;
        private readonly Collider[] contactBuffer = new Collider[32];
        private readonly HashSet<BallMotor> contactedBalls = new HashSet<BallMotor>();
        private readonly HashSet<ParticipantState> contactedParticipants = new HashSet<ParticipantState>();
        private CharacterController controller;
        private float cooldownRemaining;
        private bool simulationEnabled = true;
        private bool collisionSubscribed;
        private bool requestPending;
        private Vector3 requestedAim;

        public float CooldownRemaining => Mathf.Max(cooldownRemaining, 0f);
        public bool SimulationEnabled => simulationEnabled;

        /// <summary>Raised only when dash activation is accepted.</summary>
        public event Action DashStarted;

        /// <summary>Raised once when dash contact successfully applies ball velocity.</summary>
        public event Action KickSucceeded;

        private void Awake()
        {
            CacheSameObjectReferences();
            controller = player != null ? player.GetComponent<CharacterController>() : null;
            if (!ValidateComposition())
            {
                return;
            }

            SubscribeCollision();
        }

        private void OnEnable()
        {
            SubscribeCollision();
        }

        private void OnDisable()
        {
            UnsubscribeCollision();
            ClearProgrammaticRequest();
        }

        private void FixedUpdate()
        {
            if (!simulationEnabled || player == null)
            {
                ClearProgrammaticRequest();
                return;
            }

            cooldownRemaining = DashKickRules.TickCooldown(cooldownRemaining, Time.fixedDeltaTime);
            var useProgrammaticRequest = requestPending;
            var programmaticAim = requestedAim;
            ClearProgrammaticRequest();
            var localRequest = input != null && input.ConsumeKickPressed();
            var hasRequest = useProgrammaticRequest || localRequest;
            var aim = useProgrammaticRequest ? programmaticAim : GetAimDirection();
            if (player.IsDashing)
            {
                player.SetDashAim(aim);
            }

            if (hasRequest &&
                DashKickRules.CanActivate(
                    simulationEnabled,
                    aim,
                    player.IsDashing,
                    cooldownRemaining,
                    player.IsGrounded || player.HasGroundContact,
                    player.AirDashAvailable) &&
                player.TryStartDash(aim))
            {
                cooldownRemaining = cooldown;
                contactedBalls.Clear();
                contactedParticipants.Clear();
                DashStarted?.Invoke();
            }

            if (player.IsDashing)
            {
                player.SetDashAim(aim);
                TryProcessContacts();
            }
        }

        /// <summary>Queues the latest valid one-step dash-kick aim.</summary>
        public bool RequestKick(Vector3 aim)
        {
            if (!isActiveAndEnabled || !simulationEnabled || !IsFinite(aim) || aim.sqrMagnitude <= Epsilon)
            {
                return false;
            }

            requestedAim = aim.normalized;
            requestPending = true;
            return true;
        }

        /// <summary>Enables or freezes dash-kick input without changing cooldown ownership.</summary>
        public void SetSimulationEnabled(bool enabled)
        {
            simulationEnabled = enabled;
            if (!enabled)
            {
                contactedBalls.Clear();
                contactedParticipants.Clear();
                ClearProgrammaticRequest();
            }
        }

        /// <summary>Clears cooldown and per-activation contact state for coordinated reset.</summary>
        public void ResetState()
        {
            cooldownRemaining = 0f;
            contactedBalls.Clear();
            contactedParticipants.Clear();
            ClearProgrammaticRequest();
        }

        private void ClearProgrammaticRequest()
        {
            requestPending = false;
            requestedAim = Vector3.zero;
        }

        private void TryProcessContacts()
        {
            if (controller == null || !DashKickRules.IsContactActive(player.DashElapsed, dashContactStartDelay))
            {
                return;
            }

            var dashDirection = player.DashDirection;
            if (dashDirection.sqrMagnitude <= Epsilon)
            {
                return;
            }

            var center = controller.transform.TransformPoint(controller.center);
            var count = UnityEngine.Physics.OverlapCapsuleNonAlloc(
                center,
                center + dashDirection * dashContactReach,
                controller.radius + dashContactRadiusPadding,
                contactBuffer,
                ~0,
                QueryTriggerInteraction.Ignore);

            BallMotor contactedBall = null;
            ParticipantState nearestParticipant = null;
            var nearestDistance = float.PositiveInfinity;
            var nearestEntityId = EntityId.None;

            for (var i = 0; i < count; i++)
            {
                var hit = contactBuffer[i];
                contactBuffer[i] = null;
                if (hit == null)
                {
                    continue;
                }

                var hitBall = hit.GetComponentInParent<BallMotor>();
                if (hitBall != null && hitBall == ball && DashKickRules.ShouldProcessContact(contactedBalls.Contains(hitBall)))
                {
                    contactedBall = hitBall;
                }

                var participant = hit.GetComponentInParent<ParticipantState>();
                if (participant == null || !DashKickRules.ShouldProcessContact(contactedParticipants.Contains(participant)) || !DashKickRules.IsForward(center, dashDirection, participant.transform.position))
                {
                    continue;
                }

                var distance = Vector3.Distance(center, participant.transform.position);
                var entityId = participant.GetEntityId();
                if (nearestParticipant == null || DashKickRules.IsBetterContactCandidate(distance, entityId, nearestDistance, nearestEntityId))
                {
                    nearestParticipant = participant;
                    nearestDistance = distance;
                    nearestEntityId = entityId;
                }
            }

            if (contactedBall != null)
            {
                contactedBalls.Add(contactedBall);
                if (contactedBall.ApplyKick(dashDirection, player.Velocity, speedFraction, playerMomentumShare))
                {
                    KickSucceeded?.Invoke();
                }
            }

            if (nearestParticipant == null)
            {
                return;
            }

            contactedParticipants.Add(nearestParticipant);
            if (!nearestParticipant.TryApplyDamage(ownerParticipant, enemyContactDamage, ParticipantDamageCause.DashKick, "Dash Kick"))
            {
                return;
            }

            var shoveDirection = new Vector3(dashDirection.x, 0f, dashDirection.z) + Vector3.up * 0.15f;
            if (shoveDirection.sqrMagnitude > Epsilon)
            {
                nearestParticipant.Motor?.AddExternalImpulse(shoveDirection.normalized * enemyShoveImpulse);
            }
            player.EndDash(DashEndReason.EnemyContact, enemyDashRetention);
        }

        private void OnPlayerCollision(ControllerColliderHit hit)
        {
            if (hit == null || player == null || !player.IsDashing || hit.collider == null)
            {
                return;
            }

            if (hit.collider.GetComponentInParent<BallMotor>() != null || hit.collider.GetComponentInParent<ParticipantState>() != null)
            {
                return;
            }

            if (controller != null && MovementMath.IsWalkableNormal(hit.normal, controller.slopeLimit))
            {
                return;
            }

            if (Vector3.Dot(player.DashDirection, hit.normal) < -Epsilon)
            {
                player.EndDash(DashEndReason.Wall, 0f);
            }
        }

        private void SubscribeCollision()
        {
            if (collisionSubscribed || player == null)
            {
                return;
            }

            player.CollisionHit += OnPlayerCollision;
            collisionSubscribed = true;
        }

        private void UnsubscribeCollision()
        {
            if (!collisionSubscribed || player == null)
            {
                return;
            }

            player.CollisionHit -= OnPlayerCollision;
            collisionSubscribed = false;
        }

        private void CacheSameObjectReferences()
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
            if (ownerParticipant == null)
            {
                ownerParticipant = GetComponent<ParticipantState>();
            }
        }

        private bool ValidateComposition()
        {
            if (input != null && player != null && look != null && aimCamera != null && ownerParticipant != null && controller != null)
            {
                return true;
            }

            Debug.LogError("BallKick requires serialized references: input, player, look, aimCamera, ownerParticipant, and CharacterController.", this);
            enabled = false;
            return false;
        }

        private Vector3 GetAimDirection()
        {
            if (aimCamera != null)
            {
                return aimCamera.transform.forward;
            }

            return look != null && look.Head != null ? look.Head.forward : transform.forward;
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
