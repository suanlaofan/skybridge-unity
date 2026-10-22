using System;
using System.Collections.Generic;
using System.Linq;

namespace Skybridge.Core
{
    public static class BridgeRules
    {
        public const int NormalSafeLimit = 3;
        public const int TrussedSafeLimit = 5;
        public const int MinimumVerticalClearance = 2;

        private static readonly PieceType[] PieceTypes =
        {
            PieceType.Deck,
            PieceType.Ramp,
            PieceType.Pier,
            PieceType.Truss,
        };

        public static string GridPointKey(GridPoint point)
        {
            return point.Key;
        }

        public static bool SameGridPoint(GridPoint left, GridPoint right)
        {
            return left == right;
        }

        public static RoadPath FindRoadPath(
            IEnumerable<RoadPiecePlacement> roads,
            GridPoint startAnchor,
            GridPoint goalAnchor)
        {
            var startKey = GridPointKey(startAnchor);
            var goalKey = GridPointKey(goalAnchor);
            var graph = new Dictionary<string, List<GraphEdge>>();
            var points = new Dictionary<string, GridPoint>
            {
                [startKey] = startAnchor,
                [goalKey] = goalAnchor,
            };

            foreach (var road in roads)
            {
                points[GridPointKey(road.From)] = road.From;
                points[GridPointKey(road.To)] = road.To;
                AddGraphEdge(graph, road.From, road.To, road);
                AddGraphEdge(graph, road.To, road.From, road);
            }

            if (startKey == goalKey)
            {
                return new RoadPath(new[] { startAnchor }, Array.Empty<RoadPathSegment>());
            }

            var queue = new Queue<string>();
            queue.Enqueue(startKey);
            var visited = new HashSet<string> { startKey };
            var previous = new Dictionary<string, PreviousStep>();

            while (queue.Count > 0 && !visited.Contains(goalKey))
            {
                var currentKey = queue.Dequeue();
                if (!graph.TryGetValue(currentKey, out var adjacentEdges))
                {
                    continue;
                }

                var edges = adjacentEdges
                    .OrderBy(edge => edge.Other)
                    .ThenBy(edge => edge.Piece.Id, StringComparer.Ordinal)
                    .ToList();
                foreach (var edge in edges)
                {
                    var nextKey = GridPointKey(edge.Other);
                    if (!visited.Add(nextKey))
                    {
                        continue;
                    }

                    previous[nextKey] = new PreviousStep(currentKey, edge.Piece);
                    queue.Enqueue(nextKey);
                }
            }

            if (!visited.Contains(goalKey))
            {
                return null;
            }

            var reversedSteps = new List<PreviousStep>();
            var reversedNodeKeys = new List<string> { goalKey };
            var cursor = goalKey;
            while (cursor != startKey)
            {
                if (!previous.TryGetValue(cursor, out var step))
                {
                    return null;
                }

                reversedSteps.Add(step);
                cursor = step.PreviousKey;
                reversedNodeKeys.Add(cursor);
            }

            reversedNodeKeys.Reverse();
            reversedSteps.Reverse();
            var nodes = reversedNodeKeys.Select(key => points.TryGetValue(key, out var point) ? point : startAnchor).ToList();
            var segments = new List<RoadPathSegment>();
            for (var index = 0; index < reversedSteps.Count; index++)
            {
                var step = reversedSteps[index];
                var from = nodes[index];
                var to = nodes[index + 1];
                segments.Add(new RoadPathSegment(step.Piece, from, to, step.Piece.From != from));
            }

            return new RoadPath(nodes, segments);
        }

        public static BridgeValidationResult ValidateBridge(LevelDefinition level, BridgeLayout layout)
        {
            var duplicateIssue = FindDuplicateIdIssue(layout);
            var inventoryIssue = FindInventoryIssue(level, layout);
            var placementIssue = FindRoadPlacementIssue(layout);
            var path = FindRoadPath(layout.Roads, level.StartAnchor, level.GoalAnchor);
            var disconnectedIssue = path == null
                ? new ValidationIssue(
                    ValidationIssueCode.DisconnectedPath,
                    "No continuous deck path from start to goal.",
                    point: level.StartAnchor)
                : null;
            var traversalIssue = path != null ? FindPathTraversalIssue(level, path) : null;
            var clearanceIssue = path != null ? FindClearanceIssue(layout, path) : null;
            var firstIssue = duplicateIssue ?? inventoryIssue ?? placementIssue ?? disconnectedIssue ?? traversalIssue ?? clearanceIssue;

            return new BridgeValidationResult(
                firstIssue == null,
                firstIssue == null ? Array.Empty<ValidationIssue>() : new[] { firstIssue },
                path);
        }

        public static IReadOnlyList<HardSupport> GetHardSupports(
            LevelDefinition level,
            BridgeLayout layout,
            RoadPath path)
        {
            var supports = new Dictionary<int, HardSupport>
            {
                [0] = new HardSupport(
                    HardSupportKind.Start,
                    path.Nodes.Count > 0 ? path.Nodes[0] : level.StartAnchor,
                    0),
            };

            foreach (var pier in layout.Piers.OrderBy(candidate => candidate.Id, StringComparer.Ordinal))
            {
                if (!IsValidPierSupport(level, path, pier))
                {
                    continue;
                }

                var pathNodeIndex = IndexOf(path.Nodes, pier.Top);
                if (pathNodeIndex > 0 && pathNodeIndex < path.Nodes.Count - 1 && !supports.ContainsKey(pathNodeIndex))
                {
                    supports[pathNodeIndex] = new HardSupport(
                        HardSupportKind.Pier,
                        pier.Top,
                        pathNodeIndex,
                        pier.Id);
                }
            }

            var goalIndex = path.Nodes.Count - 1;
            supports[goalIndex] = new HardSupport(
                HardSupportKind.Goal,
                goalIndex >= 0 ? path.Nodes[goalIndex] : level.GoalAnchor,
                goalIndex);

            return supports.Values.OrderBy(support => support.PathNodeIndex).ToList();
        }

        public static bool IsSpanFullyTrussed(
            IEnumerable<string> roadPieceIds,
            IEnumerable<TrussPlacement> trusses)
        {
            var roadIds = roadPieceIds.ToList();
            var trussList = trusses.ToList();
            return roadIds.Count > 0 && roadIds.All(roadPieceId =>
            {
                var matching = trussList.Where(truss => truss.RoadPieceId == roadPieceId).ToList();
                if (matching.Count != 1)
                {
                    return false;
                }

                var truss = matching[0];
                return truss.Snapped && truss.ConnectedAtFrom && truss.ConnectedAtTo;
            });
        }

        public static int SelectWeakPointPathNodeIndex(int pathStartIndex, int spanLength)
        {
            return pathStartIndex + (int)Math.Ceiling(spanLength / 2d);
        }

        public static SpanAnalysisResult AnalyzeSpans(LevelDefinition level, BridgeLayout layout)
        {
            var validation = ValidateBridge(level, layout);
            if (!validation.CanTest || validation.Path == null)
            {
                return new SpanAnalysisResult(
                    validation,
                    Array.Empty<HardSupport>(),
                    Array.Empty<SpanResult>(),
                    Array.Empty<WeakPoint>());
            }

            var path = validation.Path;
            var hardSupports = GetHardSupports(level, layout, path);
            var spans = new List<SpanResult>();
            for (var index = 0; index < hardSupports.Count - 1; index++)
            {
                var startSupport = hardSupports[index];
                var endSupport = hardSupports[index + 1];
                var pathStartIndex = startSupport.PathNodeIndex;
                var pathEndIndex = endSupport.PathNodeIndex;
                var segments = path.Segments
                    .Skip(pathStartIndex)
                    .Take(pathEndIndex - pathStartIndex)
                    .ToList();
                var roadPieceIds = segments.Select(segment => segment.Piece.Id).ToList();
                var fullyTrussed = IsSpanFullyTrussed(roadPieceIds, layout.Trusses);
                var safeLimit = fullyTrussed ? TrussedSafeLimit : NormalSafeLimit;
                var length = segments.Count;
                var safe = length <= safeLimit;
                var weakPoint = safe ? null : MakeWeakPoint(index, pathStartIndex, length, path);
                spans.Add(new SpanResult(
                    index,
                    startSupport,
                    endSupport,
                    pathStartIndex,
                    pathEndIndex,
                    roadPieceIds,
                    length,
                    fullyTrussed,
                    safeLimit,
                    safe,
                    weakPoint));
            }

            return new SpanAnalysisResult(
                validation,
                hardSupports,
                spans,
                spans.Where(span => span.WeakPoint != null).Select(span => span.WeakPoint));
        }

        public static int CalculateBuildCost(BridgeLayout layout)
        {
            var counts = CountPieces(layout);
            return PieceTypes.Sum(type => counts[type] * LevelCatalog.GetPieceCost(type));
        }

        public static Medal CalculateRating(LevelDefinition level, int cost, bool succeeded)
        {
            if (!succeeded)
            {
                return Medal.None;
            }

            if (cost <= level.MedalThresholds.Gold)
            {
                return Medal.Gold;
            }

            return cost <= level.MedalThresholds.Silver ? Medal.Silver : Medal.Bronze;
        }

        private static void AddGraphEdge(
            IDictionary<string, List<GraphEdge>> graph,
            GridPoint from,
            GridPoint other,
            RoadPiecePlacement piece)
        {
            var key = GridPointKey(from);
            if (!graph.TryGetValue(key, out var edges))
            {
                edges = new List<GraphEdge>();
                graph[key] = edges;
            }

            edges.Add(new GraphEdge(other, piece));
        }

        private static Dictionary<PieceType, int> CountPieces(BridgeLayout layout)
        {
            return new Dictionary<PieceType, int>
            {
                [PieceType.Deck] = layout.Roads.Count(piece => piece.Type == PieceType.Deck),
                [PieceType.Ramp] = layout.Roads.Count(piece => piece.Type == PieceType.Ramp),
                [PieceType.Pier] = layout.Piers.Count,
                [PieceType.Truss] = layout.Trusses.Count,
            };
        }

        private static ValidationIssue FindDuplicateIdIssue(BridgeLayout layout)
        {
            var ids = layout.AllPieces().Select(piece => piece.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
            for (var index = 1; index < ids.Count; index++)
            {
                if (ids[index] == ids[index - 1])
                {
                    return new ValidationIssue(
                        ValidationIssueCode.DuplicatePieceId,
                        $"Duplicate piece ID {ids[index]}.",
                        ids[index]);
                }
            }

            return null;
        }

        private static ValidationIssue FindInventoryIssue(LevelDefinition level, BridgeLayout layout)
        {
            var counts = CountPieces(layout);
            foreach (var type in PieceTypes)
            {
                if (counts[type] > level.Inventory[type])
                {
                    return new ValidationIssue(
                        ValidationIssueCode.InventoryExceeded,
                        $"Too many {GetPieceTypeToken(type)} pieces for this level's inventory.");
                }
            }

            return null;
        }

        private static ValidationIssue FindRoadPlacementIssue(BridgeLayout layout)
        {
            foreach (var road in layout.Roads.OrderBy(candidate => candidate.Id, StringComparer.Ordinal))
            {
                if (!road.Snapped)
                {
                    return new ValidationIssue(
                        ValidationIssueCode.UnsnappedRoad,
                        "A deck isn't fully snapped to the grid.",
                        road.Id,
                        road.From);
                }

                if (road.Type == PieceType.Deck && !IsDeckGeometryValid(road))
                {
                    return new ValidationIssue(
                        ValidationIssueCode.InvalidDeckGeometry,
                        "Decks must link adjacent grid nodes at the same height.",
                        road.Id,
                        road.From);
                }

                if (road.Type == PieceType.Ramp && !IsRampGeometryValid(road))
                {
                    return new ValidationIssue(
                        ValidationIssueCode.InvalidRampGeometry,
                        "Ramps must advance one cell and rise or drop exactly one level.",
                        road.Id,
                        road.From);
                }
            }

            return null;
        }

        private static ValidationIssue FindPathTraversalIssue(LevelDefinition level, RoadPath path)
        {
            foreach (var segment in path.Segments)
            {
                if (segment.Piece.Type == PieceType.Ramp && segment.Reversed)
                {
                    return new ValidationIssue(
                        ValidationIssueCode.WrongRampDirection,
                        "The ramp faces against the vehicle's direction of travel.",
                        segment.Piece.Id,
                        segment.From);
                }
            }

            if (level.AllowTurns)
            {
                return null;
            }

            for (var index = 1; index < path.Segments.Count; index++)
            {
                var previous = path.Segments[index - 1];
                var current = path.Segments[index];
                var previousX = previous.To.X - previous.From.X;
                var previousZ = previous.To.Z - previous.From.Z;
                var currentX = current.To.X - current.From.X;
                var currentZ = current.To.Z - current.From.Z;
                if (previousX != currentX || previousZ != currentZ)
                {
                    return new ValidationIssue(
                        ValidationIssueCode.SharpTurn,
                        "The vehicle path has a turn too sharp to pass.",
                        current.Piece.Id,
                        current.From);
                }
            }

            return null;
        }

        private static ValidationIssue FindClearanceIssue(BridgeLayout layout, RoadPath path)
        {
            var pathPieceIds = new HashSet<string>(path.Segments.Select(segment => segment.Piece.Id));
            var otherRoads = layout.Roads
                .Where(road => !pathPieceIds.Contains(road.Id))
                .OrderBy(road => road.Id, StringComparer.Ordinal)
                .ToList();

            for (var pathNodeIndex = 0; pathNodeIndex < path.Nodes.Count; pathNodeIndex++)
            {
                var point = path.Nodes[pathNodeIndex];
                foreach (var road in otherRoads)
                {
                    if (IsPerpendicularBranchAtPathNode(road, path, pathNodeIndex))
                    {
                        return new ValidationIssue(
                            ValidationIssueCode.ClearanceBlocked,
                            "Not enough side clearance on the vehicle path.",
                            road.Id,
                            point);
                    }

                    var obstacleHeight = RoadHeightAtProjection(road, point);
                    if (obstacleHeight.HasValue &&
                        obstacleHeight.Value > point.Y &&
                        obstacleHeight.Value - point.Y < MinimumVerticalClearance)
                    {
                        return new ValidationIssue(
                            ValidationIssueCode.ClearanceBlocked,
                            "Not enough headroom on the vehicle path.",
                            road.Id,
                            point);
                    }
                }
            }

            return null;
        }

        private static bool IsValidPierSupport(LevelDefinition level, RoadPath path, PierPlacement pier)
        {
            bool hasValidFoundation;
            if (level.BuildVolume != null)
            {
                var volume = level.BuildVolume;
                hasValidFoundation =
                    pier.Foundation.Y == volume.PierFoundationY &&
                    pier.Top.X >= volume.Min.X &&
                    pier.Top.X <= volume.Max.X &&
                    pier.Top.Y >= volume.Min.Y &&
                    pier.Top.Y <= volume.Max.Y &&
                    pier.Top.Z >= volume.Min.Z &&
                    pier.Top.Z <= volume.Max.Z;
            }
            else
            {
                hasValidFoundation = level.FoundationPads.Contains(pier.Foundation);
            }

            return pier.SnappedBase &&
                   pier.SnappedTop &&
                   pier.Foundation.Y < pier.Top.Y &&
                   pier.Foundation.X == pier.Top.X &&
                   pier.Foundation.Z == pier.Top.Z &&
                   hasValidFoundation &&
                   path.Nodes.Contains(pier.Top);
        }

        private static bool IsDeckGeometryValid(RoadPiecePlacement road)
        {
            return HorizontalDistance(road.From, road.To) == 1 && road.To.Y == road.From.Y;
        }

        private static bool IsRampGeometryValid(RoadPiecePlacement road)
        {
            return HorizontalDistance(road.From, road.To) == 1 && Math.Abs(road.To.Y - road.From.Y) == 1;
        }

        private static int HorizontalDistance(GridPoint from, GridPoint to)
        {
            return Math.Abs(to.X - from.X) + Math.Abs(to.Z - from.Z);
        }

        private static double? RoadHeightAtProjection(RoadPiecePlacement road, GridPoint point)
        {
            var deltaX = road.To.X - road.From.X;
            var deltaZ = road.To.Z - road.From.Z;
            var lengthSquared = deltaX * deltaX + deltaZ * deltaZ;
            if (lengthSquared == 0)
            {
                return null;
            }

            var offsetX = point.X - road.From.X;
            var offsetZ = point.Z - road.From.Z;
            var progress = (offsetX * deltaX + offsetZ * deltaZ) / (double)lengthSquared;
            var projectedX = road.From.X + deltaX * progress;
            var projectedZ = road.From.Z + deltaZ * progress;
            if (progress < 0 ||
                progress > 1 ||
                Math.Abs(projectedX - point.X) > double.Epsilon ||
                Math.Abs(projectedZ - point.Z) > double.Epsilon)
            {
                return null;
            }

            return road.From.Y + (road.To.Y - road.From.Y) * progress;
        }

        private static bool IsPerpendicularBranchAtPathNode(
            RoadPiecePlacement road,
            RoadPath path,
            int pathNodeIndex)
        {
            if (pathNodeIndex < 0 || pathNodeIndex >= path.Nodes.Count)
            {
                return false;
            }

            var point = path.Nodes[pathNodeIndex];
            RoadPathSegment tangent = null;
            if (pathNodeIndex < path.Segments.Count)
            {
                tangent = path.Segments[pathNodeIndex];
            }
            else if (pathNodeIndex > 0 && pathNodeIndex - 1 < path.Segments.Count)
            {
                tangent = path.Segments[pathNodeIndex - 1];
            }

            if (tangent == null || (road.From != point && road.To != point))
            {
                return false;
            }

            var pathX = tangent.To.X - tangent.From.X;
            var pathZ = tangent.To.Z - tangent.From.Z;
            var roadX = road.To.X - road.From.X;
            var roadZ = road.To.Z - road.From.Z;
            return pathX * roadX + pathZ * roadZ == 0;
        }

        private static WeakPoint MakeWeakPoint(int spanIndex, int pathStartIndex, int length, RoadPath path)
        {
            var pathNodeIndex = SelectWeakPointPathNodeIndex(pathStartIndex, length);
            if (pathNodeIndex <= 0 || pathNodeIndex >= path.Nodes.Count || pathNodeIndex >= path.Segments.Count)
            {
                throw new InvalidOperationException("A weak span must contain an internal joint.");
            }

            return new WeakPoint(
                spanIndex,
                pathNodeIndex,
                path.Nodes[pathNodeIndex],
                path.Segments[pathNodeIndex - 1].Piece.Id,
                path.Segments[pathNodeIndex].Piece.Id);
        }

        private static int IndexOf(IReadOnlyList<GridPoint> points, GridPoint target)
        {
            for (var index = 0; index < points.Count; index++)
            {
                if (points[index] == target)
                {
                    return index;
                }
            }

            return -1;
        }

        private static string GetPieceTypeToken(PieceType type)
        {
            switch (type)
            {
                case PieceType.Deck:
                    return "deck";
                case PieceType.Ramp:
                    return "ramp";
                case PieceType.Pier:
                    return "pier";
                case PieceType.Truss:
                    return "truss";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private sealed class GraphEdge
        {
            public GraphEdge(GridPoint other, RoadPiecePlacement piece)
            {
                Other = other;
                Piece = piece;
            }

            public GridPoint Other { get; }
            public RoadPiecePlacement Piece { get; }
        }

        private sealed class PreviousStep
        {
            public PreviousStep(string previousKey, RoadPiecePlacement piece)
            {
                PreviousKey = previousKey;
                Piece = piece;
            }

            public string PreviousKey { get; }
            public RoadPiecePlacement Piece { get; }
        }
    }
}
