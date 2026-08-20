using System;
using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    public enum BotNavigationArea
    {
        Floor = 0,
        RampDeck = 1,
        GoalRecess = 2
    }

    public enum BotNavigationTraversal
    {
        Walk = 0,
        Ramp = 1,
        Drop = 2,
        ShieldGate = 3
    }

    /// <summary>Authoritative X/Z limits shared by bot navigation and corner recovery.</summary>
    [Serializable]
    public sealed class BotArenaBounds
    {
        [SerializeField] private Vector3 center = Vector3.zero;
        [SerializeField, Min(0f)] private float halfLength = 65f;
        [SerializeField, Min(0f)] private float halfWidth = 45f;

        public BotArenaBounds() { }

        public BotArenaBounds(Vector3 center, float halfLength, float halfWidth)
        {
            this.center = center;
            this.halfLength = halfLength;
            this.halfWidth = halfWidth;
        }

        public Vector3 Center => center;
        public float HalfLength => halfLength;
        public float HalfWidth => halfWidth;

        public bool TryValidate(out string reason)
        {
            if (!IsFinite(center) || !IsFinite(halfLength) || !IsFinite(halfWidth))
            {
                reason = "center and half-extents must be finite";
                return false;
            }

            if (halfLength <= 0f || halfWidth <= 0f)
            {
                reason = "half-extents must be positive";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public bool IsValid => TryValidate(out _);

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [Serializable]
    public sealed class BotNavigationNodeRecord
    {
        [SerializeField] private int id;
        [SerializeField] private Vector3 position;
        [SerializeField] private BotNavigationArea area;
        [SerializeField] private float safeRadius;

        public BotNavigationNodeRecord(int id, Vector3 position, BotNavigationArea area, float safeRadius)
        {
            this.id = id;
            this.position = position;
            this.area = area;
            this.safeRadius = safeRadius;
        }

        public int Id => id;
        public Vector3 Position => position;
        public BotNavigationArea Area => area;
        public float SafeRadius => safeRadius;
    }

    [Serializable]
    public sealed class BotNavigationEdgeRecord
    {
        [SerializeField] private int id;
        [SerializeField] private int fromNodeId;
        [SerializeField] private int toNodeId;
        [SerializeField] private BotNavigationTraversal traversal;
        [SerializeField] private float cost;
        [SerializeField] private float corridorHalfWidth;
        [SerializeField] private Collider gateCollider;

        public BotNavigationEdgeRecord(
            int id,
            int fromNodeId,
            int toNodeId,
            BotNavigationTraversal traversal,
            float cost,
            float corridorHalfWidth,
            Collider gateCollider)
        {
            this.id = id;
            this.fromNodeId = fromNodeId;
            this.toNodeId = toNodeId;
            this.traversal = traversal;
            this.cost = cost;
            this.corridorHalfWidth = corridorHalfWidth;
            this.gateCollider = gateCollider;
        }

        public int Id => id;
        public int FromNodeId => fromNodeId;
        public int ToNodeId => toNodeId;
        public BotNavigationTraversal Traversal => traversal;
        public float Cost => cost;
        public float CorridorHalfWidth => corridorHalfWidth;
        public Collider GateCollider => gateCollider;
    }

    [DisallowMultipleComponent]
    public sealed class BotNavigationGraph : MonoBehaviour
    {
        public const float ExpectedControllerRadius = 0.8f;
        public const float ExpectedControllerHeight = 3.6f;
        public const float ExpectedControllerSlopeLimit = 60f;
        public const float ExpectedControllerStepOffset = 0.3f;
        public const float ExpectedControllerSkinWidth = 0.08f;
        public const float ExpectedControllerEffectiveRadius = ExpectedControllerRadius - ExpectedControllerSkinWidth;
        public const float ExpectedControllerClearance = ExpectedControllerEffectiveRadius;
        public const float ExpectedGoalRecessSafeRadius = 1f;
        public const float ExpectedArenaHalfLength = 65f;
        public const float ExpectedArenaHalfWidth = 45f;
        public static readonly Vector3 ExpectedControllerCenter = new Vector3(0f, 1.8f, 0f);
        public static readonly Vector3 ExpectedArenaCenter = Vector3.zero;

        [SerializeField] private BotNavigationNodeRecord[] nodes = new BotNavigationNodeRecord[0];
        [SerializeField] private BotNavigationEdgeRecord[] edges = new BotNavigationEdgeRecord[0];
        [SerializeField] private BotArenaBounds arenaBounds = new BotArenaBounds();

        [Header("Player CharacterController facts")]
        [SerializeField] private float controllerRadius = ExpectedControllerRadius;
        [SerializeField] private float controllerHeight = ExpectedControllerHeight;
        [SerializeField] private Vector3 controllerCenter = ExpectedControllerCenter;
        [SerializeField] private float controllerSlopeLimit = ExpectedControllerSlopeLimit;
        [SerializeField] private float controllerStepOffset = ExpectedControllerStepOffset;
        [SerializeField] private float controllerSkinWidth = ExpectedControllerSkinWidth;

        public int NodeCount => nodes == null ? 0 : nodes.Length;
        public int EdgeCount => edges == null ? 0 : edges.Length;
        public BotNavigationNodeRecord GetNode(int index)
        {
            return nodes != null && index >= 0 && index < nodes.Length ? nodes[index] : null;
        }

        public BotNavigationEdgeRecord GetEdge(int index)
        {
            return edges != null && index >= 0 && index < edges.Length ? edges[index] : null;
        }

        public float ControllerRadius => controllerRadius;
        public float ControllerHeight => controllerHeight;
        public Vector3 ControllerCenter => controllerCenter;
        public float ControllerSlopeLimit => controllerSlopeLimit;
        public float ControllerStepOffset => controllerStepOffset;
        public float ControllerSkinWidth => controllerSkinWidth;
        public float EffectiveControllerRadius => controllerRadius - controllerSkinWidth;
        public float ControllerClearance => EffectiveControllerRadius;
        public BotArenaBounds ArenaBounds => arenaBounds;

        internal BotNavigationNodeRecord[] NodesForNavigation => nodes;
        internal BotNavigationEdgeRecord[] EdgesForNavigation => edges;

        public bool TryValidate(out string reason)
        {
            reason = string.Empty;

            if (!IsFinite(controllerRadius) || !IsFinite(controllerHeight) || !IsFinite(controllerCenter) ||
                !IsFinite(controllerSlopeLimit) || !IsFinite(controllerStepOffset) || !IsFinite(controllerSkinWidth))
            {
                reason = "controller facts must be finite";
                return false;
            }

            if (Mathf.Abs(controllerRadius - ExpectedControllerRadius) > 0.001f ||
                Mathf.Abs(controllerHeight - ExpectedControllerHeight) > 0.001f ||
                Vector3.Distance(controllerCenter, ExpectedControllerCenter) > 0.001f ||
                Mathf.Abs(controllerSlopeLimit - ExpectedControllerSlopeLimit) > 0.001f ||
                Mathf.Abs(controllerStepOffset - ExpectedControllerStepOffset) > 0.001f ||
                Mathf.Abs(controllerSkinWidth - ExpectedControllerSkinWidth) > 0.001f)
            {
                reason = "controller facts do not match the Player CharacterController contract";
                return false;
            }

            var boundsReason = string.Empty;
            if (arenaBounds == null || !arenaBounds.TryValidate(out boundsReason))
            {
                reason = "arena bounds are invalid" + (string.IsNullOrEmpty(boundsReason) ? string.Empty : ": " + boundsReason);
                return false;
            }

            if (Vector3.Distance(arenaBounds.Center, ExpectedArenaCenter) > 0.001f ||
                Mathf.Abs(arenaBounds.HalfLength - ExpectedArenaHalfLength) > 0.001f ||
                Mathf.Abs(arenaBounds.HalfWidth - ExpectedArenaHalfWidth) > 0.001f)
            {
                reason = "arena bounds do not match the MovementLab X/Z contract";
                return false;
            }

            if (nodes == null || nodes.Length == 0)
            {
                reason = "nodes are missing";
                return false;
            }

            if (edges == null)
            {
                reason = "edges are missing";
                return false;
            }

            var minimumClearance = EffectiveControllerRadius;
            for (var i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null)
                {
                    reason = "node " + i + " is missing";
                    return false;
                }

                if (node.Id < 0)
                {
                    reason = "node " + i + " has a negative id";
                    return false;
                }

                if (!IsFinite(node.Position) || !IsFinite(node.SafeRadius) || node.SafeRadius < minimumClearance)
                {
                    reason = "node " + node.Id + " has invalid position or clearance";
                    return false;
                }

                if (node.Area == BotNavigationArea.GoalRecess && node.SafeRadius < ExpectedGoalRecessSafeRadius)
                {
                    reason = "goal recess node " + node.Id + " has insufficient safe radius";
                    return false;
                }

                if (node.Area != BotNavigationArea.Floor && node.Area != BotNavigationArea.RampDeck &&
                    node.Area != BotNavigationArea.GoalRecess)
                {
                    reason = "node " + node.Id + " has an invalid area";
                    return false;
                }

                for (var j = 0; j < i; j++)
                {
                    if (nodes[j] != null && nodes[j].Id == node.Id)
                    {
                        reason = "node ids are not unique: " + node.Id;
                        return false;
                    }
                }
            }

            for (var i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                if (edge == null)
                {
                    reason = "edge " + i + " is missing";
                    return false;
                }

                if (edge.Id < 0)
                {
                    reason = "edge " + i + " has a negative id";
                    return false;
                }

                for (var j = 0; j < i; j++)
                {
                    if (edges[j] != null && edges[j].Id == edge.Id)
                    {
                        reason = "edge ids are not unique: " + edge.Id;
                        return false;
                    }
                }

                var from = FindNode(edge.FromNodeId);
                var to = FindNode(edge.ToNodeId);
                if (from == null || to == null)
                {
                    reason = "edge " + edge.Id + " has an invalid endpoint";
                    return false;
                }

                var distance = Vector3.Distance(from.Position, to.Position);
                if (!IsFinite(edge.Cost) || edge.Cost <= 0f || edge.Cost + 0.0001f < distance)
                {
                    reason = "edge " + edge.Id + " has invalid cost";
                    return false;
                }

                if (!IsFinite(edge.CorridorHalfWidth) || edge.CorridorHalfWidth < minimumClearance)
                {
                    reason = "edge " + edge.Id + " has insufficient corridor clearance";
                    return false;
                }

                if (!IsValidTraversal(edge, from, to, out reason))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsValidTraversal(
            BotNavigationEdgeRecord edge,
            BotNavigationNodeRecord from,
            BotNavigationNodeRecord to,
            out string reason)
        {
            reason = string.Empty;
            var verticalDelta = to.Position.y - from.Position.y;
            var horizontal = new Vector2(to.Position.x - from.Position.x, to.Position.z - from.Position.z).magnitude;

            switch (edge.Traversal)
            {
                case BotNavigationTraversal.Walk:
                    var maxWalkVerticalDelta = controllerStepOffset + controllerSkinWidth + 0.0001f;
                    if (from.Area == to.Area)
                    {
                        if (Mathf.Abs(verticalDelta) > maxWalkVerticalDelta)
                        {
                            reason = "walk edge " + edge.Id + " exceeds the controller step limit";
                            return false;
                        }

                        return true;
                    }

                    var floorRampPair =
                        (from.Area == BotNavigationArea.Floor && to.Area == BotNavigationArea.RampDeck) ||
                        (from.Area == BotNavigationArea.RampDeck && to.Area == BotNavigationArea.Floor);
                    if (!floorRampPair || Mathf.Abs(verticalDelta) > maxWalkVerticalDelta)
                    {
                        reason = "walk edge " + edge.Id + " crosses an invalid area or step";
                        return false;
                    }

                    return true;

                case BotNavigationTraversal.Ramp:
                    if (from.Area != BotNavigationArea.RampDeck || to.Area != BotNavigationArea.RampDeck ||
                        horizontal <= 0.0001f)
                    {
                        reason = "ramp edge " + edge.Id + " must join RampDeck nodes";
                        return false;
                    }

                    var slope = Mathf.Atan2(Mathf.Abs(verticalDelta), horizontal) * Mathf.Rad2Deg;
                    if (!IsFinite(slope) || slope > controllerSlopeLimit + 0.001f)
                    {
                        reason = "ramp edge " + edge.Id + " exceeds the slope limit";
                        return false;
                    }

                    return true;

                case BotNavigationTraversal.Drop:
                    if (from.Area != BotNavigationArea.RampDeck || to.Area != BotNavigationArea.Floor ||
                        verticalDelta >= -0.0001f)
                    {
                        reason = "drop edge " + edge.Id + " must descend from RampDeck to Floor";
                        return false;
                    }

                    return true;

                case BotNavigationTraversal.ShieldGate:
                    var validGateAreas =
                        (from.Area == BotNavigationArea.Floor && to.Area == BotNavigationArea.GoalRecess) ||
                        (from.Area == BotNavigationArea.GoalRecess && to.Area == BotNavigationArea.Floor);
                    if (!validGateAreas || edge.GateCollider == null)
                    {
                        reason = "shield gate edge " + edge.Id + " requires Floor/GoalRecess endpoints and a collider";
                        return false;
                    }

                    return true;

                default:
                    reason = "edge " + edge.Id + " has an invalid traversal";
                    return false;
            }
        }

        private BotNavigationNodeRecord FindNode(int id)
        {
            for (var i = 0; i < NodeCount; i++)
            {
                var node = nodes[i];
                if (node != null && node.Id == id)
                {
                    return node;
                }
            }

            return null;
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
