using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Skybridge.Core;

namespace Skybridge.Tests
{
    public sealed class BridgeRulesTests
    {
        [Test]
        public void ThreeOrdinaryBoardsBetweenHardSupportsAreSafe()
        {
            var analysis = BridgeRules.AnalyzeSpans(MakeLevel(3), MakeLayout(3));

            Assert.That(analysis.Validation.CanTest, Is.True);
            Assert.That(analysis.Spans, Has.Count.EqualTo(1));
            Assert.That(analysis.Spans[0].Length, Is.EqualTo(3));
            Assert.That(analysis.Spans[0].SafeLimit, Is.EqualTo(3));
            Assert.That(analysis.Spans[0].Safe, Is.True);
            Assert.That(analysis.WeakPoints, Is.Empty);
        }

        [Test]
        public void FourOrdinaryBoardsHaveOneFixedCentralBreakJoint()
        {
            var level = MakeLevel(4);
            var layout = MakeLayout(4);

            for (var iteration = 0; iteration < 20; iteration++)
            {
                var analysis = BridgeRules.AnalyzeSpans(level, layout);
                Assert.That(analysis.Spans[0].Length, Is.EqualTo(4));
                Assert.That(analysis.Spans[0].SafeLimit, Is.EqualTo(3));
                Assert.That(analysis.Spans[0].Safe, Is.False);
                Assert.That(analysis.WeakPoints, Has.Count.EqualTo(1));
                Assert.That(analysis.WeakPoints[0].PathNodeIndex, Is.EqualTo(2));
                Assert.That(analysis.WeakPoints[0].Joint, Is.EqualTo(Point(2)));
                Assert.That(analysis.WeakPoints[0].BeforePieceId, Is.EqualTo("road-2"));
                Assert.That(analysis.WeakPoints[0].AfterPieceId, Is.EqualTo("road-3"));
            }
        }

        [Test]
        public void FiveContinuouslyConnectedTrussModulesAreSafe()
        {
            var analysis = BridgeRules.AnalyzeSpans(MakeLevel(5), MakeLayout(5, fullyTrussed: true));

            Assert.That(analysis.Spans[0].Length, Is.EqualTo(5));
            Assert.That(analysis.Spans[0].FullyTrussed, Is.True);
            Assert.That(analysis.Spans[0].SafeLimit, Is.EqualTo(5));
            Assert.That(analysis.Spans[0].Safe, Is.True);
        }

        [Test]
        public void SixFullyTrussedBoardsHaveOneFixedCentralBreakJoint()
        {
            var analysis = BridgeRules.AnalyzeSpans(MakeLevel(6), MakeLayout(6, fullyTrussed: true));

            Assert.That(analysis.Spans[0].Length, Is.EqualTo(6));
            Assert.That(analysis.Spans[0].SafeLimit, Is.EqualTo(5));
            Assert.That(analysis.Spans[0].Safe, Is.False);
            Assert.That(analysis.WeakPoints[0].PathNodeIndex, Is.EqualTo(3));
            Assert.That(analysis.WeakPoints[0].Joint, Is.EqualTo(Point(3)));
        }

        [Test]
        public void ValidCentralPierSplitsSixBoardsIntoTwoSafeSpans()
        {
            var foundation = Point(3, -2);
            var pier = new PierPlacement("pier-1", Point(3), foundation);
            var analysis = BridgeRules.AnalyzeSpans(
                MakeLevel(6, new[] { foundation }),
                MakeLayout(6, new[] { pier }));

            CollectionAssert.AreEqual(
                new[] { HardSupportKind.Start, HardSupportKind.Pier, HardSupportKind.Goal },
                analysis.HardSupports.Select(support => support.Kind));
            CollectionAssert.AreEqual(new[] { 3, 3 }, analysis.Spans.Select(span => span.Length));
            Assert.That(analysis.Spans.All(span => span.Safe), Is.True);
        }

        [Test]
        public void PierDetachedFromFoundationPadDoesNotCountAsSupport()
        {
            var foundation = Point(3, -2);
            var detachedPier = new PierPlacement("pier-1", Point(3), foundation, snappedBase: false);
            var analysis = BridgeRules.AnalyzeSpans(
                MakeLevel(6, new[] { foundation }),
                MakeLayout(6, new[] { detachedPier }));

            CollectionAssert.AreEqual(
                new[] { HardSupportKind.Start, HardSupportKind.Goal },
                analysis.HardSupports.Select(support => support.Kind));
            Assert.That(analysis.Spans[0].Length, Is.EqualTo(6));
            Assert.That(analysis.Spans[0].Safe, Is.False);
        }

        [Test]
        public void IncompleteOrDetachedTrussRowsAreRejected()
        {
            var roads = MakeFlatRoads(5);
            var complete = MakeTrusses(roads);
            var missingOne = new BridgeLayout(roads, trusses: complete.Take(4));
            var detached = complete.Select((truss, index) => index == 0
                ? new TrussPlacement(truss.Id, truss.RoadPieceId, connectedAtFrom: false)
                : truss);
            var detachedEnd = new BridgeLayout(roads, trusses: detached);

            var missingAnalysis = BridgeRules.AnalyzeSpans(MakeLevel(5), missingOne);
            var detachedAnalysis = BridgeRules.AnalyzeSpans(MakeLevel(5), detachedEnd);
            Assert.That(missingAnalysis.Spans[0].FullyTrussed, Is.False);
            Assert.That(missingAnalysis.Spans[0].Safe, Is.False);
            Assert.That(detachedAnalysis.Spans[0].FullyTrussed, Is.False);
            Assert.That(detachedAnalysis.Spans[0].Safe, Is.False);
        }

        [Test]
        public void MultipleWeakPointsAreOrderedByVehicleTravel()
        {
            var foundation = Point(4, -2);
            var pier = new PierPlacement("pier-1", Point(4), foundation);
            var analysis = BridgeRules.AnalyzeSpans(
                MakeLevel(8, new[] { foundation }),
                MakeLayout(8, new[] { pier }));

            CollectionAssert.AreEqual(new[] { 4, 4 }, analysis.Spans.Select(span => span.Length));
            CollectionAssert.AreEqual(new[] { 2, 6 }, analysis.WeakPoints.Select(point => point.PathNodeIndex));
        }

        [Test]
        public void OddLengthTieBreaksTowardGoal()
        {
            Assert.That(BridgeRules.SelectWeakPointPathNodeIndex(0, 5), Is.EqualTo(3));
            Assert.That(BridgeRules.AnalyzeSpans(MakeLevel(5), MakeLayout(5)).WeakPoints[0].Joint, Is.EqualTo(Point(3)));
        }

        [Test]
        public void DisconnectedRoadBlocksTesting()
        {
            var roads = MakeFlatRoads(3).Where(road => road.Id != "road-2");
            var validation = BridgeRules.ValidateBridge(MakeLevel(3), new BridgeLayout(roads));

            Assert.That(validation.CanTest, Is.False);
            Assert.That(validation.Issues[0].Code, Is.EqualTo(ValidationIssueCode.DisconnectedPath));
        }

        [Test]
        public void RampBeyondOneUnitSlopeBlocksTesting()
        {
            var level = CopyLevel(MakeLevel(1), Point(1, 2));
            var illegalRamp = new RoadPiecePlacement("ramp-1", PieceType.Ramp, Point(0), Point(1, 2));
            var validation = BridgeRules.ValidateBridge(level, new BridgeLayout(new[] { illegalRamp }));

            Assert.That(validation.CanTest, Is.False);
            Assert.That(validation.Issues[0].Code, Is.EqualTo(ValidationIssueCode.InvalidRampGeometry));
        }

        [Test]
        public void OrthogonalTurnsRequireExplicitLevelOptIn()
        {
            var roads = new[]
            {
                new RoadPiecePlacement("turn-x", PieceType.Deck, Point(0, 0, 0), Point(1, 0, 0)),
                new RoadPiecePlacement("turn-z", PieceType.Deck, Point(1, 0, 0), Point(1, 0, 1)),
            };
            var strictLevel = CopyLevel(MakeLevel(2), Point(1, 0, 1), allowTurns: false);
            var openLevel = CopyLevel(strictLevel, strictLevel.GoalAnchor, allowTurns: true);
            var layout = new BridgeLayout(roads);

            Assert.That(BridgeRules.ValidateBridge(strictLevel, layout).Issues[0].Code, Is.EqualTo(ValidationIssueCode.SharpTurn));
            Assert.That(BridgeRules.ValidateBridge(openLevel, layout).CanTest, Is.True);
        }

        [Test]
        public void OneLevelOverheadObstacleBlocksButTwoLevelsClear()
        {
            var mainRoads = MakeFlatRoads(2);
            var blocked = BridgeRules.ValidateBridge(MakeLevel(2), new BridgeLayout(
                mainRoads.Concat(new[] { OverheadRoad(1) })));
            var clear = BridgeRules.ValidateBridge(MakeLevel(2), new BridgeLayout(
                mainRoads.Concat(new[] { OverheadRoad(2) })));

            Assert.That(blocked.CanTest, Is.False);
            Assert.That(blocked.Issues[0].Code, Is.EqualTo(ValidationIssueCode.ClearanceBlocked));
            Assert.That(blocked.Issues[0].PieceId, Is.EqualTo("overhead-1"));
            Assert.That(blocked.Issues[0].Point, Is.EqualTo(Point(1)));
            Assert.That(clear.CanTest, Is.True);
        }

        [Test]
        public void SameLevelPerpendicularBranchBlocksVehiclePath()
        {
            var branch = new RoadPiecePlacement("side-branch", PieceType.Deck, Point(1), Point(1, 0, 1));
            var validation = BridgeRules.ValidateBridge(
                MakeLevel(3),
                new BridgeLayout(MakeFlatRoads(3).Concat(new[] { branch })));

            Assert.That(validation.CanTest, Is.False);
            Assert.That(validation.Issues[0].Code, Is.EqualTo(ValidationIssueCode.ClearanceBlocked));
            Assert.That(validation.Issues[0].PieceId, Is.EqualTo("side-branch"));
            Assert.That(validation.Issues[0].Point, Is.EqualTo(Point(1)));
        }

        [Test]
        public void ParallelSideRoadOneLaneAwayIsAccepted()
        {
            var sideRoad = new RoadPiecePlacement(
                "parallel-side-road",
                PieceType.Deck,
                Point(1, 0, 1),
                Point(2, 0, 1));
            var validation = BridgeRules.ValidateBridge(
                MakeLevel(3),
                new BridgeLayout(MakeFlatRoads(3).Concat(new[] { sideRoad })));

            Assert.That(validation.CanTest, Is.True);
        }

        [Test]
        public void SideClearanceObstructionIsIndependentOfInsertionOrder()
        {
            var branchA = new RoadPiecePlacement("branch-a", PieceType.Deck, Point(1, 0, -1), Point(1));
            var branchB = new RoadPiecePlacement("branch-b", PieceType.Deck, Point(1), Point(1, 0, 1));
            var mainRoads = MakeFlatRoads(3);
            var forward = BridgeRules.ValidateBridge(
                MakeLevel(3),
                new BridgeLayout(mainRoads.Concat(new[] { branchA, branchB })));
            var reversed = BridgeRules.ValidateBridge(
                MakeLevel(3),
                new BridgeLayout(mainRoads.Concat(new[] { branchB, branchA })));

            Assert.That(forward.Issues[0].PieceId, Is.EqualTo("branch-a"));
            Assert.That(reversed.Issues[0].PieceId, Is.EqualTo("branch-a"));
        }

        [Test]
        public void SameStructureAlwaysReturnsSameRouteAndBreakPoint()
        {
            var level = MakeLevel(4);
            var layout = MakeLayout(4);
            var signatures = Enumerable.Range(0, 20)
                .Select(_ => AnalysisSignature(BridgeRules.AnalyzeSpans(level, layout)))
                .Distinct()
                .ToList();

            Assert.That(signatures, Has.Count.EqualTo(1));
        }

        [Test]
        public void FiveReferencesValidateWithMatchingCostsAndGoldThresholds()
        {
            Assert.That(LevelCatalog.All, Has.Count.EqualTo(5));
            foreach (var level in LevelCatalog.All)
            {
                var analysis = BridgeRules.AnalyzeSpans(level, level.ReferenceSolution.Layout);
                Assert.That(analysis.Validation.CanTest, Is.True, level.Id.ToString());
                Assert.That(analysis.WeakPoints, Is.Empty, level.Id.ToString());
                Assert.That(BridgeRules.CalculateBuildCost(level.ReferenceSolution.Layout), Is.EqualTo(level.ReferenceCost), level.Id.ToString());
                Assert.That(level.ReferenceSolution.Cost, Is.EqualTo(level.ReferenceCost), level.Id.ToString());
                Assert.That(level.TheoreticalMinimumCost, Is.EqualTo(level.MedalThresholds.Gold), level.Id.ToString());
            }
        }

        [Test]
        public void LevelFiveIsSafeTenByTenByTenOpenVolumeRoute()
        {
            var level = LevelCatalog.Get(LevelId.Level5);
            var analysis = BridgeRules.AnalyzeSpans(level, level.ReferenceSolution.Layout);

            Assert.That(level.StartAnchor, Is.EqualTo(Point(0, 0, 0)));
            Assert.That(level.GoalAnchor, Is.EqualTo(Point(10, 10, 10)));
            Assert.That(level.BuildVolume.Min, Is.EqualTo(Point(0, 0, 0)));
            Assert.That(level.BuildVolume.Max, Is.EqualTo(Point(10, 10, 10)));
            Assert.That(level.BuildVolume.PierFoundationY, Is.EqualTo(-2));
            Assert.That(level.AllowTurns, Is.True);
            Assert.That(new[] { level.Inventory.Deck, level.Inventory.Ramp, level.Inventory.Pier, level.Inventory.Truss }.All(count => count > 0), Is.True);
            Assert.That(level.ReferenceSolution.Layout.Roads, Has.Count.EqualTo(20));
            Assert.That(level.ReferenceSolution.Layout.Roads.Count(road => road.Type == PieceType.Ramp), Is.EqualTo(10));
            CollectionAssert.AreEqual(
                new[] { 0, 3, 6, 9, 12, 15, 18, 20 },
                analysis.HardSupports.Select(support => support.PathNodeIndex));
            CollectionAssert.AreEqual(
                new[] { 3, 3, 3, 3, 3, 3, 2 },
                analysis.Spans.Select(span => span.Length));
            Assert.That(analysis.WeakPoints, Is.Empty);
        }

        [Test]
        public void RatingUsesOnlySuccessAndCost()
        {
            var level = LevelCatalog.Get(LevelId.Level4);

            Assert.That(BridgeRules.CalculateRating(level, 1, false), Is.EqualTo(Medal.None));
            Assert.That(BridgeRules.CalculateRating(level, 13, true), Is.EqualTo(Medal.Bronze));
            Assert.That(BridgeRules.CalculateRating(level, 12, true), Is.EqualTo(Medal.Silver));
            Assert.That(BridgeRules.CalculateRating(level, 10, true), Is.EqualTo(Medal.Gold));
        }

        private static LevelDefinition MakeLevel(int length, IEnumerable<GridPoint> foundationPads = null)
        {
            var source = LevelCatalog.Get(LevelId.Level1);
            return new LevelDefinition(
                source.Id,
                source.Number,
                source.Name,
                source.Objective,
                source.StartAnchor,
                Point(length),
                source.BuildVolume,
                source.AllowTurns,
                foundationPads ?? Array.Empty<GridPoint>(),
                new PieceInventory(20, 20, 20, 20),
                source.MedalThresholds,
                source.ReferenceCost,
                source.TheoreticalMinimumCost,
                source.ReferenceSolution);
        }

        private static LevelDefinition CopyLevel(
            LevelDefinition source,
            GridPoint goalAnchor,
            bool? allowTurns = null)
        {
            return new LevelDefinition(
                source.Id,
                source.Number,
                source.Name,
                source.Objective,
                source.StartAnchor,
                goalAnchor,
                source.BuildVolume,
                allowTurns ?? source.AllowTurns,
                source.FoundationPads,
                source.Inventory,
                source.MedalThresholds,
                source.ReferenceCost,
                source.TheoreticalMinimumCost,
                source.ReferenceSolution);
        }

        private static IReadOnlyList<RoadPiecePlacement> MakeFlatRoads(int length)
        {
            return Enumerable.Range(0, length)
                .Select(index => new RoadPiecePlacement(
                    $"road-{index + 1}",
                    PieceType.Deck,
                    Point(index),
                    Point(index + 1)))
                .ToList();
        }

        private static IReadOnlyList<TrussPlacement> MakeTrusses(IEnumerable<RoadPiecePlacement> roads)
        {
            return roads.Select((road, index) => new TrussPlacement($"truss-{index + 1}", road.Id)).ToList();
        }

        private static BridgeLayout MakeLayout(
            int length,
            IEnumerable<PierPlacement> piers = null,
            bool fullyTrussed = false)
        {
            var roads = MakeFlatRoads(length);
            return new BridgeLayout(
                roads,
                piers,
                fullyTrussed ? MakeTrusses(roads) : Array.Empty<TrussPlacement>());
        }

        private static RoadPiecePlacement OverheadRoad(int height)
        {
            return new RoadPiecePlacement(
                $"overhead-{height}",
                PieceType.Deck,
                Point(1, height, -1),
                Point(1, height, 0));
        }

        private static string AnalysisSignature(SpanAnalysisResult analysis)
        {
            var nodes = string.Join(";", analysis.Validation.Path.Nodes.Select(node => node.Key));
            var weakPoints = string.Join(";", analysis.WeakPoints.Select(point =>
                $"{point.PathNodeIndex}:{point.BeforePieceId}:{point.AfterPieceId}"));
            return $"{nodes}|{weakPoints}";
        }

        private static GridPoint Point(int x, int y = 0, int z = 0)
        {
            return new GridPoint(x, y, z);
        }
    }
}
