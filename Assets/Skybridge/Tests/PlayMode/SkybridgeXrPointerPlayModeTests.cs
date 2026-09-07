using System.Collections;
using System.Linq;
using NUnit.Framework;
using Skybridge.Core;
using Skybridge.Game;
using Skybridge.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Skybridge.Tests
{
    public sealed class SkybridgeXrPointerPlayModeTests
    {
        private SkybridgeGameController game;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (SkybridgeGameController.Instance != null) Object.Destroy(SkybridgeGameController.Instance.gameObject);
            yield return null;
            game = new GameObject("XrPointerTest").AddComponent<SkybridgeGameController>();
            yield return null;
            game.EnableXrPreview(true);
            game.XrWorkbench.CloseModal();
            game.enabled = false; // Feed an explicit controller stream instead of editor mouse input.
            yield return null;
            Canvas.ForceUpdateCanvases();
            Physics.SyncTransforms();
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (game != null) Object.Destroy(game.gameObject);
            yield return null;
            Time.timeScale = 1f;
        }
        private Button Button(string name) => game.Hud.GetComponentsInChildren<Button>(true).First(item => item.name == name);
        private Ray ButtonRay(string name)
        {
            var rect = Button(name).GetComponent<RectTransform>();
            var target = rect.TransformPoint(rect.rect.center);
            var origin = game.XrCamera.transform.position + new Vector3(0.2f, -0.15f, 0f);
            return new Ray(origin, target - origin);
        }

        [UnityTest]
        public IEnumerator UiHoverAndReleaseSelectToolWithoutWorldPlacement()
        {
            game.XrDispatch("select");
            var ray = ButtonRay("Deck");
            game.XrPointer.Process(ray, true, false);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(game.XrPointer.HoverTarget), Is.EqualTo(Button("Deck").gameObject));
            game.XrPointer.Process(ray, true, true);
            Assert.That(game.Builder.ActiveTool, Is.Null, "Press alone must not commit.");
            for (var i = 0; i < 10; i++) game.XrPointer.Process(ray, true, true);
            game.XrPointer.Process(ray, true, false);
            Assert.That(game.Builder.ActiveTool, Is.EqualTo(PieceType.Deck));
            Assert.That(game.Builder.GetCost(), Is.Zero, "Clicking UI must not place a piece behind it.");
            Assert.That(game.XrPointer.CommittedClicks, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator LostTrackingCancelsCaptureAndRequiresFreshPress()
        {
            game.XrDispatch("select");
            var ray = ButtonRay("Deck");
            game.XrPointer.Process(ray, true, false);
            game.XrPointer.Process(ray, true, true);
            game.XrPointer.Process(ray, false, true);
            game.XrPointer.Process(ray, true, true);
            game.XrPointer.Process(ray, true, false);
            Assert.That(game.Builder.ActiveTool, Is.Null);
            Assert.That(game.XrPointer.CommittedClicks, Is.Zero);
            Canvas.ForceUpdateCanvases();
            game.XrPointer.ClickForAutomation(ray);
            Assert.That(game.Builder.ActiveTool, Is.EqualTo(PieceType.Deck));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReleaseOverDifferentButtonCancelsClick()
        {
            game.XrDispatch("select");
            game.XrPointer.Process(ButtonRay("Deck"), true, false);
            game.XrPointer.Process(ButtonRay("Deck"), true, true);
            game.XrPointer.Process(ButtonRay("TutorialHelp"), true, false);
            Assert.That(game.Builder.ActiveTool, Is.Null);
            Assert.That(game.XrPointer.CommittedClicks, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealRayPlacesExactlyOnceAndModalBlocksWorld()
        {
            game.XrDispatch("tool", (int)PieceType.Deck);
            yield return null;
            Physics.SyncTransforms();
            var target = game.World.GetComponentsInChildren<SkybridgePickable>().First(item => item.Kind == PickKind.RoadTarget && item.Index == 0);
            var point = target.GetComponentInChildren<Collider>().bounds.center;
            var ray = new Ray(point + Vector3.up * 0.4f, Vector3.down);
            game.XrPointer.Process(ray, true, false);
            game.XrPointer.Process(ray, true, true);
            for (var i = 0; i < 6; i++) game.XrPointer.Process(ray, true, true);
            game.XrPointer.Process(ray, true, false);
            Assert.That(game.Builder.GetLayout().Roads.Count, Is.EqualTo(1));
            Assert.That(game.Builder.GetCost(), Is.EqualTo(1));
            game.Hud.OpenSettings();
            game.XrPointer.ClickForAutomation(ray);
            Assert.That(game.Builder.GetCost(), Is.EqualTo(1));
            Assert.That(game.XrInteractionBlocked, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WorldPanelPoseRemainsFixedWhenCameraMoves()
        {
            var pose = game.Hud.transform.position;
            var rotation = game.Hud.transform.rotation;
            var screen = game.XrCamera.WorldToScreenPoint(pose);
            game.XrCamera.transform.position += new Vector3(0.3f, 0.1f, 0.2f);
            game.XrCamera.transform.rotation = Quaternion.Euler(5, 35, 0);
            yield return null;
            Assert.That(game.Hud.transform.position, Is.EqualTo(pose));
            Assert.That(game.Hud.transform.rotation, Is.EqualTo(rotation));
            Assert.That(game.Hud.transform.IsChildOf(game.XrCamera.transform), Is.False);
            Assert.That(Vector2.Distance(screen, game.XrCamera.WorldToScreenPoint(pose)), Is.GreaterThan(50), "Changing the eye pose must produce UI parallax.");
            game.XrDispatch("help");
            yield return null;
            Assert.That(game.Hud.transform.position, Is.EqualTo(pose), "Opening help must not pull the UI in front of the eye.");
            Assert.That(game.Hud.transform.rotation, Is.EqualTo(rotation));
            game.XrDispatch("tutorial-start");
            yield return null;
            Assert.That(Vector3.Distance(game.Hud.transform.position, pose), Is.LessThan(0.0001f), "Starting a lesson must preserve the established scene location.");
            game.XrDispatch("tutorial-exit");
        }
    }
}
