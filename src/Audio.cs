using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Autumn
{
    // A small streaming mixer on waveOut. One-shot voices and continuous ambiences are mixed on a
    // worker thread. The device is opened only while something is audible and closed after a few
    // seconds of silence, so it never keeps the PC awake.
    class Audio : ISound, IDisposable
    {
        public const int Rate = 44100;
        const int Block = 1024, NBuf = 4, HdrSize = 48, FlagsOffset = 24;

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr hwo, int dev, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr hwo);

        class Voice { public float[] Data; public int Pos; public float Gain, Pan; }

        public volatile float Master = 0.35f;     // 0 off, 0.35 quiet, 0.8 full
        public volatile bool Idle;                // person away: fade everything out
        readonly object gate = new object();
        readonly List<Voice> voices = new List<Voice>();
        readonly Dictionary<string, Ambient> ambients = new Dictionary<string, Ambient>();
        readonly Synth synth = new Synth();
        readonly float[] L = new float[Block], R = new float[Block];
        IntPtr hwo;
        readonly IntPtr[] hdr = new IntPtr[NBuf], buf = new IntPtr[NBuf];
        bool open;
        readonly Thread thread;
        volatile bool running = true;
        double silentFor;
        float masterNow;
        public string LastError = "";
        public bool IsOpen { get { return open; } }
        public int Opens;

        public Audio()
        {
            foreach (var a in new Ambient[] { new RainNoise(), new WindNoise(), new Crickets(), new Hearth() }) ambients[a.Name] = a;
            for (int i = 0; i < NBuf; i++)
            {
                hdr[i] = Marshal.AllocHGlobal(HdrSize);
                buf[i] = Marshal.AllocHGlobal(Block * 4);
            }
            thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "Autumn audio" };
            thread.Start();
        }

        // ---------------------------------------------------------------- ISound

        public void Play(string name, float gain, float pan)
        {
            if (Master <= 0.001f || Idle) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                float[] data;
                try { data = synth.Make(name, gain); } catch { return; }
                if (data == null) return;
                lock (gate) { if (voices.Count < 24) voices.Add(new Voice { Data = data, Gain = Math.Min(1.2f, gain), Pan = pan }); }
            });
        }

        public void Ambience(string name, float level)
        {
            Ambient a;
            if (ambients.TryGetValue(name, out a)) a.Target = Math.Max(0, Math.Min(1, level));
        }

        public void CricketTemperature(float t) { var c = ambients["crickets"] as Crickets; if (c != null) c.Temperature = t; }

        // ---------------------------------------------------------------- device

        bool Open()
        {
            var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 2, nSamplesPerSec = Rate, wBitsPerSample = 16, nBlockAlign = 4, nAvgBytesPerSec = Rate * 4 };
            int r = waveOutOpen(out hwo, -1, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != 0) { LastError = "waveOutOpen " + r; return false; }
            for (int i = 0; i < NBuf; i++)
            {
                for (int k = 0; k < HdrSize; k++) Marshal.WriteByte(hdr[i], k, 0);
                Marshal.WriteIntPtr(hdr[i], 0, buf[i]);
                Marshal.WriteInt32(hdr[i], 8, Block * 4);
                waveOutPrepareHeader(hwo, hdr[i], HdrSize);
                Fill(i);
                waveOutWrite(hwo, hdr[i], HdrSize);
            }
            open = true;
            Opens++;
            return true;
        }

        void Close()
        {
            if (!open) return;
            waveOutReset(hwo);
            for (int i = 0; i < NBuf; i++) waveOutUnprepareHeader(hwo, hdr[i], HdrSize);
            waveOutClose(hwo);
            open = false;
        }

        void Run()
        {
            while (running)
            {
                bool audible;
                lock (gate) audible = voices.Count > 0;
                foreach (var a in ambients.Values) audible |= a.Level > 0.002f || (a.Target > 0.002f && !Idle && Master > 0.001f);
                if (audible) silentFor = 0;
                if (audible && !open) { if (!Open()) { Thread.Sleep(2000); continue; } }
                if (open)
                {
                    for (int i = 0; i < NBuf; i++)
                        if ((Marshal.ReadInt32(hdr[i], FlagsOffset) & 1) != 0)
                        {
                            Fill(i);
                            waveOutWrite(hwo, hdr[i], HdrSize);
                        }
                    if (!audible) { silentFor += 0.005; if (silentFor > 3) Close(); }
                }
                Thread.Sleep(5);
            }
            Close();
        }

        void Fill(int i)
        {
            Array.Clear(L, 0, Block); Array.Clear(R, 0, Block);
            float target = Idle ? 0 : Master;
            foreach (var a in ambients.Values)
            {
                float tgt = Idle || Master <= 0.001f ? 0 : a.Target;
                a.Level += (tgt - a.Level) * 0.02f;                 // ~1 s glide per block series
                if (a.Level > 0.002f) a.Render(L, R, Block, a.Level);
                else a.Level = Math.Max(0, a.Level);
            }
            lock (gate)
            {
                for (int v = voices.Count - 1; v >= 0; v--)
                {
                    var vo = voices[v];
                    float gl = (float)Math.Cos((vo.Pan + 1) * Math.PI / 4) * vo.Gain, gr = (float)Math.Sin((vo.Pan + 1) * Math.PI / 4) * vo.Gain;
                    int n = Math.Min(Block, vo.Data.Length - vo.Pos);
                    for (int k = 0; k < n; k++) { float x = vo.Data[vo.Pos + k]; L[k] += x * gl; R[k] += x * gr; }
                    vo.Pos += n;
                    if (vo.Pos >= vo.Data.Length) voices.RemoveAt(v);
                }
            }
            unsafe
            {
                short* o = (short*)buf[i];
                for (int k = 0; k < Block; k++)
                {
                    masterNow += (target - masterNow) * 0.0005f;
                    o[2 * k] = Clip(L[k] * masterNow);
                    o[2 * k + 1] = Clip(R[k] * masterNow);
                }
            }
        }

        // Gentle soft-clip so stacked sounds never crackle.
        static short Clip(float x)
        {
            float y = x > 0.6f ? 0.6f + (x - 0.6f) / (1 + (x - 0.6f) * 2.5f) : x < -0.6f ? -0.6f + (x + 0.6f) / (1 - (x + 0.6f) * 2.5f) : x;
            return (short)(Math.Max(-1f, Math.Min(1f, y)) * 32000);
        }

        public void Dispose()
        {
            running = false;
            thread.Join(500);
            for (int i = 0; i < NBuf; i++) { Marshal.FreeHGlobal(hdr[i]); Marshal.FreeHGlobal(buf[i]); }
        }
    }

    // A continuous sound whose loudness follows a level set by the simulation.
    abstract class Ambient
    {
        public string Name;
        public volatile float Target;
        public float Level;
        protected readonly Random rng = new Random();
        protected float Noise() { return (float)(rng.NextDouble() * 2 - 1); }
        public abstract void Render(float[] L, float[] R, int n, float level);
    }

    // Steady hiss of rainfall plus individual drop ticks scattered across the stereo field.
    class RainNoise : Ambient
    {
        float hl, hr, ll, lr, pl, pr;
        public RainNoise() { Name = "rain"; }
        public override void Render(float[] L, float[] R, int n, float level)
        {
            float g = 0.16f * level;
            for (int i = 0; i < n; i++)
            {
                float a = Noise(), b = Noise();
                ll += (a - ll) * 0.35f; lr += (b - lr) * 0.35f;            // low-pass ~ 3 kHz
                float xl = ll - hl, xr = lr - hr;                         // high-pass ~ 300 Hz
                hl += xl * 0.045f; hr += xr * 0.045f;
                L[i] += xl * g; R[i] += xr * g;
                if (rng.NextDouble() < 0.0012 * level)
                {
                    // a close drop: short bright tick
                    float amp = 0.25f * level * (float)rng.NextDouble(), pan = (float)rng.NextDouble();
                    int len = 40 + rng.Next(60);
                    for (int k = 0; k < len && i + k < n; k++)
                    {
                        float t = Noise() * amp * (float)Math.Exp(-k / 12.0);
                        L[i + k] += t * (1 - pan); R[i + k] += t * pan;
                    }
                }
            }
            pl = pr = 0;
        }
    }

    // Wind: noise through a slowly wandering resonance, louder as the wind rises.
    class WindNoise : Ambient
    {
        float b1l, b2l, b1r, b2r, phase;
        public WindNoise() { Name = "wind"; }
        public override void Render(float[] L, float[] R, int n, float level)
        {
            float g = 0.22f * level * level;
            for (int i = 0; i < n; i++)
            {
                phase += 1f / Audio.Rate;
                double f = 260 + 180 * Math.Sin(phase * 0.37) + 120 * Math.Sin(phase * 1.13) + 400 * level;
                float q = 0.06f;
                float w = (float)(2 * Math.Sin(Math.PI * f / Audio.Rate));
                float xl = Noise(), xr = Noise();
                b1l += w * (xl - b1l - q * b2l); b2l += w * b1l;
                b1r += w * (xr - b1r - q * b2r); b2r += w * b1r;
                L[i] += b1l * g * 0.3f; R[i] += b1r * g * 0.3f;
            }
        }
    }

    // A few crickets, chirping at a rate set by temperature (Dolbear's law).
    class Crickets : Ambient
    {
        class C { public double Freq, Period, Phase, Pan, Carrier; }
        readonly List<C> list = new List<C>();
        public float Temperature = 22;
        public Crickets()
        {
            Name = "crickets";
            for (int i = 0; i < 4; i++) list.Add(new C { Freq = 4200 + rng.Next(700), Phase = rng.NextDouble(), Pan = rng.NextDouble(), Period = 0.55 + rng.NextDouble() * 0.3 });
        }
        public override void Render(float[] L, float[] R, int n, float level)
        {
            // Dolbear: chirps per minute ~ 7 * (T °C) - 30 for the snowy tree cricket (clamped).
            double cpm = Math.Max(40, Math.Min(200, 7 * Temperature - 30));
            foreach (var c in list)
            {
                double period = 60.0 / cpm * (0.85 + 0.3 * c.Period);
                for (int i = 0; i < n; i++)
                {
                    c.Phase += 1.0 / Audio.Rate / period;
                    if (c.Phase >= 1) c.Phase -= 1;
                    double t = c.Phase * period;                  // seconds into this chirp cycle
                    float env = 0;
                    if (t < 0.09)
                    {
                        double pulse = (t % 0.03) / 0.03;           // three pulses of ~20 ms
                        env = pulse < 0.66 ? (float)Math.Sin(Math.PI * pulse / 0.66) : 0;
                    }
                    c.Carrier += 2 * Math.PI * c.Freq / Audio.Rate;
                    float s = (float)Math.Sin(c.Carrier) * env * 0.05f * level;
                    L[i] += s * (float)(1 - c.Pan); R[i] += s * (float)c.Pan;
                }
            }
        }
    }

    // Soft crackle of a far-off fire: used on festival nights and cold evenings.
    class Hearth : Ambient
    {
        float lp;
        public Hearth() { Name = "hearth"; }
        public override void Render(float[] L, float[] R, int n, float level)
        {
            for (int i = 0; i < n; i++)
            {
                lp += (Noise() - lp) * 0.02f;
                float s = lp * 0.08f * level;
                if (rng.NextDouble() < 0.0006 * level) s += Noise() * 0.3f * level;
                L[i] += s; R[i] += s;
            }
        }
    }
}
