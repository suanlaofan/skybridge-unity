using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ByteDance.PICO.XR;
using Skybridge.Core;
using Skybridge.Presentation;

namespace Skybridge.Editor
{
    public static class SkybridgeProjectTools
    {
        public const string MainScenePath = "Assets/Scenes/SkybridgeMain.unity";
        public const string MacBuildPath = "Builds/macOS/Skybridge.app";
        public const string PicoAndroidBuildPath = "Builds/PICO/SkybridgePico.apk";

        private const string MainRootName = "SkybridgeMain";
        private const string SettingsFolder = "Assets/Skybridge/Settings";
        private const string ResourcesFolder = "Assets/Resources";
        private const string RendererPath = SettingsFolder + "/SkybridgeRenderer.asset";
        private const string PipelinePath = SettingsFolder + "/SkybridgeURP.asset";
        private const string ShaderAnchorPath = ResourcesFolder + "/SkybridgeShaderAnchor.mat";
        private const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";
        private const string ModelAuthoringRootName = "SkybridgeModelAuthoring";
        private const string ModelPreviewRootName = "SceneModelPreviews";
        private const string ModelMaterialsFolder = "Assets/Resources/SkybridgeMaterials";
        private const string IslandModelPath = "Assets/Resources/SkybridgeModels/Island/tripo_convert_a3c039d2-83f0-441c-a722-098cbe09a69e.fbx";
        private const string IslandTexturePath = "Assets/Resources/SkybridgeModels/Island/圆形能量平台3d模型_basecolor.JPEG";
        private const string RescueVehicleModelPath = "Assets/Resources/SkybridgeModels/RescueVehicle/tripo_convert_72313d7f-4ed4-492b-a48d-6aa3ddb5ad59.fbx";
        private const string RescueVehicleTexturePath = "Assets/Resources/SkybridgeModels/RescueVehicle/三维模型_探测车_basecolor.jpg";
        private const string GoalGateModelPath = "Assets/Resources/SkybridgeModels/GoalGate/tripo_convert_02717a75-bd4c-451a-a5d2-ba9a7d107b7e.fbx";
        private const string GoalGateTexturePath = "Assets/Resources/SkybridgeModels/GoalGate/科幻拱门3d模型_basecolor.JPEG";
        private const string IslandMaterialPath = ModelMaterialsFolder + "/IslandModel.mat";
        private const string RescueVehicleMaterialPath = ModelMaterialsFolder + "/RescueVehicleModel.mat";
        private const string GoalGateMaterialPath = ModelMaterialsFolder + "/GoalGateModel.mat";

        [MenuItem("Skybridge/Project/Configure Project")]
        public static void ConfigureProject()
        {
            ConfigureProjectInternal();
            ValidateConfiguration();
            Debug.Log("SKYBRIDGE_CONFIGURE_PASS");
        }

        [MenuItem("Skybridge/Project/Validate Configuration")]
        public static void ValidateConfiguration()
        {
            var errors = new List<string>();
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var shaderAnchor = AssetDatabase.LoadAssetAtPath<Material>(ShaderAnchorPath);

            if (pipeline == null)
            {
                errors.Add("Missing URP asset: " + PipelinePath);
            }
            else if (GraphicsSettings.defaultRenderPipeline != pipeline)
            {
                errors.Add("Skybridge URP asset is not the default render pipeline.");
            }

            if (renderer == null)
            {
                errors.Add("Missing renderer asset: " + RendererPath);
            }

            if (shaderAnchor == null || shaderAnchor.shader == null || shaderAnchor.shader.name != "Universal Render Pipeline/Lit")
            {
                errors.Add("Missing URP Lit shader anchor: " + ShaderAnchorPath);
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath) == null)
            {
                errors.Add("Missing bootstrap scene: " + MainScenePath);
            }
            else
            {
                ValidateSceneContents(errors);
            }

            var buildScene = EditorBuildSettings.scenes.FirstOrDefault(scene => scene.path == MainScenePath);
            if (buildScene == null || !buildScene.enabled)
            {
                errors.Add("Bootstrap scene is not enabled in Build Settings.");
            }

            if (PlayerSettings.defaultScreenWidth != 1280 || PlayerSettings.defaultScreenHeight != 720)
            {
                errors.Add("Default resolution must be 1280x720.");
            }

            if (PlayerSettings.fullScreenMode != FullScreenMode.Windowed)
            {
                errors.Add("Default desktop display mode must be Windowed.");
            }

            if (GetActiveInputHandler() != 1)
            {
                errors.Add("Active Input Handling must use the Input System package.");
            }

            if (PlayerSettings.GetArchitecture(NamedBuildTarget.Standalone) != 2)
            {
                errors.Add("macOS architecture must be Universal.");
            }

            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.Mono2x)
            {
                errors.Add("macOS scripting backend must be Mono.");
            }

            if (errors.Count > 0)
            {
                throw new BuildFailedException(string.Join(Environment.NewLine, errors));
            }

            Debug.Log("SKYBRIDGE_VALIDATE_PASS");
        }

        [MenuItem("Skybridge/Build/Build macOS Player")]
        public static void BuildMacPlayer()
        {
            ConfigureProjectInternal();
            ValidateConfiguration();

            Directory.CreateDirectory(Path.GetDirectoryName(MacBuildPath) ?? "Builds/macOS");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = MacBuildPath,
                targetGroup = BuildTargetGroup.Standalone,
                target = BuildTarget.StandaloneOSX,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"Skybridge macOS build failed: {report.summary.result}; errors={report.summary.totalErrors}");
            }

            Debug.Log($"SKYBRIDGE_MAC_BUILD_PASS:{report.summary.outputPath}:{report.summary.totalSize}");
        }

        /// <summary>
        /// PICO-only build path. This intentionally does not call the desktop
        /// ConfigureProjectInternal method, which resets macOS settings.
        /// </summary>
        [MenuItem("Skybridge/PICO/Build Android APK")]
        public static void BuildPicoAndroid()
        {
            ConfigurePicoAndroidInternal();
            Directory.CreateDirectory(Path.GetDirectoryName(PicoAndroidBuildPath) ?? "Builds/PICO");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = PicoAndroidBuildPath,
                targetGroup = BuildTargetGroup.Android,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"Skybridge PICO Android build failed: {report.summary.result}; errors={report.summary.totalErrors}");
            }
            Debug.Log($"SKYBRIDGE_PICO_BUILD_PASS:{report.summary.outputPath}:{report.summary.totalSize}");
        }

        [MenuItem("Skybridge/PICO/Validate Android Configuration")]
        public static void ValidatePicoAndroidConfiguration()
        {
            var errors = new List<string>();
            if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) != "com.skybridge.pico")
            {
                errors.Add("PICO package identifier must be com.skybridge.pico.");
            }
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
            {
                errors.Add("PICO Android scripting backend must be IL2CPP.");
            }
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                errors.Add("PICO Android architecture must be ARM64 only.");
            }
            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel29)
            {
                errors.Add("PICO requires Android API level 29 or newer.");
            }
            if (PlayerSettings.Android.applicationEntry != AndroidApplicationEntry.Activity)
            {
                errors.Add("PICO requires the Android Activity application entry point.");
            }
            if (!HasPicoLoaderConfiguration())
            {
                errors.Add("PICO XR Loader is not assigned to Android.");
            }
            var picoSettings = PXR_Settings.GetSettings();
            if (picoSettings == null || picoSettings.appMode != PXR_Settings.AppMode.XR)
            {
                errors.Add("PICO settings must use Unity XR rendering mode.");
            }
            var picoProject = PXR_ProjectSetting.GetProjectConfig();
            if (picoProject == null || picoProject.handTracking || picoProject.videoSeeThrough || picoProject.arFoundation)
            {
                errors.Add("The first PICO slice must be controller-only and cannot enable hand tracking, VST, or AR Foundation.");
            }
            if (errors.Count > 0) throw new BuildFailedException(string.Join(Environment.NewLine, errors));
            Debug.Log("SKYBRIDGE_PICO_VALIDATE_PASS");
        }

        private static void ConfigurePicoAndroidInternal()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.skybridge.pico");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
            // PICO XR's Unity 6 integration validates against the classic
            // Activity entry point; newly created Unity projects default to
            // GameActivity, which the PICO loader does not support.
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            SetActiveInputHandler(1);
            ConfigurePicoLoader();
            ConfigurePicoAssets();
            EnsureBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidatePicoAndroidConfiguration();
        }

        private static void ConfigurePicoLoader()
        {
            // XR Management's editor API surface differs across Unity/PICO
            // package combinations. Resolve it by reflection so this build
            // entry compiles even when its editor assembly is not exposed to
            // this project's asmdef.
            EnsureAssetFolder("Assets", "XR");
            EnsureAssetFolder("Assets/XR", "Loaders");
            const string loaderPath = "Assets/XR/Loaders/PXR_Loader.asset";
            var loaderType = Type.GetType("ByteDance.PICO.XR.PXR_Loader, ByteDance.PICO.XR");
            if (loaderType == null) throw new BuildFailedException("PICO XR Loader runtime type is unavailable.");
            var loader = AssetDatabase.LoadAssetAtPath(loaderPath, loaderType);
            if (loader == null)
            {
                loader = ScriptableObject.CreateInstance(loaderType);
                AssetDatabase.CreateAsset(loader, loaderPath);
            }
            var perTargetType = Type.GetType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget, Unity.XR.Management.Editor");
            var generalType = Type.GetType("UnityEngine.XR.Management.XRGeneralSettings, Unity.XR.Management");
            var managerType = Type.GetType("UnityEngine.XR.Management.XRManagerSettings, Unity.XR.Management");
            var metadataType = Type.GetType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore, Unity.XR.Management.Editor");
            if (perTargetType == null || generalType == null || managerType == null || metadataType == null)
            {
                throw new BuildFailedException("XR Management runtime/editor types are unavailable for PICO Loader configuration.");
            }

            const string settingsPath = "Assets/XRGeneralSettingsPerBuildTarget.asset";
            var targetSettings = AssetDatabase.LoadAssetAtPath(settingsPath, perTargetType) as ScriptableObject;
            if (targetSettings == null)
            {
                targetSettings = ScriptableObject.CreateInstance(perTargetType);
                AssetDatabase.CreateAsset(targetSettings, settingsPath);
            }
            var getSettings = perTargetType.GetMethod("SettingsForBuildTarget", new[] { typeof(BuildTargetGroup) });
            var setSettings = perTargetType.GetMethod("SetSettingsForBuildTarget", new[] { typeof(BuildTargetGroup), generalType });
            if (getSettings == null || setSettings == null) throw new BuildFailedException("XR Management build-target settings API is unavailable.");
            var generalSettings = getSettings.Invoke(targetSettings, new object[] { BuildTargetGroup.Android }) as ScriptableObject;
            if (generalSettings == null)
            {
                generalSettings = ScriptableObject.CreateInstance(generalType);
                AssetDatabase.AddObjectToAsset(generalSettings, targetSettings);
                setSettings.Invoke(targetSettings, new object[] { BuildTargetGroup.Android, generalSettings });
            }
            var managerProperty = generalType.GetProperty("Manager");
            var manager = managerProperty?.GetValue(generalSettings) as ScriptableObject;
            if (manager == null)
            {
                manager = ScriptableObject.CreateInstance(managerType);
                AssetDatabase.AddObjectToAsset(manager, targetSettings);
                managerProperty?.SetValue(generalSettings, manager);
            }
            var assignLoader = metadataType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method => method.Name == "AssignLoader" && method.GetParameters().Length == 3);
            var assigned = assignLoader != null && (bool)assignLoader.Invoke(
                null,
                new object[] { manager, "ByteDance.PICO.XR.PXR_Loader", BuildTargetGroup.Android });
            if (!assigned) throw new BuildFailedException("Unable to assign PICO XR Loader to Android.");
            generalType.GetProperty("InitManagerOnStart")?.SetValue(generalSettings, true);
            EditorUtility.SetDirty(loader);
            EditorUtility.SetDirty(targetSettings);
            EditorUtility.SetDirty(generalSettings);
            EditorUtility.SetDirty(manager);
        }

        private static bool HasPicoLoaderConfiguration()
        {
            var perTargetType = Type.GetType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget, Unity.XR.Management.Editor");
            if (perTargetType == null) return false;
            var targetSettings = AssetDatabase.LoadAssetAtPath("Assets/XRGeneralSettingsPerBuildTarget.asset", perTargetType);
            if (targetSettings == null) return false;
            var getSettings = perTargetType.GetMethod("SettingsForBuildTarget", new[] { typeof(BuildTargetGroup) });
            var generalSettings = getSettings?.Invoke(targetSettings, new object[] { BuildTargetGroup.Android });
            var manager = generalSettings?.GetType().GetProperty("Manager")?.GetValue(generalSettings);
            var loaders = manager?.GetType().GetProperty("activeLoaders")?.GetValue(manager) as System.Collections.IEnumerable;
            return loaders != null && loaders.Cast<object>().Any(loader =>
                loader != null && loader.GetType().FullName == "ByteDance.PICO.XR.PXR_Loader");
        }

        private static void ConfigurePicoAssets()
        {
            EnsureAssetFolder("Assets", "Resources");
            var projectSettings = PXR_ProjectSetting.GetProjectConfig();
            projectSettings.handTracking = false;
            projectSettings.adaptiveHand = false;
            projectSettings.highFrequencyHand = false;
            projectSettings.videoSeeThrough = false;
            projectSettings.arFoundation = false;
            projectSettings.planeDetection = false;
            projectSettings.spatialAnchor = false;
            projectSettings.environmentDepth = false;
            projectSettings.lightEstimation = false;
            projectSettings.openMRC = false;
            EditorUtility.SetDirty(projectSettings);

            const string picoSettingsPath = "Assets/XR/Settings/PXR_Settings.asset";
            EnsureAssetFolder("Assets", "XR");
            EnsureAssetFolder("Assets/XR", "Settings");
            var picoSettings = AssetDatabase.LoadAssetAtPath<PXR_Settings>(picoSettingsPath);
            if (picoSettings == null)
            {
                picoSettings = ScriptableObject.CreateInstance<PXR_Settings>();
                AssetDatabase.CreateAsset(picoSettings, picoSettingsPath);
            }
            picoSettings.appMode = PXR_Settings.AppMode.XR;
            picoSettings.stereoRenderingModeAndroid = PXR_Settings.StereoRenderingModeAndroid.Multiview;
            picoSettings.systemDisplayFrequency = PXR_Settings.SystemDisplayFrequency.RefreshRate90;
            picoSettings.optimizeBufferDiscards = true;
            EditorBuildSettings.AddConfigObject("ByteDance.PICO.XR.Settings", picoSettings, true);
            EditorUtility.SetDirty(picoSettings);
        }

        private static void ConfigureProjectInternal()
        {
            EnsureAssetFolder("Assets", "Scenes");
            EnsureAssetFolder("Assets/Skybridge", "Settings");
            EnsureAssetFolder("Assets", "Resources");

            UniversalRenderPipelineAsset pipeline = EnsureRenderPipeline();
            EnsureShaderAnchor();
            ConfigureRenderPipeline(pipeline);
            ConfigurePlayerSettings();
            EnsureBootstrapScene();
            EnsureEditableModelLayer(false);
            EnsureBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static UniversalRenderPipelineAsset EnsureRenderPipeline()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "SkybridgeRenderer";
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            else
            {
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "SkybridgeURP";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(pipeline);
            return pipeline;
        }

        private static void EnsureShaderAnchor()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new BuildFailedException("Universal Render Pipeline/Lit shader is unavailable.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(ShaderAnchorPath);
            if (material == null)
            {
                material = new Material(shader) { name = "SkybridgeShaderAnchor" };
                AssetDatabase.CreateAsset(material, ShaderAnchorPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
        }

        private static void ConfigureRenderPipeline(UniversalRenderPipelineAsset pipeline)
        {
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1f;
            pipeline.shadowDistance = 35f;
            pipeline.shadowCascadeCount = 2;
            pipeline.maxAdditionalLightsCount = 8;
            pipeline.useSRPBatcher = true;
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int previousQuality = QualitySettings.GetQualityLevel();
            try
            {
                for (int quality = 0; quality < QualitySettings.names.Length; quality++)
                {
                    QualitySettings.SetQualityLevel(quality, false);
                    QualitySettings.renderPipeline = pipeline;
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(previousQuality, false);
            }
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.skybridge.game");
            PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, 2);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            SetActiveInputHandler(1);
        }

        [MenuItem("Skybridge/Scene/Sync Editable Models To Scene")]
        public static void SyncEditableModelsToScene()
        {
            EnsureEditableModelLayer(false);
            Debug.Log("SKYBRIDGE_SCENE_MODEL_SYNC_PASS");
        }

        // Automation entry point used when the source asset pose changes.
        // Normal scene sync stays non-destructive and preserves manual edits.
        public static void RebuildEditableModelsFromAssets()
        {
            EnsureEditableModelLayer(true);
            Debug.Log("SKYBRIDGE_SCENE_MODEL_REBUILD_PASS");
        }

        /// <summary>
        /// Creates the Scene-view authoring layer once, without overwriting
        /// any existing manual material or transform edits.  The menu command
        /// and project configuration both use this idempotent path.
        /// </summary>
        private static void EnsureEditableModelLayer(bool rebuild)
        {
            EnsureAssetFolder("Assets/Resources", "SkybridgeMaterials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var scene = SceneManager.GetSceneByPath(MainScenePath);
            var closeAfter = !scene.IsValid() || !scene.isLoaded;
            if (closeAfter)
            {
                scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
            }

            try
            {
                var sceneRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == MainRootName);
                if (sceneRoot == null)
                {
                    sceneRoot = new GameObject(MainRootName);
                    SceneManager.MoveGameObjectToScene(sceneRoot, scene);
                }

                var authoring = sceneRoot.transform.Find(ModelAuthoringRootName);
                if (rebuild && authoring != null)
                {
                    UnityEngine.Object.DestroyImmediate(authoring.gameObject);
                    authoring = null;
                }
                if (authoring == null)
                {
                    authoring = new GameObject(ModelAuthoringRootName).transform;
                    authoring.SetParent(sceneRoot.transform, false);
                }

                var library = authoring.GetComponent<SkybridgeSceneModelLibrary>();
                if (library == null) library = authoring.gameObject.AddComponent<SkybridgeSceneModelLibrary>();

                var previewRoot = authoring.Find(ModelPreviewRootName);
                if (previewRoot == null)
                {
                    previewRoot = new GameObject(ModelPreviewRootName).transform;
                    previewRoot.SetParent(authoring, false);
                }
                previewRoot.gameObject.SetActive(true);

                var islandMaterial = EnsureModelMaterial(
                    IslandMaterialPath,
                    IslandTexturePath,
                    "IslandModelMaterial");
                var rescueMaterial = EnsureModelMaterial(
                    RescueVehicleMaterialPath,
                    RescueVehicleTexturePath,
                    "RescueVehicleModelMaterial");
                var gateMaterial = EnsureModelMaterial(
                    GoalGateMaterialPath,
                    GoalGateTexturePath,
                    "GoalGateModelMaterial");

                var islandSlot = EnsurePreviewSlot(previewRoot, "StartIslandPreview", new Vector3(-5.95f, SkybridgeWorldView.RoadBaseY, 0f));
                var goalIslandSlot = EnsurePreviewSlot(previewRoot, "GoalIslandPreview", new Vector3(5.95f, SkybridgeWorldView.RoadBaseY, 0f));
                var vehicleSlot = EnsurePreviewSlot(previewRoot, "RescueVehiclePreview", new Vector3(-5.15f, SkybridgeWorldView.RoadBaseY + SkybridgeWorldView.VehicleClearance, 0f));
                var gateSlot = EnsurePreviewSlot(previewRoot, "GoalGatePreview", new Vector3(4.95f, SkybridgeWorldView.RoadBaseY, 0f));

                var islandTemplate = EnsureModelTemplate(
                    islandSlot,
                    "IslandTemplate_EDIT",
                    IslandModelPath,
                    islandMaterial,
                    new Vector3(5.35f, 2.9f, 4.45f),
                    new Vector3(-90f, 0f, 0f),
                    allowAspectYaw: false,
                    alignTop: true);
                var goalIslandTemplate = EnsurePreviewClone(goalIslandSlot, "GoalIslandPreview_EDIT", islandTemplate);

                var vehicleTemplate = EnsureModelTemplate(
                    vehicleSlot,
                    "RescueVehicleTemplate_EDIT",
                    RescueVehicleModelPath,
                    rescueMaterial,
                    new Vector3(1.62f, 1.05f, 1.08f),
                    Vector3.zero,
                    allowAspectYaw: true,
                    alignTop: false);
                var gateTemplate = EnsureModelTemplate(
                    gateSlot,
                    "GoalGateTemplate_EDIT",
                    GoalGateModelPath,
                    gateMaterial,
                    new Vector3(0.9f, 2.45f, 1.75f),
                    Vector3.zero,
                    allowAspectYaw: true,
                    alignTop: false);

                library.AssignTemplates(islandTemplate, goalIslandTemplate, vehicleTemplate, gateTemplate, previewRoot);
                EditorUtility.SetDirty(library);
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene);
                Selection.activeGameObject = authoring.gameObject;
            }
            finally
            {
                if (closeAfter) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Transform EnsurePreviewSlot(Transform parent, string name, Vector3 position)
        {
            var slot = parent.Find(name);
            if (slot == null)
            {
                slot = new GameObject(name).transform;
                slot.SetParent(parent, false);
            }
            slot.localPosition = position;
            slot.localRotation = Quaternion.identity;
            slot.localScale = Vector3.one;
            return slot;
        }

        private static GameObject EnsureModelTemplate(
            Transform slot,
            string name,
            string prefabPath,
            Material material,
            Vector3 targetSize,
            Vector3 sourceEulerCorrection,
            bool allowAspectYaw,
            bool alignTop)
        {
            var existing = slot.Find(name);
            if (existing != null) return existing.gameObject;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new BuildFailedException("Missing model asset: " + prefabPath);
            }

            var template = SkybridgeWorldView.BuildSceneModelTemplate(
                prefab,
                material,
                targetSize,
                sourceEulerCorrection,
                allowAspectYaw,
                alignTop,
                slot);
            if (template == null)
            {
                throw new BuildFailedException("Unable to create scene model template: " + prefabPath);
            }
            template.name = name;
            template.transform.localPosition = Vector3.zero;
            template.transform.localRotation = Quaternion.identity;
            template.transform.localScale = Vector3.one;
            return template;
        }

        private static GameObject EnsurePreviewClone(Transform slot, string name, GameObject source)
        {
            var existing = slot.Find(name);
            if (existing != null) return existing.gameObject;
            var clone = UnityEngine.Object.Instantiate(source, slot, false);
            clone.name = name;
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            return clone;
        }

        private static Material EnsureModelMaterial(string materialPath, string texturePath, string materialName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var created = false;
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null) throw new BuildFailedException("Unable to find a Lit shader for " + materialName);
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, materialPath);
                created = true;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture != null && material.mainTexture == null)
            {
                material.mainTexture = texture;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            }
            if (created)
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                else material.color = Color.white;
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.15f);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.52f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureBootstrapScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath) != null)
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var root = new GameObject(MainRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            if (!EditorSceneManager.SaveScene(scene, MainScenePath))
            {
                throw new BuildFailedException("Failed to save bootstrap scene: " + MainScenePath);
            }

            EditorSceneManager.CloseScene(scene, true);
        }

        private static void EnsureBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.path != MainScenePath)
                .ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(MainScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void ValidateSceneContents(List<string> errors)
        {
            Scene scene = SceneManager.GetSceneByPath(MainScenePath);
            bool closeAfterValidation = !scene.isLoaded;
            if (closeAfterValidation)
            {
                scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
            }

            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                if (roots.Length != 1 || roots[0].name != MainRootName)
                {
                    errors.Add($"Bootstrap scene must contain only the empty root '{MainRootName}'.");
                    return;
                }

                var authoring = roots[0].transform.Find(ModelAuthoringRootName);
                if (authoring == null)
                {
                    errors.Add($"Bootstrap root '{MainRootName}' is missing the editable model layer '{ModelAuthoringRootName}'.");
                }
                else
                {
                    var library = authoring.GetComponent<SkybridgeSceneModelLibrary>();
                    if (library == null || !library.IsConfigured)
                    {
                        errors.Add($"Editable model layer '{ModelAuthoringRootName}' must reference island, rescue vehicle and goal gate templates.");
                    }
                    if (authoring.Find(ModelPreviewRootName) == null)
                    {
                        errors.Add($"Editable model layer '{ModelAuthoringRootName}' is missing '{ModelPreviewRootName}'.");
                    }
                }
            }
            finally
            {
                if (closeAfterValidation)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static int GetActiveInputHandler()
        {
            SerializedObject settings = LoadProjectSettings();
            SerializedProperty property = settings.FindProperty("activeInputHandler");
            if (property == null)
            {
                throw new BuildFailedException("Unable to find PlayerSettings.activeInputHandler.");
            }

            return property.intValue;
        }

        private static void SetActiveInputHandler(int value)
        {
            SerializedObject settings = LoadProjectSettings();
            SerializedProperty property = settings.FindProperty("activeInputHandler");
            if (property == null)
            {
                throw new BuildFailedException("Unable to find PlayerSettings.activeInputHandler.");
            }

            property.intValue = value;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedObject LoadProjectSettings()
        {
            UnityEngine.Object settings = AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsPath).FirstOrDefault();
            if (settings == null)
            {
                throw new BuildFailedException("Unable to load " + ProjectSettingsPath);
            }

            return new SerializedObject(settings);
        }

        private static void EnsureAssetFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
