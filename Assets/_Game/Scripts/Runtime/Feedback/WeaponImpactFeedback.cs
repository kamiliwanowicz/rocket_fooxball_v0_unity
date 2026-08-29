using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Emits pooled player-owned weapon tracers and persistent impact marks.</summary>
    [MovedFrom("RocketFooxball")]
    public sealed class WeaponImpactFeedback : MonoBehaviour
    {
        private const float Epsilon = 0.000001f;
        private const float TracerOriginOffset = 0.45f;
        private const float TracerSpeed = 120f;
        private const float MinimumTracerLifetime = 0.04f;
        private const float MarkSurfaceOffset = 0.015f;
        private const float MarkLifetime = 30f;
        private const float ShotgunMarkSize = 0.16f;
        private const float RocketMarkSize = 1.15f;
        private const string CompositionError =
            "WeaponImpactFeedback requires serialized references: shotgunPellets and impactMarks.";

        [SerializeField] private ParticleSystem shotgunPellets;
        [SerializeField] private ParticleSystem impactMarks;

        private bool compositionChecked;
        private bool compositionInvalid;
        private bool compositionErrorLogged;

        private void Awake()
        {
            ValidateComposition();
        }

        private void OnEnable()
        {
            ValidateComposition();
        }

        /// <summary>Emits one world-space tracer particle from origin toward the hit endpoint.</summary>
        public void EmitTracer(Vector3 origin, Vector3 end)
        {
            if (!IsReady() || !IsFinite(origin) || !IsFinite(end))
            {
                return;
            }

            var offset = end - origin;
            if (!IsFinite(offset) || offset.sqrMagnitude <= Epsilon)
            {
                return;
            }

            var distance = Vector3.Distance(origin, end);
            if (!IsFinite(distance))
            {
                return;
            }

            var direction = offset.normalized;
            if (!IsFinite(direction) || direction.sqrMagnitude <= Epsilon)
            {
                return;
            }

            var adjustedOrigin = origin + direction * Mathf.Min(TracerOriginOffset, distance * 0.5f);
            var remainingDistance = Vector3.Distance(adjustedOrigin, end);
            if (!IsFinite(adjustedOrigin) || !IsFinite(remainingDistance))
            {
                return;
            }

            var lifetime = Mathf.Max(MinimumTracerLifetime, remainingDistance / TracerSpeed);
            var velocity = (end - adjustedOrigin) / lifetime;
            if (!IsFinite(lifetime) || !IsFinite(velocity))
            {
                return;
            }

            var emitParams = new ParticleSystem.EmitParams
            {
                position = adjustedOrigin,
                velocity = velocity,
                startLifetime = lifetime
            };
            shotgunPellets.Emit(emitParams, 1);
        }

        /// <summary>Emits one world-space shotgun impact mark.</summary>
        public void EmitShotgunMark(Vector3 point, Vector3 normal)
        {
            EmitMark(point, normal, ShotgunMarkSize);
        }

        /// <summary>Emits one world-space rocket impact mark.</summary>
        public void EmitRocketMark(Vector3 point, Vector3 normal)
        {
            EmitMark(point, normal, RocketMarkSize);
        }

        private void EmitMark(Vector3 point, Vector3 normal, float size)
        {
            if (!IsReady() || !IsFinite(point) || !IsFinite(normal) || normal.sqrMagnitude <= Epsilon)
            {
                return;
            }

            var normalizedNormal = normal.normalized;
            if (!IsFinite(normalizedNormal) || normalizedNormal.sqrMagnitude <= Epsilon)
            {
                return;
            }

            var position = point + normalizedNormal * MarkSurfaceOffset;
            if (!IsFinite(position))
            {
                return;
            }

            var emitParams = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = Vector3.zero,
                startLifetime = MarkLifetime,
                startSize = size,
                rotation3D = Quaternion.LookRotation(normalizedNormal).eulerAngles
            };
            impactMarks.Emit(emitParams, 1);
        }

        private bool IsReady()
        {
            ValidateComposition();
            return !compositionInvalid && isActiveAndEnabled;
        }

        private void ValidateComposition()
        {
            if (compositionChecked)
            {
                return;
            }

            compositionChecked = true;
            if (shotgunPellets == null || impactMarks == null)
            {
                compositionInvalid = true;
                if (!compositionErrorLogged)
                {
                    compositionErrorLogged = true;
                    Debug.LogError(CompositionError, this);
                }

                enabled = false;
                return;
            }

            ConfigureParticleSystem(shotgunPellets);
            ConfigureParticleSystem(impactMarks);
        }

        private static void ConfigureParticleSystem(ParticleSystem particleSystem)
        {
            var main = particleSystem.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = false;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
