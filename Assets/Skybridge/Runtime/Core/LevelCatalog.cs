using System;
using System.Collections.Generic;
using System.Linq;

namespace Skybridge.Core
{
    public static class LevelCatalog
    {
        private static readonly IReadOnlyDictionary<PieceType, int> Costs =
            new Dictionary<PieceType, int>
            {
                { PieceType.Deck, 1 },
                { PieceType.Ramp, 1 },
                { PieceType.Pier, 2 },
                { PieceType.Truss, 1 },
            };

        private static readonly IReadOnlyList<LevelDefinition> Definitions = CreateDefinitions();

        public static IReadOnlyList<LevelId> LevelIds { get; } = new[]
        {
            LevelId.Level1,
            LevelId.Level2,
            LevelId.Level3,
            LevelId.Level4,
            LevelId.Level5,
        };

        public static IReadOnlyList<LevelDefinition> All => Definitions;

        public static int GetPieceCost(PieceType type)
        {
            return Costs[type];
        }

        public static LevelDefinition Get(LevelId id)
        {
            var level = Definitions.FirstOrDefault(candidate => candidate.Id == id);
            if (level == null)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown level.");
            }

            return level;
        }

        private static IReadOnlyList<LevelDefinition> CreateDefinitions()
        {
            var level1Roads = MakeFlatRoads(3);
            var level2Roads = MakeFlatRoads(6);
            var level3Roads = new List<RoadPiecePlacement>
            {
                MakeRoad("deck-1", PieceType.Deck, Point(0, 0), Point(1, 0)),
                MakeRoad("deck-2", PieceType.Deck, Point(1, 0), Point(2, 0)),
                MakeRoad("ramp-1", PieceType.Ramp, Point(2, 0), Point(3, 1)),
                MakeRoad("deck-3", PieceType.Deck, Point(3, 1), Point(4, 1)),
            };
            var level4Roads = MakeFlatRoads(5);
            var level5Roads = new List<RoadPiecePlacement>();
            for (var index = 0; index < 10; index++)
            {
                level5Roads.Add(MakeRoad(
                    $"ramp-{index + 1}",
                    PieceType.Ramp,
                    Point(index, index, index),
                    Point(index + 1, index + 1, index)));
                level5Roads.Add(MakeRoad(
                    $"deck-{index + 1}",
                    PieceType.Deck,
                    Point(index + 1, index + 1, index),
                    Point(index + 1, index + 1, index + 1)));
            }

            var level5PathNodes = new List<GridPoint> { Point(0, 0, 0) };
            level5PathNodes.AddRange(level5Roads.Select(road => road.To));
            var level5PierNodeIndices = new[] { 3, 6, 9, 12, 15, 18 };
            var level5Piers = level5PierNodeIndices.Select((pathNodeIndex, index) =>
            {
                var top = level5PathNodes[pathNodeIndex];
                return new PierPlacement(
                    $"pier-{index + 1}",
                    top,
                    Point(top.X, -2, top.Z));
            }).ToList();

            var level1Layout = new BridgeLayout(level1Roads);
            var level2Layout = new BridgeLayout(
                level2Roads,
                new[] { new PierPlacement("pier-1", Point(3, 0), Point(3, -2)) });
            var level3Layout = new BridgeLayout(
                level3Roads,
                new[] { new PierPlacement("pier-1", Point(2, 0), Point(2, -2)) });
            var level4Layout = new BridgeLayout(level4Roads, trusses: MakeTrusses(level4Roads));
            var level5Layout = new BridgeLayout(level5Roads, level5Piers);

            return new List<LevelDefinition>
            {
                new LevelDefinition(
                    LevelId.Level1,
                    1,
                    "直接连接",
                    "用标准桥板连接两座等高岛屿。",
                    Point(0, 0),
                    Point(3, 0),
                    null,
                    false,
                    Array.Empty<GridPoint>(),
                    Inventory(5, 0, 0, 0),
                    new MedalThresholds(3, 4),
                    3,
                    3,
                    new ReferenceSolution(3, level1Layout)),
                new LevelDefinition(
                    LevelId.Level2,
                    2,
                    "中继支撑",
                    "在中央基础座安装桥墩，将长桥分为两个安全跨度。",
                    Point(0, 0),
                    Point(6, 0),
                    null,
                    false,
                    new[] { Point(3, -2) },
                    Inventory(9, 0, 1, 0),
                    new MedalThresholds(8, 10),
                    8,
                    8,
                    new ReferenceSolution(8, level2Layout)),
                new LevelDefinition(
                    LevelId.Level3,
                    3,
                    "高差连接",
                    "用固定坡道连接高一格的终点，并复用中央桥墩。",
                    Point(0, 0),
                    Point(4, 1),
                    null,
                    false,
                    new[] { Point(2, -2) },
                    Inventory(5, 2, 1, 0),
                    new MedalThresholds(6, 8),
                    6,
                    6,
                    new ReferenceSolution(6, level3Layout)),
                new LevelDefinition(
                    LevelId.Level4,
                    4,
                    "完整桁架",
                    "让连续桁架覆盖整段桥面并连接两端硬支撑。",
                    Point(0, 0),
                    Point(5, 0),
                    null,
                    false,
                    Array.Empty<GridPoint>(),
                    Inventory(7, 0, 0, 7),
                    new MedalThresholds(10, 12),
                    10,
                    10,
                    new ReferenceSolution(10, level4Layout)),
                new LevelDefinition(
                    LevelId.Level5,
                    5,
                    "三维远征",
                    "在十格长、宽、高的开放网格中自由转向，连接对角的两座岛屿。",
                    Point(0, 0, 0),
                    Point(10, 10, 10),
                    new BuildVolume(Point(0, 0, 0), Point(10, 10, 10), -2),
                    true,
                    Array.Empty<GridPoint>(),
                    Inventory(40, 40, 20, 40),
                    new MedalThresholds(32, 40),
                    32,
                    32,
                    new ReferenceSolution(32, level5Layout)),
            };
        }

        private static GridPoint Point(int x, int y, int z = 0)
        {
            return new GridPoint(x, y, z);
        }

        private static PieceInventory Inventory(int deck, int ramp, int pier, int truss)
        {
            return new PieceInventory(deck, ramp, pier, truss);
        }

        private static RoadPiecePlacement MakeRoad(
            string id,
            PieceType type,
            GridPoint from,
            GridPoint to)
        {
            return new RoadPiecePlacement(id, type, from, to);
        }

        private static IReadOnlyList<RoadPiecePlacement> MakeFlatRoads(int length)
        {
            return Enumerable.Range(0, length)
                .Select(index => MakeRoad(
                    $"deck-{index + 1}",
                    PieceType.Deck,
                    Point(index, 0),
                    Point(index + 1, 0)))
                .ToList();
        }

        private static IReadOnlyList<TrussPlacement> MakeTrusses(IEnumerable<RoadPiecePlacement> roads)
        {
            return roads.Select((road, index) =>
                new TrussPlacement($"truss-{index + 1}", road.Id)).ToList();
        }
    }
}
