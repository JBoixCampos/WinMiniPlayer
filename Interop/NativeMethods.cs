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
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

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
    public struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

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
}
