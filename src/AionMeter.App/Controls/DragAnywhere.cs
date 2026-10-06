using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AionMeter.App.Controls;

/// <summary>
/// Lets a window be dragged by any non-interactive part of it. A press only turns into a drag once the mouse has
/// moved a few pixels, so plain clicks (opening a player's breakdown, selecting a list item) keep working; buttons,
/// sliders, scrollbars and text fields are never hijacked.
/// </summary>
public sealed class DragAnywhere
{
    private readonly Window _window;
    private readonly Func<bool> _canDrag;
    private readonly Action? _dropped;
    private Point _start;
    private bool _pending;

    private DragAnywhere(Window window, Func<bool>? canDrag, Action? dropped)
    {
        _window = window;
        _canDrag = canDrag ?? (() => true);
        _dropped = dropped;
        window.PreviewMouseLeftButtonDown += OnDown;
        window.PreviewMouseMove += OnMove;
        window.PreviewMouseLeftButtonUp += (_, _) => _pending = false;
    }

    /// <summary>True when the last press became a drag — click handlers should ignore that release.</summary>
    public bool JustDragged { get; private set; }

    public static DragAnywhere Attach(Window window, Func<bool>? canDrag = null, Action? dropped = null) =>
        new(window, canDrag, dropped);

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        JustDragged = false;
        _pending = _canDrag() && !IsInteractive(e.OriginalSource as DependencyObject);
        _start = e.GetPosition(_window);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pending || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(_window) - _start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _pending = false;
        JustDragged = true;
        try
        {
            _window.DragMove(); // returns when the button is released
        }
        catch (InvalidOperationException)
        {
            return;
        }
        _dropped?.Invoke();
    }

    private static bool IsInteractive(DependencyObject? node)
    {
        for (; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(node)
                 : LogicalTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or Slider or Thumb or ScrollBar or TextBoxBase or PasswordBox or ComboBox or MenuItem)
                return true;
            if (node is Window) return false;
        }
        return false;
    }
}
