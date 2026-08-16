using UnityEngine;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Pickups
{
    /// <summary>Reusable automatic pickup contact, availability, respawn, and coordinated-reset owner.</summary>
    public abstract class ArenaPickup : MonoBehaviour
    {
        [SerializeField] private MatchController match;
        [SerializeField] private Collider pickupTrigger;
        [SerializeField] private GameObject visualRoot;
        [SerializeField, Min(0f)] private float respawnDelay = 15f;

        private PickupRespawnState respawnState = new PickupRespawnState();
        private bool compositionValid;
        private bool resetEventSubscribed;

        public bool IsAvailable => respawnState != null && respawnState.IsAvailable;
        public float RespawnRemaining => respawnState != null ? respawnState.Remaining : 0f;
        public float RespawnDelay => respawnDelay;

        protected MatchController Match => match;

        protected virtual void Awake()
        {
            respawnState = new PickupRespawnState();
            compositionValid = ValidateComposition();
            if (!compositionValid)
            {
                enabled = false;
                return;
            }

            ResetPresentation();
        }

        protected virtual void OnEnable()
        {
            if (compositionValid && !resetEventSubscribed)
            {
                match.CoordinatedResetRequested += OnCoordinatedResetRequested;
                resetEventSubscribed = true;
            }
        }

        protected virtual void OnDisable()
        {
            if (!resetEventSubscribed)
            {
                return;
            }

            if (match != null)
            {
                match.CoordinatedResetRequested -= OnCoordinatedResetRequested;
            }
            resetEventSubscribed = false;
        }

        protected virtual void FixedUpdate()
        {
            if (!compositionValid || respawnState == null || respawnState.IsAvailable)
            {
                return;
            }

            if (respawnState.Advance(Time.fixedDeltaTime))
            {
                SetVisualActive(true);
            }
        }

        protected void OnTriggerEnter(Collider other)
        {
            TryCollect(other);
        }

        protected void OnTriggerStay(Collider other)
        {
            TryCollect(other);
        }

        protected abstract bool TryApplyToParticipant(ParticipantState participant);

        private void TryCollect(Collider other)
        {
            if (!compositionValid || respawnState == null || !respawnState.IsAvailable || other == null)
            {
                return;
            }

            var participant = other.GetComponentInParent<ParticipantState>();
            if (participant == null || !TryApplyToParticipant(participant))
            {
                return;
            }

            if (respawnState.TryConsume(respawnDelay))
            {
                SetVisualActive(false);
            }
        }

        private void OnCoordinatedResetRequested(MatchResetReason reason)
        {
            ResetAvailability();
        }

        private void ResetAvailability()
        {
            respawnState?.Reset();
            SetVisualActive(true);
        }

        private void ResetPresentation()
        {
            respawnState.Reset();
            SetVisualActive(true);
        }

        private void SetVisualActive(bool active)
        {
            if (visualRoot != null)
            {
                visualRoot.SetActive(active);
            }
        }

        private bool ValidateComposition()
        {
            if (match == null)
            {
                Debug.LogError("ArenaPickup requires serialized match reference.", this);
                return false;
            }

            if (pickupTrigger == null)
            {
                Debug.LogError("ArenaPickup requires serialized pickupTrigger reference.", this);
                return false;
            }

            if (pickupTrigger.gameObject != gameObject || !pickupTrigger.enabled || !pickupTrigger.isTrigger)
            {
                Debug.LogError("ArenaPickup pickupTrigger must be an enabled trigger collider on the pickup root.", this);
                return false;
            }

            if (visualRoot == null)
            {
                Debug.LogError("ArenaPickup requires serialized visualRoot reference.", this);
                return false;
            }

            if (visualRoot == gameObject || !visualRoot.transform.IsChildOf(transform))
            {
                Debug.LogError("ArenaPickup visualRoot must be a distinct child of the pickup root.", this);
                return false;
            }

            if (!IsFinite(respawnDelay) || respawnDelay < 0f)
            {
                Debug.LogError("ArenaPickup respawnDelay must be finite and nonnegative.", this);
                return false;
            }

            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
