using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace GlassShell;

internal sealed class StatusBar : ShellWindow
{
    public double ReservedHeight { get; private set; } = 36; public double VisualHeight => height.Value;
    readonly Spring height = new(36); readonly ShellController owner; uint callback; bool registered, positioning;
    readonly Dictionary<string, Button> anchors = new(); readonly TextBlock clock = Ui.Text("", 12), title = Ui.Text("", 12, weight: FontWeights.SemiBold), artist = Ui.Text("", 10, Ui.Muted), time = Ui.Text("", 14);
    readonly Image art = new() { Width = 34, Height = 34, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 9, 0) };
    readonly Image timerIcon = TablerIcon.Create("stopwatch", 16); bool? lastExpired; bool? lastPlaying; readonly Button music, timerButton, play, previous, next; readonly Grid divider; readonly TimerRing ring = new();
    bool musicShown, timerShown, dividerShown;
    public bool MusicVisible => music.Visibility == Visibility.Visible; public bool TimerVisible => timerButton.Visibility == Visibility.Visible;
    public StatusBar(ShellController controller) : base("GlassShell · Status", 1000, 56)
    {
        owner = controller; Glass.TintAmount = .48; Glass.BottomBorderOnly = true;
        var left = Ui.Row(Link("layout-grid", "Widgets", "widgets"), Link("stopwatch", "Timer", "timer"), Link("pencil", "Notes", "notes")); left.HorizontalAlignment = HorizontalAlignment.Left; left.VerticalAlignment = VerticalAlignment.Center; left.Margin = new Thickness(10, 0, 0, 0); Glass.Content.Children.Add(left);
        var right = Ui.Row(Link("apps", "Background apps", "tray"), Link("adjustments-horizontal", "Control Center", "controls"), Link("bell", "Notifications", "notifications"), clock, Link("dots", "Session", "session")); right.HorizontalAlignment = HorizontalAlignment.Right; right.VerticalAlignment = VerticalAlignment.Center; right.Margin = new Thickness(0, 0, 10, 0); clock.Margin = new Thickness(12, 0, 6, 0); Glass.Content.Children.Add(right);
        var labels = new StackPanel { Width = 185, VerticalAlignment = VerticalAlignment.Center }; labels.Children.Add(title); labels.Children.Add(artist);
        previous = Ui.Icon("player-skip-back", "Previous", () => _ = owner.Media.Control("previous"), 28); play = Ui.Icon("player-play", "Play / pause", () => _ = owner.Media.Control("toggle"), 28); next = Ui.Icon("player-skip-forward", "Next", () => _ = owner.Media.Control("next"), 28);
        music = Ui.Button("", () => owner.OpenPanel("music"), 360, 46); music.Margin = new Thickness(0); music.Padding = new Thickness(12, 6, 12, 6); music.ClipToBounds = true; music.Tag = "music"; anchors["music"] = music; music.Content = Ui.Row(art, labels, previous, play, next);
        var icon = new Grid { Width = 32, Height = 32 }; icon.Children.Add(ring); icon.Children.Add(timerIcon);
        var timerLabels = new StackPanel { Width = 104, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        timerLabels.Children.Add(Ui.Text("Active Timer", 10, Ui.Muted)); timerLabels.Children.Add(time);
        System.Windows.Documents.Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        timerButton = Ui.Button("", owner.OpenActiveTimer, 176, 46); timerButton.Margin = new Thickness(0); timerButton.Padding = new Thickness(12, 7, 12, 7); timerButton.ClipToBounds = true; timerButton.Tag = "active-timer"; anchors["active-timer"] = timerButton; timerButton.Content = Ui.Row(icon, timerLabels);
        divider = new Grid { Width = 25, Height = 24, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
        divider.Children.Add(new Border { Width = 1, Height = 24, Background = Ui.Muted, Opacity = .35, HorizontalAlignment = HorizontalAlignment.Center });
        music.Visibility = timerButton.Visibility = divider.Visibility = Visibility.Collapsed;
        var center = Ui.Row(music, divider, timerButton); center.HorizontalAlignment = HorizontalAlignment.Center; center.VerticalAlignment = VerticalAlignment.Center; Glass.Content.Children.Add(center);
        SourceInitialized += (_, _) => Register(); Closed += (_, _) => Unregister(); Tick();
    }
    Button Link(string icon, string tip, string page) { var b = Ui.Icon(icon, tip, () => owner.OpenPanel(page), 28); b.Tag = page; anchors[page] = b; return b; }
    public double AnchorCenter(string page) { if (anchors.TryGetValue(page, out var b) && b.IsVisible) return b.PointToScreen(new Point(b.ActualWidth / 2, 0)).X / Scale; return Left + Width / 2; }
    public void Tick()
    {
        clock.Text = DateTime.Now.ToString("ddd d MMM   h:mm tt"); var m = owner.Media;
        SetActivityVisible(music, m.Visible, ref musicShown, -12, 360);
        SetActivityVisible(timerButton, owner.Model.TimerActive, ref timerShown, 12, 176);
        SetActivityVisible(divider, m.Visible && owner.Model.TimerActive, ref dividerShown, 0, 25);
        title.Text = m.Title; artist.Text = m.Artist; art.Source = m.AlbumArt; if (lastPlaying != m.Playing) { lastPlaying = m.Playing; play.Content = TablerIcon.Create(m.Playing ? "player-pause" : "player-play", 16); }
        play.IsEnabled = m.CanToggle; previous.IsEnabled = m.CanPrevious; next.IsEnabled = m.CanNext; music.SetValue(Ui.IsSelectedProperty, owner.Panel?.IsOpen == true && owner.Panel.CurrentPage == "music");
        timerButton.SetValue(Ui.IsSelectedProperty, (owner.Panel?.IsOpen == true && owner.Panel.CurrentPage == "active-timer") || owner.Alert?.IsOpen == true);
        if (lastExpired != owner.Model.TimerFinished) { lastExpired = owner.Model.TimerFinished; timerIcon.Source = TablerIcon.Create("stopwatch", 16, owner.Model.TimerFinished ? Ui.Danger : Ui.White).Source; }
        time.Text = owner.Model.TimerText; ring.Progress = owner.Model.TimerProgress; ring.InvalidateVisual(); height.Target = m.Visible || owner.Model.TimerActive ? 56 : 36; if (height.Target > ReservedHeight) { ReservedHeight = height.Target; if (registered) Reserve(); }
    }
    static void SetActivityVisible(FrameworkElement element, bool visible, ref bool shown, double offset, double expandedWidth)
    {
        if (shown == visible) return;
        shown = visible;
        element.BeginAnimation(OpacityProperty, null);
        var transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        if (visible)
        {
            element.Visibility = Visibility.Visible;
            element.Opacity = 0;
            element.Width = 0;
            transform.X = offset;
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(210)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            element.BeginAnimation(WidthProperty, new DoubleAnimation(0, expandedWidth, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            var fade = new DoubleAnimation(element.Opacity, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => { if (element.Opacity < .01) { element.Visibility = Visibility.Collapsed; element.BeginAnimation(WidthProperty, null); element.Width = expandedWidth; } };
            element.BeginAnimation(OpacityProperty, fade);
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -offset, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            element.BeginAnimation(WidthProperty, new DoubleAnimation(element.ActualWidth, 0, TimeSpan.FromMilliseconds(210)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        }
    }
    public void Animate(double dt) { height.Step(dt); Shape(0, 0, Width, height.Value, 0); if (!height.Active && ReservedHeight != height.Target) { ReservedHeight = height.Target; if (registered) Reserve(); } if (owner.Panel.IsVisible) owner.Panel.PositionSurface(); if (owner.Alert.IsVisible) owner.Alert.PositionSurface(); }
    public override void PositionSurface() { Left = Monitor.Left / Scale; Top = Monitor.Top / Scale; Width = Monitor.Width / Scale; Height = 56; Shape(0, 0, Width, height.Value, 0); if (registered) Reserve(); }
    Native.AppBarData Data() => new() { Size = (uint)Marshal.SizeOf<Native.AppBarData>(), Hwnd = Handle, Callback = callback, Edge = 1 };
    void Register() { callback = Native.RegisterWindowMessage("GlassShell.AppBar.v1"); var d = Data(); registered = Native.SHAppBarMessage(0, ref d) != UIntPtr.Zero; if (registered) Reserve(); Native.RegisterHotKey(Handle, 1, 0x4003, 0x1B); Native.RegisterHotKey(Handle, 2, 0x4003, 0x20); }
    void Reserve() { if (positioning) return; positioning = true; try { var d = Data(); d.Rect = Monitor; d.Rect.Bottom = d.Rect.Top + (int)Math.Ceiling(ReservedHeight * Scale); Native.SHAppBarMessage(2, ref d); d.Rect.Bottom = d.Rect.Top + (int)Math.Ceiling(ReservedHeight * Scale); Native.SHAppBarMessage(3, ref d); Left = d.Rect.Left / Scale; Top = d.Rect.Top / Scale; } finally { positioning = false; } }
    public void Unregister() { Native.UnregisterHotKey(Handle, 1); Native.UnregisterHotKey(Handle, 2); if (!registered) return; registered = false; var d = Data(); Native.SHAppBarMessage(1, ref d); }
    protected override IntPtr WndProc(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) { if (callback != 0 && msg == callback && wp.ToInt32() == 1 && !positioning) Dispatcher.BeginInvoke(new Action(Reserve)); if (msg == Native.WmHotkey) { handled = true; if (wp.ToInt32() == 1) owner.Exit(); else owner.OpenPanel("widgets"); } return base.WndProc(h, msg, wp, lp, ref handled); }
}
internal sealed class TimerRing : FrameworkElement
{
    public double Progress { get; set; }
    protected override void OnRender(DrawingContext dc) { var center = new Point(16, 16); dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), 2), center, 14, 14); double p = Math.Clamp(Progress, 0, 1); if (p <= 0) return; var pen = new Pen(Ui.Accent, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; if (p >= .9999) { dc.DrawEllipse(null, pen, center, 14, 14); return; } double a = p * Math.PI * 2 - Math.PI / 2; var g = new StreamGeometry(); using (var c = g.Open()) { c.BeginFigure(new Point(16, 2), false, false); c.ArcTo(new Point(16 + 14 * Math.Cos(a), 16 + 14 * Math.Sin(a)), new Size(14, 14), 0, p > .5, SweepDirection.Clockwise, true, false); } dc.DrawGeometry(null, pen, g); }
}



