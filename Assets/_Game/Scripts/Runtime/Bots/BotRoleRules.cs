using System;
using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    public readonly struct BotRoleContext
    {
        public BotRoleContext(
            bool hasBallObservation,
            Vector3 ballPosition,
            Vector3 ownGoalPosition,
            Vector3 enemyGoalPosition,
            bool aliveSetChanged)
        {
            HasBallObservation = hasBallObservation;
            BallPosition = ballPosition;
            OwnGoalPosition = ownGoalPosition;
            EnemyGoalPosition = enemyGoalPosition;
            AliveSetChanged = aliveSetChanged;
        }

        public bool HasBallObservation { get; }
        public Vector3 BallPosition { get; }
        public Vector3 OwnGoalPosition { get; }
        public Vector3 EnemyGoalPosition { get; }
        public bool AliveSetChanged { get; }
    }

    public readonly struct BotRoleCandidate
    {
        public BotRoleCandidate(
            int slotId,
            bool isLocalParticipant,
            bool isAlive,
            Vector3 position,
            bool hasCurrentRole,
            BotRole currentRole,
            float heldSeconds)
        {
            SlotId = slotId;
            IsLocalParticipant = isLocalParticipant;
            IsAlive = isAlive;
            Position = position;
            HasCurrentRole = hasCurrentRole;
            CurrentRole = currentRole;
            HeldSeconds = heldSeconds;
        }

        public int SlotId { get; }
        public bool IsLocalParticipant { get; }
        public bool IsAlive { get; }
        public Vector3 Position { get; }
        public bool HasCurrentRole { get; }
        public BotRole CurrentRole { get; }
        public float HeldSeconds { get; }
    }

    public readonly struct BotRoleAssignment
    {
        public BotRoleAssignment(bool isAssigned, int slotId, BotRole role, float score)
        {
            IsAssigned = isAssigned;
            SlotId = slotId;
            Role = role;
            Score = score;
        }

        public bool IsAssigned { get; }
        public int SlotId { get; }
        public BotRole Role { get; }
        public float Score { get; }
    }

    public readonly struct BotRoleAssignmentSet
    {
        public BotRoleAssignmentSet(
            BotRoleAssignment first,
            BotRoleAssignment second,
            BotRoleAssignment third)
        {
            BotRoleAssignment sortedFirst;
            BotRoleAssignment sortedSecond;
            BotRoleAssignment sortedThird;
            SortAssigned(first, second, third, out sortedFirst, out sortedSecond, out sortedThird);
            _first = sortedFirst;
            _second = sortedSecond;
            _third = sortedThird;
        }

        public BotRoleAssignment First => _first;
        public BotRoleAssignment Second => _second;
        public BotRoleAssignment Third => _third;
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

        public bool TryGetAssignment(int slotId, out BotRoleAssignment assignment)
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

            assignment = default(BotRoleAssignment);
            return false;
        }

        public static BotRoleAssignmentSet Empty => new BotRoleAssignmentSet(
            Unassigned,
            Unassigned,
            Unassigned);

        private static readonly BotRoleAssignment Unassigned = new BotRoleAssignment(
            false,
            -1,
            BotRole.Attacker,
            0f);

        private readonly BotRoleAssignment _first;
        private readonly BotRoleAssignment _second;
        private readonly BotRoleAssignment _third;

        private static void SortAssigned(
            BotRoleAssignment first,
            BotRoleAssignment second,
            BotRoleAssignment third,
            out BotRoleAssignment sortedFirst,
            out BotRoleAssignment sortedSecond,
            out BotRoleAssignment sortedThird)
        {
            sortedFirst = Unassigned;
            sortedSecond = Unassigned;
            sortedThird = Unassigned;

            AddSorted(first, ref sortedFirst, ref sortedSecond, ref sortedThird);
            AddSorted(second, ref sortedFirst, ref sortedSecond, ref sortedThird);
            AddSorted(third, ref sortedFirst, ref sortedSecond, ref sortedThird);
        }

        private static void AddSorted(
            BotRoleAssignment assignment,
            ref BotRoleAssignment first,
            ref BotRoleAssignment second,
            ref BotRoleAssignment third)
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

    public static class BotRoleRules
    {
        public const float RoleDistance = 75f;
        public const float RoleHoldSeconds = 2f;
        public const float SwitchMargin = 0.15f;

        public static BotRoleAssignmentSet Assign(
            BotRoleContext context,
            BotRoleCandidate first,
            BotRoleCandidate second,
            BotRoleCandidate third)
        {
            if (!IsFinite(context.OwnGoalPosition) || !IsFinite(context.EnemyGoalPosition))
                return BotRoleAssignmentSet.Empty;

            var effectiveBall = context.HasBallObservation && IsFinite(context.BallPosition)
                ? context.BallPosition
                : (context.OwnGoalPosition + context.EnemyGoalPosition) * 0.5f;
            if (!IsFinite(effectiveBall))
                return BotRoleAssignmentSet.Empty;

            var candidates = new BotRoleCandidate[3];
            var count = 0;
            AddCandidate(first, candidates, ref count);
            AddCandidate(second, candidates, ref count);
            AddCandidate(third, candidates, ref count);

            if (count == 0)
                return BotRoleAssignmentSet.Empty;

            SortCandidates(candidates, count);
            var best = FindBest(candidates, count, effectiveBall, context.OwnGoalPosition);
            if (!best.IsValid)
                return BotRoleAssignmentSet.Empty;

            if (!context.AliveSetChanged && TryBuildCurrent(candidates, count, effectiveBall, context.OwnGoalPosition, out var current))
            {
                if (HasUnfinishedHold(candidates, count))
                    return BuildSet(candidates, current);

                if (best.MeanScore < current.MeanScore + SwitchMargin)
                    return BuildSet(candidates, current);
            }

            return BuildSet(candidates, best);
        }

        private static void AddCandidate(BotRoleCandidate candidate, BotRoleCandidate[] candidates, ref int count)
        {
            if (candidate.IsLocalParticipant || !candidate.IsAlive || !IsFinite(candidate.Position))
                return;

            for (var index = 0; index < count; index++)
            {
                if (candidates[index].SlotId == candidate.SlotId)
                    return;
            }

            candidates[count++] = candidate;
        }

        private static AssignmentPlan FindBest(
            BotRoleCandidate[] candidates,
            int count,
            Vector3 effectiveBall,
            Vector3 ownGoal)
        {
            var best = AssignmentPlan.Invalid;
            if (count == 1)
            {
                best = Evaluate(candidates, count, new[] { BotRole.Defender }, effectiveBall, ownGoal);
                return best;
            }

            if (count == 2)
            {
                ConsiderPlan(ref best, Evaluate(candidates, count, new[] { BotRole.Attacker, BotRole.Defender }, effectiveBall, ownGoal));
                ConsiderPlan(ref best, Evaluate(candidates, count, new[] { BotRole.Defender, BotRole.Attacker }, effectiveBall, ownGoal));
                return best;
            }

            var roles = new[] { BotRole.Attacker, BotRole.Support, BotRole.Defender };
            for (var first = 0; first < roles.Length; first++)
            {
                for (var second = 0; second < roles.Length; second++)
                {
                    if (second == first)
                        continue;
                    for (var third = 0; third < roles.Length; third++)
                    {
                        if (third == first || third == second)
                            continue;
                        ConsiderPlan(ref best, Evaluate(
                            candidates,
                            count,
                            new[] { roles[first], roles[second], roles[third] },
                            effectiveBall,
                            ownGoal));
                    }
                }
            }
            return best;
        }

        private static AssignmentPlan Evaluate(
            BotRoleCandidate[] candidates,
            int count,
            BotRole[] roles,
            Vector3 effectiveBall,
            Vector3 ownGoal)
        {
            var scores = new float[3];
            var total = 0f;
            for (var index = 0; index < count; index++)
            {
                scores[index] = Fitness(candidates[index].Position, roles[index], effectiveBall, ownGoal);
                total += scores[index];
            }

            return new AssignmentPlan(true, roles, scores, total / count);
        }

        private static void ConsiderPlan(ref AssignmentPlan best, AssignmentPlan candidate)
        {
            if (!candidate.IsValid || !best.IsValid)
            {
                if (candidate.IsValid)
                    best = candidate;
                return;
            }

            if (candidate.MeanScore > best.MeanScore ||
                (candidate.MeanScore == best.MeanScore && IsLexicographicallyEarlier(candidate.Roles, best.Roles)))
            {
                best = candidate;
            }
        }

        private static bool TryBuildCurrent(
            BotRoleCandidate[] candidates,
            int count,
            Vector3 effectiveBall,
            Vector3 ownGoal,
            out AssignmentPlan current)
        {
            var roles = new BotRole[3];
            for (var index = 0; index < count; index++)
            {
                if (!candidates[index].HasCurrentRole || !IsValidRole(candidates[index].CurrentRole))
                {
                    current = AssignmentPlan.Invalid;
                    return false;
                }

                for (var previous = 0; previous < index; previous++)
                {
                    if (roles[previous] == candidates[index].CurrentRole)
                    {
                        current = AssignmentPlan.Invalid;
                        return false;
                    }
                }
                roles[index] = candidates[index].CurrentRole;
            }

            if (!IsLegalRoleSet(roles, count))
            {
                current = AssignmentPlan.Invalid;
                return false;
            }

            current = Evaluate(candidates, count, roles, effectiveBall, ownGoal);
            return true;
        }

        private static bool IsLegalRoleSet(BotRole[] roles, int count)
        {
            if (count == 1)
                return roles[0] == BotRole.Defender;
            if (count == 2)
                return (roles[0] == BotRole.Attacker && roles[1] == BotRole.Defender) ||
                    (roles[0] == BotRole.Defender && roles[1] == BotRole.Attacker);
            if (count == 3)
                return HasRole(roles, BotRole.Attacker) && HasRole(roles, BotRole.Support) && HasRole(roles, BotRole.Defender);
            return false;
        }

        private static bool HasRole(BotRole[] roles, BotRole role)
        {
            for (var index = 0; index < roles.Length; index++)
            {
                if (roles[index] == role)
                    return true;
            }
            return false;
        }

        private static bool HasUnfinishedHold(BotRoleCandidate[] candidates, int count)
        {
            for (var index = 0; index < count; index++)
            {
                if (candidates[index].HeldSeconds < RoleHoldSeconds)
                    return true;
            }
            return false;
        }

        private static BotRoleAssignmentSet BuildSet(BotRoleCandidate[] candidates, AssignmentPlan plan)
        {
            var first = new BotRoleAssignment(true, candidates[0].SlotId, plan.Roles[0], plan.Scores[0]);
            var second = plan.Roles.Length > 1
                ? new BotRoleAssignment(true, candidates[1].SlotId, plan.Roles[1], plan.Scores[1])
                : new BotRoleAssignment(false, -1, BotRole.Attacker, 0f);
            var third = plan.Roles.Length > 2
                ? new BotRoleAssignment(true, candidates[2].SlotId, plan.Roles[2], plan.Scores[2])
                : new BotRoleAssignment(false, -1, BotRole.Attacker, 0f);
            return new BotRoleAssignmentSet(first, second, third);
        }

        private static float Fitness(Vector3 position, BotRole role, Vector3 effectiveBall, Vector3 ownGoal)
        {
            var target = effectiveBall;
            if (role == BotRole.Defender)
                target = ownGoal;
            else if (role == BotRole.Support)
                target = (effectiveBall + ownGoal) * 0.5f;

            var distance = Vector3.Distance(position, target);
            if (!IsFinite(distance))
                return 0f;
            return 1f - Clamp01(distance / RoleDistance);
        }

        private static bool IsLexicographicallyEarlier(BotRole[] candidate, BotRole[] current)
        {
            var count = Math.Min(candidate.Length, current.Length);
            for (var index = 0; index < count; index++)
            {
                var candidateOrdinal = (int)candidate[index];
                var currentOrdinal = (int)current[index];
                if (candidateOrdinal < currentOrdinal)
                    return true;
                if (candidateOrdinal > currentOrdinal)
                    return false;
            }
            return candidate.Length < current.Length;
        }

        private static void SortCandidates(BotRoleCandidate[] candidates, int count)
        {
            for (var index = 1; index < count; index++)
            {
                var value = candidates[index];
                var previous = index - 1;
                while (previous >= 0 && value.SlotId < candidates[previous].SlotId)
                {
                    candidates[previous + 1] = candidates[previous];
                    previous--;
                }
                candidates[previous + 1] = value;
            }
        }

        private static bool IsValidRole(BotRole role)
        {
            return role == BotRole.Attacker || role == BotRole.Support || role == BotRole.Defender;
        }

        private static float Clamp01(float value)
        {
            if (value <= 0f)
                return 0f;
            if (value >= 1f)
                return 1f;
            return value;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct AssignmentPlan
        {
            public AssignmentPlan(bool isValid, BotRole[] roles, float[] scores, float meanScore)
            {
                IsValid = isValid;
                Roles = roles;
                Scores = scores;
                MeanScore = meanScore;
            }

            public bool IsValid { get; }
            public BotRole[] Roles { get; }
            public float[] Scores { get; }
            public float MeanScore { get; }

            public static AssignmentPlan Invalid => new AssignmentPlan(false, new BotRole[0], new float[0], 0f);
        }
    }
}
