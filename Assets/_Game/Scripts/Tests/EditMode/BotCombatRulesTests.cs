using NUnit.Framework;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Participants;
using UnityEngine;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotCombatRulesTests
    {
        [Test]
        public void PreferBallActionsUseDashThenShotgunThenRocket()
        {
            var input = Input(
                preferBall: true,
                ball: Ball(true, new Vector3(2.2f, 0f, 0f)),
                ballDirectAim: Aim(),
                ballRocketAim: Aim(true),
                ballRocketLineClear: true,
                ballLaunch: new Vector3(1f, 0f, 0f));

            Assert.That(BotCombatRules.Evaluate(input).Action, Is.EqualTo(BotCombatAction.DashKick));

            input = Input(
                preferBall: true,
                dashReady: false,
                ball: Ball(true, new Vector3(20f, 0f, 0f)),
                ballDirectAim: Aim(),
                ballRocketAim: Aim(true),
                ballRocketLineClear: true,
                ballLaunch: new Vector3(1f, 0f, 0f));
            Assert.That(BotCombatRules.Evaluate(input).Action, Is.EqualTo(BotCombatAction.FireShotgun));

            input = Input(
                preferBall: true,
                dashReady: false,
                shotgunReady: false,
                ball: Ball(true, new Vector3(100f, 0f, 0f)),
                ballRocketAim: Aim(true),
                ballRocketLineClear: true,
                ballLaunch: new Vector3(1f, 0f, 0f));
            var result = BotCombatRules.Evaluate(input);
            Assert.That(result.Action, Is.EqualTo(BotCombatAction.FireRocket));
            Assert.That(result.LaunchPosition, Is.EqualTo(input.BallRocketLaunchPosition));
        }

        [Test]
        public void PreferredBallActionsFallBackToVisibleEnemyWhenNotSuppressed()
        {
            var result = BotCombatRules.Evaluate(Input(
                preferBall: true,
                ball: Ball(false, new Vector3(3f, 0f, 0f)),
                enemy: Enemy(true, new Vector3(1f, 0f, 0f)),
                enemyDirectAim: Aim()));

            Assert.That(result.Action, Is.EqualTo(BotCombatAction.DashKick));
            Assert.That(result.Target, Is.EqualTo(BotCombatTarget.Enemy));
        }

        [Test]
        public void PreferredBallActionsDoNotFallBackWhenParticipantCombatIsSuppressed()
        {
            var result = BotCombatRules.Evaluate(Input(
                preferBall: true,
                suppressCombat: true,
                ball: Ball(false, new Vector3(3f, 0f, 0f)),
                enemy: Enemy(true, new Vector3(1f, 0f, 0f)),
                enemyDirectAim: Aim()));

            Assert.That(result.Action, Is.EqualTo(BotCombatAction.None));
        }

        [Test]
        public void EnemyActionsRequireCurrentVisibilityButIgnoreTargetScore()
        {
            var invisible = BotCombatRules.Evaluate(Input(
                enemy: Enemy(false, Vector3.one),
                enemyDirectAim: Aim()));
            Assert.That(invisible.Action, Is.EqualTo(BotCombatAction.None));

            var atBoundary = BotCombatRules.Evaluate(Input(
                activeScore: 20f,
                enemy: Enemy(true, new Vector3(16f, 0f, 0f)),
                dashReady: false,
                enemyDirectAim: Aim()));
            Assert.That(atBoundary.Action, Is.EqualTo(BotCombatAction.FireShotgun));

            var overBudget = BotCombatRules.Evaluate(Input(
                activeScore: 20.01f,
                enemy: Enemy(true, Vector3.one),
                enemyDirectAim: Aim()));
            Assert.That(overBudget.Action, Is.EqualTo(BotCombatAction.DashKick));
        }

        [Test]
        public void RocketRequiresInterceptLineAndClearInclusiveCorridor()
        {
            var input = Input(
                preferBall: true,
                shotgunReady: false,
                ball: Ball(true, new Vector3(100f, 0f, 0f)),
                ballRocketAim: Aim(true, new Vector3(10f, 0f, 0f)),
                ballRocketLineClear: true,
                ballLaunch: Vector3.zero,
                allyA: Ally(true, new Vector3(0f, 1.5f, 0f)));
            Assert.That(BotCombatRules.Evaluate(input).Action, Is.EqualTo(BotCombatAction.None));

            input = Input(
                preferBall: true,
                shotgunReady: false,
                ball: Ball(true, new Vector3(100f, 0f, 0f)),
                ballRocketAim: Aim(true, new Vector3(10f, 0f, 0f)),
                ballRocketLineClear: true,
                ballLaunch: Vector3.zero,
                allyA: Ally(true, new Vector3(0f, 1.5001f, 0f)));
            Assert.That(BotCombatRules.Evaluate(input).Action, Is.EqualTo(BotCombatAction.FireRocket));
        }

        [Test]
        public void HighRocketJumpNeedsAllGatesAndSuppressesWhenBallActionExists()
        {
            var jump = Input(
                difficulty: BotDifficulty.High,
                grounded: true,
                upwardTransition: true,
                ball: Ball(false, Vector3.zero),
                ballLaunch: new Vector3(1f, 0f, 0f),
                jumpLaunch: new Vector3(2f, 3f, 4f),
                jumpAim: new Vector3(2f, 0f, 4f),
                jumpLineClear: true,
                routeForward: Vector3.forward);
            var result = BotCombatRules.Evaluate(jump);
            Assert.That(result.Action, Is.EqualTo(BotCombatAction.RocketJump));
            Assert.That(result.Target, Is.EqualTo(BotCombatTarget.SelfImpact));
            Assert.That(result.LaunchPosition, Is.EqualTo(jump.RocketJumpLaunchPosition));
            Assert.That(result.FireAimDirection.y, Is.LessThan(0f));
            Assert.That(result.BodyFacingDirection, Is.EqualTo(Vector3.forward));

            jump = Input(
                difficulty: BotDifficulty.Medium,
                grounded: true,
                upwardTransition: true,
                jumpLineClear: true,
                routeForward: Vector3.forward);
            Assert.That(BotCombatRules.Evaluate(jump).Action, Is.EqualTo(BotCombatAction.None));

            jump = Input(
                difficulty: BotDifficulty.High,
                grounded: true,
                upwardTransition: true,
                ball: Ball(true, new Vector3(2f, 0f, 0f)),
                ballDirectAim: Aim(),
                jumpLineClear: true,
                routeForward: Vector3.forward);
            Assert.That(BotCombatRules.Evaluate(jump).Action, Is.EqualTo(BotCombatAction.None));

            jump = Input(
                difficulty: BotDifficulty.High,
                grounded: true,
                upwardTransition: true,
                jumpLaunch: Vector3.one,
                jumpAim: Vector3.one,
                jumpLineClear: true,
                routeForward: Vector3.up);
            Assert.That(BotCombatRules.Evaluate(jump).Action, Is.EqualTo(BotCombatAction.None));
        }

        [Test]
        public void RocketJumpWithInvalidRouteProducesNoAction()
        {
            var jump = Input(
                difficulty: BotDifficulty.High,
                grounded: true,
                upwardTransition: true,
                ball: Ball(false, Vector3.zero),
                launcherReady: true,
                jumpLaunch: Vector3.one,
                jumpAim: Vector3.one,
                jumpLineClear: true,
                routeForward: Vector3.zero);

            Assert.That(BotCombatRules.Evaluate(jump).Action, Is.EqualTo(BotCombatAction.None));
        }

        [Test]
        public void RocketJumpDirectionIsFiniteAndNormalized()
        {
            var direction = BotCombatRules.GetRocketJumpDirection(Vector3.forward);
            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(direction.y, Is.LessThan(0f));
            Assert.That(BotCombatRules.GetRocketJumpDirection(Vector3.zero), Is.EqualTo(Vector3.zero));
            Assert.That(BotCombatRules.GetRocketJumpDirection(new Vector3(float.NaN, 0f, 0f)), Is.EqualTo(Vector3.zero));
        }

        private static BotCombatInput Input(
            BotDifficulty difficulty = BotDifficulty.Low,
            bool preferBall = false,
            bool suppressCombat = false,
            float activeScore = 20f,
            Vector3 actionOrigin = default,
            Vector3 ballLaunch = default,
            Vector3 enemyLaunch = default,
            Vector3 jumpLaunch = default,
            Vector3 routeForward = default,
            BotBallObservation ball = default,
            BotParticipantObservation enemy = default,
            BotParticipantObservation allyA = default,
            BotParticipantObservation allyB = default,
            bool ballReachable = true,
            bool enemyReachable = true,
            bool dashReady = true,
            bool shotgunReady = true,
            bool launcherReady = true,
            BotAimSolution ballDirectAim = default,
            BotAimSolution ballRocketAim = default,
            BotAimSolution enemyDirectAim = default,
            BotAimSolution enemyRocketAim = default,
            bool ballRocketLineClear = false,
            bool enemyRocketLineClear = false,
            bool grounded = false,
            bool upwardTransition = false,
            Vector3 jumpAim = default,
            bool jumpLineClear = false)
        {
            return new BotCombatInput(
                difficulty,
                BotRole.Attacker,
                BotTargetKind.EnemyOpportunity,
                activeScore,
                suppressCombat,
                preferBall,
                actionOrigin,
                ballLaunch,
                enemyLaunch,
                jumpLaunch,
                routeForward,
                ball,
                enemy,
                allyA,
                allyB,
                ballReachable,
                enemyReachable,
                dashReady,
                shotgunReady,
                launcherReady,
                ballDirectAim,
                ballRocketAim,
                enemyDirectAim,
                enemyRocketAim,
                ballRocketLineClear,
                enemyRocketLineClear,
                grounded,
                upwardTransition,
                jumpAim,
                jumpLineClear);
        }

        private static BotAimSolution Aim(bool intercept = false, Vector3 point = default)
        {
            return new BotAimSolution(true, intercept, point == default ? Vector3.forward : point, Vector3.forward, intercept ? 0.1f : 0f);
        }

        private static BotBallObservation Ball(bool visible, Vector3 position)
        {
            return new BotBallObservation(true, visible, false, position, Vector3.zero, 0f);
        }

        private static BotParticipantObservation Enemy(bool visible, Vector3 position)
        {
            return new BotParticipantObservation(true, visible, 3, ParticipantTeam.Red, false, true, position, Vector3.zero, 100f, 100f, false, 0, 2, 0f);
        }

        private static BotParticipantObservation Ally(bool alive, Vector3 position)
        {
            return new BotParticipantObservation(true, false, 1, ParticipantTeam.Blue, false, alive, position, Vector3.zero, 100f, 100f, false, 0, 2, 0f);
        }
    }
}
