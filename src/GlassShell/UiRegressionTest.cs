using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Media;
namespace GlassShell;

internal sealed class UiRegressionTest
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    async Task Press(Point p) { SetCursorPos((int)p.X, (int)p.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(230); }
    async Task Capture(ShellWindow window, string file) { s.PauseCapture = true; Native.SetWindowDisplayAffinity(window.Handle, 0); await Task.Delay(200); var p = window.PointToScreen(new Point()); using var bmp = new System.Drawing.Bitmap((int)(window.Width * window.Scale), (int)(window.Glass.ShapeBounds.Height * window.Scale)); using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen((int)p.X, (int)p.Y, 0, 0, bmp.Size); bmp.Save("artifacts/ui-regression/" + file, System.Drawing.Imaging.ImageFormat.Png); Native.SetWindowDisplayAffinity(window.Handle, 0x11); s.PauseCapture = false; }
    readonly ShellController s; public UiRegressionTest(ShellController shell) { s = shell; }
    public async Task Run()
    {
        var checks = new Dictionary<string, bool>(); string? error = null; Window? fixture = null; Native.GetCursorPos(out var originalCursor); try
        {
            Directory.CreateDirectory("artifacts/ui-regression");
            await Task.Delay(700); checks["idle bar"] = !s.Bar.MusicVisible && !s.Bar.TimerVisible && s.Bar.ReservedHeight == 36;
            s.Media.SetTestState(true);
            var enteringCenter = (StackPanel)s.Bar.Glass.Content.Children[2];
            var enteringMusic = (Button)enteringCenter.Children[0];
            checks["activity enters from transparent offset"] = enteringMusic.Visibility == Visibility.Visible && enteringMusic.Opacity < 1 && ((TranslateTransform)enteringMusic.RenderTransform).X < 0;
            await Task.Delay(700); checks["playing visible and reserved"] = s.Bar.MusicVisible && s.Bar.ReservedHeight == 56;
            checks["music has even card padding"] = enteringMusic.Padding == new Thickness(8);
            var artwork = new DrawingImage(new GeometryDrawing(Brushes.SteelBlue, new Pen(Brushes.LightSkyBlue, 8), new EllipseGeometry(new Point(40, 40), 36, 36))); s.Media.SetTestState(false, true, artwork); checks["paused visible"] = s.Bar.MusicVisible;
            s.Media.TestMediaGap(); checks["track gap preserves metadata and art"] = s.Bar.MusicVisible && s.Media.Title == "A test track" && s.Media.AlbumArt == artwork;
            s.OpenPanel("music"); await Task.Delay(250); checks["music dropdown"] = s.Panel.IsOpen && s.Panel.CurrentPage == "music";
            int clicks = 0; var target = Ui.Button("Test click target", () => clicks++, 280, 120); fixture = new Window { Left = 80, Top = 500, Width = 300, Height = 160, Content = target, Topmost = true, ShowInTaskbar = false, ShowActivated = false }; fixture.Show(); await Task.Delay(200); long before = s.ObservedPresses; await Press(target.PointToScreen(new Point(100, 60))); checks["native outside click propagates"] = clicks == 1 && s.ObservedPresses > before && !s.Panel.IsOpen;
            await Press(new Point(s.Bar.AnchorCenter("music") * s.Bar.Scale, (s.Bar.Top + 24) * s.Bar.Scale)); checks["music surface click opens dropdown"] = s.Panel.IsOpen && s.Panel.CurrentPage == "music";
            var center = (StackPanel)s.Bar.Glass.Content.Children[2]; var music = (Button)center.Children[0]; checks["selected music highlight"] = ((SolidColorBrush)((Border)music.Template.FindName("ButtonSurface", music)).Background).Color.A == 54; var musicRow = (StackPanel)music.Content; double musicLeft = musicRow.TranslatePoint(new Point(), music).X; checks["music content has equal edge insets"] = Math.Abs(musicLeft - 8) < .6 && Math.Abs(music.ActualWidth - musicLeft - musicRow.ActualWidth - 8) < .6; checks["compact album art is rounded"] = ((Image)((RoundedImage)musicRow.Children[0]).Child).Clip is RectangleGeometry compactClip && compactClip.RadiusX == 6; var transport = (Button)musicRow.Children[2]; await Press(transport.PointToScreen(new Point(14, 14))); checks["bar transport does not toggle music menu"] = s.Panel.IsOpen && s.Panel.CurrentPage == "music";
            var musicBody = (StackPanel)s.Panel.Glass.Content.Children[0];
            var detailsRow = (Grid)musicBody.Children[2]; var detailArt = (RoundedImage)detailsRow.Children[0]; var detailLike = (Button)detailsRow.Children[2]; checks["detail album art is rounded"] = ((Image)detailArt.Child).Clip is RectangleGeometry detailClip && detailClip.RadiusX == 12; checks["heart aligns to content edge"] = Math.Abs(detailLike.TranslatePoint(new Point(detailLike.ActualWidth, 0), detailsRow).X - detailsRow.ActualWidth) < 1; checks["heart has no idle fill"] = detailLike.Background == Brushes.Transparent;
            s.Media.SetTestTrack("Next test track", "Next artist", 1); await Task.Delay(70);
            checks["next track exits left"] = ((TranslateTransform)detailArt.RenderTransform).X < 0;
            await Task.Delay(560); checks["next track enters and settles"] = ((TextBlock)((StackPanel)detailsRow.Children[1]).Children[0]).Text == "Next test track";
            s.Media.SetTestTrack("Previous test track", "Previous artist", -1); await Task.Delay(70);
            checks["previous track exits right"] = ((TranslateTransform)detailArt.RenderTransform).X > 0;
            await Task.Delay(560); if (s.Media.Liked) s.Media.ToggleLike(); await Press(detailLike.PointToScreen(new Point(20, 20))); checks["song like toggles"] = s.Media.Liked; checks["liked heart is filled"] = detailLike.Content is Image heart && heart.Source is DrawingImage heartDrawing && ((DrawingGroup)heartDrawing.Drawing).Children.OfType<GeometryDrawing>().Any(d => d.Brush != null && d.Brush != Brushes.Transparent);
            foreach (var child in musicBody.Children) if (child is Slider rail)
            {
                await Press(rail.PointToScreen(new Point(rail.ActualWidth * .75, 12)));
                Storage.Log("Click seek=" + s.Media.LastTestSeek + " rail=" + rail.Value + " enabled=" + rail.IsEnabled); checks["click rail seeks"] = s.Media.LastTestSeek is double f && Math.Abs(f - .75) < .03;
                var start = rail.PointToScreen(new Point(rail.ActualWidth * .75, 12)); SetCursorPos((int)start.X, (int)start.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(80);
                var end = rail.PointToScreen(new Point(rail.ActualWidth * .30, 12)); SetCursorPos((int)end.X, (int)end.Y); await Task.Delay(80); mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(160);
                Storage.Log("Drag seek=" + s.Media.LastTestSeek + " rail=" + rail.Value); checks["drag rail seeks"] = s.Media.LastTestSeek is double d && d < .55;
            }
            s.Model.StartTimer(TimeSpan.FromMinutes(25)); await Task.Delay(700); await Capture(s.Bar, "status-bar.png"); await Capture(s.Panel, "music-dropdown.png");
            s.OpenActiveTimer(); await Task.Delay(250); checks["active timer details"] = s.Panel.CurrentPage == "active-timer";
            var timerBody = (StackPanel)s.Panel.Glass.Content.Children[0]; var options = (StackPanel)timerBody.Children[timerBody.Children.Count - 1]; var add = (Button)options.Children[1]; var remaining = s.Model.Remaining;
            await Press(add.PointToScreen(new Point(40, 20))); checks["add minute updates countdown"] = s.Model.Remaining.TotalSeconds > remaining.TotalSeconds + 59;
            await Capture(s.Panel, "timer-details.png");
            var activeModule = (Button)center.Children[2]; checks["timer has even card padding"] = activeModule.Padding == new Thickness(8); var timerRow = (StackPanel)activeModule.Content; double timerLeft = timerRow.TranslatePoint(new Point(), activeModule).X; checks["timer content has equal edge insets"] = Math.Abs(timerLeft - 8) < .6 && Math.Abs(activeModule.ActualWidth - timerLeft - timerRow.ActualWidth - 8) < .6; double width = activeModule.ActualWidth; s.Model.Advance(TimeSpan.FromMinutes(20)); await Task.Delay(80); checks["timer module stable width"] = activeModule.ActualWidth == width;
            s.Panel.Dismiss();
            s.Model.StartTimer(TimeSpan.FromSeconds(1)); checks["both activities"] = s.Bar.MusicVisible && s.Bar.TimerVisible;
            await Task.Delay(1300); checks["expired popup and overtime"] = s.Alert.IsOpen && s.Model.TimerFinished && s.Model.TimerRunning && s.Model.Remaining < TimeSpan.Zero;
            s.CaptureOutsidePress(new Native.Point { X = (int)(s.Bar.Left * s.Bar.Scale + 20), Y = (int)((s.Bar.Top + 600) * s.Bar.Scale) }); await Task.Delay(250); checks["outside closes both but timer runs"] = !s.Alert.IsOpen && !s.Panel.IsOpen && s.Model.TimerRunning;
            s.OpenActiveTimer(); await Capture(s.Alert, "timer-expired.png"); checks["reopen expired"] = s.Alert.IsOpen; s.Model.ResetTimer(); await Task.Delay(260); checks["stop clears timer and popup"] = !s.Bar.TimerVisible && !s.Alert.IsOpen;
            s.Tray.SetTestIcon(s.Bar.Handle);
            foreach (var page in new[] { "widgets", "timer", "notes", "controls", "notifications" }) { s.OpenPanel(page); await Task.Delay(180); if (page == "notes") { foreach (var child in ((System.Windows.Controls.StackPanel)s.Panel.Glass.Content.Children[0]).Children) if (child is TextBox note) { note.Text = "Isolated autosave check"; checks["note autosaves"] = Storage.Read("quick-note.txt") == note.Text; } } checks[page + " opens below bar"] = s.Panel.IsOpen && s.Panel.Top + 1 >= s.Bar.Top + s.Bar.VisualHeight; s.Panel.Dismiss(); await Task.Delay(150); checks[page + " dismisses"] = !s.Panel.IsOpen; }
            s.OpenPanel("controls"); await Task.Delay(220); await Capture(s.Panel, "control-center.png");
            s.Panel.Navigate("wifi"); await Task.Delay(220); checks["wifi subpage stays in panel"] = s.Panel.IsOpen && s.Panel.CurrentPage == "wifi"; checks["wifi test network rendered"] = FindText(s.Panel, "Test Network"); await Capture(s.Panel, "wifi-panel.png");
            s.Panel.Navigate("bluetooth"); await Task.Delay(220); checks["bluetooth subpage stays in panel"] = s.Panel.IsOpen && s.Panel.CurrentPage == "bluetooth"; checks["bluetooth test device rendered"] = FindText(s.Panel, "Test Headphones"); await Capture(s.Panel, "bluetooth-panel.png"); s.Panel.Dismiss(); await Task.Delay(160);
            s.Tray.ShowNativeOverflow(); checks["top tray requests native flyout"] = s.Tray.NativeFlyoutRequested;
            s.Media.SetTestState(false); checks["activity remains during exit animation"] = music.Visibility == Visibility.Visible; await Task.Delay(1000); checks["idle restored"] = !s.Bar.MusicVisible && s.Bar.ReservedHeight == 36;
            fixture.Close(); fixture = null;
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally { fixture?.Close(); SetCursorPos(originalCursor.X, originalCursor.Y); Directory.CreateDirectory("artifacts/ui-regression"); File.WriteAllText("artifacts/ui-regression/report.json", JsonSerializer.Serialize(new { checks, error }, new JsonSerializerOptions { WriteIndented = true })); s.Bar.Unregister(); await Task.Delay(300); var monitor = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() }; Native.GetMonitorInfo(Native.MonitorFromWindow(s.Bar.Handle, 1), ref monitor); checks["reservation released"] = monitor.Work.Top == monitor.Monitor.Top; File.WriteAllText("artifacts/ui-regression/report.json", JsonSerializer.Serialize(new { checks, error }, new JsonSerializerOptions { WriteIndented = true })); s.Exit(); }
    }
    static bool FindText(DependencyObject parent, string text)
    {
        if (parent is TextBlock label && label.Text.Contains(text, StringComparison.Ordinal)) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) if (FindText(VisualTreeHelper.GetChild(parent, i), text)) return true;
        return false;
    }
}






