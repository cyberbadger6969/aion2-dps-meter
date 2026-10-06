using AionMeter.Core.Combat;

namespace AionMeter.Core;

/// <summary>One-line fight summaries sized for the in-game chat box (plain ASCII separators).</summary>
public static class ChatLine
{
    public const int MaxLength = 180;

    /// <summary><c>Grandios 1:10 KILL | Sylvaen 23.3K/s (8.2%) | party 285K/s</c></summary>
    public static string Player(EncounterSnapshot s, uint actorId)
    {
        var c = s.Combatants.FirstOrDefault(x => x.ActorId == actorId);
        if (c is null) return Party(s);
        var rank = s.Combatants.ToList().IndexOf(c) + 1;
        return Trim($"{Header(s)} | #{rank} {c.Name} {Format.Compact(c.Dps)}/s ({Format.Percent(c.Share)}) | party {Format.Compact(s.PartyDps)}/s");
    }

    /// <summary><c>Grandios 1:10 KILL | 1.Sylvaen 23.3K 2.Borgrim 20.1K …</c> — as many players as fit.</summary>
    public static string Party(EncounterSnapshot s)
    {
        var line = Header(s) + " |";
        var i = 0;
        foreach (var c in s.Combatants)
        {
            if (c.IsUnknownSummons) continue;
            var part = $" {++i}.{c.Name} {Format.Compact(c.Dps)}";
            if (line.Length + part.Length > MaxLength) break;
            line += part;
        }
        return line;
    }

    private static string Header(EncounterSnapshot s)
    {
        var result = s.Reason switch
        {
            EncounterEndReason.Kill => " KILL",
            EncounterEndReason.Wipe => " WIPE",
            _ => "",
        };
        var t = TimeSpan.FromMilliseconds(s.CombatMs);
        return $"{s.Title} {(int)t.TotalMinutes}:{t.Seconds:00}{result}";
    }

    private static string Trim(string s) => s.Length <= MaxLength ? s : s[..MaxLength];
}
