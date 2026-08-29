using System;
using System.Collections.Generic;
using UnityEngine;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Pickups;

namespace RocketFooxball.Runtime.Bots
{
    /// <summary>Bounded, per-bot world observer. It never discovers scene objects at runtime.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-300)]
    public sealed class BotPerception : MonoBehaviour
    {
        private const int RosterSize = 6;
        private const int PickupCount = 6;
        public const float ExpectedSightDistance = 75f;
        public const float ExpectedFieldOfViewDegrees = 130f;
        public const float ExpectedMemorySeconds = 1.5f;
        private const float TargetDistanceEpsilon = 0.05f;
        private const float PositionEpsilon = 0.000001f;

        [Header("Observer")]
        [SerializeField] private ParticipantState self;
        [SerializeField] private MatchController match;
        [SerializeField] private BallMotor ball;
        [SerializeField] private Transform head;

        [Header("Roster")]
        [SerializeField] private ParticipantState[] roster = new ParticipantState[RosterSize];

        [Header("Pickups")]
        [SerializeField] private ArenaPickup[] pickups = new ArenaPickup[PickupCount];

        [Header("Goals and Visibility")]
        [SerializeField] private GoalTrigger ownGoal;
        [SerializeField] private GoalTrigger enemyGoal;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField, Min(0f)] private float sightDistance = ExpectedSightDistance;
        [SerializeField, Range(1f, 360f)] private float fieldOfViewDegrees = ExpectedFieldOfViewDegrees;
        [SerializeField, Min(0f)] private float memorySeconds = ExpectedMemorySeconds;

        private readonly RaycastHit[] raycastHits = new RaycastHit[8];
        private readonly BotPickupMemoryState[] pickupMemory = new BotPickupMemoryState[PickupCount];
        private readonly float[] pickupLastSeenTimes = new float[PickupCount];
        private readonly BotPickupKind[] pickupKinds = new BotPickupKind[PickupCount];
        private readonly BotPickupObservation[] pickupObservations = new BotPickupObservation[PickupCount];
        private readonly BotParticipantObservation[] participantObservations = new BotParticipantObservation[RosterSize];
        private readonly bool[] participantKnown = new bool[RosterSize];
        private readonly float[] participantLastSeenTimes = new float[RosterSize];

        private BotBallObservation ballObservation;
        private bool ballKnown;
        private float ballLastSeenTime;
        private float gameplayTime;
        private float sightCosine;
        private bool compositionValid;
        private bool resetEventSubscribed;

        public int ObserverSlotId => self != null ? self.SlotId : -1;
        public float GameplayTime => gameplayTime;
        public Vector3 OwnGoalPosition => ownGoal != null ? ownGoal.transform.position : Vector3.zero;
        public Vector3 EnemyGoalPosition => enemyGoal != null ? enemyGoal.transform.position : Vector3.zero;

        private void OnEnable()
        {
            compositionValid = ValidateComposition();
            ResetMemory();
            if (!compositionValid)
            {
                enabled = false;
                return;
            }

            if (!resetEventSubscribed)
            {
                match.CoordinatedResetRequested += OnCoordinatedResetRequested;
                resetEventSubscribed = true;
            }
        }

        private void OnDisable()
        {
            if (resetEventSubscribed && match != null)
            {
                match.CoordinatedResetRequested -= OnCoordinatedResetRequested;
            }

            resetEventSubscribed = false;
            ResetMemory();
        }

        private void FixedUpdate()
        {
            if (!compositionValid)
            {
                return;
            }

            if (!IsPlaying())
            {
                return;
            }

            var gameplayDelta = SafeFixedDeltaTime();
            gameplayTime += gameplayDelta;
            SampleBall();
            SampleParticipants();
            SamplePickups(gameplayDelta);
        }

        public bool TryGetBallObservation(out BotBallObservation observation)
        {
            if (ballKnown && ballObservation.HasObservation)
            {
                observation = ballObservation;
                return true;
            }

            observation = default(BotBallObservation);
            return false;
        }

        public bool TryGetParticipantObservation(int slotId, out BotParticipantObservation observation)
        {
            if (roster == null)
            {
                observation = default(BotParticipantObservation);
                return false;
            }

            for (var i = 0; i < roster.Length; i++)
            {
                if (roster[i] == null || roster[i].SlotId != slotId || !participantKnown[i])
                {
                    continue;
                }

                observation = participantObservations[i];
                if (observation.HasObservation)
                {
                    return true;
                }
                break;
            }

            observation = default(BotParticipantObservation);
            return false;
        }

        public bool TryGetPickupObservation(int stablePickupId, out BotPickupObservation observation)
        {
            if (pickups != null && pickups.Length == PickupCount && stablePickupId >= 0 && stablePickupId < PickupCount)
            {
                observation = pickupObservations[stablePickupId];
                if (observation.HasObservation)
                {
                    return true;
                }
            }

            observation = default(BotPickupObservation);
            return false;
        }

        private void SampleBall()
        {
            if (ball == null)
            {
                ballKnown = false;
                ballObservation = default(BotBallObservation);
                return;
            }

            if (IsVisibleTarget(ball.transform, ball.Rigidbody))
            {
                ballKnown = true;
                ballLastSeenTime = gameplayTime;
                ballObservation = new BotBallObservation(
                    true,
                    true,
                    ball.IsGrounded,
                    ball.transform.position,
                    ball.Velocity,
                    0f);
                return;
            }

            if (!ballKnown)
            {
                return;
            }

            var age = AgeSince(ballLastSeenTime);
            if (age > memorySeconds)
            {
                ballKnown = false;
                ballObservation = default(BotBallObservation);
                return;
            }

            ballObservation = new BotBallObservation(
                true,
                false,
                ballObservation.IsGrounded,
                ballObservation.Position,
                ballObservation.Velocity,
                age);
        }

        private void SampleParticipants()
        {
            for (var i = 0; i < roster.Length; i++)
            {
                var participant = roster[i];
                if (participant == null)
                {
                    participantKnown[i] = false;
                    participantObservations[i] = default(BotParticipantObservation);
                    continue;
                }

                if (participant == self)
                {
                    participantKnown[i] = true;
                    participantLastSeenTimes[i] = gameplayTime;
                    participantObservations[i] = CopyParticipant(participant, true, 0f);
                    continue;
                }

                var visible = IsVisibleTarget(participant.transform, null);
                if (participant.Team == self.Team)
                {
                    // Ally safety/etiquette data is intentionally not gated by LOS.
                    participantKnown[i] = true;
                    participantLastSeenTimes[i] = gameplayTime;
                    participantObservations[i] = CopyParticipant(participant, visible, 0f);
                    continue;
                }

                if (visible)
                {
                    if (!participant.IsAlive)
                    {
                        participantKnown[i] = false;
                        participantObservations[i] = default(BotParticipantObservation);
                        continue;
                    }

                    participantKnown[i] = true;
                    participantLastSeenTimes[i] = gameplayTime;
                    participantObservations[i] = CopyParticipant(participant, true, 0f);
                    continue;
                }

                if (!participantKnown[i])
                {
                    continue;
                }

                var age = AgeSince(participantLastSeenTimes[i]);
                if (age > memorySeconds)
                {
                    participantKnown[i] = false;
                    participantObservations[i] = default(BotParticipantObservation);
                    continue;
                }

                var previous = participantObservations[i];
                participantObservations[i] = new BotParticipantObservation(
                    true,
                    false,
                    previous.SlotId,
                    previous.Team,
                    previous.IsLocalParticipant,
                    previous.IsAlive,
                    previous.Position,
                    previous.Velocity,
                    previous.Health,
                    previous.MaxHealth,
                    previous.HasShotgun,
                    previous.ShotgunShells,
                    previous.ShotgunShellCapacity,
                    age);
            }
        }

        private void SamplePickups(float gameplayDelta)
        {
            for (var i = 0; i < pickups.Length; i++)
            {
                var pickup = pickups[i];
                if (pickup == null)
                {
                    pickupObservations[i] = default(BotPickupObservation);
                    continue;
                }

                var visible = IsVisibleTarget(pickup.transform, null);
                var available = visible && pickup.IsAvailable;
                pickupMemory[i] = BotPerceptionMemory.UpdatePickup(
                    pickupMemory[i],
                    visible,
                    available,
                    pickup.RespawnDelay,
                    gameplayDelta);

                if (visible)
                {
                    pickupLastSeenTimes[i] = gameplayTime;
                }

                var age = visible ? 0f : AgeSince(pickupLastSeenTimes[i]);
                pickupObservations[i] = BotPerceptionMemory.PublishPickup(
                    i,
                    pickupKinds[i],
                    pickup.transform.position,
                    pickupMemory[i],
                    visible,
                    available,
                    age);
            }
        }

        private BotParticipantObservation CopyParticipant(ParticipantState participant, bool visible, float age)
        {
            var velocity = participant.Motor != null ? participant.Motor.Velocity : Vector3.zero;
            return new BotParticipantObservation(
                true,
                visible,
                participant.SlotId,
                participant.Team,
                participant.IsLocalParticipant,
                participant.IsAlive,
                participant.transform.position,
                velocity,
                participant.Health,
                participant.MaxHealth,
                participant.HasShotgun,
                participant.ShotgunShells,
                participant.ShotgunShellCapacity,
                age);
        }

        private bool IsVisibleTarget(Transform target, Rigidbody targetBody)
        {
            if (target == null || head == null)
            {
                return false;
            }

            var offset = target.position - head.position;
            var distance = offset.magnitude;
            if (!IsFinite(distance) || distance > sightDistance)
            {
                return false;
            }

            if (distance > PositionEpsilon)
            {
                var direction = offset / distance;
                var forward = head.forward;
                if (forward.sqrMagnitude <= PositionEpsilon || Vector3.Dot(forward.normalized, direction) < sightCosine)
                {
                    return false;
                }

                return HasLineOfSight(head.position, direction, distance, target, targetBody);
            }

            return true;
        }

        private bool HasLineOfSight(Vector3 origin, Vector3 direction, float distance, Transform target, Rigidbody targetBody)
        {
            var hitCount = UnityEngine.Physics.RaycastNonAlloc(
                origin,
                direction,
                raycastHits,
                distance,
                EffectiveObstacleMask(),
                QueryTriggerInteraction.Ignore);
            if (hitCount <= 0)
            {
                return true;
            }

            var targetRoot = target.root;
            var blockingDistance = distance - TargetDistanceEpsilon;
            for (var i = 0; i < hitCount; i++)
            {
                var collider = raycastHits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                if (collider.transform.root == targetRoot ||
                    (targetBody != null && collider.attachedRigidbody == targetBody))
                {
                    continue;
                }

                if (raycastHits[i].distance < blockingDistance)
                {
                    return false;
                }
            }

            return true;
        }

        private int EffectiveObstacleMask()
        {
            var mask = obstacleMask.value;
            RemoveNamedLayer(ref mask, "Participants");
            RemoveNamedLayer(ref mask, "Projectiles");
            return mask;
        }

        private static void RemoveNamedLayer(ref int mask, string layerName)
        {
            var layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                mask &= ~(1 << layer);
            }
        }

        private bool ValidateComposition()
        {
            if (self == null || match == null || ball == null || head == null || ownGoal == null || enemyGoal == null ||
                roster == null || roster.Length != RosterSize || pickups == null || pickups.Length != PickupCount)
            {
                Debug.LogError("BotPerception requires serialized references: self, match, ball, head, six roster participants, six ordered pickups, ownGoal, enemyGoal.", this);
                return false;
            }

            var objectSet = new HashSet<ParticipantState>();
            var slotSet = new HashSet<int>();
            var pickupSet = new HashSet<ArenaPickup>();
            var blueCount = 0;
            var redCount = 0;
            var hasSelf = false;
            for (var i = 0; i < roster.Length; i++)
            {
                var participant = roster[i];
                if (participant == null || !objectSet.Add(participant) || !slotSet.Add(participant.SlotId))
                {
                    Debug.LogError("BotPerception requires six non-null roster participants with unique objects and slot IDs.", this);
                    return false;
                }

                if (participant.Team == ParticipantTeam.Blue)
                {
                    blueCount++;
                }
                else if (participant.Team == ParticipantTeam.Red)
                {
                    redCount++;
                }
                else
                {
                    Debug.LogError("BotPerception roster participants must use Blue or Red teams.", this);
                    return false;
                }

                hasSelf |= participant == self;
            }

            if (!hasSelf || blueCount != 3 || redCount != 3)
            {
                Debug.LogError("BotPerception requires self in a six-slot roster with exactly three Blue and three Red participants.", this);
                return false;
            }

            if (ownGoal.DefendingTeam != self.Team || enemyGoal.DefendingTeam == self.Team)
            {
                Debug.LogError("BotPerception ownGoal and enemyGoal must defend self's team and the opposing team respectively.", this);
                return false;
            }

            for (var i = 0; i < pickups.Length; i++)
            {
                var pickup = pickups[i];
                if (pickup == null || !pickupSet.Add(pickup) || !TryGetPickupKind(pickup, out var kind))
                {
                    Debug.LogError("BotPerception requires six non-null pickups of Health, Shotgun, and Ammo types.", this);
                    return false;
                }

                var expected = ExpectedPickupKind(i);
                if (kind != expected)
                {
                    Debug.LogError("BotPerception pickup order must be health IDs 0-1, shotgun IDs 2-3, and ammo IDs 4-5.", this);
                    return false;
                }

                pickupKinds[i] = kind;
            }

            if (!IsFinite(sightDistance) || sightDistance <= 0f ||
                !IsFinite(fieldOfViewDegrees) || fieldOfViewDegrees <= 0f || fieldOfViewDegrees > 360f ||
                !IsFinite(memorySeconds) || memorySeconds < 0f || ContainsForbiddenLayer(obstacleMask.value))
            {
                Debug.LogError("BotPerception requires positive finite sight distance, valid field of view and memory duration, and an obstacle mask excluding Participants and Projectiles.", this);
                return false;
            }

            sightCosine = Mathf.Cos(fieldOfViewDegrees * 0.5f * Mathf.Deg2Rad);
            return true;
        }

        private static BotPickupKind ExpectedPickupKind(int index)
        {
            if (index < 2)
            {
                return BotPickupKind.Health;
            }
            if (index < 4)
            {
                return BotPickupKind.Shotgun;
            }
            return BotPickupKind.Ammo;
        }

        private static bool TryGetPickupKind(ArenaPickup pickup, out BotPickupKind kind)
        {
            if (pickup is HealthPickup)
            {
                kind = BotPickupKind.Health;
                return true;
            }
            if (pickup is ShotgunPickup)
            {
                kind = BotPickupKind.Shotgun;
                return true;
            }
            if (pickup is AmmoPickup)
            {
                kind = BotPickupKind.Ammo;
                return true;
            }

            kind = BotPickupKind.Health;
            return false;
        }

        private bool ContainsForbiddenLayer(int mask)
        {
            return ContainsLayer(mask, "Participants") || ContainsLayer(mask, "Projectiles");
        }

        private static bool ContainsLayer(int mask, string layerName)
        {
            var layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 && (mask & (1 << layer)) != 0;
        }

        private void OnCoordinatedResetRequested(MatchResetReason reason)
        {
            ResetMemory();
        }

        private void ResetMemory()
        {
            gameplayTime = 0f;
            ballKnown = false;
            ballLastSeenTime = 0f;
            ballObservation = default(BotBallObservation);
            for (var i = 0; i < pickupMemory.Length; i++)
            {
                pickupMemory[i] = default(BotPickupMemoryState);
                pickupLastSeenTimes[i] = 0f;
                pickupObservations[i] = default(BotPickupObservation);
            }
            for (var i = 0; i < participantKnown.Length; i++)
            {
                participantKnown[i] = false;
                participantLastSeenTimes[i] = 0f;
                participantObservations[i] = default(BotParticipantObservation);
            }
        }

        private bool IsPlaying()
        {
            return match != null && match.State == MatchController.MatchState.Playing;
        }

        private static float SafeFixedDeltaTime()
        {
            return IsFinite(Time.fixedDeltaTime) && Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0f;
        }

        private float AgeSince(float sampleTime)
        {
            var age = gameplayTime - sampleTime;
            return IsFinite(age) && age >= 0f ? age : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
