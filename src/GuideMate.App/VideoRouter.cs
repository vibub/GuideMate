using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace GuideMate.App;

internal sealed class VideoRouter
{
    private sealed record VideoState(double Area, long Seen, string Json);
    private readonly CoreWebView2 _core;
    private readonly string _script;
    private readonly Dictionary<uint, CoreWebView2Frame> _frames = [];
    private readonly Dictionary<uint, VideoState> _states = [];
    private uint _active;
    public uint ActiveFrame => _active;
    public event Action<string>? MessageReceived;
    public event Action? ActiveChanged;

    public VideoRouter(CoreWebView2 core, string script)
    {
        _core = core; _script = script;
        core.WebMessageReceived += (_, e) => Receive(0, e.WebMessageAsJson);
        core.FrameCreated += (_, e) => Attach(e.Frame);
        core.NavigationStarting += (_, _) => { _states.Clear(); _active = 0; };
    }

    private void Attach(CoreWebView2Frame frame)
    {
        var id = frame.FrameId;
        if (!_frames.TryAdd(id, frame)) return;
        frame.FrameCreated += (_, e) => Attach(e.Frame);
        frame.WebMessageReceived += (_, e) => Receive(id, e.WebMessageAsJson);
        frame.NavigationStarting += (_, _) => RemoveState(id);
        frame.Destroyed += (_, _) => { _frames.Remove(id); RemoveState(id); };
        frame.DOMContentLoaded += async (_, _) =>
        {
            try { await frame.ExecuteScriptAsync(_script); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { RemoveState(id); }
        };
    }

    private void RemoveState(uint id)
    {
        _states.Remove(id);
        if (_active == id) SelectActive();
    }

    private void SelectActive()
    {
        var now = Environment.TickCount64;
        foreach (var pair in _states.Where(p => now - p.Value.Seen > 1500).ToArray()) _states.Remove(pair.Key);
        var selected = _states.OrderByDescending(p => p.Value.Area).ThenBy(p => p.Key).FirstOrDefault();
        if (_active != selected.Key) { _active = selected.Key; ActiveChanged?.Invoke(); }
        if (selected.Value != null) MessageReceived?.Invoke(selected.Value.Json);
        else MessageReceived?.Invoke("{\"type\":\"no-video\"}");
    }

    private void Receive(uint id, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return;
            if (type.GetString() == "no-video") { RemoveState(id); return; }
            if (type.GetString() != "state") { if (id == _active) MessageReceived?.Invoke(json); return; }
            if (!root.TryGetProperty("area", out var area) || !area.TryGetDouble(out var number) || !double.IsFinite(number) || number <= 0) return;
            _states[id] = new(number, Environment.TickCount64, json);
            SelectActive();
        }
        catch (JsonException) { }
    }

    public Task<string> ExecuteAsync(string script, bool topLevel = false)
    {
        if (!topLevel && _active != 0 && _frames.TryGetValue(_active, out var frame)) return frame.ExecuteScriptAsync(script);
        return _core.ExecuteScriptAsync(script);
    }
}
