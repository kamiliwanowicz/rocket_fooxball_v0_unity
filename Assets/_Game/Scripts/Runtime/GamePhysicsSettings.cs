using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Locks gameplay simulation to source prototype's 60 Hz and gravity contract.</summary>
    public static class GamePhysicsSettings
    {
        public const float FixedDeltaTime = 1f / 60f;
        public const float GravityMagnitude = 16.875f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            Time.fixedDeltaTime = FixedDeltaTime;
            Physics.gravity = Vector3.down * GravityMagnitude;
        }
    }
}
