using System.Globalization;

namespace AionMeter.Core;

public static class Format
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>1234 → 1.23K, 78130 → 78.13K, 1_200_000 → 1.2M. Two decimals below 100 of a unit, fewer above.</summary>
    public static string Compact(double value)
    {
        var abs = Math.Abs(value);
        return abs switch
        {
            >= 1e9 => Scaled(value / 1e9, "B"),
            >= 1e6 => Scaled(value / 1e6, "M"),
            >= 1e3 => Scaled(value / 1e3, "K"),
            _ => value.ToString(abs >= 100 ? "0" : "0.#", Inv),
        };

        static string Scaled(double v, string suffix) => v.ToString(Math.Abs(v) >= 100 ? "0.#" : "0.##", Inv) + suffix;
    }

    public static string Grouped(long value) => value.ToString("#,0", Inv);

    public static string Percent(double fraction, int decimals = 1) =>
        (fraction * 100).ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), Inv) + "%";

    public static string Percent(double? fraction) => fraction is { } f ? Percent(f) : "—";

    /// <summary>Share of the damage: a token contribution reads "&lt;0.1%" rather than a misleading "0.0%".</summary>
    public static string Share(double fraction) => fraction is > 0 and < 0.001 ? "<0.1%" : Percent(fraction);

    public static string Clock(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", Inv) : t.ToString(@"mm\:ss", Inv);
    }

    /// <summary>0:24, 1:10, 12:05 — minutes without a leading zero.</summary>
    public static string ClockShort(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    public static string ClockPrecise(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }
}
