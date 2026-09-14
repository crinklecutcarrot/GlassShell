# Native taskbar styling

The requested content-hugging taskbar can use Windhawk's Windows 11 Taskbar Styler and its built-in DockLike theme. This retains Explorer's pinned/running app buttons, thumbnail previews, and native behavior. Acrylic styling does not reproduce the custom refraction shader used in our top bar.

Nothing has been installed or applied to the Windows taskbar. Windhawk was not found on this machine. The top bar now has an experimental mirror for third-party `Shell_NotifyIcon` registrations, but Explorer's tray remains visible while coverage and recovery are validated.

This cannot be completed through the documented Windows notification-area API alone. `Shell_NotifyIcon` is producer-oriented: the application that owns an icon registers its image, identifier, owner window, and callback message with Explorer. GlassShell's `native/TrayHook` component uses a thread-specific `WH_CALLWNDPROC` hook inside Explorer to observe its private `WM_COPYDATA` payload and sends a bounded copy to the WPF process. This needs per-Windows-build maintenance and a fallback recovery path.

When ready, install Windhawk and Windows 11 Taskbar Styler, select DockLike, and center the taskbar in Windows settings. Hide Search, Task View, and Widgets through taskbar settings if only Start and application icons are wanted. DockLike alone retains the tray. The adjacent `native-taskbar-icons-only.yaml` is the additional proposed configuration to import in the mod's Settings → Textual mode once tray access has a replacement. Disable the mod to restore Explorer's original appearance. This proposal has not been tested on this Windows build.

Sources checked September 13, 2026:
- https://github.com/ramensoftware/windows-11-taskbar-styling-guide/blob/main/Themes/DockLike/README.md
- https://github.com/ramensoftware/windows-11-taskbar-styling-guide
