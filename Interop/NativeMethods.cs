using System.Runtime.InteropServices;

namespace MiniPlayer.Interop;

/// <summary>Thin P/Invoke layer for taskbar discovery and no-activate window placement.</summary>
internal static class NativeMethods
{
    // ----- Window styles -----
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_EX_TOOLWINDOW = 0x00000080;

    // ----- SetWindowPos -----
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    // ----- GetSystemMetrics (virtual screen bounds, physical pixels) -----
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    // ----- Taskbar (app bar) -----
    public const uint ABM_GETTASKBARPOS = 0x00000005;
    public const uint ABM_GETSTATE = 0x00000004;
    public const int ABS_AUTOHIDE = 0x00000001;
    public const int ABE_LEFT = 0;
    public const int ABE_TOP = 1;
    public const int ABE_RIGHT = 2;
    public const int ABE_BOTTOM = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    /// <summary>One detected taskbar (primary or secondary-monitor), with its live window rect.</summary>
    public readonly record struct TaskbarInfo(IntPtr Hwnd, RECT Rect, int Edge, string MonitorDevice, bool IsPrimary);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    /// <summary>Reads the primary taskbar's screen rectangle (physical pixels) and its docked edge.</summary>
    public static bool TryGetTaskbar(out RECT rect, out int edge)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        IntPtr result = SHAppBarMessage(ABM_GETTASKBARPOS, ref data);
        rect = data.rc;
        edge = (int)data.uEdge;
        return result != IntPtr.Zero && rect.Width > 0 && rect.Height > 0;
    }

    public static bool IsTaskbarAutoHide()
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        IntPtr state = SHAppBarMessage(ABM_GETSTATE, ref data);
        return ((int)state & ABS_AUTOHIDE) != 0;
    }

    /// <summary>
    /// Enumerates every taskbar (the primary "Shell_TrayWnd" plus one
    /// "Shell_SecondaryTrayWnd" per additional monitor), reading each one's *live*
    /// window rect directly — unlike <see cref="TryGetTaskbar"/>, this reflects the
    /// taskbar's real animated position, including mid-slide during auto-hide.
    /// </summary>
    public static bool TryGetAllTaskbars(out List<TaskbarInfo> taskbars)
    {
        taskbars = new List<TaskbarInfo>();

        IntPtr primaryHwnd = FindWindow("Shell_TrayWnd", null);
        if (primaryHwnd != IntPtr.Zero && TryBuildTaskbarInfo(primaryHwnd, isPrimary: true, out var primary))
            taskbars.Add(primary);

        var secondaryHandles = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            var sb = new System.Text.StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            if (sb.ToString() == "Shell_SecondaryTrayWnd") secondaryHandles.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        foreach (var hwnd in secondaryHandles)
        {
            if (TryBuildTaskbarInfo(hwnd, isPrimary: false, out var info)) taskbars.Add(info);
        }

        return taskbars.Count > 0;
    }

    private static bool TryBuildTaskbarInfo(IntPtr hwnd, bool isPrimary, out TaskbarInfo info)
    {
        info = default;
        if (!GetWindowRect(hwnd, out var rect)) return false;

        var monitorRect = rect;
        string device = string.Empty;

        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(monitor, ref mi))
            {
                monitorRect = mi.rcMonitor;
                device = mi.szDevice;
            }
        }

        info = new TaskbarInfo(hwnd, rect, InferEdge(rect, monitorRect), device, isPrimary);
        return true;
    }

    /// <summary>Infers the docked edge by seeing which side of its monitor the taskbar rect hugs.</summary>
    private static int InferEdge(RECT taskbar, RECT monitor)
    {
        if (taskbar.Width >= taskbar.Height)
        {
            int distTop = Math.Abs(taskbar.Top - monitor.Top);
            int distBottom = Math.Abs(taskbar.Bottom - monitor.Bottom);
            return distTop <= distBottom ? ABE_TOP : ABE_BOTTOM;
        }
        else
        {
            int distLeft = Math.Abs(taskbar.Left - monitor.Left);
            int distRight = Math.Abs(taskbar.Right - monitor.Right);
            return distLeft <= distRight ? ABE_LEFT : ABE_RIGHT;
        }
    }
}
