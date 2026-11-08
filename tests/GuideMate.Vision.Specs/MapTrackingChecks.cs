using GuideMate.Core;
using GuideMate.Vision;
using OpenCvSharp;
using System.Diagnostics;
using System.Text.Json;

internal static class MapTrackingChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            Console.WriteLine("PASS map: " + name); checks++;
        }
        using var atlas = new Mat(1000, 1200, MatType.CV_8UC3, new Scalar(90, 110, 70));
        var random = new Random(751);
        for (var i = 0; i < 2500; i++)
        {
            var p = new Point(random.Next(1200), random.Next(1000));
            Cv2.Circle(atlas, p, random.Next(2, 13), new Scalar(random.Next(255), random.Next(255), random.Next(255)), random.Next(1, 3));
        }
        var watch = Stopwatch.StartNew();
        using var locator = new MapLocator(atlas);
        using var guide = new Mat(atlas, new Rect(360, 250, 240, 240));
        using var player = new Mat(atlas, new Rect(410, 280, 240, 240));
        var guideFix = locator.Locate(guide); var playerFix = locator.Locate(player);
        Check(guideFix != null && Distance(guideFix.Position, new(480, 370)) < 2, "攻略独立定位到已知底图坐标");
        Check(playerFix != null && Distance(playerFix.Position, new(530, 400)) < 2, "玩家独立定位到已知底图坐标");
        Check(Distance(playerFix!.ToMinimap(guideFix!.Position), new(70, 90)) < 2, "两路坐标投影到玩家地图");
        using var compressed = new Mat();
        Cv2.Resize(guide, compressed, new(192, 192));
        Cv2.ImEncode(".jpg", compressed, out var jpeg, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 65)]);
        using var decoded = Cv2.ImDecode(jpeg, ImreadModes.Color);
        var scaled = locator.Locate(decoded);
        Check(scaled != null && Distance(scaled.Position, new(480, 370)) < 3, "缩放与有损压缩后位置一致");
        using var covered = guide.Clone();
        Cv2.Circle(covered, new(120, 120), 18, Scalar.White, -1);
        var masked = locator.Locate(covered);
        Check(masked != null && Distance(masked.Position, new(480, 370)) < 3, "中心玩家图标不影响定位");
        Check(locator.Locate(covered, true) == null, "无玩家箭头的大地图或过场不显示标记");
        using var hud = guide.Clone();
        Cv2.FillPoly(hud, [new Point[] { new(120, 108), new(129, 131), new(120, 125), new(111, 131) }], new Scalar(255, 220, 20));
        Check(locator.Locate(hud, true) != null, "中心玩家箭头存在时可定位小地图");
        using var blank = new Mat(240, 240, MatType.CV_8UC3, Scalar.Black);
        Check(locator.Locate(blank) == null, "黑帧不产生位置");
        using var unrelated = new Mat(240, 240, MatType.CV_8UC3, Scalar.Black);
        for (var i = 0; i < 300; i++) Cv2.Line(unrelated, new(random.Next(240), random.Next(240)), new(random.Next(240), random.Next(240)), Scalar.White, 1);
        Check(locator.Locate(unrelated) == null, "无关场景不产生位置");
        var confirmation = new MapTrackConfirmation();
        Check(confirmation.Accept(guideFix) == null && confirmation.Accept(guideFix) != null, "必须连续两帧确认");
        Check(confirmation.Accept(guideFix with { Tx = guideFix.Tx + 500 }) == null, "拒绝单帧远距离跳变");
        Check(confirmation.Accept(null) == null && confirmation.Accept(guideFix) == null, "丢失后必须重新确认");
        confirmation.Reset(); Check(confirmation.Accept(guideFix) == null, "切集后清空跟踪");
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Hotkeys\":{\"PlayPause\":\"Mouse4\"},\"Opacity\":0.7}")!;
        Check(old.MapTracking == null && old.Hotkeys["PlayPause"] == "Mouse4" && old.Opacity == .7, "旧资料不重置热键和透明度");
        old.MapTracking = new("map.png", new(.1, .1, .2, .2), new(.05, .05, .1, .1));
        var roundtrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(old))!;
        Check(roundtrip.MapTracking == old.MapTracking && roundtrip.Hotkeys["PlayPause"] == "Mouse4", "地图配置保存且保留原热键");
        var temporary = Path.Combine(Path.GetTempPath(), "GuideMate-map-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var archive = Path.Combine(temporary, "invalid.nupkg");
        var destination = Path.Combine(temporary, "resources");
        try
        {
            File.WriteAllText(archive, "invalid package");
            var rejected = false;
            try { BuiltInMapAssets.InstallArchiveAsync(archive, destination).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && !Directory.Exists(destination), "资源摘要错误时不安装、不覆盖缓存");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var cancelled = false;
            try { BuiltInMapAssets.InstallArchiveAsync(archive, destination, cancellation.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && !Directory.Exists(destination), "取消安装时不产生资源文件");
            Check(!BuiltInMapAssets.IsReadyAsync(destination).GetAwaiter().GetResult(), "不存在的资源不被当作离线缓存");
        }
        finally { File.Delete(archive); Directory.Delete(temporary); }
        Console.WriteLine($"Map synthetic checks: {checks}; elapsed {watch.ElapsedMilliseconds} ms (not a game benchmark)");
        return checks;
    }
    private static double Distance(MapPoint a, MapPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
