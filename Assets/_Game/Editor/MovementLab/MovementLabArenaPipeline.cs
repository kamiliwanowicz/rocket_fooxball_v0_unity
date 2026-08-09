using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Diagnostics;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Physics;
using RocketFooxball.Runtime.Rendering;
using RocketFooxball.Runtime.Weapons;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static RocketFooxball.Editor.MovementLabSerializedProperties;
using MaterialSpecification = RocketFooxball.Editor.MovementLabContract.MaterialSpecification;
using PbrMaterialSpecification = RocketFooxball.Editor.MovementLabContract.PbrMaterialSpecification;
using WorldAnimatorConditionSpecification = RocketFooxball.Editor.MovementLabContract.WorldAnimatorConditionSpecification;
using WorldAnimatorTransitionSpecification = RocketFooxball.Editor.MovementLabContract.WorldAnimatorTransitionSpecification;

using static RocketFooxball.Editor.MovementLabBuildContext;
using static RocketFooxball.Editor.MovementLabImportPipeline;
using static RocketFooxball.Editor.MovementLabMaterialPipeline;
using static RocketFooxball.Editor.MovementLabAnimatorPipeline;
using static RocketFooxball.Editor.MovementLabPrefabPipeline;
using static RocketFooxball.Editor.MovementLabArenaPipeline;
using static RocketFooxball.Editor.MovementLabLightingPipeline;
using static RocketFooxball.Editor.MovementLabSceneComposer;
using static RocketFooxball.Editor.MovementLabValidator;
namespace RocketFooxball.Editor
{
    internal static partial class MovementLabArenaPipeline
    {
        internal static void Validate()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid() || !string.Equals(scene.path, MovementLabContract.ScenePath, System.StringComparison.Ordinal)) throw new System.InvalidOperationException("MovementLab arena validation requires active generated scene: " + MovementLabContract.ScenePath);
            var arena = UnityEngine.GameObject.Find("Arena");
            if (arena == null) throw new System.InvalidOperationException("Missing Arena root.");
            RequireChild(arena.transform, "Containment"); RequireChild(arena.transform, "NorthGoal"); RequireChild(arena.transform, "SouthGoal");
        }
        internal static void RequireChild(UnityEngine.Transform root, string name)
        { if (root.Find(name) == null) throw new System.InvalidOperationException("Arena missing required child: " + name); }
    }

    internal static partial class MovementLabArenaPipeline
    {
                internal static ArenaBuild BuildArena(Material floorMaterial, Material wallMaterial, Material markingMaterial, Material frameMaterial, Material shieldMaterial, PhysicsMaterial ballSurface, Material arenaPrimaryMaterial, Material arenaTrimMaterial, Material arenaHazardMaterial, Material arenaGlowMaterial, Material gridCeilingMaterial, Material gridLongWallMaterial, Material gridEndWallMaterial, Material northShieldMaterial, Material southShieldMaterial)
                {
                    var arena = new GameObject("Arena");
                    CreateSolid("Floor", arena.transform, new Vector3(0f, -0.5f, 0f), new Vector3(130f, 1f, 90f), floorMaterial, ballSurface);
                    // Longest arena axis runs along X. Goals occupy opposite X ends;
                    // north/south walls therefore remain solid while end walls split
                    // around each goal opening.
                    CreateSolid("NorthWall", arena.transform, new Vector3(0f, 4f, -44.5f), new Vector3(130f, 8f, 1f), wallMaterial, ballSurface);
                    CreateSolid("SouthWall", arena.transform, new Vector3(0f, 4f, 44.5f), new Vector3(130f, 8f, 1f), wallMaterial, ballSurface);
                    const float endWallSegmentSpan = 26.5f;
                    CreateSolid("WestWallNorth", arena.transform, new Vector3(-64.5f, 4f, -31.75f), new Vector3(1f, 8f, endWallSegmentSpan), wallMaterial, ballSurface);
                    CreateSolid("WestWallSouth", arena.transform, new Vector3(-64.5f, 4f, 31.75f), new Vector3(1f, 8f, endWallSegmentSpan), wallMaterial, ballSurface);
                    CreateSolid("EastWallNorth", arena.transform, new Vector3(64.5f, 4f, -31.75f), new Vector3(1f, 8f, endWallSegmentSpan), wallMaterial, ballSurface);
                    CreateSolid("EastWallSouth", arena.transform, new Vector3(64.5f, 4f, 31.75f), new Vector3(1f, 8f, endWallSegmentSpan), wallMaterial, ballSurface);

                    // Each ramp rises from midfield toward its nearest X-axis goal.
                    CreateSolid("RampWest", arena.transform, new Vector3(-22f, 2.1f, 2f), new Vector3(18f, 0.5f, 20f), wallMaterial, ballSurface, Quaternion.Euler(-15f, -90f, 0f));
                    CreateSolid("RampEast", arena.transform, new Vector3(22f, 2.1f, -2f), new Vector3(18f, 0.5f, 20f), wallMaterial, ballSurface, Quaternion.Euler(-15f, 90f, 0f));

                    var markings = new GameObject("Markings").transform;
                    markings.SetParent(arena.transform, false);
                    CreateMarking("CenterLine", markings, Vector3.zero, new Vector3(126f, 0.02f, 0.25f), markingMaterial);
                    CreateMarking("WestBox", markings, new Vector3(-29f, 0.015f, 0f), new Vector3(0.25f, 0.02f, 36f), markingMaterial);
                    CreateMarking("EastBox", markings, new Vector3(29f, 0.015f, 0f), new Vector3(0.25f, 0.02f, 36f), markingMaterial);
                    CreateMarking("CenterSpot", markings, new Vector3(0f, 0.015f, 0f), new Vector3(1f, 0.02f, 1f), markingMaterial);

                    var north = BuildGoal("NorthGoal", GoalTrigger.GoalSide.North, new Vector3(-GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), frameMaterial, northShieldMaterial, wallMaterial, ballSurface);
                    var south = BuildGoal("SouthGoal", GoalTrigger.GoalSide.South, new Vector3(GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), frameMaterial, southShieldMaterial, wallMaterial, ballSurface);
                    north.Root.transform.SetParent(arena.transform, true);
                    south.Root.transform.SetParent(arena.transform, true);

                    var containment = new GameObject("Containment").transform;
                    containment.SetParent(arena.transform, false);
                    CreateContainment("FloorContainment", containment, new Vector3(0f, -4f, 0f), new Vector3(140f, 1f, 120f), ballSurface);
                    CreateContainment("CeilingContainment", containment, new Vector3(0f, 48.5f, 0f), new Vector3(130f, 1f, 90f), ballSurface);
                    CreateContainment("EastContainment", containment, new Vector3(64.5f, 28f, 0f), new Vector3(1f, 42f, 90f), ballSurface);
                    CreateContainment("WestContainment", containment, new Vector3(-64.5f, 28f, 0f), new Vector3(1f, 42f, 90f), ballSurface);
                    CreateContainment("NorthContainment", containment, new Vector3(0f, 28f, -44.5f), new Vector3(130f, 42f, 1f), ballSurface);
                    CreateContainment("SouthContainment", containment, new Vector3(0f, 28f, 44.5f), new Vector3(130f, 42f, 1f), ballSurface);
                    CreateContainment("WestGoalOpeningContainment", containment, new Vector3(-67f, 3.5f, 0f), new Vector3(1f, 8f, 38f), ballSurface);
                    CreateContainment("EastGoalOpeningContainment", containment, new Vector3(67f, 3.5f, 0f), new Vector3(1f, 8f, 38f), ballSurface);

                    var gridVisuals = new GameObject("GridVisuals").transform;
                    gridVisuals.SetParent(containment, false);
                    CreateGridVisual("CeilingGrid", gridVisuals, new Vector3(0f, 47.98f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector2(130f, 90f), gridCeilingMaterial);
                    CreateGridVisual("NorthUpperGrid", gridVisuals, new Vector3(0f, 28f, -43.98f), Quaternion.identity, new Vector2(130f, 40f), gridLongWallMaterial);
                    CreateGridVisual("SouthUpperGrid", gridVisuals, new Vector3(0f, 28f, 43.98f), Quaternion.Euler(0f, 180f, 0f), new Vector2(130f, 40f), gridLongWallMaterial);
                    CreateGridVisual("WestUpperGrid", gridVisuals, new Vector3(-63.98f, 28f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector2(90f, 40f), gridEndWallMaterial);
                    CreateGridVisual("EastUpperGrid", gridVisuals, new Vector3(63.98f, 28f, 0f), Quaternion.Euler(0f, -90f, 0f), new Vector2(90f, 40f), gridEndWallMaterial);

                    BuildArenaArchitecture(arena.transform, north, south, new[] { arenaPrimaryMaterial, arenaTrimMaterial, arenaHazardMaterial, arenaGlowMaterial });

                    return new ArenaBuild
                    {
                        Root = arena,
                        NorthGoal = north,
                        SouthGoal = south,
                        Shields = new[] { north.Shield, south.Shield }
                    };
                }

                internal static GoalBuild BuildGoal(string name, GoalTrigger.GoalSide side, Vector3 position, Quaternion rotation, Material frameMaterial, Material shieldMaterial, Material wallMaterial, PhysicsMaterial ballSurface)
                {
                    var root = new GameObject(name);
                    root.transform.SetPositionAndRotation(position, rotation);
                    var triggerCollider = root.AddComponent<BoxCollider>();
                    var trigger = root.AddComponent<GoalTrigger>();
                    triggerCollider.isTrigger = true;
                    triggerCollider.center = new Vector3(0f, 3.5f, 0f);
                    triggerCollider.size = new Vector3(36f, 7f, 0.5f);
                    SetEnum(trigger, "goalSide", side == GoalTrigger.GoalSide.North ? "North" : "South");
                    SetVector3(trigger, "planeNormal", Vector3.right);
                    SetFloat(trigger, "openingHalfWidth", 18f);
                    SetFloat(trigger, "openingMinHeight", 0f);
                    SetFloat(trigger, "openingMaxHeight", 7f);
                    SetFloat(trigger, "rearmDistance", 0.5f);
                    SetObjectReference(trigger, "planeReference", root.transform);
                    SetObjectReference(trigger, "openingTrigger", triggerCollider);

                    var shield = new GameObject("ShieldCollider");
                    shield.transform.SetParent(root.transform, false);
                    shield.transform.localPosition = new Vector3(0f, 3.5f, 0f);
                    var shieldCollider = shield.AddComponent<BoxCollider>();
                    shieldCollider.size = new Vector3(36f, 7f, 0.4f);
                    shieldCollider.sharedMaterial = ballSurface;
                    var shieldVisual = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    shieldVisual.name = "ShieldVisual";
                    shieldVisual.transform.SetParent(root.transform, false);
                    shieldVisual.transform.localPosition = new Vector3(0f, 3.5f, -0.22f);
                    shieldVisual.transform.localScale = new Vector3(36f, 7f, 1f);
                    UnityEngine.Object.DestroyImmediate(shieldVisual.GetComponent<Collider>());
                    shieldVisual.GetComponent<Renderer>().sharedMaterial = shieldMaterial;
                    var frameWest = CreateSolid("FrameWest", root.transform, new Vector3(-18.5f, 3.5f, 0f), new Vector3(1f, 7f, 1f), frameMaterial, ballSurface);
                    var frameEast = CreateSolid("FrameEast", root.transform, new Vector3(18.5f, 3.5f, 0f), new Vector3(1f, 7f, 1f), frameMaterial, ballSurface);
                    var frameTop = CreateSolid("FrameTop", root.transform, new Vector3(0f, 7.5f, 0f), new Vector3(38f, 1f, 1f), frameMaterial, ballSurface);
                    frameWest.GetComponent<Renderer>().enabled = false;
                    frameEast.GetComponent<Renderer>().enabled = false;
                    frameTop.GetComponent<Renderer>().enabled = false;
                    // Local +Z points outward for both rotated goal roots.
                    CreateSolid("RecessWest", root.transform, new Vector3(-18.5f, 3.5f, 4.5f), new Vector3(1f, 7f, 9f), wallMaterial, ballSurface);
                    CreateSolid("RecessEast", root.transform, new Vector3(18.5f, 3.5f, 4.5f), new Vector3(1f, 7f, 9f), wallMaterial, ballSurface);
                    CreateSolid("RecessFloor", root.transform, new Vector3(0f, -0.25f, 4.5f), new Vector3(37f, 0.5f, 9f), wallMaterial, ballSurface);
                    var recessBack = CreateSolid("RecessBack", root.transform, new Vector3(0f, 3.5f, 9f), new Vector3(37f, 7f, 1f), wallMaterial, ballSurface);
                    var recesses = root.GetComponentsInChildren<Renderer>(true);
                    for (var i = 0; i < recesses.Length; i++)
                    {
                        if (recesses[i].gameObject.name.StartsWith("Recess", StringComparison.Ordinal)) recesses[i].enabled = false;
                    }
                    return new GoalBuild { Root = root, Trigger = trigger, Shield = shieldCollider };
                }

                internal static void BuildArenaArchitecture(Transform arenaRoot, GoalBuild northGoal, GoalBuild southGoal, Material[] arenaMaterials)
                {
                    var architecture = new GameObject("Architecture").transform;
                    architecture.SetParent(arenaRoot, false);

                    var northShell = CreateArenaKitVisual(architecture, "NorthGoalShell", "ArenaGoalShell", new Vector3(-GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), arenaMaterials);
                    var southShell = CreateArenaKitVisual(architecture, "SouthGoalShell", "ArenaGoalShell", new Vector3(GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), arenaMaterials);
                    northShell.transform.localScale = Vector3.one;
                    southShell.transform.localScale = Vector3.one;

                    CreateArenaKitVisual(architecture, "RampWestRails", "ArenaRampRails", new Vector3(-22f, 2.1f, 2f), Quaternion.Euler(-15f, -90f, 0f), arenaMaterials);
                    CreateArenaKitVisual(architecture, "RampEastRails", "ArenaRampRails", new Vector3(22f, 2.1f, -2f), Quaternion.Euler(-15f, 90f, 0f), arenaMaterials);

                    // A symmetric nine-per-wall truss cadence keeps the complete
                    // loaded scene inside the strict MeshRenderer budget.
                    for (var x = -48f; x <= 48f; x += 12f)
                    {
                        CreateArenaKitVisual(architecture, "NorthTruss_" + x.ToString("0"), "ArenaPerimeterTruss", new Vector3(x, 9.0f, -45.0f), Quaternion.identity, arenaMaterials);
                        CreateArenaKitVisual(architecture, "SouthTruss_" + x.ToString("0"), "ArenaPerimeterTruss", new Vector3(x, 9.0f, 45.0f), Quaternion.identity, arenaMaterials);
                    }

                    for (var x = -48f; x <= 48f; x += 24f)
                    {
                        CreateArenaKitVisual(architecture, "NorthWallPylon_" + x.ToString("0"), "ArenaWallPylon", new Vector3(x, 0f, -44f), Quaternion.identity, arenaMaterials);
                        CreateArenaKitVisual(architecture, "SouthWallPylon_" + x.ToString("0"), "ArenaWallPylon", new Vector3(x, 0f, 44f), Quaternion.Euler(0f, 180f, 0f), arenaMaterials);
                    }

                    CreateArenaKitVisual(architecture, "NorthScoreboard", "ArenaScoreboard", new Vector3(-64f, 12f, -2.5f), Quaternion.Euler(0f, -90f, 0f), arenaMaterials);
                    CreateArenaKitVisual(architecture, "SouthScoreboard", "ArenaScoreboard", new Vector3(64f, 12f, 2.5f), Quaternion.Euler(0f, 90f, 0f), arenaMaterials);
                }

                internal static GameObject CreateArenaKitVisual(Transform parent, string name, string meshName, Vector3 localPosition, Quaternion localRotation, Material[] arenaMaterials)
                {
                    var mesh = FindArenaKitMesh(meshName);
                    var visual = new GameObject(name);
                    visual.transform.SetParent(parent, false);
                    visual.transform.localPosition = localPosition;
                    visual.transform.localRotation = localRotation;
                    visual.transform.localScale = Vector3.one;
                    var filter = visual.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    var renderer = visual.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = ResolveArenaKitMaterials(meshName, arenaMaterials);
                    visual.isStatic = true;
                    return visual;
                }

                internal static Mesh FindArenaKitMesh(string meshName)
                {
                    var assets = AssetDatabase.LoadAllAssetsAtPath(ArenaKitModelPath);
                    for (var i = 0; i < assets.Length; i++)
                    {
                        var mesh = assets[i] as Mesh;
                        if (mesh != null && string.Equals(mesh.name, meshName + "Mesh", StringComparison.Ordinal)) return mesh;
                        if (mesh != null && string.Equals(mesh.name, meshName, StringComparison.Ordinal)) return mesh;
                    }
                    var names = new List<string>();
                    for (var i = 0; i < assets.Length; i++) if (assets[i] != null) names.Add(assets[i].name + "[" + assets[i].GetType().Name + "]");
                    throw new InvalidOperationException("Missing ArenaKit mesh subasset: " + meshName + "; imported assets=" + string.Join(",", names.ToArray()));
                }

                internal static Material[] ResolveArenaKitMaterials(string meshName, Material[] allMaterials)
                {
                    if (allMaterials == null || allMaterials.Length != 4) throw new InvalidOperationException("ArenaKit material palette is incomplete.");
                    if (meshName == "ArenaWallPylon" || meshName == "ArenaScoreboard") return new[] { allMaterials[0], allMaterials[1], allMaterials[3] };
                    if (meshName == "ArenaPerimeterTruss") return new[] { allMaterials[0], allMaterials[1] };
                    return new[] { allMaterials[0], allMaterials[1], allMaterials[2], allMaterials[3] };
                }

                internal static GameObject CreateSolid(string name, Transform parent, Vector3 position, Vector3 size, Material material, PhysicsMaterial ballSurface, Quaternion rotation = default)
                {
                    var solid = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    solid.name = name;
                    solid.transform.SetParent(parent, false);
                    solid.transform.localPosition = position;
                    solid.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
                    solid.transform.localScale = size;
                    solid.GetComponent<Renderer>().sharedMaterial = material;
                    var collider = solid.GetComponent<Collider>();
                    collider.sharedMaterial = ballSurface;
                    return solid;
                }

                internal static void CreateContainment(string name, Transform parent, Vector3 position, Vector3 size, PhysicsMaterial ballSurface)
                {
                    var containment = new GameObject(name);
                    containment.transform.SetParent(parent, false);
                    containment.transform.localPosition = position;
                    var collider = containment.AddComponent<BoxCollider>();
                    collider.size = size;
                    collider.sharedMaterial = ballSurface;
                }

                internal static void CreateGridVisual(string name, Transform parent, Vector3 position, Quaternion rotation, Vector2 size, Material material)
                {
                    var grid = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    grid.name = name;
                    grid.transform.SetParent(parent, false);
                    grid.transform.localPosition = position;
                    grid.transform.localRotation = rotation;
                    grid.transform.localScale = new Vector3(size.x, size.y, 1f);
                    UnityEngine.Object.DestroyImmediate(grid.GetComponent<Collider>());
                    grid.isStatic = true;
                    var renderer = grid.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    renderer.allowOcclusionWhenDynamic = false;
                }

                internal static void CreateMarking(string name, Transform parent, Vector3 position, Vector3 size, Material material)
                {
                    var marking = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    marking.name = name;
                    marking.transform.SetParent(parent, false);
                    marking.transform.localPosition = position;
                    marking.transform.localScale = size;
                    UnityEngine.Object.DestroyImmediate(marking.GetComponent<Collider>());
                    marking.GetComponent<Renderer>().sharedMaterial = material;
                }

                internal static void ValidateArenaMaterials(GameObject arena, PhysicsMaterial ballSurface)
                {
                    var floor = Require(arena.transform.Find("Floor"), "Arena Floor");
                    var floorRenderer = Require(floor.GetComponent<Renderer>(), "Arena Floor renderer");
                    ValidatePbrMaterial(floorRenderer.sharedMaterial, LoadTexture(GrassTexturePath), LoadTexture(GrassNormalTexturePath), LoadTexture(GrassMetallicTexturePath), LoadTexture(GrassOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(32.5f, 22.5f), "Floor");
                    ValidatePbrScalars(floorRenderer.sharedMaterial, 1f, 1f, 0.75f, 0.65f, 0f, "Floor");
                    ValidateEmission(floorRenderer.sharedMaterial, Color.clear, 0f, "Floor");
                    ValidatePbrMaterial(Require(arena.transform.Find("NorthWall").GetComponent<Renderer>(), "NorthWall renderer").sharedMaterial, LoadTexture(WallTexturePath), LoadTexture(WallNormalTexturePath), LoadTexture(WallMetallicTexturePath), LoadTexture(WallOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(8f, 2f), "Wall");
                    ValidatePbrScalars(Require(arena.transform.Find("NorthWall").GetComponent<Renderer>(), "NorthWall renderer").sharedMaterial, 1f, 1f, 0.80f, 0.80f, 0f, "Wall");
                    ValidateEmission(Require(arena.transform.Find("NorthWall").GetComponent<Renderer>(), "NorthWall renderer").sharedMaterial, Color.clear, 0f, "Wall");
                    ValidatePbrMaterial(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Trim.mat"), LoadTexture(TrimTexturePath), LoadTexture(TrimNormalTexturePath), LoadTexture(TrimMetallicTexturePath), LoadTexture(TrimOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(4f, 1f), "Trim");
                    ValidatePbrScalars(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Trim.mat"), 1f, 1f, 0.80f, 1f, 0f, "Trim");
                    ValidateEmission(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Trim.mat"), Color.clear, 0f, "Trim");
                    ValidatePbrMaterial(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Hazard.mat"), LoadTexture(HazardTexturePath), LoadTexture(HazardNormalTexturePath), LoadTexture(HazardMetallicTexturePath), LoadTexture(HazardOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(4f, 1f), "Hazard");
                    ValidatePbrScalars(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Hazard.mat"), 1f, 1f, 0.80f, 0.75f, 0f, "Hazard");
                    ValidateEmission(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Hazard.mat"), Color.clear, 0f, "Hazard");
                    ValidatePbrMaterial(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaPrimary.mat"), LoadTexture(WallTexturePath), LoadTexture(WallNormalTexturePath), LoadTexture(WallMetallicTexturePath), LoadTexture(WallOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, "ArenaPrimary");
                    ValidatePbrScalars(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaPrimary.mat"), 1f, 1f, 0.80f, 0.80f, 0f, "ArenaPrimary");
                    ValidateEmission(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaPrimary.mat"), Color.clear, 0f, "ArenaPrimary");
                    ValidatePbrMaterial(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaTrim.mat"), LoadTexture(TrimTexturePath), LoadTexture(TrimNormalTexturePath), LoadTexture(TrimMetallicTexturePath), LoadTexture(TrimOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, "ArenaTrim");
                    ValidatePbrScalars(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaTrim.mat"), 1f, 1f, 0.80f, 1f, 0f, "ArenaTrim");
                    ValidateEmission(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaTrim.mat"), Color.clear, 0f, "ArenaTrim");
                    ValidatePbrMaterial(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaHazard.mat"), LoadTexture(HazardTexturePath), LoadTexture(HazardNormalTexturePath), LoadTexture(HazardMetallicTexturePath), LoadTexture(HazardOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, "ArenaHazard");
                    ValidatePbrScalars(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaHazard.mat"), 1f, 1f, 0.80f, 0.75f, 0f, "ArenaHazard");
                    ValidateEmission(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaHazard.mat"), Color.clear, 0f, "ArenaHazard");
                    ValidatePbrMaterial(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaGlow.mat"), LoadTexture(TrimTexturePath), LoadTexture(TrimNormalTexturePath), LoadTexture(TrimMetallicTexturePath), LoadTexture(TrimOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, "ArenaGlow");
                    ValidatePbrScalars(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaGlow.mat"), 1f, 1f, 0.80f, 1f, 2f, "ArenaGlow");
                    ValidateEmission(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaGlow.mat"), new Color(0.10f, 0.95f, 0.88f, 1f), 2f, "ArenaGlow");
                    var colliders = arena.GetComponentsInChildren<Collider>(true);
                    var relevant = 0;
                    for (var i = 0; i < colliders.Length; i++)
                    {
                        var collider = colliders[i];
                        if (collider == null || collider.isTrigger || collider.name == "Shield")
                        {
                            continue;
                        }
                        relevant++;
                        if (collider.sharedMaterial != ballSurface)
                        {
                            throw new InvalidOperationException("Arena collider missing shared BallSurface: " + collider.name);
                        }
                    }
                    if (relevant < 12)
                    {
                        throw new InvalidOperationException("Arena has too few shared-surface colliders.");
                    }
                    Require(arena.transform.Find("RampWest"), "West ramp");
                    Require(arena.transform.Find("RampEast"), "East ramp");
                    Require(arena.transform.Find("NorthGoal"), "North goal root");
                    Require(arena.transform.Find("SouthGoal"), "South goal root");
                    Require(arena.transform.Find("Containment"), "Containment root");
                    ValidateContainment(arena.transform.Find("Containment"), ballSurface);
                }

                internal static void ValidateContainment(Transform containment, PhysicsMaterial ballSurface)
                {
                    var names = new[] { "FloorContainment", "CeilingContainment", "NorthContainment", "SouthContainment", "WestContainment", "EastContainment", "WestGoalOpeningContainment", "EastGoalOpeningContainment" };
                    var positions = new[] { new Vector3(0f, -4f, 0f), new Vector3(0f, 48.5f, 0f), new Vector3(0f, 28f, -44.5f), new Vector3(0f, 28f, 44.5f), new Vector3(-64.5f, 28f, 0f), new Vector3(64.5f, 28f, 0f), new Vector3(-67f, 3.5f, 0f), new Vector3(67f, 3.5f, 0f) };
                    var sizes = new[] { new Vector3(140f, 1f, 120f), new Vector3(130f, 1f, 90f), new Vector3(130f, 42f, 1f), new Vector3(130f, 42f, 1f), new Vector3(1f, 42f, 90f), new Vector3(1f, 42f, 90f), new Vector3(1f, 8f, 38f), new Vector3(1f, 8f, 38f) };
                    for (var i = 0; i < names.Length; i++)
                    {
                        var item = Require(containment.Find(names[i]), names[i]);
                        var collider = Require(item.GetComponent<BoxCollider>(), names[i] + " BoxCollider");
                        if (Vector3.Distance(item.localPosition, positions[i]) > 0.001f || Vector3.Distance(collider.size, sizes[i]) > 0.001f || collider.isTrigger || collider.sharedMaterial != ballSurface || item.GetComponents<Component>().Length != 2 || item.GetComponent<Renderer>() != null || item.GetComponent<Rigidbody>() != null)
                        {
                            throw new InvalidOperationException("Containment collider contract invalid: " + names[i]);
                        }
                    }
                    var gridRoot = Require(containment.Find("GridVisuals"), "Containment GridVisuals");
                    var gridNames = new[] { "CeilingGrid", "NorthUpperGrid", "SouthUpperGrid", "WestUpperGrid", "EastUpperGrid" };
                    var gridPositions = new[] { new Vector3(0f, 47.98f, 0f), new Vector3(0f, 28f, -43.98f), new Vector3(0f, 28f, 43.98f), new Vector3(-63.98f, 28f, 0f), new Vector3(63.98f, 28f, 0f) };
                    var gridRotations = new[] { Quaternion.Euler(90f, 0f, 0f), Quaternion.identity, Quaternion.Euler(0f, 180f, 0f), Quaternion.Euler(0f, 90f, 0f), Quaternion.Euler(0f, -90f, 0f) };
                    var gridScales = new[] { new Vector3(130f, 90f, 1f), new Vector3(130f, 40f, 1f), new Vector3(130f, 40f, 1f), new Vector3(90f, 40f, 1f), new Vector3(90f, 40f, 1f) };
                    var gridMaterials = new[] { AssetDatabase.LoadAssetAtPath<Material>(GridCeilingMaterialPath), AssetDatabase.LoadAssetAtPath<Material>(GridLongWallMaterialPath), AssetDatabase.LoadAssetAtPath<Material>(GridLongWallMaterialPath), AssetDatabase.LoadAssetAtPath<Material>(GridEndWallMaterialPath), AssetDatabase.LoadAssetAtPath<Material>(GridEndWallMaterialPath) };
                    ValidateGridMaterial(gridMaterials[0], new Vector2(32.5f, 22.5f), "ContainmentGridCeiling");
                    ValidateGridMaterial(gridMaterials[1], new Vector2(32.5f, 10f), "ContainmentGridLongWall");
                    ValidateGridMaterial(gridMaterials[3], new Vector2(22.5f, 10f), "ContainmentGridEndWall");
                    for (var i = 0; i < gridNames.Length; i++)
                    {
                        var item = Require(gridRoot.Find(gridNames[i]), gridNames[i]);
                        var renderer = Require(item.GetComponent<MeshRenderer>(), gridNames[i] + " renderer");
                        if (Vector3.Distance(item.localPosition, gridPositions[i]) > 0.001f || Quaternion.Angle(item.localRotation, gridRotations[i]) > 0.1f || Vector3.Distance(item.localScale, gridScales[i]) > 0.001f || renderer.sharedMaterial != gridMaterials[i] || !item.gameObject.isStatic || renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off || renderer.receiveShadows || renderer.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off || renderer.reflectionProbeUsage != UnityEngine.Rendering.ReflectionProbeUsage.Off || item.GetComponent<Collider>() != null || item.GetComponent<Rigidbody>() != null || item.GetComponents<MonoBehaviour>().Length != 0)
                        {
                            throw new InvalidOperationException("Containment grid visual contract invalid: " + gridNames[i]);
                        }
                    }
                }

                internal static void ValidateArenaArchitecture(GameObject arena)
                {
                    var sceneRendererCount = 0;
                    var sceneRoots = arena.scene.GetRootGameObjects();
                    for (var i = 0; i < sceneRoots.Length; i++) sceneRendererCount += sceneRoots[i].GetComponentsInChildren<MeshRenderer>(true).Length;
                    if (sceneRendererCount > 80)
                    {
                        throw new InvalidOperationException("Complete MovementLab MeshRenderer budget exceeded: " + sceneRendererCount);
                    }
                    Debug.Log("Rocket Fooxball Movement Lab MeshRenderer total: " + sceneRendererCount);

                    var architecture = Require(arena.transform.Find("Architecture"), "Arena Architecture");
                    var renderers = architecture.GetComponentsInChildren<MeshRenderer>(true);
                    if (renderers.Length == 0 || renderers.Length > 80) throw new InvalidOperationException("Arena architecture renderer budget invalid: " + renderers.Length);
                    var triangleCount = 0;
                    var uniqueMeshes = new HashSet<Mesh>();
                    var palette = new[]
                    {
                        AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaPrimary.mat"),
                        AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaTrim.mat"),
                        AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaHazard.mat"),
                        AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaGlow.mat")
                    };
                    for (var i = 0; i < renderers.Length; i++)
                    {
                        var renderer = renderers[i];
                        var filter = Require(renderer.GetComponent<MeshFilter>(), "Architecture MeshFilter");
                        var mesh = Require(filter.sharedMesh, "Architecture mesh");
                        if (AssetDatabase.GetAssetPath(mesh) != ArenaKitModelPath) throw new InvalidOperationException("Architecture mesh provenance mismatch: " + renderer.name);
                        if (renderer.GetComponentsInChildren<Collider>(true).Length != 0 || renderer.GetComponent<Rigidbody>() != null) throw new InvalidOperationException("Architecture visual must remain renderer-only: " + renderer.name);
                        var materials = renderer.sharedMaterials;
                        if (materials == null || materials.Length == 0) throw new InvalidOperationException("Architecture material slots missing: " + renderer.name);
                        for (var j = 0; j < materials.Length; j++) if (materials[j] == null) throw new InvalidOperationException("Architecture material slot null: " + renderer.name);
                        var expectedMaterials = ResolveArenaKitMaterials(mesh.name, palette);
                        if (materials.Length != expectedMaterials.Length) throw new InvalidOperationException("Architecture material slot count mismatch: " + renderer.name);
                        for (var j = 0; j < materials.Length; j++) if (materials[j] != expectedMaterials[j]) throw new InvalidOperationException("Architecture material slot order mismatch: " + renderer.name);
                        if (uniqueMeshes.Add(mesh)) triangleCount += mesh.triangles.Length / 3;
                    }
                    if (triangleCount > 75000) throw new InvalidOperationException("ArenaKit imported triangle budget exceeded: " + triangleCount);
                    ValidateArenaKitModel();
                    ValidateArchitectureTransform(architecture, "NorthGoalShell", new Vector3(-GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
                    ValidateArchitectureTransform(architecture, "SouthGoalShell", new Vector3(GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
                    ValidateArchitectureTransform(architecture, "RampWestRails", new Vector3(-22f, 2.1f, 2f), Quaternion.Euler(-15f, -90f, 0f));
                    ValidateArchitectureTransform(architecture, "RampEastRails", new Vector3(22f, 2.1f, -2f), Quaternion.Euler(-15f, 90f, 0f));
                    for (var x = -48f; x <= 48f; x += 24f)
                    {
                        ValidateArchitectureTransform(architecture, "NorthWallPylon_" + x.ToString("0"), new Vector3(x, 0f, -44f), Quaternion.identity);
                        ValidateArchitectureTransform(architecture, "SouthWallPylon_" + x.ToString("0"), new Vector3(x, 0f, 44f), Quaternion.Euler(0f, 180f, 0f));
                    }
                    ValidateShieldVisual(arena.transform.Find("NorthGoal"), "NorthGoal");
                    ValidateShieldVisual(arena.transform.Find("SouthGoal"), "SouthGoal");
                }

                internal static void ValidateArchitectureTransform(Transform architecture, string name, Vector3 position, Quaternion rotation)
                {
                    var item = Require(architecture.Find(name), "Architecture " + name);
                    if (Vector3.Distance(item.localPosition, position) > 0.01f || Quaternion.Angle(item.localRotation, rotation) > 0.1f || Vector3.Distance(item.localScale, Vector3.one) > 0.001f) throw new InvalidOperationException("Architecture transform mismatch: " + name);
                    if (!item.gameObject.isStatic) throw new InvalidOperationException("Architecture visual must be static: " + name);
                }

                internal static void ValidateShieldVisual(Transform goal, string label)
                {
                    var collider = Require(goal != null ? goal.Find("ShieldCollider") : null, label + " ShieldCollider").GetComponent<BoxCollider>();
                    var visual = Require(goal != null ? goal.Find("ShieldVisual") : null, label + " ShieldVisual");
                    if (collider == null || collider.isTrigger || visual.GetComponent<Collider>() != null || visual.GetComponent<MeshRenderer>() == null) throw new InvalidOperationException(label + " shield collider/render split invalid.");
                    var material = visual.GetComponent<MeshRenderer>().sharedMaterial;
                    if (material == null || material.shader == null || material.shader.name != "RocketFooxball/RetroShield" || Mathf.Abs(material.GetFloat("_Alpha") - 0.52f) > 0.001f) throw new InvalidOperationException(label + " shield material contract invalid.");
                }

                internal static void ValidateArenaKitModel()
                {
                    var importer = AssetImporter.GetAtPath(ArenaKitModelPath) as ModelImporter;
                    if (importer == null || importer.animationType != ModelImporterAnimationType.None || importer.importAnimation || importer.materialImportMode != ModelImporterMaterialImportMode.None || Mathf.Abs(importer.globalScale - 1f) > 0.0001f) throw new InvalidOperationException("ArenaKit importer contract invalid.");
                    ValidatePbrModelImporter(importer, true, "ArenaKit");
                    var expected = new[] { "ArenaGoalShell", "ArenaRampRails", "ArenaWallPylon", "ArenaPerimeterTruss", "ArenaScoreboard" };
                    var assets = AssetDatabase.LoadAllAssetsAtPath(ArenaKitModelPath);
                    for (var i = 0; i < expected.Length; i++)
                    {
                        Mesh found = null;
                        for (var j = 0; j < assets.Length; j++) if (assets[j] is Mesh mesh && mesh.name == expected[i]) found = mesh;
                        if (found == null || AssetDatabase.GetAssetPath(found) != ArenaKitModelPath || found.subMeshCount < 1)
                        {
                            var importedNames = new List<string>();
                            for (var k = 0; k < assets.Length; k++) if (assets[k] != null) importedNames.Add(assets[k].name + "[" + assets[k].GetType().Name + "]");
                            throw new InvalidOperationException("ArenaKit named mesh missing/provenance invalid: " + expected[i] + "; imported assets=" + string.Join(",", importedNames.ToArray()));
                        }
                        ValidateMeshPbrChannels(found, true, "ArenaKit/" + expected[i]);
                    }
                }

    }
}
