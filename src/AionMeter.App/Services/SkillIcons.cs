using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AionMeter.App.Services;

/// <summary>
/// Skill icons from the official AION 2 game-data CDN, cached in %AppData%\AionMeter\icons. Lookups never block:
/// a missing icon starts a background download and <see cref="IconReady"/> fires when it lands.
/// </summary>
public sealed class SkillIcons
{
    private const string BaseUrl = "https://assets.playnccdn.com/static-aion2-gamedata/resources/";

    private static readonly Dictionary<int, string> ClassCodes = new()
    {
        [11] = "GL", [12] = "TE", [13] = "AS", [14] = "RA", [15] = "SO",
        [16] = "EL", [17] = "CL", [18] = "CH", [19] = "GT",
    };

    private readonly string _cacheDir = Path.Combine(AppSettings.AppDataDir, "icons");
    private readonly Dictionary<int, string> _overrides = new();   // first 4 digits → icon name
    private readonly ConcurrentDictionary<string, ImageSource?> _loaded = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public SkillIcons(string dataDirectory, bool enabled)
    {
        Enabled = enabled;
        Directory.CreateDirectory(_cacheDir);
        var path = Path.Combine(dataDirectory, "skill_icons.json");
        if (!File.Exists(path)) return;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (map is null) return;
            foreach (var (k, v) in map)
                if (int.TryParse(k, out var key) && v.Length > 0) _overrides[key] = v;
        }
        catch (JsonException)
        {
        }
    }

    public bool Enabled { get; set; }

    /// <summary>Raised on a worker thread when a downloaded icon becomes available.</summary>
    public event Action? IconReady;

    /// <summary>The CDN file name for a skill, or null when the skill has no icon (mob skills, basic variants).</summary>
    public string? IconName(int skillCode)
    {
        var text = skillCode.ToString();
        if (skillCode > 999 && int.TryParse(text[..4], out var key) && _overrides.TryGetValue(key, out var icon))
            return icon;

        if (skillCode is >= 30_000_000 and <= 30_999_999 && text.Length >= 7)
        {
            // Theostone: digits 5–6 select the stone icon.
            return int.TryParse(text.Substring(5, 2), out var stone) && stone > 0
                ? $"Icon_Item_Usable_Godstone_WP_r_{stone:x3}"
                : null;
        }

        if (skillCode is < 10_000_000 or > 19_999_999) return null;
        if (skillCode / 100 % 10_000 == 0 && skillCode % 100 != 0) return null;
        if (!ClassCodes.TryGetValue(skillCode / 1_000_000, out var cls)) return null;
        var sub = skillCode / 10_000 % 100;
        var passive = sub >= 70;
        if (passive) sub -= 70;
        return $"ICON_{cls}_SKILL_{(passive ? "Passive_" : "")}{sub:D3}";
    }

    public ImageSource? Get(int skillCode)
    {
        if (IconName(skillCode) is not { } name) return null;
        if (_loaded.TryGetValue(name, out var img)) return img;

        var file = Path.Combine(_cacheDir, name + ".png");
        if (File.Exists(file))
        {
            img = Load(file);
            _loaded[name] = img;
            return img;
        }
        if (Enabled && _pending.TryAdd(name, 0)) _ = DownloadAsync(name, file);
        return null;
    }

    private async Task DownloadAsync(string name, string file)
    {
        try
        {
            using var response = await _http.GetAsync(BaseUrl + name + ".png").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _loaded[name] = null; // remember the miss for this session
                return;
            }
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var tmp = file + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
            File.Move(tmp, file, overwrite: true);
            _loaded[name] = Load(file);
            IconReady?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            Log.Info($"Icon {name} not downloaded: {ex.Message}");
        }
        finally
        {
            _pending.TryRemove(name, out _);
        }
    }

    private static ImageSource? Load(string file)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(file);
            bmp.DecodePixelWidth = 48;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }
}
