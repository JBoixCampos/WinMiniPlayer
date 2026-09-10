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

    private readonly MediaService _media = new();
    private readonly TaskbarTracker _tracker;
    private readonly Brush _artworkPlaceholder;

    public MainWindow()
    {
        InitializeComponent();
        _artworkPlaceholder = ArtHost.Background;
        _tracker = new TaskbarTracker(this);
        _media.Changed += OnMediaChanged;
        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
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

    private async void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => await _media.PlayPauseAsync();

    private async void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta < 0) await _media.NextAsync();
        else await _media.PreviousAsync();
    }

    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();
    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.PlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();

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
