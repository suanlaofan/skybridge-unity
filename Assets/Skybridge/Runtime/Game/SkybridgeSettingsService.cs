using UnityEngine;

namespace Skybridge.Game
{
    public sealed class SkybridgeSettings
    {
        public bool Muted;
        public bool ReducedMotion;
        public bool AutoFocus = true;
    }

    public static class SkybridgeSettingsService
    {
        private const string MutedKey = "skybridge-threejs.settings.muted";
        private const string ReducedMotionKey = "skybridge-threejs.settings.reduced-motion";
        private const string AutoFocusKey = "skybridge-threejs.settings.auto-focus";

        public static SkybridgeSettings Load()
        {
            return new SkybridgeSettings
            {
                Muted = PlayerPrefs.GetInt(MutedKey, 0) != 0,
                ReducedMotion = PlayerPrefs.GetInt(ReducedMotionKey, 0) != 0,
                AutoFocus = PlayerPrefs.GetInt(AutoFocusKey, 1) != 0,
            };
        }

        public static void Save(SkybridgeSettings settings)
        {
            if (settings == null) return;
            PlayerPrefs.SetInt(MutedKey, settings.Muted ? 1 : 0);
            PlayerPrefs.SetInt(ReducedMotionKey, settings.ReducedMotion ? 1 : 0);
            PlayerPrefs.SetInt(AutoFocusKey, settings.AutoFocus ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
