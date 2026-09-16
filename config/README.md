# Native taskbar styling

The requested content-hugging taskbar can use Windhawk's Windows 11 Taskbar Styler and its built-in DockLike theme. This retains Explorer's pinned/running app buttons, thumbnail previews, and native behavior. Acrylic styling does not reproduce the custom refraction shader used in our top bar.

Windhawk and Windows 11 Taskbar Styler are installed on the development machine. The DockLike theme is active, with the individual tray controls transparent and non-interactive to remove Explorer's tray and clock from the bottom taskbar. Their measured layout stays alive because collapsing the controls breaks DockLike's auto-width calculation on Windows build 26200. The native app buttons, previews, jump lists, and running indicators remain owned by Explorer.

This cannot be completed through the documented Windows notification-area API alone. `Shell_NotifyIcon` is producer-oriented: the application that owns an icon registers its image, identifier, owner window, and callback message with Explorer. GlassShell's `native/TrayHook` component uses a thread-specific `WH_CALLWNDPROC` hook inside Explorer to observe its private `WM_COPYDATA` payload and sends a bounded copy to the WPF process. This needs per-Windows-build maintenance and a fallback recovery path.

`tools/Apply-NativeTaskbar.ps1` applies the theme and tray-hiding rules and restarts Explorer. Run it from an elevated PowerShell prompt. Its first run saves the previous mod registry configuration to `%LOCALAPPDATA%\GlassShell\windhawk-taskbar-styler-backup.reg`; run the script with `-Restore` to put that configuration back. Search, Task View, and Widgets can still be shown or hidden through Windows taskbar settings.

Sources checked September 13, 2026:
- https://github.com/ramensoftware/windows-11-taskbar-styling-guide/blob/main/Themes/DockLike/README.md
- https://github.com/ramensoftware/windows-11-taskbar-styling-guide
