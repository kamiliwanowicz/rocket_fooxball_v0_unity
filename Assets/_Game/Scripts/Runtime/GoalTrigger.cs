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

            if (entryLatched)
            {
                if (match == null && Mathf.Abs(SignedDistance(ball.transform.position)) > rearmDistance)
                {
                    entryLatched = false;
                    previousDistanceValid = false;
                }
                return;
            }

            var signedDistance = SignedDistance(ball.transform.position);
            if (!previousDistanceValid)
            {
                previousSignedDistance = signedDistance;
                previousDistanceValid = true;
                return;
            }

            var crossed = Mathf.Abs(signedDistance) <= 0.02f || previousSignedDistance * signedDistance < 0f;
            if (crossed && IsInsideOpening(ball.transform.position))
            {
                TryScore();
            }

            previousSignedDistance = signedDistance;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (entryLatched || other == null || ball == null)
            {
                return;
            }

            var otherBall = other.GetComponentInParent<BallMotor>();
            if (otherBall == ball && IsInsideOpening(ball.transform.position))
            {
                TryScore();
            }
        }

        /// <summary>Clears one-entry latch before play resumes.</summary>
        public void Rearm()
        {
            entryLatched = false;
            previousDistanceValid = false;
            previousSignedDistance = 0f;
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
