using System.Diagnostics;
using GuideMate.Core;
using GuideMate.Vision;
using OpenCvSharp;

internal static class BuiltInMapChecks
{
    public static async Task RunAsync(string archive, string preview)
    {
        var root = Path.Combine(Path.GetTempPath(), "GuideMate-map-test-" + Guid.NewGuid().ToString("N"));
        var directory = BuiltInMapAssets.CacheDirectory(root);
        try
        {
            if (archive == "download") await BuiltInMapAssets.EnsureAsync(root, null, default);
            else await BuiltInMapAssets.InstallArchiveAsync(archive, directory);
            if (!await BuiltInMapAssets.IsReadyAsync(directory)) throw new Exception("安装后校验失败");
            Console.WriteLine("PASS upstream archive hash, exact extraction, feature file hashes");
            if (Directory.GetFiles(directory).Length != 2) throw new Exception("安装遗留临时文件或安装了多余资源");
            if (await BuiltInMapAssets.EnsureAsync(root, null, default) != directory) throw new Exception("离线缓存未命中");
            Console.WriteLine("PASS offline cache reuse");
            var timer = Stopwatch.StartNew();
            using var locator = new MapLocator(directory);
            Console.WriteLine($"PASS upstream feature decoding and index construction: {timer.ElapsedMilliseconds} ms");
            using var map = Cv2.ImRead(preview);
            var width = map.Width; var height = map.Height;
            var successes = 0;
            for (var y = 600; y < height - 200 && successes < 3; y += 450)
            for (var x = 600; x < width - 200 && successes < 3; x += 450)
            {
                using var crop = new Mat(map, new Rect(x, y, 160, 160));
                using var query = new Mat(); Cv2.Resize(crop, query, new(320, 320));
                timer.Restart(); var result = locator.Locate(query);
                if (result == null) continue;
                var error = Math.Sqrt(Math.Pow(result.Position.X / 8 - x - 80, 2) + Math.Pow(result.Position.Y / 8 - y - 80, 2));
                if (error > 5) throw new Exception($"地图纹理错误定位：({x},{y}) error={error}");
                successes++; Console.WriteLine($"PASS upstream terrain patch ({x},{y}), error {error:0.00} preview px, {timer.ElapsedMilliseconds} ms");
            }
            if (successes < 3) throw new Exception("真实地图纹理不足三个样本定位成功");
            using var blank = new Mat(240, 240, MatType.CV_8UC3, Scalar.Black);
            if (locator.Locate(blank) != null) throw new Exception("空白画面误匹配");
            Console.WriteLine("PASS upstream map smoke checks; map texture samples only, not game screenshots");
        }
        finally
        {
            foreach (var name in new[] { BuiltInMapAssets.KeypointsFile, BuiltInMapAssets.DescriptorsFile })
            { var path = Path.Combine(directory, name); if (File.Exists(path)) File.Delete(path); }
            if (Directory.Exists(directory)) Directory.Delete(directory);
            if (Directory.Exists(Path.Combine(root, "MapResources"))) Directory.Delete(Path.Combine(root, "MapResources"));
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }
}
