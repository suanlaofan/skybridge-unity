using System;
using System.Collections.Generic;
using Skybridge.Game;
using Skybridge.Presentation;
using UnityEngine;

namespace Skybridge.UI
{
    /// <summary>Compatibility presenter for the original SkybridgeHud; creates no replacement UI.</summary>
    public sealed class SkybridgeXrWorkbenchView : MonoBehaviour
    {
        private SkybridgeGameController game;
        private SkybridgeHud hud;
        private bool confirmation, adjustment, tracking = true;
        public bool HasModal => confirmation || hud != null && (hud.IsSettingsOpen || hud.IsResultOpen);
        public bool IsAdjustmentOpen => adjustment;
        public bool BlocksWorldInput => HasModal;
        public bool ShortcutsEnabled => false;
        public IReadOnlyList<Canvas> Canvases => new[] { hud.HudCanvas };
        public void Initialize(SkybridgeGameController controller, Camera camera)
        {
            game = controller; hud = game.Hud;
            hud.TutorialHelpRequested += ShowHelp;
        }
        public void Place(Transform workspace, Vector3 position, Quaternion rotation)
        {
            if (game.XrUsesWorldSpace) hud.ConfigureWorldSpaceHud(game.XrCamera, workspace, position, rotation);
        }
        public void Refresh() { }
        public void ShowWelcome() => hud.ShowTutorialNote("Skybridge · Controls",
            "Pick a piece → click a connection point → press \"Test\" to send the truck across." + ViewHint,
            "Basic Tutorial", () => game.XrDispatch("tutorial-start"), "Build Now", hud.HideTutorialNote);
        private string ViewHint => game.XrUsesSimulatorPointer
            ? "\nIn the emulator, pick the right-controller mode at the bottom and left-click. Use the bottom view tools to move and rotate the view."
            : Application.platform == RuntimePlatform.Android
                ? "\nLeft stick: rotate. Left grip + left stick: pan. Right stick: zoom. Right trigger: confirm."
                : "\nRight-drag to rotate, middle-drag to pan, scroll to zoom. The crosshair button on the right resets the view.";
        public void ShowHelp()
        {
            var lesson = game.CurrentLevel.Number == 1 ? 0 : game.CurrentLevel.Number;
            hud.ShowTutorialNote("Tutorial & View Controls", "Click the selected piece again to deselect it, then click a placed part to edit it." + ViewHint,
                "Practice Level", () => game.XrDispatch("tutorial-replay", lesson),
                "Basic Tutorial", () => game.XrDispatch("tutorial-start"), "Close", hud.HideTutorialNote);
        }
        public void ShowSettings() => hud.OpenSettings();
        public void ShowLevelSelect() => hud.ShowTutorialNote("Select Level", "Use the level tabs at the top of the screen to switch levels.", "Close", hud.HideTutorialNote);
        public void ShowLevelIntro() => hud.ShowTutorialNote("Level " + game.CurrentLevel.Number + " · " + game.CurrentLevel.Name,
            game.CurrentLevel.Objective, "Start Building", hud.HideTutorialNote,
            "Level Tutorial", () => game.XrDispatch("tutorial-replay", game.CurrentLevel.Number == 1 ? 0 : game.CurrentLevel.Number));
        public void ShowAdjustment()
        {
            adjustment = true;
            hud.ShowTutorialNote("Change View", ViewHint.Trim(), "Done", () => game.XrDispatch("continue"));
        }
        public void ShowTracking(bool available)
        {
            tracking = available; hud.SetXrTrackingAvailable(available);
            if (!available) hud.ShowStatus("Controller tracking lost. Release the trigger once it's back.", SkybridgePalette.Warning, 0);
            else hud.ClearStatus();
        }
        public void SetTutorial(string title, string body, string progress, bool active, int assistance = 0)
        {
            if (!active) return;
            if (confirmation || adjustment || hud.IsSettingsOpen) return;
            if (game.XrTutorial.IsComplete)
                hud.ShowTutorialNote(title, "Tutorial complete. Going back restores your level bridge and undo history.", "Back to Game", () => game.XrDispatch("tutorial-exit"));
            else
                hud.ShowTutorialNote(title + "  " + progress, body,
                    "Show Hint", () => game.XrDispatch("tutorial-hint"), "Retry Step", () => game.XrDispatch("tutorial-retry"),
                    "Exit Tutorial", () => game.XrDispatch("tutorial-exit"));
        }
        public void ShowResult(string title, string body, bool success)
        {
            if (game.XrIsTutorialActive)
            {
                hud.HideResult();
                if (success) SetTutorial(title, body, "", true);
                else hud.ShowTutorialNote(title, body, "Keep Practicing", hud.HideTutorialNote);
            }
            else if (!success) hud.ShowStatus(body, SkybridgePalette.Warning, 5);
        }
        public void SetPointerHint(string text, bool valid)
        {
            // Preserve the original transient status/tooltip system.
        }
        public void ShowConfirm(string title, string body, Action confirmed)
        {
            confirmation = true;
            hud.ShowTutorialNote(title, body, "Confirm", () => { confirmation = false; hud.HideTutorialNote(); confirmed?.Invoke(); },
                "Cancel", () => { confirmation = false; hud.HideTutorialNote(); });
        }
        public void CloseModal()
        {
            confirmation = false; adjustment = false;
            hud.CloseSettings(); hud.HideResult(); hud.HideTutorialNote();
        }
        private void OnDestroy()
        {
            if (hud != null) hud.TutorialHelpRequested -= ShowHelp;
        }
    }
}
