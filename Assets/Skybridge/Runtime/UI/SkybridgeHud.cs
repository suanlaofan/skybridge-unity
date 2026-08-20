using System;
using System.Collections.Generic;
using Skybridge.Core;
using Skybridge.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Skybridge.UI
{
    public sealed class SkybridgeHud : MonoBehaviour, IPointerClickHandler
    {
        private const float TopbarHeight = 58f;
        private static readonly Vector2 HudReferenceResolution = new(1280f, 720f);

        private enum HudIcon
        {
            Volume,
            VolumeMuted,
            Settings,
            Undo,
            Redo,
            RotateLeft,
            RotateRight,
            Refresh,
            Delete,
            Crosshair,
            ChevronDown,
            ChevronUp,
            ArrowDownRight,
            ArrowUpRight,
            Play,
            Pause,
            Close,
            ChevronRight,
        }
        private readonly Dictionary<LevelId, Button> levelButtons = new();
        private readonly Dictionary<LevelId, Text> levelBestLabels = new();
        private readonly Dictionary<LevelId, Image> levelMedalMarks = new();
        private readonly Dictionary<PieceType, Button> componentButtons = new();
        private readonly Dictionary<PieceType, Text> componentCounts = new();
        private readonly Dictionary<PieceType, Image> componentSelectionBars = new();
        private readonly Dictionary<Button, Image> buttonIcons = new();
        private readonly Dictionary<HudIcon, Sprite> iconSprites = new();

        private Canvas canvas;
        private Font font;
        private Sprite panelSprite;
        private RectTransform topbar;
        private RectTransform brand;
        private RectTransform tabs;
        private RectTransform budget;
        private RectTransform utilities;
        private RectTransform commandRail;
        private GridLayoutGroup commandRailLayout;
        private RectTransform buildDock;
        private RectTransform inventoryPanel;
        private HorizontalLayoutGroup inventoryLayout;
        private RectTransform volumeControls;
        private RectTransform statusPanel;
        private RectTransform resultOverlay;
        private RectTransform settingsOverlay;
        private Text levelName;
        private Text phaseLabel;
        private Text costValue;
        private Text goldValue;
        private Text silverValue;
        private Text statusText;
        private Text layerValue;
        private Text heightLabel;
        private Text slopeLabel;
        private Text testButtonText;
        private Text resultTitle;
        private RectTransform resultMedal;
        private Text resultCost;
        private Text resultBest;
        private Button undoButton;
        private Button redoButton;
        private Button rotateLeftButton;
        private Button rotateRightButton;
        private Button deleteButton;
        private Button resetButton;
        private Button layerDownButton;
        private Button layerUpButton;
        private Button rampDownButton;
        private Button rampUpButton;
        private Button testButton;
        private Button restartButton;
        private Button audioButton;
        private Button nextButton;
        private Toggle reducedMotionToggle;
        private Toggle autoFocusToggle;
        private bool muted;
        private float statusHideAt;
        private bool layoutDirty = true;
        private int cachedScreenWidth;
        private int cachedScreenHeight;

        public event Action<PieceType> ComponentSelected;
        public event Action<LevelId> LevelSelected;
        public event Action UndoRequested;
        public event Action RedoRequested;
        public event Action<int> RotateRequested;
        public event Action<int> LayerChangeRequested;
        public event Action<int> RampDeltaRequested;
        public event Action DeleteRequested;
        public event Action ResetRequested;
        public event Action TestRequested;
        public event Action RestartTestRequested;
        public event Action CameraResetRequested;
        public event Action NextLevelRequested;
        public event Action CloseResultRequested;
        public event Action<bool> MutedChanged;
        public event Action<bool> ReducedMotionChanged;
        public event Action<bool> AutoFocusChanged;

        public bool IsSettingsOpen => settingsOverlay != null && settingsOverlay.gameObject.activeSelf;

        public void CloseSettings()
        {
            if (settingsOverlay != null) settingsOverlay.gameObject.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!IsSettingsOpen || eventData == null) return;
            var panel = settingsOverlay.Find("SettingsPanel") as RectTransform;
            if (panel == null || !RectTransformUtility.RectangleContainsScreenPoint(panel, eventData.position, eventData.pressEventCamera))
            {
                CloseSettings();
            }
        }

        private sealed class SettingsBackdropClickHandler : MonoBehaviour, IPointerClickHandler
        {
            public SkybridgeHud Owner;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (Owner == null || !Owner.IsSettingsOpen || eventData == null) return;
                var panel = Owner.settingsOverlay != null
                    ? Owner.settingsOverlay.Find("SettingsPanel") as RectTransform
                    : null;
                if (panel == null || !RectTransformUtility.RectangleContainsScreenPoint(
                        panel, eventData.position, eventData.pressEventCamera))
                {
                    Owner.CloseSettings();
                }
            }
        }

        public void Initialize()
        {
            EnsureEventSystem();
            font = Font.CreateDynamicFontFromOSFont(
                new[] { "PingFang SC", "Hiragino Sans GB", "Arial Unicode MS", "Arial" },
                16);
            panelSprite = CreateRoundedSprite(24, 4f, Color.white);
            BuildCanvas();
            BuildTopbar();
            BuildCommandRail();
            BuildBuildDock();
            BuildVolumeControls();
            BuildStatus();
            BuildResult();
            BuildSettings();
            ApplyResponsiveLayout();
        }

        public void SetLevel(LevelDefinition definition)
        {
            levelName.text = $"{definition.Number:00} / {definition.Name}";
            goldValue.text = definition.MedalThresholds.Gold.ToString();
            silverValue.text = definition.MedalThresholds.Silver.ToString();
        }

        public void SetLevelProgress(SaveDataV1 save, LevelId current)
        {
            foreach (var pair in levelButtons)
            {
                var levelId = pair.Key;
                var button = pair.Value;
                var progress = save.Levels[levelId];
                var unlocked = levelId == LevelId.Level1 || save.Levels[(LevelId)((int)levelId - 1)].HighestMedal != Medal.None;
                button.interactable = unlocked;
                var currentLevel = levelId == current;
                SetButtonColors(button, currentLevel ? SkybridgePalette.Mechanical : SkybridgePalette.Surface, currentLevel ? SkybridgePalette.Environment : SkybridgePalette.Mechanical);
                levelBestLabels[levelId].text = progress.LowestCost.HasValue ? Mathf.RoundToInt((float)progress.LowestCost.Value).ToString() : "--";
                levelBestLabels[levelId].color = currentLevel ? new Color(0.92f, 0.93f, 0.91f, 0.62f) : new Color(0.09f, 0.1f, 0.11f, 0.5f);
                var medalColor = progress.HighestMedal switch
                {
                    Medal.Gold => SkybridgePalette.Road,
                    Medal.Silver => SkybridgePalette.Hex("AEB5B9"),
                    Medal.Bronze => SkybridgePalette.Hex("9B5B34"),
                    _ => Color.clear,
                };
                levelMedalMarks[levelId].color = medalColor;
            }
        }

        public void SetCost(int cost, int gold, int silver)
        {
            costValue.text = cost.ToString();
            costValue.color = cost > silver
                ? SkybridgePalette.Hex("C72E20")
                : cost > gold
                    ? SkybridgePalette.Hex("A75B00")
                    : SkybridgePalette.Mechanical;
            goldValue.text = gold.ToString();
            silverValue.text = silver.ToString();
        }

        public void SetInventory(PieceInventory remaining, PieceInventory total, PieceType? selected, bool canEdit)
        {
            foreach (var pair in componentButtons)
            {
                var type = pair.Key;
                var count = remaining[type];
                componentCounts[type].text = $"{count} / {total[type]}";
                pair.Value.interactable = canEdit && total[type] > 0 && count > 0;
                componentSelectionBars[type].gameObject.SetActive(selected.HasValue && selected.Value == type);
            }
        }

        public void SetPhase(GamePhase phase)
        {
            phaseLabel.text = phase switch
            {
                GamePhase.Build => "搭建",
                GamePhase.Validating => "检查",
                GamePhase.TestPreparing => "准备",
                GamePhase.VehicleRunning => "通车",
                GamePhase.Breaking => "断裂",
                GamePhase.VehicleFalling => "坠落",
                GamePhase.RestoreSnapshot => "恢复",
                GamePhase.Success => "完成",
                _ => "载入",
            };
            var testing = phase == GamePhase.TestPreparing || phase == GamePhase.VehicleRunning || phase == GamePhase.Breaking || phase == GamePhase.VehicleFalling || phase == GamePhase.RestoreSnapshot;
            var canEdit = GameSessionRules.CanEditBridge(phase);
            inventoryPanel.gameObject.SetActive(!testing);
            testButtonText.text = testing ? "停止" : "测试";
            SetButtonIcon(testButton, testing ? HudIcon.Pause : HudIcon.Play);
            SetButtonColors(
                testButton,
                testing ? SkybridgePalette.Invalid : SkybridgePalette.Road,
                testing ? Color.white : SkybridgePalette.Mechanical);
            SetButtonInteractable(testButton, phase == GamePhase.Build || testing);
            restartButton.gameObject.SetActive(testing);
            SetButtonInteractable(rotateLeftButton, canEdit);
            SetButtonInteractable(rotateRightButton, canEdit);
            SetButtonInteractable(resetButton, canEdit);
            volumeControls.GetComponent<CanvasGroup>().alpha = canEdit ? 1f : 0.42f;
            volumeControls.GetComponent<CanvasGroup>().interactable = canEdit;
            layoutDirty = true;
        }

        public void SetCommandAvailability(bool canUndo, bool canRedo, bool canDelete)
        {
            SetButtonInteractable(undoButton, canUndo);
            SetButtonInteractable(redoButton, canRedo);
            SetButtonInteractable(deleteButton, canDelete);
        }

        public void SetBuildVolumeControls(bool visible, int layer, int minLayer, int maxLayer, int rampDelta)
        {
            volumeControls.gameObject.SetActive(visible);
            if (!visible) return;
            layerValue.text = $"Y {layer:00}";
            SetButtonInteractable(layerDownButton, layer > minLayer);
            SetButtonInteractable(layerUpButton, layer < maxLayer);
            SetButtonInteractable(rampDownButton, layer > minLayer);
            SetButtonInteractable(rampUpButton, layer < maxLayer);
            SetModeButton(rampDownButton, rampDelta < 0);
            SetModeButton(rampUpButton, rampDelta > 0);
        }

        public void ShowStatus(string message, Color accent, float seconds = 1.8f)
        {
            statusText.text = message;
            statusPanel.GetChild(0).GetComponent<Image>().color = accent;
            statusPanel.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
            statusHideAt = Time.unscaledTime + seconds;
        }

        public void ClearStatus()
        {
            statusPanel.gameObject.SetActive(false);
            statusHideAt = 0f;
        }

        public void ShowResult(Medal medal, int cost, int? previousBest, bool hasNextLevel)
        {
            resultTitle.text = medal switch
            {
                Medal.Gold => "金牌",
                Medal.Silver => "银牌",
                _ => "铜牌",
            };
            resultMedal.GetComponent<Image>().color = medal switch
            {
                Medal.Gold => SkybridgePalette.Road,
                Medal.Silver => SkybridgePalette.Hex("AEB5B9"),
                _ => SkybridgePalette.Hex("9B5B34"),
            };
            resultCost.text = cost.ToString();
            resultBest.text = (previousBest ?? cost).ToString();
            nextButton.gameObject.SetActive(hasNextLevel);
            resultOverlay.gameObject.SetActive(true);
        }

        public void HideResult()
        {
            resultOverlay.gameObject.SetActive(false);
        }

        public void SetSettings(bool muted, bool reducedMotion, bool autoFocus)
        {
            this.muted = muted;
            SetButtonIcon(audioButton, muted ? HudIcon.VolumeMuted : HudIcon.Volume);
            reducedMotionToggle.SetIsOnWithoutNotify(reducedMotion);
            autoFocusToggle.SetIsOnWithoutNotify(autoFocus);
        }

        private void Update()
        {
            if (statusHideAt > 0f && Time.unscaledTime >= statusHideAt) ClearStatus();
            if (layoutDirty || cachedScreenWidth != Screen.width || cachedScreenHeight != Screen.height) ApplyResponsiveLayout();
        }

        private void BuildCanvas()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            // Keep the authored 1280x720 layout legible when the Game view or
            // a Retina display renders at a higher backing resolution.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = HudReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
            gameObject.AddComponent<GraphicRaycaster>();
        }

        private void BuildTopbar()
        {
            topbar = Panel("Topbar", transform, SkybridgePalette.Surface, 0f).GetComponent<RectTransform>();
            topbar.anchorMin = new Vector2(0f, 1f);
            topbar.anchorMax = new Vector2(1f, 1f);
            topbar.pivot = new Vector2(0.5f, 1f);
            topbar.sizeDelta = new Vector2(0f, TopbarHeight);

            brand = EmptyRect("Brand", topbar);
            var mark = ImageRect("BrandMark", brand, SkybridgePalette.Mechanical);
            SetRect(mark.rectTransform, new Vector2(14f, -14f), new Vector2(30f, 30f), new Vector2(0f, 1f));
            var markInset = ImageRect("MarkInset", mark.rectTransform, SkybridgePalette.Road);
            SetRect(markInset.rectTransform, new Vector2(10f, -10f), new Vector2(9f, 9f), new Vector2(0f, 1f));
            var title = Label("Title", brand, "空桥运输", 14, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(56f, -8f), new Vector2(150f, 23f), new Vector2(0f, 1f));
            levelName = Label("LevelName", brand, "01 / 直接连接", 11, FontStyle.Normal, new Color(0.09f, 0.1f, 0.11f, 0.62f), TextAnchor.MiddleLeft);
            SetRect(levelName.rectTransform, new Vector2(56f, -30f), new Vector2(180f, 18f), new Vector2(0f, 1f));

            tabs = EmptyRect("LevelTabs", topbar);
            for (var index = 1; index <= 5; index++)
            {
                var id = (LevelId)index;
                var button = ButtonRect($"Level{index}", tabs, $"{index:00}", SkybridgePalette.Surface, SkybridgePalette.Mechanical, 13, () => LevelSelected?.Invoke(id));
                button.GetComponent<RectTransform>().sizeDelta = new Vector2(48f, TopbarHeight);
                var best = Label("Best", button.transform, "--", 8, FontStyle.Normal, new Color(0.09f, 0.1f, 0.11f, 0.5f), TextAnchor.UpperRight);
                SetStretch(best.rectTransform, 4f, 5f, 4f, 30f);
                var medal = ImageRect("Medal", button.transform, Color.clear);
                medal.rectTransform.anchorMin = new Vector2(0.5f, 0f);
                medal.rectTransform.anchorMax = new Vector2(0.5f, 0f);
                medal.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                medal.rectTransform.anchoredPosition = new Vector2(0f, 11f);
                medal.rectTransform.sizeDelta = new Vector2(7f, 7f);
                medal.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                levelButtons[id] = button;
                levelBestLabels[id] = best;
                levelMedalMarks[id] = medal;
            }
            var tabLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabLayout.childControlWidth = false;
            tabLayout.childForceExpandWidth = false;
            tabLayout.childControlHeight = true;
            tabLayout.childForceExpandHeight = true;
            tabLayout.spacing = 0f;

            budget = EmptyRect("Budget", topbar);
            var budgetLayout = budget.gameObject.AddComponent<HorizontalLayoutGroup>();
            budgetLayout.childControlWidth = false;
            budgetLayout.childForceExpandWidth = false;
            budgetLayout.childControlHeight = true;
            budgetLayout.childForceExpandHeight = true;
            costValue = CreateBudgetCell(budget, "成本", "0", out _);
            goldValue = CreateBudgetCell(budget, "金", "0", out var goldCell);
            silverValue = CreateBudgetCell(budget, "银", "0", out _);
            var goldStripe = ImageRect("GoldStripe", goldCell, SkybridgePalette.Road);
            goldStripe.rectTransform.anchorMin = new Vector2(0f, 1f);
            goldStripe.rectTransform.anchorMax = new Vector2(1f, 1f);
            goldStripe.rectTransform.pivot = new Vector2(0.5f, 1f);
            goldStripe.rectTransform.sizeDelta = new Vector2(0f, 3f);

            utilities = EmptyRect("Utilities", topbar);
            phaseLabel = Label("Phase", utilities, "搭建", 11, FontStyle.Bold, new Color(0.09f, 0.1f, 0.11f, 0.66f), TextAnchor.MiddleCenter);
            audioButton = IconButton("Audio", utilities, HudIcon.Volume, "声音", () =>
            {
                muted = !muted;
                SetButtonIcon(audioButton, muted ? HudIcon.VolumeMuted : HudIcon.Volume);
                MutedChanged?.Invoke(muted);
            });
            var settings = IconButton("Settings", utilities, HudIcon.Settings, "设置", () => settingsOverlay.gameObject.SetActive(true));
            var utilityLayout = utilities.gameObject.AddComponent<HorizontalLayoutGroup>();
            utilityLayout.padding = new RectOffset(7, 10, 7, 7);
            utilityLayout.spacing = 4f;
            utilityLayout.childControlWidth = true;
            utilityLayout.childForceExpandWidth = false;
            utilityLayout.childControlHeight = true;
            utilityLayout.childForceExpandHeight = false;
            utilityLayout.childAlignment = TextAnchor.MiddleCenter;
            phaseLabel.GetComponent<LayoutElement>().preferredWidth = 48f;
            audioButton.GetComponent<LayoutElement>().preferredWidth = 40f;
            settings.GetComponent<LayoutElement>().preferredWidth = 40f;
        }

        private void BuildCommandRail()
        {
            commandRail = Panel("CommandRail", transform, SkybridgePalette.Surface, 6f).GetComponent<RectTransform>();
            commandRail.anchorMin = new Vector2(1f, 1f);
            commandRail.anchorMax = new Vector2(1f, 1f);
            commandRail.pivot = new Vector2(1f, 1f);
            commandRail.anchoredPosition = new Vector2(-14f, -76f);
            commandRail.sizeDelta = new Vector2(54f, 330f);
            commandRailLayout = commandRail.gameObject.AddComponent<GridLayoutGroup>();
            commandRailLayout.padding = new RectOffset(6, 6, 6, 6);
            commandRailLayout.spacing = new Vector2(6f, 6f);
            commandRailLayout.cellSize = new Vector2(40f, 40f);
            commandRailLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            commandRailLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            commandRailLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            commandRailLayout.constraintCount = 1;
            undoButton = IconButton("Undo", commandRail, HudIcon.Undo, "撤销", () => UndoRequested?.Invoke());
            redoButton = IconButton("Redo", commandRail, HudIcon.Redo, "重做", () => RedoRequested?.Invoke());
            rotateLeftButton = IconButton("RotateLeft", commandRail, HudIcon.RotateLeft, "向左旋转", () => RotateRequested?.Invoke(-1));
            rotateRightButton = IconButton("RotateRight", commandRail, HudIcon.RotateRight, "向右旋转", () => RotateRequested?.Invoke(1));
            deleteButton = IconButton("Delete", commandRail, HudIcon.Delete, "删除", () => DeleteRequested?.Invoke());
            resetButton = IconButton("Reset", commandRail, HudIcon.Refresh, "重置桥梁", () => ResetRequested?.Invoke());
            IconButton("Camera", commandRail, HudIcon.Crosshair, "复位镜头", () => CameraResetRequested?.Invoke());
        }

        private void BuildBuildDock()
        {
            buildDock = EmptyRect("BuildDock", transform);
            buildDock.anchorMin = new Vector2(0f, 0f);
            buildDock.anchorMax = new Vector2(1f, 0f);
            buildDock.pivot = new Vector2(0.5f, 0f);
            buildDock.anchoredPosition = new Vector2(0f, 14f);
            buildDock.sizeDelta = new Vector2(-28f, 76f);

            inventoryPanel = Panel("Inventory", buildDock, SkybridgePalette.Surface, 6f).GetComponent<RectTransform>();
            inventoryPanel.anchorMin = new Vector2(0f, 0f);
            inventoryPanel.anchorMax = new Vector2(1f, 1f);
            inventoryPanel.offsetMin = Vector2.zero;
            inventoryPanel.offsetMax = new Vector2(-120f, 0f);
            inventoryLayout = inventoryPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            inventoryLayout.padding = new RectOffset(7, 7, 7, 7);
            inventoryLayout.spacing = 6f;
            inventoryLayout.childAlignment = TextAnchor.MiddleCenter;
            inventoryLayout.childControlWidth = true;
            inventoryLayout.childForceExpandWidth = false;
            inventoryLayout.childControlHeight = true;
            inventoryLayout.childForceExpandHeight = false;
            CreateComponentButton(PieceType.Deck, "标准桥板", SkybridgePalette.Road);
            CreateComponentButton(PieceType.Ramp, "坡道桥板", SkybridgePalette.RoadEdge);
            CreateComponentButton(PieceType.Pier, "桥墩", SkybridgePalette.Truss);
            CreateComponentButton(PieceType.Truss, "桁架", SkybridgePalette.Truss);

            testButton = ButtonRect("Test", buildDock, "测试", SkybridgePalette.Road, SkybridgePalette.Mechanical, 13, () => TestRequested?.Invoke());
            var testRect = testButton.GetComponent<RectTransform>();
            testRect.anchorMin = new Vector2(1f, 0f);
            testRect.anchorMax = new Vector2(1f, 1f);
            testRect.pivot = new Vector2(1f, 0.5f);
            testRect.anchoredPosition = Vector2.zero;
            testRect.sizeDelta = new Vector2(112f, 0f);
            testButtonText = testButton.GetComponentInChildren<Text>();
            testButtonText.rectTransform.offsetMin = new Vector2(28f, 0f);
            AddButtonIcon(testButton, HudIcon.Play, 18f, new Vector2(-25f, 0f));

            restartButton = IconButton("RestartTest", buildDock, HudIcon.Refresh, "重新开始", () => RestartTestRequested?.Invoke());
            var restartRect = restartButton.GetComponent<RectTransform>();
            restartRect.anchorMin = new Vector2(1f, 0f);
            restartRect.anchorMax = new Vector2(1f, 1f);
            restartRect.pivot = new Vector2(1f, 0.5f);
            restartRect.anchoredPosition = new Vector2(-118f, 0f);
            restartRect.sizeDelta = new Vector2(50f, 0f);
            restartButton.GetComponent<LayoutElement>().ignoreLayout = true;
            restartButton.gameObject.SetActive(false);
        }

        private void BuildVolumeControls()
        {
            volumeControls = Panel("VolumeControls", transform, SkybridgePalette.Surface, 6f).GetComponent<RectTransform>();
            volumeControls.anchorMin = new Vector2(0.5f, 0f);
            volumeControls.anchorMax = new Vector2(0.5f, 0f);
            volumeControls.pivot = new Vector2(0.5f, 0f);
            volumeControls.anchoredPosition = new Vector2(0f, 102f);
            volumeControls.sizeDelta = new Vector2(306f, 42f);
            volumeControls.gameObject.AddComponent<CanvasGroup>();
            var layout = volumeControls.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(5, 5, 5, 5);
            layout.spacing = 3f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            heightLabel = Label("HeightLabel", volumeControls, "高度", 9, FontStyle.Bold, new Color(0.09f, 0.1f, 0.11f, 0.62f), TextAnchor.MiddleCenter);
            heightLabel.GetComponent<LayoutElement>().preferredWidth = 28f;
            layerDownButton = SmallIconButton("LayerDown", volumeControls, HudIcon.ChevronDown, "下一层", () => LayerChangeRequested?.Invoke(-1), 30f);
            layerValue = Label("Layer", volumeControls, "Y 00", 11, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.MiddleCenter);
            layerValue.GetComponent<LayoutElement>().preferredWidth = 46f;
            layerUpButton = SmallIconButton("LayerUp", volumeControls, HudIcon.ChevronUp, "上一层", () => LayerChangeRequested?.Invoke(1), 30f);
            var divider = ImageRect("Divider", volumeControls, new Color(0.09f, 0.1f, 0.11f, 0.18f));
            divider.gameObject.AddComponent<LayoutElement>().preferredWidth = 1f;
            slopeLabel = Label("SlopeLabel", volumeControls, "坡向", 9, FontStyle.Bold, new Color(0.09f, 0.1f, 0.11f, 0.62f), TextAnchor.MiddleCenter);
            slopeLabel.GetComponent<LayoutElement>().preferredWidth = 28f;
            rampDownButton = ModeButton("RampDown", volumeControls, "下坡", HudIcon.ArrowDownRight, "坡道下降一格", () => RampDeltaRequested?.Invoke(-1), 54f);
            rampUpButton = ModeButton("RampUp", volumeControls, "上坡", HudIcon.ArrowUpRight, "坡道上升一格", () => RampDeltaRequested?.Invoke(1), 54f);
        }

        private void BuildStatus()
        {
            statusPanel = Panel("Status", transform, SkybridgePalette.Surface, 3f).GetComponent<RectTransform>();
            statusPanel.anchorMin = new Vector2(0.5f, 1f);
            statusPanel.anchorMax = new Vector2(0.5f, 1f);
            statusPanel.pivot = new Vector2(0.5f, 1f);
            statusPanel.anchoredPosition = new Vector2(0f, -70f);
            statusPanel.sizeDelta = new Vector2(440f, 34f);
            var stripe = ImageRect("Accent", statusPanel, SkybridgePalette.Mechanical);
            stripe.rectTransform.anchorMin = new Vector2(0f, 0f);
            stripe.rectTransform.anchorMax = new Vector2(0f, 1f);
            stripe.rectTransform.pivot = new Vector2(0f, 0.5f);
            stripe.rectTransform.sizeDelta = new Vector2(4f, 0f);
            statusText = Label("Text", statusPanel, string.Empty, 12, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.MiddleCenter);
            SetStretch(statusText.rectTransform, 8f, 4f, 8f, 4f);
            statusPanel.gameObject.SetActive(false);
        }

        private void BuildResult()
        {
            resultOverlay = ImageRect("ResultOverlay", transform, new Color(0.04f, 0.05f, 0.055f, 0.42f)).rectTransform;
            StretchFull(resultOverlay);
            var panel = Panel("ResultPanel", resultOverlay, SkybridgePalette.Surface, 6f).GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(380f, 386f);

            var close = IconButton("Close", panel, HudIcon.Close, "关闭结果", () => CloseResultRequested?.Invoke());
            SetRect(close.GetComponent<RectTransform>(), new Vector2(-10f, -10f), new Vector2(40f, 40f), new Vector2(1f, 1f));
            close.GetComponent<LayoutElement>().ignoreLayout = true;

            var medalBorder = ImageRect("MedalBorder", panel, SkybridgePalette.Mechanical);
            medalBorder.rectTransform.anchorMin = medalBorder.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            medalBorder.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            medalBorder.rectTransform.anchoredPosition = new Vector2(0f, -76f);
            medalBorder.rectTransform.sizeDelta = new Vector2(84f, 84f);
            medalBorder.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            resultMedal = ImageRect("MedalFill", medalBorder.transform, SkybridgePalette.Road).rectTransform;
            SetStretch(resultMedal, 8f, 8f, 8f, 8f);
            var ribbonLeft = ImageRect("RibbonLeft", medalBorder.transform, SkybridgePalette.Accent);
            SetRect(ribbonLeft.rectTransform, new Vector2(9f, -28f), new Vector2(22f, 34f), new Vector2(0f, 0f));
            var ribbonRight = ImageRect("RibbonRight", medalBorder.transform, SkybridgePalette.Accent);
            SetRect(ribbonRight.rectTransform, new Vector2(-9f, -28f), new Vector2(22f, 34f), new Vector2(1f, 0f));

            resultTitle = Label("Title", panel, "金牌", 23, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.MiddleCenter);
            resultTitle.rectTransform.anchorMin = resultTitle.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            resultTitle.rectTransform.anchoredPosition = new Vector2(0f, -148f);
            resultTitle.rectTransform.sizeDelta = new Vector2(300f, 38f);

            var metrics = EmptyRect("Metrics", panel);
            metrics.anchorMin = metrics.anchorMax = new Vector2(0.5f, 1f);
            metrics.anchoredPosition = new Vector2(0f, -222f);
            metrics.sizeDelta = new Vector2(320f, 74f);
            var topLine = ImageRect("TopLine", metrics, new Color(0.09f, 0.1f, 0.11f, 0.18f));
            SetRect(topLine.rectTransform, Vector2.zero, new Vector2(320f, 1f), new Vector2(0f, 1f));
            var bottomLine = ImageRect("BottomLine", metrics, new Color(0.09f, 0.1f, 0.11f, 0.18f));
            SetRect(bottomLine.rectTransform, Vector2.zero, new Vector2(320f, 1f), new Vector2(0f, 0f));
            var divider = ImageRect("Divider", metrics, new Color(0.09f, 0.1f, 0.11f, 0.18f));
            divider.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            divider.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            divider.rectTransform.sizeDelta = new Vector2(1f, 0f);
            var currentLabel = Label("CurrentLabel", metrics, "本次成本", 10, FontStyle.Normal, new Color(0.09f, 0.1f, 0.11f, 0.55f), TextAnchor.MiddleCenter);
            SetRect(currentLabel.rectTransform, new Vector2(0f, -8f), new Vector2(160f, 24f), new Vector2(0f, 1f));
            var bestLabel = Label("BestLabel", metrics, "最佳成本", 10, FontStyle.Normal, new Color(0.09f, 0.1f, 0.11f, 0.55f), TextAnchor.MiddleCenter);
            bestLabel.rectTransform.anchorMin = bestLabel.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            bestLabel.rectTransform.pivot = new Vector2(0f, 1f);
            bestLabel.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            bestLabel.rectTransform.sizeDelta = new Vector2(160f, 24f);
            resultCost = Label("Cost", metrics, "0", 25, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.MiddleCenter);
            SetRect(resultCost.rectTransform, new Vector2(0f, 7f), new Vector2(160f, 42f), new Vector2(0f, 0f));
            resultBest = Label("Best", metrics, "0", 25, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.MiddleCenter);
            resultBest.rectTransform.anchorMin = resultBest.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            resultBest.rectTransform.pivot = new Vector2(0f, 0f);
            resultBest.rectTransform.anchoredPosition = new Vector2(0f, 7f);
            resultBest.rectTransform.sizeDelta = new Vector2(160f, 42f);

            nextButton = ButtonRect("Next", panel, "下一关", SkybridgePalette.Mechanical, SkybridgePalette.Environment, 12, () => NextLevelRequested?.Invoke());
            var nextRect = nextButton.GetComponent<RectTransform>();
            nextRect.anchorMin = nextRect.anchorMax = new Vector2(0.5f, 0f);
            nextRect.pivot = new Vector2(0.5f, 0f);
            nextRect.anchoredPosition = new Vector2(0f, 30f);
            nextRect.sizeDelta = new Vector2(320f, 48f);
            var nextText = nextButton.GetComponentInChildren<Text>();
            nextText.rectTransform.offsetMax = new Vector2(-22f, 0f);
            AddButtonIcon(nextButton, HudIcon.ChevronRight, 16f, new Vector2(34f, 0f));
            SetButtonColors(nextButton, SkybridgePalette.Mechanical, SkybridgePalette.Environment);
            resultOverlay.gameObject.SetActive(false);
        }

        private void BuildSettings()
        {
            settingsOverlay = ImageRect("SettingsOverlay", transform, new Color(0.04f, 0.05f, 0.055f, 0.42f)).rectTransform;
            StretchFull(settingsOverlay);
            settingsOverlay.gameObject.AddComponent<SettingsBackdropClickHandler>().Owner = this;
            var panel = Panel("SettingsPanel", settingsOverlay, SkybridgePalette.Surface, 6f).GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(380f, 174f);
            var title = Label("Title", panel, "设置", 16, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(18f, -9f), new Vector2(250f, 40f), new Vector2(0f, 1f));
            var headerLine = ImageRect("HeaderLine", panel, new Color(0.09f, 0.1f, 0.11f, 0.18f));
            SetRect(headerLine.rectTransform, new Vector2(0f, -58f), new Vector2(380f, 1f), new Vector2(0f, 1f));
            reducedMotionToggle = CreateToggle(panel, "减少动态镜头", new Vector2(24f, -75f), value => ReducedMotionChanged?.Invoke(value));
            autoFocusToggle = CreateToggle(panel, "测试时自动聚焦", new Vector2(24f, -128f), value => AutoFocusChanged?.Invoke(value));
            var close = IconButton("Close", panel, HudIcon.Close, "关闭设置", CloseSettings);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(-10f, -9f), new Vector2(40f, 40f), new Vector2(1f, 1f));
            close.GetComponent<LayoutElement>().ignoreLayout = true;
            settingsOverlay.gameObject.SetActive(false);
        }

        private void CreateComponentButton(PieceType type, string label, Color glyphColor)
        {
            var button = ButtonRect(type.ToString(), inventoryPanel, string.Empty, SkybridgePalette.Hex("F8F9F5"), SkybridgePalette.Mechanical, 10, () => ComponentSelected?.Invoke(type));
            var layoutElement = button.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 118f;
            layoutElement.minWidth = 88f;
            layoutElement.preferredHeight = 60f;
            var glyph = ImageRect("Glyph", button.transform, glyphColor);
            SetRect(glyph.rectTransform, new Vector2(9f, -14f), new Vector2(36f, 24f), new Vector2(0f, 1f));
            var railLeft = ImageRect("GlyphRailLeft", glyph.transform, SkybridgePalette.Accent);
            SetRect(railLeft.rectTransform, new Vector2(2f, 2f), new Vector2(3f, 20f), new Vector2(0f, 0f));
            var railRight = ImageRect("GlyphRailRight", glyph.transform, SkybridgePalette.Accent);
            railRight.rectTransform.anchorMin = railRight.rectTransform.anchorMax = new Vector2(1f, 0f);
            railRight.rectTransform.pivot = new Vector2(1f, 0f);
            railRight.rectTransform.anchoredPosition = new Vector2(-2f, 2f);
            railRight.rectTransform.sizeDelta = new Vector2(3f, 20f);
            var labelText = Label("Label", button.transform, label, 10, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.MiddleLeft);
            SetRect(labelText.rectTransform, new Vector2(52f, -7f), new Vector2(60f, 23f), new Vector2(0f, 1f));
            var countText = Label("Count", button.transform, "0 / 0", 9, FontStyle.Normal, new Color(0.09f, 0.1f, 0.11f, 0.62f), TextAnchor.MiddleLeft);
            SetRect(countText.rectTransform, new Vector2(52f, 6f), new Vector2(60f, 20f), new Vector2(0f, 0f));
            var cost = Label("Cost", button.transform, type == PieceType.Pier ? "2" : "1", 9, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.UpperRight);
            SetStretch(cost.rectTransform, 3f, 3f, 4f, 35f);
            var selection = ImageRect("Selection", button.transform, SkybridgePalette.Road);
            selection.rectTransform.anchorMin = new Vector2(0f, 0f);
            selection.rectTransform.anchorMax = new Vector2(1f, 0f);
            selection.rectTransform.pivot = new Vector2(0.5f, 0f);
            selection.rectTransform.sizeDelta = new Vector2(0f, 4f);
            selection.gameObject.SetActive(false);
            componentButtons[type] = button;
            componentCounts[type] = countText;
            componentSelectionBars[type] = selection;
        }

        private Text CreateBudgetCell(Transform parent, string label, string value, out RectTransform cell)
        {
            cell = EmptyRect($"{label}Budget", parent);
            cell.sizeDelta = new Vector2(58f, TopbarHeight);
            var small = Label("Label", cell, label, 9, FontStyle.Bold, new Color(0.09f, 0.1f, 0.11f, 0.56f), TextAnchor.MiddleCenter);
            small.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            small.rectTransform.anchorMax = new Vector2(1f, 1f);
            small.rectTransform.offsetMin = Vector2.zero;
            small.rectTransform.offsetMax = Vector2.zero;
            var number = Label("Value", cell, value, 17, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.MiddleCenter);
            number.rectTransform.anchorMin = new Vector2(0f, 0f);
            number.rectTransform.anchorMax = new Vector2(1f, 0.62f);
            number.rectTransform.offsetMin = Vector2.zero;
            number.rectTransform.offsetMax = Vector2.zero;
            return number;
        }

        private Toggle CreateToggle(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction<bool> callback)
        {
            var root = EmptyRect(label, parent);
            SetRect(root, position, new Vector2(312f, 36f), new Vector2(0f, 1f));
            var background = ImageRect("Background", root, SkybridgePalette.EnvironmentShade);
            SetRect(background.rectTransform, new Vector2(0f, -4f), new Vector2(28f, 28f), new Vector2(0f, 1f));
            var checkmark = ImageRect("Checkmark", background.transform, SkybridgePalette.Road);
            SetStretch(checkmark.rectTransform, 5f, 5f, 5f, 5f);
            var text = Label("Label", root, label, 12, FontStyle.Bold, SkybridgePalette.Mechanical, TextAnchor.MiddleLeft);
            SetRect(text.rectTransform, new Vector2(42f, 0f), new Vector2(250f, 36f), new Vector2(0f, 1f));
            var toggle = root.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.onValueChanged.AddListener(callback);
            return toggle;
        }

        private Button IconButton(string name, Transform parent, HudIcon icon, string tooltip, UnityEngine.Events.UnityAction callback)
        {
            var button = ButtonRect(name, parent, string.Empty, SkybridgePalette.Hex("F4F5F1"), SkybridgePalette.Mechanical, 18, callback);
            var layout = button.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 40f;
            layout.preferredWidth = 40f;
            AddButtonBorder(button.transform);
            AddButtonIcon(button, icon, 21f, Vector2.zero);
            var tip = button.gameObject.AddComponent<UiTooltipTrigger>();
            tip.Message = tooltip;
            tip.Font = font;
            tip.Canvas = canvas;
            return button;
        }

        private Button SmallIconButton(string name, Transform parent, HudIcon icon, string tooltip, UnityEngine.Events.UnityAction callback, float width)
        {
            var button = SmallButton(name, parent, string.Empty, tooltip, callback, width);
            AddButtonIcon(button, icon, 17f, Vector2.zero);
            return button;
        }

        private Button ModeButton(string name, Transform parent, string label, HudIcon icon, string tooltip, UnityEngine.Events.UnityAction callback, float width)
        {
            var button = SmallButton(name, parent, label, tooltip, callback, width);
            var text = button.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleRight;
            text.rectTransform.offsetMin = new Vector2(19f, 0f);
            text.rectTransform.offsetMax = new Vector2(-4f, 0f);
            AddButtonIcon(button, icon, 13f, new Vector2(-17f, 0f));
            return button;
        }

        private Button SmallButton(string name, Transform parent, string label, string tooltip, UnityEngine.Events.UnityAction callback, float width)
        {
            var button = ButtonRect(name, parent, label, Color.clear, SkybridgePalette.Mechanical, 10, callback);
            var layout = button.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = 32f;
            var tip = button.gameObject.AddComponent<UiTooltipTrigger>();
            tip.Message = tooltip;
            tip.Font = font;
            tip.Canvas = canvas;
            return button;
        }

        private Button ButtonRect(string name, Transform parent, string text, Color background, Color foreground, int size, UnityEngine.Events.UnityAction callback)
        {
            var image = ImageRect(name, parent, background);
            image.type = Image.Type.Sliced;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = background;
            colors.highlightedColor = Color.Lerp(background, Color.white, 0.35f);
            colors.pressedColor = Color.Lerp(background, SkybridgePalette.Mechanical, 0.12f);
            colors.disabledColor = new Color(background.r, background.g, background.b, 0.32f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.onClick.AddListener(callback);
            var label = Label("Text", button.transform, text, size, FontStyle.Bold, foreground, TextAnchor.MiddleCenter);
            StretchFull(label.rectTransform);
            return button;
        }

        private Image ImageRect(string name, Transform parent, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.sprite = panelSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        private GameObject Panel(string name, Transform parent, Color color, float ignoredRadius)
        {
            return ImageRect(name, parent, color).gameObject;
        }

        private RectTransform EmptyRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<RectTransform>();
        }

        private Text Label(string name, Transform parent, string value, int size, FontStyle style, Color color, TextAnchor alignment)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var text = gameObject.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            if (gameObject.GetComponent<LayoutElement>() == null) gameObject.AddComponent<LayoutElement>();
            return text;
        }

        private void SetModeButton(Button button, bool selected)
        {
            SetButtonColors(button, selected ? SkybridgePalette.Road : Color.clear, SkybridgePalette.Mechanical);
        }

        private void SetButtonInteractable(Button button, bool interactable)
        {
            if (button == null) return;
            button.interactable = interactable;
            if (buttonIcons.TryGetValue(button, out var icon) && icon != null)
            {
                var color = icon.color;
                color.a = interactable ? 1f : 0.28f;
                icon.color = color;
            }
        }

        private void SetButtonColors(Button button, Color background, Color foreground)
        {
            var image = button.targetGraphic as Image;
            if (image != null) image.color = background;
            var text = button.GetComponentInChildren<Text>();
            if (text != null) text.color = foreground;
            if (buttonIcons.TryGetValue(button, out var icon) && icon != null)
            {
                var alpha = icon.color.a;
                icon.color = new Color(foreground.r, foreground.g, foreground.b, alpha);
            }
            var colors = button.colors;
            colors.normalColor = background;
            colors.highlightedColor = Color.Lerp(background, Color.white, 0.25f);
            colors.pressedColor = Color.Lerp(background, SkybridgePalette.Mechanical, 0.12f);
            colors.disabledColor = new Color(background.r, background.g, background.b, 0.32f);
            button.colors = colors;
        }

        private Image AddButtonIcon(Button button, HudIcon icon, float size, Vector2 offset)
        {
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(button.transform, false);
            var image = iconObject.GetComponent<Image>();
            image.sprite = GetIconSprite(icon);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = SkybridgePalette.Mechanical;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = Vector2.one * size;
            buttonIcons[button] = image;
            return image;
        }

        private void AddButtonBorder(Transform parent)
        {
            var color = new Color(0.09f, 0.1f, 0.11f, 0.18f);
            var top = ImageRect("BorderTop", parent, color);
            top.raycastTarget = false;
            SetRect(top.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 1f));
            top.rectTransform.anchorMax = new Vector2(1f, 1f);
            var bottom = ImageRect("BorderBottom", parent, color);
            bottom.raycastTarget = false;
            SetRect(bottom.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0f));
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            var left = ImageRect("BorderLeft", parent, color);
            left.raycastTarget = false;
            SetRect(left.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 0f));
            left.rectTransform.anchorMax = new Vector2(0f, 1f);
            var right = ImageRect("BorderRight", parent, color);
            right.raycastTarget = false;
            right.rectTransform.anchorMin = new Vector2(1f, 0f);
            right.rectTransform.anchorMax = new Vector2(1f, 1f);
            right.rectTransform.pivot = new Vector2(1f, 0f);
            right.rectTransform.anchoredPosition = Vector2.zero;
            right.rectTransform.sizeDelta = new Vector2(1f, 0f);
        }

        private void SetButtonIcon(Button button, HudIcon icon)
        {
            if (button == null || !buttonIcons.TryGetValue(button, out var image) || image == null) return;
            image.sprite = GetIconSprite(icon);
        }

        private Sprite GetIconSprite(HudIcon icon)
        {
            if (iconSprites.TryGetValue(icon, out var sprite)) return sprite;
            const int size = 32;
            var pixels = new Color32[size * size];
            var white = new Color32(255, 255, 255, 255);

            void Dot(float x, float y, float radius = 1.65f)
            {
                var minX = Mathf.Max(0, Mathf.FloorToInt(x - radius - 1f));
                var maxX = Mathf.Min(size - 1, Mathf.CeilToInt(x + radius + 1f));
                var minY = Mathf.Max(0, Mathf.FloorToInt(y - radius - 1f));
                var maxY = Mathf.Min(size - 1, Mathf.CeilToInt(y + radius + 1f));
                for (var py = minY; py <= maxY; py++)
                {
                    for (var px = minX; px <= maxX; px++)
                    {
                        var distance = Vector2.Distance(new Vector2(px + 0.5f, py + 0.5f), new Vector2(x, y));
                        if (distance <= radius) pixels[py * size + px] = white;
                    }
                }
            }

            void Line(float x0, float y0, float x1, float y1, float thickness = 2.2f)
            {
                var distance = Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1));
                var steps = Mathf.Max(1, Mathf.CeilToInt(distance * 1.5f));
                for (var step = 0; step <= steps; step++)
                {
                    var t = step / (float)steps;
                    Dot(Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), thickness * 0.5f);
                }
            }

            void Circle(float cx, float cy, float radius, float startDegrees = 0f, float endDegrees = 360f)
            {
                var segments = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(endDegrees - startDegrees) / 12f));
                var previous = new Vector2(
                    cx + Mathf.Cos(startDegrees * Mathf.Deg2Rad) * radius,
                    cy + Mathf.Sin(startDegrees * Mathf.Deg2Rad) * radius);
                for (var segment = 1; segment <= segments; segment++)
                {
                    var angle = Mathf.Lerp(startDegrees, endDegrees, segment / (float)segments) * Mathf.Deg2Rad;
                    var next = new Vector2(cx + Mathf.Cos(angle) * radius, cy + Mathf.Sin(angle) * radius);
                    Line(previous.x, previous.y, next.x, next.y, 2.1f);
                    previous = next;
                }
            }

            void FillRect(float left, float bottom, float right, float top)
            {
                for (var y = Mathf.FloorToInt(bottom); y <= Mathf.CeilToInt(top); y++)
                {
                    for (var x = Mathf.FloorToInt(left); x <= Mathf.CeilToInt(right); x++)
                    {
                        if (x >= 0 && x < size && y >= 0 && y < size) pixels[y * size + x] = white;
                    }
                }
            }

            void Chevron(bool up)
            {
                var middleY = up ? 20f : 12f;
                var sideY = up ? 12f : 20f;
                Line(8f, sideY, 16f, middleY);
                Line(16f, middleY, 24f, sideY);
            }

            void TurnArrow(bool right, bool fullCircle)
            {
                var start = right ? -50f : 230f;
                var end = right ? (fullCircle ? 255f : 160f) : (fullCircle ? -75f : 20f);
                Circle(16f, 15.5f, 8.4f, start, end);
                var tipAngle = end * Mathf.Deg2Rad;
                var tip = new Vector2(16f + Mathf.Cos(tipAngle) * 8.4f, 15.5f + Mathf.Sin(tipAngle) * 8.4f);
                var tangent = new Vector2(-Mathf.Sin(tipAngle), Mathf.Cos(tipAngle)) * (right ? 1f : -1f);
                var radial = (tip - new Vector2(16f, 15.5f)).normalized;
                Line(tip.x, tip.y, tip.x - tangent.x * 5f - radial.x * 2f, tip.y - tangent.y * 5f - radial.y * 2f);
                Line(tip.x, tip.y, tip.x - tangent.x * 5f + radial.x * 2f, tip.y - tangent.y * 5f + radial.y * 2f);
            }

            switch (icon)
            {
                case HudIcon.Volume:
                case HudIcon.VolumeMuted:
                    FillRect(6f, 12f, 10f, 20f);
                    Line(10f, 12f, 16f, 7.5f, 2.5f);
                    Line(16f, 7.5f, 16f, 24.5f, 2.5f);
                    Line(16f, 24.5f, 10f, 20f, 2.5f);
                    if (icon == HudIcon.VolumeMuted)
                    {
                        Line(20f, 12f, 27f, 20f);
                        Line(27f, 12f, 20f, 20f);
                    }
                    else
                    {
                        Circle(15.5f, 16f, 6.5f, -42f, 42f);
                        Circle(15.5f, 16f, 10.5f, -40f, 40f);
                    }
                    break;
                case HudIcon.Settings:
                    for (var row = 0; row < 3; row++)
                    {
                        var y = 9f + row * 7f;
                        Line(6f, y, 26f, y);
                        Dot(row == 1 ? 20f : 12f, y, 2.4f);
                    }
                    break;
                case HudIcon.Undo:
                case HudIcon.Redo:
                    var mirror = icon == HudIcon.Redo ? 1f : -1f;
                    Circle(16f, 15f, 8f, icon == HudIcon.Redo ? -60f : 240f, icon == HudIcon.Redo ? 155f : 25f);
                    Line(16f + mirror * -8.3f, 18.3f, 16f + mirror * -2.8f, 23.8f);
                    Line(16f + mirror * -8.3f, 18.3f, 16f + mirror * -8.7f, 25.8f);
                    break;
                case HudIcon.RotateLeft:
                    TurnArrow(false, false);
                    break;
                case HudIcon.RotateRight:
                    TurnArrow(true, false);
                    break;
                case HudIcon.Refresh:
                    TurnArrow(true, true);
                    break;
                case HudIcon.Delete:
                    Line(8f, 23f, 24f, 23f);
                    Line(12f, 26f, 20f, 26f);
                    Line(10f, 21f, 11.5f, 7f);
                    Line(11.5f, 7f, 20.5f, 7f);
                    Line(20.5f, 7f, 22f, 21f);
                    Line(15f, 10f, 15f, 19f, 1.7f);
                    Line(18f, 10f, 18f, 19f, 1.7f);
                    break;
                case HudIcon.Crosshair:
                    Circle(16f, 16f, 7.5f);
                    Line(16f, 4f, 16f, 10f);
                    Line(16f, 22f, 16f, 28f);
                    Line(4f, 16f, 10f, 16f);
                    Line(22f, 16f, 28f, 16f);
                    Dot(16f, 16f, 1.6f);
                    break;
                case HudIcon.ChevronDown:
                    Chevron(false);
                    break;
                case HudIcon.ChevronUp:
                    Chevron(true);
                    break;
                case HudIcon.ArrowDownRight:
                case HudIcon.ArrowUpRight:
                    var startY = icon == HudIcon.ArrowUpRight ? 8f : 24f;
                    var endY = icon == HudIcon.ArrowUpRight ? 24f : 8f;
                    Line(7f, startY, 24f, endY);
                    Line(24f, endY, 17f, endY);
                    Line(24f, endY, 24f, icon == HudIcon.ArrowUpRight ? 17f : 15f);
                    break;
                case HudIcon.Play:
                    for (var x = 9; x <= 23; x++)
                    {
                        var half = (23 - x) * 0.48f;
                        for (var y = Mathf.CeilToInt(16f - half); y <= Mathf.FloorToInt(16f + half); y++) pixels[y * size + x] = white;
                    }
                    break;
                case HudIcon.Pause:
                    FillRect(9f, 8f, 13f, 24f);
                    FillRect(19f, 8f, 23f, 24f);
                    break;
                case HudIcon.Close:
                    Line(8f, 8f, 24f, 24f);
                    Line(8f, 24f, 24f, 8f);
                    break;
                case HudIcon.ChevronRight:
                    Line(11f, 7f, 21f, 16f);
                    Line(21f, 16f, 11f, 25f);
                    break;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"SkybridgeIcon:{icon}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = $"SkybridgeIcon:{icon}";
            iconSprites[icon] = sprite;
            return sprite;
        }

        private void ApplyResponsiveLayout()
        {
            layoutDirty = false;
            cachedScreenWidth = Screen.width;
            cachedScreenHeight = Screen.height;
            var width = canvas.GetComponent<RectTransform>().rect.width;
            // CanvasScaler changes the logical canvas width on high-DPI and
            // narrow displays. Breakpoints remain tied to the actual viewport
            // so the existing desktop/tablet/phone composition stays stable.
            var breakpointWidth = Screen.width;
            var medium = breakpointWidth <= 880f;
            var compact = breakpointWidth <= 680f;
            var tiny = breakpointWidth <= 430f;

            var levelButtonWidth = !medium ? 48f : tiny ? 40f : 44f;
            foreach (var button in levelButtons.Values)
            {
                var rect = button.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(levelButtonWidth, rect.sizeDelta.y);
            }
            var budgetCellWidth = tiny ? 42f : 58f;
            foreach (RectTransform cell in budget)
            {
                cell.sizeDelta = new Vector2(budgetCellWidth, cell.sizeDelta.y);
            }

            var utilityLayout = utilities.GetComponent<HorizontalLayoutGroup>();
            var utilityButtonWidth = tiny ? 36f : 40f;
            audioButton.GetComponent<LayoutElement>().preferredWidth = utilityButtonWidth;
            audioButton.GetComponent<LayoutElement>().preferredHeight = utilityButtonWidth;
            var settingsButton = utilities.Find("Settings")?.GetComponent<LayoutElement>();
            if (settingsButton != null)
            {
                settingsButton.preferredWidth = utilityButtonWidth;
                settingsButton.preferredHeight = utilityButtonWidth;
            }
            utilityLayout.padding = tiny ? new RectOffset(4, 6, 7, 7) : new RectOffset(7, 10, 7, 7);

            if (!medium)
            {
                phaseLabel.gameObject.SetActive(true);
                topbar.sizeDelta = new Vector2(0f, TopbarHeight);
                var utilityWidth = 158f;
                var budgetWidth = 174f;
                var tabsWidth = 240f;
                var brandWidth = Mathf.Max(190f, width - utilityWidth - budgetWidth - tabsWidth);
                SetTopbarSection(brand, 0f, brandWidth, 0f, TopbarHeight);
                SetTopbarSection(tabs, brandWidth, tabsWidth, 0f, TopbarHeight);
                SetTopbarSection(budget, brandWidth + tabsWidth, budgetWidth, 0f, TopbarHeight);
                SetTopbarSection(utilities, brandWidth + tabsWidth + budgetWidth, utilityWidth, 0f, TopbarHeight);
            }
            else if (!compact)
            {
                topbar.sizeDelta = new Vector2(0f, 100f);
                phaseLabel.gameObject.SetActive(true);
                const float utilityWidth = 158f;
                const float budgetWidth = 174f;
                const float tabsWidth = 220f;
                var brandWidth = Mathf.Max(160f, width - utilityWidth - budgetWidth);
                SetTopbarSection(brand, 0f, brandWidth, 42f, 58f);
                SetTopbarSection(budget, brandWidth, budgetWidth, 42f, 58f);
                SetTopbarSection(utilities, brandWidth + budgetWidth, utilityWidth, 42f, 58f);
                SetTopbarSection(tabs, 0f, tabsWidth, 0f, 42f);
            }
            else
            {
                topbar.sizeDelta = new Vector2(0f, 100f);
                phaseLabel.gameObject.SetActive(false);
                var utilityWidth = tiny ? 90f : 104f;
                var tabsWidth = tiny ? 200f : 220f;
                var budgetWidth = tiny ? 126f : 174f;
                SetTopbarSection(brand, 0f, width - utilityWidth, 42f, 58f);
                SetTopbarSection(utilities, width - utilityWidth, utilityWidth, 42f, 58f);
                SetTopbarSection(tabs, 0f, tabsWidth, 0f, 42f);
                SetTopbarSection(budget, width - budgetWidth, budgetWidth, 0f, 42f);
            }

            ConfigureCommandRail(medium, compact);
            ConfigureBuildDock(compact);
            ConfigureVolumeControls(compact, tiny);
            statusPanel.anchoredPosition = new Vector2(0f, !medium ? -70f : compact ? -106f : -108f);
            statusPanel.sizeDelta = new Vector2(Mathf.Min(440f, width - 16f), 34f);

            // CSS reference uses width:min(380px, calc(100% - 28px)) for the
            // settings dialog and width:min(380px, 100%) inside a 20px result
            // overlay.  Clamp both panels and their fixed-width children so
            // a 320px window never lets a modal escape the canvas.
            var resultPanel = resultOverlay.Find("ResultPanel") as RectTransform;
            if (resultPanel != null)
            {
                var resultWidth = Mathf.Max(1f, Mathf.Min(380f, width - 40f));
                resultPanel.sizeDelta = new Vector2(resultWidth, Mathf.Min(386f, Mathf.Max(260f, Screen.height - 40f)));
                var resultInnerWidth = Mathf.Max(1f, resultWidth - 40f);
                var metrics = resultPanel.Find("Metrics") as RectTransform;
                if (metrics != null)
                {
                    var metricsWidth = Mathf.Min(320f, resultInnerWidth);
                    metrics.sizeDelta = new Vector2(metricsWidth, metrics.sizeDelta.y);
                    var half = metricsWidth * 0.5f;
                    var topLine = metrics.Find("TopLine") as RectTransform;
                    var bottomLine = metrics.Find("BottomLine") as RectTransform;
                    if (topLine != null) topLine.sizeDelta = new Vector2(metricsWidth, topLine.sizeDelta.y);
                    if (bottomLine != null) bottomLine.sizeDelta = new Vector2(metricsWidth, bottomLine.sizeDelta.y);
                    foreach (var childName in new[] { "CurrentLabel", "Cost" })
                    {
                        var child = metrics.Find(childName) as RectTransform;
                        if (child != null) child.sizeDelta = new Vector2(half, child.sizeDelta.y);
                    }
                    foreach (var childName in new[] { "BestLabel", "Best" })
                    {
                        var child = metrics.Find(childName) as RectTransform;
                        if (child != null) child.sizeDelta = new Vector2(half, child.sizeDelta.y);
                    }
                }
                var next = resultPanel.Find("Next") as RectTransform;
                if (next != null) next.sizeDelta = new Vector2(Mathf.Min(320f, resultInnerWidth), next.sizeDelta.y);
                var resultTitle = resultPanel.Find("Title") as RectTransform;
                if (resultTitle != null) resultTitle.sizeDelta = new Vector2(Mathf.Min(300f, resultInnerWidth), resultTitle.sizeDelta.y);
            }
            var settingsPanel = settingsOverlay.Find("SettingsPanel") as RectTransform;
            if (settingsPanel != null)
            {
                var settingsWidth = Mathf.Max(1f, Mathf.Min(380f, width - 28f));
                settingsPanel.sizeDelta = new Vector2(settingsWidth, settingsPanel.sizeDelta.y);
                var header = settingsPanel.Find("HeaderLine") as RectTransform;
                if (header != null) header.sizeDelta = new Vector2(settingsWidth, header.sizeDelta.y);
                var title = settingsPanel.Find("Title") as RectTransform;
                if (title != null) title.sizeDelta = new Vector2(Mathf.Max(1f, settingsWidth - 72f), title.sizeDelta.y);
                foreach (var rowName in new[] { "减少动态镜头", "测试时自动聚焦" })
                {
                    var row = settingsPanel.Find(rowName) as RectTransform;
                    if (row == null) continue;
                    row.sizeDelta = new Vector2(Mathf.Max(1f, settingsWidth - 48f), row.sizeDelta.y);
                    var rowLabel = row.Find("Label") as RectTransform;
                    if (rowLabel != null) rowLabel.sizeDelta = new Vector2(Mathf.Max(1f, row.sizeDelta.x - 52f), rowLabel.sizeDelta.y);
                }
            }
        }

        private void ConfigureCommandRail(bool medium, bool compact)
        {
            if (compact)
            {
                commandRail.anchorMin = commandRail.anchorMax = new Vector2(1f, 0f);
                commandRail.pivot = new Vector2(1f, 0f);
                commandRail.anchoredPosition = new Vector2(-8f, 88f);
                commandRail.sizeDelta = new Vector2(284f, 44f);
                commandRailLayout.padding = new RectOffset(4, 4, 4, 4);
                commandRailLayout.spacing = new Vector2(4f, 4f);
                commandRailLayout.cellSize = new Vector2(36f, 36f);
                commandRailLayout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                commandRailLayout.constraintCount = 1;
                return;
            }

            commandRail.anchorMin = commandRail.anchorMax = new Vector2(1f, 1f);
            commandRail.pivot = new Vector2(1f, 1f);
            commandRail.anchoredPosition = new Vector2(-14f, medium ? -108f : -76f);
            commandRail.sizeDelta = new Vector2(54f, 330f);
            commandRailLayout.padding = new RectOffset(6, 6, 6, 6);
            commandRailLayout.spacing = new Vector2(6f, 6f);
            commandRailLayout.cellSize = new Vector2(40f, 40f);
            commandRailLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            commandRailLayout.constraintCount = 1;
        }

        private void ConfigureBuildDock(bool compact)
        {
            buildDock.anchoredPosition = new Vector2(0f, compact ? 8f : 14f);
            buildDock.sizeDelta = new Vector2(compact ? -16f : -28f, compact ? 72f : 76f);
            inventoryPanel.offsetMax = new Vector2(compact ? -87f : -120f, 0f);
            inventoryLayout.padding = compact ? new RectOffset(5, 5, 5, 5) : new RectOffset(7, 7, 7, 7);
            inventoryLayout.spacing = compact ? 4f : 6f;
            inventoryLayout.childAlignment = compact ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;

            var testRect = testButton.GetComponent<RectTransform>();
            testRect.sizeDelta = new Vector2(compact ? 82f : 112f, 0f);
            var restartRect = restartButton.GetComponent<RectTransform>();
            restartRect.anchoredPosition = new Vector2(compact ? -87f : -118f, 0f);
            restartRect.sizeDelta = new Vector2(compact ? 42f : 50f, 0f);

            foreach (var button in componentButtons.Values) ConfigureComponentButton(button, compact);
        }

        private static void ConfigureComponentButton(Button button, bool compact)
        {
            var layout = button.GetComponent<LayoutElement>();
            layout.preferredWidth = compact ? 58f : 118f;
            layout.minWidth = compact ? 58f : 88f;
            layout.preferredHeight = 60f;

            var glyph = button.transform.Find("Glyph") as RectTransform;
            var label = button.transform.Find("Label")?.GetComponent<Text>();
            var count = button.transform.Find("Count")?.GetComponent<Text>();
            var cost = button.transform.Find("Cost")?.GetComponent<Text>();
            if (glyph == null || label == null || count == null || cost == null) return;

            if (compact)
            {
                SetRect(glyph, new Vector2(0f, -3f), new Vector2(34f, 30f), new Vector2(0.5f, 1f));
                SetRect(label.rectTransform, new Vector2(0f, 2f), new Vector2(54f, 16f), new Vector2(0.5f, 0f));
                label.fontSize = 9;
                label.alignment = TextAnchor.MiddleCenter;
                SetRect(count.rectTransform, new Vector2(5f, -2f), new Vector2(34f, 12f), new Vector2(0f, 1f));
                count.fontSize = 8;
                count.alignment = TextAnchor.MiddleLeft;
                SetRect(cost.rectTransform, new Vector2(-3f, -2f), new Vector2(16f, 12f), new Vector2(1f, 1f));
                cost.fontSize = 8;
                cost.alignment = TextAnchor.UpperRight;
                return;
            }

            SetRect(glyph, new Vector2(9f, -14f), new Vector2(36f, 24f), new Vector2(0f, 1f));
            SetRect(label.rectTransform, new Vector2(52f, -7f), new Vector2(60f, 23f), new Vector2(0f, 1f));
            label.fontSize = 10;
            label.alignment = TextAnchor.MiddleLeft;
            SetRect(count.rectTransform, new Vector2(52f, 6f), new Vector2(60f, 20f), new Vector2(0f, 0f));
            count.fontSize = 9;
            count.alignment = TextAnchor.MiddleLeft;
            SetStretch(cost.rectTransform, 3f, 3f, 4f, 35f);
            cost.fontSize = 9;
            cost.alignment = TextAnchor.UpperRight;
        }

        private void ConfigureVolumeControls(bool compact, bool tiny)
        {
            heightLabel.gameObject.SetActive(!compact);
            slopeLabel.gameObject.SetActive(!compact);
            rampDownButton.GetComponent<LayoutElement>().preferredWidth = tiny ? 42f : 54f;
            rampUpButton.GetComponent<LayoutElement>().preferredWidth = tiny ? 42f : 54f;

            if (!compact)
            {
                volumeControls.anchorMin = volumeControls.anchorMax = new Vector2(0.5f, 0f);
                volumeControls.pivot = new Vector2(0.5f, 0f);
                volumeControls.anchoredPosition = new Vector2(0f, 102f);
                volumeControls.sizeDelta = new Vector2(306f, 42f);
                return;
            }

            volumeControls.anchorMin = volumeControls.anchorMax = tiny ? new Vector2(0f, 0f) : new Vector2(0.5f, 0f);
            volumeControls.pivot = tiny ? new Vector2(0f, 0f) : new Vector2(0.5f, 0f);
            volumeControls.anchoredPosition = tiny ? new Vector2(8f, 136f) : new Vector2(0f, 136f);
            volumeControls.sizeDelta = new Vector2(tiny ? 216f : 240f, 40f);
        }

        private static void SetTopbarSection(RectTransform rect, float left, float width, float bottom, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(left, bottom);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size, Vector2 anchor)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetStretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Sprite CreateRoundedSprite(int size, float radius, Color color)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "RuntimeRoundedPanel",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var center = radius - 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var edgeX = Mathf.Min(x, size - 1 - x);
                    var edgeY = Mathf.Min(y, size - 1 - y);
                    var alpha = 1f;
                    if (edgeX < radius && edgeY < radius)
                    {
                        var dx = center - edgeX;
                        var dy = center - edgeY;
                        alpha = Mathf.Clamp01(radius + 0.4f - Mathf.Sqrt(dx * dx + dy * dy));
                    }
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
                }
            }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, Vector4.one * radius);
        }

        private void OnDestroy()
        {
            foreach (var sprite in iconSprites.Values)
            {
                if (sprite == null) continue;
                var texture = sprite.texture;
                Destroy(sprite);
                if (texture != null) Destroy(texture);
            }
            iconSprites.Clear();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(eventSystem);
        }
    }

    internal sealed class UiTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private GameObject tooltip;
        public string Message;
        public Font Font;
        public Canvas Canvas;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (tooltip != null || Canvas == null) return;
            tooltip = new GameObject("Tooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            tooltip.transform.SetParent(Canvas.transform, false);
            var rect = tooltip.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Canvas.transform as RectTransform, eventData.position, Canvas.worldCamera, out var local);
            rect.anchoredPosition = local + Vector2.up * 18f;
            rect.sizeDelta = new Vector2(Mathf.Clamp(Message.Length * 13f + 16f, 54f, 150f), 28f);
            tooltip.GetComponent<Image>().color = SkybridgePalette.Mechanical;
            var labelObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(tooltip.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(5f, 3f);
            labelRect.offsetMax = new Vector2(-5f, -3f);
            var text = labelObject.GetComponent<Text>();
            text.font = Font;
            text.fontSize = 10;
            text.text = Message;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = SkybridgePalette.Environment;
            text.raycastTarget = false;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (tooltip != null) Destroy(tooltip);
            tooltip = null;
        }

        private void OnDisable()
        {
            if (tooltip != null) Destroy(tooltip);
            tooltip = null;
        }
    }
}
