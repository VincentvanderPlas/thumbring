using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace Thumbring;

/// <summary>
/// Thumbring.exe                  run the overlay (one instance)
/// Thumbring.exe --toggle         open or close the ring from outside
/// Thumbring.exe --go gmail       run one button
/// Thumbring.exe --buzz knock     play one vibration
/// Thumbring.exe --hook stop      what Claude Code runs
/// Thumbring.exe --setup          start with Windows and add the Claude Code alerts
/// Thumbring.exe --snapshot a.png [--hover gmail] [--bg #1E1E1E]   render without opening a window
/// </summary>
internal static class Program
{
    private static Mutex? _single;

    private static readonly string[] Waveforms =
    {
        "completed", "knock", "happy_alert", "sharp_state_change", "damp_state_change",
        "sharp_collision", "damp_collision", "subtle_collision", "angry_alert",
        "square", "wave", "firework", "mad", "jingle", "ringing"
    };

    [STAThread]
    public static int Main(string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        if (Arg("--hook") is { } word) return Setup.Hook(word);

        if (args.Contains("--setup"))
        {
            Setup.StartsWithWindows = true;
            Setup.ClaudeAlerts = true;
            return 0;
        }

        if (args.Length > 0 && Arg("--snapshot") == null)
        {
            var msg = new Dictionary<string, string?>();
            if (args.Contains("--toggle")) msg["cmd"] = "toggle";
            else if (Arg("--go") is { } id) { msg["cmd"] = "go"; msg["id"] = id; }
            else if (Arg("--buzz") is { } w) { msg["cmd"] = "buzz"; msg["w"] = w; }
            return Bus.Send(JsonSerializer.Serialize(msg)) ? 0 : 1;
        }

        try
        {
            Config.Load();
        }
        catch (Exception e)
        {
            MessageBox.Show($"config.json could not be read.\n\n{e.Message}\n\n{Config.FilePath}", "Thumbring");
            return 1;
        }

        if (Arg("--snapshot") is { } path)
        {
            _ = new Application();
            var prefs0 = new Prefs();
            var hap0 = new Haptics();
            var win0 = new RingWindow(new Spaces(), hap0, prefs0, new Chats(hap0, prefs0, () => false));
            win0.Snapshot(path, Arg("--hover"), new Front(IntPtr.Zero, "chrome", "Notes - Google Chrome", Config.Current.Spaces[0]),
                int.Parse(Arg("--chats") ?? "2"), int.Parse(Arg("--need") ?? "0"),
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Arg("--bg") ?? "#1E1E1E"));
            return 0;
        }

        _single = new Mutex(true, "ThumbringSingleInstance", out bool first);
        if (!first) return 0;

        Setup.InstallPlugin();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var prefs = Prefs.Load();
        var haptics = new Haptics();
        var spaces = new Spaces();
        var chats = new Chats(haptics, prefs,
            () => spaces.Snapshot().Process.Equals("Code", StringComparison.OrdinalIgnoreCase));
        spaces.Changed += f =>
        {
            if (f.Process.Equals("Code", StringComparison.OrdinalIgnoreCase)) chats.SeenInVsCode();
        };

        var window = new RingWindow(spaces, haptics, prefs, chats);
        window.Show();
        window.Warm();

        Bus.Listen(app.Dispatcher, m =>
        {
            switch (m.GetProperty("cmd").GetString())
            {
                case "toggle": window.Toggle(); break;
                case "go": window.GoById(m.GetProperty("id").GetString()!); break;
                case "buzz": haptics.Play(m.GetProperty("w").GetString()!); break;
                case "event": chats.On(m.GetProperty("e").GetString()!, m.GetProperty("s").GetString()!); break;
            }
        });

        using var tray = BuildTray(app, haptics, prefs);
        app.Run();
        return 0;
    }

    private static WinForms.NotifyIcon BuildTray(Application app, Haptics haptics, Prefs prefs)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Edit buttons", null, (_, _) =>
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Config.FilePath}\"") { UseShellExecute = true }));
        menu.Items.Add("Reload buttons", null, (_, _) =>
        {
            _single?.ReleaseMutex();
            _single?.Dispose();
            Process.Start(Environment.ProcessPath!);
            app.Shutdown();
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Picker("Chat finished pulse", () => prefs.Finished, v => prefs.Finished = v, haptics, prefs));
        menu.Items.Add(Picker("Needs permission pulse", () => prefs.Permission, v => prefs.Permission = v, haptics, prefs));
        menu.Items.Add(Check("Also pulse while in VS Code", prefs.BuzzWhileInVsCode, on => { prefs.BuzzWhileInVsCode = on; prefs.Save(); }));
        menu.Items.Add(Check("Claude Code alerts", Setup.ClaudeAlerts, on => Setup.ClaudeAlerts = on));
        menu.Items.Add(Check("Start with Windows", Setup.StartsWithWindows, on => Setup.StartsWithWindows = on));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quit Thumbring", null, (_, _) => app.Shutdown());
        return new WinForms.NotifyIcon { Text = "Thumbring", Icon = TrayIcon(), Visible = true, ContextMenuStrip = menu };
    }

    private static WinForms.ToolStripMenuItem Check(string title, bool state, Action<bool> set)
    {
        var item = new WinForms.ToolStripMenuItem(title) { Checked = state, CheckOnClick = true };
        item.Click += (_, _) => set(item.Checked);
        return item;
    }

    private static WinForms.ToolStripMenuItem Picker(string title, Func<string> get, Action<string> set, Haptics haptics, Prefs prefs)
    {
        var item = new WinForms.ToolStripMenuItem(title);
        foreach (var w in Waveforms)
        {
            var sub = new WinForms.ToolStripMenuItem(w) { Checked = get() == w };
            sub.Click += (_, _) =>
            {
                set(w);
                prefs.Save();
                haptics.Play(w);
                foreach (WinForms.ToolStripMenuItem o in item.DropDownItems) o.Checked = o.Text == w;
            };
            item.DropDownItems.Add(sub);
        }
        return item;
    }

    private static Icon TrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(0xEC, 0xEC, 0xEE), 5);
            g.DrawEllipse(pen, 5, 5, 22, 22);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
