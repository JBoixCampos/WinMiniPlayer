using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MiniPlayer.Models;
using MiniPlayer.Services;

namespace MiniPlayer;

public partial class MainWindow : Window, IDisposable
{
    // Segoe Fluent Icons glyphs
    private const string GlyphPlay = "";
    private const string GlyphPause = "";

    private const double DragThreshold = 4.0;

    private readonly MediaService _media = new();
    private readonly TaskbarTracker _tracker;
    private readonly Brush _artworkPlaceholder;
    private readonly Settings _settings = Settings.Load();

    private bool _dragArmed;
    private bool _didDrag;
    private Point _dragOriginScreen;

    public MainWindow()
    {
        InitializeComponent();
        _artworkPlaceholder = ArtHost.Background;
        _tracker = new TaskbarTracker(this);
        _media.Changed += OnMediaChanged;
        Loaded += OnLoaded;

        DraggableItem.IsChecked = _settings.Draggable;
        SyncPositionMenu();
        UpdateDragCursor();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _tracker.Position = _settings.Position;
        _tracker.CustomLocation = _settings.Position == BarPosition.Custom
            ? (_settings.CustomX, _settings.CustomY)
            : null;
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
            ? _artworkPlaceholder
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

    private void SyncPositionMenu()
    {
        PosAboveItem.IsChecked = _settings.Position == BarPosition.AboveTaskbar;
        PosOnItem.IsChecked = _settings.Position == BarPosition.OnTaskbar;
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

    public void Dispose()
    {
        _media.Changed -= OnMediaChanged;
        _media.Dispose();
        _tracker.Dispose();
    }
}
