using System.Collections;
using NUnit.Framework;
using Skybridge.Game;
using Skybridge.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Skybridge.Tests
{
    public sealed class SkybridgeTutorialViewPlayModeTests
    {
        private SkybridgeGameController game;
        private Mouse mouse;
        private InputSettings.UpdateMode previousInputUpdateMode;
        private Vector3 initialCameraPosition;
        private Quaternion initialCameraRotation;

        [UnitySetUp] public IEnumerator SetUp()
        {
            if (SkybridgeGameController.Instance != null) Object.Destroy(SkybridgeGameController.Instance.gameObject);
            yield return null;
            previousInputUpdateMode = InputSystem.settings.updateMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            mouse = InputSystem.AddDevice<Mouse>();
            game = new GameObject("TutorialViewTest").AddComponent<SkybridgeGameController>();
            yield return null;
            game.XrWorkbench.CloseModal();
            game.enabled = false; // Drive input once per test step, independently of Game View focus.
            initialCameraPosition = game.XrCamera.transform.position;
            initialCameraRotation = game.XrCamera.transform.rotation;
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.updateMode = previousInputUpdateMode;
            if (game != null)
            {
                game.XrCamera.transform.SetPositionAndRotation(initialCameraPosition, initialCameraRotation);
                Object.Destroy(game.gameObject);
            }
            yield return null;
        }

        private IEnumerator SendMouse(MouseState state)
        {
            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.Update();
            mouse.MakeCurrent();
            game.SendMessage("UpdateXrDesktopInput");
            yield return null;
        }

        [UnityTest] public IEnumerator PicoSimulatorMouseWorksWithoutTrackedControllers()
        {
            typeof(SkybridgeGameController).GetField("picoSimulator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(game, true);
            var adapter = new GameObject("PicoInputAdapter");
            adapter.transform.SetParent(game.transform, false);
            var input = adapter.AddComponent<SkybridgePicoControllerInput>();
            input.Initialize(game, game.XrCamera);
            input.enabled = false;
            game.ApplyReferenceSolution();
            yield return null;
            game.XrSetTracking(false);
            Canvas.ForceUpdateCanvases();
            var test = System.Linq.Enumerable.First(game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>(), button => button.name == "Test");
            var rect = (RectTransform)test.transform;
            Vector2 point = game.XrCamera.WorldToScreenPoint(rect.TransformPoint(rect.rect.center));
            foreach (var held in new[] { false, true, false })
            {
                var state = new MouseState { position = point };
                if (held) state = state.WithButton(MouseButton.Left);
                InputSystem.QueueStateEvent(mouse, state);
                InputSystem.Update();
                mouse.MakeCurrent();
                input.SendMessage("Update");
                yield return null;
                Assert.That(game.XrTrackingAvailable, Is.True, "Missing XR controllers must not disable the emulator mouse.");
            }
            Assert.That(game.XrPointer.CommittedClicks, Is.EqualTo(1));
            Assert.That(game.Phase, Is.Not.EqualTo(Skybridge.Core.GamePhase.Build), "Mouse press/release must start testing through the original button.");
        }

        [UnityTest] public IEnumerator PicoSimulatorControllerClicksAfterViewHeightChanges()
        {
            typeof(SkybridgeGameController).GetField("picoSimulator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(game, true);
            var adapter = new GameObject("PicoInputAdapter");
            adapter.transform.SetParent(game.transform, false);
            var input = adapter.AddComponent<SkybridgePicoControllerInput>();
            input.Initialize(game, game.XrCamera);
            input.enabled = false;
            game.ApplyReferenceSolution();
            yield return null;
            game.XrCamera.transform.position += Vector3.up * 1.12f;
            Canvas.ForceUpdateCanvases();
            var test = System.Linq.Enumerable.First(game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>(), button => button.name == "Test");
            var rect = (RectTransform)test.transform;
            var target = rect.TransformPoint(rect.rect.center);
            var direction = (target - game.XrCamera.transform.position).normalized;
            var method = typeof(SkybridgePicoControllerInput).GetMethod("CreatePointerRay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var ray = (Ray)method.Invoke(input, new object[] { adapter.transform, Vector3.zero, Quaternion.LookRotation(direction) });
            foreach (var held in new[] { false, true, false }) game.ProcessXrInput(ray, true, held);
            Assert.That(game.XrPointer.CommittedClicks, Is.EqualTo(1), "A moved simulator view must still click the button under its mouse-driven aim.");
            Assert.That(game.Phase, Is.Not.EqualTo(Skybridge.Core.GamePhase.Build));
            typeof(SkybridgeGameController).GetField("picoSimulator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(game, false);
            var controllerPosition = new Vector3(0.2f, 0.7f, 0.3f);
            ray = (Ray)method.Invoke(input, new object[] { adapter.transform, controllerPosition, Quaternion.identity });
            Assert.That(ray.origin, Is.EqualTo(adapter.transform.TransformPoint(controllerPosition)), "Real headsets keep the tracked controller origin.");
        }

        [UnityTest] public IEnumerator PicoSimulatorTouchMappedMouseCommitsExactlyOnce()
        {
            typeof(SkybridgeGameController).GetField("picoSimulator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(game, true);
            var touch = InputSystem.AddDevice<Touchscreen>();
            try
            {
                game.ApplyReferenceSolution();
                yield return null;
                Canvas.ForceUpdateCanvases();
                var test = System.Linq.Enumerable.First(game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>(), button => button.name == "Test");
                var rect = (RectTransform)test.transform;
                Vector2 point = game.XrCamera.WorldToScreenPoint(rect.TransformPoint(rect.rect.center));
                mouse.MakeCurrent();
                game.ProcessXrSimulatorPointer();
                foreach (var phase in new[] { UnityEngine.InputSystem.TouchPhase.Began, UnityEngine.InputSystem.TouchPhase.Ended })
                {
                    InputSystem.QueueStateEvent(touch, new TouchState { touchId = 1, phase = phase, position = point });
                    InputSystem.Update();
                    touch.MakeCurrent();
                    mouse.MakeCurrent();
                    game.ProcessXrSimulatorPointer();
                    yield return null;
                }
                game.ProcessXrSimulatorPointer();
                Assert.That(game.XrPointer.CommittedClicks, Is.EqualTo(1));
                Assert.That(game.Phase, Is.Not.EqualTo(Skybridge.Core.GamePhase.Build));
            }
            finally { InputSystem.RemoveDevice(touch); }
        }

        [UnityTest] public IEnumerator DefaultWorldUiRetainsMouseViewAndDirectTestDuringTutorial()
        {
            game.XrDispatch("tutorial-start");
            var canvas = game.Hud.HudCanvas;
            var camera = game.XrCamera;
            var world = game.XrWorldRoot;
            var hudPosition = canvas.transform.position;
            var hudRotation = canvas.transform.rotation;
            var localPosition = canvas.transform.localPosition;
            var localRotation = canvas.transform.localRotation;
            var headPosition = camera.transform.position;
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(canvas.transform.parent, Is.EqualTo(world));
            Assert.That(game.Hud.HasTutorialNote, Is.True);
            Assert.That(camera.GetComponent<OrbitCameraController>().enabled, Is.False);
            var point = new Vector2(Screen.width * 0.65f, Screen.height * 0.5f);
            yield return SendMouse(new MouseState { position = point });
            var rotation = world.rotation;
            yield return SendMouse(new MouseState { position = point }.WithButton(MouseButton.Right));
            point += new Vector2(60, 20);
            yield return SendMouse(new MouseState { position = point, delta = new Vector2(60, 20) }.WithButton(MouseButton.Right));
            Assert.That(Quaternion.Angle(rotation, world.rotation), Is.GreaterThan(3), "Right drag must orbit during the lesson.");
            yield return SendMouse(new MouseState { position = point });
            rotation = world.rotation;
            game.ApplyReferenceSolution();
            yield return null;
            Assert.That(Quaternion.Angle(rotation, world.rotation), Is.LessThan(0.1), "Building preserves the manual view.");
            var position = world.position;
            yield return SendMouse(new MouseState { position = point }.WithButton(MouseButton.Middle));
            point += Vector2.right * 40;
            yield return SendMouse(new MouseState { position = point, delta = Vector2.right * 40 }.WithButton(MouseButton.Middle));
            Assert.That(Vector3.Distance(position, world.position), Is.GreaterThan(0.02), "Middle drag must pan.");
            yield return SendMouse(new MouseState { position = point });
            var scale = world.localScale.x;
            yield return SendMouse(new MouseState { position = point, scroll = new Vector2(0, 120) });
            Assert.That(world.localScale.x, Is.GreaterThan(scale), "Wheel must zoom.");
            Assert.That(Vector3.Distance(canvas.transform.position, hudPosition), Is.GreaterThan(0.05));
            Assert.That(Quaternion.Angle(canvas.transform.rotation, hudRotation), Is.GreaterThan(3));
            Assert.That(canvas.transform.localPosition, Is.EqualTo(localPosition));
            Assert.That(canvas.transform.localRotation, Is.EqualTo(localRotation));
            Assert.That(camera.transform.position, Is.EqualTo(headPosition));
            // Click the original Test button with the same mouse-to-world ray used in desktop preview.
            Canvas.ForceUpdateCanvases();
            var test = System.Linq.Enumerable.First(game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>(), button => button.name == "Test");
            var testRect = (RectTransform)test.transform;
            point = camera.WorldToScreenPoint(testRect.TransformPoint(testRect.rect.center));
            yield return SendMouse(new MouseState { position = point });
            var committed = game.XrPointer.CommittedClicks;
            yield return SendMouse(new MouseState { position = point }.WithButton(MouseButton.Left));
            yield return SendMouse(new MouseState { position = point });
            Assert.That(game.XrPointer.CommittedClicks, Is.EqualTo(committed + 1));
            Assert.That(game.Phase, Is.Not.EqualTo(Skybridge.Core.GamePhase.Build), "One click starts the test without exiting material selection.");
            game.StopCurrentTest();
            game.XrDispatch("tutorial-exit");
        }

        [UnityTest] public IEnumerator DefaultAndRecenterKeepBridgeLevelAndStructuresUpright()
        {
            for (var pass = 0; pass < 2; pass++)
            {
                var world = game.World;
                var start = world.transform.TransformPoint(world.GridToWorld(game.CurrentLevel.StartAnchor));
                var goal = world.transform.TransformPoint(world.GridToWorld(game.CurrentLevel.GoalAnchor));
                Assert.That(Vector3.Dot(world.transform.up, Vector3.up), Is.GreaterThan(0.9999f), "The scene itself must be upright, independently of the HUD.");
                Assert.That(Vector3.Dot(world.GetImportedModel("GoalGate").up, Vector3.up), Is.GreaterThan(0.99f), "Vertical scene structures must not lean.");
                Assert.That(game.XrCamera.WorldToScreenPoint(start).y,
                    Is.EqualTo(game.XrCamera.WorldToScreenPoint(goal).y).Within(0.5f), "The first level's two bridge endpoints must line up horizontally on screen.");
                Assert.That(game.XrCamera.transform.position.y - (start.y + goal.y) * 0.5f, Is.GreaterThan(0.3f), "Place the level below the eye to show the deck without tilting the scene.");
                Assert.That(Vector3.Dot(game.Hud.transform.up, Vector3.up), Is.GreaterThan(0.9999f));
                game.XrAdjustView(30, 20, 0);
                game.XrDispatch("reposition");
            }
            yield return null;
        }

        [UnityTest] public IEnumerator AlternatingYawAndPitchDoesNotAccumulateRoll()
        {
            var startRotation = game.XrWorldRoot.rotation;
            var ui = game.Hud.transform;
            var anchor = ui.localPosition;
            for (var cycle = 0; cycle < 6; cycle++)
            {
                foreach (var delta in new[] { new Vector2(30, 0), new Vector2(0, 20), new Vector2(-30, 0), new Vector2(0, -20) })
                {
                    game.XrAdjustView(delta.x, delta.y, 0);
                    Assert.That(Mathf.Abs(ui.right.y), Is.LessThan(0.0001f), "Yaw and pitch must not tip the UI sideways.");
                    Assert.That(ui.localPosition, Is.EqualTo(anchor), "Roll correction must preserve the scene anchor.");
                    Assert.That(ui.up.y, Is.GreaterThan(0));
                }
            }
            Assert.That(Quaternion.Angle(startRotation, game.XrWorldRoot.rotation), Is.LessThan(0.01f), "Returning both angles to zero must restore the placed orientation.");
            yield return null;
        }

        [UnityTest] public IEnumerator ControllerViewMovesUiWithBridgeWhileHeadPoseRemainsFixed()
        {
            game.EnableXrPreview(true);
            var hudPose = game.Hud.transform.position;
            var hudRotation = game.Hud.transform.rotation;
            var localPosition = game.Hud.transform.localPosition;
            var localRotation = game.Hud.transform.localRotation;
            var headPose = game.XrCamera.transform.position;
            var headRotation = game.XrCamera.transform.rotation;
            var worldRotation = game.XrWorldRoot.rotation;
            var scale = game.XrWorldRoot.localScale;
            game.XrAdjustView(25, 12, 0.2f);
            Assert.That(Quaternion.Angle(worldRotation, game.XrWorldRoot.rotation), Is.GreaterThan(10));
            Assert.That(game.XrWorldRoot.localScale.x, Is.GreaterThan(scale.x));
            Assert.That(Vector3.Distance(game.Hud.transform.position, hudPose), Is.GreaterThan(0.05));
            Assert.That(Quaternion.Angle(game.Hud.transform.rotation, hudRotation), Is.GreaterThan(10));
            Assert.That(game.Hud.transform.localPosition, Is.EqualTo(localPosition));
            Assert.That(game.Hud.transform.localRotation, Is.EqualTo(localRotation));
            Assert.That(game.XrCamera.transform.position, Is.EqualTo(headPose));
            Assert.That(game.XrCamera.transform.rotation, Is.EqualTo(headRotation));
            var position = game.XrWorldRoot.position;
            hudPose = game.Hud.transform.position;
            game.XrPanView(new Vector2(0.2f, 0.15f));
            Assert.That(Vector3.Distance(position, game.XrWorldRoot.position), Is.GreaterThan(0.2));
            Assert.That(Vector3.Distance(game.Hud.transform.position, hudPose), Is.GreaterThan(0.2));
            Assert.That(game.Hud.transform.localPosition, Is.EqualTo(localPosition));
            yield return null;
        }
    }
}
