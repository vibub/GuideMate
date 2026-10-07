using System.Diagnostics;
using System.Text.Json;
using GuideMate.Vision;
using OpenCvSharp;

internal static class ArrowRobustnessChecks
{
    private sealed record Result(string Name, double Expected, double? Angle, double? Error, double Milliseconds);
    private sealed record Report(int Cases, int Correct, int Incorrect, int Rejected, int NegativeCases, int FalsePositives,
        double MedianMilliseconds, double P95Milliseconds, List<Result> Results, List<string> NegativeDetections);

    public static int Run(string root, string output, string? baselinePath)
    {
        var results = new List<Result>();
        var negatives = 0;
        var negativeDetections = new List<string>();
        void Record(Mat image, string name, double expected, bool lowResolution)
        {
            var clock = Stopwatch.StartNew();
            var detection = ArrowDetector.Detect(image, lowResolution);
            clock.Stop();
            var error = detection == null ? (double?)null : Math.Abs((detection.Angle - expected + 540) % 360 - 180);
            results.Add(new(name, expected, detection?.Angle, error, clock.Elapsed.TotalMilliseconds));
        }
        foreach (var (time, expected) in new[] { (60, 50d), (300, 320d), (900, 108d), (120, 263d), (480, 270d), (720, 274d), (1080, 333d) })
        {
            var development = time is 60 or 300 or 900;
            using var source = Cv2.ImRead(Path.Combine(root, development ? $"genshin-01-{time}.png" : $"genshin-holdout-{time}.png"));
            using var crop = development ? new Mat(source, new Rect(210, 204, 140, 140)) : source.Clone();
            using var original = new Mat(); Cv2.Resize(crop, original, new(160, 160));
            Record(original, $"original-{time}", expected, false);
            foreach (var (size, brightness) in new[] { (160, 1d), (46, 1d), (32, 1d), (24, 1d), (46, 0.8) })
            {
                using var small = new Mat();
                Cv2.Resize(crop, small, new(size, size), interpolation: InterpolationFlags.Area);
                small.ConvertTo(small, MatType.CV_8UC3, brightness);
                Cv2.ImEncode(".jpg", small, out var jpeg, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 55)]);
                using var decoded = Cv2.ImDecode(jpeg, ImreadModes.Color);
                using var scaled = new Mat(); Cv2.Resize(decoded, scaled, new(160, 160));
                Record(scaled, $"compressed-{time}-{size}-{brightness}", expected, size < 60);
            }
        }
        using var arrow = new Mat(160, 160, MatType.CV_8UC3, Scalar.Black);
        Cv2.FillPoly(arrow, [new Point[] { new(80, 34), new(116, 115), new(80, 95), new(44, 115) }], new Scalar(255, 220, 20));
        for (var angle = 0; angle < 360; angle += 10)
        {
            using var rotation = Cv2.GetRotationMatrix2D(new Point2f(80, 80), -angle, 1);
            using var rotated = new Mat(); Cv2.WarpAffine(arrow, rotated, rotation, arrow.Size());
            rotated.ConvertTo(rotated, MatType.CV_8UC3, 0.75);
            Record(rotated, $"dimmed-synthetic-{angle}", angle, true);
        }
        foreach (var brightness in new[] { 1d, 0.75 })
        foreach (var size in new[] { 160, 32 })
        foreach (var kind in new[] { "blank", "circle", "triangle", "diamond", "star", "solid", "clipped" })
        {
            using var image = new Mat(160, 160, MatType.CV_8UC3, Scalar.Black);
            var cyan = new Scalar(255, 220, 20);
            switch (kind)
            {
                case "circle": Cv2.Circle(image, new(80, 80), 28, cyan, -1); break;
                case "triangle": Cv2.FillPoly(image, [new Point[] { new(80, 34), new(116, 115), new(44, 115) }], cyan); break;
                case "diamond": Cv2.FillPoly(image, [new Point[] { new(80, 40), new(120, 80), new(80, 120), new(40, 80) }], cyan); break;
                case "star":
                    var points = Enumerable.Range(0, 10).Select(i => new Point(
                        (int)Math.Round(80 + (i % 2 == 0 ? 40 : 18) * Math.Sin(i * Math.PI / 5)),
                        (int)Math.Round(80 - (i % 2 == 0 ? 40 : 18) * Math.Cos(i * Math.PI / 5)))).ToArray();
                    Cv2.FillPoly(image, [points], cyan); break;
                case "solid": image.SetTo(cyan); break;
                case "clipped": Cv2.FillPoly(image, [new Point[] { new(20, 34), new(56, 115), new(20, 95), new(-16, 115) }], cyan); break;
            }
            image.ConvertTo(image, MatType.CV_8UC3, brightness);
            using var small = new Mat(); Cv2.Resize(image, small, new(size, size), interpolation: InterpolationFlags.Area);
            Cv2.ImEncode(".jpg", small, out var jpeg, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 55)]);
            using var decoded = Cv2.ImDecode(jpeg, ImreadModes.Color);
            using var scaled = new Mat(); Cv2.Resize(decoded, scaled, new(160, 160));
            if (ArrowDetector.Detect(scaled, size < 60) != null)
                negativeDetections.Add($"{kind}, size {size}, brightness {brightness}");
            negatives++;
        }
        var timings = results.Select(r => r.Milliseconds).Order().ToArray();
        var report = new Report(results.Count, results.Count(r => r.Error < 20), results.Count(r => r.Error >= 20),
            results.Count(r => r.Angle == null), negatives, negativeDetections.Count, timings[timings.Length / 2], timings[(int)(timings.Length * 0.95)], results, negativeDetections);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report with { Results = [] }));
        var checks = negatives - negativeDetections.Count;
        if (baselinePath != null)
        {
            var baseline = JsonSerializer.Deserialize<Report>(File.ReadAllText(baselinePath))!;
            if (report.Cases != baseline.Cases || report.Correct < baseline.Correct + 4 || report.Incorrect > baseline.Incorrect)
                throw new Exception("Arrow robustness did not improve correct detections without increasing wrong headings.");
            if (report.FalsePositives > baseline.FalsePositives)
                throw new Exception("Non-arrow false positives increased.");
            var previous = baseline.Results.ToDictionary(r => r.Name);
            if (results.Any(r => previous[r.Name].Error < 20 && (r.Error == null || r.Error >= 20)))
                throw new Exception("An existing correct arrow detection regressed.");
            checks += 2;
            Console.WriteLine($"PASS correct detections {baseline.Correct}/{baseline.Cases} -> {report.Correct}/{report.Cases}; wrong headings {baseline.Incorrect} -> {report.Incorrect}; false positives {baseline.FalsePositives} -> {report.FalsePositives}/{negatives}");
        }
        return checks;
    }

    public static int ReviewOverlaps(string directory)
    {
        using var labels = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "labels.json")));
        var checks = 0;
        foreach (var label in labels.RootElement.EnumerateArray())
        {
            var file = label.GetProperty("path").GetString()!;
            using var image = Cv2.ImRead(Path.Combine(directory, file));
            var result = ArrowDetector.Detect(image);
            if (label.GetProperty("expected").ValueKind == JsonValueKind.Null)
            {
                Console.WriteLine($"OVERLAP {file}: no confident manual heading; excluded from angle checks");
                continue;
            }
            var expected = label.GetProperty("expected").GetDouble();
            var required = label.GetProperty("required").GetBoolean();
            var error = result == null ? (double?)null : Math.Abs((result.Angle - expected + 540) % 360 - 180);
            Console.WriteLine($"OVERLAP {file} " + JsonSerializer.Serialize(new { result, expected, error }));
            if ((required && result == null) || error >= 25)
                throw new Exception("Overlapping map icons caused an incorrect arrow heading: " + file);
            checks++;
        }
        return checks;
    }
}
