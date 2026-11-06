using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Thumbring;

/// <summary>
/// The overlay. It never takes focus: the window under the ring stays the foreground window,
/// which is how the smart buttons know where you are and how Wispr types into the right field.
/// </summary>
internal sealed class RingWindow : Window
{
    private const double Dim = 400, C = Dim / 2, R1 = 86, R2 = 140, Main = 52, Sat = 42, Hub = 34, Reach = 182;
    private const int HotkeyRing = 1, HotkeyDictate = 2;

    private readonly Spaces _spaces;
    private readonly Haptics _haptics;
    private readonly Prefs _prefs;
    private readonly Chats _chats;

    private readonly Canvas _root = new() { Width = Dim, Height = Dim };
    private readonly Canvas _satLayer = new();
    private readonly List<Bubble> _mains = new();
    private readonly List<Bubble> _sats = new();
    private readonly Grid _hub = new() { Width = Hub, Height = Hub, Cursor = Cursors.Hand };
    private readonly SolidColorBrush _hubFill = new(Bubble.Rest);
    private readonly SolidColorBrush _hubInk = new(Color.FromRgb(0x6E, 0x6E, 0x73));
    private readonly Border _pill = new();
    private readonly TextBlock _pillTitle = Text(12.5, FontWeights.SemiBold, Config.Ink);
    private readonly TextBlock _pillSub = Text(10.5, FontWeights.Normal, Color.FromRgb(0x6E, 0x6E, 0x73));
    private readonly DispatcherTimer _hoverIntent = new() { Interval = TimeSpan.FromMilliseconds(90) };

    private Bubble? _hovered;
    private Bubble? _openParent;
    private Bubble? _pendingParent;
    private Front _front = new(IntPtr.Zero, "", "", null);
    private bool _open;
    private double _scale = 1;
    private int _ox, _oy;
    private IntPtr _mouseHook, _keyHook;
    private Native.HookProc? _mouseProc, _keyProc;

    public RingWindow(Spaces spaces, Haptics haptics, Prefs prefs, Chats chats)
    {
        _spaces = spaces;
        _haptics = haptics;
        _prefs = prefs;
        _chats = chats;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = Height = Dim;
        Left = Top = -10000;
        Title = "Thumbring";
        Content = _root;

        BuildScene();
        _hoverIntent.Tick += (_, _) => { _hoverIntent.Stop(); OpenSatellites(_pendingParent); };
        _chats.Changed += () => { if (_open) Refresh(); };
    }

    // ---- scene ------------------------------------------------------------------------------

    private static TextBlock Text(double size, FontWeight weight, Color color) => new()
    {
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontSize = size, FontWeight = weight, Foreground = new SolidColorBrush(color),
        HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 92
    };

    private void BuildScene()
    {
        // Invisible, but solid to the mouse: a click between the buttons closes the ring
        // instead of landing in the application behind it.
        var catcher = new Ellipse
        {
            Width = Reach * 2, Height = Reach * 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0))
        };
        Place(catcher, C, C, Reach * 2);
        catcher.MouseLeftButtonDown += (_, _) => Close2();
        catcher.MouseEnter += (_, _) => { if (_hovered == null) HidePill(); };
        _root.Children.Add(catcher);

        _root.Children.Add(_satLayer);

        for (int i = 0; i < Config.Current.Buttons.Count; i++)
        {
            var b = new Bubble(Config.Current.Buttons[i], Main);
            double a = (i * 360.0 / Config.Current.Buttons.Count - 90) * Math.PI / 180;
            b.CenterX = C + R1 * Math.Cos(a);
            b.CenterY = C + R1 * Math.Sin(a);
            Place(b, b.CenterX, b.CenterY, Main);
            Wire(b);
            _mains.Add(b);
            _root.Children.Add(b);
        }

        _hub.Children.Add(Bubble.Disc(_hubFill));
        _hub.Children.Add(Bubble.Symbol(Glyphs.Back, _hubInk, 13));
        Place(_hub, C, C, Hub);
        _hub.MouseEnter += (_, _) => HoverHub();
        _hub.MouseLeave += (_, _) => _hubFill.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(Bubble.Rest, TimeSpan.FromMilliseconds(110)));
        _hub.MouseLeftButtonUp += (_, _) => GoBack();
        _root.Children.Add(_hub);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(_pillTitle);
        words.Children.Add(_pillSub);
        _pill.Child = words;
        _pill.CornerRadius = new CornerRadius(13);
        _pill.Padding = new Thickness(12, 5, 12, 6);
        _pill.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF7));
        _pill.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = 0.24 };
        _pill.IsHitTestVisible = false;
        _pill.Opacity = 0;
        _pill.SizeChanged += (_, _) =>
        {
            Canvas.SetLeft(_pill, C - _pill.ActualWidth / 2);
            Canvas.SetTop(_pill, C - _pill.ActualHeight / 2);
        };
        _root.Children.Add(_pill);
    }

    private static void Place(FrameworkElement e, double cx, double cy, double size)
    {
        Canvas.SetLeft(e, cx - size / 2);
        Canvas.SetTop(e, cy - size / 2);
    }

    private void Wire(Bubble b)
    {
        b.MouseEnter += (_, _) => OnEnter(b);
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; Run(b.Target); };
    }

    // ---- hover ------------------------------------------------------------------------------

    private void OnEnter(Bubble b)
    {
        if (_hovered == b) return;
        _hovered?.Hover(false);
        _hovered = b;
        b.Hover(true);
        _haptics.Play(_prefs.Hover);
        Describe(b.Target);

        if (_sats.Contains(b)) return;
        _hoverIntent.Stop();
        if (HasChoices(b.Target))
        {
            if (_openParent == b) return;
            _pendingParent = b;
            _hoverIntent.Start();
        }
        else
        {
            CloseSatellites();
        }
    }

    private void HoverHub()
    {
        _hoverIntent.Stop();
        _hovered?.Hover(false);
        _hovered = null;
        CloseSatellites();
        _hubFill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Bubble.Lit, TimeSpan.FromMilliseconds(110)));
        var back = _spaces.Previous;
        if (back != null) ShowPill("Back", Friendly(back));
        else HidePill();
    }

    private static bool HasChoices(Target t) => t.Kind == Kind.Folder || (t.Kind == Kind.Smart && Config.Current.Spaces.Count > 1);

    private static List<Target> ChoicesOf(Target t) => t.Kind == Kind.Folder ? t.Children! : Config.Current.Choices(t);

    private void LayOutSatellites(Bubble parent, bool animate)
    {
        var items = ChoicesOf(parent.Target);
        double step = items.Count > 2 ? 27 : 24;
        double centre = Math.Atan2(parent.CenterY - C, parent.CenterX - C) * 180 / Math.PI;
        double start = centre - step * (items.Count - 1) / 2;

        for (int i = 0; i < items.Count; i++)
        {
            var s = new Bubble(items[i], Sat);
            double a = (start + i * step) * Math.PI / 180;
            s.CenterX = C + R2 * Math.Cos(a);
            s.CenterY = C + R2 * Math.Sin(a);
            Place(s, s.CenterX, s.CenterY, Sat);
            Wire(s);
            _sats.Add(s);
            _satLayer.Children.Add(s);
            if (animate) Pop(s, i * 12, 0.82);
        }
    }

    private void OpenSatellites(Bubble? parent)
    {
        if (parent == null || !_open) return;
        CloseSatellites();
        _openParent = parent;
        LayOutSatellites(parent, true);
    }

    private void CloseSatellites()
    {
        _satLayer.Children.Clear();
        _sats.Clear();
        _openParent = null;
    }

    private static void Pop(FrameworkElement e, int delayMs, double from)
    {
        var scale = new ScaleTransform(from, from);
        e.RenderTransform = scale;
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        e.Opacity = 0;
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(120))) { BeginTime = begin });
        var up = new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(220))) { BeginTime = begin, EasingFunction = ease };
        up.Completed += (_, _) =>
        {
            // Hand the transform back to the bubble so its own hover scale keeps working.
            if (e is Bubble b)
            {
                e.BeginAnimation(OpacityProperty, null);
                e.Opacity = 1;
                b.RestoreScale();
            }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, up);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, up);
    }

    // ---- the small label --------------------------------------------------------------------

    private void ShowPill(string title, string sub)
    {
        _pillTitle.Text = title;
        _pillSub.Text = sub;
        _pillSub.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _pill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(90)));
    }

    private void HidePill() =>
        _pill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(90)));

    private (string title, string sub) Words(Target t)
    {
        string title = t.Short ?? t.Name;
        string sub = t.Kind switch
        {
            Kind.Smart => (Smart.IsSwitch(t, _front) ? "Switch to " : "") + Smart.Resolve(t, _front, _spaces.LastSpace).Name,
            Kind.Page => Config.Current.SpaceOf(t).Name,
            _ => ""
        };
        if (t.Id == "vscode")
        {
            int n = _chats.Count, need = _chats.NeedYou;
            sub = need > 0 ? $"{need} needs you" : n > 0 ? $"{n} finished" : "";
        }
        return (title, sub);
    }

    private void Describe(Target t)
    {
        var (title, sub) = Words(t);
        ShowPill(title, sub);
    }

    private static string Friendly(Front f) => f.Process.ToLowerInvariant() switch
    {
        "code" => "VS Code",
        "chrome" => f.Space?.Name ?? "Chrome",
        "slack" => "Slack",
        "whatsapp.root" => "WhatsApp",
        "zoom" => "Zoom",
        _ => f.Process
    };

    private void Refresh()
    {
        foreach (var b in _mains)
        {
            var t = b.Target;
            if (t.Id == "vscode") b.CountBadge(_chats.Count, _chats.NeedYou > 0);
            else if (t.Kind == Kind.Smart) b.SpaceBadge(Config.Current.Spaces.Count > 1 ? Smart.Resolve(t, _front, _spaces.LastSpace) : null);
        }
    }

    // ---- open and close ---------------------------------------------------------------------

    /// <summary>Runs the whole opening once off-screen, so the first real press is as fast as the hundredth.</summary>
    public void Warm()
    {
        _front = _spaces.Snapshot();
        _open = true;
        Refresh();
        Pop(_hub, 0, 0.9);
        foreach (var b in _mains) Pop(b, 0, 0.82);
        OpenSatellites(_mains.FirstOrDefault(b => b.Target.Kind == Kind.Folder));
        Describe(_mains[0].Target);
        _root.UpdateLayout();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            CloseSatellites();
            _pill.BeginAnimation(OpacityProperty, null);
            _pill.Opacity = 0;
            _hovered = null;
            _open = false;
        });
    }

    public void Toggle()
    {
        if (_open) Close2();
        else OpenRing();
    }

    private void OpenRing()
    {
        _front = _spaces.Snapshot();
        _open = true;
        _hovered = null;
        foreach (var b in _mains) b.Hover(false);
        CloseSatellites();
        _pill.BeginAnimation(OpacityProperty, null);
        _pill.Opacity = 0;
        Refresh();
        MoveToCursor();

        _root.BeginAnimation(OpacityProperty, null);
        _root.Opacity = 1;
        Pop(_hub, 0, 0.9);
        for (int i = 0; i < _mains.Count; i++) Pop(_mains[i], 8 + i * 9, 0.82);
        InstallHooks();
    }

    private void Close2()
    {
        if (!_open) return;
        _open = false;
        _hoverIntent.Stop();
        RemoveHooks();
        var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(90)));
        fade.Completed += (_, _) =>
        {
            if (!_open) Native.SetWindowPos(Handle, Native.HWND_TOPMOST, -10000, -10000, 0, 0, 1u | Native.SWP_NOACTIVATE);
        };
        _root.BeginAnimation(OpacityProperty, fade);
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    private void MoveToCursor()
    {
        Native.GetCursorPos(out var p);
        var mon = Native.MonitorFromPoint(p, 2);
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(mon, ref info);
        Native.GetDpiForMonitor(mon, 0, out var dpi, out _);
        _scale = dpi / 96.0;
        int w = (int)Math.Round(Dim * _scale);
        var work = info.rcWork;
        _ox = Math.Clamp(p.X - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        _oy = Math.Clamp(p.Y - w / 2, work.Top, Math.Max(work.Top, work.Bottom - w));
        // Twice: the first move can cross onto a monitor with another scale, which resizes the window.
        for (int i = 0; i < 2; i++)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, _ox, _oy, w, w, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    // ---- run a target -----------------------------------------------------------------------

    private void Run(Target t)
    {
        if (!_open) return;
        if (t.Kind == Kind.Folder) { OpenSatellites(_mains.First(b => b.Target == t)); return; }

        _haptics.Play(_prefs.Click);
        var front = _front;
        Close2();
        Execute(t, front);
    }

    public void Execute(Target t, Front front)
    {
        switch (t.Kind)
        {
            case Kind.Smart:
                Launcher.OpenPage(t.Url!, Smart.Resolve(t, front, _spaces.LastSpace));
                break;
            case Kind.Page:
                Launcher.OpenPage(t.Url!, Config.Current.SpaceOf(t));
                break;
            case Kind.App:
                Launcher.OpenApp(t);
                break;
            case Kind.Dictate:
                _ = Launcher.DictateAsync(120, t.Keys);
                break;
        }
    }

    private void GoBack()
    {
        var back = _spaces.Previous;
        Close2();
        if (back != null) Native.Focus(back.Hwnd);
    }

    public void GoById(string id)
    {
        var t = Config.Current.Find(id);
        if (t != null) Execute(t, _spaces.Snapshot());
    }

    // ---- input while open -------------------------------------------------------------------

    private void InstallHooks()
    {
        _mouseProc = (code, w, l) =>
        {
            if (code >= 0 && ((int)w == Native.WM_LBUTTONDOWN || (int)w == Native.WM_RBUTTONDOWN))
            {
                var m = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(l);
                double dx = (m.pt.X - _ox) / _scale - C, dy = (m.pt.Y - _oy) / _scale - C;
                if (Math.Sqrt(dx * dx + dy * dy) > Reach || (int)w == Native.WM_RBUTTONDOWN)
                    Dispatcher.BeginInvoke(Close2);
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, w, l);
        };
        _keyProc = (code, w, l) =>
        {
            if (code >= 0 && (int)w == Native.WM_KEYDOWN && Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(l).vkCode == Native.VK_ESCAPE)
                Dispatcher.BeginInvoke(Close2);
            return Native.CallNextHookEx(IntPtr.Zero, code, w, l);
        };
        var mod = Native.GetModuleHandle(null);
        _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, mod, 0);
        _keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyProc, mod, 0);
    }

    private void RemoveHooks()
    {
        if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyHook);
        _mouseHook = _keyHook = IntPtr.Zero;
    }

    // ---- window plumbing --------------------------------------------------------------------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = Handle;
        var style = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW));
        var source = HwndSource.FromHwnd(h)!;
        source.AddHook(WndProc);
        uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT | Native.MOD_NOREPEAT;
        Native.RegisterHotKey(h, HotkeyRing, mods, Native.VK_F12);
        Native.RegisterHotKey(h, HotkeyDictate, mods, Native.VK_F11);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == Native.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(Native.MA_NOACTIVATE);
        }
        if (msg == Native.WM_HOTKEY)
        {
            handled = true;
            if (w.ToInt32() == HotkeyRing) Toggle();
            else _ = Launcher.DictateAsync(220, Config.Current.Buttons.FirstOrDefault(b => b.Kind == Kind.Dictate)?.Keys);   // waits for the shortcut to let go
        }
        return IntPtr.Zero;
    }

    // ---- snapshot for design review, renders without opening a window ------------------------

    public void Snapshot(string path, string? hoverId, Front front, int chats, int need, Color backdrop)
    {
        _front = front;
        _open = true;
        foreach (var b in _mains)
        {
            if (b.Target.Id == "vscode") b.CountBadge(chats, need > 0);
            else if (b.Target.Kind == Kind.Smart) b.SpaceBadge(Config.Current.Spaces.Count > 1 ? Smart.Resolve(b.Target, front, _spaces.LastSpace) : null);
        }

        if (hoverId != null)
        {
            var parent = _mains.First(b => b.Target.Id == hoverId);
            parent.HoverNow();
            var (title, sub) = Words(parent.Target);
            _pillTitle.Text = title;
            _pillSub.Text = sub;
            _pillSub.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _pill.Opacity = 1;
            if (HasChoices(parent.Target)) LayOutSatellites(parent, false);
        }

        var page = new Grid { Width = 560, Height = 480, Background = new SolidColorBrush(backdrop) };
        Content = null;
        _root.HorizontalAlignment = HorizontalAlignment.Center;
        _root.VerticalAlignment = VerticalAlignment.Center;
        page.Children.Add(_root);
        page.Measure(new Size(560, 480));
        page.Arrange(new Rect(0, 0, 560, 480));
        page.UpdateLayout();
        var bmp = new RenderTargetBitmap(1120, 960, 192, 192, PixelFormats.Pbgra32);
        bmp.Render(page);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
