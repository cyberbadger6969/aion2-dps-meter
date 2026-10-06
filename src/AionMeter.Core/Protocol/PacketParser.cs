using System.Text;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace AionMeter.Core.Protocol;

/// <summary>
/// Decodes game packets (opcode + body, server→client) into <see cref="GameEvent"/>s. Layouts are for the
/// post-June-2026 client used on the Global servers. Every reader validates aggressively and drops a record
/// rather than emit a guessed number.
/// </summary>
public sealed class PacketParser
{
    private readonly GameData _data;
    private readonly Action<GameEvent> _sink;
    private int _lastMapId = -1;

    public PacketParser(GameData data, Action<GameEvent> sink)
    {
        _data = data;
        _sink = sink;
    }

    /// <summary>Timestamp (Unix ms) stamped on events from the packet being parsed.</summary>
    public long TimeMs { get; set; }

    public Dictionary<ushort, long> OpcodeCounts { get; } = new();
    public long DamageRecords { get; private set; }
    public long DamageRejected { get; private set; }
    public long Heartbeats { get; private set; }

    /// <summary>Optional raw packet tap for the diagnostics dump.</summary>
    public Action<long, ushort, ReadOnlySpan<byte>>? Tap { get; set; }

    /// <summary>Diagnostics: every accepted damage record's raw header fields (layout, damage type, modifier byte, direction byte).</summary>
    public Action<DamageEvent, int, uint, byte, byte>? DamageTap { get; set; }

    public void Handle(ushort opcode, ReadOnlySpan<byte> body)
    {
        OpcodeCounts[opcode] = OpcodeCounts.GetValueOrDefault(opcode) + 1;
        Tap?.Invoke(TimeMs, opcode, body);

        switch (opcode)
        {
            case Opcodes.Heartbeat:
                Heartbeats++;
                break;
            case Opcodes.Damage:
                ParseDamage(body);
                break;
            case Opcodes.Cast:
                ParseCast(body);
                break;
            case Opcodes.FieldBossList:
                ParseFieldBossList(body);
                break;
            case Opcodes.Dot:
                ParseDot(body);
                break;
            case Opcodes.SelfInfo:
                ParseIdentity(body, 0, isSelf: true);
                ScanEmbeddedBundles(body);
                break;
            case Opcodes.PlayerInfo:
            case Opcodes.PlayerInfoOld:
                ParseIdentity(body, 0, isSelf: false);
                ScanEmbeddedBundles(body);
                break;
            case Opcodes.Spawn:
                ParseSpawn(body);
                break;
            case Opcodes.Death:
                ParseDeath(body);
                break;
            case Opcodes.MapLoad:
                ParseMapLoad(body);
                break;
            case Opcodes.RemainHp:
                ParseRemainHp(body);
                break;
            case Opcodes.HpMp:
                ParseHpMp(body);
                break;
            case Opcodes.KillCredit:
                ParseKillCredit(body);
                break;
            case Opcodes.BattleToggle:
                ParseBattleToggle(body);
                break;
            default:
                // Identity records also ride inside other packets: mid-packet, and inside LZ4 bundles embedded in a
                // larger packet (the self record repeats that way every few minutes after a zone load).
                if (body.Length >= 16)
                {
                    ScanForIdentities(body);
                    ScanEmbeddedBundles(body);
                }
                break;
        }
    }

    private void ScanEmbeddedBundles(ReadOnlySpan<byte> b)
    {
        for (var i = 1; i + 8 < b.Length; i++)
        {
            if (b[i] != 0xFF || b[i + 1] != 0xFF) continue;
            for (var n = 3; n >= 1; n--)
            {
                var at = i - n;
                if (at < 0 || !Wire.TryVarint(b, at, out var len, out var vlen) || vlen != n) continue;
                var end = at + (long)len + vlen - 4;
                if (len < 12 || end > b.Length) continue;
                var raw = Wire.U32(b, i + 2);
                if (raw is 0 or > FrameDecoder.MaxBundle) continue;
                var target = System.Buffers.ArrayPool<byte>.Shared.Rent((int)raw);
                try
                {
                    var decoded = K4os.Compression.LZ4.LZ4Codec.Decode(b[(i + 6)..(int)end], target.AsSpan(0, (int)raw));
                    if (decoded <= 0) continue;
                    EmbeddedBundles++;
                    ScanForIdentities(target.AsSpan(0, decoded));
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(target);
                }
                i = (int)end - 1;
                break;
            }
        }
    }

    public long EmbeddedBundles { get; private set; }
    public long CastMarkers { get; private set; }

    // ------------------------------------------------------------------ field boss list (01 91)

    /// <summary>
    /// <code>
    /// u16 0, map u32, count u8, count × { alive u8, slot varint, [alive: x y z f32], [u8 — some slots only], time u64 ms }, 00 00 00
    /// </code>
    /// Slot = map × 100 + place. Time is when a living boss appeared, when a dead one comes back, or 0 when the game
    /// has no time for it. The optional byte (a few slots, meaning unknown) is resolved by reading on: of the two ways
    /// to read a slot, only one leaves the next slot (or the end) where it must be.
    /// </summary>
    private void ParseFieldBossList(ReadOnlySpan<byte> b)
    {
        if (b.Length < 7) return;
        var map = (int)Wire.U32(b, 2);
        int count = b[6];
        var slots = new List<FieldBossSlot>(count);
        var o = 7;
        for (var n = 0; n < count && TryFieldBossSlot(b, ref o, map, last: n == count - 1, out var slot); n++)
            slots.Add(slot);
        // Emitted even when a slot could not be read: the slots before it are good, and the raw bytes get logged.
        Emit(new FieldBossListEvent(TimeMs, map, count, slots, b.ToArray()));
    }

    private static bool TryFieldBossSlot(ReadOnlySpan<byte> b, ref int o, int map, bool last, out FieldBossSlot slot)
    {
        slot = null!;
        if (!SlotHeader(b, o, map, out var alive, out var id, out var at)) return false;
        float x = 0, y = 0, z = 0;
        if (alive)
        {
            if (at + 12 > b.Length) return false;
            x = BitConverter.ToSingle(b.Slice(at, 4));
            y = BitConverter.ToSingle(b.Slice(at + 4, 4));
            z = BitConverter.ToSingle(b.Slice(at + 8, 4));
            at += 12;
        }
        for (var extra = 0; extra <= 1; extra++)
        {
            var t = at + extra;
            if (t + 8 > b.Length) break;
            var time = BitConverter.ToInt64(b.Slice(t, 8));
            if (time != 0 && time is < 1_600_000_000_000 or > 2_600_000_000_000) continue;
            var end = t + 8;
            var fits = last ? b[end..].IndexOfAnyExcept((byte)0) < 0 && b.Length - end <= 8 : SlotHeader(b, end, map, out _, out _, out _);
            if (!fits) continue;
            slot = new FieldBossSlot(id, alive, time, x, y, z);
            o = end;
            return true;
        }
        return false;
    }

    /// <summary><c>alive u8 (0 / 1), slot varint</c> with the slot inside this map's range.</summary>
    private static bool SlotHeader(ReadOnlySpan<byte> b, int o, int map, out bool alive, out int slot, out int next)
    {
        alive = false;
        slot = 0;
        next = o;
        if (o >= b.Length || b[o] > 1) return false;
        alive = b[o] == 1;
        if (!Wire.TryVarint(b, o + 1, out var v, out var len)) return false;
        if (v <= (long)map * 100 || v >= (long)map * 100 + 100) return false;
        slot = (int)v;
        next = o + 1 + len;
        return true;
    }

    // ------------------------------------------------------------------ casts (02 38)

    /// <summary><c>actor varint, flag varint, skill u32 (raw variant), seq u8, kind u8, target varint, position …</c></summary>
    private void ParseCast(ReadOnlySpan<byte> b)
    {
        var o = 0;
        if (!Wire.TryVarint(b, ref o, out var actor) || !IsEntityId(actor)) return;
        if (!Wire.TryVarint(b, ref o, out _) || !Wire.TryU32(b, ref o, out var skill)) return;
        if (skill is < 1 or > 299_999_999 || o + 2 > b.Length) return;
        o += 2;
        if (!Wire.TryVarint(b, ref o, out var target) || !IsEntityId(target)) return;
        Emit(new CastEvent(TimeMs, actor, target, (int)skill));
    }

    // ------------------------------------------------------------------ damage (04 38)

    private void ParseDamage(ReadOnlySpan<byte> b)
    {
        var o = 0;
        var first = true;
        while (o < b.Length)
        {
            if (o + 1 < b.Length && b[o] == 0x01 && b[o + 1] == 0x00) o += 2; // chained record marker
            else if (!first) break;

            if (first && IsCastMarker(b, o))
            {
                CastMarkers++;
                return;
            }
            if (!TryDamageRecord(b, ref o, out var ev, out var clean))
            {
                if (first) DamageRejected++;
                break;
            }
            DamageRecords++;
            Emit(ev);
            first = false;
            if (!clean) break; // tail length unsure: do not risk misreading a chained record
        }
    }

    /// <summary><c>04 38</c> records with switch 0 announce a cast (actor = target), they carry no damage.</summary>
    private static bool IsCastMarker(ReadOnlySpan<byte> b, int o) =>
        Wire.TryVarint(b, ref o, out _) && Wire.TryVarint(b, ref o, out var sw) && (sw & 0x0F) == 0;

    private static readonly int[][] BlockCandidates =
    [
        [],          // 0..3 unused
        [], [], [],
        [8],         // layout 4: u32, u32
        [11, 12],    // layout 5: mods, 00, dir, u32, u32 (unverified; legacy table said 12)
        [11, 10],    // layout 6: verified 11 (= legacy 10 + zero pad)
        [11, 14],    // layout 7: unverified; legacy table said 14
    ];

    /// <summary>
    /// <code>
    /// target varint, switch varint (layout = switch &amp; 0x0F ∈ 4..7, 0x20 = additional hits), flag varint,
    /// actor varint, skill u32, uid u8, dmgType varint (3 = crit), block (layout-dependent),
    /// [0 pad] scalar varint, damage varint, tail
    /// </code>
    /// </summary>
    private bool TryDamageRecord(ReadOnlySpan<byte> b, ref int o, out GameEvent ev, out bool cleanTail)
    {
        ev = null!;
        cleanTail = false;
        if (!Wire.TryVarint(b, ref o, out var target) || !IsEntityId(target)) return false;
        if (!Wire.TryVarint(b, ref o, out var sw)) return false;
        var layout = (int)(sw & 0x0F);
        if (layout is < 4 or > 7) return false;
        if (!Wire.TryVarint(b, ref o, out _)) return false;
        if (!Wire.TryVarint(b, ref o, out var actor) || !IsEntityId(actor)) return false;
        if (!Wire.TryU32(b, ref o, out var rawSkill)) return false;
        var skill = (long)rawSkill;
        if (skill is >= 3_000_000 and <= 3_099_999) skill = skill * 10 + 1; // Theostone item ids
        if (skill is < 1 or > 299_999_999) return false;
        if (!Wire.TryU8(b, ref o, out _)) return false; // per-hit uid
        if (!Wire.TryVarint(b, ref o, out var dmgType)) return false;

        var flags = HitFlags.None;
        if (dmgType == 3) flags |= HitFlags.Critical;
        byte mods = 0, dir = 0;
        if (layout >= 5 && o + 2 < b.Length)
        {
            mods = b[o];
            dir = b[o + 2];
            if ((mods & 0x02) != 0) flags |= HitFlags.Parry;
            if ((mods & 0x04) != 0) flags |= HitFlags.Perfect;
            if ((mods & 0x08) != 0) flags |= HitFlags.Double;
            if ((mods & 0x20) != 0) flags |= HitFlags.Heavy;
            if (dir == 0x01) flags |= HitFlags.Back;
            else if (dir == 0x02) flags |= HitFlags.Front;
        }

        var blockStart = o;
        (int End, uint Scalar, uint Damage)? chosen = null;
        foreach (var blockLen in BlockCandidates[layout])
        {
            if (!TryValues(b, blockStart + blockLen, layout, dmgType, out var end, out var scalar, out var damage)) continue;
            if (damage is < 1 or > 99_999_999) continue;
            var plausible = scalar == 0 || scalar is >= 1_000 and <= 200_000;
            if (plausible)
            {
                chosen = (end, scalar, damage);
                break;
            }
            chosen ??= (end, 0, damage); // keep as a fallback if no candidate validates
        }
        if (chosen is not { } c) return false;
        o = c.End;

        var hits = 1;
        if (TryTail(b, o, layout, sw, c.Damage, out var tailEnd, out var extra) ||
            (layout != 6 && TryTail(b, o, 6, sw, c.Damage, out tailEnd, out extra)))
        {
            o = tailEnd;
            hits += extra;
            cleanTail = true;
        }
        else if ((sw & 0x20) != 0 && Wire.TryVarint(b, o, out var count, out var clen) && count is >= 1 and <= 25)
        {
            hits += (int)count; // lenient: count is plausible but the hit list did not end cleanly
        }

        if (cleanTail && o + 1 < b.Length && b[o] == 0x03 && b[o + 1] == 0x00)
        {
            o += 2; // life-steal suffix
            Wire.TryVarint(b, ref o, out _);
        }

        if (actor == target)
        {
            // A known player's self-targeted record is an instant self-heal.
            ev = new HealEvent(TimeMs, actor, target, NormalizeSkill((int)skill), c.Damage, flags);
            return true;
        }
        var damageEvent = new DamageEvent(TimeMs, actor, target, NormalizeSkill((int)skill), c.Damage, flags, hits, (int)c.Scalar);
        DamageTap?.Invoke(damageEvent, layout, dmgType, mods, dir);
        ev = damageEvent;
        return true;
    }

    /// <summary>Reads [0 pad] v1 v2: normally v1 = power scalar and v2 = damage.</summary>
    private static bool TryValues(ReadOnlySpan<byte> b, int o, int layout, uint dmgType, out int end, out uint scalar, out uint damage)
    {
        end = 0;
        scalar = damage = 0;
        if (o >= b.Length) return false;
        if (!Wire.TryVarint(b, ref o, out var v1)) return false;
        var afterV1 = o;
        if (!Wire.TryVarint(b, ref o, out var v2)) return false;
        if (v1 == 0)
        {
            v1 = v2;
            afterV1 = o;
            if (!Wire.TryVarint(b, ref o, out v2)) return false;
        }

        // Some layout-6 crits carry the damage first and no scalar (the next value is a tiny tail field).
        if (layout == 6 && dmgType == 3 && v1 is >= 1_000 and <= 5_000_000 && v2 <= 25)
        {
            damage = v1;
            end = afterV1;
            return true;
        }
        scalar = v1;
        damage = v2;
        end = o;
        return true;
    }

    /// <summary>
    /// Record tail: layout 4 carries one varint field (1..25); switch bit 0x20 adds count + count varints of
    /// additional hits (already included in the damage). Accepted only when it ends exactly at the end of the
    /// packet or at the next sub-record marker (<c>0x01..0x07 00</c>, or <c>04 38</c>).
    /// </summary>
    private static bool TryTail(ReadOnlySpan<byte> b, int o, int layout, uint sw, uint damage, out int end, out int extraHits)
    {
        end = o;
        extraHits = 0;
        if (layout == 4)
        {
            if (!Wire.TryVarint(b, ref o, out var field) || field is < 1 or > 25) return false;
        }
        if ((sw & 0x20) != 0)
        {
            if (!Wire.TryVarint(b, ref o, out var count) || count is < 1 or > 25) return false;
            long sum = 0;
            for (var i = 0; i < count; i++)
            {
                if (!Wire.TryVarint(b, ref o, out var hit)) return false;
                sum += hit;
            }
            if (sum >= damage) return false;
            extraHits = (int)count;
        }
        var rest = b[o..];
        var clean = rest.IsEmpty
                    || (rest.Length >= 2 && rest[1] == 0x00 && rest[0] is >= 1 and <= 7)
                    || (rest.Length >= 2 && rest[0] == 0x04 && rest[1] == 0x38);
        if (!clean) return false;
        end = o;
        return true;
    }

    /// <summary>Collapse level/specialisation digits of player skills unless the data names the variant differently.</summary>
    private int NormalizeSkill(int raw)
    {
        if (!GameData.IsClassSkill(raw)) return raw;
        var b = GameData.BaseSkill(raw);
        if (b == raw) return raw;
        if (_data.Skills.TryGetValue(raw, out var rawName) && _data.Skills.TryGetValue(b, out var baseName) && rawName != baseName)
            return raw;
        return b;
    }

    // ------------------------------------------------------------------ DoT / HoT (05 38)

    /// <summary><c>target varint, effect u8, actor varint, unknown varint, skill u32 (×100), amount varint</c></summary>
    private void ParseDot(ReadOnlySpan<byte> b)
    {
        var o = 0;
        if (!Wire.TryVarint(b, ref o, out var target) || !IsEntityId(target)) return;
        if (!Wire.TryU8(b, ref o, out var effect)) return;
        var isDamage = effect is 0x02 or 0x0A;
        var isHeal = effect is 0x01 or 0x09 or 0x0B;
        if (!isDamage && !isHeal) return;
        if (!Wire.TryVarint(b, ref o, out var actor) || !IsEntityId(actor)) return;
        if (!Wire.TryVarint(b, ref o, out _)) return;
        if (!Wire.TryU32(b, ref o, out var rawSkill)) return;
        var skill = (int)(rawSkill / 100);
        if (skill is < 1 or > 299_999_999) return;
        if (!Wire.TryVarint(b, ref o, out var amount) || amount is 0 or > 99_999_999) return;

        if (isHeal)
        {
            Emit(new HealEvent(TimeMs, actor, target, NormalizeSkill(skill), amount, HitFlags.Dot));
            return;
        }
        if (actor == target || !_data.IsDamageDot(skill)) return;
        Emit(new DamageEvent(TimeMs, actor, target, NormalizeSkill(skill), amount, HitFlags.Dot));
    }

    // ------------------------------------------------------------------ identities (33 36 / 45 36)

    /// <summary>
    /// <code>id varint, mask1 u32, mask2 u8, [mask2 &amp; 1] len u8 + utf8 name, server u16, class u32, u8, level u32</code>
    /// The tail after the name is only trusted when server and class both validate.
    /// </summary>
    private bool ParseIdentity(ReadOnlySpan<byte> b, int o, bool isSelf)
    {
        if (!Wire.TryVarint(b, ref o, out var id) || !IsEntityId(id)) return false;
        o += 4; // mask1
        if (o + 2 > b.Length || (b[o] & 0x01) == 0) return false;
        var len = b[o + 1];
        o += 2;
        if (len is < 1 or > 36 || o + len > b.Length) return false;
        var field = b.Slice(o, len);
        o += len;

        if (Wire.IsPlaceholderName(field))
        {
            if (isSelf) Emit(new SelfIdentifiedEvent(TimeMs, id, "", 0, GameClass.Unknown));
            return true;
        }
        if (!Wire.TryName(field, out var name)) return false;

        var server = 0;
        var cls = GameClass.Unknown;
        var level = 0;
        if (o + 6 <= b.Length)
        {
            var s = Wire.U16(b, o);
            var c = GameData.ClassFromWire(Wire.U32(b, o + 2));
            if (s is >= 1000 and < 3000 && c != GameClass.Unknown)
            {
                server = s;
                cls = c;
                if (o + 11 <= b.Length && Wire.U32(b, o + 7) is var lv and >= 1 and <= 99) level = (int)lv;
            }
            else if (!isSelf && o + 4 <= b.Length && GameData.ClassFromWire(Wire.U32(b, o)) is var other and not GameClass.Unknown)
            {
                // Other players' records carry the class right after the name (no server id there):
                // "Faelis" 18 00 00 00 = 24 = Elementalist (EU capture 2026-10-05).
                cls = other;
            }
        }

        Emit(isSelf
            ? new SelfIdentifiedEvent(TimeMs, id, name, server, cls, level)
            : new PlayerSeenEvent(TimeMs, id, name, server, cls));
        return true;
    }

    private void ScanForIdentities(ReadOnlySpan<byte> b)
    {
        for (var i = 0; i + 10 < b.Length; i++)
        {
            if (b[i + 1] != 0x36) continue;
            var op = b[i];
            if (op != 0x33 && op != 0x45 && op != 0x44) continue;
            ParseIdentity(b, i + 2, op == 0x33);
        }
    }

    // ------------------------------------------------------------------ spawns (41 36)

    private void ParseSpawn(ReadOnlySpan<byte> b)
    {
        var o = 0;
        if (!Wire.TryVarint(b, ref o, out var rawId) || rawId == 0) return;
        // Spawn ids above 1M are folded into the id space the combat records use.
        var id = rawId > 1_000_000 ? (rawId & 0x3FFF) | 0x4000 : rawId;
        if (o + 2 > b.Length) return;

        var maskStart = o;
        var mask = (uint)b[o] | (uint)b[o + 1] << 8;
        if (o + 4 <= b.Length) mask = Wire.U32(b, o);
        var kind = b[o];

        // Inline name (for a summon: its owner's name). The mask was u16 on older builds, so try both positions.
        string? spawnName = null;
        var cursor = maskStart + 5;
        foreach (var sub in (ReadOnlySpan<int>)[4, 2])
        {
            var g = maskStart + sub;
            if (g + 2 >= b.Length || (b[g] & 0x01) == 0) continue;
            var len = b[g + 1];
            if (len is < 1 or > 36 || g + 2 + len > b.Length) continue;
            if (!Wire.TryName(b.Slice(g + 2, len), out var n)) continue;
            spawnName = n;
            cursor = g + 2 + len;
            break;
        }

        var (npcCode, maxHp, x, y, z) = FindNpcCode(b, maskStart);
        var summonish = kind is 0x5F or 0x1C or 0x1F or 0x1D or 0x5D;

        if (kind == 0x5F && (mask & 0x10) != 0 && FindParentKey(b, cursor, id) is { } owner)
        {
            Emit(new SummonSeenEvent(TimeMs, id, owner, null, npcCode));
            return;
        }
        if (summonish && spawnName is not null)
        {
            Emit(new SummonSeenEvent(TimeMs, id, 0, spawnName, npcCode));
            return;
        }
        if (kind is 0x5F or 0x1F or 0x1D or 0x5D)
        {
            // Spirits, pets and lingering skill effects (Bittercold Wind, Fire Wall …). Without an owner link the
            // entity is still announced: whatever class skills it uses, it must never be counted as a player.
            var spiritOwner = FindSpiritOwner(b, maskStart);
            Emit(new SummonSeenEvent(TimeMs, id, spiritOwner is { } so && so != id ? so : 0, null, npcCode));
            return;
        }
        if (kind is 0x0C or 0x0D || (!summonish && npcCode != 0))
            Emit(new NpcSeenEvent(TimeMs, id, npcCode, maxHp, X: x, Y: y, Z: z));
    }

    /// <summary>
    /// NPC template id: u24 right before the marker <c>00 (00|40) 02</c>; the position (x y z f32) follows the marker and
    /// HP comes after as <c>01 cur max</c>.
    /// </summary>
    private static (int Code, long MaxHp, float X, float Y, float Z) FindNpcCode(ReadOnlySpan<byte> b, int start)
    {
        var end = Math.Min(b.Length - 2, start + 60);
        for (var i = start; i < end; i++)
        {
            if (b[i] != 0x00 || (b[i + 1] != 0x40 && b[i + 1] != 0x00) || b[i + 2] != 0x02) continue;
            if (i < start + 3) return (0, 0, 0, 0, 0);
            var code = (int)Wire.U24(b, i - 3);
            float x = 0, y = 0, z = 0;
            if (i + 15 <= b.Length)
            {
                x = BitConverter.ToSingle(b.Slice(i + 3, 4));
                y = BitConverter.ToSingle(b.Slice(i + 7, 4));
                z = BitConverter.ToSingle(b.Slice(i + 11, 4));
                if (!(Math.Abs(x) < 1e7f && Math.Abs(y) < 1e7f && Math.Abs(z) < 1e7f)) x = y = z = 0;
            }
            long maxHp = 0;
            var hpEnd = Math.Min(b.Length - 2, i + 3 + 64);
            for (var h = i + 3; h < hpEnd; h++)
            {
                if (b[h] != 0x01) continue;
                if (!Wire.TryVarint(b, h + 1, out var cur, out var l1) || cur == 0) continue;
                if (!Wire.TryVarint(b, h + 1 + l1, out var max, out _) || max < cur) continue;
                maxHp = max;
                break;
            }
            return (code, maxHp, x, y, z);
        }
        return (0, 0, 0, 0, 0);
    }

    /// <summary>
    /// The summon's parent key sits behind variable-length subtrees, so anchor on the owner block that follows it:
    /// <c>06 | parent u32 | legion u32 | 00 00 | server u16 | len u8 | legion name</c>.
    /// </summary>
    private static uint? FindParentKey(ReadOnlySpan<byte> b, int from, uint selfId)
    {
        for (var i = Math.Max(1, from); i + 13 <= b.Length; i++)
        {
            if (b[i - 1] != 0x06) continue;
            var parent = Wire.U32(b, i);
            if (!IsEntityId(parent) || parent == selfId) continue;
            if (Wire.U16(b, i + 8) != 0) continue;
            var server = Wire.U16(b, i + 10);
            if (server is < 1000 or >= 3000) continue;
            var len = b[i + 12];
            if (len is < 1 or > 48 || i + 13 + len > b.Length) continue;
            if (!IsText(b.Slice(i + 13, len))) continue;
            return parent;
        }
        return null;
    }

    private static readonly byte[] SpiritAnchor = [0x80, 0x75, 0xD5, 0x2A, 0xBB, 0x03, 0x00, 0x00];

    private static uint? FindSpiritOwner(ReadOnlySpan<byte> b, int from)
    {
        var window = b[from..Math.Min(b.Length, from + 128 + SpiritAnchor.Length)];
        var i = window.IndexOf(SpiritAnchor);
        if (i < 0) return null;
        return Wire.TryVarint(b, from + i + SpiritAnchor.Length, out var owner, out _) && IsEntityId(owner) ? owner : null;
    }

    // ------------------------------------------------------------------ deaths, HP, zone

    /// <summary><c>entity varint, varint, flag varint</c> — 3 = died in combat, 1 = loaded already dead.</summary>
    private void ParseDeath(ReadOnlySpan<byte> b)
    {
        var o = 0;
        if (!Wire.TryVarint(b, ref o, out var id) || !IsEntityId(id)) return;
        if (!Wire.TryVarint(b, ref o, out _) || !Wire.TryVarint(b, ref o, out var flag)) return;
        if (flag is 1 or 3) Emit(new DeathEvent(TimeMs, id, AlreadyDead: flag == 1));
    }

    /// <summary><c>entity varint, u32, killer varint, server u16, len + name, …</c>; all zero = a summon despawning.</summary>
    private void ParseKillCredit(ReadOnlySpan<byte> b)
    {
        var o = 0;
        if (!Wire.TryVarint(b, ref o, out var id) || !IsEntityId(id)) return;
        if (!Wire.TryU32(b, ref o, out var mid)) return;
        if (!Wire.TryVarint(b, ref o, out var killer)) return;
        if (mid == 0 && killer == 0) return;
        Emit(new DeathEvent(TimeMs, id));
        if (!IsEntityId(killer) || !Wire.TryU16(b, ref o, out var server) || server is < 1000 or >= 3000) return;
        if (!Wire.TryU8(b, ref o, out var len) || len is < 1 or > 36 || o + len > b.Length) return;
        if (Wire.TryName(b.Slice(o, len), out var name))
            Emit(new PlayerSeenEvent(TimeMs, killer, name, server, GameClass.Unknown));
    }

    /// <summary><c>mob varint, varint, toggle varint</c> (1 = engaged, 0 = left combat) — seen on Global for every mob.</summary>
    private void ParseBattleToggle(ReadOnlySpan<byte> b)
    {
        var o = 0;
        if (!Wire.TryVarint(b, ref o, out var id) || !IsEntityId(id)) return;
        if (!Wire.TryVarint(b, ref o, out _) || !Wire.TryVarint(b, ref o, out var toggle) || toggle > 1) return;
        if (o != b.Length) return;
        Emit(new BattleStateEvent(TimeMs, id, toggle == 1));
    }

    /// <summary><c>entity varint, kind varint (2 = NPC, 1 = player), varint, varint, hp u64</c></summary>
    private void ParseRemainHp(ReadOnlySpan<byte> b)
    {
        var o = 0;
        if (!Wire.TryVarint(b, ref o, out var id) || !IsEntityId(id)) return;
        if (!Wire.TryVarint(b, ref o, out var kind) || kind != 2) return;
        if (!Wire.TryVarint(b, ref o, out _) || !Wire.TryVarint(b, ref o, out _)) return;
        if (!Wire.TryU32(b, ref o, out var lo)) return;
        Wire.TryU32(b, ref o, out var hi);
        var hp = (long)hi << 32 | lo;
        if (hp is < 0 or > 10_000_000_000) return;
        Emit(new NpcHpEvent(TimeMs, id, hp, 0, IsNpc: true));
    }

    /// <summary><c>actor varint, hp varint, hpMax varint</c> — sent for players and NPCs alike.</summary>
    private void ParseHpMp(ReadOnlySpan<byte> b)
    {
        var o = 0;
        if (!Wire.TryVarint(b, ref o, out var id) || !IsEntityId(id)) return;
        if (!Wire.TryVarint(b, ref o, out var hp) || !Wire.TryVarint(b, ref o, out var max)) return;
        if (max == 0 || hp > max) return;
        Emit(new NpcHpEvent(TimeMs, id, hp, max, IsNpc: false));
    }

    /// <summary><c>u32 loadCount, u32 mapId, …</c> — a second load of the same map is an in-map teleport.</summary>
    private void ParseMapLoad(ReadOnlySpan<byte> b)
    {
        var o = 0;
        if (!Wire.TryU32(b, ref o, out _) || !Wire.TryU32(b, ref o, out var mapId)) return;
        if (mapId is 0 or > 9_999_999) return;
        var teleport = (int)mapId == _lastMapId;
        _lastMapId = (int)mapId;
        Emit(new ZoneChangedEvent(TimeMs, (int)mapId, _data.MapName((int)mapId), GameData.IsDungeonMap((int)mapId), teleport));
    }

    // ------------------------------------------------------------------ helpers

    private static bool IsEntityId(uint id) => id is >= 1 and <= 9_999_999;

    private static bool IsText(ReadOnlySpan<byte> field)
    {
        try
        {
            var s = new UTF8Encoding(false, true).GetString(field);
            foreach (var ch in s)
                if (char.IsControl(ch)) return false;
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private void Emit(GameEvent e) => _sink(e);
}
