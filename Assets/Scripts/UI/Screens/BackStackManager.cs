using UnityEngine;
using UnityEngine.InputSystem;

namespace TirumalaAR.UI.Screens
{
    /// <summary>
    /// Android hardware back-button handling for the whole navigation scene.
    ///
    /// Added once by <see cref="UIRoot.Build"/>. Polls the Escape key each frame — under the
    /// Input System package (already active here via <see cref="UnityEngine.InputSystem.UI.InputSystemUIInputModule"/>
    /// on the event system), Android's hardware back button surfaces as the keyboard's Escape
    /// key, so no platform-specific plugin is needed.
    ///
    /// Priority order, highest first: a dialog on top of everything closes first, then a modal
    /// overlay, then pause, then screen navigation falls back one level at a time, and only the
    /// AR screen — the root of the flow — offers to exit the app.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackStackManager : MonoBehaviour
    {
        void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
                return;

            GoBack();
        }

        static void GoBack()
        {
            var root = UIRoot.Instance;

            if (root == null)
                return;

            var overlays = root.Overlays;
            var popup = root.LandmarkPopup;

            // 1. The exit-confirmation dialog sits on top of everything; Back closes it exactly
            //    like pressing "No".
            if (overlays != null && overlays.IsExitConfirmShown)
            {
                overlays.DismissTopOverlay();
                return;
            }

            // 2. The landmark detail popup closes on its own, without touching anything beneath it.
            if (popup != null && popup.IsShowing)
            {
                popup.Dismiss();
                return;
            }

            // 3. A turn / arrival / weak-GPS card dismisses itself.
            if (overlays != null && overlays.HasBlockingOverlay)
            {
                overlays.DismissTopOverlay();
                return;
            }

            // 4. Paused navigation resumes rather than falling through to screen navigation.
            if (overlays != null && overlays.IsPaused)
            {
                overlays.Resume();
                return;
            }

            var active = root.ActiveScreen;

            if (active is SettingsScreen or ProgressScreen)
            {
                if (root.PreviousScreen != null)
                    root.ShowScreen(root.PreviousScreen);
                else
                    root.ShowScreen<ARNavigationScreen>();

                return;
            }

            if (active is LandmarksScreen)
            {
                root.ShowScreen<MapScreen>();
                return;
            }

            if (active is MapScreen)
            {
                root.ShowScreen<ARNavigationScreen>();
                return;
            }

            if (active is ARNavigationScreen)
                overlays?.ShowExitConfirm();
        }
    }
}
