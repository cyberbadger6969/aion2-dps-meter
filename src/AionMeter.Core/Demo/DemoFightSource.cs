using AionMeter.Core.Events;

namespace AionMeter.Core.Demo;

/// <summary>
/// Synthesises a 5-player boss fight in real time so the overlay can be developed and checked without the game.
/// Uses actor ids and skill codes that do not collide with real ones.
/// </summary>
public sealed class DemoFightSource : IDisposable
{
    private sealed record Member(uint Id, string Name, GameClass Class, double Power, int[] Skills, bool Self = false);

    private const uint BossId = 9_000_001;
    private const uint SummonId = 9_000_101;

    private readonly Action<GameEvent> _sink;
    private readonly CancellationTokenSource _cts = new();
    private readonly Random _rng = new();
    private Task? _task;

    public DemoFightSource(Action<GameEvent> sink) => _sink = sink;

    public bool IsRunning => _task is { IsCompleted: false };

    public void Start(TimeSpan duration)
    {
        if (IsRunning) return;
        _task = Task.Run(() => Run(duration, _cts.Token));
    }

    private async Task Run(TimeSpan duration, CancellationToken ct)
    {
        Member[] party =
        [
            new(9_100_001, "Ilvane", GameClass.Elementalist, 0.95, [16010000, 16020000, 16030000, 16040000, 16050000], Self: true),
            new(9_100_002, "Borgrim", GameClass.Gladiator, 1.1, [11010000, 11020000, 11030000, 11040000]),
            new(9_100_003, "Talwyn", GameClass.Ranger, 1.0, [14010000, 14020000, 14030000, 14040000]),
            new(9_100_004, "Celesta", GameClass.Cleric, 0.75, [17010000, 17020000, 17030000]),
            new(9_100_005, "Hymnar", GameClass.Chanter, 0.55, [18010000, 18020000, 18030000]),
        ];

        var now = Now();
        _sink(new ZoneChangedEvent(now, 600001, "Krao Cave", true));
        _sink(new SelfIdentifiedEvent(now, party[0].Id, party[0].Name, 2001, party[0].Class));
        foreach (var m in party.Skip(1)) _sink(new PlayerSeenEvent(now, m.Id, m.Name, 2001, m.Class));
        _sink(new SummonSeenEvent(now, SummonId, party[0].Id, null, 9_900_001));

        var totalPower = party.Sum(p => p.Power);
        var targetDps = 2_600_000.0;                    // party DPS, in line with current Global hard-mode numbers
        long maxHp = (long)(targetDps * duration.TotalSeconds);
        long hp = maxHp;
        _sink(new NpcSeenEvent(now, BossId, 0, maxHp, "Ultimate Berk (demo)"));
        _sink(new BattleStateEvent(now, BossId, true));
        _sink(new NpcHpEvent(now, BossId, hp, maxHp));

        var lastHpPush = now;
        while (!ct.IsCancellationRequested && hp > 0)
        {
            await Task.Delay(_rng.Next(30, 90), ct).ConfigureAwait(false);
            now = Now();
            var m = party[_rng.Next(party.Length)];
            var skill = m.Skills[Math.Min(m.Skills.Length - 1, (int)Math.Floor(Math.Pow(_rng.NextDouble(), 1.6) * m.Skills.Length))];

            var flags = HitFlags.None;
            if (_rng.NextDouble() < 0.45) flags |= HitFlags.Critical;
            if (_rng.NextDouble() < 0.25) flags |= HitFlags.Back;
            else if (_rng.NextDouble() < 0.5) flags |= HitFlags.Front;
            if (_rng.NextDouble() < 0.3) flags |= HitFlags.Perfect;
            if (_rng.NextDouble() < 0.12) flags |= HitFlags.Heavy;
            var dot = _rng.NextDouble() < 0.12;
            if (dot) flags = HitFlags.Dot;

            // Each event averages out to the member's share of the target party DPS (≈ 16 events/s overall).
            var mean = targetDps / 16.0 * party.Length * m.Power / totalPower;
            var dmg = (long)(mean * (0.5 + _rng.NextDouble()) * ((flags & HitFlags.Critical) != 0 ? 1.5 : 1.0) * (dot ? 0.35 : 1.0));
            var hits = !dot && _rng.NextDouble() < 0.08 ? 3 : 1;
            var source = m.Self && _rng.NextDouble() < 0.15 ? SummonId : m.Id;
            if (source == SummonId) skill = 16900000;

            dmg = Math.Min(dmg, hp);
            hp -= dmg;
            _sink(new DamageEvent(now, source, BossId, skill, dmg, flags, hits));

            // Boss swings back at the party now and then.
            if (_rng.NextDouble() < 0.1)
                _sink(new DamageEvent(now, BossId, party[_rng.Next(2)].Id, 1, _rng.Next(800, 3000), HitFlags.None));

            if (now - lastHpPush >= 250 || hp == 0)
            {
                _sink(new NpcHpEvent(now, BossId, hp, maxHp));
                lastHpPush = now;
            }
        }
        if (hp <= 0) _sink(new BattleStateEvent(Now(), BossId, false));
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void Dispose()
    {
        _cts.Cancel();
        try { _task?.Wait(500); } catch (AggregateException) { }
        _cts.Dispose();
    }
}
