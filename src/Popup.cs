using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace Kopfhoererpegel
{
    public enum Status { Off, Idle, Ok, Loud, VeryLoud }

    // The hover card above the tray icon in the style of the iPhone's "Kopfhörerpegel": title, status with icon,
    // dB value and a bar of 18 segments (5 dB each, 20–110 dB) that light up softly. Never takes the focus.
    public class Popup : Window
    {
        static readonly Color Text = Colors.White;
        static readonly Color Muted = Color.FromRgb(0x9E, 0x9E, 0x9E);
        static readonly Color Dark = Color.FromRgb(0x1C, 0x1C, 0x1C);
        static readonly Color Unlit = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
        public static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58);   // iOS system colours (dark)
        public static readonly Color Yellow = Color.FromRgb(0xFF, 0xD6, 0x0A);
        public static readonly Color Red = Color.FromRgb(0xFF, 0x45, 0x3A);
        static readonly FontFamily DisplayFont = new FontFamily("Segoe UI Variable Display, Segoe UI");

        public const double LoudFrom = 80, VeryLoudFrom = 90;
        const double DefaultCorner = 49;
        double corner = DefaultCorner;  // corner radius in DIPs (49 = the user's choice from the Feintuning round)

        readonly Border rim = new Border();
        public const double ScaleMin = 20, ScaleMax = 110;
        const int Segments = 18;                                       // 5 dB each
        const double SegWidth = 10, SegGap = 8, SegHeight = 26, MarkHeight = 34;
        const double BarWidth = Segments * SegWidth + (Segments - 1) * SegGap;
        static readonly int MarkIndex = (int)((LoudFrom - ScaleMin) / 5);  // the taller segment starts at 80 dB

        readonly TextBlock status = new TextBlock(), value = new TextBlock();
        readonly Ellipse statusDot = new Ellipse();
        readonly Path statusMark = new Path();
        readonly Rectangle[] segments = new Rectangle[Segments];
        readonly TranslateTransform slide = new TranslateTransform();
        double shownLevel = ScaleMin;  // animated level for the segments
        public bool Pinned;
        public bool FadingOut;

        public static Status StatusOf(Reading r)
        {
            if (!r.Connected) return Status.Off;
            if (double.IsNaN(r.Level)) return Status.Idle;
            return r.Level >= VeryLoudFrom ? Status.VeryLoud : r.Level >= LoudFrom ? Status.Loud : Status.Ok;
        }

        public static Color ColorOf(Status s)
        {
            return s == Status.VeryLoud ? Red : s == Status.Loud ? Yellow : s == Status.Ok ? Green : Muted;
        }

        public Popup()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = DisplayFont;

            var title = new TextBlock { Text = "Kopfhörerpegel", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Muted) };

            // Status: round icon (check / exclamation mark / dash) + text on the left, dB value on the right
            statusDot.Width = statusDot.Height = 22;
            statusMark.StrokeThickness = 2.2;
            statusMark.StrokeStartLineCap = statusMark.StrokeEndLineCap = PenLineCap.Round;
            statusMark.StrokeLineJoin = PenLineJoin.Round;
            statusMark.Stroke = new SolidColorBrush(Dark);
            statusMark.Fill = new SolidColorBrush(Dark);
            var icon = new Grid { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 9, 0) };
            icon.Children.Add(statusDot);
            icon.Children.Add(statusMark);
            status.FontSize = 21; status.FontWeight = FontWeights.SemiBold; status.Foreground = new SolidColorBrush(Text);
            value.FontSize = 21; value.FontWeight = FontWeights.SemiBold;
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(icon);
            left.Children.Add(status);
            var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 6, 0, 12) };
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(left);

            var bar = new Canvas { Width = BarWidth, Height = MarkHeight };
            for (int i = 0; i < Segments; i++)
            {
                var h = i == MarkIndex ? MarkHeight : SegHeight;
                segments[i] = new Rectangle { Width = SegWidth, Height = h, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(Unlit) };
                Canvas.SetLeft(segments[i], i * (SegWidth + SegGap));
                Canvas.SetTop(segments[i], (MarkHeight - h) / 2);
                bar.Children.Add(segments[i]);
            }

            // Fixed labels under the bar
            var markCenter = MarkIndex * (SegWidth + SegGap) + SegWidth / 2;
            var labels = new Canvas { Width = BarWidth, Height = 18, Margin = new Thickness(0, 6, 0, 0) };
            labels.Children.Add(Label(ScaleMin.ToString("0"), 0, false));
            labels.Children.Add(Label(LoudFrom.ToString("0"), markCenter, true));
            var max = Label(ScaleMax.ToString("0"), 0, false);
            max.Width = 40; max.TextAlignment = TextAlignment.Right;
            Canvas.SetLeft(max, BarWidth - 40);
            labels.Children.Add(max);

            var stack = new StackPanel { Margin = new Thickness(28, 18, 28, 16) };
            stack.Children.Add(title);
            stack.Children.Add(row);
            stack.Children.Add(bar);
            stack.Children.Add(labels);
            // Frosted card with big iOS-like corners: a blurred picture of what lies behind the card (taken when it opens), a dark tint,
            // a thin light rim, then the content, all clipped to a rounded rectangle (anti-aliased, unlike a window region).
            backdrop.Stretch = Stretch.Fill;
            backdrop.Effect = new BlurEffect { Radius = BlurRadius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
            var canvas = new Canvas();
            canvas.Children.Add(backdrop);
            card.Children.Add(canvas);
            card.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x1C, 0x1C, 0x1E)) });
            rim.BorderThickness = new Thickness(1);
            rim.BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
            card.Children.Add(rim);
            card.Children.Add(stack);
            card.SizeChanged += delegate { SetCorner(corner); };
            card.RenderTransform = slide;
            Content = card;
            fade.Tick += delegate { FadeStep(); };
        }

        // Never rounder than half the card height (a full pill).
        public void SetCorner(double radius)
        {
            corner = radius;
            var r = Math.Min(radius, card.ActualHeight / 2);
            rim.CornerRadius = new CornerRadius(r);
            card.Clip = new RectangleGeometry(new Rect(0, 0, card.ActualWidth, card.ActualHeight), r, r);
        }

        const double BlurRadius = 36;
        readonly Grid card = new Grid();
        readonly Image backdrop = new Image();
        readonly DispatcherTimer fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        DateTime fadeStart;
        double fadeFrom, fadeTo, fadeMs;

        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);

        // Screenshot of the area behind the card plus a margin for the blur.
        void CaptureBehind()
        {
            if (!IsVisible || card.ActualWidth < 1) return;
            try
            {
                var source = PresentationSource.FromVisual(this);
                var scale = source != null ? source.CompositionTarget.TransformToDevice.M11 : 1.0;
                var origin = card.PointToScreen(new Point(0, 0));
                int pad = (int)Math.Ceiling(BlurRadius * scale);
                int w = (int)Math.Ceiling(card.ActualWidth * scale) + 2 * pad, h = (int)Math.Ceiling(card.ActualHeight * scale) + 2 * pad;
                using (var bmp = new System.Drawing.Bitmap(w, h))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                        g.CopyFromScreen((int)origin.X - pad, (int)origin.Y - pad, 0, 0, bmp.Size);
                    var hb = bmp.GetHbitmap();
                    try { backdrop.Source = Imaging.CreateBitmapSourceFromHBitmap(hb, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
                    finally { DeleteObject(hb); }
                }
                backdrop.Width = w / scale; backdrop.Height = h / scale;
                Canvas.SetLeft(backdrop, -pad / scale); Canvas.SetTop(backdrop, -pad / scale);
            }
            catch { }
        }

        void FadeTo(double to, double ms)
        {
            fadeFrom = Opacity; fadeTo = to; fadeMs = ms; fadeStart = DateTime.Now;
            fade.Start();
            FadeStep();
        }

        void FadeStep()
        {
            var t = Math.Min(1, (DateTime.Now - fadeStart).TotalMilliseconds / fadeMs);
            var eased = fadeTo > fadeFrom ? 1 - Math.Pow(1 - t, 3) : t * t * t;  // ease out when showing, ease in when hiding
            Opacity = fadeFrom + (fadeTo - fadeFrom) * eased;
            if (t < 1) return;
            fade.Stop();
            if (FadingOut) { Hide(); FadingOut = false; }
        }

        static TextBlock Label(string text, double x, bool centered)
        {
            var t = new TextBlock { Text = text, FontSize = 13, Foreground = new SolidColorBrush(Muted) };
            if (centered) { t.Width = 40; t.TextAlignment = TextAlignment.Center; x -= 20; }
            Canvas.SetLeft(t, x);
            return t;
        }

        // Texts and colours; called a few times per second.
        public void Render(Reading r)
        {
            var s = StatusOf(r);
            var c = ColorOf(s);
            status.Text = s == Status.Off ? "Nicht verbunden" : s == Status.Idle ? (r.Muted ? "Stumm" : "Keine Wiedergabe")
                        : s == Status.Ok ? "OK" : s == Status.Loud ? "Laut" : "Sehr laut";
            value.Text = s == Status.Off || s == Status.Idle ? "– dB" : Math.Round(r.Level).ToString("0") + " dB";
            value.Foreground = new SolidColorBrush(s == Status.Loud || s == Status.VeryLoud ? c : Text);
            statusDot.Fill = new SolidColorBrush(c);
            // 22 x 22 icon: check mark, exclamation mark (line + dot) or a dash
            statusMark.Data = Geometry.Parse(
                s == Status.Ok ? "M6.4,11.4 L9.6,14.4 L15.6,7.8" :
                s == Status.Loud || s == Status.VeryLoud ? "M11,5.8 L11,12.2 M11,16 L11,16.1" : "M7,11 L15,11");
            statusMark.Fill = null;
        }

        // Segment animation, called ~30x per second. The bar fills segment by segment with the 1-second level (calm,
        // with a little hysteresis so it does not flip between two segments). Only the two front segments live with
        // the beat: their saturation follows the 125 ms level against the average, the outermost strongly, the one
        // behind it gently; everything further left stays fully lit and still.
        int front = -1;            // index of the outermost lit segment, -1 = none
        double beat = 0.5;         // 0 = quieter than average, 1 = on a beat
        readonly double[] on = new double[Segments];  // per segment 0..1, so segments switch on/off softly

        public void Animate(Reading r)
        {
            var s = StatusOf(r);
            var playing = s != Status.Off && s != Status.Idle;
            var target = playing ? r.Level : ScaleMin - 10;
            shownLevel += (target - shownLevel) * (target > shownLevel ? 0.25 : 0.10);

            if (!playing && shownLevel < ScaleMin) front = -1;
            else
            {
                front = Math.Max(front, 0);
                while (front < Segments - 1 && shownLevel > ScaleMin + (front + 1) * 5 + 1) front++;
                while (front > 0 && shownLevel < ScaleMin + front * 5 - 1) front--;
            }

            var deviation = playing && !double.IsNaN(r.Fast) ? r.Fast - r.Level : -4;
            var k = Math.Max(0, Math.Min(1, 0.5 + deviation / 8));
            beat += (k - beat) * (k > beat ? 0.9 : 0.45);  // quick, the steps below give the look

            var c = ColorOf(playing ? s : Status.Ok);
            for (int i = 0; i < Segments; i++)
            {
                var want = i <= front ? 1.0 : 0.0;
                on[i] += (want - on[i]) * 0.3;
                // Stepped, not smooth: each of the two segments jumps between a fixed number of brightness levels.
                var intensity = i == front ? 0.30 + 0.70 * Step(beat, FrontLevels) : i == front - 1 ? 0.72 + 0.28 * Step(beat, BackLevels) : 1.0;
                ((SolidColorBrush)segments[i].Fill).Color = Blend(Unlit, Saturate(c, intensity), on[i]);
            }
        }

        // Brightness levels of the two pulsing segments (2 = only dim/full); tuned by the user by eye.
        const int FrontLevels = 4, BackLevels = 5;

        static double Step(double v, int levels)
        {
            var steps = Math.Max(1, levels - 1);
            return Math.Round(v * steps) / steps;
        }

        // intensity 1 = the full colour, lower = less saturated and a little darker.
        static Color Saturate(Color c, double intensity)
        {
            var lum = (byte)Math.Round((0.299 * c.R + 0.587 * c.G + 0.114 * c.B) * 0.6);
            return Blend(Color.FromRgb(lum, lum, lum), c, intensity);
        }

        static Color Blend(Color a, Color b, double t)
        {
            Func<byte, byte, byte> mix = (x, y) => (byte)Math.Round(x + (y - x) * t);
            return Color.FromArgb(mix(a.A, b.A), mix(a.R, b.R), mix(a.G, b.G), mix(a.B, b.B));
        }

        // trayLeft/Right/Top: tray icon rectangle in device pixels.
        public void ShowAtTray(double trayLeft, double trayRight, double trayTop)
        {
            FadingOut = false;
            var fresh = !IsVisible || Opacity < 0.05;
            if (!IsVisible)
            {
                Opacity = 0;
                base.Show();
                UpdateLayout();
            }
            var source = PresentationSource.FromVisual(this);
            var scale = source != null ? source.CompositionTarget.TransformFromDevice.M11 : 1.0;
            var area = SystemParameters.WorkArea;
            var center = (trayLeft + trayRight) / 2 * scale;
            Left = Math.Max(area.Left + 8, Math.Min(area.Right - ActualWidth - 8, center - ActualWidth / 2));
            // Normally 12 px above the taskbar; icons in the overflow menu get the card above the icon itself.
            Top = Math.Min(area.Bottom, trayTop * scale) - ActualHeight - 12;
            UpdateLayout();
            // The background picture is taken while the card is still invisible: Windows screen copies include layered
            // windows, so a live refresh would blur the card into itself.
            if (fresh) CaptureBehind();
            FadeTo(1, 180);
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // Fades out slowly (never abruptly).
        public void FadeOut()
        {
            if (!IsVisible || FadingOut) return;
            FadingOut = true;
            Pinned = false;
            FadeTo(0, 450);
        }
    }
}
