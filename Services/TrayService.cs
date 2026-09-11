using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MiniPlayer.Services;

/// <summary>
/// A notification-area icon that gives access to the right-click menu (position,
/// draggable, startup, exit) even when the bar itself is hidden — no media
/// playing — or has been dragged off-screen. Wraps
/// <see cref="System.Windows.Forms.NotifyIcon"/> since WPF has no native tray-icon
/// API; all actions are delegated to <see cref="ITrayHost"/> so this menu never
/// drifts out of sync with the bar's own context menu.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _posAboveItem;
    private readonly ToolStripMenuItem _posOnItem;
    private readonly ToolStripMenuItem _draggableItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ITrayHost _host;

    public TrayService(ITrayHost host)
    {
        _host = host;

        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => SyncMenu();

        var positionMenu = new ToolStripMenuItem("Position");
        _posAboveItem = new ToolStripMenuItem("Above taskbar");
        _posAboveItem.Click += (_, _) => _host.SetPositionAboveTaskbar();
        _posOnItem = new ToolStripMenuItem("On taskbar");
        _posOnItem.Click += (_, _) => _host.SetPositionOnTaskbar();
        positionMenu.DropDownItems.Add(_posAboveItem);
        positionMenu.DropDownItems.Add(_posOnItem);

        _draggableItem = new ToolStripMenuItem("Draggable");
        _draggableItem.Click += (_, _) => _host.ToggleDraggable();

        _startupItem = new ToolStripMenuItem("Start with Windows");
        _startupItem.Click += (_, _) => _host.ToggleStartup();

        var resetItem = new ToolStripMenuItem("Reset position");
        resetItem.Click += (_, _) => _host.ResetPosition();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => _host.ExitApp();

        menu.Items.Add(positionMenu);
        menu.Items.Add(_draggableItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Windows MiniPlayer",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => _host.ResetPosition();
    }

    private static Icon LoadAppIcon()
    {
        // Reuse the exe's own embedded icon (set via <ApplicationIcon> in the csproj)
        // rather than shipping/locating icon.ico separately at runtime.
        string exePath = Process.GetCurrentProcess().MainModule!.FileName!;
        return Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application;
    }

    private void SyncMenu()
    {
        _posAboveItem.Checked = _host.IsAboveTaskbar;
        _posOnItem.Checked = _host.IsOnTaskbar;
        _draggableItem.Checked = _host.Draggable;
        _startupItem.Checked = _host.StartupEnabled;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
