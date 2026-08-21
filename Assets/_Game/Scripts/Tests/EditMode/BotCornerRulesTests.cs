using NUnit.Framework;
using UnityEngine;
using RocketFooxball.Runtime.Bots;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotCornerRulesTests
    {
        private static readonly BotArenaBounds Bounds = new BotArenaBounds(Vector3.zero, 65f, 45f);

        [Test]
        public void CornerEntryRequiresContinuousDwell()
        {
            var state = Advance(BotCornerState.Inactive, new Vector3(60f, 4f, 40f), 0.5f);
            Assert.That(state.Active, Is.False);
            Assert.That(state.DwellSeconds, Is.EqualTo(0.5f).Within(0.0001f));

            state = Advance(state, new Vector3(60f, 4f, 40f), 0.25f);
            Assert.That(state.Active, Is.True);
            Assert.That(state.DwellSeconds, Is.EqualTo(BotCornerRules.EnterDwellSeconds).Within(0.0001f));
        }

        [Test]
        public void LeavingEntryWindowBeforeDwellResetsTimer()
        {
            var state = Advance(BotCornerState.Inactive, new Vector3(60f, 4f, 40f), 0.5f);
            state = Advance(state, new Vector3(50f, 4f, 40f), 0.1f);
            Assert.That(state.Active, Is.False);
            Assert.That(state.DwellSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void ActiveStateUsesExitHysteresis()
        {
            var state = Advance(BotCornerState.Inactive, new Vector3(60f, 4f, 40f), 0.75f);
            state = Advance(state, new Vector3(50f, 4f, 40f), 0.01f);
            Assert.That(state.Active, Is.True);

            state = Advance(state, new Vector3(46f, 4f, 40f), 0.01f);
            Assert.That(state.Active, Is.False);
            Assert.That(state.DwellSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void CornerAndReleaseFollowXAndZAxes()
        {
            var xCorner = BotCornerRules.GetCorner(Bounds, new Vector3(60f, 4f, 0f));
            var xRelease = BotCornerRules.GetReleaseDirection(Bounds, xCorner);
            Assert.That(xCorner, Is.EqualTo(new Vector3(65f, 0f, 0f)));
            Assert.That(xRelease, Is.EqualTo(new Vector3(-1f, 0f, 0f)));

            var zCorner = BotCornerRules.GetCorner(Bounds, new Vector3(0f, 4f, 40f));
            var zRelease = BotCornerRules.GetReleaseDirection(Bounds, zCorner);
            Assert.That(zCorner, Is.EqualTo(new Vector3(0f, 0f, 45f)));
            Assert.That(zRelease, Is.EqualTo(new Vector3(0f, 0f, -1f)));

            var state = Advance(BotCornerState.Inactive, new Vector3(60f, 4f, 40f), 0.75f);
            Assert.That(state.Corner, Is.EqualTo(new Vector3(65f, 0f, 45f)));
        }

        [Test]
        public void FreshestObservationUsesAgeThenObserverSlot()
        {
            var older = Sample(2, new Vector3(60f, 4f, 0f), 0.5f);
            var tiedHighSlot = Sample(7, new Vector3(50f, 4f, 0f), 0.2f);
            var tiedLowSlot = Sample(3, new Vector3(40f, 4f, 0f), 0.2f);
            Assert.That(BotCornerRules.TrySelectFreshestBall(older, tiedHighSlot, tiedLowSlot, out var selected), Is.True);
            Assert.That(selected.ObserverSlotId, Is.EqualTo(3));
            Assert.That(selected.Observation.Position, Is.EqualTo(new Vector3(40f, 4f, 0f)));
        }

        [Test]
        public void ExpiredOrInvalidObservationResetsState()
        {
            var active = Advance(BotCornerState.Inactive, new Vector3(60f, 4f, 40f), 0.75f);
            var expired = Sample(0, new Vector3(60f, 4f, 40f), BotCornerRules.ObservationMemorySeconds + 0.01f);
            var state = BotCornerRules.Advance(active, Bounds, 0.1f, false, expired);
            Assert.That(state.Active, Is.False);

            var invalid = new BotCornerBallObservation(
                0,
                new BotBallObservation(true, true, true, new Vector3(float.NaN, 0f, 0f), Vector3.zero, 0f));
            state = BotCornerRules.Advance(active, Bounds, 0.1f, false, invalid);
            Assert.That(state.Active, Is.False);
        }

        [Test]
        public void ExplicitResetClearsStateEvenWithFreshBall()
        {
            var active = Advance(BotCornerState.Inactive, new Vector3(60f, 4f, 40f), 0.75f);
            var reset = BotCornerRules.Advance(
                active,
                Bounds,
                0.1f,
                true,
                Sample(0, new Vector3(60f, 4f, 40f), 0f));
            Assert.That(reset.Active, Is.False);
            Assert.That(reset.DwellSeconds, Is.EqualTo(0f));
            Assert.That(reset.Corner, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void RecoveryChoosesNearestLivingNonLocalWithSlotTieBreak()
        {
            var state = ActiveState(new Vector3(60f, 4f, 40f));
            var assignments = BotCornerRules.Assign(
                state,
                Bounds,
                new Vector3(60f, 4f, 40f),
                new Vector3(65f, 0f, 40f),
                new BotCornerParticipant(3, false, true, new Vector3(55f, 0f, 40f)),
                new BotCornerParticipant(1, false, true, new Vector3(53f, 0f, 40f)),
                new BotCornerParticipant(2, true, true, new Vector3(54f, 0f, 40f)));

            Assert.That(assignments.Count, Is.EqualTo(2));
            Assert.That(assignments.First.SlotId, Is.EqualTo(1));
            Assert.That(assignments.First.Intent, Is.EqualTo(BotCornerIntent.Recovery));
            Assert.That(assignments.Second.Intent, Is.EqualTo(BotCornerIntent.Combat));
        }

        [Test]
        public void RecoveryReadinessSwitchesNavigationAndActions()
        {
            var state = ActiveState(new Vector3(60f, 4f, 40f));
            var far = BotCornerRules.Assign(
                state,
                Bounds,
                new Vector3(60f, 4f, 40f),
                new Vector3(65f, 0f, 40f),
                new BotCornerParticipant(1, false, true, new Vector3(0f, 0f, 0f)));
            Assert.That(far.First.IsActionReady, Is.False);
            Assert.That(far.First.AllowsBallActions, Is.False);
            Assert.That(far.First.AllowsParticipantActions, Is.False);
            Assert.That(far.First.NavigationPoint, Is.EqualTo(far.First.RecoveryPoint));

            var ready = BotCornerRules.Assign(
                state,
                Bounds,
                new Vector3(60f, 4f, 40f),
                new Vector3(65f, 0f, 40f),
                new BotCornerParticipant(1, false, true, far.First.RecoveryPoint));
            Assert.That(ready.First.IsActionReady, Is.True);
            Assert.That(ready.First.AllowsBallActions, Is.True);
            Assert.That(ready.First.AllowsParticipantActions, Is.False);
            Assert.That(ready.First.NavigationPoint, Is.EqualTo(ready.First.ActionPoint));
        }

        [Test]
        public void RecoveryAndActionPointsClampToInsetBounds()
        {
            var recovery = BotCornerRules.GetRecoveryPoint(
                Bounds,
                new Vector3(65f, 8f, 45f),
                new Vector3(-100f, 0f, -100f));
            var action = BotCornerRules.GetActionPoint(
                Bounds,
                new Vector3(65f, 8f, 45f),
                Vector3.right);
            Assert.That(recovery, Is.EqualTo(new Vector3(62.5f, 0f, 42.5f)));
            Assert.That(action, Is.EqualTo(new Vector3(62.5f, 0f, 42.5f)));
        }

        [Test]
        public void CombatAnchorsUseCenterAndAscendingPerpendicularLanes()
        {
            var ball = new Vector3(60f, 2f, 0f);
            var release = Vector3.left;
            var one = BotCornerRules.GetCombatAnchor(Bounds, ball, release, 0, 1);
            var twoPlus = BotCornerRules.GetCombatAnchor(Bounds, ball, release, 0, 2);
            var twoMinus = BotCornerRules.GetCombatAnchor(Bounds, ball, release, 1, 2);
            var threeCenter = BotCornerRules.GetCombatAnchor(Bounds, ball, release, 0, 3);
            var threePlus = BotCornerRules.GetCombatAnchor(Bounds, ball, release, 1, 3);
            var threeMinus = BotCornerRules.GetCombatAnchor(Bounds, ball, release, 2, 3);

            Assert.That(one, Is.EqualTo(new Vector3(42f, 0f, 0f)));
            Assert.That(twoPlus, Is.EqualTo(new Vector3(42f, 0f, 6f)));
            Assert.That(twoMinus, Is.EqualTo(new Vector3(42f, 0f, -6f)));
            Assert.That(threeCenter, Is.EqualTo(one));
            Assert.That(threePlus, Is.EqualTo(twoPlus));
            Assert.That(threeMinus, Is.EqualTo(twoMinus));
        }

        [Test]
        public void AssignmentSetSortsSlotsAndCapsAtThree()
        {
            var state = ActiveState(new Vector3(60f, 4f, 40f));
            var assignments = BotCornerRules.Assign(
                state,
                Bounds,
                new Vector3(60f, 4f, 40f),
                new Vector3(65f, 0f, 40f),
                new BotCornerParticipant(9, false, true, Vector3.zero),
                new BotCornerParticipant(4, false, true, Vector3.right),
                new BotCornerParticipant(7, false, true, Vector3.forward),
                new BotCornerParticipant(2, false, true, Vector3.left));

            Assert.That(assignments.Count, Is.EqualTo(3));
            Assert.That(assignments.First.SlotId, Is.EqualTo(2));
            Assert.That(assignments.Second.SlotId, Is.EqualTo(4));
            Assert.That(assignments.Third.SlotId, Is.EqualTo(7));
        }

        private static BotCornerState ActiveState(Vector3 ballPosition)
        {
            return Advance(BotCornerState.Inactive, ballPosition, BotCornerRules.EnterDwellSeconds);
        }

        private static BotCornerState Advance(BotCornerState state, Vector3 ballPosition, float delta)
        {
            return BotCornerRules.Advance(
                state,
                Bounds,
                delta,
                false,
                Sample(0, ballPosition, 0f));
        }

        private static BotCornerBallObservation Sample(int observerSlotId, Vector3 position, float age)
        {
            return new BotCornerBallObservation(
                observerSlotId,
                new BotBallObservation(true, true, true, position, Vector3.zero, age));
        }
    }
}
