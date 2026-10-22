using System;
using System.Collections.Generic;
using System.Linq;

namespace Skybridge.Core
{
    public sealed class BuildController
    {
        private static readonly GridDirection[] Directions =
        {
            new GridDirection(1, 0),
            new GridDirection(0, 1),
            new GridDirection(-1, 0),
            new GridDirection(0, -1),
        };

        private LevelDefinition level;
        private BridgeLayout layout = BridgeLayout.Empty;
        private List<BridgeLayout> history = new List<BridgeLayout> { BridgeLayout.Empty };
        private int historyIndex;
        private PieceType? activeTool;
        private string selectedPieceId;
        private int rotationQuarterTurns;
        private int activeLayer;
        private int rampDelta = 1;
        private int idCounter = 1;
        private IReadOnlyList<GridPoint> referenceNodes;
        private IReadOnlyList<BuildEdge> buildEdges;

        public BuildController(LevelDefinition level)
        {
            this.level = level ?? throw new ArgumentNullException(nameof(level));
            activeLayer = level.StartAnchor.Y;
            referenceNodes = GetReferenceNodes(level);
            buildEdges = GetBuildEdges();
        }

        public LevelDefinition Level => level;
        public PieceType? ActiveTool => activeTool;
        public string SelectedPieceId => selectedPieceId;
        public int RotationQuarterTurns => rotationQuarterTurns;
        public int ActiveLayer => activeLayer;
        public int RampDelta => rampDelta;
        public bool CanUndo => historyIndex > 0;
        public bool CanRedo => historyIndex < history.Count - 1;

        public void SetLevel(LevelDefinition nextLevel)
        {
            level = nextLevel ?? throw new ArgumentNullException(nameof(nextLevel));
            layout = BridgeLayout.Empty;
            history = new List<BridgeLayout> { BridgeLayout.Empty };
            historyIndex = 0;
            activeTool = null;
            selectedPieceId = null;
            rotationQuarterTurns = 0;
            activeLayer = level.StartAnchor.Y;
            rampDelta = 1;
            idCounter = 1;
            referenceNodes = GetReferenceNodes(level);
            buildEdges = GetBuildEdges();
        }

        public BridgeLayout GetLayout()
        {
            return layout.DeepClone();
        }

        public int GetCost()
        {
            return BridgeRules.CalculateBuildCost(layout);
        }

        public PieceInventory GetInventoryRemaining()
        {
            var used = CountPieces(layout);
            return new PieceInventory(
                Math.Max(0, level.Inventory.Deck - used[PieceType.Deck]),
                Math.Max(0, level.Inventory.Ramp - used[PieceType.Ramp]),
                Math.Max(0, level.Inventory.Pier - used[PieceType.Pier]),
                Math.Max(0, level.Inventory.Truss - used[PieceType.Truss]));
        }

        public IReadOnlyList<RoadSlot> GetRoadSlots()
        {
            var edges = level.BuildVolume != null ? GetVolumeBuildEdges() : buildEdges;
            return edges.Select((edge, index) =>
            {
                var road = layout.Roads.FirstOrDefault(candidate => SameRoadEdge(candidate, edge.From, edge.To));
                var truss = road == null
                    ? null
                    : layout.Trusses.FirstOrDefault(candidate => candidate.RoadPieceId == road.Id);
                return new RoadSlot(
                    index,
                    edge.From,
                    edge.To,
                    edge.Axis,
                    true,
                    road?.Id,
                    truss?.Id);
            }).ToList();
        }

        public IReadOnlyList<FoundationSlot> GetFoundationSlots()
        {
            var volume = level.BuildVolume;
            if (volume != null)
            {
                var slots = new List<FoundationSlot>();
                for (var z = volume.Min.Z; z <= volume.Max.Z; z++)
                {
                    for (var x = volume.Min.X; x <= volume.Max.X; x++)
                    {
                        var foundation = new GridPoint(x, volume.PierFoundationY, z);
                        var top = new GridPoint(x, activeLayer, z);
                        var pier = layout.Piers.FirstOrDefault(candidate => candidate.Foundation == foundation);
                        slots.Add(new FoundationSlot(slots.Count, foundation, top, pier?.Id));
                    }
                }

                return slots;
            }

            return level.FoundationPads.Select((foundation, index) =>
            {
                var pier = layout.Piers.FirstOrDefault(candidate => candidate.Foundation == foundation);
                var node = referenceNodes.FirstOrDefault(candidate =>
                    candidate.X == foundation.X && candidate.Z == foundation.Z);
                var hasNode = referenceNodes.Any(candidate =>
                    candidate.X == foundation.X && candidate.Z == foundation.Z);
                var top = hasNode ? node : new GridPoint(foundation.X, 0, foundation.Z);
                return new FoundationSlot(index, foundation, top, pier?.Id);
            }).ToList();
        }

        public LayerBounds? GetLayerBounds()
        {
            var volume = level.BuildVolume;
            return volume == null ? (LayerBounds?)null : new LayerBounds(volume.Min.Y, volume.Max.Y);
        }

        public bool SetActiveLayer(double layer)
        {
            var bounds = GetLayerBounds();
            if (!bounds.HasValue)
            {
                return false;
            }

            var rounded = (int)Math.Floor(layer + 0.5d);
            var next = Math.Max(bounds.Value.Min, Math.Min(bounds.Value.Max, rounded));
            if (next == activeLayer)
            {
                return false;
            }

            activeLayer = next;
            KeepRampDeltaInBounds();
            return true;
        }

        public bool SetRampDelta(int delta)
        {
            if (delta != -1 && delta != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), "Ramp delta must be -1 or 1.");
            }

            var volume = level.BuildVolume;
            if (volume != null &&
                (activeLayer + delta < volume.Min.Y || activeLayer + delta > volume.Max.Y))
            {
                return false;
            }

            if (rampDelta == delta)
            {
                return false;
            }

            rampDelta = delta;
            return true;
        }

        public PieceType? GetPlacementType()
        {
            var selected = selectedPieceId == null ? null : FindPiece(layout, selectedPieceId);
            return selected?.Type ?? activeTool;
        }

        public int? FindRoadSlotIndex(RoadPiecePlacement piece)
        {
            var slots = GetRoadSlots();
            for (var index = 0; index < slots.Count; index++)
            {
                if (SameRoadEdge(piece, slots[index].From, slots[index].To))
                {
                    return index;
                }
            }

            return null;
        }

        public void SelectTool(PieceType tool)
        {
            activeTool = activeTool == tool ? (PieceType?)null : tool;
            selectedPieceId = null;
            rotationQuarterTurns = 0;
        }

        public void CancelSelection()
        {
            activeTool = null;
            selectedPieceId = null;
        }

        public void Rotate(int direction)
        {
            if (direction != -1 && direction != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(direction), "Rotation direction must be -1 or 1.");
            }

            rotationQuarterTurns = (rotationQuarterTurns + direction + 4) % 4;
        }

        public bool Undo()
        {
            if (!CanUndo)
            {
                return false;
            }

            historyIndex--;
            layout = history[historyIndex].DeepClone();
            selectedPieceId = null;
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo)
            {
                return false;
            }

            historyIndex++;
            layout = history[historyIndex].DeepClone();
            selectedPieceId = null;
            return true;
        }

        public bool Reset()
        {
            if (layout.Roads.Count + layout.Piers.Count + layout.Trusses.Count == 0)
            {
                return false;
            }

            Commit(BridgeLayout.Empty);
            selectedPieceId = null;
            return true;
        }

        public void ReplaceLayout(BridgeLayout nextLayout)
        {
            Commit(nextLayout);
            selectedPieceId = null;
            activeTool = null;
            rotationQuarterTurns = 0;
        }

        public bool RemoveSelected()
        {
            if (selectedPieceId == null)
            {
                return false;
            }

            var piece = FindPiece(layout, selectedPieceId);
            if (piece == null)
            {
                selectedPieceId = null;
                return false;
            }

            var removesRoad = piece.Type == PieceType.Deck || piece.Type == PieceType.Ramp;
            var next = new BridgeLayout(
                removesRoad ? layout.Roads.Where(road => road.Id != piece.Id) : layout.Roads,
                piece.Type == PieceType.Pier ? layout.Piers.Where(pier => pier.Id != piece.Id) : layout.Piers,
                piece.Type == PieceType.Truss
                    ? layout.Trusses.Where(truss => truss.Id != piece.Id)
                    : removesRoad
                        ? layout.Trusses.Where(truss => truss.RoadPieceId != piece.Id)
                        : layout.Trusses);
            Commit(next);
            selectedPieceId = null;
            return true;
        }

        public BuildPreview Preview(BuildHit hit)
        {
            if (hit == null)
            {
                throw new ArgumentNullException(nameof(hit));
            }

            var movingPiece = selectedPieceId == null ? null : FindPiece(layout, selectedPieceId);
            var type = movingPiece?.Type ?? activeTool;
            if (!type.HasValue)
            {
                return new BuildPreview(false, null, null, null, string.Empty);
            }

            if (type == PieceType.Pier)
            {
                if (hit.Kind != BuildHitKind.Foundation)
                {
                    return InvalidPreview(type.Value, "Piers can only go on a foundation.", null, null);
                }

                var foundation = ElementAtOrDefault(GetFoundationSlots(), hit.FoundationIndex);
                var occupiedByOther = foundation != null &&
                                      foundation.PierPieceId != null &&
                                      foundation.PierPieceId != movingPiece?.Id;
                var hasInventory = movingPiece != null || GetInventoryRemaining().Pier > 0;
                return new BuildPreview(
                    foundation != null && !occupiedByOther && hasInventory,
                    type,
                    null,
                    hit.FoundationIndex,
                    occupiedByOther ? "This foundation is already occupied." : hasInventory ? string.Empty : "No piers left.");
            }

            if (hit.Kind != BuildHitKind.Road && hit.Kind != BuildHitKind.Truss)
            {
                return InvalidPreview(type.Value, "Select a bridge grid cell.", null, null);
            }

            if (type == PieceType.Truss)
            {
                if (hit.Kind != BuildHitKind.Truss)
                {
                    return InvalidPreview(type.Value, "Trusses must go under a deck.", null, null);
                }

                var road = layout.Roads.FirstOrDefault(candidate => candidate.Id == hit.RoadPieceId);
                var existing = road == null
                    ? null
                    : layout.Trusses.FirstOrDefault(candidate => candidate.RoadPieceId == road.Id);
                var occupiedByOther = existing != null && existing.Id != movingPiece?.Id;
                var hasInventory = movingPiece != null || GetInventoryRemaining().Truss > 0;
                var valid = road != null && !occupiedByOther && hasInventory;
                var message = road == null
                    ? "Trusses must go under a deck."
                    : occupiedByOther
                        ? "This deck already has a truss."
                        : hasInventory
                            ? string.Empty
                            : "No trusses left.";
                return new BuildPreview(
                    valid,
                    type,
                    null,
                    null,
                    message,
                    road?.Id,
                    road?.From,
                    road?.To);
            }

            if (hit.Kind != BuildHitKind.Road)
            {
                return InvalidPreview(type.Value, "Select a bridge grid cell.", null, null);
            }

            var slotIndex = hit.SlotIndex;
            var slot = ElementAtOrDefault(GetRoadSlots(), slotIndex);
            if (slot == null)
            {
                return InvalidPreview(type.Value, "Outside the buildable grid.", slotIndex, null);
            }

            var occupiedRoad = slot.RoadPieceId != null && slot.RoadPieceId != movingPiece?.Id;
            var orientationMatchesEdge = slot.Axis == RoadAxis.X
                ? rotationQuarterTurns % 2 == 0
                : rotationQuarterTurns % 2 == 1;
            var forwardOrientation = rotationQuarterTurns == 0 || rotationQuarterTurns == 1;
            GridPoint from;
            GridPoint to;
            if (level.BuildVolume != null || orientationMatchesEdge && forwardOrientation)
            {
                from = slot.From;
                to = slot.To;
            }
            else
            {
                from = slot.To;
                to = slot.From;
            }

            var expectedDelta = to.Y - from.Y;
            var geometryMatches = type == PieceType.Deck ? expectedDelta == 0 : Math.Abs(expectedDelta) == 1;
            var forwardX = level.GoalAnchor.X - level.StartAnchor.X;
            var forwardZ = level.GoalAnchor.Z - level.StartAnchor.Z;
            var directionMatchesPath = level.BuildVolume != null ||
                                       (to.X - from.X) * forwardX + (to.Z - from.Z) * forwardZ > 0;
            var hasRoadInventory = movingPiece != null || GetInventoryRemaining()[type.Value] > 0;
            var validRoad = slot.InBounds &&
                            orientationMatchesEdge &&
                            !occupiedRoad &&
                            geometryMatches &&
                            (type == PieceType.Deck || directionMatchesPath) &&
                            hasRoadInventory;

            string roadMessage;
            if (!slot.InBounds)
            {
                roadMessage = "Outside the buildable grid.";
            }
            else if (!orientationMatchesEdge)
            {
                roadMessage = "Piece rotation doesn't match this grid edge.";
            }
            else if (occupiedRoad)
            {
                roadMessage = "This grid edge is already occupied by a deck.";
            }
            else if (!geometryMatches)
            {
                roadMessage = type == PieceType.Ramp
                    ? "Ramps must span a one-level height difference."
                    : "Decks can only link nodes at the same height.";
            }
            else if (type == PieceType.Ramp && !directionMatchesPath)
            {
                roadMessage = "The ramp direction doesn't match the road direction.";
            }
            else
            {
                roadMessage = hasRoadInventory
                    ? string.Empty
                    : type == PieceType.Deck ? "No decks left." : "No ramps left.";
            }

            return new BuildPreview(validRoad, type, slotIndex, null, roadMessage, from: from, to: to);
        }

        public BuildActionResult HandleHit(BuildHit hit)
        {
            if (hit == null)
            {
                throw new ArgumentNullException(nameof(hit));
            }

            var hitPieceId = hit.PieceId;
            if (hitPieceId != null && FindPiece(layout, hitPieceId) != null && !activeTool.HasValue)
            {
                selectedPieceId = hitPieceId;
                var selected = FindPiece(layout, hitPieceId);
                if (selected is RoadPiecePlacement selectedRoad)
                {
                    if (level.BuildVolume != null)
                    {
                        activeLayer = selectedRoad.From.Y;
                        if (selectedRoad.Type == PieceType.Ramp)
                        {
                            rampDelta = selectedRoad.To.Y > selectedRoad.From.Y ? 1 : -1;
                        }
                    }

                    AlignRotationToEdge(selectedRoad.From, selectedRoad.To);
                }
                else if (selected is TrussPlacement selectedTruss)
                {
                    var road = layout.Roads.FirstOrDefault(candidate => candidate.Id == selectedTruss.RoadPieceId);
                    if (road != null)
                    {
                        if (level.BuildVolume != null)
                        {
                            activeLayer = road.From.Y;
                        }

                        AlignRotationToEdge(road.From, road.To);
                    }
                }
                else if (selected is PierPlacement selectedPier && level.BuildVolume != null)
                {
                    activeLayer = selectedPier.Top.Y;
                }

                return new BuildActionResult(false, hitPieceId, "Piece selected.", true);
            }

            var preview = Preview(hit);
            if (!preview.Type.HasValue)
            {
                selectedPieceId = null;
                return new BuildActionResult(false, null, string.Empty, true);
            }

            if (!preview.Valid)
            {
                return new BuildActionResult(false, selectedPieceId, preview.Message, false);
            }

            var moving = selectedPieceId == null ? null : FindPiece(layout, selectedPieceId);
            if (preview.Type == PieceType.Pier && preview.FoundationIndex.HasValue)
            {
                PlacePier(preview.FoundationIndex.Value, moving);
            }
            else if (preview.Type == PieceType.Truss && preview.RoadPieceId != null)
            {
                PlaceTruss(preview.RoadPieceId, moving);
            }
            else if ((preview.Type == PieceType.Deck || preview.Type == PieceType.Ramp) &&
                     preview.SlotIndex.HasValue)
            {
                PlaceRoad(preview.From, preview.To, preview.Type.Value, moving);
            }

            selectedPieceId = null;
            return new BuildActionResult(true, null, "Piece placed.", true);
        }

        public int GetPieceCost(PieceType type)
        {
            return LevelCatalog.GetPieceCost(type);
        }

        private void PlaceRoad(GridPoint? from, GridPoint? to, PieceType type, PiecePlacement moving)
        {
            if (!from.HasValue || !to.HasValue)
            {
                return;
            }

            var movingRoad = moving as RoadPiecePlacement;
            var id = movingRoad != null ? movingRoad.Id : $"{PieceTypeToken(type)}-{idCounter++}";
            var road = new RoadPiecePlacement(id, type, from.Value, to.Value);
            var roads = layout.Roads.Where(candidate => candidate.Id != id).Concat(new[] { road });
            var trusses = layout.Trusses.Where(truss => truss.RoadPieceId != id);
            Commit(new BridgeLayout(roads, layout.Piers, trusses));

            if (level.BuildVolume != null && type == PieceType.Ramp)
            {
                activeLayer = to.Value.Y;
                KeepRampDeltaInBounds();
            }
        }

        private void KeepRampDeltaInBounds()
        {
            var volume = level.BuildVolume;
            if (volume == null)
            {
                return;
            }

            if (activeLayer <= volume.Min.Y)
            {
                rampDelta = 1;
            }
            else if (activeLayer >= volume.Max.Y)
            {
                rampDelta = -1;
            }
        }

        private void PlaceTruss(string roadPieceId, PiecePlacement moving)
        {
            if (!layout.Roads.Any(road => road.Id == roadPieceId))
            {
                return;
            }

            var movingTruss = moving as TrussPlacement;
            var id = movingTruss != null ? movingTruss.Id : $"truss-{idCounter++}";
            var truss = new TrussPlacement(id, roadPieceId);
            Commit(new BridgeLayout(
                layout.Roads,
                layout.Piers,
                layout.Trusses.Where(candidate => candidate.Id != id).Concat(new[] { truss })));
        }

        private void PlacePier(int foundationIndex, PiecePlacement moving)
        {
            var slot = ElementAtOrDefault(GetFoundationSlots(), foundationIndex);
            if (slot == null)
            {
                return;
            }

            var movingPier = moving as PierPlacement;
            var id = movingPier != null ? movingPier.Id : $"pier-{idCounter++}";
            var pier = new PierPlacement(id, slot.Top, slot.Foundation);
            Commit(new BridgeLayout(
                layout.Roads,
                layout.Piers.Where(candidate => candidate.Id != id).Concat(new[] { pier }),
                layout.Trusses));
        }

        private void Commit(BridgeLayout next)
        {
            var snapshot = next.DeepClone();
            history = history.Take(historyIndex + 1).Concat(new[] { snapshot }).ToList();
            if (history.Count > 51)
            {
                history = history.Skip(history.Count - 51).ToList();
            }

            historyIndex = history.Count - 1;
            layout = snapshot.DeepClone();
        }

        private BuildPreview InvalidPreview(
            PieceType type,
            string message,
            int? slotIndex,
            int? foundationIndex)
        {
            return new BuildPreview(false, type, slotIndex, foundationIndex, message);
        }

        private IReadOnlyList<GridPoint> GetReferenceNodes(LevelDefinition definition)
        {
            var path = BridgeRules.FindRoadPath(
                definition.ReferenceSolution.Layout.Roads,
                definition.StartAnchor,
                definition.GoalAnchor);
            if (path == null)
            {
                throw new InvalidOperationException($"{definition.Id} reference solution has no road path.");
            }

            return path.Nodes;
        }

        private void AlignRotationToEdge(GridPoint from, GridPoint to)
        {
            var dx = to.X - from.X;
            var dz = to.Z - from.Z;
            for (var index = 0; index < Directions.Length; index++)
            {
                if (Directions[index].X == dx && Directions[index].Z == dz)
                {
                    rotationQuarterTurns = index;
                    return;
                }
            }
        }

        private IReadOnlyList<BuildEdge> GetBuildEdges()
        {
            var minX = Math.Min(level.StartAnchor.X, level.GoalAnchor.X);
            var maxX = Math.Max(level.StartAnchor.X, level.GoalAnchor.X);
            var centerZ = level.StartAnchor.Z;
            var edges = new List<BuildEdge>();

            for (var x = minX; x < maxX; x++)
            {
                edges.Add(new BuildEdge(Node(x, centerZ), Node(x + 1, centerZ), RoadAxis.X));
            }

            foreach (var zOffset in new[] { -1, 1 })
            {
                for (var x = minX; x < maxX; x++)
                {
                    edges.Add(new BuildEdge(
                        Node(x, centerZ + zOffset),
                        Node(x + 1, centerZ + zOffset),
                        RoadAxis.X));
                }
            }

            foreach (var zOffset in new[] { -1, 0 })
            {
                for (var x = minX; x <= maxX; x++)
                {
                    edges.Add(new BuildEdge(
                        Node(x, centerZ + zOffset),
                        Node(x, centerZ + zOffset + 1),
                        RoadAxis.Z));
                }
            }

            return edges;
        }

        private IReadOnlyList<BuildEdge> GetVolumeBuildEdges()
        {
            var volume = level.BuildVolume;
            if (volume == null)
            {
                return Array.Empty<BuildEdge>();
            }

            var placementType = GetPlacementType();
            var verticalDelta = placementType == PieceType.Ramp ? rampDelta : 0;
            var targetLayer = activeLayer + verticalDelta;
            if (targetLayer < volume.Min.Y || targetLayer > volume.Max.Y)
            {
                return Array.Empty<BuildEdge>();
            }

            var direction = Directions[rotationQuarterTurns];
            var axis = direction.X == 0 ? RoadAxis.Z : RoadAxis.X;
            var edges = new List<BuildEdge>();
            for (var z = volume.Min.Z; z <= volume.Max.Z; z++)
            {
                for (var x = volume.Min.X; x <= volume.Max.X; x++)
                {
                    var nextX = x + direction.X;
                    var nextZ = z + direction.Z;
                    if (nextX < volume.Min.X ||
                        nextX > volume.Max.X ||
                        nextZ < volume.Min.Z ||
                        nextZ > volume.Max.Z)
                    {
                        continue;
                    }

                    edges.Add(new BuildEdge(
                        new GridPoint(x, activeLayer, z),
                        new GridPoint(nextX, targetLayer, nextZ),
                        axis));
                }
            }

            return edges;
        }

        private GridPoint Node(int x, int z)
        {
            return new GridPoint(x, GetElevationAtX(x), z);
        }

        private int GetElevationAtX(int x)
        {
            foreach (var node in referenceNodes)
            {
                if (node.X == x)
                {
                    return node.Y;
                }
            }

            return level.StartAnchor.Y;
        }

        private static Dictionary<PieceType, int> CountPieces(BridgeLayout source)
        {
            return new Dictionary<PieceType, int>
            {
                [PieceType.Deck] = source.Roads.Count(piece => piece.Type == PieceType.Deck),
                [PieceType.Ramp] = source.Roads.Count(piece => piece.Type == PieceType.Ramp),
                [PieceType.Pier] = source.Piers.Count,
                [PieceType.Truss] = source.Trusses.Count,
            };
        }

        private static PiecePlacement FindPiece(BridgeLayout source, string pieceId)
        {
            return source.AllPieces().FirstOrDefault(piece => piece.Id == pieceId);
        }

        private static string RoadEdgeKey(GridPoint from, GridPoint to)
        {
            var fromKey = BridgeRules.GridPointKey(from);
            var toKey = BridgeRules.GridPointKey(to);
            return string.CompareOrdinal(fromKey, toKey) <= 0
                ? $"{fromKey}|{toKey}"
                : $"{toKey}|{fromKey}";
        }

        private static bool SameRoadEdge(RoadPiecePlacement road, GridPoint from, GridPoint to)
        {
            return RoadEdgeKey(road.From, road.To) == RoadEdgeKey(from, to);
        }

        private static T ElementAtOrDefault<T>(IReadOnlyList<T> values, int index) where T : class
        {
            return index >= 0 && index < values.Count ? values[index] : null;
        }

        private static string PieceTypeToken(PieceType type)
        {
            switch (type)
            {
                case PieceType.Deck:
                    return "deck";
                case PieceType.Ramp:
                    return "ramp";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Only road pieces have road ids.");
            }
        }

        private readonly struct GridDirection
        {
            public GridDirection(int x, int z)
            {
                X = x;
                Z = z;
            }

            public int X { get; }
            public int Z { get; }
        }

        private readonly struct BuildEdge
        {
            public BuildEdge(GridPoint from, GridPoint to, RoadAxis axis)
            {
                From = from;
                To = to;
                Axis = axis;
            }

            public GridPoint From { get; }
            public GridPoint To { get; }
            public RoadAxis Axis { get; }
        }
    }
}
