using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
namespace GlassShell;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        bool test = args.Contains("--ui-regression-test"); if (test) Storage.OverrideRoot = Path.Combine(Environment.CurrentDirectory, "artifacts", "ui-regression", "data");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; ShellController? shell = null;
        app.DispatcherUnhandledException += (_, e) => { Storage.Log(e.Exception.ToString()); e.Handled = true; shell?.Dispose(); app.Shutdown(1); };
        app.Startup += async (_, _) => { shell = new ShellController(); shell.Start(); if (test) _ = new UiRegressionTest(shell).Run(); else await shell.Media.Initialize(); };
        app.Exit += (_, _) => shell?.Dispose(); app.Run();
    }
}
internal sealed class ShellController : IDisposable
{
    public ShellModel Model { get; } = new(); public MediaService Media { get; } = new(); public TrayService Tray { get; } = new(); public ConnectivityService Connectivity { get; } = new(); public AudioService Audio { get; } = new(); public CalendarService Calendar { get; } = new();
    public StatusBar Bar { get; }
    public PanelWindow Panel { get; }
    public TimerAlertWindow Alert { get; }
    public bool LiveGlass { get; private set; } = true; public bool PauseCapture { get; set; }
    public long ObservedPresses { get; private set; }
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly Stopwatch watch = Stopwatch.StartNew(); double lastMedia, lastConnectivity, lastCalendar, screenshotUntil; bool disposed, fullscreen, expired, screenshotMode; IntPtr foreground;
    OutsideClickMonitor? outside; System.Windows.Forms.NotifyIcon? tray; TimeSpan lastFrame;
    public ShellController() { Bar = new(this); Panel = new(this); Alert = new(this); Model.Changed += Update; Media.Changed += Update; Tray.Changed += TrayChanged; Connectivity.Changed += ConnectivityChanged; Calendar.Changed += CalendarChanged; }
    public void Start()
    {
        Bar.Show(); timer.Tick += Tick; timer.Start(); CompositionTarget.Rendering += Render; outside = new(CaptureOutsidePress);
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Widgets", null, (_, _) => OpenPanel("widgets"));
        var liveGlass = new System.Windows.Forms.ToolStripMenuItem("Live glass") { Checked = LiveGlass, CheckOnClick = true };
        liveGlass.CheckedChanged += (_, _) => SetLiveGlass(liveGlass.Checked); menu.Items.Add(liveGlass);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator()); menu.Items.Add("Quit GlassShell", null, (_, _) => Exit());
        tray = new() { Visible = true, Text = "GlassShell · Ctrl+Alt+Esc to quit", Icon = System.Drawing.SystemIcons.Application, ContextMenuStrip = menu }; Update(); Storage.Log("Started status-bar layout");
        _ = Connectivity.RefreshWifi();
        if (Calendar.Connected) _ = Calendar.Refresh(DateTime.Today.AddDays(-7), DateTime.Today.AddDays(14));
    }
    void TrayChanged() { Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!disposed) { Bar.Tick(); Panel.RefreshTray(); } })); }
    void ConnectivityChanged() { Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!disposed) { Bar.Tick(); Panel.RefreshConnectivity(); } })); }
    void CalendarChanged() { Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!disposed) { Bar.Tick(); Panel.RefreshCalendar(); } })); }
    void Update() { Bar.Tick(); Panel.Tick(); Alert.Tick(); if (Model.TimerFinished && !expired && !fullscreen) { Alert.Open(); System.Media.SystemSounds.Exclamation.Play(); } expired = Model.TimerFinished; if (!Model.TimerFinished) Alert.Dismiss(); }
    void Tick(object? s, EventArgs e) { Model.Tick(); Update(); UpdateContext(); if (!fullscreen && LiveGlass && !PauseCapture && !screenshotMode) foreach (var w in new ShellWindow[] { Bar, Panel, Alert }) w.RefreshGlass(); if (watch.Elapsed.TotalSeconds - lastMedia > .5) { lastMedia = watch.Elapsed.TotalSeconds; _ = Media.Refresh(); } if (watch.Elapsed.TotalSeconds - lastConnectivity > 15) { lastConnectivity = watch.Elapsed.TotalSeconds; _ = Connectivity.RefreshWifi(); } if (Calendar.Connected && !Calendar.Busy && watch.Elapsed.TotalSeconds - lastCalendar > 300) { lastCalendar = watch.Elapsed.TotalSeconds; _ = Calendar.Refresh(DateTime.Today.AddDays(-7), DateTime.Today.AddDays(14)); } }
    void Render(object? s, EventArgs e) { var t = ((RenderingEventArgs)e).RenderingTime; if (t == lastFrame) return; double dt = lastFrame == TimeSpan.Zero ? 1.0 / 60 : (t - lastFrame).TotalSeconds; lastFrame = t; if (!fullscreen) Bar.Animate(dt); }
    internal void CaptureOutsidePress(Native.Point p) { ObservedPresses++; int v = Panel.PresentationVersion, a = Alert.PresentationVersion; string anchor = Panel.CurrentPage == "calendar-detail" ? "calendar" : Panel.CurrentPage; bool close = Panel.IsOpen && !Panel.ContainsScreenPoint(p) && !Bar.IsPanelAnchor(p, anchor); bool alert = Alert.IsOpen && !Alert.ContainsScreenPoint(p) && !Bar.IsPanelAnchor(p, "active-timer"); Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => { if (disposed) return; if (close && v == Panel.PresentationVersion) Panel.Dismiss(); if (alert && a == Alert.PresentationVersion) Alert.Dismiss(); })); }
    void UpdateContext()
    {
        if (Storage.OverrideRoot != null) return; var h = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(h, out uint pid); string currentClass = Native.Class(h); Native.GetWindowRect(h, out var currentRect); var monitor = Bar.Monitor;
        bool coversMonitor = currentRect.Left <= monitor.Left && currentRect.Top <= monitor.Top && currentRect.Right >= monitor.Right && currentRect.Bottom >= monitor.Bottom; bool snipping = IsSnippingSurface(pid, currentClass, coversMonitor);
        if (snipping) screenshotUntil = watch.Elapsed.TotalSeconds + 2.5; bool screenshotActive = snipping || watch.Elapsed.TotalSeconds < screenshotUntil; SetScreenshotMode(screenshotActive);
        if (screenshotActive) { if (fullscreen) fullscreen = false; if (!Bar.IsVisible) Bar.Show(); return; }
        if (pid != Environment.ProcessId && h != IntPtr.Zero) foreground = h; if (foreground == IntPtr.Zero) return; string c = Native.Class(foreground); Native.GetWindowRect(foreground, out var r); bool next = c is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") && !Native.IsIconic(foreground) && r.Left <= monitor.Left && r.Top <= monitor.Top && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom; if (next == fullscreen) return; fullscreen = next; if (next) { Bar.Hide(); Panel.HideImmediately(); Alert.Dismiss(); } else Bar.Show();
    }
    static bool IsSnippingSurface(uint pid, string windowClass, bool coversMonitor)
    {
        if (windowClass.Contains("ScreenClipping", StringComparison.OrdinalIgnoreCase) || windowClass.Contains("Snip", StringComparison.OrdinalIgnoreCase)) return true;
        if (!coversMonitor || pid == 0) return false; try { string name = Process.GetProcessById((int)pid).ProcessName; return name.Equals("SnippingTool", StringComparison.OrdinalIgnoreCase) || name.Equals("ScreenClippingHost", StringComparison.OrdinalIgnoreCase) || name.Equals("SnipAndSketch", StringComparison.OrdinalIgnoreCase); } catch { return false; }
    }
    void SetScreenshotMode(bool active)
    {
        if (screenshotMode == active) return; screenshotMode = active; foreach (var w in new ShellWindow[] { Bar, Panel, Alert }) if (w.Handle != IntPtr.Zero) Native.SetWindowDisplayAffinity(w.Handle, active || !LiveGlass ? 0u : 0x11u); Storage.Log(active ? "Screenshot mode: froze glass and enabled capture" : "Screenshot mode: restored live capture exclusion"); if (!active && LiveGlass) foreach (var w in new ShellWindow[] { Bar, Panel, Alert }) w.RefreshGlass();
    }
    public void OpenPanel(string page) { if (!fullscreen) { Panel.Open(page); if (page == "controls") _ = Connectivity.RefreshAll(); else if (page == "wifi") _ = Connectivity.RefreshWifi(); else if (page == "bluetooth") _ = Connectivity.RefreshBluetooth(); else if (page == "calendar" && Calendar.Connected) _ = Calendar.Refresh(DateTime.Today.AddDays(-7), DateTime.Today.AddDays(14)); } }

    public void OpenActiveTimer() { if (Model.TimerFinished) Alert.Open(); else OpenPanel("active-timer"); }
    public void SetLiveGlass(bool enabled) { LiveGlass = enabled; foreach (var w in new ShellWindow[] { Bar, Panel, Alert }) { if (w.Handle != IntPtr.Zero) Native.SetWindowDisplayAffinity(w.Handle, enabled && !screenshotMode ? 0x11u : 0u); w.Glass.Live = enabled && w.CaptureExcluded; w.Glass.Refresh(); } }
    public void Exit() { if (!Panel.SaveNote()) return; Dispose(); Application.Current.Shutdown(); }
    public void Dispose() { if (disposed) return; disposed = true; timer.Stop(); CompositionTarget.Rendering -= Render; outside?.Dispose(); Panel.SaveNote(); Tray.Dispose(); Connectivity.Dispose(); Bar.Unregister(); tray?.Dispose(); Media.Dispose(); Panel.Close(); Alert.Close(); Bar.Close(); Storage.Log("Stopped status-bar layout"); }
}


