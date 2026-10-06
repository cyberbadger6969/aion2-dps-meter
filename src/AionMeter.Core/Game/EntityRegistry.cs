using AionMeter.Core.Events;

namespace AionMeter.Core.Game;

public sealed class PlayerInfo
{
    public required uint ActorId { get; init; }
    public string Name { get; set; } = "";
    public int ServerId { get; set; }
    public GameClass Class { get; set; }
    /// <summary>Restored from the name cache of a previous run rather than announced in this one.</summary>
    public bool FromCache { get; set; }
}

public sealed record CachedPlayer(uint Id, string Name, GameClass Class, int ServerId);

/// <summary>A spawned pet / spirit / skill effect whose owner is not known yet.</summary>
public sealed record UnownedEntity(long SpawnMs, int NpcCode, string? OwnerName);

public sealed class NpcInfo
{
    public required uint ActorId { get; init; }
    public int NpcCode { get; set; }
    public long MaxHp { get; set; }
    /// <summary>-1 until an HP record arrives; 0 = dead.</summary>
    public long Hp { get; set; } = -1;
    public bool InCombat { get; set; }
    public bool IsBoss { get; set; }
    public bool IsDummy { get; set; }
    public string? Name { get; set; }
}

/// <summary>
/// Maps actor ids seen on the wire to players, NPCs and summons. Entity ids are reissued on every map load, so
/// everything but the local player is dropped on zone change. The server announces a player's name only when they
/// come into view or on a loading screen, so names are also exported to a cache that survives a meter restart.
/// </summary>
public sealed class EntityRegistry
{
    private readonly Dictionary<uint, PlayerInfo> _players = new();
    private readonly Dictionary<uint, NpcInfo> _npcs = new();
    private readonly Dictionary<uint, uint> _summonOwner = new();
    private readonly Dictionary<uint, UnownedEntity> _unowned = new();
    private readonly HashSet<int> _entitySkills = new();
    private readonly HashSet<uint> _classSkillUsers = new();
    private readonly GameData _data;

    public EntityRegistry(GameData data) => _data = data;

    /// <summary>NPCs at or above this max HP count as bosses when the data files do not know them.</summary>
    public long BossHpThreshold { get; set; } = 5_000_000;

    public uint? SelfId { get; private set; }
    public PlayerInfo? Self => SelfId is { } id && _players.TryGetValue(id, out var p) ? p : null;
    public string? ZoneName { get; private set; }
    public int MapId { get; private set; }

    /// <summary>Bumped whenever a player's identity changes, so the name cache knows when to save.</summary>
    public int NamesVersion { get; private set; }

    public void SetSelf(uint actorId, string name, int serverId, GameClass cls)
    {
        if (SelfId is { } old && old != actorId)
        {
            if (_players.TryGetValue(old, out var prev) && prev.Name == name) _players.Remove(old);
            // Our own id changed while the cache was in use: the zone was reloaded behind our back, so every cached
            // id now belongs to someone else.
            DropCached();
        }
        SelfId = actorId;
        UpsertPlayer(actorId, name, serverId, cls);
    }

    public PlayerInfo UpsertPlayer(uint actorId, string name, int serverId, GameClass cls)
    {
        if (!_players.TryGetValue(actorId, out var p))
        {
            p = new PlayerInfo { ActorId = actorId };
            _players[actorId] = p;
        }
        p.FromCache = false;
        NamesVersion++;
        if (!string.IsNullOrWhiteSpace(name))
        {
            // A name belongs to one entity at a time: forget stale ids still carrying it.
            if (p.Name != name)
                foreach (var other in _players.Values.Where(o => o.ActorId != actorId && o.Name == name).ToList())
                    _players.Remove(other.ActorId);
            p.Name = name;
        }
        if (serverId != 0) p.ServerId = serverId;
        if (cls != GameClass.Unknown) p.Class = cls;
        _npcs.Remove(actorId);
        _summonOwner.Remove(actorId);
        _unowned.Remove(actorId);
        return p;
    }

    public NpcInfo UpsertNpc(uint actorId, int npcCode = 0)
    {
        if (!_npcs.TryGetValue(actorId, out var n))
        {
            n = new NpcInfo { ActorId = actorId };
            _npcs[actorId] = n;
        }
        if (npcCode != 0 && n.NpcCode != npcCode)
        {
            n.NpcCode = npcCode;
            if (_data.Npcs.TryGetValue(npcCode, out var def))
            {
                n.IsBoss = def.IsBoss || def.IsDummy;
                n.IsDummy = def.IsDummy;
            }
        }
        return n;
    }

    public void SetMaxHp(NpcInfo npc, long maxHp)
    {
        if (maxHp <= 0) return;
        npc.MaxHp = maxHp;
        // Without a data entry, a big HP pool is the best boss signal we have.
        var known = npc.NpcCode != 0 && _data.Npcs.ContainsKey(npc.NpcCode);
        if (!known && maxHp >= BossHpThreshold) npc.IsBoss = true;
    }

    public void SetSummon(uint summonId, uint ownerId)
    {
        if (summonId == ownerId) return;
        if (_players.TryGetValue(summonId, out var p))
        {
            if (!p.FromCache) return;
            _players.Remove(summonId); // a cached id now reused by someone's pet
            NamesVersion++;
        }
        _summonOwner[summonId] = ownerId;
        _npcs.Remove(summonId);
        _unowned.Remove(summonId);
        _classSkillUsers.Remove(summonId);
    }

    /// <summary>
    /// A pet / spirit / skill effect spawned without a resolvable owner. It stays out of the player list whatever skills
    /// it uses, until an owner is found (by the caster's name, a cast it can be tied to, or its power scalar).
    /// </summary>
    public void MarkUnowned(uint actorId, long timeMs, int npcCode, string? ownerName)
    {
        if (_players.TryGetValue(actorId, out var p))
        {
            if (!p.FromCache) return;
            _players.Remove(actorId);
            NamesVersion++;
        }
        _npcs.Remove(actorId);
        _summonOwner.Remove(actorId);
        _classSkillUsers.Remove(actorId);
        _unowned[actorId] = new UnownedEntity(timeMs, npcCode, ownerName);
    }

    public bool IsUnowned(uint actorId) => _unowned.ContainsKey(actorId);
    public bool TryGetUnowned(uint actorId, out UnownedEntity entity) => _unowned.TryGetValue(actorId, out entity!);

    /// <summary>Unowned entities that carry this character name as their caster.</summary>
    public List<uint> UnownedCastBy(string name) =>
        _unowned.Where(kv => kv.Value.OwnerName == name).Select(kv => kv.Key).ToList();

    /// <summary>Skills seen from pets and skill effects: an unannounced actor using only these is not a player.</summary>
    public void NoteEntitySkill(int skillCode) => _entitySkills.Add(skillCode);

    public bool IsEntitySkill(int skillCode) => skillCode < 1_000_000 || _entitySkills.Contains(skillCode);

    public uint? FindPlayerByName(string name)
    {
        foreach (var p in _players.Values)
            if (p.Name == name) return p.ActorId;
        return null;
    }

    /// <summary>
    /// The entity cast a class skill: it is a player (or fights like one), whatever weaker hints said. Players whose
    /// identity record we missed would otherwise be mistaken for mobs when they use a 7-digit item / godstone skill.
    /// </summary>
    public void MarkPlayerLike(uint actorId)
    {
        if (_unowned.ContainsKey(actorId) || !_classSkillUsers.Add(actorId)) return;
        if (_npcs.TryGetValue(actorId, out var n) && n.NpcCode == 0 && n.MaxHp == 0) _npcs.Remove(actorId);
    }

    public bool IsPlayerLike(uint actorId) => _players.ContainsKey(actorId) || _classSkillUsers.Contains(actorId);

    public bool TryGetPlayer(uint actorId, out PlayerInfo player) => _players.TryGetValue(actorId, out player!);
    public bool TryGetNpc(uint actorId, out NpcInfo npc) => _npcs.TryGetValue(actorId, out npc!);
    public bool IsKnownPlayer(uint actorId) => _players.ContainsKey(actorId);
    public bool IsKnownNpc(uint actorId) => _npcs.ContainsKey(actorId);
    public bool IsSummon(uint actorId) => _summonOwner.ContainsKey(actorId);

    /// <summary>Follows summon → owner links (pets, spirits, totems) so their damage is credited to the player.</summary>
    public uint ResolveOwner(uint actorId)
    {
        for (var i = 0; i < 4 && _summonOwner.TryGetValue(actorId, out var owner); i++) actorId = owner;
        return actorId;
    }

    public string NpcName(uint actorId)
    {
        if (_npcs.TryGetValue(actorId, out var n))
        {
            if (!string.IsNullOrEmpty(n.Name)) return n.Name;
            if (n.NpcCode != 0) return _data.NpcName(n.NpcCode);
        }
        return $"#{actorId}";
    }

    public void OnZoneChanged(int mapId, string zoneName)
    {
        MapId = mapId;
        ZoneName = zoneName;
        _npcs.Clear();
        _summonOwner.Clear();
        _unowned.Clear();
        _classSkillUsers.Clear();
        // Ids are reissued: other players' names would now point at strangers. Ours is re-sent right after the load.
        var self = Self;
        _players.Clear();
        if (self is not null) _players[self.ActorId] = self;
        NamesVersion++;
    }

    // ------------------------------------------------------------------ name cache

    public IReadOnlyList<CachedPlayer> ExportPlayers() =>
        _players.Values.Where(p => p.Name.Length > 0).Select(p => new CachedPlayer(p.ActorId, p.Name, p.Class, p.ServerId)).ToList();

    /// <summary>Restores names saved by a previous run in the same zone. Live announcements always win.</summary>
    public void ImportPlayers(uint? selfId, int mapId, IEnumerable<CachedPlayer> players)
    {
        MapId = mapId;
        foreach (var c in players)
        {
            if (_players.ContainsKey(c.Id)) continue;
            _players[c.Id] = new PlayerInfo { ActorId = c.Id, Name = c.Name, Class = c.Class, ServerId = c.ServerId, FromCache = true };
        }
        if (selfId is { } s && _players.ContainsKey(s)) SelfId = s;
    }

    /// <summary>A cached name whose entity now fights as another class belongs to someone else: forget it.</summary>
    public void ForgetCachedIfClassDiffers(uint actorId, GameClass observed)
    {
        if (observed == GameClass.Unknown || !_players.TryGetValue(actorId, out var p) || !p.FromCache) return;
        if (p.Class != GameClass.Unknown && p.Class != observed)
        {
            _players.Remove(actorId);
            if (SelfId == actorId) SelfId = null;
            NamesVersion++;
        }
    }

    private void DropCached()
    {
        foreach (var id in _players.Values.Where(p => p.FromCache).Select(p => p.ActorId).ToList()) _players.Remove(id);
        NamesVersion++;
    }

    public void Clear()
    {
        _players.Clear();
        _npcs.Clear();
        _summonOwner.Clear();
        _unowned.Clear();
        SelfId = null;
    }
}
