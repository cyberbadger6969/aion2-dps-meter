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

    private const double SideDb = 1.5, TrendDb = 1.5;
    private const double FullSideDb = 9;   // this much louder on one side ≈ the cube is straight to that side
    private bool _behind;                  // last call: louder as you go means ahead, quieter means it is behind you

    /// <summary>The card's text: how many cubes, how long they have been near, the chime's side and trend (the render
    /// tool calls this alone).</summary>
    public void Fill(int count, long nearMs, CubeHearing hearing = default)
    {
        var t = UiText.Current;
        TitleText.Text = string.Join(' ', t.CubeTitle.ToUpperInvariant().ToCharArray()) + (count > 1 ? $"  ×{count}" : "");
        MessageText.Text = t.CubeMessage;
        ClockText.Text = Format.ClockShort(nearMs);
        if (!hearing.Hearing)
        {
            ArrowPath.Visibility = ArrowRing.Visibility = Visibility.Collapsed;
            HintText.Text = t.CubeListening;
            return;
        }

        // Stereo tells left from right, and how far to the side; it cannot tell ahead from behind. Going on and the chime
        // growing quieter can: then the cube is behind you.
        if (hearing.TrendDb >= TrendDb) _behind = false;
        else if (hearing.TrendDb <= -TrendDb) _behind = true;
        var side = Math.Clamp(hearing.BalanceDb / FullSideDb, -1, 1) * 90; // + = left
        ArrowTurn.Angle = _behind ? 180 + side : -side;
        ArrowPath.Visibility = ArrowRing.Visibility = Visibility.Visible;

        var where = hearing.BalanceDb switch
        {
            >= SideDb => t.CubeLeft,
            <= -SideDb => t.CubeRight,
            _ => _behind ? t.CubeBehind : t.CubeAhead,
        };
        var trend = hearing.TrendDb switch
        {
            >= TrendDb => " · " + t.CubeCloser,
            <= -TrendDb => " · " + t.CubeFarther,
            _ => "",
        };
        HintText.Text = where + trend;
    }

    /// <summary>Upper middle of the primary screen, where the game's own toasts are.</summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + area.Height * 0.27;
    }
}
