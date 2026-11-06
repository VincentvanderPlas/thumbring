using System;

namespace Thumbring;

/// <summary>What was in front at one moment.</summary>
internal sealed record Front(IntPtr Hwnd, string Process, string Title, Space? Space);

/// <summary>
/// Watches the foreground window. Remembers the Chrome space used last and the window before this one.
/// After a restart the last space is the configured start space until a Chrome space has been used.
/// </summary>
internal sealed class Spaces
{
    private readonly Native.WinEventProc _callback;
    private Front? _previous;

    public Space LastSpace { get; private set; } = Config.Current.Start;
    public Front? Current { get; private set; }
    public Front? Previous => _previous is { } p && Native.IsWindow(p.Hwnd) ? _previous : null;
    public event Action<Front>? Changed;

    public Spaces()
    {
        _callback = (_, _, hwnd, _, _, _, _) => Update(hwnd);
        Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        Update(Native.GetForegroundWindow());
    }

    public static Space? SpaceOf(IntPtr hwnd) => Config.Current.ByWindowId(Native.AppUserModelId(hwnd));

    /// <summary>A fresh read at the moment the ring opens: titles change without a focus change.</summary>
    public Front Snapshot()
    {
        var hwnd = Native.GetForegroundWindow();
        return new Front(hwnd, Native.ProcessName(hwnd), Native.Title(hwnd), SpaceOf(hwnd));
    }

    private void Update(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var process = Native.ProcessName(hwnd);
        if (process.Equals("Thumbring", StringComparison.OrdinalIgnoreCase)) return;
        if (process.Equals("explorer", StringComparison.OrdinalIgnoreCase) && Native.Title(hwnd).Length == 0) return;

        var front = new Front(hwnd, process, Native.Title(hwnd), SpaceOf(hwnd));
        if (Current is { } c && c.Hwnd != hwnd) _previous = c;
        Current = front;
        if (front.Space is { } s) LastSpace = s;
        Changed?.Invoke(front);
    }
}

/// <summary>The rules behind a smart button.</summary>
internal static class Smart
{
    public static Space Resolve(Target t, Front front, Space lastSpace)
    {
        // Already on this page: go to the same page in the next space.
        if (IsSwitch(t, front)) return Config.Current.Next(front.Space!);

        // A button can be told to always start in one space.
        if (t.Prefer != null) return t.Prefer;

        // Working in a Chrome space: stay in it.
        if (front.Space is { } s) return s;

        // Coming from another application: the Chrome space used last.
        return lastSpace;
    }

    public static bool IsSwitch(Target t, Front front) =>
        front.Space != null && t.OnPage != null && t.OnPage.IsMatch(front.Title);
}
