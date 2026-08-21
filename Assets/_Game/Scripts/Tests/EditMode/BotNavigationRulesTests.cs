using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using RocketFooxball.Runtime.Bots;

namespace RocketFooxball.Tests.EditMode
{
    public sealed class BotNavigationRulesTests
    {
        [Test]
        public void FindPathUsesDeterministicNodeTieBreaksWhenEdgesAreScrambled()
        {
            var nodes = new[]
            {
                Node(0, 0f),
                Node(3, 1f),
                Node(1, 1f),
                Node(9, 2f)
            };
            var edges = new[]
            {
                Edge(21, 1, 9, 1f),
                Edge(10, 0, 1, 1f),
                Edge(20, 0, 3, 1f),
                Edge(11, 3, 9, 1f)
            };

            var enabled = new bool[22];
            for (var i = 0; i < enabled.Length; i++) enabled[i] = true;
            var status = Find(nodes, edges, 0, 9, enabled, 4, out var path, out var pathCount, out _);

            Assert.That(status, Is.EqualTo(BotPathStatus.Success));
            Assert.That(pathCount, Is.EqualTo(3));
            Assert.That(path, Is.EqualTo(new[] { 0, 1, 9, 0 }));
        }

        [Test]
        public void FindPathHonoursDirectedEdgesAndReportsUnreachable()
        {
            var nodes = new[] { Node(0, 0f), Node(1, 1f), Node(2, 2f) };
            var edges = new[] { Edge(0, 1, 0, 1f), Edge(1, 1, 2, 1f) };

            var status = Find(nodes, edges, 0, 2, new[] { true, true }, 3, out _, out _, out _);

            Assert.That(status, Is.EqualTo(BotPathStatus.Unreachable));
        }

        [Test]
        public void FindPathReturnsBufferTooSmallWithoutPartialRoute()
        {
            var nodes = new[] { Node(0, 0f), Node(1, 1f), Node(2, 2f) };
            var edges = new[] { Edge(0, 0, 1, 1f), Edge(1, 1, 2, 1f) };

            var status = Find(nodes, edges, 0, 2, new[] { true, true }, 2, out _, out var pathCount, out var cost);

            Assert.That(status, Is.EqualTo(BotPathStatus.BufferTooSmall));
            Assert.That(pathCount, Is.EqualTo(0));
            Assert.That(cost, Is.EqualTo(0f));
        }

        [Test]
        public void FindPathRejectsReverseDropAndAcceptsDescendingDrop()
        {
            var ramp = new BotNavigationNodeRecord(0, new Vector3(0f, 4f, 0f), BotNavigationArea.RampDeck, 1f);
            var floor = new BotNavigationNodeRecord(1, new Vector3(0f, 0f, 4f), BotNavigationArea.Floor, 1f);
            var nodes = new[] { ramp, floor };
            var reverse = new[] { new BotNavigationEdgeRecord(0, 1, 0, BotNavigationTraversal.Drop, 6f, 2f, null) };
            var forward = new[] { new BotNavigationEdgeRecord(0, 0, 1, BotNavigationTraversal.Drop, 6f, 2f, null) };

            Assert.That(Find(nodes, reverse, 1, 0, new[] { true }, 2, out _, out _, out _), Is.EqualTo(BotPathStatus.Invalid));
            Assert.That(Find(nodes, forward, 0, 1, new[] { true }, 2, out var path, out var pathCount, out _), Is.EqualTo(BotPathStatus.Success));
            Assert.That(pathCount, Is.EqualTo(2));
            Assert.That(path, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void FindPathRejectsNonFiniteNodePosition()
        {
            var nodes = new[]
            {
                new BotNavigationNodeRecord(0, new Vector3(float.NaN, 0f, 0f), BotNavigationArea.Floor, 1f),
                Node(1, 1f)
            };
            var edges = new[] { Edge(0, 0, 1, 1f) };

            Assert.That(Find(nodes, edges, 0, 1, new[] { true }, 2, out _, out _, out _), Is.EqualTo(BotPathStatus.Invalid));
        }

        [Test]
        public void FindPathAllowsSameAreaWalkWithinControllerStepLimit()
        {
            var nodes = new[]
            {
                new BotNavigationNodeRecord(0, new Vector3(0f, 0f, 0f), BotNavigationArea.Floor, 1f),
                new BotNavigationNodeRecord(1, new Vector3(1f, 0.38f, 0f), BotNavigationArea.Floor, 1f)
            };
            var edges = new[] { Edge(0, 0, 1, 2f) };

            Assert.That(Find(nodes, edges, 0, 1, new[] { true }, 2, out var path, out var pathCount, out _),
                Is.EqualTo(BotPathStatus.Success));
            Assert.That(pathCount, Is.EqualTo(2));
            Assert.That(path, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void FindPathRejectsSameAreaWalkAboveControllerStepLimit()
        {
            var nodes = new[]
            {
                new BotNavigationNodeRecord(0, new Vector3(0f, 0f, 0f), BotNavigationArea.Floor, 1f),
                new BotNavigationNodeRecord(1, new Vector3(1f, 0.39f, 0f), BotNavigationArea.Floor, 1f)
            };
            var edges = new[] { Edge(0, 0, 1, 2f) };

            Assert.That(Find(nodes, edges, 0, 1, new[] { true }, 2, out _, out _, out _), Is.EqualTo(BotPathStatus.Invalid));
        }

        [Test]
        public void GraphValidationEnforcesSameAreaWalkStepLimit()
        {
            var graphObject = new GameObject("BotNavigationRulesTestGraph");
            try
            {
                var graph = graphObject.AddComponent<BotNavigationGraph>();
                SetPrivateField(graph, "nodes", new[]
                {
                    new BotNavigationNodeRecord(0, new Vector3(0f, 0f, 0f), BotNavigationArea.Floor, 1f),
                    new BotNavigationNodeRecord(1, new Vector3(1f, 0.39f, 0f), BotNavigationArea.Floor, 1f)
                });
                SetPrivateField(graph, "edges", new[] { Edge(0, 0, 1, 2f) });

                Assert.That(graph.TryValidate(out _), Is.False);

                SetPrivateField(graph, "nodes", new[]
                {
                    new BotNavigationNodeRecord(0, new Vector3(0f, 0f, 0f), BotNavigationArea.Floor, 1f),
                    new BotNavigationNodeRecord(1, new Vector3(1f, 0.38f, 0f), BotNavigationArea.Floor, 1f)
                });

                Assert.That(graph.TryValidate(out _), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(graphObject);
            }
        }

        [Test]
        public void GraphValidationAndFindPathShareEffectiveRadiusBoundary()
        {
            var graphObject = new GameObject("BotNavigationEffectiveRadiusBoundaryGraph");
            try
            {
                var nodes = new[]
                {
                    new BotNavigationNodeRecord(0, Vector3.zero, BotNavigationArea.Floor, 0.72f),
                    new BotNavigationNodeRecord(1, new Vector3(0.5f, 0f, 0f), BotNavigationArea.Floor, 0.72f)
                };
                var edges = new[]
                {
                    new BotNavigationEdgeRecord(0, 0, 1, BotNavigationTraversal.Walk, 0.5f, 0.72f, null)
                };
                var graph = graphObject.AddComponent<BotNavigationGraph>();
                SetPrivateField(graph, "nodes", nodes);
                SetPrivateField(graph, "edges", edges);

                Assert.That(graph.TryValidate(out var graphReason), Is.True, graphReason);
                Assert.That(
                    Find(nodes, edges, 0, 1, new[] { true }, 2, out var path, out var pathCount, out _),
                    Is.EqualTo(BotPathStatus.Success));
                Assert.That(pathCount, Is.EqualTo(2));
                Assert.That(path, Is.EqualTo(new[] { 0, 1 }));
            }
            finally
            {
                Object.DestroyImmediate(graphObject);
            }
        }

        [Test]
        public void DoubledControllerAndArenaContractsExposeExactGeometry()
        {
            Assert.That(BotNavigationGraph.ExpectedControllerRadius, Is.EqualTo(0.8f));
            Assert.That(BotNavigationGraph.ExpectedControllerHeight, Is.EqualTo(3.6f));
            Assert.That(BotNavigationGraph.ExpectedControllerCenter, Is.EqualTo(new Vector3(0f, 1.8f, 0f)));
            Assert.That(BotNavigationGraph.ExpectedControllerSkinWidth, Is.EqualTo(0.08f));
            Assert.That(BotNavigationGraph.ExpectedControllerEffectiveRadius, Is.EqualTo(0.72f).Within(0.0001f));
            Assert.That(BotNavigationGraph.ExpectedRocketJumpGroundProbeDistance, Is.EqualTo(8f));
            Assert.That(BotNavigationGraph.ExpectedGoalRecessSafeRadius, Is.EqualTo(1f));

            var botObject = new GameObject("BotNavigationRocketJumpProbeContractTest");
            try
            {
                botObject.SetActive(false);
                var bot = botObject.AddComponent<BotController>();
                var jumpProbeField = typeof(BotController).GetField(
                    "jumpProbeDistance",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(jumpProbeField, Is.Not.Null);
                Assert.That(
                    (float)jumpProbeField.GetValue(bot),
                    Is.EqualTo(BotNavigationGraph.ExpectedRocketJumpGroundProbeDistance).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(botObject);
            }

            var graphObject = new GameObject("BotNavigationContractTestGraph");
            try
            {
                var graph = graphObject.AddComponent<BotNavigationGraph>();
                Assert.That(graph.ArenaBounds, Is.Not.Null);
                Assert.That(graph.ArenaBounds.Center, Is.EqualTo(Vector3.zero));
                Assert.That(graph.ArenaBounds.HalfLength, Is.EqualTo(65f));
                Assert.That(graph.ArenaBounds.HalfWidth, Is.EqualTo(45f));
                SetPrivateField(graph, "nodes", new[] { Node(0, 0f) });
                SetPrivateField(graph, "edges", new BotNavigationEdgeRecord[0]);

                Assert.That(graph.TryValidate(out var reason), Is.True, reason);
                Assert.That(graph.EffectiveControllerRadius, Is.EqualTo(0.72f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(graphObject);
            }
        }

        [Test]
        public void ArenaBoundsRejectNonFiniteOrNonPositiveExtents()
        {
            var invalid = new[]
            {
                new BotArenaBounds(new Vector3(float.NaN, 0f, 0f), 65f, 45f),
                new BotArenaBounds(Vector3.zero, float.PositiveInfinity, 45f),
                new BotArenaBounds(Vector3.zero, 0f, 45f),
                new BotArenaBounds(Vector3.zero, 65f, -1f)
            };

            for (var i = 0; i < invalid.Length; i++)
            {
                Assert.That(invalid[i].TryValidate(out _), Is.False, "bounds " + i + " should be rejected");
            }
        }

        [Test]
        public void GraphValidationRejectsNonContractArenaBoundsAndSmallGoalRecess()
        {
            var graphObject = new GameObject("BotNavigationInvalidGeometryTestGraph");
            try
            {
                var graph = graphObject.AddComponent<BotNavigationGraph>();
                SetPrivateField(graph, "nodes", new[]
                {
                    new BotNavigationNodeRecord(0, Vector3.zero, BotNavigationArea.GoalRecess, 0.99f)
                });
                SetPrivateField(graph, "edges", new BotNavigationEdgeRecord[0]);
                Assert.That(graph.TryValidate(out _), Is.False);

                SetPrivateField(graph, "nodes", new[]
                {
                    new BotNavigationNodeRecord(0, Vector3.zero, BotNavigationArea.Floor, 1f)
                });
                SetPrivateField(graph, "arenaBounds", new BotArenaBounds(Vector3.zero, 64f, 45f));
                Assert.That(graph.TryValidate(out _), Is.False);

                SetPrivateField(graph, "arenaBounds", new BotArenaBounds(Vector3.zero, 65f, 45f));
                Assert.That(graph.TryValidate(out _), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(graphObject);
            }
        }

        [Test]
        public void NavigatorCorridorUsesEffectiveControllerRadius()
        {
            var graphObject = new GameObject("BotNavigationEffectiveRadiusGraph");
            var navigatorObject = new GameObject("BotNavigationEffectiveRadiusNavigator");
            try
            {
                var graph = graphObject.AddComponent<BotNavigationGraph>();
                navigatorObject.SetActive(false);
                var navigator = navigatorObject.AddComponent<BotNavigator>();
                SetPrivateField(navigator, "graph", graph);
                var method = typeof(BotNavigator).GetMethod("SafeCorridorHalfWidth", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                var edge = new BotNavigationEdgeRecord(0, 0, 0, BotNavigationTraversal.Walk, 1f, 3f, null);
                Assert.That((float)method.Invoke(navigator, new object[] { edge }), Is.EqualTo(2.28f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(navigatorObject);
                Object.DestroyImmediate(graphObject);
            }
        }

        [Test]
        public void RampTerminalProjectionReportsMotionTowardOtherEndpoint()
        {
            var graphObject = new GameObject("BotNavigationRulesTestGraph");
            var navigatorObject = new GameObject("BotNavigationRulesTestNavigator");
            try
            {
                var low = new BotNavigationNodeRecord(
                    0,
                    new Vector3(0f, 0f, 0f),
                    BotNavigationArea.RampDeck,
                    1f);
                var high = new BotNavigationNodeRecord(
                    1,
                    new Vector3(8f, 4f, 0f),
                    BotNavigationArea.RampDeck,
                    1f);
                var graph = graphObject.AddComponent<BotNavigationGraph>();
                SetPrivateField(graph, "nodes", new[] { low, high });
                SetPrivateField(graph, "edges", new[]
                {
                    new BotNavigationEdgeRecord(0, low.Id, high.Id, BotNavigationTraversal.Ramp, 9f, 2f, null)
                });

                navigatorObject.SetActive(false);
                var navigator = navigatorObject.AddComponent<BotNavigator>();
                SetPrivateField(navigator, "graph", graph);
                navigatorObject.SetActive(true);

                var worldTarget = new Vector3(4f, 2f, 0f);
                Assert.That(
                    InvokeRampTerminalProjection(navigator, worldTarget, high, out var highSteeringPoint),
                    Is.EqualTo(BotVerticalRoute.Descend));
                Assert.That(highSteeringPoint, Is.EqualTo(worldTarget));

                Assert.That(
                    InvokeRampTerminalProjection(navigator, worldTarget, low, out var lowSteeringPoint),
                    Is.EqualTo(BotVerticalRoute.Ascend));
                Assert.That(lowSteeringPoint, Is.EqualTo(worldTarget));
            }
            finally
            {
                Object.DestroyImmediate(navigatorObject);
                Object.DestroyImmediate(graphObject);
            }
        }

        private static BotVerticalRoute InvokeRampTerminalProjection(
            BotNavigator navigator,
            Vector3 worldTarget,
            BotNavigationNodeRecord terminalNode,
            out Vector3 steeringPoint)
        {
            var method = typeof(BotNavigator).GetMethod(
                "TryProjectRampTerminal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var arguments = new object[] { worldTarget, terminalNode, Vector3.zero, BotVerticalRoute.None };
            Assert.That(method.Invoke(navigator, arguments), Is.EqualTo(true));
            steeringPoint = (Vector3)arguments[2];
            return (BotVerticalRoute)arguments[3];
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static BotNavigationNodeRecord Node(int id, float x)
        {
            return new BotNavigationNodeRecord(id, new Vector3(x, 0f, 0f), BotNavigationArea.Floor, 1f);
        }

        private static BotNavigationEdgeRecord Edge(int id, int from, int to, float cost)
        {
            return new BotNavigationEdgeRecord(id, from, to, BotNavigationTraversal.Walk, cost, 2f, null);
        }

        private static BotPathStatus Find(
            BotNavigationNodeRecord[] nodes,
            BotNavigationEdgeRecord[] edges,
            int start,
            int goal,
            bool[] enabled,
            int pathBufferLength,
            out int[] path,
            out int pathCount,
            out float cost)
        {
            path = new int[pathBufferLength];
            return BotNavigationRules.FindPath(
                nodes,
                edges,
                start,
                goal,
                enabled,
                path,
                new float[nodes.Length],
                new float[nodes.Length],
                new int[nodes.Length],
                new int[nodes.Length],
                new bool[nodes.Length],
                new bool[nodes.Length],
                out pathCount,
                out cost);
        }
    }
}
