// settings.cfg loader, OCISLY-style. Mirrors the shape used by
// OfCourseIStillLoveYou's own settings.cfg — a single top-level
// `Settings { ... }` node parsed via KSP's ConfigNode API. All fields
// are optional; missing ones fall back to the defaults below.
//
// Fields:
//   BindAddress       — host the sidecar's HTTP signalling endpoint
//                       binds to. 127.0.0.1 = localhost only (default,
//                       safe). 0.0.0.0 = any interface (needed if you
//                       want a browser on another LAN device to hit
//                       the sidecar directly).
//   Port              — sidecar HTTP signalling port (default 8088).
//   Width / Height    — capture dimensions per Hullcam (default 768).
//                       Larger = more pixels to push through openh264
//                       on the CPU.
//   AutoSpawnSidecar  — whether the plugin should `Process.Start` the
//                       bundled sidecar binary on Awake. Set to false
//                       during sidecar development so `cargo run`
//                       owns the process.

using System.IO;
using UnityEngine;

namespace Kerbcam
{
    internal sealed class KerbcamSettings
    {
        public string BindAddress { get; private set; } = "0.0.0.0";
        public int Port { get; private set; } = 8088;
        public int Width { get; private set; } = 768;
        public int Height { get; private set; } = 768;
        public bool AutoSpawnSidecar { get; private set; } = true;

        public string HttpBind => $"{BindAddress}:{Port}";

        public static KerbcamSettings Load()
        {
            var settings = new KerbcamSettings();
            var path = Path.Combine(
                KSPUtil.ApplicationRootPath,
                "GameData", "Kerbcam", "settings.cfg");
            if (!File.Exists(path))
            {
                Debug.Log($"[Kerbcam] no settings.cfg at {path}; using defaults ({settings.HttpBind}, {settings.Width}×{settings.Height})");
                return settings;
            }

            var root = ConfigNode.Load(path);
            var node = root?.GetNode("Settings");
            if (node == null)
            {
                Debug.LogWarning($"[Kerbcam] settings.cfg at {path} is missing a 'Settings' node; using defaults");
                return settings;
            }

            ApplyString(node, "BindAddress", v => settings.BindAddress = v);
            ApplyInt(node, "Port", v => settings.Port = v);
            ApplyInt(node, "Width", v => settings.Width = v);
            ApplyInt(node, "Height", v => settings.Height = v);
            ApplyBool(node, "AutoSpawnSidecar", v => settings.AutoSpawnSidecar = v);

            Debug.Log($"[Kerbcam] settings loaded: bind={settings.HttpBind} dims={settings.Width}x{settings.Height} autoSpawn={settings.AutoSpawnSidecar}");
            return settings;
        }

        private static void ApplyString(ConfigNode node, string key, System.Action<string> set)
        {
            var raw = node.GetValue(key);
            if (!string.IsNullOrEmpty(raw)) set(raw.Trim());
        }

        private static void ApplyInt(ConfigNode node, string key, System.Action<int> set)
        {
            var raw = node.GetValue(key);
            if (string.IsNullOrEmpty(raw)) return;
            if (int.TryParse(raw.Trim(), out int v)) set(v);
            else Debug.LogWarning($"[Kerbcam] settings.cfg: {key}='{raw}' is not an integer; using default");
        }

        private static void ApplyBool(ConfigNode node, string key, System.Action<bool> set)
        {
            var raw = node.GetValue(key);
            if (string.IsNullOrEmpty(raw)) return;
            if (bool.TryParse(raw.Trim(), out bool v)) set(v);
            else Debug.LogWarning($"[Kerbcam] settings.cfg: {key}='{raw}' is not a bool; using default");
        }
    }
}
