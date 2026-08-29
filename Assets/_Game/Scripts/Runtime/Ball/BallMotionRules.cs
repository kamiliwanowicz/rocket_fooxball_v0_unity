using System.Collections.Generic;
using RocketFooxball.Runtime.Physics;
using UnityEngine;

namespace RocketFooxball.Runtime.Ball
{
    /// <summary>Pure ball motion calculations. BallMotor applies all Rigidbody writes.</summary>
    public static class BallMotionRules
    {
        private const float Epsilon = 0.000001f;

        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        public static Vector3 ApplyKick(Vector3 currentVelocity, Vector3 aimDirection, Vector3 playerVelocity, float hardCap, float speedFraction, float playerMomentumShare)
        {
            var direction = aimDirection.normalized;
            var opposing = Vector3.Dot(currentVelocity, direction);
            if (opposing < 0f)
            {
                currentVelocity -= direction * opposing * 0.65f;
            }

            var momentum = Mathf.Clamp(Vector3.Dot(playerVelocity, direction), 0f, hardCap * 0.25f) * Mathf.Clamp01(playerMomentumShare);
            var kickVelocity = hardCap * Mathf.Clamp(speedFraction, 0f, 1f);
            return ClampVelocity(currentVelocity + direction * (kickVelocity + momentum), hardCap);
        }

        public static Vector3 ApplyRollingResistance(Vector3 velocity, float rollingResistance, float restSpeed, float deltaTime)
        {
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            var nextMagnitude = Mathf.MoveTowards(horizontal.magnitude, 0f, rollingResistance * Mathf.Max(deltaTime, 0f));
            if (nextMagnitude <= restSpeed)
            {
                horizontal = Vector3.zero;
            }
            else if (horizontal.sqrMagnitude > Epsilon)
            {
                horizontal = horizontal.normalized * nextMagnitude;
            }

            return new Vector3(horizontal.x, velocity.y, horizontal.z);
        }

        public static Vector3 ClampVelocity(Vector3 velocity, float hardCap)
        {
            return Vector3.ClampMagnitude(velocity, hardCap);
        }

        /// <summary>Resolves inward player motion against static or dynamic contact.</summary>
        public static Vector3 ResolvePlayerCollision(
            Vector3 incomingPlayerVelocity,
            Vector3 contactNormal,
            Vector3 attachedBodyVelocity,
            bool dynamicBody,
            float transferFraction = GamePhysicsSettings.PlayerCollisionTransferFraction)
        {
            if (!IsFinite(incomingPlayerVelocity))
            {
                return Vector3.zero;
            }
            if (!IsFinite(contactNormal) || contactNormal.sqrMagnitude <= Epsilon)
            {
                return incomingPlayerVelocity;
            }

            var toward = -contactNormal.normalized;
            var playerInto = Mathf.Max(0f, Vector3.Dot(incomingPlayerVelocity, toward));
            if (!IsFinite(playerInto))
            {
                return incomingPlayerVelocity;
            }
            if (playerInto <= Epsilon)
            {
                return incomingPlayerVelocity;
            }

            if (!dynamicBody)
            {
                return incomingPlayerVelocity - toward * playerInto;
            }

            if (!IsFinite(attachedBodyVelocity) || !IsFinite(transferFraction))
            {
                return incomingPlayerVelocity;
            }

            var closing = Mathf.Max(0f, Vector3.Dot(incomingPlayerVelocity - attachedBodyVelocity, toward));
            if (!IsFinite(closing))
            {
                return incomingPlayerVelocity;
            }
            var removal = toward * Mathf.Min(playerInto, closing) * Mathf.Clamp01(transferFraction);
            if (!IsFinite(removal))
            {
                return incomingPlayerVelocity;
            }
            return incomingPlayerVelocity - removal;
        }

        /// <summary>Computes the dash contribution after resolving total and base velocities identically.</summary>
        public static Vector3 ResolveDashContributionAfterCollision(
            Vector3 incomingPlayerVelocity,
            Vector3 dashContribution,
            Vector3 contactNormal,
            Vector3 attachedBodyVelocity,
            bool dynamicBody,
            float transferFraction = GamePhysicsSettings.PlayerCollisionTransferFraction)
        {
            if (!IsFinite(incomingPlayerVelocity))
            {
                return Vector3.zero;
            }
            if (!IsFinite(dashContribution))
            {
                return Vector3.zero;
            }

            var resolvedTotal = ResolvePlayerCollision(
                incomingPlayerVelocity,
                contactNormal,
                attachedBodyVelocity,
                dynamicBody,
                transferFraction);
            var resolvedBase = ResolvePlayerCollision(
                incomingPlayerVelocity - dashContribution,
                contactNormal,
                attachedBodyVelocity,
                dynamicBody,
                transferFraction);
            return resolvedTotal - resolvedBase;
        }

        /// <summary>Computes bounded ball velocity transfer from relative closing speed.</summary>
        public static Vector3 ComputeContactAssist(
            Vector3 incomingPlayerVelocity,
            Vector3 effectiveBallVelocity,
            Vector3 contactNormal,
            float perContactCap = GamePhysicsSettings.BallContactAssistPerContactCap,
            float transferFraction = GamePhysicsSettings.PlayerCollisionTransferFraction)
        {
            if (!IsFinite(incomingPlayerVelocity) || !IsFinite(effectiveBallVelocity) ||
                !IsFinite(contactNormal) || contactNormal.sqrMagnitude <= Epsilon ||
                !IsFinite(perContactCap) || !IsFinite(transferFraction))
            {
                return Vector3.zero;
            }

            var toward = -contactNormal.normalized;
            var relativeVelocity = incomingPlayerVelocity - effectiveBallVelocity;
            if (!IsFinite(relativeVelocity))
            {
                return Vector3.zero;
            }

            var closing = Mathf.Max(0f, Vector3.Dot(relativeVelocity, toward));
            if (!IsFinite(closing))
            {
                return Vector3.zero;
            }
            var magnitude = Mathf.Min(Mathf.Max(perContactCap, 0f), closing * Mathf.Clamp01(transferFraction));
            return toward * magnitude;
        }

        /// <summary>Adds one contact transfer while enforcing the aggregate cap.</summary>
        public static Vector3 AccumulateContactAssist(
            Vector3 queuedAssist,
            Vector3 candidate,
            float aggregateCap = GamePhysicsSettings.BallContactAssistAggregateCap)
        {
            if (!IsFinite(queuedAssist) || !IsFinite(candidate) || !IsFinite(aggregateCap))
            {
                return IsFinite(queuedAssist) ? queuedAssist : Vector3.zero;
            }

            var combined = queuedAssist + candidate;
            return IsFinite(combined)
                ? Vector3.ClampMagnitude(combined, Mathf.Max(aggregateCap, 0f))
                : queuedAssist;
        }

        /// <summary>Accepts only the first dynamic-contact callback for a participant in a fixed step.</summary>
        public static bool ShouldAcceptParticipantContact(ISet<ulong> acceptedParticipantIds, ulong participantId)
        {
            return acceptedParticipantIds != null && participantId != 0UL && acceptedParticipantIds.Add(participantId);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
