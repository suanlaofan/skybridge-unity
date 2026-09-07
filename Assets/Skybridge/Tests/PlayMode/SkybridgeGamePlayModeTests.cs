using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Skybridge.Core;
using Skybridge.Game;
using Skybridge.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Skybridge.Tests
{
    public sealed class SkybridgeGamePlayModeTests
    {
        private SkybridgeGameController game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            SaveDataService.ClearSaveData();
            if (SkybridgeGameController.Instance == null)
            {
                new GameObject("SkybridgeGame_PlayModeTest").AddComponent<SkybridgeGameController>();
            }
            yield return null;
            game = SkybridgeGameController.Instance;
            Assert.That(game, Is.Not.Null);
            Assert.That(game.IsInitialized, Is.True);
            game.VehicleSpeedMultiplier = 40f;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (game != null)
            {
                game.StopCurrentTest();
                Object.Destroy(game.gameObject);
            }
            yield return null;
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator BootstrapCreatesPlayableLevelAndUi()
        {
            yield return null;
            Assert.That(game.CurrentLevel.Id, Is.EqualTo(LevelId.Level1));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Build));
            Assert.That(game.World, Is.Not.Null);
            Assert.That(game.Hud, Is.Not.Null);
            Assert.That(game.World.WorldCamera, Is.Not.Null);
            Assert.That(game.World.GetSceneBounds().size.sqrMagnitude, Is.GreaterThan(1f));
            Assert.That(ColorUtility.ToHtmlStringRGB(game.World.WorldCamera.backgroundColor), Is.EqualTo("DFE5E4"));
            Assert.That(game.Hud.HudCanvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(game.Hud.transform.parent, Is.EqualTo(game.XrWorldRoot));
            Assert.That(RenderSettings.fog, Is.False);
            Assert.That(RenderSettings.fogMode, Is.EqualTo(FogMode.Linear));
            Assert.That(RenderSettings.fogStartDistance, Is.EqualTo(34f).Within(0.001f));
            Assert.That(RenderSettings.fogEndDistance, Is.EqualTo(78f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator BootstrapUsesImportedIslandGateAndVehicleModels()
        {
            yield return null;
            Assert.That(game.World.ImportedModelAssetsReady, Is.True,
                "The three user-provided FBX assets must be available under Resources/SkybridgeModels.");
            Assert.That(game.World.HasImportedModel("StartIsland"), Is.True);
            Assert.That(game.World.HasImportedModel("GoalIsland"), Is.True);
            Assert.That(game.World.HasImportedModel("GoalGate"), Is.True);
            var authoringLibrary = Object.FindAnyObjectByType<SkybridgeSceneModelLibrary>(FindObjectsInactive.Include);
            if (authoringLibrary != null)
            {
                Assert.That(authoringLibrary.IsConfigured, Is.True);
                Assert.That(authoringLibrary.PreviewRoot, Is.Not.Null);
                Assert.That(authoringLibrary.PreviewRoot.gameObject.activeSelf, Is.False,
                    "The Scene preview is hidden while the Game view uses its templates.");
            }
            var vehicle = FindDescendant(game.World.transform, "UnmannedRescueVehicle");
            Assert.That(vehicle, Is.Not.Null);
            Assert.That(FindDescendant(vehicle.transform, "ImportedModel"), Is.Not.Null);
            Assert.That(vehicle.GetComponent<BoxCollider>(), Is.Not.Null);

            foreach (var endpoint in new[] { "StartIsland", "GoalIsland", "GoalGate" })
            {
                var imported = game.World.GetImportedModel(endpoint);
                Assert.That(imported, Is.Not.Null, endpoint);
                Assert.That(Vector3.Dot(imported.up, game.World.transform.up), Is.GreaterThan(0.95f), endpoint);
                Assert.That(imported.GetComponentsInChildren<Renderer>(true), Is.Not.Empty, endpoint);
            }

            // The supplied circular energy-platform FBX is a horizontal
            // landing surface.  Its vertical extent must remain clearly
            // smaller than both footprint axes; this catches an accidental
            // 90-degree FBX axis correction that would stand it on edge.
            foreach (var islandName in new[] { "StartIsland", "GoalIsland" })
            {
                var islandBounds = BoundsInWorkspace(game.World.GetImportedModel(islandName), game.World.transform);
                Assert.That(islandBounds.size.y,
                    Is.LessThan(Mathf.Min(islandBounds.size.x, islandBounds.size.z) * 0.75f),
                    $"{islandName} must remain a horizontal platform, not a vertical disk.");
            }

            var vehicleModel = FindDescendant(vehicle.transform, "ImportedModel").transform;
            Assert.That(Vector3.Dot(vehicleModel.up, game.World.transform.up), Is.GreaterThan(0.95f));
            var vehicleBounds = BoundsInWorkspace(vehicleModel, game.World.transform);
            Assert.That(vehicleBounds.size.sqrMagnitude, Is.GreaterThan(0.1f));
            Assert.That(vehicleBounds.min.y, Is.GreaterThanOrEqualTo(SkybridgeWorldView.RoadBaseY - 0.02f));
            var gateMaterial = game.World.GetImportedModel("GoalGate")
                .GetComponentsInChildren<Renderer>(true)
                .First(renderer => renderer.sharedMaterial != null)
                .sharedMaterial;
            var vehicleMaterial = FindDescendant(vehicle.transform, "ImportedModel")
                .GetComponentsInChildren<Renderer>(true)
                .First(renderer => renderer.sharedMaterial != null)
                .sharedMaterial;
            Assert.That(gateMaterial.name, Does.Contain("GoalGateModel"));
            Assert.That(vehicleMaterial.name, Does.Contain("RescueVehicleModel"));

            // The endpoint visuals must be the imported assets, with no old
            // procedural island/gate/vehicle shells left under their roots.
            Assert.That(FindDescendant(game.World.transform, "EngineeringShell"), Is.Null);
            Assert.That(FindDescendant(game.World.transform, "Chassis"), Is.Null);
            Assert.That(FindDescendant(game.World.transform, "Lintel"), Is.Null);
        }

        [UnityTest]
        public IEnumerator HudUsesReferenceResolutionScalingForReadableUi()
        {
            var scaler = game.Hud.GetComponent<CanvasScaler>();
            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ConstantPixelSize));
            Assert.That(scaler.referenceResolution.x, Is.EqualTo(1280f).Within(0.001f));
            Assert.That(scaler.referenceResolution.y, Is.EqualTo(720f).Within(0.001f));
            Assert.That(scaler.screenMatchMode, Is.EqualTo(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight));
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(scaler.scaleFactor, Is.GreaterThan(0f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PicoWorldHudKeepsTheFullToolSelectionBarWithoutFollowingHead()
        {
            var hud = game.Hud;
            var originalParent = hud.transform.parent;
            var originalPose = hud.transform.position;
            var picoCameraObject = new GameObject("PicoWorldHudTestCamera");
            var picoCamera = picoCameraObject.AddComponent<Camera>();

            try
            {
                hud.ConfigurePicoPresentation(picoCamera, picoCamera.transform, 0.92f);
                yield return null;
                Canvas.ForceUpdateCanvases();

                var canvas = hud.GetComponent<Canvas>();
                var inventory = FindDescendant(hud.transform, "Inventory");
                var topbar = FindDescendant(hud.transform, "Topbar") as RectTransform;
                var commandRail = FindDescendant(hud.transform, "CommandRail") as RectTransform;
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
                Assert.That(canvas.worldCamera, Is.EqualTo(picoCamera));
                Assert.That(hud.transform.parent, Is.EqualTo(game.XrWorldRoot));
                AssertVector3Approximately(originalPose, hud.transform.position, "world HUD position");
                AssertVector3Approximately(
                    Vector3.one * (2.1f / 1280f),
                    hud.transform.lossyScale,
                    "world HUD base scale");
                Assert.That(Vector3.Dot(hud.transform.forward, picoCamera.transform.forward), Is.GreaterThan(0.999f));
                Assert.That(topbar, Is.Not.Null);
                Assert.That(commandRail, Is.Not.Null);
                AssertVector2Approximately(Vector2.zero, topbar.anchoredPosition, "original topbar position");
                AssertVector2Approximately(new Vector2(1f, 1f), commandRail.anchorMin, "PICO command rail anchor min");
                AssertVector2Approximately(new Vector2(1f, 1f), commandRail.anchorMax, "PICO command rail anchor max");
                AssertVector2Approximately(new Vector2(1f, 1f), commandRail.pivot, "PICO command rail pivot");
                Assert.That(commandRail.anchoredPosition.y, Is.LessThan(0f));
                Assert.That(inventory, Is.Not.Null);
                Assert.That(inventory.gameObject.activeSelf, Is.True);
                foreach (var toolName in new[] { "Deck", "Ramp", "Pier", "Truss" })
                {
                    Assert.That(FindDescendant(inventory, toolName), Is.Not.Null, toolName);
                }

                picoCamera.transform.SetPositionAndRotation(new Vector3(2f, 1.5f, -3f), Quaternion.Euler(0f, 35f, 0f));
                yield return null;
                AssertVector3Approximately(
                    originalPose,
                    hud.transform.position,
                    "world HUD must stay fixed when the head moves");
            }
            finally
            {
                hud.transform.SetParent(originalParent, false);
                Object.Destroy(picoCameraObject);
            }
        }

        [UnityTest]
        public IEnumerator EmptyBridgeIsRejectedWithoutStartingVehicle()
        {
            Assert.That(game.LoadLevel(LevelId.Level1, true), Is.True);
            Assert.That(game.StartCurrentTest(), Is.True);
            yield return null;
            yield return null;
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Build));
            Assert.That(game.LastTestOutcome, Is.EqualTo(TestOutcome.Pending));
            Assert.That(game.LastStatusMessage, Does.Contain("路径"));
            Assert.That(game.World.IsRunning, Is.False);
            Assert.That(FindDescendant(game.World.transform, "TargetIssueHighlight"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator RunningTestCanRestartWithoutReturningToEditFlow()
        {
            game.VehicleSpeedMultiplier = 5f;
            Assert.That(game.LoadLevel(LevelId.Level1, true), Is.True);
            Assert.That(game.ApplyReferenceSolution(), Is.True);
            Assert.That(game.StartCurrentTest(), Is.True);

            var runningDeadline = Time.realtimeSinceStartup + 2f;
            while (game.Phase != GamePhase.VehicleRunning && Time.realtimeSinceStartup < runningDeadline) yield return null;
            Assert.That(game.Phase, Is.EqualTo(GamePhase.VehicleRunning));
            var restart = FindDescendant(game.Hud.transform, "RestartTest");
            Assert.That(restart, Is.Not.Null);
            Assert.That(restart.gameObject.activeInHierarchy, Is.True);

            game.RestartCurrentTest();
            yield return null;
            yield return null;
            Assert.That(game.Phase, Is.Not.EqualTo(GamePhase.Build));
            Assert.That(game.Phase, Is.Not.EqualTo(GamePhase.Success));
            Assert.That(game.World.VehicleProgress, Is.LessThan(0.25f));

            var successDeadline = Time.realtimeSinceStartup + 6f;
            while (game.Phase != GamePhase.Success && Time.realtimeSinceStartup < successDeadline) yield return null;
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Success));
        }

        [UnityTest]
        public IEnumerator ClosingSuccessResultRestoresBuildAudioMode()
        {
            Assert.That(game.LoadLevel(LevelId.Level1, true), Is.True);
            Assert.That(game.ApplyReferenceSolution(), Is.True);
            Assert.That(game.StartCurrentTest(), Is.True);

            var deadline = Time.realtimeSinceStartup + 6f;
            while (game.Phase != GamePhase.Success && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Success));
            Assert.That(game.AudioMode, Is.EqualTo(SkybridgeAudioMode.Silent));

            var close = game.Hud.GetComponentsInChildren<Button>(true)
                .First(button => button.name == "Close" && button.gameObject.activeInHierarchy);
            close.onClick.Invoke();
            yield return null;

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Build));
            Assert.That(game.AudioMode, Is.EqualTo(SkybridgeAudioMode.Build));
        }

        [UnityTest]
        public IEnumerator HudUsesRenderableIconsAndAudioButtonEmitsOncePerClick()
        {
            var audio = FindDescendant(game.Hud.transform, "Audio");
            Assert.That(audio, Is.Not.Null);
            var icon = FindDescendant(audio, "Icon")?.GetComponent<Image>();
            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.sprite, Is.Not.Null);
            Assert.That(icon.sprite.texture.GetPixels32().Count(pixel => pixel.a > 0), Is.GreaterThan(24));
            var iconScreenPosition = RectTransformUtility.WorldToScreenPoint(game.Hud.HudCanvas.worldCamera, icon.rectTransform.position);
            Assert.That(iconScreenPosition.x, Is.InRange(0f, (float)Screen.width));
            Assert.That(iconScreenPosition.y, Is.InRange(0f, (float)Screen.height));
            Assert.That(icon.color.a, Is.GreaterThan(0.9f));

            var changeCount = 0;
            void OnMuted(bool _) => changeCount++;
            game.Hud.MutedChanged += OnMuted;
            var button = audio.GetComponent<Button>();
            button.onClick.Invoke();
            Assert.That(changeCount, Is.EqualTo(1));
            game.Hud.MutedChanged -= OnMuted;
            button.onClick.Invoke();
            yield return null;
        }

        [UnityTest]
        public IEnumerator FifthLevelTargetsSupportScreenSpaceSnapWithoutColliderHit()
        {
            Assert.That(game.LoadLevel(LevelId.Level5, true), Is.True);
            yield return null;
            var targets = game.World.GetComponentsInChildren<SkybridgePickable>(true)
                .Where(candidate => candidate.Kind == PickKind.RoadTarget)
                .ToArray();
            Assert.That(targets, Is.Not.Empty);
            var camera = game.World.WorldCamera;
            var candidate = targets
                .Select(target => new { Target = target, Screen = camera.WorldToScreenPoint(CalculateBounds(target.gameObject).center) })
                .Where(item => item.Screen.z > 0f && item.Screen.x > 80f && item.Screen.x < Screen.width - 80f && item.Screen.y > 80f && item.Screen.y < Screen.height - 80f)
                .OrderBy(item => Vector2.Distance(new Vector2(item.Screen.x, item.Screen.y), new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)))
                .First();
            foreach (var target in targets)
            {
                if (target != candidate.Target) target.gameObject.SetActive(false);
            }
            foreach (var collider in game.World.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            var snapRadius = SkybridgeWorldView.CalculateTargetSnapRadius(camera.pixelWidth);
            Assert.That(SkybridgeWorldView.CalculateTargetSnapRadius(1280f), Is.EqualTo(62f));
            Assert.That(snapRadius, Is.GreaterThan(34f));
            var probe = new Vector2(candidate.Screen.x + snapRadius - 0.25f, candidate.Screen.y);
            Assert.That(game.World.TryPick(probe, true, out var snapped, out _), Is.True);
            Assert.That(snapped, Is.Not.Null);
            Assert.That(snapped.Id, Is.EqualTo(candidate.Target.Id));
            yield return null;
        }

        [UnityTest]
        public IEnumerator IdlePickingPrefersExistingPieceOverTransparentBuildTarget()
        {
            Assert.That(game.LoadLevel(LevelId.Level1, true), Is.True);
            Assert.That(game.ApplyReferenceSolution(), Is.True);
            yield return null;
            Physics.SyncTransforms();

            var piece = game.World.GetComponentsInChildren<SkybridgePickable>(true)
                .First(candidate => candidate.Kind == PickKind.Piece && candidate.PieceType == PieceType.Deck.ToString());
            var screen = game.World.WorldCamera.WorldToScreenPoint(CalculateBounds(piece.gameObject).center);
            Assert.That(screen.z, Is.GreaterThan(0f));

            Assert.That(
                game.World.TryPick(new Vector2(screen.x, screen.y), false, out var idlePick, out _),
                Is.True,
                "An existing bridge component must remain selectable while idle targets are visible.");
            Assert.That(idlePick.Kind, Is.EqualTo(PickKind.Piece));
            Assert.That(idlePick.Id, Is.EqualTo(piece.Id));

            Assert.That(
                game.World.TryPick(new Vector2(screen.x, screen.y), true, out var placementPick, out _),
                Is.True);
            Assert.That(
                placementPick.Kind,
                Is.Not.EqualTo(PickKind.Piece),
                "With a tool or moving piece active, build targets must retain placement priority.");
        }

        [UnityTest]
        public IEnumerator DesktopHudSectionsKeepAllControlsInsideTheirPanels()
        {
            Assert.That(game.LoadLevel(LevelId.Level5, true), Is.True);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var canvas = game.Hud.GetComponent<RectTransform>();
            var topbar = FindDescendant(game.Hud.transform, "Topbar") as RectTransform;
            var volume = FindDescendant(game.Hud.transform, "VolumeControls") as RectTransform;
            var utilities = FindDescendant(game.Hud.transform, "Utilities") as RectTransform;
            var commandRail = FindDescendant(game.Hud.transform, "CommandRail") as RectTransform;
            var buildDock = FindDescendant(game.Hud.transform, "BuildDock") as RectTransform;
            Assert.That(canvas, Is.Not.Null);
            Assert.That(topbar, Is.Not.Null);
            Assert.That(volume, Is.Not.Null);
            Assert.That(utilities, Is.Not.Null);
            Assert.That(commandRail, Is.Not.Null);
            Assert.That(buildDock, Is.Not.Null);
            AssertRectContained(canvas, topbar);
            AssertRectContained(canvas, volume);
            AssertRectContained(canvas, commandRail);
            AssertRectContained(canvas, buildDock);
            AssertChildrenContained(topbar);
            AssertChildrenContained(volume);
            AssertChildrenContained(utilities);
            AssertChildrenContained(commandRail);
        }

        [UnityTest]
        public IEnumerator EveryReferenceSolutionCompletesThroughRuntimeVehicleFlow()
        {
            foreach (var definition in LevelCatalog.All)
            {
                Assert.That(game.LoadLevel(definition.Id, true), Is.True, definition.Id.ToString());
                Assert.That(game.ApplyReferenceSolution(), Is.True, definition.Id.ToString());
                var analysis = BridgeRules.AnalyzeSpans(definition, game.Builder.GetLayout());
                Assert.That(analysis.Validation.CanTest, Is.True, definition.Id.ToString());
                Assert.That(analysis.WeakPoints, Is.Empty, definition.Id.ToString());
                Assert.That(game.Builder.GetCost(), Is.EqualTo(definition.ReferenceCost), definition.Id.ToString());
                Assert.That(game.StartCurrentTest(), Is.True, definition.Id.ToString());

                var deadline = Time.realtimeSinceStartup + 8f;
                while (game.Phase != GamePhase.Success && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(game.Phase, Is.EqualTo(GamePhase.Success), definition.Id.ToString());
                Assert.That(game.LastTestOutcome, Is.EqualTo(TestOutcome.Success), definition.Id.ToString());
                Assert.That(game.World.IsVehicleFullyInGoal(), Is.True, definition.Id.ToString());
            }
        }

        [UnityTest]
        public IEnumerator PicoLikeTiltedScaledRootStillCompletesGoal()
        {
            Assert.That(game.LoadLevel(LevelId.Level1, true), Is.True);
            Assert.That(game.ApplyReferenceSolution(), Is.True);

            var picoWorldRoot = new GameObject("PicoLikeWorldRoot").transform;
            picoWorldRoot.position = new Vector3(0.18f, 1.4f, 0.92f);
            picoWorldRoot.rotation = Quaternion.Euler(22f, 180f, 0f);
            picoWorldRoot.localScale = Vector3.one * 0.055f;
            var originalParent = game.World.transform.parent;
            // Match the runtime hierarchy: World stays identity-local beneath
            // the tilted, scaled PICO WorldRoot rather than preserving its
            // desktop world transform during the reparent.
            game.World.transform.SetParent(picoWorldRoot, false);
            try
            {
                var path = BridgeRules.FindRoadPath(
                    game.CurrentLevel.ReferenceSolution.Layout.Roads,
                    game.CurrentLevel.StartAnchor,
                    game.CurrentLevel.GoalAnchor);
                Assert.That(path, Is.Not.Null);
                game.World.SetVehicleGridPath(path.Nodes);
                game.World.SetVehicleProgress(1f);
                Assert.That(game.World.IsVehicleFullyInGoal(), Is.True);
            }
            finally
            {
                game.World.transform.SetParent(originalParent, false);
                Object.Destroy(picoWorldRoot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator WeakSpanBreaksOnlyAfterRunAndRestoresExactLayout()
        {
            Assert.That(game.LoadLevel(LevelId.Level2, true), Is.True);
            var weakLayout = new BridgeLayout(game.CurrentLevel.ReferenceSolution.Layout.Roads);
            Assert.That(game.SetLayoutForAutomation(weakLayout), Is.True);
            var before = game.Builder.GetLayout();
            var analysis = BridgeRules.AnalyzeSpans(game.CurrentLevel, before);
            Assert.That(analysis.Validation.CanTest, Is.True);
            Assert.That(analysis.WeakPoints, Is.Not.Empty);
            Assert.That(game.StartCurrentTest(), Is.True);

            var deadline = Time.realtimeSinceStartup + 8f;
            while ((game.Phase != GamePhase.Build || game.LastTestOutcome == TestOutcome.Pending) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                game.LastTestOutcome,
                Is.EqualTo(TestOutcome.Failure),
                $"minimum vehicle Y was {game.World.MinimumBreakVehicleY:0.000}");
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Build));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            var restored = game.Builder.GetLayout();
            Assert.That(restored.Roads.Select(piece => piece.Id), Is.EquivalentTo(before.Roads.Select(piece => piece.Id)));
            Assert.That(restored.Piers, Is.Empty);
            Assert.That(restored.Trusses, Is.Empty);
            Assert.That(game.World.VehicleProgress, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator WeakSpanBreaksAtComputedJointInsteadOfNextRoadNode()
        {
            game.VehicleSpeedMultiplier = 4f;
            Assert.That(game.LoadLevel(LevelId.Level2, true), Is.True);
            var weakLayout = new BridgeLayout(game.CurrentLevel.ReferenceSolution.Layout.Roads);
            Assert.That(game.SetLayoutForAutomation(weakLayout), Is.True);
            var analysis = BridgeRules.AnalyzeSpans(game.CurrentLevel, weakLayout);
            var weakPoint = analysis.WeakPoints.Single();
            Assert.That(game.StartCurrentTest(), Is.True);

            var deadline = Time.realtimeSinceStartup + 4f;
            while (game.Phase != GamePhase.Breaking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Breaking));

            var distanceField = typeof(SkybridgeWorldView).GetField(
                "vehiclePathDistances",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var lengthField = typeof(SkybridgeWorldView).GetField(
                "vehiclePathLength",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(distanceField, Is.Not.Null);
            Assert.That(lengthField, Is.Not.Null);
            var distances = (List<float>)distanceField.GetValue(game.World);
            var pathLength = (float)lengthField.GetValue(game.World);
            var breakWaypointIndex = weakPoint.PathNodeIndex + 1;
            var breakProgress = distances[breakWaypointIndex] / pathLength;
            var nextNodeProgress = distances[breakWaypointIndex + 1] / pathLength;

            Assert.That(game.World.VehicleProgress, Is.GreaterThanOrEqualTo(breakProgress));
            Assert.That(
                game.World.VehicleProgress,
                Is.LessThan(nextNodeProgress),
                "The vehicle must break at the weak joint, not after crossing the following road piece.");
        }

        [UnityTest]
        public IEnumerator FifthLevelLayerBoundsAutoCorrectRampDirection()
        {
            Assert.That(game.LoadLevel(LevelId.Level5, true), Is.True);
            var builder = game.Builder;
            builder.SelectTool(PieceType.Ramp);
            Assert.That(builder.SetActiveLayer(10), Is.True);
            Assert.That(builder.RampDelta, Is.EqualTo(-1));
            Assert.That(builder.GetRoadSlots(), Has.Count.EqualTo(110));
            Assert.That(builder.SetActiveLayer(0), Is.True);
            Assert.That(builder.RampDelta, Is.EqualTo(1));
            Assert.That(builder.GetRoadSlots(), Has.Count.EqualTo(110));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FifthLevelHudLayerButtonsRegenerateTargetsOnSelectedLayer()
        {
            Assert.That(game.LoadLevel(LevelId.Level5, true), Is.True);
            yield return null;

            var layerUp = FindDescendant(game.Hud.transform, "LayerUp")?.GetComponent<Button>();
            var layerDown = FindDescendant(game.Hud.transform, "LayerDown")?.GetComponent<Button>();
            Assert.That(layerUp, Is.Not.Null);
            Assert.That(layerDown, Is.Not.Null);
            Assert.That(layerUp.interactable, Is.True);
            Assert.That(layerDown.interactable, Is.False);

            layerUp.onClick.Invoke();
            yield return null;

            Assert.That(game.Builder.ActiveLayer, Is.EqualTo(1));
            Assert.That(
                game.World.GetComponentsInChildren<SkybridgePickable>(true)
                    .Where(candidate => candidate.Kind == PickKind.RoadTarget),
                Is.Not.Empty.And.All.Matches<SkybridgePickable>(target =>
                    game.World.transform.InverseTransformPoint(target.transform.position).y > game.World.GridToWorld(new GridPoint(0, 0, 0)).y),
                "Changing layer through the real HUD event must replace the visible Y=0 target grid.");
            Assert.That(layerDown.interactable, Is.True);

            layerDown.onClick.Invoke();
            yield return null;

            Assert.That(game.Builder.ActiveLayer, Is.EqualTo(0));
            Assert.That(layerDown.interactable, Is.False);
            var targets = game.World.GetComponentsInChildren<SkybridgePickable>(true)
                .Where(candidate => candidate.Kind == PickKind.RoadTarget)
                .ToArray();
            Assert.That(targets, Has.Length.EqualTo(110));
            Assert.That(
                targets.All(target => Mathf.Abs(
                    game.World.transform.InverseTransformPoint(target.transform.position).y -
                    (game.World.GridToWorld(new GridPoint(0, 0, 0)).y + 0.07f)) < 0.001f),
                Is.True,
                "LayerDown must regenerate the flat road target grid at Y=0.");
        }

        [TestCase(0f, 0f, true)]
        [TestCase(3f, 4f, true)]
        [TestCase(5.01f, 0f, false)]
        [TestCase(8f, 6f, false)]
        public void PointerClickThresholdMatchesThreeJs(float deltaX, float deltaY, bool expectedClick)
        {
            var method = typeof(SkybridgeGameController).GetMethod(
                "IsPointerClick",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var result = (bool)method.Invoke(
                null,
                new object[] { Vector2.zero, new Vector2(deltaX, deltaY) });

            Assert.That(result, Is.EqualTo(expectedClick));
        }

        [UnityTest]
        public IEnumerator PointerStateMachineCommitsOnlyWorldClicksAndClearsOnFocusLoss()
        {
            Assert.That(game.LoadLevel(LevelId.Level1, true), Is.True);
            var builder = game.Builder;
            builder.SelectTool(PieceType.Deck);

            var target = game.World.GetComponentsInChildren<SkybridgePickable>(true)
                .First(candidate => candidate.Kind == PickKind.RoadTarget && candidate.Index == 0);
            var point = CalculateBounds(target.gameObject).center;
            var beginLeft = GetPrivateMethod("BeginLeftPointer");
            var releaseLeft = GetPrivateMethod("ReleaseLeftPointer");
            var beginRight = GetPrivateMethod("BeginRightPointer");
            var releaseRight = GetPrivateMethod("ReleaseRightPointer");
            var clearTracking = GetPrivateMethod("ClearPointerTracking");

            beginLeft.Invoke(game, new object[] { Vector2.zero, false });
            var placed = (bool)releaseLeft.Invoke(
                game,
                new object[] { new Vector2(3f, 4f), false, target, point });
            Assert.That(placed, Is.True);
            Assert.That(builder.GetLayout().Roads, Has.Count.EqualTo(1));

            builder.Undo();
            beginLeft.Invoke(game, new object[] { Vector2.zero, false });
            var dragged = (bool)releaseLeft.Invoke(
                game,
                new object[] { new Vector2(6f, 0f), false, target, point });
            Assert.That(dragged, Is.False);
            Assert.That(builder.GetLayout().Roads, Is.Empty);

            beginLeft.Invoke(game, new object[] { Vector2.zero, true });
            var hudStartedClick = (bool)releaseLeft.Invoke(
                game,
                new object[] { Vector2.zero, false, target, point });
            Assert.That(hudStartedClick, Is.False);
            Assert.That(builder.GetLayout().Roads, Is.Empty);

            beginLeft.Invoke(game, new object[] { Vector2.zero, false });
            beginRight.Invoke(game, new object[] { Vector2.zero, false });
            Assert.That(GetPrivateField<bool>(game, "leftPointerTracking"), Is.True);
            Assert.That(GetPrivateField<bool>(game, "rightPointerTracking"), Is.True);
            clearTracking.Invoke(game, null);
            Assert.That(GetPrivateField<bool>(game, "leftPointerTracking"), Is.False);
            Assert.That(GetPrivateField<bool>(game, "rightPointerTracking"), Is.False);

            beginRight.Invoke(game, new object[] { Vector2.zero, true });
            var hudStartedRightClick = (bool)releaseRight.Invoke(
                game,
                new object[] { Vector2.zero, false });
            Assert.That(hudStartedRightClick, Is.False);
            Assert.That(builder.ActiveTool, Is.EqualTo(PieceType.Deck));

            yield return null;
        }

        [UnityTest]
        public IEnumerator FifthLevelFailureRestorePreservesValidatedTurningRoute()
        {
            Assert.That(game.LoadLevel(LevelId.Level5, true), Is.True);
            var route = MakeTurningLevelFiveRoute();
            var roads = route
                .Zip(route.Skip(1), (from, to) => new RoadPiecePlacement(
                    $"road-{from.Key}-{to.Key}",
                    to.Y != from.Y ? PieceType.Ramp : PieceType.Deck,
                    from,
                    to))
                .ToArray();
            // Leave the first four low, flat cells unsupported.  This gives
            // the deterministic weak-span break a short fall to the shared
            // failure volume while the route still contains a visible X→Z
            // turn and a rising Level 5 section later on.
            var pier = new PierPlacement(
                "pier-turn-support",
                route[4],
                new GridPoint(route[4].X, -2, route[4].Z));
            Assert.That(game.SetLayoutForAutomation(new BridgeLayout(roads, new[] { pier })), Is.True);
            Assert.That(game.StartCurrentTest(), Is.True);

            var runningDeadline = Time.realtimeSinceStartup + 3f;
            while (game.Phase != GamePhase.VehicleRunning &&
                   game.Phase != GamePhase.Breaking &&
                   game.Phase != GamePhase.VehicleFalling &&
                   Time.realtimeSinceStartup < runningDeadline)
            {
                yield return null;
            }

            CollectionAssert.AreEqual(
                route.Select(point => point.Key),
                game.World.GetVehicleGridPath().Select(point => point.Key),
                "The validated Level 5 route should include its early turn before failure.");

            var failureDeadline = Time.realtimeSinceStartup + 8f;
            while ((game.Phase != GamePhase.Build || game.LastTestOutcome == TestOutcome.Pending) &&
                   Time.realtimeSinceStartup < failureDeadline)
            {
                yield return null;
            }

            Assert.That(game.LastTestOutcome, Is.EqualTo(TestOutcome.Failure));
            CollectionAssert.AreEqual(
                route.Select(point => point.Key),
                game.World.GetVehicleGridPath().Select(point => point.Key),
                "Failure recovery must preserve the tested turning route instead of reloading Level 5 defaults.");
        }

        [UnityTest]
        public IEnumerator FailureAndGoalVolumesMatchThreeJsGeometry()
        {
            foreach (var definition in LevelCatalog.All)
            {
                Assert.That(game.LoadLevel(definition.Id, true), Is.True, definition.Id.ToString());
                yield return null;

                var volume = definition.BuildVolume ?? new BuildVolume(
                    new GridPoint(
                        Mathf.Min(definition.StartAnchor.X, definition.GoalAnchor.X),
                        Mathf.Min(definition.StartAnchor.Y, definition.GoalAnchor.Y),
                        Mathf.Min(definition.StartAnchor.Z, definition.GoalAnchor.Z)),
                    new GridPoint(
                        Mathf.Max(definition.StartAnchor.X, definition.GoalAnchor.X),
                        Mathf.Max(definition.StartAnchor.Y, definition.GoalAnchor.Y),
                        Mathf.Max(definition.StartAnchor.Z, definition.GoalAnchor.Z)),
                    -2);
                var volumeMin = game.World.GridToWorld(volume.Min);
                var volumeMax = game.World.GridToWorld(volume.Max);
                var margin = SkybridgeWorldView.GridUnit * 2.5f;
                var expectedFailureMin = new Vector3(
                    Mathf.Min(volumeMin.x, volumeMax.x) - margin,
                    SkybridgeWorldView.TableTopY - 0.4f,
                    Mathf.Min(volumeMin.z, volumeMax.z) - margin);
                var expectedFailureMax = new Vector3(
                    Mathf.Max(volumeMin.x, volumeMax.x) + margin,
                    SkybridgeWorldView.TableTopY + 0.32f,
                    Mathf.Max(volumeMin.z, volumeMax.z) + margin);
                var failureZone = GetPrivateField<Bounds>(game.World, "vehicleFailureZone");
                AssertVector3Approximately(expectedFailureMin, failureZone.min, $"{definition.Id} failure min");
                AssertVector3Approximately(expectedFailureMax, failureZone.max, $"{definition.Id} failure max");

                var path = BridgeRules.FindRoadPath(
                    definition.ReferenceSolution.Layout.Roads,
                    definition.StartAnchor,
                    definition.GoalAnchor);
                Assert.That(path, Is.Not.Null, definition.Id.ToString());
                var beforeGoal = path.Nodes[^2];
                var goalNode = path.Nodes[^1];
                var goalDirection = new Vector3(
                    goalNode.X - beforeGoal.X,
                    0f,
                    goalNode.Z - beforeGoal.Z).normalized;
                var goal = game.World.GridToWorld(definition.GoalAnchor);
                var expectedGoalCenter = goal + goalDirection * 1.95f + Vector3.up * 1.135f;
                var goalWidth = SkybridgeWorldView.RoadWidth * 1.64f;
                var expectedGoalSize = new Vector3(
                    Mathf.Abs(goalDirection.x) * 3.4f + Mathf.Abs(goalDirection.z) * goalWidth,
                    2.43f,
                    Mathf.Abs(goalDirection.z) * 3.4f + Mathf.Abs(goalDirection.x) * goalWidth);
                var goalZone = GetPrivateField<Bounds>(game.World, "goalZone");
                AssertVector3Approximately(expectedGoalCenter, goalZone.center, $"{definition.Id} goal center");
                AssertVector3Approximately(expectedGoalSize, goalZone.size, $"{definition.Id} goal size");
            }
        }

        private static Bounds BoundsInWorkspace(Transform model, Transform workspace)
        {
            var result = new Bounds();
            var first = true;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var bounds = renderer.localBounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var local = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var point = workspace.InverseTransformPoint(renderer.transform.TransformPoint(local));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(candidate => candidate.name == name);
        }

        private static IReadOnlyList<GridPoint> MakeTurningLevelFiveRoute()
        {
            var route = new List<GridPoint> { new GridPoint(0, 0, 0) };
            for (var index = 0; index < 4; index++)
            {
                var current = route[route.Count - 1];
                route.Add(new GridPoint(current.X + 1, current.Y, current.Z));
            }
            for (var index = 0; index < 10; index++)
            {
                var current = route[route.Count - 1];
                route.Add(new GridPoint(current.X, current.Y + 1, current.Z + 1));
            }
            for (var index = 0; index < 6; index++)
            {
                var current = route[route.Count - 1];
                route.Add(new GridPoint(current.X + 1, current.Y, current.Z));
            }
            return route;
        }

        private static Bounds CalculateBounds(GameObject gameObject)
        {
            var renderers = gameObject.GetComponentsInChildren<Renderer>();
            Assert.That(renderers, Is.Not.Empty);
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(target);
        }

        private static MethodInfo GetPrivateMethod(string methodName)
        {
            var method = typeof(SkybridgeGameController).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            return method;
        }

        private static void AssertVector3Approximately(Vector3 expected, Vector3 actual, string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f), $"{label}.x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f), $"{label}.y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f), $"{label}.z");
        }

        private static void AssertVector2Approximately(Vector2 expected, Vector2 actual, string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f), $"{label}.x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f), $"{label}.y");
        }

        private static void AssertChildrenContained(RectTransform parent)
        {
            foreach (RectTransform child in parent)
            {
                if (!child.gameObject.activeSelf) continue;
                var layout = child.GetComponent<LayoutElement>();
                if (layout != null && layout.ignoreLayout) continue;
                AssertRectContained(parent, child);
            }
        }

        private static void AssertRectContained(RectTransform parent, RectTransform child)
        {
            var parentRect = parent.rect;
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            for (var index = 0; index < corners.Length; index++) corners[index] = parent.InverseTransformPoint(corners[index]);
            Assert.That(corners.Min(corner => corner.x), Is.GreaterThanOrEqualTo(parentRect.xMin - 1f), child.name);
            Assert.That(corners.Max(corner => corner.x), Is.LessThanOrEqualTo(parentRect.xMax + 1f), child.name);
            Assert.That(corners.Min(corner => corner.y), Is.GreaterThanOrEqualTo(parentRect.yMin - 1f), child.name);
            Assert.That(corners.Max(corner => corner.y), Is.LessThanOrEqualTo(parentRect.yMax + 1f), child.name);
        }
    }
}
