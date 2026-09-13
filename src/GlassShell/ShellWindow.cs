using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace GlassShell;

internal class ShellWindow : Window
{
    public readonly GlassPanel Glass = new();
    protected readonly Canvas Canvas = new();
    public IntPtr Handle { get; private set; }
    public double Scale { get; private set; } = 1.5;
    public Native.Rect Monitor { get; private set; }
    public bool CaptureExcluded { get; private set; }
    private bool disposed;
    public ShellWindow(string name, double width, double height)
    {
        Title = name; Width = width; Height = height; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        FontFamily = Ui.Font; Foreground = Ui.White;
        Glass.CaptureHost = this;
        Canvas.Children.Add(Glass); Content = Canvas;
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(Handle, Native.GwlExStyle, Native.GetWindowLong(Handle, Native.GwlExStyle) | Native.WsExToolWindow | Native.WsExNoActivate);
            CaptureExcluded = Native.SetWindowDisplayAffinity(Handle, 0x11);
            if (!CaptureExcluded) { Glass.Live = false; Storage.Log(name + ": capture exclusion failed; using opaque material to prevent feedback"); }
            ReadMonitor(); HwndSource.FromHwnd(Handle)?.AddHook(WndProc); PositionSurface();
        };
        Closed += (_, _) => { if (disposed) return; disposed = true; Glass.Dispose(); };
    }
    protected virtual IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (msg == Native.WmDpiChanged || msg == Native.WmDisplayChange)
            Dispatcher.BeginInvoke(new Action(() => { ReadMonitor(); PositionSurface(); }));
        return IntPtr.Zero;
    }
    public void ReadMonitor()
    {
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        Native.GetMonitorInfo(Native.MonitorFromWindow(Handle, 1), ref info); Monitor = info.Monitor;
        Scale = Math.Max(1, Native.GetDpiForWindow(Handle)) / 96.0;
    }
    public virtual void PositionSurface() { }
    protected void Shape(double x, double y, double w, double h, double radius)
    {
        Glass.SetShape(x, y, w, h, radius);
        System.Windows.Controls.Canvas.SetLeft(Glass, x); System.Windows.Controls.Canvas.SetTop(Glass, y);
        // WS_EX_LAYERED's per-pixel alpha handles native hit testing outside the clip.
        // Keeping the HWND stable avoids an asynchronous SetWindowRgn repaint each frame.
    }
    public bool ContainsScreenPoint(Native.Point point)
    {
        if (!IsVisible || Opacity < .01) return false;
        var local = Glass.PointFromScreen(new Point(point.X, point.Y));
        return RoundedShape.Contains(local.X, local.Y, Glass.ShapeBounds.Width, Glass.ShapeBounds.Height, Glass.ShapeRadius);
    }
    public bool IsPanelAnchor(Native.Point point, string page)
    {
        if (!IsVisible) return false;
        var local = PointFromScreen(new Point(point.X, point.Y));
        DependencyObject? hit = InputHitTest(local) as DependencyObject;
        while (hit != null && hit != this)
        {
            if (hit is Button button && (string?)button.Tag == page) return true;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return false;
    }
    protected void SetInput(bool active)
    {
        int style = Native.GetWindowLong(Handle, Native.GwlExStyle);
        Native.SetWindowLong(Handle, Native.GwlExStyle, active ? style & ~Native.WsExNoActivate : style | Native.WsExNoActivate);
        if (active) { Activate(); Native.SetForegroundWindow(Handle); }
    }
    public void RefreshGlass() { if (IsVisible && CaptureExcluded) Glass.Refresh(); }
}



