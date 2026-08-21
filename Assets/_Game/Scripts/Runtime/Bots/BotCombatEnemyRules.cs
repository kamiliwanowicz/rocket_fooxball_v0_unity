using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    /// <summary>Pure selection of the nearest currently visible living enemy.</summary>
    public static class BotCombatEnemyRules
    {
        /// <summary>
        /// Selects the candidate with the smallest horizontal (XZ) distance. Equal distances use
        /// the lowest stable participant slot so input ordering cannot change a decision.
        /// </summary>
        public static bool TrySelectVisibleEnemy(
            Vector3 actionOrigin,
            BotParticipantObservation enemyA,
            BotParticipantObservation enemyB,
            BotParticipantObservation enemyC,
            out BotParticipantObservation selected)
        {
            selected = default(BotParticipantObservation);
            if (!IsFinite(actionOrigin))
            {
                return false;
            }

            var found = false;
            var bestDistanceSquared = float.PositiveInfinity;
            TryConsider(actionOrigin, enemyA, ref found, ref bestDistanceSquared, ref selected);
            TryConsider(actionOrigin, enemyB, ref found, ref bestDistanceSquared, ref selected);
            TryConsider(actionOrigin, enemyC, ref found, ref bestDistanceSquared, ref selected);
            return found;
        }

        public static bool TrySelectVisibleEnemy(
            Vector3 actionOrigin,
            BotParticipantObservation enemyA,
            BotParticipantObservation enemyB,
            out BotParticipantObservation selected)
        {
            return TrySelectVisibleEnemy(
                actionOrigin,
                enemyA,
                enemyB,
                default(BotParticipantObservation),
                out selected);
        }

        public static bool TrySelectVisibleEnemy(
            Vector3 actionOrigin,
            BotParticipantObservation[] candidates,
            out BotParticipantObservation selected)
        {
            selected = default(BotParticipantObservation);
            if (!IsFinite(actionOrigin) || candidates == null)
            {
                return false;
            }

            var found = false;
            var bestDistanceSquared = float.PositiveInfinity;
            for (var i = 0; i < candidates.Length; i++)
            {
                TryConsider(actionOrigin, candidates[i], ref found, ref bestDistanceSquared, ref selected);
            }

            return found;
        }

        /// <summary>Returns the selected enemy or a default observation when none is eligible.</summary>
        public static BotParticipantObservation SelectVisibleEnemy(
            Vector3 actionOrigin,
            BotParticipantObservation enemyA,
            BotParticipantObservation enemyB,
            BotParticipantObservation enemyC)
        {
            TrySelectVisibleEnemy(actionOrigin, enemyA, enemyB, enemyC, out var selected);
            return selected;
        }

        public static BotParticipantObservation SelectVisibleEnemy(
            Vector3 actionOrigin,
            BotParticipantObservation[] candidates)
        {
            TrySelectVisibleEnemy(actionOrigin, candidates, out var selected);
            return selected;
        }

        public static bool TrySelectNearestVisibleEnemy(
            Vector3 actionOrigin,
            BotParticipantObservation enemyA,
            BotParticipantObservation enemyB,
            BotParticipantObservation enemyC,
            out BotParticipantObservation selected)
        {
            return TrySelectVisibleEnemy(actionOrigin, enemyA, enemyB, enemyC, out selected);
        }

        public static bool TryGetNearestVisibleEnemy(
            Vector3 actionOrigin,
            BotParticipantObservation enemyA,
            BotParticipantObservation enemyB,
            BotParticipantObservation enemyC,
            out BotParticipantObservation selected)
        {
            return TrySelectVisibleEnemy(actionOrigin, enemyA, enemyB, enemyC, out selected);
        }

        public static BotParticipantObservation SelectNearestVisibleEnemy(
            Vector3 actionOrigin,
            BotParticipantObservation enemyA,
            BotParticipantObservation enemyB,
            BotParticipantObservation enemyC)
        {
            return SelectVisibleEnemy(actionOrigin, enemyA, enemyB, enemyC);
        }

        public static bool IsEligibleVisibleEnemy(BotParticipantObservation candidate)
        {
            return candidate.HasObservation && candidate.IsVisible && candidate.IsAlive &&
                   IsFinite(candidate.Position);
        }

        private static void TryConsider(
            Vector3 actionOrigin,
            BotParticipantObservation candidate,
            ref bool found,
            ref float bestDistanceSquared,
            ref BotParticipantObservation selected)
        {
            if (!IsEligibleVisibleEnemy(candidate))
            {
                return;
            }

            var offset = candidate.Position - actionOrigin;
            offset.y = 0f;
            var distanceSquared = offset.sqrMagnitude;
            if (!IsFinite(distanceSquared))
            {
                return;
            }

            if (!found || distanceSquared < bestDistanceSquared ||
                (distanceSquared == bestDistanceSquared && candidate.SlotId < selected.SlotId))
            {
                found = true;
                bestDistanceSquared = distanceSquared;
                selected = candidate;
            }
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
