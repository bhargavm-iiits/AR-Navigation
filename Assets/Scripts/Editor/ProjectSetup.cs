using System.Collections.Generic;
using System.IO;
using TirumalaAR.AR;
using TirumalaAR.Audio;
using TirumalaAR.GPS;
using TirumalaAR.Managers;
using TirumalaAR.UI;
using TirumalaAR.UI.Framework;
using TirumalaAR.UI.Screens;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace TirumalaAR.EditorTools
{
    /// <summary>
    /// One-click project generation.
    ///
    /// Scenes and prefabs are generated from code rather than committed as binary/YAML assets so
    /// that every reference is guaranteed to be wired correctly, and so the setup can be re-run
    /// after a package upgrade without hand-repairing broken inspector links.
    ///
    /// Menu: Tools ▸ Tirumala AR ▸ Set Up Everything
    /// </summary>
    public static class ProjectSetup
    {
        const string k_PrefabRoot = "Assets/Prefabs";
        const string k_ResourceRoot = "Assets/Resources";
        const string k_SceneRoot = "Assets/Scenes";
        const string k_ArrowPrefabPath = k_PrefabRoot + "/Arrow/NavigationArrow.prefab";
        const string k_MarkerPrefabPath = k_PrefabRoot + "/Landmarks/TempleMarker.prefab";
        const string k_MainMenuScenePath = k_SceneRoot + "/MainMenu.unity";
        const string k_NavigationScenePath = k_SceneRoot + "/NavigationScene.unity";
        const string k_ConfigPath = k_ResourceRoot + "/AppConfig.asset";

        static readonly Color k_ArrowBlue = new Color(0.13f, 0.48f, 1f, 1f);

        [MenuItem("Tools/Tirumala AR/Set Up Everything", priority = 0)]
        public static void SetUpEverything()
        {
            CreateFolders();

            // Must run before the scenes are built: without an XR loader registered for Android,
            // ARCore never initialises and the camera feed stays black on device.
            EditorTools.XRSetup.ConfigureAndroid();

            CreateAppConfig();
            CreateUITheme();
            CreateArrowPrefab();
            CreateLandmarkMarkerPrefab();
            BuildMainMenuScene();
            BuildNavigationScene();
            ConfigureAndroidBuild();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Setup] Project generated. Open Assets/Scenes/MainMenu.unity and press Play, " +
                      "or build to an ARCore device.");
        }

        // -----------------------------------------------------------------------------------
        // Folders and assets
        // -----------------------------------------------------------------------------------

        [MenuItem("Tools/Tirumala AR/Create Folders", priority = 20)]
        public static void CreateFolders()
        {
            string[] folders =
            {
                "Assets/Prefabs", "Assets/Prefabs/Arrow", "Assets/Prefabs/Landmarks", "Assets/Prefabs/UI",
                "Assets/Resources", "Assets/Resources/Audio", "Assets/Resources/Images",
                "Assets/Resources/Landmarks", "Assets/Resources/ReferenceImages",
                "Assets/Scenes", "Assets/Settings/AR",
                "Assets/StreamingAssets/GeoJSON", "Assets/StreamingAssets/Database",
                "Assets/StreamingAssets/Images", "Assets/StreamingAssets/Metadata"
            };

            foreach (var folder in folders)
            {
                if (AssetDatabase.IsValidFolder(folder))
                    continue;

                var parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }
        }

        [MenuItem("Tools/Tirumala AR/Create App Config", priority = 21)]
        public static AppConfig CreateAppConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AppConfig>(k_ConfigPath);

            if (existing != null)
                return existing;

            var config = ScriptableObject.CreateInstance<AppConfig>();
            AssetDatabase.CreateAsset(config, k_ConfigPath);
            Debug.Log($"[Setup] Created {k_ConfigPath}");
            return config;
        }

        // -----------------------------------------------------------------------------------
        // Prefabs
        // -----------------------------------------------------------------------------------

        [MenuItem("Tools/Tirumala AR/Create UI Theme", priority = 24)]
        public static UITheme CreateUITheme()
        {
            const string path = k_ResourceRoot + "/UITheme.asset";
            var existing = AssetDatabase.LoadAssetAtPath<UITheme>(path);

            if (existing != null)
                return existing;

            var theme = ScriptableObject.CreateInstance<UITheme>();
            AssetDatabase.CreateAsset(theme, path);
            Debug.Log($"[Setup] Created {path}");
            return theme;
        }

        [MenuItem("Tools/Tirumala AR/Create Arrow Prefab", priority = 22)]
        public static GameObject CreateArrowPrefab()
        {
            var root = new GameObject("NavigationArrow");

            // A chevron built from two stretched cubes reads clearly on a staircase from several
            // metres away and costs 24 triangles — far cheaper than an imported mesh, and it needs
            // no external art asset for the project to run.
            var material = CreateArrowMaterial();

            var left = GameObject.CreatePrimitive(PrimitiveType.Cube);
            left.name = "ChevronLeft";
            left.transform.SetParent(root.transform, false);
            left.transform.localPosition = new Vector3(-0.11f, 0f, -0.06f);
            left.transform.localRotation = Quaternion.Euler(0f, -35f, 0f);
            left.transform.localScale = new Vector3(0.30f, 0.035f, 0.085f);

            var right = GameObject.CreatePrimitive(PrimitiveType.Cube);
            right.name = "ChevronRight";
            right.transform.SetParent(root.transform, false);
            right.transform.localPosition = new Vector3(0.11f, 0f, -0.06f);
            right.transform.localRotation = Quaternion.Euler(0f, 35f, 0f);
            right.transform.localScale = new Vector3(0.30f, 0.035f, 0.085f);

            var renderers = new List<Renderer>();

            foreach (var part in new[] { left, right })
            {
                // Colliders would make arrows block AR raycasts against the real ground.
                Object.DestroyImmediate(part.GetComponent<Collider>());

                var renderer = part.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderers.Add(renderer);
            }

            var arrow = root.AddComponent<NavigationArrow>();
            SetPrivateField(arrow, "m_Renderers", renderers.ToArray());

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_ArrowPrefabPath);
            Object.DestroyImmediate(root);

            Debug.Log($"[Setup] Created {k_ArrowPrefabPath}");
            return prefab;
        }

        static Material CreateArrowMaterial()
        {
            const string path = "Assets/Settings/AR/ArrowBlue.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
                return existing;

            // Unlit keeps the arrows a consistent, readable blue in the harsh outdoor lighting on
            // the hillside, where a lit material washes out completely at midday.
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", k_ArrowBlue);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", k_ArrowBlue);

            // Transparent so NavigationArrow can fade instances in and out.
            SetUpTransparency(material);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void SetUpTransparency(Material material)
        {
            material.SetFloat("_Surface", 1f);       // URP: transparent
            material.SetFloat("_Blend", 0f);         // alpha blend
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
        }

        [MenuItem("Tools/Tirumala AR/Create Landmark Marker Prefab", priority = 23)]
        public static GameObject CreateLandmarkMarkerPrefab()
        {
            var root = new GameObject("TempleMarker");

            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Beacon";
            pillar.transform.SetParent(root.transform, false);
            pillar.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            pillar.transform.localScale = new Vector3(0.12f, 1.2f, 0.12f);
            Object.DestroyImmediate(pillar.GetComponent<Collider>());

            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            var gold = new Color(1f, 0.78f, 0.25f, 0.75f);

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", gold);
            if (material.HasProperty("_Color")) material.SetColor("_Color", gold);
            SetUpTransparency(material);

            AssetDatabase.CreateAsset(material, "Assets/Settings/AR/LandmarkGold.mat");
            pillar.GetComponent<MeshRenderer>().sharedMaterial = material;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_MarkerPrefabPath);
            Object.DestroyImmediate(root);

            // The landmark trigger service loads prefabs from Resources/Landmarks by name, so a
            // copy has to live there too.
            const string resourceCopy = "Assets/Resources/Landmarks/TempleMarker.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(resourceCopy) == null)
                AssetDatabase.CopyAsset(k_MarkerPrefabPath, resourceCopy);

            Debug.Log($"[Setup] Created {k_MarkerPrefabPath}");
            return prefab;
        }

        // -----------------------------------------------------------------------------------
        // Scenes
        // -----------------------------------------------------------------------------------

        [MenuItem("Tools/Tirumala AR/Build Navigation Scene", priority = 40)]
        public static void BuildNavigationScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- AR session ---------------------------------------------------------------
            // AR Foundation 6 drives the camera pose through the Input System's TrackedPoseDriver,
            // so no separate input manager component is needed on the session.
            var sessionGo = new GameObject("AR Session");
            sessionGo.AddComponent<ARSession>();

            // --- XR Origin ----------------------------------------------------------------
            var originGo = new GameObject("XR Origin");
            var origin = originGo.AddComponent<XROrigin>();

            var offsetGo = new GameObject("Camera Offset");
            offsetGo.transform.SetParent(originGo.transform, false);

            var cameraGo = new GameObject("AR Camera");
            cameraGo.transform.SetParent(offsetGo.transform, false);
            cameraGo.tag = "MainCamera";

            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;   // the route is long, but nothing is drawn beyond ~30 m

            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<ARCameraManager>();
            cameraGo.AddComponent<ARCameraBackground>();
            cameraGo.AddComponent<AROcclusionManager>();
            ConfigureTrackedPoseDriver(cameraGo.AddComponent<TrackedPoseDriver>());

            origin.Camera = camera;
            origin.CameraFloorOffsetObject = offsetGo;

            var planeManager = originGo.AddComponent<ARPlaneManager>();
            var raycastManager = originGo.AddComponent<ARRaycastManager>();
            var anchorManager = originGo.AddComponent<ARAnchorManager>();
            var trackedImageManager = originGo.AddComponent<ARTrackedImageManager>();
            var pointCloudManager = originGo.AddComponent<ARPointCloudManager>();
            pointCloudManager.enabled = false; // debug only

            // --- Managers -----------------------------------------------------------------
            var managersGo = new GameObject("Managers");

            var arBootstrapper = managersGo.AddComponent<ARSessionBootstrapper>();
            SetPrivateField(arBootstrapper, "m_Session", sessionGo.GetComponent<ARSession>());
            SetPrivateField(arBootstrapper, "m_Origin", origin);
            SetPrivateField(arBootstrapper, "m_PlaneManager", planeManager);
            SetPrivateField(arBootstrapper, "m_RaycastManager", raycastManager);
            SetPrivateField(arBootstrapper, "m_AnchorManager", anchorManager);
            SetPrivateField(arBootstrapper, "m_CameraManager", cameraGo.GetComponent<ARCameraManager>());
            SetPrivateField(arBootstrapper, "m_OcclusionManager", cameraGo.GetComponent<AROcclusionManager>());

            var gps = managersGo.AddComponent<GpsManager>();
            var voice = managersGo.AddComponent<VoiceNavigationManager>();

            var relocalizer = managersGo.AddComponent<ReferenceImageRelocalizer>();
            SetPrivateField(relocalizer, "m_TrackedImageManager", trackedImageManager);

            var arrowsGo = new GameObject("Arrows");
            var arrows = arrowsGo.AddComponent<DynamicArrowManager>();
            SetPrivateField(arrows, "m_ArrowPrefab",
                AssetDatabase.LoadAssetAtPath<GameObject>(k_ArrowPrefabPath));

            var landmarkRoot = new GameObject("Landmark Content").transform;

            var bootstrapGo = new GameObject("AppBootstrap");
            var bootstrap = bootstrapGo.AddComponent<AppBootstrap>();
            SetPrivateField(bootstrap, "m_Config", AssetDatabase.LoadAssetAtPath<AppConfig>(k_ConfigPath));
            SetPrivateField(bootstrap, "m_ArSession", arBootstrapper);
            SetPrivateField(bootstrap, "m_Gps", gps);
            SetPrivateField(bootstrap, "m_Arrows", arrows);
            SetPrivateField(bootstrap, "m_Relocalizer", relocalizer);
            SetPrivateField(bootstrap, "m_Voice", voice);
            SetPrivateField(bootstrap, "m_LandmarkContentRoot", landmarkRoot);

            // --- UI ------------------------------------------------------------------------
            BuildNavigationUI(bootstrap, planeManager, pointCloudManager);

            Directory.CreateDirectory(k_SceneRoot);
            EditorSceneManager.SaveScene(scene, k_NavigationScenePath);
            Debug.Log($"[Setup] Built {k_NavigationScenePath}");
        }

        /// <summary>
        /// The Input System's TrackedPoseDriver needs explicit bindings; without them the AR
        /// camera never moves and every arrow appears frozen in space.
        /// </summary>
        static void ConfigureTrackedPoseDriver(TrackedPoseDriver driver)
        {
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

            driver.positionInput = new InputActionProperty(
                new InputAction("Position", InputActionType.Value, "<XRHMD>/centerEyePosition",
                    expectedControlType: "Vector3"));

            driver.rotationInput = new InputActionProperty(
                new InputAction("Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation",
                    expectedControlType: "Quaternion"));

            driver.trackingStateInput = new InputActionProperty(
                new InputAction("Tracking State", InputActionType.Value, "<XRHMD>/trackingState",
                    expectedControlType: "Integer"));
        }

        /// <summary>
        /// The interface builds itself. <see cref="UIRoot"/> constructs the canvas, the responsive
        /// scaler, all five screens, the bottom navigation bar and the modal overlays at runtime,
        /// so the generated scene holds one component instead of several hundred serialised
        /// references that could drift out of sync with the code.
        /// </summary>
        static void BuildNavigationUI(AppBootstrap bootstrap, ARPlaneManager planeManager,
            ARPointCloudManager pointCloudManager)
        {
            var uiGo = new GameObject("UI");
            uiGo.AddComponent<UIRoot>();

            // The debug overlay is authored here rather than in UIRoot because it is a developer
            // surface, not part of the pilgrim-facing design, and it is disabled by default.
            var canvasGo = new GameObject("Debug Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(uiGo.transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // above the app UI

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);

            var debugPanel = CreatePanel(canvasGo.transform, "Debug Panel",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(320f, -720f), new Vector2(620f, 1040f));
            debugPanel.gameObject.AddComponent<SafeAreaFitter>();

            var debugLabel = CreateLabel(debugPanel, "DebugText", string.Empty, 22, Vector2.zero);
            debugLabel.alignment = TextAlignmentOptions.TopLeft;
            debugLabel.rectTransform.anchorMin = Vector2.zero;
            debugLabel.rectTransform.anchorMax = Vector2.one;
            debugLabel.rectTransform.offsetMin = new Vector2(18f, 18f);
            debugLabel.rectTransform.offsetMax = new Vector2(-18f, -18f);

            var overlay = canvasGo.AddComponent<DebugOverlay>();
            SetPrivateField(overlay, "m_Bootstrap", bootstrap);
            SetPrivateField(overlay, "m_Panel", debugPanel.gameObject);
            SetPrivateField(overlay, "m_Readout", debugLabel);
            SetPrivateField(overlay, "m_PlaneManager", planeManager);
            SetPrivateField(overlay, "m_PointCloudManager", pointCloudManager);
            debugPanel.gameObject.SetActive(false);
        }

        [MenuItem("Tools/Tirumala AR/Build Main Menu Scene", priority = 41)]
        public static void BuildMainMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.09f);

            var canvasGo = new GameObject("Menu Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);

            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            var panel = CreatePanel(canvasGo.transform, "Menu",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 1400f));

            CreateLabel(panel, "Title", "Tirumala AR Navigation", 58, new Vector2(0f, 560f));
            CreateLabel(panel, "Subtitle", "Alipiri Mettu · offline guidance", 32, new Vector2(0f, 490f));

            var controller = canvasGo.AddComponent<MainMenuController>();

            SetPrivateField(controller, "m_StartButton", CreateButton(panel, "Start Walk", new Vector2(0f, 320f)));
            SetPrivateField(controller, "m_ResumeButton", CreateButton(panel, "Resume", new Vector2(0f, 200f)));
            SetPrivateField(controller, "m_ResetProgressButton", CreateButton(panel, "Reset Progress", new Vector2(0f, -420f)));
            SetPrivateField(controller, "m_QuitButton", CreateButton(panel, "Quit", new Vector2(0f, -540f)));

            SetPrivateField(controller, "m_LastWalkLabel", CreateLabel(panel, "LastWalk", "", 28, new Vector2(0f, 100f)));
            SetPrivateField(controller, "m_VersionLabel", CreateLabel(panel, "Version", "", 24, new Vector2(0f, -640f)));
            SetPrivateField(controller, "m_ArrowSpacingLabel",
                CreateLabel(panel, "ArrowSpacingLabel", "Arrow spacing 0.75 m", 28, new Vector2(0f, -140f)));

            SetPrivateField(controller, "m_VoiceToggle", CreateToggle(panel, "Voice guidance", new Vector2(-200f, 0f)));
            SetPrivateField(controller, "m_DarkModeToggle", CreateToggle(panel, "Dark mode", new Vector2(-200f, -60f)));
            SetPrivateField(controller, "m_DebugToggle", CreateToggle(panel, "Debug overlay", new Vector2(-200f, -220f)));
            SetPrivateField(controller, "m_ArrowSpacingSlider", CreateSlider(panel, new Vector2(0f, -180f)));

            Directory.CreateDirectory(k_SceneRoot);
            EditorSceneManager.SaveScene(scene, k_MainMenuScenePath);
            Debug.Log($"[Setup] Built {k_MainMenuScenePath}");
        }

        // -----------------------------------------------------------------------------------
        // UI factory helpers
        // -----------------------------------------------------------------------------------

        static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.color = new Color(0.04f, 0.05f, 0.08f, 0.72f);
            image.raycastTarget = false;

            return rect;
        }

        static TMP_Text CreateLabel(Transform parent, string name, string text, int fontSize, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var label = go.AddComponent<TextMeshProUGUI>();

            var rect = label.rectTransform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(560f, 70f);

            label.text = text;
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            return label;
        }

        static Button CreateButton(Transform parent, string text, Vector2 position)
        {
            var go = new GameObject($"Button_{text}", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(560f, 96f);

            go.GetComponent<Image>().color = new Color(0.13f, 0.48f, 1f, 0.92f);

            var label = CreateLabel(rect, "Label", text, 36, Vector2.zero);
            label.rectTransform.sizeDelta = new Vector2(540f, 90f);

            return go.GetComponent<Button>();
        }

        static Toggle CreateToggle(Transform parent, string text, Vector2 position)
        {
            var go = new GameObject($"Toggle_{text}", typeof(RectTransform), typeof(Toggle));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(64f, 64f);

            var boxGo = new GameObject("Box", typeof(RectTransform), typeof(Image));
            var boxRect = boxGo.GetComponent<RectTransform>();
            boxRect.SetParent(rect, false);
            boxRect.sizeDelta = new Vector2(56f, 56f);
            boxGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);

            var checkGo = new GameObject("Check", typeof(RectTransform), typeof(Image));
            var checkRect = checkGo.GetComponent<RectTransform>();
            checkRect.SetParent(boxRect, false);
            checkRect.sizeDelta = new Vector2(36f, 36f);
            var check = checkGo.GetComponent<Image>();
            check.color = new Color(0.2f, 0.85f, 0.4f, 1f);

            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = boxGo.GetComponent<Image>();
            toggle.graphic = check;

            var label = CreateLabel(rect, "Label", text, 30, new Vector2(300f, 0f));
            label.alignment = TextAlignmentOptions.Left;

            return toggle;
        }

        static Slider CreateSlider(Transform parent, Vector2 position)
        {
            var go = new GameObject("Slider_ArrowSpacing", typeof(RectTransform), typeof(Slider));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(560f, 40f);

            var backgroundGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            var backgroundRect = backgroundGo.GetComponent<RectTransform>();
            backgroundRect.SetParent(rect, false);
            backgroundRect.anchorMin = new Vector2(0f, 0.25f);
            backgroundRect.anchorMax = new Vector2(1f, 0.75f);
            backgroundRect.sizeDelta = Vector2.zero;
            backgroundGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.2f);

            var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
            var handleArea = handleAreaGo.GetComponent<RectTransform>();
            handleArea.SetParent(rect, false);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.sizeDelta = new Vector2(-20f, 0f);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var handleRect = handleGo.GetComponent<RectTransform>();
            handleRect.SetParent(handleArea, false);
            handleRect.sizeDelta = new Vector2(36f, 36f);
            handleGo.GetComponent<Image>().color = new Color(0.13f, 0.48f, 1f, 1f);

            var slider = go.GetComponent<Slider>();
            slider.targetGraphic = handleGo.GetComponent<Image>();
            slider.handleRect = handleRect;
            slider.minValue = 0.4f;
            slider.maxValue = 2f;
            slider.value = 0.75f;

            return slider;
        }

        // -----------------------------------------------------------------------------------
        // Build configuration
        // -----------------------------------------------------------------------------------

        [MenuItem("Tools/Tirumala AR/Configure Android Build", priority = 60)]
        public static void ConfigureAndroidBuild()
        {
            PlayerSettings.companyName = "Tirumala AR Research";
            PlayerSettings.productName = "Tirumala AR Navigation";
            PlayerSettings.SetApplicationIdentifier(
                UnityEditor.Build.NamedBuildTarget.Android, "com.tirumalaar.navigation");

            // ARCore requires OpenGL ES 3.0+ or Vulkan, ARM64, and no multithreaded rendering
            // conflicts. IL2CPP + ARM64 is also mandatory for Play Store uploads.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3,
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan
            });

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            // The camera permission is declared by the ARCore package; location must be requested
            // explicitly at runtime, which GpsManager does.
            PlayerSettings.Android.forceInternetPermission = false;
            PlayerSettings.Android.forceSDCardPermission = false;

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(k_MainMenuScenePath, true),
                new EditorBuildSettingsScene(k_NavigationScenePath, true)
            };

            Debug.Log("[Setup] Android build configured: IL2CPP, ARM64, min SDK 26, portrait, GLES3/Vulkan.");
        }

        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Assigns a [SerializeField] private field. Generated scenes need their inspector
        /// references wired, and SerializedObject is the only supported way to touch private
        /// serialised state from an editor script.
        /// </summary>
        static void SetPrivateField(Object target, string fieldName, object value)
        {
            if (target == null)
                return;

            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[Setup] {target.GetType().Name} has no serialised field '{fieldName}'.");
                return;
            }

            switch (value)
            {
                case null:
                    property.objectReferenceValue = null;
                    break;

                case Object unityObject:
                    property.objectReferenceValue = unityObject;
                    break;

                case Renderer[] renderers:
                    property.arraySize = renderers.Length;
                    for (var i = 0; i < renderers.Length; i++)
                        property.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
                    break;

                default:
                    Debug.LogWarning($"[Setup] Unsupported value type for '{fieldName}'.");
                    break;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
