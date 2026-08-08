using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Bridges every kick input to world and FPS-only kick animation triggers.</summary>
    public sealed class PlayerPresentation : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BallKick kick;
        [SerializeField] private Animator worldAnimator;
        [SerializeField] private Animator fpsKickAnimator;

        private static readonly int KickTrigger = Animator.StringToHash("Kick");
        private bool subscribed;

        private void OnEnable()
        {
            if (kick == null)
            {
                kick = GetComponent<BallKick>();
            }

            if (kick != null && !subscribed)
            {
                kick.KickAttempted += OnKickAttempted;
                subscribed = true;
            }
        }

        private void OnDisable()
        {
            if (kick != null && subscribed)
            {
                kick.KickAttempted -= OnKickAttempted;
                subscribed = false;
            }
        }

        private void OnKickAttempted()
        {
            if (worldAnimator != null && worldAnimator.isActiveAndEnabled)
            {
                worldAnimator.SetTrigger(KickTrigger);
            }

            if (fpsKickAnimator != null && fpsKickAnimator.isActiveAndEnabled)
            {
                fpsKickAnimator.SetTrigger(KickTrigger);
            }
        }
    }
}
