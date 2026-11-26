using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace Kopfhoererpegel
{
    // Pictures for the README: the card on a tidy desktop (own wallpaper, a plain taskbar with the tray icon) and the
    // frames of two GIFs with a mouse pointer (hover → live level, right click → settings). tools\gifs.py makes the GIFs.
    // Also the app icon (ear with the green check and the three dots on a dark tile).
    static class Shots
    {
        const double W = 1100, H = 620, Bar = 44;    // scene in DIPs
        const double S = 1800 / W;                    // pixels per DIP (1800 x 1014 pictures, the GIFs are scaled to half)
        const int Fps = 20;
        static readonly FontFamily Ui = new FontFamily("Segoe UI Variable Text, Segoe UI");
        static readonly FontFamily Glyphs = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        // ---- app icon ----

        public static void SaveIcon(string file)
        {
            var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var pngs = sizes.Select(size =>
            {
                using (var bmp = AppIcon(size))
                using (var ms = new MemoryStream()) { bmp.Save(ms, Drawing.Imaging.ImageFormat.Png); return ms.ToArray(); }
            }).ToArray();
            using (var w = new BinaryWriter(File.Create(file)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
            // Preview next to it
            using (var bmp = AppIcon(256)) bmp.Save(Path.ChangeExtension(file, ".png"), Drawing.Imaging.ImageFormat.Png);
        }

        // Dark rounded tile with the tray symbol (white ear, green check, green dots): readable on light and dark backgrounds.
        public static Drawing.Bitmap AppIcon(int size)
        {
            var bmp = new Drawing.Bitmap(size, size);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                float m = size <= 20 ? 0 : size * 0.03f, r = size * 0.22f, d = size - 2 * m;
                using (var path = new Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddArc(m, m, 2 * r, 2 * r, 180, 90);
                    path.AddArc(m + d - 2 * r, m, 2 * r, 2 * r, 270, 90);
                    path.AddArc(m + d - 2 * r, m + d - 2 * r, 2 * r, 2 * r, 0, 90);
                    path.AddArc(m, m + d - 2 * r, 2 * r, 2 * r, 90, 90);
                    path.CloseFigure();
                    using (var fill = new Drawing.Drawing2D.LinearGradientBrush(new Drawing.RectangleF(0, 0, size, size),
                        Drawing.Color.FromArgb(0x3A, 0x3A, 0x3E), Drawing.Color.FromArgb(0x16, 0x16, 0x18), 90f))
                        g.FillPath(fill, path);
                    if (size >= 32)
                        using (var rim = new Drawing.Pen(Drawing.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), Math.Max(1f, size / 128f))) g.DrawPath(rim, path);
                }
                var inner = (int)Math.Round(size * (size <= 20 ? 0.9 : 0.76));
                using (var sym = Program.RenderTray(inner * 2, Status.Ok, new[] { 1.0, 1.0, 0.35 }, false))
                    g.DrawImage(sym, (size - inner) / 2f, (size - inner) / 2f + size * 0.01f, inner, inner);
            }
            return bmp;
        }

        // ---- scene ----

        static BitmapSource wallpaper;
        static ImageSource trayIcon;

        public static void Render(string dir, string wallpaperFile)
        {
            Directory.CreateDirectory(dir);
            wallpaper = new BitmapImage(new Uri(Path.GetFullPath(wallpaperFile)));
            using (var ic = Program.RenderTray((int)Math.Round(22 * S), Status.Ok, new[] { 0.8, 1.0, 0.0 }, false)) trayIcon = ToSource(ic);

            var popup = new Popup { Left = -30000, Top = -30000, ShowActivated = false };
            double calibration = 106;
            popup.AutostartOn = () => true;
            popup.GetCalibration = () => calibration;
            popup.Show();
            popup.ShowView(false, true);
            popup.UpdateLayout();
            double cw = popup.Card.ActualWidth, ch = popup.Card.ActualHeight;
            var icon = IconCenter();
            var cardPos = new Point(Math.Min(W - 8 - cw, icon.X - cw / 2), H - Bar - 12 - ch);
            var background = Scene(null);
            popup.UseBackdrop(background, cardPos.X, cardPos.Y, S);

            Func<double, Reading> reading = t => new Reading
            {
                Connected = true, Level = t,
                Fast = t - 3 + 9 * Math.Pow(Math.Max(0, Math.Sin(t * 7.3)), 6), History = new double[0]
            };
            Action<Reading, int> settle = (r, n) => { popup.Render(r); for (int i = 0; i < n; i++) popup.Animate(r); };

            // Still pictures
            settle(reading(68), 60);
            Save(Compose(background, popup, cardPos, 1, 0, null, 0, null), Path.Combine(dir, "titelbild.png"));
            popup.ShowView(true, true);
            popup.Unfold(true, true);
            popup.HoverPill(1, true);
            popup.UpdateLayout();
            var plus = popup.Where("1");
            Save(Compose(background, popup, cardPos, 1, 0, new Point(cardPos.X + plus.X + 14, cardPos.Y + plus.Y + 9), 0, null), Path.Combine(dir, "einstellungen.png"));
            popup.HoverPill(1, false);

            // GIF 1: hover over the icon, the level climbs through all three colours, mouse away, the card fades.
            var frames = Path.Combine(dir, "frames-pegel");
            Fresh(frames);
            popup.ShowView(false, true);
            settle(reading(20), 80);
            var start = new Point(600, 300);
            var away = new Point(560, 250);
            double tShow = 1.6, tLeave = 8.4, tEnd = 10;
            int frameCount = (int)(tEnd * Fps);
            for (int f = 0; f < frameCount; f++)
            {
                double t = f / (double)Fps;
                var cursor = t < 0.6 ? start : t < tShow ? Lerp(start, icon, Ease((t - 0.6) / (tShow - 0.6)))
                           : t < tLeave ? icon : Lerp(icon, away, Ease(Math.Min(1, (t - tLeave) / 0.9)));
                double level = 64 + 30 * Smooth((t - tShow - 0.6) / 5.6) + 2.5 * Math.Sin(t * 1.9);
                var r = reading(level);
                r.Fast = level - 3 + 9 * Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2.1)), 6);
                if (t >= tShow) { if (f % 4 == 0 || t < tShow + 0.1) popup.Render(r); popup.Animate(r); popup.Animate(r); }
                var opacity = CardOpacity(t, tShow, tLeave + 0.3);
                var hover = Near(cursor, icon) ? 1 : 0;
                Save(Compose(background, popup, cardPos, opacity, Slide(t, tShow), cursor, hover, null), Path.Combine(frames, f.ToString("000") + ".png"));
            }

            // GIF 2: right click → settings, unfold "Kalibrierung", +1 dB, hover "Beenden" and "Neu starten", mouse away.
            frames = Path.Combine(dir, "frames-einstellungen");
            Fresh(frames);
            popup.ShowView(true, true);
            popup.UpdateLayout();
            Func<string, Point> at = n => { var p = popup.Where(n); return new Point(cardPos.X + p.X, cardPos.Y + p.Y); };
            var calibPt = at("calib"); calibPt.X -= 40;
            var plusPt = at("1"); plusPt.X += 14; plusPt.Y += 9;
            var quitPt = at("quit"); quitPt.X += 10;
            var restartPt = at("restart"); restartPt.X += 8; restartPt.Y += 2;
            popup.ShowView(false, true);
            settle(reading(70), 80);
            start = new Point(640, 330);
            away = new Point(520, 230);
            // (time, position) keys of the pointer
            var keys = new[]
            {
                Tuple.Create(0.0, start), Tuple.Create(0.4, start), Tuple.Create(1.4, icon), Tuple.Create(2.6, icon),
                Tuple.Create(3.4, calibPt), Tuple.Create(4.0, calibPt), Tuple.Create(4.5, plusPt), Tuple.Create(5.4, plusPt),
                Tuple.Create(6.0, quitPt), Tuple.Create(6.8, quitPt), Tuple.Create(7.2, restartPt), Tuple.Create(8.0, restartPt),
                Tuple.Create(8.9, away), Tuple.Create(11.0, away)
            };
            double tRight = 2.0, tPlus = 4.9, tGone = 8.9;
            tShow = 1.4; tEnd = 10.4;
            frameCount = (int)(tEnd * Fps);
            double mix = 0, unfold = 0;
            for (int f = 0; f < frameCount; f++)
            {
                double t = f / (double)Fps;
                var cursor = Path2(keys, t);
                var r = reading(70 + 2 * Math.Sin(t * 1.3));
                r.Fast = r.Level - 3 + 9 * Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2.1)), 6);
                calibration = t >= tPlus ? 107 : 106;
                if (t < tRight) { if (f % 4 == 0) popup.Render(r); popup.Animate(r); popup.Animate(r); }
                else
                {
                    if (!popup.SettingsShown) { popup.ShowView(true, true); mix = 0; }
                    popup.RefreshSettings();
                    mix = Math.Min(1, mix + 0.25);
                    bool onCalib = t >= 3.3 && t < 5.7;
                    unfold += ((onCalib ? 1 : 0) - unfold) * (onCalib ? 0.45 : 0.55);
                    popup.SetMixForRender(mix, unfold);
                    popup.HoverPill(1, t >= 4.45 && t < 5.6);
                    popup.HoverQuit(t >= 5.9 && t < 6.95);
                    popup.HoverRestart(t >= 7.15 && t < 8.1);
                }
                popup.UpdateLayout();
                var opacity = CardOpacity(t, tShow, tGone + 0.5);
                var hover = Near(cursor, icon) ? 1 : 0;
                // Click rings: right click on the icon, left click on "+1 dB"
                Tuple<double, string> click = null;
                if (t >= tRight && t < tRight + 0.7) click = Tuple.Create((t - tRight) / 0.7, "Rechtsklick");
                if (t >= tPlus && t < tPlus + 0.5) click = Tuple.Create((t - tPlus) / 0.5, (string)null);
                Save(Compose(background, popup, cardPos, opacity, Slide(t, tShow), cursor, hover, click), Path.Combine(frames, f.ToString("000") + ".png"));
            }
            popup.Close();
        }

        static void Fresh(string dir)
        {
            if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            Directory.CreateDirectory(dir);
        }

        static double Ease(double t) { t = Math.Max(0, Math.Min(1, t)); return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; }
        static double Smooth(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
        static Point Lerp(Point a, Point b, double k) { return new Point(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k); }
        static bool Near(Point a, Point b) { return Math.Abs(a.X - b.X) < 14 && Math.Abs(a.Y - b.Y) < 18; }

        static Point Path2(Tuple<double, Point>[] keys, double t)
        {
            for (int i = 1; i < keys.Length; i++)
                if (t <= keys[i].Item1)
                    return Lerp(keys[i - 1].Item2, keys[i].Item2, Ease((t - keys[i - 1].Item1) / (keys[i].Item1 - keys[i - 1].Item1)));
            return keys[keys.Length - 1].Item2;
        }

        // Like the app: 180 ms fade in (ease out), 450 ms fade out (ease in).
        static double CardOpacity(double t, double shown, double hide)
        {
            if (t < shown) return 0;
            if (t < hide) return 1 - Math.Pow(1 - Math.Min(1, (t - shown) / 0.18), 3);
            var k = Math.Min(1, (t - hide) / 0.45);
            return 1 - k * k * k;
        }

        static double Slide(double t, double shown)
        {
            var k = Math.Max(0, Math.Min(1, (t - shown) / 0.26));
            return 6 * Math.Pow(1 - k, 3);
        }

        static Point IconCenter() { return new Point(W - 157, H - Bar / 2); }

        // Wallpaper (cropped to fill) plus the taskbar; with a card when popup != null.
        static BitmapSource Scene(Action<DrawingContext> over)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double iw = wallpaper.PixelWidth, ih = wallpaper.PixelHeight, k = Math.Max(W / iw, H / ih);
                dc.DrawImage(wallpaper, new Rect((W - iw * k) / 2, (H - ih * k) / 2, iw * k, ih * k));
                Taskbar(dc);
                if (over != null) over(dc);
            }
            var rtb = new RenderTargetBitmap((int)Math.Round(W * S), (int)Math.Round(H * S), 96 * S, 96 * S, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        static FormattedText Txt(string s, double size, Color c, FontFamily f = null, FontWeight? weight = null)
        {
            return new FormattedText(s, System.Globalization.CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight,
                new Typeface(f ?? Ui, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), size, new SolidColorBrush(c), 1.0);
        }

        static void Taskbar(DrawingContext dc)
        {
            double top = H - Bar;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xF0, 0x1C, 0x1C, 0x1F)), null, new Rect(0, top, W, Bar));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), null, new Rect(0, top, W, 1));
            // Search box on the left
            var box = new Rect(14, top + 8, 150, Bar - 16);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)), new Pen(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), 1), box, 14, 14);
            var mag = Txt("", 13, Color.FromRgb(0xD0, 0xD0, 0xD0), Glyphs);
            dc.DrawText(mag, new Point(box.X + 13, box.Y + (box.Height - mag.Height) / 2));
            var search = Txt("Suche", 13, Color.FromRgb(0xC8, 0xC8, 0xC8));
            dc.DrawText(search, new Point(box.X + 36, box.Y + (box.Height - search.Height) / 2));
            // Tray: ^, our icon, Wi-Fi, volume, clock
            var light = Color.FromRgb(0xF0, 0xF0, 0xF0);
            var clock = Txt("12:00", 12, light);
            var date = Txt("26.11.2026", 12, light);
            double right = W - 14;
            dc.DrawText(clock, new Point(right - clock.Width, top + 6));
            dc.DrawText(date, new Point(right - date.Width, top + 22));
            Action<string, double> glyph = (g, cx) => { var ft = Txt(g, 15, light, Glyphs); dc.DrawText(ft, new Point(cx - ft.Width / 2, top + (Bar - ft.Height) / 2)); };
            glyph("", W - 100);
            glyph("", W - 124);
            glyph("", W - 188);
        }

        // Scene + card (opacity, slide) + optional pointer, taskbar hover and click ring.
        static BitmapSource Compose(BitmapSource background, Popup popup, Point cardPos, double opacity, double slide, Point? cursor, double hover, Tuple<double, string> click)
        {
            popup.UpdateLayout();
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(background, new Rect(0, 0, W, H));
                var ic = IconCenter();
                if (hover > 0)
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), null, new Rect(ic.X - 16, H - Bar + 5, 32, Bar - 10), 5, 5);
                dc.DrawImage(trayIcon, new Rect(ic.X - 11, ic.Y - 11, 22, 22));
                if (opacity > 0.001)
                {
                    var card = popup.Card;
                    dc.PushOpacity(opacity);
                    dc.DrawRectangle(new VisualBrush(card) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                        null, new Rect(cardPos.X, cardPos.Y + slide, card.ActualWidth, card.ActualHeight));
                    dc.Pop();
                }
                if (cursor != null)
                {
                    var c = cursor.Value;
                    if (click != null)
                    {
                        var k = click.Item1;
                        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(220 * (1 - k)), 0xFF, 0xFF, 0xFF)), 2.2), c, 5 + 17 * k, 5 + 17 * k);
                        if (click.Item2 != null)
                        {
                            var label = Txt(click.Item2, 13, Colors.White, null, FontWeights.SemiBold);
                            var a = (byte)(255 * Math.Min(1, (1 - k) * 2.5));
                            var rect = new Rect(c.X + 18, c.Y - 40, label.Width + 22, 26);
                            dc.PushOpacity(a / 255.0);
                            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x2A, 0x2A, 0x2E)), new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 1), rect, 13, 13);
                            dc.DrawText(label, new Point(rect.X + 11, rect.Y + (26 - label.Height) / 2));
                            dc.Pop();
                        }
                    }
                    Pointer(dc, c);
                }
            }
            var rtb = new RenderTargetBitmap((int)Math.Round(W * S), (int)Math.Round(H * S), 96 * S, 96 * S, PixelFormats.Pbgra32);
            rtb.Render(dv);
            return rtb;
        }

        // The white Windows arrow with a dark outline and a soft shadow; tip at p.
        static void Pointer(DrawingContext dc, Point p)
        {
            var pts = new[] { new Point(0, 0), new Point(0, 17.5), new Point(4.3, 13.6), new Point(7.2, 20.2), new Point(9.9, 19), new Point(7.1, 12.6), new Point(12.6, 12.6) };
            Func<double, double, StreamGeometry> geo = (dx, dy) =>
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(p.X + pts[0].X * 1.15 + dx, p.Y + pts[0].Y * 1.15 + dy), true, true);
                    ctx.PolyLineTo(pts.Skip(1).Select(q => new Point(p.X + q.X * 1.15 + dx, p.Y + q.Y * 1.15 + dy)).ToList(), true, true);
                }
                return g;
            };
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), null, geo(1.2, 1.6));
            dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1.1) { LineJoin = PenLineJoin.Round }, geo(0, 0));
        }

        static void Save(BitmapSource b, string file)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(b));
            using (var fs = File.Create(file)) enc.Save(fs);
        }

        static BitmapSource ToSource(Drawing.Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var img = new BitmapImage();
                img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit();
                img.Freeze();
                return img;
            }
        }
    }
}
