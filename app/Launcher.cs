using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Thumbring;

internal static class Log
{
    public static void Line(string text)
    {
        try
        {
            File.AppendAllText(Path.Combine(Config.Folder, "thumbring.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
        }
        catch { }
    }
}

internal static class Launcher
{
    private static string ChromeExe()
    {
        var known = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", null) as string;
        return !string.IsNullOrEmpty(known) ? known : @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    }

    /// <summary>Opens a page in one Chrome profile. The extension turns the marked tab into a jump to an open tab.</summary>
    public static void OpenPage(string url, Space space)
    {
        Native.AllowSetForegroundWindow(-1);
        try
        {
            Process.Start(new ProcessStartInfo(ChromeExe())
            {
                ArgumentList = { "--profile-directory=" + space.ChromeProfile, url + "#ringjump" },
                UseShellExecute = false
            });
        }
        catch (Exception e) { Log.Line("Chrome did not start: " + e.Message); }
    }

    public static void OpenApp(Target t)
    {
        var windows = t.Process == null ? new() : Native.WindowsOf(t.Process);
        var front = Native.GetForegroundWindow();

        // A second click on an application with two windows reaches the other window.
        var pick = windows.FirstOrDefault(h => h != front);
        if (pick == IntPtr.Zero && windows.Count > 0) return;   // the only window is already in front
        if (pick != IntPtr.Zero && Native.Focus(pick)) return;
        if (t.Launch != null) Launch(t.Launch);
    }

    private static void Launch(string command)
    {
        Native.AllowSetForegroundWindow(-1);
        try
        {
            if (command.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe", command) { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo(command) { UseShellExecute = true });
        }
        catch (Exception e) { Log.Line($"Could not open {command}: {e.Message}"); }
    }

    /// <summary>Keys from the button if set, else Wispr Flow's hands-free shortcut, else Windows voice typing.</summary>
    public static async Task DictateAsync(int delayMs, int[]? keys)
    {
        await Task.Delay(delayMs);
        Native.SendKeys(keys ?? WisprKeys() ?? new[] { 91, 72 });
    }

    private static int[]? WisprKeys()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wispr Flow", "config.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var map = doc.RootElement.GetProperty("prefs").GetProperty("user").GetProperty("shortcuts");
            foreach (var p in map.EnumerateObject())
                if (p.Value.GetString() == "popo")   // Wispr's name for hands-free
                    return p.Name.Split('+').Select(int.Parse).ToArray();
        }
        catch { }
        return null;
    }
}
