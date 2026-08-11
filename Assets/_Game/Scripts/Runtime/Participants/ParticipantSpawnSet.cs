using System.Collections.Generic;
using UnityEngine;
using RocketFooxball.Runtime.Ball;

namespace RocketFooxball.Runtime.Participants
{
    /// <summary>Authored Blue/Red spawn candidates and deterministic safety scoring.</summary>
    public sealed class ParticipantSpawnSet : MonoBehaviour
    {
        [Header("Candidates")]
        [SerializeField] private Transform[] blueCandidates = new Transform[3];
        [SerializeField] private Transform[] redCandidates = new Transform[3];
        [SerializeField] private Transform blueEnemyGoal;
        [SerializeField] private Transform redEnemyGoal;

        [Header("Visibility")]
        [SerializeField] private LayerMask visibilityMask = ~0;
        [SerializeField, Min(0f)] private float eyeHeight = 1.2f;
        [SerializeField, Min(0f)] private float occupiedRadius = 2f;

        [Header("Safety Weights")]
        [SerializeField, Min(0f)] private float ballDistanceWeight = 1f;
        [SerializeField, Min(0f)] private float enemyGoalDistanceWeight = 0.5f;
        [SerializeField, Min(0f)] private float nearestEnemyDistanceWeight = 1f;
        [SerializeField, Min(0f)] private float noVisibleEnemyBonus = 4f;
        [SerializeField, Min(0f)] private float visibleEnemyCountPenalty = 2f;
        [SerializeField, Min(0f)] private float occupiedFallbackPenalty = 8f;
        [SerializeField, Min(0f)] private float ballDistanceCap = 30f;
        [SerializeField, Min(0f)] private float enemyGoalDistanceCap = 30f;
        [SerializeField, Min(0f)] private float enemyDistanceCap = 30f;

        public IReadOnlyList<Transform> BlueCandidates => blueCandidates;
        public IReadOnlyList<Transform> RedCandidates => redCandidates;
        public Transform BlueEnemyGoal => blueEnemyGoal;
        public Transform RedEnemyGoal => redEnemyGoal;
        public LayerMask VisibilityMask => visibilityMask;
        public float OccupiedRadius => occupiedRadius;

        public IReadOnlyList<Transform> GetCandidates(ParticipantTeam participantTeam)
        {
            return participantTeam == ParticipantTeam.Blue ? blueCandidates : redCandidates;
        }

        public Transform GetEnemyGoal(ParticipantTeam participantTeam)
        {
            return participantTeam == ParticipantTeam.Blue ? blueEnemyGoal : redEnemyGoal;
        }

        public Transform SelectSafestSpawn(ParticipantState participant, BallMotor ball, IReadOnlyList<ParticipantState> roster)
        {
            if (participant == null)
            {
                return null;
            }

            var candidates = participant.Team == ParticipantTeam.Blue ? blueCandidates : redCandidates;
            if (candidates == null || candidates.Length == 0)
            {
                return null;
            }

            var available = new bool[candidates.Length];
            var hasAvailable = false;
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                available[i] = candidate != null && !IsOccupied(candidate.position, participant, roster);
                hasAvailable |= available[i];
            }

            Transform best = null;
            var bestScore = float.NegativeInfinity;
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (candidate == null || (hasAvailable && !available[i]))
                {
                    continue;
                }

                var score = ScoreCandidate(candidate, participant, ball, roster);
                if (!available[i])
                {
                    score -= occupiedFallbackPenalty;
                }

                // Strict comparison preserves authored order for ties.
                if (best == null || score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        public Transform SelectSpawn(ParticipantState participant, BallMotor ball, IReadOnlyList<ParticipantState> roster)
        {
            return SelectSafestSpawn(participant, ball, roster);
        }

        /// <summary>Returns authored slot-order spawn for kickoff/reset; ties never depend on physics.</summary>
        public Transform GetKickoffSpawn(ParticipantState participant, int rosterIndex)
        {
            if (participant == null)
            {
                return null;
            }

            var candidates = participant.Team == ParticipantTeam.Blue ? blueCandidates : redCandidates;
            if (candidates == null || candidates.Length == 0)
            {
                return null;
            }

            var teamIndex = rosterIndex % 3;
            if (teamIndex < 0)
            {
                teamIndex += 3;
            }
            return candidates[Mathf.Min(teamIndex, candidates.Length - 1)];
        }

        private float ScoreCandidate(Transform candidate, ParticipantState participant, BallMotor ball, IReadOnlyList<ParticipantState> roster)
        {
            var position = candidate.position;
            var score = 0f;
            if (ball != null)
            {
                score += Mathf.Min(Vector3.Distance(position, ball.transform.position), ballDistanceCap) * ballDistanceWeight;
            }

            var enemyGoal = participant.Team == ParticipantTeam.Blue ? blueEnemyGoal : redEnemyGoal;
            if (enemyGoal != null)
            {
                score += Mathf.Min(Vector3.Distance(position, enemyGoal.position), enemyGoalDistanceCap) * enemyGoalDistanceWeight;
            }

            var visibleCount = 0;
            var nearestVisible = enemyDistanceCap;
            if (roster != null)
            {
                for (var i = 0; i < roster.Count; i++)
                {
                    var enemy = roster[i];
                    if (enemy == null || !enemy.IsAlive || enemy.Team == participant.Team)
                    {
                        continue;
                    }

                    var distance = Vector3.Distance(position, enemy.transform.position);
                    if (!IsVisible(position, enemy))
                    {
                        continue;
                    }

                    visibleCount++;
                    nearestVisible = Mathf.Min(nearestVisible, Mathf.Min(distance, enemyDistanceCap));
                }
            }

            if (visibleCount == 0)
            {
                score += noVisibleEnemyBonus;
            }
            else
            {
                score += nearestVisible * nearestEnemyDistanceWeight;
                score -= visibleCount * visibleEnemyCountPenalty;
            }

            return score;
        }

        private bool IsVisible(Vector3 candidatePosition, ParticipantState enemy)
        {
            var origin = candidatePosition + Vector3.up * eyeHeight;
            var target = enemy.transform.position + Vector3.up * eyeHeight;
            var offset = target - origin;
            var distance = offset.magnitude;
            if (distance <= 0.0001f)
            {
                return true;
            }

            if (!Physics.Raycast(origin, offset / distance, out var hit, distance, visibilityMask, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return hit.collider != null && hit.collider.GetComponentInParent<ParticipantState>() == enemy;
        }

        private bool IsOccupied(Vector3 candidate, ParticipantState participant, IReadOnlyList<ParticipantState> roster)
        {
            if (roster == null || occupiedRadius <= 0f)
            {
                return false;
            }

            var radiusSqr = occupiedRadius * occupiedRadius;
            for (var i = 0; i < roster.Count; i++)
            {
                var other = roster[i];
                if (other == null || other == participant || !other.IsAlive)
                {
                    continue;
                }

                var delta = other.transform.position - candidate;
                if (delta.sqrMagnitude < radiusSqr)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
