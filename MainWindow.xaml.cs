using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MiniPlayer.Interop;
using MiniPlayer.Models;
using MiniPlayer.Services;

namespace MiniPlayer;

public partial class MainWindow : Window, IDisposable, ITrayHost
{
    // Segoe Fluent Icons glyphs
    private const string GlyphPlay = "";
    private const string GlyphPause = "";

    private const double DragThreshold = 4.0;

    // Heights have a floor around 42 (32px artwork + 5+5 padding) below which the artwork clips.
    private static readonly (double Width, double Height) SizeCompact = (250, 42);
    private static readonly (double Width, double Height) SizeDefault = (300, 44);
    private static readonly (double Width, double Height) SizeLarge = (360, 52);

    private readonly MediaService _media = new();
    private readonly TaskbarTracker _tracker;
    private readonly ThemeService _theme = new();
    private readonly Settings _settings = Settings.Load();

    private bool _dragArmed;
    private bool _didDrag;
    private Point _dragOriginScreen;
    private NowPlaying? _current;

    public MainWindow()
    {
        InitializeComponent();
        _tracker = new TaskbarTracker(this);
        _media.Changed += OnMediaChanged;
        _theme.Changed += OnSystemThemeChanged;
        Loaded += OnLoaded;

        DraggableItem.IsChecked = _settings.Draggable;
        SyncPositionMenu();
        SyncSizeMenu();
        SyncThemeMenu();
        UpdateDragCursor();
        ApplyTheme();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _tracker.Position = _settings.Position;
        _tracker.CustomLocation = _settings.Position == BarPosition.Custom
            ? (_settings.CustomX, _settings.CustomY)
            : null;
        _tracker.BarWidth = _settings.BarWidth;
        _tracker.BarHeight = _settings.BarHeight;
        _tracker.MonitorId = _settings.MonitorId;
        _tracker.Start();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartupItem.IsChecked = StartupManager.IsEnabled();
        Render(null);

        try
        {
            await _media.InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MiniPlayer: media init failed: {ex}");
        }
    }

    private void OnMediaChanged(NowPlaying? snapshot) => Dispatcher.Invoke(() => Render(snapshot));

    private void Render(NowPlaying? snapshot)
    {
        _current = snapshot;

        if (snapshot is null || !snapshot.HasText)
        {
            Root.Visibility = Visibility.Collapsed;
            return;
        }

        Root.Visibility = Visibility.Visible;

        TitleText.Text = string.IsNullOrWhiteSpace(snapshot.Title) ? "(unknown track)" : snapshot.Title;
        ArtistText.Text = snapshot.Artist;
        ArtistText.Visibility = string.IsNullOrWhiteSpace(snapshot.Artist)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ArtHost.Background = snapshot.Artwork is null
            ? (Brush)FindResource("ArtworkPlaceholderBrush")
            : new ImageBrush(snapshot.Artwork) { Stretch = Stretch.UniformToFill };

        PlayPauseGlyph.Text = snapshot.IsPlaying ? GlyphPause : GlyphPlay;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_settings.Draggable) return;

        _dragArmed = true;
        _didDrag = false;
        _dragOriginScreen = PointToScreen(e.GetPosition(this));
        _tracker.StartDrag();
        ((IInputElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;

        if (!_didDrag)
        {
            var moved = PointToScreen(e.GetPosition(this)) - _dragOriginScreen;
            if (moved.Length >= DragThreshold) _didDrag = true;
        }

        if (_didDrag) _tracker.UpdateDrag();
    }

    private async void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragArmed)
        {
            _dragArmed = false;
            ((IInputElement)sender).ReleaseMouseCapture();

            var (x, y) = _tracker.StopDrag();
            if (_didDrag)
            {
                _settings.Position = BarPosition.Custom;
                _settings.CustomX = x;
                _settings.CustomY = y;
                _settings.Save();
                _tracker.Apply(BarPosition.Custom, (x, y));
                SyncPositionMenu();
                return; // a drag is not a click
            }
        }

        await _media.PlayPauseAsync();
    }

    private void Root_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // Safety net if capture is stolen mid-drag (e.g. by a system gesture).
        if (_dragArmed)
        {
            _dragArmed = false;
            _tracker.StopDrag();
        }
    }

    private async void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta < 0) await _media.NextAsync();
        else await _media.PreviousAsync();
    }

    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();
    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.PlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();

    private void PosAbove_Click(object sender, RoutedEventArgs e) => SetPosition(BarPosition.AboveTaskbar);

    private void PosOn_Click(object sender, RoutedEventArgs e) => SetPosition(BarPosition.OnTaskbar);

    private void SetPosition(BarPosition position)
    {
        _settings.Position = position;
        _settings.Save();
        _tracker.Apply(position, null);
        SyncPositionMenu();
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e) => RebuildMonitorMenu();

    private void RebuildMonitorMenu()
    {
        MonitorMenu.Items.Clear();

        if (!NativeMethods.TryGetAllTaskbars(out var taskbars) || taskbars.Count <= 1)
        {
            MonitorMenu.Visibility = Visibility.Collapsed;
            return;
        }

        MonitorMenu.Visibility = Visibility.Visible;
        int number = 1;
        foreach (var t in taskbars)
        {
            string label = t.IsPrimary ? "Primary display" : $"Display {++number}";
            string? id = t.IsPrimary ? null : t.MonitorDevice;

            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = string.Equals(_settings.MonitorId, id, StringComparison.Ordinal),
            };
            item.Click += (_, _) => SetMonitor(id);
            MonitorMenu.Items.Add(item);
        }
    }

    private void SetMonitor(string? monitorId)
    {
        _settings.MonitorId = monitorId;
        _settings.Save();
        _tracker.MonitorId = monitorId;
        _tracker.Reposition();
    }

    private void SyncPositionMenu()
    {
        PosAboveItem.IsChecked = _settings.Position == BarPosition.AboveTaskbar;
        PosOnItem.IsChecked = _settings.Position == BarPosition.OnTaskbar;
    }

    private void SizeCompact_Click(object sender, RoutedEventArgs e) => SetSize(SizeCompact);
    private void SizeDefault_Click(object sender, RoutedEventArgs e) => SetSize(SizeDefault);
    private void SizeLarge_Click(object sender, RoutedEventArgs e) => SetSize(SizeLarge);

    private void SetSize((double Width, double Height) size)
    {
        _settings.BarWidth = size.Width;
        _settings.BarHeight = size.Height;
        _settings.Save();
        _tracker.BarWidth = size.Width;
        _tracker.BarHeight = size.Height;
        _tracker.Reposition();
        SyncSizeMenu();
    }

    private void SyncSizeMenu()
    {
        SizeCompactItem.IsChecked = Matches(SizeCompact);
        SizeDefaultItem.IsChecked = Matches(SizeDefault);
        SizeLargeItem.IsChecked = Matches(SizeLarge);

        bool Matches((double Width, double Height) size) =>
            Math.Abs(_settings.BarWidth - size.Width) < 0.5 && Math.Abs(_settings.BarHeight - size.Height) < 0.5;
    }

    private void ThemeSystem_Click(object sender, RoutedEventArgs e) => SetThemeMode(ThemeMode.System);
    private void ThemeLight_Click(object sender, RoutedEventArgs e) => SetThemeMode(ThemeMode.Light);
    private void ThemeDark_Click(object sender, RoutedEventArgs e) => SetThemeMode(ThemeMode.Dark);

    private void SetThemeMode(ThemeMode mode)
    {
        _settings.ThemeMode = mode;
        _settings.Save();
        SyncThemeMenu();
        ApplyTheme();
    }

    private void SyncThemeMenu()
    {
        ThemeSystemItem.IsChecked = _settings.ThemeMode == ThemeMode.System;
        ThemeLightItem.IsChecked = _settings.ThemeMode == ThemeMode.Light;
        ThemeDarkItem.IsChecked = _settings.ThemeMode == ThemeMode.Dark;
    }

    private void OnSystemThemeChanged()
    {
        if (_settings.ThemeMode == ThemeMode.System) Dispatcher.Invoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        bool light = _settings.ThemeMode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => _theme.IsLightTheme,
        };

        SetBrush("BarBackgroundBrush", "#E61F1F1F", "#E6F3F3F3", light);
        SetBrush("BarBorderBrush", "#26FFFFFF", "#26000000", light);
        SetBrush("TitleForegroundBrush", "#FFFFFFFF", "#FF1F1F1F", light);
        SetBrush("ArtistForegroundBrush", "#9EFFFFFF", "#9E1F1F1F", light);
        SetBrush("ArtworkPlaceholderBrush", "#22FFFFFF", "#22000000", light);
        SetBrush("TransportForegroundBrush", "#F2FFFFFF", "#F21F1F1F", light);
        SetBrush("TransportHoverBrush", "#28FFFFFF", "#28000000", light);
        SetBrush("TransportPressedBrush", "#40FFFFFF", "#40000000", light);

        // The placeholder brush may be showing right now; refresh it in place.
        if (_current?.Artwork is null) ArtHost.Background = (Brush)FindResource("ArtworkPlaceholderBrush");
    }

    private void SetBrush(string key, string darkHex, string lightHex, bool light)
    {
        var color = (Color)ColorConverter.ConvertFromString(light ? lightHex : darkHex)!;
        Resources[key] = new SolidColorBrush(color);
    }

    private void Draggable_Click(object sender, RoutedEventArgs e)
    {
        _settings.Draggable = DraggableItem.IsChecked;
        _settings.Save();
        UpdateDragCursor();
    }

    private void UpdateDragCursor() => Root.Cursor = _settings.Draggable ? Cursors.SizeAll : Cursors.Arrow;

    private void StartupItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupManager.SetEnabled(StartupItem.IsChecked);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MiniPlayer: startup toggle failed: {ex}");
            StartupItem.IsChecked = StartupManager.IsEnabled();
        }
    }

    private void ExitItem_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // ----- ITrayHost: lets the tray icon's menu drive the same state as this menu -----

    bool ITrayHost.IsAboveTaskbar => _settings.Position == BarPosition.AboveTaskbar;
    bool ITrayHost.IsOnTaskbar => _settings.Position == BarPosition.OnTaskbar;
    bool ITrayHost.Draggable => _settings.Draggable;
    bool ITrayHost.StartupEnabled => StartupManager.IsEnabled();

    void ITrayHost.SetPositionAboveTaskbar() => SetPosition(BarPosition.AboveTaskbar);
    void ITrayHost.SetPositionOnTaskbar() => SetPosition(BarPosition.OnTaskbar);

    void ITrayHost.ToggleDraggable()
    {
        _settings.Draggable = !_settings.Draggable;
        _settings.Save();
        DraggableItem.IsChecked = _settings.Draggable;
        UpdateDragCursor();
    }

    void ITrayHost.ToggleStartup()
    {
        try
        {
            StartupManager.SetEnabled(!StartupManager.IsEnabled());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MiniPlayer: startup toggle failed: {ex}");
        }
        StartupItem.IsChecked = StartupManager.IsEnabled();
    }

    void ITrayHost.ResetPosition()
    {
        _settings.Position = BarPosition.AboveTaskbar;
        _settings.MonitorId = null;
        _settings.Save();
        _tracker.MonitorId = null;
        _tracker.Apply(BarPosition.AboveTaskbar, null);
        SyncPositionMenu();
    }

    void ITrayHost.ExitApp() => Application.Current.Shutdown();

    public void Dispose()
    {
        _media.Changed -= OnMediaChanged;
        _theme.Changed -= OnSystemThemeChanged;
        _media.Dispose();
        _tracker.Dispose();
        _theme.Dispose();
    }
}
