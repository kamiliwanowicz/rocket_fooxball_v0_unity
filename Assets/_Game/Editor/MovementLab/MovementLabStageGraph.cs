using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RocketFooxball.Editor
{
    internal enum MovementLabStage
    {
        Importer,
        MaterialPrefab,
        GameplayScene,
        Quality,
        Lighting,
        BakedOutput
    }

    internal sealed class MovementLabStageProbe
    {
        private readonly HashSet<MovementLabStage> staleStages;

        internal MovementLabStageProbe(IEnumerable<MovementLabStage> staleStages, string lightingInputDigest)
        {
            this.staleStages = new HashSet<MovementLabStage>(staleStages);
            LightingInputDigest = lightingInputDigest;
        }

        internal string LightingInputDigest { get; }
        internal bool IsStale(MovementLabStage stage) => staleStages.Contains(stage);
        internal MovementLabStage[] StaleStages => staleStages.OrderBy(stage => (int)stage).ToArray();
    }

    internal static class MovementLabStageGraph
    {
        private const string ImporterContract = "importer-contract:1";
        private const string MaterialContract = "material-prefab-contract:1";
        private const string GameplayContract = "gameplay-scene-contract:2";
        private const string QualityContract = "quality-contract:1";
        private const string LightingContract = "lighting-contract:1";
        private const string BakedContract = "baked-output-contract:2";

        private static readonly StageDefinition[] Definitions =
        {
            new StageDefinition(MovementLabStage.Importer, Array.Empty<MovementLabStage>(), ImporterContract,
                MovementLabContract.ImporterContractInputs, MovementLabContract.ImportedAssetPaths,
                Array.Empty<string>(), MovementLabContract.ImporterContractInputs, true),
            new StageDefinition(MovementLabStage.MaterialPrefab, new[] { MovementLabStage.Importer },
                MaterialContract + ";serialized:" + MovementLabContract.SerializedContractVersion,
                WithMetas(new[]
                {
                    MovementLabContract.InputActionsPath,
                    MovementLabContract.ShadersPath + "/RetroToonLit.shader", MovementLabContract.ShadersPath + "/RetroParticle.shader",
                    MovementLabContract.ShadersPath + "/RetroAdditiveParticle.shader", MovementLabContract.ShadersPath + "/RetroPowerGrid.shader",
                    MovementLabContract.ShadersPath + "/RetroShield.shader", MovementLabContract.ShadersPath + "/SunnyArenaSky.shader"
                }), MovementLabContract.ImportedAssetPaths, Array.Empty<string>(), WithMetas(MovementLabContract.MaterialPrefabOutputs), false),
            new StageDefinition(MovementLabStage.GameplayScene, new[] { MovementLabStage.MaterialPrefab },
                GameplayContract + ";serialized:" + MovementLabContract.SerializedContractVersion,
                WithMetas(new[]
                {
                    MovementLabContract.PlayerPrefabPath, MovementLabContract.BallPrefabPath,
                    MovementLabContract.RocketPrefabPath, MovementLabContract.ExplosionPrefabPath,
                    MovementLabContract.InputActionsPath
                }), Array.Empty<string>(), Array.Empty<string>(), WithAssetMetasOnly(MovementLabContract.GameplaySceneOutputs), false),
            new StageDefinition(MovementLabStage.Quality, Array.Empty<MovementLabStage>(), QualityContract,
                new[]
                {
                    "Packages/manifest.json", "Packages/packages-lock.json",
                    "Assets/_Game/Editor/GraphicsQualityConfigurator.cs"
                }, Array.Empty<string>(),
                Array.Empty<string>(), WithAssetMetasOnly(MovementLabContract.QualityOutputs), false),
            new StageDefinition(MovementLabStage.Lighting,
                new[] { MovementLabStage.MaterialPrefab, MovementLabStage.GameplayScene, MovementLabStage.Quality }, LightingContract,
                new[]
                {
                    MovementLabContract.LightingSettingsPath, MovementLabContract.LightingSettingsPath + ".meta",
                    MovementLabContract.VolumeProfilePath, MovementLabContract.VolumeProfilePath + ".meta",
                    "Assets/_Game/Editor/GraphicsQualityConfigurator.cs"
                }, Concat(MovementLabContract.MaterialPrefabOutputs, MovementLabContract.QualityOutputs),
                Array.Empty<string>(), Array.Empty<string>(), true),
            new StageDefinition(MovementLabStage.BakedOutput, new[] { MovementLabStage.Lighting }, BakedContract,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                WithMetas(MovementLabContract.BakedOutputPaths), false)
        };

        internal static MovementLabStageProbe Probe(bool stopOnOutputDrift, bool allowBakedOutputDrift = false)
        {
            var manifestRead = MovementLabManifestStore.Read();
            var manifest = manifestRead.State;
            var currentRecords = new Dictionary<MovementLabStage, MovementLabStageRecord>();
            var stale = new HashSet<MovementLabStage>();
            var schemaCurrent = manifestRead.Status == MovementLabManifestReadStatus.Current &&
                MovementLabManifestStore.IsCurrentAndReadable(manifest);

            for (var i = 0; i < Definitions.Length; i++)
            {
                var definition = Definitions[i];
                var predecessorDigests = GetPredecessorDigests(definition, currentRecords);
                var current = CaptureRecord(definition, predecessorDigests);
                currentRecords.Add(definition.Stage, current);
                var prior = schemaCurrent ? manifest.Find(definition.Stage.ToString()) : null;
                var predecessorStale = definition.Predecessors.Any(stale.Contains);
                var inputStale = prior == null || prior.schemaVersion != MovementLabContract.ManifestSchemaVersion ||
                    !string.Equals(prior.contractVersion, current.contractVersion, StringComparison.Ordinal) ||
                    !string.Equals(prior.unityVersion, current.unityVersion, StringComparison.Ordinal) ||
                    !string.Equals(prior.inputDigest, current.inputDigest, StringComparison.Ordinal) ||
                    !SequenceEqual(prior.predecessorDigests, current.predecessorDigests);

                // Output drift is fatal for trusted records, even when input/predecessor state is stale.
                // Compare before marking stale so writers cannot overwrite unreviewed changes.
                if (schemaCurrent && prior != null)
                {
                    var drift = FindOutputDrift(prior.outputs, current.outputs);
                    if (drift.Count > 0)
                    {
                        var ignoreDrift = allowBakedOutputDrift && definition.Stage == MovementLabStage.BakedOutput;
                        if (stopOnOutputDrift && !ignoreDrift)
                        {
                            throw new InvalidOperationException(FormatOutputDrift(definition.Stage, drift));
                        }
                        stale.Add(definition.Stage);
                    }
                }

                if (predecessorStale || inputStale)
                {
                    stale.Add(definition.Stage);
                    continue;
                }

            }

            return new MovementLabStageProbe(stale, currentRecords[MovementLabStage.Lighting].inputDigest);
        }

        internal static MovementLabGeneratedState CaptureAssembledState()
        {
            return CaptureState(MovementLabStage.Quality);
        }

        internal static MovementLabGeneratedState CaptureBakedState()
        {
            return CaptureState(MovementLabStage.BakedOutput);
        }

        private static MovementLabGeneratedState CaptureState(MovementLabStage lastStage)
        {
            var records = new List<MovementLabStageRecord>();
            var current = new Dictionary<MovementLabStage, MovementLabStageRecord>();
            for (var i = 0; i < Definitions.Length; i++)
            {
                var definition = Definitions[i];
                if ((int)definition.Stage > (int)lastStage) break;
                var record = CaptureRecord(definition, GetPredecessorDigests(definition, current));
                current.Add(definition.Stage, record);
                records.Add(record);
            }

            var paths = records.SelectMany(record => record.outputs ?? Array.Empty<MovementLabPathDigest>())
                .Select(output => output.path).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var sourceSignature = HashText(records.Select(record => record.stage + ":" + record.inputDigest));
            var outputFingerprint = HashText(records.Select(record => record.stage + ":" + record.outputDigest));
            return new MovementLabGeneratedState
            {
                schemaVersion = MovementLabContract.ManifestSchemaVersion,
                sourceSignature = sourceSignature,
                generatedOutputFingerprint = outputFingerprint,
                unityVersion = Application.unityVersion,
                fingerprintPaths = paths,
                stages = records.ToArray()
            };
        }

        private static MovementLabStageRecord CaptureRecord(StageDefinition definition, string[] predecessorDigests)
        {
            var inputParts = new List<string> { "contract:" + definition.ContractVersion };
            AddRepositoryDigests(inputParts, definition.RepositoryInputs);
            AddDependencyDigests(inputParts, definition.DependencyInputs);
            AddLiteralDigests(inputParts, definition.LiteralInputs);
            if (definition.Stage == MovementLabStage.Lighting) inputParts.AddRange(CaptureLightingSceneState());
            if (definition.IncludeUnityVersion) inputParts.Add("unity:" + Application.unityVersion);
            for (var i = 0; i < predecessorDigests.Length; i++) inputParts.Add("predecessor:" + predecessorDigests[i]);

            var outputs = CaptureOutputs(definition.Outputs, definition.Stage == MovementLabStage.Importer, definition.Stage);
            return new MovementLabStageRecord
            {
                stage = definition.Stage.ToString(),
                schemaVersion = MovementLabContract.ManifestSchemaVersion,
                contractVersion = definition.ContractVersion,
                unityVersion = Application.unityVersion,
                inputDigest = HashText(inputParts),
                outputDigest = HashText(outputs.Select(output => output.path + ":" + (output.missing ? "missing" : output.digest))),
                predecessorDigests = predecessorDigests,
                outputs = outputs
            };
        }

        private static MovementLabPathDigest[] CaptureOutputs(string[] paths, bool useDependencyHash, MovementLabStage stage)
        {
            var normalizedPaths = paths.Select(MovementLabManifestStore.NormalizeRepositoryPath)
                .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var result = new MovementLabPathDigest[normalizedPaths.Length];
            for (var i = 0; i < normalizedPaths.Length; i++)
            {
                var path = normalizedPaths[i];
                var absolute = MovementLabManifestStore.ResolveProjectPath(path);
                var missing = !File.Exists(absolute);
                result[i] = new MovementLabPathDigest
                {
                    path = path,
                    missing = missing,
                    digest = missing ? string.Empty : useDependencyHash && path.StartsWith("Assets/", StringComparison.Ordinal) && !path.EndsWith(".meta", StringComparison.Ordinal)
                        ? AssetDatabase.GetAssetDependencyHash(path).ToString()
                        : HashOutputFile(path, absolute, stage)
                };
            }
            return result;
        }

        private static string HashOutputFile(string repositoryPath, string absolutePath, MovementLabStage stage)
        {
            // Gameplay stage owns scene content except bake-owned LightmapSettings; BakedOutput owns raw post-bake scene.
            if (stage == MovementLabStage.GameplayScene && string.Equals(repositoryPath, MovementLabContract.ScenePath, StringComparison.Ordinal))
            {
                return HashGameplaySceneWithoutLightmapSettings(absolutePath);
            }

            return HashFile(absolutePath);
        }

        private static string HashGameplaySceneWithoutLightmapSettings(string path)
        {
            var normalized = NormalizeLineEndings(File.ReadAllText(path, Encoding.UTF8));
            var lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
            var retained = new StringBuilder(normalized.Length);
            var index = 0;
            while (index < lines.Length)
            {
                var next = index + 1;
                while (next < lines.Length && !IsYamlDocumentHeader(lines[next])) next++;
                if (!IsLightmapSettingsDocument(lines, index, next))
                {
                    for (var lineIndex = index; lineIndex < next; lineIndex++)
                    {
                        if (retained.Length > 0) retained.Append('\n');
                        retained.Append(lines[lineIndex]);
                    }
                }
                index = next;
            }

            return HashBytes(Encoding.UTF8.GetBytes(retained.ToString()));
        }

        private static bool IsLightmapSettingsDocument(string[] lines, int start, int end)
        {
            if (start >= end || !lines[start].StartsWith("--- !u!157 ", StringComparison.Ordinal)) return false;
            for (var index = start + 1; index < end; index++)
            {
                if (string.Equals(lines[index], "LightmapSettings:", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool IsYamlDocumentHeader(string line)
        {
            return line.StartsWith("--- !u!", StringComparison.Ordinal);
        }

        private static string NormalizeLineEndings(string value)
        {
            return value.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static void AddRepositoryDigests(List<string> parts, string[] paths)
        {
            var sorted = paths.Select(MovementLabManifestStore.NormalizeRepositoryPath).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal);
            foreach (var path in sorted)
            {
                var absolute = MovementLabManifestStore.ResolveProjectPath(path);
                parts.Add("file:" + path + ":" + (File.Exists(absolute) ? HashFile(absolute) : "missing"));
            }
        }

        private static void AddDependencyDigests(List<string> parts, string[] paths)
        {
            foreach (var path in paths.Select(MovementLabManifestStore.NormalizeRepositoryPath).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
            {
                var absolute = MovementLabManifestStore.ResolveProjectPath(path);
                var digest = File.Exists(absolute) ? AssetDatabase.GetAssetDependencyHash(path).ToString() : "missing";
                parts.Add("asset:" + path + ":" + digest);
            }
        }

        private static void AddLiteralDigests(List<string> parts, string[] values)
        {
            for (var i = 0; i < values.Length; i++) parts.Add("literal:" + values[i]);
        }

        private static IEnumerable<string> CaptureLightingSceneState()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MovementLabContract.ScenePath) == null)
            {
                return new[] { "lighting-scene:missing" };
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !string.Equals(scene.path, MovementLabContract.ScenePath, StringComparison.Ordinal))
            {
                scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            }

            var parts = new List<string>();
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(renderer => renderer != null && renderer.gameObject.scene == scene && IsLightingStatic(renderer.gameObject))
                .OrderBy(renderer => GetHierarchyPath(renderer.transform), StringComparer.Ordinal).ToArray();
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                parts.Add("renderer:" + GetHierarchyPath(renderer.transform) + ":" + TransformDigest(renderer.transform) +
                    ":static=" + (int)GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) +
                    ":mesh=" + AssetIdentity(mesh) +
                    ":materials=" + string.Join(",", renderer.sharedMaterials.Select(AssetIdentity)) +
                    ":shadow=" + renderer.shadowCastingMode + ":receive=" + renderer.receiveShadows +
                    ":lightProbe=" + renderer.lightProbeUsage + ":reflectionProbe=" + renderer.reflectionProbeUsage);
            }

            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .Where(light => light != null && light.gameObject.scene == scene)
                .OrderBy(light => GetHierarchyPath(light.transform), StringComparer.Ordinal).ToArray();
            for (var i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                parts.Add("light:" + GetHierarchyPath(light.transform) + ":" + TransformDigest(light.transform) +
                    ":type=" + light.type + ":color=" + ColorDigest(light.color) + ":intensity=" + F(light.intensity) +
                    ":range=" + F(light.range) + ":spot=" + F(light.spotAngle) + ":shadows=" + light.shadows +
                    ":bake=" + light.lightmapBakeType + ":culling=" + light.cullingMask);
            }

            var probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)
                .Where(probe => probe != null && probe.gameObject.scene == scene)
                .OrderBy(probe => GetHierarchyPath(probe.transform), StringComparer.Ordinal).ToArray();
            for (var i = 0; i < probes.Length; i++)
            {
                var probe = probes[i];
                parts.Add("reflection-probe:" + GetHierarchyPath(probe.transform) + ":" + TransformDigest(probe.transform) +
                    ":center=" + VectorDigest(probe.center) + ":size=" + VectorDigest(probe.size) +
                    ":resolution=" + probe.resolution + ":mode=" + probe.mode + ":importance=" + probe.importance +
                    ":box=" + probe.boxProjection + ":blend=" + F(probe.blendDistance) + ":culling=" + probe.cullingMask +
                    ":hdr=" + probe.hdr + ":intensity=" + F(probe.intensity) + ":near=" + F(probe.nearClipPlane) +
                    ":far=" + F(probe.farClipPlane));
            }

            var probeGroups = UnityEngine.Object.FindObjectsByType<LightProbeGroup>(FindObjectsSortMode.None)
                .Where(group => group != null && group.gameObject.scene == scene)
                .OrderBy(group => GetHierarchyPath(group.transform), StringComparer.Ordinal).ToArray();
            for (var i = 0; i < probeGroups.Length; i++)
            {
                parts.Add("light-probe-group:" + GetHierarchyPath(probeGroups[i].transform) + ":" + TransformDigest(probeGroups[i].transform) + ":" +
                    string.Join(";", (probeGroups[i].probePositions ?? Array.Empty<Vector3>()).Select(VectorDigest)));
            }

            var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)
                .Where(volume => volume != null && volume.gameObject.scene == scene)
                .OrderBy(volume => GetHierarchyPath(volume.transform), StringComparer.Ordinal).ToArray();
            for (var i = 0; i < volumes.Length; i++)
            {
                var volume = volumes[i];
                parts.Add("volume:" + GetHierarchyPath(volume.transform) + ":" + TransformDigest(volume.transform) +
                    ":global=" + volume.isGlobal + ":blend=" + F(volume.blendDistance) + ":weight=" + F(volume.weight) +
                    ":priority=" + F(volume.priority) + ":profile=" + AssetIdentity(volume.sharedProfile));
            }

            parts.Add("render-settings:sky=" + AssetIdentity(RenderSettings.skybox) + ":ambientMode=" + RenderSettings.ambientMode +
                ":ambientIntensity=" + F(RenderSettings.ambientIntensity) + ":fog=" + RenderSettings.fog +
                ":fogColor=" + ColorDigest(RenderSettings.fogColor) + ":fogStart=" + F(RenderSettings.fogStartDistance) +
                ":fogEnd=" + F(RenderSettings.fogEndDistance));
            return parts;
        }

        private static string AssetIdentity(UnityEngine.Object asset)
        {
            if (asset == null) return "null";
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var guid, out long localId)) return guid + ":" + localId;
            if (asset is Mesh mesh)
            {
                return "builtin-mesh:" + mesh.name + ":vertices=" + mesh.vertexCount + ":submeshes=" + mesh.subMeshCount +
                    ":bounds=" + VectorDigest(mesh.bounds.size);
            }
            return asset.GetType().FullName + ":" + asset.name;
        }

        private static bool IsLightingStatic(GameObject gameObject)
        {
            var flags = GameObjectUtility.GetStaticEditorFlags(gameObject);
            return (flags & (StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic)) != 0;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent) names.Push(current.name);
            return string.Join("/", names.ToArray());
        }

        private static string TransformDigest(Transform transform)
        {
            return VectorDigest(transform.localPosition) + ":" + QuaternionDigest(transform.localRotation) + ":" + VectorDigest(transform.localScale);
        }

        private static string VectorDigest(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);
        private static string QuaternionDigest(Quaternion value) => F(value.x) + "," + F(value.y) + "," + F(value.z) + "," + F(value.w);
        private static string ColorDigest(Color value) => F(value.r) + "," + F(value.g) + "," + F(value.b) + "," + F(value.a);
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static string[] GetPredecessorDigests(StageDefinition definition, Dictionary<MovementLabStage, MovementLabStageRecord> records)
        {
            var result = new string[definition.Predecessors.Length];
            for (var i = 0; i < definition.Predecessors.Length; i++)
            {
                var predecessor = definition.Predecessors[i];
                result[i] = predecessor + ":" + records[predecessor].outputDigest;
            }
            return result;
        }

        private static List<string> FindOutputDrift(MovementLabPathDigest[] expected, MovementLabPathDigest[] actual)
        {
            var drift = new List<string>();
            var expectedByPath = (expected ?? Array.Empty<MovementLabPathDigest>()).ToDictionary(item => item.path, StringComparer.Ordinal);
            var actualByPath = (actual ?? Array.Empty<MovementLabPathDigest>()).ToDictionary(item => item.path, StringComparer.Ordinal);
            foreach (var path in expectedByPath.Keys.Union(actualByPath.Keys, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
            {
                expectedByPath.TryGetValue(path, out var oldValue);
                actualByPath.TryGetValue(path, out var newValue);
                if (newValue == null || newValue.missing) drift.Add("missing:" + path);
                else if (oldValue == null || oldValue.missing || !string.Equals(oldValue.digest, newValue.digest, StringComparison.Ordinal)) drift.Add("changed:" + path);
            }
            return drift;
        }

        private static string FormatOutputDrift(MovementLabStage stage, List<string> drift)
        {
            return "MovementLab output drift in " + stage + "; generation stopped. " + string.Join(", ", drift.ToArray());
        }

        private static bool SequenceEqual(string[] left, string[] right)
        {
            return (left ?? Array.Empty<string>()).SequenceEqual(right ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        private static string HashBytes(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        private static string HashText(IEnumerable<string> values)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(string.Join("\n", values.OrderBy(value => value, StringComparer.Ordinal)) + "\n");
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string[] WithMetas(string[] paths)
        {
            var result = new List<string>();
            for (var i = 0; i < paths.Length; i++)
            {
                result.Add(paths[i]);
                result.Add(paths[i] + ".meta");
            }
            return result.ToArray();
        }

        private static string[] WithAssetMetasOnly(string[] paths)
        {
            var result = new List<string>();
            for (var i = 0; i < paths.Length; i++)
            {
                result.Add(paths[i]);
                if (paths[i].StartsWith("Assets/", StringComparison.Ordinal)) result.Add(paths[i] + ".meta");
            }
            return result.ToArray();
        }

        private static string[] Concat(string[] first, string[] second)
        {
            return first.Concat(second).ToArray();
        }

        private sealed class StageDefinition
        {
            internal readonly MovementLabStage Stage;
            internal readonly MovementLabStage[] Predecessors;
            internal readonly string ContractVersion;
            internal readonly string[] RepositoryInputs;
            internal readonly string[] DependencyInputs;
            internal readonly string[] LiteralInputs;
            internal readonly string[] Outputs;
            internal readonly bool IncludeUnityVersion;

            internal StageDefinition(MovementLabStage stage, MovementLabStage[] predecessors, string contractVersion,
                string[] repositoryInputs, string[] dependencyInputs, string[] literalInputs, string[] outputs, bool includeUnityVersion)
            {
                Stage = stage;
                Predecessors = (MovementLabStage[])predecessors.Clone();
                ContractVersion = contractVersion;
                RepositoryInputs = (string[])repositoryInputs.Clone();
                DependencyInputs = (string[])dependencyInputs.Clone();
                LiteralInputs = (string[])literalInputs.Clone();
                Outputs = (string[])outputs.Clone();
                IncludeUnityVersion = includeUnityVersion;
            }
        }
    }
}
