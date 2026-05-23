using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Watermark.Config;
using Watermark.Native;
using Watermark.Services;

namespace Watermark.Windows;

public partial class WatermarkWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SessionInfoProvider _info;
    private System.Windows.Forms.Screen _screen;
    private DispatcherTimer? _refreshTimer;

    public System.Windows.Forms.Screen Screen => _screen;

    public WatermarkWindow(AppSettings settings, SessionInfoProvider info, System.Windows.Forms.Screen screen)
    {
        InitializeComponent();
        _settings = settings;
        _info = info;
        _screen = screen;

        // Place window on this monitor in physical pixel space first; we'll translate to DIPs after handle creation.
        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;

        Loaded += OnLoaded;
        SizeChanged += (_, _) => RenderTiles();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // Apply extended styles: transparent (click-through), tool window (no alt-tab/taskbar),
        // no-activate (don't steal focus), layered.
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TRANSPARENT
            | NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_NOACTIVATE
            | NativeMethods.WS_EX_LAYERED
            | NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));

        ApplyScreenBounds();

        // Force topmost with SetWindowPos as a belt-and-braces measure.
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    /// <summary>
    /// Re-targets this window to (a possibly-resized) screen and updates bounds + tiles.
    /// Called by the manager when DisplaySettingsChanged fires or every watchdog tick.
    /// </summary>
    public void UpdateForScreen(System.Windows.Forms.Screen screen)
    {
        _screen = screen;
        ApplyScreenBounds();
        // SizeChanged hook will trigger RenderTiles. Force it anyway in case dimensions are unchanged in DIPs.
        RenderTiles();
    }

    private void ApplyScreenBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            Left = _screen.Bounds.Left;
            Top = _screen.Bounds.Top;
            Width = _screen.Bounds.Width;
            Height = _screen.Bounds.Height;
            return;
        }

        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null)
        {
            var fromDevice = source.CompositionTarget.TransformFromDevice;
            var topLeft = fromDevice.Transform(new Point(_screen.Bounds.Left, _screen.Bounds.Top));
            var bottomRight = fromDevice.Transform(new Point(_screen.Bounds.Right, _screen.Bounds.Bottom));

            double newLeft = topLeft.X;
            double newTop = topLeft.Y;
            double newWidth = bottomRight.X - topLeft.X;
            double newHeight = bottomRight.Y - topLeft.Y;

            const double eps = 0.5;
            if (Math.Abs(Left - newLeft) > eps) Left = newLeft;
            if (Math.Abs(Top - newTop) > eps) Top = newTop;
            if (Math.Abs(Width - newWidth) > eps) Width = newWidth;
            if (Math.Abs(Height - newHeight) > eps) Height = newHeight;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RenderTiles();
        int seconds = Math.Max(1, _settings.Watermark.TimestampRefreshSeconds);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _refreshTimer.Tick += (_, _) => RenderTiles();
        _refreshTimer.Start();
    }

    public void ForceTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void RenderTiles()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        WatermarkCanvas.Children.Clear();

        var lines = BuildLines();
        if (lines.Count == 0) return;

        var ws = _settings.Watermark;
        var brush = TryParseBrush(ws.Color, Brushes.White);

        double angle = ws.RotationAngle;
        double tileW, tileH;
        if (ws.WatermarksPerScreen > 0)
        {
            // Derive tile size so the screen contains roughly N watermarks.
            // Preserve a 1.6:1 aspect ratio per tile (wider than tall, fits multi-line text well).
            const double aspect = 1.6;
            double area = (ActualWidth * ActualHeight) / ws.WatermarksPerScreen;
            tileW = Math.Max(120, Math.Sqrt(area * aspect));
            tileH = Math.Max(80, Math.Sqrt(area / aspect));
        }
        else
        {
            tileW = Math.Max(80, ws.TileWidth);
            tileH = Math.Max(60, ws.TileHeight);
        }

        // Oversize the tile grid so the rotated canvas still covers all corners.
        double diag = Math.Sqrt(ActualWidth * ActualWidth + ActualHeight * ActualHeight);
        int cols = (int)Math.Ceiling(diag / tileW) + 2;
        int rows = (int)Math.Ceiling(diag / tileH) + 2;

        double startX = (ActualWidth - cols * tileW) / 2.0;
        double startY = (ActualHeight - rows * tileH) / 2.0;

        var canvas = new Canvas
        {
            Width = cols * tileW,
            Height = rows * tileH,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(angle),
        };
        Canvas.SetLeft(canvas, startX);
        Canvas.SetTop(canvas, startY);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var tile = BuildTile(lines, ws, brush);
                Canvas.SetLeft(tile, c * tileW);
                Canvas.SetTop(tile, r * tileH);
                tile.Width = tileW;
                tile.Height = tileH;
                canvas.Children.Add(tile);
            }
        }

        WatermarkCanvas.Children.Add(canvas);
    }

    private List<string> BuildLines()
    {
        var ws = _settings.Watermark;
        var lines = new List<string>();
        if (ws.ShowUserAndHost) lines.Add(_info.GetFormattedUserHost());
        if (ws.ShowClientAddress) lines.Add(_info.GetFormattedClient());
        if (ws.ShowTimestamp) lines.Add(DateTime.Now.ToString(ws.TimestampFormat));
        if (ws.ShowCustomText && !string.IsNullOrWhiteSpace(ws.CustomText)) lines.Add(ws.CustomText);
        return lines;
    }

    private static FrameworkElement BuildTile(List<string> lines, WatermarkSettings ws, System.Windows.Media.Brush brush)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        FontFamily ff;
        try { ff = new FontFamily(ws.FontFamily); }
        catch { ff = new FontFamily("Segoe UI"); }

        foreach (var line in lines)
        {
            // Compose each line as a grid: black "halo" copy at multiple offsets +
            // white text on top. This stays legible on any background regardless
            // of color/brightness underneath.
            var lineHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false,
            };

            // Heavy outer glow (black) — large blur, no offset.
            var outerGlow = MakeText(line, ff, ws.FontSize, System.Windows.Media.Brushes.Black);
            outerGlow.FontWeight = FontWeights.Bold;
            outerGlow.Opacity = 0.85;
            outerGlow.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 1.0,
                RenderingBias = RenderingBias.Quality,
            };
            lineHost.Children.Add(outerGlow);

            // Foreground text — ws.Opacity controls the white text directly;
            // the black halo behind it stays at full strength so the watermark
            // remains readable on any background.
            var fg = MakeText(line, ff, ws.FontSize, brush);
            fg.FontWeight = FontWeights.SemiBold;
            fg.Opacity = Math.Clamp(ws.Opacity, 0.1, 1.0);
            fg.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 2,
                ShadowDepth = 0,
                Opacity = 0.9,
            };
            lineHost.Children.Add(fg);

            panel.Children.Add(lineHost);
        }

        var border = new Border
        {
            Child = panel,
            IsHitTestVisible = false,
        };
        return border;
    }

    private static TextBlock MakeText(string text, FontFamily ff, double size, System.Windows.Media.Brush brush)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = ff,
            FontSize = size,
            Foreground = brush,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
    }

    private static System.Windows.Media.Brush TryParseBrush(string color, System.Windows.Media.Brush fallback)
    {
        try
        {
            var conv = ColorConverter.ConvertFromString(color);
            if (conv is System.Windows.Media.Color c) return new SolidColorBrush(c);
        }
        catch { }
        return fallback;
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
        base.OnClosed(e);
    }
}
