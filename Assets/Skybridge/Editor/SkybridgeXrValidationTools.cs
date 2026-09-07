using System;
using System.IO;
using System.Linq;
using Skybridge.Core;
using Skybridge.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.UI;

namespace Skybridge.Editor
{
    /// <summary>Explicit Editor menus. Loading this class never starts a new test or build.</summary>
    [InitializeOnLoad]
    public static class SkybridgeXrValidationTools
    {
        private const string Prefix = "Skybridge.XR.Validation.";
        private const string TestModeKey = Prefix + "TestMode";
        private const string TestStageKey = Prefix + "TestStage";
        private const string TestRunKey = Prefix + "TestRun";
        private const string SaveSnapshotKey = Prefix + "SaveSnapshot";
        private const string SaveExistedKey = Prefix + "SaveExisted";
        private const string SaveSnapshotActiveKey = Prefix + "SaveSnapshotActive";
        private const string BuildStageKey = Prefix + "BuildStage";
        private const string BuildStartedKey = Prefix + "BuildStarted";
        private static readonly ResultsCallbacks Callbacks = new ResultsCallbacks();
        private static TestRunnerApi testRunner;
        private static bool buildCallActive;
        private static double nextBuildCheck;

        public static string EvidenceDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../work"));
        private static string TestModeName => SessionState.GetString(TestModeKey, "editmode");
        private static string TestXmlPath => Path.Combine(EvidenceDirectory, "editor-tests-" + TestModeName + ".xml");
        private static string TestStatusPath => Path.Combine(EvidenceDirectory, "editor-tests-" + TestModeName + "-status.json");
        private static string BuildStatusPath => Path.Combine(EvidenceDirectory, "android-build-status.json");

        static SkybridgeXrValidationTools()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            // Register again after domain reloads so genuine PlayMode run completion is retained.
            TestRunnerApi.RegisterTestCallback(Callbacks);
            if (SessionState.GetString(BuildStageKey, "") != "")
            {
                nextBuildCheck = EditorApplication.timeSinceStartup + 2;
                EditorApplication.update += AdvanceQueuedBuild;
            }
            if (SessionState.GetString(TestStageKey, "") == "queued")
                EditorApplication.delayCall += StartQueuedTests;
        }

        [MenuItem("Skybridge/XR/Enable Preview")]
        public static void EnablePreview()
        {
            if (!EditorApplication.isPlaying || SkybridgeGameController.Instance == null)
                throw new InvalidOperationException("Enter Play Mode in SkybridgeMain before enabling XR preview.");
            SkybridgeGameController.Instance.EnableXrPreview();
            Debug.Log("SKYBRIDGE_XR_PREVIEW_ENABLED: editor preview only; headset tracking is not simulated.");
        }

        [MenuItem("Skybridge/XR/Apply Preview Action")]
        public static void ApplyPreviewAction()
        {
            var game = SkybridgeGameController.Instance;
            if (!EditorApplication.isPlaying || game == null || !game.UsesXrPresentation)
                throw new InvalidOperationException("Enable XR preview in Play Mode first.");
            var request = JsonUtility.FromJson<PreviewAction>(File.ReadAllText(Path.Combine(EvidenceDirectory, "xr-preview-action.json")));
            if (request.action == "button")
            {
                Canvas.ForceUpdateCanvases();
                Physics.SyncTransforms();
                var button = game.Hud.GetComponentsInChildren<Button>().First(item => item.name == request.button);
                var rect = button.GetComponent<RectTransform>();
                var point = rect.TransformPoint(rect.rect.center);
                var origin = game.XrCamera.transform.position + new Vector3(0.2f, -0.15f, 0f);
                if (game.XrPointer != null) game.XrPointer.ClickForAutomation(new Ray(origin, point - origin));
                else
                {
                    var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                    { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
                    UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                    UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                    UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                }
            }
            else if (request.action == "world-preview") game.EnableXrPreview(true);
            else if (request.action == "view") game.XrAdjustView(request.yaw, request.pitch, request.zoom);
            else if (request.action == "level")
            {
                game.XrWorkbench.CloseModal();
                game.LoadLevel((LevelId)Mathf.Clamp(request.level, 1, 5), true);
                game.XrDispatch("reposition");
            }
            else if (request.action == "vehicle")
            {
                game.XrWorkbench.CloseModal();
                game.ApplyReferenceSolution();
                var path = BridgeRules.AnalyzeSpans(game.CurrentLevel, game.Builder.GetLayout()).Validation.Path;
                game.World.SetVehicleGridPath(path.Nodes);
                game.World.SetVehicleProgress(request.progress);
            }
            else throw new ArgumentException("Unknown preview action.");
            Canvas.ForceUpdateCanvases();
            Debug.Log("SKYBRIDGE_XR_PREVIEW_ACTION:" + request.action + ":" + request.button);
        }

        [Serializable]
        private sealed class PreviewAction
        {
            public string action, button;
            public int level;
            public float progress, yaw, pitch, zoom;
        }

        [MenuItem("Skybridge/XR/Run EditMode Tests")]
        public static void QueueEditModeTests() => QueueTests("editmode");

        [MenuItem("Skybridge/XR/Run PlayMode Tests")]
        public static void QueuePlayModeTests() => QueueTests("playmode");

        private static void QueueTests(string mode)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave manual Play Mode before running the test suite.");
            if (SessionState.GetString(TestStageKey, "") != "" || SessionState.GetString(BuildStageKey, "") != "" || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("A validation run or build is already pending.");
            SnapshotProgress();
            SessionState.SetString(TestModeKey, mode);
            SessionState.SetString(TestStageKey, "queued");
            SessionState.SetString(TestRunKey, "");
            WriteJson(TestStatusPath, new TestStatus { stage = "queued", mode = mode, updatedUtc = UtcNow() });
            EditorApplication.delayCall += StartQueuedTests;
            Debug.Log("SKYBRIDGE_TESTS_QUEUED:" + mode);
        }

        [MenuItem("Skybridge/XR/Start Pending Tests")]
        private static void StartQueuedTests()
        {
            if (SessionState.GetString(TestStageKey, "") != "queued") return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += StartQueuedTests;
                return;
            }
            try
            {
                testRunner = ScriptableObject.CreateInstance<TestRunnerApi>();
                var mode = TestModeName == "playmode" ? TestMode.PlayMode : TestMode.EditMode;
                var assembly = mode == TestMode.PlayMode ? "Skybridge.Tests.PlayMode" : "Skybridge.Tests.EditMode";
                // Whole normal test assembly, with no test-name/category or synchronous filters.
                var settings = new ExecutionSettings(new Filter { testMode = mode, assemblyNames = new[] { assembly } })
                {
                    runSynchronously = false,
                };
                SessionState.SetString(TestStageKey, "starting");
                WriteJson(TestStatusPath, new TestStatus { stage = "starting", mode = TestModeName, updatedUtc = UtcNow() });
                var runId = testRunner.Execute(settings);
                SessionState.SetString(TestRunKey, runId ?? "");
            }
            catch (Exception exception)
            {
                FinishTestsWithError(exception.ToString());
            }
        }

        private static void SnapshotProgress()
        {
            if (SessionState.GetBool(SaveSnapshotActiveKey, false))
                throw new InvalidOperationException("An earlier progress snapshot still needs restoration.");
            SessionState.SetBool(SaveExistedKey, PlayerPrefs.HasKey(SaveDataService.SaveDataKey));
            SessionState.SetString(SaveSnapshotKey, PlayerPrefs.GetString(SaveDataService.SaveDataKey, ""));
            SessionState.SetBool(SaveSnapshotActiveKey, true);
        }

        private static void RestoreProgress()
        {
            if (!SessionState.GetBool(SaveSnapshotActiveKey, false)) return;
            if (SessionState.GetBool(SaveExistedKey, false))
                PlayerPrefs.SetString(SaveDataService.SaveDataKey, SessionState.GetString(SaveSnapshotKey, ""));
            else
                PlayerPrefs.DeleteKey(SaveDataService.SaveDataKey);
            PlayerPrefs.Save();
            SessionState.EraseString(SaveSnapshotKey);
            SessionState.EraseBool(SaveExistedKey);
            SessionState.EraseBool(SaveSnapshotActiveKey);
        }

        private static void FinishTestsWithError(string message)
        {
            try
            {
                WriteJson(TestStatusPath, new TestStatus { stage = "failed", mode = TestModeName, runId = SessionState.GetString(TestRunKey, ""), updatedUtc = UtcNow(), message = message });
                Debug.LogError("SKYBRIDGE_TEST_RUN_FAILED:" + message);
            }
            finally
            {
                RestoreProgress();
                SessionState.EraseString(TestStageKey);
            }
        }

        private sealed class ResultsCallbacks : IErrorCallbacks
        {
            private static bool OwnsRun => SessionState.GetString(TestStageKey, "") != "";
            public void RunStarted(ITestAdaptor testsToRun)
            {
                if (!OwnsRun) return;
                SessionState.SetString(TestStageKey, "running");
                WriteJson(TestStatusPath, new TestStatus { stage = "running", mode = TestModeName, runId = SessionState.GetString(TestRunKey, ""), updatedUtc = UtcNow() });
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!OwnsRun) return;
                try
                {
                    Directory.CreateDirectory(EvidenceDirectory);
                    TestRunnerApi.SaveResultToFile(result, TestXmlPath);
                    var count = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
                    // A zero-test run or a run containing only skips is never a pass.
                    var passed = result.FailCount == 0 && result.InconclusiveCount == 0 && result.PassCount > 0 && result.ResultState == "Passed";
                    WriteJson(TestStatusPath, new TestStatus
                    {
                        stage = passed ? "passed" : "failed",
                        mode = TestModeName,
                        runId = SessionState.GetString(TestRunKey, ""),
                        updatedUtc = UtcNow(),
                        resultState = result.ResultState,
                        total = count,
                        passed = result.PassCount,
                        failed = result.FailCount,
                        skipped = result.SkipCount,
                        inconclusive = result.InconclusiveCount,
                        durationSeconds = result.Duration,
                        xmlPath = TestXmlPath,
                        message = count == 0 ? "No tests were executed." : result.Message,
                    });
                    Debug.Log("SKYBRIDGE_TEST_RUN_COMPLETE:" + TestModeName + ":" + result.ResultState + ":" + TestXmlPath);
                }
                catch (Exception exception)
                {
                    FinishTestsWithError(exception.ToString());
                }
                finally
                {
                    RestoreProgress();
                    SessionState.EraseString(TestStageKey);
                }
            }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void OnError(string message)
            {
                if (OwnsRun) FinishTestsWithError(message);
            }
        }

        [MenuItem("Skybridge/XR/Queue Android Build")]
        public static void QueueAndroidBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play Mode before queuing an Android build.");
            if (SessionState.GetString(BuildStageKey, "") != "" || SessionState.GetString(TestStageKey, "") != "" || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("A validation run or build is already pending.");
            SessionState.SetString(BuildStartedKey, UtcNow());
            SessionState.SetString(BuildStageKey, "queued");
            WriteBuildStatus("queued", "Build requested; not started yet.");
            EditorApplication.delayCall += ArmBuildQueue;
            Debug.Log("SKYBRIDGE_ANDROID_BUILD_QUEUED");
        }

        [MenuItem("Skybridge/XR/Start Pending Build")]
        private static void ArmBuildQueue()
        {
            nextBuildCheck = EditorApplication.timeSinceStartup;
            EditorApplication.update -= AdvanceQueuedBuild;
            EditorApplication.update += AdvanceQueuedBuild;
            AdvanceQueuedBuild();
        }

        private static void AdvanceQueuedBuild()
        {
            var stage = SessionState.GetString(BuildStageKey, "");
            if (stage == "")
            {
                EditorApplication.update -= AdvanceQueuedBuild;
                return;
            }
            if (buildCallActive || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorApplication.timeSinceStartup < nextBuildCheck) return;
            nextBuildCheck = EditorApplication.timeSinceStartup + 1;
            try
            {
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                    throw new BuildFailedException("This Unity Editor has no usable Android Build Support module.");
                if (stage == "building")
                {
                    // A domain reload erased the initiating stack without a completion report.
                    FailBuild("Build was interrupted by an Editor reload; no successful completion was recorded.");
                    return;
                }
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                {
                    if (stage == "switching")
                    {
                        if (DateTime.TryParse(SessionState.GetString(BuildStartedKey, ""), out var requested) && DateTime.UtcNow - requested.ToUniversalTime() > TimeSpan.FromMinutes(15))
                            throw new BuildFailedException("Android target switch did not complete within 15 minutes; no build was started.");
                        return;
                    }
                    SessionState.SetString(BuildStageKey, "switching");
                    WriteBuildStatus("switching", "Waiting for Android target switch and script reload.");
                    if (!EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android))
                        throw new BuildFailedException("Unity rejected the Android target switch.");
                    return;
                }
                SessionState.SetString(BuildStageKey, "ready");
                WriteBuildStatus("ready", "Android target is ready; build queued on the next Editor tick.");
                EditorApplication.update -= AdvanceQueuedBuild;
                EditorApplication.delayCall += BuildAndroidNow;
            }
            catch (Exception exception)
            {
                FailBuild(exception.ToString());
            }
        }

        [MenuItem("Skybridge/XR/Build Pending Android")]
        private static void BuildAndroidNow()
        {
            if (SessionState.GetString(BuildStageKey, "") != "ready") return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ArmBuildQueue();
                return;
            }
            SessionState.SetString(BuildStageKey, "building");
            buildCallActive = true;
            WriteBuildStatus("building", "BuildPipeline.BuildPlayer is executing. This does not install or publish the APK.");
            try
            {
                SkybridgeProjectTools.BuildPicoAndroid();
                RecordBuildReport(SkybridgeProjectTools.LastPicoAndroidBuildReport);
            }
            catch (Exception exception)
            {
                var report = SkybridgeProjectTools.LastPicoAndroidBuildReport;
                if (report != null) SaveBuildReport(report);
                FailBuild(exception.ToString());
            }
            finally
            {
                buildCallActive = false;
            }
        }

        private static void RecordBuildReport(BuildReport report)
        {
            if (report == null) throw new BuildFailedException("Unity returned no BuildReport.");
            SaveBuildReport(report);
            var summary = report.summary;
            var succeeded = summary.result == BuildResult.Succeeded && File.Exists(summary.outputPath);
            WriteBuildStatus(succeeded ? "succeeded" : "failed", "Unity BuildReport result: " + summary.result, report);
            SessionState.EraseString(BuildStageKey);
        }

        private static void SaveBuildReport(BuildReport report)
        {
            var summary = report.summary;
            var detail = new BuildReportRecord
            {
                result = summary.result.ToString(),
                outputPath = summary.outputPath,
                platform = summary.platform.ToString(),
                startedUtc = summary.buildStartedAt.ToUniversalTime().ToString("O"),
                endedUtc = summary.buildEndedAt.ToUniversalTime().ToString("O"),
                durationSeconds = summary.totalTime.TotalSeconds,
                totalBytes = summary.totalSize.ToString(),
                errors = summary.totalErrors,
                warnings = summary.totalWarnings,
                steps = report.steps.Select(step => new BuildStepRecord
                {
                    name = step.name,
                    durationSeconds = step.duration.TotalSeconds,
                    messages = step.messages.Select(message => message.type + ": " + message.content).ToArray(),
                }).ToArray(),
            };
            WriteJson(Path.Combine(EvidenceDirectory, "android-build-report.json"), detail);
            var native = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/LastBuild.buildreport"));
            if (File.Exists(native)) File.Copy(native, Path.Combine(EvidenceDirectory, "android-build-report.buildreport"), true);
        }

        private static void FailBuild(string message)
        {
            WriteBuildStatus("failed", message);
            SessionState.EraseString(BuildStageKey);
            Debug.LogError("SKYBRIDGE_ANDROID_BUILD_FAILED:" + message);
        }

        private static void WriteBuildStatus(string stage, string message, BuildReport report = null)
        {
            WriteJson(BuildStatusPath, new BuildStatus
            {
                stage = stage,
                requestedUtc = SessionState.GetString(BuildStartedKey, ""),
                updatedUtc = UtcNow(),
                message = message,
                outputPath = report == null ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", SkybridgeProjectTools.PicoAndroidBuildPath)) : report.summary.outputPath,
                result = report == null ? "" : report.summary.result.ToString(),
                version = SkybridgeProjectTools.InternalCandidateVersion,
                bundleVersionCode = PlayerSettings.Android.bundleVersionCode,
            });
        }

        private static string UtcNow() => DateTime.UtcNow.ToString("O");
        private static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(value, true));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        [Serializable]
        private sealed class TestStatus
        {
            public string stage, mode, runId, updatedUtc, resultState, xmlPath, message;
            public int total, passed, failed, skipped, inconclusive;
            public double durationSeconds;
        }
        [Serializable]
        private sealed class BuildStatus
        {
            public string stage, requestedUtc, updatedUtc, message, outputPath, result, version;
            public int bundleVersionCode;
        }
        [Serializable]
        private sealed class BuildReportRecord
        {
            public string result, outputPath, platform, startedUtc, endedUtc, totalBytes;
            public double durationSeconds;
            public int errors, warnings;
            public BuildStepRecord[] steps;
        }
        [Serializable]
        private sealed class BuildStepRecord
        {
            public string name;
            public double durationSeconds;
            public string[] messages;
        }
    }
}
