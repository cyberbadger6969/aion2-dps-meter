using System.Text.Json;
using System.Text.Json.Serialization;
using AionMeter.Core.Game;

namespace AionMeter.Core.Storage;

/// <summary>
/// Who is who in the current zone, kept on disk. AION 2 only sends a player's name when they come into view or on a
/// loading screen, so without this a meter restart mid-zone leaves everyone as "#id" until the next load.
/// Entries are only trusted for a limited time and are dropped as soon as the game shows the zone was reloaded.
/// </summary>
public sealed class NameCache(string path)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(45);

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private sealed record FileData(DateTimeOffset SavedAt, uint? SelfId, int MapId, List<CachedPlayer> Players);

    public void Save(uint? selfId, int mapId, IReadOnlyList<CachedPlayer> players)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new FileData(DateTimeOffset.UtcNow, selfId, mapId, players.ToList()), Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>The saved names, or nothing when the file is missing, unreadable or older than <see cref="MaxAge"/>.</summary>
    public (uint? SelfId, int MapId, IReadOnlyList<CachedPlayer> Players)? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var data = JsonSerializer.Deserialize<FileData>(File.ReadAllText(path), Json);
            if (data is null || DateTimeOffset.UtcNow - data.SavedAt > MaxAge) return null;
            return (data.SelfId, data.MapId, data.Players);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}
