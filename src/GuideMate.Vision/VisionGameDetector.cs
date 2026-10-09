using GuideMate.Core;
using OpenCvSharp;

namespace GuideMate.Vision;

public record VisionGameDetection(VisionGame Game, ArrowDetection Arrow);

public static class VisionGameDetector
{
    public static VisionGameDetection? Detect(Mat bgr, bool lowResolution = false)
    {
        var genshin = ArrowDetector.Detect(bgr, lowResolution, VisionGame.Genshin);
        var endfield = ArrowDetector.Detect(bgr, lowResolution, VisionGame.Endfield);
        // Each detector has its own geometric score; competing matches are ambiguous.
        if (genshin != null && endfield == null) return new(VisionGame.Genshin, genshin);
        if (endfield != null && genshin == null) return new(VisionGame.Endfield, endfield);
        return null;
    }
}
