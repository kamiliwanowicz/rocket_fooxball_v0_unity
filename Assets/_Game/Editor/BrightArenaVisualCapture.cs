using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RocketFooxball;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace RocketFooxball.Editor
{
    /// <summary>
    /// Non-mutating, graphics-enabled evidence capture for the final MovementLab scene.
    /// This class never builds or saves Unity assets; all output is written to a fresh
    /// ignored directory below Temp/BrightArenaVisuals.
    /// </summary>
    public static class BrightArenaVisualCapture
    {
        private const string ScenePath = "Assets/_Game/Scenes/MovementLab.unity";
        private const string BuildManifestPath = "Assets/_Game/Generated/MovementLabBuildManifest.json";
        private const string EvidenceRoot = "Temp/BrightArenaVisuals";
        private const int Width = 1280;
        private const int Height = 720;
        private const int DepthBits = 24;
        private const int Samples = 1;
        private const int RendererCap = 80;
        private const int OpaquePassCap = 100;
        private const int TransparentRendererCap = 8;
        private const int TriangleCap = 50000;
        private const long TextureBytesCap = 8L * 1024L * 1024L;
        private const int TextureDimensionCap = 512;
        private const float MeanLuminanceFloor = 0.28f;
        private const float DarkPixelFractionCap = 0.35f;
        private const float ClippedPixelFractionCap = 0.25f;
        private const float DarkLuminance = 0.08f;
        private const float ClippedLuminance = 0.98f;
        private const int UniqueColorFloor = 32;

        private sealed class BuildManifestDto
        {
            public int schemaVersion;
            public string sourceSignature;
            public string generatedOutputFingerprint;
            public string unityVersion;
            public string[] fingerprintPaths;
        }

        [Serializable]
        private sealed class ManifestDto
        {
            public int schemaVersion = 1;
            public string captureUtc;
            public string evidenceDirectory;
            public SourceInfo source;
            public UnityInfo unity;
            public BuildInfo build;
            public CameraPose[] cameras;
            public ImageEvidence[] images;
            public BudgetEvidence budgets;
            public bool pass;
        }

        [Serializable]
        private sealed class SourceInfo
        {
            public string gitSha;
            public bool gitDirty;
        }

        [Serializable]
        private sealed class UnityInfo
        {
            public string unityVersion;
            public string renderPipeline;
            public string renderPipelineAsset;
            public string qualityLevel;
            public string graphicsDevice;
            public string graphicsDeviceVersion;
            public int graphicsMemoryMb;
            public int width;
            public int height;
            public string colorFormat;
            public int depthBits;
            public int samples;
        }

        [Serializable]
        private sealed class BuildInfo
        {
            public string manifestPath;
            public int manifestSchemaVersion;
            public string sourceSignature;
            public string generatedOutputFingerprint;
            public string scenePath = ScenePath;
        }

        [Serializable]
        private sealed class CameraPose
        {
            public string view;
            public string camera;
            public Vector3 position;
            public Vector3 eulerAngles;
            public float fieldOfView;
            public int cullingMask;
        }

        [Serializable]
        private sealed class ImageEvidence
        {
            public string view;
            public string path;
            public string sha256;
            public int width;
            public int height;
            public long bytes;
            public int uniqueColors;
            public float meanSrgbLuminance;
            public float darkPixelFraction;
            public float clippedPixelFraction;
            public bool pass;
        }

        [Serializable]
        private sealed class BudgetEvidence
        {
            public int enabledMeshRenderers;
            public int opaqueMaterialPasses;
            public int transparentRenderers;
            public int uniqueMeshes;
            public int visibleTriangles;
            public long authoredTextureRgbaBytes;
            public int maxTextureDimension;
            public int rendererCap = RendererCap;
            public int opaqueMaterialPassCap = OpaquePassCap;
            public int transparentRendererCap = TransparentRendererCap;
            public int triangleCap = TriangleCap;
            public long authoredTextureRgbaBytesCap = TextureBytesCap;
            public int textureDimensionCap = TextureDimensionCap;
            public bool pass;
        }

        private sealed class ViewDefinition
        {
            public string Name;
            public string FileName;
            public bool FirstPerson;
            public Vector3 Position;
            public Vector3 Target;
            public float FieldOfView;

            public ViewDefinition(string name, string fileName, bool firstPerson, Vector3 position, Vector3 target, float fieldOfView)
            {
                Name = name;
                FileName = fileName;
                FirstPerson = firstPerson;
                Position = position;
                Target = target;
                FieldOfView = fieldOfView;
            }
        }

        private struct CameraState
        {
            public Camera Camera;
            public Transform Transform;
            public RenderTexture TargetTexture;
            public bool Enabled;
            public CameraClearFlags ClearFlags;
            public Color Background;
            public float FieldOfView;
            public float NearClip;
            public float FarClip;
            public float Aspect;
            public Rect Rect;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public int CullingMask;
        }

        private struct ObjectState
        {
            public GameObject Object;
            public bool Active;
        }

        [MenuItem("Rocket Fooxball/Capture Bright Arena Visuals")]
        public static void Capture()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var evidenceDirectory = CreateEvidenceDirectory(projectRoot);
            var manifestPath = Path.Combine(evidenceDirectory, "BrightArenaVisualManifest.json");
            RenderTexture previousActive = null;
            RenderTexture renderTarget = null;
            GameObject externalCameraObject = null;
            Camera gameplayCamera = null;
            Camera externalCamera = null;
            CameraState gameplayState = default;
            ObjectState viewmodelsState = default;
            ObjectState crosshairState = default;
            var manifest = new ManifestDto
            {
                captureUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                evidenceDirectory = evidenceDirectory
            };

            try
            {
                if (!Application.isBatchMode)
                {
                    throw new InvalidOperationException("Bright arena capture requires Unity batch mode.");
                }
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                {
                    throw new InvalidOperationException("Bright arena capture requires graphics-enabled Unity; GraphicsDeviceType.Null is unsupported.");
                }

                // Validator is the single authoritative scene/manifest check. Do not build,
                // save, refresh, or otherwise mutate generated Unity assets from this path.
                MovementLabBuilder.ValidateMovementLab();
                var scene = EditorSceneManager.GetActiveScene();
                if (!scene.IsValid() || !string.Equals(scene.path, ScenePath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("MovementLab scene is not active after validation: " + scene.path);
                }
                if (scene.isDirty)
                {
                    throw new InvalidOperationException("MovementLab scene became dirty during validation; capture refuses to continue.");
                }

                var buildManifest = ReadBuildManifest(projectRoot);
                manifest.source = ReadSourceInfo(projectRoot);
                manifest.unity = ReadUnityInfo();
                manifest.build = new BuildInfo
                {
                    manifestPath = BuildManifestPath,
                    manifestSchemaVersion = buildManifest.schemaVersion,
                    sourceSignature = buildManifest.sourceSignature,
                    generatedOutputFingerprint = buildManifest.generatedOutputFingerprint
                };

                var player = Require(GameObject.Find("Player"), "Player root");
                gameplayCamera = Require(player.transform.Find("Head/Camera")?.GetComponent<Camera>(), "Player camera");
                var viewmodels = Require(gameplayCamera.transform.Find("Viewmodels")?.gameObject, "Viewmodels");
                var crosshair = Require(gameplayCamera.transform.Find("CrosshairCanvas")?.gameObject, "CrosshairCanvas");
                gameplayState = SaveCameraState(gameplayCamera);
                viewmodelsState = new ObjectState { Object = viewmodels, Active = viewmodels.activeSelf };
                crosshairState = new ObjectState { Object = crosshair, Active = crosshair.activeSelf };

                renderTarget = new RenderTexture(Width, Height, DepthBits, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "BrightArenaCaptureRT",
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

                // One warm render avoids first-view shader/import stalls while retaining
                // exactly six written PNGs in the ordered view list.
                gameplayCamera.Render();

                var views = CreateViews();
                var images = new List<ImageEvidence>(views.Length);
                var cameras = new List<CameraPose>(views.Length);
                for (var i = 0; i < views.Length; i++)
                {
                    var view = views[i];
                    Camera captureCamera;
                    if (view.FirstPerson)
                    {
                        captureCamera = gameplayCamera;
                        RestoreCameraState(gameplayCamera, gameplayState);
                        gameplayCamera.targetTexture = renderTarget;
                        gameplayCamera.aspect = (float)Width / Height;
                        viewmodels.SetActive(viewmodelsState.Active);
                        crosshair.SetActive(crosshairState.Active);
                    }
                    else
                    {
                        if (externalCameraObject == null)
                        {
                            externalCameraObject = new GameObject("__BrightArenaExternalCamera")
                            {
                                hideFlags = HideFlags.HideAndDontSave
                            };
                            externalCamera = externalCameraObject.AddComponent<Camera>();
                            externalCamera.hideFlags = HideFlags.HideAndDontSave;
                            externalCamera.enabled = false;
                            externalCamera.targetTexture = renderTarget;
                            externalCamera.clearFlags = CameraClearFlags.SolidColor;
                            externalCamera.backgroundColor = gameplayCamera.backgroundColor;
                            externalCamera.nearClipPlane = 0.05f;
                            externalCamera.farClipPlane = gameplayCamera.farClipPlane;
                        }
                        viewmodels.SetActive(false);
                        crosshair.SetActive(false);
                        externalCamera.cullingMask = ~0;
                        externalCamera.fieldOfView = view.FieldOfView;
                        externalCamera.transform.position = view.Position;
                        externalCamera.transform.rotation = Quaternion.LookRotation(view.Target - view.Position, Vector3.up);
                        externalCamera.aspect = (float)Width / Height;
                        captureCamera = externalCamera;
                    }

                    captureCamera.targetTexture = renderTarget;
                    captureCamera.Render();
                    cameras.Add(new CameraPose
                    {
                        view = view.Name,
                        camera = captureCamera.name,
                        position = captureCamera.transform.position,
                        eulerAngles = captureCamera.transform.eulerAngles,
                        fieldOfView = captureCamera.fieldOfView,
                        cullingMask = captureCamera.cullingMask
                    });
                    images.Add(CaptureImage(captureCamera, view, evidenceDirectory));
                }

                manifest.cameras = cameras.ToArray();
                manifest.images = images.ToArray();
                manifest.budgets = ScanBudgets();
                UnityEngine.Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "BRIGHT_ARENA_CAPTURE_BUDGET renderers={0} opaquePasses={1} transparentRenderers={2} uniqueMeshes={3} triangles={4} textureBytes={5} maxTextureDimension={6}",
                    manifest.budgets.enabledMeshRenderers, manifest.budgets.opaqueMaterialPasses, manifest.budgets.transparentRenderers,
                    manifest.budgets.uniqueMeshes, manifest.budgets.visibleTriangles, manifest.budgets.authoredTextureRgbaBytes, manifest.budgets.maxTextureDimension));
                if (!manifest.budgets.pass)
                {
                    throw new InvalidOperationException("Bright arena render budget exceeded; see manifest budget fields.");
                }
                for (var i = 0; i < manifest.images.Length; i++)
                {
                    if (!manifest.images[i].pass)
                    {
                        throw new InvalidOperationException("Bright arena image threshold failed: " + manifest.images[i].view);
                    }
                }

                manifest.pass = true;
                File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
                var evidenceAttributes = File.GetAttributes(evidenceDirectory);
                File.SetAttributes(evidenceDirectory, evidenceAttributes | FileAttributes.ReadOnly);
                LaunchEvidenceFinalizer(projectRoot, evidenceDirectory);
                UnityEngine.Debug.Log("BRIGHT_ARENA_CAPTURE_MANIFEST_WRITTEN " + File.Exists(manifestPath) + " " + manifestPath);
                UnityEngine.Debug.Log("BRIGHT_ARENA_CAPTURE_PASS " + manifestPath);
                UnityEngine.Debug.Log("BRIGHT_ARENA_CAPTURE_EVIDENCE " + evidenceDirectory);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                throw;
            }
            finally
            {
                if (gameplayCamera != null)
                {
                    RestoreCameraState(gameplayCamera, gameplayState);
                }
                if (viewmodelsState.Object != null)
                {
                    viewmodelsState.Object.SetActive(viewmodelsState.Active);
                }
                if (crosshairState.Object != null)
                {
                    crosshairState.Object.SetActive(crosshairState.Active);
                }
                if (externalCameraObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(externalCameraObject);
                }
                RenderTexture.active = previousActive;
                if (renderTarget != null)
                {
                    renderTarget.Release();
                    UnityEngine.Object.DestroyImmediate(renderTarget);
                }
            }
        }

        private static string CreateEvidenceDirectory(string projectRoot)
        {
            var root = Path.GetFullPath(Path.Combine(projectRoot, EvidenceRoot));
            var id = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            var directory = Path.Combine(root, id);
            if (Directory.Exists(directory))
            {
                throw new InvalidOperationException("Capture evidence directory already exists/non-empty: " + directory);
            }
            Directory.CreateDirectory(directory);
            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                throw new InvalidOperationException("Capture evidence directory was not empty: " + directory);
            }
            return directory;
        }

        private static BuildManifestDto ReadBuildManifest(string projectRoot)
        {
            var absolute = Path.Combine(projectRoot, BuildManifestPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                throw new InvalidOperationException("Build manifest is missing: " + BuildManifestPath);
            }
            var manifest = JsonUtility.FromJson<BuildManifestDto>(File.ReadAllText(absolute));
            if (manifest == null || manifest.schemaVersion <= 0 || string.IsNullOrEmpty(manifest.sourceSignature) || string.IsNullOrEmpty(manifest.generatedOutputFingerprint))
            {
                throw new InvalidOperationException("Build manifest is stale or incomplete: " + BuildManifestPath);
            }
            return manifest;
        }

        private static SourceInfo ReadSourceInfo(string projectRoot)
        {
            var sha = RunGit(projectRoot, "rev-parse HEAD").Trim();
            if (string.IsNullOrEmpty(sha))
            {
                throw new InvalidOperationException("Unable to resolve Git SHA for capture evidence.");
            }
            var status = RunGit(projectRoot, "status --porcelain --untracked-files=all");
            return new SourceInfo { gitSha = sha, gitDirty = !string.IsNullOrEmpty(status.Trim()) };
        }

        private static string RunGit(string projectRoot, string arguments)
        {
            var start = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Failed to start git.");
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("git " + arguments + " failed: " + error.Trim());
                }
                return output;
            }
        }

        private static UnityInfo ReadUnityInfo()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            return new UnityInfo
            {
                unityVersion = Application.unityVersion,
                renderPipeline = pipeline != null ? pipeline.GetType().FullName : "BuiltIn",
                renderPipelineAsset = pipeline != null ? pipeline.name : string.Empty,
                qualityLevel = QualitySettings.names.Length > QualitySettings.GetQualityLevel() ? QualitySettings.names[QualitySettings.GetQualityLevel()] : QualitySettings.GetQualityLevel().ToString(CultureInfo.InvariantCulture),
                graphicsDevice = SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + ")",
                graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion,
                graphicsMemoryMb = SystemInfo.graphicsMemorySize,
                width = Width,
                height = Height,
                colorFormat = "ARGB32",
                depthBits = DepthBits,
                samples = Samples
            };
        }

        private static ViewDefinition[] CreateViews()
        {
            return new[]
            {
                new ViewDefinition("FirstPersonSpawn", "01_FirstPersonSpawn.png", true, Vector3.zero, Vector3.zero, 75f),
                new ViewDefinition("ArenaOverview", "02_ArenaOverview.png", false, new Vector3(0f, 68f, -92f), new Vector3(0f, 2.5f, 0f), 60f),
                new ViewDefinition("NorthGoalThreeQuarter", "03_NorthGoalThreeQuarter.png", false, new Vector3(-45f, 11f, -30f), new Vector3(-64f, 3.5f, 0f), 55f),
                new ViewDefinition("SouthGoalThreeQuarter", "04_SouthGoalThreeQuarter.png", false, new Vector3(45f, 11f, 30f), new Vector3(64f, 3.5f, 0f), 55f),
                new ViewDefinition("RampArchitectureDetail", "05_RampArchitectureDetail.png", false, new Vector3(-8f, 13f, -19f), new Vector3(-22f, 3.5f, 2f), 58f),
                new ViewDefinition("ExternalCharacterBallDetail", "06_ExternalCharacterBallDetail.png", false, new Vector3(10f, 5.5f, -13f), new Vector3(2f, 2.4f, 0f), 52f)
            };
        }

        private static ImageEvidence CaptureImage(Camera camera, ViewDefinition view, string evidenceDirectory)
        {
            var absolutePath = Path.Combine(evidenceDirectory, view.FileName);
            RenderTexture.active = camera.targetTexture;
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
            try
            {
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                texture.Apply(false, false);
                var png = texture.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    throw new InvalidOperationException("PNG encoding returned no bytes: " + view.Name);
                }
                File.WriteAllBytes(absolutePath, png);
                return AnalyzeImage(absolutePath, view.Name, png);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static ImageEvidence AnalyzeImage(string path, string viewName, byte[] png)
        {
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!decoded.LoadImage(png, false) || decoded.width != Width || decoded.height != Height)
                {
                    throw new InvalidOperationException("PNG decode/dimensions invalid: " + viewName);
                }
                var pixels = decoded.GetPixels32();
                var colors = new HashSet<int>();
                var luminanceTotal = 0.0;
                var dark = 0;
                var clipped = 0;
                for (var i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    colors.Add(pixel.r | (pixel.g << 8) | (pixel.b << 16) | (pixel.a << 24));
                    var luminance = (0.2126 * pixel.r + 0.7152 * pixel.g + 0.0722 * pixel.b) / 255.0;
                    luminanceTotal += luminance;
                    if (luminance < DarkLuminance) dark++;
                    if (luminance > ClippedLuminance) clipped++;
                }
                var count = Math.Max(1, pixels.Length);
                var mean = (float)(luminanceTotal / count);
                var darkFraction = (float)dark / count;
                var clippedFraction = (float)clipped / count;
                var evidence = new ImageEvidence
                {
                    view = viewName,
                    path = path,
                    sha256 = Sha256(png),
                    width = decoded.width,
                    height = decoded.height,
                    bytes = png.LongLength,
                    uniqueColors = colors.Count,
                    meanSrgbLuminance = mean,
                    darkPixelFraction = darkFraction,
                    clippedPixelFraction = clippedFraction,
                    pass = png.Length > 0 && colors.Count >= UniqueColorFloor && mean >= MeanLuminanceFloor && darkFraction <= DarkPixelFractionCap && clippedFraction <= ClippedPixelFractionCap
                };
                return evidence;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(decoded);
            }
        }

        private static BudgetEvidence ScanBudgets()
        {
            var evidence = new BudgetEvidence();
            var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var meshes = new HashSet<Mesh>();
            var opaqueMaterials = new HashSet<Material>();
            var activeRenderers = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                activeRenderers++;
                var transparent = false;
                var materials = renderer.sharedMaterials ?? Array.Empty<Material>();
                for (var j = 0; j < materials.Length; j++)
                {
                    var material = materials[j];
                    if (material == null) continue;
                    var queue = material.renderQueue > 0 ? material.renderQueue : (material.shader != null ? material.shader.renderQueue : 2000);
                    transparent |= queue >= 3000 || string.Equals(material.GetTag("RenderType", false, string.Empty), "Transparent", StringComparison.OrdinalIgnoreCase);
                    if (queue < 3000)
                    {
                        // Count each distinct opaque material's shader passes once.
                        // Repeated architecture slots share the same pass program;
                        // counting every binding would report duplicate shader cost.
                        if (opaqueMaterials.Add(material))
                        {
                            evidence.opaqueMaterialPasses += Math.Max(1, material.passCount);
                        }
                    }
                }
                if (transparent) evidence.transparentRenderers++;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                meshes.Add(mesh);
                for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    evidence.visibleTriangles += (int)(mesh.GetIndexCount(submesh) / 3u);
                }
            }

            evidence.enabledMeshRenderers = activeRenderers;
            evidence.uniqueMeshes = meshes.Count;
            var texturePaths = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/_Game" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            for (var i = 0; i < texturePaths.Length; i++)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePaths[i]);
                if (texture == null) continue;
                evidence.maxTextureDimension = Math.Max(evidence.maxTextureDimension, Math.Max(texture.width, texture.height));
                evidence.authoredTextureRgbaBytes += (long)texture.width * texture.height * 4L;
            }
            evidence.pass = evidence.enabledMeshRenderers <= RendererCap && evidence.opaqueMaterialPasses <= OpaquePassCap && evidence.transparentRenderers <= TransparentRendererCap && evidence.visibleTriangles <= TriangleCap && evidence.authoredTextureRgbaBytes <= TextureBytesCap && evidence.maxTextureDimension <= TextureDimensionCap;
            return evidence;
        }

        private static CameraState SaveCameraState(Camera camera)
        {
            return new CameraState
            {
                Camera = camera,
                Transform = camera.transform,
                TargetTexture = camera.targetTexture,
                Enabled = camera.enabled,
                ClearFlags = camera.clearFlags,
                Background = camera.backgroundColor,
                FieldOfView = camera.fieldOfView,
                NearClip = camera.nearClipPlane,
                FarClip = camera.farClipPlane,
                Aspect = camera.aspect,
                Rect = camera.rect,
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
            state.Transform.localPosition = state.LocalPosition;
            state.Transform.localRotation = state.LocalRotation;
        }

        private static T Require<T>(T value, string label) where T : UnityEngine.Object
        {
            if (value == null) throw new InvalidOperationException("Missing " + label + ".");
            return value;
        }

        private static string Sha256(byte[] bytes)
        {
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void LaunchEvidenceFinalizer(string projectRoot, string evidenceDirectory)
        {
            var scriptPath = Path.Combine(projectRoot, "Tools/Validation/Capture-BrightArenaVisuals.ps1".Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(scriptPath)) throw new InvalidOperationException("Capture wrapper missing for evidence finalization: " + scriptPath);
            var escapedScript = scriptPath.Replace("\"", "\\\"");
            var escapedDirectory = evidenceDirectory.Replace("\"", "\\\"");
            var finalizerLog = Path.Combine(projectRoot, EvidenceRoot.Replace('/', Path.DirectorySeparatorChar), "Finalizer-" + Path.GetFileName(evidenceDirectory) + ".log");
            var escapedLog = finalizerLog.Replace("\"", "\\\"");
            var arguments = string.Format(CultureInfo.InvariantCulture,
                "-NoProfile -ExecutionPolicy Bypass -File \"{0}\" -FinalizeEvidence -SourceDirectory \"{1}\" -TargetDirectory \"{1}\" -ParentPid {2} -FinalizerLog \"{3}\"",
                escapedScript, escapedDirectory, Process.GetCurrentProcess().Id, escapedLog);
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (Process.Start(start) == null) throw new InvalidOperationException("Failed to start evidence finalizer.");
        }
    }
}
