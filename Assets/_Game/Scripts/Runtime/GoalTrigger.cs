using UnityEngine;

namespace RocketFooxball
{
    /// <summary>Detects ball-centre crossing of one goal plane and emits one score per entry.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class GoalTrigger : MonoBehaviour
    {
        public enum GoalSide
        {
            North,
            South
        }

        [Header("References")]
        [SerializeField] private BallMotor ball;
        [SerializeField] private MatchController match;
        [SerializeField] private Transform planeReference;
        [SerializeField] private Collider openingTrigger;

        [Header("Goal")]
        [SerializeField] private GoalSide goalSide;
        [SerializeField] private Vector3 planeNormal = Vector3.forward;
        [SerializeField, Min(0.1f)] private float openingHalfWidth = 18f;
        [SerializeField, Min(0f)] private float openingMinHeight = 0f;
        [SerializeField, Min(0.1f)] private float openingMaxHeight = 7f;
        [SerializeField, Min(0.01f)] private float rearmDistance = 0.5f;

        private Collider ownCollider;
        private float previousSignedDistance;
        private bool previousDistanceValid;
        private bool entryLatched;
        private bool ballInsideTrigger;
        private int previousNonZeroSide;

        private const float PlaneDeadband = 0.0001f;

        public GoalSide Side => goalSide;
        public bool EntryLatched => entryLatched;
        public Collider OpeningTrigger => openingTrigger != null ? openingTrigger : ownCollider;

        private void Awake()
        {
            ownCollider = GetComponent<Collider>();
            if (openingTrigger == null)
            {
                openingTrigger = ownCollider;
            }
            if (planeReference == null)
            {
                planeReference = transform;
            }
            if (ball == null)
            {
                ball = FindAnyObjectByType<BallMotor>();
            }
            if (match == null)
            {
                match = FindAnyObjectByType<MatchController>();
            }
        }

        private void FixedUpdate()
        {
            if (ball == null)
            {
                return;
            }

            var signedDistance = SignedDistance(ball.transform.position);
            if (ballInsideTrigger && Mathf.Abs(signedDistance) > rearmDistance)
            {
                ballInsideTrigger = false;
            }

            if (entryLatched)
            {
                if (Mathf.Abs(signedDistance) > rearmDistance)
                {
                    entryLatched = false;
                    previousDistanceValid = false;
                    previousNonZeroSide = 0;
                }
            }

            if (!previousDistanceValid)
            {
                previousSignedDistance = signedDistance;
                previousNonZeroSide = SignOutsideDeadband(signedDistance);
                previousDistanceValid = true;
                return;
            }

            var currentNonZeroSide = SignOutsideDeadband(signedDistance);
            var crossed = previousNonZeroSide != 0 && currentNonZeroSide != 0 && previousNonZeroSide != currentNonZeroSide;
            if (!entryLatched && crossed && IsInsideOpening(ball.transform.position))
            {
                TryScore();
            }

            previousSignedDistance = signedDistance;
            if (currentNonZeroSide != 0)
            {
                previousNonZeroSide = currentNonZeroSide;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null || ball == null)
            {
                return;
            }

            var otherBall = other.GetComponentInParent<BallMotor>();
            if (otherBall == ball)
            {
                // Trigger entry only observes the candidate. Scoring remains
                // gated by the signed ball-centre plane crossing in FixedUpdate.
                ballInsideTrigger = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null || ball == null)
            {
                return;
            }

            var otherBall = other.GetComponentInParent<BallMotor>();
            if (otherBall == ball)
            {
                ballInsideTrigger = false;
            }
        }

        /// <summary>Clears one-entry latch before play resumes.</summary>
        public void Rearm()
        {
            entryLatched = false;
            previousDistanceValid = false;
            previousSignedDistance = 0f;
            previousNonZeroSide = 0;
            ballInsideTrigger = false;
        }

        /// <summary>Compatibility alias for reset owners.</summary>
        public void ResetState()
        {
            Rearm();
        }

        public void SetBall(BallMotor target)
        {
            ball = target;
            Rearm();
        }

        public void SetMatch(MatchController target)
        {
            match = target;
        }

        private void TryScore()
        {
            if (entryLatched || match == null)
            {
                return;
            }

            entryLatched = true;
            match.NotifyGoal(this);
        }

        private float SignedDistance(Vector3 worldPosition)
        {
            var normal = GetPlaneNormal();
            return Vector3.Dot(worldPosition - GetPlanePosition(), normal);
        }

        private bool IsInsideOpening(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            return Mathf.Abs(local.x) <= openingHalfWidth && local.y >= openingMinHeight && local.y <= openingMaxHeight;
        }

        private static int SignOutsideDeadband(float value)
        {
            if (value > PlaneDeadband)
            {
                return 1;
            }
            if (value < -PlaneDeadband)
            {
                return -1;
            }
            return 0;
        }

        private Vector3 GetPlanePosition()
        {
            return planeReference != null ? planeReference.position : transform.position;
        }

        private Vector3 GetPlaneNormal()
        {
            var normal = planeNormal.sqrMagnitude > 0.000001f ? planeNormal.normalized : transform.forward;
            if (planeReference != null && planeReference != transform && planeNormal.sqrMagnitude > 0.000001f)
            {
                normal = planeReference.TransformDirection(planeNormal).normalized;
            }
            return normal;
        }
    }
}
