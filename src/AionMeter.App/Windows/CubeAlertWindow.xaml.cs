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

    /// <summary>Shows the card (with how long the cube has been in view) or hides it.</summary>
    public void Update((int Count, long SinceMs)? cubes, long nowMs)
    {
        if (cubes is not { } c)
        {
            if (IsVisible) Hide();
            return;
        }
        Fill(c.Count, nowMs - c.SinceMs);
        if (!IsVisible) Show();
    }

    /// <summary>The card's text: how many cubes, how long they have been near (the render tool calls this alone).</summary>
    public void Fill(int count, long nearMs)
    {
        var t = UiText.Current;
        TitleText.Text = string.Join(' ', t.CubeTitle.ToUpperInvariant().ToCharArray()) + (count > 1 ? $"  ×{count}" : "");
        MessageText.Text = t.CubeMessage;
        ClockText.Text = Format.ClockShort(nearMs);
    }

    /// <summary>Upper middle of the primary screen, where the game's own toasts are.</summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + area.Height * 0.27;
    }
}
