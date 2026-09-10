using System.IO;
using System.Windows.Media.Imaging;
using MiniPlayer.Models;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MiniPlayer.Services;

/// <summary>
/// Watches the Windows "System Media Transport Controls" session that the volume
/// flyout uses, and raises <see cref="Changed"/> whenever the track, artwork, or
/// play state moves. Works for any app that reports media (Spotify, Edge, Chrome,
/// VLC, ...). Events may arrive on a thread-pool thread; subscribers marshal.
/// </summary>
public sealed class MediaService : IDisposable
{
    public event Action<NowPlaying?>? Changed;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private CancellationTokenSource? _debounce;
    private bool _disposed;

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => HookCurrentSession();
        HookCurrentSession();
    }

    private void HookCurrentSession()
    {
        if (_disposed) return;

        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
        }

        _session = _manager?.GetCurrentSession();

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnSessionChanged;
            _session.PlaybackInfoChanged += OnSessionChanged;
        }

        ScheduleRefresh();
    }

    private void OnSessionChanged(object? sender, object args) => ScheduleRefresh();

    /// <summary>Coalesce bursts of change events into a single refresh.</summary>
    private void ScheduleRefresh()
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, token);
                await RefreshAsync(token);
            }
            catch (OperationCanceledException) { /* superseded */ }
            catch (Exception) { /* session vanished mid-read; next event recovers */ }
        }, token);
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        var session = _session;
        if (session is null)
        {
            Changed?.Invoke(null);
            return;
        }

        var props = await session.TryGetMediaPropertiesAsync();
        token.ThrowIfCancellationRequested();
        var playback = session.GetPlaybackInfo();

        var artwork = await LoadArtworkAsync(props.Thumbnail, token);

        var snapshot = new NowPlaying(
            Title: props.Title ?? string.Empty,
            Artist: string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumTitle ?? string.Empty : props.Artist,
            Artwork: artwork,
            IsPlaying: playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            SourceAppId: session.SourceAppUserModelId ?? string.Empty);

        Changed?.Invoke(snapshot);
    }

    private static async Task<BitmapImage?> LoadArtworkAsync(IRandomAccessStreamReference? thumbnail, CancellationToken token)
    {
        if (thumbnail is null) return null;

        using var randomStream = await thumbnail.OpenReadAsync();
        token.ThrowIfCancellationRequested();
        if (randomStream.Size == 0) return null;

        using var netStream = randomStream.AsStreamForRead();
        using var buffer = new MemoryStream();
        await netStream.CopyToAsync(buffer, token);
        buffer.Position = 0;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = buffer;
        bitmap.DecodePixelHeight = 64; // artwork is displayed small; decode small
        bitmap.EndInit();
        bitmap.Freeze(); // usable from the UI thread
        return bitmap;
    }

    // ----- Transport controls -----
    public Task PlayPauseAsync() => Wrap(_session?.TryTogglePlayPauseAsync());
    public Task NextAsync() => Wrap(_session?.TrySkipNextAsync());
    public Task PreviousAsync() => Wrap(_session?.TrySkipPreviousAsync());

    private static async Task Wrap(IAsyncOperation<bool>? op)
    {
        if (op is null) return;
        try { await op; } catch { /* app rejected the command */ }
    }

    public void Dispose()
    {
        _disposed = true;
        _debounce?.Cancel();
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
        }
    }
}
