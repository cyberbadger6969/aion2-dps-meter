using System.Text.Json;
using System.Text.Json.Serialization;
using AionMeter.Core.Game;

namespace AionMeter.Core.Storage;

/// <summary>
/// Who is who in the current zone, kept on disk. AION 2 only sends a player's name when they come into view or on a
/// loading screen, and a boss's template and max HP only when it comes into view, so without this a meter restart
/// mid-zone leaves everyone as "#id" and a boss already in view nameless, with a max HP guessed from its current HP.
/// Entries are only trusted for a limited time and are dropped as soon as the game shows the zone was reloaded.
/// </summary>
public sealed class NameCache(string path)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(45);

    /// <summary>Bosses are kept for a shorter time: a stale one would put a wrong name and HP on whatever reuses its id.</summary>
    public static readonly TimeSpan NpcMaxAge = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private sealed record FileData(DateTimeOffset SavedAt, uint? SelfId, int MapId, List<CachedPlayer> Players, List<CachedNpc>? Npcs = null);

    public void Save(SessionState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            var data = new FileData(DateTimeOffset.UtcNow, state.SelfId, state.MapId, state.Players.ToList(), state.Npcs.ToList());
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>The saved state, or nothing when the file is missing, unreadable or older than <see cref="MaxAge"/>.</summary>
    public SessionState? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var data = JsonSerializer.Deserialize<FileData>(File.ReadAllText(path), Json);
            var age = DateTimeOffset.UtcNow - data?.SavedAt;
            if (data is null || age > MaxAge) return null;
            return new SessionState(data.SelfId, data.MapId, data.Players, age <= NpcMaxAge ? data.Npcs ?? [] : []);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}
