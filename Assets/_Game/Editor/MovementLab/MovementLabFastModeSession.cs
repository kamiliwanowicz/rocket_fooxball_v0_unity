#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
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
        private const int FastReflectionBounces = 1;
        private static readonly Color FastAmbientSky = MovementLabLightingPipeline.ProductionAmbientSkyColor;
        private static readonly Color FastAmbientEquator = MovementLabLightingPipeline.ProductionAmbientEquatorColor;
        private static readonly Color FastAmbientGround = MovementLabLightingPipeline.ProductionAmbientGroundColor;
        private static Snapshot snapshot;
        private static bool applying;
        private static bool directionalHardShadows;

        static MovementLabFastModeSession()
        {
            EditorApplication.quitting += RestoreIfActive;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreIfActive;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
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
            EnterInternal(FastQualityIndex, requireIteration: true);
        }

        internal static void EnterForCapture(int qualityIndex)
        {
            if (qualityIndex != GraphicsQualityConfigurator.HighQualityIndex && qualityIndex != GraphicsQualityConfigurator.LowQualityIndex)
                throw new InvalidOperationException("Fast capture requires High or Low quality index: " + qualityIndex);
            EnterInternal(qualityIndex, requireIteration: false);
        }

        private static void EnterInternal(int qualityIndex, bool requireIteration)
        {
            if (IsActive) return;
            if (requireIteration && qualityIndex != FastQualityIndex)
                throw new InvalidOperationException("Interactive fast preview requires the Iteration quality profile.");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !string.Equals(scene.path, MovementLabContract.ScenePath, StringComparison.Ordinal))
                throw new InvalidOperationException("Fast preview requires the loaded MovementLab scene: " + MovementLabContract.ScenePath);
            snapshot = Capture(scene, qualityIndex);
            try
            {
                applying = true;
                Apply(snapshot);
                AssertApplied(snapshot);
                Debug.Log("Rocket Fooxball fast preview entered: quality=" + qualityIndex + "; renderer lightmap bindings detached.");
            }
            catch
            {
                applying = false;
                try { RestoreIfActive(); } catch { /* retain snapshot; lifecycle/save remains fail-closed */ }
                throw;
            }
            finally { applying = false; }
        }

        internal static void RestoreIfActive()
        {
            if (snapshot == null || applying) return;
            var current = snapshot;
            try
            {
                applying = true;
                EnsurePreviewStateIntact(current);
                RestoreSnapshot(current);
                AssertRestored(current);
                AssertPersistedAssetHashesUnchanged(current);
                snapshot = null;
            }
            finally
            {
                applying = false;
            }
        }

        internal static void AssertAppliedState()
        {
            if (snapshot == null) throw new InvalidOperationException("Fast preview is not active.");
            AssertApplied(snapshot);
        }

        internal static void AssertAppliedState(int expectedQualityIndex)
        {
            if (snapshot == null) throw new InvalidOperationException("Fast preview is not active.");
            if (snapshot.appliedQualityIndex != expectedQualityIndex)
                throw new InvalidOperationException("Fast capture quality contract mismatch: expected " + expectedQualityIndex + ".");
            AssertApplied(snapshot);
        }

        private static Snapshot Capture(Scene scene, int appliedQualityIndex)
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

            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(camera => camera != null && camera.gameObject.scene == scene)
                .OrderBy(camera => Identity(camera.transform), StringComparer.Ordinal)
                .Select(camera =>
                {
                    var additional = camera.GetComponent<UniversalAdditionalCameraData>();
                    return new CameraSnapshot
                    {
                        identity = Identity(camera.transform), camera = camera, allowHdr = camera.allowHDR,
                        additional = additional, renderPostProcessing = additional != null && additional.renderPostProcessing
                    };
                }).ToArray();

            var rendererFeatures = CaptureFastRendererFeatures();
            var sceneObjects = CaptureSceneObjects(scene);
            var persistedAssetHashes = CapturePersistedAssetHashes();
            var persistedAssetDirty = CapturePersistedAssetDirtyState();
            var persistedAssetDigests = CapturePersistedAssetDigests();

            var sceneDirty = scene.isDirty;
            var defaultReflectionMode = RenderSettings.defaultReflectionMode;
            Cubemap customReflection = null;
            if (defaultReflectionMode == DefaultReflectionMode.Custom)
            {
                customReflection = RenderSettings.customReflection;
                if (customReflection == null)
                    throw new InvalidOperationException("Fast preview requires a valid custom reflection cubemap when default reflection mode is Custom.");
            }
            return new Snapshot
            {
                scene = scene,
                sceneDirty = sceneDirty,
                qualityIndex = QualitySettings.GetQualityLevel(),
                appliedQualityIndex = appliedQualityIndex,
                ambientMode = RenderSettings.ambientMode,
                ambientSky = RenderSettings.ambientSkyColor,
                ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor,
                ambientIntensity = RenderSettings.ambientIntensity,
                sun = RenderSettings.sun,
                skybox = RenderSettings.skybox,
                fog = RenderSettings.fog,
                fogColor = RenderSettings.fogColor,
                fogMode = RenderSettings.fogMode,
                fogStartDistance = RenderSettings.fogStartDistance,
                fogEndDistance = RenderSettings.fogEndDistance,
                fogDensity = RenderSettings.fogDensity,
                customReflection = customReflection,
                subtractiveShadowColor = RenderSettings.subtractiveShadowColor,
                haloStrength = RenderSettings.haloStrength,
                flareStrength = RenderSettings.flareStrength,
                flareFadeSpeed = RenderSettings.flareFadeSpeed,
                defaultReflectionMode = defaultReflectionMode,
                defaultReflectionResolution = RenderSettings.defaultReflectionResolution,
                reflectionBounces = RenderSettings.reflectionBounces,
                reflectionIntensity = RenderSettings.reflectionIntensity,
                renderers = renderers,
                lights = lights,
                volumes = volumes,
                probes = probes,
                cameras = cameras,
                rendererFeatures = rendererFeatures,
                sceneObjects = sceneObjects,
                persistedAssetHashes = persistedAssetHashes,
                persistedAssetDirty = persistedAssetDirty,
                persistedAssetDigests = persistedAssetDigests
            };
        }

        private static Dictionary<string, string> CapturePersistedAssetHashes()
        {
            var paths = new[]
            {
                GraphicsQualityConfigurator.HighPipelinePath, GraphicsQualityConfigurator.HighRendererPath,
                GraphicsQualityConfigurator.LowPipelinePath, GraphicsQualityConfigurator.LowRendererPath,
                GraphicsQualityConfigurator.IterationPipelinePath, GraphicsQualityConfigurator.IterationRendererPath,
                GraphicsQualityConfigurator.QualitySettingsPath, GraphicsQualityConfigurator.ProjectSettingsPath
            };
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < paths.Length; i++)
            {
                var absolute = MovementLabManifestStore.ResolveProjectPath(paths[i]);
                result[paths[i]] = File.Exists(absolute) ? HashFile(absolute) : "missing";
            }
            return result;
        }

        private static void AssertPersistedAssetHashesUnchanged(Snapshot state)
        {
            if (state.persistedAssetHashes == null) throw new InvalidOperationException("Fast preview persisted asset snapshot is unavailable.");
            var current = CapturePersistedAssetHashes();
            foreach (var pair in state.persistedAssetHashes)
            {
                if (!current.TryGetValue(pair.Key, out var value) || !string.Equals(value, pair.Value, StringComparison.Ordinal))
                    throw new InvalidOperationException("Fast preview persisted asset changed during preview: " + pair.Key);
            }
        }

        private static Dictionary<string, bool> CapturePersistedAssetDirtyState()
        {
            var result = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var path in CapturePersistedAssetHashes().Keys)
            {
                var target = AssetDatabase.LoadMainAssetAtPath(path);
                if (target != null) result[path] = EditorUtility.IsDirty(target);
            }
            return result;
        }

        private static void RestorePersistedAssetDirtyState(Snapshot state)
        {
            if (state.persistedAssetDirty == null) return;
            foreach (var pair in state.persistedAssetDirty)
            {
                var target = AssetDatabase.LoadMainAssetAtPath(pair.Key);
                if (target == null) continue;
                if (state.persistedAssetDigests == null || !state.persistedAssetDigests.TryGetValue(pair.Key, out var beforeDigest) ||
                    !DigestMatches(beforeDigest, target)) continue;
                var absolute = MovementLabManifestStore.ResolveProjectPath(pair.Key);
                var unchangedOnDisk = File.Exists(absolute) && state.persistedAssetHashes != null &&
                    state.persistedAssetHashes.TryGetValue(pair.Key, out var beforeHash) &&
                    string.Equals(HashFile(absolute), beforeHash, StringComparison.Ordinal);
                if (!unchangedOnDisk) continue;
                if (pair.Value) EditorUtility.SetDirty(target); else EditorUtility.ClearDirty(target);
            }
        }

        private static Dictionary<string, string> CapturePersistedAssetDigests()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in CapturePersistedAssetHashes().Keys)
            {
                var target = AssetDatabase.LoadMainAssetAtPath(path);
                if (target != null) result[path] = SerializedDigest(target);
            }
            return result;
        }

        private static string HashFile(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = System.IO.File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static RendererFeatureSnapshot[] CaptureFastRendererFeatures()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(GraphicsQualityConfigurator.IterationRendererPath);
            if (renderer == null || renderer.rendererFeatures == null) return Array.Empty<RendererFeatureSnapshot>();
            return renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>()
                .Select(feature =>
                {
                    if (feature == null) return null;
                    var digest = SerializedDigest(feature);
                    if (string.IsNullOrEmpty(digest))
                        throw new InvalidOperationException("Fast preview cannot prove SSAO renderer-feature serialization.");
                    return new RendererFeatureSnapshot
                    {
                        identity = feature.name,
                        feature = feature,
                        active = feature.isActive,
                        dirty = EditorUtility.IsDirty(feature),
                        digest = digest
                    };
                })
                .Where(feature => feature != null)
                .ToArray();
        }

        private static SceneObjectSnapshot[] CaptureSceneObjects(Scene scene)
        {
            var result = new List<SceneObjectSnapshot>();
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                AddSceneObjectSnapshot(result, root);
                var components = root.GetComponentsInChildren<Component>(true);
                for (var j = 0; j < components.Length; j++) if (components[j] != null) AddSceneObjectSnapshot(result, components[j]);
                var children = root.GetComponentsInChildren<Transform>(true);
                for (var j = 0; j < children.Length; j++) if (children[j] != null) AddSceneObjectSnapshot(result, children[j].gameObject);
            }
            return result.OrderBy(item => item.identity, StringComparer.Ordinal).ToArray();
        }

        private static void AddSceneObjectSnapshot(List<SceneObjectSnapshot> result, UnityEngine.Object target)
        {
            if (target == null) return;
            var identity = target is Component component ? Identity(component.transform) + ":" + target.GetType().FullName :
                target is GameObject gameObject ? Identity(gameObject.transform) + ":GameObject" : target.GetType().FullName;
            if (result.Any(item => item.target == target)) return;
            var digest = SerializedDigest(target);
            if (string.IsNullOrEmpty(digest))
                throw new InvalidOperationException("Fast preview cannot prove scene-object serialization: " + identity);
            result.Add(new SceneObjectSnapshot
            {
                identity = identity,
                target = target,
                dirty = EditorUtility.IsDirty(target),
                digest = digest
            });
        }

        private static string SerializedDigest(UnityEngine.Object target)
        {
            if (target == null) return null;
            try
            {
                var json = EditorJsonUtility.ToJson(target);
                return string.IsNullOrEmpty(json) ? null : json;
            }
            catch { return null; }
        }

        private static bool DigestMatches(string expected, UnityEngine.Object target)
        {
            if (string.IsNullOrEmpty(expected)) return false;
            var actual = SerializedDigest(target);
            return !string.IsNullOrEmpty(actual) && string.Equals(actual, expected, StringComparison.Ordinal);
        }

        private static void Apply(Snapshot state)
        {
            QualitySettings.SetQualityLevel(state.appliedQualityIndex, true);
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
            RenderSettings.ambientIntensity = MovementLabLightingPipeline.FastAmbientIntensity;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 64;
            RenderSettings.reflectionBounces = FastReflectionBounces;
            RenderSettings.reflectionIntensity = 1f;

            if (state.sun != null)
            {
                state.sun.lightmapBakeType = LightmapBakeType.Realtime;
                state.sun.shadows = directionalHardShadows ? LightShadows.Hard : LightShadows.None;
            }
            for (var i = 0; i < state.volumes.Length; i++) if (state.volumes[i].volume != null) state.volumes[i].volume.enabled = false;
            for (var i = 0; i < state.probes.Length; i++) if (state.probes[i].probe != null) state.probes[i].probe.enabled = false;
            for (var i = 0; i < state.cameras.Length; i++)
            {
                var item = state.cameras[i];
                if (item.camera != null) item.camera.allowHDR = false;
                if (item.additional != null) item.additional.renderPostProcessing = false;
            }
            for (var i = 0; i < state.rendererFeatures.Length; i++)
            {
                var item = state.rendererFeatures[i];
                if (item.feature != null) item.feature.SetActive(false);
            }
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
            for (var i = 0; i < state.cameras.Length; i++)
            {
                var item = state.cameras[i];
                if (item.camera != null) item.camera.allowHDR = item.allowHdr;
                if (item.additional != null) item.additional.renderPostProcessing = item.renderPostProcessing;
            }
            for (var i = 0; i < state.rendererFeatures.Length; i++)
            {
                var item = state.rendererFeatures[i];
                if (item.feature == null) continue;
                item.feature.SetActive(item.active);
            }
            RenderSettings.ambientMode = state.ambientMode;
            RenderSettings.ambientSkyColor = state.ambientSky;
            RenderSettings.ambientEquatorColor = state.ambientEquator;
            RenderSettings.ambientGroundColor = state.ambientGround;
            RenderSettings.ambientIntensity = state.ambientIntensity;
            RenderSettings.sun = state.sun;
            RenderSettings.skybox = state.skybox;
            RenderSettings.fog = state.fog;
            RenderSettings.fogColor = state.fogColor;
            RenderSettings.fogMode = state.fogMode;
            RenderSettings.fogStartDistance = state.fogStartDistance;
            RenderSettings.fogEndDistance = state.fogEndDistance;
            RenderSettings.fogDensity = state.fogDensity;
            if (state.defaultReflectionMode == DefaultReflectionMode.Custom) RenderSettings.customReflection = state.customReflection;
            RenderSettings.subtractiveShadowColor = state.subtractiveShadowColor;
            RenderSettings.haloStrength = state.haloStrength;
            RenderSettings.flareStrength = state.flareStrength;
            RenderSettings.flareFadeSpeed = state.flareFadeSpeed;
            RenderSettings.defaultReflectionMode = state.defaultReflectionMode;
            RenderSettings.defaultReflectionResolution = state.defaultReflectionResolution;
            RenderSettings.reflectionBounces = state.reflectionBounces;
            RenderSettings.reflectionIntensity = state.reflectionIntensity;
            QualitySettings.SetQualityLevel(state.qualityIndex, true);
            RestorePersistedAssetDirtyState(state);
            RestoreSceneDirtyState(state);
        }

        private static void RestoreSceneDirtyState(Snapshot state)
        {
            var exact = true;
            for (var i = 0; i < state.sceneObjects.Length; i++)
            {
                var item = state.sceneObjects[i];
                if (item.target == null) { exact = false; continue; }
                var digestMatches = DigestMatches(item.digest, item.target);
                if (!digestMatches) exact = false;
            }
            for (var i = 0; i < state.rendererFeatures.Length; i++)
            {
                var item = state.rendererFeatures[i];
                if (item.feature == null) { exact = false; continue; }
                var digestMatches = DigestMatches(item.digest, item.feature);
                if (!digestMatches) { exact = false; continue; }
            }

            // Do not partially clear dirty flags.  Any digest mismatch means a
            // user/editor edit is not provably preview-only, so leave all dirty
            // state untouched and fail closed in AssertRestored below.
            if (!exact) return;
            for (var i = 0; i < state.sceneObjects.Length; i++)
            {
                var item = state.sceneObjects[i];
                if (item.dirty) EditorUtility.SetDirty(item.target); else EditorUtility.ClearDirty(item.target);
            }
            for (var i = 0; i < state.rendererFeatures.Length; i++)
            {
                var item = state.rendererFeatures[i];
                if (item.dirty) EditorUtility.SetDirty(item.feature); else EditorUtility.ClearDirty(item.feature);
            }

            // Never clear a user's dirty scene.  A clean baseline may be reset
            // only when every captured object returned byte-for-byte exactly.
            if (!state.sceneDirty && exact) ClearSceneDirtiness(state.scene);
        }

        private static void EnsurePreviewStateIntact(Snapshot state)
        {
            if (state == null || !state.scene.IsValid() || !string.Equals(state.scene.path, MovementLabContract.ScenePath, StringComparison.Ordinal))
                throw new InvalidOperationException("Fast preview restoration refused: MovementLab scene is no longer loaded.");
            var currentObjects = CaptureSceneObjects(state.scene);
            if (currentObjects.Length != state.sceneObjects.Length || currentObjects.Any(item => state.sceneObjects.All(old => old.target != item.target)))
                throw new InvalidOperationException("Fast preview restoration refused: scene hierarchy changed during preview.");
            if (QualitySettings.GetQualityLevel() != state.appliedQualityIndex)
                throw new InvalidOperationException("Fast preview restoration refused: quality changed while preview was active.");
            if (state.renderers.Any(item => item.renderer != null &&
                (item.renderer.lightmapIndex != -1 || item.renderer.realtimeLightmapIndex != -1 ||
                 item.renderer.lightmapScaleOffset != item.lightmapScaleOffset ||
                 item.renderer.realtimeLightmapScaleOffset != item.realtimeLightmapScaleOffset)))
                throw new InvalidOperationException("Fast preview restoration refused: renderer lightmap binding was changed during preview.");
            if (state.lights.Any(item => item.light != null &&
                (item.light.lightmapBakeType != (item.light == state.sun ? LightmapBakeType.Realtime : item.bakeType) ||
                 item.light.shadows != (item.light == state.sun ? (directionalHardShadows ? LightShadows.Hard : LightShadows.None) : item.shadows) ||
                 !Mathf.Approximately(item.light.intensity, item.intensity) ||
                 !Mathf.Approximately(item.light.shadowStrength, item.shadowStrength) ||
                 !Mathf.Approximately(item.light.shadowBias, item.shadowBias) ||
                 !Mathf.Approximately(item.light.shadowNormalBias, item.shadowNormalBias))))
                throw new InvalidOperationException("Fast preview restoration refused: light preview state was changed during preview.");
            if (state.volumes.Any(item => item.volume != null &&
                (item.volume.enabled || !Mathf.Approximately(item.volume.weight, item.weight) ||
                 !Mathf.Approximately(item.volume.priority, item.priority) || item.volume.sharedProfile != item.sharedProfile)) ||
                state.probes.Any(item => item.probe != null &&
                (item.probe.enabled || item.probe.mode != item.mode || item.probe.refreshMode != item.refreshMode ||
                 item.probe.timeSlicingMode != item.timeSlicingMode || item.probe.resolution != item.resolution ||
                 item.probe.hdr != item.hdr || item.probe.boxProjection != item.boxProjection ||
                 !Mathf.Approximately(item.probe.intensity, item.intensity))))
                throw new InvalidOperationException("Fast preview restoration refused: post/reflection preview state was changed during preview.");
            if (state.cameras.Any(item => item.camera != null && item.camera.allowHDR) || state.cameras.Any(item => item.additional != null && item.additional.renderPostProcessing))
                throw new InvalidOperationException("Fast preview restoration refused: camera HDR/post state was changed during preview.");
            if (state.rendererFeatures.Any(item => item.feature != null && item.feature.isActive))
                throw new InvalidOperationException("Fast preview restoration refused: SSAO feature was changed during preview.");
            if (RenderSettings.sun != state.sun || RenderSettings.ambientMode != AmbientMode.Trilight ||
                RenderSettings.ambientSkyColor != FastAmbientSky || RenderSettings.ambientEquatorColor != FastAmbientEquator ||
                 RenderSettings.ambientGroundColor != FastAmbientGround || Mathf.Abs(RenderSettings.ambientIntensity - MovementLabLightingPipeline.FastAmbientIntensity) > 0.0001f ||
                RenderSettings.defaultReflectionMode != DefaultReflectionMode.Skybox || RenderSettings.defaultReflectionResolution != 64 ||
                RenderSettings.reflectionBounces != FastReflectionBounces || Mathf.Abs(RenderSettings.reflectionIntensity - 1f) > 0.0001f)
                throw new InvalidOperationException("Fast preview restoration refused: RenderSettings changed during preview.");
            if (RenderSettings.skybox != state.skybox || RenderSettings.fog != state.fog || RenderSettings.fogColor != state.fogColor ||
                RenderSettings.fogMode != state.fogMode || !Mathf.Approximately(RenderSettings.fogStartDistance, state.fogStartDistance) ||
                !Mathf.Approximately(RenderSettings.fogEndDistance, state.fogEndDistance) || !Mathf.Approximately(RenderSettings.fogDensity, state.fogDensity) ||
                (state.defaultReflectionMode == DefaultReflectionMode.Custom && RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom &&
                 RenderSettings.customReflection != state.customReflection) || RenderSettings.subtractiveShadowColor != state.subtractiveShadowColor ||
                !Mathf.Approximately(RenderSettings.haloStrength, state.haloStrength) || !Mathf.Approximately(RenderSettings.flareStrength, state.flareStrength) ||
                !Mathf.Approximately(RenderSettings.flareFadeSpeed, state.flareFadeSpeed))
                throw new InvalidOperationException("Fast preview restoration refused: unrelated RenderSettings changed during preview.");
        }

        private static void AssertApplied(Snapshot state)
        {
            if (QualitySettings.GetQualityLevel() != state.appliedQualityIndex) throw new InvalidOperationException("Fast preview quality profile was not applied.");
            if (state.renderers.Any(item => item.renderer != null && (item.renderer.lightmapIndex != -1 || item.renderer.realtimeLightmapIndex != -1)))
                throw new InvalidOperationException("Fast preview failed to detach a renderer lightmap binding.");
            if (RenderSettings.ambientMode != AmbientMode.Trilight || RenderSettings.ambientSkyColor != FastAmbientSky ||
                RenderSettings.ambientEquatorColor != FastAmbientEquator || RenderSettings.ambientGroundColor != FastAmbientGround ||
                Mathf.Abs(RenderSettings.ambientIntensity - MovementLabLightingPipeline.FastAmbientIntensity) > 0.0001f)
                throw new InvalidOperationException("Fast preview ambient contract was not applied.");
            if (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Skybox || RenderSettings.defaultReflectionResolution != 64 ||
                RenderSettings.reflectionBounces != FastReflectionBounces || Mathf.Abs(RenderSettings.reflectionIntensity - 1f) > 0.0001f)
                throw new InvalidOperationException("Fast preview reflection contract was not applied.");
            if (state.sun != null && state.sun.shadows != (directionalHardShadows ? LightShadows.Hard : LightShadows.None))
                throw new InvalidOperationException("Fast preview directional shadow contract was not applied.");
            if (state.volumes.Any(item => item.volume != null && item.volume.enabled)) throw new InvalidOperationException("Fast preview post volume remained enabled.");
            if (state.probes.Any(item => item.probe != null && item.probe.enabled)) throw new InvalidOperationException("Fast preview reflection probe remained enabled.");
            if (state.cameras.Any(item => item.camera != null && item.camera.allowHDR) || state.cameras.Any(item => item.additional != null && item.additional.renderPostProcessing))
                throw new InvalidOperationException("Fast preview HDR/post processing remained enabled.");
            if (state.rendererFeatures.Any(item => item.feature != null && item.feature.isActive)) throw new InvalidOperationException("Fast preview SSAO remained enabled.");
        }

        private static void AssertRestored(Snapshot state)
        {
            if (QualitySettings.GetQualityLevel() != state.qualityIndex)
                throw new InvalidOperationException("Fast preview failed to restore quality/default pipeline state.");
            if (RenderSettings.ambientMode != state.ambientMode || RenderSettings.ambientSkyColor != state.ambientSky ||
                RenderSettings.ambientEquatorColor != state.ambientEquator || RenderSettings.ambientGroundColor != state.ambientGround ||
                Mathf.Abs(RenderSettings.ambientIntensity - state.ambientIntensity) > 0.0001f || RenderSettings.sun != state.sun ||
                RenderSettings.skybox != state.skybox || RenderSettings.fog != state.fog || RenderSettings.fogColor != state.fogColor ||
                RenderSettings.fogMode != state.fogMode || !Mathf.Approximately(RenderSettings.fogStartDistance, state.fogStartDistance) ||
                !Mathf.Approximately(RenderSettings.fogEndDistance, state.fogEndDistance) || !Mathf.Approximately(RenderSettings.fogDensity, state.fogDensity) ||
                (state.defaultReflectionMode == DefaultReflectionMode.Custom && RenderSettings.customReflection != state.customReflection) || RenderSettings.subtractiveShadowColor != state.subtractiveShadowColor ||
                !Mathf.Approximately(RenderSettings.haloStrength, state.haloStrength) || !Mathf.Approximately(RenderSettings.flareStrength, state.flareStrength) ||
                !Mathf.Approximately(RenderSettings.flareFadeSpeed, state.flareFadeSpeed) ||
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
            for (var i = 0; i < state.cameras.Length; i++)
            {
                var item = state.cameras[i];
                if (item.camera != null && item.camera.allowHDR != item.allowHdr) throw new InvalidOperationException("Fast preview failed to restore camera HDR state: " + item.identity);
                if (item.additional != null && item.additional.renderPostProcessing != item.renderPostProcessing) throw new InvalidOperationException("Fast preview failed to restore camera post state: " + item.identity);
            }
            for (var i = 0; i < state.rendererFeatures.Length; i++)
            {
                var item = state.rendererFeatures[i];
                if (item.feature == null || item.feature.isActive != item.active || !DigestMatches(item.digest, item.feature))
                    throw new InvalidOperationException("Fast preview failed to restore renderer feature state: " + item.identity);
            }
            for (var i = 0; i < state.sceneObjects.Length; i++)
            {
                var item = state.sceneObjects[i];
                if (item.target == null || !DigestMatches(item.digest, item.target))
                    throw new InvalidOperationException("Fast preview failed to restore scene object state: " + item.identity);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode && state != PlayModeStateChange.ExitingEditMode) return;
            RestoreIfActive();
        }

        private static void OnSceneClosing(Scene scene, bool removingScene)
        {
            if (snapshot != null && snapshot.scene == scene) RestoreIfActive();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            if (snapshot != null && snapshot.scene == scene) RestoreIfActive();
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
            var method = typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method != null) method.Invoke(null, new object[] { scene });
        }

        private sealed class MovementLabFastModeSaveGuard : AssetModificationProcessor
        {
            internal static string[] OnWillSaveAssets(string[] paths)
            {
                try
                {
                    RestoreIfActive();
                    return paths;
                }
                catch (Exception exception)
                {
                    // Never permit an asset save while a preview snapshot
                    // cannot be proven restored. Keep the snapshot for
                    // diagnostics/retry and fail closed at the save boundary.
                    Debug.LogError("Rocket Fooxball fast preview blocked save: " + exception.Message);
                    return Array.Empty<string>();
                }
            }
        }

        private sealed class Snapshot
        {
            internal Scene scene;
            internal bool sceneDirty;
            internal int qualityIndex;
            internal int appliedQualityIndex;
            internal AmbientMode ambientMode;
            internal Color ambientSky;
            internal Color ambientEquator;
            internal Color ambientGround;
            internal float ambientIntensity;
            internal Light sun;
            internal Material skybox;
            internal bool fog;
            internal Color fogColor;
            internal FogMode fogMode;
            internal float fogStartDistance;
            internal float fogEndDistance;
            internal float fogDensity;
            internal Cubemap customReflection;
            internal Color subtractiveShadowColor;
            internal float haloStrength;
            internal float flareStrength;
            internal float flareFadeSpeed;
            internal DefaultReflectionMode defaultReflectionMode;
            internal int defaultReflectionResolution;
            internal int reflectionBounces;
            internal float reflectionIntensity;
            internal RendererSnapshot[] renderers;
            internal LightSnapshot[] lights;
            internal VolumeSnapshot[] volumes;
            internal ReflectionSnapshot[] probes;
            internal CameraSnapshot[] cameras;
            internal RendererFeatureSnapshot[] rendererFeatures;
            internal SceneObjectSnapshot[] sceneObjects;
            internal Dictionary<string, string> persistedAssetHashes;
            internal Dictionary<string, bool> persistedAssetDirty;
            internal Dictionary<string, string> persistedAssetDigests;
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

        private sealed class CameraSnapshot
        {
            internal string identity;
            internal Camera camera;
            internal UniversalAdditionalCameraData additional;
            internal bool allowHdr;
            internal bool renderPostProcessing;
        }

        private sealed class RendererFeatureSnapshot
        {
            internal string identity;
            internal ScreenSpaceAmbientOcclusion feature;
            internal bool active;
            internal bool dirty;
            internal string digest;
        }

        private sealed class SceneObjectSnapshot
        {
            internal string identity;
            internal UnityEngine.Object target;
            internal bool dirty;
            internal string digest;
        }
    }
}
#endif
