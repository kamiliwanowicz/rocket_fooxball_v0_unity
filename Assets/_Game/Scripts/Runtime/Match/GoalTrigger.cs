using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Ball;

namespace RocketFooxball.Runtime.Match
{
    /// <summary>Detects ball-centre crossing of one goal plane and emits one score per entry.</summary>
    [RequireComponent(typeof(Collider))]
    [MovedFrom("RocketFooxball")]
    public sealed class GoalTrigger : MonoBehaviour
    {
        [MovedFrom(false, "RocketFooxball", "RocketFooxball.Runtime", "GoalTrigger/GoalSide")]
        public enum GoalSide
        {
            North,
            South
        }

        [Header("References")]
        [SerializeField] private BallMotor ball;
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
        private bool previousDistanceValid;
        private bool entryLatched;
        private int previousNonZeroSide;

        private const float PlaneDeadband = 0.0001f;

        public GoalSide Side => goalSide;
        public bool EntryLatched => entryLatched;
        public Collider OpeningTrigger => openingTrigger;
        public event Action<GoalTrigger> GoalCrossed;

        private void Awake()
        {
            ownCollider = GetComponent<Collider>();
            if (!ValidateComposition())
            {
                return;
            }
        }

        private void FixedUpdate()
        {
            if (ball == null)
            {
                return;
            }

            var signedDistance = SignedDistance(ball.transform.position);

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

            if (currentNonZeroSide != 0)
            {
                previousNonZeroSide = currentNonZeroSide;
            }
        }

        /// <summary>Clears one-entry latch before play resumes.</summary>
        public void Rearm()
        {
            entryLatched = false;
            previousDistanceValid = false;
            previousNonZeroSide = 0;
        }

        private bool ValidateComposition()
        {
            if (ownCollider == null || ball == null || planeReference == null || openingTrigger == null)
            {
                Debug.LogError("GoalTrigger requires serialized references: ball, planeReference, openingTrigger.", this);
                enabled = false;
                return false;
            }

            return true;
        }

        private void TryScore()
        {
            if (entryLatched)
            {
                return;
            }

            entryLatched = true;
            GoalCrossed?.Invoke(this);
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
            return planeReference.position;
        }

        private Vector3 GetPlaneNormal()
        {
            var normal = planeNormal.sqrMagnitude > 0.000001f ? planeNormal.normalized : transform.forward;
            if (planeReference != transform && planeNormal.sqrMagnitude > 0.000001f)
            {
                normal = planeReference.TransformDirection(planeNormal).normalized;
            }
            return normal;
        }
    }
}
