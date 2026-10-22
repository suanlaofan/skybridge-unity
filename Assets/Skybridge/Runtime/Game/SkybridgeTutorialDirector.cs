using System;
using System.Collections.Generic;
using System.Linq;
using Skybridge.Core;

namespace Skybridge.Game
{
    /// <summary>
    /// Observes successful game actions and their resulting state. It never edits a bridge or game score.
    /// The owner supplies an isolated practice BuildController and restores its real session on exit.
    /// </summary>
    public sealed class SkybridgeTutorialDirector
    {
        private enum Step
        {
            SelectDeck, PlaceFirstDeck, SelectFirstDeck, DeleteFirstDeck, UndoDeletion,
            SelectDeckAgain, ConnectGoal, StartTest, AwaitSuccess,
            SelectPier, PlacePier, SelectRamp, PlaceRamp, SelectTruss, CoverTrusses,
            Rotate, PlaceTurnedDeck, ChangeLayer, ChangeRampDirection, PlaceSpatialRamp, AdjustWorkspace
        }

        private static readonly Step[] BasicSteps =
        {
            Step.SelectDeck, Step.PlaceFirstDeck, Step.ConnectGoal, Step.StartTest, Step.AwaitSuccess
        };
        private static readonly Step[] PierSteps = { Step.SelectPier, Step.PlacePier, Step.ConnectGoal, Step.StartTest, Step.AwaitSuccess };
        private static readonly Step[] RampSteps = { Step.SelectRamp, Step.PlaceRamp, Step.ConnectGoal, Step.StartTest, Step.AwaitSuccess };
        private static readonly Step[] TrussSteps = { Step.SelectTruss, Step.CoverTrusses, Step.StartTest, Step.AwaitSuccess };
        private static readonly Step[] SpatialSteps =
        {
            Step.SelectDeck, Step.Rotate, Step.PlaceTurnedDeck, Step.ChangeLayer,
            Step.SelectRamp, Step.ChangeRampDirection, Step.PlaceSpatialRamp, Step.AdjustWorkspace
        };

        private readonly ISkybridgeTutorialProgressStore progressStore;
        private Step[] steps = BasicSteps;
        private int stepIndex;
        private float idleSeconds;
        private BuildController lastBuilder;
        private Snapshot previous;
        private string firstDeckId;
        private string spatialDeckId;
        private string spatialRampId;
        private string beforeDeletion;
        private PieceInventory inventoryBeforeDeletion;
        private bool editPracticeCompleted;
        private bool focusedActionCompleted;
        private bool observedTestStart;
        private string testedLayout;
        private bool requireReset;
        private GamePhase lastPhase = GamePhase.Build;

        public SkybridgeTutorialDirector(ISkybridgeTutorialProgressStore progressStore = null)
        {
            this.progressStore = progressStore ?? new SkybridgeTutorialProgressStore();
        }

        public event Action Changed;
        public bool IsActive { get; private set; }
        public bool IsComplete { get; private set; }
        public int Lesson { get; private set; }
        public LevelId PracticeLevelId => GetPracticeLevelId(Lesson);
        public int AssistanceLevel { get; private set; }
        public string StepId => IsComplete ? "Complete" : steps[stepIndex].ToString();
        public string Progress => IsComplete ? "Practice complete" : $"{stepIndex + 1} / {steps.Length}";
        public bool RequiresPracticeReset => requireReset;
        public bool HasCompleted(int lesson) => progressStore.IsCompleted(lesson);

        public string Title => IsComplete
            ? (Lesson == 5 ? "3D Practice Complete" : "You've Completed This Practice")
            : LessonName(Lesson) + " · " + StepTitle(steps[stepIndex]);

        public string Body
        {
            get
            {
                if (IsComplete) return "Choose \"Back to Game\" to continue the real challenge, or practice again from \"Help\". Your real bridge and results stay unchanged.";
                if (requireReset) return "The practice's helper bridge sections have changed. Use \"Undo / Redo\" to restore them, or replay this practice.";
                var body = Lesson == 5 && steps[stepIndex] == Step.SelectDeck
                    ? "This practice covers turning, changing layers, ramp direction and adjusting the workspace. First tap \"Deck\" in the bottom material bar."
                    : StepBody(steps[stepIndex]);
                if (steps[stepIndex] == Step.ChangeRampDirection && previous != null && previous.RampDelta == -1)
                    body = "The ramp is already set to Down. Switch to \"Up\" once, then tap \"Down\" and watch the high and low ends flip.";
                if (Lesson == 4 && steps[stepIndex] == Step.CoverTrusses && previous != null)
                {
                    var path = BridgeRules.FindRoadPath(previous.Layout.Roads, lastBuilder.Level.StartAnchor, lastBuilder.Level.GoalAnchor);
                    var covered = path == null ? 0 : path.Segments.Count(segment =>
                        previous.Layout.Trusses.Any(truss => truss.RoadPieceId == segment.Piece.Id &&
                            truss.Snapped && truss.ConnectedAtFrom && truss.ConnectedAtTo));
                    body += $" Covered: {covered} / 5 sections.";
                }
                // The world card displays one short instruction at a time; escalating help replaces text.
                if (AssistanceLevel == 1) body = StepHint(steps[stepIndex]);
                if (AssistanceLevel == 2) body = StepHint(steps[stepIndex]) + "\nPoint at the target, check that it highlights, then pull and release the trigger once.";
                if (AssistanceLevel >= 3) body = StepHint(steps[stepIndex]) + "\nYou can retry this step or skip the practice. Skipping won't mark it as mastered.";
                return body;
            }
        }

        public static LevelId GetPracticeLevelId(int lesson)
        {
            switch (lesson)
            {
                case 0: return LevelId.Level1;
                case 2: return LevelId.Level2;
                case 3: return LevelId.Level3;
                case 4: return LevelId.Level4;
                case 5: return LevelId.Level5;
                default: throw new ArgumentOutOfRangeException(nameof(lesson), "Lessons are 0, 2, 3, 4 or 5.");
            }
        }

        public static BridgeLayout CreatePracticeLayout(int lesson)
        {
            var reference = LevelCatalog.Get(GetPracticeLevelId(lesson)).ReferenceSolution.Layout;
            switch (lesson)
            {
                case 2: return new BridgeLayout(reference.Roads).DeepClone();
                case 3: return new BridgeLayout(reference.Roads.Where(road => road.Type != PieceType.Ramp), reference.Piers).DeepClone();
                case 4: return new BridgeLayout(reference.Roads).DeepClone();
                default: return BridgeLayout.Empty;
            }
        }

        public void Start(int lesson = 0)
        {
            GetPracticeLevelId(lesson); // Reject an unknown lesson before changing state.
            Lesson = lesson;
            steps = lesson == 2 ? PierSteps : lesson == 3 ? RampSteps : lesson == 4 ? TrussSteps : lesson == 5 ? SpatialSteps : BasicSteps;
            stepIndex = 0;
            IsActive = true;
            IsComplete = false;
            AssistanceLevel = 0;
            idleSeconds = 0;
            lastBuilder = null;
            previous = null;
            firstDeckId = spatialDeckId = spatialRampId = beforeDeletion = testedLayout = null;
            inventoryBeforeDeletion = null;
            editPracticeCompleted = focusedActionCompleted = observedTestStart = requireReset = false;
            lastPhase = GamePhase.Build;
            Changed?.Invoke();
        }

        public void Stop(bool completed = false)
        {
            if (!IsActive) return;
            // A caller cannot turn a skip or an early exit into a completed capability.
            if (!IsComplete) progressStore.MarkSkipped(Lesson);
            IsActive = false;
            Changed?.Invoke();
        }

        public bool Allows(string action)
        {
            if (!IsActive) return true;
            action = Normalize(action);
            if (action == "cancel" || action == "help" || action == "exit" || action == "skip" ||
                action == "stop" || action == "hint" || action == "retry" || action == "replay" ||
                action == "menu" || action == "settings" || action == "workspace" || action == "sync" ||
                action == "adjust" || action == "reposition" || action == "continue" || action == "next" ||
                action == "mute" || action == "reduced-motion") return true;
            if (IsComplete) return false;
            if (lastPhase != GamePhase.Build && lastPhase != GamePhase.Success) return false;
            if (action == "undo" || action == "redo") return true;
            if (action == "test") return true; // The original Test button is never gated by lesson bookkeeping.
            if (steps[stepIndex] == Step.AwaitSuccess) return false;
            if (action == "rotate" || action == "layer" || action == "ramp") return Lesson == 5;
            return action == "tool" || action == "select" || action == "pick" || action == "delete" || action == "move" || action == "check";
        }

        /// <summary>Call after successful semantic actions, and once with "sync" after creating the practice builder.</summary>
        public void Observe(string action, BuildController builder, GamePhase phase, TestOutcome outcome)
        {
            if (!IsActive || IsComplete || builder == null || builder.Level.Id != PracticeLevelId) return;
            action = Normalize(action);
            lastBuilder = builder;
            lastPhase = phase;
            var current = new Snapshot(builder);
            var before = previous;
            var previousIndex = stepIndex;
            var previousReset = requireReset;
            // Clicking the existing Test button starts immediately, including a deliberate weak-span test.
            if (action == "test" && (phase == GamePhase.TestPreparing || phase == GamePhase.VehicleRunning) &&
                outcome == TestOutcome.Pending && BridgeRules.ValidateBridge(builder.Level, current.Layout).CanTest)
            {
                observedTestStart = true;
                testedLayout = current.Signature;
                SetStep(Step.AwaitSuccess);
            }
            if (Lesson == 5 && action == "success" && observedTestStart && phase == GamePhase.Success &&
                outcome == TestOutcome.Success && testedLayout == current.Signature && ReadyForTest(builder, current.Layout))
            {
                IsComplete = true;
                progressStore.MarkCompleted(Lesson);
                previous = current;
                Changed?.Invoke();
                return;
            }
            if (action == "pick" && before != null &&
                ((steps[stepIndex] == Step.CoverTrusses && current.Layout.Trusses.Count > before.Layout.Trusses.Count) ||
                 (steps[stepIndex] == Step.ConnectGoal && current.Layout.Roads.Count > before.Layout.Roads.Count)))
            {
                idleSeconds = 0;
                AssistanceLevel = 0;
            }
            if (action != "sync" && !requireReset &&
                Qualifies(steps[stepIndex], action, builder, before, current, phase, outcome))
            {
                var completedStep = steps[stepIndex];
                if (completedStep == Step.PlacePier || completedStep == Step.PlaceRamp || completedStep == Step.CoverTrusses)
                    focusedActionCompleted = true;
                stepIndex++;
                AssistanceLevel = 0;
                idleSeconds = 0;
                if (stepIndex == steps.Length)
                {
                    IsComplete = true;
                    stepIndex = steps.Length - 1;
                    progressStore.MarkCompleted(Lesson);
                }
            }
            // Reconcile after the action: a deliberate deletion must first enter its undo step.
            // Test failure removes transient world objects; wait for the owner's restored build snapshot.
            if (!IsComplete && phase == GamePhase.Build) Reconcile(builder, current);
            if (!IsComplete && steps[stepIndex] == Step.ConnectGoal && ReadyForTest(builder, current.Layout))
                SetStep(Step.StartTest);
            previous = current;
            if (stepIndex != previousIndex || IsComplete || previousReset != requireReset || action != "sync") Changed?.Invoke();
        }

        public void Tick(float deltaSeconds, bool paused)
        {
            if (!IsActive || IsComplete || paused || deltaSeconds <= 0 || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return;
            idleSeconds += deltaSeconds;
            var next = idleSeconds >= 60 ? 3 : idleSeconds >= 30 ? 2 : idleSeconds >= 15 ? 1 : 0;
            if (next <= AssistanceLevel) return;
            AssistanceLevel = next;
            Changed?.Invoke();
        }

        public void RequestHint()
        {
            if (!IsActive || IsComplete) return;
            AssistanceLevel = Math.Min(3, AssistanceLevel + 1);
            Changed?.Invoke();
        }

        public void RetryStep()
        {
            if (!IsActive || IsComplete) return;
            // No hidden edits or inventory resets: retry explains the next satisfiable action.
            if (lastBuilder != null && lastPhase == GamePhase.Build) Reconcile(lastBuilder, new Snapshot(lastBuilder));
            AssistanceLevel = 1;
            idleSeconds = 0;
            Changed?.Invoke();
        }

        private bool Qualifies(Step step, string action, BuildController builder, Snapshot before, Snapshot now, GamePhase phase, TestOutcome outcome)
        {
            if (step != Step.StartTest && step != Step.AwaitSuccess && phase != GamePhase.Build) return false;
            switch (step)
            {
                case Step.SelectDeck:
                case Step.SelectDeckAgain:
                    return action == "tool" && builder.ActiveTool == PieceType.Deck && builder.SelectedPieceId == null;
                case Step.PlaceFirstDeck:
                    var first = NewRoad(before, now, PieceType.Deck);
                    if (action != "pick" || first == null || !IsFirstEdge(builder.Level, first)) return false;
                    firstDeckId = first.Id;
                    return true;
                case Step.SelectFirstDeck:
                    return (action == "select" || action == "pick") && builder.ActiveTool == null &&
                        builder.SelectedPieceId == firstDeckId && now.Layout.Roads.Any(road => road.Id == firstDeckId);
                case Step.DeleteFirstDeck:
                    if (action != "delete" || before == null || before.SelectedPieceId != firstDeckId ||
                        !before.Layout.Roads.Any(road => road.Id == firstDeckId) || now.Layout.Roads.Any(road => road.Id == firstDeckId) ||
                        now.Inventory.Deck != before.Inventory.Deck + 1) return false;
                    beforeDeletion = before.Signature;
                    inventoryBeforeDeletion = before.Inventory;
                    return true;
                case Step.UndoDeletion:
                    if (action != "undo" || now.Signature != beforeDeletion || !now.Inventory.Equals(inventoryBeforeDeletion)) return false;
                    editPracticeCompleted = true;
                    return true;
                case Step.ConnectGoal:
                    // Can already be connected after the focused pier/ramp action; a new semantic action confirms it.
                    return IsBuildAction(action) && ReadyForTest(builder, now.Layout);
                case Step.StartTest:
                    if (action != "test" || (phase != GamePhase.TestPreparing && phase != GamePhase.VehicleRunning) ||
                        outcome != TestOutcome.Pending || !ReadyForTest(builder, now.Layout)) return false;
                    observedTestStart = true;
                    testedLayout = now.Signature;
                    return true;
                case Step.AwaitSuccess:
                    return action == "success" && observedTestStart && phase == GamePhase.Success && outcome == TestOutcome.Success &&
                        testedLayout == now.Signature && ReadyForTest(builder, now.Layout);
                case Step.SelectPier:
                    return action == "tool" && builder.ActiveTool == PieceType.Pier;
                case Step.PlacePier:
                    return action == "pick" && before != null && now.Inventory.Pier == before.Inventory.Pier - 1 &&
                        now.Layout.Piers.Any(pier => !before.Layout.Piers.Any(old => old.Id == pier.Id) &&
                            builder.Level.FoundationPads.Contains(pier.Foundation) && pier.SnappedBase && pier.SnappedTop) &&
                        ReadyForTest(builder, now.Layout);
                case Step.SelectRamp:
                    return action == "tool" && builder.ActiveTool == PieceType.Ramp;
                case Step.PlaceRamp:
                    return action == "pick" && NewRoad(before, now, PieceType.Ramp) != null && ReadyForTest(builder, now.Layout);
                case Step.SelectTruss:
                    return action == "tool" && builder.ActiveTool == PieceType.Truss;
                case Step.CoverTrusses:
                    return action == "pick" && before != null && now.Inventory.Truss == before.Inventory.Truss - 1 &&
                        now.Layout.Trusses.Count == before.Layout.Trusses.Count + 1 && ReadyForTest(builder, now.Layout);
                case Step.Rotate:
                    return action == "rotate" && before != null && builder.ActiveTool == PieceType.Deck &&
                        QuarterTurnChanged(before.Rotation, now.Rotation);
                case Step.PlaceTurnedDeck:
                    var deck = NewRoad(before, now, PieceType.Deck);
                    if (action != "pick" || deck == null || deck.From.X != deck.To.X || Math.Abs(deck.To.Z - deck.From.Z) != 1) return false;
                    spatialDeckId = deck.Id;
                    return true;
                case Step.ChangeLayer:
                    return action == "layer" && before != null && now.Layer != before.Layer && now.Layer > 0 &&
                        now.Layer < builder.Level.BuildVolume.Max.Y;
                case Step.ChangeRampDirection:
                    return action == "ramp" && before != null && before.RampDelta != now.RampDelta && now.RampDelta == -1 && now.Layer > 0;
                case Step.PlaceSpatialRamp:
                    var ramp = NewRoad(before, now, PieceType.Ramp);
                    if (action != "pick" || ramp == null || ramp.To.Y != ramp.From.Y - 1 || now.Layer != ramp.To.Y) return false;
                    spatialRampId = ramp.Id;
                    return true;
                case Step.AdjustWorkspace:
                    // Workbench transforms belong to the presentation layer; the owner emits this only after a real adjustment.
                    return action == "workspace" && HasRoad(now.Layout, spatialDeckId) && HasRoad(now.Layout, spatialRampId);
                default: return false;
            }
        }

        private void Reconcile(BuildController builder, Snapshot now)
        {
            requireReset = false;
            if (Lesson == 2 || Lesson == 3 || Lesson == 4)
            {
                var seed = CreatePracticeLayout(Lesson);
                requireReset = seed.Roads.Any(road => !now.Layout.Roads.Any(piece => SameEdge(road, piece))) ||
                    seed.Piers.Any(pier => !now.Layout.Piers.Any(piece => piece.Top == pier.Top && piece.Foundation == pier.Foundation));
            }
            var step = steps[stepIndex];
            if (Lesson == 0)
            {
                if (ReadyForTest(builder, now.Layout) && step != Step.AwaitSuccess) SetStep(Step.StartTest);
                else if (step == Step.PlaceFirstDeck && HasRoad(now.Layout, firstDeckId)) SetStep(Step.ConnectGoal);
                else if (stepIndex >= 2 && !HasRoad(now.Layout, firstDeckId)) SetStep(Step.PlaceFirstDeck);
                else if ((step == Step.StartTest || step == Step.AwaitSuccess) && !ReadyForTest(builder, now.Layout)) SetStep(Step.ConnectGoal);
            }
            if ((Lesson == 2 || Lesson == 3 || Lesson == 4) &&
                (step == Step.StartTest || step == Step.AwaitSuccess || step == Step.ConnectGoal) && !ReadyForTest(builder, now.Layout))
            {
                SetStep(Lesson == 2 ? Step.PlacePier : Lesson == 3 ? Step.PlaceRamp : Step.CoverTrusses);
            }
            if (focusedActionCompleted && (step == Step.PlacePier || step == Step.PlaceRamp || step == Step.CoverTrusses) &&
                ReadyForTest(builder, now.Layout)) SetStep(Step.StartTest);
            if (step == Step.AwaitSuccess && lastPhase == GamePhase.Build)
            {
                observedTestStart = false;
                testedLayout = null;
                if (ReadyForTest(builder, now.Layout)) SetStep(Step.StartTest);
            }
            if (Lesson == 5)
            {
                var hasDeck = HasRoad(now.Layout, spatialDeckId);
                var hasRamp = HasRoad(now.Layout, spatialRampId);
                if (stepIndex >= 3 && !hasDeck) SetStep(Step.SelectDeck);
                else if (hasDeck && hasRamp) SetStep(Step.AdjustWorkspace);
                else if (hasDeck && stepIndex < 3) SetStep(Step.ChangeLayer);
                else if (steps[stepIndex] == Step.AdjustWorkspace && !hasRamp)
                    SetStep(now.Layer > 0 ? Step.PlaceSpatialRamp : Step.ChangeLayer);
                if ((steps[stepIndex] == Step.ChangeRampDirection || steps[stepIndex] == Step.PlaceSpatialRamp) && now.Layer == 0)
                    SetStep(Step.ChangeLayer);
                if (steps[stepIndex] == Step.ChangeRampDirection && now.Layer == builder.Level.BuildVolume.Max.Y)
                    SetStep(Step.ChangeLayer);
            }
        }

        private void SetStep(Step step)
        {
            var index = Array.IndexOf(steps, step);
            if (index < 0 || index == stepIndex) return;
            stepIndex = index;
            idleSeconds = 0;
            AssistanceLevel = 0;
        }

        private static bool ReadyForTest(BuildController builder, BridgeLayout layout)
        {
            var analysis = BridgeRules.AnalyzeSpans(builder.Level, layout);
            return analysis.Validation.CanTest && analysis.WeakPoints.Count == 0;
        }

        private static bool IsBuildAction(string action) => action == "pick" || action == "tool" || action == "undo" || action == "redo";
        private static bool HasRoad(BridgeLayout layout, string id) => id != null && layout.Roads.Any(road => road.Id == id);
        private static bool SameEdge(RoadPiecePlacement a, RoadPiecePlacement b) => a.Type == b.Type &&
            ((a.From == b.From && a.To == b.To) || (a.Type == PieceType.Deck && a.From == b.To && a.To == b.From));
        private static bool QuarterTurnChanged(int before, int after) => (after - before + 4) % 4 == 1 || (before - after + 4) % 4 == 1;
        private static bool IsFirstEdge(LevelDefinition level, RoadPiecePlacement road)
        {
            var expected = level.ReferenceSolution.Layout.Roads.First();
            return SameEdge(expected, road);
        }

        private static RoadPiecePlacement NewRoad(Snapshot before, Snapshot now, PieceType type)
        {
            if (before == null || now.Inventory[type] != before.Inventory[type] - 1 || now.Layout.Roads.Count != before.Layout.Roads.Count + 1) return null;
            return now.Layout.Roads.FirstOrDefault(road => road.Type == type && road.Snapped && !before.Layout.Roads.Any(old => old.Id == road.Id));
        }

        private static string Normalize(string action)
        {
            if (string.IsNullOrEmpty(action)) return string.Empty;
            var value = action.Trim().ToLowerInvariant();
            if (value.StartsWith("workspace-", StringComparison.Ordinal)) return "workspace";
            var separator = value.IndexOf(':');
            return separator < 0 ? value : value.Substring(0, separator);
        }

        private sealed class Snapshot
        {
            public Snapshot(BuildController builder)
            {
                Layout = builder.GetLayout();
                Inventory = builder.GetInventoryRemaining();
                SelectedPieceId = builder.SelectedPieceId;
                Rotation = builder.RotationQuarterTurns;
                Layer = builder.ActiveLayer;
                RampDelta = builder.RampDelta;
                Signature = LayoutSignature(Layout);
            }
            public BridgeLayout Layout { get; }
            public PieceInventory Inventory { get; }
            public string SelectedPieceId { get; }
            public int Rotation { get; }
            public int Layer { get; }
            public int RampDelta { get; }
            public string Signature { get; }
        }

        private static string LayoutSignature(BridgeLayout layout)
        {
            var roads = layout.Roads.Select(p => $"R:{p.Id}:{p.Type}:{p.From}:{p.To}:{p.Snapped}");
            var piers = layout.Piers.Select(p => $"P:{p.Id}:{p.Top}:{p.Foundation}:{p.SnappedTop}:{p.SnappedBase}");
            var trusses = layout.Trusses.Select(p => $"T:{p.Id}:{p.RoadPieceId}:{p.Snapped}:{p.ConnectedAtFrom}:{p.ConnectedAtTo}");
            return string.Join("|", roads.Concat(piers).Concat(trusses).OrderBy(value => value, StringComparer.Ordinal));
        }

        private static string LessonName(int lesson) => lesson == 2 ? "Pier Practice" : lesson == 3 ? "Ramp Practice" : lesson == 4 ? "Truss Practice" : lesson == 5 ? "3D Practice" : "Basics";

        private static string StepTitle(Step step)
        {
            switch (step)
            {
                case Step.SelectDeck: case Step.SelectDeckAgain: return "Select a Deck";
                case Step.PlaceFirstDeck: return "Place the First Deck";
                case Step.SelectFirstDeck: return "Select the Placed Deck";
                case Step.DeleteFirstDeck: return "Delete and Refund";
                case Step.UndoDeletion: return "Undo the Delete";
                case Step.ConnectGoal: return "Reach the Far Side";
                case Step.StartTest: return "Test";
                case Step.AwaitSuccess: return "Watch the Truck Arrive";
                case Step.SelectPier: return "Select a Pier";
                case Step.PlacePier: return "Support the Middle";
                case Step.SelectRamp: return "Select a Ramp";
                case Step.PlaceRamp: return "Reach the High Goal";
                case Step.SelectTruss: return "Select a Truss";
                case Step.CoverTrusses: return "Cover the Whole Deck";
                case Step.Rotate: return "Rotate 90°";
                case Step.PlaceTurnedDeck: return "Place in the New Direction";
                case Step.ChangeLayer: return "Change Build Layer";
                case Step.ChangeRampDirection: return "Set Ramp to Down";
                case Step.PlaceSpatialRamp: return "Place Ramp, Follow the Layer";
                case Step.AdjustWorkspace: return "Adjust the Workspace";
                default: return string.Empty;
            }
        }

        private static string StepBody(Step step)
        {
            switch (step)
            {
                case Step.SelectDeck: return "Connect the two islands so the truck reaches the far side safely. First tap \"Deck\" at the bottom.";
                case Step.SelectDeckAgain: return "Decks and inventory are restored. Switch back to \"Deck\" and keep building.";
                case Step.PlaceFirstDeck: return "Point at the first empty cell next to the start. When you see \"Can place\", left-click or pull the right trigger once.";
                case Step.SelectFirstDeck: return "Click the selected material again to deselect it, then click the deck you just placed.";
                case Step.DeleteFirstDeck: return "Tap the trash-can \"Delete\" icon in the right toolbar. The material is refunded, and we'll restore it next.";
                case Step.UndoDeletion: return "Tap \"Undo\" to bring back the deck and the material count.";
                case Step.ConnectGoal: return "Keep laying decks to the far side. The road must be continuous and the supports safe.";
                case Step.StartTest: return "The road is connected and the spans are safe. Tap \"Test\" and the truck sets off on its own.";
                case Step.AwaitSuccess: return "Watch the truck drive safely into the goal. To make changes, tap \"Stop\" anytime to return to building.";
                case Step.SelectPier: return "An unsupported span holds at most three decks. This bridge has six, so tap \"Pier\" first.";
                case Step.PlacePier: return "Install the pier on the central foundation to split the six-cell bridge into two safe three-cell spans.";
                case Step.SelectRamp: return "Ramps bridge a one-level height difference. Tap \"Ramp\" and note the high end, low end and direction in the preview.";
                case Step.PlaceRamp: return "Place an up ramp in the gap in the road to reach the deck one level higher. The central pier is ready.";
                case Step.SelectTruss: return "A five-cell span needs a full, continuous truss. Tap \"Truss\".";
                case Step.CoverTrusses: return "Point at each existing deck to add a truss, covering all five sections and tying into the solid supports at both ends.";
                case Step.Rotate: return "Tap a rotate arrow in the right toolbar and watch the direction change from the X axis to the Z axis.";
                case Step.PlaceTurnedDeck: return "Place a deck along the new Z direction in any valid empty cell on the current layer.";
                case Step.ChangeLayer: return "Use the layer buttons to switch the current layer to Y=1 (layers 2–9 also work). The active grid changes with it.";
                case Step.ChangeRampDirection: return "Tap \"Down\" so the ramp links the current layer to the one below. The current layer must be above Y=0.";
                case Step.PlaceSpatialRamp: return "Place a down ramp in a valid empty cell. Once it's placed, the active layer follows to the low end.";
                case Step.AdjustWorkspace: return "Now rotate or zoom the view once to look at the bridge. Mouse: right-drag / scroll wheel. Controller: left stick rotates, right stick zooms.";
                default: return string.Empty;
            }
        }

        private static string StepHint(Step step)
        {
            switch (step)
            {
                case Step.SelectDeck: case Step.SelectDeckAgain: return "Tap the \"Deck\" button in the bottom material bar. Its name highlights when selected.";
                case Step.PlaceFirstDeck: return "The first deck goes right next to the start island, linking two points of equal height toward the far side.";
                case Step.SelectFirstDeck: return "Click the material with the yellow selection mark again to stop placing. With a deck selected, the Delete button on the right becomes active.";
                case Step.DeleteFirstDeck: return "Delete only the selected piece; \"Undo\" can bring it back.";
                case Step.UndoDeletion: return "Tap the \"Undo\" arrow at the top of the right toolbar to reverse the delete.";
                case Step.ConnectGoal: return "Fill in the road from start to goal, watching for the \"Can place\" preview.";
                case Step.StartTest: return "Tap \"Test\" next to the bottom material bar. The truck drives on its own.";
                case Step.SelectPier: return "Tap \"Pier\", then point at the foundation in the middle of the bridge.";
                case Step.PlacePier: return "The central foundation sits at the bridge's midpoint; the top of the full column should meet the deck.";
                case Step.SelectRamp: return "Tap \"Ramp\" and check that the preview shows a slope linking the low and high levels.";
                case Step.PlaceRamp: return "The gap is between the low deck and the high deck; the up ramp's high end faces the goal.";
                case Step.SelectTruss: return "Tap \"Truss\"; it attaches to decks you've already placed.";
                case Step.CoverTrusses: return "Cover every deck; a few scattered trusses won't make the whole span safe.";
                case Step.Rotate: return "The two rotate arrows on the right change direction; one tap should switch from the X axis to the Z axis.";
                case Step.PlaceTurnedDeck: return "Select a deck, face it along the Z axis, and place a level deck on the current active layer.";
                case Step.ChangeLayer: return "The current layer shows next to the layer buttons; they're disabled at the top or bottom.";
                case Step.ChangeRampDirection: return "Move to Y=1 or higher before choosing Down; you can't go below Y=0.";
                case Step.PlaceSpatialRamp: return "Check the preview's high and low ends. After placing, the current layer drops by one.";
                case Step.AdjustWorkspace: return "Adjusting the view doesn't change the bridge rules or inventory. You can restore the default anytime.";
                case Step.AwaitSuccess: return "It only counts once the whole truck is inside the goal and stable; no need to steer it.";
                default: return "Just point with your right hand and confirm with one trigger pull; Help and Exit are always available.";
            }
        }
    }
}
