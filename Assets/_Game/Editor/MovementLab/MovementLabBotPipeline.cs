using System;
using System.Collections.Generic;
using System.Linq;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Pickups;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

using static RocketFooxball.Editor.MovementLabSerializedProperties;

namespace RocketFooxball.Editor
{
    /// <summary>Authoritative MovementLab graph data and bot composition.</summary>
    internal static class MovementLabBotPipeline
    {
        internal const string SystemsRootName = "BotSystems";
        internal const string NavigationGraphName = "NavigationGraph";
        internal const string BlueCoordinatorName = "BlueRoleCoordinator";
        internal const string RedCoordinatorName = "RedRoleCoordinator";

        private const int NodeCount = 34;
        private const int EdgeCount = 98;
        private const float WalkWidth = 3f;
        private const float RampWidth = 6f;
        private const float DropWidth = 2f;
        private const float ShieldWidth = 16f;
        private const float RampCost = 1.15f;
        private const float DropCost = 1.25f;
        private const float ClearanceQueryRadius = 0.36f;
        private const int ClearanceBufferSize = 32;

        private static readonly float[] LaneXs = { -52f, -12f, 0f, 12f, 52f };
        private static readonly float[] LaneZs = { -28f, 0f, 28f };

        internal static void ComposeScene(
            ParticipantState[] roster,
            MatchController match,
            BallMotor ball,
            ArenaPickup[] pickups,
            GoalTrigger redDefendedGoal,
            GoalTrigger blueDefendedGoal,
            Collider redShield,
            Collider blueShield)
        {
            ValidateComposeArguments(roster, match, ball, pickups, redDefendedGoal, blueDefendedGoal, redShield, blueShield);

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) throw new InvalidOperationException("Bot composition requires a valid active MovementLab scene.");
            if (scene.GetRootGameObjects().Any(root => root != null && root.name == SystemsRootName))
                throw new InvalidOperationException("MovementLab contains a duplicate BotSystems root.");

            var rampWest = GameObject.Find("Arena/RampWest")?.transform;
            var rampEast = GameObject.Find("Arena/RampEast")?.transform;
            if (rampWest == null || rampEast == null)
                throw new InvalidOperationException("Bot composition requires Arena/RampWest and Arena/RampEast.");

            var systems = new GameObject(SystemsRootName);
            var graphObject = new GameObject(NavigationGraphName);
            graphObject.transform.SetParent(systems.transform, false);
            var graph = graphObject.AddComponent<BotNavigationGraph>();
            WriteGraph(graph, BuildNodes(rampWest, rampEast), BuildEdges(rampWest, rampEast, redShield, blueShield));

            var blueObject = new GameObject(BlueCoordinatorName);
            blueObject.transform.SetParent(systems.transform, false);
            var blueCoordinator = blueObject.AddComponent<BotTeamRoleCoordinator>();
            var redObject = new GameObject(RedCoordinatorName);
            redObject.transform.SetParent(systems.transform, false);
            var redCoordinator = redObject.AddComponent<BotTeamRoleCoordinator>();
            blueCoordinator.enabled = false;
            redCoordinator.enabled = false;

            var obstacleMask = LayerMaskMaskWithout(MovementLabContract.ParticipantsLayerName, MovementLabContract.ProjectilesLayerName);
            for (var i = 0; i < roster.Length; i++)
            {
                var participant = roster[i];
                var controller = participant.GetComponent<BotController>();
                var navigator = participant.GetComponent<BotNavigator>();
                var perception = participant.GetComponent<BotPerception>();
                if (controller == null || navigator == null || perception == null ||
                    participant.GetComponents<BotController>().Length != 1 ||
                    participant.GetComponents<BotNavigator>().Length != 1 ||
                    participant.GetComponents<BotPerception>().Length != 1)
                {
                    throw new InvalidOperationException("Each participant must contain exactly one BotController, BotNavigator, and BotPerception.");
                }

                controller.enabled = false;
                navigator.enabled = false;
                perception.enabled = false;
                SetObjectReference(participant, "botController", controller);
                SetObjectReference(navigator, "graph", graph);
                SetLayerMask(navigator, "groundMask", 1);
                SetFloat(navigator, "arrivalDistance", 0.75f);
                SetFloat(navigator, "verticalArrivalDistance", 0.65f);
                SetFloat(navigator, "lookaheadDistance", 3f);
                SetFloat(navigator, "targetReplanDistance", 1.5f);
                SetFloat(navigator, "corridorMargin", BotNavigationGraph.ExpectedControllerClearance);
                SetFloat(navigator, "ledgeProbeDistance", 1.5f);
                SetFloat(navigator, "ledgeProbeDepth", 2.5f);

                SetObjectReference(perception, "self", participant);
                SetObjectReference(perception, "match", match);
                SetObjectReference(perception, "ball", ball);
                SetObjectReference(perception, "head", participant.transform.Find("Head"));
                SetObjectArray(perception, "roster", roster.Cast<UnityEngine.Object>().ToArray());
                SetObjectArray(perception, "pickups", pickups.Cast<UnityEngine.Object>().ToArray());
                SetObjectReference(perception, "ownGoal", participant.Team == ParticipantTeam.Red ? redDefendedGoal : blueDefendedGoal);
                SetObjectReference(perception, "enemyGoal", participant.Team == ParticipantTeam.Red ? blueDefendedGoal : redDefendedGoal);
                SetLayerMask(perception, "obstacleMask", obstacleMask);
                SetFloat(perception, "sightDistance", BotPerception.ExpectedSightDistance);
                SetFloat(perception, "fieldOfViewDegrees", BotPerception.ExpectedFieldOfViewDegrees);
                SetFloat(perception, "memorySeconds", BotPerception.ExpectedMemorySeconds);

                var coordinator = participant.Team == ParticipantTeam.Blue ? blueCoordinator : redCoordinator;
                SetObjectReference(controller, "participant", participant);
                SetObjectReference(controller, "match", match);
                SetObjectReference(controller, "motor", participant.Motor);
                SetObjectReference(controller, "head", participant.transform.Find("Head"));
                SetObjectReference(controller, "kick", participant.Kick);
                SetObjectReference(controller, "launcher", participant.Launcher);
                SetObjectReference(controller, "shotgun", participant.Shotgun);
                SetObjectReference(controller, "navigator", navigator);
                SetObjectReference(controller, "perception", perception);
                SetObjectReference(controller, "roleCoordinator", coordinator);
                SetLayerMask(controller, "combatObstacleMask", obstacleMask);
                SetFloat(controller, "maxPitchDegrees", BotController.ExpectedMaxPitchDegrees);
                SetFloat(controller, "jumpProbeDistance", BotNavigationGraph.ExpectedRocketJumpGroundProbeDistance);
            }

            SetEnum(blueCoordinator, "team", "Blue");
            SetObjectReference(blueCoordinator, "match", match);
            SetObjectReference(blueCoordinator, "navigationGraph", graph);
            SetObjectArray(blueCoordinator, "participants", roster.Take(3).Cast<UnityEngine.Object>().ToArray());
            SetObjectArray(blueCoordinator, "perceptions", new[] { roster[1].GetComponent<BotPerception>(), roster[2].GetComponent<BotPerception>() }.Cast<UnityEngine.Object>().ToArray());
            SetCoordinatorTuning(blueCoordinator);

            SetEnum(redCoordinator, "team", "Red");
            SetObjectReference(redCoordinator, "match", match);
            SetObjectReference(redCoordinator, "navigationGraph", graph);
            SetObjectArray(redCoordinator, "participants", roster.Skip(3).Take(3).Cast<UnityEngine.Object>().ToArray());
            SetObjectArray(redCoordinator, "perceptions", new[] { roster[3].GetComponent<BotPerception>(), roster[4].GetComponent<BotPerception>(), roster[5].GetComponent<BotPerception>() }.Cast<UnityEngine.Object>().ToArray());
            SetCoordinatorTuning(redCoordinator);

            blueCoordinator.enabled = true;
            redCoordinator.enabled = true;
            for (var slot = 1; slot < roster.Length; slot++)
            {
                roster[slot].GetComponent<BotPerception>().enabled = true;
                roster[slot].GetComponent<BotNavigator>().enabled = true;
                roster[slot].GetComponent<BotController>().enabled = true;
            }
        }

        internal static void ValidateScene(
            Scene scene,
            ParticipantState[] roster,
            MatchController match,
            BallMotor ball,
            ArenaPickup[] pickups,
            GoalTrigger redDefendedGoal,
            GoalTrigger blueDefendedGoal,
            Collider redShield,
            Collider blueShield)
        {
            ValidateComposeArguments(roster, match, ball, pickups, redDefendedGoal, blueDefendedGoal, redShield, blueShield);
            if (!scene.IsValid()) throw new InvalidOperationException("Bot validation requires a valid reopened scene.");
            var systems = scene.GetRootGameObjects().SingleOrDefault(root => root != null && root.name == SystemsRootName);
            if (systems == null) throw new InvalidOperationException("MovementLab requires exactly one root-level BotSystems object.");
            if (scene.GetRootGameObjects().Count(root => root != null && root.name == SystemsRootName) != 1)
                throw new InvalidOperationException("MovementLab contains duplicate BotSystems roots.");
            if (systems.transform.childCount != 3)
                throw new InvalidOperationException("BotSystems must contain exactly NavigationGraph, BlueRoleCoordinator, and RedRoleCoordinator children.");

            var graphObject = RequireSingleChild(systems.transform, NavigationGraphName);
            var blueObject = RequireSingleChild(systems.transform, BlueCoordinatorName);
            var redObject = RequireSingleChild(systems.transform, RedCoordinatorName);
            var graph = RequireSingleComponent<BotNavigationGraph>(graphObject.gameObject, NavigationGraphName);
            var blueCoordinator = RequireSingleComponent<BotTeamRoleCoordinator>(blueObject.gameObject, BlueCoordinatorName);
            var redCoordinator = RequireSingleComponent<BotTeamRoleCoordinator>(redObject.gameObject, RedCoordinatorName);
            if (UnityEngine.Object.FindObjectsByType<BotController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 6 ||
                UnityEngine.Object.FindObjectsByType<BotNavigator>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 6 ||
                UnityEngine.Object.FindObjectsByType<BotPerception>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 6 ||
                UnityEngine.Object.FindObjectsByType<BotNavigationGraph>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 1 ||
                UnityEngine.Object.FindObjectsByType<BotTeamRoleCoordinator>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 2)
                throw new InvalidOperationException("MovementLab must contain exactly six participant bot sets, one graph, and two coordinators; the builder produced a different component population. Check bot composition for duplicate instantiation or stray AddComponent calls.");
            if (!blueCoordinator.enabled || !redCoordinator.enabled)
                throw new InvalidOperationException("Both bot role coordinators must be enabled after composition.");
            if (!graph.TryValidate(out var graphReason)) throw new InvalidOperationException("Bot navigation graph is invalid: " + graphReason);

            var rampWest = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).FirstOrDefault(item => item.name == "RampWest");
            var rampEast = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).FirstOrDefault(item => item.name == "RampEast");
            if (rampWest == null || rampEast == null) throw new InvalidOperationException("Bot validation requires both authored ramps.");
            ValidateGraphGeometry(graph, rampWest, rampEast, redShield, blueShield);
            ValidateNodeClearance(graph);
            ValidateProductionRecesses(graph, redShield, blueShield);
            if (Vector3.Distance(roster[0].transform.position, graph.GetNode(10).Position) > 0.001f ||
                Vector3.Distance(roster[3].transform.position, graph.GetNode(4).Position) > 0.001f)
                throw new InvalidOperationException("Local Blue slot 0 and first Red slot 3 must reuse graph spawn nodes 10 and 4.");

            var inputAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(MovementLabContract.InputActionsPath);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MovementLabContract.PlayerPrefabPath);
            if (playerPrefab == null) throw new InvalidOperationException("Bot validation requires the Player prefab.");
            var expectedInput = playerPrefab.GetComponent<PlayerInputReader>();
            var expectedController = playerPrefab.GetComponent<BotController>();
            var expectedNavigator = playerPrefab.GetComponent<BotNavigator>();
            var expectedPerception = playerPrefab.GetComponent<BotPerception>();
            if (expectedInput == null || expectedController == null || expectedNavigator == null || expectedPerception == null ||
                playerPrefab.GetComponents<PlayerInputReader>().Length != 1 || playerPrefab.GetComponents<BotController>().Length != 1 ||
                playerPrefab.GetComponents<BotNavigator>().Length != 1 || playerPrefab.GetComponents<BotPerception>().Length != 1)
                throw new InvalidOperationException("Bot validation requires exactly one PlayerInputReader, BotController, BotNavigator, and BotPerception on the Player prefab.");
            for (var i = 0; i < roster.Length; i++)
            {
                var participant = roster[i];
                if (participant.GetComponents<PlayerInputReader>().Length != 1 || participant.GetComponents<BotController>().Length != 1 ||
                    participant.GetComponents<BotNavigator>().Length != 1 || participant.GetComponents<BotPerception>().Length != 1)
                    throw new InvalidOperationException("Participant " + i + " must contain exactly one bot component set.");
                var controller = participant.GetComponent<BotController>();
                var navigator = participant.GetComponent<BotNavigator>();
                var perception = participant.GetComponent<BotPerception>();
                var input = participant.GetComponent<PlayerInputReader>();
                ValidatePrefabComponentSource(input, expectedInput, participant.DisplayName + ".PlayerInputReader");
                ValidatePrefabComponentSource(controller, expectedController, participant.DisplayName + ".BotController");
                ValidatePrefabComponentSource(navigator, expectedNavigator, participant.DisplayName + ".BotNavigator");
                ValidatePrefabComponentSource(perception, expectedPerception, participant.DisplayName + ".BotPerception");
                var expectedEnabled = i != 0;
                if (controller.enabled != expectedEnabled || navigator.enabled != expectedEnabled || perception.enabled != expectedEnabled)
                    throw new InvalidOperationException("Bot component enabled state mismatch for participant slot " + i + ".");
                ValidateReference(participant, "botController", controller, "Participant.botController");
                ValidateReference(navigator, "graph", graph, "BotNavigator.graph");
                ValidateSerializedLayerMask(navigator, "groundMask", 1, "BotNavigator.groundMask");
                ValidateNavigatorTuning(navigator);
                ValidateReference(perception, "self", participant, "BotPerception.self");
                ValidateReference(perception, "match", match, "BotPerception.match");
                ValidateReference(perception, "ball", ball, "BotPerception.ball");
                ValidateReference(perception, "head", participant.transform.Find("Head"), "BotPerception.head");
                ValidateObjectArray(perception, "roster", roster.Cast<UnityEngine.Object>().ToArray(), "BotPerception.roster");
                ValidateObjectArray(perception, "pickups", pickups.Cast<UnityEngine.Object>().ToArray(), "BotPerception.pickups");
                ValidateReference(perception, "ownGoal", participant.Team == ParticipantTeam.Red ? redDefendedGoal : blueDefendedGoal, "BotPerception.ownGoal");
                ValidateReference(perception, "enemyGoal", participant.Team == ParticipantTeam.Red ? blueDefendedGoal : redDefendedGoal, "BotPerception.enemyGoal");
                ValidateSerializedLayerMask(perception, "obstacleMask", LayerMaskMaskWithout(MovementLabContract.ParticipantsLayerName, MovementLabContract.ProjectilesLayerName), "BotPerception.obstacleMask");
                ValidateSerializedFloat(perception, "sightDistance", BotPerception.ExpectedSightDistance, "BotPerception.sightDistance");
                ValidateSerializedFloat(perception, "fieldOfViewDegrees", BotPerception.ExpectedFieldOfViewDegrees, "BotPerception.fieldOfViewDegrees");
                ValidateSerializedFloat(perception, "memorySeconds", BotPerception.ExpectedMemorySeconds, "BotPerception.memorySeconds");
                ValidateReference(controller, "participant", participant, "BotController.participant");
                ValidateReference(controller, "match", match, "BotController.match");
                ValidateReference(controller, "motor", participant.Motor, "BotController.motor");
                ValidateReference(controller, "head", participant.transform.Find("Head"), "BotController.head");
                ValidateReference(controller, "kick", participant.Kick, "BotController.kick");
                ValidateReference(controller, "launcher", participant.Launcher, "BotController.launcher");
                ValidateReference(controller, "shotgun", participant.Shotgun, "BotController.shotgun");
                ValidateReference(controller, "navigator", navigator, "BotController.navigator");
                ValidateReference(controller, "perception", perception, "BotController.perception");
                ValidateReference(controller, "roleCoordinator", participant.Team == ParticipantTeam.Blue ? blueCoordinator : redCoordinator, "BotController.roleCoordinator");
                ValidateSerializedLayerMask(controller, "combatObstacleMask", LayerMaskMaskWithout(MovementLabContract.ParticipantsLayerName, MovementLabContract.ProjectilesLayerName), "BotController.combatObstacleMask");
                ValidateSerializedFloat(controller, "maxPitchDegrees", BotController.ExpectedMaxPitchDegrees, "BotController.maxPitchDegrees");
                ValidateSerializedFloat(controller, "jumpProbeDistance", BotNavigationGraph.ExpectedRocketJumpGroundProbeDistance, "BotController.jumpProbeDistance");
                ValidateReference(input, "actions", inputAsset, "PlayerInputReader.actions");
            }

            ValidateCoordinator(blueCoordinator, match, ParticipantTeam.Blue, roster.Take(3).ToArray(), new[] { roster[1].GetComponent<BotPerception>(), roster[2].GetComponent<BotPerception>() });
            ValidateCoordinator(redCoordinator, match, ParticipantTeam.Red, roster.Skip(3).Take(3).ToArray(), new[] { roster[3].GetComponent<BotPerception>(), roster[4].GetComponent<BotPerception>(), roster[5].GetComponent<BotPerception>() });
            ValidateExecutionOrders();
            ValidatePauseAdapters();
        }

        private static void ValidateComposeArguments(
            ParticipantState[] roster,
            MatchController match,
            BallMotor ball,
            ArenaPickup[] pickups,
            GoalTrigger redDefendedGoal,
            GoalTrigger blueDefendedGoal,
            Collider redShield,
            Collider blueShield)
        {
            if (roster == null || roster.Length != 6 || roster.Any(item => item == null) ||
                roster.Select(item => item.SlotId).Distinct().Count() != 6)
                throw new InvalidOperationException("Bot composition requires an exact six-participant roster.");
            if (match == null || ball == null || pickups == null || pickups.Length != 5 || pickups.Any(item => item == null) ||
                redDefendedGoal == null || blueDefendedGoal == null || redShield == null || blueShield == null)
                throw new InvalidOperationException("Bot composition requires match, ball, five pickups, both goals, and both shields.");
        }

        private static BotNavigationNodeRecord[] BuildNodes(Transform rampWest, Transform rampEast)
        {
            var nodes = new List<BotNavigationNodeRecord>(NodeCount);
            var id = 0;
            for (var x = 0; x < LaneXs.Length; x++)
            {
                for (var z = 0; z < LaneZs.Length; z++)
                {
                    nodes.Add(new BotNavigationNodeRecord(id++, new Vector3(LaneXs[x], 0f, LaneZs[z]), BotNavigationArea.Floor, 4f));
                }
            }
            nodes.Add(new BotNavigationNodeRecord(15, new Vector3(12f, 0f, -10f), BotNavigationArea.Floor, 2f));
            nodes.Add(new BotNavigationNodeRecord(16, new Vector3(12f, 0f, 10f), BotNavigationArea.Floor, 2f));
            nodes.Add(new BotNavigationNodeRecord(17, new Vector3(-12f, 0f, 10f), BotNavigationArea.Floor, 2f));
            nodes.Add(new BotNavigationNodeRecord(18, new Vector3(-12f, 0f, -10f), BotNavigationArea.Floor, 2f));
            nodes.Add(new BotNavigationNodeRecord(19, new Vector3(-36f, 0f, -28f), BotNavigationArea.Floor, 2f));
            nodes.Add(new BotNavigationNodeRecord(20, new Vector3(36f, 0f, 28f), BotNavigationArea.Floor, 2f));
            nodes.Add(new BotNavigationNodeRecord(21, new Vector3(0f, 0f, 14f), BotNavigationArea.Floor, 2f));
            nodes.Add(new BotNavigationNodeRecord(22, new Vector3(-38f, 0f, 18f), BotNavigationArea.Floor, 2f));
            nodes.Add(new BotNavigationNodeRecord(23, new Vector3(38f, 0f, -18f), BotNavigationArea.Floor, 2f));
            nodes.Add(new BotNavigationNodeRecord(24, rampWest.position + rampWest.rotation * new Vector3(0f, 0.25f, -8f), BotNavigationArea.RampDeck, 1.25f));
            nodes.Add(new BotNavigationNodeRecord(25, rampWest.position + rampWest.rotation * new Vector3(0f, 0.25f, 8f), BotNavigationArea.RampDeck, 1.25f));
            nodes.Add(new BotNavigationNodeRecord(26, rampEast.position + rampEast.rotation * new Vector3(0f, 0.25f, -8f), BotNavigationArea.RampDeck, 1.25f));
            nodes.Add(new BotNavigationNodeRecord(27, rampEast.position + rampEast.rotation * new Vector3(0f, 0.25f, 8f), BotNavigationArea.RampDeck, 1.25f));
            nodes.Add(new BotNavigationNodeRecord(28, new Vector3(-34f, 0f, 2f), BotNavigationArea.Floor, 1.5f));
            nodes.Add(new BotNavigationNodeRecord(29, new Vector3(34f, 0f, -2f), BotNavigationArea.Floor, 1.5f));
            nodes.Add(new BotNavigationNodeRecord(30, new Vector3(-62f, 0f, 0f), BotNavigationArea.Floor, 2f));
            nodes.Add(new BotNavigationNodeRecord(31, new Vector3(-65.5f, 0f, 0f), BotNavigationArea.GoalRecess, BotNavigationGraph.ExpectedGoalRecessSafeRadius));
            nodes.Add(new BotNavigationNodeRecord(32, new Vector3(62f, 0f, 0f), BotNavigationArea.Floor, 2f));
            nodes.Add(new BotNavigationNodeRecord(33, new Vector3(65.5f, 0f, 0f), BotNavigationArea.GoalRecess, BotNavigationGraph.ExpectedGoalRecessSafeRadius));
            return nodes.ToArray();
        }

        private static BotNavigationEdgeRecord[] BuildEdges(Transform rampWest, Transform rampEast, Collider redShield, Collider blueShield)
        {
            var nodes = BuildNodes(rampWest, rampEast);
            var edges = new List<BotNavigationEdgeRecord>(EdgeCount);
            var edgeId = 0;
            for (var column = 0; column < 5; column++)
            {
                var first = column * 3;
                AddPair(edges, ref edgeId, nodes, first, first + 1, BotNavigationTraversal.Walk, WalkWidth, null);
                AddPair(edges, ref edgeId, nodes, first + 1, first + 2, BotNavigationTraversal.Walk, WalkWidth, null);
            }
            foreach (var zRow in new[] { 0, 2 })
            {
                var ids = new[] { zRow, zRow + 3, zRow + 6, zRow + 9, zRow + 12 };
                for (var i = 0; i < ids.Length - 1; i++) AddPair(edges, ref edgeId, nodes, ids[i], ids[i + 1], BotNavigationTraversal.Walk, WalkWidth, null);
            }
            AddPair(edges, ref edgeId, nodes, 4, 7, BotNavigationTraversal.Walk, WalkWidth, null);
            AddPair(edges, ref edgeId, nodes, 7, 10, BotNavigationTraversal.Walk, WalkWidth, null);
            foreach (var tuple in new[] { new[] { 9, 15 }, new[] { 10, 15 }, new[] { 10, 16 }, new[] { 11, 16 }, new[] { 4, 17 }, new[] { 5, 17 }, new[] { 3, 18 }, new[] { 4, 18 } })
                AddPair(edges, ref edgeId, nodes, tuple[0], tuple[1], BotNavigationTraversal.Walk, WalkWidth, null);
            foreach (var pickup in new[] { new[] { 0, 3, 19 }, new[] { 11, 14, 20 }, new[] { 7, 8, 21 }, new[] { 2, 5, 22 }, new[] { 9, 12, 23 } })
            {
                AddPair(edges, ref edgeId, nodes, pickup[0], pickup[2], BotNavigationTraversal.Walk, WalkWidth, null);
                AddPair(edges, ref edgeId, nodes, pickup[1], pickup[2], BotNavigationTraversal.Walk, WalkWidth, null);
            }
            AddPair(edges, ref edgeId, nodes, 4, 24, BotNavigationTraversal.Walk, WalkWidth, null);
            AddPair(edges, ref edgeId, nodes, 24, 25, BotNavigationTraversal.Ramp, RampWidth, null);
            AddOneWay(edges, ref edgeId, nodes, 25, 28, BotNavigationTraversal.Drop, DropWidth, null);
            AddPair(edges, ref edgeId, nodes, 28, 1, BotNavigationTraversal.Walk, WalkWidth, null);
            AddPair(edges, ref edgeId, nodes, 10, 26, BotNavigationTraversal.Walk, WalkWidth, null);
            AddPair(edges, ref edgeId, nodes, 26, 27, BotNavigationTraversal.Ramp, RampWidth, null);
            AddOneWay(edges, ref edgeId, nodes, 27, 29, BotNavigationTraversal.Drop, DropWidth, null);
            AddPair(edges, ref edgeId, nodes, 29, 13, BotNavigationTraversal.Walk, WalkWidth, null);
            AddPair(edges, ref edgeId, nodes, 1, 30, BotNavigationTraversal.Walk, WalkWidth, null);
            AddPair(edges, ref edgeId, nodes, 30, 31, BotNavigationTraversal.ShieldGate, ShieldWidth, redShield);
            AddPair(edges, ref edgeId, nodes, 13, 32, BotNavigationTraversal.Walk, WalkWidth, null);
            AddPair(edges, ref edgeId, nodes, 32, 33, BotNavigationTraversal.ShieldGate, ShieldWidth, blueShield);
            if (edges.Count != EdgeCount) throw new InvalidOperationException("Bot navigation edge catalog must contain exactly 98 edges.");
            return edges.ToArray();
        }

        private static void AddPair(List<BotNavigationEdgeRecord> edges, ref int id, BotNavigationNodeRecord[] nodes, int from, int to, BotNavigationTraversal traversal, float width, Collider gate)
        {
            AddOneWay(edges, ref id, nodes, from, to, traversal, width, gate);
            AddOneWay(edges, ref id, nodes, to, from, traversal, width, gate);
        }

        private static void AddOneWay(List<BotNavigationEdgeRecord> edges, ref int id, BotNavigationNodeRecord[] nodes, int from, int to, BotNavigationTraversal traversal, float width, Collider gate)
        {
            var distance = Vector3.Distance(nodes[from].Position, nodes[to].Position);
            var multiplier = traversal == BotNavigationTraversal.Ramp ? RampCost : traversal == BotNavigationTraversal.Drop ? DropCost : 1f;
            edges.Add(new BotNavigationEdgeRecord(id++, from, to, traversal, distance * multiplier, width, gate));
        }

        private static void WriteGraph(BotNavigationGraph graph, BotNavigationNodeRecord[] nodes, BotNavigationEdgeRecord[] edges)
        {
            var serialized = new SerializedObject(graph);
            var nodeArray = serialized.FindProperty("nodes");
            var edgeArray = serialized.FindProperty("edges");
            nodeArray.arraySize = nodes.Length;
            edgeArray.arraySize = edges.Length;
            for (var i = 0; i < nodes.Length; i++)
            {
                var item = nodeArray.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("id").intValue = nodes[i].Id;
                item.FindPropertyRelative("position").vector3Value = nodes[i].Position;
                item.FindPropertyRelative("area").enumValueIndex = (int)nodes[i].Area;
                item.FindPropertyRelative("safeRadius").floatValue = nodes[i].SafeRadius;
            }
            for (var i = 0; i < edges.Length; i++)
            {
                var item = edgeArray.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("id").intValue = edges[i].Id;
                item.FindPropertyRelative("fromNodeId").intValue = edges[i].FromNodeId;
                item.FindPropertyRelative("toNodeId").intValue = edges[i].ToNodeId;
                item.FindPropertyRelative("traversal").enumValueIndex = (int)edges[i].Traversal;
                item.FindPropertyRelative("cost").floatValue = edges[i].Cost;
                item.FindPropertyRelative("corridorHalfWidth").floatValue = edges[i].CorridorHalfWidth;
                item.FindPropertyRelative("gateCollider").objectReferenceValue = edges[i].GateCollider;
            }
            serialized.FindProperty("controllerRadius").floatValue = BotNavigationGraph.ExpectedControllerRadius;
            serialized.FindProperty("controllerHeight").floatValue = BotNavigationGraph.ExpectedControllerHeight;
            serialized.FindProperty("controllerCenter").vector3Value = BotNavigationGraph.ExpectedControllerCenter;
            serialized.FindProperty("controllerSlopeLimit").floatValue = BotNavigationGraph.ExpectedControllerSlopeLimit;
            serialized.FindProperty("controllerStepOffset").floatValue = BotNavigationGraph.ExpectedControllerStepOffset;
            serialized.FindProperty("controllerSkinWidth").floatValue = BotNavigationGraph.ExpectedControllerSkinWidth;
            var bounds = serialized.FindProperty("arenaBounds");
            if (bounds == null) throw new InvalidOperationException("BotNavigationGraph arena bounds property is missing.");
            bounds.FindPropertyRelative("center").vector3Value = BotNavigationGraph.ExpectedArenaCenter;
            bounds.FindPropertyRelative("halfLength").floatValue = BotNavigationGraph.ExpectedArenaHalfLength;
            bounds.FindPropertyRelative("halfWidth").floatValue = BotNavigationGraph.ExpectedArenaHalfWidth;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetCoordinatorTuning(BotTeamRoleCoordinator coordinator)
        {
            SetFloat(coordinator, "evaluationInterval", BotTeamRoleCoordinator.ExpectedEvaluationInterval);
            SetFloat(coordinator, "roleHoldSeconds", BotTeamRoleCoordinator.ExpectedRoleHoldSeconds);
            SetFloat(coordinator, "switchMargin", BotTeamRoleCoordinator.ExpectedSwitchMargin);
            SetFloat(coordinator, "humanShotgunYieldDistance", BotTargetRules.HumanShotgunYieldDistance);
            SetFloat(coordinator, "healthYieldDistance", BotTargetRules.HealthYieldDistance);
            SetFloat(coordinator, "criticalHealthRatio", BotTargetRules.CriticalHealthRatio);
        }

        private static int LayerMaskMaskWithout(params string[] layerNames)
        {
            var mask = ~0;
            for (var i = 0; i < layerNames.Length; i++)
            {
                var layer = LayerMask.NameToLayer(layerNames[i]);
                if (layer >= 0) mask &= ~(1 << layer);
            }
            return mask;
        }

        private static void SetLayerMask(UnityEngine.Object target, string propertyName, int value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.LayerMask)
                throw new InvalidOperationException(target.GetType().Name + " has no serialized layer mask '" + propertyName + "'.");
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform RequireSingleChild(Transform root, string name)
        {
            var matches = Enumerable.Range(0, root.childCount).Select(root.GetChild).Where(child => child.name == name).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("BotSystems requires exactly one child named " + name + ".");
            return matches[0];
        }

        private static T RequireSingleComponent<T>(GameObject gameObject, string label) where T : Component
        {
            var components = gameObject.GetComponents<T>();
            if (components.Length != 1) throw new InvalidOperationException(label + " requires exactly one " + typeof(T).Name + " component.");
            return components[0];
        }

        private static void ValidateGraphGeometry(BotNavigationGraph graph, Transform rampWest, Transform rampEast, Collider redShield, Collider blueShield)
        {
            if (graph.NodeCount != NodeCount || graph.EdgeCount != EdgeCount) throw new InvalidOperationException("Bot graph must contain nodes 0..33 and edges 0..97.");
            for (var column = 0; column < LaneXs.Length; column++)
                for (var z = 0; z < LaneZs.Length; z++)
                    ValidateNode(graph.GetNode(column * 3 + z), column * 3 + z, new Vector3(LaneXs[column], 0f, LaneZs[z]), BotNavigationArea.Floor, 4f);
            ValidateNode(graph.GetNode(15), 15, new Vector3(12f, 0f, -10f), BotNavigationArea.Floor, 2f);
            ValidateNode(graph.GetNode(16), 16, new Vector3(12f, 0f, 10f), BotNavigationArea.Floor, 2f);
            ValidateNode(graph.GetNode(17), 17, new Vector3(-12f, 0f, 10f), BotNavigationArea.Floor, 2f);
            ValidateNode(graph.GetNode(18), 18, new Vector3(-12f, 0f, -10f), BotNavigationArea.Floor, 2f);
            ValidateNode(graph.GetNode(19), 19, new Vector3(-36f, 0f, -28f), BotNavigationArea.Floor, 2f);
            ValidateNode(graph.GetNode(20), 20, new Vector3(36f, 0f, 28f), BotNavigationArea.Floor, 2f);
            ValidateNode(graph.GetNode(21), 21, new Vector3(0f, 0f, 14f), BotNavigationArea.Floor, 2f);
            ValidateNode(graph.GetNode(22), 22, new Vector3(-38f, 0f, 18f), BotNavigationArea.Floor, 2f);
            ValidateNode(graph.GetNode(23), 23, new Vector3(38f, 0f, -18f), BotNavigationArea.Floor, 2f);
            ValidateNode(graph.GetNode(24), 24, new Vector3(-14.207888f, 0.270929f, 2f), BotNavigationArea.RampDeck, 1.25f);
            ValidateNode(graph.GetNode(25), 25, new Vector3(-29.662702f, 4.412034f, 2f), BotNavigationArea.RampDeck, 1.25f);
            ValidateNode(graph.GetNode(26), 26, new Vector3(14.207888f, 0.270929f, -2f), BotNavigationArea.RampDeck, 1.25f);
            ValidateNode(graph.GetNode(27), 27, new Vector3(29.662702f, 4.412034f, -2f), BotNavigationArea.RampDeck, 1.25f);
            ValidateNode(graph.GetNode(28), 28, new Vector3(-34f, 0f, 2f), BotNavigationArea.Floor, 1.5f);
            ValidateNode(graph.GetNode(29), 29, new Vector3(34f, 0f, -2f), BotNavigationArea.Floor, 1.5f);
            ValidateNode(graph.GetNode(30), 30, new Vector3(-62f, 0f, 0f), BotNavigationArea.Floor, 2f);
            ValidateNode(graph.GetNode(31), 31, new Vector3(-65.5f, 0f, 0f), BotNavigationArea.GoalRecess, BotNavigationGraph.ExpectedGoalRecessSafeRadius);
            ValidateNode(graph.GetNode(32), 32, new Vector3(62f, 0f, 0f), BotNavigationArea.Floor, 2f);
            ValidateNode(graph.GetNode(33), 33, new Vector3(65.5f, 0f, 0f), BotNavigationArea.GoalRecess, BotNavigationGraph.ExpectedGoalRecessSafeRadius);
            if (graph.ArenaBounds == null || graph.ArenaBounds.Center != BotNavigationGraph.ExpectedArenaCenter ||
                Mathf.Abs(graph.ArenaBounds.HalfLength - BotNavigationGraph.ExpectedArenaHalfLength) > 0.001f ||
                Mathf.Abs(graph.ArenaBounds.HalfWidth - BotNavigationGraph.ExpectedArenaHalfWidth) > 0.001f)
                throw new InvalidOperationException("Bot arena bounds must match the authored 65 by 45 field.");
            var expectedWestLow = rampWest.position + rampWest.rotation * new Vector3(0f, 0.25f, -8f);
            var expectedWestHigh = rampWest.position + rampWest.rotation * new Vector3(0f, 0.25f, 8f);
            var expectedEastLow = rampEast.position + rampEast.rotation * new Vector3(0f, 0.25f, -8f);
            var expectedEastHigh = rampEast.position + rampEast.rotation * new Vector3(0f, 0.25f, 8f);
            if (Vector3.Distance(graph.GetNode(24).Position, expectedWestLow) > 0.001f || Vector3.Distance(graph.GetNode(25).Position, expectedWestHigh) > 0.001f ||
                Vector3.Distance(graph.GetNode(26).Position, expectedEastLow) > 0.001f || Vector3.Distance(graph.GetNode(27).Position, expectedEastHigh) > 0.001f)
                throw new InvalidOperationException("Ramp graph nodes must use authored rotation-relative offsets without scale-multiplying transform offsets.");
            var expectedEdges = BuildEdges(rampWest, rampEast, redShield, blueShield);
            for (var i = 0; i < graph.EdgeCount; i++)
            {
                var edge = graph.GetEdge(i);
                if (edge.Id != i) throw new InvalidOperationException("Bot edge IDs must be contiguous and ordered.");
                var expected = expectedEdges[i];
                if (edge.FromNodeId != expected.FromNodeId || edge.ToNodeId != expected.ToNodeId || edge.Traversal != expected.Traversal ||
                    Mathf.Abs(edge.Cost - expected.Cost) > 0.001f || Mathf.Abs(edge.CorridorHalfWidth - expected.CorridorHalfWidth) > 0.001f || edge.GateCollider != expected.GateCollider)
                    throw new InvalidOperationException("Bot edge " + edge.Id + " does not match the authored deterministic catalog.");
                if (edge.Traversal == BotNavigationTraversal.ShieldGate)
                {
                    var isRedGate = (edge.FromNodeId == 30 && edge.ToNodeId == 31) || (edge.FromNodeId == 31 && edge.ToNodeId == 30);
                    var isBlueGate = (edge.FromNodeId == 32 && edge.ToNodeId == 33) || (edge.FromNodeId == 33 && edge.ToNodeId == 32);
                    var expectedGate = isRedGate ? redShield : isBlueGate ? blueShield : null;
                    if (expectedGate == null || edge.GateCollider != expectedGate)
                        throw new InvalidOperationException("Bot shield-gate collider reference mismatch at edge " + edge.Id + ".");
                }
                else if (edge.GateCollider != null)
                {
                    throw new InvalidOperationException("Only shield-gate edges may reference a gate collider.");
                }
                if (edge.Traversal == BotNavigationTraversal.Drop && (edge.ToNodeId == 24 || edge.ToNodeId == 26))
                    throw new InvalidOperationException("Drop edges must remain one-way from ramp deck to floor.");
            }
        }

        private static void ValidateNode(BotNavigationNodeRecord node, int id, Vector3 position, BotNavigationArea area, float safeRadius)
        {
            if (node == null || node.Id != id || node.Area != area || node.SafeRadius < safeRadius - 0.001f || Vector3.Distance(node.Position, position) > 0.001f)
                throw new InvalidOperationException("Bot navigation node " + id + " does not match the authored catalog.");
        }

        private static void ValidateNodeClearance(BotNavigationGraph graph)
        {
            var hits = new Collider[ClearanceBufferSize];
            for (var i = 0; i < graph.NodeCount; i++)
            {
                var node = graph.GetNode(i);
                var center = node.Position + BotNavigationGraph.ExpectedControllerCenter;
                var half = graph.ControllerHeight * 0.5f - graph.ControllerRadius;
                var count = UnityEngine.Physics.OverlapCapsuleNonAlloc(center - Vector3.up * half, center + Vector3.up * half, ClearanceQueryRadius, hits, 1, QueryTriggerInteraction.Ignore);
                if (count >= hits.Length) throw new InvalidOperationException("Bot node " + node.Id + " capsule clearance query overflowed its fixed buffer.");
                for (var hitIndex = 0; hitIndex < count; hitIndex++)
                {
                    if (hits[hitIndex] != null && hits[hitIndex].attachedRigidbody == null)
                        throw new InvalidOperationException("Bot node " + node.Id + " capsule overlaps authored Default geometry.");
                }
            }
        }

        private static void ValidateProductionRecesses(BotNavigationGraph graph, Collider redShield, Collider blueShield)
        {
            if (redShield == null || blueShield == null || !redShield.gameObject.activeInHierarchy || !redShield.enabled ||
                !blueShield.gameObject.activeInHierarchy || !blueShield.enabled)
                throw new InvalidOperationException("Production shield gates must both be active and enabled.");

            var reachable = new HashSet<int>();
            var pending = new Queue<int>();
            for (var i = 0; i < graph.NodeCount; i++)
            {
                var node = graph.GetNode(i);
                if (node.Area != BotNavigationArea.GoalRecess)
                {
                    reachable.Add(node.Id);
                    pending.Enqueue(node.Id);
                }
            }
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                for (var i = 0; i < graph.EdgeCount; i++)
                {
                    var edge = graph.GetEdge(i);
                    if (edge.FromNodeId != current) continue;
                    var gateOpen = edge.Traversal != BotNavigationTraversal.ShieldGate ||
                        (edge.GateCollider != null && !edge.GateCollider.enabled);
                    if (!gateOpen) continue;
                    var target = graph.GetNode(FindNodeIndex(graph, edge.ToNodeId));
                    if (target.Area == BotNavigationArea.GoalRecess) throw new InvalidOperationException("Production shield gates must keep goal recesses unreachable.");
                    if (reachable.Add(target.Id)) pending.Enqueue(target.Id);
                }
            }
        }

        private static void ValidatePrefabComponentSource(Component instance, Component expected, string label)
        {
            if (instance == null || expected == null)
                throw new InvalidOperationException(label + " is missing from the participant or Player prefab.");
            ValidatePersistentIdentity(instance, label);
            var source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            if (source == null || source != expected || !string.Equals(AssetDatabase.GetAssetPath(source), MovementLabContract.PlayerPrefabPath, StringComparison.Ordinal))
                throw new InvalidOperationException(label + " must correspond to the Player prefab component, but its prefab source link is absent or points elsewhere; the builder added this component to the scene instance instead of instantiating it from the Player prefab.");
            ValidatePersistentIdentity(source, label + " prefab source");
        }

        private static int FindNodeIndex(BotNavigationGraph graph, int id)
        {
            for (var i = 0; i < graph.NodeCount; i++) if (graph.GetNode(i).Id == id) return i;
            throw new InvalidOperationException("Graph edge references missing node " + id + ".");
        }

        private static void ValidateNavigatorTuning(BotNavigator navigator)
        {
            if (navigator.ArrivalDistance != 0.75f || navigator.VerticalArrivalDistance != 0.65f || navigator.LookaheadDistance != 3f || navigator.TargetReplanDistance != 1.5f || navigator.CorridorMargin != BotNavigationGraph.ExpectedControllerClearance || navigator.LedgeProbeDistance != 1.5f || navigator.LedgeProbeDepth != 2.5f)
                throw new InvalidOperationException("BotNavigator tuning mismatch.");
        }

        private static void ValidateCoordinator(BotTeamRoleCoordinator coordinator, MatchController match, ParticipantTeam team, ParticipantState[] participants, BotPerception[] perceptions)
        {
            ValidateSerializedEnum(coordinator, "team", (int)team, "BotTeamRoleCoordinator.team");
            ValidateReference(coordinator, "match", match, "BotTeamRoleCoordinator.match");
            var graph = UnityEngine.Object.FindObjectsByType<BotNavigationGraph>(FindObjectsInactive.Include, FindObjectsSortMode.None).SingleOrDefault();
            ValidateReference(coordinator, "navigationGraph", graph, "BotTeamRoleCoordinator.navigationGraph");
            ValidateReferenceArray(coordinator, "participants", participants.Cast<UnityEngine.Object>().ToArray(), "BotTeamRoleCoordinator.participants");
            ValidateReferenceArray(coordinator, "perceptions", perceptions.Cast<UnityEngine.Object>().ToArray(), "BotTeamRoleCoordinator.perceptions");
            ValidateSerializedFloat(coordinator, "evaluationInterval", BotTeamRoleCoordinator.ExpectedEvaluationInterval, "BotTeamRoleCoordinator.evaluationInterval");
            ValidateSerializedFloat(coordinator, "roleHoldSeconds", BotTeamRoleCoordinator.ExpectedRoleHoldSeconds, "BotTeamRoleCoordinator.roleHoldSeconds");
            ValidateSerializedFloat(coordinator, "switchMargin", BotTeamRoleCoordinator.ExpectedSwitchMargin, "BotTeamRoleCoordinator.switchMargin");
            ValidateSerializedFloat(coordinator, "humanShotgunYieldDistance", BotTargetRules.HumanShotgunYieldDistance, "BotTeamRoleCoordinator.humanShotgunYieldDistance");
            ValidateSerializedFloat(coordinator, "healthYieldDistance", BotTargetRules.HealthYieldDistance, "BotTeamRoleCoordinator.healthYieldDistance");
            ValidateSerializedFloat(coordinator, "criticalHealthRatio", BotTargetRules.CriticalHealthRatio, "BotTeamRoleCoordinator.criticalHealthRatio");
        }

        private static void ValidateExecutionOrders()
        {
            ValidateExecutionOrder(typeof(BotPerception), -300);
            ValidateExecutionOrder(typeof(BotTeamRoleCoordinator), -250);
            ValidateExecutionOrder(typeof(BotController), -200);
            ValidateExecutionOrder(typeof(BallKick), -100);
        }

        private static void ValidateExecutionOrder(Type type, int expected)
        {
            var attribute = (DefaultExecutionOrder)Attribute.GetCustomAttribute(type, typeof(DefaultExecutionOrder));
            if (attribute == null || attribute.order != expected) throw new InvalidOperationException(type.Name + " execution order must be " + expected + ".");
        }

        private static void ValidatePauseAdapters()
        {
            var types = new[] { typeof(RocketFooxball.Runtime.Movement.PlayerMotor), typeof(RocketFooxball.Runtime.Movement.PlayerLook), typeof(BallKick), typeof(BallMotor), typeof(RocketFooxball.Runtime.Weapons.RocketLauncher), typeof(RocketFooxball.Runtime.Weapons.ShotgunWeapon), typeof(RocketFooxball.Runtime.Weapons.RocketProjectile) };
            for (var i = 0; i < types.Length; i++)
            {
                var method = types[i].GetMethod("SetPaused", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (method == null || method.ReturnType != typeof(void) || method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(bool))
                    throw new InvalidOperationException(types[i].Name + ".SetPaused(bool) pause adapter is missing.");
            }
        }

        private static void ValidateSerializedLayerMask(UnityEngine.Object target, string propertyName, int expected, string label)
        {
            var property = new SerializedObject(target).FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.LayerMask || property.intValue != expected)
                throw new InvalidOperationException(label + " mismatch.");
        }

        private static void ValidateSerializedEnum(UnityEngine.Object target, string propertyName, int expected, string label)
        {
            var property = new SerializedObject(target).FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Enum || property.enumValueIndex != expected)
                throw new InvalidOperationException(label + " mismatch.");
        }

        private static void ValidateObjectArray(UnityEngine.Object target, string propertyName, UnityEngine.Object[] expected, string label)
        {
            ValidateReferenceArray(target, propertyName, expected, label);
        }

        private static void ValidateReferenceArray(UnityEngine.Object target, string propertyName, UnityEngine.Object[] expected, string label)
        {
            var property = new SerializedObject(target).FindProperty(propertyName);
            if (property == null || !property.isArray || property.arraySize != expected.Length)
                throw new InvalidOperationException(label + " length mismatch.");
            for (var i = 0; i < expected.Length; i++)
            {
                var actual = property.GetArrayElementAtIndex(i).objectReferenceValue;
                if (actual != expected[i]) throw new InvalidOperationException(label + " order mismatch at " + i + ".");
                ValidatePersistentIdentity(actual, label + "[" + i + "]");
            }
        }
    }
}
