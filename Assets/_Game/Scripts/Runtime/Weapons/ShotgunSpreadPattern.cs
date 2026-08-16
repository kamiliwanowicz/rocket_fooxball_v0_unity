using UnityEngine;

namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Fixed, deterministic normalized offsets for shotgun pellet spread.</summary>
    public static class ShotgunSpreadPattern
    {
        public const int Count = 12;

        private static readonly Vector2[] Offsets =
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

        /// <summary>Returns the stable offset at index, or zero for an invalid index.</summary>
        public static Vector2 GetOffset(int index)
        {
            return index >= 0 && index < Count ? Offsets[index] : Vector2.zero;
        }
    }
}
