using System.Windows.Media.Imaging;

namespace MiniPlayer.Models;

/// <summary>
/// Immutable snapshot of the current system media session, as surfaced by
/// <see cref="Windows.Media.Control.GlobalSystemMediaTransportControlsSession"/>.
/// </summary>
public sealed record NowPlaying(
    string Title,
    string Artist,
    BitmapImage? Artwork,
    bool IsPlaying,
    string SourceAppId)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist);
}
