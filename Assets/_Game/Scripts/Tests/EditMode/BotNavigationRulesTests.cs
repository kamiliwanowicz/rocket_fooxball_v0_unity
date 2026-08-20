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
                new BotNavigationNodeRecord(1, new Vector3(1f, 0.34f, 0f), BotNavigationArea.Floor, 1f)
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
                new BotNavigationNodeRecord(1, new Vector3(1f, 0.35f, 0f), BotNavigationArea.Floor, 1f)
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
                    new BotNavigationNodeRecord(1, new Vector3(1f, 0.35f, 0f), BotNavigationArea.Floor, 1f)
                });
                SetPrivateField(graph, "edges", new[] { Edge(0, 0, 1, 2f) });

                Assert.That(graph.TryValidate(out _), Is.False);

                SetPrivateField(graph, "nodes", new[]
                {
                    new BotNavigationNodeRecord(0, new Vector3(0f, 0f, 0f), BotNavigationArea.Floor, 1f),
                    new BotNavigationNodeRecord(1, new Vector3(1f, 0.34f, 0f), BotNavigationArea.Floor, 1f)
                });

                Assert.That(graph.TryValidate(out _), Is.True);
            }
            finally
            {
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
