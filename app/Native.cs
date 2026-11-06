using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Thumbring;

internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    public const int WM_HOTKEY = 0x312, WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000;
    public const uint VK_F11 = 0x7A, VK_F12 = 0x7B, VK_ESCAPE = 0x1B;
    public const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public const int SW_RESTORE = 9;
    public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x100, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204;
    public const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; public uint pad1, pad2; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public KEYBDINPUT ki; }

    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
    [DllImport("user32")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("shcore")] public static extern int GetDpiForMonitor(IntPtr m, int type, out uint x, out uint y);
    [DllImport("user32")] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32")] public static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32")] public static extern void SwitchToThisWindow(IntPtr h, bool altTab);
    [DllImport("user32")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32")] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("kernel32")] public static extern uint GetCurrentThreadId();
    [DllImport("user32")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    // ---- which application does a window belong to ------------------------------------------

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint c);
        int GetAt(uint i, out PropertyKey k);
        int GetValue(ref PropertyKey k, out PropVariant v);
        int SetValue(ref PropertyKey k, ref PropVariant v);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct PropertyKey { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr ptr; }

    [DllImport("shell32")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    /// <summary>Chrome stamps each window with an id per profile: "Chrome" or "Chrome.UserData.Profile7".</summary>
    public static string? AppUserModelId(IntPtr hwnd)
    {
        try
        {
            var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0) return null;
            var key = new PropertyKey { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
            store.GetValue(ref key, out var value);
            var id = value.vt == 31 ? Marshal.PtrToStringUni(value.ptr) : null;
            Marshal.ReleaseComObject(store);
            return id;
        }
        catch { return null; }
    }

    public static string Title(IntPtr h)
    {
        int n = GetWindowTextLength(h);
        if (n <= 0) return "";
        var s = new StringBuilder(n + 2);
        GetWindowText(h, s, s.Capacity);
        return s.ToString();
    }

    public static string ProcessName(IntPtr h)
    {
        try
        {
            GetWindowThreadProcessId(h, out var pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    /// <summary>Visible titled top-level windows of a process, most recently used first.</summary>
    public static List<IntPtr> WindowsOf(string processName)
    {
        var found = new List<IntPtr>();
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) && GetWindowTextLength(h) > 0 &&
                GetWindow(h, 4) == IntPtr.Zero &&
                string.Equals(ProcessName(h), processName, StringComparison.OrdinalIgnoreCase))
                found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Windows only hands the foreground to the process with the last input. Try the honest route first.</summary>
    public static bool Focus(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        if (SetForegroundWindow(h) && Settled(h)) return true;

        var fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        AttachThreadInput(me, fgThread, true);
        BringWindowToTop(h);
        SetForegroundWindow(h);
        AttachThreadInput(me, fgThread, false);
        if (Settled(h)) return true;

        SwitchToThisWindow(h, true);
        if (Settled(h)) return true;

        // Last resort: a bare Alt tap counts as input and lifts the foreground lock.
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        System.Threading.Thread.Sleep(20);
        SetForegroundWindow(h);
        return Settled(h);
    }

    private static bool Settled(IntPtr h)
    {
        for (int i = 0; i < 8; i++)
        {
            if (GetForegroundWindow() == h) return true;
            System.Threading.Thread.Sleep(10);
        }
        return false;
    }

    public static void SendKeys(IReadOnlyList<int> vks)
    {
        // Right-hand modifiers need the extended flag or a hook reads them as left-hand.
        static uint Flags(int vk) => vk is 163 or 165 or 91 or 92 ? 1u : 0u;
        var inputs = new List<INPUT>();
        foreach (var vk in vks)
            inputs.Add(Key(vk, Flags(vk)));
        for (int i = vks.Count - 1; i >= 0; i--)
            inputs.Add(Key(vks[i], Flags(vks[i]) | 2u));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(int vk, uint flags) => new()
    {
        type = 1,
        ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)MapVirtualKey((uint)vk, 0), dwFlags = flags }
    };
}
