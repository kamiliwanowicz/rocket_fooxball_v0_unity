using NUnit.Framework;
using RocketFooxball.Runtime.Weapons;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class ShotgunAmmoRulesTests
    {
        [Test]
        public void AmmoPickupPrecollectsWithoutShotgun()
        {
            Assert.That(ShotgunAmmoRules.TryApplyPickup(false, 0, 8, 16, false, out var hasShotgun, out var shells), Is.True);
            Assert.That(hasShotgun, Is.False);
            Assert.That(shells, Is.EqualTo(8));
        }

        [Test]
        public void ShotgunPickupAtCapacityStillGrantsWeaponToUnarmedPlayer()
        {
            Assert.That(ShotgunAmmoRules.TryApplyPickup(false, 16, 8, 16, true, out var hasShotgun, out var shells), Is.True);
            Assert.That(hasShotgun, Is.True);
            Assert.That(shells, Is.EqualTo(16));
        }

        [Test]
        public void OwnedShotgunAtCapacityRejectsWithoutMutation()
        {
            Assert.That(ShotgunAmmoRules.TryApplyPickup(true, 16, 8, 16, true, out var hasShotgun, out var shells), Is.False);
            Assert.That(hasShotgun, Is.True);
            Assert.That(shells, Is.EqualTo(16));
        }

        [Test]
        public void UnarmedAmmoPickupAtCapacityRejectsWithoutMutation()
        {
            Assert.That(ShotgunAmmoRules.TryApplyPickup(false, 16, 8, 16, false, out var hasShotgun, out var shells), Is.False);
            Assert.That(hasShotgun, Is.False);
            Assert.That(shells, Is.EqualTo(16));
        }

        [Test]
        public void OverflowClampsAndOwnedZeroRetainsOwnership()
        {
            Assert.That(ShotgunAmmoRules.TryApplyPickup(true, 0, 8, 16, false, out var hasShotgun, out var shells), Is.True);
            Assert.That(hasShotgun, Is.True);
            Assert.That(shells, Is.EqualTo(8));

            Assert.That(ShotgunAmmoRules.TryApplyPickup(true, 12, 8, 16, false, out hasShotgun, out shells), Is.True);
            Assert.That(hasShotgun, Is.True);
            Assert.That(shells, Is.EqualTo(16));
        }

        [Test]
        public void ZeroGrantAndInvalidInputsDoNotMutateOutputs()
        {
            Assert.That(ShotgunAmmoRules.TryApplyPickup(false, 4, 0, 16, true, out var hasShotgun, out var shells), Is.False);
            Assert.That(hasShotgun, Is.False);
            Assert.That(shells, Is.EqualTo(4));

            Assert.That(ShotgunAmmoRules.TryApplyPickup(true, -1, 8, 16, false, out hasShotgun, out shells), Is.False);
            Assert.That(hasShotgun, Is.True);
            Assert.That(shells, Is.EqualTo(-1));

            Assert.That(ShotgunAmmoRules.TryApplyPickup(false, 17, 8, 16, false, out hasShotgun, out shells), Is.False);
            Assert.That(hasShotgun, Is.False);
            Assert.That(shells, Is.EqualTo(17));

            Assert.That(ShotgunAmmoRules.TryApplyPickup(false, 4, 8, -1, false, out hasShotgun, out shells), Is.False);
            Assert.That(hasShotgun, Is.False);
            Assert.That(shells, Is.EqualTo(4));

            Assert.That(ShotgunAmmoRules.TryApplyPickup(false, 0, 8, 0, true, out hasShotgun, out shells), Is.False);
            Assert.That(hasShotgun, Is.False);
            Assert.That(shells, Is.EqualTo(0));
        }
    }
}
