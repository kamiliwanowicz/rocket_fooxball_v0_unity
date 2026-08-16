using System;
using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    public enum BotPathStatus
    {
        Invalid = 0,
        Unreachable = 1,
        Success = 2,
        BufferTooSmall = 3
    }

    public enum BotNavigationStatus
    {
        Invalid = 0,
        Unreachable = 1,
        Following = 2,
        Arrived = 3
    }

    public enum BotVerticalRoute
    {
        None = 0,
        Ascend = 1,
        Descend = 2
    }

    public readonly struct BotNavigationSteeringResult
    {
        public BotNavigationSteeringResult(
            BotNavigationStatus status,
            Vector3 steeringPoint,
            Vector3 worldDirection,
            BotVerticalRoute verticalRoute)
        {
            Status = status;
            SteeringPoint = steeringPoint;
            WorldDirection = worldDirection;
            VerticalRoute = verticalRoute;
        }

        public BotNavigationStatus Status { get; }
        public Vector3 SteeringPoint { get; }
        public Vector3 WorldDirection { get; }
        public BotVerticalRoute VerticalRoute { get; }
        public bool HasSteering => Status == BotNavigationStatus.Following && IsFinite(WorldDirection) && WorldDirection.sqrMagnitude > 0.000001f;
        public bool Arrived => Status == BotNavigationStatus.Arrived;

        // Short aliases keep the result convenient at call sites while the named properties retain the contract terms.
        public Vector3 Point => SteeringPoint;
        public Vector3 Direction => WorldDirection;

        public static BotNavigationSteeringResult Invalid => new BotNavigationSteeringResult(
            BotNavigationStatus.Invalid,
            Vector3.zero,
            Vector3.zero,
            BotVerticalRoute.None);

        public static BotNavigationSteeringResult Unreachable => new BotNavigationSteeringResult(
            BotNavigationStatus.Unreachable,
            Vector3.zero,
            Vector3.zero,
            BotVerticalRoute.None);

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class BotNavigationRules
    {
        private const float TieEpsilon = 0.00001f;
        private const float ClearanceEpsilon = 0.0001f;

        public static BotPathStatus FindPath(
            BotNavigationNodeRecord[] nodes,
            BotNavigationEdgeRecord[] edges,
            int startNodeId,
            int goalNodeId,
            bool[] edgeEnabled,
            int[] nodePathBuffer,
            float[] gScoreBuffer,
            float[] fScoreBuffer,
            int[] parentNodeBuffer,
            int[] parentEdgeBuffer,
            bool[] openBuffer,
            bool[] closedBuffer,
            out int nodeCount,
            out float routeCost)
        {
            nodeCount = 0;
            routeCost = 0f;

            if (!TryValidateInputs(
                    nodes,
                    edges,
                    startNodeId,
                    goalNodeId,
                    edgeEnabled,
                    nodePathBuffer,
                    gScoreBuffer,
                    fScoreBuffer,
                    parentNodeBuffer,
                    parentEdgeBuffer,
                    openBuffer,
                    closedBuffer,
                    out var startIndex,
                    out var goalIndex,
                    out var maxEdgeId))
            {
                return BotPathStatus.Invalid;
            }

            for (var i = 0; i < nodes.Length; i++)
            {
                openBuffer[i] = false;
                closedBuffer[i] = false;
                gScoreBuffer[i] = float.PositiveInfinity;
                fScoreBuffer[i] = float.PositiveInfinity;
                parentNodeBuffer[i] = -1;
                parentEdgeBuffer[i] = -1;
            }

            if (edgeEnabled.Length <= maxEdgeId)
            {
                return BotPathStatus.Invalid;
            }

            gScoreBuffer[startIndex] = 0f;
            fScoreBuffer[startIndex] = Heuristic(nodes[startIndex], nodes[goalIndex]);
            parentNodeBuffer[startIndex] = -1;
            parentEdgeBuffer[startIndex] = -1;
            openBuffer[startIndex] = true;

            while (true)
            {
                var currentIndex = FindBestOpenIndex(nodes, openBuffer, fScoreBuffer, parentNodeBuffer, parentEdgeBuffer);
                if (currentIndex < 0)
                {
                    return BotPathStatus.Unreachable;
                }

                openBuffer[currentIndex] = false;
                closedBuffer[currentIndex] = true;
                if (currentIndex == goalIndex)
                {
                    return WritePath(
                        nodes,
                        startIndex,
                        goalIndex,
                        nodePathBuffer,
                        parentNodeBuffer,
                        gScoreBuffer[goalIndex],
                        out nodeCount,
                        out routeCost);
                }

                var currentNode = nodes[currentIndex];
                for (var edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                {
                    var edge = edges[edgeIndex];
                    if (edge == null || edge.Id < 0 || edge.Id > maxEdgeId || !edgeEnabled[edge.Id] ||
                        edge.FromNodeId != currentNode.Id)
                    {
                        continue;
                    }

                    var nextIndex = FindNodeIndex(nodes, edge.ToNodeId);
                    if (nextIndex < 0 || closedBuffer[nextIndex])
                    {
                        continue;
                    }

                    var candidateG = gScoreBuffer[currentIndex] + edge.Cost;
                    var candidateF = candidateG + Heuristic(nodes[nextIndex], nodes[goalIndex]);
                    if (!IsFinite(candidateG) || !IsFinite(candidateF))
                    {
                        return BotPathStatus.Invalid;
                    }

                    if (!ShouldReplace(
                            candidateG,
                            candidateF,
                            currentNode.Id,
                            edge.Id,
                            gScoreBuffer[nextIndex],
                            fScoreBuffer[nextIndex],
                            parentNodeBuffer[nextIndex],
                            parentEdgeBuffer[nextIndex]))
                    {
                        continue;
                    }

                    gScoreBuffer[nextIndex] = candidateG;
                    fScoreBuffer[nextIndex] = candidateF;
                    parentNodeBuffer[nextIndex] = currentNode.Id;
                    parentEdgeBuffer[nextIndex] = edge.Id;
                    openBuffer[nextIndex] = true;
                }
            }
        }

        internal static bool TryValidateRecords(
            BotNavigationNodeRecord[] nodes,
            BotNavigationEdgeRecord[] edges,
            out int maxEdgeId)
        {
            maxEdgeId = -1;
            if (nodes == null || nodes.Length == 0 || edges == null)
            {
                return false;
            }

            for (var i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null || node.Id < 0 || !IsFinite(node.Position) || !IsFinite(node.SafeRadius) ||
                    node.SafeRadius < BotNavigationGraph.ExpectedControllerRadius + BotNavigationGraph.ExpectedControllerSkinWidth)
                {
                    return false;
                }

                if (node.Area != BotNavigationArea.Floor && node.Area != BotNavigationArea.RampDeck &&
                    node.Area != BotNavigationArea.GoalRecess)
                {
                    return false;
                }

                for (var j = 0; j < i; j++)
                {
                    if (nodes[j] != null && nodes[j].Id == node.Id)
                    {
                        return false;
                    }
                }
            }

            for (var i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                if (edge == null || edge.Id < 0 || edge.FromNodeId < 0 || edge.ToNodeId < 0 ||
                    !IsFinite(edge.Cost) || edge.Cost <= 0f || !IsFinite(edge.CorridorHalfWidth) ||
                    edge.CorridorHalfWidth < BotNavigationGraph.ExpectedControllerRadius + BotNavigationGraph.ExpectedControllerSkinWidth)
                {
                    return false;
                }

                if (edge.Id > maxEdgeId)
                {
                    maxEdgeId = edge.Id;
                }

                for (var j = 0; j < i; j++)
                {
                    if (edges[j] != null && edges[j].Id == edge.Id)
                    {
                        return false;
                    }
                }

                var fromIndex = FindNodeIndex(nodes, edge.FromNodeId);
                var toIndex = FindNodeIndex(nodes, edge.ToNodeId);
                if (fromIndex < 0 || toIndex < 0)
                {
                    return false;
                }

                var from = nodes[fromIndex];
                var to = nodes[toIndex];
                var distance = Vector3.Distance(from.Position, to.Position);
                if (!IsFinite(distance) || edge.Cost + ClearanceEpsilon < distance)
                {
                    return false;
                }

                if (!IsTraversalValid(edge, from, to))
                {
                    return false;
                }
            }

            return true;
        }

        internal static int FindNodeIndex(BotNavigationNodeRecord[] nodes, int id)
        {
            if (nodes == null)
            {
                return -1;
            }

            for (var i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] != null && nodes[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        internal static bool IsTraversalValid(
            BotNavigationEdgeRecord edge,
            BotNavigationNodeRecord from,
            BotNavigationNodeRecord to)
        {
            var verticalDelta = to.Position.y - from.Position.y;
            var horizontal = new Vector2(to.Position.x - from.Position.x, to.Position.z - from.Position.z).magnitude;

            switch (edge.Traversal)
            {
                case BotNavigationTraversal.Walk:
                    if (from.Area == to.Area)
                    {
                        return true;
                    }

                    var floorRampPair =
                        (from.Area == BotNavigationArea.Floor && to.Area == BotNavigationArea.RampDeck) ||
                        (from.Area == BotNavigationArea.RampDeck && to.Area == BotNavigationArea.Floor);
                    return floorRampPair && Mathf.Abs(verticalDelta) <= BotNavigationGraph.ExpectedControllerStepOffset +
                        BotNavigationGraph.ExpectedControllerSkinWidth + ClearanceEpsilon;

                case BotNavigationTraversal.Ramp:
                    if (from.Area != BotNavigationArea.RampDeck || to.Area != BotNavigationArea.RampDeck || horizontal <= 0.0001f)
                    {
                        return false;
                    }

                    var slope = Mathf.Atan2(Mathf.Abs(verticalDelta), horizontal) * Mathf.Rad2Deg;
                    return IsFinite(slope) && slope <= BotNavigationGraph.ExpectedControllerSlopeLimit + ClearanceEpsilon;

                case BotNavigationTraversal.Drop:
                    return from.Area == BotNavigationArea.RampDeck && to.Area == BotNavigationArea.Floor && verticalDelta < -ClearanceEpsilon;

                case BotNavigationTraversal.ShieldGate:
                    return edge.GateCollider != null &&
                        ((from.Area == BotNavigationArea.Floor && to.Area == BotNavigationArea.GoalRecess) ||
                         (from.Area == BotNavigationArea.GoalRecess && to.Area == BotNavigationArea.Floor));

                default:
                    return false;
            }
        }

        private static bool TryValidateInputs(
            BotNavigationNodeRecord[] nodes,
            BotNavigationEdgeRecord[] edges,
            int startNodeId,
            int goalNodeId,
            bool[] edgeEnabled,
            int[] nodePathBuffer,
            float[] gScoreBuffer,
            float[] fScoreBuffer,
            int[] parentNodeBuffer,
            int[] parentEdgeBuffer,
            bool[] openBuffer,
            bool[] closedBuffer,
            out int startIndex,
            out int goalIndex,
            out int maxEdgeId)
        {
            startIndex = -1;
            goalIndex = -1;
            maxEdgeId = -1;

            if (edgeEnabled == null || nodePathBuffer == null || gScoreBuffer == null || fScoreBuffer == null ||
                parentNodeBuffer == null || parentEdgeBuffer == null || openBuffer == null || closedBuffer == null)
            {
                return false;
            }

            if (nodes == null || nodes.Length == 0 || edges == null ||
                gScoreBuffer.Length < nodes.Length || fScoreBuffer.Length < nodes.Length ||
                parentNodeBuffer.Length < nodes.Length || parentEdgeBuffer.Length < nodes.Length ||
                openBuffer.Length < nodes.Length || closedBuffer.Length < nodes.Length)
            {
                return false;
            }

            if (!TryValidateRecords(nodes, edges, out maxEdgeId) || edgeEnabled.Length <= maxEdgeId)
            {
                return false;
            }

            startIndex = FindNodeIndex(nodes, startNodeId);
            goalIndex = FindNodeIndex(nodes, goalNodeId);
            return startIndex >= 0 && goalIndex >= 0;
        }

        private static int FindBestOpenIndex(
            BotNavigationNodeRecord[] nodes,
            bool[] openBuffer,
            float[] fScoreBuffer,
            int[] parentNodeBuffer,
            int[] parentEdgeBuffer)
        {
            var bestIndex = -1;
            for (var i = 0; i < nodes.Length; i++)
            {
                if (!openBuffer[i])
                {
                    continue;
                }

                if (bestIndex < 0 || IsOpenCandidateBetter(
                        nodes[i],
                        fScoreBuffer[i],
                        parentNodeBuffer[i],
                        parentEdgeBuffer[i],
                        nodes[bestIndex],
                        fScoreBuffer[bestIndex],
                        parentNodeBuffer[bestIndex],
                        parentEdgeBuffer[bestIndex]))
                {
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static bool IsOpenCandidateBetter(
            BotNavigationNodeRecord candidate,
            float candidateF,
            int candidateParent,
            int candidateEdge,
            BotNavigationNodeRecord incumbent,
            float incumbentF,
            int incumbentParent,
            int incumbentEdge)
        {
            if (candidateF < incumbentF - TieEpsilon)
            {
                return true;
            }

            if (Mathf.Abs(candidateF - incumbentF) > TieEpsilon)
            {
                return false;
            }

            if (candidate.Id != incumbent.Id)
            {
                return candidate.Id < incumbent.Id;
            }

            if (candidateParent != incumbentParent)
            {
                return candidateParent < incumbentParent;
            }

            return candidateEdge < incumbentEdge;
        }

        private static bool ShouldReplace(
            float candidateG,
            float candidateF,
            int candidateParent,
            int candidateEdge,
            float incumbentG,
            float incumbentF,
            int incumbentParent,
            int incumbentEdge)
        {
            if (candidateG < incumbentG - TieEpsilon)
            {
                return true;
            }

            if (Mathf.Abs(candidateG - incumbentG) > TieEpsilon)
            {
                return false;
            }

            if (candidateF < incumbentF - TieEpsilon)
            {
                return true;
            }

            if (Mathf.Abs(candidateF - incumbentF) > TieEpsilon)
            {
                return false;
            }

            if (candidateParent != incumbentParent)
            {
                return candidateParent < incumbentParent;
            }

            if (candidateEdge != incumbentEdge)
            {
                return candidateEdge < incumbentEdge;
            }

            return false;
        }

        private static BotPathStatus WritePath(
            BotNavigationNodeRecord[] nodes,
            int startIndex,
            int goalIndex,
            int[] nodePathBuffer,
            int[] parentNodeBuffer,
            float cost,
            out int nodeCount,
            out float routeCost)
        {
            nodeCount = 0;
            routeCost = 0f;
            var current = goalIndex;
            while (true)
            {
                if (nodeCount >= nodePathBuffer.Length)
                {
                    nodeCount = 0;
                    routeCost = 0f;
                    return BotPathStatus.BufferTooSmall;
                }

                nodeCount++;
                if (current == startIndex)
                {
                    break;
                }

                var parentId = parentNodeBuffer[current];
                current = FindNodeIndex(nodes, parentId);
                if (current < 0)
                {
                    nodeCount = 0;
                    routeCost = 0f;
                    return BotPathStatus.Unreachable;
                }
            }

            current = goalIndex;
            for (var i = nodeCount - 1; i >= 0; i--)
            {
                nodePathBuffer[i] = nodes[current].Id;
                if (i > 0)
                {
                    current = FindNodeIndex(nodes, parentNodeBuffer[current]);
                }
            }

            routeCost = IsFinite(cost) ? cost : 0f;
            return BotPathStatus.Success;
        }

        private static float Heuristic(BotNavigationNodeRecord from, BotNavigationNodeRecord to)
        {
            var distance = Vector3.Distance(from.Position, to.Position);
            return IsFinite(distance) ? distance : float.PositiveInfinity;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
