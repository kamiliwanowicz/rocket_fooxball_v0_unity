using RocketFooxball;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace RocketFooxball.Editor
{
    public static class MovementLabBuilder
    {
        private const string PrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        private const string ScenePath = "Assets/_Game/Scenes/MovementLab.unity";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string MaterialsPath = "Assets/_Game/Materials";

        [MenuItem("Rocket Fooxball/Build Movement Lab")]
        public static void BuildMovementLab()
        {
            EnsureFolders();
            var playerPrefab = BuildPlayerPrefab();
            var floorMaterial = GetOrCreateMaterial("Floor", new Color(0.16f, 0.32f, 0.19f));
            var wallMaterial = GetOrCreateMaterial("Wall", new Color(0.16f, 0.22f, 0.34f));
            var markingMaterial = GetOrCreateMaterial("Marking", new Color(0.9f, 0.9f, 0.9f));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var defaultCamera = Camera.main;
            if (defaultCamera != null)
            {
                Object.DestroyImmediate(defaultCamera.gameObject);
            }

            BuildArena(floorMaterial, wallMaterial, markingMaterial);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 0f, 32f);

            var hud = new GameObject("DebugHUD");
            var hudComponent = hud.AddComponent<MovementDebugHud>();
            var serializedHud = new SerializedObject(hudComponent);
            serializedHud.FindProperty("player").objectReferenceValue = player.GetComponent<PlayerMotor>();
            serializedHud.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterBuildScene();
            Physics.gravity = Vector3.down * GamePhysicsSettings.GravityMagnitude;
            SetProjectFixedTimestep();
            AssetDatabase.SaveAssets();
            Debug.Log("Rocket Fooxball Movement Lab built: " + ScenePath);
        }

        private static GameObject BuildPlayerPrefab()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null)
            {
                throw new System.InvalidOperationException("Missing Input System asset: " + InputActionsPath);
            }

            var root = new GameObject("Player");
            root.tag = "Player";
            var controller = root.AddComponent<CharacterController>();
            controller.radius = 0.4f;
            controller.height = 1.8f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 60f;
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.04f;

            var input = root.AddComponent<PlayerInputReader>();
            var motor = root.AddComponent<PlayerMotor>();
            var look = root.AddComponent<PlayerLook>();
            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 1.55f, 0f);
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(head, false);
            camera.tag = "MainCamera";
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.03f;
            camera.gameObject.AddComponent<AudioListener>();

            var inputSerialized = new SerializedObject(input);
            inputSerialized.FindProperty("actions").objectReferenceValue = actions;
            inputSerialized.ApplyModifiedPropertiesWithoutUndo();

            var motorSerialized = new SerializedObject(motor);
            motorSerialized.FindProperty("input").objectReferenceValue = input;
            motorSerialized.ApplyModifiedPropertiesWithoutUndo();
            var lookSerialized = new SerializedObject(look);
            lookSerialized.FindProperty("input").objectReferenceValue = input;
            lookSerialized.FindProperty("head").objectReferenceValue = head;
            lookSerialized.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void BuildArena(Material floorMaterial, Material wallMaterial, Material markingMaterial)
        {
            var arena = new GameObject("Arena");
            CreateSolid("Floor", arena.transform, new Vector3(0f, -0.5f, 0f), new Vector3(130f, 1f, 90f), floorMaterial);
            CreateSolid("NorthWall", arena.transform, new Vector3(0f, 4f, -44.5f), new Vector3(130f, 8f, 1f), wallMaterial);
            CreateSolid("SouthWall", arena.transform, new Vector3(0f, 4f, 44.5f), new Vector3(130f, 8f, 1f), wallMaterial);
            CreateSolid("EastWall", arena.transform, new Vector3(64.5f, 4f, 0f), new Vector3(1f, 8f, 88f), wallMaterial);
            CreateSolid("WestWall", arena.transform, new Vector3(-64.5f, 4f, 0f), new Vector3(1f, 8f, 88f), wallMaterial);

            var markings = new GameObject("Markings").transform;
            markings.SetParent(arena.transform, false);
            CreateMarking("CenterLine", markings, Vector3.zero, new Vector3(0.25f, 0.02f, 88f), markingMaterial);
            CreateMarking("NorthBox", markings, new Vector3(0f, 0.015f, -29f), new Vector3(36f, 0.02f, 0.25f), markingMaterial);
            CreateMarking("SouthBox", markings, new Vector3(0f, 0.015f, 29f), new Vector3(36f, 0.02f, 0.25f), markingMaterial);
            CreateMarking("CenterSpot", markings, new Vector3(0f, 0.015f, 0f), new Vector3(1f, 0.02f, 1f), markingMaterial);
        }

        private static void CreateSolid(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var solid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            solid.name = name;
            solid.transform.SetParent(parent, false);
            solid.transform.localPosition = position;
            solid.transform.localScale = size;
            solid.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void CreateMarking(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var marking = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marking.name = name;
            marking.transform.SetParent(parent, false);
            marking.transform.localPosition = position;
            marking.transform.localScale = size;
            Object.DestroyImmediate(marking.GetComponent<Collider>());
            marking.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void RegisterBuildScene()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            var path = MaterialsPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    throw new System.InvalidOperationException("URP Lit shader is unavailable.");
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetProjectFixedTimestep()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset");
            if (settings.Length == 0)
            {
                throw new System.InvalidOperationException("Unable to load ProjectSettings/TimeManager.asset.");
            }

            var serializedSettings = new SerializedObject(settings[0]);
            var fixedTimestep = serializedSettings.FindProperty("Fixed Timestep");
            if (fixedTimestep == null)
            {
                throw new System.InvalidOperationException("TimeManager 'Fixed Timestep' property is unavailable.");
            }

            // Unity 6 serializes this as a RationalTime: count / rate. The default
            // rate is 141,120,000 ticks/sec, so 60 Hz is exactly 2,352,000 ticks.
            var count = fixedTimestep.FindPropertyRelative("m_Count");
            var rate = fixedTimestep.FindPropertyRelative("m_Rate");
            var denominator = rate?.FindPropertyRelative("m_Denominator");
            var numerator = rate?.FindPropertyRelative("m_Numerator");
            if (count == null || denominator == null || numerator == null || denominator.longValue == 0)
            {
                throw new System.InvalidOperationException("TimeManager fixed-step RationalTime fields are unavailable.");
            }

            count.longValue = System.Convert.ToInt64(
                System.Math.Round((double)numerator.longValue / denominator.longValue * GamePhysicsSettings.FixedDeltaTime)
            );
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings[0]);
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/_Game");
            EnsureFolder(MaterialsPath);
            EnsureFolder("Assets/_Game/Prefabs");
            EnsureFolder("Assets/_Game/Scenes");
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets", System.IO.Path.GetFileName(path));
            }
        }
    }
}
