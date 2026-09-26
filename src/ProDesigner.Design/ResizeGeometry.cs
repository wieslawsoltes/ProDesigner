namespace ProDesigner.Design;

[Flags]
public enum ResizeEdges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

/// <summary>Host-independent resize mathematics shared by pointer and keyboard tools.</summary>
public static class ResizeGeometry
{
    public static IReadOnlyList<(ResizeEdges Edges, double X, double Y)> Handles(DesignRect bounds) =>
    [
        (ResizeEdges.Left | ResizeEdges.Top, bounds.X, bounds.Y),
        (ResizeEdges.Top, bounds.X + bounds.Width / 2, bounds.Y),
        (ResizeEdges.Right | ResizeEdges.Top, bounds.Right, bounds.Y),
        (ResizeEdges.Right, bounds.Right, bounds.Y + bounds.Height / 2),
        (ResizeEdges.Right | ResizeEdges.Bottom, bounds.Right, bounds.Bottom),
        (ResizeEdges.Bottom, bounds.X + bounds.Width / 2, bounds.Bottom),
        (ResizeEdges.Left | ResizeEdges.Bottom, bounds.X, bounds.Bottom),
        (ResizeEdges.Left, bounds.X, bounds.Y + bounds.Height / 2)
    ];
    public static ResizeEdges HitTest(DesignRect bounds, double x, double y, double tolerance = 8) =>
        Handles(bounds).Where(h => Math.Abs(h.X - x) <= tolerance && Math.Abs(h.Y - y) <= tolerance)
            .OrderBy(h => Math.Abs(h.X - x) + Math.Abs(h.Y - y)).Select(h => h.Edges).FirstOrDefault();
    public static DesignRect Resize(DesignRect original, ResizeEdges edges, double dx, double dy, bool snap = true, bool preserveAspect = false)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new ArgumentOutOfRangeException(nameof(dx));
        var left = original.X; var top = original.Y; var right = original.Right; var bottom = original.Bottom;
        if (edges.HasFlag(ResizeEdges.Left)) left = Math.Min(right - 8, original.X + dx);
        if (edges.HasFlag(ResizeEdges.Right)) right = Math.Max(left + 8, original.Right + dx);
        if (edges.HasFlag(ResizeEdges.Top)) top = Math.Min(bottom - 8, original.Y + dy);
        if (edges.HasFlag(ResizeEdges.Bottom)) bottom = Math.Max(top + 8, original.Bottom + dy);
        if (snap)
        {
            if (edges.HasFlag(ResizeEdges.Left)) left = Math.Min(right - 8, LayoutEngine.Snap(left));
            if (edges.HasFlag(ResizeEdges.Right)) right = Math.Max(left + 8, LayoutEngine.Snap(right));
            if (edges.HasFlag(ResizeEdges.Top)) top = Math.Min(bottom - 8, LayoutEngine.Snap(top));
            if (edges.HasFlag(ResizeEdges.Bottom)) bottom = Math.Max(top + 8, LayoutEngine.Snap(bottom));
        }
        var width = right - left; var height = bottom - top;
        if (preserveAspect && original.Width > 0 && original.Height > 0)
        {
            var ratio = original.Width / original.Height;
            if (Math.Abs(width - original.Width) / original.Width >= Math.Abs(height - original.Height) / original.Height) height = width / ratio;
            else width = height * ratio;
            width = Math.Max(8, width); height = Math.Max(8, height);
            if (edges.HasFlag(ResizeEdges.Left)) left = original.Right - width;
            if (edges.HasFlag(ResizeEdges.Top)) top = original.Bottom - height;
        }
        return new(left, top, width, height);
    }
}
