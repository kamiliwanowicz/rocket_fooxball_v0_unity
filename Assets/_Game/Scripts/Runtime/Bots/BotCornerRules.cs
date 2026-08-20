using System;
using System.Collections.Generic;
using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    /// <summary>Deterministic corner hysteresis, geometry, and three-slot assignment policy.</summary>
    public static class BotCornerRules
    {
        public const float EnterDwellSeconds = 0.75f;
        public const float EnterEndDistance = 12f;
        public const float EnterSideDistance = 10f;
        public const float EnterBallHeight = 8f;
        public const float ExitEndDistance = 18f;
        public const float ExitSideDistance = 16f;
        public const float ExitBallHeight = 12f;
        public const float ObservationMemorySeconds = 1.5f;
        public const float RecoveryRingDistance = 6f;
        public const float ActionOffsetDistance = 12f;
        public const float CombatOffsetDistance = 18f;
        public const float CombatLaneOffset = 6f;
        public const float ArenaInset = 2.5f;
        public const float RecoveryReadyDistance = 2.5f;

        /// <summary>
        /// Advances corner hysteresis from up to three observer samples. A sample is valid for
        /// the same 1.5-second memory window used by BotPerception.
        /// </summary>
        public static BotCornerState Advance(
            BotCornerState previous,
            BotArenaBounds bounds,
            float deltaSeconds,
            bool reset,
            params BotCornerBallObservation[] observations)
        {
            return AdvanceCore(previous, bounds, observations, deltaSeconds, reset);
        }

        public static BotCornerState Advance(
            BotCornerState previous,
            BotArenaBounds bounds,
            float deltaSeconds,
            params BotCornerBallObservation[] observations)
        {
            return AdvanceCore(previous, bounds, observations, deltaSeconds, false);
        }

        public static BotCornerState Advance(
            BotCornerState previous,
            BotArenaBounds bounds,
            BotCornerBallObservation[] observations,
            float deltaSeconds,
            bool reset = false)
        {
            return AdvanceCore(previous, bounds, observations, deltaSeconds, reset);
        }

        public static BotCornerState Reset(BotCornerState previous)
        {
            return BotCornerState.Inactive;
        }

        public static bool TrySelectFreshestBall(
            BotCornerBallObservation first,
            BotCornerBallObservation second,
            BotCornerBallObservation third,
            out BotCornerBallObservation selected)
        {
            var observations = new[] { first, second, third };
            return TrySelectFreshestBall(observations, out selected);
        }

        public static bool TrySelectFreshestBall(
            IList<BotCornerBallObservation> observations,
            out BotCornerBallObservation selected)
        {
            selected = default(BotCornerBallObservation);
            if (observations == null)
            {
                return false;
            }

            var found = false;
            var bestAge = float.PositiveInfinity;
            var bestSlot = int.MaxValue;
            for (var index = 0; index < observations.Count; index++)
            {
                var candidate = observations[index];
                if (!IsValidObservation(candidate))
                {
                    continue;
                }

                var age = candidate.Observation.AgeSeconds;
                if (!found || age < bestAge ||
                    (age == bestAge && candidate.ObserverSlotId < bestSlot))
                {
                    found = true;
                    bestAge = age;
                    bestSlot = candidate.ObserverSlotId;
                    selected = candidate;
                }
            }

            return found;
        }

        /// <summary>Builds up to three assignments for an active corner state.</summary>
        public static BotCornerAssignmentSet Assign(
            BotCornerState state,
            BotArenaBounds bounds,
            Vector3 ballPosition,
            Vector3 enemyGoalPosition,
            params BotCornerParticipant[] participants)
        {
            if (!state.Active || bounds == null || !bounds.IsValid ||
                !IsFinite(ballPosition) || !IsFinite(enemyGoalPosition))
            {
                return BotCornerAssignmentSet.Empty;
            }

            var candidates = CollectCandidates(participants);
            if (candidates.Count == 0)
            {
                return BotCornerAssignmentSet.Empty;
            }

            var ball = GroundPoint(ballPosition);
            var recoveryPoint = GetRecoveryPoint(bounds, ball, enemyGoalPosition);
            var actionPoint = GetActionPoint(bounds, ball, state.ReleaseDirection);
            var recoveryIndex = FindRecoveryCandidate(candidates, recoveryPoint);
            var assignments = new BotCornerAssignment[candidates.Count];

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (index == recoveryIndex)
                {
                    var ready = DistanceXZ(candidate.Position, recoveryPoint) <= RecoveryReadyDistance;
                    assignments[index] = new BotCornerAssignment(
                        true,
                        candidate.SlotId,
                        BotCornerIntent.Recovery,
                        ready ? actionPoint : recoveryPoint,
                        recoveryPoint,
                        actionPoint,
                        ready,
                        ready,
                        false);
                    continue;
                }

                var fighterIndex = index < recoveryIndex ? index : index - 1;
                var fighterCount = candidates.Count - 1;
                var anchor = GetCombatAnchor(
                    bounds,
                    ball,
                    state.ReleaseDirection,
                    fighterIndex,
                    fighterCount);
                assignments[index] = new BotCornerAssignment(
                    true,
                    candidate.SlotId,
                    BotCornerIntent.Combat,
                    anchor,
                    recoveryPoint,
                    actionPoint,
                    true,
                    false,
                    true);
            }

            var first = assignments.Length > 0 ? assignments[0] : default(BotCornerAssignment);
            var second = assignments.Length > 1 ? assignments[1] : default(BotCornerAssignment);
            var third = assignments.Length > 2 ? assignments[2] : default(BotCornerAssignment);
            return new BotCornerAssignmentSet(first, second, third);
        }

        public static BotCornerAssignmentSet Assign(
            BotCornerState state,
            BotArenaBounds bounds,
            BotBallObservation ball,
            Vector3 enemyGoalPosition,
            params BotCornerParticipant[] participants)
        {
            if (!ball.HasObservation || !IsFinite(ball.Position) || !IsFinite(ball.AgeSeconds) ||
                ball.AgeSeconds < 0f || ball.AgeSeconds > ObservationMemorySeconds)
            {
                return BotCornerAssignmentSet.Empty;
            }

            return Assign(state, bounds, ball.Position, enemyGoalPosition, participants);
        }

        public static BotCornerAssignmentSet Assign(
            BotCornerState state,
            BotArenaBounds bounds,
            BotCornerBallObservation ball,
            Vector3 enemyGoalPosition,
            params BotCornerParticipant[] participants)
        {
            return Assign(state, bounds, ball.Observation, enemyGoalPosition, participants);
        }

        public static BotCornerAssignmentSet Assign(
            BotCornerState state,
            BotArenaBounds bounds,
            Vector3 ballPosition,
            Vector3 enemyGoalPosition,
            BotParticipantObservation first,
            BotParticipantObservation second,
            BotParticipantObservation third)
        {
            return Assign(
                state,
                bounds,
                ballPosition,
                enemyGoalPosition,
                new BotCornerParticipant(first),
                new BotCornerParticipant(second),
                new BotCornerParticipant(third));
        }

        public static Vector3 GetCorner(BotArenaBounds bounds, Vector3 ballPosition)
        {
            if (bounds == null || !bounds.IsValid || !IsFinite(ballPosition))
            {
                return Vector3.zero;
            }

            var center = bounds.Center;
            return new Vector3(
                center.x + Sign(ballPosition.x - center.x) * bounds.HalfLength,
                0f,
                center.z + Sign(ballPosition.z - center.z) * bounds.HalfWidth);
        }

        public static Vector3 GetReleaseDirection(BotArenaBounds bounds, Vector3 corner)
        {
            if (bounds == null || !bounds.IsValid || !IsFinite(corner))
            {
                return Vector3.zero;
            }

            var direction = bounds.Center - corner;
            direction.y = 0f;
            return NormalizeOrZero(direction);
        }

        public static Vector3 GetRecoveryPoint(
            BotArenaBounds bounds,
            Vector3 ballPosition,
            Vector3 enemyGoalPosition)
        {
            if (bounds == null || !bounds.IsValid || !IsFinite(ballPosition) || !IsFinite(enemyGoalPosition))
            {
                return Vector3.zero;
            }

            var ball = GroundPoint(ballPosition);
            var goalDirection = enemyGoalPosition - ballPosition;
            goalDirection.y = 0f;
            goalDirection = NormalizeOrZero(goalDirection);
            return ClampInsideBounds(bounds, ball - goalDirection * RecoveryRingDistance);
        }

        public static Vector3 GetActionPoint(
            BotArenaBounds bounds,
            Vector3 ballPosition,
            Vector3 releaseDirection)
        {
            if (bounds == null || !bounds.IsValid || !IsFinite(ballPosition) || !IsFinite(releaseDirection))
            {
                return Vector3.zero;
            }

            var release = releaseDirection;
            release.y = 0f;
            release = NormalizeOrZero(release);
            return ClampInsideBounds(bounds, GroundPoint(ballPosition) + release * ActionOffsetDistance);
        }

        public static Vector3 GetCombatAnchor(
            BotArenaBounds bounds,
            Vector3 ballPosition,
            Vector3 releaseDirection,
            int fighterIndex,
            int fighterCount)
        {
            if (bounds == null || !bounds.IsValid || !IsFinite(ballPosition) ||
                !IsFinite(releaseDirection) || fighterIndex < 0 || fighterIndex >= fighterCount || fighterCount <= 0)
            {
                return Vector3.zero;
            }

            var release = releaseDirection;
            release.y = 0f;
            release = NormalizeOrZero(release);
            var perpendicular = Vector3.Cross(Vector3.up, release);
            if (perpendicular.sqrMagnitude <= 0.000001f)
            {
                perpendicular = Vector3.forward;
            }
            else
            {
                perpendicular.Normalize();
            }

            var lane = CombatLaneOffsetFor(fighterIndex, fighterCount);
            var basePoint = GroundPoint(ballPosition) + release * CombatOffsetDistance;
            return ClampInsideBounds(bounds, basePoint + perpendicular * lane);
        }

        private static BotCornerState AdvanceCore(
            BotCornerState previous,
            BotArenaBounds bounds,
            IList<BotCornerBallObservation> observations,
            float deltaSeconds,
            bool reset)
        {
            if (reset || bounds == null || !bounds.IsValid ||
                !TrySelectFreshestBall(observations, out var selected))
            {
                return BotCornerState.Inactive;
            }

            var ball = selected.Observation.Position;
            var center = bounds.Center;
            var endDistance = bounds.HalfLength - Mathf.Abs(ball.x - center.x);
            var sideDistance = bounds.HalfWidth - Mathf.Abs(ball.z - center.z);
            if (!IsFinite(endDistance) || !IsFinite(sideDistance) ||
                ball.y > ExitBallHeight || endDistance > ExitEndDistance || sideDistance > ExitSideDistance)
            {
                return BotCornerState.Inactive;
            }

            var corner = GetCorner(bounds, ball);
            var release = GetReleaseDirection(bounds, corner);
            if (previous.Active)
            {
                return new BotCornerState(true, EnterDwellSeconds, corner, release);
            }

            if (ball.y > EnterBallHeight || endDistance > EnterEndDistance || sideDistance > EnterSideDistance)
            {
                return BotCornerState.Inactive;
            }

            var priorDwell = IsFinite(previous.DwellSeconds) && previous.DwellSeconds > 0f
                ? previous.DwellSeconds
                : 0f;
            var delta = IsFinite(deltaSeconds) && deltaSeconds > 0f ? deltaSeconds : 0f;
            var dwell = Mathf.Min(priorDwell + delta, EnterDwellSeconds);
            if (dwell + 0.000001f >= EnterDwellSeconds)
            {
                return new BotCornerState(true, EnterDwellSeconds, corner, release);
            }

            return new BotCornerState(false, dwell, corner, release);
        }

        private static List<BotCornerParticipant> CollectCandidates(BotCornerParticipant[] participants)
        {
            var result = new List<BotCornerParticipant>(3);
            if (participants == null)
            {
                return result;
            }

            for (var index = 0; index < participants.Length; index++)
            {
                var candidate = participants[index];
                if (!candidate.IsAlive || candidate.IsLocalParticipant || !IsFinite(candidate.Position))
                {
                    continue;
                }

                var duplicate = false;
                for (var previous = 0; previous < result.Count; previous++)
                {
                    if (result[previous].SlotId == candidate.SlotId)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    result.Add(candidate);
                }
            }

            result.Sort(CompareSlot);
            if (result.Count > 3)
            {
                result.RemoveRange(3, result.Count - 3);
            }
            return result;
        }

        private static int FindRecoveryCandidate(List<BotCornerParticipant> candidates, Vector3 recoveryPoint)
        {
            var bestIndex = -1;
            var bestDistance = float.PositiveInfinity;
            for (var index = 0; index < candidates.Count; index++)
            {
                var distance = DistanceXZSquared(candidates[index].Position, recoveryPoint);
                if (!IsFinite(distance))
                {
                    continue;
                }

                if (bestIndex < 0 || distance < bestDistance ||
                    (distance == bestDistance && candidates[index].SlotId < candidates[bestIndex].SlotId))
                {
                    bestIndex = index;
                    bestDistance = distance;
                }
            }
            return bestIndex;
        }

        private static float CombatLaneOffsetFor(int fighterIndex, int fighterCount)
        {
            if (fighterCount <= 1 || fighterIndex == 0 && fighterCount >= 3)
            {
                return 0f;
            }

            if (fighterCount == 2)
            {
                return fighterIndex == 0 ? CombatLaneOffset : -CombatLaneOffset;
            }

            return fighterIndex == 1 ? CombatLaneOffset : -CombatLaneOffset;
        }

        private static Vector3 ClampInsideBounds(BotArenaBounds bounds, Vector3 point)
        {
            var center = bounds.Center;
            var minX = center.x - Mathf.Max(bounds.HalfLength - ArenaInset, 0f);
            var maxX = center.x + Mathf.Max(bounds.HalfLength - ArenaInset, 0f);
            var minZ = center.z - Mathf.Max(bounds.HalfWidth - ArenaInset, 0f);
            var maxZ = center.z + Mathf.Max(bounds.HalfWidth - ArenaInset, 0f);
            return new Vector3(
                Mathf.Clamp(point.x, minX, maxX),
                0f,
                Mathf.Clamp(point.z, minZ, maxZ));
        }

        private static bool IsValidObservation(BotCornerBallObservation candidate)
        {
            var observation = candidate.Observation;
            return observation.HasObservation && IsFinite(observation.Position) &&
                IsFinite(observation.AgeSeconds) && observation.AgeSeconds >= 0f &&
                observation.AgeSeconds <= ObservationMemorySeconds;
        }

        private static Vector3 GroundPoint(Vector3 point)
        {
            point.y = 0f;
            return point;
        }

        private static float DistanceXZ(Vector3 first, Vector3 second)
        {
            var distance = DistanceXZSquared(first, second);
            return IsFinite(distance) ? Mathf.Sqrt(distance) : float.PositiveInfinity;
        }

        private static float DistanceXZSquared(Vector3 first, Vector3 second)
        {
            var x = first.x - second.x;
            var z = first.z - second.z;
            return x * x + z * z;
        }

        private static Vector3 NormalizeOrZero(Vector3 value)
        {
            var magnitude = value.magnitude;
            return IsFinite(magnitude) && magnitude > 0.000001f ? value / magnitude : Vector3.zero;
        }

        private static Vector3 Sign(Vector3 value)
        {
            return new Vector3(Sign(value.x), Sign(value.y), Sign(value.z));
        }

        private static float Sign(float value)
        {
            if (value > 0f)
            {
                return 1f;
            }
            if (value < 0f)
            {
                return -1f;
            }
            return 0f;
        }

        private static int CompareSlot(BotCornerParticipant first, BotCornerParticipant second)
        {
            return first.SlotId.CompareTo(second.SlotId);
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
