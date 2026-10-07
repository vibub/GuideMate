using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using GuideMate.Core;
using OpenCvSharp;

namespace GuideMate.Vision;

public record VideoInfo(int Width, int Height, double Duration);
public record AnalysisProgress(double Time, double Duration, int Recognized, int Total);

public static class VideoAnalysis
{
    public static string? FindFfmpeg() => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(p => Path.Combine(p.Trim('"'), "ffmpeg.exe")).FirstOrDefault(File.Exists);

    private static Process Start(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new IOException("Could not start video decoder.");
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (InvalidOperationException) { } // Exit can race the cancellation callback.
    }

    public static async Task<VideoInfo> ProbeAsync(string ffmpeg, string path, CancellationToken cancellation = default)
    {
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        using var process = Start(probe, ["-v", "error", "-show_entries", "stream=codec_type,width,height:format=duration", "-of", "json", path]);
        using var registration = cancellation.Register(() => Stop(process));
        var output = process.StandardOutput.ReadToEndAsync(cancellation);
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            var error = await errors.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException(error);
            using var json = JsonDocument.Parse(await output.ConfigureAwait(false));
            var video = json.RootElement.GetProperty("streams").EnumerateArray().First(s => s.GetProperty("codec_type").GetString() == "video");
            var info = new VideoInfo(video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(),
                double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture));
            if (info.Width <= 0 || info.Height <= 0 || !double.IsFinite(info.Duration) || info.Duration <= 0 || info.Duration > 86400)
                throw new FormatException("Unsupported video dimensions or duration.");
            return info;
        }
        finally { Stop(process); await process.WaitForExitAsync().ConfigureAwait(false); }
    }

    public static async Task<byte[]> PreviewAsync(string ffmpeg, string path, double time, CancellationToken cancellation = default)
    {
        using var process = Start(ffmpeg, ["-nostdin", "-hide_banner", "-loglevel", "error", "-ss", time.ToString("R", CultureInfo.InvariantCulture),
            "-i", path, "-frames:v", "1", "-f", "image2pipe", "-c:v", "png", "pipe:1"]);
        using var registration = cancellation.Register(() => Stop(process));
        var errors = process.StandardError.ReadToEndAsync();
        using var bytes = new MemoryStream();
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(bytes, cancellation).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            var error = await errors.ConfigureAwait(false);
            if (process.ExitCode != 0 || bytes.Length == 0) throw new IOException("Video frame extraction failed: " + error);
            return bytes.ToArray();
        }
        finally { Stop(process); await process.WaitForExitAsync().ConfigureAwait(false); }
    }

    public static Rect PixelRegion(ArrowRegion region, VideoInfo info)
    {
        if (!region.IsValid) throw new FormatException("Select a player-arrow region inside the video.");
        var x = (int)Math.Round(region.X * info.Width) & ~1;
        var y = (int)Math.Round(region.Y * info.Height) & ~1;
        var size = Math.Min((int)Math.Round(region.Width * info.Width), (int)Math.Round(region.Height * info.Height)) & ~1;
        size = Math.Min(size, Math.Min(info.Width - x, info.Height - y)) & ~1;
        if (size < 8) throw new FormatException("Player-arrow region is too small.");
        return new(x, y, size, size);
    }

    public static async Task<VisualDirectionTrack> AnalyzeAsync(string ffmpeg, string path, VideoInfo info, ArrowRegion region,
        bool northLocked, double northAngle, IProgress<AnalysisProgress>? progress, CancellationToken cancellation)
    {
        var roi = PixelRegion(region, info);
        var file = new FileInfo(path);
        var track = new VisualDirectionTrack { SourcePath = file.FullName, SourceLength = file.Length, SourceModifiedUtcTicks = file.LastWriteTimeUtc.Ticks,
            Region = region, NorthLocked = northLocked, NorthAngle = northAngle };
        using var process = Start(ffmpeg, ["-nostdin", "-hide_banner", "-loglevel", "error", "-noautorotate", "-i", path, "-an", "-vf",
            $"fps=2:start_time=0,crop={roi.Width}:{roi.Height}:{roi.X}:{roi.Y},scale=160:160", "-pix_fmt", "bgr24", "-f", "rawvideo", "pipe:1"]);
        using var registration = cancellation.Register(() => Stop(process));
        var errors = process.StandardError.ReadToEndAsync();
        var buffer = new byte[160 * 160 * 3];
        using var frame = new Mat(160, 160, MatType.CV_8UC3);
        var recognized = 0;
        try
        {
            while (true)
            {
                var count = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellation).ConfigureAwait(false);
                if (count == 0) break;
                await process.StandardOutput.BaseStream.ReadExactlyAsync(buffer.AsMemory(count), cancellation).ConfigureAwait(false);
                Marshal.Copy(buffer, 0, frame.Data, buffer.Length);
                var result = ArrowDetector.Detect(frame);
                if (result != null) recognized++;
                track.Frames.Add(new(track.Frames.Count * track.Interval, result?.Angle, result?.Score ?? 0));
                if (track.Frames.Count % 20 == 0) progress?.Report(new(track.Frames[^1].Time, info.Duration, recognized, track.Frames.Count));
            }
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            var error = await errors.ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException("Video analysis failed: " + error);
            if (track.Frames.Count == 0) throw new IOException("Video decoder returned no frames.");
            if (!track.Matches(path)) throw new IOException("Video changed during analysis; please analyze it again.");
            progress?.Report(new(info.Duration, info.Duration, recognized, track.Frames.Count));
            return track;
        }
        finally { Stop(process); await process.WaitForExitAsync().ConfigureAwait(false); }
    }
}
