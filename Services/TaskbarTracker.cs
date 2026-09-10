using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using MiniPlayer.Interop;

namespace MiniPlayer.Services;

/// <summary>
/// Places a borderless window relative to the primary taskbar and keeps it there
/// across resolution / DPI / taskbar-position changes. Supports three modes:
/// floating just outside the taskbar edge, overlapping the taskbar near its
/// leading corner, or a fixed user-dragged location.
/// </summary>
public sealed class TaskbarTracker : IDisposable
{
    private readonly Window _window;
    private readonly DispatcherTimer _timer;
    private IntPtr _hwnd;
    private bool _started;

    private bool _dragging;
    private NativeMethods.POINT _dragCursorStart;
    private NativeMethods.RECT _dragWindowStart;

    /// <summary>Bar size in device-independent pixels (scaled per-monitor at runtime).</summary>
    public double BarWidth { get; set; } = 300;
    public double BarHeight { get; set; } = 44;
    public double LeadingMargin { get; set; } = 8;
    public double GapFromTaskbar { get; set; } = 4;

    public BarPosition Position { get; set; } = BarPosition.AboveTaskbar;

    /// <summary>Target location in physical screen pixels; used only when <see cref="Position"/> is Custom.</summary>
    public (int X, int Y)? CustomLocation { get; set; }

    public TaskbarTracker(Window window)
    {
        _window = window;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Reposition();
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        _hwnd = new WindowInteropHelper(_window).EnsureHandle();
        ApplyNoActivateStyles();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Reposition();
        _timer.Start();
    }

    /// <summary>Switch position mode (and custom location) and re-place immediately.</summary>
    public void Apply(BarPosition position, (int X, int Y)? customLocation)
    {
        Position = position;
        CustomLocation = customLocation;
        Reposition();
    }

    private void ApplyNoActivateStyles()
    {
        long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Reposition();

    public void Reposition()
    {
        if (_hwnd == IntPtr.Zero || _dragging) return;

        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;

        bool onBar = Position == BarPosition.OnTaskbar;
        bool hasTaskbar = NativeMethods.TryGetTaskbar(out var tb, out int edge);

        int w = (int)Math.Round(BarWidth * scale);
        int h = (int)Math.Round(BarHeight * scale);

        if (onBar && hasTaskbar && edge is NativeMethods.ABE_TOP or NativeMethods.ABE_BOTTOM)
        {
            // Fit within the taskbar's thickness with a small inset.
            int inset = (int)Math.Round(3 * scale);
            h = Math.Min(h, Math.Max(1, tb.Height - inset * 2));
        }

        // WPF lays out content in DIPs; the OS positions the HWND in physical px.
        _window.Width = w / scale;
        _window.Height = h / scale;

        if (Position == BarPosition.Custom || !hasTaskbar)
        {
            var (cx, cy) = CustomLocation ?? (0, 0);
            (cx, cy) = ClampToVirtualScreen(cx, cy, w, h);
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, cx, cy, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            return;
        }

        int margin = (int)Math.Round(LeadingMargin * scale);
        int gap = (int)Math.Round(GapFromTaskbar * scale);

        (int x, int y) = edge switch
        {
            NativeMethods.ABE_TOP => onBar
                ? (tb.Left + margin, tb.Top + (tb.Height - h) / 2)
                : (tb.Left + margin, tb.Bottom + gap),
            NativeMethods.ABE_LEFT => onBar
                ? (tb.Left + (tb.Width - w) / 2, tb.Top + margin)
                : (tb.Right + gap, tb.Top + margin),
            NativeMethods.ABE_RIGHT => onBar
                ? (tb.Left + (tb.Width - w) / 2, tb.Top + margin)
                : (tb.Left - w - gap, tb.Top + margin),
            _ /* ABE_BOTTOM */ => onBar
                ? (tb.Left + margin, tb.Top + (tb.Height - h) / 2)
                : (tb.Left + margin, tb.Top - h - gap),
        };

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, w, h,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    // ----- Manual drag (driven by the window's mouse handlers) -----

    public void StartDrag()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.GetCursorPos(out _dragCursorStart);
        NativeMethods.GetWindowRect(_hwnd, out _dragWindowStart);
        _dragging = true;
    }

    public void UpdateDrag()
    {
        if (!_dragging) return;
        NativeMethods.GetCursorPos(out var now);
        int x = _dragWindowStart.Left + (now.X - _dragCursorStart.X);
        int y = _dragWindowStart.Top + (now.Y - _dragCursorStart.Y);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Ends a drag and returns the window's final top-left in physical pixels.</summary>
    public (int X, int Y) StopDrag()
    {
        _dragging = false;
        NativeMethods.GetWindowRect(_hwnd, out var r);
        return (r.Left, r.Top);
    }

    private static (int X, int Y) ClampToVirtualScreen(int x, int y, int w, int h)
    {
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        if (vw <= 0 || vh <= 0) return (x, y);

        x = Math.Clamp(x, vx, Math.Max(vx, vx + vw - w));
        y = Math.Clamp(y, vy, Math.Max(vy, vy + vh - h));
        return (x, y);
    }

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }
}
