#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RocketFooxball.Editor
{
    /// <summary>
    /// Zero-save visual preview.  Every touched editor value is captured by a
    /// stable hierarchy/component identity and restored before any save or
    /// lifecycle boundary.
    /// </summary>
    [InitializeOnLoad]
    internal static class MovementLabFastModeSession
    {
        private const int FastQualityIndex = GraphicsQualityConfigurator.IterationQualityIndex;
        private static readonly Color FastAmbientSky = new Color(0.62f, 0.70f, 0.78f, 1f);
        private static readonly Color FastAmbientEquator = new Color(0.48f, 0.52f, 0.56f, 1f);
        private static readonly Color FastAmbientGround = new Color(0.28f, 0.31f, 0.35f, 1f);
        private static Snapshot snapshot;
        private static bool applying;
        private static bool directionalHardShadows;

        static MovementLabFastModeSession()
        {
            EditorApplication.quitting += RestoreIfActive;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            SceneManager.sceneUnloaded += _ => RestoreIfActive();
            EditorSceneManager.sceneOpened += (_, __) => RestoreIfActive();
        }

        internal static bool IsActive => snapshot != null;

        [MenuItem("Rocket Fooxball/Fast Preview/Enter")]
        internal static void EnterFromMenu() => Enter();

        [MenuItem("Rocket Fooxball/Fast Preview/Exit")]
        internal static void ExitFromMenu() => RestoreIfActive();

        [MenuItem("Rocket Fooxball/Fast Preview/Toggle Directional Hard Shadows")]
        internal static void ToggleDirectionalHardShadows()
        {
            directionalHardShadows = !directionalHardShadows;
            if (IsActive) ApplyDirectionalShadowState();
            Debug.Log("Rocket Fooxball fast preview directional hard shadows: " + (directionalHardShadows ? "on" : "off"));
        }

        internal static void Enter()
        {
            if (IsActive) return;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !string.Equals(scene.path, MovementLabContract.ScenePath, StringComparison.Ordinal))
                throw new InvalidOperationException("Fast preview requires the loaded MovementLab scene: " + MovementLabContract.ScenePath);
            if (scene.isDirty) throw new InvalidOperationException("Fast preview requires a clean loaded MovementLab scene; save or discard scene edits first.");

            snapshot = Capture(scene);
            try
            {
                Apply(snapshot);
                AssertApplied(snapshot);
                Debug.Log("Rocket Fooxball fast preview entered: quality=Iteration index=" + FastQualityIndex + "; renderer lightmap bindings detached.");
            }
            catch
            {
                try { RestoreSnapshot(snapshot); } finally { snapshot = null; }
                throw;
            }
        }

        internal static void RestoreIfActive()
        {
            if (snapshot == null || applying) return;
            var current = snapshot;
            try
            {
                applying = true;
                RestoreSnapshot(current);
                AssertRestored(current);
            }
            finally
            {
                applying = false;
                snapshot = null;
            }
        }

        internal static void AssertAppliedState() { if (snapshot == null) throw new InvalidOperationException("Fast preview is not active."); AssertApplied(snapshot); }

        private static Snapshot Capture(Scene scene)
        {
            var allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(renderer => renderer != null && renderer.gameObject.scene == scene)
                .OrderBy(renderer => Identity(renderer.transform), StringComparer.Ordinal).ToArray();
            var renderers = allRenderers.Select(renderer => new RendererSnapshot
            {
                identity = Identity(renderer.transform) + ":" + renderer.GetType().FullName,
                renderer = renderer,
                lightmapIndex = renderer.lightmapIndex,
                realtimeLightmapIndex = renderer.realtimeLightmapIndex,
                lightmapScaleOffset = renderer.lightmapScaleOffset,
                realtimeLightmapScaleOffset = renderer.realtimeLightmapScaleOffset
            }).ToArray();

            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(light => light != null && light.gameObject.scene == scene)
                .OrderBy(light => Identity(light.transform), StringComparer.Ordinal)
                .Select(light => new LightSnapshot
                {
                    identity = Identity(light.transform), light = light, bakeType = light.lightmapBakeType,
                    shadows = light.shadows, intensity = light.intensity, shadowStrength = light.shadowStrength,
                    shadowBias = light.shadowBias, shadowNormalBias = light.shadowNormalBias
                }).ToArray();

            var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(volume => volume != null && volume.gameObject.scene == scene)
                .OrderBy(volume => Identity(volume.transform), StringComparer.Ordinal)
                .Select(volume => new VolumeSnapshot { identity = Identity(volume.transform), volume = volume, enabled = volume.enabled, weight = volume.weight, priority = volume.priority, sharedProfile = volume.sharedProfile }).ToArray();
            var probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(probe => probe != null && probe.gameObject.scene == scene)
                .OrderBy(probe => Identity(probe.transform), StringComparer.Ordinal)
                .Select(probe => new ReflectionSnapshot
                {
                    identity = Identity(probe.transform), probe = probe, enabled = probe.enabled, mode = probe.mode,
                    refreshMode = probe.refreshMode, timeSlicingMode = probe.timeSlicingMode, resolution = probe.resolution,
                    hdr = probe.hdr, boxProjection = probe.boxProjection, intensity = probe.intensity
                }).ToArray();

            var sceneDirty = scene.isDirty;
            return new Snapshot
            {
                scene = scene,
                sceneDirty = sceneDirty,
                qualityIndex = QualitySettings.GetQualityLevel(),
                ambientMode = RenderSettings.ambientMode,
                ambientSky = RenderSettings.ambientSkyColor,
                ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor,
                ambientIntensity = RenderSettings.ambientIntensity,
                sun = RenderSettings.sun,
                defaultReflectionMode = RenderSettings.defaultReflectionMode,
                defaultReflectionResolution = RenderSettings.defaultReflectionResolution,
                reflectionBounces = RenderSettings.reflectionBounces,
                reflectionIntensity = RenderSettings.reflectionIntensity,
                renderers = renderers,
                lights = lights,
                volumes = volumes,
                probes = probes
            };
        }

        private static void Apply(Snapshot state)
        {
            applying = true;
            QualitySettings.SetQualityLevel(FastQualityIndex, true);
            for (var i = 0; i < state.renderers.Length; i++)
            {
                var renderer = state.renderers[i].renderer;
                if (renderer == null) continue;
                renderer.lightmapIndex = -1;
                renderer.realtimeLightmapIndex = -1;
                renderer.lightmapScaleOffset = state.renderers[i].lightmapScaleOffset;
                renderer.realtimeLightmapScaleOffset = state.renderers[i].realtimeLightmapScaleOffset;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = FastAmbientSky;
            RenderSettings.ambientEquatorColor = FastAmbientEquator;
            RenderSettings.ambientGroundColor = FastAmbientGround;
            RenderSettings.ambientIntensity = 1.6f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 64;
            RenderSettings.reflectionBounces = 0;
            RenderSettings.reflectionIntensity = 1f;

            if (state.sun != null)
            {
                state.sun.lightmapBakeType = LightmapBakeType.Realtime;
                state.sun.shadows = directionalHardShadows ? LightShadows.Hard : LightShadows.None;
            }
            for (var i = 0; i < state.volumes.Length; i++) if (state.volumes[i].volume != null) state.volumes[i].volume.enabled = false;
            for (var i = 0; i < state.probes.Length; i++) if (state.probes[i].probe != null) state.probes[i].probe.enabled = false;
            applying = false;
        }

        private static void ApplyDirectionalShadowState()
        {
            if (snapshot?.sun == null) return;
            snapshot.sun.shadows = directionalHardShadows ? LightShadows.Hard : LightShadows.None;
            snapshot.sun.lightmapBakeType = LightmapBakeType.Realtime;
        }

        private static void RestoreSnapshot(Snapshot state)
        {
            if (state == null) return;
            applying = true;
            for (var i = 0; i < state.renderers.Length; i++)
            {
                var item = state.renderers[i];
                if (item.renderer == null) continue;
                item.renderer.lightmapIndex = item.lightmapIndex;
                item.renderer.realtimeLightmapIndex = item.realtimeLightmapIndex;
                item.renderer.lightmapScaleOffset = item.lightmapScaleOffset;
                item.renderer.realtimeLightmapScaleOffset = item.realtimeLightmapScaleOffset;
            }
            for (var i = 0; i < state.lights.Length; i++)
            {
                var item = state.lights[i];
                if (item.light == null) continue;
                item.light.lightmapBakeType = item.bakeType;
                item.light.shadows = item.shadows;
                item.light.intensity = item.intensity;
                item.light.shadowStrength = item.shadowStrength;
                item.light.shadowBias = item.shadowBias;
                item.light.shadowNormalBias = item.shadowNormalBias;
            }
            for (var i = 0; i < state.volumes.Length; i++)
            {
                var item = state.volumes[i];
                if (item.volume == null) continue;
                item.volume.enabled = item.enabled;
                item.volume.weight = item.weight;
                item.volume.priority = item.priority;
                item.volume.sharedProfile = item.sharedProfile;
            }
            for (var i = 0; i < state.probes.Length; i++)
            {
                var item = state.probes[i];
                if (item.probe == null) continue;
                item.probe.enabled = item.enabled;
                item.probe.mode = item.mode;
                item.probe.refreshMode = item.refreshMode;
                item.probe.timeSlicingMode = item.timeSlicingMode;
                item.probe.resolution = item.resolution;
                item.probe.hdr = item.hdr;
                item.probe.boxProjection = item.boxProjection;
                item.probe.intensity = item.intensity;
            }
            RenderSettings.ambientMode = state.ambientMode;
            RenderSettings.ambientSkyColor = state.ambientSky;
            RenderSettings.ambientEquatorColor = state.ambientEquator;
            RenderSettings.ambientGroundColor = state.ambientGround;
            RenderSettings.ambientIntensity = state.ambientIntensity;
            RenderSettings.sun = state.sun;
            RenderSettings.defaultReflectionMode = state.defaultReflectionMode;
            RenderSettings.defaultReflectionResolution = state.defaultReflectionResolution;
            RenderSettings.reflectionBounces = state.reflectionBounces;
            RenderSettings.reflectionIntensity = state.reflectionIntensity;
            QualitySettings.SetQualityLevel(state.qualityIndex, true);
            if (!state.sceneDirty) ClearSceneDirtiness(state.scene);
            applying = false;
        }

        private static void AssertApplied(Snapshot state)
        {
            if (QualitySettings.GetQualityLevel() != FastQualityIndex) throw new InvalidOperationException("Fast preview quality profile was not applied.");
            if (state.renderers.Any(item => item.renderer != null && (item.renderer.lightmapIndex != -1 || item.renderer.realtimeLightmapIndex != -1)))
                throw new InvalidOperationException("Fast preview failed to detach a renderer lightmap binding.");
            if (RenderSettings.ambientMode != AmbientMode.Trilight || RenderSettings.ambientIntensity != 1.6f)
                throw new InvalidOperationException("Fast preview ambient contract was not applied.");
            if (state.sun != null && state.sun.shadows != (directionalHardShadows ? LightShadows.Hard : LightShadows.None))
                throw new InvalidOperationException("Fast preview directional shadow contract was not applied.");
            if (state.volumes.Any(item => item.volume != null && item.volume.enabled)) throw new InvalidOperationException("Fast preview post volume remained enabled.");
            if (state.probes.Any(item => item.probe != null && item.probe.enabled)) throw new InvalidOperationException("Fast preview reflection probe remained enabled.");
        }

        private static void AssertRestored(Snapshot state)
        {
            if (QualitySettings.GetQualityLevel() != state.qualityIndex)
                throw new InvalidOperationException("Fast preview failed to restore quality/default pipeline state.");
            if (RenderSettings.ambientMode != state.ambientMode || RenderSettings.ambientSkyColor != state.ambientSky ||
                RenderSettings.ambientEquatorColor != state.ambientEquator || RenderSettings.ambientGroundColor != state.ambientGround ||
                Mathf.Abs(RenderSettings.ambientIntensity - state.ambientIntensity) > 0.0001f || RenderSettings.sun != state.sun ||
                RenderSettings.defaultReflectionMode != state.defaultReflectionMode || RenderSettings.defaultReflectionResolution != state.defaultReflectionResolution ||
                RenderSettings.reflectionBounces != state.reflectionBounces || Mathf.Abs(RenderSettings.reflectionIntensity - state.reflectionIntensity) > 0.0001f)
                throw new InvalidOperationException("Fast preview failed to restore exact RenderSettings snapshot.");
            for (var i = 0; i < state.renderers.Length; i++)
            {
                var item = state.renderers[i];
                if (item.renderer == null) continue;
                if (item.renderer.lightmapIndex != item.lightmapIndex || item.renderer.realtimeLightmapIndex != item.realtimeLightmapIndex ||
                    item.renderer.lightmapScaleOffset != item.lightmapScaleOffset || item.renderer.realtimeLightmapScaleOffset != item.realtimeLightmapScaleOffset)
                    throw new InvalidOperationException("Fast preview failed to restore renderer binding: " + item.identity);
            }
            for (var i = 0; i < state.lights.Length; i++)
            {
                var item = state.lights[i];
                if (item.light == null) continue;
                if (item.light.lightmapBakeType != item.bakeType || item.light.shadows != item.shadows ||
                    !Mathf.Approximately(item.light.intensity, item.intensity) ||
                    !Mathf.Approximately(item.light.shadowStrength, item.shadowStrength) ||
                    !Mathf.Approximately(item.light.shadowBias, item.shadowBias) ||
                    !Mathf.Approximately(item.light.shadowNormalBias, item.shadowNormalBias))
                    throw new InvalidOperationException("Fast preview failed to restore light decision fields: " + item.identity);
            }
            for (var i = 0; i < state.volumes.Length; i++)
            {
                var item = state.volumes[i];
                if (item.volume == null) continue;
                if (item.volume.enabled != item.enabled || !Mathf.Approximately(item.volume.weight, item.weight) ||
                    !Mathf.Approximately(item.volume.priority, item.priority) || item.volume.sharedProfile != item.sharedProfile)
                    throw new InvalidOperationException("Fast preview failed to restore volume decision fields: " + item.identity);
            }
            for (var i = 0; i < state.probes.Length; i++)
            {
                var item = state.probes[i];
                if (item.probe == null) continue;
                if (item.probe.enabled != item.enabled || item.probe.mode != item.mode || item.probe.refreshMode != item.refreshMode ||
                    item.probe.timeSlicingMode != item.timeSlicingMode || item.probe.resolution != item.resolution ||
                    item.probe.hdr != item.hdr || item.probe.boxProjection != item.boxProjection ||
                    !Mathf.Approximately(item.probe.intensity, item.intensity))
                    throw new InvalidOperationException("Fast preview failed to restore reflection decision fields: " + item.identity);
            }
            if (state.scene != null && state.scene.isDirty != state.sceneDirty) throw new InvalidOperationException("Fast preview changed scene dirty state.");
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode && state != PlayModeStateChange.ExitingEditMode) return;
            RestoreIfActive();
        }

        private static string Identity(Transform transform)
        {
            var parts = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                parts.Push(current.name + "[" + current.GetSiblingIndex().ToString() + "]");
            return string.Join("/", parts.ToArray());
        }

        private static void ClearSceneDirtiness(Scene scene)
        {
            if (!scene.IsValid()) return;
            var method = typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness", BindingFlags.Public | BindingFlags.Static);
            if (method != null) method.Invoke(null, new object[] { scene });
        }

        private sealed class MovementLabFastModeSaveGuard : AssetModificationProcessor
        {
            internal static string[] OnWillSaveAssets(string[] paths)
            {
                RestoreIfActive();
                return paths;
            }
        }

        private sealed class Snapshot
        {
            internal Scene scene;
            internal bool sceneDirty;
            internal int qualityIndex;
            internal AmbientMode ambientMode;
            internal Color ambientSky;
            internal Color ambientEquator;
            internal Color ambientGround;
            internal float ambientIntensity;
            internal Light sun;
            internal DefaultReflectionMode defaultReflectionMode;
            internal int defaultReflectionResolution;
            internal int reflectionBounces;
            internal float reflectionIntensity;
            internal RendererSnapshot[] renderers;
            internal LightSnapshot[] lights;
            internal VolumeSnapshot[] volumes;
            internal ReflectionSnapshot[] probes;
        }

        private sealed class RendererSnapshot
        {
            internal string identity;
            internal Renderer renderer;
            internal int lightmapIndex;
            internal int realtimeLightmapIndex;
            internal Vector4 lightmapScaleOffset;
            internal Vector4 realtimeLightmapScaleOffset;
        }

        private sealed class LightSnapshot
        {
            internal string identity;
            internal Light light;
            internal LightmapBakeType bakeType;
            internal LightShadows shadows;
            internal float intensity;
            internal float shadowStrength;
            internal float shadowBias;
            internal float shadowNormalBias;
        }

        private sealed class VolumeSnapshot
        {
            internal string identity;
            internal Volume volume;
            internal bool enabled;
            internal float weight;
            internal float priority;
            internal VolumeProfile sharedProfile;
        }

        private sealed class ReflectionSnapshot
        {
            internal string identity;
            internal ReflectionProbe probe;
            internal bool enabled;
            internal ReflectionProbeMode mode;
            internal ReflectionProbeRefreshMode refreshMode;
            internal ReflectionProbeTimeSlicingMode timeSlicingMode;
            internal int resolution;
            internal bool hdr;
            internal bool boxProjection;
            internal float intensity;
        }
    }
}
#endif
