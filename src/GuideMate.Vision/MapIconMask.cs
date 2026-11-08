using OpenCvSharp;

namespace GuideMate.Vision;

internal static class MapIconMask
{
    // 只排除小型高亮标记及其边缘，不填补被遮挡的地形，也不把整片浅色山体涂黑。
    public static Mat Create(Mat image)
    {
        using var hsv = new Mat(); Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
        using var white = new Mat(); using var cyan = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 220), new Scalar(179, 60, 255), white);
        Cv2.InRange(hsv, new Scalar(80, 140, 190), new Scalar(106, 255, 255), cyan);
        Cv2.BitwiseOr(white, cyan, white);
        Cv2.FindContours(white, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var result = new Mat(image.Size(), MatType.CV_8UC1, Scalar.Black);
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour); var box = Cv2.BoundingRect(contour);
            if (area < 4 || area > image.Width * image.Height * .035 || Math.Max(box.Width, box.Height) > Math.Min(image.Width, image.Height) * .3) continue;
            Cv2.DrawContours(result, new[] { contour }, -1, Scalar.White, -1);
        }
        var padding = Math.Clamp((int)(Math.Min(image.Width, image.Height) * .012), 2, 8);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new(padding * 2 + 1, padding * 2 + 1));
        Cv2.Dilate(result, result, kernel);
        return result;
    }
}
