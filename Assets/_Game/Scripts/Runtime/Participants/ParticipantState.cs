using System;
using UnityEngine;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Weapons;

namespace RocketFooxball.Runtime.Participants
{
    /// <summary>Owns one roster slot's identity, vitals, lifecycle, immunity, and leaf reset/gate commands.</summary>
    [DisallowMultipleComponent]
    public sealed class ParticipantState : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private int slotId;
        [SerializeField] private string displayName = "Participant";
        [SerializeField] private ParticipantTeam team = ParticipantTeam.Blue;
        [SerializeField] private bool localParticipant;

        [Header("Vitals")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField, Min(0f)] private float deathWait = 5f;
        [SerializeField, Min(0f)] private float immunityDuration = 2f;
        [SerializeField, Min(1)] private int shotgunShellCapacity = 16;

        [Header("Leaf Owners")]
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerLook look;
        [SerializeField] private BallKick kick;
        [SerializeField] private RocketLauncher launcher;
        [SerializeField] private ShotgunWeapon shotgun;
        [SerializeField] private BotController botController;
        [SerializeField] private PlayerPresentation presentation;
        [SerializeField] private PlayerCameraFeedback cameraFeedback;

        private float health;
        private float deathRemaining;
        private float immunityRemaining;
        private ParticipantLifecycle lifecycle = ParticipantLifecycle.Alive;
        private bool matchSimulationEnabled = true;
        private bool matchPaused;
        private bool identityConfigured;
        private bool eventsSubscribed;
        private bool hasShotgun = true;
        private int shotgunShells = 8;

        public int SlotId => slotId;
        public string DisplayName => displayName;
        public ParticipantTeam Team => team;
        public bool IsLocalParticipant => localParticipant;
        public float Health => health;
        public float MaxHealth => maxHealth;
        public float DeathWait => deathWait;
        public float ImmunityDuration => immunityDuration;
        public ParticipantLifecycle Lifecycle => lifecycle;
        public ParticipantLifecycle State => lifecycle;
        public bool IsAlive => lifecycle == ParticipantLifecycle.Alive;
        public bool IsDead => lifecycle == ParticipantLifecycle.Dead;
        public bool IsRespawning => lifecycle == ParticipantLifecycle.Respawning;
        public bool IsLocal => localParticipant;
        public float CurrentHealth => health;
        public float RespawnRemaining => IsDead ? Mathf.Max(deathRemaining, 0f) : 0f;
        public float ImmunityRemaining => IsAlive ? Mathf.Max(immunityRemaining, 0f) : 0f;
        public bool IsImmune => IsAlive && immunityRemaining > 0f;
        public bool HasShotgun => hasShotgun;
        public int ShotgunShells => shotgunShells;
        public int ShotgunShellCapacity => Mathf.Max(1, shotgunShellCapacity);
        public ShotgunWeapon Shotgun => shotgun;
        public bool IsCollisionPassThrough => !IsAlive || IsImmune;
        public bool MatchSimulationEnabled => matchSimulationEnabled;
        public bool MatchPaused => matchPaused;
        public PlayerMotor Motor => motor;
        public CharacterController CharacterController => characterController;
        public PlayerInputReader Input => input;
        public PlayerLook Look => look;
        public BallKick Kick => kick;
        public RocketLauncher Launcher => launcher;
        public BotController BotController => botController;
        public PlayerPresentation Presentation => presentation;
        public PlayerCameraFeedback CameraFeedback => cameraFeedback;
        public ParticipantSlotContract Slot => new ParticipantSlotContract(slotId, displayName, team, localParticipant);
        public ParticipantReadModel ReadModel => new ParticipantReadModel(
            slotId,
            displayName,
            team,
            localParticipant,
            health,
            maxHealth,
            lifecycle,
            RespawnRemaining,
            ImmunityRemaining);

        public event Action<ParticipantReadModel> ReadModelChanged;
        public event Action<ParticipantDeathEvent> Died;
        public event Action<ParticipantLifecycleEvent> LifecycleChanged;
        public event Action<ParticipantState> RespawnRequested;
        public event Action<ParticipantState> CollisionStateChanged;

        private void Awake()
        {
            CacheReferences();
            health = Mathf.Clamp(maxHealth, 1f, Mathf.Max(maxHealth, 1f));
            maxHealth = Mathf.Max(maxHealth, 1f);
            shotgunShellCapacity = Mathf.Max(1, shotgunShellCapacity);
            shotgunShells = Mathf.Clamp(shotgunShells, 0, shotgunShellCapacity);
            identityConfigured = true;
            if (!ValidateComposition())
            {
                return;
            }

            ApplyLeafSimulation();
            presentation?.ConfigureSlot(this);
            presentation?.SetLocalMode(localParticipant);
            presentation?.SetAlive(true);
            presentation?.SetImmune(false);
            presentation?.SetShotgunOwned(hasShotgun);
        }

        private void OnEnable()
        {
            CacheReferences();
            SubscribeLeafEvents();
        }

        private void OnDisable()
        {
            UnsubscribeLeafEvents();
        }

        private void FixedUpdate()
        {
            if (matchPaused)
            {
                return;
            }

            var changed = false;
            if (IsAlive && immunityRemaining > 0f)
            {
                immunityRemaining = Mathf.Max(immunityRemaining - Time.fixedDeltaTime, 0f);
                if (immunityRemaining <= 0f)
                {
                    presentation?.SetImmune(false);
                    CollisionStateChanged?.Invoke(this);
                    changed = true;
                }
            }

            if (lifecycle == ParticipantLifecycle.Dead)
            {
                deathRemaining = Mathf.Max(deathRemaining - Time.fixedDeltaTime, 0f);
                if (deathRemaining <= 0f)
                {
                    SetLifecycle(ParticipantLifecycle.Respawning);
                    RespawnRequested?.Invoke(this);
                    changed = true;
                }
            }

            if (changed)
            {
                PublishReadModel();
            }
        }

        /// <summary>Locks serialized identity after composition; repeated matching calls are harmless.</summary>
        public bool ConfigureSlot(int configuredSlotId, string configuredDisplayName, ParticipantTeam configuredTeam, bool configuredLocal)
        {
            if (identityConfigured &&
                (slotId != configuredSlotId || !string.Equals(displayName, configuredDisplayName, StringComparison.Ordinal) ||
                 team != configuredTeam || localParticipant != configuredLocal))
            {
                Debug.LogError("ParticipantState identity is immutable after configuration.", this);
                return false;
            }

            slotId = configuredSlotId;
            displayName = configuredDisplayName ?? string.Empty;
            team = configuredTeam;
            localParticipant = configuredLocal;
            identityConfigured = true;
            presentation?.ConfigureSlot(this);
            presentation?.SetLocalMode(localParticipant);
            presentation?.SetShotgunOwned(hasShotgun);
            return true;
        }

        /// <summary>Applies match freeze/gate to leaf simulation while keeping lifecycle timers active.</summary>
        public void SetMatchSimulationEnabled(bool enabled)
        {
            matchSimulationEnabled = enabled;
            ApplyLeafSimulation();
        }

        /// <summary>Pauses leaf simulation without changing normal match/lifecycle gates.</summary>
        public void SetMatchPaused(bool paused)
        {
            if (matchPaused == paused)
            {
                return;
            }

            if (!paused && localParticipant)
            {
                input?.ClearGameplayState();
            }

            motor?.SetPaused(paused);
            kick?.SetPaused(paused);
            launcher?.SetPaused(paused);
            shotgun?.SetPaused(paused);
            if (localParticipant)
            {
                look?.SetPaused(paused);
            }
            else
            {
                botController?.SetPaused(paused);
            }
            matchPaused = paused;
        }

        /// <summary>Restores full health and alive state for coordinated kickoff/goal reset.</summary>
        public void ResetForKickoff()
        {
            var previous = PrepareKickoffReset();
            botController?.ResetState();
            FinishKickoffReset(previous);
        }

        /// <summary>Restores kickoff state and places slot without granting respawn immunity.</summary>
        public void ResetForKickoff(Vector3 worldPosition, Quaternion worldRotation)
        {
            var previous = PrepareKickoffReset();
            motor?.ResetState(worldPosition, worldRotation);
            if (motor == null)
            {
                transform.SetPositionAndRotation(worldPosition, worldRotation);
            }
            look?.ResetView(transform.forward);
            cameraFeedback?.ResetFeedback();
            botController?.ResetState();
            FinishKickoffReset(previous);
        }

        /// <summary>Moves participant to an authored spawn and grants post-respawn immunity.</summary>
        public void RespawnAt(Vector3 worldPosition, Quaternion worldRotation)
        {
            BeginLifecycleReset();
            var previous = lifecycle;
            lifecycle = ParticipantLifecycle.Alive;
            health = maxHealth;
            deathRemaining = 0f;
            immunityRemaining = Mathf.Max(immunityDuration, 0f);
            ClearShotgunState();
            motor?.ResetState(worldPosition, worldRotation);
            if (motor == null)
            {
                transform.SetPositionAndRotation(worldPosition, worldRotation);
            }
            look?.ResetView(transform.forward);
            input?.ResetInputState();
            kick?.ResetState();
            launcher?.ResetState();
            cameraFeedback?.ResetFeedback();
            botController?.ResetState();
            presentation?.SetAlive(true);
            presentation?.SetImmune(immunityRemaining > 0f);
            ApplyLeafSimulation();
            if (previous != lifecycle)
            {
                LifecycleChanged?.Invoke(new ParticipantLifecycleEvent(this, previous, lifecycle));
            }
            CollisionStateChanged?.Invoke(this);
            PublishReadModel();
        }

        /// <summary>Cancels immunity once for offensive action or meaningful ball contact.</summary>
        public void CancelImmunity()
        {
            if (!IsImmune)
            {
                return;
            }

            immunityRemaining = 0f;
            presentation?.SetImmune(false);
            CollisionStateChanged?.Invoke(this);
            PublishReadModel();
        }

        /// <summary>Applies enemy damage. Self and same-team requests reject before health mutation.</summary>
        public bool TryApplyDamage(ParticipantState attacker, float amount, ParticipantDamageCause cause, string weapon = "Rocket Launcher")
        {
            if (!IsAlive || IsImmune || !IsFinite(amount) || amount <= 0f || attacker == this || (attacker != null && attacker.Team == team))
            {
                return false;
            }

            var healthBefore = health;
            health = Mathf.Max(health - amount, 0f);
            if (health <= 0f)
            {
                KillInternal(attacker, cause, weapon, healthBefore);
            }
            else
            {
                PublishReadModel();
            }
            return true;
        }

        public bool TryApplyDamage(ParticipantDamageRequest request)
        {
            return TryApplyDamage(request.Attacker, request.Amount, request.Cause, request.Weapon);
        }

        public bool TryTakeDamage(ParticipantState attacker, float amount, ParticipantDamageCause cause, string weapon = "Rocket Launcher")
        {
            return TryApplyDamage(attacker, amount, cause, weapon);
        }

        /// <summary>Restores health without overheal; pickup owners request this mutation.</summary>
        public bool TryRestoreHealth(float amount)
        {
            if (!IsAlive || !IsFinite(amount) || amount <= 0f || health >= maxHealth)
            {
                return false;
            }

            var previous = health;
            health = Mathf.Min(maxHealth, health + amount);
            if (health <= previous)
            {
                return false;
            }
            PublishReadModel();
            return true;
        }

        /// <summary>Terminates participant without killer attribution, used by arena/self damage.</summary>
        public bool KillFromArena()
        {
            if (!IsAlive)
            {
                return false;
            }

            KillInternal(null, ParticipantDamageCause.Arena, "Arena", health);
            return true;
        }

        public bool KillFromSelf()
        {
            if (!IsAlive)
            {
                return false;
            }

            KillInternal(null, ParticipantDamageCause.Other, "Self", health);
            return true;
        }

        /// <summary>Marks contact with ball as meaningful only after caller applies speed threshold.</summary>
        public void NotifyMeaningfulBallContact()
        {
            CancelImmunity();
        }

        /// <summary>Collects a shotgun and its standard eight-shell pack.</summary>
        public bool TryCollectShotgun()
        {
            return TryCollectShotgun(8);
        }

        /// <summary>Collects a shotgun pickup with a caller-supplied shell grant.</summary>
        public bool TryCollectShotgun(int shellGrant)
        {
            if (!IsAlive || shotgunShellCapacity <= 0)
            {
                return false;
            }

            if (!ShotgunAmmoRules.TryApplyPickup(
                    hasShotgun,
                    shotgunShells,
                    shellGrant,
                    ShotgunShellCapacity,
                    true,
                    out var nextHasShotgun,
                    out var nextShells))
            {
                return false;
            }

            hasShotgun = nextHasShotgun;
            shotgunShells = nextShells;
            presentation?.SetShotgunOwned(hasShotgun);
            return true;
        }

        /// <summary>Collects an ammo pack without requiring a carried shotgun.</summary>
        public bool TryCollectShotgunAmmo(int shellGrant = 8)
        {
            if (!IsAlive || shotgunShellCapacity <= 0)
            {
                return false;
            }

            if (!ShotgunAmmoRules.TryApplyPickup(
                    hasShotgun,
                    shotgunShells,
                    shellGrant,
                    ShotgunShellCapacity,
                    false,
                    out var nextHasShotgun,
                    out var nextShells))
            {
                return false;
            }

            hasShotgun = nextHasShotgun;
            shotgunShells = nextShells;
            return true;
        }

        /// <summary>Consumes one shell while retaining the empty carried weapon.</summary>
        public bool TryConsumeShotgunShell()
        {
            if (!IsAlive || !hasShotgun || shotgunShells <= 0)
            {
                return false;
            }

            shotgunShells--;
            return true;
        }

        private void KillInternal(ParticipantState attacker, ParticipantDamageCause cause, string weapon, float healthBefore)
        {
            var previous = lifecycle;
            health = 0f;
            deathRemaining = Mathf.Max(deathWait, 0f);
            immunityRemaining = 0f;
            lifecycle = ParticipantLifecycle.Dead;
            ClearShotgunState();
            ApplyLeafSimulation();
            presentation?.SetImmune(false);
            presentation?.SetAlive(false);
            CollisionStateChanged?.Invoke(this);
            var deathCause = attacker != null ? ToDeathCause(cause) : ParticipantDeathCause.Arena;
            if (cause == ParticipantDamageCause.Arena && attacker == null)
            {
                deathCause = ParticipantDeathCause.Arena;
            }
            else if (cause == ParticipantDamageCause.Other && attacker == null)
            {
                deathCause = ParticipantDeathCause.Self;
            }
            Died?.Invoke(new ParticipantDeathEvent(this, attacker, deathCause, cause, weapon, healthBefore));
            if (previous != lifecycle)
            {
                LifecycleChanged?.Invoke(new ParticipantLifecycleEvent(this, previous, lifecycle));
            }
            PublishReadModel();
        }

        private void SetLifecycle(ParticipantLifecycle next)
        {
            if (lifecycle == next)
            {
                return;
            }

            var previous = lifecycle;
            lifecycle = next;
            ApplyLeafSimulation();
            LifecycleChanged?.Invoke(new ParticipantLifecycleEvent(this, previous, next));
            CollisionStateChanged?.Invoke(this);
        }

        private void ApplyLeafSimulation()
        {
            var active = matchSimulationEnabled && IsAlive;
            if (characterController != null)
            {
                characterController.enabled = IsAlive;
            }
            motor?.SetSimulationEnabled(active);
            launcher?.SetSimulationEnabled(active);
            kick?.SetSimulationEnabled(active);
            shotgun?.SetSimulationEnabled(active);
            botController?.SetSimulationEnabled(active && !localParticipant);
            if (input != null)
            {
                input.enabled = localParticipant && IsAlive;
                input.SetGameplayInputEnabled(active && localParticipant);
            }
            if (look != null)
            {
                look.enabled = active && localParticipant;
            }
            presentation?.SetLocalMode(localParticipant);
            if (cameraFeedback != null)
            {
                cameraFeedback.enabled = localParticipant;
            }
            presentation?.SetAlive(IsAlive);
        }

        private void SubscribeLeafEvents()
        {
            if (eventsSubscribed)
            {
                return;
            }

            if (kick != null)
            {
                kick.DashStarted += OnDashStarted;
            }
            if (launcher != null)
            {
                launcher.RocketLaunched += OnRocketLaunched;
            }
            eventsSubscribed = true;
        }

        private void UnsubscribeLeafEvents()
        {
            if (!eventsSubscribed)
            {
                return;
            }

            if (kick != null)
            {
                kick.DashStarted -= OnDashStarted;
            }
            if (launcher != null)
            {
                launcher.RocketLaunched -= OnRocketLaunched;
            }
            eventsSubscribed = false;
        }

        private void OnDashStarted() => CancelImmunity();
        private void OnRocketLaunched() => CancelImmunity();

        private void CacheReferences()
        {
            if (motor == null)
            {
                motor = GetComponent<PlayerMotor>();
            }
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }
            if (input == null)
            {
                input = GetComponent<PlayerInputReader>();
            }
            if (look == null)
            {
                look = GetComponent<PlayerLook>();
            }
            if (kick == null)
            {
                kick = GetComponent<BallKick>();
            }
            if (launcher == null)
            {
                launcher = GetComponent<RocketLauncher>();
            }
            if (shotgun == null)
            {
                shotgun = GetComponent<ShotgunWeapon>();
            }
            if (botController == null)
            {
                botController = GetComponent<BotController>();
            }
            if (presentation == null)
            {
                presentation = GetComponent<PlayerPresentation>();
            }
            if (cameraFeedback == null)
            {
                cameraFeedback = GetComponent<PlayerCameraFeedback>();
            }
        }

        private bool ValidateComposition()
        {
            if (motor == null || characterController == null || kick == null || launcher == null || shotgun == null ||
                presentation == null || (!localParticipant && botController == null) ||
                (localParticipant && (input == null || look == null || cameraFeedback == null)))
            {
                Debug.LogError("ParticipantState requires serialized references: shared motor, characterController, kick, launcher, shotgun, presentation, non-local botController, and local input/look/cameraFeedback.", this);
                enabled = false;
                return false;
            }

            return true;
        }

        private void BeginLifecycleReset()
        {
            botController?.SetSimulationEnabled(false);
            SetMatchPaused(false);
        }

        private ParticipantLifecycle PrepareKickoffReset()
        {
            BeginLifecycleReset();
            var previous = lifecycle;
            health = maxHealth;
            deathRemaining = 0f;
            immunityRemaining = 0f;
            lifecycle = ParticipantLifecycle.Alive;
            ClearShotgunState();
            motor?.ClearQueuedState();
            input?.ResetInputState();
            kick?.ResetState();
            launcher?.ResetState();
            look?.ResetView();
            cameraFeedback?.ResetFeedback();
            return previous;
        }

        private void FinishKickoffReset(ParticipantLifecycle previous)
        {
            presentation?.SetImmune(false);
            presentation?.SetAlive(true);
            ApplyLeafSimulation();
            if (previous != lifecycle)
            {
                LifecycleChanged?.Invoke(new ParticipantLifecycleEvent(this, previous, lifecycle));
                CollisionStateChanged?.Invoke(this);
            }
            PublishReadModel();
        }

        private void PublishReadModel() => ReadModelChanged?.Invoke(ReadModel);

        private static ParticipantDeathCause ToDeathCause(ParticipantDamageCause cause)
        {
            switch (cause)
            {
                case ParticipantDamageCause.Rocket:
                    return ParticipantDeathCause.Rocket;
                case ParticipantDamageCause.DashKick:
                    return ParticipantDeathCause.DashKick;
                case ParticipantDamageCause.Shotgun:
                    return ParticipantDeathCause.Shotgun;
                default:
                    return ParticipantDeathCause.Unknown;
            }
        }

        private void ClearShotgunState()
        {
            hasShotgun = false;
            shotgunShells = 0;
            shotgun?.ResetState();
            presentation?.SetShotgunOwned(false);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
