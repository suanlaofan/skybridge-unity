using System.Linq;
using NUnit.Framework;
using Skybridge.Core;
using Skybridge.Game;

namespace Skybridge.Tests
{
    public sealed class SkybridgeTutorialDirectorTests
    {
        private SkybridgeTutorialDirector tutorial;
        private SkybridgeMemoryTutorialProgressStore store;
        private BuildController builder;

        [SetUp]
        public void SetUp() => StartLesson(0);

        [Test]
        public void WrongEventOrUnchangedBuilderCannotAdvancePlacement()
        {
            Observe("pick");
            Assert.That(tutorial.StepId, Is.EqualTo("SelectDeck"));
            builder.SelectTool(PieceType.Pier);
            Observe("tool");
            Assert.That(tutorial.StepId, Is.EqualTo("SelectDeck"));
            Select(PieceType.Deck);
            Observe("pick");
            Observe("success", GamePhase.Success, TestOutcome.Success);
            Assert.That(tutorial.StepId, Is.EqualTo("PlaceFirstDeck"));
            Assert.That(store.IsCompleted(0), Is.False);
        }

        [Test]
        public void FirstPlacementMustBeActualDeckBesideStart()
        {
            Select(PieceType.Deck);
            PlaceRoad(2);
            Assert.That(tutorial.StepId, Is.EqualTo("PlaceFirstDeck"));
            PlaceRoad(0);
            Assert.That(tutorial.StepId, Is.EqualTo("ConnectGoal"));
        }

        [Test]
        public void OptionalDeletionAndUndoKeepInventoryAndReturnToBuilding()
        {
            ReachDeletion();
            var id = builder.SelectedPieceId;
            var inventory = builder.GetInventoryRemaining();
            Assert.That(builder.RemoveSelected(), Is.True);
            Observe("delete");
            Assert.That(tutorial.StepId, Is.EqualTo("PlaceFirstDeck"));
            Assert.That(builder.GetInventoryRemaining().Deck, Is.EqualTo(inventory.Deck + 1));
            Observe("undo"); // Merely reporting an action cannot restore the board.
            Assert.That(tutorial.StepId, Is.EqualTo("PlaceFirstDeck"));
            Assert.That(builder.Undo(), Is.True);
            Observe("undo");
            Assert.That(tutorial.StepId, Is.EqualTo("ConnectGoal"));
            Assert.That(builder.GetLayout().Roads.Single().Id, Is.EqualTo(id));
            Assert.That(builder.GetInventoryRemaining(), Is.EqualTo(inventory));
        }

        [Test]
        public void OptionalSelectionNeverAddsARequiredDeletionStep()
        {
            ReachDeletion();
            builder.CancelSelection();
            Observe("cancel");
            Assert.That(tutorial.StepId, Is.EqualTo("ConnectGoal"));
            Assert.That(tutorial.Allows("pick"), Is.True);
            SelectFirstPiece();
            Assert.That(tutorial.StepId, Is.EqualTo("ConnectGoal"));
        }

        [Test]
        public void UndoOfRequiredFirstBoardReturnsToPlacementAndCanRecoverWithRedo()
        {
            Select(PieceType.Deck);
            PlaceRoad(0);
            Assert.That(builder.Undo(), Is.True);
            Observe("undo");
            Assert.That(tutorial.StepId, Is.EqualTo("PlaceFirstDeck"));
            Assert.That(builder.Redo(), Is.True);
            Observe("redo");
            // Redo restores a dependency; it should give a satisfiable selection step.
            Assert.That(tutorial.StepId, Is.EqualTo("ConnectGoal"));
        }

        [Test]
        public void UndoAfterConnectingReturnsToBuildAndRedoRestoresTestReadiness()
        {
            ReachTestReady();
            Assert.That(builder.Undo(), Is.True);
            Observe("undo");
            Assert.That(tutorial.StepId, Is.EqualTo("ConnectGoal"));
            Assert.That(tutorial.Allows("test"), Is.True, "The game validates the bridge; the tutorial must not block Test.");
            Assert.That(builder.Redo(), Is.True);
            Observe("redo");
            Assert.That(tutorial.StepId, Is.EqualTo("StartTest"));
        }

        [Test]
        public void PausedTimeAndInvalidDeltasNeverEscalateHelpOrAdvance()
        {
            tutorial.Tick(60, true);
            tutorial.Tick(float.NaN, false);
            tutorial.Tick(float.PositiveInfinity, false);
            tutorial.Tick(-10, false);
            Assert.That(tutorial.AssistanceLevel, Is.Zero);
            tutorial.Tick(14, false);
            Assert.That(tutorial.AssistanceLevel, Is.Zero);
            tutorial.Tick(1, false);
            Assert.That(tutorial.AssistanceLevel, Is.EqualTo(1));
            tutorial.Tick(15, false);
            Assert.That(tutorial.AssistanceLevel, Is.EqualTo(2));
            tutorial.Tick(30, false);
            Assert.That(tutorial.AssistanceLevel, Is.EqualTo(3));
            Assert.That(tutorial.StepId, Is.EqualTo("SelectDeck"));
            Assert.That(tutorial.IsComplete, Is.False);
        }

        [Test]
        public void HintAndRetryDoNotChangeLayoutInventoryOrMastery()
        {
            Select(PieceType.Deck);
            PlaceRoad(0);
            var roadId = builder.GetLayout().Roads.Single().Id;
            var inventory = builder.GetInventoryRemaining();
            tutorial.RequestHint();
            tutorial.RetryStep();
            Assert.That(tutorial.AssistanceLevel, Is.EqualTo(1));
            Assert.That(builder.GetLayout().Roads.Single().Id, Is.EqualTo(roadId));
            Assert.That(builder.GetInventoryRemaining(), Is.EqualTo(inventory));
            Assert.That(store.IsCompleted(0), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SkipOrPrematureCompletedArgumentCannotGrantMastery(bool completed)
        {
            tutorial.Stop(completed);
            Assert.That(tutorial.IsActive, Is.False);
            Assert.That(tutorial.IsComplete, Is.False);
            Assert.That(store.IsCompleted(0), Is.False);
            Assert.That(store.WasSkipped(0), Is.True);
        }

        [Test]
        public void CompleteBasicLessonRequiresRealStartedTestAndMatchingSuccess()
        {
            ReachTestReady();
            Observe("success", GamePhase.Success, TestOutcome.Success);
            Assert.That(tutorial.IsComplete, Is.False);
            Observe("test", GamePhase.Build);
            Assert.That(tutorial.StepId, Is.EqualTo("StartTest"));
            Observe("test", GamePhase.TestPreparing);
            Assert.That(tutorial.StepId, Is.EqualTo("AwaitSuccess"));
            Observe("success", GamePhase.VehicleRunning, TestOutcome.Success);
            Assert.That(tutorial.IsComplete, Is.False);
            Observe("success", GamePhase.Success, TestOutcome.Success);
            Assert.That(tutorial.IsComplete, Is.True);
            Assert.That(store.IsCompleted(0), Is.True);
            Assert.That(store.IsCompleted(2), Is.False);
        }

        [Test]
        public void FailureOrStopReturnsToTestReadyWithoutGrantingCompletion()
        {
            ReachTestReady();
            Observe("test", GamePhase.VehicleRunning);
            Assert.That(tutorial.Allows("stop"), Is.True);
            Assert.That(tutorial.Allows("pick"), Is.False);
            Observe("failure", GamePhase.RestoreSnapshot, TestOutcome.Failure);
            Assert.That(tutorial.StepId, Is.EqualTo("AwaitSuccess"));
            Observe("sync", GamePhase.Build, TestOutcome.Failure);
            Assert.That(tutorial.StepId, Is.EqualTo("StartTest"));
            Assert.That(store.IsCompleted(0), Is.False);
            Observe("test", GamePhase.TestPreparing);
            Observe("stop", GamePhase.Build);
            Assert.That(tutorial.StepId, Is.EqualTo("StartTest"));
        }

        [Test]
        public void EveryStepKeepsEscapeAndHelpAvailableIncludingDuringTest()
        {
            foreach (var action in new[] { "cancel", "help", "exit", "skip", "hint", "retry", "replay", "stop" })
                Assert.That(tutorial.Allows(action), Is.True, action);
            ReachTestReady();
            Observe("test", GamePhase.VehicleRunning);
            foreach (var action in new[] { "cancel", "help", "exit", "skip", "stop" })
                Assert.That(tutorial.Allows(action), Is.True, action);
        }

        [Test]
        public void RootWorkbenchActionsAllowAdjustmentAndModalReturnDuringSpatialLesson()
        {
            StartLesson(5);
            foreach (var action in new[]
            {
                "adjust", "workspace-x", "workspace-y", "workspace-z", "workspace-rotate",
                "workspace-scale", "workspace-reset", "reposition", "continue", "help", "mute", "reduced-motion"
            }) Assert.That(tutorial.Allows(action), Is.True, action);
            Observe("adjust");
            Observe("workspace");
            Observe("continue");
            Assert.That(tutorial.StepId, Is.EqualTo("SelectDeck"));
        }

        [Test]
        public void CompletedTutorialStillAllowsResultAndAdjustmentExit()
        {
            ReachTestReady();
            CompleteTest();
            foreach (var action in new[] { "continue", "next", "help", "cancel", "exit", "workspace-reset" })
                Assert.That(tutorial.Allows(action), Is.True, action);
        }

        [Test]
        public void RootSelectSemanticFromWorldPickAdvancesSelectionWithoutPlacementEvent()
        {
            Select(PieceType.Deck);
            PlaceRoad(0);
            builder.CancelSelection();
            Observe("select");
            var id = builder.GetLayout().Roads.Single().Id;
            builder.HandleHit(BuildHit.Piece(id));
            Observe("select"); // XrPrepareWorldPick emits this and consumes the world pick.
            Assert.That(tutorial.StepId, Is.EqualTo("ConnectGoal"));
            Assert.That(builder.GetLayout().Roads, Has.Count.EqualTo(1));
        }

        [Test]
        public void ReplayUsesFreshStepStateAndDoesNotErasePreviouslyEarnedMastery()
        {
            ReachTestReady();
            CompleteTest();
            tutorial.Stop(true);
            tutorial.Start(0);
            Assert.That(tutorial.IsComplete, Is.False);
            Assert.That(tutorial.StepId, Is.EqualTo("SelectDeck"));
            tutorial.Stop();
            Assert.That(store.IsCompleted(0), Is.True);
            Assert.That(store.WasSkipped(0), Is.True);
        }

        [Test]
        public void BridgePierLessonMustActuallyRemoveWeakSpanBeforeTesting()
        {
            StartLesson(2);
            Assert.That(BridgeRules.AnalyzeSpans(builder.Level, builder.GetLayout()).WeakPoints, Is.Not.Empty);
            Select(PieceType.Pier);
            Observe("pick");
            Assert.That(tutorial.StepId, Is.EqualTo("PlacePier"));
            Assert.That(builder.HandleHit(BuildHit.Foundation(0)).Changed, Is.True);
            Observe("pick");
            Assert.That(tutorial.StepId, Is.EqualTo("StartTest"));
            CompleteTest();
            Assert.That(store.IsCompleted(2), Is.True);
        }

        [Test]
        public void RampLessonStartsWithGapAndRequiresNewLegalRamp()
        {
            StartLesson(3);
            Assert.That(BridgeRules.ValidateBridge(builder.Level, builder.GetLayout()).CanTest, Is.False);
            Select(PieceType.Ramp);
            var slot = builder.GetRoadSlots().First(candidate => candidate.From.Y != candidate.To.Y);
            Assert.That(builder.HandleHit(BuildHit.Road(slot.Index)).Changed, Is.True);
            Observe("pick");
            Assert.That(tutorial.StepId, Is.EqualTo("StartTest"));
            CompleteTest();
            Assert.That(store.IsCompleted(3), Is.True);
        }

        [Test]
        public void TrussLessonDoesNotAcceptPartialCoverageAndRollsBackWhenUndone()
        {
            StartLesson(4);
            Select(PieceType.Truss);
            var roads = builder.GetLayout().Roads.ToArray();
            foreach (var road in roads.Take(4))
            {
                Assert.That(builder.HandleHit(BuildHit.Truss(road.Id)).Changed, Is.True);
                Observe("pick");
                Assert.That(tutorial.StepId, Is.EqualTo("CoverTrusses"));
            }
            Assert.That(tutorial.Body, Does.Contain("4 / 5"));
            Assert.That(builder.HandleHit(BuildHit.Truss(roads[4].Id)).Changed, Is.True);
            Observe("pick");
            Assert.That(tutorial.StepId, Is.EqualTo("StartTest"));
            Assert.That(builder.Undo(), Is.True);
            Observe("undo");
            Assert.That(tutorial.StepId, Is.EqualTo("CoverTrusses"));
            Assert.That(builder.Redo(), Is.True);
            Observe("redo");
            Assert.That(tutorial.StepId, Is.EqualTo("StartTest"));
            CompleteTest();
            Assert.That(store.IsCompleted(4), Is.True);
        }

        [Test]
        public void PartialTrussProgressResetsStallTimerWithoutCompletingStep()
        {
            StartLesson(4);
            Select(PieceType.Truss);
            tutorial.Tick(30, false);
            Assert.That(tutorial.AssistanceLevel, Is.EqualTo(2));
            Assert.That(builder.HandleHit(BuildHit.Truss(builder.GetLayout().Roads[0].Id)).Changed, Is.True);
            Observe("pick");
            Assert.That(tutorial.AssistanceLevel, Is.Zero);
            Assert.That(tutorial.StepId, Is.EqualTo("CoverTrusses"));
            tutorial.Tick(14, false);
            Assert.That(tutorial.AssistanceLevel, Is.Zero);
        }

        [Test]
        public void UndoOfSeedIsExplicitlyRecoverableWithoutHiddenLayoutWrites()
        {
            StartLesson(2);
            Select(PieceType.Pier);
            Assert.That(builder.Undo(), Is.True);
            Observe("undo");
            Assert.That(tutorial.RequiresPracticeReset, Is.True);
            Assert.That(tutorial.Body, Does.Contain("Redo"));
            Assert.That(builder.GetLayout().Roads, Is.Empty);
            Assert.That(builder.Redo(), Is.True);
            Observe("redo");
            Assert.That(tutorial.RequiresPracticeReset, Is.False);
            Assert.That(builder.GetLayout().Roads, Has.Count.EqualTo(6));
        }

        [Test]
        public void SpatialLessonChecksActualRotationLayerSlopeAndPlacementBeforeAdjustment()
        {
            StartLesson(5);
            Select(PieceType.Deck);
            Observe("rotate");
            Assert.That(tutorial.StepId, Is.EqualTo("Rotate"));
            builder.Rotate(1);
            Observe("rotate");
            Assert.That(tutorial.StepId, Is.EqualTo("PlaceTurnedDeck"));
            var deck = builder.GetRoadSlots().First(slot => slot.From.X == 0 && slot.From.Z == 0);
            Assert.That(builder.HandleHit(BuildHit.Road(deck.Index)).Changed, Is.True);
            Observe("pick");
            Assert.That(tutorial.StepId, Is.EqualTo("ChangeLayer"));
            Observe("layer");
            Assert.That(tutorial.StepId, Is.EqualTo("ChangeLayer"));
            Assert.That(builder.SetActiveLayer(1), Is.True);
            Observe("layer");
            Select(PieceType.Ramp);
            Observe("ramp");
            Assert.That(tutorial.StepId, Is.EqualTo("ChangeRampDirection"));
            Assert.That(builder.SetRampDelta(-1), Is.True);
            Observe("ramp");
            Assert.That(tutorial.StepId, Is.EqualTo("PlaceSpatialRamp"));
            var ramp = builder.GetRoadSlots().First(slot => slot.From.X == 1 && slot.From.Z == 1);
            Assert.That(builder.HandleHit(BuildHit.Road(ramp.Index)).Changed, Is.True);
            Observe("pick");
            Assert.That(builder.ActiveLayer, Is.Zero);
            Assert.That(tutorial.StepId, Is.EqualTo("AdjustWorkspace"));
            Observe("help");
            Assert.That(tutorial.IsComplete, Is.False);
            Observe("workspace");
            Assert.That(tutorial.IsComplete, Is.True);
            Assert.That(store.IsCompleted(5), Is.True);
        }

        [Test]
        public void UndoSpatialRampAtBottomLayerReturnsToReachableLayerStepAndRedoRecovers()
        {
            StartLesson(5);
            Select(PieceType.Deck);
            builder.Rotate(1);
            Observe("rotate");
            var deck = builder.GetRoadSlots().First(slot => slot.From.X == 0 && slot.From.Z == 0);
            Assert.That(builder.HandleHit(BuildHit.Road(deck.Index)).Changed, Is.True);
            Observe("pick");
            builder.SetActiveLayer(1);
            Observe("layer");
            Select(PieceType.Ramp);
            builder.SetRampDelta(-1);
            Observe("ramp");
            var ramp = builder.GetRoadSlots().First(slot => slot.From.X == 1 && slot.From.Z == 1);
            Assert.That(builder.HandleHit(BuildHit.Road(ramp.Index)).Changed, Is.True);
            Observe("pick");
            Assert.That(tutorial.StepId, Is.EqualTo("AdjustWorkspace"));

            Assert.That(builder.Undo(), Is.True);
            Observe("undo");
            Assert.That(builder.ActiveLayer, Is.Zero);
            Assert.That(tutorial.StepId, Is.EqualTo("ChangeLayer"), "Down-ramp placement is impossible at Y=0.");
            Assert.That(builder.Undo(), Is.True);
            Observe("undo");
            Assert.That(tutorial.StepId, Is.EqualTo("SelectDeck"));
            Assert.That(builder.Redo(), Is.True);
            Observe("redo");
            Assert.That(tutorial.StepId, Is.EqualTo("ChangeLayer"));
            Assert.That(builder.Redo(), Is.True);
            Observe("redo");
            Assert.That(tutorial.StepId, Is.EqualTo("AdjustWorkspace"));
        }

        [Test]
        public void WrongPracticeLevelCannotAdvanceCurrentLesson()
        {
            var wrongBuilder = new BuildController(LevelCatalog.Get(LevelId.Level2));
            wrongBuilder.SelectTool(PieceType.Deck);
            tutorial.Observe("tool", wrongBuilder, GamePhase.Build, TestOutcome.Pending);
            Assert.That(tutorial.StepId, Is.EqualTo("SelectDeck"));
        }

        private void StartLesson(int lesson)
        {
            store = new SkybridgeMemoryTutorialProgressStore();
            tutorial = new SkybridgeTutorialDirector(store);
            tutorial.Start(lesson);
            builder = new BuildController(LevelCatalog.Get(tutorial.PracticeLevelId));
            var seed = SkybridgeTutorialDirector.CreatePracticeLayout(lesson);
            if (seed.AllPieces().Any()) builder.ReplaceLayout(seed);
            Observe("sync");
        }

        private void Select(PieceType type)
        {
            builder.SelectTool(type);
            Observe("tool");
        }

        private void PlaceRoad(int index)
        {
            Assert.That(builder.HandleHit(BuildHit.Road(index)).Changed, Is.True);
            Observe("pick");
        }

        private void SelectFirstPiece()
        {
            builder.CancelSelection();
            Observe("select");
            var first = builder.GetLayout().Roads.First(road => road.From == builder.Level.StartAnchor);
            builder.HandleHit(BuildHit.Piece(first.Id));
            Observe("pick");
        }

        private void ReachDeletion()
        {
            Select(PieceType.Deck);
            PlaceRoad(0);
            SelectFirstPiece();
            Assert.That(tutorial.StepId, Is.EqualTo("ConnectGoal"));
        }

        private void ReachTestReady()
        {
            Select(PieceType.Deck);
            PlaceRoad(0);
            PlaceRoad(1);
            PlaceRoad(2);
            Assert.That(tutorial.StepId, Is.EqualTo("StartTest"));
        }

        private void CompleteTest()
        {
            Observe("test", GamePhase.TestPreparing);
            Observe("success", GamePhase.Success, TestOutcome.Success);
            Assert.That(tutorial.IsComplete, Is.True);
        }

        private void Observe(string action, GamePhase phase = GamePhase.Build, TestOutcome outcome = TestOutcome.Pending)
            => tutorial.Observe(action, builder, phase, outcome);
    }
}
