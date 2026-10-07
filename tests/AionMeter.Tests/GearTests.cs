using System.Text;
using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.Protocol;

namespace AionMeter.Tests;

/// <summary>Gear score ("GS") and combat power ("CP"): the party roster, the own record, and where they show.</summary>
public class GearTests
{
    private sealed record Member(string Name, ushort Server, uint Class, uint Level, uint Gear, ulong Power, byte[] Flags, byte[] Tail);

    /// <summary>A <c>02 97</c> body laid out as documented (see PacketParser.ParsePartyRoster), vacant slots at the end.</summary>
    private static byte[] Roster(Member[] members, int vacant = 0)
    {
        var b = new List<byte> { 1, 2, 3, 4 };                    // party key
        b.Add(5);
        b.AddRange("Raids"u8.ToArray());                         // party name
        b.Add(5);                                                // party size
        b.AddRange(BitConverter.GetBytes(600091u));              // dungeon
        b.AddRange([0, 0]);
        b.AddRange(BitConverter.GetBytes((2305UL << 48) | 1));  // leader dbid
        b.AddRange([0, 0, 0]);
        b.Add((byte)(members.Length + vacant));                  // member count
        for (var i = 0; i < members.Length; i++)
        {
            var m = members[i];
            b.Add(0x2C);
            b.Add((byte)(i + 1));
            b.AddRange(BitConverter.GetBytes(((ulong)m.Server << 48) | (ulong)(1000 + i)));
            b.Add((byte)Encoding.UTF8.GetByteCount(m.Name));
            b.AddRange(Encoding.UTF8.GetBytes(m.Name));
            b.AddRange(BitConverter.GetBytes(m.Class));
            b.AddRange(BitConverter.GetBytes(m.Level));
            b.AddRange(BitConverter.GetBytes(m.Gear));
            b.AddRange(m.Flags);                                 // ready / login: not the same width for everyone
            b.AddRange(BitConverter.GetBytes(m.Server));         // born server
            b.AddRange(BitConverter.GetBytes(m.Server));         // current server
            b.Add(0);                                            // party role
            b.AddRange(BitConverter.GetBytes(m.Power));
            b.AddRange(m.Tail);
        }
        for (var i = 0; i < vacant; i++) b.AddRange(new byte[11]);  // mask 0, slot, dbid 0, empty name
        return b.ToArray();
    }

    private static List<GameEvent> Parse(ushort opcode, byte[] body)
    {
        var events = new List<GameEvent>();
        new PacketParser(GameData.Empty, events.Add).Handle(opcode, body);
        return events;
    }

    [Fact]
    public void Party_roster_gives_every_members_gear_score_and_combat_power()
    {
        var body = Roster(
        [
            new("Sylvaen", 2305, 24, 45, 2859, 59_072, [0x01], [0x00, 0x00, 0x00]),
            // Wider flags and a ticket in the tail: combat power is still found past the server id, the next member by its header.
            new("Borgrim", 2305, 6, 45, 3012, 61_234, [0x00, 0x01], [0x01, 0x05, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00]),
            new("Квилл", 1102, 36, 44, 2650, 38_900, [0x01], [0x00, 0x00, 0x00]),
        ], vacant: 3);

        var roster = Assert.IsType<PartyRosterEvent>(Assert.Single(Parse(Opcodes.PartyRoster, body)));
        Assert.Equal(
        [
            new PartyMemberInfo("Sylvaen", 2305, 45, 2859, 59_072, GameClass.Elementalist),
            new PartyMemberInfo("Borgrim", 2305, 45, 3012, 61_234, GameClass.Gladiator),
            new PartyMemberInfo("Квилл", 1102, 44, 2650, 38_900, GameClass.Chanter),
        ], roster.Members);
    }

    [Fact]
    public void Something_else_under_the_roster_opcode_gives_nothing()
    {
        Assert.Empty(Parse(Opcodes.PartyRoster, [1, 2, 3, 4, 200, 9, 9, 9]));
        Assert.Empty(Parse(Opcodes.PartyRoster, Enumerable.Range(0, 64).Select(i => (byte)(i * 37)).ToArray()));
    }

    [Fact]
    public void Own_record_ends_with_the_combat_power_pair()
    {
        var b = new List<byte> { 0xF8, 0x6A, 0, 0, 0, 0, 0x01, 7 };      // id 13688, mask1, mask2 (name follows), length
        b.AddRange("Sylvaen"u8.ToArray());
        b.AddRange([0x01, 0x09, 24, 0, 0, 0, 0, 45, 0, 0, 0]);         // server 2305, Elementalist, level 45
        b.AddRange(new byte[60]);
        b.AddRange(BitConverter.GetBytes(59_072UL));                    // current
        b.AddRange(BitConverter.GetBytes(59_072UL));                    // highest
        b.AddRange(new byte[32]);                                       // 48 bytes before the end, as on Global

        var self = Assert.IsType<SelfIdentifiedEvent>(Assert.Single(Parse(Opcodes.SelfInfo, b.ToArray())));
        Assert.Equal(("Sylvaen", 2305, GameClass.Elementalist, 45, 59_072L), (self.Name, self.ServerId, self.Class, self.Level, self.CombatPower));
    }

    [Fact]
    public void Gear_follows_players_by_name_across_zones_and_restarts()
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All });
        t.Process(new SelfIdentifiedEvent(0, 100, "Sylvaen", 2305, GameClass.Elementalist, 45, CombatPower: 59_072));
        t.Process(new PlayerSeenEvent(0, 101, "Borgrim", 2305, GameClass.Gladiator));
        t.Process(new PartyRosterEvent(0,
        [
            new("Sylvaen", 2305, 45, 2859, 59_500, GameClass.Elementalist),
            new("Borgrim", 2305, 45, 3012, 61_234, GameClass.Gladiator),
        ]));
        t.Process(new DamageEvent(1_000, 100, 500, 16040000, 1_000, HitFlags.None));
        t.Process(new DamageEvent(1_000, 101, 500, 11020000, 1_000, HitFlags.None));
        var snap = t.Snapshot(null, 2_000)!;
        Assert.Equal((2859, 59_500L), Gear(snap, "Sylvaen"));
        Assert.Equal((3012, 61_234L), Gear(snap, "Borgrim"));

        // A loading screen reissues ids; the roster's names still apply when the players are announced again.
        t.Process(new ZoneChangedEvent(30_000, 600021, "Fire Temple", true));
        t.Process(new PlayerSeenEvent(31_000, 202, "Borgrim", 2305, GameClass.Gladiator));
        t.Process(new DamageEvent(32_000, 202, 600, 11020000, 1_000, HitFlags.None));
        Assert.Equal((3012, 61_234L), Gear(t.Snapshot(null, 33_000)!, "Borgrim"));

        // And a restarted meter gets them back from the cache.
        var restarted = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All });
        restarted.ImportCache(t.ExportCache());
        restarted.Process(new DamageEvent(40_000, 202, 600, 11020000, 1_000, HitFlags.None));
        Assert.Equal((3012, 61_234L), Gear(restarted.Snapshot(null, 41_000)!, "Borgrim"));
    }

    private static (int, long) Gear(EncounterSnapshot s, string name) =>
        s.Combatants.Single(c => c.Name == name) is var c ? (c.GearScore, c.CombatPower) : default;

    // ---------------------------------------------------------------- chat

    private static CombatantSnapshot Player(uint id, string name, long damage, int gear = 0, long power = 0) =>
        new(id, name, GameClass.Elementalist, false, damage, damage / 160.0, damage / 4_000_000.0, 300, 0.15, 34_520, 0, 2305, gear, power);

    private static EncounterSnapshot Fight(params CombatantSnapshot[] players) =>
        new(Guid.NewGuid(), "Balhash", null, DateTimeOffset.Now, 160_000, 160_000, false, EncounterEndReason.Kill, 4_000_000, 25_000,
            new BossSnapshot(500, 2701250, "Balhash", 0, 4_000_000), players);

    [Fact]
    public void Chat_names_gear_score_and_combat_power_in_brackets()
    {
        var fight = Fight(Player(1, "Sylvaen", 1_600_000, 2859, 59_072), Player(2, "Borgrim", 1_200_000, 3012, 61_234),
            Player(3, "Talwyn", 800_000, power: 47_796));
        Assert.Equal(
            "Balhash 2:40 KILL | Talwyn (CP 47.8K) #3 of 3: 5K/s, 800K dmg (20%), crit 15%, top hit 34.52K | party 25K/s",
            ChatLine.Player(fight, 3));
        Assert.Equal(
            "Balhash 2:40 KILL | 1.Sylvaen (GS 2859 / CP 59.07K) 10K/s 40% | 2.Borgrim (GS 3012 / CP 61.23K) 7.5K/s 30% | " +
            "3.Talwyn (CP 47.8K) 5K/s 20% | party 25K/s",
            ChatLine.Party(fight));
    }

    [Fact]
    public void Party_line_drops_the_gear_rather_than_players_when_it_does_not_fit()
    {
        var fight = Fight(Enumerable.Range(1, 5).Select(i => Player((uint)i, $"Player{i}", 800_000 - i, 3000 + i, 60_000 + i)).ToArray());
        var line = ChatLine.Party(fight);
        Assert.DoesNotContain("GS", line);
        Assert.Contains("5.Player5", line);
    }

    [Fact]
    public void Discord_table_gets_gs_and_cp_columns()
    {
        var expected = string.Join(Environment.NewLine,
            "**Balhash 2:40 KILL** | party 25K/s",
            "```",
            "#  Player      DPS  Damage  Share    GS      CP",
            "1  Sylvaen   10K/s    1.6M  40.0%  2859  59.07K",
            "2  Borgrim  7.5K/s    1.2M  30.0%     -       -",
            "```");
        Assert.Equal(expected, ChatLine.Table(Fight(Player(1, "Sylvaen", 1_600_000, 2859, 59_072), Player(2, "Borgrim", 1_200_000))));
    }
}
