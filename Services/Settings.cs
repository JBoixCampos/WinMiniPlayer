using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniPlayer.Services;

public enum BarPosition
{
    /// <summary>Floating in the gap just outside the taskbar's leading edge (default).</summary>
    AboveTaskbar,

    /// <summary>Overlapping the taskbar itself, near its leading corner.</summary>
    OnTaskbar,

    /// <summary>A user-chosen spot from dragging the bar.</summary>
    Custom,
}

/// <summary>User preferences, persisted to <c>%APPDATA%\WindowsMiniPlayer\settings.json</c>.</summary>
public sealed class Settings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsMiniPlayer");

    private static string FilePath => Path.Combine(Directory, "settings.json");

    public BarPosition Position { get; set; } = BarPosition.AboveTaskbar;
    public bool Draggable { get; set; }

    /// <summary>Last dragged location, in physical screen pixels. Only meaningful when <see cref="Position"/> is Custom.</summary>
    public int CustomX { get; set; }
    public int CustomY { get; set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // Corrupt or unreadable file — fall back to defaults.
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Best effort; a failed save just means the choice isn't remembered.
        }
    }
}
