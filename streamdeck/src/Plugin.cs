using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Kopfhoererpegel
{
    // Stream Deck plugin: one key action "Kopfhörerpegel" that shows the live dB of the AirPods.
    // It measures on its own with the same Meter as the tray app (passive WASAPI loopback, same calibration file),
    // so the key works whether or not the tray app is running.
    // Stream Deck starts it as: <exe> -port N -pluginUUID X -registerEvent registerPlugin -info {...}
    static class Plugin
    {
        static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kopfhoererpegel");

        static ClientWebSocket socket;
        static readonly object sendGate = new object();
        static readonly JavaScriptSerializer json = new JavaScriptSerializer();

        // Visible keys: context -> view ("pegel" or "verlauf") and what was last sent (so unchanged images are skipped).
        class Key { public string View = "pegel"; public string Last = ""; }
        static readonly Dictionary<string, Key> keys = new Dictionary<string, Key>();
        static Meter meter;
        static readonly Kachel.Zustand zustand = new Kachel.Zustand();

        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);
            if (args.Length > 1 && args[0] == "--icons") { Kachel.SaveIcons(args[1]); return 0; }
            if (args.Length > 1 && args[0] == "--render") { Kachel.SaveSamples(args[1]); return 0; }

            string port = Arg(args, "-port"), uuid = Arg(args, "-pluginUUID"), register = Arg(args, "-registerEvent");
            if (port == null || uuid == null || register == null) return 1;
            try
            {
                socket = new ClientWebSocket();
                socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port), CancellationToken.None).Wait();
                Send("{\"event\":" + Q(register) + ",\"uuid\":" + Q(uuid) + "}");
                new Thread(Render) { IsBackground = true, Name = "Render" }.Start();
                Receive();
            }
            catch (Exception e) { Log(e); }
            return 0;
        }

        static string Arg(string[] args, string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static void Receive()
        {
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            while (socket.State == WebSocketState.Open)
            {
                var r = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).Result;
                if (r.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, r.Count);
                if (!r.EndOfMessage) continue;
                var text = Encoding.UTF8.GetString(message.ToArray());
                message.SetLength(0);
                try { Handle(text); } catch (Exception e) { Log(e); }
            }
            // Stream Deck closed the connection (quit or plugin reload): exit, it starts the plugin again when needed.
        }

        static void Handle(string text)
        {
            var msg = json.Deserialize<Dictionary<string, object>>(text);
            object ev, ctx;
            if (!msg.TryGetValue("event", out ev) || !msg.TryGetValue("context", out ctx)) return;
            var context = ctx as string;
            if (context == null) return;
            switch (ev as string)
            {
                case "willAppear":
                case "didReceiveSettings":
                    lock (keys)
                    {
                        Key k;
                        if (!keys.TryGetValue(context, out k)) keys[context] = k = new Key();
                        k.View = ViewFrom(msg) ?? k.View;
                        k.Last = "";
                        if (meter == null) StartMeter();
                    }
                    break;
                case "willDisappear":
                    lock (keys)
                    {
                        keys.Remove(context);
                        if (keys.Count == 0 && meter != null) { meter.Stop(); meter = null; }  // no key visible: stop recording
                    }
                    break;
                case "keyDown":
                    lock (keys)
                    {
                        Key k;
                        if (!keys.TryGetValue(context, out k)) break;
                        k.View = k.View == "verlauf" ? "pegel" : "verlauf";  // press switches level <-> last 60 seconds
                        k.Last = "";
                        Send("{\"event\":\"setSettings\",\"context\":" + Q(context) + ",\"payload\":{\"ansicht\":" + Q(k.View) + "}}");
                    }
                    break;
            }
        }

        static string ViewFrom(Dictionary<string, object> msg)
        {
            object p, s, v;
            var payload = msg.TryGetValue("payload", out p) ? p as Dictionary<string, object> : null;
            var settings = payload != null && payload.TryGetValue("settings", out s) ? s as Dictionary<string, object> : null;
            return settings != null && settings.TryGetValue("ansicht", out v) ? v as string : null;
        }

        static void StartMeter()
        {
            meter = new Meter { Calibration = LoadCalibration() };
            meter.Start();
        }

        // Same file the tray app writes, so a calibration done there also applies to the key.
        static double LoadCalibration()
        {
            try
            {
                foreach (var line in File.ReadAllLines(Path.Combine(DataDir, "einstellungen.txt")))
                    if (line.StartsWith("Kalibrierung="))
                        return double.Parse(line.Substring(13), CultureInfo.InvariantCulture);
            }
            catch { }
            return 106;
        }

        // 8 frames per second (= the meter's 125 ms blocks); only changed images are sent.
        static void Render()
        {
            int frame = 0;
            while (true)
            {
                Thread.Sleep(125);
                try
                {
                    Meter m;
                    List<KeyValuePair<string, Key>> visible;
                    lock (keys) { m = meter; visible = keys.ToList(); }
                    if (m == null || visible.Count == 0) continue;
                    if (++frame % 40 == 0) m.Calibration = LoadCalibration();
                    zustand.Update(m.Current);
                    foreach (var view in visible.Select(v => v.Value.View).Distinct())
                    {
                        var signature = zustand.Signature(view);
                        string image = null;
                        foreach (var kv in visible.Where(v => v.Value.View == view && v.Value.Last != signature))
                        {
                            image = image ?? "data:image/png;base64," + Convert.ToBase64String(Kachel.Png(zustand, view));
                            Send("{\"event\":\"setImage\",\"context\":" + Q(kv.Key) + ",\"payload\":{\"image\":" + Q(image) + ",\"target\":0}}");
                            kv.Value.Last = signature;
                        }
                    }
                }
                catch (Exception e) { Log(e); Thread.Sleep(2000); }
            }
        }

        static void Send(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            lock (sendGate)
                socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
        }

        static string Q(string s) { return json.Serialize(s); }

        static void Log(Exception e)
        {
            if (e == null) return;
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(Path.Combine(DataDir, "streamdeck.log"), DateTime.Now.ToString("s") + " " + e + "\r\n\r\n");
            }
            catch { }
        }
    }
}
