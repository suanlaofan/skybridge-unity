using System;
using System.Collections.Generic;
using System.Linq;

namespace Skybridge.Core
{
    public enum LevelId
    {
        Level1 = 1,
        Level2 = 2,
        Level3 = 3,
        Level4 = 4,
        Level5 = 5,
    }

    public enum PieceType
    {
        Deck,
        Ramp,
        Pier,
        Truss,
    }

    public enum Medal
    {
        None,
        Bronze,
        Silver,
        Gold,
    }

    public enum ValidationIssueCode
    {
        DuplicatePieceId,
        InventoryExceeded,
        UnsnappedRoad,
        InvalidDeckGeometry,
        InvalidRampGeometry,
        DisconnectedPath,
        WrongRampDirection,
        SharpTurn,
        ClearanceBlocked,
    }

    public enum HardSupportKind
    {
        Start,
        Pier,
        Goal,
    }

    public enum BuildCommandKind
    {
        Place,
        Remove,
        Move,
    }

    public enum GamePhase
    {
        LevelLoading,
        Build,
        Validating,
        TestPreparing,
        VehicleRunning,
        Breaking,
        VehicleFalling,
        RestoreSnapshot,
        Success,
    }

    public enum TestOutcome
    {
        Pending,
        Success,
        Failure,
        RuntimeError,
    }

    public enum BuildHitKind
    {
        Road,
        Truss,
        Foundation,
        Piece,
        Empty,
    }

    public enum RoadAxis
    {
        X,
        Z,
    }

    [Serializable]
    public readonly struct GridPoint : IEquatable<GridPoint>, IComparable<GridPoint>
    {
        public GridPoint(int x, int y, int z = 0)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public int X { get; }
        public int Y { get; }
        public int Z { get; }

        public string Key => $"{X},{Y},{Z}";

        public bool Equals(GridPoint other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is GridPoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = X;
                hashCode = (hashCode * 397) ^ Y;
                hashCode = (hashCode * 397) ^ Z;
                return hashCode;
            }
        }

        public int CompareTo(GridPoint other)
        {
            var xComparison = X.CompareTo(other.X);
            if (xComparison != 0)
            {
                return xComparison;
            }

            var yComparison = Y.CompareTo(other.Y);
            return yComparison != 0 ? yComparison : Z.CompareTo(other.Z);
        }

        public override string ToString()
        {
            return Key;
        }

        public static bool operator ==(GridPoint left, GridPoint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GridPoint left, GridPoint right)
        {
            return !left.Equals(right);
        }
    }

    public sealed class BuildVolume
    {
        public BuildVolume(GridPoint min, GridPoint max, int pierFoundationY)
        {
            Min = min;
            Max = max;
            PierFoundationY = pierFoundationY;
        }

        public GridPoint Min { get; }
        public GridPoint Max { get; }
        public int PierFoundationY { get; }
    }

    public sealed class PieceInventory : IEquatable<PieceInventory>
    {
        public PieceInventory(int deck, int ramp, int pier, int truss)
        {
            Deck = deck;
            Ramp = ramp;
            Pier = pier;
            Truss = truss;
        }

        public int Deck { get; }
        public int Ramp { get; }
        public int Pier { get; }
        public int Truss { get; }

        public int this[PieceType type]
        {
            get
            {
                switch (type)
                {
                    case PieceType.Deck:
                        return Deck;
                    case PieceType.Ramp:
                        return Ramp;
                    case PieceType.Pier:
                        return Pier;
                    case PieceType.Truss:
                        return Truss;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(type), type, null);
                }
            }
        }

        public PieceInventory Clone()
        {
            return new PieceInventory(Deck, Ramp, Pier, Truss);
        }

        public bool Equals(PieceInventory other)
        {
            return other != null && Deck == other.Deck && Ramp == other.Ramp && Pier == other.Pier && Truss == other.Truss;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PieceInventory);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Deck;
                hashCode = (hashCode * 397) ^ Ramp;
                hashCode = (hashCode * 397) ^ Pier;
                hashCode = (hashCode * 397) ^ Truss;
                return hashCode;
            }
        }
    }

    public abstract class PiecePlacement
    {
        protected PiecePlacement(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("A piece id is required.", nameof(id));
            }

            Id = id;
        }

        public string Id { get; }
        public abstract PieceType Type { get; }
        public abstract PiecePlacement DeepClone();
    }

    public sealed class RoadPiecePlacement : PiecePlacement
    {
        public RoadPiecePlacement(
            string id,
            PieceType type,
            GridPoint from,
            GridPoint to,
            bool snapped = true)
            : base(id)
        {
            if (type != PieceType.Deck && type != PieceType.Ramp)
            {
                throw new ArgumentException("A road must be a deck or ramp.", nameof(type));
            }

            Type = type;
            From = from;
            To = to;
            Snapped = snapped;
        }

        public override PieceType Type { get; }
        public GridPoint From { get; }
        public GridPoint To { get; }
        public bool Snapped { get; }

        public override PiecePlacement DeepClone()
        {
            return new RoadPiecePlacement(Id, Type, From, To, Snapped);
        }
    }

    public sealed class PierPlacement : PiecePlacement
    {
        public PierPlacement(
            string id,
            GridPoint top,
            GridPoint foundation,
            bool snappedTop = true,
            bool snappedBase = true)
            : base(id)
        {
            Top = top;
            Foundation = foundation;
            SnappedTop = snappedTop;
            SnappedBase = snappedBase;
        }

        public override PieceType Type => PieceType.Pier;
        public GridPoint Top { get; }
        public GridPoint Foundation { get; }
        public bool SnappedTop { get; }
        public bool SnappedBase { get; }

        public override PiecePlacement DeepClone()
        {
            return new PierPlacement(Id, Top, Foundation, SnappedTop, SnappedBase);
        }
    }

    public sealed class TrussPlacement : PiecePlacement
    {
        public TrussPlacement(
            string id,
            string roadPieceId,
            bool snapped = true,
            bool connectedAtFrom = true,
            bool connectedAtTo = true)
            : base(id)
        {
            RoadPieceId = roadPieceId ?? throw new ArgumentNullException(nameof(roadPieceId));
            Snapped = snapped;
            ConnectedAtFrom = connectedAtFrom;
            ConnectedAtTo = connectedAtTo;
        }

        public override PieceType Type => PieceType.Truss;
        public string RoadPieceId { get; }
        public bool Snapped { get; }
        public bool ConnectedAtFrom { get; }
        public bool ConnectedAtTo { get; }

        public override PiecePlacement DeepClone()
        {
            return new TrussPlacement(Id, RoadPieceId, Snapped, ConnectedAtFrom, ConnectedAtTo);
        }
    }

    public sealed class BridgeLayout
    {
        public BridgeLayout(
            IEnumerable<RoadPiecePlacement> roads = null,
            IEnumerable<PierPlacement> piers = null,
            IEnumerable<TrussPlacement> trusses = null)
        {
            Roads = (roads ?? Enumerable.Empty<RoadPiecePlacement>()).ToList();
            Piers = (piers ?? Enumerable.Empty<PierPlacement>()).ToList();
            Trusses = (trusses ?? Enumerable.Empty<TrussPlacement>()).ToList();
        }

        public IReadOnlyList<RoadPiecePlacement> Roads { get; }
        public IReadOnlyList<PierPlacement> Piers { get; }
        public IReadOnlyList<TrussPlacement> Trusses { get; }

        public static BridgeLayout Empty => new BridgeLayout();

        public BridgeLayout DeepClone()
        {
            return new BridgeLayout(
                Roads.Select(road => (RoadPiecePlacement)road.DeepClone()),
                Piers.Select(pier => (PierPlacement)pier.DeepClone()),
                Trusses.Select(truss => (TrussPlacement)truss.DeepClone()));
        }

        public IEnumerable<PiecePlacement> AllPieces()
        {
            return Roads.Cast<PiecePlacement>().Concat(Piers).Concat(Trusses);
        }
    }

    public sealed class MedalThresholds
    {
        public MedalThresholds(int gold, int silver)
        {
            Gold = gold;
            Silver = silver;
        }

        public int Gold { get; }
        public int Silver { get; }
    }

    public sealed class ReferenceSolution
    {
        public ReferenceSolution(int cost, BridgeLayout layout)
        {
            Cost = cost;
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        public int Cost { get; }
        public BridgeLayout Layout { get; }
    }

    public sealed class LevelDefinition
    {
        public LevelDefinition(
            LevelId id,
            int number,
            string name,
            string objective,
            GridPoint startAnchor,
            GridPoint goalAnchor,
            BuildVolume buildVolume,
            bool allowTurns,
            IEnumerable<GridPoint> foundationPads,
            PieceInventory inventory,
            MedalThresholds medalThresholds,
            int referenceCost,
            int theoreticalMinimumCost,
            ReferenceSolution referenceSolution)
        {
            Id = id;
            Number = number;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Objective = objective ?? throw new ArgumentNullException(nameof(objective));
            StartAnchor = startAnchor;
            GoalAnchor = goalAnchor;
            BuildVolume = buildVolume;
            AllowTurns = allowTurns;
            FoundationPads = (foundationPads ?? Enumerable.Empty<GridPoint>()).ToList();
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            MedalThresholds = medalThresholds ?? throw new ArgumentNullException(nameof(medalThresholds));
            ReferenceCost = referenceCost;
            TheoreticalMinimumCost = theoreticalMinimumCost;
            ReferenceSolution = referenceSolution ?? throw new ArgumentNullException(nameof(referenceSolution));
        }

        public LevelId Id { get; }
        public int Number { get; }
        public string Name { get; }
        public string Objective { get; }
        public GridPoint StartAnchor { get; }
        public GridPoint GoalAnchor { get; }
        public BuildVolume BuildVolume { get; }
        public bool AllowTurns { get; }
        public IReadOnlyList<GridPoint> FoundationPads { get; }
        public PieceInventory Inventory { get; }
        public MedalThresholds MedalThresholds { get; }
        public int ReferenceCost { get; }
        public int TheoreticalMinimumCost { get; }
        public ReferenceSolution ReferenceSolution { get; }
    }

    public sealed class RoadPathSegment
    {
        public RoadPathSegment(RoadPiecePlacement piece, GridPoint from, GridPoint to, bool reversed)
        {
            Piece = piece;
            From = from;
            To = to;
            Reversed = reversed;
        }

        public RoadPiecePlacement Piece { get; }
        public GridPoint From { get; }
        public GridPoint To { get; }
        public bool Reversed { get; }
    }

    public sealed class RoadPath
    {
        public RoadPath(IEnumerable<GridPoint> nodes, IEnumerable<RoadPathSegment> segments)
        {
            Nodes = nodes.ToList();
            Segments = segments.ToList();
        }

        public IReadOnlyList<GridPoint> Nodes { get; }
        public IReadOnlyList<RoadPathSegment> Segments { get; }
    }

    public sealed class ValidationIssue
    {
        public ValidationIssue(ValidationIssueCode code, string message, string pieceId = null, GridPoint? point = null)
        {
            Code = code;
            Message = message;
            PieceId = pieceId;
            Point = point;
        }

        public ValidationIssueCode Code { get; }
        public string Message { get; }
        public string PieceId { get; }
        public GridPoint? Point { get; }
    }

    public sealed class BridgeValidationResult
    {
        public BridgeValidationResult(bool canTest, IEnumerable<ValidationIssue> issues, RoadPath path)
        {
            CanTest = canTest;
            Issues = issues.ToList();
            Path = path;
        }

        public bool CanTest { get; }
        public IReadOnlyList<ValidationIssue> Issues { get; }
        public RoadPath Path { get; }
    }

    public sealed class HardSupport
    {
        public HardSupport(HardSupportKind kind, GridPoint node, int pathNodeIndex, string pierId = null)
        {
            Kind = kind;
            Node = node;
            PathNodeIndex = pathNodeIndex;
            PierId = pierId;
        }

        public HardSupportKind Kind { get; }
        public GridPoint Node { get; }
        public int PathNodeIndex { get; }
        public string PierId { get; }
    }

    public sealed class WeakPoint
    {
        public WeakPoint(
            int spanIndex,
            int pathNodeIndex,
            GridPoint joint,
            string beforePieceId,
            string afterPieceId)
        {
            SpanIndex = spanIndex;
            PathNodeIndex = pathNodeIndex;
            Joint = joint;
            BeforePieceId = beforePieceId;
            AfterPieceId = afterPieceId;
        }

        public int SpanIndex { get; }
        public int PathNodeIndex { get; }
        public GridPoint Joint { get; }
        public string BeforePieceId { get; }
        public string AfterPieceId { get; }
    }

    public sealed class SpanResult
    {
        public SpanResult(
            int index,
            HardSupport startSupport,
            HardSupport endSupport,
            int pathStartIndex,
            int pathEndIndex,
            IEnumerable<string> roadPieceIds,
            int length,
            bool fullyTrussed,
            int safeLimit,
            bool safe,
            WeakPoint weakPoint)
        {
            Index = index;
            StartSupport = startSupport;
            EndSupport = endSupport;
            PathStartIndex = pathStartIndex;
            PathEndIndex = pathEndIndex;
            RoadPieceIds = roadPieceIds.ToList();
            Length = length;
            FullyTrussed = fullyTrussed;
            SafeLimit = safeLimit;
            Safe = safe;
            WeakPoint = weakPoint;
        }

        public int Index { get; }
        public HardSupport StartSupport { get; }
        public HardSupport EndSupport { get; }
        public int PathStartIndex { get; }
        public int PathEndIndex { get; }
        public IReadOnlyList<string> RoadPieceIds { get; }
        public int Length { get; }
        public bool FullyTrussed { get; }
        public int SafeLimit { get; }
        public bool Safe { get; }
        public WeakPoint WeakPoint { get; }
    }

    public sealed class SpanAnalysisResult
    {
        public SpanAnalysisResult(
            BridgeValidationResult validation,
            IEnumerable<HardSupport> hardSupports,
            IEnumerable<SpanResult> spans,
            IEnumerable<WeakPoint> weakPoints)
        {
            Validation = validation;
            HardSupports = hardSupports.ToList();
            Spans = spans.ToList();
            WeakPoints = weakPoints.ToList();
        }

        public BridgeValidationResult Validation { get; }
        public IReadOnlyList<HardSupport> HardSupports { get; }
        public IReadOnlyList<SpanResult> Spans { get; }
        public IReadOnlyList<WeakPoint> WeakPoints { get; }
    }

    public sealed class BuildCommand
    {
        public BuildCommand(BuildCommandKind kind, string pieceId, PiecePlacement before, PiecePlacement after)
        {
            Kind = kind;
            PieceId = pieceId;
            Before = before;
            After = after;
        }

        public BuildCommandKind Kind { get; }
        public string PieceId { get; }
        public PiecePlacement Before { get; }
        public PiecePlacement After { get; }

        public BuildCommand DeepClone()
        {
            return new BuildCommand(Kind, PieceId, Before?.DeepClone(), After?.DeepClone());
        }
    }

    public sealed class BuildSessionState
    {
        public BuildSessionState(
            BridgeLayout layout,
            PieceInventory inventoryRemaining,
            IEnumerable<BuildCommand> undoStack,
            IEnumerable<BuildCommand> redoStack)
        {
            Layout = layout;
            InventoryRemaining = inventoryRemaining;
            UndoStack = undoStack.ToList();
            RedoStack = redoStack.ToList();
        }

        public BridgeLayout Layout { get; }
        public PieceInventory InventoryRemaining { get; }
        public IReadOnlyList<BuildCommand> UndoStack { get; }
        public IReadOnlyList<BuildCommand> RedoStack { get; }
    }

    public readonly struct TestOutcomeSignals
    {
        public TestOutcomeSignals(bool entireVehicleInGoal, bool failureZoneEntered, bool stalledForThreeSeconds = false)
        {
            EntireVehicleInGoal = entireVehicleInGoal;
            FailureZoneEntered = failureZoneEntered;
            StalledForThreeSeconds = stalledForThreeSeconds;
        }

        public bool EntireVehicleInGoal { get; }
        public bool FailureZoneEntered { get; }
        public bool StalledForThreeSeconds { get; }
    }

    public sealed class LevelProgress
    {
        public LevelProgress(Medal highestMedal, double? lowestCost)
        {
            HighestMedal = highestMedal;
            LowestCost = lowestCost;
        }

        public Medal HighestMedal { get; }
        public double? LowestCost { get; }

        public LevelProgress Clone()
        {
            return new LevelProgress(HighestMedal, LowestCost);
        }
    }

    public sealed class SaveDataV1
    {
        public SaveDataV1(IDictionary<LevelId, LevelProgress> levels)
        {
            Version = 1;
            Levels = new Dictionary<LevelId, LevelProgress>(levels);
        }

        public int Version { get; }
        public IReadOnlyDictionary<LevelId, LevelProgress> Levels { get; }
    }

    public interface IKeyValueStorage
    {
        string GetItem(string key);
        void SetItem(string key, string value);
        void RemoveItem(string key);
    }

    public sealed class BuildHit
    {
        private BuildHit(BuildHitKind kind, int slotIndex, int foundationIndex, string roadPieceId, string pieceId)
        {
            Kind = kind;
            SlotIndex = slotIndex;
            FoundationIndex = foundationIndex;
            RoadPieceId = roadPieceId;
            PieceId = pieceId;
        }

        public BuildHitKind Kind { get; }
        public int SlotIndex { get; }
        public int FoundationIndex { get; }
        public string RoadPieceId { get; }
        public string PieceId { get; }

        public static BuildHit Road(int slotIndex, string pieceId = null)
        {
            return new BuildHit(BuildHitKind.Road, slotIndex, -1, null, pieceId);
        }

        public static BuildHit Truss(string roadPieceId, string pieceId = null)
        {
            return new BuildHit(BuildHitKind.Truss, -1, -1, roadPieceId, pieceId);
        }

        public static BuildHit Foundation(int foundationIndex, string pieceId = null)
        {
            return new BuildHit(BuildHitKind.Foundation, -1, foundationIndex, null, pieceId);
        }

        public static BuildHit Piece(string pieceId)
        {
            return new BuildHit(BuildHitKind.Piece, -1, -1, null, pieceId);
        }

        public static BuildHit Empty()
        {
            return new BuildHit(BuildHitKind.Empty, -1, -1, null, null);
        }
    }

    public sealed class RoadSlot
    {
        public RoadSlot(
            int index,
            GridPoint from,
            GridPoint to,
            RoadAxis axis,
            bool inBounds,
            string roadPieceId,
            string trussPieceId)
        {
            Index = index;
            From = from;
            To = to;
            Axis = axis;
            InBounds = inBounds;
            RoadPieceId = roadPieceId;
            TrussPieceId = trussPieceId;
        }

        public int Index { get; }
        public GridPoint From { get; }
        public GridPoint To { get; }
        public RoadAxis Axis { get; }
        public bool InBounds { get; }
        public string RoadPieceId { get; }
        public string TrussPieceId { get; }
    }

    public sealed class FoundationSlot
    {
        public FoundationSlot(int index, GridPoint foundation, GridPoint top, string pierPieceId)
        {
            Index = index;
            Foundation = foundation;
            Top = top;
            PierPieceId = pierPieceId;
        }

        public int Index { get; }
        public GridPoint Foundation { get; }
        public GridPoint Top { get; }
        public string PierPieceId { get; }
    }

    public readonly struct LayerBounds
    {
        public LayerBounds(int min, int max)
        {
            Min = min;
            Max = max;
        }

        public int Min { get; }
        public int Max { get; }
    }

    public sealed class BuildPreview
    {
        public BuildPreview(
            bool valid,
            PieceType? type,
            int? slotIndex,
            int? foundationIndex,
            string message,
            string roadPieceId = null,
            GridPoint? from = null,
            GridPoint? to = null)
        {
            Valid = valid;
            Type = type;
            SlotIndex = slotIndex;
            FoundationIndex = foundationIndex;
            RoadPieceId = roadPieceId;
            From = from;
            To = to;
            Message = message;
        }

        public bool Valid { get; }
        public PieceType? Type { get; }
        public int? SlotIndex { get; }
        public int? FoundationIndex { get; }
        public string RoadPieceId { get; }
        public GridPoint? From { get; }
        public GridPoint? To { get; }
        public string Message { get; }
    }

    public sealed class BuildActionResult
    {
        public BuildActionResult(bool changed, string selectedPieceId, string message, bool valid)
        {
            Changed = changed;
            SelectedPieceId = selectedPieceId;
            Message = message;
            Valid = valid;
        }

        public bool Changed { get; }
        public string SelectedPieceId { get; }
        public string Message { get; }
        public bool Valid { get; }
    }
}
