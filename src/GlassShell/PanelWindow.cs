using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace GlassShell;

internal sealed class PanelWindow : ShellWindow
{
    readonly ShellController owner; string current = ""; TextBox? note; TextBlock? live, elapsed, total, musicTitle, musicArtist; Slider? seek; Image? art; Button? play, pause, prev, next; ProgressBar? timerProgress; Button? addMinute; bool dragging; bool? lastPlaying, lastRunning; double panelHeight = 410;
    public string CurrentPage => current; public bool IsOpen { get; private set; }
    public int PresentationVersion { get; private set; }
    public PanelWindow(ShellController controller) : base("GlassShell · Panel", 420, 410) { owner = controller; Glass.TintAmount = .66; }
    public void Open(string page)
    {
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
    public bool SaveNote() { if (note == null) return true; try { Storage.Write("quick-note.txt", note.Text); return true; } catch (Exception e) { Storage.Log("Save note: " + e); if (live != null) live.Text = "Could not save. Your note is still open."; return false; } }
    public void Dismiss() { if (!IsOpen || !SaveNote()) return; SetInput(false); IsOpen = false; IsHitTestVisible = false; int version = ++PresentationVersion; var fade = new DoubleAnimation(Glass.Opacity, 0, TimeSpan.FromMilliseconds(150)); fade.Completed += (_, _) => { if (version == PresentationVersion && !IsOpen) { Opacity = 0; Hide(); } }; Glass.BeginAnimation(OpacityProperty, fade); if (Glass.RenderTransform is TranslateTransform translate) translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -5, TimeSpan.FromMilliseconds(150))); }
    public void HideImmediately() { if (!SaveNote()) return; IsOpen = false; PresentationVersion++; SetInput(false); Glass.BeginAnimation(OpacityProperty, null); Glass.Opacity = 0; BeginAnimation(OpacityProperty, null); Opacity = 0; Hide(); }
    public void RefreshTray() { if (IsOpen && current == "tray") { Build(); PositionSurface(); UpdateLayout(); } }
    public override void PositionSurface() { double anchor = owner.Bar.AnchorCenter(current); Left = Math.Clamp(anchor - Width / 2, owner.Bar.Left + 8, owner.Bar.Left + owner.Bar.Width - Width - 8); Top = owner.Bar.Top + owner.Bar.VisualHeight + 8; Shape(0, 0, Width, panelHeight, 25); }
    void Build()
    {
        var trayIcons = current == "tray" ? owner.Tray.Icons : Array.Empty<TrayIconItem>();
        panelHeight = current switch { "active-timer" => 250, "timer" => 280, "widgets" => 160, "music" => 330, "notifications" => 260, "tray" => Math.Clamp(135 + Math.Ceiling(trayIcons.Count / 7.0) * 48, 190, 410), _ => 410 }; Glass.Content.Children.Clear(); lastPlaying = lastRunning = null; note = null; live = elapsed = total = musicTitle = musicArtist = null; timerProgress = null; addMinute = null; seek = null; art = null; play = pause = prev = next = null; dragging = false;
        var body = new StackPanel { Margin = new Thickness(24) }; var header = new Grid(); header.Children.Add(Ui.Text(current switch { "music" => "Now playing", "active-timer" => "Active Timer", "timer" => "Timer", "notes" => "Notes", "widgets" => "Widgets", "controls" => "Control Center", "notifications" => "Notifications", "tray" => "Background apps", _ => "GlassShell" }, 22, weight: FontWeights.SemiBold)); var close = Ui.Icon("x", "Close", Dismiss, 30); close.HorizontalAlignment = HorizontalAlignment.Right; header.Children.Add(close); body.Children.Add(header); body.Children.Add(new Border { Height = 18 });
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
                art = new Image { Width = 72, Height = 72, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 16, 0) }; musicTitle = Ui.Text("", 18, weight: FontWeights.SemiBold); musicArtist = Ui.Text("", 13, Ui.Muted); var labels = new StackPanel { Width = 260, VerticalAlignment = VerticalAlignment.Center }; labels.Children.Add(musicTitle); labels.Children.Add(musicArtist); body.Children.Add(Ui.Row(art, labels)); body.Children.Add(new Border { Height = 24 });
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
            case "controls": body.Children.Add(Ui.Row(Ui.ActionButton("wifi", "Wi-Fi settings", () => Ui.Open("ms-settings:network-wifi"), 174, 58), Ui.ActionButton("bluetooth", "Bluetooth settings", () => Ui.Open("ms-settings:bluetooth"), 174, 58))); body.Children.Add(Ui.Row(Ui.ActionButton("volume", "Sound settings", () => Ui.Open("ms-settings:sound"), 174, 58), Ui.ActionButton("device-desktop", "Display settings", () => Ui.Open("ms-settings:display"), 174, 58))); body.Children.Add(Ui.Row(Ui.Icon("volume-2", "Volume down", () => Native.Shortcut(0xAE, false), 52), Ui.Icon("volume-off", "Mute", () => Native.Shortcut(0xAD, false), 52), Ui.Icon("volume", "Volume up", () => Native.Shortcut(0xAF, false), 52))); body.Children.Add(Ui.Button("Open Windows quick settings", () => { HideImmediately(); Native.Shortcut(0x41); }, 350)); break;
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
    public void Tick() { if (!IsOpen) return; if ((current == "timer" || current == "active-timer") && live != null) { if (timerProgress != null) timerProgress.Value = owner.Model.TimerProgress; if (addMinute != null) addMinute.IsEnabled = owner.Model.TimerActive; live.Text = owner.Model.TimerActive ? owner.Model.TimerText : "00:00"; if (pause != null) { if (lastRunning != owner.Model.TimerRunning) { lastRunning = owner.Model.TimerRunning; pause.Content = Ui.Text(owner.Model.TimerRunning ? "Pause" : "Resume"); } pause.IsEnabled = owner.Model.TimerActive && !owner.Model.TimerFinished; } } if (current == "music" && seek != null) { var m = owner.Media; art!.Source = m.AlbumArt; musicTitle!.Text = m.Visible ? m.Title : "Nothing playing"; musicArtist!.Text = m.Artist; seek.IsEnabled = m.CanSeek && m.Duration > TimeSpan.Zero; seek.ToolTip = seek.IsEnabled ? "Seek" : "This player does not support seeking"; if (!dragging) seek.Value = m.Duration > TimeSpan.Zero ? m.Position.TotalSeconds / m.Duration.TotalSeconds : 0; elapsed!.Text = ShellModel.FormatTime(dragging ? TimeSpan.FromTicks((long)(m.Duration.Ticks * seek.Value)) : m.Position); total!.Text = ShellModel.FormatTime(m.Duration); if (lastPlaying != m.Playing) { lastPlaying = m.Playing; play!.Content = TablerIcon.Create(m.Playing ? "player-pause" : "player-play", 22); } play!.IsEnabled = m.CanToggle; prev!.IsEnabled = m.CanPrevious; next!.IsEnabled = m.CanNext; } }
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



