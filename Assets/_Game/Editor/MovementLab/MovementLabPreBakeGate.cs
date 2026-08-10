using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static RocketFooxball.Editor.MovementLabContractCatalog;

namespace RocketFooxball.Editor
{
    internal static class MovementLabPreBakeGate
    {
        private const int PassRecordSchemaVersion = 3;
        private static readonly MovementLabStage[] RequiredPreBakeStages =
        {
            MovementLabStage.Importer,
            MovementLabStage.MaterialPrefab,
            MovementLabStage.GameplayScene,
            MovementLabStage.Quality
        };

        [Serializable]
        private sealed class PassRecord
        {
            public int schemaVersion = PassRecordSchemaVersion;
            public string unityVersion;
            public string profileId;
            public string profileTag;
            public string lightingInputDigest;
            public string passedUtc;
            public string[] validatedStages;
        }

        internal static string ValidateAndWritePassRecord()
        {
            return ValidateAndWritePassRecord(MovementLabLightingProfiles.ProfileId.Production);
        }

        internal static string ValidateAndWritePassRecord(MovementLabLightingProfiles.ProfileId profile)
        {
            ValidatePersistedNonLightingState();
            MovementLabValidator.ValidatePreBakeSemantics();
            var probe = ProbePreparedScene(profile);

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) throw new InvalidOperationException("Unable to resolve Unity project root.");
            var commonGitDirectory = ResolveCommonGitDirectory(projectRoot);
            var evidenceRoot = Path.Combine(commonGitDirectory, "architecture-evidence", "movement-lab-prebake");

            var passDirectory = Path.Combine(evidenceRoot, "passes");
            var passPath = Path.Combine(passDirectory, profile + "-" + probe.LightingInputDigest + ".json");
            EnsureDurableEvidencePath(passPath, projectRoot);
            Directory.CreateDirectory(passDirectory);
            var record = new PassRecord
            {
                unityVersion = Application.unityVersion,
                profileId = profile.ToString(),
                profileTag = MovementLabLightingProfiles.Get(profile).Tag,
                lightingInputDigest = probe.LightingInputDigest,
                passedUtc = DateTime.UtcNow.ToString("O"),
                validatedStages = RequiredPreBakeStages.Select(stage => stage.ToString()).ToArray()
            };
            WriteOutsideProjectAtomic(passPath, JsonUtility.ToJson(record, true) + "\n", projectRoot);
            return passPath;
        }

        internal static void RevalidatePassRecord(string passPath)
        {
            RevalidatePassRecord(passPath, MovementLabLightingProfiles.ProfileId.Production);
        }

        internal static void RevalidatePassRecord(string passPath, MovementLabLightingProfiles.ProfileId profile)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) throw new InvalidOperationException("Unable to resolve Unity project root.");
            var fullPassPath = Path.GetFullPath(passPath ?? string.Empty);
            EnsureDurableEvidencePath(fullPassPath, projectRoot);
            if (!File.Exists(fullPassPath)) throw new InvalidOperationException("MovementLab pre-bake pass record is missing: " + fullPassPath);

            PassRecord pass;
            try
            {
                pass = JsonUtility.FromJson<PassRecord>(Encoding.UTF8.GetString(File.ReadAllBytes(fullPassPath)));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("MovementLab pre-bake pass record could not be parsed: " + exception.Message, exception);
            }

            var expectedProfile = MovementLabLightingProfiles.Get(profile);
            if (pass == null || pass.schemaVersion != PassRecordSchemaVersion ||
                !string.Equals(pass.unityVersion, Application.unityVersion, StringComparison.Ordinal) ||
                !string.Equals(pass.profileId, profile.ToString(), StringComparison.Ordinal) ||
                !string.Equals(pass.profileTag, expectedProfile.Tag, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(pass.lightingInputDigest) || string.IsNullOrWhiteSpace(pass.passedUtc) ||
                !DateTime.TryParse(pass.passedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out _) ||
                !SequenceEqual(pass.validatedStages, RequiredPreBakeStages.Select(stage => stage.ToString()).ToArray()))
            {
                throw new InvalidOperationException("MovementLab pre-bake pass record is stale or incomplete; semantic profile evidence required.");
            }

            var commonGitDirectory = ResolveCommonGitDirectory(projectRoot);
            var expectedPassPath = Path.GetFullPath(Path.Combine(commonGitDirectory, "architecture-evidence", "movement-lab-prebake", "passes", profile + "-" + pass.lightingInputDigest + ".json"));
            if (!string.Equals(fullPassPath, expectedPassPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("MovementLab pre-bake pass path is not bound to selected profile and lighting digest.");
            }

            ValidatePersistedNonLightingState();
            MovementLabValidator.ValidatePreBakeSemantics();
            var probe = ProbePreparedScene(profile);
            if (!string.Equals(probe.LightingInputDigest, pass.lightingInputDigest, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("MovementLab pre-bake finalized lighting digest or non-lighting state changed after pass validation.");
            }
        }

        private static bool SequenceEqual(string[] left, string[] right)
        {
            return (left ?? Array.Empty<string>()).SequenceEqual(right ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        private static MovementLabStageProbe ProbePreparedScene(MovementLabLightingProfiles.ProfileId profile)
        {
            // Profile preparation intentionally rewrites lighting-owned fields
            // in the serialized scene. The StageGraph allowance is narrow for
            // GameplayScene (all output drift must be this exact scene path),
            // while every non-lighting stage/path remains fail-closed below.
            // Prior BakedOutput files are replaced by the selected bake and
            // therefore are not pre-bake prerequisites.
            MovementLabLightingProfiles.ValidatePreparedScene(profile);
            var probe = MovementLabStageGraph.Probe(true, allowBakedOutputDrift: true);

            var stale = RequiredPreBakeStages
                .Where(stage => probe.IsStale(stage) &&
                    !IsRawOutputDriftOnly(probe, stage) &&
                    !(stage == MovementLabStage.GameplayScene && IsPreparedSceneHandoff(probe)))
                .Select(stage => stage.ToString())
                .ToArray();
            if (stale.Length > 0)
            {
                throw new InvalidOperationException("MovementLab pre-bake gate failed; stale non-lighting stages: " + string.Join(", ", stale));
            }

            return probe;
        }

        private static bool IsRawOutputDriftOnly(MovementLabStageProbe probe, MovementLabStage stage)
        {
            if (probe == null || !probe.TryGetStaleReason(stage, out var reason) || string.IsNullOrWhiteSpace(reason)) return false;
            var tokens = reason.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length > 0 && tokens.All(token => token.StartsWith("changed:", StringComparison.Ordinal));
        }

        private static bool IsPreparedSceneHandoff(MovementLabStageProbe probe)
        {
            if (probe == null || !probe.IsStale(MovementLabStage.GameplayScene) ||
                !probe.TryGetStaleReason(MovementLabStage.GameplayScene, out var reason)) return false;

            // Do not trim or discard empty tokens: a handoff is valid only for
            // one exact changed/missing ScenePath reason, with no extra reason.
            var tokens = (reason ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.None);
            return tokens.Length == 1 &&
                (string.Equals(tokens[0], "changed:" + MovementLabContract.ScenePath, StringComparison.Ordinal) ||
                 string.Equals(tokens[0], "missing:" + MovementLabContract.ScenePath, StringComparison.Ordinal));
        }

        private static void ValidatePersistedNonLightingState()
        {
            ValidateAssetAndMetaCoverage(MovementLabContract.ImportedAssetPaths);
            ValidateAssetAndMetaCoverage(MovementLabContract.MaterialPrefabOutputs);
            ValidateAssetAndMetaCoverage(MovementLabContract.QualityOutputs.Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)).ToArray());
            for (var i = 0; i < MovementLabContract.GameplaySceneOutputs.Length; i++)
            {
                var path = MovementLabContract.GameplaySceneOutputs[i];
                if (!File.Exists(MovementLabManifestStore.ResolveProjectPath(path)))
                {
                    throw new InvalidOperationException("MovementLab pre-bake output is missing: " + path);
                }
                if (path.StartsWith("Assets/", StringComparison.Ordinal) &&
                    !File.Exists(MovementLabManifestStore.ResolveProjectPath(path + ".meta")))
                {
                    throw new InvalidOperationException("MovementLab pre-bake output meta is missing: " + path + ".meta");
                }
                if (path.StartsWith("Assets/", StringComparison.Ordinal)) ValidateAssetMetaGuid(path);
            }

            var scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !string.Equals(scene.path, MovementLabContract.ScenePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("MovementLab scene failed to reopen: " + MovementLabContract.ScenePath);
            }

            var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.InstanceID);
            var persistedVolumeCount = 0;
            for (var i = 0; i < volumes.Length; i++)
            {
                var volume = volumes[i];
                if (volume == null || volume.sharedProfile == null) continue;
                persistedVolumeCount++;
                if (!EditorUtility.IsPersistent(volume.sharedProfile))
                {
                    throw new InvalidOperationException("MovementLab Volume must use a persisted sharedProfile.");
                }
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(volume.sharedProfile, out var profileGuid, out long profileLocalId) ||
                    string.IsNullOrEmpty(profileGuid) || profileLocalId == 0)
                {
                    throw new InvalidOperationException("MovementLab Volume sharedProfile has no persistent GUID/local file ID.");
                }
                var components = volume.sharedProfile.components;
                var requiredTypes = new[] { typeof(UnityEngine.Rendering.Universal.Tonemapping),
                    typeof(UnityEngine.Rendering.Universal.Bloom), typeof(UnityEngine.Rendering.Universal.ColorAdjustments) };
                if (components == null || components.Count != requiredTypes.Length)
                    throw new InvalidOperationException("MovementLab VolumeProfile required component set is incomplete or has unexpected entries.");
                for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
                {
                    var component = components[componentIndex];
                    if (component == null || !EditorUtility.IsPersistent(component) ||
                        !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out _, out long localId) || localId == 0)
                    {
                        throw new InvalidOperationException("MovementLab VolumeProfile component is not a persistent subasset.");
                    }
                    if (!requiredTypes.Contains(component.GetType()))
                        throw new InvalidOperationException("MovementLab VolumeProfile contains unexpected component: " + component.GetType().Name);
                }
                for (var typeIndex = 0; typeIndex < requiredTypes.Length; typeIndex++)
                    if (components.Count(component => component != null && component.GetType() == requiredTypes[typeIndex]) != 1)
                        throw new InvalidOperationException("MovementLab VolumeProfile required component is missing or duplicated: " + requiredTypes[typeIndex].Name);
            }
            if (persistedVolumeCount == 0) throw new InvalidOperationException("MovementLab persisted shared VolumeProfile is missing.");

            ValidateLitMaterialPersistence();
        }

        private static void ValidateAssetAndMetaCoverage(string[] paths)
        {
            for (var i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                if (AssetDatabase.LoadMainAssetAtPath(path) == null) throw new InvalidOperationException("MovementLab pre-bake asset is missing: " + path);
                if (!File.Exists(MovementLabManifestStore.ResolveProjectPath(path + ".meta")))
                {
                    throw new InvalidOperationException("MovementLab pre-bake asset meta is missing: " + path + ".meta");
                }
                ValidateAssetMetaGuid(path);
            }
        }

        private static void ValidateAssetMetaGuid(string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
                throw new InvalidOperationException("MovementLab pre-bake asset GUID is missing: " + path);

            var metaPath = MovementLabManifestStore.ResolveProjectPath(path + ".meta");
            var metaGuid = File.ReadAllLines(metaPath)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("guid:", StringComparison.Ordinal))
                .Select(line => line.Substring("guid:".Length).Trim())
                .FirstOrDefault();
            if (!string.Equals(guid, metaGuid, StringComparison.Ordinal))
                throw new InvalidOperationException("MovementLab pre-bake asset GUID/meta mismatch: " + path);
        }

        private static void ValidateLitMaterialPersistence()
        {
            var materialPaths = MovementLabContract.MaterialPrefabOutputs.Where(path => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase));
            foreach (var path in materialPaths)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null) throw new InvalidOperationException("MovementLab material failed persisted reload: " + path);
                if (material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") != null && !material.IsKeywordEnabled("_NORMALMAP"))
                {
                    throw new InvalidOperationException("MovementLab material normal-map keyword mismatch: " + path);
                }
                if (material.HasProperty("_MetallicGlossMap") && material.GetTexture("_MetallicGlossMap") != null && !material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"))
                {
                    throw new InvalidOperationException("MovementLab material metallic-map keyword mismatch: " + path);
                }
                if (material.shader != null && material.shader.name == LitShaderName)
                {
                    var hasEmission = material.GetTexture("_EmissionMap") != null ||
                        (material.HasProperty("_EmissionColor") && material.GetColor("_EmissionColor").maxColorComponent > 0.001f);
                    var expectedFlags = hasEmission ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    if (material.globalIlluminationFlags != expectedFlags || material.IsKeywordEnabled("_EMISSION") != hasEmission)
                        throw new InvalidOperationException("MovementLab material emission state is not persisted: " + path);

                    if (string.Equals(path, RocketHotMaterialPath, StringComparison.Ordinal))
                        ValidateReloadedEmission(material, RocketEmissionColor, RocketEmissionStrength, true, path);
                    else if (string.Equals(path, MaterialsPath + "/ArenaGlow.mat", StringComparison.Ordinal))
                        ValidateReloadedEmission(material, new Color(0.10f, 0.95f, 0.88f, 1f), 2f, false, path);
                    else if (string.Equals(path, MaterialsPath + "/WeaponAccent.mat", StringComparison.Ordinal))
                        ValidateReloadedEmission(material, new Color(1f, 0.16f, 0.03f, 1f), 1.5f, true, path);
                }
            }
        }

        private static void ValidateReloadedEmission(Material material, Color baseColor, float strength, bool requireEmissionMap, string path)
        {
            if (material.globalIlluminationFlags != MaterialGlobalIlluminationFlags.BakedEmissive ||
                !material.IsKeywordEnabled("_EMISSION") || (requireEmissionMap && material.GetTexture("_EmissionMap") == null) ||
                Vector4.Distance(material.GetColor("_EmissionColor"), baseColor * strength) > 0.01f ||
                (material.HasProperty("_EmissionStrength") && Mathf.Abs(material.GetFloat("_EmissionStrength") - strength) > 0.001f))
            {
                throw new InvalidOperationException("MovementLab emissive material failed clean-reload public-state validation: " + path);
            }
        }

        private static void EnsureDurableEvidencePath(string path, string projectRoot)
        {
            var fullPath = Path.GetFullPath(path);
            var projectPrefix = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("MovementLab pre-bake evidence must be outside worktree: " + fullPath);
            }
            if (fullPath.IndexOf(Path.DirectorySeparatorChar + "Library" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullPath.IndexOf(Path.DirectorySeparatorChar + "Temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException("MovementLab pre-bake evidence path is not durable: " + fullPath);
            }
        }

        private static string ResolveCommonGitDirectory(string projectRoot)
        {
            var value = RunGit(projectRoot, "rev-parse --git-common-dir");
            var path = Path.IsPathRooted(value) ? value : Path.Combine(projectRoot, value);
            return Path.GetFullPath(path);
        }

        private static string RunGit(string projectRoot, string arguments)
        {
            var start = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Unable to start Git for MovementLab pre-bake gate.");
                var output = process.StandardOutput.ReadToEnd().Trim();
                var error = process.StandardError.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("MovementLab pre-bake Git query failed: " + error);
                return output;
            }
        }

        private static void WriteOutsideProjectAtomic(string path, string content, string projectRoot)
        {
            var fullPath = Path.GetFullPath(path);
            var projectPrefix = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("MovementLab pre-bake evidence must be outside worktree: " + fullPath);
            }
            if (fullPath.IndexOf(Path.DirectorySeparatorChar + "Library" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullPath.IndexOf(Path.DirectorySeparatorChar + "Temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException("MovementLab pre-bake evidence path is not durable: " + fullPath);
            }
            var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Unable to resolve pre-bake evidence directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, ".tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                var bytes = new System.Text.UTF8Encoding(false).GetBytes(content);
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
                else File.Move(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
