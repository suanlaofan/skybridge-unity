using System;
using System.Linq;
using Skybridge.Core;
using Skybridge.Presentation;
using Skybridge.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Skybridge.Game
{
    public sealed partial class SkybridgeGameController
    {
        private SkybridgeXrWorkbenchView xrWorkbench;
        private SkybridgeXrPointer xrPointer;
        private SkybridgeTutorialDirector xrTutorial;
        private bool xrPreview;
        private bool xrWorldPreview;
        private bool xrWorkspacePlaced;
        private bool xrMouseOrbit, xrMousePan;
        private Vector2 xrMouseDown;
        private bool xrMouseDragged;
        private float xrViewPitch, xrViewYaw;
        private bool xrTrackingAvailable = true;
        private bool xrApplicationFocused = true;
        private bool xrMovingSelection;
        private bool xrAdjusting;
        private string xrPreviewKey;
        private Vector3 xrPlacementOrigin;
        private Quaternion xrPlacementYaw;
        private Vector3 xrDefaultWorldPosition;
        private Quaternion xrDefaultWorldRotation;
        private Vector3 xrDefaultWorldScale;
        private XrPracticeSession xrPracticeSession;

        private sealed class XrPracticeSession
        {
            public LevelDefinition Level;
            public BuildController Builder;
            public GamePhase Phase;
            public TestOutcome Outcome;
            public System.Collections.Generic.IReadOnlyList<GridPoint> Route;
        }

        public bool UsesXrPresentation => picoRuntime || xrPreview;
        public bool XrUsesWorldSpace => picoRuntime || xrWorldPreview;
        public bool XrUsesSimulatorPointer => picoSimulator;
        public bool XrUsesScreenPointer => xrWorldPreview || picoSimulator;
        private bool UsesDesktopCamera => !XrUsesWorldSpace;
        public bool XrIsTutorialActive => xrPracticeSession != null;
        public bool XrIsMovingSelection => xrMovingSelection;
        public bool XrTrackingAvailable => xrTrackingAvailable && xrApplicationFocused;
        public bool XrInteractionBlocked => !XrTrackingAvailable || (xrWorkbench != null && xrWorkbench.BlocksWorldInput);
        public SkybridgeXrWorkbenchView XrWorkbench => xrWorkbench;
        public SkybridgeXrPointer XrPointer => xrPointer;
        public SkybridgeTutorialDirector XrTutorial => xrTutorial;
        public Camera XrCamera => worldCamera;
        public Transform XrWorldRoot => picoWorldRoot;
        public bool XrIsLevelUnlocked(LevelId id) => saveData != null && SaveDataService.IsLevelUnlocked(saveData, id);

        private void InitializeXrPresentation()
        {
            if (xrWorkbench != null) return;
            if (XrUsesWorldSpace)
            {
                world.SetXrPresentation(true);
                foreach (var module in FindObjectsByType<BaseInputModule>()) module.enabled = false;
            }
            if (EventSystem.current == null) new GameObject("XR Event System").AddComponent<EventSystem>();
            var presenter = new GameObject("Tutorial Presenter");
            presenter.transform.SetParent(transform, false);
            xrWorkbench = presenter.AddComponent<SkybridgeXrWorkbenchView>();
            xrWorkbench.Initialize(this, worldCamera);
            if (XrUsesWorldSpace)
            {
                xrPointer = gameObject.AddComponent<SkybridgeXrPointer>();
                xrPointer.Initialize(this, xrWorkbench, worldCamera);
            }
            xrTutorial = new SkybridgeTutorialDirector();
            xrTutorial.Changed += RefreshXrTutorial;
            if (orbitCamera != null) orbitCamera.ViewAdjusted += ObserveDesktopViewAdjustment;
            xrWorkbench.ShowWelcome();
        }

        /// <summary>Editor-only presentation inspection uses the same UI, router and game actions as PICO.</summary>
        public void EnableXrPreview(bool worldSpace = true)
        {
            if (picoRuntime || xrPreview && (!worldSpace || xrWorldPreview)) return;
            xrPreview = true;
            xrApplicationFocused = true;
            if (worldSpace)
            {
                xrWorldPreview = true;
                if (orbitCamera != null) orbitCamera.enabled = false;
                worldCamera.transform.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.identity);
                worldCamera.fieldOfView = 70;
                worldCamera.nearClipPlane = PicoNearClipPlane;
                RenderSettings.fog = false;
                picoWorldRoot = new GameObject("XR Preview WorldRoot").transform;
                picoWorldRoot.SetParent(transform, false);
                world.transform.SetParent(picoWorldRoot, false);
                if (xrWorkbench != null)
                {
                    world.SetXrPresentation(true);
                    foreach (var module in FindObjectsByType<BaseInputModule>()) module.enabled = false;
                    xrPointer = gameObject.AddComponent<SkybridgeXrPointer>();
                    xrPointer.Initialize(this, xrWorkbench, worldCamera);
                }
            }
            else if (orbitCamera != null) orbitCamera.enabled = true;
            InitializeXrPresentation();
            ResetXrWorkspace();
            Debug.Log("SKYBRIDGE_ORIGINAL_HUD_TUTORIAL_READY: " + (worldSpace ? "WorldSpace" : "Desktop"));
        }

        private void PlaceXrWorkbench(Vector3 origin, Quaternion yaw)
        {
            xrPlacementOrigin = origin;
            xrPlacementYaw = yaw;
            FrameWorkspaceAtPlacement();
            xrWorkbench?.Place(picoWorldRoot, origin + yaw * new Vector3(0, -0.08f, 1.5f), yaw);
            xrWorkspacePlaced = true;
            xrDefaultWorldPosition = picoWorldRoot.position;
            xrDefaultWorldRotation = picoWorldRoot.rotation;
            xrDefaultWorldScale = picoWorldRoot.localScale;
        }

        private void ResetXrWorkspace(bool recenter = false)
        {
            if (!XrUsesWorldSpace)
            {
                orbitCamera.ResetTo(world.GetSceneBounds(), world.GridToWorld(level.StartAnchor), world.GridToWorld(level.GoalAnchor));
                return;
            }
            var eye = worldCamera.transform;
            var useCurrentEye = recenter || !xrWorkspacePlaced;
            var origin = useCurrentEye ? eye.position : xrPlacementOrigin;
            var forward = useCurrentEye ? Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized : xrPlacementYaw * Vector3.forward;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            var yaw = Quaternion.LookRotation(forward, Vector3.up);
            xrViewPitch = xrViewYaw = 0;
            var focus = (world.GridToWorld(level.StartAnchor) + world.GridToWorld(level.GoalAnchor)) * 0.5f;
            picoWorldRoot.localScale = Vector3.one * (level.BuildVolume == null ? 0.15f : 0.065f);
            // Keep the bridge level and its vertical structures upright. The table sits below
            // eye height so its surface is visible without tilting the entire scene.
            picoWorldRoot.rotation = yaw * Quaternion.Euler(0, 180, 0);
            picoWorldRoot.position = origin + forward * 2.3f - Vector3.up * 0.45f -
                                     picoWorldRoot.rotation * Vector3.Scale(focus, picoWorldRoot.localScale);
            PlaceXrWorkbench(origin, yaw);
            Physics.SyncTransforms();
        }

        private void UpdateXrPresentation()
        {
            if (xrWorldPreview && xrPointer != null && Mouse.current != null && Application.isFocused)
            {
                UpdateXrDesktopInput();
            }
            xrTutorial?.Tick(Time.unscaledDeltaTime, !XrTrackingAvailable || (xrWorkbench != null && xrWorkbench.HasModal));
        }

        // The desktop preview uses the same world-space canvas and ray routing as the headset.
        // Preserve the existing mouse gestures while moving the bridge and its scene-anchored UI together.
        private void UpdateXrDesktopInput()
        {
            var mouse = Mouse.current;
            var touch = picoSimulator ? Touchscreen.current?.primaryTouch : null;
            var useTouch = touch != null && (mouse == null || touch.press.isPressed || touch.press.wasReleasedThisFrame);
            if (mouse == null && touch == null || xrPointer == null) return;
            var position = useTouch ? touch.position.ReadValue() : mouse.position.ReadValue();
            var held = useTouch ? touch.press.isPressed : mouse.leftButton.isPressed;
            xrPointer.Process(worldCamera.ScreenPointToRay(position), xrApplicationFocused, held);
            if (useTouch) return;
            var overUi = xrPointer.HoverTarget != null;
            if (mouse.rightButton.wasPressedThisFrame)
            {
                xrMouseOrbit = !overUi;
                xrMouseDown = position;
                xrMouseDragged = false;
            }
            if (mouse.middleButton.wasPressedThisFrame) xrMousePan = !overUi;
            var delta = mouse.delta.ReadValue();
            if (xrMouseOrbit && mouse.rightButton.isPressed)
            {
                xrMouseDragged |= Vector2.Distance(position, xrMouseDown) > 5f || delta.sqrMagnitude > 25f;
                XrAdjustView(-delta.x * 0.18f, delta.y * 0.14f, 0);
            }
            if (xrMousePan && mouse.middleButton.isPressed)
            {
                var metersPerPixel = 2f * Vector3.Distance(worldCamera.transform.position, XrWorldFocus()) *
                    Mathf.Tan(worldCamera.fieldOfView * Mathf.Deg2Rad * 0.5f) / Mathf.Max(1, Screen.height);
                XrPanView(delta * metersPerPixel);
            }
            if (!overUi) XrAdjustView(0, 0, mouse.scroll.ReadValue().y * 0.0011f);
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                if (xrMouseOrbit && !xrMouseDragged) XrDispatch("cancel");
                xrMouseOrbit = false;
            }
            if (mouse.middleButton.wasReleasedThisFrame) xrMousePan = false;
            HandleKeyboard();
        }

        public void ProcessXrSimulatorPointer()
        {
            if (picoSimulator && xrPointer != null) UpdateXrDesktopInput();
        }

        private void ObserveDesktopViewAdjustment()
        {
            if (!XrUsesWorldSpace) ObserveXrTutorial("workspace");
        }

        public void ProcessXrInput(Ray ray, bool tracked, bool held)
        {
            xrPointer?.Process(ray, tracked && xrApplicationFocused, held);
        }

        public void XrAdjustView(float yawDegrees, float pitchDegrees, float zoomDelta)
        {
            if (!XrUsesWorldSpace || XrInteractionBlocked ||
                Mathf.Abs(yawDegrees) + Mathf.Abs(pitchDegrees) + Mathf.Abs(zoomDelta) < 0.0001f) return;
            xrPointer?.CancelInput();
            var focus = XrWorldFocus();
            xrViewPitch = Mathf.Clamp(xrViewPitch + pitchDegrees, -85f, 85f);
            xrViewYaw = Mathf.Repeat(xrViewYaw + yawDegrees + 180f, 360f) - 180f;
            // Rebuild yaw/pitch from the placed orientation. Incremental rotations around fixed
            // world axes introduce roll when the user alternates horizontal and vertical drags.
            var viewRotation = Quaternion.Euler(0, xrViewYaw, 0) * Quaternion.Euler(xrViewPitch, 0, 0);
            picoWorldRoot.rotation = xrPlacementYaw * viewRotation * Quaternion.Inverse(xrPlacementYaw) * xrDefaultWorldRotation;
            var scale = Mathf.Clamp(picoWorldRoot.localScale.x * Mathf.Exp(zoomDelta), 0.025f, 0.20f);
            picoWorldRoot.localScale = Vector3.one * scale;
            picoWorldRoot.position += focus - XrWorldFocus();
            Physics.SyncTransforms();
            ObserveXrTutorial("workspace");
        }

        public void XrPanView(Vector2 deltaMeters)
        {
            if (!XrUsesWorldSpace || XrInteractionBlocked || deltaMeters.sqrMagnitude < 0.0000001f) return;
            xrPointer?.CancelInput();
            picoWorldRoot.position += xrPlacementYaw * new Vector3(deltaMeters.x, deltaMeters.y, 0);
            Physics.SyncTransforms();
            ObserveXrTutorial("workspace");
        }

        public void XrSetTracking(bool available)
        {
            if (xrTrackingAvailable == available) return;
            xrTrackingAvailable = available;
            xrWorkbench?.ShowTracking(available);
            world?.HideGhost();
            xrPreviewKey = null;
            if (!available && IsTestPhase(phase)) StopCurrentTest();
        }

        public void XrDispatch(string action, int value = 0)
        {
            if (xrWorkbench == null) return;
            var safetyAction = action == "help" || action == "cancel" || action.StartsWith("tutorial-") || action == "stop";
            if (!XrTrackingAvailable && !safetyAction) return;
            if (XrIsTutorialActive && xrTutorial != null && !xrTutorial.Allows(action) && !safetyAction)
            {
                ShowStatus(xrTutorial.Body, SkybridgePalette.Warning, 2f);
                return;
            }
            switch (action)
            {
                case "tool": SelectComponent((PieceType)Mathf.Clamp(value, 0, 3)); break;
                case "select":
                    if (!CanEdit()) break;
                    builder.CancelSelection();
                    xrMovingSelection = false;
                    SyncAll();
                    ObserveXrTutorial("select");
                    break;
                case "move":
                    if (!CanEdit() || string.IsNullOrEmpty(builder.SelectedPieceId)) break;
                    xrMovingSelection = true;
                    SyncBuildTargets();
                    ShowStatus("指向新位置，确认后移动；取消会保留原件。", SkybridgePalette.Mechanical, 2f);
                    break;
                case "delete": RemoveSelected(); break;
                case "undo": Undo(); break;
                case "redo": Redo(); break;
                case "rotate": Rotate(value); break;
                case "layer": ChangeLayer(value); break;
                case "ramp": SetRampDelta(value); break;
                case "check": CheckXrBridge(false); break;
                case "test": CheckXrBridge(true); break;
                case "stop": StopCurrentTest(); break;
                case "continue":
                    xrWorkbench.CloseModal();
                    xrAdjusting = false;
                    if (phase == GamePhase.Success) CloseResult();
                    break;
                case "resume-level":
                    xrWorkbench.CloseModal();
                    var next = LevelCatalog.All.FirstOrDefault(item => XrIsLevelUnlocked(item.Id) && !saveData.Levels[item.Id].LowestCost.HasValue);
                    LoadLevel(next?.Id ?? level.Id, true);
                    ResetXrWorkspace();
                    xrWorkbench.ShowLevelIntro();
                    break;
                case "level":
                    if (value < 1 || value > 5 || !XrIsLevelUnlocked((LevelId)value)) break;
                    ConfirmDiscard(() => { LoadLevel((LevelId)value); ResetXrWorkspace(); xrWorkbench.ShowLevelIntro(); });
                    break;
                case "next":
                    if (XrIsTutorialActive) { EndXrPractice(); break; }
                    xrWorkbench.CloseModal();
                    NextLevel();
                    ResetXrWorkspace();
                    xrWorkbench.ShowLevelIntro();
                    break;
                case "reset":
                    if (phase == GamePhase.Success) CloseResult();
                    ConfirmDiscard(() => { ResetBuild(); });
                    break;
                case "settings":
                    if (IsTestPhase(phase)) StopCurrentTest();
                    xrWorkbench.ShowSettings();
                    break;
                case "help":
                    if (IsTestPhase(phase)) StopCurrentTest();
                    xrWorkbench.ShowHelp();
                    break;
                case "cancel":
                    if (xrWorkbench.HasModal) { xrWorkbench.CloseModal(); xrAdjusting = false; }
                    else if (phase == GamePhase.Success) CloseResult();
                    else { xrMovingSelection = false; CancelBuildSelection(); }
                    break;
                case "tutorial-start": StartXrPractice(0); break;
                case "tutorial-replay": StartXrPractice(value); break;
                case "tutorial-skip":
                case "tutorial-exit": EndXrPractice(); break;
                case "tutorial-hint": xrTutorial?.RequestHint(); break;
                case "tutorial-retry":
                    if (XrIsTutorialActive) xrTutorial.RetryStep();
                    break;
                case "adjust":
                    if (IsTestPhase(phase)) StopCurrentTest();
                    xrAdjusting = true;
                    xrWorkbench.ShowAdjustment();
                    break;
                case "reposition":
                    if (IsTestPhase(phase)) StopCurrentTest();
                    xrAdjusting = false;
                    ResetXrWorkspace(true);
                    ObserveXrTutorial("workspace");
                    break;
                case "workspace-x": AdjustXrWorld(xrPlacementYaw * Vector3.right * value * 0.08f); break;
                case "workspace-y": AdjustXrWorld(Vector3.up * value * 0.08f); break;
                case "workspace-z": AdjustXrWorld(xrPlacementYaw * Vector3.forward * value * 0.08f); break;
                case "workspace-rotate":
                    if (!XrUsesWorldSpace || !xrAdjusting || value == 0) break;
                    XrAdjustView(Mathf.Sign(value) * 15f, 0, 0);
                    break;
                case "workspace-scale":
                    if (!XrUsesWorldSpace || !xrAdjusting || value == 0) break;
                    var focus = XrWorldFocus();
                    var before = picoWorldRoot.localScale.x;
                    var scale = Mathf.Clamp(before * (value > 0 ? 1.15f : 1f / 1.15f), 0.025f, 0.20f);
                    if (Mathf.Approximately(scale, before)) break;
                    picoWorldRoot.localScale = Vector3.one * scale;
                    picoWorldRoot.position += focus - XrWorldFocus();
                    ObserveXrTutorial("workspace");
                    break;
                case "workspace-reset":
                    if (!XrUsesWorldSpace) { ResetXrWorkspace(); break; }
                    if (picoWorldRoot.position == xrDefaultWorldPosition && picoWorldRoot.rotation == xrDefaultWorldRotation && picoWorldRoot.localScale == xrDefaultWorldScale) break;
                    picoWorldRoot.SetPositionAndRotation(xrDefaultWorldPosition, xrDefaultWorldRotation);
                    picoWorldRoot.localScale = xrDefaultWorldScale;
                    xrViewPitch = xrViewYaw = 0;
                    ObserveXrTutorial("workspace");
                    break;
                case "mute": SetMuted(value != 0); break;
                case "reduced-motion": SetReducedMotion(value != 0); break;
            }
            xrPreviewKey = null;
            Physics.SyncTransforms();
            xrWorkbench.Refresh();
            RefreshXrTutorial();
        }

        private void ConfirmDiscard(Action confirmed)
        {
            if (builder.GetLayout().AllPieces().Any())
                xrWorkbench.ShowConfirm("清空当前桥梁？", "当前局内桥梁方案将清空，已经获得的成绩保留。", confirmed);
            else { xrWorkbench.CloseModal(); confirmed(); }
        }

        private Vector3 XrWorldFocus() => world.transform.TransformPoint((world.GridToWorld(level.StartAnchor) + world.GridToWorld(level.GoalAnchor)) * 0.5f);
        private void FrameWorkspaceAtPlacement()
        {
            // Establish the initial scene layout only. Never push geometry behind a fixed eye-plane during view changes.
            var forward = xrPlacementYaw * Vector3.forward;
            var nearest = float.PositiveInfinity;
            foreach (var renderer in world.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                var bounds = renderer.bounds;
                var extents = bounds.extents;
                var projectedExtent = Mathf.Abs(forward.x) * extents.x + Mathf.Abs(forward.y) * extents.y + Mathf.Abs(forward.z) * extents.z;
                nearest = Mathf.Min(nearest, Vector3.Dot(bounds.center - xrPlacementOrigin, forward) - projectedExtent);
            }
            if (nearest < 1.7f) picoWorldRoot.position += forward * (1.7f - nearest);
        }
        private void AdjustXrWorld(Vector3 delta)
        {
            if (!XrUsesWorldSpace || !xrAdjusting || delta.sqrMagnitude < 0.000001f) return;
            var candidate = picoWorldRoot.position + delta;
            if (Vector3.Distance(candidate, xrDefaultWorldPosition) > 0.7f) return;
            var before = picoWorldRoot.position;
            picoWorldRoot.position = candidate;
            if ((picoWorldRoot.position - before).sqrMagnitude > 0.000001f) ObserveXrTutorial("workspace");
        }

        private void CheckXrBridge(bool start)
        {
            if (!CanEdit()) return;
            var analysis = BridgeRules.AnalyzeSpans(level, builder.GetLayout());
            if (!analysis.Validation.CanTest)
            {
                var issue = analysis.Validation.Issues.FirstOrDefault();
                HighlightIssue(issue?.PieceId);
                ShowStatus(issue?.Message ?? "道路尚未连到终点。请查看高亮缺口。", SkybridgePalette.Invalid, 4f);
                return;
            }
            if (analysis.WeakPoints.Count > 0)
            {
                var message = "存在薄弱跨度。普通跨度最多 3 块桥板，完整桁架最多 5 块；可以增加合法桥墩或连续桁架。";
                world.PulseWarningJoint(analysis.WeakPoints[0].PathNodeIndex);
                ShowStatus(message, SkybridgePalette.Warning, 4f);
                if (start) StartCurrentTest();
                return;
            }
            ShowStatus("桥梁已连通，结构检查通过。可以开始通车。", SkybridgePalette.Valid, 3f);
            if (start) StartCurrentTest();
        }

        private bool XrPrepareWorldPick(SkybridgePickable pickable)
        {
            if (xrTutorial != null && XrIsTutorialActive && !xrTutorial.Allows("pick")) return false;
            if (!builder.ActiveTool.HasValue && !xrMovingSelection)
            {
                if (pickable.Kind != PickKind.Piece)
                {
                    if (string.IsNullOrEmpty(builder.SelectedPieceId)) return false;
                    xrMovingSelection = true;
                    return true;
                }
                builder.CancelSelection();
                builder.HandleHit(BuildHit.Piece(pickable.Id));
                world.SelectPiece(pickable.Id);
                SyncUi();
                ObserveXrTutorial("select");
                return false;
            }
            if (!xrMovingSelection && (builder.ActiveTool == PieceType.Deck || builder.ActiveTool == PieceType.Ramp))
            {
                var slot = pickable.Kind == PickKind.RoadTarget ? builder.GetRoadSlots().FirstOrDefault(item => item.Index == pickable.Index) : null;
                if (pickable.Kind == PickKind.Piece || !string.IsNullOrEmpty(slot?.RoadPieceId))
                {
                    ShowStatus("这里已有部件。再次点击已选材料取消选材，再点击部件进行编辑。", SkybridgePalette.Warning, 2f);
                    return false;
                }
            }
            return true;
        }

        public void XrCommitWorldPick(SkybridgePickable pickable, Vector3 point) => OnWorldPick(pickable, point);
        public void XrHidePreview()
        {
            if (xrPreviewKey == null) return;
            xrPreviewKey = null;
            if (world != null) world.HideGhost();
        }

        public void XrPreviewWorldPick(SkybridgePickable pickable)
        {
            if (!CanEdit() || pickable == null) { XrHidePreview(); return; }
            var key = $"{pickable.GetEntityId()}:{builder.ActiveTool}:{builder.SelectedPieceId}:{builder.ActiveLayer}:{builder.RampDelta}:{builder.RotationQuarterTurns}:{builder.GetCost()}:{xrMovingSelection}";
            if (xrPreviewKey == key) return;
            xrPreviewKey = key;
            UpdateGhost(pickable);
            var preview = builder.Preview(PickToBuildHit(pickable));
            var message = builder.ActiveTool.HasValue || xrMovingSelection ? (preview.Valid ? "可放置 · 扣一下扳机确认" : preview.Message) : "选择部件 · 扣一下扳机";
            xrWorkbench.SetPointerHint(message, !builder.ActiveTool.HasValue || preview.Valid);
        }

        private void StartXrPractice(int lesson)
        {
            if (IsTestPhase(phase)) StopCurrentTest();
            if (xrPracticeSession == null)
                xrPracticeSession = new XrPracticeSession { Level = level, Builder = builder, Phase = phase, Outcome = LastTestOutcome, Route = activeTestRouteNodes };
            xrWorkbench.CloseModal();
            xrAdjusting = false;
            xrMovingSelection = false;
            xrTutorial.Start(lesson);
            LoadLevel(SkybridgeTutorialDirector.GetPracticeLevelId(lesson), true);
            builder.ReplaceLayout(SkybridgeTutorialDirector.CreatePracticeLayout(lesson));
            builder.CancelSelection();
            SyncAll();
            ResetXrWorkspace();
            ObserveXrTutorial("sync");
        }

        private void EndXrPractice()
        {
            if (xrPracticeSession == null) { xrWorkbench.CloseModal(); return; }
            CancelTestAnimation();
            xrTutorial.Stop(xrTutorial.IsComplete);
            var original = xrPracticeSession;
            xrPracticeSession = null;
            level = original.Level;
            builder = original.Builder; // Preserve undo/redo and selection by restoring the original session object.
            activeTestRouteNodes = original.Route;
            LastTestOutcome = original.Outcome;
            xrMovingSelection = false;
            xrAdjusting = false;
            xrWorkbench.CloseModal();
            hud.HideResult();
            RestoreWorldFromBuilder();
            SetPhase(original.Phase == GamePhase.Success ? GamePhase.Success : GamePhase.Build);
            SyncAll();
            ResetXrWorkspace();
            xrWorkbench.SetTutorial("", "", "", false);
            if (phase == GamePhase.Success)
            {
                var cost = builder.GetCost();
                var best = saveData.Levels[level.Id].LowestCost;
                hud.ShowResult(BridgeRules.CalculateRating(level, cost, true), cost,
                    best.HasValue ? Mathf.RoundToInt((float)best.Value) : (int?)null, level.Number < LevelCatalog.All.Count);
            }
            else ShowStatus("已返回正式游戏，原桥梁方案和撤销记录均保留。", SkybridgePalette.Valid, 3f);
        }

        private void ObserveXrTutorial(string action)
        {
            if (!XrIsTutorialActive || xrTutorial == null) return;
            xrTutorial.Observe(action, builder, phase, LastTestOutcome);
            RefreshXrTutorial();
        }

        private void RefreshXrTutorial()
        {
            if (xrWorkbench == null || xrTutorial == null) return;
            xrWorkbench.SetTutorial(xrTutorial.Title, xrTutorial.Body, xrTutorial.Progress, XrIsTutorialActive, xrTutorial.AssistanceLevel);
        }

        private void XrShowRestoredResult()
        {
            if (xrWorkbench == null) return;
            ObserveXrTutorial("failure");
            xrWorkbench.ShowResult(LastTestOutcome == TestOutcome.Failure ? "桥梁断裂 · 方案已恢复" : "测试异常 · 方案已恢复",
                LastTestOutcome == TestOutcome.Failure ? "薄弱跨度在车辆经过时断开。请增加合法桥墩或完整桁架，再开始通车。原方案与材料已保留。" : LastStatusMessage, false);
        }

        private void DisposeXrPresentation()
        {
            if (orbitCamera != null) orbitCamera.ViewAdjusted -= ObserveDesktopViewAdjustment;
            if (xrTutorial != null) xrTutorial.Changed -= RefreshXrTutorial;
            if (xrWorkbench != null) Destroy(xrWorkbench.gameObject);
            if (hud != null && hud.transform.parent == null) Destroy(hud.gameObject);
        }
    }
}
