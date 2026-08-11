using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Weapons;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Bridges gameplay state to world/FPS presentation without owning simulation.</summary>
    [MovedFrom("RocketFooxball")]
    public sealed class PlayerPresentation : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BallKick kick;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private RocketLauncher launcher;
        [SerializeField] private Animator worldAnimator;
        [SerializeField] private Animator fpsKickAnimator;
        [SerializeField] private Transform weaponVisual;

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int GroundedParameter = Animator.StringToHash("Grounded");
        private static readonly int VerticalSpeedParameter = Animator.StringToHash("VerticalSpeed");
        private static readonly int KickTrigger = Animator.StringToHash("Kick");
        private static readonly Vector3 RecoilOffset = new Vector3(0f, 0.025f, -0.08f);
        private static readonly Vector3 RecoilEuler = new Vector3(-6f, 0f, 1.5f);
        private const float RecoilDuration = 0.16f;

        private Vector3 neutralLocalPosition;
        private Quaternion neutralLocalRotation;
        private float recoilElapsed;
        private bool neutralPoseCached;
        private bool recoilActive;
        private bool kickSubscribed;
        private bool launcherSubscribed;

        private void OnEnable()
        {
            CacheReferences();
            CacheNeutralPose();

            if (kick != null && !kickSubscribed)
            {
                kick.KickAttempted += OnKickAttempted;
                kickSubscribed = true;
            }

            if (launcher != null && !launcherSubscribed)
            {
                launcher.RocketLaunched += OnRocketLaunched;
                launcherSubscribed = true;
            }
        }

        private void OnDisable()
        {
            if (kick != null && kickSubscribed)
            {
                kick.KickAttempted -= OnKickAttempted;
                kickSubscribed = false;
            }

            if (launcher != null && launcherSubscribed)
            {
                launcher.RocketLaunched -= OnRocketLaunched;
                launcherSubscribed = false;
            }

            recoilActive = false;
            recoilElapsed = 0f;
            RestoreNeutralPose();
        }

        private void Update()
        {
            if (worldAnimator == null || !worldAnimator.isActiveAndEnabled || motor == null)
            {
                return;
            }

            worldAnimator.SetFloat(SpeedParameter, motor.HorizontalSpeed);
            worldAnimator.SetBool(GroundedParameter, motor.IsGrounded || motor.HasGroundContact);
            worldAnimator.SetFloat(VerticalSpeedParameter, motor.Velocity.y);
        }

        private void LateUpdate()
        {
            if (!neutralPoseCached || weaponVisual == null)
            {
                return;
            }

            if (!recoilActive)
            {
                RestoreNeutralPose();
                return;
            }

            recoilElapsed += Time.unscaledDeltaTime;
            var normalized = Mathf.Clamp01(recoilElapsed / RecoilDuration);
            var envelope = Mathf.Sin(normalized * Mathf.PI);
            weaponVisual.localPosition = neutralLocalPosition + RecoilOffset * envelope;
            weaponVisual.localRotation = neutralLocalRotation * Quaternion.Euler(RecoilEuler * envelope);

            if (normalized >= 1f)
            {
                recoilActive = false;
                RestoreNeutralPose();
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

        private void OnRocketLaunched()
        {
            if (weaponVisual == null)
            {
                return;
            }

            if (!neutralPoseCached)
            {
                CacheNeutralPose();
            }

            // Restart from the exact neutral transform; this prevents rapid-fire
            // events from adding offsets or rotations on top of one another.
            RestoreNeutralPose();
            recoilElapsed = 0f;
            recoilActive = true;
        }

        private void CacheReferences()
        {
            if (kick == null)
            {
                kick = GetComponent<BallKick>();
            }
            if (motor == null)
            {
                motor = GetComponent<PlayerMotor>();
            }
            if (launcher == null)
            {
                launcher = GetComponent<RocketLauncher>();
            }
        }

        private void CacheNeutralPose()
        {
            if (weaponVisual == null)
            {
                neutralPoseCached = false;
                return;
            }

            neutralLocalPosition = weaponVisual.localPosition;
            neutralLocalRotation = weaponVisual.localRotation;
            neutralPoseCached = true;
        }

        private void RestoreNeutralPose()
        {
            if (!neutralPoseCached || weaponVisual == null)
            {
                return;
            }

            weaponVisual.localPosition = neutralLocalPosition;
            weaponVisual.localRotation = neutralLocalRotation;
        }
    }
}
