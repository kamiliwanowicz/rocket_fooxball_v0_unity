using System;
using UnityEngine;

namespace RocketFooxball.Runtime.Participants
{
    public enum ParticipantTeam
    {
        Blue,
        Red
    }

    public enum ParticipantLifecycle
    {
        Alive,
        Dead,
        Respawning
    }

    public enum ParticipantDamageCause
    {
        Unknown,
        Rocket,
        DashKick,
        Arena,
        Ball,
        Other
    }

    public enum ParticipantDeathCause
    {
        Unknown,
        Rocket,
        DashKick,
        Arena,
        Self
    }

    /// <summary>Stable serialized identity used by roster consumers and score systems.</summary>
    [Serializable]
    public readonly struct ParticipantSlotContract
    {
        public ParticipantSlotContract(int slotId, string displayName, ParticipantTeam team, bool isLocal)
        {
            SlotId = slotId;
            DisplayName = displayName ?? string.Empty;
            Team = team;
            IsLocal = isLocal;
        }

        public int SlotId { get; }
        public int Id => SlotId;
        public string DisplayName { get; }
        public ParticipantTeam Team { get; }
        public bool IsLocal { get; }
    }

    /// <summary>Immutable participant snapshot. Read-only consumers cannot mutate runtime state.</summary>
    public readonly struct ParticipantReadModel
    {
        public ParticipantReadModel(
            int slotId,
            string displayName,
            ParticipantTeam team,
            bool isLocal,
            float health,
            float maxHealth,
            ParticipantLifecycle lifecycle,
            float respawnRemaining,
            float immunityRemaining)
        {
            SlotId = slotId;
            DisplayName = displayName ?? string.Empty;
            Team = team;
            IsLocal = isLocal;
            Health = health;
            MaxHealth = maxHealth;
            Lifecycle = lifecycle;
            RespawnRemaining = respawnRemaining;
            ImmunityRemaining = immunityRemaining;
        }

        public int SlotId { get; }
        public int Id => SlotId;
        public string DisplayName { get; }
        public ParticipantTeam Team { get; }
        public bool IsLocal { get; }
        public bool IsLocalParticipant => IsLocal;
        public float Health { get; }
        public float CurrentHealth => Health;
        public float MaxHealth { get; }
        public ParticipantLifecycle Lifecycle { get; }
        public ParticipantLifecycle State => Lifecycle;
        public float RespawnRemaining { get; }
        public float ImmunityRemaining { get; }
        public bool IsAlive => Lifecycle == ParticipantLifecycle.Alive;
        public bool IsDead => Lifecycle == ParticipantLifecycle.Dead;
        public bool IsRespawning => Lifecycle == ParticipantLifecycle.Respawning;
        public bool IsImmune => ImmunityRemaining > 0f && IsAlive;
    }

    public readonly struct ParticipantDamageRequest
    {
        public ParticipantDamageRequest(ParticipantState attacker, float amount, ParticipantDamageCause cause, string weapon)
        {
            Attacker = attacker;
            Amount = amount;
            Cause = cause;
            Weapon = weapon ?? string.Empty;
        }

        public ParticipantState Attacker { get; }
        public float Amount { get; }
        public ParticipantDamageCause Cause { get; }
        public string Weapon { get; }
    }

    public readonly struct ParticipantDeathEvent
    {
        public ParticipantDeathEvent(
            ParticipantState victim,
            ParticipantState killer,
            ParticipantDeathCause cause,
            ParticipantDamageCause damageCause,
            string weapon,
            float healthBefore)
        {
            Victim = victim;
            Killer = killer;
            Cause = cause;
            DamageCause = damageCause;
            Weapon = weapon ?? string.Empty;
            HealthBefore = healthBefore;
        }

        public ParticipantState Victim { get; }
        public ParticipantState Killer { get; }
        public ParticipantDeathCause Cause { get; }
        public ParticipantDamageCause DamageCause { get; }
        public string Weapon { get; }
        public float HealthBefore { get; }
        public bool HasKiller => Killer != null;
    }

    public readonly struct ParticipantLifecycleEvent
    {
        public ParticipantLifecycleEvent(ParticipantState participant, ParticipantLifecycle previous, ParticipantLifecycle current)
        {
            Participant = participant;
            Previous = previous;
            Current = current;
        }

        public ParticipantState Participant { get; }
        public ParticipantLifecycle Previous { get; }
        public ParticipantLifecycle Current { get; }
    }
}
