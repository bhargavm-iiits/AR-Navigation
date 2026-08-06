using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace TirumalaAR.EditorTools
{
    /// <summary>
    /// Configures XR Plug-in Management for Android.
    ///
    /// Creating the ARCoreLoader asset is not enough on its own: XR Management only initialises
    /// loaders that are registered against a specific build target. If the Android entry is
    /// missing, ARCore never starts, ARSession reports Unsupported, and the camera feed never
    /// appears — with no error beyond a single log line. That failure mode is quiet enough to
    /// cost hours, so this runs as part of the project setup rather than relying on someone
    /// remembering to tick a checkbox in Project Settings.
    /// </summary>
    public static class XRSetup
    {
        const string k_ARCoreLoaderType = "UnityEngine.XR.ARCore.ARCoreLoader";
        const string k_SettingsDirectory = "Assets/XR";
        const string k_SettingsAssetPath = k_SettingsDirectory + "/XRGeneralSettingsPerBuildTarget.asset";

        [MenuItem("Tools/Tirumala AR/Configure XR for Android", priority = 5)]
        public static void ConfigureAndroid()
        {
            var perTarget = GetOrCreatePerBuildTargetSettings();

            if (perTarget == null)
            {
                Debug.LogError("[XRSetup] Could not create XRGeneralSettingsPerBuildTarget.");
                return;
            }

            var settings = GetOrCreateSettingsForAndroid(perTarget);

            if (settings?.Manager == null)
            {
                Debug.LogError("[XRSetup] Could not create the XRManagerSettings for Android.");
                return;
            }

            // Start XR automatically when the player launches; AR is the whole app.
            settings.InitManagerOnStart = true;

            // Note the asymmetry in this API: IsLoaderAssigned is queried by build target, while
            // AssignLoader operates on a specific XRManagerSettings instance.
            if (XRPackageMetadataStore.IsLoaderAssigned(k_ARCoreLoaderType, BuildTargetGroup.Android))
            {
                Debug.Log("[XRSetup] ARCore loader was already assigned to Android.");
            }
            else if (XRPackageMetadataStore.AssignLoader(settings.Manager, k_ARCoreLoaderType, BuildTargetGroup.Android))
            {
                Debug.Log("[XRSetup] Assigned the ARCore loader to the Android build target.");
            }
            else
            {
                Debug.LogError(
                    "[XRSetup] Failed to assign the ARCore loader. Confirm com.unity.xr.arcore is installed, " +
                    "then use Project Settings > XR Plug-in Management > Android and tick ARCore.");
                return;
            }

            EditorUtility.SetDirty(settings.Manager);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Assigning the loader only starts ARCore; URP still needs the background feature
            // before any of it reaches the screen.
            ConfigureUrpRenderers();

            Verify();
        }

        static XRGeneralSettingsPerBuildTarget GetOrCreatePerBuildTargetSettings()
        {
            EditorBuildSettings.TryGetConfigObject(
                XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);

            if (perTarget != null)
                return perTarget;

            // The asset may exist on disk without being registered as the config object, which is
            // exactly the half-configured state a fresh AR project lands in.
            perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(k_SettingsAssetPath);

            if (perTarget == null)
            {
                if (!Directory.Exists(k_SettingsDirectory))
                    Directory.CreateDirectory(k_SettingsDirectory);

                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, k_SettingsAssetPath);
                AssetDatabase.SaveAssets();
            }

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            return perTarget;
        }

        static XRGeneralSettings GetOrCreateSettingsForAndroid(XRGeneralSettingsPerBuildTarget perTarget)
        {
            if (perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                return perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);

            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);

            if (settings == null)
                return null;

            // The manager is stored as a sub-asset of the per-target settings.
            if (settings.Manager == null)
            {
                var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = "Android Providers";
                settings.Manager = manager;
                AssetDatabase.AddObjectToAsset(manager, perTarget);
            }

            if (settings.name == string.Empty)
                settings.name = "Android Settings";

            return settings;
        }

        /// <summary>
        /// Adds <see cref="ARBackgroundRendererFeature"/> to every URP renderer in the project.
        ///
        /// This is the second silent killer of AR on URP. The built-in pipeline draws the camera
        /// feed from ARCameraBackground directly, but URP renders it through a
        /// ScriptableRendererFeature instead. If the feature is not on the renderer, the AR
        /// session runs perfectly, tracking works, planes are detected — and the screen stays
        /// black, because nothing ever blits the camera texture. Absolutely nothing warns you.
        /// </summary>
        [MenuItem("Tools/Tirumala AR/Add AR Background to URP Renderers", priority = 7)]
        public static void ConfigureUrpRenderers()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(UniversalRendererData)}");

            if (guids.Length == 0)
            {
                Debug.LogWarning("[XRSetup] No URP renderer assets found. If this project uses the " +
                                 "Built-in pipeline no action is needed.");
                return;
            }

            var added = 0;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);

                if (data == null)
                    continue;

                var alreadyPresent = false;

                foreach (var existing in data.rendererFeatures)
                {
                    if (existing is ARBackgroundRendererFeature)
                    {
                        alreadyPresent = true;
                        break;
                    }
                }

                if (alreadyPresent)
                {
                    Debug.Log($"[XRSetup] '{path}' already has the AR background feature.");
                    continue;
                }

                var feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
                feature.name = "AR Background";

                data.rendererFeatures.Add(feature);
                AssetDatabase.AddObjectToAsset(feature, data);

                // URP keeps a parallel list of local file IDs for its features. Adding to the
                // list alone leaves that map stale, and the feature silently drops out on the
                // next import. ValidateRendererFeatures rebuilds it, but it is internal.
                var validate = typeof(ScriptableRendererData).GetMethod(
                    "ValidateRendererFeatures",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);

                validate?.Invoke(data, null);

                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
                added++;

                Debug.Log($"[XRSetup] Added the AR background feature to '{path}'.");
            }

            if (added > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        [MenuItem("Tools/Tirumala AR/Verify XR Configuration", priority = 6)]
        public static void Verify()
        {
            EditorBuildSettings.TryGetConfigObject(
                XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);

            if (perTarget == null)
            {
                Debug.LogError("[XRSetup] No XR settings are registered. Run 'Configure XR for Android'.");
                return;
            }

            var settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);

            if (settings?.Manager == null)
            {
                Debug.LogError("[XRSetup] Android has no XRManagerSettings. Run 'Configure XR for Android'.");
                return;
            }

            var loaders = settings.Manager.activeLoaders;

            if (loaders == null || loaders.Count == 0)
            {
                Debug.LogError(
                    "[XRSetup] Android has no active XR loaders — ARCore will not start and the camera " +
                    "will stay black. Run 'Configure XR for Android'.");
                return;
            }

            foreach (var loader in loaders)
                Debug.Log($"[XRSetup] Android active loader: {loader.GetType().FullName}");

            Debug.Log($"[XRSetup] InitManagerOnStart = {settings.InitManagerOnStart}");

            VerifyUrpRenderers();
        }

        static void VerifyUrpRenderers()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(UniversalRendererData)}");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);

                if (data == null)
                    continue;

                var hasFeature = false;

                foreach (var feature in data.rendererFeatures)
                {
                    if (feature is ARBackgroundRendererFeature)
                    {
                        hasFeature = true;
                        break;
                    }
                }

                if (hasFeature)
                    Debug.Log($"[XRSetup] '{path}' has the AR background feature.");
                else
                    Debug.LogError(
                        $"[XRSetup] '{path}' is MISSING ARBackgroundRendererFeature — the camera feed " +
                        "will not render and the screen will be black. " +
                        "Run 'Add AR Background to URP Renderers'.");
            }
        }
    }
}
