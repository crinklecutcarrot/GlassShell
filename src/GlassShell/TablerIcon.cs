using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace GlassShell;

internal static class TablerIcon
{
    private static readonly Dictionary<string, Geometry[]> cache = new(StringComparer.Ordinal);
    public static Image Create(string name, double size = 18, Brush? brush = null)
    {
        if (!cache.TryGetValue(name, out var shapes))
        {
            var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/GlassShell;component/Assets/Icons/{name}.svg"))
                ?? throw new InvalidOperationException("Missing Tabler icon: " + name);
            using var stream = resource.Stream;
            var svg = XDocument.Load(stream);
            shapes = svg.Root!.Elements().Where(e => (string?)e.Attribute("stroke") != "none").Select(Parse).ToArray();
            foreach (var shape in shapes) shape.Freeze(); cache[name] = shapes;
        }
        var drawing = new DrawingGroup();
        var pen = new Pen(brush ?? Ui.White, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        // Preserve the SVG viewBox even when an icon does not reach all four edges.
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        foreach (var shape in shapes) drawing.Children.Add(new GeometryDrawing(null, pen, shape));
        drawing.Freeze();
        return new Image { Source = new DrawingImage(drawing), Width = size, Height = size, Stretch = Stretch.Uniform, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    }
    private static double N(XElement e, string name, double fallback = 0) => double.TryParse((string?)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static Geometry Parse(XElement element) => element.Name.LocalName switch
    {
        "path" => Geometry.Parse((string?)element.Attribute("d") ?? ""),
        "circle" => new EllipseGeometry(new Point(N(element, "cx"), N(element, "cy")), N(element, "r"), N(element, "r")),
        "ellipse" => new EllipseGeometry(new Point(N(element, "cx"), N(element, "cy")), N(element, "rx"), N(element, "ry")),
        "line" => new LineGeometry(new Point(N(element, "x1"), N(element, "y1")), new Point(N(element, "x2"), N(element, "y2"))),
        "rect" => new RectangleGeometry(new Rect(N(element, "x"), N(element, "y"), N(element, "width"), N(element, "height")), N(element, "rx"), N(element, "ry", N(element, "rx"))),
        "polyline" => Geometry.Parse("M" + (string?)element.Attribute("points")),
        "polygon" => Geometry.Parse("M" + (string?)element.Attribute("points") + "Z"),
        _ => throw new NotSupportedException("Unsupported Tabler SVG element: " + element.Name.LocalName),
    };
}
