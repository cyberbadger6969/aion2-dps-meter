using System.Windows;
using System.Windows.Interop;
using AionMeter.App.Services;
using AionMeter.Core;

namespace AionMeter.App.Windows;

/// <summary>
/// "Hidden cube — nearby, take a look": its own card in the upper middle of the screen while the server has a hidden cube
/// in view (Settings → Hidden cube hint). Click-through and never activated, so it cannot get in the game's way.
/// </summary>
public partial class CubeAlertWindow : Window
{
    public CubeAlertWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetExStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT);
        };
        SizeChanged += (_, _) => Place();
    }

    /// <summary>Shows the card (with how long the cube has been in view and what its chime says) or hides it.</summary>
    public void Update((int Count, long SinceMs)? cubes, long nowMs, CubeHearing hearing)
    {
        if (cubes is not { } c)
        {
            if (IsVisible) Hide();
            return;
        }
        Fill(c.Count, nowMs - c.SinceMs, hearing);
        if (!IsVisible) Show();
    }

    private const double TrendDb = 1.5;
    // The chime's loudness to bars: about −36 dB standing at the cube (recordings of 2026-10-24), 6 dB a bar.
    private const double NearDb = -36, BarDb = 6;
    private static readonly System.Windows.Media.Brush BarOn = Frozen(0xFF, 0xE7, 0x7C, 0x93);
    private static readonly System.Windows.Media.Brush BarOff = Frozen(0x40, 0xFF, 0xFF, 0xFF);

    private static System.Windows.Media.Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>The card's text: how many cubes, how long they have been near, the chime's side and trend (the render
    /// tool calls this alone).</summary>
    public void Fill(int count, long nearMs, CubeHearing hearing = default)
    {
        var t = UiText.Current;
        TitleText.Text = string.Join(' ', t.CubeTitle.ToUpperInvariant().ToCharArray()) + (count > 1 ? $"  ×{count}" : "");
        MessageText.Text = t.CubeMessage;
        ClockText.Text = Format.ClockShort(nearMs);
        // The chime is not a 3D sound (walking round a cube keeps it equal in both ears), so it tells no direction —
        // only how near: it grows louder as you get closer.
        var lit = hearing.Hearing ? (int)Math.Clamp(Math.Round(5 + (hearing.LevelDb - NearDb) / BarDb), 1, 5) : 0;
        var bars = new[] { Bar1, Bar2, Bar3, Bar4, Bar5 };
        for (var i = 0; i < bars.Length; i++) bars[i].Fill = i < lit ? BarOn : BarOff;
        if (!hearing.Hearing)
        {
            HintText.Text = t.CubeListening;
            return;
        }
        HintText.Text = hearing.TrendDb switch
        {
            >= TrendDb => t.CubeWarmer,
            <= -TrendDb => t.CubeColder,
            _ => t.CubeSteady,
        };
    }

    /// <summary>Upper middle of the primary screen, where the game's own toasts are.</summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + area.Height * 0.27;
    }
}
