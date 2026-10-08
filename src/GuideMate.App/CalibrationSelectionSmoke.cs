namespace GuideMate.App;

internal sealed partial class VisionCalibrationWindow
{
    internal void VerifySelectionInteraction(List<string> checks, string screenshot)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("Calibration selection: " + label);
            checks.Add(label);
        }
        var original = _region; var originalResult = OnlineResult;
        var width = _info!.Width; var height = _info.Height;
        ArrowRegion Region(Rect r) => new(r.X / width, r.Y / height, r.Width / width, r.Height / height);
        bool Near(double a, double b) => Math.Abs(a - b) < 2;
        void Seed(Rect r) { EndSelectionDrag(true); _region = Region(r); DrawRegion(); }
        void Drag(Point start, Point end)
        { BeginSelectionDrag(start); UpdateSelectionDrag(end); EndSelectionDrag(false); }
        var seed = new Rect(width * 0.3, height * 0.3, 160, 160);
        try
        {
            Seed(seed); var before = SelectionRect();
            var center = new Point(before.Left + before.Width / 2, before.Top + before.Height / 2);
            BeginSelectionDrag(center);
            Check(_dragKind == SelectionDrag.Move && _selection.IsMouseCaptured, "selection interior starts a captured move");
            UpdateSelectionDrag(center + new Vector(18, 24));
            UpdateSelectionDrag(center + new Vector(36, 48)); EndSelectionDrag(false);
            var moved = SelectionRect();
            Check(Near(moved.X, before.X + 36) && Near(moved.Y, before.Y + 48) && moved.Size == before.Size,
                "selection move uses total pointer displacement without accumulating earlier deltas");
            Check(!_selection.IsMouseCaptured && _dragOrigin == null, "selection release ends mouse capture");
            foreach (var (gx, gy, handle) in _handles)
            {
                Seed(seed); before = SelectionRect();
                var grip = new Point(before.Left + (gx + 1) * before.Width / 2, before.Top + (gy + 1) * before.Height / 2);
                Check(HitSelection(grip) == (SelectionDrag.Resize, gx, gy), $"selection resize grip hit testing ({gx},{gy})");
                Drag(grip, grip + new Vector(gx * 40, gy * 40));
                var resized = SelectionRect();
                Check(Near(resized.Width, before.Width + 40) && resized.Width == resized.Height && _region.IsValid,
                    $"selection expands as a bounded square from grip ({gx},{gy})");
                var anchorX = gx < 0 ? before.Right : gx > 0 ? before.Left : before.Left + before.Width / 2;
                var anchorY = gy < 0 ? before.Bottom : gy > 0 ? before.Top : before.Top + before.Height / 2;
                Check(Near(gx < 0 ? resized.Right : gx > 0 ? resized.Left : resized.Left + resized.Width / 2, anchorX)
                    && Near(gy < 0 ? resized.Bottom : gy > 0 ? resized.Top : resized.Top + resized.Height / 2, anchorY),
                    $"selection resize retains its opposite anchor ({gx},{gy})");
                Check(Near(handle.ActualWidth * SelectionScale(), 10) || Near(handle.Width * SelectionScale(), 10),
                    $"selection handles keep a usable size inside scaled preview ({gx},{gy})");
            }
            Seed(seed); before = SelectionRect();
            Drag(new(before.Right, before.Bottom), new(-1000, -1000));
            Check(SelectionRect().Width == 8 && _region.IsValid, "selection resize stops at minimum size without flipping");
            Seed(seed); before = SelectionRect();
            Drag(new(before.Right, before.Bottom), new(width * 2, height * 2));
            Check(_region.IsValid && (Near(SelectionRect().Right, width) || Near(SelectionRect().Bottom, height)),
                "selection resize clamps at video boundary");
            Seed(seed); before = SelectionRect();
            Drag(new(before.Left + 80, before.Top + 80), new(-1000, -1000));
            Check(SelectionRect().TopLeft == new Point(0, 0) && SelectionRect().Size == before.Size, "selection move clamps at top-left boundary");
            Seed(seed); Drag(new(seed.Left + 80, seed.Top + 80), new(width * 2, height * 2));
            Check(_region.IsValid && Near(SelectionRect().Right, width) && Near(SelectionRect().Bottom, height),
                "selection move clamps at bottom-right boundary");
            Seed(seed); before = SelectionRect();
            Drag(new(200, 180), new(80, 60));
            Check(SelectionRect() == new Rect(80, 60, 120, 120), "blank-space reverse drag creates a new square selection");
            Seed(seed); before = SelectionRect();
            BeginSelectionDrag(new(before.Left + 80, before.Top + 80)); UpdateSelectionDrag(new(before.Left + 120, before.Top + 140));
            _selection.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(SelectionRect() == before && _dragOrigin == null && !_selection.IsMouseCaptured,
                "Escape restores pre-drag selection and releases capture");
            BeginSelectionDrag(new(before.Left + 80, before.Top + 80)); UpdateSelectionDrag(new(before.Left + 120, before.Top + 140));
            _selection.ReleaseMouseCapture();
            Check(SelectionRect() == before && _dragOrigin == null, "lost mouse capture cancels rather than leaving an active move");
            Check(OnlineResult == originalResult, "editing selection does not save or replace calibration before confirmation");
            VerifyLayoutAndCapture(screenshot);
        }
        finally { EndSelectionDrag(true); _region = original; DrawRegion(); DetectPreview(); }
    }
}
