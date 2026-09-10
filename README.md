# Windows MiniPlayer

A tiny always-on-top bar that floats just above the Windows 11 taskbar and shows
whatever is currently playing through the system media controls — Spotify, Edge,
Chrome, Firefox, VLC, Groove, and anything else that shows up in the volume
flyout's media popup.

Windows 11 has no supported way to put content *inside* the taskbar, so this is a
separate borderless window that tracks the taskbar's position and pins itself to
its leading edge.

![Windows MiniPlayer sitting above the taskbar](docs/screenshot.png)

<sub>Preview mockup — swap in a real screenshot when you publish.</sub>

## Features

- Album art + title + artist, updated live as the track changes
- Click the bar (or the middle button) to play/pause
- Prev / play-pause / next buttons fade in on hover
- Mouse wheel over the bar = previous / next track
- Right-click → **Start with Windows** (per-user `HKCU\...\Run` entry), or **Exit**
- Follows the taskbar across resolution / DPI / taskbar-position changes
- Never takes focus (`WS_EX_NOACTIVATE`), hidden from Alt-Tab, not in the taskbar
- Collapses to nothing when no media session is active

## Privacy

Runs entirely locally. It reads the current media-session metadata through the
Windows `GlobalSystemMediaTransportControlsSessionManager` API and writes one
optional registry value (the "Start with Windows" entry). **No network calls, no
telemetry, no analytics.**

## Requirements

- Windows 10 1809+ or Windows 11
- To run: [.NET Desktop Runtime 8](https://dotnet.microsoft.com/download/dotnet/8.0)
  (or grab a self-contained build from Releases, which bundles it)
- To build: .NET 8 SDK — `winget install Microsoft.DotNet.SDK.8`

## Build & run

```powershell
dotnet build
dotnet run
```

### Self-contained single file

```powershell
dotnet publish -c Release -p:PublishSingleFile=true -p:SelfContained=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

The result is one `MiniPlayer.exe` under
`bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`. Copy it anywhere and
make a shortcut, or use the right-click **Start with Windows** toggle.

For a much smaller binary that needs the .NET Desktop Runtime 8 installed, drop
`-p:SelfContained=true` (and the compression flag).

## How it works

| File | Role |
| --- | --- |
| [`Services/MediaService.cs`](Services/MediaService.cs) | Wraps `GlobalSystemMediaTransportControlsSessionManager`; raises a `NowPlaying` snapshot on any track/playback change (debounced) |
| [`Services/TaskbarTracker.cs`](Services/TaskbarTracker.cs) | Reads the taskbar rect via `SHAppBarMessage(ABM_GETTASKBARPOS)`, positions the window in physical pixels with `SetWindowPos`, re-checks on a timer + display-change events |
| [`Services/StartupManager.cs`](Services/StartupManager.cs) | Toggles the `HKCU` Run-key entry |
| [`Interop/NativeMethods.cs`](Interop/NativeMethods.cs) | P/Invoke declarations |
| [`MainWindow.xaml`](MainWindow.xaml) / [`.cs`](MainWindow.xaml.cs) | The bar UI and event wiring |

## Known limitations

- Docks to the **primary** taskbar only; secondary-monitor taskbars are ignored
- When the taskbar is set to auto-hide, the bar stays at the taskbar's normal
  position rather than sliding with it
- Fullscreen apps (games, video) cover the bar — expected for a topmost window
- Tuning constants (`BarWidth`, `BarHeight`, `LeadingMargin`) live in
  [`TaskbarTracker.cs`](Services/TaskbarTracker.cs); there is no settings UI yet

## Why not *inside* the taskbar?

Windows 10 supported "deskbands" (COM toolbars hosted in the taskbar). Windows 11's
rewritten taskbar dropped that API, and there is no supported replacement.
Getting truly inside the Win11 taskbar today means either a third-party taskbar
replacement (ExplorerPatcher, StartAllBack, Start11) that restores deskband
hosting, or unsupported `SetParent` reparenting that breaks on every Explorer
restart. A tracked floating bar is the trade-off this project makes.

## License

[MIT](LICENSE) © Javier Boix
