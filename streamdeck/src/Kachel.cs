using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace Kopfhoererpegel
{
    // Draws the Stream Deck key (144 x 144, the @2x size every model scales from) in the look of the tray card:
    // dark background, status with badge, the dB number (status colour when loud) and the 18-segment bar 20..110 dB
    // whose two front segments pulse with the beat in steps. Second view: the last 60 seconds as bars.
    static class Kachel
    {
        public enum Status { Off, Idle, Ok, Loud, VeryLoud }

        const double LoudFrom = 80, VeryLoudFrom = 90, ScaleMin = 20, ScaleMax = 110;
        const int Segments = 18, MarkIndex = 12;   // 5 dB each; the taller segment starts at 80 dB
        const int FrontLevels = 4, BackLevels = 5; // same steps as the card

        static readonly Color Green = Color.FromArgb(0x30, 0xD1, 0x58), Yellow = Color.FromArgb(0xFF, 0xD6, 0x0A),
            Red = Color.FromArgb(0xFF, 0x45, 0x3A), Muted = Color.FromArgb(0x9E, 0x9E, 0x9E),
            Dark = Color.FromArgb(0x1C, 0x1C, 0x1C), Unlit = Color.FromArgb(0x2E, 0x2E, 0x31);

        static Color ColorOf(Status s)
        {
            return s == Status.VeryLoud ? Red : s == Status.Loud ? Yellow : s == Status.Ok ? Green : Muted;
        }

        static Status StatusOf(double level)
        {
            return level >= VeryLoudFrom ? Status.VeryLoud : level >= LoudFrom ? Status.Loud : Status.Ok;
        }

        // Smoothed state between frames (8 per second), so the bar fills calmly like on the card.
        public class Zustand
        {
            public Status Status = Status.Off;
            public double Level = double.NaN, Shown = ScaleMin - 10, Beat = 0.5;
            public int Front = -1;
            public bool Muted;
            public double[] History = new double[0];

            public void Update(Reading r)
            {
                Muted = r.Muted;
                History = r.History ?? new double[0];
                Level = r.Level;
                Status = !r.Connected ? Status.Off : double.IsNaN(r.Level) ? Status.Idle : StatusOf(r.Level);
                var playing = Status != Status.Off && Status != Status.Idle;
                var target = playing ? r.Level : ScaleMin - 10;
                Shown += (target - Shown) * (target > Shown ? 0.65 : 0.35);

                if (!playing && Shown < ScaleMin) Front = -1;
                else
                {
                    Front = Math.Max(Front, 0);
                    while (Front < Segments - 1 && Shown > ScaleMin + (Front + 1) * 5 + 1) Front++;
                    while (Front > 0 && Shown < ScaleMin + Front * 5 - 1) Front--;
                }
                var deviation = playing && !double.IsNaN(r.Fast) ? r.Fast - r.Level : -4;
                var k = Math.Max(0, Math.Min(1, 0.5 + deviation / 8));
                Beat += (k - Beat) * (k > Beat ? 1.0 : 0.85);
            }

            // Everything the image depends on; equal signature = no need to send the image again.
            public string Signature(string view)
            {
                var head = view + "|" + Status + "|" + Muted + "|" + (double.IsNaN(Level) ? "-" : Math.Round(Level).ToString());
                if (view == "verlauf")
                    return head + "|" + string.Join(",", History.Select(h => double.IsNaN(h) ? "-" : Math.Round(h).ToString()));
                return head + "|" + Front + "|" + Step(Beat, FrontLevels) + "|" + Step(Beat, BackLevels);
            }
        }

        static double Step(double v, int levels)
        {
            var steps = Math.Max(1, levels - 1);
            return Math.Round(v * steps) / steps;
        }

        // intensity 1 = full colour, lower = less saturated and darker (same formula as the card).
        static Color Saturate(Color c, double intensity)
        {
            var lum = (int)Math.Round((0.299 * c.R + 0.587 * c.G + 0.114 * c.B) * 0.6);
            return Blend(Color.FromArgb(lum, lum, lum), c, intensity);
        }

        static Color Blend(Color a, Color b, double t)
        {
            Func<int, int, int> mix = (x, y) => (int)Math.Round(x + (y - x) * t);
            return Color.FromArgb(mix(a.A, b.A), mix(a.R, b.R), mix(a.G, b.G), mix(a.B, b.B));
        }

        // ---- fonts --------------------------------------------------------------------------------------------------

        static FontFamily family;
        static FontFamily Family
        {
            get
            {
                if (family != null) return family;
                var installed = new InstalledFontCollection().Families.Select(f => f.Name).ToList();
                foreach (var name in new[] { "Segoe UI Variable Display Semib", "Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI" })
                    if (installed.Contains(name)) return family = new FontFamily(name);
                return family = FontFamily.GenericSansSerif;
            }
        }

        static Font F(float px, bool bold = true)
        {
            return new Font(Family, px, bold && Family.Name == "Segoe UI" ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // ---- key image ----------------------------------------------------------------------------------------------

        public static byte[] Png(Zustand z, string view)
        {
            using (var bmp = Draw(z, view))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        static Bitmap Draw(Zustand z, string view)
        {
            var bmp = new Bitmap(144, 144);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var bg = new LinearGradientBrush(new Rectangle(0, 0, 144, 144), Color.FromArgb(0x2A, 0x2A, 0x2E), Color.FromArgb(0x14, 0x14, 0x16), 90f))
                    g.FillRectangle(bg, 0, 0, 144, 144);

                if (z.Status == Status.Off)
                {
                    Ear(g, 40, 22, 1.0f, Muted, 5.2f);
                    Centered(g, "Nicht", F(19), Muted, 96);
                    Centered(g, "verbunden", F(19), Muted, 118);
                    return bmp;
                }
                if (view == "verlauf") DrawHistory(g, z);
                else DrawLevel(g, z);
            }
            return bmp;
        }

        static void DrawLevel(Graphics g, Zustand z)
        {
            var playing = z.Status != Status.Idle;
            var c = ColorOf(z.Status);

            // Status row: badge + text, centred together.
            var text = z.Status == Status.Idle ? (z.Muted ? "Stumm" : "Pause") : z.Status == Status.Ok ? "OK" : z.Status == Status.Loud ? "Laut" : "Sehr laut";
            using (var f = F(21))
            {
                var w = g.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic).Width;
                var x = (144 - (22 + 7 + w)) / 2;
                Badge(g, z.Status, x, 13, 22);
                using (var b = new SolidBrush(playing ? c : Muted)) g.DrawString(text, f, b, x + 29, 13 - 1, StringFormat.GenericTypographic);
            }

            // Number + "dB"
            var number = playing ? Math.Round(z.Level).ToString("0") : "–";
            var ink = z.Status == Status.Loud || z.Status == Status.VeryLoud ? c : playing ? Color.White : Muted;
            using (var big = F(62))
            using (var unit = F(22))
            using (var b = new SolidBrush(ink))
            {
                var fmt = StringFormat.GenericTypographic;
                var wn = g.MeasureString(number, big, PointF.Empty, fmt).Width;
                var wu = g.MeasureString("dB", unit, PointF.Empty, fmt).Width;
                var x = (144 - (wn + 4 + wu)) / 2;
                g.DrawString(number, big, b, x, 40, fmt);
                g.DrawString("dB", unit, b, x + wn + 4, 40 + 62 * 0.93f - 22 * 0.93f, fmt);
            }

            // Bar: 18 segments, 5 px wide with ~2 px gaps over 124 px; the 80 dB one is taller.
            var segColor = playing ? c : Green;
            for (int i = 0; i < Segments; i++)
            {
                float x = 10 + i * (124f - 5) / (Segments - 1), h = i == MarkIndex ? 22 : 15, y = 130 - h;
                var intensity = i == z.Front ? 0.30 + 0.70 * Step(z.Beat, FrontLevels)
                              : i == z.Front - 1 ? 0.72 + 0.28 * Step(z.Beat, BackLevels) : 1.0;
                var col = i <= z.Front ? Saturate(segColor, intensity) : Unlit;
                using (var b = new SolidBrush(col))
                using (var p = Rounded(x, y, 5, h, 2)) g.FillPath(b, p);
            }
        }

        static void DrawHistory(Graphics g, Zustand z)
        {
            var playing = z.Status != Status.Idle;
            var c = ColorOf(z.Status);
            using (var f = F(19))
            using (var b = new SolidBrush(Muted)) g.DrawString("60 s", f, b, 10, 12, StringFormat.GenericTypographic);
            var value = playing ? Math.Round(z.Level).ToString("0") + " dB" : "– dB";
            using (var f = F(25))
            using (var b = new SolidBrush(z.Status == Status.Loud || z.Status == Status.VeryLoud ? c : playing ? Color.White : Muted))
            {
                var w = g.MeasureString(value, f, PointF.Empty, StringFormat.GenericTypographic).Width;
                g.DrawString(value, f, b, 134 - w, 9, StringFormat.GenericTypographic);
            }

            // 60 bars, newest on the right; height 20..110 dB over 80 px; faint line at 80 dB.
            const float top = 48, bottom = 132, left = 10, width = 124;
            Func<double, float> yOf = db => bottom - (float)((Math.Max(ScaleMin, Math.Min(ScaleMax, db)) - ScaleMin) / (ScaleMax - ScaleMin)) * (bottom - top);
            using (var line = new Pen(Color.FromArgb(0x55, Yellow), 1) { DashPattern = new[] { 3f, 3f } })
                g.DrawLine(line, left, yOf(LoudFrom), left + width, yOf(LoudFrom));
            var h = z.History;
            float step = width / (float)Meter.HistorySeconds;
            for (int i = 0; i < h.Length && i < Meter.HistorySeconds; i++)
            {
                var v = h[h.Length - 1 - i];
                var x = left + width - (i + 1) * step;
                if (double.IsNaN(v))
                {
                    using (var b = new SolidBrush(Unlit)) g.FillRectangle(b, x + 0.3f, bottom - 2, step - 0.6f, 2);
                    continue;
                }
                using (var b = new SolidBrush(ColorOf(StatusOf(v)))) g.FillRectangle(b, x + 0.3f, yOf(v), step - 0.6f, bottom - yOf(v));
            }
        }

        // Round badge (check / exclamation mark / dash) or the red warning triangle, size d at (x, y).
        static void Badge(Graphics g, Status s, float x, float y, float d)
        {
            var c = ColorOf(s);
            using (var mark = new Pen(Dark, d * 0.16f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(c))
            {
                float cx = x + d / 2, cy = y + d / 2;
                if (s == Status.VeryLoud)
                {
                    var tri = new[] { new PointF(cx, y), new PointF(x + d, y + d * 0.92f), new PointF(x, y + d * 0.92f) };
                    using (var edge = new Pen(c, d * 0.12f) { LineJoin = LineJoin.Round }) { g.FillPolygon(fill, tri); g.DrawPolygon(edge, tri); }
                    g.DrawLine(mark, cx, y + d * 0.34f, cx, y + d * 0.58f);
                    g.DrawLine(mark, cx, y + d * 0.76f, cx, y + d * 0.77f);
                    return;
                }
                g.FillEllipse(fill, x, y, d, d);
                if (s == Status.Ok)
                    g.DrawLines(mark, new[] { new PointF(x + d * 0.28f, y + d * 0.52f), new PointF(x + d * 0.44f, y + d * 0.68f), new PointF(x + d * 0.73f, y + d * 0.34f) });
                else if (s == Status.Loud)
                {
                    g.DrawLine(mark, cx, y + d * 0.24f, cx, y + d * 0.56f);
                    g.DrawLine(mark, cx, y + d * 0.76f, cx, y + d * 0.77f);
                }
                else g.DrawLine(mark, x + d * 0.3f, cy, x + d * 0.7f, cy);
            }
        }

        // The ear from the tray icon (64-unit grid), drawn at (x, y) with the given scale.
        static void Ear(Graphics g, float x, float y, float scale, Color ink, float width)
        {
            var state = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(scale, scale);
            using (var pen = new Pen(ink, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var outer = new GraphicsPath())
            using (var inner = new GraphicsPath())
            {
                outer.AddLine(15, 30, 15, 21);
                outer.AddArc(15, 6, 30, 30, 180, 200);
                outer.AddBezier(44.1f, 26.1f, 42, 32, 36, 34, 34, 40);
                outer.AddBezier(34, 40, 32, 46, 26, 48, 21, 44);
                g.DrawPath(pen, outer);
                inner.AddArc(23, 14, 14, 14, 185, 195);
                inner.AddBezier(36.6f, 23.4f, 35.5f, 27, 32, 27, 30.5f, 31);
                g.DrawPath(pen, inner);
            }
            g.Restore(state);
        }

        static GraphicsPath Rounded(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, 2 * r, 2 * r, 180, 90);
            p.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
            p.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
            p.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
            p.CloseFigure();
            return p;
        }

        static void Centered(Graphics g, string text, Font f, Color c, float y)
        {
            using (f)
            using (var b = new SolidBrush(c))
            {
                var w = g.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic).Width;
                g.DrawString(text, f, b, (144 - w) / 2, y, StringFormat.GenericTypographic);
            }
        }

        // ---- plugin icons (built by Bauen.ps1) ----------------------------------------------------------------------

        public static void SaveIcons(string dir)
        {
            Directory.CreateDirectory(dir);
            // Monochrome white ear for the action list and the category (Stream Deck tints these itself).
            foreach (var size in new[] { 20, 28, 40, 56 })
            {
                using (var bmp = new Bitmap(size, size))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var s = size / 64f * 1.3f;
                    Ear(g, size / 2f - 30 * s, size / 2f - 27 * s, s, Color.White, Math.Max(5.2f, 3.2f / s));
                    var name = size == 20 ? "aktion.png" : size == 40 ? "aktion@2x.png" : size == 28 ? "kategorie.png" : "kategorie@2x.png";
                    bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
                }
            }
            // Plugin icon (store / plugin list) and the key's default image: a sample key.
            var sample = new Zustand { Status = Status.Ok, Level = 72, Front = 10, Beat = 1 };
            using (var key = Draw(sample, "pegel"))
            {
                key.Save(Path.Combine(dir, "taste@2x.png"), ImageFormat.Png);
                foreach (var e in new[] { Tuple.Create(72, "taste.png"), Tuple.Create(28, "plugin.png"), Tuple.Create(56, "plugin@2x.png"), Tuple.Create(288, "marketplace.png") })
                    using (var small = new Bitmap(e.Item1, e.Item1))
                    using (var g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(key, 0, 0, e.Item1, e.Item1);
                        small.Save(Path.Combine(dir, e.Item2), ImageFormat.Png);
                    }
            }
        }

        // Test pictures of all states (--render <dir>).
        public static void SaveSamples(string dir)
        {
            Directory.CreateDirectory(dir);
            var hist = Enumerable.Range(0, 60).Select(i => i < 8 ? double.NaN : 62 + 18 * Math.Sin(i / 6.0) + (i % 7)).ToArray();
            var samples = new[]
            {
                Tuple.Create("ok", new Zustand { Status = Status.Ok, Level = 72, Front = 10, Beat = 1 }),
                Tuple.Create("laut", new Zustand { Status = Status.Loud, Level = 84, Front = 12, Beat = 0.3 }),
                Tuple.Create("sehr-laut", new Zustand { Status = Status.VeryLoud, Level = 93, Front = 14, Beat = 0.7 }),
                Tuple.Create("pause", new Zustand { Status = Status.Idle }),
                Tuple.Create("aus", new Zustand { Status = Status.Off }),
            };
            foreach (var s in samples)
            {
                s.Item2.History = hist;
                foreach (var view in new[] { "pegel", "verlauf" })
                    using (var bmp = Draw(s.Item2, view)) bmp.Save(Path.Combine(dir, s.Item1 + "-" + view + ".png"), ImageFormat.Png);
            }
        }
    }
}
