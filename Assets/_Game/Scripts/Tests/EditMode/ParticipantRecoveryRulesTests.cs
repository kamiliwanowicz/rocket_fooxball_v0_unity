using NUnit.Framework;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Participants;
using UnityEngine;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class ParticipantRecoveryRulesTests
    {
        private const float Threshold = -1f;

        [Test]
        public void PositionAtOrAboveThresholdDoesNotRecover()
        {
            Assert.That(ParticipantRecoveryRules.ShouldRecover(
                MatchRules.MatchState.Playing,
                ParticipantLifecycle.Alive,
                new Vector3(0f, Threshold, 0f),
                Threshold), Is.False);
            Assert.That(ParticipantRecoveryRules.ShouldRecover(
                MatchRules.MatchState.Playing,
                ParticipantLifecycle.Alive,
                new Vector3(0f, 0f, 0f),
                Threshold), Is.False);
        }

        [Test]
        public void PositionBelowThresholdRecovers()
        {
            Assert.That(ParticipantRecoveryRules.ShouldRecover(
                MatchRules.MatchState.Playing,
                ParticipantLifecycle.Alive,
                new Vector3(0f, -1.01f, 0f),
                Threshold), Is.True);
        }

        [Test]
        public void NonFinitePositionRecovers()
        {
            Assert.That(ParticipantRecoveryRules.ShouldRecover(
                MatchRules.MatchState.Playing,
                ParticipantLifecycle.Alive,
                new Vector3(float.NaN, 0f, 0f),
                Threshold), Is.True);
            Assert.That(ParticipantRecoveryRules.ShouldRecover(
                MatchRules.MatchState.Playing,
                ParticipantLifecycle.Alive,
                new Vector3(0f, float.PositiveInfinity, 0f),
                Threshold), Is.True);
        }

        [Test]
        public void MatchAndLifecycleGatesRejectRecovery()
        {
            var below = new Vector3(0f, -10f, 0f);
            Assert.That(ParticipantRecoveryRules.ShouldRecover(MatchRules.MatchState.GoalFreeze, ParticipantLifecycle.Alive, below, Threshold), Is.False);
            Assert.That(ParticipantRecoveryRules.ShouldRecover(MatchRules.MatchState.Playing, ParticipantLifecycle.Dead, below, Threshold), Is.False);
            Assert.That(ParticipantRecoveryRules.ShouldRecover(MatchRules.MatchState.Playing, ParticipantLifecycle.Respawning, below, Threshold), Is.False);
        }

        [Test]
        public void ValidPostRecoveryPositionClearsTrigger()
        {
            Assert.That(ParticipantRecoveryRules.IsValidDestination(new Vector3(12f, 0f, 0f), Threshold), Is.True);
            Assert.That(ParticipantRecoveryRules.ShouldRecover(
                MatchRules.MatchState.Playing,
                ParticipantLifecycle.Alive,
                new Vector3(12f, 0f, 0f),
                Threshold), Is.False);
        }
    }
}
