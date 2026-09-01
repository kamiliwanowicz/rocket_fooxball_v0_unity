using System;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Pickups;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Fixed-step hitscan shotgun owned by one participant.</summary>
    [MovedFrom("RocketFooxball")]
    public sealed class ShotgunWeapon : MonoBehaviour
    {
        private const float SpreadBasisEpsilon = 0.000001f;
        private const float SpreadReferenceParallelThreshold = 0.999f;
        private const float VisualTraceRange = 180f;

        [Header("References")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerLook look;
        [SerializeField] private Camera aimCamera;
        [SerializeField] private ParticipantState ownerParticipant;
        [SerializeField] private BallMotor ball;
        [SerializeField] private WeaponImpactFeedback impactFeedback;

        [Header("Hitscan")]
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField, Min(0f)] private float pelletDamage = ShotgunDamageRules.DefaultPelletDamage;
        [SerializeField, Min(1)] private int pelletCount = ShotgunDamageRules.DefaultPelletCount;
        [SerializeField, Min(0f)] private float spreadAngleDegrees = ShotgunDamageRules.DefaultSpreadAngleDegrees;
        [SerializeField, Min(0f)] private float fullDamageRange = ShotgunDamageRules.DefaultFullDamageRange;
        [SerializeField, Min(0f)] private float mediumRange = ShotgunDamageRules.DefaultMediumRange;
        [SerializeField, Min(0f)] private float maxRange = ShotgunDamageRules.DefaultMaxRange;
        [SerializeField, Range(0f, 1f)] private float mediumMultiplier = ShotgunDamageRules.DefaultMediumMultiplier;
        [SerializeField, Range(0f, 1f)] private float farMultiplier = ShotgunDamageRules.DefaultFarMultiplier;

        [Header("Timing and Ball")]
        [SerializeField, Min(0f)] private float pumpDelay = ShotgunDamageRules.DefaultPumpDelay;
        [SerializeField, Min(0f)] private float ballImpulsePerPellet = ShotgunDamageRules.DefaultPerPelletBallImpulse;
        [SerializeField, Min(0f)] private float ballImpulseCap = ShotgunDamageRules.DefaultBallImpulseCap;

        private const float Epsilon = 0.000001f;
        private readonly ParticipantState[] participantTargets = new ParticipantState[16];
        private readonly float[] participantDamage = new float[16];
        private float pumpRemaining;
        private bool simulationEnabled = true;
        private bool paused;
        private bool requestPending;
        private Vector3 requestedOrigin;
        private Vector3 requestedDirection;

        public bool CanFire => !paused && simulationEnabled && ownerParticipant != null && ownerParticipant.IsAlive &&
                               ownerParticipant.HasShotgun && ownerParticipant.ShotgunShells > 0 &&
                               pumpRemaining <= 0f;
        public float PumpRemaining => Mathf.Max(pumpRemaining, 0f);
        public float PelletDamage => pelletDamage;
        public int PelletCount => ShotgunDamageRules.ClampPelletCount(pelletCount);
        public float SpreadAngleDegrees => spreadAngleDegrees;
        public float FullDamageRange => fullDamageRange;
        public float MediumRange => mediumRange;
        public float MaxRange => maxRange;
        public float MediumMultiplier => mediumMultiplier;
        public float FarMultiplier => farMultiplier;
        public float PumpDelay => pumpDelay;
        public float BallImpulsePerPellet => ballImpulsePerPellet;
        public float BallImpulseCap => ballImpulseCap;
        public LayerMask HitMask => hitMask;

        /// <summary>Raised once for each accepted shot, before hit dispatch.</summary>
        public event Action ShotFired;

        /// <summary>Raised once when an enemy participant accepts accumulated damage.</summary>
        public event Action HitConfirmed;

        private void Awake()
        {
            CacheReferences();
            ValidateComposition();
        }

        private void OnEnable()
        {
            CacheReferences();
        }

        private void OnDisable()
        {
            paused = false;
            ClearProgrammaticRequest();
        }

        private void FixedUpdate()
        {
            if (paused)
            {
                return;
            }

            pumpRemaining = Mathf.Max(pumpRemaining - Time.fixedDeltaTime, 0f);

            var localRequest = input != null && input.ConsumeShotgunPressed();
            var hasRequest = requestPending || localRequest;
            if (!hasRequest)
            {
                return;
            }

            // Every edge/request is one-shot, including an attempt while pumping,
            // empty, unowned, or simulation-disabled.
            var useProgrammaticRequest = requestPending;
            var programmaticOrigin = requestedOrigin;
            var programmaticDirection = requestedDirection;
            requestPending = false;
            requestedOrigin = Vector3.zero;
            requestedDirection = Vector3.zero;

            if (!CanFire)
            {
                return;
            }

            var origin = useProgrammaticRequest ? programmaticOrigin : GetAimOrigin();
            var direction = useProgrammaticRequest ? programmaticDirection : GetAimDirection();
            if (!IsFinite(origin) || !IsFinite(direction) || direction.sqrMagnitude <= Epsilon)
            {
                return;
            }

            if (!ownerParticipant.TryConsumeShotgunShell())
            {
                return;
            }

            pumpRemaining = Mathf.Max(pumpDelay, 0f);
            ownerParticipant.CancelImmunity();
            ShotFired?.Invoke();
            ResolveShot(origin, direction.normalized, useProgrammaticRequest);
        }

        /// <summary>Queues the latest valid programmatic one-slot fire request.</summary>
        public bool RequestFire(Vector3 origin, Vector3 direction)
        {
            if (paused || !isActiveAndEnabled || !simulationEnabled || !IsFinite(origin) ||
                !IsFinite(direction) || direction.sqrMagnitude <= Epsilon)
            {
                return false;
            }

            requestedOrigin = origin;
            requestedDirection = direction;
            requestPending = true;
            return true;
        }

        /// <summary>Enables fixed-step firing without changing inventory state.</summary>
        public void SetSimulationEnabled(bool enabled)
        {
            if (!enabled)
            {
                paused = false;
            }

            simulationEnabled = enabled;
            if (!enabled)
            {
                ClearProgrammaticRequest();
            }
        }

        /// <summary>Freezes firing while preserving pump timing and inventory.</summary>
        public void SetPaused(bool pausedState)
        {
            if (paused == pausedState)
            {
                return;
            }

            paused = pausedState;
            if (paused)
            {
                ClearProgrammaticRequest();
            }
        }

        /// <summary>Clears pump timing and queued programmatic input for a reset.</summary>
        public void ResetState()
        {
            paused = false;
            pumpRemaining = 0f;
            ClearProgrammaticRequest();
        }

        private void ClearProgrammaticRequest()
        {
            requestPending = false;
            requestedOrigin = Vector3.zero;
            requestedDirection = Vector3.zero;
        }

        private void ResolveShot(Vector3 origin, Vector3 forward, bool useProgrammaticRequest)
        {
            Array.Clear(participantTargets, 0, participantTargets.Length);
            Array.Clear(participantDamage, 0, participantDamage.Length);
            var targetCount = 0;
            var accumulatedBallFalloff = 0f;
            var accumulatedBallDirection = Vector3.zero;
            var effectiveMask = hitMask.value;
            var projectileLayer = LayerMask.NameToLayer("Projectiles");
            if (projectileLayer >= 0)
            {
                effectiveMask &= ~(1 << projectileLayer);
            }

            var right = aimCamera != null ? aimCamera.transform.right : transform.right;
            var up = aimCamera != null ? aimCamera.transform.up : transform.up;
            if (useProgrammaticRequest)
            {
                var referenceUp = aimCamera != null ? aimCamera.transform.up : Vector3.up;
                if (!TryBuildSpreadBasis(forward, referenceUp, out right, out up))
                {
                    return;
                }
            }

            var spreadScale = Mathf.Tan(Mathf.Clamp(spreadAngleDegrees, 0f, 89f) * Mathf.Deg2Rad);
            var count = ShotgunDamageRules.ClampPelletCount(pelletCount);
            var traceRange = Mathf.Max(VisualTraceRange, Mathf.Max(maxRange, 0f));
            for (var pelletIndex = 0; pelletIndex < count; pelletIndex++)
            {
                var offset = ShotgunSpreadPattern.GetOffset(pelletIndex) * spreadScale;
                var direction = useProgrammaticRequest
                    ? GetProgrammaticPelletDirection(pelletIndex, forward, right, up, spreadAngleDegrees)
                    : (forward + right * offset.x + up * offset.y).normalized;
                var didHit = UnityEngine.Physics.Raycast(
                    origin,
                    direction,
                    out var hit,
                    traceRange,
                    effectiveMask,
                    QueryTriggerInteraction.Ignore);
                if (didHit)
                {
                    impactFeedback?.EmitTracer(origin, hit.point);
                    if (ShouldEmitShotgunMark(hit))
                    {
                        impactFeedback?.EmitShotgunMark(hit.point, hit.normal);
                    }
                }
                else
                {
                    impactFeedback?.EmitTracer(origin, origin + direction * traceRange);
                }

                if (!didHit)
                {
                    continue;
                }

                var distance = Mathf.Max(hit.distance, Vector3.Distance(origin, hit.point));
                var falloff = ShotgunDamageRules.EvaluateFalloff(
                    distance,
                    fullDamageRange,
                    mediumRange,
                    maxRange,
                    mediumMultiplier,
                    farMultiplier);
                if (falloff <= 0f)
                {
                    continue;
                }

                var participant = hit.collider != null ? hit.collider.GetComponentInParent<ParticipantState>() : null;
                if (participant != null)
                {
                    if (ParticipantRelationshipAdapter.Classify(participant, ownerParticipant) == ParticipantRelationship.Enemy)
                    {
                        var index = FindOrAddParticipant(participant, ref targetCount);
                        if (index >= 0)
                        {
                            participantDamage[index] += ShotgunDamageRules.CalculatePelletDamage(pelletDamage, falloff);
                        }
                    }
                    // A participant, including self/friendly/immune, is the first
                    // hit and therefore blocks this pellet.
                    continue;
                }

                var hitBall = hit.collider != null ? hit.collider.GetComponentInParent<BallMotor>() : null;
                if (hitBall != null && hitBall == ball)
                {
                    accumulatedBallFalloff += falloff;
                    accumulatedBallDirection += direction * falloff;
                }
            }

            var hitConfirmed = false;
            for (var targetIndex = 0; targetIndex < targetCount; targetIndex++)
            {
                var target = participantTargets[targetIndex];
                if (target != null)
                {
                    var damageRequest = new ParticipantDamageRequest(
                        ownerParticipant,
                        participantDamage[targetIndex],
                        ParticipantDamageCause.Shotgun,
                        "Shotgun",
                        origin);
                    if (target.TryApplyDamage(damageRequest))
                    {
                        hitConfirmed = true;
                    }
                }
            }

            if (ball != null && accumulatedBallFalloff > 0f && accumulatedBallDirection.sqrMagnitude > Epsilon)
            {
                var impulse = ShotgunDamageRules.CalculateBallImpulse(accumulatedBallFalloff, ballImpulsePerPellet, ballImpulseCap);
                if (impulse > 0f)
                {
                    if (ball.QueueImpulse(accumulatedBallDirection.normalized * impulse))
                    {
                        ball.RecordParticipantTouch(ownerParticipant);
                    }
                }
            }

            if (hitConfirmed)
            {
                HitConfirmed?.Invoke();
            }
        }

        private static bool ShouldEmitShotgunMark(RaycastHit hit)
        {
            var collider = hit.collider;
            return collider != null && !collider.isTrigger && collider.attachedRigidbody == null &&
                   collider.GetComponentInParent<ParticipantState>() == null &&
                   collider.GetComponentInParent<BallMotor>() == null &&
                   collider.GetComponentInParent<ArenaPickup>() == null;
        }

        private int FindOrAddParticipant(ParticipantState participant, ref int targetCount)
        {
            for (var i = 0; i < targetCount; i++)
            {
                if (participantTargets[i] == participant)
                {
                    return i;
                }
            }

            if (targetCount >= participantTargets.Length)
            {
                return -1;
            }

            participantTargets[targetCount] = participant;
            participantDamage[targetCount] = 0f;
            return targetCount++;
        }

        private Vector3 GetAimOrigin()
        {
            return aimCamera != null ? aimCamera.transform.position : transform.position;
        }

        private Vector3 GetAimDirection()
        {
            if (aimCamera != null)
            {
                return aimCamera.transform.forward;
            }

            return transform.forward;
        }

        private void CacheReferences()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (look == null) look = GetComponent<PlayerLook>();
            if (ownerParticipant == null) ownerParticipant = GetComponent<ParticipantState>();
            if (aimCamera == null) aimCamera = GetComponentInChildren<Camera>(true);
        }

        private bool ValidateComposition()
        {
            if (ownerParticipant == null)
            {
                Debug.LogError("ShotgunWeapon requires serialized ownerParticipant.", this);
                enabled = false;
                return false;
            }

            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>Builds a stable orthonormal tangent basis around a forward direction.</summary>
        public static bool TryBuildSpreadBasis(Vector3 forward, Vector3 referenceUp, out Vector3 right, out Vector3 up)
        {
            right = Vector3.zero;
            up = Vector3.zero;
            if (!IsFinite(forward) || forward.sqrMagnitude <= SpreadBasisEpsilon)
            {
                return false;
            }

            var normalizedForward = forward.normalized;
            var normalizedReferenceUp = IsFinite(referenceUp) && referenceUp.sqrMagnitude > SpreadBasisEpsilon
                ? referenceUp.normalized
                : Vector3.up;

            if (Mathf.Abs(Vector3.Dot(normalizedForward, normalizedReferenceUp)) >= SpreadReferenceParallelThreshold)
            {
                normalizedReferenceUp = Mathf.Abs(normalizedForward.y) < SpreadReferenceParallelThreshold
                    ? Vector3.up
                    : Vector3.right;
            }

            var rightCandidate = Vector3.Cross(normalizedReferenceUp, normalizedForward);
            if (rightCandidate.sqrMagnitude <= SpreadBasisEpsilon)
            {
                return false;
            }

            right = rightCandidate.normalized;
            up = Vector3.Cross(normalizedForward, right).normalized;
            return IsFinite(right) && IsFinite(up) &&
                   right.sqrMagnitude > SpreadBasisEpsilon && up.sqrMagnitude > SpreadBasisEpsilon;
        }

        /// <summary>Returns one normalized pellet direction from a precomputed tangent basis.</summary>
        public static Vector3 GetProgrammaticPelletDirection(
            int index,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            float spreadAngleDegrees)
        {
            if (index < 0 || index >= ShotgunSpreadPattern.Count || !IsFinite(forward) ||
                forward.sqrMagnitude <= SpreadBasisEpsilon || !IsFinite(right) || !IsFinite(up))
            {
                return Vector3.zero;
            }

            var spreadScale = Mathf.Tan(Mathf.Clamp(spreadAngleDegrees, 0f, 89f) * Mathf.Deg2Rad);
            var offset = ShotgunSpreadPattern.GetOffset(index) * spreadScale;
            var direction = (forward.normalized + right * offset.x + up * offset.y).normalized;
            return IsFinite(direction) && direction.sqrMagnitude > SpreadBasisEpsilon ? direction : Vector3.zero;
        }
    }
}
