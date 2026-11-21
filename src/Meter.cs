using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Kopfhoererpegel
{
    // What the UI shows. Level is the estimated sound pressure in dB(A); NaN = nothing playing.
    public class Reading
    {
        public bool Connected;
        public string DeviceName = "";
        public double Level = double.NaN;
        public double Fast = double.NaN;           // last 125 ms only: follows the beat
        public double VolumePercent;
        public bool Muted;
        public double[] History = new double[0];   // one value per second, oldest first (NaN = silence)
    }

    // Records what Windows sends to the AirPods (WASAPI loopback, so nothing is opened on the headphones themselves),
    // A-weights it, takes the 1-second average power and adds the AirPods volume plus a calibration offset.
    // That is the same idea as the iPhone's headphone level: it is calculated from the signal, not measured in the ear.
    public class Meter
    {
        public const int HistorySeconds = 60;
        const int TickMs = 125;
        const int WindowTicks = 8;  // 8 x 125 ms = 1 s average

        public double Calibration = 106;  // dB SPL that a full-scale square wave would reach at 100 % volume

        readonly object gate = new object();
        Reading current = new Reading();
        volatile bool running = true;
        Thread thread;

        public Reading Current { get { lock (gate) return current; } }

        public void Start()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "Meter" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public void Stop() { running = false; }

        // Only the stereo (A2DP) endpoint. The hands-free one is skipped on purpose: opening it could pull the AirPods
        // into call mode.
        static bool IsAirPods(CoreAudio.Endpoint e)
        {
            return (e.Name + " " + e.InterfaceName).IndexOf("AirPods", StringComparison.OrdinalIgnoreCase) >= 0
                && (e.Name + " " + e.InterfaceName).IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) < 0;
        }

        // "AirPods Pro 3 von Max - Find My" -> "AirPods Pro 3"
        static string ShortName(CoreAudio.Endpoint e)
        {
            var name = e.InterfaceName.Length > 0 ? e.InterfaceName : e.Name;
            foreach (var cut in new[] { " von ", " - ", " (" })
            {
                var i = name.IndexOf(cut, StringComparison.Ordinal);
                if (i > 0) name = name.Substring(0, i);
            }
            return name.Trim();
        }

        void Run()
        {
            var enumerator = CoreAudio.CreateEnumerator();
            var captures = new Dictionary<string, Capture>();
            var history = new Queue<double>();
            var lastScan = DateTime.MinValue;
            var nextTick = DateTime.Now;
            int ticks = 0;
            double secondPower = 0; int secondTicks = 0;

            while (running)
            {
                if ((DateTime.Now - lastScan).TotalSeconds >= 2)
                {
                    lastScan = DateTime.Now;
                    try
                    {
                        var found = CoreAudio.ActiveRenderEndpoints(enumerator).Where(IsAirPods).ToList();
                        foreach (var gone in captures.Keys.Where(id => found.All(f => f.Id != id)).ToList())
                        {
                            captures[gone].Dispose();
                            captures.Remove(gone);
                        }
                        foreach (var e in found)
                        {
                            if (captures.ContainsKey(e.Id)) { Marshal.ReleaseComObject(e.Device); continue; }
                            try { captures[e.Id] = new Capture(e, ShortName(e)); }
                            catch { Marshal.ReleaseComObject(e.Device); }
                        }
                    }
                    catch { }
                }

                foreach (var c in captures.Values.ToList())
                {
                    if (!c.Drain()) { c.Dispose(); captures.Remove(c.Id); }
                }

                if (DateTime.Now >= nextTick)
                {
                    nextTick = nextTick.AddMilliseconds(TickMs);
                    if (nextTick < DateTime.Now) nextTick = DateTime.Now.AddMilliseconds(TickMs);
                    ticks++;

                    var r = new Reading { Connected = captures.Count > 0 };
                    Capture loudest = null;
                    double best = double.NegativeInfinity;
                    foreach (var c in captures.Values)
                    {
                        var db = c.Tick(Calibration);
                        if (loudest == null || db > best) { loudest = c; best = db; }
                    }
                    if (loudest != null)
                    {
                        r.DeviceName = loudest.Name;
                        r.VolumePercent = loudest.VolumePercent;
                        r.Muted = loudest.Muted;
                        r.Level = best > 20 ? best : double.NaN;  // below that it is silence / noise floor
                        r.Fast = loudest.Fast > 20 ? loudest.Fast : double.NaN;
                    }

                    // History: energy average of the last second.
                    if (!double.IsNaN(r.Level)) secondPower += Math.Pow(10, r.Level / 10);
                    secondTicks++;
                    if (secondTicks == 1000 / TickMs)
                    {
                        history.Enqueue(secondPower > 0 ? 10 * Math.Log10(secondPower / secondTicks) : double.NaN);
                        while (history.Count > HistorySeconds) history.Dequeue();
                        secondPower = 0; secondTicks = 0;
                    }
                    r.History = history.ToArray();
                    lock (gate) current = r;
                }
                Thread.Sleep(15);
            }
            foreach (var c in captures.Values) c.Dispose();
        }

        class Capture : IDisposable
        {
            public readonly string Id, Name;
            public double VolumePercent;
            public bool Muted;

            readonly CoreAudio.IMMDevice device;
            readonly CoreAudio.IAudioClient client;
            readonly CoreAudio.IAudioCaptureClient capture;
            readonly CoreAudio.IAudioEndpointVolume volume;
            readonly CoreAudio.Format format;
            readonly AWeighting[] weighting;
            readonly double[] ring = new double[WindowTicks];
            int ringPos;
            double energy;  // sum of A-weighted squared samples (mean over channels) since the last tick

            public Capture(CoreAudio.Endpoint e, string name)
            {
                Id = e.Id; Name = name; device = e.Device;
                client = CoreAudio.Activate<CoreAudio.IAudioClient>(device, CoreAudio.IID_IAudioClient);
                IntPtr mix;
                Marshal.ThrowExceptionForHR(client.GetMixFormat(out mix));
                try
                {
                    format = CoreAudio.ReadFormat(mix);
                    Marshal.ThrowExceptionForHR(client.Initialize(CoreAudio.AUDCLNT_SHAREMODE_SHARED,
                        CoreAudio.AUDCLNT_STREAMFLAGS_LOOPBACK, 2000000 /* 200 ms */, 0, mix, IntPtr.Zero));
                }
                finally { Marshal.FreeCoTaskMem(mix); }
                var iid = CoreAudio.IID_IAudioCaptureClient;
                object service;
                Marshal.ThrowExceptionForHR(client.GetService(ref iid, out service));
                capture = (CoreAudio.IAudioCaptureClient)service;
                volume = CoreAudio.Activate<CoreAudio.IAudioEndpointVolume>(device, CoreAudio.IID_IAudioEndpointVolume);
                weighting = Enumerable.Range(0, format.Channels).Select(_ => new AWeighting(format.SampleRate)).ToArray();
                Marshal.ThrowExceptionForHR(client.Start());
            }

            // Reads every waiting packet. False = the device is gone.
            public bool Drain()
            {
                try
                {
                    uint packet;
                    while (true)
                    {
                        if (capture.GetNextPacketSize(out packet) != 0) return false;
                        if (packet == 0) return true;
                        IntPtr data; uint frames, flags; ulong pos, qpc;
                        if (capture.GetBuffer(out data, out frames, out flags, out pos, out qpc) != 0) return false;
                        if ((flags & CoreAudio.AUDCLNT_BUFFERFLAGS_SILENT) == 0) Accumulate(data, (int)frames);
                        capture.ReleaseBuffer(frames);
                    }
                }
                catch { return false; }
            }

            void Accumulate(IntPtr data, int frames)
            {
                int ch = format.Channels, bytes = format.BitsPerSample / 8;
                var raw = new byte[frames * format.BlockAlign];
                Marshal.Copy(data, raw, 0, raw.Length);
                double sum = 0;
                for (int f = 0; f < frames; f++)
                {
                    double frame = 0;
                    for (int c = 0; c < ch; c++)
                    {
                        int o = f * format.BlockAlign + c * bytes;
                        double s;
                        if (format.IsFloat) s = BitConverter.ToSingle(raw, o);
                        else if (bytes == 2) s = BitConverter.ToInt16(raw, o) / 32768.0;
                        else if (bytes == 3) s = ((raw[o] << 8 | raw[o + 1] << 16 | raw[o + 2] << 24) >> 8) / 8388608.0;
                        else s = BitConverter.ToInt32(raw, o) / 2147483648.0;
                        var w = weighting[c].Process(s);
                        frame += w * w;
                    }
                    sum += frame / ch;
                }
                energy += sum;
            }

            public double Fast = double.NegativeInfinity;  // dB SPL of the last 125 ms

            // Level in dB SPL (estimated) over the last second; Fast gets the last 125 ms.
            public double Tick(double calibration)
            {
                var block = energy;
                ring[ringPos] = energy;
                ringPos = (ringPos + 1) % ring.Length;
                energy = 0;

                float db = 0, scalar = 1;
                bool mute = false;
                try
                {
                    volume.GetMasterVolumeLevel(out db);
                    volume.GetMasterVolumeLevelScalar(out scalar);
                    volume.GetMute(out mute);
                }
                catch { }
                VolumePercent = Math.Round(scalar * 100);
                Muted = mute;
                Fast = double.NegativeInfinity;
                if (mute) return double.NegativeInfinity;
                var blockSquare = block / (format.SampleRate * (double)TickMs / 1000);
                if (blockSquare > 1e-12) Fast = 10 * Math.Log10(blockSquare) + db + calibration;

                // Mean square over the whole second; time without packets counts as silence.
                var meanSquare = ring.Sum() / (format.SampleRate * (double)TickMs * WindowTicks / 1000);
                if (meanSquare <= 1e-12) return double.NegativeInfinity;
                // Loopback is recorded before the endpoint volume, so the volume is added here.
                return 10 * Math.Log10(meanSquare) + db + calibration;
            }

            public void Dispose()
            {
                try { client.Stop(); } catch { }
                foreach (var o in new object[] { capture, client, volume, device })
                    try { if (o != null) Marshal.ReleaseComObject(o); } catch { }
            }
        }

        // --probe <file>: lists the endpoints and records 4 s of levels (for checking without the UI).
        public static void Probe(string file)
        {
            var sb = new StringBuilder();
            var t = new Thread(() =>
            {
                try
                {
                    var en = CoreAudio.CreateEnumerator();
                    foreach (var e in CoreAudio.ActiveRenderEndpoints(en))
                    {
                        sb.AppendLine((IsAirPods(e) ? "[AirPods] " : "          ") + e.Name + " | " + e.InterfaceName + " -> " + ShortName(e));
                        if (!IsAirPods(e)) continue;
                        var v = CoreAudio.Activate<CoreAudio.IAudioEndpointVolume>(e.Device, CoreAudio.IID_IAudioEndpointVolume);
                        float db, sc, min, max, inc; uint hw; bool mute;
                        v.GetMasterVolumeLevel(out db); v.GetMasterVolumeLevelScalar(out sc); v.GetMute(out mute);
                        v.QueryHardwareSupport(out hw); v.GetVolumeRange(out min, out max, out inc);
                        sb.AppendFormat("    Lautstärke {0:0.0} dB / {1:0} %, stumm={2}, Hardware-Maske={3}, Bereich {4:0.0}..{5:0.0} dB Schritt {6:0.00}\r\n",
                            db, sc * 100, mute, hw, min, max, inc);
                    }
                    var m = new Meter();
                    m.Start();
                    for (int i = 0; i < 16; i++)
                    {
                        Thread.Sleep(250);
                        var r = m.Current;
                        sb.AppendFormat("    {0,5} ms  verbunden={1}  {2}  Pegel={3:0.0}  Vol={4}%\r\n", (i + 1) * 250, r.Connected, r.DeviceName, r.Level, r.VolumePercent);
                    }
                    m.Stop();
                }
                catch (Exception ex) { sb.AppendLine(ex.ToString()); }
            });
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            t.Join();
            System.IO.File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
        }
    }

    // IEC 61672 A-weighting as six first-order sections (bilinear transform with pre-warped poles), 0 dB at 1 kHz.
    class AWeighting
    {
        readonly double[] b0 = new double[6], b1 = new double[6], a1 = new double[6], x1 = new double[6], y1 = new double[6];
        readonly double gain;

        public AWeighting(int sampleRate)
        {
            double[] poles = { 20.598997, 20.598997, 107.65265, 737.86223, 12194.217, 12194.217 };
            double k = 2.0 * sampleRate, gainAt1k = 1, omega = 2 * Math.PI * 1000 / sampleRate;
            for (int i = 0; i < 6; i++)
            {
                var w = k * Math.Tan(Math.PI * Math.Min(poles[i], sampleRate * 0.45) / sampleRate);
                bool highPass = i < 4;
                b0[i] = (highPass ? k : w) / (k + w);
                b1[i] = highPass ? -b0[i] : b0[i];
                a1[i] = (w - k) / (k + w);
                // |H(e^jω)| of this section at 1 kHz
                double nr = b0[i] + b1[i] * Math.Cos(omega), ni = -b1[i] * Math.Sin(omega);
                double dr = 1 + a1[i] * Math.Cos(omega), di = -a1[i] * Math.Sin(omega);
                gainAt1k *= Math.Sqrt((nr * nr + ni * ni) / (dr * dr + di * di));
            }
            gain = 1 / gainAt1k;
        }

        public double Process(double x)
        {
            for (int i = 0; i < 6; i++)
            {
                var y = b0[i] * x + b1[i] * x1[i] - a1[i] * y1[i];
                x1[i] = x; y1[i] = y;
                x = y;
            }
            return x * gain;
        }
    }
}
