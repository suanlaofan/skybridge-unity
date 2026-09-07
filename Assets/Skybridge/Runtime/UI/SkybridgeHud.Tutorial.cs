using System;
using UnityEngine;
using UnityEngine.UI;
using Skybridge.Presentation;

namespace Skybridge.UI
{
    // Adds only tutorial notes to the existing HUD. All game controls retain their original layout.
    public sealed partial class SkybridgeHud
    {
        private RectTransform tutorialNote;
        private Text tutorialNoteTitle, tutorialNoteBody;
        private Button tutorialPrimary, tutorialSecondary, tutorialExtra;
        private Action tutorialPrimaryAction, tutorialSecondaryAction, tutorialExtraAction;
        private bool worldAnchoredPresentation;
        public event Action TutorialHelpRequested;
        public Canvas HudCanvas => canvas;
        public bool IsResultOpen => resultOverlay != null && resultOverlay.gameObject.activeSelf;
        public bool HasTutorialNote => tutorialNote != null && tutorialNote.gameObject.activeSelf;

        private void BuildTutorialNotes()
        {
            var entry = ButtonRect("TutorialHelp", transform, "教程 / 帮助", SkybridgePalette.Surface, SkybridgePalette.Mechanical, 13,
                () => TutorialHelpRequested?.Invoke());
            SetRect(entry.GetComponent<RectTransform>(), new Vector2(14, -64), new Vector2(112, 32), new Vector2(0, 1));
            tutorialNote = Panel("TutorialNote", transform, SkybridgePalette.Surface, 6).GetComponent<RectTransform>();
            SetRect(tutorialNote, new Vector2(14, -104), new Vector2(570, 156), new Vector2(0, 1));
            tutorialNoteTitle = Label("TutorialTitle", tutorialNote, "教程", 19, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.MiddleLeft);
            SetRect(tutorialNoteTitle.rectTransform, new Vector2(14, -8), new Vector2(542, 32), new Vector2(0, 1));
            tutorialNoteBody = Label("TutorialBody", tutorialNote, "", 17, FontStyle.Normal, SkybridgePalette.Mechanical, TextAnchor.UpperLeft);
            SetRect(tutorialNoteBody.rectTransform, new Vector2(14, -44), new Vector2(542, 58), new Vector2(0, 1));
            tutorialPrimary = NoteButton("TutorialPrimary", "开始教程", 14, () => tutorialPrimaryAction?.Invoke(), true);
            tutorialSecondary = NoteButton("TutorialSecondary", "跳过", 192, () => tutorialSecondaryAction?.Invoke(), false);
            tutorialExtra = NoteButton("TutorialExtra", "提示", 370, () => tutorialExtraAction?.Invoke(), false);
            tutorialNote.gameObject.SetActive(false);
        }
        private Button NoteButton(string name, string label, float x, Action callback, bool primary)
        {
            var button = ButtonRect(name, tutorialNote, label, primary ? SkybridgePalette.Road : SkybridgePalette.Environment,
                SkybridgePalette.Mechanical, 14, () => callback?.Invoke());
            SetRect(button.GetComponent<RectTransform>(), new Vector2(x, 10), new Vector2(164, 34), new Vector2(0, 0));
            return button;
        }
        public void ShowTutorialNote(string title, string body, string primaryLabel, Action primary,
            string secondaryLabel = null, Action secondary = null, string extraLabel = null, Action extra = null)
        {
            tutorialNoteTitle.text = title;
            tutorialNoteBody.text = body;
            var bodyHeight = Mathf.Max(58, Mathf.Ceil(tutorialNoteBody.preferredHeight));
            tutorialNoteBody.rectTransform.sizeDelta = new Vector2(542, bodyHeight);
            tutorialNote.sizeDelta = new Vector2(570, bodyHeight + 98);
            tutorialPrimaryAction = primary; tutorialSecondaryAction = secondary; tutorialExtraAction = extra;
            SetNoteButton(tutorialPrimary, primaryLabel);
            SetNoteButton(tutorialSecondary, secondaryLabel);
            SetNoteButton(tutorialExtra, extraLabel);
            tutorialNote.gameObject.SetActive(true);
            // Keep modal settings/results on top of the added note.
            settingsOverlay.SetAsLastSibling(); resultOverlay.SetAsLastSibling();
        }
        private static void SetNoteButton(Button button, string label)
        {
            button.gameObject.SetActive(!string.IsNullOrEmpty(label));
            button.GetComponentInChildren<Text>().text = label ?? "";
        }
        public void HideTutorialNote() => tutorialNote.gameObject.SetActive(false);
        public void OpenSettings() => settingsOverlay.gameObject.SetActive(true);
        public void ConfigureWorldSpaceHud(Camera xrCamera, Transform workspace, Vector3 position, Quaternion rotation)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            worldAnchoredPresentation = true;
            picoPresentation = true;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = xrCamera;
            canvas.overrideSorting = false;
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasScaler.scaleFactor = 1;
            var rect = canvas.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = HudReferenceResolution;
            // UI and bridge share a scene coordinate frame. View manipulation must transform both.
            canvas.transform.SetParent(null, false);
            canvas.transform.SetPositionAndRotation(position, rotation);
            canvas.transform.localScale = Vector3.one * (2.1f / 1280f);
            canvas.transform.SetParent(workspace, true);
            picoForegroundMaterial = null;
            foreach (var graphic in GetComponentsInChildren<Graphic>(true)) graphic.material = null;
            layoutDirty = true;
            ApplyResponsiveLayout();
        }
        public void SetXrTrackingAvailable(bool available)
        {
            var group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.interactable = available;
        }
    }
}
