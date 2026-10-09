namespace GuideMate.Core;

[Flags]
public enum ResizeEdges { Left = 1, Right = 2, Top = 4, Bottom = 8 }

public static class WindowResizing
{
    public static WindowPlacement Calculate(WindowPlacement origin, ResizeEdges edges,
        double dx, double dy, double minWidth, double minHeight, double? aspectRatio = null)
    {
        var left = (edges & ResizeEdges.Left) != 0;
        var right = (edges & ResizeEdges.Right) != 0;
        var top = (edges & ResizeEdges.Top) != 0;
        var bottom = (edges & ResizeEdges.Bottom) != 0;
        var horizontal = left || right;
        var vertical = top || bottom;
        var width = origin.Width + (left ? -dx : right ? dx : 0);
        var height = origin.Height + (top ? -dy : bottom ? dy : 0);
        if (aspectRatio is { } ratio)
        {
            // Project corner movement onto the ratio line; either mouse axis can resize.
            height = horizontal && vertical ? (ratio * width + height) / (ratio * ratio + 1)
                : horizontal ? width / ratio : height;
            height = Math.Max(Math.Max(minHeight, minWidth / ratio), height);
            width = height * ratio;
        }
        else
        {
            width = Math.Max(minWidth, width); height = Math.Max(minHeight, height);
        }
        return new(origin.Left + (left ? origin.Width - width : !horizontal ? (origin.Width - width) / 2 : 0),
            origin.Top + (top ? origin.Height - height : !vertical ? (origin.Height - height) / 2 : 0), width, height);
    }
}
