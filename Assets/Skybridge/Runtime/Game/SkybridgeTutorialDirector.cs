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
        public string Progress => IsComplete ? "练习完成" : $"{stepIndex + 1} / {steps.Length}";
        public bool RequiresPracticeReset => requireReset;
        public bool HasCompleted(int lesson) => progressStore.IsCompleted(lesson);

        public string Title => IsComplete
            ? (Lesson == 5 ? "空间操作练习完成" : "你已经完成这项练习")
            : LessonName(Lesson) + " · " + StepTitle(steps[stepIndex]);

        public string Body
        {
            get
            {
                if (IsComplete) return "选择“返回游戏”继续正式挑战，或从“教程 / 帮助”再练一次。正式桥梁与成绩将保持原样。";
                if (requireReset) return "练习所需的辅助桥段已改变。请用“撤销 / 重做”恢复辅助桥段；也可以重播本项练习。";
                var body = Lesson == 5 && steps[stepIndex] == Step.SelectDeck
                    ? "本练习依次学习转向、换层、坡向与沙盘调整。先点底部材料栏上的“标准桥板”。"
                    : StepBody(steps[stepIndex]);
                if (steps[stepIndex] == Step.ChangeRampDirection && previous != null && previous.RampDelta == -1)
                    body = "当前已经是下坡。先切一次“上坡”，再点“下坡”，观察高低端方向的变化。";
                if (Lesson == 4 && steps[stepIndex] == Step.CoverTrusses && previous != null)
                {
                    var path = BridgeRules.FindRoadPath(previous.Layout.Roads, lastBuilder.Level.StartAnchor, lastBuilder.Level.GoalAnchor);
                    var covered = path == null ? 0 : path.Segments.Count(segment =>
                        previous.Layout.Trusses.Any(truss => truss.RoadPieceId == segment.Piece.Id &&
                            truss.Snapped && truss.ConnectedAtFrom && truss.ConnectedAtTo));
                    body += $" 当前覆盖 {covered} / 5 段。";
                }
                // The world card displays one short instruction at a time; escalating help replaces text.
                if (AssistanceLevel == 1) body = StepHint(steps[stepIndex]);
                if (AssistanceLevel == 2) body = StepHint(steps[stepIndex]) + "\n指向目标，确认高亮，再扣下并释放一次扳机。";
                if (AssistanceLevel >= 3) body = StepHint(steps[stepIndex]) + "\n可重试本步或跳过练习。跳过不会标记已掌握。";
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

        private static string LessonName(int lesson) => lesson == 2 ? "桥墩练习" : lesson == 3 ? "坡道练习" : lesson == 4 ? "桁架练习" : lesson == 5 ? "三维练习" : "基础练习";

        private static string StepTitle(Step step)
        {
            switch (step)
            {
                case Step.SelectDeck: case Step.SelectDeckAgain: return "选择桥板";
                case Step.PlaceFirstDeck: return "放下第一块";
                case Step.SelectFirstDeck: return "选中已放桥板";
                case Step.DeleteFirstDeck: return "删除并返还材料";
                case Step.UndoDeletion: return "撤销删除";
                case Step.ConnectGoal: return "连接对岸";
                case Step.StartTest: return "测试";
                case Step.AwaitSuccess: return "观察小车到达终点";
                case Step.SelectPier: return "选择桥墩";
                case Step.PlacePier: return "在中央支撑长桥";
                case Step.SelectRamp: return "选择坡道";
                case Step.PlaceRamp: return "接上高处终点";
                case Step.SelectTruss: return "选择桁架";
                case Step.CoverTrusses: return "连续覆盖整段桥面";
                case Step.Rotate: return "旋转九十度";
                case Step.PlaceTurnedDeck: return "沿新方向放置";
                case Step.ChangeLayer: return "切换建造楼层";
                case Step.ChangeRampDirection: return "切换下坡方向";
                case Step.PlaceSpatialRamp: return "放下坡道并跟随楼层";
                case Step.AdjustWorkspace: return "调整沙盘";
                default: return string.Empty;
            }
        }

        private static string StepBody(Step step)
        {
            switch (step)
            {
                case Step.SelectDeck: return "连接两座岛，让小车安全到达对岸。先点底部“标准桥板”。";
                case Step.SelectDeckAgain: return "桥板与库存已恢复。切回“标准桥板”，继续搭桥。";
                case Step.PlaceFirstDeck: return "指向起点旁第一格空位。看到“可放置”后，单击鼠标左键或扣一下右手扳机。";
                case Step.SelectFirstDeck: return "再次点击已选材料取消选材，再点击刚放下的桥板。";
                case Step.DeleteFirstDeck: return "点右侧工具栏的垃圾桶图标“删除”。材料会返还，随后我们会把它恢复。";
                case Step.UndoDeletion: return "点“撤销”，恢复刚才的桥板与材料数量。";
                case Step.ConnectGoal: return "继续用桥板连到对岸。道路应连续，支撑应安全。";
                case Step.StartTest: return "道路已经连通、跨度安全。点“测试”，小车会自动出发。";
                case Step.AwaitSuccess: return "观察小车安全驶入终点。需要修改时，可随时点“停止测试”返回建造。";
                case Step.SelectPier: return "普通无支撑跨度最多三块桥板。这座桥有六块，先点“桥墩”。";
                case Step.PlacePier: return "把桥墩安装在中央基础座，使六格桥分成两个三格安全跨度。";
                case Step.SelectRamp: return "坡道连接一格高差。点“坡道”，注意预览中的高端、低端和方向。";
                case Step.PlaceRamp: return "在现有道路缺口放一块上坡，接上高一格的桥面。中央桥墩已备好。";
                case Step.SelectTruss: return "五格跨度需要完整连续的桁架。点“桁架”。";
                case Step.CoverTrusses: return "逐块指向现有桥板安装桁架，覆盖全部五段并连接两端硬支撑。";
                case Step.Rotate: return "点右侧工具栏的旋转箭头，看看朝向从 X 轴变到 Z 轴。";
                case Step.PlaceTurnedDeck: return "沿新的 Z 方向，在当前层任意合法空位放下一块桥板。";
                case Step.ChangeLayer: return "用楼层按钮把当前层切到 Y=1（也可用 2–9 层）。活动网格会随之变化。";
                case Step.ChangeRampDirection: return "点“下坡”，让坡道从当前层连接到低一层。当前层须高于 Y=0。";
                case Step.PlaceSpatialRamp: return "在合法空位放下一块下坡道。放置成功后，活动层会自动跟到低端。";
                case Step.AdjustWorkspace: return "现在转换一次视角或缩放，观察桥梁。鼠标右键拖动 / 滚轮；手柄左摇杆旋转、右摇杆缩放。";
                default: return string.Empty;
            }
        }

        private static string StepHint(Step step)
        {
            switch (step)
            {
                case Step.SelectDeck: case Step.SelectDeckAgain: return "点底部材料栏的“标准桥板”按钮，选中后名称会高亮。";
                case Step.PlaceFirstDeck: return "第一块应紧挨起点岛，沿对岸方向连接两个等高点。";
                case Step.SelectFirstDeck: return "再次点击黄色选中标记的材料即可退出选材。选中桥板后，右侧删除按钮会启用。";
                case Step.DeleteFirstDeck: return "只删除选中的那块；“撤销”可以恢复它。";
                case Step.UndoDeletion: return "点右侧工具栏最上方的“撤销”箭头，恢复刚才的删除。";
                case Step.ConnectGoal: return "沿起点到终点方向补齐道路，留意“可放置”预览。";
                case Step.StartTest: return "点底部材料栏上的“测试”。小车会自动行驶。";
                case Step.SelectPier: return "点“桥墩”，随后指向桥中间的基础座。";
                case Step.PlacePier: return "中央基础座位于桥梁中点，完整支柱的顶部应接到桥面。";
                case Step.SelectRamp: return "点“坡道”，确认预览显示连接高低两层的斜面。";
                case Step.PlaceRamp: return "缺口在低处桥面与高处桥面之间；上坡高端朝向终点。";
                case Step.SelectTruss: return "点“桁架”；它附着于已经放好的桥板。";
                case Step.CoverTrusses: return "每块桥板都要覆盖；只有零散几块桁架不会提高整段安全跨度。";
                case Step.Rotate: return "右侧两枚旋转箭头可改变朝向；点一次后应从 X 轴变为 Z 轴。";
                case Step.PlaceTurnedDeck: return "选择桥板并朝 Z 轴方向，在当前活动层放一块等高桥板。";
                case Step.ChangeLayer: return "当前层数显示在楼层按钮旁；到顶或到底时按钮会禁用。";
                case Step.ChangeRampDirection: return "先到 Y=1 以上再选下坡；Y=0 不能继续向下。";
                case Step.PlaceSpatialRamp: return "检查预览高低端。放置后当前层应减少一层。";
                case Step.AdjustWorkspace: return "调整呈现不会改变桥梁规则或库存。可以随时恢复默认。";
                case Step.AwaitSuccess: return "小车全部进入终点并稳定后才算成功；无需操纵车辆。";
                default: return "只需右手指向和一次扳机确认；帮助与退出始终可用。";
            }
        }
    }
}
