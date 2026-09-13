# Native taskbar styling

The requested content-hugging taskbar can use Windhawk's Windows 11 Taskbar Styler and its built-in DockLike theme. This retains Explorer's pinned/running app buttons, thumbnail previews, and native behavior. Acrylic styling does not reproduce the custom refraction shader used in our top bar.

Nothing has been installed or applied. Windhawk was not found on this machine. The top bar's background-apps panel currently opens the existing Windows tray; it does not host third-party tray icons. Hiding the entire native tray now would remove that access.

This cannot be completed through the documented Windows notification-area API alone. `Shell_NotifyIcon` is producer-oriented: the application that owns an icon registers its image, identifier, owner window, and callback message with Explorer. Windows has no documented API for GlassShell to subscribe to all registrations or impersonate their mouse callbacks. A real replacement would need a separate native interception component injected into Explorer and/or tray-owning processes, with per-Windows-build maintenance and a fallback recovery path.

When ready, install Windhawk and Windows 11 Taskbar Styler, select DockLike, and center the taskbar in Windows settings. Hide Search, Task View, and Widgets through taskbar settings if only Start and application icons are wanted. DockLike alone retains the tray. The adjacent `native-taskbar-icons-only.yaml` is the additional proposed configuration to import in the mod's Settings → Textual mode once tray access has a replacement. Disable the mod to restore Explorer's original appearance. This proposal has not been tested on this Windows build.

Sources checked September 13, 2026:
- https://github.com/ramensoftware/windows-11-taskbar-styling-guide/blob/main/Themes/DockLike/README.md
- https://github.com/ramensoftware/windows-11-taskbar-styling-guide
