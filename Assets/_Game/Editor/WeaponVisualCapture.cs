using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RocketFooxball.Editor
{
    /// <summary>
    /// Deterministic first-person weapon evidence capture. The capture only writes
    /// immutable evidence outside the project; scene and quality state are restored
    /// and the generated scene is reopened before the batch process exits.
    /// </summary>
    public static class WeaponVisualCapture
    {
        private const string ScenePath = "Assets/_Game/Scenes/MovementLab.unity";
        private const string BuildManifestPath = "Assets/_Game/Generated/MovementLabBuildManifest.json";
        private const int Width = GraphicsQualityConfigurator.NativeWidth;
        private const int Height = GraphicsQualityConfigurator.NativeHeight;
        private const int DepthBits = 24;
        private const int Samples = 1;
        private const int MaskXMin = 64;
        private const int MaskXMax = 1855;
        private const int MaskTopYMin = 540;
        private const int MaskTopYMax = 1079;
        private const int MaskBottomRowMin = 0;
        private const int MaskBottomRowMax = 539;
        private const int DifferencePixelFloor = 10000;
        private const byte DifferenceChannelFloor = 8;
        private const float DifferenceMeanFloor = 0.01f;
        private const float AngleTolerance = 0.25f;
        private const float CaptureFieldOfView = 75f;
        private const float CaptureCameraPitch = 8f;
        private const int MaxReferenceManifestBytes = 1024 * 1024;
        private const int MaxReferenceManifestJsonDepth = 32;
        private const int MaxReferenceManifestJsonTokens = 10000;
        private const int MaxReferenceManifestEntries = 128;

        private static readonly string[] ExpectedReferenceIds = { "game-bright", "game-dark", "quake-hires" };

        [Serializable]
        private sealed class ReferenceManifestDto
        {
            public int schemaVersion;
            public ReferenceEntry[] references;
        }

        [Serializable]
        private sealed class ReferenceEntry
        {
            public string logicalId;
            public string originalPath;
            public string copiedEvidencePath;
            public long byteLength;
            public string sha256;
        }

        [Serializable]
        private sealed class ReferenceHashDto
        {
            public string id;
            public string sha256;
        }

        [Serializable]
        private sealed class ManifestDto
        {
            public int schemaVersion = 1;
            public string weaponCaptureEvidenceRoot;
            public string weaponCaptureAttemptId;
            public string weaponCaptureWeapon;
            public string weaponCaptureMode;
            public string weaponCaptureReferenceManifest;
            public string attemptId;
            public string weapon;
            public string mode;
            public string captureUtc;
            public string evidenceDirectory;
            public string sourceSha;
            public string generatedManifestPath = BuildManifestPath;
            public string generatedManifestSha256;
            public string referenceManifestPath;
            public string referenceManifestSha256;
            public ReferenceHashDto[] referenceHashes;
            public Vector3 sunVector;
            public ImageEvidence[] images;
            public bool pass;
        }

        [Serializable]
        private sealed class ImageEvidence
        {
            public string filename;
            public string path;
            public string view;
            public string qualityLevel;
            public int qualityIndex;
            public string visualMode;
            public bool fastSessionApplied;
            public float targetAngle;
            public float actualAngle;
            public float angleDelta;
            public Vector3 cameraForward;
            public Vector3 planarCameraForward;
            public Vector3 weaponForward;
            public Vector3 playerPosition;
            public Vector3 playerEulerAngles;
            public Vector3 playerLocalPosition;
            public Vector3 cameraPosition;
            public Vector3 cameraEulerAngles;
            public Vector3 cameraLocalEulerAngles;
            public Vector3 weaponPosition;
            public Vector3 weaponEulerAngles;
            public float fieldOfView;
            public string maskOrigin = "top-left";
            public string maskRowOrigin = "bottom-left";
            public int maskXMin = MaskXMin;
            public int maskXMax = MaskXMax;
            public int maskYMin = MaskTopYMin;
            public int maskYMax = MaskTopYMax;
            public int maskRowMin = MaskBottomRowMin;
            public int maskRowMax = MaskBottomRowMax;
            public int differencePixelCount;
            public int differencePixelThreshold = DifferencePixelFloor;
            public int differenceChannelThreshold = DifferenceChannelFloor;
            public float differenceMeanAbsRgb;
            public float differenceMaxAbsRgb;
            public bool controlRendered;
            public int width;
            public int height;
            public long bytes;
            public string sha256;
            public bool pass;
        }

        private sealed class CaptureOptions
        {
            public string EvidenceRoot;
            public string AttemptId;
            public string Weapon;
            public string Mode;
            public string ReferenceManifest;
        }

        private sealed class PoseDefinition
        {
            public readonly string Name;
            public readonly string FileToken;
            public readonly float TargetAngle;

            public PoseDefinition(string name, string fileToken, float targetAngle)
            {
                Name = name;
                FileToken = fileToken;
                TargetAngle = targetAngle;
            }
        }

        private sealed class ObjectActiveState
        {
            public GameObject Object;
            public bool Active;
        }

        private sealed class BehaviourState
        {
            public Behaviour Behaviour;
            public bool Enabled;
        }

        private struct CameraState
        {
            public Camera Camera;
            public RenderTexture TargetTexture;
            public bool Enabled;
            public CameraClearFlags ClearFlags;
            public Color Background;
            public float FieldOfView;
            public float NearClip;
            public float FarClip;
            public float Aspect;
            public Rect Rect;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public int CullingMask;
        }

        private sealed class DifferenceEvidence
        {
            public int PixelCount;
            public float MeanAbsRgb;
            public float MaxAbsRgb;
            public bool Pass => PixelCount >= DifferencePixelFloor && MeanAbsRgb >= DifferenceMeanFloor;
        }

        private sealed class ReferenceEvidence
        {
            public string ManifestPath;
            public string ManifestSha256;
            public ReferenceEntry[] Entries;
        }

        [MenuItem("Rocket Fooxball/Capture Weapon Visuals")]
        public static void Capture()
        {
            var options = ReadCaptureOptions();
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var reference = ReadAndValidateReferenceManifest(options.ReferenceManifest, projectRoot);
            var generatedManifestPath = ResolveProjectPath(projectRoot, BuildManifestPath);
            var generatedManifestSha = HashFile(generatedManifestPath);
            var sourceSha = ReadGitSha(projectRoot);
            // The wrapper owns the evidence root and runner directories. The
            // capture facade alone reserves and creates this absent leaf.
            var evidenceDirectory = CreateEvidenceDirectory(options.EvidenceRoot, options.AttemptId, projectRoot);
            var manifestPath = Path.Combine(evidenceDirectory, "WeaponVisualManifest.json");
            var initialQuality = QualitySettings.GetQualityLevel();
            var initialFastSession = MovementLabFastModeSession.IsActive;
            if (initialFastSession)
                throw new InvalidOperationException("Weapon capture requires no active Fast session at entry; refusing to disturb an existing preview.");

            RenderTexture previousActive = null;
            RenderTexture renderTarget = null;
            PlayerPresentation presentation = null;
            GameObject player = null;
            GameObject viewmodels = null;
            GameObject rocketVisual = null;
            GameObject shotgunVisual = null;
            Light viewmodelLight = null;
            Camera gameplayCamera = null;
            GraphicsQualityRuntime qualityRuntime = null;
            CameraState cameraState = default;
            ObjectActiveState[] objectStates = Array.Empty<ObjectActiveState>();
            BehaviourState[] behaviourStates = Array.Empty<BehaviourState>();
            Vector3 playerPosition = Vector3.zero;
            Quaternion playerRotation = Quaternion.identity;
            Vector3 playerLocalPosition = Vector3.zero;
            Quaternion playerLocalRotation = Quaternion.identity;
            var images = new List<ImageEvidence>(6);
            var manifest = new ManifestDto
            {
                weaponCaptureEvidenceRoot = options.EvidenceRoot,
                weaponCaptureAttemptId = options.AttemptId,
                weaponCaptureWeapon = options.Weapon,
                weaponCaptureMode = options.Mode,
                weaponCaptureReferenceManifest = reference.ManifestPath,
                attemptId = options.AttemptId,
                weapon = options.Weapon,
                mode = options.Mode,
                captureUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                evidenceDirectory = evidenceDirectory,
                sourceSha = sourceSha,
                generatedManifestSha256 = generatedManifestSha,
                referenceManifestPath = reference.ManifestPath,
                referenceManifestSha256 = reference.ManifestSha256,
                referenceHashes = reference.Entries.Select(entry => new ReferenceHashDto { id = entry.logicalId, sha256 = entry.sha256 }).ToArray()
            };

            try
            {
                if (!Application.isBatchMode)
                    throw new InvalidOperationException("Weapon visual capture requires Unity batch mode.");
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Weapon visual capture requires graphics-enabled Unity; GraphicsDeviceType.Null is unsupported.");

                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                if (!scene.IsValid() || !string.Equals(scene.path, ScenePath, StringComparison.Ordinal))
                    throw new InvalidOperationException("MovementLab scene is not active after validation: " + scene.path);

                if (options.Mode == "Persisted")
                    ValidatePersistedLighting();
                else if (MovementLabFastModeSession.IsActive)
                    throw new InvalidOperationException("Fast weapon capture cannot begin with an active Fast session.");

                var presentations = UnityEngine.Object.FindObjectsByType<PlayerPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(candidate => candidate != null && candidate.Participant != null && candidate.Participant.IsLocalParticipant)
                    .ToArray();
                if (presentations.Length != 1)
                    throw new InvalidOperationException("Expected exactly one local PlayerPresentation; found " + presentations.Length + ".");
                presentation = presentations[0];
                player = presentation.gameObject;
                gameplayCamera = Require(player.transform.Find("Head/Camera")?.GetComponent<Camera>(), "local player camera");
                Require(gameplayCamera.GetComponent<UniversalAdditionalCameraData>(), "local player UniversalAdditionalCameraData");
                qualityRuntime = Require(gameplayCamera.GetComponent<GraphicsQualityRuntime>(), "local player GraphicsQualityRuntime");
                viewmodels = Require(gameplayCamera.transform.Find("Viewmodels")?.gameObject, "local player Viewmodels");
                rocketVisual = Require(viewmodels.transform.Find("WeaponVisual")?.gameObject, "local player rocket FPS visual");
                shotgunVisual = Require(viewmodels.transform.Find("FpsShotgunVisual")?.gameObject, "local player shotgun FPS visual");
                viewmodelLight = Require(viewmodels.transform.Find(MovementLabContract.ViewmodelLightName)?.GetComponent<Light>(), "local player ViewmodelLight");

                cameraState = SaveCameraState(gameplayCamera);
                objectStates = player.GetComponentsInChildren<Transform>(true)
                    .Where(transform => transform != null)
                    .Select(transform => new ObjectActiveState { Object = transform.gameObject, Active = transform.gameObject.activeSelf })
                    .ToArray();
                behaviourStates = player.GetComponentsInChildren<Behaviour>(true)
                    .Where(behaviour => behaviour != null)
                    .Select(behaviour => new BehaviourState { Behaviour = behaviour, Enabled = behaviour.enabled })
                    .ToArray();
                playerPosition = player.transform.position;
                playerRotation = player.transform.rotation;
                playerLocalPosition = player.transform.localPosition;
                playerLocalRotation = player.transform.localRotation;

                presentation.SetLocalMode(true);
                presentation.SetAlive(true);
                presentation.SetShotgunOwned(string.Equals(options.Weapon, "Shotgun", StringComparison.Ordinal));
                viewmodels.SetActive(true);
                rocketVisual.SetActive(string.Equals(options.Weapon, "Rocket", StringComparison.Ordinal));
                shotgunVisual.SetActive(string.Equals(options.Weapon, "Shotgun", StringComparison.Ordinal));
                if (!string.Equals(options.Weapon, "Rocket", StringComparison.Ordinal) && !string.Equals(options.Weapon, "Shotgun", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsupported weapon: " + options.Weapon);
                if (!rocketVisual.activeSelf && !shotgunVisual.activeSelf)
                    throw new InvalidOperationException("Requested weapon visual was not enabled after presentation setup.");

                renderTarget = new RenderTexture(Width, Height, DepthBits, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "WeaponVisualCaptureRT",
                    antiAliasing = Samples,
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                renderTarget.Create();
                previousActive = RenderTexture.active;
                gameplayCamera.targetTexture = renderTarget;
                gameplayCamera.aspect = (float)Width / Height;
                gameplayCamera.enabled = false;

                var sun = Require(RenderSettings.sun, "Environment/Sun");
                var sunward = Planar(-sun.transform.forward, "sunward");
                manifest.sunVector = sunward;
                var requestedVisual = string.Equals(options.Weapon, "Rocket", StringComparison.Ordinal) ? rocketVisual : shotgunVisual;
                var poses = new[]
                {
                    new PoseDefinition("sunward", "sunward", 0f),
                    new PoseDefinition("crosslight", "crosslight", 90f),
                    new PoseDefinition("awaylight", "awaylight", 180f)
                };
                var qualityLevels = new[]
                {
                    (GraphicsQualityConfigurator.HighQualityIndex, GraphicsQualityConfigurator.HighQualityName),
                    (GraphicsQualityConfigurator.LowQualityIndex, GraphicsQualityConfigurator.LowQualityName)
                };

                for (var poseIndex = 0; poseIndex < poses.Length; poseIndex++)
                {
                    var pose = poses[poseIndex];
                    SetCapturePose(player, gameplayCamera, sunward, pose.TargetAngle);
                    for (var qualityIndex = 0; qualityIndex < qualityLevels.Length; qualityIndex++)
                    {
                        var quality = qualityLevels[qualityIndex];
                        var image = CaptureImage(options, pose, quality.Item1, quality.Item2, player, gameplayCamera,
                            qualityRuntime, viewmodels, requestedVisual, renderTarget, sunward, evidenceDirectory);
                        images.Add(image);
                    }
                }

                var imageHashes = images.Select(image => image.sha256).ToArray();
                if (imageHashes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != imageHashes.Length)
                    throw new InvalidOperationException("Weapon capture image hashes must be unique.");
                if (HashFile(generatedManifestPath) != generatedManifestSha)
                    throw new InvalidOperationException("Generated MovementLabBuildManifest.json changed during capture.");
                if (HashFile(reference.ManifestPath) != reference.ManifestSha256)
                    throw new InvalidOperationException("Reference manifest changed during capture.");
                if (ReadGitSha(projectRoot) != sourceSha)
                    throw new InvalidOperationException("Git HEAD changed during capture.");
                if (images.Count != 6 || images.Any(image => !image.pass))
                    throw new InvalidOperationException("Weapon capture did not produce six passing images.");
                manifest.images = images.ToArray();
                manifest.pass = true;
                WriteImmutableJson(manifestPath, manifest);
                UnityEngine.Debug.Log("WEAPON_VISUAL_CAPTURE_MANIFEST_WRITTEN " + manifestPath);
                UnityEngine.Debug.Log("WEAPON_VISUAL_CAPTURE_PASS " + manifestPath);
                UnityEngine.Debug.Log("WEAPON_VISUAL_CAPTURE_EVIDENCE " + evidenceDirectory);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                throw;
            }
            finally
            {
                Exception restoreError = null;
                var fastRestoreFailed = false;
                if (MovementLabFastModeSession.IsActive)
                {
                    try
                    {
                        MovementLabFastModeSession.RestoreIfActive();
                    }
                    catch (Exception exception)
                    {
                        fastRestoreFailed = true;
                        restoreError = exception;
                    }
                }

                TryRestoreCaptureState(ref restoreError, () => RestoreBehaviourStates(behaviourStates));
                TryRestoreCaptureState(ref restoreError, () => RestoreObjectStates(objectStates));
                TryRestoreCaptureState(ref restoreError, () =>
                {
                    if (player == null) return;
                    player.transform.position = playerPosition;
                    player.transform.rotation = playerRotation;
                    player.transform.localPosition = playerLocalPosition;
                    player.transform.localRotation = playerLocalRotation;
                });
                TryRestoreCaptureState(ref restoreError, () =>
                {
                    if (gameplayCamera != null)
                        RestoreCameraState(gameplayCamera, cameraState);
                });
                TryRestoreCaptureState(ref restoreError, () => QualitySettings.SetQualityLevel(initialQuality, true));
                TryRestoreCaptureState(ref restoreError, () =>
                {
                    if (qualityRuntime != null)
                        qualityRuntime.ApplyCurrentQuality();
                });
                TryRestoreCaptureState(ref restoreError, () =>
                {
                    if (viewmodelLight != null)
                        viewmodelLight.enabled = behaviourStates.FirstOrDefault(state => state.Behaviour == viewmodelLight)?.Enabled ?? viewmodelLight.enabled;
                });
                TryRestoreCaptureState(ref restoreError, () => RenderTexture.active = previousActive);
                TryRestoreCaptureState(ref restoreError, () =>
                {
                    if (renderTarget == null) return;
                    renderTarget.Release();
                    UnityEngine.Object.DestroyImmediate(renderTarget);
                });

                if (!MovementLabFastModeSession.IsActive)
                {
                    try
                    {
                        // Reopen without saving so transient presentation, quality, and
                        // runtime-created state cannot leak into the next invocation.
                        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    }
                    catch (Exception exception)
                    {
                        if (restoreError == null) restoreError = exception;
                        else UnityEngine.Debug.LogException(exception);
                    }
                }
                else if (fastRestoreFailed)
                    UnityEngine.Debug.LogError("Rocket Fooxball weapon capture left Fast preview active after restore failure; scene reopen skipped.");

                if (restoreError != null)
                    throw restoreError;
            }
        }

        private static void TryRestoreCaptureState(ref Exception restoreError, Action restoreAction)
        {
            try
            {
                restoreAction();
            }
            catch (Exception exception)
            {
                if (restoreError == null) restoreError = exception;
                else UnityEngine.Debug.LogException(exception);
            }
        }

        private static CaptureOptions ReadCaptureOptions()
        {
            var arguments = Environment.GetCommandLineArgs();
            var root = ReadArgument(arguments, "-weaponCaptureEvidenceRoot");
            var attemptId = ReadArgument(arguments, "-weaponCaptureAttemptId");
            var weapon = ReadArgument(arguments, "-weaponCaptureWeapon");
            var mode = ReadArgument(arguments, "-weaponCaptureMode");
            var referenceManifest = ReadArgument(arguments, "-weaponCaptureReferenceManifest");
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(attemptId) || string.IsNullOrWhiteSpace(weapon) ||
                string.IsNullOrWhiteSpace(mode) || string.IsNullOrWhiteSpace(referenceManifest))
                throw new InvalidOperationException("Weapon capture requires -weaponCaptureEvidenceRoot, -weaponCaptureAttemptId, -weaponCaptureWeapon, -weaponCaptureMode, and -weaponCaptureReferenceManifest.");
            if (attemptId.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(attemptId, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$") || attemptId == "." || attemptId == "..")
                throw new InvalidOperationException("weaponCaptureAttemptId must be 1-64 filename-safe characters.");
            if (!string.Equals(weapon, "Rocket", StringComparison.OrdinalIgnoreCase) && !string.Equals(weapon, "Shotgun", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("weaponCaptureWeapon must be Rocket or Shotgun.");
            if (!string.Equals(mode, "Fast", StringComparison.OrdinalIgnoreCase) && !string.Equals(mode, "Persisted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("weaponCaptureMode must be Fast or Persisted.");
            var fullRoot = Path.GetFullPath(root).TrimEnd('\\');
            if (!fullRoot.StartsWith("C:\\wt\\", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(fullRoot))
                throw new InvalidOperationException("weaponCaptureEvidenceRoot must be an existing descendant of C:\\wt.");
            if (!Path.IsPathRooted(referenceManifest))
                throw new InvalidOperationException("weaponCaptureReferenceManifest must be an absolute immutable path.");
            return new CaptureOptions
            {
                EvidenceRoot = fullRoot,
                AttemptId = attemptId,
                Weapon = char.ToUpperInvariant(weapon[0]) + weapon.Substring(1).ToLowerInvariant(),
                Mode = char.ToUpperInvariant(mode[0]) + mode.Substring(1).ToLowerInvariant(),
                ReferenceManifest = Path.GetFullPath(referenceManifest)
            };
        }

        private static string ReadArgument(string[] arguments, string name)
        {
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                    return arguments[index + 1];
            }
            return null;
        }

        private static string CreateEvidenceDirectory(string evidenceRoot, string attemptId, string projectRoot)
        {
            var root = Path.GetFullPath(evidenceRoot).TrimEnd('\\');
            var project = Path.GetFullPath(projectRoot).TrimEnd('\\');
            if (!Directory.Exists(root))
                throw new InvalidOperationException("Weapon evidence root must already exist: " + root);
            var rootInfo = new DirectoryInfo(root);
            if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Weapon evidence root may not be a junction or alias: " + root);
            var directory = Path.GetFullPath(Path.Combine(root, attemptId));
            if (directory.Equals(project, StringComparison.OrdinalIgnoreCase) || directory.StartsWith(project + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Weapon evidence must be outside the Unity project.");
            if (directory.Length >= 260 || Path.Combine(directory, "shotgun-awaylight-low.png").Length >= 260)
                throw new InvalidOperationException("Weapon evidence path exceeds the Windows 260-character limit: " + directory);
            if (Directory.Exists(directory) || File.Exists(directory))
                throw new InvalidOperationException("Weapon evidence attempt already exists: " + directory);
            // Directory.CreateDirectory is idempotent and therefore cannot by
            // itself reserve a unique attempt. Hold an atomic sibling marker
            // while creating the leaf so concurrent Rocket/Shotgun attempts
            // with the same id cannot both publish into one directory.
            var reservation = Path.Combine(root, ".weapon-capture-" + attemptId + ".reservation");
            var reservationCreated = false;
            try
            {
                using (var stream = new FileStream(reservation, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    reservationCreated = true;
                    var bytes = Encoding.UTF8.GetBytes("weapon-capture-reservation\n");
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (Directory.Exists(directory) || File.Exists(directory))
                    throw new InvalidOperationException("Weapon evidence attempt already exists: " + directory);
                Directory.CreateDirectory(directory);
                if (!Directory.Exists(directory))
                    throw new InvalidOperationException("Weapon evidence attempt directory was not created: " + directory);
                return directory;
            }
            finally
            {
                // Never delete a contender's reservation when CreateNew fails.
                if (reservationCreated && File.Exists(reservation)) File.Delete(reservation);
            }
        }

        private static ReferenceEvidence ReadAndValidateReferenceManifest(string path, string projectRoot)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath)) throw new InvalidOperationException("Reference manifest missing: " + fullPath);
            var manifestBytes = File.ReadAllBytes(fullPath);
            if (manifestBytes.Length > MaxReferenceManifestBytes)
                throw new InvalidOperationException("Reference manifest exceeds the maximum supported size.");
            var hash = Sha256(manifestBytes);
            string manifestJson;
            try { manifestJson = new UTF8Encoding(false, true).GetString(manifestBytes); }
            catch (DecoderFallbackException exception) { throw new InvalidOperationException("Reference manifest JSON encoding is invalid: " + exception.Message); }
            RunReferenceManifestStructureSelfTests();
            ValidateReferenceManifestStructure(manifestJson);
            ReferenceManifestDto manifest;
            try { manifest = JsonUtility.FromJson<ReferenceManifestDto>(manifestJson); }
            catch (Exception exception) { throw new InvalidOperationException("Reference manifest JSON is invalid: " + exception.Message); }
            if (manifest == null || manifest.schemaVersion != 1)
                throw new InvalidOperationException("Reference manifest schemaVersion must equal 1.");
            var entries = manifest.references;
            if (entries == null || entries.Length != ExpectedReferenceIds.Length)
                throw new InvalidOperationException("Reference manifest must contain exactly three schema-1 references.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (entry == null || !ExpectedReferenceIds.Contains(entry.logicalId, StringComparer.Ordinal) || !seen.Add(entry.logicalId))
                    throw new InvalidOperationException("Reference manifest references must contain each expected logicalId exactly once.");
                if (entry.byteLength <= 0 || string.IsNullOrWhiteSpace(entry.originalPath) || string.IsNullOrWhiteSpace(entry.copiedEvidencePath) ||
                    entry.sha256 == null || !System.Text.RegularExpressions.Regex.IsMatch(entry.sha256, "^[0-9a-fA-F]{64}$"))
                    throw new InvalidOperationException("Reference manifest reference is incomplete: " + entry.logicalId);
                var copiedEvidencePath = ResolveReferencePath(entry.copiedEvidencePath, fullPath, projectRoot);
                var originalPath = ResolveReferencePath(entry.originalPath, fullPath, projectRoot);
                ValidateReferenceFile(copiedEvidencePath, entry, entry.logicalId + " copiedEvidencePath");
                if (!File.Exists(originalPath)) throw new InvalidOperationException("Reference originalPath missing: " + originalPath);
                if (new FileInfo(originalPath).Length != entry.byteLength || !HashFile(originalPath).Equals(entry.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Reference originalPath hash mismatch: " + entry.logicalId);
            }
            return new ReferenceEvidence { ManifestPath = fullPath, ManifestSha256 = hash, Entries = entries };
        }

        private static void ValidateReferenceManifestStructure(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > MaxReferenceManifestBytes)
                throw new InvalidOperationException("Reference manifest JSON is empty or exceeds the maximum supported size.");
            new StrictReferenceManifestJsonReader(json).ReadManifest();
        }

        private static void RunReferenceManifestStructureSelfTests()
        {
            var canonical = "{\"references\":[{\"sha256\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"byteLength\":1,\"copiedEvidencePath\":\"e\\\\vidence\",\"originalPath\":\"o\",\"logicalId\":\"game-bright\"}],\"\\u0073chemaVersion\":1}";
            ValidateReferenceManifestStructure(canonical);
            var prettyPrinted = canonical
                .Replace("{\"", "{\r\n\t\"")
                .Replace("\":", "\": \r\n\t")
                .Replace(",", ",\r\n\t")
                .Replace("}", "\r\n}");
            ValidateReferenceManifestStructure(prettyPrinted);
            var reordered = "{\"\\u0073chemaVersion\":1,\"references\":[{\"logicalId\":\"game-bright\",\"sha256\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"originalPath\":\"o\",\"byteLength\":1,\"copiedEvidencePath\":\"e\\\\vidence\"}]}";
            ValidateReferenceManifestStructure(reordered);
            AssertReferenceManifestStructureRejected(
                canonical.Replace(",\"\\u0073chemaVersion\":1}", ",\"entries\":[],\"\\u0073chemaVersion\":1}"),
                "mixed root alias");
            AssertReferenceManifestStructureRejected(
                canonical.Replace("\"logicalId\":\"game-bright\"", "\"logicalId\":\"game-bright\",\"id\":\"game-bright\""),
                "mixed entry alias");
            AssertReferenceManifestStructureRejected(
                canonical.Replace(",\"\\u0073chemaVersion\":1}", ",\"unexpected\":{\"nested\":[{\"value\":true}]},\"\\u0073chemaVersion\":1}"),
                "unknown property");
            AssertReferenceManifestStructureRejected(
                canonical.Replace(",\"\\u0073chemaVersion\":1}", ",\"schemaVersion\":1,\"\\u0073chemaVersion\":1}"),
                "duplicate property");
        }

        private static void AssertReferenceManifestStructureRejected(string json, string caseName)
        {
            var rejected = false;
            try { ValidateReferenceManifestStructure(json); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected)
                throw new InvalidOperationException("Reference manifest structure self-test accepted " + caseName + ".");
        }

        private sealed class StrictReferenceManifestJsonReader
        {
            private readonly string json;
            private int offset;
            private int depth;
            private int tokenCount;

            public StrictReferenceManifestJsonReader(string json)
            {
                this.json = json;
            }

            public void ReadManifest()
            {
                SkipWhitespace();
                BeginContainer('{');
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var hasSchemaVersion = false;
                var hasReferences = false;
                try
                {
                    SkipWhitespace();
                    if (Consume('}'))
                        throw Error("root object is empty");
                    while (true)
                    {
                        CountToken();
                        var name = ReadString();
                        SkipWhitespace();
                        Expect(':');
                        if (!seen.Add(name))
                            throw Error("duplicate root property '" + name + "'");
                        switch (name)
                        {
                            case "schemaVersion":
                                hasSchemaVersion = true;
                                ReadValue();
                                break;
                            case "references":
                                hasReferences = true;
                                ReadReferenceEntries();
                                break;
                            default:
                                throw Error("unknown root property '" + name + "'");
                        }

                        SkipWhitespace();
                        if (Consume(','))
                        {
                            SkipWhitespace();
                            if (Peek() == '}') throw Error("trailing comma in root object");
                            continue;
                        }
                        Expect('}');
                        break;
                    }
                }
                finally
                {
                    EndContainer();
                }

                if (!hasSchemaVersion || !hasReferences || seen.Count != 2)
                    throw Error("root properties must be exactly schemaVersion and references");
                SkipWhitespace();
                if (offset != json.Length)
                    throw Error("trailing JSON content");
            }

            private void ReadReferenceEntries()
            {
                SkipWhitespace();
                BeginContainer('[');
                var count = 0;
                try
                {
                    SkipWhitespace();
                    if (Consume(']')) return;
                    while (true)
                    {
                        count++;
                        if (count > MaxReferenceManifestEntries)
                            throw Error("references array exceeds the maximum supported entry count");
                        ReadReferenceEntry();
                        SkipWhitespace();
                        if (Consume(','))
                        {
                            SkipWhitespace();
                            if (Peek() == ']') throw Error("trailing comma in references array");
                            continue;
                        }
                        Expect(']');
                        break;
                    }
                }
                finally
                {
                    EndContainer();
                }
            }

            private void ReadReferenceEntry()
            {
                SkipWhitespace();
                BeginContainer('{');
                var seen = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    SkipWhitespace();
                    if (Consume('}'))
                        throw Error("reference entry object is empty");
                    while (true)
                    {
                        CountToken();
                        var name = ReadString();
                        SkipWhitespace();
                        Expect(':');
                        if (!seen.Add(name))
                            throw Error("duplicate reference entry property '" + name + "'");
                        switch (name)
                        {
                            case "logicalId":
                            case "originalPath":
                            case "copiedEvidencePath":
                            case "byteLength":
                            case "sha256":
                                ReadValue();
                                break;
                            default:
                                throw Error("unknown reference entry property '" + name + "'");
                        }

                        SkipWhitespace();
                        if (Consume(','))
                        {
                            SkipWhitespace();
                            if (Peek() == '}') throw Error("trailing comma in reference entry object");
                            continue;
                        }
                        Expect('}');
                        break;
                    }
                }
                finally
                {
                    EndContainer();
                }

                if (seen.Count != 5)
                    throw Error("reference entry properties must be exactly logicalId, originalPath, copiedEvidencePath, byteLength, and sha256");
            }

            private void ReadValue()
            {
                SkipWhitespace();
                CountToken();
                switch (Peek())
                {
                    case '"':
                        ReadString();
                        return;
                    case '{':
                        ReadObjectValue();
                        return;
                    case '[':
                        ReadArrayValue();
                        return;
                    case 't':
                        ReadLiteral("true");
                        return;
                    case 'f':
                        ReadLiteral("false");
                        return;
                    case 'n':
                        ReadLiteral("null");
                        return;
                    default:
                        if (Peek() == '-' || IsDigit(Peek()))
                        {
                            ReadNumber();
                            return;
                        }
                        throw Error("invalid JSON value");
                }
            }

            private void ReadObjectValue()
            {
                SkipWhitespace();
                BeginContainer('{');
                var seen = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    SkipWhitespace();
                    if (Consume('}')) return;
                    while (true)
                    {
                        CountToken();
                        var name = ReadString();
                        if (!seen.Add(name))
                            throw Error("duplicate nested object property '" + name + "'");
                        SkipWhitespace();
                        Expect(':');
                        ReadValue();
                        SkipWhitespace();
                        if (Consume(','))
                        {
                            SkipWhitespace();
                            if (Peek() == '}') throw Error("trailing comma in nested object");
                            continue;
                        }
                        Expect('}');
                        break;
                    }
                }
                finally
                {
                    EndContainer();
                }
            }

            private void ReadArrayValue()
            {
                SkipWhitespace();
                BeginContainer('[');
                try
                {
                    SkipWhitespace();
                    if (Consume(']')) return;
                    while (true)
                    {
                        ReadValue();
                        SkipWhitespace();
                        if (Consume(','))
                        {
                            SkipWhitespace();
                            if (Peek() == ']') throw Error("trailing comma in nested array");
                            continue;
                        }
                        Expect(']');
                        break;
                    }
                }
                finally
                {
                    EndContainer();
                }
            }

            private string ReadString()
            {
                SkipWhitespace();
                Expect('"');
                var builder = new StringBuilder();
                while (offset < json.Length)
                {
                    var character = json[offset++];
                    if (character == '"') return builder.ToString();
                    if (character < 0x20) throw Error("unescaped control character in string");
                    if (character != '\\')
                    {
                        builder.Append(character);
                        continue;
                    }
                    if (offset >= json.Length) throw Error("unterminated escape sequence");
                    var escaped = json[offset++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u': builder.Append((char)ReadHexQuad()); break;
                        default: throw Error("invalid string escape sequence");
                    }
                }
                throw Error("unterminated string");
            }

            private int ReadHexQuad()
            {
                if (offset + 4 > json.Length) throw Error("incomplete Unicode escape sequence");
                var value = 0;
                for (var index = 0; index < 4; index++)
                {
                    var digit = json[offset++];
                    var hex = HexValue(digit);
                    if (hex < 0) throw Error("invalid Unicode escape sequence");
                    value = (value << 4) | hex;
                }
                return value;
            }

            private void ReadNumber()
            {
                SkipWhitespace();
                if (Consume('-') && !IsDigit(Peek())) throw Error("invalid JSON number");
                if (Consume('0'))
                {
                    if (IsDigit(Peek())) throw Error("JSON number has a leading zero");
                }
                else
                {
                    if (!IsDigitOneToNine(Peek())) throw Error("invalid JSON number");
                    while (IsDigit(Peek())) offset++;
                }
                if (Consume('.'))
                {
                    if (!IsDigit(Peek())) throw Error("JSON number fraction has no digits");
                    while (IsDigit(Peek())) offset++;
                }
                if (Peek() == 'e' || Peek() == 'E')
                {
                    offset++;
                    if (Peek() == '+' || Peek() == '-') offset++;
                    if (!IsDigit(Peek())) throw Error("JSON number exponent has no digits");
                    while (IsDigit(Peek())) offset++;
                }
            }

            private void ReadLiteral(string literal)
            {
                SkipWhitespace();
                if (offset + literal.Length > json.Length || !string.Equals(json.Substring(offset, literal.Length), literal, StringComparison.Ordinal))
                    throw Error("invalid JSON literal");
                offset += literal.Length;
            }

            private void BeginContainer(char opening)
            {
                SkipWhitespace();
                Expect(opening);
                depth++;
                if (depth > MaxReferenceManifestJsonDepth)
                    throw Error("JSON nesting exceeds the maximum supported depth");
            }

            private void EndContainer()
            {
                depth--;
            }

            private void SkipWhitespace()
            {
                while (offset < json.Length)
                {
                    var character = json[offset];
                    if (character != ' ' && character != '\t' && character != '\r' && character != '\n') return;
                    offset++;
                }
            }

            private void Expect(char expected)
            {
                if (!Consume(expected)) throw Error("expected '" + expected + "'");
            }

            private bool Consume(char expected)
            {
                if (offset >= json.Length || json[offset] != expected) return false;
                offset++;
                return true;
            }

            private char Peek()
            {
                return offset < json.Length ? json[offset] : '\0';
            }

            private void CountToken()
            {
                tokenCount++;
                if (tokenCount > MaxReferenceManifestJsonTokens)
                    throw Error("JSON token count exceeds the maximum supported size");
            }

            private InvalidOperationException Error(string message)
            {
                return new InvalidOperationException("Reference manifest JSON structure invalid at offset " + offset + ": " + message);
            }

            private static bool IsDigit(char value)
            {
                return value >= '0' && value <= '9';
            }

            private static bool IsDigitOneToNine(char value)
            {
                return value >= '1' && value <= '9';
            }

            private static int HexValue(char value)
            {
                if (value >= '0' && value <= '9') return value - '0';
                if (value >= 'a' && value <= 'f') return value - 'a' + 10;
                if (value >= 'A' && value <= 'F') return value - 'A' + 10;
                return -1;
            }
        }

        private static string ResolveReferencePath(string value, string manifestPath, string projectRoot)
        {
            if (Path.IsPathRooted(value)) return Path.GetFullPath(value);
            var normalized = value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (normalized.StartsWith("Assets" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("graphics references" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(Path.Combine(projectRoot, normalized));
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath), normalized));
        }

        private static void ValidateReferenceFile(string path, ReferenceEntry entry, string label)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Reference " + label + " missing: " + path);
            var info = new FileInfo(path);
            if (info.Length != entry.byteLength || !HashFile(path).Equals(entry.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Reference " + label + " hash mismatch: " + path);
        }

        private static void ValidatePersistedLighting()
        {
            if (MovementLabFastModeSession.IsActive)
                throw new InvalidOperationException("Persisted weapon capture cannot run while Fast mode is active.");
            var probe = MovementLabStageGraph.Probe(false, allowBakedOutputDrift: false);
            if (!string.Equals(probe.CurrentState?.bakedProfile ?? "none", MovementLabLightingProfiles.Production.Tag, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Persisted weapon capture requires a production lighting profile.");
            if (probe.IsStale(MovementLabStage.Lighting) || probe.IsStale(MovementLabStage.BakedOutput))
                throw new InvalidOperationException("Persisted weapon capture requires current Lighting and BakedOutput stages.");
        }

        private static ImageEvidence CaptureImage(CaptureOptions options, PoseDefinition pose, int qualityIndex, string qualityName,
            GameObject player, Camera camera, GraphicsQualityRuntime qualityRuntime, GameObject viewmodels, GameObject requestedVisual,
            RenderTexture renderTarget, Vector3 sunward, string evidenceDirectory)
        {
            SetCapturePose(player, camera, sunward, pose.TargetAngle);
            var fastApplied = false;
            try
            {
                if (options.Mode == "Fast")
                {
                    MovementLabFastModeSession.EnterForCapture(qualityIndex);
                    MovementLabFastModeSession.AssertAppliedState(qualityIndex);
                    fastApplied = true;
                }
                else
                {
                    if (MovementLabFastModeSession.IsActive)
                        throw new InvalidOperationException("Persisted weapon capture unexpectedly found an active Fast session.");
                    QualitySettings.SetQualityLevel(qualityIndex, true);
                    qualityRuntime.ApplyCurrentQuality();
                    if (MovementLabFastModeSession.IsActive)
                        throw new InvalidOperationException("Persisted weapon capture unexpectedly entered Fast mode.");
                }

                camera.targetTexture = renderTarget;
                camera.aspect = (float)Width / Height;
                camera.Render();
                var imageName = options.Weapon.ToLowerInvariant() + "-" + pose.FileToken + "-" + qualityName.ToLowerInvariant() + ".png";
                var imagePath = Path.Combine(evidenceDirectory, imageName);
                var main = ReadPixels(camera, out var png);
                File.WriteAllBytes(imagePath, png);
                var control = CaptureControlPixels(camera, requestedVisual, renderTarget);
                var difference = AnalyzeDifference(main, control);
                var planarCameraForward = Planar(camera.transform.forward, "camera forward");
                var actualAngle = Vector3.SignedAngle(sunward, planarCameraForward, Vector3.up);
                var delta = Mathf.Abs(Mathf.DeltaAngle(actualAngle, pose.TargetAngle));
                if (delta > AngleTolerance)
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        "Weapon capture orientation mismatch for {0}/{1}: target={2:0.###}, actual={3:0.###}, delta={4:0.###}.",
                        pose.Name, qualityName, pose.TargetAngle, actualAngle, delta));
                if (!difference.Pass)
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        "Weapon visibility control failed for {0}/{1}: pixels={2}, meanAbsRgb={3:0.######}.",
                        pose.Name, qualityName, difference.PixelCount, difference.MeanAbsRgb));
                var hash = HashFile(imagePath);
                return new ImageEvidence
                {
                    filename = imageName,
                    path = imagePath,
                    view = pose.Name,
                    qualityLevel = qualityName,
                    qualityIndex = qualityIndex,
                    visualMode = options.Mode,
                    fastSessionApplied = fastApplied,
                    targetAngle = pose.TargetAngle,
                    actualAngle = actualAngle,
                    angleDelta = delta,
                    cameraForward = camera.transform.forward.normalized,
                    planarCameraForward = planarCameraForward,
                    weaponForward = requestedVisual.transform.forward.normalized,
                    playerPosition = player.transform.position,
                    playerEulerAngles = player.transform.eulerAngles,
                    playerLocalPosition = player.transform.localPosition,
                    cameraPosition = camera.transform.position,
                    cameraEulerAngles = camera.transform.eulerAngles,
                    cameraLocalEulerAngles = camera.transform.localEulerAngles,
                    weaponPosition = requestedVisual.transform.position,
                    weaponEulerAngles = requestedVisual.transform.eulerAngles,
                    fieldOfView = camera.fieldOfView,
                    differencePixelCount = difference.PixelCount,
                    differenceMeanAbsRgb = difference.MeanAbsRgb,
                    differenceMaxAbsRgb = difference.MaxAbsRgb,
                    controlRendered = true,
                    width = Width,
                    height = Height,
                    bytes = png.LongLength,
                    sha256 = hash,
                    pass = true
                };
            }
            finally
            {
                if (fastApplied || MovementLabFastModeSession.IsActive)
                    MovementLabFastModeSession.RestoreIfActive();
            }
        }

        private static Color32[] ReadPixels(Camera camera, out byte[] png)
        {
            RenderTexture.active = camera.targetTexture;
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
            try
            {
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                texture.Apply(false, false);
                png = texture.EncodeToPNG();
                if (png == null || png.Length == 0) throw new InvalidOperationException("Weapon PNG encoding returned no bytes.");
                return texture.GetPixels32();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static Color32[] CaptureControlPixels(Camera camera, GameObject requestedVisual, RenderTexture renderTarget)
        {
            var wasActive = requestedVisual.activeSelf;
            try
            {
                requestedVisual.SetActive(false);
                camera.targetTexture = renderTarget;
                camera.Render();
                return ReadPixels(camera, out _);
            }
            finally
            {
                requestedVisual.SetActive(wasActive);
            }
        }

        private static DifferenceEvidence AnalyzeDifference(Color32[] image, Color32[] control)
        {
            if (image == null || control == null || image.Length != Width * Height || control.Length != Width * Height)
                throw new InvalidOperationException("Weapon visibility control pixel dimensions are invalid.");
            var changed = 0;
            var total = 0.0;
            var max = 0.0;
            for (var topY = MaskTopYMin; topY <= MaskTopYMax; topY++)
            {
                var bottomRow = Height - 1 - topY;
                if (bottomRow < MaskBottomRowMin || bottomRow > MaskBottomRowMax)
                    throw new InvalidOperationException("Weapon visibility mask origin conversion is invalid.");
                for (var x = MaskXMin; x <= MaskXMax; x++)
                {
                    var index = bottomRow * Width + x;
                    var r = Math.Abs(image[index].r - control[index].r);
                    var g = Math.Abs(image[index].g - control[index].g);
                    var b = Math.Abs(image[index].b - control[index].b);
                    var localMax = Math.Max(r, Math.Max(g, b));
                    if (localMax >= DifferenceChannelFloor) changed++;
                    total += r + g + b;
                    max = Math.Max(max, Math.Max(r / 255.0, Math.Max(g / 255.0, b / 255.0)));
                }
            }
            var sampleCount = (MaskXMax - MaskXMin + 1) * (MaskTopYMax - MaskTopYMin + 1);
            return new DifferenceEvidence
            {
                PixelCount = changed,
                MeanAbsRgb = (float)(total / (sampleCount * 3.0 * 255.0)),
                MaxAbsRgb = (float)max
            };
        }

        private static void SetCapturePose(GameObject player, Camera camera, Vector3 sunward, float targetAngle)
        {
            player.transform.position = Vector3.zero;
            player.transform.localPosition = Vector3.zero;
            var baseYaw = Mathf.Atan2(sunward.x, sunward.z) * Mathf.Rad2Deg;
            player.transform.rotation = Quaternion.Euler(0f, baseYaw + targetAngle, 0f);
            camera.transform.localEulerAngles = new Vector3(CaptureCameraPitch, 0f, 0f);
            camera.fieldOfView = CaptureFieldOfView;
        }

        private static CameraState SaveCameraState(Camera camera)
        {
            return new CameraState
            {
                Camera = camera,
                TargetTexture = camera.targetTexture,
                Enabled = camera.enabled,
                ClearFlags = camera.clearFlags,
                Background = camera.backgroundColor,
                FieldOfView = camera.fieldOfView,
                NearClip = camera.nearClipPlane,
                FarClip = camera.farClipPlane,
                Aspect = camera.aspect,
                Rect = camera.rect,
                Position = camera.transform.position,
                Rotation = camera.transform.rotation,
                LocalPosition = camera.transform.localPosition,
                LocalRotation = camera.transform.localRotation,
                CullingMask = camera.cullingMask
            };
        }

        private static void RestoreCameraState(Camera camera, CameraState state)
        {
            if (camera == null || state.Camera == null || camera != state.Camera) return;
            camera.targetTexture = state.TargetTexture;
            camera.enabled = state.Enabled;
            camera.clearFlags = state.ClearFlags;
            camera.backgroundColor = state.Background;
            camera.fieldOfView = state.FieldOfView;
            camera.nearClipPlane = state.NearClip;
            camera.farClipPlane = state.FarClip;
            camera.aspect = state.Aspect;
            camera.rect = state.Rect;
            camera.cullingMask = state.CullingMask;
            camera.transform.position = state.Position;
            camera.transform.rotation = state.Rotation;
            camera.transform.localPosition = state.LocalPosition;
            camera.transform.localRotation = state.LocalRotation;
        }

        private static void RestoreObjectStates(ObjectActiveState[] states)
        {
            if (states == null) return;
            for (var index = 0; index < states.Length; index++)
            {
                if (states[index]?.Object != null)
                    states[index].Object.SetActive(states[index].Active);
            }
        }

        private static void RestoreBehaviourStates(BehaviourState[] states)
        {
            if (states == null) return;
            for (var index = 0; index < states.Length; index++)
            {
                if (states[index]?.Behaviour != null)
                    states[index].Behaviour.enabled = states[index].Enabled;
            }
        }

        private static Vector3 Planar(Vector3 value, string label)
        {
            value.y = 0f;
            if (value.sqrMagnitude < 0.000001f)
                throw new InvalidOperationException(label + " must have a non-zero planar direction.");
            return value.normalized;
        }

        private static T Require<T>(T value, string label) where T : UnityEngine.Object
        {
            if (value == null) throw new InvalidOperationException("Missing " + label + ".");
            return value;
        }

        private static string ResolveProjectPath(string projectRoot, string relativePath)
        {
            return Path.GetFullPath(Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ReadGitSha(string projectRoot)
        {
            var start = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --verify HEAD",
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Failed to start git.");
                var output = process.StandardOutput.ReadToEnd().Trim().ToLowerInvariant();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || output.Length != 40 || output.Any(character => !Uri.IsHexDigit(character)))
                    throw new InvalidOperationException("Unable to resolve exact Git HEAD SHA: " + error.Trim());
                return output;
            }
        }

        private static void WriteImmutableJson(string path, object value)
        {
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            if (File.Exists(path)) throw new InvalidOperationException("Immutable capture path already exists: " + path);
            var temporary = Path.Combine(directory, ".weapon-visual-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(value, true) + "\n", new UTF8Encoding(false));
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string HashFile(string path)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Required file missing: " + path);
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
