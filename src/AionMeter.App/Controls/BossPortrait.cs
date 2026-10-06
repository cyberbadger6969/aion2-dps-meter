using System.Windows;
using System.Windows.Media;

namespace AionMeter.App.Controls;

/// <summary>
/// A boss portrait in a round gold frame (history, breakdown). Without a picture it shows a dim skull, so lists stay
/// aligned while portraits download or for fights that have none. Size it with Width/Height.
/// </summary>
public sealed class BossPortrait : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(BossPortrait),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    // Same skull as the overlay medallion (24×24 design grid).
    private static readonly Geometry Skull = Frozen(Geometry.Parse(
        "M12 3 C7 3 4 6.5 4 11 C4 14 5.5 15.5 7 16.5 L7 19 L9.5 19 L9.5 17.5 L11 17.5 L11 19 L13 19 L13 17.5 L14.5 17.5 L14.5 19 " +
        "L17 19 L17 16.5 C18.5 15.5 20 14 20 11 C20 6.5 17 3 12 3 Z M8.8 9.8 A1.9 1.9 0 1 0 8.81 9.8 Z M15.2 9.8 A1.9 1.9 0 1 0 15.21 9.8 Z"));

    private static readonly Brush Coin = Frozen(new RadialGradientBrush(Color.FromRgb(0x3C, 0x34, 0x26), Color.FromRgb(0x14, 0x12, 0x0E))
        { GradientOrigin = new Point(0.45, 0.35) });
    private static readonly Pen Ring = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xD7, 0xAE, 0x5C)), 1.6));
    private static readonly Pen DimRing = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xD7, 0xAE, 0x5C)), 1.2));
    private static readonly Pen InnerShade = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), 1));
    private static readonly Brush SkullFill = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0xEC, 0xCB, 0x82)));

    public BossPortrait()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var r = size / 2;

        if (Source is { } image)
        {
            // Head shots: fill the circle, keep the top of the picture (faces sit high).
            var w = image.Width;
            var h = image.Height;
            var scale = size / Math.Min(w, h);
            var rect = new Rect(center.X - w * scale / 2, center.Y - r, w * scale, h * scale);
            dc.PushClip(new EllipseGeometry(center, r - 0.5, r - 0.5));
            dc.DrawImage(image, rect);
            dc.Pop();
            dc.DrawEllipse(null, InnerShade, center, r - 1.8, r - 1.8);
            dc.DrawEllipse(null, Ring, center, r - 0.8, r - 0.8);
            return;
        }

        dc.DrawEllipse(Coin, DimRing, center, r - 0.6, r - 0.6);
        var s = size * 0.42 / 24;
        dc.PushTransform(new TranslateTransform(center.X - 12 * s, center.Y - 11.5 * s));
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawGeometry(SkullFill, null, Skull);
        dc.Pop();
        dc.Pop();
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
