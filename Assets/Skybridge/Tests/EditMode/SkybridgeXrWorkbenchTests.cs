using NUnit.Framework;
using Skybridge.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Skybridge.Tests
{
    public sealed class SkybridgeXrWorkbenchTests
    {
        private SkybridgeHud hud;
        private Camera camera;
        private Transform workspace;
        [SetUp] public void SetUp()
        {
            camera = new GameObject("HudTestCamera", typeof(Camera)).GetComponent<Camera>();
            hud = new GameObject("OriginalHud", typeof(RectTransform)).AddComponent<SkybridgeHud>();
            hud.Initialize();
            workspace = new GameObject("Workspace").transform;
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(hud.gameObject); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(workspace.gameObject); }
        private Transform Find(string name)
        {
            foreach (var item in hud.GetComponentsInChildren<Transform>(true)) if (item.name == name) return item;
            Assert.Fail("Missing original HUD element: " + name); return null;
        }
        [Test] public void TutorialPreservesOriginalGameControls()
        {
            var top = Find("Topbar"); var dock = Find("BuildDock"); var rail = Find("CommandRail");
            hud.ShowTutorialNote("Tutorial", "Place a deck.", "Continue", () => {});
            Assert.That(Find("Topbar"), Is.SameAs(top)); Assert.That(Find("BuildDock"), Is.SameAs(dock)); Assert.That(Find("CommandRail"), Is.SameAs(rail));
            foreach (var name in new[] { "Deck", "Ramp", "Pier", "Truss", "Test", "Undo", "Redo", "Camera" }) Assert.That(Find(name).gameObject.activeSelf, Is.True, name);
            Assert.That(hud.GetComponentsInChildren<Canvas>(true).Length, Is.EqualTo(1));
        }
        [Test] public void HidingTutorialKeepsOriginalDockVisible()
        {
            hud.ShowTutorialNote("Tutorial", "Hint", "Continue", () => {}); hud.HideTutorialNote();
            Assert.That(hud.HasTutorialNote, Is.False); Assert.That(Find("BuildDock").gameObject.activeSelf, Is.True);
        }
        [Test] public void UpdatedTutorialActionsDoNotRetainOldCallbacks()
        {
            var old = 0; var current = 0;
            hud.ShowTutorialNote("Tutorial", "Hint", "Old action", () => old++);
            hud.ShowTutorialNote("Tutorial", "Hint", "New action", () => current++);
            Find("TutorialPrimary").GetComponent<Button>().onClick.Invoke();
            Assert.That(old, Is.Zero); Assert.That(current, Is.EqualTo(1));
        }
        [Test] public void OriginalHudWorldPoseDoesNotFollowHead()
        {
            hud.ConfigureWorldSpaceHud(camera, workspace, new Vector3(0, 1.6f, 0), Quaternion.Euler(0, 35, 0));
            var position = hud.transform.position; var rotation = hud.transform.rotation;
            camera.transform.SetPositionAndRotation(Vector3.one * 2, Quaternion.Euler(10, 90, 0));
            Assert.That(hud.transform.position, Is.EqualTo(position)); Assert.That(hud.transform.rotation, Is.EqualTo(rotation));
            Assert.That(hud.HudCanvas.renderMode, Is.EqualTo(RenderMode.WorldSpace)); Assert.That(hud.transform.parent, Is.EqualTo(workspace));
            var localPosition = hud.transform.localPosition;
            workspace.SetPositionAndRotation(Vector3.one, Quaternion.Euler(0, 30, 0));
            workspace.localScale = Vector3.one * 1.3f;
            Assert.That(hud.transform.localPosition, Is.EqualTo(localPosition));
            Assert.That(Vector3.Distance(position, hud.transform.position), Is.GreaterThan(0.1f));
        }
        [Test] public void TrackingLossDisablesControlsWithoutLosingTutorial()
        {
            hud.ShowTutorialNote("Tutorial", "Hint", "Continue", () => {}); hud.SetXrTrackingAvailable(false);
            Assert.That(Find("Deck").GetComponent<Button>().IsInteractable(), Is.False); Assert.That(hud.HasTutorialNote, Is.True);
            hud.SetXrTrackingAvailable(true); Assert.That(Find("Deck").GetComponent<Button>().IsInteractable(), Is.True);
        }
        [Test] public void TutorialBackgroundBlocksRayButTextDoesNot()
        {
            Assert.That(Find("TutorialNote").GetComponent<Image>().raycastTarget, Is.True);
            foreach (var label in Find("TutorialNote").GetComponentsInChildren<Text>(true)) Assert.That(label.raycastTarget, Is.False);
            hud.ConfigureWorldSpaceHud(camera, workspace, Vector3.zero, Quaternion.identity);
            foreach (var graphic in hud.GetComponentsInChildren<Graphic>(true)) Assert.That(graphic.material.shader.name, Does.Not.Contain("HeadLocked"));
        }
    }
}
