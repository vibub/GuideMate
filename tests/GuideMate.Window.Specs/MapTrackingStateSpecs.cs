using System.Reflection;
using GuideMate.App;
using GuideMate.Core;

internal static class MapTrackingStateSpecs
{
    public static void Run()
    {
        const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic;
        var windowType = typeof(MainWindow);
        var laneType = windowType.GetNestedType("MapLane", BindingFlags.NonPublic)!;
        var unavailable = windowType.GetMethod("MapPositionUnavailableReason", methods)!;
        var reset = windowType.GetMethod("ResetMapLane", methods)!;
        var guide = Activator.CreateInstance(laneType, true)!;
        var game = Activator.CreateInstance(laneType, true)!;
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            Console.WriteLine("PASS map state: " + name); checks++;
        }
        FieldInfo Field(string name) => laneType.GetField(name, BindingFlags.Public | BindingFlags.Instance)!;
        void Set(object lane, string name, object? value) => Field(name).SetValue(lane, value);
        T Get<T>(object lane, string name) => (T)Field(name).GetValue(lane)!;
        string? Reason(long timestamp, double videoPosition = 0, double rate = 1) =>
            (string?)unavailable.Invoke(null, [guide, game, timestamp, videoPosition, rate]);
        var location = new MapLocation(1, 0, 200, 300, 240, 240, 1) { PlayerHeading = 15, HeadingConfidence = .95 };
        void Located(object lane, long stamp)
        {
            Set(lane, "Position", location); Set(lane, "Stamp", stamp); Set(lane, "VideoTime", 0d);
        }
        try
        {
            // 任务排队不计入截图图龄；本测试不创建窗口、用户资料或真实游戏进程。
            const long scheduledAt = 100, capturedAt = 300, completedAt = 650;
            Located(guide, capturedAt); Located(game, capturedAt);
            Check(completedAt - scheduledAt > 500 && Reason(completedAt) == null,
                "排队后取得的新截图按实际采集时间计算图龄，350毫秒有效而非任务年龄550毫秒过期");
            Check(Reason(800) == null, "游戏截图图龄恰好500毫秒仍有效");
            Check(Reason(801) == "game_expired", "游戏截图图龄501毫秒隐藏，不能长期冻结旧游戏坐标");

            Located(game, 1300);
            Check(Reason(1300) == null, "攻略截图图龄恰好1000毫秒仍有效");
            Check(Reason(1301) == "guide_expired", "攻略截图图龄1001毫秒隐藏");

            Located(guide, capturedAt); Located(game, capturedAt);
            Set(guide, "Position", null);
            Check(Reason(completedAt) == "guide_position_unavailable", "攻略尚未确认时不显示标记");
            Located(guide, capturedAt); Set(game, "Position", null);
            Check(Reason(completedAt) == "game_position_unavailable", "游戏无当前定位时不显示攻略旧点");
            Located(game, capturedAt);
            Check(Reason(completedAt, 1) == null && Reason(completedAt, 1.001) == "video_time_mismatch",
                "视频进度偏差超过正常播放容差后隐藏");
            Check(Reason(completedAt, 1.6, 2) == null && Reason(completedAt, 1.601, 2) == "video_time_mismatch",
                "视频进度容差随当前倍速变化且保留边界");

            var confirmation = Get<MapTrackConfirmation>(guide, "Confirmation");
            Check(confirmation.Accept(location) == null && confirmation.Accept(location) == location,
                "首次位置仍须连续两帧确认");
            var distant = location with { Tx = location.Tx + 500 };
            Check(confirmation.Accept(distant) == null && confirmation.Accept(distant) == distant,
                "远距离跳变仍须连续两帧确认，不能由显示宽限绕过");
            var heading = Get<MapHeadingConfirmation>(guide, "Heading");
            heading.Accept(15, .95, capturedAt);
            heading.Accept(150, .95, capturedAt + 100);
            var revision = Get<int>(guide, "Revision");
            reset.Invoke(null, [guide]);
            Check(Get<MapLocation?>(guide, "Position") == null && Get<long>(guide, "Stamp") == 0
                && Get<int>(guide, "Revision") == revision + 1,
                "来源失效重置清除位置与图龄，并使旧后台任务版本失效");
            Check(Reason(completedAt) == "guide_position_unavailable",
                "来源重置后即使旧图龄原本有效也不能继续显示");
            Check(confirmation.Accept(distant) == null, "来源重置清空原位置的两帧确认状态");
            Check(heading.Accept(240, .95, capturedAt + 200) == 240,
                "来源重置清空原方向及待确认转向，不继承上个视频方向");
            Located(guide, capturedAt); reset.Invoke(null, [game]);
            Check(Reason(completedAt) == "game_position_unavailable", "游戏来源重置后立即失去显示资格");
            Console.WriteLine($"PASS {checks} synthetic map state checks; no desktop windows, live game or user profile used");
        }
        finally
        {
            Get<IDisposable>(guide, "Tracker").Dispose();
            Get<IDisposable>(game, "Tracker").Dispose();
        }
    }
}