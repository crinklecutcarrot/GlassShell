using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GlassShell;

internal sealed class DockWindow : ShellWindow
{
    readonly ShellController owner; readonly StackPanel apps = new() { Orientation = Orientation.Horizontal };
    public DockPreviewWindow Preview { get; }
    uint callback; bool registered, positioning; public bool Revealed { get; private set; } = true;
    public DockWindow(ShellController shell) : base("GlassShell · Dock", 300, 68)
    {
        owner = shell; Preview = new(shell); Glass.TintAmount = .62; var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
        var start = DockButton(TablerIcon.Create("brand-windows", 24), "Start", () => Native.Shortcut(0x5B, false)); row.Children.Add(start);
        var divider = new Border { Width = 1, Height = 34, Margin = new Thickness(7, 0, 7, 0), Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)) }; row.Children.Add(divider); row.Children.Add(apps); Glass.Content.Children.Add(row);
        Loaded += (_, _) => { Register(); Rebuild(); };
    }
    Button DockButton(object content, string tip, Action action)
    {
        var b = Ui.Button("", action, 48, 48); b.Content = content; b.ToolTip = tip; b.Margin = new Thickness(2); b.Padding = new Thickness(8); b.Background = Brushes.Transparent; return b;
    }
    public void Rebuild()
    {
        apps.Children.Clear(); foreach (var app in owner.DockApps.Apps)
        {
            var icon = app.Icon != null ? new Image { Source = app.Icon, Width = 30, Height = 30, Stretch = Stretch.Uniform } : TablerIcon.Create("apps", 25);
            var indicator = new Border { Height = 3, Width = app.Windows.Count > 1 ? 18 : 7, CornerRadius = new CornerRadius(2), Background = app.Windows.Count > 0 ? Ui.WindowsAccent : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            var content = new StackPanel(); content.Children.Add(icon); content.Children.Add(indicator); var button = DockButton(content, app.Name, () => Open(app));
            button.ContextMenu = Menu(app); button.MouseEnter += (_, _) => { if (app.Windows.Count > 0) Preview.Open(app, this); }; button.MouseLeave += (_, _) => Preview.ScheduleDismiss(); apps.Children.Add(button);
        }
        Width = 78 + apps.Children.Count * 52; Height = 68; PositionSurface();
    }
    ContextMenu Menu(DockApp app)
    {
        var menu = new ContextMenu(); foreach (var h in app.Windows) { var item = new MenuItem { Header = Native.Title(h) }; item.Click += (_, _) => { Native.ShowWindow(h, 9); Native.SetForegroundWindow(h); }; menu.Items.Add(item); }
        if (app.Windows.Count > 0) menu.Items.Add(new Separator()); var pin = new MenuItem { Header = app.Pinned ? "Unpin from dock" : "Pin to dock" }; pin.Click += (_, _) => owner.DockApps.TogglePin(app); menu.Items.Add(pin);
        if (app.Pinned) { var left = new MenuItem { Header = "Move left" }; left.Click += (_, _) => owner.DockApps.Move(app, -1); var right = new MenuItem { Header = "Move right" }; right.Click += (_, _) => owner.DockApps.Move(app, 1); menu.Items.Add(left); menu.Items.Add(right); }
        return menu;
    }
    void Open(DockApp app) { if (app.Windows.Count <= 1) owner.DockApps.Activate(app); else { var button = apps.Children.OfType<Button>().FirstOrDefault(x => Equals(x.ToolTip, app.Name)); if (button?.ContextMenu != null) { button.ContextMenu.PlacementTarget = button; button.ContextMenu.IsOpen = true; } } }
    public void SetRevealed(bool show)
    {
        if (Revealed == show) return; Revealed = show; if (show) { Show(); Register(); PositionSurface(); } else { Preview.Dismiss(); Unregister(); Hide(); }
    }
    public override void PositionSurface()
    {
        if (Handle == IntPtr.Zero) return; Left = (Monitor.Left + Monitor.Width / 2.0) / Scale - Width / 2; Top = Monitor.Bottom / Scale - Height - 8; Shape(0, 0, Width, Height, 24); if (registered) Reserve();
    }
    Native.AppBarData Data() => new() { Size = (uint)Marshal.SizeOf<Native.AppBarData>(), Hwnd = Handle, Callback = callback, Edge = 3 };
    void Register() { if (registered || Handle == IntPtr.Zero) return; callback = Native.RegisterWindowMessage("GlassShell.Dock.AppBar.v1"); var d = Data(); registered = Native.SHAppBarMessage(0, ref d) != UIntPtr.Zero; if (registered) Reserve(); }
    void Reserve() { if (!registered || positioning) return; positioning = true; try { var d = Data(); d.Rect = Monitor; d.Rect.Top = d.Rect.Bottom - (int)Math.Ceiling(76 * Scale); Native.SHAppBarMessage(2, ref d); d.Rect.Top = d.Rect.Bottom - (int)Math.Ceiling(76 * Scale); Native.SHAppBarMessage(3, ref d); } finally { positioning = false; } }
    public void Unregister() { if (!registered) return; registered = false; var d = Data(); Native.SHAppBarMessage(1, ref d); }
    protected override IntPtr WndProc(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) { if (callback != 0 && msg == callback && wp.ToInt32() == 1 && !positioning) Dispatcher.BeginInvoke(new Action(Reserve)); return base.WndProc(h, msg, wp, lp, ref handled); }
}

internal sealed class DockPreviewWindow : ShellWindow
{
    readonly ShellController owner; readonly System.Windows.Threading.DispatcherTimer dismiss = new() { Interval = TimeSpan.FromMilliseconds(320) };
    public DockPreviewWindow(ShellController shell) : base("GlassShell · Window previews", 270, 80) { owner = shell; Glass.TintAmount = .7; dismiss.Tick += (_, _) => { dismiss.Stop(); if (!IsMouseOver && !owner.Dock.IsMouseOver) Dismiss(); }; MouseEnter += (_, _) => dismiss.Stop(); MouseLeave += (_, _) => ScheduleDismiss(); }
    public void Open(DockApp app, DockWindow dock)
    {
        Glass.Content.Children.Clear(); var list = new StackPanel { Margin = new Thickness(8) }; foreach (var h in app.Windows)
        {
            var title = Native.Title(h); var button = Ui.Button(title.Length > 0 ? title : app.Name, () => { Native.ShowWindow(h, 9); Native.SetForegroundWindow(h); Dismiss(); }, 254, 42); button.HorizontalContentAlignment = HorizontalAlignment.Left; list.Children.Add(button);
        }
        Glass.Content.Children.Add(list); Width = 270; Height = 16 + app.Windows.Count * 48; Left = dock.Left + dock.Width / 2 - Width / 2; Top = dock.Top - Height - 8; Shape(0, 0, Width, Height, 18); Show();
    }
    public void ScheduleDismiss() { dismiss.Stop(); dismiss.Start(); }
    public void Dismiss() { dismiss.Stop(); Hide(); }
    public override void PositionSurface() { }
}
