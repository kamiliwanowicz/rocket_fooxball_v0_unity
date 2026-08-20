using System;
using UnityEngine;

namespace RocketFooxball.Runtime.Bots
{
    [DisallowMultipleComponent]
    public sealed class BotNavigator : MonoBehaviour
    {
        private const float TieEpsilon = 0.00001f;
        private const float DirectionEpsilon = 0.000001f;
        private const uint GateHashSeed = 2166136261u;
        private const uint GateHashPrime = 16777619u;

        [SerializeField] private BotNavigationGraph graph;
        [SerializeField] private LayerMask groundMask = 1;
        [SerializeField, Min(0f)] private float arrivalDistance = 0.75f;
        [SerializeField, Min(0f)] private float verticalArrivalDistance = 0.65f;
        [SerializeField, Min(0f)] private float lookaheadDistance = 3f;
        [SerializeField, Min(0f)] private float targetReplanDistance = 1.5f;
        [SerializeField, Min(0f)] private float corridorMargin = 1f;
        [SerializeField, Min(0f)] private float ledgeProbeDistance = 1.5f;
        [SerializeField, Min(0f)] private float ledgeProbeDepth = 2.5f;

        private int[] pathNodes;
        private int[] pathEdges;
        private float[] gScore;
        private float[] fScore;
        private int[] parentNode;
        private int[] parentEdge;
        private bool[] open;
        private bool[] closed;
        private int[] probePathNodes;
        private float[] probeGScore;
        private float[] probeFScore;
        private int[] probeParentNode;
        private int[] probeParentEdge;
        private bool[] probeOpen;
        private bool[] probeClosed;
        private bool[] edgeEnabled;

        private int pathCount;
        private int pathCursor;
        private bool hasRoute;
        private bool hasFailedRoute;
        private int routeStartNodeId = -1;
        private int routeTargetNodeId = -1;
        private Vector3 routeTarget;
        private uint routeGateHash;
        private int failedStartNodeId = -1;
        private int failedTargetNodeId = -1;
        private Vector3 failedTarget;
        private uint failedGateHash;
        private int bufferedNodeCount = -1;
        private int bufferedEdgeCapacity = -1;
        private uint gateHash;

        public BotNavigationGraph Graph => graph;
        public BotNavigationGraph NavigationGraph => graph;
        public LayerMask GroundMask => groundMask;
        public float ArrivalDistance => arrivalDistance;
        public float VerticalArrivalDistance => verticalArrivalDistance;
        public float LookaheadDistance => lookaheadDistance;
        public float TargetReplanDistance => targetReplanDistance;
        public float CorridorMargin => corridorMargin;
        public float LedgeProbeDistance => ledgeProbeDistance;
        public float LedgeProbeDepth => ledgeProbeDepth;
        public float EffectiveControllerRadius => graph != null ? graph.EffectiveControllerRadius : 0f;

        private void OnEnable()
        {
            if (!TryPrepareGraph())
            {
                enabled = false;
                return;
            }

            ClearRoute();
        }

        private void OnDisable()
        {
            ClearRoute();
        }

        public bool TryProbeTarget(Vector3 worldTarget, out Vector3 projectedTarget, out float routeCost)
        {
            projectedTarget = Vector3.zero;
            routeCost = 0f;
            if (!IsReady() || !IsFinite(worldTarget))
            {
                return false;
            }

            UpdateGateState();
            var startNodeId = FindNearestNodeId(transform.position);
            var targetNodeId = FindNearestNodeId(worldTarget);
            var targetNode = FindNode(targetNodeId);
            if (startNodeId < 0 || targetNode == null)
            {
                return false;
            }

            projectedTarget = targetNode.Position;
            var status = BotNavigationRules.FindPath(
                BuildNodeArrayView(),
                BuildEdgeArrayView(),
                startNodeId,
                targetNodeId,
                edgeEnabled,
                probePathNodes,
                probeGScore,
                probeFScore,
                probeParentNode,
                probeParentEdge,
                probeOpen,
                probeClosed,
                out _,
                out routeCost);
            if (status != BotPathStatus.Success)
            {
                projectedTarget = Vector3.zero;
                routeCost = 0f;
                return false;
            }

            return true;
        }

        public BotNavigationSteeringResult EvaluateSteering(Vector3 worldTarget)
        {
            if (!IsReady() || !IsFinite(worldTarget) || !IsFinite(transform.position))
            {
                ClearRoute();
                return BotNavigationSteeringResult.Invalid;
            }

            UpdateGateState();
            var startNodeId = FindNearestNodeId(transform.position);
            var targetNodeId = FindNearestNodeId(worldTarget);
            var targetNode = FindNode(targetNodeId);
            if (startNodeId < 0 || targetNode == null)
            {
                ClearRoute();
                return BotNavigationSteeringResult.Invalid;
            }

            var needsReplan = !hasRoute && !hasFailedRoute;
            if (hasRoute)
            {
                needsReplan = routeTargetNodeId != targetNodeId ||
                    gateHash != routeGateHash ||
                    HasMovedEnough(worldTarget, routeTarget, targetReplanDistance);

                if (!needsReplan && !HasActiveEdge() && routeStartNodeId != startNodeId)
                {
                    needsReplan = true;
                }

                if (!needsReplan && HasActiveEdge() && IsOutsideCorridor(pathCursor))
                {
                    DiscardRouteForReplan();
                    needsReplan = true;
                }
            }

            if (!needsReplan && hasFailedRoute)
            {
                needsReplan = failedGateHash != gateHash || failedTargetNodeId != targetNodeId ||
                    failedStartNodeId != startNodeId || HasMovedEnough(worldTarget, failedTarget, targetReplanDistance);
            }

            if (needsReplan)
            {
                if (!TryBuildRoute(startNodeId, targetNodeId, worldTarget))
                {
                    return BotNavigationSteeringResult.Unreachable;
                }
            }

            return EvaluateCurrentRoute(worldTarget, targetNode);
        }

        public void ClearRoute()
        {
            pathCount = 0;
            pathCursor = 0;
            hasRoute = false;
            hasFailedRoute = false;
            routeStartNodeId = -1;
            routeTargetNodeId = -1;
            routeTarget = Vector3.zero;
            routeGateHash = 0u;
            failedStartNodeId = -1;
            failedTargetNodeId = -1;
            failedTarget = Vector3.zero;
            failedGateHash = 0u;
        }

        private bool TryPrepareGraph()
        {
            var reason = string.Empty;
            if (graph == null || !graph.TryValidate(out reason))
            {
                if (graph == null)
                {
                    reason = "graph is missing";
                }

                Debug.LogError("BotNavigator requires a valid serialized BotNavigationGraph: " + reason + ".", this);
                return false;
            }

            if (!IsFinite(arrivalDistance) || !IsFinite(verticalArrivalDistance) || !IsFinite(lookaheadDistance) ||
                !IsFinite(targetReplanDistance) || !IsFinite(corridorMargin) || !IsFinite(ledgeProbeDistance) ||
                !IsFinite(ledgeProbeDepth) || arrivalDistance < 0f || verticalArrivalDistance < 0f ||
                lookaheadDistance < 0f || targetReplanDistance < 0f || corridorMargin < 0f ||
                ledgeProbeDistance < 0f || ledgeProbeDepth < 0f)
            {
                Debug.LogError("BotNavigator requires valid serialized steering tuning.", this);
                return false;
            }

            var maxEdgeId = FindMaxEdgeId();
            var edgeCapacity = maxEdgeId < 0 ? 0 : maxEdgeId + 1;
            if (bufferedNodeCount != graph.NodeCount || bufferedEdgeCapacity != edgeCapacity ||
                pathNodes == null || pathEdges == null || gScore == null || fScore == null || parentNode == null ||
                parentEdge == null || open == null || closed == null || probePathNodes == null || probeGScore == null ||
                probeFScore == null || probeParentNode == null || probeParentEdge == null || probeOpen == null ||
                probeClosed == null || edgeEnabled == null)
            {
                pathNodes = new int[graph.NodeCount];
                pathEdges = new int[Mathf.Max(graph.NodeCount - 1, 0)];
                gScore = new float[graph.NodeCount];
                fScore = new float[graph.NodeCount];
                parentNode = new int[graph.NodeCount];
                parentEdge = new int[graph.NodeCount];
                open = new bool[graph.NodeCount];
                closed = new bool[graph.NodeCount];
                probePathNodes = new int[graph.NodeCount];
                probeGScore = new float[graph.NodeCount];
                probeFScore = new float[graph.NodeCount];
                probeParentNode = new int[graph.NodeCount];
                probeParentEdge = new int[graph.NodeCount];
                probeOpen = new bool[graph.NodeCount];
                probeClosed = new bool[graph.NodeCount];
                edgeEnabled = new bool[edgeCapacity];
                bufferedNodeCount = graph.NodeCount;
                bufferedEdgeCapacity = edgeCapacity;
            }

            return true;
        }

        private bool IsReady()
        {
            return isActiveAndEnabled && graph != null && graph.NodeCount > 0 && graph.TryValidate(out _) &&
                bufferedNodeCount == graph.NodeCount && bufferedEdgeCapacity == FindMaxEdgeId() + 1 &&
                pathNodes != null && pathEdges != null && gScore != null && fScore != null && parentNode != null &&
                parentEdge != null && open != null && closed != null && probePathNodes != null && probeGScore != null &&
                probeFScore != null && probeParentNode != null && probeParentEdge != null && probeOpen != null &&
                probeClosed != null && edgeEnabled != null;
        }

        private bool TryBuildRoute(int startNodeId, int targetNodeId, Vector3 worldTarget)
        {
            var status = BotNavigationRules.FindPath(
                BuildNodeArrayView(),
                BuildEdgeArrayView(),
                startNodeId,
                targetNodeId,
                edgeEnabled,
                pathNodes,
                gScore,
                fScore,
                parentNode,
                parentEdge,
                open,
                closed,
                out pathCount,
                out _);

            if (status != BotPathStatus.Success || pathCount <= 0)
            {
                hasRoute = false;
                hasFailedRoute = true;
                pathCount = 0;
                pathCursor = 0;
                failedStartNodeId = startNodeId;
                failedTargetNodeId = targetNodeId;
                failedTarget = worldTarget;
                failedGateHash = gateHash;
                return false;
            }

            hasRoute = true;
            hasFailedRoute = false;
            pathCursor = 0;
            routeStartNodeId = startNodeId;
            routeTargetNodeId = targetNodeId;
            routeTarget = worldTarget;
            routeGateHash = gateHash;
            for (var i = 1; i < pathCount; i++)
            {
                var childIndex = FindNodeIndex(pathNodes[i]);
                pathEdges[i - 1] = childIndex >= 0 ? parentEdge[childIndex] : -1;
            }

            return true;
        }

        private BotNavigationSteeringResult EvaluateCurrentRoute(
            Vector3 worldTarget,
            BotNavigationNodeRecord targetNode)
        {
            if (!hasRoute || pathCount <= 0 || pathCursor < 0 || pathCursor >= pathCount)
            {
                return BotNavigationSteeringResult.Unreachable;
            }

            if (HasActiveEdge())
            {
                var edge = FindEdge(pathEdges[pathCursor]);
                var to = FindNode(pathNodes[pathCursor + 1]);
                if (edge == null || to == null)
                {
                    DiscardRouteForReplan();
                    return BotNavigationSteeringResult.Unreachable;
                }

                if (HasReachedEdgeEndpoint(to))
                {
                    pathCursor++;
                }
            }

            if (!HasActiveEdge())
            {
                var terminal = EvaluateTerminal(worldTarget, targetNode);
                if (terminal.Status != BotNavigationStatus.Invalid)
                {
                    return terminal;
                }
            }

            if (!HasActiveEdge())
            {
                return BotNavigationSteeringResult.Unreachable;
            }

            var activeEdge = FindEdge(pathEdges[pathCursor]);
            var fromNode = FindNode(pathNodes[pathCursor]);
            var toNode = FindNode(pathNodes[pathCursor + 1]);
            if (activeEdge == null || fromNode == null || toNode == null)
            {
                DiscardRouteForReplan();
                return BotNavigationSteeringResult.Unreachable;
            }

            var verticalRoute = GetVerticalRoute(fromNode, toNode);
            var steeringPoint = ComputeCorridorSteeringPoint(activeEdge, fromNode, toNode, transform.position);
            var direction = steeringPoint - transform.position;
            if (!IsFinite(steeringPoint) || !IsFinite(direction) || direction.sqrMagnitude <= DirectionEpsilon)
            {
                direction = toNode.Position - transform.position;
            }

            if (!IsFinite(direction) || direction.sqrMagnitude <= DirectionEpsilon)
            {
                return new BotNavigationSteeringResult(
                    BotNavigationStatus.Following,
                    steeringPoint,
                    Vector3.zero,
                    verticalRoute);
            }

            direction.Normalize();
            if (activeEdge.Traversal != BotNavigationTraversal.Drop && !PassesLedgeProbe(direction))
            {
                DiscardRouteForReplan();
                return new BotNavigationSteeringResult(
                    BotNavigationStatus.Following,
                    steeringPoint,
                    Vector3.zero,
                    verticalRoute);
            }

            return new BotNavigationSteeringResult(
                BotNavigationStatus.Following,
                steeringPoint,
                direction,
                verticalRoute);
        }

        private BotNavigationSteeringResult EvaluateTerminal(
            Vector3 worldTarget,
            BotNavigationNodeRecord targetNode)
        {
            var terminalNode = FindNode(pathNodes[pathCount - 1]);
            if (terminalNode == null || targetNode == null || terminalNode.Id != targetNode.Id ||
                terminalNode.Area != targetNode.Area)
            {
                DiscardRouteForReplan();
                return BotNavigationSteeringResult.Invalid;
            }

            var position = transform.position;
            var horizontal = new Vector2(worldTarget.x - position.x, worldTarget.z - position.z).magnitude;
            if (horizontal <= SafeArrivalDistance())
            {
                return new BotNavigationSteeringResult(
                    BotNavigationStatus.Arrived,
                    new Vector3(worldTarget.x, position.y, worldTarget.z),
                    Vector3.zero,
                    BotVerticalRoute.None);
            }

            Vector3 steeringPoint;
            BotVerticalRoute verticalRoute;
            if (terminalNode.Area == BotNavigationArea.Floor || terminalNode.Area == BotNavigationArea.GoalRecess)
            {
                steeringPoint = new Vector3(worldTarget.x, position.y, worldTarget.z);
                verticalRoute = BotVerticalRoute.None;
            }
            else if (!TryProjectRampTerminal(worldTarget, terminalNode, out steeringPoint, out verticalRoute))
            {
                return new BotNavigationSteeringResult(
                    BotNavigationStatus.Following,
                    position,
                    Vector3.zero,
                    BotVerticalRoute.None);
            }

            var direction = steeringPoint - position;
            if (!IsFinite(direction) || direction.sqrMagnitude <= DirectionEpsilon)
            {
                return new BotNavigationSteeringResult(
                    BotNavigationStatus.Following,
                    steeringPoint,
                    Vector3.zero,
                    verticalRoute);
            }

            direction.Normalize();
            if (!PassesLedgeProbe(direction))
            {
                DiscardRouteForReplan();
                return new BotNavigationSteeringResult(
                    BotNavigationStatus.Following,
                    steeringPoint,
                    Vector3.zero,
                    verticalRoute);
            }

            return new BotNavigationSteeringResult(
                BotNavigationStatus.Following,
                steeringPoint,
                direction,
                verticalRoute);
        }

        private bool TryProjectRampTerminal(
            Vector3 worldTarget,
            BotNavigationNodeRecord terminalNode,
            out Vector3 steeringPoint,
            out BotVerticalRoute verticalRoute)
        {
            steeringPoint = transform.position;
            verticalRoute = BotVerticalRoute.None;
            var bestEdgeId = int.MaxValue;
            BotNavigationNodeRecord bestOther = null;
            var bestT = 0f;
            var bestLateral = 0f;
            for (var i = 0; i < graph.EdgeCount; i++)
            {
                var edge = graph.GetEdge(i);
                if (edge == null || edge.Traversal != BotNavigationTraversal.Ramp || edge.Id >= bestEdgeId)
                {
                    continue;
                }

                BotNavigationNodeRecord candidateOther = null;
                if (edge.FromNodeId == terminalNode.Id)
                {
                    candidateOther = FindNode(edge.ToNodeId);
                }
                else if (edge.ToNodeId == terminalNode.Id)
                {
                    candidateOther = FindNode(edge.FromNodeId);
                }

                if (candidateOther == null)
                {
                    continue;
                }

                var delta = terminalNode.Position - candidateOther.Position;
                var deltaXZ = new Vector2(delta.x, delta.z);
                var lengthSquared = deltaXZ.sqrMagnitude;
                if (lengthSquared <= DirectionEpsilon)
                {
                    continue;
                }

                var targetDelta = new Vector2(worldTarget.x - candidateOther.Position.x, worldTarget.z - candidateOther.Position.z);
                var unclampedT = Vector2.Dot(targetDelta, deltaXZ) / lengthSquared;
                if (unclampedT < -TieEpsilon || unclampedT > 1f + TieEpsilon)
                {
                    continue;
                }

                var t = Mathf.Clamp01(unclampedT);
                var directionXZ = deltaXZ.normalized;
                var lateral = targetDelta - deltaXZ * t;
                var lateralDirection = new Vector2(-directionXZ.y, directionXZ.x);
                var lateralAmount = Vector2.Dot(lateral, lateralDirection);
                if (Mathf.Abs(lateralAmount) > SafeCorridorHalfWidth(edge) + TieEpsilon)
                {
                    continue;
                }

                bestEdgeId = edge.Id;
                bestOther = candidateOther;
                bestT = t;
                bestLateral = lateralAmount;
            }

            if (bestOther == null)
            {
                return false;
            }

            var bestDelta = terminalNode.Position - bestOther.Position;
            var bestDeltaXZ = new Vector2(bestDelta.x, bestDelta.z);
            var bestCenter = bestOther.Position + bestDelta * bestT;
            var bestDirectionXZ = bestDeltaXZ.normalized;
            var bestLateralDirection = new Vector2(-bestDirectionXZ.y, bestDirectionXZ.x);
            var projected = bestCenter + new Vector3(bestLateralDirection.x, 0f, bestLateralDirection.y) * bestLateral;
            steeringPoint = new Vector3(projected.x, bestCenter.y, projected.z);
            verticalRoute = GetVerticalRoute(terminalNode, bestOther);
            return true;
        }

        private Vector3 ComputeCorridorSteeringPoint(
            BotNavigationEdgeRecord edge,
            BotNavigationNodeRecord from,
            BotNavigationNodeRecord to,
            Vector3 position)
        {
            var delta = to.Position - from.Position;
            var deltaXZ = new Vector2(delta.x, delta.z);
            var lengthSquared = deltaXZ.sqrMagnitude;
            if (lengthSquared <= DirectionEpsilon)
            {
                return to.Position;
            }

            var currentDelta = new Vector2(position.x - from.Position.x, position.z - from.Position.z);
            var t = Mathf.Clamp01(Vector2.Dot(currentDelta, deltaXZ) / lengthSquared);
            var length = Mathf.Sqrt(lengthSquared);
            var lookaheadT = length > DirectionEpsilon ? SafeLookaheadDistance() / length : 0f;
            var steeringT = Mathf.Clamp01(t + lookaheadT);
            var center = from.Position + delta * steeringT;
            var directionXZ = deltaXZ / length;
            var lateralDirection = new Vector2(-directionXZ.y, directionXZ.x);
            var lateral = currentDelta - deltaXZ * t;
            var lateralAmount = Mathf.Clamp(Vector2.Dot(lateral, lateralDirection), -SafeCorridorHalfWidth(edge), SafeCorridorHalfWidth(edge));
            var steering = center + new Vector3(lateralDirection.x, 0f, lateralDirection.y) * lateralAmount;

            if (t >= 1f - SafeLookaheadDistance() / Mathf.Max(length, DirectionEpsilon))
            {
                steering = to.Position;
            }

            return steering;
        }

        private bool HasReachedEdgeEndpoint(BotNavigationNodeRecord to)
        {
            var position = transform.position;
            var horizontal = new Vector2(position.x - to.Position.x, position.z - to.Position.z).magnitude;
            return horizontal <= Mathf.Min(SafeArrivalDistance(), Mathf.Max(to.SafeRadius, 0f)) &&
                Mathf.Abs(position.y - to.Position.y) <= SafeVerticalArrivalDistance();
        }

        private bool IsOutsideCorridor(int edgeIndex)
        {
            if (edgeIndex < 0 || edgeIndex >= pathCount - 1)
            {
                return false;
            }

            var edge = FindEdge(pathEdges[edgeIndex]);
            var from = FindNode(pathNodes[edgeIndex]);
            var to = FindNode(pathNodes[edgeIndex + 1]);
            if (edge == null || from == null || to == null)
            {
                return true;
            }

            var deltaXZ = new Vector2(to.Position.x - from.Position.x, to.Position.z - from.Position.z);
            if (deltaXZ.sqrMagnitude <= DirectionEpsilon)
            {
                return false;
            }

            var positionDelta = new Vector2(transform.position.x - from.Position.x, transform.position.z - from.Position.z);
            var direction = deltaXZ.normalized;
            var lateralDirection = new Vector2(-direction.y, direction.x);
            var lateral = Mathf.Abs(Vector2.Dot(positionDelta, lateralDirection));
            return lateral > SafeCorridorHalfWidth(edge) + SafeCorridorMargin();
        }

        private bool PassesLedgeProbe(Vector3 direction)
        {
            var desiredXZ = new Vector3(direction.x, 0f, direction.z);
            if (desiredXZ.sqrMagnitude > DirectionEpsilon)
            {
                desiredXZ.Normalize();
            }

            var origin = transform.position + desiredXZ * SafeLedgeProbeDistance() +
                Vector3.up * (graph.ControllerStepOffset + graph.ControllerRadius + graph.ControllerSkinWidth);
            if (!UnityEngine.Physics.SphereCast(
                    origin,
                    graph.EffectiveControllerRadius,
                    Vector3.down,
                    out var hit,
                    SafeLedgeProbeDepth(),
                    groundMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (hit.collider == null || hit.rigidbody != null || hit.collider.attachedRigidbody != null || !IsFinite(hit.normal) ||
                hit.normal.sqrMagnitude <= DirectionEpsilon)
            {
                return false;
            }

            var angle = Vector3.Angle(hit.normal, Vector3.up);
            return IsFinite(angle) && angle <= graph.ControllerSlopeLimit + 0.001f;
        }

        private void UpdateGateState()
        {
            var hash = GateHashSeed;
            for (var i = 0; i < graph.EdgeCount; i++)
            {
                var edge = graph.GetEdge(i);
                if (edge == null || edge.Id < 0 || edge.Id >= edgeEnabled.Length)
                {
                    continue;
                }

                var enabledEdge = edge.Traversal != BotNavigationTraversal.ShieldGate ||
                    (edge.GateCollider != null && !edge.GateCollider.enabled);
                edgeEnabled[edge.Id] = enabledEdge;
                unchecked
                {
                    hash = (hash ^ (uint)edge.Id) * GateHashPrime;
                    hash = (hash ^ (enabledEdge ? 1u : 0u)) * GateHashPrime;
                }
            }

            gateHash = hash;
        }

        private int FindNearestNodeId(Vector3 position)
        {
            if (!IsFinite(position))
            {
                return -1;
            }

            var found = false;
            var bestId = -1;
            var bestDistance = float.PositiveInfinity;
            for (var i = 0; i < graph.NodeCount; i++)
            {
                var node = graph.GetNode(i);
                if (node == null || !IsFinite(node.Position))
                {
                    continue;
                }

                var distance = (node.Position - position).sqrMagnitude;
                if (!IsFinite(distance))
                {
                    continue;
                }

                if (!found || distance < bestDistance - TieEpsilon ||
                    (Mathf.Abs(distance - bestDistance) <= TieEpsilon && node.Id < bestId))
                {
                    found = true;
                    bestId = node.Id;
                    bestDistance = distance;
                }
            }

            return found ? bestId : -1;
        }

        private BotNavigationNodeRecord FindNode(int id)
        {
            for (var i = 0; i < graph.NodeCount; i++)
            {
                var node = graph.GetNode(i);
                if (node != null && node.Id == id)
                {
                    return node;
                }
            }

            return null;
        }

        private int FindNodeIndex(int id)
        {
            for (var i = 0; i < graph.NodeCount; i++)
            {
                var node = graph.GetNode(i);
                if (node != null && node.Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private BotNavigationEdgeRecord FindEdge(int id)
        {
            for (var i = 0; i < graph.EdgeCount; i++)
            {
                var edge = graph.GetEdge(i);
                if (edge != null && edge.Id == id)
                {
                    return edge;
                }
            }

            return null;
        }

        private BotNavigationNodeRecord[] BuildNodeArrayView()
        {
            return graph.NodesForNavigation;
        }

        private BotNavigationEdgeRecord[] BuildEdgeArrayView()
        {
            return graph.EdgesForNavigation;
        }

        private int FindMaxEdgeId()
        {
            var max = -1;
            if (graph == null)
            {
                return max;
            }

            for (var i = 0; i < graph.EdgeCount; i++)
            {
                var edge = graph.GetEdge(i);
                if (edge != null && edge.Id > max)
                {
                    max = edge.Id;
                }
            }

            return max;
        }

        private bool HasActiveEdge()
        {
            return hasRoute && pathCursor >= 0 && pathCursor < pathCount - 1;
        }

        private void DiscardRouteForReplan()
        {
            hasRoute = false;
            pathCount = 0;
            pathCursor = 0;
            routeStartNodeId = -1;
            routeTargetNodeId = -1;
            routeTarget = Vector3.zero;
            routeGateHash = 0u;
        }

        private float SafeArrivalDistance()
        {
            return IsFinite(arrivalDistance) ? Mathf.Max(arrivalDistance, 0f) : 0f;
        }

        private float SafeVerticalArrivalDistance()
        {
            return IsFinite(verticalArrivalDistance) ? Mathf.Max(verticalArrivalDistance, 0f) : 0f;
        }

        private float SafeLookaheadDistance()
        {
            return IsFinite(lookaheadDistance) ? Mathf.Max(lookaheadDistance, 0f) : 0f;
        }

        private float SafeCorridorMargin()
        {
            return IsFinite(corridorMargin) ? Mathf.Max(corridorMargin, 0f) : 0f;
        }

        private float SafeLedgeProbeDistance()
        {
            return IsFinite(ledgeProbeDistance) ? Mathf.Max(ledgeProbeDistance, 0f) : 0f;
        }

        private float SafeLedgeProbeDepth()
        {
            return IsFinite(ledgeProbeDepth) ? Mathf.Max(ledgeProbeDepth, 0f) : 0f;
        }

        private float SafeCorridorHalfWidth(BotNavigationEdgeRecord edge)
        {
            if (edge == null || !IsFinite(edge.CorridorHalfWidth))
            {
                return 0f;
            }

            return Mathf.Max(
                edge.CorridorHalfWidth - graph.EffectiveControllerRadius,
                0f);
        }

        private BotVerticalRoute GetVerticalRoute(BotNavigationNodeRecord from, BotNavigationNodeRecord to)
        {
            if (to.Position.y > from.Position.y + TieEpsilon)
            {
                return BotVerticalRoute.Ascend;
            }

            if (to.Position.y < from.Position.y - TieEpsilon)
            {
                return BotVerticalRoute.Descend;
            }

            return BotVerticalRoute.None;
        }

        private static bool HasMovedEnough(Vector3 a, Vector3 b, float threshold)
        {
            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(threshold))
            {
                return true;
            }

            var safeThreshold = Mathf.Max(threshold, 0f);
            return (a - b).sqrMagnitude >= safeThreshold * safeThreshold;
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
