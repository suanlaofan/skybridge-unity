using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Skybridge.Core;
using Skybridge.Game;
using Skybridge.Presentation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Skybridge.Tests
{
    public sealed class SkybridgeXrTutorialPlayModeTests
    {
        private SkybridgeGameController game;
        private SkybridgeMemoryTutorialProgressStore progress;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            if (SkybridgeGameController.Instance == null)
                new GameObject("Skybridge_XrTutorial_Test").AddComponent<SkybridgeGameController>();
            yield return null;
            game = SkybridgeGameController.Instance;
            Assert.That(game.IsInitialized, Is.True);
            game.VehicleSpeedMultiplier = 40f;
            game.EnableXrPreview(true);
            // Do not clear official saves or write the player's tutorial flags from this fixture.
            progress = new SkybridgeMemoryTutorialProgressStore();
            var director = new SkybridgeTutorialDirector(progress);
            typeof(SkybridgeGameController).GetField("xrTutorial", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(game, director);
            game.XrWorkbench.CloseModal();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (game != null)
            {
                if (game.XrIsTutorialActive) game.XrDispatch("tutorial-exit");
                game.StopCurrentTest();
                Object.Destroy(game.gameObject);
            }
            yield return null;
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator RealBasicPracticeCompletesAndRestoresOriginalBuilderHistoryAndScore()
        {
            game.LoadLevel(LevelId.Level1, true);
            var originalBuilder = game.Builder;
            originalBuilder.SelectTool(PieceType.Deck);
            Assert.That(originalBuilder.HandleHit(BuildHit.Road(0)).Changed, Is.True);
            Assert.That(originalBuilder.Undo(), Is.True);
            Assert.That(originalBuilder.CanRedo, Is.True);
            var originalScore = GetField("saveData");

            game.XrDispatch("tutorial-start");
            Assert.That(game.Builder, Is.Not.SameAs(originalBuilder));
            game.XrDispatch("tool", (int)PieceType.Deck);
            CommitRoad(0);
            Assert.That(game.XrTutorial.StepId, Is.EqualTo("ConnectGoal"));
            CommitRoad(1);
            CommitRoad(2);
            Assert.That(game.XrTutorial.StepId, Is.EqualTo("StartTest"));

            var testButton = game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>().Single(button => button.name == "Test");
            var rect = testButton.GetComponent<RectTransform>();
            var origin = game.XrCamera.transform.position;
            game.XrPointer.ClickForAutomation(new Ray(origin, rect.TransformPoint(rect.rect.center) - origin));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Validating).Or.EqualTo(GamePhase.TestPreparing).Or.EqualTo(GamePhase.VehicleRunning), "One click on the original Test button must start without another step.");
            Assert.That(game.XrWorkbench.HasModal, Is.False);
            var deadline = Time.realtimeSinceStartup + 8f;
            while (game.Phase != GamePhase.Success && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Success));
            Assert.That(game.XrTutorial.IsComplete, Is.True);
            Assert.That(progress.IsCompleted(0), Is.True);
            Assert.That(GetField("saveData"), Is.SameAs(originalScore), "Practice completion must not replace formal medal data.");
            game.XrDispatch("tutorial-exit");
            Assert.That(game.Builder, Is.SameAs(originalBuilder));
            Assert.That(game.Builder.CanRedo, Is.True);
            Assert.That(game.Builder.GetLayout().Roads, Is.Empty);
            Assert.That(game.XrIsTutorialActive, Is.False);
        }

        [UnityTest]
        public IEnumerator WeakBridgeStartsImmediatelyWithoutAnExtraConfirmation()
        {
            game.XrDispatch("tutorial-replay", 2);
            game.XrAdjustView(75, -45, 0.1f);
            Assert.That(BridgeRules.AnalyzeSpans(game.CurrentLevel, game.Builder.GetLayout()).WeakPoints, Is.Not.Empty);
            var button = game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>().Single(item => item.name == "Test");
            var rect = button.GetComponent<RectTransform>();
            var origin = game.XrCamera.transform.position;
            game.XrPointer.ClickForAutomation(new Ray(origin, rect.TransformPoint(rect.rect.center) - origin));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Validating).Or.EqualTo(GamePhase.TestPreparing).Or.EqualTo(GamePhase.VehicleRunning));
            Assert.That(game.XrWorkbench.HasModal, Is.False);
            Assert.That(game.XrTutorial.IsComplete, Is.False);
            var deadline = Time.realtimeSinceStartup + 10;
            var viewChangedDuringFall = false;
            while ((game.Phase != GamePhase.Build || game.LastTestOutcome == TestOutcome.Pending) && Time.realtimeSinceStartup < deadline)
            {
                if (!viewChangedDuringFall && (game.Phase == GamePhase.Breaking || game.Phase == GamePhase.VehicleFalling))
                {
                    game.XrAdjustView(60, 20, -0.1f);
                    game.XrPanView(new Vector2(0.1f, 0.1f));
                    viewChangedDuringFall = true;
                }
                yield return null;
            }
            Assert.That(viewChangedDuringFall, Is.True);
            Assert.That(game.LastTestOutcome, Is.EqualTo(TestOutcome.Failure), "Changing view must not turn a broken bridge into a runtime error.");
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Build));
        }

        [UnityTest]
        public IEnumerator SpatialPracticeAdjustmentRequiresRealTransformAndCanReturnFromModal()
        {
            game.XrDispatch("tutorial-replay", 5);
            game.XrDispatch("tool", (int)PieceType.Deck);
            game.XrDispatch("rotate", 1);
            var deck = game.Builder.GetRoadSlots().First(slot => slot.From.X == 0 && slot.From.Z == 0);
            CommitRoad(deck.Index);
            game.XrDispatch("layer", 1);
            game.XrDispatch("tool", (int)PieceType.Ramp);
            game.XrDispatch("ramp", -1);
            var ramp = game.Builder.GetRoadSlots().First(slot => slot.From.X == 1 && slot.From.Z == 1);
            CommitRoad(ramp.Index);
            Assert.That(game.XrTutorial.StepId, Is.EqualTo("AdjustWorkspace"));
            game.XrDispatch("adjust");
            Assert.That(game.XrWorkbench.IsAdjustmentOpen, Is.True);
            Assert.That(game.XrWorkbench.BlocksWorldInput, Is.False, "View adjustment remains available alongside the original UI.");
            var originalPosition = game.XrWorldRoot.position;
            game.XrDispatch("workspace-reset");
            Assert.That(game.XrTutorial.IsComplete, Is.False, "Resetting an unchanged workspace is not an actual adjustment.");
            game.XrDispatch("workspace-x", 1);
            Assert.That(Vector3.Distance(originalPosition, game.XrWorldRoot.position), Is.GreaterThan(0.01f));
            Assert.That(game.XrTutorial.IsComplete, Is.True);
            game.XrDispatch("continue");
            Assert.That(game.XrWorkbench.HasModal, Is.False, "The tutorial gate must not swallow the adjustment return button.");
            Assert.That(progress.IsCompleted(5), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OptionalSelectionAndCancelDoNotBlockBuildingAndExitRestoresEntry()
        {
            var originalBuilder = game.Builder;
            game.XrDispatch("tutorial-start");
            game.XrDispatch("tool", (int)PieceType.Deck);
            CommitRoad(0);
            game.XrDispatch("select");
            CommitPiece(game.Builder.GetLayout().Roads.Single().Id);
            Assert.That(game.XrTutorial.StepId, Is.EqualTo("ConnectGoal"));
            game.XrDispatch("cancel");
            Assert.That(game.Builder.SelectedPieceId, Is.Null);
            Assert.That(game.XrTutorial.StepId, Is.EqualTo("ConnectGoal"));
            Assert.That(game.XrTutorial.Allows("help"), Is.True);
            game.XrDispatch("tutorial-skip");
            Assert.That(game.Builder, Is.SameAs(originalBuilder));
            Assert.That(progress.IsCompleted(0), Is.False);
            Assert.That(progress.WasSkipped(0), Is.True);
            yield return null;
        }

        private void CommitRoad(int index)
        {
            var target = game.World.GetComponentsInChildren<SkybridgePickable>(true)
                .First(candidate => candidate.Kind == PickKind.RoadTarget && candidate.Index == index && candidate.gameObject.activeInHierarchy);
            game.XrCommitWorldPick(target, target.transform.position);
        }

        private void CommitPiece(string id)
        {
            var target = game.World.GetComponentsInChildren<SkybridgePickable>(true)
                .First(candidate => candidate.Kind == PickKind.Piece && candidate.Id == id && candidate.gameObject.activeInHierarchy);
            game.XrCommitWorldPick(target, target.transform.position);
        }

        private object GetField(string name) => typeof(SkybridgeGameController)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
    }
}
