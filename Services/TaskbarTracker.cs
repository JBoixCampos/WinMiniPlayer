using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using MiniPlayer.Interop;

namespace MiniPlayer.Services;

/// <summary>
/// Keeps a borderless window pinned just outside the primary taskbar, on the
/// same edge the taskbar is docked to, aligned to its leading corner. Re-checks
/// on a slow timer plus display-change events so it survives resolution changes,
/// taskbar moves, and DPI changes.
/// </summary>
public sealed class TaskbarTracker : IDisposable
{
    private readonly Window _window;
    private readonly DispatcherTimer _timer;
    private IntPtr _hwnd;
    private bool _started;

    /// <summary>Bar size in device-independent pixels (scaled per-monitor at runtime).</summary>
    public double BarWidth { get; set; } = 300;
    public double BarHeight { get; set; } = 44;
    public double LeadingMargin { get; set; } = 8;
    public double GapFromTaskbar { get; set; } = 4;

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

    private void ApplyNoActivateStyles()
    {
        long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Reposition();

    public void Reposition()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!NativeMethods.TryGetTaskbar(out var tb, out int edge)) return;

        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;

        // WPF lays out content in DIPs; the OS positions the HWND in physical px.
        _window.Width = BarWidth;
        _window.Height = BarHeight;

        int w = (int)Math.Round(BarWidth * scale);
        int h = (int)Math.Round(BarHeight * scale);
        int margin = (int)Math.Round(LeadingMargin * scale);
        int gap = (int)Math.Round(GapFromTaskbar * scale);

        (int x, int y) = edge switch
        {
            NativeMethods.ABE_TOP   => (tb.Left + margin, tb.Bottom + gap),
            NativeMethods.ABE_LEFT  => (tb.Right + gap, tb.Top + margin),
            NativeMethods.ABE_RIGHT => (tb.Left - w - gap, tb.Top + margin),
            _ /* ABE_BOTTOM */      => (tb.Left + margin, tb.Top - h - gap),
        };

        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            x, y, w, h,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }
}
