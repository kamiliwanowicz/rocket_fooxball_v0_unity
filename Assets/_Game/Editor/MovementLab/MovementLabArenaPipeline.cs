using System;
using System.Collections.Generic;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Weapons;
using UnityEditor;
using UnityEngine;
using static RocketFooxball.Editor.MovementLabContractCatalog;
using static RocketFooxball.Editor.MovementLabMaterialPipeline;
using static RocketFooxball.Editor.MovementLabSerializedProperties;

namespace RocketFooxball.Editor
{
    internal sealed class GoalBuild
    {
        internal GameObject Root;
        internal GoalTrigger Trigger;
        internal Collider Shield;
        internal GameObject TeamCue;
    }

    internal sealed class ArenaBuild
    {
        internal GameObject Root;
        internal GoalBuild NorthGoal;
        internal GoalBuild SouthGoal;
        internal Collider[] Shields;
    }

    internal static partial class MovementLabArenaPipeline
    {
        private const float TransformTolerance = 0.001f;
        private const float RotationTolerance = 0.1f;

        internal static void Validate()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid() || !string.Equals(scene.path, MovementLabContract.ScenePath, StringComparison.Ordinal))
                throw new InvalidOperationException("MovementLab arena validation requires active generated scene: " + MovementLabContract.ScenePath);
            var arena = GameObject.Find("Arena");
            if (arena == null) throw new InvalidOperationException("Missing Arena root.");
            RequireChild(arena.transform, "Containment");
            RequireChild(arena.transform, "NorthGoal");
            RequireChild(arena.transform, "SouthGoal");
        }

        internal static void RequireChild(Transform root, string name)
        {
            if (root.Find(name) == null) throw new InvalidOperationException("Arena missing required child: " + name);
        }

        internal static ArenaBuild BuildArena(
            Material floorMaterial, Material wallMaterial, Material markingMaterial, Material frameMaterial,
            Material shieldMaterial, PhysicsMaterial ballSurface, Material arenaPrimaryMaterial, Material arenaTrimMaterial,
            Material arenaHazardMaterial, Material arenaGlowMaterial, Material northShieldMaterial, Material southShieldMaterial,
            Material teamBlueMaterial = null, Material teamRedMaterial = null)
        {
            var arena = new GameObject("Arena");
            for (var i = 0; i < MovementLabContract.PrimaryCollisionGeometry.Length; i++)
            {
                var geometry = MovementLabContract.PrimaryCollisionGeometry[i];
                var usesGrass = geometry.Name == "Floor";
                CreateSolid(geometry.Name, arena.transform, geometry.Position, geometry.Scale,
                    usesGrass ? floorMaterial : wallMaterial, ballSurface, geometry.Rotation);
            }

            BuildRamps(arena.transform, floorMaterial, ballSurface);

            BuildMarkings(arena.transform, markingMaterial);
            var north = BuildGoal("NorthGoal", GoalTrigger.GoalSide.North,
                new Vector3(-GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, -90f, 0f),
                frameMaterial, northShieldMaterial, wallMaterial, ballSurface, teamRedMaterial);
            var south = BuildGoal("SouthGoal", GoalTrigger.GoalSide.South,
                new Vector3(GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, 90f, 0f),
                frameMaterial, southShieldMaterial, wallMaterial, ballSurface, teamBlueMaterial);
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
            CreateContainment("WestGoalOpeningContainment", containment,
                new Vector3(-67f, MovementLabContract.ArenaGoalOpeningCenterY, 0f),
                new Vector3(1f, MovementLabContract.ArenaGoalOpeningContainmentHeight, MovementLabContract.ArenaGoalOpeningContainmentDepth), ballSurface);
            CreateContainment("EastGoalOpeningContainment", containment,
                new Vector3(67f, MovementLabContract.ArenaGoalOpeningCenterY, 0f),
                new Vector3(1f, MovementLabContract.ArenaGoalOpeningContainmentHeight, MovementLabContract.ArenaGoalOpeningContainmentDepth), ballSurface);

            BuildArenaArchitecture(arena.transform, arenaPrimaryMaterial, arenaTrimMaterial, arenaHazardMaterial, arenaGlowMaterial, teamBlueMaterial);
            return new ArenaBuild
            {
                Root = arena,
                NorthGoal = north,
                SouthGoal = south,
                Shields = new[] { north.Shield, south.Shield }
            };
        }

        private static void BuildRamps(Transform arenaRoot, Material floorMaterial, PhysicsMaterial ballSurface)
        {
            var mesh = CreateRampPrismMesh();
            var specifications = MovementLabContract.ArenaRampSpecifications;
            for (var i = 0; i < specifications.Length; i++)
            {
                var specification = specifications[i];
                var ramp = new GameObject(specification.Name);
                ramp.transform.SetParent(arenaRoot, false);
                ramp.transform.localPosition = specification.Center;
                ramp.transform.localRotation = specification.Rotation;
                ramp.transform.localScale = Vector3.one;
                ramp.AddComponent<MeshFilter>().sharedMesh = mesh;
                ramp.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;
                ramp.AddComponent<MeshCollider>().sharedMesh = mesh;
                ramp.GetComponent<MeshCollider>().sharedMaterial = ballSurface;
                ramp.isStatic = true;
            }
        }

        private static Mesh CreateRampPrismMesh()
        {
            var mesh = new Mesh { name = "ArenaRampPrismMesh" };
            var height = MovementLabContract.ArenaRampHeight;
            var vertices = new[]
            {
                new Vector3(-10f, 0f, -9f),
                new Vector3(-10f, height, -9f),
                new Vector3(10f, 0f, -9f),
                new Vector3(-10f, 0f, 9f),
                new Vector3(-10f, height, 9f),
                new Vector3(10f, 0f, 9f)
            };
            var uvs = new Vector2[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
                uvs[i] = new Vector2(vertices[i].x / MovementLabContract.ArenaRampLength + 0.5f,
                    vertices[i].z / MovementLabContract.ArenaRampWidth + 0.5f);
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = new[]
            {
                0, 1, 2,
                3, 5, 4,
                0, 2, 5,
                0, 5, 3,
                0, 3, 4,
                0, 4, 1,
                1, 4, 5,
                1, 5, 2
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Unwrapping.GenerateSecondaryUVSet(mesh);
            return mesh;
        }

        private static void BuildMarkings(Transform arenaRoot, Material markingMaterial)
        {
            var markings = new GameObject("Markings").transform;
            markings.SetParent(arenaRoot, false);
            markings.gameObject.isStatic = true;
            CreateRequiredMarkingLines(markings, markingMaterial);
            for (var i = 0; i < MovementLabContract.ArenaCenterCircleSegments; i++)
            {
                CreateLineMarking("CenterCircleChord_" + i.ToString("D2"), markings,
                    CirclePoint(MovementLabContract.ArenaCenterCircleRadius, i, MovementLabContract.ArenaCenterCircleSegments),
                    CirclePoint(MovementLabContract.ArenaCenterCircleRadius, i + 1, MovementLabContract.ArenaCenterCircleSegments),
                    markingMaterial, MovementLabContract.ArenaMarkingLineWidth * 0.5f);
            }
            CreateCenterSpot(markings, markingMaterial);
        }

        private static void CreateRequiredMarkingLines(Transform root, Material material)
        {
            CreateLineMarking("BoundaryNorth", root, new Vector2(-60f, -40f), new Vector2(60f, -40f), material);
            CreateLineMarking("BoundarySouth", root, new Vector2(-60f, 40f), new Vector2(60f, 40f), material);
            CreateLineMarking("BoundaryWest", root, new Vector2(-60f, -40f), new Vector2(-60f, 40f), material);
            CreateLineMarking("BoundaryEast", root, new Vector2(60f, -40f), new Vector2(60f, 40f), material);
            CreateLineMarking("HalfwayLine", root, new Vector2(0f, -40f), new Vector2(0f, 40f), material);
            CreateLineMarking("NegativePenaltyNorth", root, new Vector2(-60f, -22f), new Vector2(-42f, -22f), material);
            CreateLineMarking("NegativePenaltyBack", root, new Vector2(-42f, -22f), new Vector2(-42f, 22f), material);
            CreateLineMarking("NegativePenaltySouth", root, new Vector2(-42f, 22f), new Vector2(-60f, 22f), material);
            CreateLineMarking("PositivePenaltyNorth", root, new Vector2(60f, -22f), new Vector2(42f, -22f), material);
            CreateLineMarking("PositivePenaltyBack", root, new Vector2(42f, -22f), new Vector2(42f, 22f), material);
            CreateLineMarking("PositivePenaltySouth", root, new Vector2(42f, 22f), new Vector2(60f, 22f), material);
            CreateLineMarking("NegativeGoalAreaNorth", root, new Vector2(-60f, -10f), new Vector2(-54f, -10f), material);
            CreateLineMarking("NegativeGoalAreaBack", root, new Vector2(-54f, -10f), new Vector2(-54f, 10f), material);
            CreateLineMarking("NegativeGoalAreaSouth", root, new Vector2(-54f, 10f), new Vector2(-60f, 10f), material);
            CreateLineMarking("PositiveGoalAreaNorth", root, new Vector2(60f, -10f), new Vector2(54f, -10f), material);
            CreateLineMarking("PositiveGoalAreaBack", root, new Vector2(54f, -10f), new Vector2(54f, 10f), material);
            CreateLineMarking("PositiveGoalAreaSouth", root, new Vector2(54f, 10f), new Vector2(60f, 10f), material);
        }

        private static Vector2 CirclePoint(float radius, int index, int count)
        {
            var angle = Mathf.PI * 2f * index / count;
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        private static void CreateLineMarking(string name, Transform parent, Vector2 start, Vector2 end, Material material, float totalOverlap = 0f)
        {
            var direction = end - start;
            var marking = GameObject.CreatePrimitive(PrimitiveType.Quad);
            marking.name = name;
            marking.transform.SetParent(parent, false);
            marking.transform.localPosition = new Vector3((start.x + end.x) * 0.5f, MovementLabContract.ArenaMarkingLineHeight, (start.y + end.y) * 0.5f);
            marking.transform.localRotation = Quaternion.Euler(90f, -Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, 0f);
            marking.transform.localScale = new Vector3(direction.magnitude + totalOverlap, MovementLabContract.ArenaMarkingLineWidth, 1f);
            UnityEngine.Object.DestroyImmediate(marking.GetComponent<Collider>());
            marking.GetComponent<MeshRenderer>().sharedMaterial = material;
            marking.isStatic = true;
        }

        private static void CreateCenterSpot(Transform parent, Material material)
        {
            var spot = new GameObject("CenterSpot");
            spot.transform.SetParent(parent, false);
            spot.transform.localPosition = new Vector3(0f, MovementLabContract.ArenaMarkingLineHeight, 0f);
            var mesh = new Mesh { name = "ArenaCenterSpotMesh" };
            var count = MovementLabContract.ArenaCenterSpotSegments;
            var vertices = new Vector3[count + 1];
            var uvs = new Vector2[count + 1];
            var triangles = new int[count * 3];
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (var i = 0; i < count; i++)
            {
                var point = CirclePoint(MovementLabContract.ArenaCenterSpotRadius, i, count);
                vertices[i + 1] = new Vector3(point.x, 0f, point.y);
                uvs[i + 1] = new Vector2(point.x / (MovementLabContract.ArenaCenterSpotRadius * 2f) + 0.5f,
                    point.y / (MovementLabContract.ArenaCenterSpotRadius * 2f) + 0.5f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % count + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            spot.AddComponent<MeshFilter>().sharedMesh = mesh;
            spot.AddComponent<MeshRenderer>().sharedMaterial = material;
            spot.isStatic = true;
        }

        internal static GoalBuild BuildGoal(string name, GoalTrigger.GoalSide side, Vector3 position, Quaternion rotation,
            Material frameMaterial, Material shieldMaterial, Material wallMaterial, PhysicsMaterial ballSurface, Material teamMaterial = null)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, rotation);
            var triggerCollider = root.AddComponent<BoxCollider>();
            var trigger = root.AddComponent<GoalTrigger>();
            triggerCollider.isTrigger = true;
            triggerCollider.center = new Vector3(0f, MovementLabContract.ArenaGoalOpeningCenterY, 0f);
            triggerCollider.size = new Vector3(MovementLabContract.ArenaGoalOpeningWidth, MovementLabContract.ArenaGoalOpeningHeight, 0.5f);
            SetEnum(trigger, "goalSide", side == GoalTrigger.GoalSide.North ? "North" : "South");
            SetVector3(trigger, "planeNormal", Vector3.right);
            SetFloat(trigger, "openingHalfWidth", MovementLabContract.ArenaGoalOpeningHalfWidth);
            SetFloat(trigger, "openingMinHeight", 0f);
            SetFloat(trigger, "openingMaxHeight", MovementLabContract.ArenaGoalOpeningHeight);
            SetFloat(trigger, "rearmDistance", 0.5f);
            SetObjectReference(trigger, "planeReference", root.transform);
            SetObjectReference(trigger, "openingTrigger", triggerCollider);

            var shield = new GameObject("ShieldCollider");
            shield.transform.SetParent(root.transform, false);
            shield.transform.localPosition = new Vector3(0f, MovementLabContract.ArenaGoalOpeningCenterY, 0f);
            var shieldCollider = shield.AddComponent<BoxCollider>();
            shieldCollider.size = new Vector3(MovementLabContract.ArenaGoalOpeningWidth, MovementLabContract.ArenaGoalOpeningHeight, 0.4f);
            shieldCollider.sharedMaterial = ballSurface;
            var shieldVisual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shieldVisual.name = "ShieldVisual";
            shieldVisual.transform.SetParent(root.transform, false);
            shieldVisual.transform.localPosition = new Vector3(0f, MovementLabContract.ArenaGoalOpeningCenterY, -0.22f);
            shieldVisual.transform.localScale = new Vector3(MovementLabContract.ArenaGoalOpeningWidth, MovementLabContract.ArenaGoalOpeningHeight, 1f);
            UnityEngine.Object.DestroyImmediate(shieldVisual.GetComponent<Collider>());
            shieldVisual.GetComponent<Renderer>().sharedMaterial = shieldMaterial;
            var cue = MovementLabPrefabPipeline.CreateShapeCue(side == GoalTrigger.GoalSide.North ? "RedTriangleCue" : "BlueCircleCue",
                side == GoalTrigger.GoalSide.North, teamMaterial != null ? teamMaterial : shieldMaterial,
                new Vector3(0f, MovementLabContract.ArenaGoalOpeningCenterY, -0.28f));
            cue.transform.SetParent(root.transform, false);
            cue.transform.localScale = new Vector3(3.5f, 3.5f, 1f);
            CreateColliderSolid("FrameWest", root.transform,
                new Vector3(-MovementLabContract.ArenaGoalOpeningFrameCenterX, MovementLabContract.ArenaGoalOpeningCenterY, 0f),
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameThickness, MovementLabContract.ArenaGoalOpeningHeight,
                    MovementLabContract.ArenaGoalOpeningFrameThickness), ballSurface);
            CreateColliderSolid("FrameEast", root.transform,
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameCenterX, MovementLabContract.ArenaGoalOpeningCenterY, 0f),
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameThickness, MovementLabContract.ArenaGoalOpeningHeight,
                    MovementLabContract.ArenaGoalOpeningFrameThickness), ballSurface);
            CreateColliderSolid("FrameTop", root.transform,
                new Vector3(0f, MovementLabContract.ArenaGoalOpeningLintelCenterY, 0f),
                new Vector3(MovementLabContract.ArenaGoalOpeningWidth + MovementLabContract.ArenaGoalOpeningFrameThickness * 2f,
                    MovementLabContract.ArenaGoalOpeningFrameThickness, MovementLabContract.ArenaGoalOpeningFrameThickness), ballSurface);
            CreateColliderSolid("RecessWest", root.transform,
                new Vector3(-MovementLabContract.ArenaGoalOpeningFrameCenterX, MovementLabContract.ArenaGoalOpeningCenterY,
                    MovementLabContract.ArenaGoalRecessDepth * 0.5f),
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameThickness, MovementLabContract.ArenaGoalOpeningHeight,
                    MovementLabContract.ArenaGoalRecessDepth), ballSurface);
            CreateColliderSolid("RecessEast", root.transform,
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameCenterX, MovementLabContract.ArenaGoalOpeningCenterY,
                    MovementLabContract.ArenaGoalRecessDepth * 0.5f),
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameThickness, MovementLabContract.ArenaGoalOpeningHeight,
                    MovementLabContract.ArenaGoalRecessDepth), ballSurface);
            CreateColliderSolid("RecessFloor", root.transform, new Vector3(0f, -0.25f, MovementLabContract.ArenaGoalRecessDepth * 0.5f),
                new Vector3(MovementLabContract.ArenaGoalRecessBackWidth, 0.5f, MovementLabContract.ArenaGoalRecessDepth), ballSurface);
            CreateColliderSolid("RecessBack", root.transform,
                new Vector3(0f, MovementLabContract.ArenaGoalOpeningCenterY, MovementLabContract.ArenaGoalRecessBackCenterZ),
                new Vector3(MovementLabContract.ArenaGoalRecessBackWidth, MovementLabContract.ArenaGoalOpeningHeight, 1f), ballSurface);
            return new GoalBuild { Root = root, Trigger = trigger, Shield = shieldCollider, TeamCue = cue };
        }

        internal static void BuildArenaArchitecture(Transform arenaRoot, Material primary, Material trim, Material hazard, Material glow, Material teamBlue)
        {
            var architecture = new GameObject("Architecture").transform;
            architecture.SetParent(arenaRoot, false);
            architecture.gameObject.isStatic = true;
            CreateArchitectureSolids(architecture, primary);
            CreateArenaKitVisual(architecture, "NorthGoalRecess", MovementLabContract.ArenaGoalRecessMesh,
                new Vector3(-GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), new[] { primary, trim, hazard, glow });
            CreateArenaKitVisual(architecture, "SouthGoalRecess", MovementLabContract.ArenaGoalRecessMesh,
                new Vector3(GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), new[] { primary, trim, teamBlue, glow });
            var materials = new[] { trim, glow };
            for (var i = 0; i < MovementLabContract.ArenaLongWallSconceXs.Length; i++)
            {
                var x = MovementLabContract.ArenaLongWallSconceXs[i];
                CreateArenaKitVisual(architecture, "NorthWallSconce_" + i.ToString("D2"), MovementLabContract.ArenaWallSconceMesh,
                    new Vector3(x, MovementLabContract.ArenaSconceHeight, -44f), Quaternion.identity, materials);
                CreateArenaKitVisual(architecture, "SouthWallSconce_" + i.ToString("D2"), MovementLabContract.ArenaWallSconceMesh,
                    new Vector3(x, MovementLabContract.ArenaSconceHeight, 44f), Quaternion.Euler(0f, 180f, 0f), materials);
            }
            for (var i = 0; i < MovementLabContract.ArenaEndWallSconceZs.Length; i++)
            {
                var z = MovementLabContract.ArenaEndWallSconceZs[i];
                CreateArenaKitVisual(architecture, "WestWallSconce_" + i.ToString("D2"), MovementLabContract.ArenaWallSconceMesh,
                    new Vector3(-64f, MovementLabContract.ArenaSconceHeight, z), Quaternion.Euler(0f, 90f, 0f), materials);
                CreateArenaKitVisual(architecture, "EastWallSconce_" + i.ToString("D2"), MovementLabContract.ArenaWallSconceMesh,
                    new Vector3(64f, MovementLabContract.ArenaSconceHeight, z), Quaternion.Euler(0f, -90f, 0f), materials);
            }
        }

        private static void CreateArchitectureSolids(Transform root, Material material)
        {
            CreatePresentationSolid("NorthApron", root, new Vector3(0f, 0.01f, -42.25f), new Vector3(130f, 0.02f, 4.5f), material);
            CreatePresentationSolid("SouthApron", root, new Vector3(0f, 0.01f, 42.25f), new Vector3(130f, 0.02f, 4.5f), material);
            CreatePresentationSolid("WestApron", root, new Vector3(-62.25f, 0.01f, 0f), new Vector3(4.5f, 0.02f, 80f), material);
            CreatePresentationSolid("EastApron", root, new Vector3(62.25f, 0.01f, 0f), new Vector3(4.5f, 0.02f, 80f), material);
            CreatePresentationSolid("NorthUpperWall", root, new Vector3(0f, 10f, -44.5f), new Vector3(130f, 4f, 1f), material);
            CreatePresentationSolid("SouthUpperWall", root, new Vector3(0f, 10f, 44.5f), new Vector3(130f, 4f, 1f), material);
            CreatePresentationSolid("WestUpperWallNorth", root, new Vector3(-64.5f, 10f, -31.75f), new Vector3(1f, 4f, 26.5f), material);
            CreatePresentationSolid("WestUpperWallSouth", root, new Vector3(-64.5f, 10f, 31.75f), new Vector3(1f, 4f, 26.5f), material);
            CreatePresentationSolid("EastUpperWallNorth", root, new Vector3(64.5f, 10f, -31.75f), new Vector3(1f, 4f, 26.5f), material);
            CreatePresentationSolid("EastUpperWallSouth", root, new Vector3(64.5f, 10f, 31.75f), new Vector3(1f, 4f, 26.5f), material);
            CreatePresentationSolid("WestUpperLintel", root, new Vector3(-64.5f, 10f, 0f), new Vector3(1f, 4f, 37f), material);
            CreatePresentationSolid("EastUpperLintel", root, new Vector3(64.5f, 10f, 0f), new Vector3(1f, 4f, 37f), material);
        }

        internal static GameObject CreateArenaKitVisual(Transform parent, string name, string meshName, Vector3 position, Quaternion rotation, Material[] materials)
        {
            var visual = new GameObject(name);
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = position;
            visual.transform.localRotation = rotation;
            visual.AddComponent<MeshFilter>().sharedMesh = FindArenaKitMesh(meshName);
            visual.AddComponent<MeshRenderer>().sharedMaterials = materials;
            visual.isStatic = true;
            return visual;
        }

        internal static Mesh FindArenaKitMesh(string meshName)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(ArenaKitModelPath);
            for (var i = 0; i < assets.Length; i++)
                if (assets[i] is Mesh mesh && string.Equals(mesh.name, meshName, StringComparison.Ordinal)) return mesh;
            var names = new List<string>();
            for (var i = 0; i < assets.Length; i++) if (assets[i] != null) names.Add(assets[i].name + "[" + assets[i].GetType().Name + "]");
            throw new InvalidOperationException("Missing ArenaKit mesh subasset: " + meshName + "; imported assets=" + string.Join(",", names));
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
            solid.GetComponent<Collider>().sharedMaterial = ballSurface;
            return solid;
        }

        internal static GameObject CreateColliderSolid(string name, Transform parent, Vector3 position, Vector3 size, PhysicsMaterial ballSurface)
        {
            var solid = new GameObject(name);
            solid.transform.SetParent(parent, false);
            solid.transform.localPosition = position;
            solid.transform.localScale = size;
            solid.AddComponent<BoxCollider>().sharedMaterial = ballSurface;
            return solid;
        }

        internal static void CreateContainment(string name, Transform parent, Vector3 position, Vector3 size, PhysicsMaterial ballSurface)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            var collider = item.AddComponent<BoxCollider>();
            collider.size = size;
            collider.sharedMaterial = ballSurface;
        }

        private static void CreatePresentationSolid(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = position;
            visual.transform.localScale = size;
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.GetComponent<MeshRenderer>().sharedMaterial = material;
            visual.isStatic = true;
        }

        internal static void ValidateArenaMaterials(GameObject arena, PhysicsMaterial ballSurface)
        {
            if (arena == null || ballSurface == null) throw new InvalidOperationException("Arena material validation requires Arena and BallSurface.");
            var floor = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Floor.mat");
            var wall = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Wall.mat");
            var marking = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Marking.mat");
            var primary = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaPrimary.mat");
            var trim = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaTrim.mat");
            var hazard = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaHazard.mat");
            var glow = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaGlow.mat");

            ValidatePbrMaterial(floor, LoadTexture(GrassTexturePath), null,
                LoadTexture(GrassMetallicTexturePath), LoadTexture(GrassOcclusionTexturePath), null, null,
                new Vector2(13f, 9f), "Floor", Vector2.one, 0f);
            ValidatePbrScalars(floor, 0f, 0.24f, 1f, 0f, 0f, "Floor");
            ValidateEmission(floor, Color.clear, 0f, "Floor");
            ValidatePbrMaterial(wall, LoadTexture(WallTexturePath), null,
                LoadTexture(WallMetallicTexturePath), LoadTexture(WallOcclusionTexturePath), null, null,
                new Vector2(13f, 2f), "Wall", Vector2.one, 0f);
            ValidatePbrScalars(wall, 0f, 0.28f, 1f, 0f, 0f, "Wall");
            ValidateEmission(wall, Color.clear, 0f, "Wall");
            ValidateFlatMaterial(marking, new Color(0.96f, 0.96f, 0.90f, 1f), 0.20f, 0f, "Marking");
            ValidatePbrMaterial(primary, LoadTexture(WallTexturePath), null,
                LoadTexture(WallMetallicTexturePath), LoadTexture(WallOcclusionTexturePath), null, null,
                new Vector2(13f, 2f), "ArenaPrimary", Vector2.one, 0f);
            ValidatePbrScalars(primary, 0f, 0.28f, 1f, 0f, 0f, "ArenaPrimary");
            ValidateEmission(primary, Color.clear, 0f, "ArenaPrimary");
            ValidateFlatMaterial(trim, new Color(0.78f, 0.79f, 0.77f, 1f), 0.30f, 0f, "ArenaTrim");
            ValidateFlatMaterial(hazard, new Color(233f / 255f, 90f / 255f, 22f / 255f, 1f), 0.35f, 0f, "ArenaHazard");
            ValidateFlatMaterial(glow, new Color(1f, 240f / 255f, 200f / 255f, 1f), 0.25f, 3f, "ArenaGlow");

            for (var i = 0; i < MovementLabContract.PrimaryCollisionGeometry.Length; i++)
            {
                var specification = MovementLabContract.PrimaryCollisionGeometry[i];
                var renderer = Require(arena.transform.Find(specification.Name)?.GetComponent<MeshRenderer>(), specification.Name + " renderer");
                var grass = specification.Name == "Floor";
                if (renderer.sharedMaterial != (grass ? floor : wall))
                    throw new InvalidOperationException("Arena gameplay surface material routing mismatch: " + specification.Name);
            }
            var rampSpecifications = MovementLabContract.ArenaRampSpecifications;
            for (var i = 0; i < rampSpecifications.Length; i++)
            {
                var renderer = Require(arena.transform.Find(rampSpecifications[i].Name)?.GetComponent<MeshRenderer>(), rampSpecifications[i].Name + " renderer");
                if (renderer.sharedMaterial != floor)
                    throw new InvalidOperationException("Arena gameplay surface material routing mismatch: " + rampSpecifications[i].Name);
            }
            var colliders = arena.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && !colliders[i].isTrigger && colliders[i].sharedMaterial != ballSurface)
                    throw new InvalidOperationException("Arena collider missing shared BallSurface: " + colliders[i].name);
            ValidateContainment(Require(arena.transform.Find("Containment"), "Containment root"), ballSurface);
            ValidateMarkings(arena, marking);
            ValidateGoalContracts(arena, ballSurface);
        }

        private static void ValidateFlatMaterial(Material material, Color color, float smoothness, float emissionStrength, string label)
        {
            ValidatePbrMaterial(material, null, null, null, null, null, null, Vector2.one, label);
            ValidatePbrScalars(material, 0f, smoothness, 1f, 1f, emissionStrength, label);
            ValidateEmission(material, emissionStrength > 0f ? color : Color.clear, emissionStrength, label);
            if (material == null || Vector4.Distance(material.GetColor("_BaseColor"), color) > 0.001f)
                throw new InvalidOperationException(label + " base color contract mismatch.");
        }

        internal static void ValidateContainment(Transform containment, PhysicsMaterial ballSurface)
        {
            var names = new[] { "FloorContainment", "CeilingContainment", "NorthContainment", "SouthContainment", "WestContainment", "EastContainment", "WestGoalOpeningContainment", "EastGoalOpeningContainment" };
            var positions = new[]
            {
                new Vector3(0f, -4f, 0f), new Vector3(0f, 48.5f, 0f), new Vector3(0f, 28f, -44.5f), new Vector3(0f, 28f, 44.5f),
                new Vector3(-64.5f, 28f, 0f), new Vector3(64.5f, 28f, 0f),
                new Vector3(-67f, MovementLabContract.ArenaGoalOpeningCenterY, 0f),
                new Vector3(67f, MovementLabContract.ArenaGoalOpeningCenterY, 0f)
            };
            var sizes = new[]
            {
                new Vector3(140f, 1f, 120f), new Vector3(130f, 1f, 90f), new Vector3(130f, 42f, 1f), new Vector3(130f, 42f, 1f),
                new Vector3(1f, 42f, 90f), new Vector3(1f, 42f, 90f),
                new Vector3(1f, MovementLabContract.ArenaGoalOpeningContainmentHeight, MovementLabContract.ArenaGoalOpeningContainmentDepth),
                new Vector3(1f, MovementLabContract.ArenaGoalOpeningContainmentHeight, MovementLabContract.ArenaGoalOpeningContainmentDepth)
            };
            if (containment == null || containment.childCount != names.Length || containment.GetComponents<Component>().Length != 1 ||
                containment.GetComponent<Renderer>() != null || containment.GetComponent<Rigidbody>() != null || containment.GetComponents<MonoBehaviour>().Length != 0)
                throw new InvalidOperationException("Containment root contract invalid.");
            for (var i = 0; i < names.Length; i++)
            {
                var item = Require(containment.Find(names[i]), names[i]);
                var collider = Require(item.GetComponent<BoxCollider>(), names[i] + " BoxCollider");
                if (Vector3.Distance(item.localPosition, positions[i]) > TransformTolerance ||
                    Quaternion.Angle(item.localRotation, Quaternion.identity) > RotationTolerance ||
                    Vector3.Distance(item.localScale, Vector3.one) > TransformTolerance ||
                    Vector3.Distance(collider.center, Vector3.zero) > TransformTolerance || Vector3.Distance(collider.size, sizes[i]) > TransformTolerance ||
                    !collider.enabled || collider.isTrigger || collider.sharedMaterial != ballSurface || !item.gameObject.activeSelf ||
                    item.GetComponents<Component>().Length != 2 || item.GetComponent<Renderer>() != null || item.GetComponent<Rigidbody>() != null ||
                    item.GetComponents<MonoBehaviour>().Length != 0)
                    throw new InvalidOperationException("Containment collider contract invalid: " + names[i]);
            }
            if (FindDescendant(containment, "GridVisuals") != null) throw new InvalidOperationException("Removed GridVisuals presentation returned.");
        }

        internal static void ValidatePrimaryCollisionGeometry(GameObject arena, PhysicsMaterial ballSurface)
        {
            if (arena == null || ballSurface == null) throw new InvalidOperationException("Primary collision geometry requires Arena and BallSurface.");
            var specifications = MovementLabContract.PrimaryCollisionGeometry;
            for (var i = 0; i < specifications.Length; i++)
            {
                var specification = specifications[i];
                var item = Require(arena.transform.Find(specification.Name), specification.Name);
                var collider = Require(item.GetComponent<BoxCollider>(), specification.Name + " BoxCollider");
                if (Vector3.Distance(item.localPosition, specification.Position) > 0.001f ||
                    Vector3.Distance(item.localScale, specification.Scale) > 0.001f ||
                    Quaternion.Angle(item.localRotation, specification.Rotation) > 0.1f || collider.center != Vector3.zero ||
                    collider.size != Vector3.one || collider.isTrigger || collider.sharedMaterial != ballSurface ||
                    item.GetComponent<Rigidbody>() != null || item.GetComponents<Collider>().Length != 1)
                    throw new InvalidOperationException("Primary collision geometry contract invalid: " + specification.Name);
            }
            ValidateRampGeometry(arena, ballSurface);
        }

        private static void ValidateRampGeometry(GameObject arena, PhysicsMaterial ballSurface)
        {
            var specifications = MovementLabContract.ArenaRampSpecifications;
            Mesh sharedMesh = null;
            for (var i = 0; i < specifications.Length; i++)
            {
                var specification = specifications[i];
                var item = Require(arena.transform.Find(specification.Name), specification.Name);
                var filter = Require(item.GetComponent<MeshFilter>(), specification.Name + " MeshFilter");
                var renderer = Require(item.GetComponent<MeshRenderer>(), specification.Name + " MeshRenderer");
                var collider = Require(item.GetComponent<MeshCollider>(), specification.Name + " MeshCollider");
                if (Vector3.Distance(item.localPosition, specification.Center) > TransformTolerance ||
                    Quaternion.Angle(item.localRotation, specification.Rotation) > RotationTolerance ||
                    Vector3.Distance(item.localScale, Vector3.one) > TransformTolerance || !item.gameObject.isStatic ||
                    !item.gameObject.activeSelf || renderer.sharedMaterial == null || filter.sharedMesh == null ||
                    collider.sharedMaterial != ballSurface || collider.sharedMesh != filter.sharedMesh ||
                    item.GetComponents<Component>().Length != 4 || item.GetComponents<Collider>().Length != 1 ||
                    item.GetComponent<Rigidbody>() != null)
                    throw new InvalidOperationException("Ramp geometry contract invalid: " + specification.Name);
                if (sharedMesh == null) sharedMesh = filter.sharedMesh;
                else if (sharedMesh != filter.sharedMesh)
                    throw new InvalidOperationException("Ramp geometry must share one canonical mesh.");
            }
            ValidateRampPrismMesh(sharedMesh);
        }

        private static void ValidateRampPrismMesh(Mesh mesh)
        {
            if (mesh == null || mesh.name != "ArenaRampPrismMesh" || mesh.vertexCount != 6 || mesh.triangles.Length != 24 ||
                mesh.uv == null || mesh.uv.Length != 6 || mesh.uv2 == null || mesh.uv2.Length != 6)
                throw new InvalidOperationException("Ramp prism mesh channel contract invalid.");
            var height = MovementLabContract.ArenaRampHeight;
            var vertices = new[]
            {
                new Vector3(-10f, 0f, -9f),
                new Vector3(-10f, height, -9f),
                new Vector3(10f, 0f, -9f),
                new Vector3(-10f, 0f, 9f),
                new Vector3(-10f, height, 9f),
                new Vector3(10f, 0f, 9f)
            };
            var triangles = new[]
            {
                0, 1, 2,
                3, 5, 4,
                0, 2, 5,
                0, 5, 3,
                0, 3, 4,
                0, 4, 1,
                1, 4, 5,
                1, 5, 2
            };
            var meshVertices = mesh.vertices;
            var meshUv = mesh.uv;
            var meshTriangles = mesh.triangles;
            for (var i = 0; i < vertices.Length; i++)
            {
                if (Vector3.Distance(meshVertices[i], vertices[i]) > TransformTolerance)
                    throw new InvalidOperationException("Ramp prism vertex mismatch: " + i);
                var uv = new Vector2(vertices[i].x / MovementLabContract.ArenaRampLength + 0.5f,
                    vertices[i].z / MovementLabContract.ArenaRampWidth + 0.5f);
                if (Vector2.Distance(meshUv[i], uv) > TransformTolerance)
                    throw new InvalidOperationException("Ramp prism UV0 mismatch: " + i);
            }
            for (var i = 0; i < triangles.Length; i++)
                if (meshTriangles[i] != triangles[i])
                    throw new InvalidOperationException("Ramp prism triangle winding mismatch: " + i);
            var expectedBounds = new Bounds(Vector3.zero, new Vector3(MovementLabContract.ArenaRampLength,
                MovementLabContract.ArenaRampHeight, MovementLabContract.ArenaRampWidth));
            if (Vector3.Distance(mesh.bounds.min, expectedBounds.min) > TransformTolerance ||
                Vector3.Distance(mesh.bounds.max, expectedBounds.max) > TransformTolerance)
                throw new InvalidOperationException("Ramp prism bounds contract invalid.");
        }

        private static void ValidateMarkings(GameObject arena, Material material)
        {
            var root = Require(arena.transform.Find("Markings"), "Arena Markings");
            if (root.childCount != 18 + MovementLabContract.ArenaCenterCircleSegments || !root.gameObject.isStatic || root.GetComponents<Component>().Length != 1)
                throw new InvalidOperationException("Markings root/count contract invalid.");
            ValidateRequiredMarkingLines(root, material);
            for (var i = 0; i < MovementLabContract.ArenaCenterCircleSegments; i++)
                ValidateLineMarking(root, "CenterCircleChord_" + i.ToString("D2"),
                    CirclePoint(MovementLabContract.ArenaCenterCircleRadius, i, MovementLabContract.ArenaCenterCircleSegments),
                    CirclePoint(MovementLabContract.ArenaCenterCircleRadius, i + 1, MovementLabContract.ArenaCenterCircleSegments),
                    material, MovementLabContract.ArenaMarkingLineWidth * 0.5f);
            ValidateCenterSpot(root, material);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 || root.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                root.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                throw new InvalidOperationException("Markings presentation must remain collider-free and component-free.");
        }

        private static void ValidateRequiredMarkingLines(Transform root, Material material)
        {
            ValidateLineMarking(root, "BoundaryNorth", new Vector2(-60f, -40f), new Vector2(60f, -40f), material);
            ValidateLineMarking(root, "BoundarySouth", new Vector2(-60f, 40f), new Vector2(60f, 40f), material);
            ValidateLineMarking(root, "BoundaryWest", new Vector2(-60f, -40f), new Vector2(-60f, 40f), material);
            ValidateLineMarking(root, "BoundaryEast", new Vector2(60f, -40f), new Vector2(60f, 40f), material);
            ValidateLineMarking(root, "HalfwayLine", new Vector2(0f, -40f), new Vector2(0f, 40f), material);
            ValidateLineMarking(root, "NegativePenaltyNorth", new Vector2(-60f, -22f), new Vector2(-42f, -22f), material);
            ValidateLineMarking(root, "NegativePenaltyBack", new Vector2(-42f, -22f), new Vector2(-42f, 22f), material);
            ValidateLineMarking(root, "NegativePenaltySouth", new Vector2(-42f, 22f), new Vector2(-60f, 22f), material);
            ValidateLineMarking(root, "PositivePenaltyNorth", new Vector2(60f, -22f), new Vector2(42f, -22f), material);
            ValidateLineMarking(root, "PositivePenaltyBack", new Vector2(42f, -22f), new Vector2(42f, 22f), material);
            ValidateLineMarking(root, "PositivePenaltySouth", new Vector2(42f, 22f), new Vector2(60f, 22f), material);
            ValidateLineMarking(root, "NegativeGoalAreaNorth", new Vector2(-60f, -10f), new Vector2(-54f, -10f), material);
            ValidateLineMarking(root, "NegativeGoalAreaBack", new Vector2(-54f, -10f), new Vector2(-54f, 10f), material);
            ValidateLineMarking(root, "NegativeGoalAreaSouth", new Vector2(-54f, 10f), new Vector2(-60f, 10f), material);
            ValidateLineMarking(root, "PositiveGoalAreaNorth", new Vector2(60f, -10f), new Vector2(54f, -10f), material);
            ValidateLineMarking(root, "PositiveGoalAreaBack", new Vector2(54f, -10f), new Vector2(54f, 10f), material);
            ValidateLineMarking(root, "PositiveGoalAreaSouth", new Vector2(54f, 10f), new Vector2(60f, 10f), material);
        }

        private static void ValidateLineMarking(Transform root, string name, Vector2 start, Vector2 end, Material material, float totalOverlap = 0f)
        {
            var item = Require(root.Find(name), "Marking " + name);
            var direction = end - start;
            var renderer = Require(item.GetComponent<MeshRenderer>(), name + " renderer");
            var filter = Require(item.GetComponent<MeshFilter>(), name + " MeshFilter");
            if (Vector3.Distance(item.localPosition, new Vector3((start.x + end.x) * 0.5f, MovementLabContract.ArenaMarkingLineHeight, (start.y + end.y) * 0.5f)) > TransformTolerance ||
                Quaternion.Angle(item.localRotation, Quaternion.Euler(90f, -Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, 0f)) > RotationTolerance ||
                Vector3.Distance(item.localScale, new Vector3(direction.magnitude + totalOverlap, MovementLabContract.ArenaMarkingLineWidth, 1f)) > TransformTolerance ||
                renderer.sharedMaterial != material || filter.sharedMesh == null || filter.sharedMesh.name != "Quad" ||
                !item.gameObject.isStatic || !item.gameObject.activeSelf || item.GetComponents<Component>().Length != 3)
                throw new InvalidOperationException("Marking line contract invalid: " + name);
        }

        private static void ValidateCenterSpot(Transform root, Material material)
        {
            var spot = Require(root.Find("CenterSpot"), "CenterSpot");
            var mesh = Require(spot.GetComponent<MeshFilter>()?.sharedMesh, "CenterSpot mesh");
            var renderer = Require(spot.GetComponent<MeshRenderer>(), "CenterSpot renderer");
            var count = MovementLabContract.ArenaCenterSpotSegments;
            var assetPath = AssetDatabase.GetAssetPath(mesh);
            if (Vector3.Distance(spot.localPosition, new Vector3(0f, MovementLabContract.ArenaMarkingLineHeight, 0f)) > TransformTolerance ||
                spot.localRotation != Quaternion.identity || spot.localScale != Vector3.one || !spot.gameObject.isStatic || renderer.sharedMaterial != material ||
                mesh.name != "ArenaCenterSpotMesh" || mesh.vertexCount != count + 1 || mesh.triangles.Length != count * 3 ||
                (!string.IsNullOrEmpty(assetPath) && assetPath != MovementLabContract.ScenePath))
                throw new InvalidOperationException("CenterSpot persisted scene-mesh contract invalid.");
            var vertices = mesh.vertices;
            for (var i = 0; i < count; i++)
            {
                var point = CirclePoint(MovementLabContract.ArenaCenterSpotRadius, i, count);
                if (Vector3.Distance(vertices[i + 1], new Vector3(point.x, 0f, point.y)) > TransformTolerance)
                    throw new InvalidOperationException("CenterSpot wedge vertex mismatch: " + i);
            }
        }

        internal static void ValidateArenaArchitecture(GameObject arena)
        {
            var root = Require(arena.transform.Find("Architecture"), "Arena Architecture");
            var primary = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaPrimary.mat");
            var trim = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaTrim.mat");
            var hazard = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaHazard.mat");
            var glow = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaGlow.mat");
            var teamBlue = AssetDatabase.LoadAssetAtPath<Material>(TeamBlueMaterialPath);
            var expectedCount = 14 + MovementLabContract.ArenaLongWallSconceXs.Length * 2 + MovementLabContract.ArenaEndWallSconceZs.Length * 2;
            if (root.childCount != expectedCount || !root.gameObject.isStatic || root.GetComponents<Component>().Length != 1)
                throw new InvalidOperationException("Arena Architecture root/count contract invalid.");
            ValidateArchitectureSolids(root, primary);
            ValidateArenaKitVisual(root, "NorthGoalRecess", MovementLabContract.ArenaGoalRecessMesh,
                new Vector3(-GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, -90f, 0f),
                new[] { primary, trim, hazard, glow }, 4,
                MovementLabContract.ArenaGoalRecessBoundsMin, MovementLabContract.ArenaGoalRecessBoundsMax);
            ValidateArenaKitVisual(root, "SouthGoalRecess", MovementLabContract.ArenaGoalRecessMesh,
                new Vector3(GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, 90f, 0f),
                new[] { primary, trim, teamBlue, glow }, 4,
                MovementLabContract.ArenaGoalRecessBoundsMin, MovementLabContract.ArenaGoalRecessBoundsMax);
            var materials = new[] { trim, glow };
            for (var i = 0; i < MovementLabContract.ArenaLongWallSconceXs.Length; i++)
            {
                var x = MovementLabContract.ArenaLongWallSconceXs[i];
                ValidateArenaKitVisual(root, "NorthWallSconce_" + i.ToString("D2"), MovementLabContract.ArenaWallSconceMesh,
                    new Vector3(x, MovementLabContract.ArenaSconceHeight, -44f), Quaternion.identity, materials, 2,
                    MovementLabContract.ArenaWallSconceBoundsMin, MovementLabContract.ArenaWallSconceBoundsMax);
                ValidateArenaKitVisual(root, "SouthWallSconce_" + i.ToString("D2"), MovementLabContract.ArenaWallSconceMesh,
                    new Vector3(x, MovementLabContract.ArenaSconceHeight, 44f), Quaternion.Euler(0f, 180f, 0f), materials, 2,
                    MovementLabContract.ArenaWallSconceBoundsMin, MovementLabContract.ArenaWallSconceBoundsMax);
            }
            for (var i = 0; i < MovementLabContract.ArenaEndWallSconceZs.Length; i++)
            {
                var z = MovementLabContract.ArenaEndWallSconceZs[i];
                ValidateArenaKitVisual(root, "WestWallSconce_" + i.ToString("D2"), MovementLabContract.ArenaWallSconceMesh,
                    new Vector3(-64f, MovementLabContract.ArenaSconceHeight, z), Quaternion.Euler(0f, 90f, 0f), materials, 2,
                    MovementLabContract.ArenaWallSconceBoundsMin, MovementLabContract.ArenaWallSconceBoundsMax);
                ValidateArenaKitVisual(root, "EastWallSconce_" + i.ToString("D2"), MovementLabContract.ArenaWallSconceMesh,
                    new Vector3(64f, MovementLabContract.ArenaSconceHeight, z), Quaternion.Euler(0f, -90f, 0f), materials, 2,
                    MovementLabContract.ArenaWallSconceBoundsMin, MovementLabContract.ArenaWallSconceBoundsMax);
            }
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 || root.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                root.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 || root.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("Arena Architecture must remain static, collider-free, and emissive-only.");
            var oldNames = new[] { "GridVisuals", "NorthGoalShell", "SouthGoalShell", "NorthScoreboard", "SouthScoreboard", "ArenaPerimeterTruss", "ArenaWallPylon", "ArenaScoreboard" };
            for (var i = 0; i < oldNames.Length; i++)
                if (FindDescendant(arena.transform, oldNames[i]) != null)
                    throw new InvalidOperationException("Removed arena presentation returned: " + oldNames[i]);
            ValidateShieldVisual(arena.transform.Find("NorthGoal"), "NorthGoal");
            ValidateShieldVisual(arena.transform.Find("SouthGoal"), "SouthGoal");
        }

        private static void ValidateArchitectureSolids(Transform root, Material material)
        {
            ValidatePresentationSolid(root, "NorthApron", new Vector3(0f, 0.01f, -42.25f), new Vector3(130f, 0.02f, 4.5f), material);
            ValidatePresentationSolid(root, "SouthApron", new Vector3(0f, 0.01f, 42.25f), new Vector3(130f, 0.02f, 4.5f), material);
            ValidatePresentationSolid(root, "WestApron", new Vector3(-62.25f, 0.01f, 0f), new Vector3(4.5f, 0.02f, 80f), material);
            ValidatePresentationSolid(root, "EastApron", new Vector3(62.25f, 0.01f, 0f), new Vector3(4.5f, 0.02f, 80f), material);
            ValidatePresentationSolid(root, "NorthUpperWall", new Vector3(0f, 10f, -44.5f), new Vector3(130f, 4f, 1f), material);
            ValidatePresentationSolid(root, "SouthUpperWall", new Vector3(0f, 10f, 44.5f), new Vector3(130f, 4f, 1f), material);
            ValidatePresentationSolid(root, "WestUpperWallNorth", new Vector3(-64.5f, 10f, -31.75f), new Vector3(1f, 4f, 26.5f), material);
            ValidatePresentationSolid(root, "WestUpperWallSouth", new Vector3(-64.5f, 10f, 31.75f), new Vector3(1f, 4f, 26.5f), material);
            ValidatePresentationSolid(root, "EastUpperWallNorth", new Vector3(64.5f, 10f, -31.75f), new Vector3(1f, 4f, 26.5f), material);
            ValidatePresentationSolid(root, "EastUpperWallSouth", new Vector3(64.5f, 10f, 31.75f), new Vector3(1f, 4f, 26.5f), material);
            ValidatePresentationSolid(root, "WestUpperLintel", new Vector3(-64.5f, 10f, 0f), new Vector3(1f, 4f, 37f), material);
            ValidatePresentationSolid(root, "EastUpperLintel", new Vector3(64.5f, 10f, 0f), new Vector3(1f, 4f, 37f), material);
        }

        private static void ValidatePresentationSolid(Transform root, string name, Vector3 position, Vector3 scale, Material material)
        {
            var item = Require(root.Find(name), "Architecture " + name);
            var renderer = Require(item.GetComponent<MeshRenderer>(), name + " renderer");
            var mesh = Require(item.GetComponent<MeshFilter>()?.sharedMesh, name + " mesh");
            if (Vector3.Distance(item.localPosition, position) > TransformTolerance ||
                Quaternion.Angle(item.localRotation, Quaternion.identity) > RotationTolerance ||
                Vector3.Distance(item.localScale, scale) > TransformTolerance || renderer.sharedMaterial != material || mesh.name != "Cube" ||
                !item.gameObject.isStatic || item.GetComponents<Component>().Length != 3)
                throw new InvalidOperationException("Architecture solid contract invalid: " + name);
        }

        private static void ValidateArenaKitVisual(Transform root, string name, string meshName, Vector3 position, Quaternion rotation,
            Material[] materials, int subMeshCount, Vector3 expectedMin, Vector3 expectedMax)
        {
            var item = Require(root.Find(name), "Architecture " + name);
            var renderer = Require(item.GetComponent<MeshRenderer>(), name + " renderer");
            var mesh = Require(item.GetComponent<MeshFilter>()?.sharedMesh, name + " mesh");
            if (Vector3.Distance(item.localPosition, position) > TransformTolerance || Quaternion.Angle(item.localRotation, rotation) > RotationTolerance ||
                Vector3.Distance(item.localScale, Vector3.one) > TransformTolerance || !item.gameObject.isStatic || item.GetComponents<Component>().Length != 3 ||
                mesh.name != meshName || AssetDatabase.GetAssetPath(mesh) != ArenaKitModelPath || mesh.subMeshCount != subMeshCount ||
                Vector3.Distance(mesh.bounds.min, expectedMin) > TransformTolerance || Vector3.Distance(mesh.bounds.max, expectedMax) > TransformTolerance ||
                renderer.sharedMaterials == null || renderer.sharedMaterials.Length != materials.Length)
                throw new InvalidOperationException("ArenaKit module contract invalid: " + name);
            for (var i = 0; i < materials.Length; i++)
                if (renderer.sharedMaterials[i] == null || renderer.sharedMaterials[i] != materials[i])
                    throw new InvalidOperationException("ArenaKit material slot order mismatch: " + name + "[" + i + "]");
        }

        private static void ValidateGoalContracts(GameObject arena, PhysicsMaterial ballSurface)
        {
            var north = ValidateGoal(arena.transform, "NorthGoal", GoalTrigger.GoalSide.North, ParticipantTeam.Red,
                new Vector3(-GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), ballSurface);
            var south = ValidateGoal(arena.transform, "SouthGoal", GoalTrigger.GoalSide.South, ParticipantTeam.Blue,
                new Vector3(GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), ballSurface);
            var setObject = GameObject.Find("GoalShieldSet");
            if (setObject == null || !setObject.activeInHierarchy) throw new InvalidOperationException("GoalShieldSet must exist and be active.");
            var set = Require(setObject.GetComponent<GoalShieldSet>(), "GoalShieldSet component");
            var property = new SerializedObject(set).FindProperty("colliders");
            if (!set.enabled || property == null || !property.isArray || property.arraySize != 2 ||
                property.GetArrayElementAtIndex(0).objectReferenceValue != north || property.GetArrayElementAtIndex(1).objectReferenceValue != south)
                throw new InvalidOperationException("GoalShieldSet.colliders must be ordered exactly [north, south].");
        }

        private static Collider ValidateGoal(Transform arena, string name, GoalTrigger.GoalSide side, ParticipantTeam defendingTeam,
            Vector3 position, Quaternion rotation, PhysicsMaterial ballSurface)
        {
            var root = Require(arena.Find(name), name);
            var trigger = Require(root.GetComponent<GoalTrigger>(), name + " GoalTrigger");
            var opening = Require(root.GetComponent<BoxCollider>(), name + " opening BoxCollider");
            if (!root.gameObject.activeInHierarchy || !trigger.enabled || !opening.enabled || !opening.isTrigger ||
                Vector3.Distance(root.localPosition, position) > TransformTolerance || Quaternion.Angle(root.localRotation, rotation) > RotationTolerance ||
                Vector3.Distance(root.localScale, Vector3.one) > TransformTolerance ||
                Vector3.Distance(opening.center, new Vector3(0f, MovementLabContract.ArenaGoalOpeningCenterY, 0f)) > TransformTolerance ||
                Vector3.Distance(opening.size, new Vector3(MovementLabContract.ArenaGoalOpeningWidth, MovementLabContract.ArenaGoalOpeningHeight, 0.5f)) > TransformTolerance ||
                trigger.Side != side || trigger.DefendingTeam != defendingTeam)
                throw new InvalidOperationException(name + " root/trigger/opening contract invalid.");
            var serialized = new SerializedObject(trigger);
            if (serialized.FindProperty("ball")?.objectReferenceValue == null || serialized.FindProperty("planeReference")?.objectReferenceValue != root ||
                serialized.FindProperty("openingTrigger")?.objectReferenceValue != opening || serialized.FindProperty("planeNormal")?.vector3Value != Vector3.right ||
                Mathf.Abs(serialized.FindProperty("openingHalfWidth").floatValue - MovementLabContract.ArenaGoalOpeningHalfWidth) > TransformTolerance ||
                Mathf.Abs(serialized.FindProperty("openingMinHeight").floatValue) > TransformTolerance ||
                Mathf.Abs(serialized.FindProperty("openingMaxHeight").floatValue - MovementLabContract.ArenaGoalOpeningHeight) > TransformTolerance ||
                Mathf.Abs(serialized.FindProperty("rearmDistance").floatValue - 0.5f) > TransformTolerance)
                throw new InvalidOperationException(name + " serialized trigger contract invalid.");
            ValidateColliderSolid(root, "FrameWest",
                new Vector3(-MovementLabContract.ArenaGoalOpeningFrameCenterX, MovementLabContract.ArenaGoalOpeningCenterY, 0f),
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameThickness, MovementLabContract.ArenaGoalOpeningHeight,
                    MovementLabContract.ArenaGoalOpeningFrameThickness), ballSurface);
            ValidateColliderSolid(root, "FrameEast",
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameCenterX, MovementLabContract.ArenaGoalOpeningCenterY, 0f),
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameThickness, MovementLabContract.ArenaGoalOpeningHeight,
                    MovementLabContract.ArenaGoalOpeningFrameThickness), ballSurface);
            ValidateColliderSolid(root, "FrameTop",
                new Vector3(0f, MovementLabContract.ArenaGoalOpeningLintelCenterY, 0f),
                new Vector3(MovementLabContract.ArenaGoalOpeningWidth + MovementLabContract.ArenaGoalOpeningFrameThickness * 2f,
                    MovementLabContract.ArenaGoalOpeningFrameThickness, MovementLabContract.ArenaGoalOpeningFrameThickness), ballSurface);
            ValidateColliderSolid(root, "RecessWest",
                new Vector3(-MovementLabContract.ArenaGoalOpeningFrameCenterX, MovementLabContract.ArenaGoalOpeningCenterY,
                    MovementLabContract.ArenaGoalRecessDepth * 0.5f),
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameThickness, MovementLabContract.ArenaGoalOpeningHeight,
                    MovementLabContract.ArenaGoalRecessDepth), ballSurface);
            ValidateColliderSolid(root, "RecessEast",
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameCenterX, MovementLabContract.ArenaGoalOpeningCenterY,
                    MovementLabContract.ArenaGoalRecessDepth * 0.5f),
                new Vector3(MovementLabContract.ArenaGoalOpeningFrameThickness, MovementLabContract.ArenaGoalOpeningHeight,
                    MovementLabContract.ArenaGoalRecessDepth), ballSurface);
            ValidateColliderSolid(root, "RecessFloor", new Vector3(0f, -0.25f, MovementLabContract.ArenaGoalRecessDepth * 0.5f),
                new Vector3(MovementLabContract.ArenaGoalRecessBackWidth, 0.5f, MovementLabContract.ArenaGoalRecessDepth), ballSurface);
            ValidateColliderSolid(root, "RecessBack",
                new Vector3(0f, MovementLabContract.ArenaGoalOpeningCenterY, MovementLabContract.ArenaGoalRecessBackCenterZ),
                new Vector3(MovementLabContract.ArenaGoalRecessBackWidth, MovementLabContract.ArenaGoalOpeningHeight, 1f), ballSurface);
            var shieldTransform = Require(root.Find("ShieldCollider"), name + " ShieldCollider");
            var shield = Require(shieldTransform.GetComponent<BoxCollider>(), name + " shield collider");
            if (!shieldTransform.gameObject.activeSelf || !shield.enabled || shield.isTrigger ||
                Vector3.Distance(shieldTransform.localPosition, new Vector3(0f, MovementLabContract.ArenaGoalOpeningCenterY, 0f)) > TransformTolerance ||
                Quaternion.Angle(shieldTransform.localRotation, Quaternion.identity) > RotationTolerance ||
                Vector3.Distance(shieldTransform.localScale, Vector3.one) > TransformTolerance ||
                colliderMismatch(shield, new Vector3(MovementLabContract.ArenaGoalOpeningWidth, MovementLabContract.ArenaGoalOpeningHeight, 0.4f), ballSurface))
                throw new InvalidOperationException(name + " shield collider contract invalid.");
            ValidateShieldVisual(root, name);
            return shield;
        }

        private static bool colliderMismatch(BoxCollider collider, Vector3 size, PhysicsMaterial surface)
        {
            return Vector3.Distance(collider.center, Vector3.zero) > TransformTolerance || Vector3.Distance(collider.size, size) > TransformTolerance ||
                collider.sharedMaterial != surface;
        }

        private static void ValidateColliderSolid(Transform root, string name, Vector3 position, Vector3 scale, PhysicsMaterial surface)
        {
            var item = Require(root.Find(name), root.name + " " + name);
            var collider = Require(item.GetComponent<BoxCollider>(), root.name + " " + name + " collider");
            if (!item.gameObject.activeSelf || !collider.enabled || collider.isTrigger || collider.sharedMaterial != surface ||
                Vector3.Distance(item.localPosition, position) > TransformTolerance || Quaternion.Angle(item.localRotation, Quaternion.identity) > RotationTolerance ||
                Vector3.Distance(item.localScale, scale) > TransformTolerance || collider.center != Vector3.zero || collider.size != Vector3.one ||
                item.GetComponents<Component>().Length != 2 || item.GetComponent<Renderer>() != null || item.GetComponent<Rigidbody>() != null)
                throw new InvalidOperationException(root.name + " physical frame/recess contract invalid: " + name);
        }

        internal static void ValidateShieldVisual(Transform goal, string label)
        {
            var collider = Require(Require(goal?.Find("ShieldCollider"), label + " ShieldCollider").GetComponent<BoxCollider>(), label + " shield collider");
            var visual = Require(goal?.Find("ShieldVisual"), label + " ShieldVisual");
            var renderer = Require(visual.GetComponent<MeshRenderer>(), label + " shield renderer");
            if (!visual.gameObject.activeSelf || collider.isTrigger || visual.GetComponent<Collider>() != null ||
                Vector3.Distance(visual.localPosition, new Vector3(0f, MovementLabContract.ArenaGoalOpeningCenterY, -0.22f)) > TransformTolerance ||
                Quaternion.Angle(visual.localRotation, Quaternion.identity) > RotationTolerance ||
                Vector3.Distance(visual.localScale, new Vector3(MovementLabContract.ArenaGoalOpeningWidth, MovementLabContract.ArenaGoalOpeningHeight, 1f)) > TransformTolerance)
                throw new InvalidOperationException(label + " shield collider/render split invalid.");
            var expectedShield = AssetDatabase.LoadAssetAtPath<Material>(label == "NorthGoal" ? MaterialsPath + "/ShieldRed.mat" : MaterialsPath + "/ShieldBlue.mat");
            var material = renderer.sharedMaterial;
            if (material == null || material != expectedShield || material.shader == null || material.shader.name != "RocketFooxball/RetroShield" ||
                Mathf.Abs(material.GetFloat("_Alpha") - 0.52f) > TransformTolerance)
                throw new InvalidOperationException(label + " shield team material contract invalid.");
            var cueName = label == "NorthGoal" ? "RedTriangleCue" : "BlueCircleCue";
            var cue = Require(goal.Find(cueName), label + " " + cueName);
            var cueRenderer = Require(cue.GetComponent<MeshRenderer>(), label + " team cue renderer");
            var cueMaterial = AssetDatabase.LoadAssetAtPath<Material>(label == "NorthGoal" ? TeamRedMaterialPath : TeamBlueMaterialPath);
            var cueMeshPath = label == "NorthGoal" ? RedTriangleCueMeshPath : BlueCircleCueMeshPath;
            if (!cue.gameObject.activeSelf || Vector3.Distance(cue.localPosition, new Vector3(0f, MovementLabContract.ArenaGoalOpeningCenterY, -0.28f)) > TransformTolerance ||
                Quaternion.Angle(cue.localRotation, Quaternion.Euler(90f, 0f, 0f)) > RotationTolerance || Vector3.Distance(cue.localScale, new Vector3(3.5f, 3.5f, 1f)) > TransformTolerance ||
                cueRenderer.sharedMaterial != cueMaterial || cue.GetComponent<MeshFilter>()?.sharedMesh == null ||
                AssetDatabase.GetAssetPath(cue.GetComponent<MeshFilter>().sharedMesh) != cueMeshPath || cue.GetComponent<Collider>() != null)
                throw new InvalidOperationException(label + " team shape cue contract invalid.");
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == name) return child;
                var nested = FindDescendant(child, name);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
