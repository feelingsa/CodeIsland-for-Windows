using System.Windows;

namespace CodeIsland.Windows;

[Flags]
public enum DockEdges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

public sealed record PanelDockPlacement(DockEdges Edges, double Left, double Top, CornerRadius Corners);

public static class PanelDocking
{
    public static PanelDockPlacement Resolve(Rect workArea, System.Windows.Size panel, System.Windows.Point position,
        double tolerance = 18, double radius = 24)
    {
        var edges = DockEdges.None;
        if (Math.Abs(position.X - workArea.Left) <= tolerance) edges |= DockEdges.Left;
        if (Math.Abs(position.Y - workArea.Top) <= tolerance) edges |= DockEdges.Top;
        if (Math.Abs(position.X + panel.Width - workArea.Right) <= tolerance) edges |= DockEdges.Right;
        if (Math.Abs(position.Y + panel.Height - workArea.Bottom) <= tolerance) edges |= DockEdges.Bottom;
        return Place(workArea, panel, position, edges, radius);
    }

    public static PanelDockPlacement Place(Rect workArea, System.Windows.Size panel, System.Windows.Point position,
        DockEdges edges, double radius = 24)
    {
        var left = edges.HasFlag(DockEdges.Left) ? workArea.Left
            : edges.HasFlag(DockEdges.Right) ? workArea.Right - panel.Width : position.X;
        var top = edges.HasFlag(DockEdges.Top) ? workArea.Top
            : edges.HasFlag(DockEdges.Bottom) ? workArea.Bottom - panel.Height : position.Y;
        // A larger screen-facing radius creates the shoulder curve used by the
        // reference design while the flat middle section remains flush to the edge.
        var dockRadius = radius + 10;
        var topLeft = edges.HasFlag(DockEdges.Top) || edges.HasFlag(DockEdges.Left) ? dockRadius : radius;
        var topRight = edges.HasFlag(DockEdges.Top) || edges.HasFlag(DockEdges.Right) ? dockRadius : radius;
        var bottomRight = edges.HasFlag(DockEdges.Bottom) || edges.HasFlag(DockEdges.Right) ? dockRadius : radius;
        var bottomLeft = edges.HasFlag(DockEdges.Bottom) || edges.HasFlag(DockEdges.Left) ? dockRadius : radius;
        return new PanelDockPlacement(edges, left, top,
            new CornerRadius(topLeft, topRight, bottomRight, bottomLeft));
    }
}
