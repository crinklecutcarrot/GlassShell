using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace GlassShell;

internal sealed class PanelWindow : ShellWindow
{
    readonly ShellController owner; string current = ""; TextBox? note; TextBlock? live, elapsed, total, musicTitle, musicArtist, radioState; Slider? seek; RoundedImage? art; StackPanel? musicLabels, connectivityBody; Button? play, pause, prev, next, like; ToggleButton? radioToggle; ProgressBar? timerProgress; Button? addMinute; bool dragging; bool? lastPlaying, lastRunning, lastLiked; int displayedTrack = -1, targetTrack = -1, trackAnimation; double panelHeight = 410;
    public string CurrentPage => current; public bool IsOpen { get; private set; }
    public int PresentationVersion { get; private set; }
    public PanelWindow(ShellController controller) : base("GlassShell · Panel", 420, 410) { owner = controller; Glass.TintAmount = .66; }
    public void Open(string page)
    {
        if (page == "tray") owner.Tray.RefreshBackfill();
        if (IsOpen && current == page) { Dismiss(); return; }
        if (!SaveNote()) return;
        PresentationVersion++;
        IsOpen = true;
        IsHitTestVisible = true;
        current = page;

        // Prepare the complete frame while both the HWND and material are invisible.
        // Fading the window itself made DWM briefly present its old backing surface.
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Glass.BeginAnimation(OpacityProperty, null);
        Glass.Opacity = 0;
        Build();
        PositionSurface();
        UpdateLayout();
        Tick();
        if (!IsVisible) Show();
        Opacity = 1;

        var translate = Glass.RenderTransform as TranslateTransform ?? new TranslateTransform();
        Glass.RenderTransform = translate;
        translate.Y = -8;
        Glass.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(210)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        SetInput(page == "notes");
        if (note != null) note.Focus();
    }
    public void Navigate(string page)
    {
        if (!IsOpen) { Open(page); return; }
        if (!SaveNote()) return;
        current = page; PresentationVersion++; Build(); PositionSurface(); UpdateLayout(); Tick();
        if (Glass.Content.Children.Count > 0 && Glass.Content.Children[0] is FrameworkElement content)
        {
            var move = new TranslateTransform(12, 0); content.RenderTransform = move; content.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease });
        }
    }
    public bool SaveNote() { if (note == null) return true; try { Storage.Write("quick-note.txt", note.Text); return true; } catch (Exception e) { Storage.Log("Save note: " + e); if (live != null) live.Text = "Could not save. Your note is still open."; return false; } }
    public void Dismiss() { if (!IsOpen || !SaveNote()) return; SetInput(false); IsOpen = false; IsHitTestVisible = false; int version = ++PresentationVersion; var fade = new DoubleAnimation(Glass.Opacity, 0, TimeSpan.FromMilliseconds(150)); fade.Completed += (_, _) => { if (version == PresentationVersion && !IsOpen) { Opacity = 0; Hide(); } }; Glass.BeginAnimation(OpacityProperty, fade); if (Glass.RenderTransform is TranslateTransform translate) translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -5, TimeSpan.FromMilliseconds(150))); }
    public void HideImmediately() { if (!SaveNote()) return; IsOpen = false; PresentationVersion++; SetInput(false); Glass.BeginAnimation(OpacityProperty, null); Glass.Opacity = 0; BeginAnimation(OpacityProperty, null); Opacity = 0; Hide(); }
    public void RefreshTray() { if (IsOpen && current == "tray") { Build(); PositionSurface(); UpdateLayout(); } }
    public void RefreshConnectivity() { if (IsOpen && connectivityBody != null && current is "controls" or "wifi" or "bluetooth") { UpdateRadioHeader(); PopulateConnectivity(); } }
    public override void PositionSurface() { double anchor = owner.Bar.AnchorCenter(current is "wifi" or "bluetooth" ? "controls" : current); Left = Math.Clamp(anchor - Width / 2, owner.Bar.Left + 8, owner.Bar.Left + owner.Bar.Width - Width - 8); Top = owner.Bar.Top + owner.Bar.VisualHeight + 8; Shape(0, 0, Width, panelHeight, 25); }
    void Build()
    {
        var trayIcons = current == "tray" ? owner.Tray.Icons : Array.Empty<TrayIconItem>();
        panelHeight = current switch { "active-timer" => 250, "timer" => 280, "widgets" => 160, "music" => 330, "notifications" => 260, "controls" or "wifi" or "bluetooth" => 400, "tray" => Math.Clamp(135 + Math.Ceiling(trayIcons.Count / 7.0) * 48, 190, 410), _ => 410 }; Height = panelHeight; Glass.Content.Children.Clear(); lastPlaying = lastRunning = lastLiked = null; displayedTrack = targetTrack = -1; trackAnimation++; note = null; live = elapsed = total = musicTitle = musicArtist = radioState = null; radioToggle = null; timerProgress = null; addMinute = null; seek = null; art = null; musicLabels = connectivityBody = null; play = pause = prev = next = like = null; dragging = false;
        var body = new StackPanel { Margin = new Thickness(24) }; var header = new Grid();
        if (current is "wifi" or "bluetooth") { var back = Ui.Icon("arrow-left", "Back to Control Center", () => Navigate("controls"), 34); back.HorizontalAlignment = HorizontalAlignment.Left; back.Margin = new Thickness(-3, -3, 0, -3); header.Children.Add(back); }
        var heading = Ui.Text(current switch { "music" => "Now playing", "active-timer" => "Active Timer", "timer" => "Timer", "notes" => "Notes", "widgets" => "Widgets", "controls" => "Control Center", "wifi" => "Wi‑Fi", "bluetooth" => "Bluetooth", "notifications" => "Notifications", "tray" => "Background apps", _ => "GlassShell" }, 22, weight: FontWeights.SemiBold); if (current is "wifi" or "bluetooth") heading.Margin = new Thickness(44, 0, 0, 0); header.Children.Add(heading); var close = Ui.Icon("x", "Close", Dismiss, 30); close.HorizontalAlignment = HorizontalAlignment.Right; Panel.SetZIndex(close, 10); header.Children.Add(close);
        if (current is "wifi" or "bluetooth") { bool wifi = current == "wifi"; radioState = Ui.Text("", 12, Ui.Muted); radioToggle = Ui.Toggle(false, () => _ = owner.Connectivity.ToggleRadio(wifi ? Windows.Devices.Radios.RadioKind.WiFi : Windows.Devices.Radios.RadioKind.Bluetooth)); var state = Ui.Row(radioState, radioToggle); radioState.Margin = new Thickness(0, 0, 8, 0); state.HorizontalAlignment = HorizontalAlignment.Right; state.Margin = new Thickness(0, 0, 40, 0); header.Children.Add(state); UpdateRadioHeader(); }
        body.Children.Add(header); body.Children.Add(new Border { Height = 18 });
        switch (current)
        {
            case "active-timer":
                live = Ui.Text("", 36, weight: FontWeights.SemiBold); body.Children.Add(live);
                timerProgress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 4, Margin = new Thickness(0, 16, 0, 20), IsHitTestVisible = false, Focusable = false, Foreground = Ui.Accent, Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), BorderThickness = new Thickness(0) };
                body.Children.Add(timerProgress);
                pause = Ui.Button("Pause", () => owner.Model.PauseResume(), 110, 40);
                addMinute = Ui.Button("+1 min", () => owner.Model.AddMinute(), 110, 40);
                body.Children.Add(Ui.Row(pause, addMinute, Ui.Button("Cancel", () => { owner.Model.ResetTimer(); Dismiss(); }, 110, 40))); break;
            case "timer":
                live = Ui.Text("", 36, weight: FontWeights.SemiBold); body.Children.Add(live); body.Children.Add(new Border { Height = 18 });
                var presets = new StackPanel { Orientation = Orientation.Horizontal }; foreach (int minutes in new[] { 5, 15, 25, 45 }) presets.Children.Add(Ui.Button(minutes + " min", () => owner.Model.StartTimer(TimeSpan.FromMinutes(minutes)), 80, 40)); body.Children.Add(presets);
                pause = Ui.Button("Pause", () => owner.Model.PauseResume(), 170, 40); body.Children.Add(Ui.Row(pause, Ui.Button("Stop", () => owner.Model.ResetTimer(), 170, 40))); break;
            case "notes":
                note = new TextBox { Text = Storage.Read("quick-note.txt"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 248, FontFamily = Ui.Font, FontSize = 15, Foreground = Ui.White, Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), BorderThickness = new Thickness(0), Padding = new Thickness(12), CaretBrush = Ui.White }; note.TextChanged += (_, _) => SaveNote(); body.Children.Add(note); live = Ui.Text("Saved on this device", 11, Ui.Muted); live.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(live); break;
            case "music":
                art = new RoundedImage(72, 72, 12); musicTitle = Ui.Text("", 18, weight: FontWeights.SemiBold); musicArtist = Ui.Text("", 13, Ui.Muted); musicLabels = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; musicLabels.Children.Add(musicTitle); musicLabels.Children.Add(musicArtist); like = Ui.Icon("heart", "Connect the YouTube Music extension", owner.Media.ToggleLike, 40); like.Margin = new Thickness(0); like.Background = Brushes.Transparent;
                var track = new Grid(); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); track.ColumnDefinitions.Add(new ColumnDefinition()); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); Grid.SetColumn(art, 0); Grid.SetColumn(musicLabels, 2); Grid.SetColumn(like, 3); track.Children.Add(art); track.Children.Add(musicLabels); track.Children.Add(like); body.Children.Add(track); body.Children.Add(new Border { Height = 24 });
                seek = new Slider { Minimum = 0, Maximum = 1, Height = 24, IsMoveToPointEnabled = false, Foreground = Ui.Accent }; StyleSeek(seek);
                // Own pointer capture across the whole rail so handled thumb events
                // cannot swallow release, and clicks and drags use the same path.
                seek.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler((_, e) => { if (!seek.IsEnabled) return; dragging = true; seek.CaptureMouse(); UpdateSeek(e.GetPosition(seek).X); e.Handled = true; }), true);
                seek.PreviewMouseMove += (_, e) => { if (dragging) { UpdateSeek(e.GetPosition(seek).X); e.Handled = true; } };
                seek.PreviewMouseLeftButtonUp += (_, e) => { if (!dragging) return; UpdateSeek(e.GetPosition(seek).X); dragging = false; seek.ReleaseMouseCapture(); _ = owner.Media.Seek(seek.Value); e.Handled = true; };
                seek.LostMouseCapture += (_, _) => dragging = false;
                body.Children.Add(seek);
                elapsed = Ui.Text("", 11, Ui.Muted); total = Ui.Text("", 11, Ui.Muted); var times = new Grid(); times.Children.Add(elapsed); total.HorizontalAlignment = HorizontalAlignment.Right; times.Children.Add(total); body.Children.Add(times);
                prev = Ui.Icon("player-skip-back", "Previous", () => _ = owner.Media.Control("previous"), 46); play = Ui.Icon("player-play", "Play / pause", () => _ = owner.Media.Control("toggle"), 52); next = Ui.Icon("player-skip-forward", "Next", () => _ = owner.Media.Control("next"), 46); var transport = Ui.Row(prev, play, next); transport.HorizontalAlignment = HorizontalAlignment.Center; transport.Margin = new Thickness(0, 18, 0, 0); body.Children.Add(transport); break;
            case "widgets": body.Children.Add(Ui.Text("Your widgets will live here.", 14, Ui.Muted)); break;
            case "controls":
            case "wifi":
            case "bluetooth": connectivityBody = new StackPanel(); body.Children.Add(connectivityBody); PopulateConnectivity(); break;
            case "notifications": var info = Ui.Text("Notification history isn’t connected yet. Your Windows banners and history remain available.", 14, Ui.Muted); info.TextWrapping = TextWrapping.Wrap; body.Children.Add(info); body.Children.Add(Ui.Button("Open Windows notification history", () => { HideImmediately(); Native.Shortcut(0x4E); }, 350, 44)); break;
            case "tray":
                if (!owner.Tray.Available)
                {
                    var unavailable = Ui.Text("The tray mirror could not attach. Explorer’s tray remains available.", 14, Ui.Muted); unavailable.TextWrapping = TextWrapping.Wrap; body.Children.Add(unavailable);
                    body.Children.Add(Ui.Button("Focus Windows tray", () => { HideImmediately(); Native.Shortcut(0x42); }, 350, 44));
                }
                else if (trayIcons.Count == 0) body.Children.Add(Ui.Text("Listening for background-app icons…", 14, Ui.Muted));
                else
                {
                    body.Children.Add(Ui.Text($"{trayIcons.Count} background app{(trayIcons.Count == 1 ? "" : "s")}", 12, Ui.Muted));
                    var iconGrid = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
                    foreach (var trayIcon in trayIcons)
                    {
                        var button = Ui.Button("", () => owner.Tray.SendAction(trayIcon, "left"), 44, 44);
                        button.Content = trayIcon.Image == null ? TablerIcon.Create("apps", 21) : new Image { Source = trayIcon.Image, Width = 22, Height = 22, Stretch = Stretch.Uniform };
                        button.ToolTip = trayIcon.Tooltip;
                        button.MouseDoubleClick += (_, e) => { owner.Tray.SendAction(trayIcon, "double"); e.Handled = true; };
                        button.PreviewMouseRightButtonUp += (_, e) => { owner.Tray.SendAction(trayIcon, "right"); e.Handled = true; };
                        iconGrid.Children.Add(button);
                    }
                    body.Children.Add(iconGrid);
                }
                break;
            default: body.Children.Add(Ui.Button(owner.LiveGlass ? "Live glass: on" : "Live glass: off", () => { owner.SetLiveGlass(!owner.LiveGlass); Build(); }, 350, 42)); body.Children.Add(Ui.Button("Quit GlassShell", owner.Exit, 350, 42)); body.Children.Add(Ui.Text("Ctrl + Alt + Space    Widgets", 12, Ui.Muted)); body.Children.Add(Ui.Text("Ctrl + Alt + Esc         Quit", 12, Ui.Muted)); break;
        }
        Glass.Content.Children.Add(body);
    }
    void PopulateConnectivity()
    {
        if (connectivityBody == null) return; connectivityBody.Children.Clear(); var c = owner.Connectivity;
        if (current == "controls")
        {
            connectivityBody.Children.Add(Ui.Row(
                ConnectivityTile("wifi", "Wi‑Fi", c.WifiOn ? ConnectedWifiLabel(c) : "Off", () => { Navigate("wifi"); _ = c.RefreshWifi(); }),
                ConnectivityTile("bluetooth", "Bluetooth", c.BluetoothOn ? ConnectedBluetoothLabel(c) : "Off", () => { Navigate("bluetooth"); _ = c.RefreshBluetooth(); })));
            connectivityBody.Children.Add(Ui.Row(Ui.ActionButton("volume", "Sound settings", () => Ui.Open("ms-settings:sound"), 174, 62), Ui.ActionButton("device-desktop", "Display settings", () => Ui.Open("ms-settings:display"), 174, 62)));
            var volume = Ui.Row(Ui.Icon("volume-2", "Volume down", () => Native.Shortcut(0xAE, false), 52), Ui.Icon("volume-off", "Mute", () => Native.Shortcut(0xAD, false), 52), Ui.Icon("volume", "Volume up", () => Native.Shortcut(0xAF, false), 52)); volume.HorizontalAlignment = HorizontalAlignment.Center; volume.Margin = new Thickness(0, 10, 0, 10); connectivityBody.Children.Add(volume);
            connectivityBody.Children.Add(Ui.Button("Open Windows quick settings", () => { HideImmediately(); Native.Shortcut(0x41); }, 350, 42)); return;
        }
        bool wifi = current == "wifi"; bool busy = wifi ? c.WifiBusy : c.BluetoothBusy;
        var list = new StackPanel();
        if (wifi)
        {
            foreach (var network in c.Networks) list.Children.Add(WifiRow(network));
            if (!string.IsNullOrEmpty(c.WifiMessage)) list.Children.Add(Message(c.WifiMessage));
        }
        else
        {
            foreach (var device in c.BluetoothDevices) list.Children.Add(BluetoothRow(device));
            if (!string.IsNullOrEmpty(c.BluetoothMessage)) list.Children.Add(Message(c.BluetoothMessage));
        }
        var scroll = new ScrollViewer { Content = list, Height = 225, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly }; connectivityBody.Children.Add(scroll);
        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var settings = Ui.Button(wifi ? "More Wi‑Fi settings" : "More Bluetooth settings", () => Ui.Open(wifi ? "ms-settings:network-wifi" : "ms-settings:bluetooth"), 285, 38); settings.HorizontalContentAlignment = HorizontalAlignment.Left; settings.Margin = new Thickness(0, 3, 3, 3); footer.Children.Add(settings);
        var refresh = Ui.Icon("refresh", wifi ? "Scan for networks" : "Refresh devices", () => { if (wifi) _ = c.RefreshWifi(); else _ = c.RefreshBluetooth(); }, 38); refresh.IsEnabled = !busy; Grid.SetColumn(refresh, 1); footer.Children.Add(refresh); connectivityBody.Children.Add(footer);
    }
    Button ConnectivityTile(string icon, string title, string status, Action action)
    {
        bool active = icon == "wifi" ? owner.Connectivity.WifiOn : owner.Connectivity.BluetoothOn; var button = Ui.Button("", action, 174, 74); if (active) button.Background = Ui.WindowsAccentSurface; var labels = new StackPanel { Margin = new Thickness(9, 0, 0, 0), Width = 112 }; labels.Children.Add(Ui.Text(title, 14, weight: FontWeights.SemiBold)); labels.Children.Add(Ui.Text(status, 11, active ? Ui.White : Ui.Muted)); button.Content = Ui.Row(TablerIcon.Create(icon, 20), labels); return button;
    }
    void UpdateRadioHeader()
    {
        if (radioState == null || radioToggle == null || current is not ("wifi" or "bluetooth")) return; bool wifi = current == "wifi"; bool on = wifi ? owner.Connectivity.WifiOn : owner.Connectivity.BluetoothOn; bool busy = wifi ? owner.Connectivity.WifiBusy : owner.Connectivity.BluetoothBusy; radioState.Text = on ? "On" : "Off"; radioToggle.IsChecked = on; radioToggle.IsEnabled = !busy; radioToggle.ToolTip = on ? "Turn off" : "Turn on";
    }
    UIElement WifiRow(WifiNetwork network)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = TablerIcon.Create("wifi", 21, network.Connected ? Ui.Accent : Ui.White); icon.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(icon);
        var labels = new StackPanel(); labels.Children.Add(Ui.Text(network.Ssid, 14, weight: network.Connected ? FontWeights.SemiBold : FontWeights.Normal)); labels.Children.Add(Ui.Text(network.Connected ? $"Connected · {network.Signal}%" : network.Secure ? $"Secured · {network.Signal}%" : $"Open · {network.Signal}%", 11, Ui.Muted)); Grid.SetColumn(labels, 1); grid.Children.Add(labels);
        var action = network.Connected ? Ui.Button("Disconnect", () => _ = owner.Connectivity.DisconnectWifi(), 92, 36) : Ui.Button(network.Saved ? "Connect" : "Details", () => _ = owner.Connectivity.ConnectWifi(network.Ssid), 76, 36); Grid.SetColumn(action, 2); grid.Children.Add(action); return Card(grid, new Thickness(12));
    }
    UIElement BluetoothRow(BluetoothItem device)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = TablerIcon.Create("bluetooth", 21, device.Connected ? Ui.Accent : Ui.White); icon.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(icon);
        var labels = new StackPanel(); labels.Children.Add(Ui.Text(device.Name, 14, weight: device.Connected ? FontWeights.SemiBold : FontWeights.Normal)); labels.Children.Add(Ui.Text(device.Connected ? "Connected" : device.Paired ? "Paired" : "Available", 11, Ui.Muted)); Grid.SetColumn(labels, 1); grid.Children.Add(labels);
        var settings = Ui.Icon("external-link", "Manage device in Windows settings", () => Ui.Open("ms-settings:bluetooth"), 36); Grid.SetColumn(settings, 2); grid.Children.Add(settings); return Card(grid, new Thickness(12));
    }
    static Border Card(UIElement child, Thickness padding) => new() { Child = child, Padding = padding, Margin = new Thickness(0, 0, 0, 7), CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), BorderThickness = new Thickness(1) };
    static Border Message(string text) { var label = Ui.Text(text, 13, Ui.Muted); label.TextWrapping = TextWrapping.Wrap; label.TextTrimming = TextTrimming.None; return Card(label, new Thickness(14)); }
    static string ConnectedWifiLabel(ConnectivityService c) => c.Networks.FirstOrDefault(x => x.Connected)?.Ssid ?? (c.WifiBusy ? "Scanning…" : "On");
    static string ConnectedBluetoothLabel(ConnectivityService c) => c.BluetoothDevices.FirstOrDefault(x => x.Connected)?.Name ?? (c.BluetoothBusy ? "Refreshing…" : "On");
    void UpdateSeek(double x) { if (seek != null) seek.Value = Math.Clamp((x - 6) / Math.Max(1, seek.ActualWidth - 12), 0, 1); }
    static void StyleSeek(Slider slider)
    {
        const string template = """
 <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Slider">
 <Grid Height="24" Background="Transparent">
 <Border Height="4" CornerRadius="2" Background="#35FFFFFF" VerticalAlignment="Center"/>
 <Track x:Name="PART_Track" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" IsDirectionReversed="False">
 <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="4" CornerRadius="2" Background="#98D3FF" VerticalAlignment="Center"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
 <Track.Thumb><Thumb Width="12" Height="12"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="#F8F9FD"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
 <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
 </Track></Grid></ControlTemplate>
 """;
        slider.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(template);
    }
    public void Tick() { if (!IsOpen) return; if ((current == "timer" || current == "active-timer") && live != null) { if (timerProgress != null) timerProgress.Value = owner.Model.TimerProgress; if (addMinute != null) addMinute.IsEnabled = owner.Model.TimerActive; live.Text = owner.Model.TimerActive ? owner.Model.TimerText : "00:00"; if (pause != null) { if (lastRunning != owner.Model.TimerRunning) { lastRunning = owner.Model.TimerRunning; pause.Content = Ui.Text(owner.Model.TimerRunning ? "Pause" : "Resume"); } pause.IsEnabled = owner.Model.TimerActive && !owner.Model.TimerFinished; } } if (current == "music" && seek != null) { var m = owner.Media; PresentTrack(m); seek.IsEnabled = m.CanSeek && m.Duration > TimeSpan.Zero; seek.ToolTip = seek.IsEnabled ? "Seek" : "This player does not support seeking"; if (!dragging) seek.Value = m.Duration > TimeSpan.Zero ? m.Position.TotalSeconds / m.Duration.TotalSeconds : 0; elapsed!.Text = ShellModel.FormatTime(dragging ? TimeSpan.FromTicks((long)(m.Duration.Ticks * seek.Value)) : m.Position); total!.Text = ShellModel.FormatTime(m.Duration); if (lastPlaying != m.Playing) { lastPlaying = m.Playing; play!.Content = TablerIcon.Create(m.Playing ? "player-pause" : "player-play", 22); } if (lastLiked != m.Liked) { lastLiked = m.Liked; like!.Content = TablerIcon.Create(m.Liked ? "heart-filled" : "heart", 20, m.Liked ? Ui.Danger : Ui.White); } like!.ToolTip = !m.LikeConnected ? "Install the GlassShell YouTube Music extension" : m.Liked ? "Unlike in YouTube Music" : "Like in YouTube Music"; like.IsEnabled = m.Visible && m.LikeConnected; play!.IsEnabled = m.CanToggle; prev!.IsEnabled = m.CanPrevious; next!.IsEnabled = m.CanNext; } }
    void PresentTrack(MediaService m)
    {
        if (art == null || musicLabels == null || musicTitle == null || musicArtist == null) return;
        if (displayedTrack < 0) { displayedTrack = targetTrack = m.TrackRevision; musicTitle.Text = m.Visible ? m.Title : "Nothing playing"; musicArtist.Text = m.Artist; art.Source = m.AlbumArt; return; }
        if (targetTrack == m.TrackRevision) { if (displayedTrack == targetTrack) art.Source = m.AlbumArt; return; }
        targetTrack = m.TrackRevision; int token = ++trackAnimation; double direction = m.TrackDirection < 0 ? 1 : -1;
        AnimatePart(art, 0, direction * 26, 0, 130);
        AnimatePart(musicLabels, 0, direction * 26, 36, 140, false, () =>
        {
            if (token != trackAnimation || art == null || musicLabels == null) return;
            musicTitle.Text = m.Title; musicArtist.Text = m.Artist; art.Source = m.AlbumArt;
            AnimatePart(art, -direction * 26, 0, 0, 185, true);
            AnimatePart(musicLabels, -direction * 26, 0, 42, 195, true, () => { if (token == trackAnimation) displayedTrack = targetTrack; });
        });
    }
    static void AnimatePart(FrameworkElement element, double from, double to, int delay, int duration, bool fadeIn = false, Action? completed = null)
    {
        var move = element.RenderTransform as TranslateTransform ?? new TranslateTransform(); element.RenderTransform = move;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var x = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(duration)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = ease };
        var opacity = new DoubleAnimation(fadeIn ? 0 : 1, fadeIn ? 1 : 0, TimeSpan.FromMilliseconds(duration)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = ease };
        if (completed != null) opacity.Completed += (_, _) => completed();
        move.BeginAnimation(TranslateTransform.XProperty, x); element.BeginAnimation(OpacityProperty, opacity);
    }
}
internal sealed class TimerAlertWindow : ShellWindow
{
    readonly ShellController owner; readonly TextBlock time = Ui.Text("", 32, weight: FontWeights.SemiBold);
    public bool IsOpen { get; private set; }
    public int PresentationVersion { get; private set; }
    public TimerAlertWindow(ShellController controller) : base("GlassShell · Timer finished", 250, 150) { owner = controller; Glass.TintAmount = .66; var body = new StackPanel { Margin = new Thickness(22) }; body.Children.Add(Ui.Text("Time’s Up", 18, weight: FontWeights.SemiBold)); body.Children.Add(time); body.Children.Add(Ui.Button("Stop", () => owner.Model.ResetTimer(), 200, 38)); Glass.Content.Children.Add(body); }
    public void Open() { PresentationVersion++; IsOpen = true; IsHitTestVisible = true; BeginAnimation(OpacityProperty, null); Opacity = 0; Show(); PositionSurface(); Tick(); BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))); }
    public void Dismiss() { if (!IsOpen) return; IsOpen = false; IsHitTestVisible = false; int version = ++PresentationVersion; var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120)); fade.Completed += (_, _) => { if (version == PresentationVersion && !IsOpen) Hide(); }; BeginAnimation(OpacityProperty, fade); }
    public void Tick() => time.Text = owner.Model.TimerText;
    public override void PositionSurface() { Left = Math.Clamp(owner.Bar.AnchorCenter("active-timer") - Width / 2, owner.Bar.Left + 8, owner.Bar.Left + owner.Bar.Width - Width - 8); Top = owner.Bar.Top + owner.Bar.VisualHeight + 8; Shape(0, 0, Width, Height, 22); }
}



