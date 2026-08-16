namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Pure shotgun pickup eligibility and capped shell accounting.</summary>
    public static class ShotgunAmmoRules
    {
        /// <summary>
        /// Applies one shell pickup without mutating the caller's state. A weapon pickup can
        /// arm a player whose stored shells are already at capacity; an ammo-only pickup cannot.
        /// </summary>
        public static bool TryApplyPickup(
            bool hasShotgun,
            int shells,
            int grant,
            int capacity,
            bool grantsWeapon,
            out bool nextHasShotgun,
            out int nextShells)
        {
            nextHasShotgun = hasShotgun;
            nextShells = shells;

            if (shells < 0 || grant <= 0 || capacity <= 0 || shells > capacity)
            {
                return false;
            }

            // A carried shotgun at capacity cannot consume another shotgun pickup. The
            // no-shotgun case is deliberately different: the weapon itself is still useful.
            if (hasShotgun && shells >= capacity)
            {
                return false;
            }

            if (!hasShotgun && grantsWeapon)
            {
                nextHasShotgun = true;
                nextShells = AddAndClamp(shells, grant, capacity);
                return true;
            }

            if (shells >= capacity)
            {
                return false;
            }

            nextShells = AddAndClamp(shells, grant, capacity);
            return nextShells != shells;
        }

        private static int AddAndClamp(int shells, int grant, int capacity)
        {
            var total = (long)shells + grant;
            return total >= capacity ? capacity : (int)total;
        }
    }
}
