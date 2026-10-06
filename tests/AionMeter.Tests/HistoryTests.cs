using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Storage;

namespace AionMeter.Tests;

public sealed class HistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aionmeter-history-" + Guid.NewGuid().ToString("N"));

    private static FightRecord Fight(string title, int minute, bool boss, int selfPlace)
    {
        var combatants = new List<CombatantSnapshot>();
        for (var i = 1; i <= 3; i++)
            combatants.Add(new CombatantSnapshot((uint)i, $"P{i}", GameClass.Cleric, i == selfPlace, 1000 - i, 100 - i, 0.33, 10, 0.5, 50, 0));
        var summary = new EncounterSnapshot(
            Guid.NewGuid(), title, "Krao Cave", new DateTimeOffset(2026, 10, 5, 18, minute, 0, TimeSpan.Zero), 30_000, 30_000,
            false, EncounterEndReason.Kill, 3000, 100,
            boss ? new BossSnapshot(7, 2300243, title, 0, 1000) : null, combatants, boss ? 2300243 : 0);
        return new FightRecord(1, summary, []);
    }

    [Fact]
    public void Index_survives_restart_and_tracks_deletes()
    {
        var store = new HistoryStore(_dir);
        store.Save(Fight("Berk", 10, boss: true, selfPlace: 2));
        var trash = store.Save(Fight("Spider +3", 12, boss: false, selfPlace: 1));

        var reopened = new HistoryStore(_dir);
        var list = reopened.List();
        Assert.Equal(["Spider +3", "Berk"], list.Select(e => e.Title));
        Assert.True(list[1].IsBoss);
        Assert.Equal(2, list[1].SelfPlace);
        Assert.Equal("P2", list[1].SelfName);
        Assert.NotNull(reopened.Load(list[0].Path));

        reopened.Delete(trash);
        Assert.Single(new HistoryStore(_dir).List());
    }

    [Fact]
    public void Missing_index_is_rebuilt_from_fight_files()
    {
        var store = new HistoryStore(_dir);
        store.Save(Fight("Berk", 10, boss: true, selfPlace: 1));
        File.Delete(Path.Combine(_dir, "index.jsonl"));
        File.AppendAllText(Path.Combine(_dir, "index.jsonl"), "{ torn line");

        var list = new HistoryStore(_dir).List();
        Assert.Equal("Berk", Assert.Single(list).Title);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
