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
        readonly TextBlock title;

        // Start notice: "Kopfhörerpegel läuft" as the title, so a start without AirPods is still visible.
        public void ShowRunning(bool on) { title.Text = on ? "Kopfhörerpegel läuft" : "Kopfhörerpegel"; }
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

            title = new TextBlock { Text = "Kopfhörerpegel", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Muted) };

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

            var stack = levelView;
            stack.Margin = new Thickness(28, 18, 28, 16);
            stack.Children.Add(title);
            stack.Children.Add(row);
            stack.Children.Add(bar);
            stack.Children.Add(labels);
            // Both views share one cell, so the card keeps its size when it switches between them.
            views.Children.Add(levelView);
            views.Children.Add(BuildSettings());
            settingsView.Visibility = Visibility.Hidden;
            SourceInitialized += delegate { NoActivate(); };
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
            card.Children.Add(views);
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
            if (FadingOut) { Hide(); FadingOut = false; ShowView(false, true); ShowRunning(false); }
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

        // ---- settings view (right click on the tray icon) ----------------------------------------------------------
        // Left: "Mit Windows starten" to tick, below it "Kalibrierung", which unfolds its buttons on hover.
        // Right: a big "Beenden" button with "Neu starten" as a link underneath.

        public Action Quit, Restart;
        public Func<bool> AutostartOn;
        public Action<bool> SetAutostart;
        public Func<double> GetCalibration;
        public Action<double> SetCalibration;
        public const double DefaultCalibration = 106;
        public bool SettingsShown { get; private set; }

        readonly StackPanel levelView = new StackPanel();
        readonly Grid views = new Grid();
        readonly StackPanel settingsView = new StackPanel();
        readonly Border check = new Border();
        readonly Path checkMark = new Path();
        readonly TextBlock calibValue = new TextBlock();
        readonly StackPanel calibOptions = new StackPanel { Orientation = Orientation.Horizontal };
        readonly TranslateTransform optionsSlide = new TranslateTransform();
        readonly RotateTransform chevronTurn = new RotateTransform();
        readonly Border quit = new Border();
        readonly TextBlock restart = new TextBlock();
        FrameworkElement calibRow, autostartRow;
        readonly DispatcherTimer anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        double expand, expandTarget;       // 0 = calibration buttons folded away, 1 = unfolded
        double viewMix = 1;                // fade-in of the view that just became visible

        static SolidColorBrush Brush(Color c) { return new SolidColorBrush(c); }
        static Color Alpha(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        FrameworkElement BuildSettings()
        {
            var v = settingsView;
            v.Margin = new Thickness(28, 18, 28, 16);
            v.Width = BarWidth;
            v.Children.Add(new TextBlock { Text = "Einstellungen", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush(Muted) });

            // Check box in the style of the card: rounded square, green with a dark tick when on.
            check.Width = check.Height = 22;
            check.CornerRadius = new CornerRadius(7);
            check.BorderThickness = new Thickness(1.6);
            checkMark.Data = Geometry.Parse("M5.6,11.2 L9.2,14.6 L16.2,7.4");
            checkMark.Stroke = Brush(Dark);
            checkMark.StrokeThickness = 2.4;
            checkMark.StrokeStartLineCap = checkMark.StrokeEndLineCap = PenLineCap.Round;
            checkMark.StrokeLineJoin = PenLineJoin.Round;
            checkMark.Margin = new Thickness(-1.6);
            check.Child = checkMark;
            var autoText = new TextBlock { Text = "Mit Windows starten", FontSize = 16, Foreground = Brush(Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 1) };
            var auto = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Top };
            auto.Children.Add(check);
            auto.Children.Add(autoText);
            auto.MouseEnter += delegate { HoverAutostart(true); };
            auto.MouseLeave += delegate { HoverAutostart(false); };
            auto.MouseLeftButtonUp += delegate { if (SetAutostart != null) SetAutostart(!AutostartOn()); RefreshSettings(); };
            autostartRow = auto;

            // "Kalibrierung  106 dB ⌄": hovering unfolds the buttons below.
            var chevron = new Path
            {
                Data = Geometry.Parse("M0,0 L5,5 L10,0"), Stroke = Brush(Muted), StrokeThickness = 1.8, Width = 10, Height = 5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 2, 0, 0),
                RenderTransform = chevronTurn, RenderTransformOrigin = new Point(0.5, 0.5)
            };
            calibValue.FontSize = 16; calibValue.Foreground = Brush(Muted); calibValue.Margin = new Thickness(8, 0, 0, 0);
            var calib = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Margin = new Thickness(0, 12, 0, 0) };
            calib.Children.Add(new TextBlock { Text = "Kalibrierung", FontSize = 16, Foreground = Brush(Text) });
            calib.Children.Add(calibValue);
            calib.Children.Add(chevron);
            calib.MouseEnter += delegate { Unfold(true); };
            calib.MouseLeave += delegate { MaybeFold(); };
            calibRow = calib;

            // The buttons unfold under "Kalibrierung" in the left column, so both views of the card are the same height.
            calibOptions.Margin = new Thickness(0, 8, 0, 0);
            calibOptions.RenderTransform = optionsSlide;
            calibOptions.Background = Brushes.Transparent;
            calibOptions.Children.Add(Pill("−1 dB", () => GetCalibration() - 1));
            calibOptions.Children.Add(Pill("+1 dB", () => GetCalibration() + 1));
            var reset = Pill("", () => DefaultCalibration);
            ((TextBlock)reset.Child).FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            ((TextBlock)reset.Child).FontSize = 13;
            reset.Width = 30; reset.Padding = new Thickness(0, 1, 0, 0);
            ((TextBlock)reset.Child).HorizontalAlignment = HorizontalAlignment.Center;
            calibOptions.Children.Add(reset);
            calibOptions.MouseLeave += delegate { MaybeFold(); };
            var left = new StackPanel();
            left.Children.Add(auto);
            left.Children.Add(calib);
            left.Children.Add(calibOptions);

            // Big "Beenden" on the right, "Neu starten" as a link under it.
            quit.Width = 112; quit.Height = 50;
            quit.CornerRadius = new CornerRadius(25);
            quit.Cursor = System.Windows.Input.Cursors.Hand;
            quit.Child = new TextBlock { Text = "Beenden", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brush(Red), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
            quit.MouseEnter += delegate { HoverQuit(true); };
            quit.MouseLeave += delegate { HoverQuit(false); };
            quit.MouseLeftButtonUp += delegate { if (Quit != null) Quit(); };
            HoverQuit(false);
            restart.Text = "Neu starten"; restart.FontSize = 14; restart.HorizontalAlignment = HorizontalAlignment.Center;
            restart.Margin = new Thickness(0, 8, 0, 0); restart.Cursor = System.Windows.Input.Cursors.Hand;
            restart.Background = Brushes.Transparent; restart.Padding = new Thickness(6, 0, 6, 2);
            restart.MouseEnter += delegate { HoverRestart(true); };
            restart.MouseLeave += delegate { HoverRestart(false); };
            restart.MouseLeftButtonUp += delegate { if (Restart != null) Restart(); };
            HoverRestart(false);
            var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(quit);
            right.Children.Add(restart);

            var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            v.Children.Add(grid);

            anim.Tick += delegate { AnimStep(); };
            ApplyExpand();
            return v;
        }

        Border Pill(string text, Func<double> value)
        {
            var b = new Border
            {
                Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(12, 0, 12, 1), Margin = new Thickness(0, 0, 7, 0),
                Background = Brush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), Cursor = System.Windows.Input.Cursors.Hand,
                Child = new TextBlock { Text = text, FontSize = 14, Foreground = Brush(Text), VerticalAlignment = VerticalAlignment.Center }
            };
            b.MouseEnter += delegate { HoverPill(b, true); };
            b.MouseLeave += delegate { HoverPill(b, false); };
            b.MouseLeftButtonUp += delegate { if (SetCalibration != null) SetCalibration(value()); RefreshSettings(); };
            return b;
        }

        // Hover looks, public so the screenshot renderer can show them too.
        public void HoverPill(int index, bool on) { HoverPill((Border)calibOptions.Children[index], on); }
        static void HoverPill(Border b, bool on) { b.Background = Brush(Color.FromArgb(on ? (byte)0x48 : (byte)0x24, 0xFF, 0xFF, 0xFF)); }
        public void HoverQuit(bool on) { quit.Background = Brush(Alpha(Red, on ? (byte)0x5C : (byte)0x2E)); }
        public void HoverRestart(bool on)
        {
            restart.Foreground = Brush(on ? Text : Muted);
            restart.TextDecorations = on ? TextDecorations.Underline : null;
        }
        bool autostartHover;
        public void HoverAutostart(bool on) { autostartHover = on; RefreshSettings(); }

        public void RefreshSettings()
        {
            var auto = AutostartOn != null && AutostartOn();
            check.Background = auto ? Brush(Green) : Brushes.Transparent;
            check.BorderBrush = Brush(auto ? Green : Color.FromArgb(autostartHover ? (byte)0xB0 : (byte)0x70, 0xFF, 0xFF, 0xFF));
            checkMark.Visibility = auto ? Visibility.Visible : Visibility.Hidden;
            calibValue.Text = GetCalibration != null ? GetCalibration().ToString("0") + " dB" : "";
        }

        public void Unfold(bool open, bool instant = false)
        {
            expandTarget = open ? 1 : 0;
            if (instant) { expand = expandTarget; ApplyExpand(); }
            else anim.Start();
        }

        // Folds the buttons away once the mouse is neither on "Kalibrierung" nor on the buttons.
        void MaybeFold()
        {
            Dispatcher.BeginInvoke(new Action(() => { if (!calibRow.IsMouseOver && !calibOptions.IsMouseOver) Unfold(false); }), DispatcherPriority.Input);
        }

        void AnimStep()
        {
            var speed = expandTarget > expand ? 0.22 : 0.3;
            expand += (expandTarget - expand) * speed;
            if (Math.Abs(expandTarget - expand) < 0.01) expand = expandTarget;
            viewMix = Math.Min(1, viewMix + 0.12);
            ApplyExpand();
            if (expand == expandTarget && viewMix >= 1) anim.Stop();
        }

        void ApplyExpand()
        {
            calibOptions.Opacity = expand;
            optionsSlide.Y = -10 * (1 - expand);
            chevronTurn.Angle = 180 * expand;
            calibOptions.IsHitTestVisible = expand > 0.5;
            var eased = 1 - Math.Pow(1 - viewMix, 3);
            (SettingsShown ? (UIElement)settingsView : levelView).Opacity = eased;
        }

        // Switches between the level and the settings; the new view fades in quickly.
        public void ShowView(bool settings, bool instant = false)
        {
            if (settings == SettingsShown && !instant) return;
            SettingsShown = settings;
            IsHitTestVisible = settings;
            if (settings) RefreshSettings();
            Unfold(false, true);
            HoverQuit(false); HoverRestart(false);
            settingsView.Visibility = settings ? Visibility.Visible : Visibility.Hidden;
            levelView.Visibility = settings ? Visibility.Hidden : Visibility.Visible;
            viewMix = instant ? 1 : 0;
            ApplyExpand();
            if (!instant) anim.Start();
        }

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out WinRect rect);
        [StructLayout(LayoutKind.Sequential)] struct WinRect { public int Left, Top, Right, Bottom; }

        // Clicks on the card never take the focus away from the window you are working in.
        void NoActivate()
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80);  // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
        }

        // Is the mouse on the card (screen pixels)?
        public bool CursorOnCard()
        {
            if (!IsVisible) return false;
            WinRect r;
            if (!GetWindowRect(new WindowInteropHelper(this).Handle, out r)) return false;
            var p = System.Windows.Forms.Cursor.Position;
            return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
        }

        // ---- screenshot renderer (Shots.cs) ----

        public FrameworkElement Card { get { return card; } }

        public void SetMixForRender(double mix, double unfold)
        {
            viewMix = mix; expand = unfold; ApplyExpand();
        }

        // Background picture from a given image (scene pixels) instead of the screen.
        public void UseBackdrop(BitmapSource picture, double xDip, double yDip, double pxPerDip)
        {
            int pad = (int)Math.Ceiling(BlurRadius * pxPerDip);
            int x0 = Math.Max(0, (int)(xDip * pxPerDip) - pad), y0 = Math.Max(0, (int)(yDip * pxPerDip) - pad);
            int x1 = Math.Min(picture.PixelWidth, (int)Math.Ceiling((xDip + card.ActualWidth) * pxPerDip) + pad);
            int y1 = Math.Min(picture.PixelHeight, (int)Math.Ceiling((yDip + card.ActualHeight) * pxPerDip) + pad);
            backdrop.Source = new CroppedBitmap(picture, new Int32Rect(x0, y0, x1 - x0, y1 - y0));
            backdrop.Width = (x1 - x0) / pxPerDip; backdrop.Height = (y1 - y0) / pxPerDip;
            Canvas.SetLeft(backdrop, x0 / pxPerDip - xDip); Canvas.SetTop(backdrop, y0 / pxPerDip - yDip);
        }

        // Position of an element of the settings view on the card (for the mouse pointer in the GIFs).
        public Point Where(string name)
        {
            FrameworkElement e = name == "auto" ? autostartRow : name == "calib" ? calibRow : name == "quit" ? quit
                               : name == "restart" ? (FrameworkElement)restart : (FrameworkElement)calibOptions.Children[int.Parse(name)];
            var p = e.TranslatePoint(new Point(0, 0), card);
            return new Point(p.X + e.ActualWidth / 2, p.Y + e.ActualHeight / 2);
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
