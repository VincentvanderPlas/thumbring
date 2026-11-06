using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Thumbring;

/// <summary>The two optional changes outside the app's own folder. Both are tray checkboxes and both undo cleanly.</summary>
internal static class Setup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string Exe => Environment.ProcessPath!;

    /// <summary>
    /// Unpacks the vibration plugin next to the other Logi plugins and asks the service to load it.
    /// Runs at start and only does work when Thumbring.lplug4 is newer than what is installed.
    /// </summary>
    public static void InstallPlugin()
    {
        try
        {
            var package = Path.Combine(Path.GetDirectoryName(Exe)!, "Thumbring.lplug4");
            var plugins = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Logi\LogiPluginService");
            if (!File.Exists(package) || !Directory.Exists(plugins)) return;

            var target = Path.Combine(plugins, "Plugins", "Thumbring");
            var dll = Path.Combine(target, "bin", "ThumbringPlugin.dll");
            if (File.Exists(dll) && File.GetLastWriteTimeUtc(dll) >= File.GetLastWriteTimeUtc(package)) return;

            Directory.CreateDirectory(target);
            ZipFile.ExtractToDirectory(package, target, true);
            File.SetLastWriteTimeUtc(dll, DateTime.UtcNow);
            Process.Start(new ProcessStartInfo("loupedeck:plugin/Thumbring/reload") { UseShellExecute = true });
        }
        catch (Exception e) { Log.Line("Plugin install failed: " + e.Message); }
    }

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Thumbring") != null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("Thumbring", $"\"{Exe}\"");
            else key.DeleteValue("Thumbring", false);
        }
    }

    // ---- Claude Code alerts ---------------------------------------------------------------------

    private static readonly (string hook, string word)[] Hooks =
    {
        ("Stop", "stop"), ("Notification", "permission"), ("UserPromptSubmit", "prompt"), ("SessionEnd", "end")
    };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    private static bool IsOurs(JsonNode? group) =>
        group?["hooks"]?.AsArray().Any(h => (h?["command"]?.GetValue<string>() ?? "").Contains("Thumbring.exe\" --hook")) == true;

    public static bool ClaudeAlerts
    {
        get
        {
            try
            {
                var hooks = JsonNode.Parse(File.ReadAllText(SettingsPath))?["hooks"];
                return hooks?["Stop"]?.AsArray().Any(IsOurs) == true;
            }
            catch { return false; }
        }
        set
        {
            var settings = File.Exists(SettingsPath)
                ? JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject()
                : new JsonObject();
            if (File.Exists(SettingsPath))
                File.Copy(SettingsPath, $"{SettingsPath}.before-thumbring-{DateTime.Now:yyyyMMdd-HHmmss}.bak");

            var hooks = settings["hooks"]?.AsObject();
            if (hooks == null) settings["hooks"] = hooks = new JsonObject();

            foreach (var (hook, word) in Hooks)
            {
                var groups = hooks[hook]?.AsArray() ?? new JsonArray();
                foreach (var ours in groups.Where(IsOurs).ToList()) groups.Remove(ours);
                if (value)
                {
                    groups.Add(new JsonObject
                    {
                        ["hooks"] = new JsonArray(new JsonObject
                        {
                            ["type"] = "command", ["command"] = $"\"{Exe}\" --hook {word}", ["timeout"] = 3, ["async"] = true
                        })
                    });
                }
                if (groups.Count == 0) hooks.Remove(hook);
                else if (hooks[hook] == null) hooks[hook] = groups;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, settings.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));
        }
    }

    /// <summary>What Claude Code runs. Never blocks a chat and stays silent when the ring is not running.</summary>
    public static int Hook(string word)
    {
        // Scripted runs (claude -p, the SDK) are not chats anyone is waiting on.
        if ((Environment.GetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT") ?? "").StartsWith("sdk")) return 0;

        string session = "?", message = "";
        try
        {
            var input = JsonNode.Parse(Console.In.ReadToEnd());
            session = input?["session_id"]?.GetValue<string>() ?? "?";
            message = (input?["notification_type"]?.GetValue<string>() ?? "") + " " + (input?["message"]?.GetValue<string>() ?? "");
        }
        catch { }

        // Notification also fires for "waiting for your input". Only a real permission question counts.
        if (word == "permission" && !message.Contains("permission", StringComparison.OrdinalIgnoreCase)) return 0;

        Bus.Send(JsonSerializer.Serialize(new { cmd = "event", e = word, s = session }));
        return 0;
    }
}
