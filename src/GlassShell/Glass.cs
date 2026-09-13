using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace GlassShell;

internal sealed class GlassEffect : ShaderEffect
{
    private static PixelShader? shared;
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(GlassEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register("Size", typeof(Point), typeof(GlassEffect), new UIPropertyMetadata(new Point(400, 100), PixelShaderConstantCallback(0)));
    public Point Size { get => (Point)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register("Radius", typeof(double), typeof(GlassEffect), new UIPropertyMetadata(28.0, PixelShaderConstantCallback(1)));
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public static readonly DependencyProperty DepthProperty = DependencyProperty.Register("Depth", typeof(double), typeof(GlassEffect), new UIPropertyMetadata(10.0, PixelShaderConstantCallback(2)));
    public double Depth { get => (double)GetValue(DepthProperty); set => SetValue(DepthProperty, value); }
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register("Tint", typeof(Color), typeof(GlassEffect), new UIPropertyMetadata(Color.FromArgb(80, 18, 24, 36), PixelShaderConstantCallback(3)));
    public Color Tint { get => (Color)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public static readonly DependencyProperty BlurProperty = DependencyProperty.Register("Blur", typeof(double), typeof(GlassEffect), new UIPropertyMetadata(1.5, PixelShaderConstantCallback(4)));
    public double Blur { get => (double)GetValue(BlurProperty); set => SetValue(BlurProperty, value); }
    public static readonly DependencyProperty BottomOnlyProperty = DependencyProperty.Register("BottomOnly", typeof(double), typeof(GlassEffect), new UIPropertyMetadata(0.0, PixelShaderConstantCallback(5)));
    public double BottomOnly { get => (double)GetValue(BottomOnlyProperty); set => SetValue(BottomOnlyProperty, value); }
    public GlassEffect()
    {
        shared ??= Compile(); PixelShader = shared;
        UpdateShaderValue(InputProperty); UpdateShaderValue(SizeProperty); UpdateShaderValue(RadiusProperty);
        UpdateShaderValue(DepthProperty); UpdateShaderValue(TintProperty); UpdateShaderValue(BlurProperty); UpdateShaderValue(BottomOnlyProperty);
    }
    private static PixelShader Compile()
    {
        const string source = """
            sampler2D backdrop : register(s0);
            float2 size : register(c0);
            float radius : register(c1);
            float depth : register(c2);
            float4 tint : register(c3);
            float blur : register(c4);
            float bottomOnly : register(c5);
            float4 main(float2 uv : TEXCOORD) : COLOR {
                float2 p = (uv - .5) * size;
                float r = min(radius, min(size.x, size.y) * .5);
                float2 q = abs(p) - (size * .5 - r);
                float2 outside = max(q, 0);
                float d = min(max(q.x, q.y), 0) + length(outside) - r;
                float2 n = length(outside) > .001 ? normalize(outside) : (q.x > q.y ? float2(1,0) : float2(0,1));
                n *= sign(p);
                float lens = pow(saturate(1 + d / max(8, r * .8)), 2);
                float2 t = clamp(uv - n * lens * depth / size, .001, .999);
                float2 b = blur / size;
                float3 color = tex2D(backdrop,t).rgb * .28;
                color += (tex2D(backdrop,t + float2(b.x,0)).rgb + tex2D(backdrop,t - float2(b.x,0)).rgb + tex2D(backdrop,t + float2(0,b.y)).rgb + tex2D(backdrop,t - float2(0,b.y)).rgb) * .12;
                color += (tex2D(backdrop,t+b).rgb + tex2D(backdrop,t-b).rgb + tex2D(backdrop,t+float2(b.x,-b.y)).rgb + tex2D(backdrop,t+float2(-b.x,b.y)).rgb) * .06;
                color = lerp(color, tint.rgb, tint.a);
                float rim = exp(-abs(d + 1.1) * 1.25);
                float light = .12 + .32 * pow(saturate(dot(n, normalize(float2(-.7,-1)))), 2);
                color += (rim * light + lens * .018) * lerp(1, step(.5, uv.y) * step(abs(n.x), .1), bottomOnly);
                float alpha = saturate(.5 - d);
                return float4(color * alpha, alpha);
            }
            """;
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        int result = D3DCompile(bytes, (UIntPtr)bytes.Length, "GlassShell", IntPtr.Zero, IntPtr.Zero, "main", "ps_3_0", 1 << 15, 0, out var code, out var error);
        if (result < 0)
        {
            string message = error == null ? "Shader compilation failed" : Marshal.PtrToStringAnsi(error.GetBufferPointer()) ?? "Shader compilation failed";
            if (error != null) Marshal.ReleaseComObject(error);
            throw new InvalidOperationException(message);
        }
        try
        {
            var output = new byte[(int)code.GetBufferSize()]; Marshal.Copy(code.GetBufferPointer(), output, 0, output.Length);
            var shader = new PixelShader(); shader.SetStreamSource(new MemoryStream(output)); shader.Freeze(); return shader;
        }
        finally { Marshal.ReleaseComObject(code); if (error != null) Marshal.ReleaseComObject(error); }
    }
    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Blob { [PreserveSig] IntPtr GetBufferPointer(); [PreserveSig] UIntPtr GetBufferSize(); }
    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int D3DCompile(byte[] data, UIntPtr length, string name, IntPtr defines, IntPtr include, string entry, string target, uint flags, uint flags2, out Blob code, out Blob? error);
}

// Experimental capture transport. Only visible glass bounds are captured; pixels stay local.
// Replace this transport with GPU capture before treating the renderer as production-ready.
internal sealed class DesktopCapture : IDisposable
{
    private IntPtr dc, bitmap, old, bits;
    private int width, height;
    private ulong lastHash;
    public DateTime LastChanged { get; private set; } = DateTime.UtcNow;
    public long Frames { get; private set; }
    public long BufferAllocations { get; private set; }
    public BitmapSource? Capture(int left, int top, int w, int h)
    {
        lock (this) return CaptureCore(left, top, w, h);
    }
    private BitmapSource? CaptureCore(int left, int top, int w, int h)
    {
        if (w < 1 || h < 1 || w > 10000 || h > 3000) return null;
        var screen = Native.GetDC(IntPtr.Zero);
        try
        {
            if (width != w || height != h || dc == IntPtr.Zero)
            {
                Dispose(); width = w; height = h; dc = Native.CreateCompatibleDC(screen);
                var info = new Native.BitmapInfo { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
                bitmap = Native.CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero || dc == IntPtr.Zero) { Dispose(); return null; }
                old = Native.SelectObject(dc, bitmap);
                BufferAllocations++;
            }
            if (!Native.BitBlt(dc, 0, 0, w, h, screen, left, top, 0x40CC0020)) return null;
            Native.GdiFlush();
            // Avoid re-uploading static desktop pixels. A sparse signature is sufficient for
            // this prototype's idle backoff, and full updates resume with user input.
            ulong hash = unchecked((ulong)(left * 397L + top * 17L + w * 7L + h));
            for (int yy = 0; yy < h; yy += Math.Max(1, h / 16))
                for (int xx = 0; xx < w; xx += Math.Max(1, w / 32))
                    hash = unchecked((hash ^ (uint)Marshal.ReadInt32(bits, (yy * w + xx) * 4)) * 1099511628211);
            if (hash == lastHash) return null;
            lastHash = hash; LastChanged = DateTime.UtcNow;
            var frame = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bits, w * h * 4, w * 4);
            frame.Freeze(); Frames++; return frame;
        }
        finally { Native.ReleaseDC(IntPtr.Zero, screen); }
    }
    public void Dispose()
    {
        lock (this) DisposeCore();
    }
    private void DisposeCore()
    {
        if (old != IntPtr.Zero && dc != IntPtr.Zero) Native.SelectObject(dc, old);
        if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
        if (dc != IntPtr.Zero) Native.DeleteDC(dc);
        bitmap = old = dc = bits = IntPtr.Zero; width = height = 0;
    }
}

internal sealed class GlassPanel : Grid, IDisposable
{
    private readonly Image backdrop = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas opticalLayer = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly BlurEffect softness = new() { Radius = 7, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
    private readonly Border fallback = new() { Background = new SolidColorBrush(Color.FromRgb(34, 39, 48)), IsHitTestVisible = false };
    private readonly Border rim = new() { BorderThickness = new Thickness(.6), BorderBrush = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)), IsHitTestVisible = false };
    private readonly DesktopCapture capture = new();
    private GlassEffect? shader;
    private bool pending, disposed;
    private DateTime nextCapture;
    private readonly RectangleGeometry roundedClip = new();
    private double surfaceX, surfaceY;
    public Window? CaptureHost { get; set; }
    public long CaptureBufferAllocations => capture.BufferAllocations;
    public Rect ShapeBounds => roundedClip.Rect;
    public double ShapeRadius => roundedClip.RadiusX;
    public bool BottomBorderOnly { get; set; }
    public bool Live { get; set; } = true;
    public double Radius { get; set; } = 26;
    public double TintAmount { get; set; } = .40;
    public Panel Content { get; } = new Grid();
    public GlassPanel()
    {
        try { if (RenderCapability.IsPixelShaderVersionSupported(3, 0)) { shader = new GlassEffect(); opticalLayer.Effect = shader; backdrop.Effect = softness; } }
        catch (Exception ex) { Storage.Log("Glass shader unavailable: " + ex.Message); }
        opticalLayer.Children.Add(backdrop);
        Children.Add(fallback); Children.Add(opticalLayer); Children.Add(rim); Children.Add(Content);
        // One clip owns the optical layer, fallback, highlights, AND content. Native
        // window-region updates and late-arriving captures cannot expose square corners.
        Clip = roundedClip;
        SizeChanged += (_, _) => UpdateGeometry();
    }
    public void SetShape(double x, double y, double width, double height, double radius)
    {
        surfaceX = x; surfaceY = y; Width = width; Height = height; Radius = radius;
        UpdateGeometry();
    }
    public void UpdateGeometry()
    {
        double w = double.IsNaN(Width) ? ActualWidth : Width;
        double h = double.IsNaN(Height) ? ActualHeight : Height;
        roundedClip.Rect = new Rect(0, 0, Math.Max(0, w), Math.Max(0, h));
        roundedClip.RadiusX = roundedClip.RadiusY = Radius;
        rim.BorderThickness = BottomBorderOnly ? new Thickness(0, 0, 0, .6) : new Thickness(.6);
        rim.CornerRadius = fallback.CornerRadius = new CornerRadius(Radius);
        if (CaptureHost != null)
        {
            // A stable host-sized image is cropped, never stretched into the changing pill.
            backdrop.Width = CaptureHost.Width; backdrop.Height = CaptureHost.Height;
            Canvas.SetLeft(backdrop, -surfaceX); Canvas.SetTop(backdrop, -surfaceY);
        }
        if (shader == null) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        shader.Size = new Point(Math.Max(1, w * dpi.DpiScaleX), Math.Max(1, h * dpi.DpiScaleY));
        shader.BottomOnly = BottomBorderOnly ? 1 : 0;
        shader.Radius = Radius * dpi.DpiScaleX;
        shader.Depth = (Radius * .25 + h * .018) * dpi.DpiScaleX;
        shader.Blur = .5 * dpi.DpiScaleX;
        softness.Radius = 7 + 7 * Math.Clamp((h - 44) / 232, 0, 1);
        shader.Tint = Color.FromArgb((byte)(255 * TintAmount), 18, 23, 32);
    }
    public async void Refresh()
    {
        if (disposed || pending) return;
        if (!IsVisible || ActualWidth < 1 || ActualHeight < 1 || !Live || shader == null) { backdrop.Visibility = Visibility.Hidden; return; }
        bool active = Native.RecentInput();
        if (!active && DateTime.UtcNow < nextCapture) return;
        nextCapture = DateTime.UtcNow.AddMilliseconds(!active && DateTime.UtcNow - capture.LastChanged > TimeSpan.FromSeconds(2) ? 250 : 33);
        backdrop.Visibility = Visibility.Visible;
        if (CaptureHost == null) return;
        var origin = CaptureHost.PointToScreen(new Point(0, 0)); var dpi = VisualTreeHelper.GetDpi(this);
        int x = (int)Math.Round(origin.X), y = (int)Math.Round(origin.Y), w = (int)Math.Ceiling(CaptureHost.Width * dpi.DpiScaleX), h = (int)Math.Ceiling(CaptureHost.Height * dpi.DpiScaleY);
        pending = true;
        try
        {
            var frame = await Task.Run(() => capture.Capture(x, y, w, h));
            if (!disposed && Live && CaptureHost.IsVisible)
            {
                var currentOrigin = CaptureHost.PointToScreen(new Point(0, 0));
                if (frame != null && Math.Abs(currentOrigin.X - x) < 1 && Math.Abs(currentOrigin.Y - y) < 1) backdrop.Source = frame;
            }
        }
        catch (Exception ex) { Storage.Log("Capture: " + ex.Message); }
        finally { pending = false; if (disposed) capture.Dispose(); }
    }
    public void Dispose() { disposed = true; if (!pending) capture.Dispose(); }
}
