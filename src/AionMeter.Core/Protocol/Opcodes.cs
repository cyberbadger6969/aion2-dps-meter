namespace AionMeter.Core.Protocol;

/// <summary>
/// Opcodes as the two bytes appear on the wire (first byte, second byte), written as <c>0xAABB</c> where AA is the
/// first byte. Values are for the post-June-2026 client (Global launched after that patch).
/// </summary>
public static class Opcodes
{
    public static ushort Of(byte first, byte second) => (ushort)(first << 8 | second);

    public const ushort Heartbeat = 0x0036;      // 8 data bytes, ~19/s
    public const ushort SelfInfo = 0x3336;       // own character
    public const ushort PlayerInfo = 0x4536;     // another player
    public const ushort PlayerInfoOld = 0x4436;  // pre-June-2026 player record
    public const ushort Spawn = 0x4136;          // NPC / summon / effect entity
    public const ushort Death = 0x4236;
    public const ushort MapLoad = 0x2136;
    public const ushort Teleport = 0x2336;
    public const ushort Cast = 0x0238;           // skill use announcement (players and effect entities)
    public const ushort Damage = 0x0438;
    public const ushort Dot = 0x0538;
    public const ushort RemainHp = 0x008D;
    public const ushort KillCredit = 0x048D;
    public const ushort BattleToggle = 0x218D;   // KR only, unverified on Global
    public const ushort HpMp = 0x1B92;
    public const ushort PartyRoster = 0x0297;
    public const ushort FieldBossList = 0x0191;  // a map's field bosses: alive since / back at (in-game map list)

    public static string Name(ushort op) => op switch
    {
        Heartbeat => "Heartbeat",
        SelfInfo => "SelfInfo",
        PlayerInfo or PlayerInfoOld => "PlayerInfo",
        Spawn => "Spawn",
        Death => "Death",
        MapLoad => "MapLoad",
        Teleport => "Teleport",
        Cast => "Cast",
        Damage => "Damage",
        Dot => "Dot",
        RemainHp => "RemainHp",
        KillCredit => "KillCredit",
        BattleToggle => "BattleToggle",
        HpMp => "HpMp",
        PartyRoster => "PartyRoster",
        FieldBossList => "FieldBosses",
        _ => $"{op >> 8:x2} {op & 0xFF:x2}",
    };
}
