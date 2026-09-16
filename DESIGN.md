# Current desktop UI design

The September 13 redesign replaces the custom bottom dock and floating island with one top status bar and the native Windows taskbar.

- Left: Widgets drawer, Timer, Notes. No Glass label or brand icon.
- Center is empty without activities. Music remains visible while paused. Music appears first, then a divider with 12 DIP on either side, then an active timer.
- Music shows artwork, title, artist and transport buttons. Its hoverable surface opens a dropdown with progress, seeking when supported, and transport controls.
- Timer displays an icon inside a decreasing circular progress ring and a live countdown. At zero it keeps counting negative; Time’s Up appears below with Stop. Outside dismissal does not cancel the timer. Notes must not be displaced by expiry.
- Right: background apps, control center, notifications, clock/date, session menu.
- Idle bar 36 DIP; activity bar 56 DIP. A stable 56-DIP host prevents capture reallocations during height animation. Work-area reservation grows before activity presentation and shrinks after collapse.
- Dropdowns appear below their anchors and dismiss on outside click without swallowing the click. Notes save atomically. Shared rounded glass clips prevent square-corner flashes. Content and optical layers remain separate.
- DM Sans with -0.01em tracking and Tabler icons throughout.
- Native taskbar retains pinned/running apps, jump lists, and previews. Windhawk's DockLike theme makes the Explorer taskbar hug the application buttons in a fully rounded dock with a 6-DIP gap above the screen edge. Individual tray controls are transparent and non-interactive while their measured layout stays alive, which preserves DockLike's auto-width calculation on Windows build 26200. The top bar opens Explorer's native overflow when tray access is needed. Connected notification history remains future work. Native notification banners remain enabled.

Target: one 3840×2160 display, 150% scaling, 60 Hz, RTX 3090, mostly SDR, YouTube Music in Chrome. The shader is an approximation; GPU-native capture and HDR color management remain future work. Widgets and Notion integration remain unfinished.
