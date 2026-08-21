using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    /// <summary>Corner-state policy selected by the pure corner rules.</summary>
    public enum BotCornerIntent
    {
        Standard = 0,
        Recovery = 1,
        Combat = 2
    }

    /// <summary>Immutable hysteresis state for one team's corner sequence.</summary>
    public readonly struct BotCornerState
    {
        public BotCornerState(
            bool active,
            float dwellSeconds,
            Vector3 corner,
            Vector3 releaseDirection)
        {
            Active = active;
            DwellSeconds = dwellSeconds;
            Corner = corner;
            ReleaseDirection = releaseDirection;
        }

        public bool Active { get; }
        public float DwellSeconds { get; }
        public Vector3 Corner { get; }
        public Vector3 ReleaseDirection { get; }

        public bool IsActive => Active;
        public bool InCorner => Active;
        public float Dwell => DwellSeconds;
        public Vector3 Release => ReleaseDirection;

        public static BotCornerState Inactive => new BotCornerState(
            false,
            0f,
            Vector3.zero,
            Vector3.zero);

        public static BotCornerState Empty => Inactive;
    }

    /// <summary>One observer's ball sample, tagged so team samples have deterministic ties.</summary>
    public readonly struct BotCornerBallObservation
    {
        public BotCornerBallObservation(int observerSlotId, BotBallObservation observation)
        {
            ObserverSlotId = observerSlotId;
            Observation = observation;
        }

        public int ObserverSlotId { get; }
        public int SlotId => ObserverSlotId;
        public BotBallObservation Observation { get; }
        public BotBallObservation Ball => Observation;
        public BotBallObservation BallObservation => Observation;
    }

    /// <summary>One candidate bot supplied to the corner assignment rules.</summary>
    public readonly struct BotCornerParticipant
    {
        public BotCornerParticipant(
            int slotId,
            bool isLocalParticipant,
            bool isAlive,
            Vector3 position)
        {
            SlotId = slotId;
            IsLocalParticipant = isLocalParticipant;
            IsAlive = isAlive;
            Position = position;
        }

        public BotCornerParticipant(BotParticipantObservation observation)
            : this(
                observation.SlotId,
                observation.IsLocalParticipant,
                observation.IsAlive,
                observation.Position)
        {
        }

        public int SlotId { get; }
        public bool IsLocalParticipant { get; }
        public bool IsAlive { get; }
        public Vector3 Position { get; }

        public static BotCornerParticipant FromObservation(BotParticipantObservation observation)
        {
            return new BotCornerParticipant(observation);
        }

        public static implicit operator BotCornerParticipant(BotParticipantObservation observation)
        {
            return new BotCornerParticipant(observation);
        }
    }

    /// <summary>One deterministic navigation/action decision for a living non-local bot.</summary>
    public readonly struct BotCornerAssignment
    {
        public BotCornerAssignment(
            bool isAssigned,
            int slotId,
            BotCornerIntent intent,
            Vector3 navigationPoint,
            Vector3 recoveryPoint,
            Vector3 actionPoint,
            bool isActionReady,
            bool allowsBallActions,
            bool allowsParticipantActions)
        {
            IsAssigned = isAssigned;
            SlotId = slotId;
            Intent = intent;
            NavigationPoint = navigationPoint;
            RecoveryPoint = recoveryPoint;
            ActionPoint = actionPoint;
            IsActionReady = isActionReady;
            AllowsBallActions = allowsBallActions;
            AllowsParticipantActions = allowsParticipantActions;
        }

        public BotCornerAssignment(
            bool isAssigned,
            int slotId,
            BotCornerIntent intent,
            Vector3 navigationPoint,
            bool isActionReady,
            bool allowsBallActions,
            bool allowsParticipantActions)
            : this(
                isAssigned,
                slotId,
                intent,
                navigationPoint,
                navigationPoint,
                navigationPoint,
                isActionReady,
                allowsBallActions,
                allowsParticipantActions)
        {
        }

        public bool IsAssigned { get; }
        public int SlotId { get; }
        public BotCornerIntent Intent { get; }
        public Vector3 NavigationPoint { get; }
        public Vector3 RecoveryPoint { get; }
        public Vector3 ActionPoint { get; }
        public Vector3 NavigationTarget => NavigationPoint;
        public Vector3 NavigationPosition => NavigationPoint;
        public bool IsActionReady { get; }
        public bool ActionReady => IsActionReady;
        public bool Ready => IsActionReady;
        public bool AllowsBallActions { get; }
        public bool BallActionsAllowed => AllowsBallActions;
        public bool CanUseBallActions => AllowsBallActions;
        public bool AllowBallActions => AllowsBallActions;
        public bool BallActionsEnabled => AllowsBallActions;
        public bool AllowsParticipantActions { get; }
        public bool ParticipantActionsAllowed => AllowsParticipantActions;
        public bool CanUseParticipantActions => AllowsParticipantActions;
        public bool AllowParticipantCombat => AllowsParticipantActions;
        public bool ParticipantCombatEnabled => AllowsParticipantActions;
        public bool SuppressParticipantCombat => !AllowsParticipantActions;
        public bool IsRecoveryBot => Intent == BotCornerIntent.Recovery;
        public bool CanAct => AllowsBallActions || AllowsParticipantActions;
        public Vector3 CombatAnchor => NavigationPoint;
    }

    /// <summary>Bounded, slot-sorted set of up to three corner assignments.</summary>
    public readonly struct BotCornerAssignmentSet
    {
        public BotCornerAssignmentSet(
            BotCornerAssignment first,
            BotCornerAssignment second,
            BotCornerAssignment third)
        {
            BotCornerAssignment sortedFirst;
            BotCornerAssignment sortedSecond;
            BotCornerAssignment sortedThird;
            SortAssigned(first, second, third, out sortedFirst, out sortedSecond, out sortedThird);
            _first = sortedFirst;
            _second = sortedSecond;
            _third = sortedThird;
        }

        public BotCornerAssignment First => _first;
        public BotCornerAssignment Second => _second;
        public BotCornerAssignment Third => _third;

        public int Count
        {
            get
            {
                var count = 0;
                if (_first.IsAssigned)
                    count++;
                if (_second.IsAssigned)
                    count++;
                if (_third.IsAssigned)
                    count++;
                return count;
            }
        }

        public bool TryGetAssignment(int slotId, out BotCornerAssignment assignment)
        {
            if (_first.IsAssigned && _first.SlotId == slotId)
            {
                assignment = _first;
                return true;
            }
            if (_second.IsAssigned && _second.SlotId == slotId)
            {
                assignment = _second;
                return true;
            }
            if (_third.IsAssigned && _third.SlotId == slotId)
            {
                assignment = _third;
                return true;
            }

            assignment = default(BotCornerAssignment);
            return false;
        }

        public bool TryGet(int slotId, out BotCornerAssignment assignment)
        {
            return TryGetAssignment(slotId, out assignment);
        }

        public static BotCornerAssignmentSet Empty => new BotCornerAssignmentSet(
            Unassigned,
            Unassigned,
            Unassigned);

        private static readonly BotCornerAssignment Unassigned = new BotCornerAssignment(
            false,
            -1,
            BotCornerIntent.Standard,
            Vector3.zero,
            Vector3.zero,
            Vector3.zero,
            false,
            false,
            false);

        private readonly BotCornerAssignment _first;
        private readonly BotCornerAssignment _second;
        private readonly BotCornerAssignment _third;

        private static void SortAssigned(
            BotCornerAssignment first,
            BotCornerAssignment second,
            BotCornerAssignment third,
            out BotCornerAssignment sortedFirst,
            out BotCornerAssignment sortedSecond,
            out BotCornerAssignment sortedThird)
        {
            sortedFirst = Unassigned;
            sortedSecond = Unassigned;
            sortedThird = Unassigned;

            AddSorted(first, ref sortedFirst, ref sortedSecond, ref sortedThird);
            AddSorted(second, ref sortedFirst, ref sortedSecond, ref sortedThird);
            AddSorted(third, ref sortedFirst, ref sortedSecond, ref sortedThird);
        }

        private static void AddSorted(
            BotCornerAssignment assignment,
            ref BotCornerAssignment first,
            ref BotCornerAssignment second,
            ref BotCornerAssignment third)
        {
            if (!assignment.IsAssigned)
                return;

            if (!first.IsAssigned || assignment.SlotId < first.SlotId)
            {
                third = second;
                second = first;
                first = assignment;
            }
            else if (!second.IsAssigned || assignment.SlotId < second.SlotId)
            {
                third = second;
                second = assignment;
            }
            else if (!third.IsAssigned || assignment.SlotId < third.SlotId)
            {
                third = assignment;
            }
        }
    }
}
