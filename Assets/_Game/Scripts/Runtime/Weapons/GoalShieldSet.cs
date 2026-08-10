using UnityEngine;

namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Single persisted goal-shield collider catalog for ball collision and blast occlusion.</summary>
    [DisallowMultipleComponent]
    public sealed class GoalShieldSet : MonoBehaviour
    {
        [SerializeField] private Collider[] colliders;

        public Collider[] Colliders => colliders;

        public bool Contains(Collider collider)
        {
            if (collider == null || colliders == null)
            {
                return false;
            }

            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == collider)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
