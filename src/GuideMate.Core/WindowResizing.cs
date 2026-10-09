namespace GuideMate.Core;

[Flags]
public enum ResizeEdges { Left = 1, Right = 2, Top = 4, Bottom = 8 }

public static class WindowResizing
{
    public static WindowPlacement Calculate(WindowPlacement origin, ResizeEdges edges,
        double dx, double dy, double minWidth, double minHeight)
    {
        var left = (edges & ResizeEdges.Left) != 0;
        var right = (edges & ResizeEdges.Right) != 0;
        var top = (edges & ResizeEdges.Top) != 0;
        var bottom = (edges & ResizeEdges.Bottom) != 0;
        var width = Math.Max(minWidth, origin.Width + (left ? -dx : right ? dx : 0));
        var height = Math.Max(minHeight, origin.Height + (top ? -dy : bottom ? dy : 0));
        return new(origin.Left + (left ? origin.Width - width : 0),
            origin.Top + (top ? origin.Height - height : 0), width, height);
    }
}
