using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Skybridge.Core;
using Skybridge.Presentation;
using Skybridge.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using ByteDance.PICO.XR;

namespace Skybridge.Game
{
    [DefaultExecutionOrder(-1000)]
    public sealed partial class SkybridgeGameController : MonoBehaviour
    {
        private const float GoalStableSeconds = TestTimings.GoalStableMilliseconds / 1000f;
        private const float StallTimeoutSeconds = TestTimings.StallTimeoutMilliseconds / 1000f;

        public const float PicoVerticalFieldOfView = 92f;
        public const float PicoNearClipPlane = 0.03f;
        public const float PicoFarClipPlane = 1000f;

        private Camera worldCamera;
        private OrbitCameraController orbitCamera;
        private bool picoRuntime;
        private bool picoSimulator;
        private Transform picoWorldRoot;
        private Transform picoHudRoot;
        private SkybridgeWorldView world;
        private SkybridgeHud hud;
        private SkybridgeAudioEngine audioEngine;
        private SkybridgeSettings settings;
        private SaveDataV1 saveData;
        private LevelDefinition level;
        private BuildController builder;
        private GamePhase phase = GamePhase.LevelLoading;
        private Coroutine testRoutine;
        private int testToken;
        private bool initialized;
        private Vector2 leftPointerStart;
        private bool leftPointerTracking;
        private Vector2 rightPointerStart;
        private bool rightPointerTracking;
        private float vehicleSpeedMultiplier = 1f;
        private IReadOnlyList<GridPoint> activeTestRouteNodes;

        public static SkybridgeGameController Instance { get; private set; }
        public LevelDefinition CurrentLevel => level;
        public BuildController Builder => builder;
        public SkybridgeWorldView World => world;
        public SkybridgeHud Hud => hud;
        public SkybridgeAudioMode AudioMode => audioEngine != null ? audioEngine.Mode : SkybridgeAudioMode.Build;
        public GamePhase Phase => phase;
        public TestOutcome LastTestOutcome { get; private set; } = TestOutcome.Pending;
        public string LastStatusMessage { get; private set; } = string.Empty;
        public bool IsInitialized => initialized;
        public float VehicleSpeedMultiplier
        {
            get => vehicleSpeedMultiplier;
            set => vehicleSpeedMultiplier = Mathf.Clamp(value, 0.1f, 50f);
        }

        public event Action<GamePhase> PhaseChanged;
        public event Action<TestOutcome> TestFinished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeController()
        {
            if (FindAnyObjectByType<SkybridgeGameController>() != null) return;
            new GameObject("SkybridgeGame").AddComponent<SkybridgeGameController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            InitializeGame();
        }

        private void InitializeGame()
        {
            if (initialized) return;
            picoRuntime = Application.platform == RuntimePlatform.Android;
            if (picoRuntime)
            {
                using var androidBuild = new AndroidJavaClass("android.os.Build");
                var hardware = androidBuild.GetStatic<string>("HARDWARE");
                picoSimulator = hardware == "ranchu" || hardware == "goldfish";
                if (picoSimulator) Debug.Log("SKYBRIDGE_PICO_SIMULATOR_POINTER_ENABLED: mouse/touch screen rays; hardware=" + hardware);
            }
            Application.targetFrameRate = picoRuntime ? 90 : 60;
            Application.runInBackground = true;
            settings = SkybridgeSettingsService.Load();
            saveData = SaveDataService.LoadSaveData();
            CreateCamera();

            // The editable model layer lives in SkybridgeMain so its
            // materials and child transforms can be tuned from the Scene
            // view.  Keep the authoring preview out of the Game view while
            // passing its templates to the runtime world.
            var sceneModelLibrary = FindAnyObjectByType<SkybridgeSceneModelLibrary>(FindObjectsInactive.Include);
            sceneModelLibrary?.SetPreviewVisible(false);

            var worldObject = new GameObject("World");
            worldObject.transform.SetParent(picoRuntime ? picoWorldRoot : transform, false);
            world = worldObject.AddComponent<SkybridgeWorldView>();
            world.Initialize(worldCamera, sceneModelLibrary);
            world.SetReducedMotion(settings.ReducedMotion);

            var hudObject = new GameObject("HUD");
            hudObject.transform.SetParent(picoRuntime ? picoHudRoot : transform, false);
            hud = hudObject.AddComponent<SkybridgeHud>();
            hud.Initialize();
            BindHudEvents();

            var audioObject = new GameObject("Audio");
            audioObject.transform.SetParent(transform, false);
            audioEngine = audioObject.AddComponent<SkybridgeAudioEngine>();
            audioEngine.Initialize(settings.Muted);
            hud.SetSettings(settings.Muted, settings.ReducedMotion, settings.AutoFocus);

            initialized = true;
            LoadLevel(LevelId.Level1, true);
            if (picoRuntime)
            {
                InitializeXrPresentation();
                StartCoroutine(PlacePicoPresentationAfterTracking());
            }
            if (!picoRuntime) EnableXrPreview();
            StartCoroutine(ProcessAutomationArguments());
        }

        private void CreateCamera()
        {
            var picoOrigin = FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (picoRuntime)
            {
                if (picoOrigin == null || picoOrigin.Camera == null)
                {
                    throw new InvalidOperationException(
                        "PICO Android requires a scene XR Origin with an assigned XR Camera.");
                }

                picoOrigin.gameObject.SetActive(true);
                worldCamera = picoOrigin.Camera;
                picoWorldRoot = picoOrigin.transform.Find("PICO WorldRoot");
                if (picoWorldRoot == null)
                {
                    throw new InvalidOperationException(
                        "PICO Android requires PICO WorldRoot under XR Origin.");
                }
                picoHudRoot = picoOrigin.transform.Find("PICO HUD");
                if (picoHudRoot == null)
                {
                    throw new InvalidOperationException(
                        "PICO Android requires PICO HUD under XR Origin.");
                }

                if (picoOrigin.GetComponent<PXR_Manager>() == null)
                {
                    picoOrigin.gameObject.AddComponent<PXR_Manager>();
                }
                var picoInput = picoOrigin.GetComponent<SkybridgePicoControllerInput>() ??
                                picoOrigin.gameObject.AddComponent<SkybridgePicoControllerInput>();
                picoInput.Initialize(this, worldCamera);
                return;
            }
            if (picoOrigin != null) picoOrigin.gameObject.SetActive(false);
            worldCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (worldCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                worldCamera = cameraObject.AddComponent<Camera>();
            }
            if (worldCamera.GetComponent<AudioListener>() == null) worldCamera.gameObject.AddComponent<AudioListener>();
            // Match the Three.js PerspectiveCamera defaults.  OrbitCamera
            // also reapplies this when it computes a focus envelope.
            worldCamera.nearClipPlane = 0.1f;
            worldCamera.depth = 0f;
            orbitCamera = worldCamera.GetComponent<OrbitCameraController>() ?? worldCamera.gameObject.AddComponent<OrbitCameraController>();
            orbitCamera.enabled = true;
            orbitCamera.Initialize(settings.ReducedMotion, settings.AutoFocus);
        }

        private IEnumerator PlacePicoPresentationAfterTracking()
        {
            if (!picoRuntime || picoWorldRoot == null || worldCamera == null) yield break;
            yield return null;
            yield return new WaitForEndOfFrame();
            if (picoWorldRoot == null || worldCamera == null || world == null || level == null) yield break;

            ResetXrWorkspace();
        }

        private void BindHudEvents()
        {
            hud.ComponentSelected += type => { if (UsesXrPresentation) XrDispatch("tool", (int)type); else SelectComponent(type); };
            hud.LevelSelected += selected => { if (UsesXrPresentation) XrDispatch("level", (int)selected); else LoadLevel(selected); };
            hud.UndoRequested += Undo;
            hud.RedoRequested += Redo;
            hud.RotateRequested += Rotate;
            hud.LayerChangeRequested += ChangeLayer;
            hud.RampDeltaRequested += SetRampDelta;
            hud.DeleteRequested += RemoveSelected;
            hud.ResetRequested += () => { if (UsesXrPresentation) XrDispatch("reset"); else ResetBuild(); };
            hud.TestRequested += () => { if (UsesXrPresentation) XrDispatch(IsTestPhase(phase) ? "stop" : "test"); else ToggleTest(); };
            hud.RestartTestRequested += RestartCurrentTest;
            hud.CameraResetRequested += ResetCamera;
            hud.NextLevelRequested += () => { if (UsesXrPresentation) XrDispatch("next"); else NextLevel(); };
            hud.CloseResultRequested += CloseResult;
            hud.MutedChanged += SetMuted;
            hud.ReducedMotionChanged += SetReducedMotion;
            hud.AutoFocusChanged += SetAutoFocus;
        }

        public bool LoadLevel(LevelId levelId, bool force = false)
        {
            if (!initialized) return false;
            if (!force && !SaveDataService.IsLevelUnlocked(saveData, levelId))
            {
                ShowStatus("该关卡尚未解锁。", SkybridgePalette.Invalid, 1.8f);
                audioEngine.PlayInvalid();
                return false;
            }

            CancelTestAnimation();
            level = LevelCatalog.Get(levelId);
            builder = new BuildController(level);
            activeTestRouteNodes = null;
            LastTestOutcome = TestOutcome.Pending;
            hud.HideResult();
            hud.ClearStatus();
            LastStatusMessage = string.Empty;
            world.LoadLevel(level);
            world.SetLayout(builder.GetLayout());
            if (picoRuntime && !builder.ActiveTool.HasValue) builder.SelectTool(PieceType.Deck);
            if (UsesDesktopCamera)
            {
                orbitCamera.SetReducedMotion(settings.ReducedMotion);
                orbitCamera.SetAutoFocus(
                    settings.AutoFocus,
                    world.GetSceneBounds(),
                    world.GridToWorld(level.StartAnchor),
                    world.GridToWorld(level.GoalAnchor));
            }
            SetPhase(GamePhase.Build);
            SyncAll();
            if (UsesDesktopCamera && settings.AutoFocus)
            {
                orbitCamera.Focus(
                    world.GetSceneBounds(),
                    world.GridToWorld(level.StartAnchor),
                    world.GridToWorld(level.GoalAnchor));
            }
            audioEngine.SetTestMode(false);
            return true;
        }

        public bool ApplyReferenceSolution()
        {
            if (phase != GamePhase.Build || builder == null) return false;
            builder.ReplaceLayout(level.ReferenceSolution.Layout);
            SyncAll();
            return true;
        }

        public bool SetLayoutForAutomation(BridgeLayout layout)
        {
            if (phase != GamePhase.Build || builder == null || layout == null) return false;
            builder.ReplaceLayout(layout);
            SyncAll();
            return true;
        }

        public void ClearProgress()
        {
            saveData = SaveDataService.CreateDefaultSaveData();
            SaveDataService.SaveSaveData(saveData);
            SyncUi();
        }

        private void SelectComponent(PieceType type)
        {
            if (!CanEdit()) return;
            xrMovingSelection = false;
            builder.SelectTool(type);
            world.ClearSelection();
            SyncBuildTargets();
            SyncUi();
            ObserveXrTutorial("tool");
        }

        private void Undo()
        {
            if (!CanEdit() || !builder.Undo()) return;
            audioEngine.PlayDelete();
            SyncAll();
            ObserveXrTutorial("undo");
        }

        private void Redo()
        {
            if (!CanEdit() || !builder.Redo()) return;
            audioEngine.PlayLock();
            SyncAll();
            ObserveXrTutorial("redo");
        }

        private void Rotate(int direction)
        {
            if (!CanEdit()) return;
            builder.Rotate(direction < 0 ? -1 : 1);
            SyncBuildTargets();
            SyncUi();
            ObserveXrTutorial("rotate");
        }

        private void ChangeLayer(int delta)
        {
            if (!CanEdit() || !builder.SetActiveLayer(builder.ActiveLayer + (delta < 0 ? -1 : 1))) return;
            world.ClearSelection();
            SyncBuildTargets();
            SyncUi();
            ObserveXrTutorial("layer");
        }

        private void SetRampDelta(int delta)
        {
            if (!CanEdit() || !builder.SetRampDelta(delta < 0 ? -1 : 1)) return;
            SyncBuildTargets();
            SyncUi();
            ObserveXrTutorial("ramp");
        }

        private void RemoveSelected()
        {
            if (!CanEdit()) return;
            var before = builder.GetLayout();
            var selectedId = builder.SelectedPieceId;
            if (!builder.RemoveSelected()) return;
            if (!string.IsNullOrEmpty(selectedId)) world.AnimatePieceRemoval(selectedId);
            foreach (var truss in before.Trusses.Where(candidate => candidate.RoadPieceId == selectedId))
            {
                world.AnimatePieceRemoval(truss.Id);
            }
            audioEngine.PlayDelete();
            ShowStatus("组件已解锁。", SkybridgePalette.Mechanical, 1.1f);
            SyncAll();
            ObserveXrTutorial("delete");
        }

        private void ResetBuild()
        {
            if (!CanEdit() || !builder.Reset()) return;
            audioEngine.PlayDelete();
            ShowStatus("桥梁已重置。", SkybridgePalette.Mechanical, 1.1f);
            SyncAll();
        }

        private void ResetCamera()
        {
            if (XrUsesWorldSpace)
            {
                ResetXrWorkspace(true);
                return;
            }
            if (world != null) orbitCamera.ResetTo(
                world.GetSceneBounds(),
                world.GridToWorld(level.StartAnchor),
                world.GridToWorld(level.GoalAnchor));
        }

        private bool CanEdit()
        {
            return builder != null && GameSessionRules.CanEditBridge(phase) &&
                   (!UsesXrPresentation || !XrInteractionBlocked);
        }

        private void ToggleTest()
        {
            if (phase == GamePhase.Build)
            {
                StartCurrentTest();
                return;
            }
            if (IsTestPhase(phase)) StopCurrentTest();
        }

        public bool StartCurrentTest()
        {
            if (phase != GamePhase.Build || testRoutine != null) return false;
            if (UsesXrPresentation && XrInteractionBlocked) return false;
            LastTestOutcome = TestOutcome.Pending;
            var token = ++testToken;
            testRoutine = StartCoroutine(RunTest(token));
            return true;
        }

        public void StopCurrentTest()
        {
            if (!IsTestPhase(phase)) return;
            CancelTestAnimation();
            LastTestOutcome = TestOutcome.Pending;
            RestoreWorldFromBuilder();
            SetPhase(GamePhase.Build);
            audioEngine.SetTestMode(false);
            ShowStatus("测试已停止。", SkybridgePalette.Mechanical, 1.1f);
            ObserveXrTutorial("stop");
        }

        public void RestartCurrentTest()
        {
            if (!IsTestPhase(phase)) return;
            StopCurrentTest();
            StartCurrentTest();
        }

        private IEnumerator RunTest(int token)
        {
            SetPhase(GamePhase.Validating);
            yield return null;
            if (token != testToken) yield break;

            var snapshot = builder.GetLayout();
            var analysis = BridgeRules.AnalyzeSpans(level, snapshot);
            if (!analysis.Validation.CanTest || analysis.Validation.Path == null)
            {
                var issue = analysis.Validation.Issues.FirstOrDefault();
                SetPhase(GamePhase.Build);
                audioEngine.PlayInvalid();
                ShowStatus(issue?.Message ?? "桥面尚未连通。", SkybridgePalette.Invalid, 2.6f);
                HighlightIssue(issue?.PieceId);
                testRoutine = null;
                yield break;
            }

            builder.CancelSelection();
            world.HideGhost();
            world.ClearSelection();
            activeTestRouteNodes = analysis.Validation.Path.Nodes.ToArray();
            world.SetVehicleGridPath(activeTestRouteNodes);
            world.SetVehicleProgress(0f);
            SetPhase(GamePhase.TestPreparing);
            audioEngine.SetTestMode(true);
            if (UsesDesktopCamera && settings.AutoFocus) orbitCamera.Focus(
                world.GetSceneBounds(),
                world.GridToWorld(level.StartAnchor),
                world.GridToWorld(level.GoalAnchor));
            yield return settings.ReducedMotion ? null : new WaitForSecondsRealtime(0.12f);
            if (token != testToken) yield break;

            var weakPoint = analysis.WeakPoints.FirstOrDefault();
            var breakRequested = false;
            var warned = false;
            var completed = false;
            var lastProgress = 0f;
            var lastProgressAt = Time.realtimeSinceStartup;
            SetPhase(GamePhase.VehicleRunning);
            ObserveXrTutorial("test");
            ShowStatus(
                weakPoint != null ? "结构可测试，检测到薄弱跨度。" : "结构通过检查。",
                weakPoint != null ? SkybridgePalette.Warning : SkybridgePalette.Valid,
                1.8f);

            world.StartVehicle(
                (level.BuildVolume != null ? 2.8f : 1.35f) * vehicleSpeedMultiplier,
                progress =>
                {
                    if (progress <= lastProgress + 0.0001f) return;
                    lastProgress = progress;
                    lastProgressAt = Time.realtimeSinceStartup;
                },
                waypointIndex =>
                {
                    if (weakPoint == null || token != testToken) return;
                    if (!warned && waypointIndex >= weakPoint.PathNodeIndex)
                    {
                        warned = true;
                        audioEngine.PlayWarning();
                        world.PulseWarningJoint(weakPoint.PathNodeIndex);
                        world.EmitSparks(world.GridToWorld(weakPoint.Joint), settings.ReducedMotion ? 4 : 9);
                    }
                    if (waypointIndex >= weakPoint.PathNodeIndex + 1) breakRequested = true;
                },
                () => completed = true);

            while (token == testToken && world.IsRunning)
            {
                if (breakRequested)
                {
                    world.StopVehicle();
                    yield return BreakAndRestore(weakPoint, snapshot, token);
                    testRoutine = null;
                    yield break;
                }
                if (Time.realtimeSinceStartup - lastProgressAt >= StallTimeoutSeconds)
                {
                    world.StopVehicle();
                    yield return RestoreRuntimeFailure(token, "测试已恢复，请重新启动。");
                    testRoutine = null;
                    yield break;
                }
                yield return null;
            }

            if (token != testToken || !completed)
            {
                testRoutine = null;
                yield break;
            }

            yield return new WaitForSecondsRealtime(GoalStableSeconds);
            if (token != testToken)
            {
                testRoutine = null;
                yield break;
            }

            if (world.IsVehicleFullyInGoal()) CompleteSuccess();
            else yield return RestoreRuntimeFailure(token, "车辆未完整进入终点，方案已恢复。");
            testRoutine = null;
        }

        private IEnumerator BreakAndRestore(WeakPoint weakPoint, BridgeLayout snapshot, int token)
        {
            SetPhase(GamePhase.Breaking);
            audioEngine.PlayBreak();
            ShowStatus("中央接头断开。", SkybridgePalette.Warning, 1.5f);
            var fallingIds = new HashSet<string> { weakPoint.BeforePieceId, weakPoint.AfterPieceId };
            foreach (var truss in snapshot.Trusses)
            {
                if (fallingIds.Contains(truss.RoadPieceId)) fallingIds.Add(truss.Id);
            }
            var failureZoneEntered = false;
            var breakAnimation = StartCoroutine(world.BreakAndFall(
                weakPoint,
                fallingIds,
                () => failureZoneEntered = true));
            yield return new WaitForSecondsRealtime(TestTimings.BreakSlowMotionMilliseconds / 1000f);
            if (token != testToken) yield break;
            SetPhase(GamePhase.VehicleFalling);
            yield return breakAnimation;
            if (token != testToken) yield break;

            LastTestOutcome = GameSessionRules.ResolveTestOutcome(
                TestOutcome.Pending,
                new TestOutcomeSignals(false, failureZoneEntered));
            if (LastTestOutcome == TestOutcome.Pending) LastTestOutcome = TestOutcome.RuntimeError;
            TestFinished?.Invoke(LastTestOutcome);
            SetPhase(GamePhase.RestoreSnapshot);
            RestoreWorldFromBuilder();
            yield return null;
            SetPhase(GamePhase.Build);
            audioEngine.SetTestMode(false);
            ShowStatus(
                LastTestOutcome == TestOutcome.Failure ? "方案已恢复。" : "测试未进入失败区，方案已恢复。",
                LastTestOutcome == TestOutcome.Failure ? SkybridgePalette.Mechanical : SkybridgePalette.Invalid,
                LastTestOutcome == TestOutcome.Failure ? 1.25f : 2.2f);
            XrShowRestoredResult();
        }

        private IEnumerator RestoreRuntimeFailure(int token, string message)
        {
            if (token != testToken) yield break;
            LastTestOutcome = GameSessionRules.ResolveTestOutcome(
                TestOutcome.Pending,
                new TestOutcomeSignals(false, false, true));
            TestFinished?.Invoke(LastTestOutcome);
            SetPhase(GamePhase.RestoreSnapshot);
            RestoreWorldFromBuilder();
            yield return null;
            SetPhase(GamePhase.Build);
            audioEngine.SetTestMode(false);
            ShowStatus(message, SkybridgePalette.Invalid, 2.2f);
            XrShowRestoredResult();
        }

        private void CompleteSuccess()
        {
            var outcome = GameSessionRules.ResolveTestOutcome(
                TestOutcome.Pending,
                new TestOutcomeSignals(true, false));
            if (outcome != TestOutcome.Success) return;
            LastTestOutcome = outcome;
            var cost = builder.GetCost();
            var medal = BridgeRules.CalculateRating(level, cost, true);
            var previousBest = saveData.Levels[level.Id].LowestCost;
            if (!XrIsTutorialActive)
            {
                saveData = SaveDataService.RecordLevelCompletion(saveData, level.Id, medal, cost);
                SaveDataService.SaveSaveData(saveData);
            }
            SetPhase(GamePhase.Success);
            audioEngine.SetSilentMode();
            audioEngine.PlaySuccess();
            SyncUi();
            var currentBest = saveData.Levels[level.Id].LowestCost;
            hud.ShowResult(
                medal,
                cost,
                previousBest.HasValue || currentBest.HasValue ? Mathf.RoundToInt((float)(currentBest ?? cost)) : (int?)null,
                level.Number < LevelCatalog.All.Count);
            TestFinished?.Invoke(LastTestOutcome);
            ObserveXrTutorial("success");
            xrWorkbench?.ShowResult(XrIsTutorialActive ? "练习通车成功" : "运输成功", $"小车已安全到达对岸。材料消耗：{cost}", true);
        }

        private void CloseResult()
        {
            if (phase != GamePhase.Success) return;
            hud.HideResult();
            world.SetVehicleProgress(0f);
            LastTestOutcome = TestOutcome.Pending;
            SetPhase(GamePhase.Build);
            audioEngine.SetTestMode(false);
            SyncAll();
        }

        private void NextLevel()
        {
            if (level.Number >= LevelCatalog.All.Count)
            {
                CloseResult();
                return;
            }
            LoadLevel((LevelId)(level.Number + 1));
        }

        private void SetMuted(bool muted)
        {
            settings.Muted = muted;
            SkybridgeSettingsService.Save(settings);
            audioEngine.SetMuted(muted);
            hud.SetSettings(settings.Muted, settings.ReducedMotion, settings.AutoFocus);
        }

        private void SetReducedMotion(bool enabled)
        {
            settings.ReducedMotion = enabled;
            SkybridgeSettingsService.Save(settings);
            if (UsesDesktopCamera) orbitCamera.SetReducedMotion(enabled);
            world.SetReducedMotion(enabled);
        }

        private void SetAutoFocus(bool enabled)
        {
            settings.AutoFocus = enabled;
            SkybridgeSettingsService.Save(settings);
            if (world != null)
            {
                if (UsesDesktopCamera)
                {
                    orbitCamera.SetAutoFocus(
                        enabled,
                        world.GetSceneBounds(),
                        world.GridToWorld(level.StartAnchor),
                        world.GridToWorld(level.GoalAnchor));
                }
            }
            hud.SetSettings(settings.Muted, settings.ReducedMotion, settings.AutoFocus);
        }

        private void OnWorldPick(SkybridgePickable pickable, Vector3 point)
        {
            if (!CanEdit()) return;
            if (pickable == null)
            {
                if (!builder.ActiveTool.HasValue) CancelBuildSelection();
                return;
            }

            var before = builder.GetLayout();
            if (UsesXrPresentation && !XrPrepareWorldPick(pickable)) return;
            var hit = PickToBuildHit(pickable);
            var action = builder.HandleHit(hit);
            if (action.Changed)
            {
                var after = builder.GetLayout();
                world.EmitSparks(world.WorldToContentPoint(point), settings.ReducedMotion ? 4 : 9);
                audioEngine.PlayLock();
                ShowStatus(action.Message, SkybridgePalette.Valid, 0.9f);
                SyncAll();
                foreach (var placement in after.AllPieces())
                {
                    var previous = before.AllPieces().FirstOrDefault(candidate => candidate.Id == placement.Id);
                    if (previous == null || !SamePlacement(previous, placement)) world.AnimatePiecePlacement(placement.Id);
                }
                xrMovingSelection = false;
                ObserveXrTutorial("pick");
                return;
            }

            if (!action.Valid)
            {
                audioEngine.PlayInvalid();
                ShowStatus(action.Message, SkybridgePalette.Invalid, 1.8f);
            }
            if (!string.IsNullOrEmpty(action.SelectedPieceId)) world.SelectPiece(action.SelectedPieceId);
            else world.ClearSelection();
            SyncBuildTargets();
            SyncUi();
            ObserveXrTutorial("select");
        }

        private BuildHit PickToBuildHit(SkybridgePickable pickable)
        {
            switch (pickable.Kind)
            {
                case PickKind.RoadTarget:
                    return BuildHit.Road(pickable.Index);
                case PickKind.FoundationTarget:
                    return BuildHit.Foundation(pickable.Index);
                case PickKind.TrussTarget:
                    return BuildHit.Truss(pickable.Id);
                case PickKind.Piece:
                    return PiecePickToBuildHit(pickable.Id);
                default:
                    return BuildHit.Empty();
            }
        }

        private BuildHit PiecePickToBuildHit(string pieceId)
        {
            var piece = builder.GetLayout().AllPieces().FirstOrDefault(candidate => candidate.Id == pieceId);
            if (piece == null) return BuildHit.Empty();
            var placement = builder.GetPlacementType();
            if (!placement.HasValue) return BuildHit.Piece(pieceId);
            if (placement == PieceType.Truss && piece is RoadPiecePlacement trussRoad)
            {
                return BuildHit.Truss(trussRoad.Id, pieceId);
            }
            if (piece is RoadPiecePlacement road)
            {
                return BuildHit.Road(builder.FindRoadSlotIndex(road) ?? -1, pieceId);
            }
            if (piece is TrussPlacement truss) return BuildHit.Truss(truss.RoadPieceId, pieceId);
            if (piece is PierPlacement pier)
            {
                var foundations = builder.GetFoundationSlots();
                for (var index = 0; index < foundations.Count; index++)
                {
                    if (foundations[index].Foundation == pier.Foundation) return BuildHit.Foundation(index, pieceId);
                }
            }
            return BuildHit.Piece(pieceId);
        }

        private void UpdateGhost(SkybridgePickable pickable)
        {
            if (UsesXrPresentation && !builder.ActiveTool.HasValue && !xrMovingSelection)
            {
                world.HideGhost();
                return;
            }
            if (!CanEdit() || pickable == null || (!builder.ActiveTool.HasValue && string.IsNullOrEmpty(builder.SelectedPieceId)))
            {
                world.HideGhost();
                return;
            }
            var preview = builder.Preview(PickToBuildHit(pickable));
            if (!preview.Type.HasValue)
            {
                world.HideGhost();
                return;
            }
            if (preview.Type == PieceType.Pier && preview.FoundationIndex.HasValue)
            {
                var foundations = builder.GetFoundationSlots();
                var index = preview.FoundationIndex.Value;
                if (index >= 0 && index < foundations.Count) world.ShowPierGhost(foundations[index].Top, preview.Valid);
                else world.HideGhost();
                return;
            }
            if (preview.From.HasValue && preview.To.HasValue)
            {
                world.ShowRoadGhost(preview.Type.Value, preview.From.Value, preview.To.Value, preview.Valid);
                return;
            }
            world.HideGhost();
        }

        private void SyncAll()
        {
            if (!initialized || builder == null) return;
            world.SetLayout(builder.GetLayout());
            SyncBuildTargets();
            if (!string.IsNullOrEmpty(builder.SelectedPieceId)) world.SelectPiece(builder.SelectedPieceId);
            else world.ClearSelection();
            SyncUi();
        }

        private void SyncBuildTargets()
        {
            if (builder == null || world == null) return;
            var placement = builder.GetPlacementType();
            var targets = new List<BuildTargetViewData>();
            if (placement != PieceType.Pier && placement != PieceType.Truss)
            {
                var horizontal = builder.RotationQuarterTurns % 2 == 0;
                foreach (var slot in builder.GetRoadSlots())
                {
                    if (!slot.InBounds || horizontal && slot.Axis != RoadAxis.X || !horizontal && slot.Axis != RoadAxis.Z) continue;
                    targets.Add(new BuildTargetViewData
                    {
                        Id = $"cell-{slot.Index}",
                        Kind = BuildTargetViewKind.Road,
                        Index = slot.Index,
                        From = slot.From,
                        To = slot.To,
                    });
                }
            }
            if (placement == PieceType.Truss)
            {
                foreach (var road in builder.GetLayout().Roads)
                {
                    targets.Add(new BuildTargetViewData
                    {
                        Id = $"truss-{road.Id}",
                        Kind = BuildTargetViewKind.Truss,
                        RoadPieceId = road.Id,
                        From = road.From,
                        To = road.To,
                    });
                }
            }
            if (placement == PieceType.Pier || !placement.HasValue && level.BuildVolume == null)
            {
                var foundations = builder.GetFoundationSlots();
                for (var index = 0; index < foundations.Count; index++)
                {
                    targets.Add(new BuildTargetViewData
                    {
                        Id = $"foundation-{index}",
                        Kind = BuildTargetViewKind.Foundation,
                        Index = index,
                        Point = foundations[index].Top,
                    });
                }
            }
            world.SetBuildTargets(targets);
            world.HideGhost();
        }

        private void SyncUi()
        {
            if (!initialized || builder == null) return;
            hud.SetPhase(phase);
            hud.SetLevel(level);
            hud.SetLevelProgress(saveData, level.Id);
            hud.SetCost(builder.GetCost(), level.MedalThresholds.Gold, level.MedalThresholds.Silver);
            hud.SetInventory(builder.GetInventoryRemaining(), level.Inventory, builder.ActiveTool, CanEdit());
            hud.SetCommandAvailability(builder.CanUndo && CanEdit(), builder.CanRedo && CanEdit(), !string.IsNullOrEmpty(builder.SelectedPieceId) && CanEdit());
            var bounds = builder.GetLayerBounds();
            hud.SetBuildVolumeControls(
                bounds.HasValue,
                builder.ActiveLayer,
                bounds?.Min ?? 0,
                bounds?.Max ?? 0,
                builder.RampDelta);
            xrWorkbench?.Refresh();
        }

        private void SetPhase(GamePhase next)
        {
            if (phase == next)
            {
                SyncUi();
                return;
            }
            phase = next;
            SyncUi();
            PhaseChanged?.Invoke(phase);
        }

        private void RestoreWorldFromBuilder()
        {
            Time.timeScale = 1f;
            world.LoadLevel(level);
            world.SetLayout(builder.GetLayout());
            if (activeTestRouteNodes != null && activeTestRouteNodes.Count >= 2)
            {
                // Three.js preserves the validated route through stop/failure
                // recovery.  Reapply it after Unity rebuilds the dynamic
                // objects so Level 5 turns and dock directions do not revert
                // to the authored default path.
                world.SetVehicleGridPath(activeTestRouteNodes);
            }
            world.SetVehicleProgress(0f);
            SyncBuildTargets();
            world.ClearSelection();
            if (UsesDesktopCamera && settings.AutoFocus) orbitCamera.Focus(
                world.GetSceneBounds(),
                world.GridToWorld(level.StartAnchor),
                world.GridToWorld(level.GoalAnchor));
        }

        private void CancelTestAnimation()
        {
            testToken++;
            if (testRoutine != null)
            {
                StopCoroutine(testRoutine);
                testRoutine = null;
            }
            if (world != null) world.StopSimulation();
            Time.timeScale = 1f;
        }

        private void CancelBuildSelection()
        {
            if (!CanEdit()) return;
            builder.CancelSelection();
            world.HideGhost();
            world.ClearSelection();
            SyncBuildTargets();
            SyncUi();
            ObserveXrTutorial("cancel");
        }

        private void HighlightIssue(string pieceId)
        {
            if (!string.IsNullOrEmpty(pieceId))
            {
                world.SelectPiece(pieceId);
                return;
            }
            var firstGap = builder.GetRoadSlots().FirstOrDefault(slot => string.IsNullOrEmpty(slot.RoadPieceId));
            if (firstGap != null) world.SelectTarget(firstGap.Index);
        }

        private static bool SamePlacement(PiecePlacement left, PiecePlacement right)
        {
            if (left == null || right == null || left.Type != right.Type || left.Id != right.Id) return false;
            if (left is RoadPiecePlacement leftRoad && right is RoadPiecePlacement rightRoad)
            {
                return leftRoad.From == rightRoad.From && leftRoad.To == rightRoad.To;
            }
            if (left is PierPlacement leftPier && right is PierPlacement rightPier)
            {
                return leftPier.Top == rightPier.Top && leftPier.Foundation == rightPier.Foundation;
            }
            if (left is TrussPlacement leftTruss && right is TrussPlacement rightTruss)
            {
                return leftTruss.RoadPieceId == rightTruss.RoadPieceId;
            }
            return false;
        }

        private void ShowStatus(string message, Color accent, float seconds)
        {
            LastStatusMessage = message ?? string.Empty;
            hud.ShowStatus(LastStatusMessage, accent, seconds);
            xrWorkbench?.Refresh();
        }

        private static bool IsTestPhase(GamePhase value)
        {
            return value == GamePhase.Validating ||
                   value == GamePhase.TestPreparing ||
                   value == GamePhase.VehicleRunning ||
                   value == GamePhase.Breaking ||
                   value == GamePhase.VehicleFalling ||
                   value == GamePhase.RestoreSnapshot;
        }

        private void Update()
        {
            if (!initialized) return;
            if (UsesXrPresentation)
            {
                UpdateXrPresentation();
                if (XrUsesWorldSpace) return;
            }
            if (picoRuntime) return;
            HandleKeyboard();
            HandlePointer();
        }

        /// <summary>
        /// Called by the PICO controller adapter. The adapter owns physical
        /// input details; this class retains the desktop-tested game rules.
        /// </summary>
        public void HandlePicoRay(Ray ray)
        {
            if (UsesXrPresentation)
            {
                // Compatibility entry point still passes through the single XR router.
                xrPointer?.ClickForAutomation(ray);
                return;
            }
            if (hud != null && hud.TryHandlePicoRay(ray)) return;
            if (!CanEdit() || world == null) return;
            var preferBuildTargets = builder.ActiveTool.HasValue || !string.IsNullOrEmpty(builder.SelectedPieceId);
            world.TryPick(ray, preferBuildTargets, out var pickable, out var point);
            OnWorldPick(pickable, point);
        }

        public void PreviewPicoRay(Ray ray)
        {
            if (UsesXrPresentation) return;
            if (!CanEdit() || world == null) return;
            var preferBuildTargets = builder.ActiveTool.HasValue || !string.IsNullOrEmpty(builder.SelectedPieceId);
            world.TryPick(ray, preferBuildTargets, out var pickable, out _);
            UpdateGhost(pickable);
        }

        public void PicoRotate(int direction) => Rotate(direction);
        public void PicoChangeLayer(int delta) => ChangeLayer(delta);
        public void PicoPrimaryAction()
        {
            if (UsesXrPresentation)
            {
                XrDispatch("help");
                return;
            }
            if (phase == GamePhase.Success)
            {
                NextLevel();
                return;
            }
            ToggleTest();
        }

        public void PicoSecondaryAction()
        {
            if (UsesXrPresentation)
            {
                XrDispatch("cancel");
                return;
            }
            if (phase == GamePhase.Success)
            {
                CloseResult();
                return;
            }
            CancelBuildSelection();
        }

        public void PicoCycleTool(int direction)
        {
            if (!CanEdit() || builder == null || level == null) return;
            var tools = new[] { PieceType.Deck, PieceType.Ramp, PieceType.Pier, PieceType.Truss };
            var currentIndex = builder.ActiveTool.HasValue ? Array.IndexOf(tools, builder.ActiveTool.Value) : -1;
            for (var offset = 1; offset <= tools.Length; offset++)
            {
                var index = (currentIndex + direction * offset + tools.Length * 2) % tools.Length;
                var tool = tools[index];
                if (level.Inventory[tool] <= 0 && builder.GetInventoryRemaining()[tool] <= 0) continue;
                SelectComponent(tool);
                return;
            }
        }

        public void PicoUndo() => Undo();
        public void PicoRedo() => Redo();

        private void HandleKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || IsTextInputFocused()) return;
            if (keyboard.escapeKey.wasPressedThisFrame && hud != null && hud.IsSettingsOpen)
            {
                hud.CloseSettings();
                return;
            }
            var modifier = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed;
            if (modifier && keyboard.zKey.wasPressedThisFrame)
            {
                if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) Redo();
                else Undo();
                return;
            }
            if (modifier && keyboard.yKey.wasPressedThisFrame)
            {
                Redo();
                return;
            }
            if (keyboard.qKey.wasPressedThisFrame) Rotate(-1);
            if (keyboard.eKey.wasPressedThisFrame) Rotate(1);
            if (keyboard.pageUpKey.wasPressedThisFrame) ChangeLayer(1);
            if (keyboard.pageDownKey.wasPressedThisFrame) ChangeLayer(-1);
            if (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) RemoveSelected();
            if (keyboard.spaceKey.wasPressedThisFrame) ToggleTest();
            if (keyboard.escapeKey.wasPressedThisFrame) CancelBuildSelection();
        }

        private void HandlePointer()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            var position = mouse.position.ReadValue();
            var overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            SkybridgePickable pickable = null;
            var point = Vector3.zero;
            var preferBuildTargets = builder != null && (builder.ActiveTool.HasValue || !string.IsNullOrEmpty(builder.SelectedPieceId));
            if (!overUi && CanEdit()) world.TryPick(position, preferBuildTargets, out pickable, out point);
            if (CanEdit()) UpdateGhost(pickable);

            // Match the Three.js canvas click contract: placement/selection is
            // committed on pointer-up only when the pointer travelled at most
            // five pixels.  Handling it on pointer-down made a drag or an
            // aborted click place a component immediately.
            if (mouse.leftButton.wasPressedThisFrame)
            {
                BeginLeftPointer(position, overUi);
            }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                ReleaseLeftPointer(position, overUi, pickable, point);
            }
            if (mouse.rightButton.wasPressedThisFrame)
            {
                BeginRightPointer(position, overUi);
            }
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                ReleaseRightPointer(position, overUi);
            }
        }

        private void BeginLeftPointer(Vector2 position, bool overUi)
        {
            leftPointerTracking = !overUi && CanEdit();
            if (leftPointerTracking) leftPointerStart = position;
        }

        private bool ReleaseLeftPointer(
            Vector2 position,
            bool overUi,
            SkybridgePickable pickable,
            Vector3 point)
        {
            if (!leftPointerTracking) return false;
            leftPointerTracking = false;
            if (overUi || !CanEdit() || !IsPointerClick(leftPointerStart, position)) return false;
            OnWorldPick(pickable, point);
            return true;
        }

        private void BeginRightPointer(Vector2 position, bool overUi)
        {
            // The browser reference listens on the scene canvas rather than
            // the whole window.  Do not let a right-click beginning over HUD
            // controls later cancel a world-space build tool.
            rightPointerTracking = !overUi && CanEdit();
            if (rightPointerTracking) rightPointerStart = position;
        }

        private bool ReleaseRightPointer(Vector2 position, bool overUi)
        {
            if (!rightPointerTracking) return false;
            rightPointerTracking = false;
            // Pointer capture in the browser canvas keeps the original
            // canvas-down gesture alive even when the pointer is released
            // over the HUD.  Only the down origin and click distance matter.
            if (!CanEdit() || !IsPointerClick(rightPointerStart, position)) return false;
            CancelBuildSelection();
            return true;
        }

        private static bool IsPointerClick(Vector2 pointerDown, Vector2 pointerUp)
        {
            return Vector2.Distance(pointerDown, pointerUp) <= 5f;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (xrPreview)
            {
                if (!hasFocus) { xrPointer?.CancelInput(); xrMouseOrbit = xrMousePan = false; ClearPointerTracking(); }
                return; // Keep preview visuals available, but never consume background desktop clicks.
            }
            xrApplicationFocused = hasFocus;
            if (!hasFocus) xrPointer?.CancelInput();
            if (hasFocus) return;
            ClearPointerTracking();
        }

        private void ClearPointerTracking()
        {
            leftPointerTracking = false;
            rightPointerTracking = false;
        }

        private static bool IsTextInputFocused()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<InputField>() != null;
        }

        private IEnumerator ProcessAutomationArguments()
        {
            yield return null;
            var args = Environment.GetCommandLineArgs();
            var smokeReport = GetArgumentValue(args, "-skybridgeSmokeAll");
            if (!string.IsNullOrEmpty(smokeReport))
            {
                yield return RunPlayerSmokeSuite(smokeReport);
                yield break;
            }

            var captureLevel = GetArgumentValue(args, "-skybridgeCaptureLevel");
            var capturePath = GetArgumentValue(args, "-skybridgeCapturePath");
            if (!int.TryParse(captureLevel, out var levelNumber) || string.IsNullOrEmpty(capturePath)) yield break;
            levelNumber = Mathf.Clamp(levelNumber, 1, LevelCatalog.All.Count);
            LoadLevel((LevelId)levelNumber, true);
            ApplyReferenceSolution();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            var directory = Path.GetDirectoryName(capturePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(capturePath, 1);
            yield return new WaitForSecondsRealtime(1f);
            Debug.Log($"SKYBRIDGE_CAPTURE_PASS {capturePath}");
            if (args.Contains("-skybridgeQuitAfterCapture")) Application.Quit(0);
        }

        private IEnumerator RunPlayerSmokeSuite(string reportPath)
        {
            var hadStoredSave = PlayerPrefs.HasKey(SaveDataService.SaveDataKey);
            var storedSave = hadStoredSave ? PlayerPrefs.GetString(SaveDataService.SaveDataKey) : null;
            var report = new List<string>
            {
                "Skybridge Unity player smoke suite",
                $"Unity={Application.unityVersion}",
            };
            var failed = false;
            try
            {
                vehicleSpeedMultiplier = 12f;
                settings.ReducedMotion = true;
                orbitCamera.SetReducedMotion(true);
                world.SetReducedMotion(true);
                foreach (var definition in LevelCatalog.All)
                {
                    LoadLevel(definition.Id, true);
                    ApplyReferenceSolution();
                    var analysis = BridgeRules.AnalyzeSpans(level, builder.GetLayout());
                    var setupValid = analysis.Validation.CanTest && analysis.WeakPoints.Count == 0 && builder.GetCost() == definition.ReferenceCost;
                    if (!setupValid || !StartCurrentTest())
                    {
                        report.Add($"{definition.Id}=FAIL setup");
                        failed = true;
                        continue;
                    }
                    var deadline = Time.realtimeSinceStartup + 20f;
                    while (phase != GamePhase.Success && Time.realtimeSinceStartup < deadline) yield return null;
                    var passed = phase == GamePhase.Success && LastTestOutcome == TestOutcome.Success;
                    report.Add($"{definition.Id}={(passed ? "PASS" : "FAIL")} cost={builder.GetCost()} routeNodes={analysis.Validation.Path?.Nodes.Count ?? 0}");
                    failed |= !passed;
                }

                LoadLevel(LevelId.Level2, true);
                var weakLayout = new BridgeLayout(level.ReferenceSolution.Layout.Roads);
                builder.ReplaceLayout(weakLayout);
                SyncAll();
                var weakAnalysis = BridgeRules.AnalyzeSpans(level, weakLayout);
                StartCurrentTest();
                var weakDeadline = Time.realtimeSinceStartup + 12f;
                while (IsTestPhase(phase) && Time.realtimeSinceStartup < weakDeadline) yield return null;
                var weakPassed = weakAnalysis.WeakPoints.Count > 0 && phase == GamePhase.Build && LastTestOutcome == TestOutcome.Failure;
                report.Add($"WeakSpanRestore={(weakPassed ? "PASS" : "FAIL")} weakPoints={weakAnalysis.WeakPoints.Count} outcome={LastTestOutcome} phase={phase}");
                failed |= !weakPassed;
            }
            finally
            {
                if (hadStoredSave) PlayerPrefs.SetString(SaveDataService.SaveDataKey, storedSave);
                else PlayerPrefs.DeleteKey(SaveDataService.SaveDataKey);
                PlayerPrefs.Save();
                saveData = SaveDataService.LoadSaveData();
            }

            var reportDirectory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(reportDirectory)) Directory.CreateDirectory(reportDirectory);
            File.WriteAllLines(reportPath, report);
            Debug.Log(failed ? "SKYBRIDGE_SMOKE_FAIL" : "SKYBRIDGE_SMOKE_PASS");
            yield return null;
            Application.Quit(failed ? 1 : 0);
        }

        private static string GetArgumentValue(IReadOnlyList<string> args, string key)
        {
            for (var index = 0; index < args.Count - 1; index++)
            {
                if (string.Equals(args[index], key, StringComparison.Ordinal)) return args[index + 1];
            }
            return null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CancelTestAnimation();
            DisposeXrPresentation();
        }
    }
}
