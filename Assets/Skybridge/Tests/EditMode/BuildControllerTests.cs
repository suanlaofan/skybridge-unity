using System.Linq;
using NUnit.Framework;
using Skybridge.Core;

namespace Skybridge.Tests
{
    public sealed class BuildControllerTests
    {
        [Test]
        public void FirstLevelBuildsWithoutTutorialOnlyState()
        {
            var level = LevelCatalog.Get(LevelId.Level1);
            var builder = new BuildController(level);

            PlaceRoads(builder, 3);

            Assert.That(BridgeRules.ValidateBridge(level, builder.GetLayout()).CanTest, Is.True);
            Assert.That(builder.GetCost(), Is.EqualTo(3));
        }

        [Test]
        public void RampOnFlatSlotIsRejectedWithGeometricReason()
        {
            var builder = new BuildController(LevelCatalog.Get(LevelId.Level1));
            builder.SelectTool(PieceType.Ramp);

            var result = builder.HandleHit(BuildHit.Road(1));

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Valid, Is.False);
            Assert.That(result.Message, Does.Contain("height difference"));
        }

        [Test]
        public void LevelTwoPierConvertsWeakSpanToTwoSafeSpans()
        {
            var level = LevelCatalog.Get(LevelId.Level2);
            var builder = new BuildController(level);
            PlaceRoads(builder, 6);
            Assert.That(BridgeRules.AnalyzeSpans(level, builder.GetLayout()).WeakPoints, Has.Count.EqualTo(1));

            builder.SelectTool(PieceType.Pier);
            Assert.That(builder.HandleHit(BuildHit.Foundation(0)).Changed, Is.True);
            var analysis = BridgeRules.AnalyzeSpans(level, builder.GetLayout());

            CollectionAssert.AreEqual(new[] { 3, 3 }, analysis.Spans.Select(span => span.Length));
            Assert.That(analysis.WeakPoints, Is.Empty);
        }

        [Test]
        public void UndoRedoSelectionAndDeletionAreDeterministic()
        {
            var builder = new BuildController(LevelCatalog.Get(LevelId.Level1));
            PlaceRoads(builder, 2);
            Assert.That(builder.GetLayout().Roads, Has.Count.EqualTo(2));
            Assert.That(builder.Undo(), Is.True);
            Assert.That(builder.GetLayout().Roads, Has.Count.EqualTo(1));
            Assert.That(builder.Redo(), Is.True);
            Assert.That(builder.GetLayout().Roads, Has.Count.EqualTo(2));

            var pieceId = builder.GetLayout().Roads[0].Id;
            builder.CancelSelection();
            builder.HandleHit(BuildHit.Piece(pieceId));
            Assert.That(builder.RemoveSelected(), Is.True);
            Assert.That(builder.GetLayout().Roads, Has.Count.EqualTo(1));
        }

        [Test]
        public void RoadPreviewRotatesOntoAdjacentGridLane()
        {
            var builder = new BuildController(LevelCatalog.Get(LevelId.Level1));
            builder.SelectTool(PieceType.Deck);
            builder.Rotate(1);
            var zSlot = builder.GetRoadSlots().First(slot => slot.Axis == RoadAxis.Z);

            var preview = builder.Preview(BuildHit.Road(zSlot.Index));

            Assert.That(preview.Valid, Is.True);
            Assert.That(preview.From, Is.EqualTo(zSlot.From));
            Assert.That(preview.To, Is.EqualTo(zSlot.To));
        }

        [Test]
        public void QuarterTurnOnHorizontalEdgeIsRejected()
        {
            var builder = new BuildController(LevelCatalog.Get(LevelId.Level1));
            builder.SelectTool(PieceType.Deck);
            builder.Rotate(1);

            var preview = builder.Preview(BuildHit.Road(0));

            Assert.That(preview.Valid, Is.False);
            Assert.That(preview.Message, Does.Contain("rotation"));
        }

        [Test]
        public void HorizontalBoardTurnedOneHundredEightyDegreesUsesSameEdge()
        {
            var builder = new BuildController(LevelCatalog.Get(LevelId.Level1));
            builder.SelectTool(PieceType.Deck);
            Assert.That(builder.HandleHit(BuildHit.Road(0)).Changed, Is.True);
            builder.Rotate(1);
            builder.Rotate(1);

            var preview = builder.Preview(BuildHit.Road(0));

            Assert.That(preview.Valid, Is.False);
            Assert.That(preview.Message, Does.Contain("occupied"));
        }

        [Test]
        public void LevelFiveBuildsOneEditableHeightLayerAtATime()
        {
            var builder = new BuildController(LevelCatalog.Get(LevelId.Level5));
            var bounds = builder.GetLayerBounds();
            Assert.That(bounds.HasValue, Is.True);
            Assert.That(bounds.Value.Min, Is.EqualTo(0));
            Assert.That(bounds.Value.Max, Is.EqualTo(10));
            Assert.That(builder.GetRoadSlots(), Has.Count.EqualTo(110));

            builder.SelectTool(PieceType.Ramp);
            Assert.That(builder.SetActiveLayer(4), Is.True);
            Assert.That(builder.SetRampDelta(1), Is.False);
            builder.Rotate(1);
            var slot = builder.GetRoadSlots().First(candidate =>
                candidate.From.X == 3 && candidate.From.Z == 2);

            Assert.That(slot.Axis, Is.EqualTo(RoadAxis.Z));
            Assert.That(slot.From, Is.EqualTo(new GridPoint(3, 4, 2)));
            Assert.That(slot.To, Is.EqualTo(new GridPoint(3, 5, 3)));
            Assert.That(builder.HandleHit(BuildHit.Road(slot.Index)).Changed, Is.True);
            Assert.That(builder.GetLayout().Roads[0].Type, Is.EqualTo(PieceType.Ramp));
            Assert.That(builder.GetLayout().Roads[0].From, Is.EqualTo(new GridPoint(3, 4, 2)));
            Assert.That(builder.GetLayout().Roads[0].To, Is.EqualTo(new GridPoint(3, 5, 3)));
            Assert.That(builder.ActiveLayer, Is.EqualTo(5));
        }

        [Test]
        public void PlacedRampFollowsDestinationLayerAndBoundaryGridsStayUsable()
        {
            var builder = new BuildController(LevelCatalog.Get(LevelId.Level5));
            builder.SelectTool(PieceType.Ramp);
            Assert.That(builder.SetRampDelta(-1), Is.False);

            Assert.That(builder.SetActiveLayer(9), Is.True);
            var risingSlot = builder.GetRoadSlots().First(slot =>
                slot.From.X == 4 && slot.From.Z == 4);
            Assert.That(builder.HandleHit(BuildHit.Road(risingSlot.Index)).Changed, Is.True);
            Assert.That(builder.ActiveLayer, Is.EqualTo(10));
            Assert.That(builder.RampDelta, Is.EqualTo(-1));
            Assert.That(builder.GetRoadSlots(), Has.Count.EqualTo(110));
            Assert.That(builder.GetRoadSlots()[0].From.Y, Is.EqualTo(10));
            Assert.That(builder.GetRoadSlots()[0].To.Y, Is.EqualTo(9));

            Assert.That(builder.SetActiveLayer(0), Is.True);
            Assert.That(builder.RampDelta, Is.EqualTo(1));
            Assert.That(builder.GetRoadSlots(), Has.Count.EqualTo(110));
        }

        [Test]
        public void LevelFiveSupportsDescendingTurnsAndPiersAtArbitraryCells()
        {
            var builder = new BuildController(LevelCatalog.Get(LevelId.Level5));
            builder.SelectTool(PieceType.Ramp);
            builder.SetActiveLayer(6);
            builder.SetRampDelta(-1);
            builder.Rotate(1);
            builder.Rotate(1);
            var rampSlot = builder.GetRoadSlots().First(slot =>
                slot.From.X == 8 && slot.From.Z == 4);

            Assert.That(rampSlot.From, Is.EqualTo(new GridPoint(8, 6, 4)));
            Assert.That(rampSlot.To, Is.EqualTo(new GridPoint(7, 5, 4)));
            Assert.That(builder.HandleHit(BuildHit.Road(rampSlot.Index)).Changed, Is.True);

            builder.SelectTool(PieceType.Pier);
            builder.SetActiveLayer(6);
            var pierSlot = builder.GetFoundationSlots().First(slot =>
                slot.Top.X == 7 && slot.Top.Z == 4);
            Assert.That(builder.HandleHit(BuildHit.Foundation(pierSlot.Index)).Changed, Is.True);
            Assert.That(builder.GetLayout().Piers[0].Top, Is.EqualTo(new GridPoint(7, 6, 4)));
            Assert.That(builder.GetLayout().Piers[0].Foundation, Is.EqualTo(new GridPoint(7, -2, 4)));
        }

        private static void PlaceRoads(BuildController builder, int count)
        {
            builder.SelectTool(PieceType.Deck);
            for (var slotIndex = 0; slotIndex < count; slotIndex++)
            {
                Assert.That(builder.HandleHit(BuildHit.Road(slotIndex)).Changed, Is.True);
            }
        }
    }
}
