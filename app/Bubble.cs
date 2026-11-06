using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Thumbring;

/// <summary>One quiet round button: a pale disc and an ink symbol. Colour only arrives on hover.</summary>
internal sealed class Bubble : Grid
{
    public Target Target { get; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }

    public static readonly Color Rest = Color.FromArgb(0xF7, 0xEC, 0xEC, 0xEE);
    public static readonly Color Lit = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private const double HoverScale = 1.07;

    private readonly SolidColorBrush _fill = new(Rest);
    private readonly SolidColorBrush _ink = new(Config.Ink);
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _badge;
    private readonly Image? _colour;

    public Bubble(Target target, double size)
    {
        Target = target;
        Width = Height = size;
        RenderTransform = _scale;
        RenderTransformOrigin = new Point(0.5, 0.5);
        Cursor = System.Windows.Input.Cursors.Hand;

        Children.Add(Disc(_fill));

        var mark = size * 0.38;
        if (target.Glyph != null)
        {
            Children.Add(Symbol(target.Glyph, _ink, mark));
        }
        else if (target.IconExe != null && ShellIcon.Load(target.IconExe) is { } icon)
        {
            Children.Add(Picture(ShellIcon.Gray(icon), mark * 1.15));
            _colour = Picture(icon, mark * 1.15);
            _colour.Opacity = 0;
            Children.Add(_colour);
        }
        else
        {
            Children.Add(new TextBlock
            {
                Text = target.Letter ?? "?", FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontWeight = FontWeights.Medium, FontSize = mark * 1.05, Foreground = _ink,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            });
        }

        _badge = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -1, -1), IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0.5, Direction = 270, Opacity = 0.25 }
        };
        Children.Add(_badge);
    }

    /// <summary>The pale disc with a soft shadow and a hairline edge, shared with the middle button.</summary>
    public static Grid Disc(Brush fill)
    {
        var grid = new Grid();
        grid.Children.Add(new Ellipse
        {
            Fill = fill,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = 0.24 }
        });
        grid.Children.Add(new Ellipse
        {
            StrokeThickness = 0.75, IsHitTestVisible = false,
            Stroke = new LinearGradientBrush(
                Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0C, 0x00, 0x00, 0x00), 90)
        });
        return grid;
    }

    public static Path Symbol(string data, Brush ink, double size) => new()
    {
        Data = Geometry.Parse(data), Fill = ink, Stretch = Stretch.Uniform, Width = size, Height = size,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false
    };

    private static Image Picture(BitmapSource source, double size)
    {
        var image = new Image
        {
            Source = source, Width = size, Height = size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    /// <summary>The small mark of a space says where a click lands before you click.</summary>
    public void SpaceBadge(Space? space)
    {
        if (space == null) { _badge.Visibility = Visibility.Collapsed; return; }
        _badge.Background = new SolidColorBrush(space.Color);
        _badge.Child = space.Glyph != null
            ? new Path
            {
                Data = Geometry.Parse(space.Glyph), Fill = Brushes.White,
                Stretch = Stretch.Uniform, Width = 8, Height = 8,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
            : new TextBlock
            {
                Text = space.Letter, FontSize = 10, FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
        _badge.Visibility = Visibility.Visible;
    }

    public void CountBadge(int count, bool urgent)
    {
        if (count <= 0) { _badge.Visibility = Visibility.Collapsed; return; }
        _badge.Background = new SolidColorBrush(urgent ? Color.FromRgb(0xFF, 0x3B, 0x30) : Config.Ink);
        _badge.Child = new TextBlock
        {
            Text = count > 9 ? "9+" : count.ToString(), FontSize = 10, FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        _badge.Visibility = Visibility.Visible;
    }

    /// <summary>The opening animation borrows the transform. This hands it back.</summary>
    public void RestoreScale() => RenderTransform = _scale;

    public void Hover(bool on)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(110);
        double s = on ? HoverScale : 1;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(s, d) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(s, d) { EasingFunction = ease });
        _fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Lit : Rest, d));
        _ink.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Target.Tint : Config.Ink, d));
        _colour?.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, d));
    }

    /// <summary>Static version of the hover state for a snapshot, where no clock runs.</summary>
    public void HoverNow()
    {
        _scale.ScaleX = _scale.ScaleY = HoverScale;
        _fill.Color = Lit;
        _ink.Color = Target.Tint;
        if (_colour != null) _colour.Opacity = 1;
    }
}

/// <summary>Large application icons through the shell, so a 256 pixel icon stays sharp in a bubble.</summary>
internal static class ShellIcon
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbitmap);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [DllImport("shell32", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr ctx,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32")] private static extern bool DeleteObject(IntPtr h);

    private static readonly System.Collections.Generic.Dictionary<string, BitmapSource?> Cache = new();
    private static readonly System.Collections.Generic.Dictionary<BitmapSource, BitmapSource> Grays = new();

    public static BitmapSource? Load(string exe)
    {
        if (!Cache.TryGetValue(exe, out var icon)) Cache[exe] = icon = Read(exe);
        return icon;
    }

    /// <summary>The same icon in ink tones, so it sits quietly next to the symbols until hover.</summary>
    public static BitmapSource Gray(BitmapSource colour)
    {
        if (Grays.TryGetValue(colour, out var gray)) return gray;
        int stride = colour.PixelWidth * 4;
        var px = new byte[stride * colour.PixelHeight];
        colour.CopyPixels(px, stride, 0);
        for (int i = 0; i < px.Length; i += 4)
        {
            byte y = (byte)(px[i] * 0.11 + px[i + 1] * 0.59 + px[i + 2] * 0.30);
            byte v = (byte)(0x1D + y * 0.45);
            px[i] = px[i + 1] = px[i + 2] = v;
        }
        gray = BitmapSource.Create(colour.PixelWidth, colour.PixelHeight, 96, 96, PixelFormats.Bgra32, null, px, stride);
        gray.Freeze();
        return Grays[colour] = gray;
    }

    private static BitmapSource? Read(string exe)
    {
        try
        {
            if (!System.IO.File.Exists(exe)) return null;
            SHCreateItemFromParsingName(exe, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
            if (factory.GetImage(new SIZE { cx = 128, cy = 128 }, 0, out var hbmp) != 0) return null;
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            DeleteObject(hbmp);
            // GDI bitmaps arrive with alpha that WPF treats as ignored. Reinterpret the same pixels with alpha.
            int stride = src.PixelWidth * 4;
            var px = new byte[stride * src.PixelHeight];
            src.CopyPixels(px, stride, 0);
            var fixedSrc = BitmapSource.Create(src.PixelWidth, src.PixelHeight, 96, 96, PixelFormats.Bgra32, null, px, stride);
            fixedSrc.Freeze();
            return fixedSrc;
        }
        catch { return null; }
    }
}
