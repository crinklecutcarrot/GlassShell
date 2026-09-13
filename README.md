# GlassShell

A Windows desktop prototype with a glass top status bar. The custom dock and Dynamic Island have been removed. Target display: 4K, 150% scaling, 60 Hz, SDR.

## Run

On Windows, clone the repository and run:

```powershell
.\Setup-Sdk.ps1
.\Start-GlassShell.ps1
```

The setup script downloads the pinned .NET SDK from Microsoft's release feed and verifies its SHA-512 checksum. Subsequent launches only need `Start-GlassShell.ps1`. Exit with Ctrl+Alt+Esc, the top-right session menu, or the Windows tray menu. Ctrl+Alt+Space toggles Widgets. No startup registration or administrator rights are required.

## Current behavior

- Left: Widgets, Timer, and Notes. Each opens a dropdown beneath its button. Widgets is a placeholder for future widgets.
- Center: music appears while playing and remains while paused. Album artwork, song/artist, and previous/play-pause/next controls are shown. Clicking the player surface opens a larger menu with progress and the same controls. Seeking is enabled only when the media session supports it. Chrome sessions are preferred.
- An active timer appears after music, separated by a divider with 12-DIP spacing. A circular progress indicator surrounds the timer icon. At zero, a small Time’s Up popup opens and the timer continues into negative overtime until Stop. Dismissing that popup leaves the timer running; click the bar timer to reopen it.
- The bar smoothly grows from 36 to 56 DIP for activities. An AppBar reserves that space for normal maximized windows; reservation returns to 36 DIP when activities end. Dropdowns overlay applications.
- Outside clicks dismiss dropdowns and continue to the underlying application. Notes use atomic local autosave and save before dismissal. Stored at `%LOCALAPPDATA%/GlassShell/quick-note.txt`. Notion sync is not implemented. Timers are session-only.
- Bundled DM Sans with -0.01em tracking and Tabler vector icons, available offline.
- Native notification banners remain. Notification history and background apps panels currently link to Windows; they are not replacement implementations. Control Center provides volume actions and settings links.
- Fullscreen application detection hides the shell. HDR/exclusive games have not been tested.

## Native taskbar

The Windows taskbar remains available. A proposed Windhawk DockLike configuration is in `config/`; no Explorer styling has been installed or applied. Do not hide the native tray until its functionality is hosted elsewhere. See `config/README.md` for details and sources.

## Material

A custom HLSL edge-refraction shader, background blur, and rounded clipping approximate liquid glass. This is not Apple's renderer. Desktop capture currently uses background GDI capture and CPU-to-GPU uploads, capped at 30 captures per second with idle backoff. WPF animation runs independently. Sustained GPU performance and HDR color management remain unverified.

Live-glass surfaces exclude themselves from screen capture to avoid recursive sampling, so they may be absent from screenshots or screen sharing. Turning live glass off in the top-right session menu switches to fallback material and removes that exclusion for existing windows.

## Development and checks

`Start-GlassShell.ps1` uses the local SDK in `.tools/dotnet`; `Setup-Sdk.ps1` recreates it from Microsoft's verified download.

```powershell
& .\.tools\dotnet\dotnet.exe build .\src\GlassShell\GlassShell.csproj -c Release
& .\.tools\dotnet\dotnet.exe run --project .\tests\GlassShell.Tests\GlassShell.Tests.csproj -c Release
# Close the running app first. Tests use isolated note storage and their own click target.
& .\.tools\dotnet\dotnet.exe .\src\GlassShell\bin\Release\net10.0-windows10.0.19041.0\GlassShell.dll --ui-regression-test
& .\.tools\dotnet\dotnet.exe publish .\src\GlassShell\GlassShell.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\app
```

Model checks cover overtime, pause/resume, reset, invalid durations, spring interruption, and rounded hit testing. UI checks cover idle/playing/paused states, actual mouse-hook propagation and player clicks, popup placement/dismissal, note autosave, expiry/reopening/stop, and work-area release. Results and screenshots are in `artifacts/ui-regression/`. Media tests use synthetic sessions; live YouTube Music playback and seeking need validation with an active Chrome session.

`StatusBar.cs` owns bar content, animation, and AppBar reservation; `PanelWindow.cs` owns dropdowns and the timer alert; `Model.cs` owns timer state; `MediaService.cs` owns Windows media sessions; `Glass.cs` owns material/capture; `ShellWindow.cs` owns native window setup.

## Activity refinements

Music retains its last presentation through up to three seconds of missing/stopped metadata during track changes. Thumbnail bytes are checked independently on each media refresh, so artwork arriving after a title change can replace the prior image. Now Playing supports click-and-drag seeking when the session permits it; seek failures are logged. Center modules show hover, pressed, and open-menu highlights.

The fixed-width Active Timer module opens details with a visual progress bar, Pause/Resume, +1 min, and Cancel. Adding time increases both remaining time and total duration; paused timers stay paused. Expired icons turn red and the Time’s Up popup has reduced bottom spacing. Only the bottom edge of the top bar receives a stroke/highlight.

Center activity cards use equal horizontal/vertical insets and animate opacity and position as the bar's height spring expands or collapses. Dropdowns prepare and lay out their complete glass surface while invisible, then animate the material instead of the native window opacity; this avoids presenting a stale or empty HWND frame on open.

Windows does not provide a supported API for a third-party shell to enumerate and re-host notification icons owned by other applications. `Shell_NotifyIcon` lets each owner add or update its own icon and receive its own callbacks; it does not expose a consumer-side tray feed. A complete top-bar tray therefore requires an unsupported Explorer/application hook or cooperation from every tray application. GlassShell keeps the native tray reachable until that architectural choice is made.
