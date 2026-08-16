using NUnit.Framework;
using RocketFooxball.Runtime.Weapons;
using UnityEngine;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class ShotgunSpreadPatternTests
    {
        private static readonly Vector2[] ExpectedOffsets =
        {
            new Vector2(-0.22f, 0.18f),
            new Vector2(0.22f, -0.18f),
            new Vector2(-0.50f, -0.15f),
            new Vector2(0.50f, 0.15f),
            new Vector2(-0.18f, 0.55f),
            new Vector2(0.18f, -0.55f),
            new Vector2(-0.72f, 0.36f),
            new Vector2(0.72f, -0.36f),
            new Vector2(0f, 0.82f),
            new Vector2(0f, -0.82f),
            new Vector2(-0.88f, -0.12f),
            new Vector2(0.88f, 0.12f)
        };

        [Test]
        public void PatternHasStableOrderAndBoundedOffsets()
        {
            Assert.That(ShotgunSpreadPattern.Count, Is.EqualTo(ExpectedOffsets.Length));
            for (var i = 0; i < ExpectedOffsets.Length; i++)
            {
                Assert.That(ShotgunSpreadPattern.GetOffset(i).x, Is.EqualTo(ExpectedOffsets[i].x).Within(0.0001f));
                Assert.That(ShotgunSpreadPattern.GetOffset(i).y, Is.EqualTo(ExpectedOffsets[i].y).Within(0.0001f));
                Assert.That(ShotgunSpreadPattern.GetOffset(i).magnitude, Is.LessThanOrEqualTo(1f));
            }
        }

        [Test]
        public void InvalidPatternIndexReturnsZero()
        {
            Assert.That(ShotgunSpreadPattern.GetOffset(-1), Is.EqualTo(Vector2.zero));
            Assert.That(ShotgunSpreadPattern.GetOffset(ShotgunSpreadPattern.Count), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ProgrammaticForwardUsesStableBasisAndKeepsPelletsInsideCone()
        {
            var forward = new Vector3(0.91f, -0.27f, 0.32f).normalized;
            Assert.That(Vector3.Dot(Vector3.forward, forward), Is.LessThan(0.5f));
            Assert.That(ShotgunWeapon.TryBuildSpreadBasis(forward, Vector3.up, out var right, out var up), Is.True);

            var firstPass = new Vector3[ShotgunSpreadPattern.Count];
            var cosineLimit = Mathf.Cos(7f * Mathf.Deg2Rad);
            for (var i = 0; i < ShotgunSpreadPattern.Count; i++)
            {
                firstPass[i] = ShotgunWeapon.GetProgrammaticPelletDirection(i, forward, right, up, 7f);
                Assert.That(firstPass[i].sqrMagnitude, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(Vector3.Dot(forward, firstPass[i]), Is.GreaterThanOrEqualTo(cosineLimit - 0.0001f));
            }

            Assert.That(ShotgunWeapon.TryBuildSpreadBasis(forward, Vector3.up, out var repeatRight, out var repeatUp), Is.True);
            for (var i = 0; i < ShotgunSpreadPattern.Count; i++)
            {
                var repeat = ShotgunWeapon.GetProgrammaticPelletDirection(i, forward, repeatRight, repeatUp, 7f);
                Assert.That(repeat, Is.EqualTo(firstPass[i]));
            }
        }

        [Test]
        public void NearReferenceUpUsesFiniteDeterministicFallback()
        {
            var forward = new Vector3(0.00001f, 1f, -0.00001f).normalized;
            Assert.That(ShotgunWeapon.TryBuildSpreadBasis(forward, Vector3.up, out var right, out var up), Is.True);
            Assert.That(Vector3.Dot(right, Vector3.forward), Is.GreaterThan(0.9999f));

            var firstPass = new Vector3[ShotgunSpreadPattern.Count];
            for (var i = 0; i < ShotgunSpreadPattern.Count; i++)
            {
                firstPass[i] = ShotgunWeapon.GetProgrammaticPelletDirection(i, forward, right, up, 7f);
                Assert.That(firstPass[i].sqrMagnitude, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(Vector3.Dot(forward, firstPass[i]), Is.GreaterThanOrEqualTo(Mathf.Cos(7f * Mathf.Deg2Rad) - 0.0001f));
            }

            Assert.That(ShotgunWeapon.TryBuildSpreadBasis(forward, Vector3.up, out var repeatRight, out var repeatUp), Is.True);
            for (var i = 0; i < ShotgunSpreadPattern.Count; i++)
            {
                Assert.That(ShotgunWeapon.GetProgrammaticPelletDirection(i, forward, repeatRight, repeatUp, 7f), Is.EqualTo(firstPass[i]));
            }
        }
    }
}
