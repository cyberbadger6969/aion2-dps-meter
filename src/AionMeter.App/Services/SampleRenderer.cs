using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.App.Windows;
using AionMeter.Core.Events;

namespace AionMeter.App.Services;

/// <summary>
/// Developer tool (<c>--render-sample out.png</c>): draws the overlay with a fixed party of every class into a PNG,
/// without showing a window, capturing traffic or touching settings and history — safe while the game is running.
/// </summary>
public static class SampleRenderer
{
    /// <param name="window">overlay (default), breakdown or history.</param>
    public static void Render(string path, string language, string window = "overlay")
    {
        var settings = new AppSettings { Transient = true, SaveHistory = false, MaxRows = 10, LayoutVersion = 99, Language = language };
        UiText.Use(settings.Language);
        using var meter = new MeterService(settings, captureFactory: null);
        Feed(meter);
        // Let boss portraits download (first run only; then they come from the cache).
        meter.Portraits.Get(SampleBossCode);
        if (window == "history")
            foreach (var entry in meter.History.List().Take(20)) meter.PortraitOf(entry);
        for (var i = 0; i < 80 && meter.Portraits.Busy; i++) Thread.Sleep(100);

        Window host;
        Action refresh;
        switch (window)
        {
            case "breakdown":
                var segment = meter.Tracker.Segments()[0].Id;
                var breakdown = new BreakdownWindow(new ViewModels.LiveFightView(meter.Tracker, segment), 2, meter.Data, meter.Icons, meter.Portraits)
                    { Width = 900, Height = 680 };
                host = breakdown;
                refresh = breakdown.ReloadNow;
                break;
            case "history":
                var history = new HistoryWindow(meter) { Width = 900, Height = 600 };
                host = history;
                refresh = history.Reload;
                break;
            case "timers":
                FeedTimers(meter);
                for (var i = 0; i < 80 && meter.Portraits.Busy; i++) Thread.Sleep(100);
                var timers = new BossTimersWindow(meter) { Width = 780, Height = 640 };
                host = timers;
                refresh = timers.ReloadNow;
                break;
            default:
                var overlay = new OverlayWindow(meter) { Width = 560, Height = 820 };
                host = overlay;
                refresh = overlay.Refresh;
                break;
        }
        refresh();
        if (host.Content is not FrameworkElement root) return;

        const double scale = 1.5;
        root.Measure(new Size(host.Width, host.Height));
        root.Arrange(new Rect(0, 0, host.Width, host.Height));
        root.UpdateLayout();
        refresh();
        root.UpdateLayout();

        var bmp = new RenderTargetBitmap((int)(host.Width * scale), (int)(host.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(host is OverlayWindow ? new SolidColorBrush(Color.FromRgb(0x2B, 0x3A, 0x2E)) : host.Background,
                null, new Rect(0, 0, host.Width, host.Height));
        bmp.Render(bg); // stand-in for the game behind the translucent card / the window background
        bmp.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private const int SampleBossCode = 2701250; // Balhash, a field boss

    // Altgard's field boss list from a real capture (00:30:07 on 2026-10-06, UTC+3) — the in-game map's timers.
    private const string AltgardList =
        "00005604000018009de30602729d660ea10100000199e30691939bc7645092c700ecdd467f04f30da1010000009fe3066d013f0ea1010000" +
        "00a0e3066efe360ea1010000009ae306c4e9010ea1010000009be306a54a140ea1010000009ee306bc74360ea1010000009ce30630852c0e" +
        "a101000001a1e30600a57447800a0dc800f0db45095a05d40da101000000a2e3061f365b0ea101000000a3e306d4eb670ea101000001a4e3" +
        "06413f42482dc5e5c700b6974683a4f40da101000000ace30650d2de0ea101000000a5e3068054070ea101000000a6e306dae65e0ea10100" +
        "0000a7e3061737050ea101000000a8e306001166e80ea101000000a9e306ef5f4d0ea101000000aae30668a8020ea101000000abe306c284" +
        "d30ea101000000ade306fbccc10ea101000000aee3061cfdc60ea101000000afe306bf00d70ea101000000b0e3063501d10ea10100000000" +
        "00";

    private static void FeedTimers(MeterService meter)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var capturedAt = new DateTimeOffset(2026, 10, 5, 21, 30, 7, 231, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var events = new List<GameEvent>();
        new Core.Protocol.PacketParser(meter.Data, events.Add) { TimeMs = capturedAt }
            .Handle(Core.Protocol.Opcodes.FieldBossList, Core.Protocol.Wire.FromHex(AltgardList));
        if (events.FirstOrDefault() is FieldBossListEvent list)
        {
            // Shifted so the capture reads as "just now".
            var shift = now - capturedAt;
            meter.Timers.OnList(list with { TimeMs = now, Slots = list.Slots.Select(s => s with { AtMs = s.AtMs + shift }).ToList(), ServerId = 2305 });
        }
        // One boss of another map, from a kill the meter saw.
        meter.Timers.OnNotice(new Core.Combat.BossNotice(2701250, 220014, null, Core.Combat.BossNoticeKind.Killed, now - 70 * 60_000, ServerId: 2305));
        meter.Timers.SetRespawn(2305, 2701250, 120);
        meter.Timers.SetWatch(2305, 2400800, true);
        foreach (var t in meter.Timers.All().Where(t => t.NpcCode > 0)) meter.Portraits.Get(t.NpcCode);
    }

    private static void Feed(MeterService meter)
    {
        var t = meter.Tracker;
        const uint boss = 900_001;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 20_000;
        (uint Id, string Name, GameClass Class, long Damage, int Skill)[] party =
        [
            (1, "Borgrim", GameClass.Gladiator, 9_890_000, 11020000),
            (2, "Sylvaen", GameClass.Elementalist, 8_310_000, 16040000),
            (3, "Talwyn", GameClass.Ranger, 5_590_000, 14050000),
            (4, "Aegisa", GameClass.Templar, 5_100_000, 12030000),
            (5, "Nyxara", GameClass.Assassin, 4_800_000, 13120000),
            (6, "Morwen", GameClass.Sorcerer, 4_300_000, 15020000),
            (7, "Celesta", GameClass.Cleric, 3_100_000, 17040000),
            (8, "Hymnar", GameClass.Chanter, 2_400_000, 18040000),
            (9, "Grohm", GameClass.Brawler, 1_900_000, 19020000),
        ];
        t.Process(new SelfIdentifiedEvent(now, 2, "Sylvaen", 2305, GameClass.Elementalist));
        foreach (var p in party.Where(p => p.Id != 2)) t.Process(new PlayerSeenEvent(now, p.Id, p.Name, 2305, p.Class));
        t.Process(new NpcSeenEvent(now, boss, SampleBossCode, 120_000_000));
        for (var i = 0; i < 10; i++)
            foreach (var p in party)
                t.Process(new DamageEvent(now + i * 2_000 + p.Id * 10, p.Id, boss, p.Skill, p.Damage / 10,
                    i % 3 == 0 ? HitFlags.Critical : HitFlags.None));
        t.Process(new NpcHpEvent(now + 19_000, boss, 70_000_000, 120_000_000));
    }
}
