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
    // Bars count from the quietest the hum has been since the cube came into view, 3 dB a bar; louder on one side by
    // 3 dB or more points that way (flying round a cube swung it 10–15 dB, recording of 2026-10-24).
    private const double BarDb = 3, SideDb = 3;
    private const double FullSideDb = 10; // this much louder on one side turns the arrow all the way (90°) to it
    private double _turn = double.NaN;

    /// <summary>Turns the arrow (degrees, clockwise from ahead), smoothly.</summary>
    private void Turn(double angle)
    {
        if (Math.Abs(angle - _turn) < 3) return;
        var first = double.IsNaN(_turn);
        _turn = angle;
        foreach (var r in new[] { ArrowTurn, ArrowTurnBack })
        {
            if (first) r.Angle = angle;
            else r.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
                new System.Windows.Media.Animation.DoubleAnimation(angle, TimeSpan.FromMilliseconds(350)));
        }
    }
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
        // The cube hums in 3D: the louder ear is its side, and it grows louder as you get closer.
        var lit = hearing.Hearing ? (int)Math.Clamp(1 + Math.Round(hearing.RiseDb / BarDb), 1, 5) : 0;
        var bars = new[] { Bar1, Bar2, Bar3, Bar4, Bar5 };
        for (var i = 0; i < bars.Length; i++) bars[i].Fill = i < lit ? BarOn : BarOff;
        if (!hearing.Hearing)
        {
            Arrow.Visibility = Visibility.Collapsed;
            HintText.Text = t.CubeListening;
            return;
        }
        var side = hearing.BalanceDb switch
        {
            >= SideDb => t.CubeLeft,
            <= -SideDb => t.CubeRight,
            _ => t.CubeAhead,
        };
        Arrow.Visibility = Visibility.Visible;
        Turn(-Math.Clamp(hearing.BalanceDb / FullSideDb, -1, 1) * 90);
        var trend = hearing.TrendDb switch
        {
            >= TrendDb => t.CubeWarmer,
            <= -TrendDb => t.CubeColder,
            _ => t.CubeSteady,
        };
        HintText.Text = $"{side} · {trend}";
    }

    /// <summary>Upper middle of the primary screen, where the game's own toasts are.</summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + area.Height * 0.27;
    }
}
