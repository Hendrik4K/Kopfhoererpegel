using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Kopfhoererpegel
{
    // Tray icon for the AirPods headphone level. Hover shows the live dB card, a left click pins it, a right click
    // turns the card into the settings (autostart, calibration, quit, restart).
    public static class Program
    {
        static Forms.NotifyIcon tray;
        static Popup popup;
        static Meter meter;
        static bool demo;
        static DateTime outsideSince = DateTime.MaxValue;
        static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kopfhoererpegel");
        static string SettingsFile { get { return Path.Combine(DataDir, "einstellungen.txt"); } }
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // --probe <file>: endpoint + level check without UI. --render-tray <dir>: tray icon states as PNG.
        // --render-icon <file.ico>: app icon. --render-shots <dir> <wallpaper>: README pictures and GIF frames.
        // --demo: fake levels (for looking at the card without audio). --show: open the card pinned at start.
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--probe") { Meter.Probe(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--render-tray") { RenderTrayStates(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--render-icon") { Shots.SaveIcon(args[1]); return 0; }
            if (args.Length == 3 && args[0] == "--render-shots") { Shots.Render(args[1], args[2]); return 0; }
            using (var single = new Mutex(false, "Hendrik4K.Kopfhoererpegel"))
            {
                // After "Neu starten" the new instance waits until the old one has let go.
                // A second start only asks the running app to show its card (so a start never looks like nothing happened).
                var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Hendrik4K.Kopfhoererpegel.Zeigen");
                try { if (!single.WaitOne(args.Contains("--restart") ? 10000 : 0)) { wake.Set(); return 0; } }
                catch (AbandonedMutexException) { }
                demo = args.Contains("--demo");
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // Errors go to fehler.log instead of a dialog.
                Forms.Application.SetUnhandledExceptionMode(Forms.UnhandledExceptionMode.CatchException);
                Forms.Application.ThreadException += (s, e) => Log(e.Exception);
                app.DispatcherUnhandledException += (s, e) => { Log(e.Exception); e.Handled = true; };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);
                meter = new Meter { Calibration = LoadSetting("Kalibrierung", 106) };
                // Older autostart entries had no --autostart yet (without it Windows startup would show the start card).
                if (AutostartOn()) SetAutostart(true);
                if (!demo) meter.Start();
                popup = new Popup();
                // Create the window handle (and its frosted backdrop) now, not in the middle of a tray mouse event.
                new System.Windows.Interop.WindowInteropHelper(popup).EnsureHandle();

                // Settings live in the card itself (right click), no context menu.
                popup.Quit = () => { tray.Visible = false; meter.Stop(); app.Shutdown(); };
                popup.Restart = () =>
                {
                    tray.Visible = false;
                    meter.Stop();
                    System.Diagnostics.Process.Start(Forms.Application.ExecutablePath, "--restart" + (demo ? " --demo" : ""));
                    app.Shutdown();
                };
                popup.AutostartOn = AutostartOn;
                popup.SetAutostart = SetAutostart;
                popup.GetCalibration = () => meter.Calibration;
                popup.SetCalibration = v => { meter.Calibration = v; SaveSettings(); };

                // No tooltip text: the card is the tooltip.
                tray = new Forms.NotifyIcon { Text = "", Visible = demo };
                tray.MouseMove += delegate { if (!popup.IsVisible || popup.FadingOut) Open(); outsideSince = DateTime.MaxValue; };
                tray.MouseClick += (s, e) =>
                {
                    if (e.Button == Forms.MouseButtons.Right)
                    {
                        popup.Pinned = false;
                        popup.ShowView(true, !popup.IsVisible || popup.FadingOut);
                        Open();
                        return;
                    }
                    if (e.Button != Forms.MouseButtons.Left) return;
                    if (popup.Pinned && !popup.SettingsShown) { popup.FadeOut(); return; }
                    popup.ShowView(false, !popup.IsVisible || popup.FadingOut);
                    Open();
                    popup.Pinned = true;
                };
                AnimateTray(Current());

                // 30 fps: bar animation, tray state, hover check. Texts 4x per second.
                int frame = 0;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                timer.Tick += delegate
                {
                    var r = Current();
                    AnimateTray(r);
                    if (!popup.IsVisible) return;
                    popup.Animate(r);
                    if (frame++ % 8 == 0) popup.Render(r);
                    if (popup.Pinned || popup.FadingOut || DateTime.Now < noticeUntil) return;
                    // The card stays while the mouse is on the icon or on the card, and goes a moment after it left both.
                    if (CursorOnIcon() || popup.CursorOnCard()) outsideSince = DateTime.MaxValue;
                    else if (outsideSince == DateTime.MaxValue) outsideSince = DateTime.Now;
                    else if ((DateTime.Now - outsideSince).TotalMilliseconds > (popup.SettingsShown ? 500 : 300)) popup.FadeOut();
                };
                timer.Start();
                if (args.Contains("--show")) app.Dispatcher.BeginInvoke(new Action(() => { Open(); popup.Pinned = true; }));
                // Started by hand (not by Windows autostart or "Neu starten"): show the card for a few seconds.
                else if (!args.Contains("--autostart") && !args.Contains("--restart"))
                    app.Dispatcher.BeginInvoke(new Action(ShowNotice), DispatcherPriority.ApplicationIdle);
                new Thread(() => { while (wake.WaitOne()) app.Dispatcher.BeginInvoke(new Action(ShowNotice)); }) { IsBackground = true }.Start();
                app.Run();
                tray.Dispose();
                single.ReleaseMutex();
            }
            return 0;
        }

        static bool opening;

        static void Open()
        {
            if (opening) return;  // never show the card again from inside its own Show()
            opening = true;
            try { OpenCard(); }
            finally { opening = false; }
        }

        static DateTime noticeUntil = DateTime.MinValue;

        // The card for 4 seconds: with AirPods above the ear as usual, without them bottom right with
        // "Kopfhörerpegel läuft · Nicht verbunden" (the ear itself stays hidden until they connect).
        static void ShowNotice()
        {
            if (popup.IsVisible && !popup.FadingOut) return;
            popup.ShowView(false, true);
            popup.ShowRunning(true);
            noticeUntil = DateTime.Now.AddSeconds(4);
            outsideSince = DateTime.MaxValue;
            if (tray.Visible) { Open(); return; }
            var r = Current();
            popup.Render(r);
            popup.Animate(r);
            var wa = Forms.Screen.PrimaryScreen.WorkingArea;
            popup.ShowAtTray(wa.Right - 160, wa.Right - 160, wa.Bottom);
        }

        static void OpenCard()
        {
            var r = Current();
            popup.Render(r);
            popup.Animate(r);
            Rect rect;
            if (TrayRect(out rect)) popup.ShowAtTray(rect.Left, rect.Right, rect.Top);
            else { var p = Forms.Cursor.Position; popup.ShowAtTray(p.X - 8, p.X + 8, p.Y - 8); }
        }

        static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(Path.Combine(DataDir, "fehler.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + "\r\n\r\n");
            }
            catch { }
        }

        static Reading Current()
        {
            return demo ? DemoReading() : meter.Current;
        }

        // A song-like level swinging between about 65 and 92 dB (all three colours) with a minute of history.
        static Reading DemoReading()
        {
            Func<double, double> level = t => 78 + 9 * Math.Sin(t / 6.0) + 3 * Math.Sin(t * 1.7) + 2 * Math.Sin(t * 4.3);
            var now = (DateTime.Now - DateTime.Today).TotalSeconds;
            return new Reading
            {
                Connected = true, DeviceName = "AirPods Pro 3", VolumePercent = 62,
                Level = level(now),
                // A kick about twice a second for the beat animation.
                Fast = level(now) - 3 + 9 * Math.Pow(Math.Max(0, Math.Sin(now * Math.PI * 2.1)), 6),
                History = Enumerable.Range(0, Meter.HistorySeconds).Select(i => i < 6 ? double.NaN : level(Math.Floor(now) - Meter.HistorySeconds + i)).ToArray()
            };
        }

        // ---- tray icon ----

        static double trayBeat = 0.5;  // 0 = quieter than average, 1 = on a beat (same idea as the card)
        static string trayKey = "";

        // Three dots under the ear. Which ones pulse with the beat depends on the status (the outer pulsing dot
        // strongly, the inner one gently, in fixed steps like the card):
        //   OK        dot 1 gentle, dot 2 strong, dot 3 off
        //   Laut      dot 1 still, dot 2 gentle, dot 3 strong
        //   Sehr laut dot 1 + 2 still, dot 3 strong
        // Redraws the icon only when it would look different.
        static void AnimateTray(Reading r)
        {
            var s = Popup.StatusOf(r);
            // AirPods not connected: no icon at all (and the card goes away); it comes back as soon as they connect.
            var show = s != Status.Off;
            if (tray.Visible != show)
            {
                tray.Visible = show;
                if (!show) popup.FadeOut();
            }
            var playing = s != Status.Off && s != Status.Idle;
            var deviation = playing && !double.IsNaN(r.Fast) ? r.Fast - r.Level : -4;
            var k = Math.Max(0, Math.Min(1, 0.5 + deviation / 8));
            trayBeat += (k - trayBeat) * (k > trayBeat ? 0.9 : 0.45);

            double strong = 0.25 + 0.75 * Step(trayBeat, 4), gentle = 0.6 + 0.4 * Step(trayBeat, 5);
            var dots = s == Status.Ok ? new[] { gentle, strong, 0 }
                     : s == Status.Loud ? new[] { 1, gentle, strong }
                     : s == Status.VeryLoud ? new[] { 1, 1, strong }
                     : new double[] { 0, 0, 0 };

            var light = TaskbarIsLight();
            var key = s + "|" + string.Join(",", dots.Select(d => Math.Round(d * 20))) + "|" + light;
            if (key == trayKey) return;
            trayKey = key;
            using (var bmp = RenderTray(Forms.SystemInformation.SmallIconSize.Width, s, dots, light))
            {
                var old = tray.Icon;
                tray.Icon = Drawing.Icon.FromHandle(bmp.GetHicon());
                if (old != null) { DestroyIcon(old.Handle); old.Dispose(); }
            }
        }

        static double Step(double v, int levels)
        {
            return Math.Round(v * (levels - 1)) / (levels - 1);
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

        static bool lightChecked;
        static DateTime lightAt;
        static bool TaskbarIsLight()
        {
            if ((DateTime.Now - lightAt).TotalSeconds < 5) return lightChecked;
            lightAt = DateTime.Now;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return lightChecked = k != null && (k.GetValue("SystemUsesLightTheme") as int? ?? 0) == 1;
        }

        static Drawing.Color Gdi(System.Windows.Media.Color c, int alpha = 255)
        {
            return Drawing.Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        static Drawing.Pen RoundPen(Drawing.Color c, float width)
        {
            return new Drawing.Pen(c, width)
            {
                StartCap = Drawing.Drawing2D.LineCap.Round, EndCap = Drawing.Drawing2D.LineCap.Round, LineJoin = Drawing.Drawing2D.LineJoin.Round
            };
        }

        // Ear with a status badge (green check, yellow exclamation mark, red warning triangle) and three dots underneath
        // (dots[i] 0 = dim, 1 = full colour). Drawn on a 64-unit grid, 4x oversampled and scaled down for clean edges.
        internal static Drawing.Bitmap RenderTray(int size, Status s, double[] dots, bool light)
        {
            const int k = 4;
            var big = new Drawing.Bitmap(size * k, size * k);
            using (var g = Drawing.Graphics.FromImage(big))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float u = size * k / 64f;
                g.ScaleTransform(u, u);
                var ink = light ? Drawing.Color.FromArgb(0x1A, 0x1A, 0x1A) : Drawing.Color.White;

                using (var pen = RoundPen(ink, size <= 20 ? 6.4f : 5.2f))
                using (var outer = new Drawing.Drawing2D.GraphicsPath())
                using (var inner = new Drawing.Drawing2D.GraphicsPath())
                {
                    // Ear drawn 10 % larger around its centre so it fills the small tray square better.
                    var state = g.Save();
                    g.TranslateTransform(30, 26);
                    g.ScaleTransform(1.1f, 1.1f);
                    g.TranslateTransform(-30, -26);
                    // Outer rim: down the left side, over the top, down the right and curling in to the lobe.
                    outer.AddLine(15, 30, 15, 21);
                    outer.AddArc(15, 6, 30, 30, 180, 200);
                    outer.AddBezier(44.1f, 26.1f, 42, 32, 36, 34, 34, 40);
                    outer.AddBezier(34, 40, 32, 46, 26, 48, 21, 44);
                    g.DrawPath(pen, outer);
                    // Inner fold
                    inner.AddArc(23, 14, 14, 14, 185, 195);
                    inner.AddBezier(36.6f, 23.4f, 35.5f, 27, 32, 27, 30.5f, 31);
                    g.DrawPath(pen, inner);
                    g.Restore(state);
                }

                var c = Popup.ColorOf(s == Status.Off || s == Status.Idle ? Status.Ok : s);
                var dark = Drawing.Color.FromArgb(0x1C, 0x1C, 0x1C);
                if (s == Status.VeryLoud)
                {
                    // Red warning triangle; a transparent margin cut into the ear keeps it apart on any taskbar.
                    var tri = new[] { new Drawing.PointF(47, 26.5f), new Drawing.PointF(60, 50), new Drawing.PointF(34, 50) };
                    g.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy;
                    using (var clear = new Drawing.SolidBrush(Drawing.Color.Transparent)) g.FillPolygon(clear, tri);
                    using (var clearPen = RoundPen(Drawing.Color.Transparent, 9)) g.DrawPolygon(clearPen, tri);
                    g.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceOver;
                    using (var fill = new Drawing.SolidBrush(Gdi(c))) g.FillPolygon(fill, tri);
                    using (var edge = RoundPen(Gdi(c), 3)) g.DrawPolygon(edge, tri);
                    using (var mark = RoundPen(dark, 3.8f)) { g.DrawLine(mark, 47, 34.5f, 47, 41.5f); g.DrawLine(mark, 47, 46, 47, 46.2f); }
                }
                else if (s == Status.Ok || s == Status.Loud)
                {
                    // Round badge bottom right of the ear, cut free the same way.
                    g.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy;
                    using (var ring = new Drawing.SolidBrush(Drawing.Color.Transparent)) g.FillEllipse(ring, 32, 24, 30, 30);
                    g.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceOver;
                    using (var fill = new Drawing.SolidBrush(Gdi(c))) g.FillEllipse(fill, 35.5f, 27.5f, 23, 23);
                    using (var mark = RoundPen(dark, 4.2f))
                    {
                        if (s == Status.Ok) g.DrawLines(mark, new[] { new Drawing.PointF(41.5f, 39.5f), new Drawing.PointF(45.5f, 43.5f), new Drawing.PointF(52.5f, 35) });
                        else { g.DrawLine(mark, 47, 33, 47, 40.5f); g.DrawLine(mark, 47, 45.2f, 47, 45.4f); }
                    }
                }

                // Three dots, centred under the ear.
                for (int i = 0; i < 3; i++)
                {
                    var lit = Math.Max(0, Math.Min(1, dots[i]));
                    var dim = Gdi(c, 0x55);
                    var full = Gdi(c);
                    var col = Drawing.Color.FromArgb(
                        (int)(dim.A + (full.A - dim.A) * lit), (int)(dim.R + (full.R - dim.R) * lit),
                        (int)(dim.G + (full.G - dim.G) * lit), (int)(dim.B + (full.B - dim.B) * lit));
                    using (var b = new Drawing.SolidBrush(col)) g.FillEllipse(b, 9 + i * 17, 52, 12, 12);
                }
            }
            var small = new Drawing.Bitmap(size, size);
            using (var g = Drawing.Graphics.FromImage(small))
            {
                g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(big, 0, 0, size, size);
            }
            big.Dispose();
            return small;
        }

        static void RenderTrayStates(string dir)
        {
            Directory.CreateDirectory(dir);
            var states = new[] { Tuple.Create(Status.Idle, new double[] { 0, 0, 0 }), Tuple.Create(Status.Ok, new[] { 0.8, 0.5, 0 }),
                                 Tuple.Create(Status.Loud, new[] { 1, 0.8, 0.4 }), Tuple.Create(Status.VeryLoud, new[] { 1, 1, 0.6 }) };
            foreach (var size in new[] { 16, 20, 24, 32, 64 })
                foreach (var st in states)
                    foreach (var light in new[] { false, true })
                        using (var bmp = RenderTray(size, st.Item1, st.Item2, light))
                            bmp.Save(Path.Combine(dir, st.Item1 + (light ? "-hell-" : "-dunkel-") + size + ".png"));
        }

        // ---- where the icon is ----

        [StructLayout(LayoutKind.Sequential)]
        struct NotifyIconId { public int cbSize; public IntPtr hWnd; public int uID; public Guid guidItem; }
        [StructLayout(LayoutKind.Sequential)]
        struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NotifyIconId id, out Rect rect);
        static NotifyIconId? trayId;

        // Screen rectangle of our tray icon (NotifyIcon keeps its window and id private).
        static bool TrayRect(out Rect r)
        {
            r = new Rect();
            try
            {
                if (trayId == null)
                {
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var window = (Forms.NativeWindow)typeof(Forms.NotifyIcon).GetField("window", flags).GetValue(tray);
                    var id = new NotifyIconId { hWnd = window.Handle, uID = (int)typeof(Forms.NotifyIcon).GetField("id", flags).GetValue(tray) };
                    id.cbSize = Marshal.SizeOf(id);
                    trayId = id;
                }
                var copy = trayId.Value;
                return Shell_NotifyIconGetRect(ref copy, out r) == 0;
            }
            catch { return false; }
        }

        static bool CursorOnIcon()
        {
            Rect r;
            if (!TrayRect(out r)) return false;
            var p = Forms.Cursor.Position;
            return p.X >= r.Left - 1 && p.X <= r.Right + 1 && p.Y >= r.Top - 1 && p.Y <= r.Bottom + 1;
        }

        // ---- settings ----

        static double LoadSetting(string name, double fallback)
        {
            try
            {
                foreach (var line in File.ReadAllLines(SettingsFile))
                    if (line.StartsWith(name + "="))
                        return double.Parse(line.Substring(name.Length + 1), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { }
            return fallback;
        }

        static void SaveSettings()
        {
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(SettingsFile, "Kalibrierung=" + meter.Calibration.ToString(inv) + "\r\n");
            }
            catch { }
        }

        static bool AutostartOn()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("Kopfhoererpegel") != null;
        }

        static void SetAutostart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("Kopfhoererpegel", "\"" + Forms.Application.ExecutablePath + "\" --autostart");
                else k.DeleteValue("Kopfhoererpegel", false);
            }
        }
    }
}
