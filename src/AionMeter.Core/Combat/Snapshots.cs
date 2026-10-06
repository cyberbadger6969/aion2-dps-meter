using AionMeter.Core.Events;

namespace AionMeter.Core.Combat;

/// <summary>Immutable copies handed to the UI and written to history; safe to read off the capture thread.</summary>
/// <param name="MaxHpKnown">False when the meter never saw the boss appear: <paramref name="MaxHp"/> is then only the
/// highest HP it saw, and the percentage means little.</param>
/// <param name="Uncounted">HP the boss lost that no counted hit explains — mostly damage dealt before the meter started.</param>
public sealed record BossSnapshot(uint ActorId, int NpcCode, string Name, long Hp, long MaxHp, bool MaxHpKnown = true, long Uncounted = 0)
{
    public double HpFraction => MaxHp > 0 && Hp >= 0 ? Math.Clamp((double)Hp / MaxHp, 0, 1) : 1;

    /// <summary>The boss's HP (fraction of max) when the meter's count began, when a sizeable part went uncounted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double? CountedFrom => MaxHpKnown && MaxHp > 0 && Uncounted >= MaxHp * 0.05 ? 1 - (double)Uncounted / MaxHp : null;
}

public sealed record CombatantSnapshot(
    uint ActorId,
    string Name,
    GameClass Class,
    bool IsSelf,
    long Damage,
    double Dps,
    double Share,
    int Hits,
    double CritRate,
    long MaxHit,
    long DamageTaken,
    int ServerId = 0)
{
    /// <summary>The "summons, owner unknown" pseudo-row (see <see cref="Combatant.UnknownSummonsId"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUnknownSummons => ActorId == Combatant.UnknownSummonsId;
}

public sealed record EncounterSnapshot(
    Guid Id,
    string Title,
    string? Zone,
    DateTimeOffset StartedAt,
    long ClockMs,
    long CombatMs,
    bool IsActive,
    EncounterEndReason Reason,
    long TotalDamage,
    double PartyDps,
    BossSnapshot? Boss,
    IReadOnlyList<CombatantSnapshot> Combatants,
    int TargetCode = 0)
{
    /// <summary>Players who took part (the unknown-summons row is not one).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int PlayerCount => Combatants.Count(c => !c.IsUnknownSummons);
}

public sealed record SkillRow(
    int SkillCode,
    string Name,
    int Hits,
    int DotTicks,
    long Damage,
    double Dps,
    double Share,
    long Average,
    long Min,
    long Max,
    double? CritRate,
    double? BackRate,
    double? FrontRate,
    double? PerfectRate,
    double? HeavyRate,
    double? MultiRate);

public sealed record HitQuality(
    int Hits,
    int DotTicks,
    double? CritRate,
    double? BackRate,
    double? FrontRate,
    double? PerfectRate,
    double? HeavyRate,
    double? MultiRate,
    long MaxHit,
    string? MaxHitSkill);

public sealed record CombatantDetail(
    uint ActorId,
    string Name,
    GameClass Class,
    bool IsSelf,
    long Damage,
    double Dps,
    double Share,
    long CombatMs,
    long DamageTaken,
    int HitsTaken,
    HitQuality Quality,
    IReadOnlyList<SkillRow> Skills,
    IReadOnlyList<long> PerSecond,
    IReadOnlyList<CastRecord> Casts,
    int ServerId = 0);

public sealed record SegmentInfo(Guid Id, string Title, DateTimeOffset StartedAt, long CombatMs, bool IsActive, EncounterEndReason Reason, long TotalDamage);

public enum BossNoticeKind
{
    /// <summary>Came into view alive (a spawn, or walking into its area).</summary>
    Alive,
    /// <summary>Died while in view: the respawn countdown starts now.</summary>
    Killed,
    /// <summary>Came into view already dead: killed earlier, time unknown.</summary>
    SeenDead,
}

/// <summary>A world boss sighting for the respawn timers; the position is where it was seen (0 when unknown); the server is
/// the local character's (every server runs its own bosses).</summary>
public sealed record BossNotice(int NpcCode, int MapId, string? Zone, BossNoticeKind Kind, long TimeMs, float X = 0, float Y = 0, float Z = 0,
    int ServerId = 0);

/// <summary>A whole fight as saved to disk.</summary>
public sealed record FightRecord(int Version, EncounterSnapshot Summary, IReadOnlyList<CombatantDetail> Details);
