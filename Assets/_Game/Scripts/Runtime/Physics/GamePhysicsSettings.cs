using UnityEngine;

namespace RocketFooxball.Runtime.Physics
{
    /// <summary>Locks gameplay simulation to the shared 60 Hz and gravity contract.</summary>
    public static class GamePhysicsSettings
    {
        public const float FixedDeltaTime = 1f / 60f;
        // Keep one shared gravity source for CharacterController and Rigidbody
        // simulation. Prototype gravity is reduced by 30% for the current feel.
        public const float GravityMagnitude = 16.875f * 0.7f;
        public const float PlayerCollisionRetentionFraction = 0.35f;
        public const float PlayerCollisionTransferFraction = 0.65f;
        public const float BallContactAssistPerContactCap = 12f;
        public const float BallContactAssistAggregateCap = 12f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            Time.fixedDeltaTime = FixedDeltaTime;
            UnityEngine.Physics.gravity = Vector3.down * GravityMagnitude;
        }
    }
}
