using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace Autumn
{
    // Offscreen checks used while building: nothing here touches the real desktop.
    static class Tests
    {
        // Runs a raw-surface paint and returns the result as a normal bitmap.
        public static Bitmap RawLayer(int w, int h, Action<Surface> paint)
        {
            IntPtr mem = System.Runtime.InteropServices.Marshal.AllocHGlobal(w * h * 4);
            try
            {
                unsafe { ulong* p = (ulong*)mem; for (long i = 0; i < (long)w * h / 2; i++) p[i] = 0; }
                paint(new Surface { Bits = mem, W = w, H = h, Stride = w * 4 });
                using (var view = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, mem))
                    return DeepCopy(view);
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(mem); }
        }

        // Clone() of a bitmap over foreign memory can share that memory; draw into a fresh one instead.
        public static Bitmap DeepCopy(Bitmap src)
        {
            var copy = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(copy)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(src, 0, 0); }
            return copy;
        }

        static Dictionary<string, string> Options(string[] args, int from)
        {
            var o = new Dictionary<string, string>();
            for (int i = from; i < args.Length; i++)
            {
                var kv = args[i].Split('=');
                o[kv[0].ToLowerInvariant()] = kv.Length > 1 ? kv[1] : "1";
            }
            return o;
        }

        static float F(Dictionary<string, string> o, string k, float d)
        {
            string v; float f;
            return o.TryGetValue(k, out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : d;
        }

        // Headless run against scripted windows, e.g.
        //   --sim DIR season=winter weather=snow hour=22 seconds=60 shots=20,40,60 script=shake,plow name=w
        public static void Sim(string[] args)
        {
            string dir = args[1];
            var o = Options(args, 2);
            string name; if (!o.TryGetValue("name", out name)) name = "sim";
            float seconds = F(o, "seconds", 40);
            var shots = new List<float>();
            string sv; if (o.TryGetValue("shots", out sv)) foreach (var p in sv.Split(',')) shots.Add(float.Parse(p, CultureInfo.InvariantCulture)); else shots.Add(seconds);
            string script; if (!o.TryGetValue("script", out script)) script = "";

            var fw = new FakeWindows();
            fw.Mons.Add(new Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 });
            var front = fw.Add(1100, 300, 1750, 820, false);
            var back = fw.Add(250, 420, 1300, 950, false);
            var taskbar = fw.Add(0, 1008, 1920, 1080, true);
            fw.Wins.Remove(taskbar); fw.Wins.Insert(0, taskbar);
            var world = new World(fw, 1.5f, (int)F(o, "seed", 42));
            Setup.AddSystems(world);
            string v;
            if (o.TryGetValue("season", out v)) world.Sky.SeasonOverride = Array.IndexOf(new[] { "spring", "summer", "autumn", "winter" }, v);
            if (o.TryGetValue("hour", out v)) world.Sky.HourOverride = F(o, "hour", 12);
            if (o.TryGetValue("weather", out v))
            {
                int k = Array.IndexOf(new[] { "clear", "breezy", "rain", "storm", "snow", "blizzard" }, v);
                world.Weather.Force(k);
                world.Sky.Update(0);
                if ((k == 4 || k == 5) && world.Sky.Temperature > -3) world.Sky.ColdOverride = -3 - world.Sky.Temperature;
            }

            var painter = new Painter();
            Leaf catchTarget = null; float catchX = 0, catchY = 0;
            float dt = 1f / 60f;
            int frames = (int)(seconds * 60);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double paintMs = 0; int paintN = 0;
            int shotIdx = 0;
            shots.Sort();
            for (int frame = 0; frame <= frames; frame++)
            {
                float t = frame * dt;
                // Scripted interactions near the end of the run.
                float end = seconds;
                if (script.Contains("drag") && t > end - 6 && t < end - 5.2f) fw.Move(front, 6, 0);
                if (script.Contains("shake") && t > end - 4 && t < end - 3) fw.Move(back, (frame / 4) % 2 == 0 ? 40 : -40, 0);
                bool cur = false; float cx = 0, cy = 0;
                if (script.Contains("sweep") && t > end - 2 && t < end - 1) { cur = true; cx = 200 + (t - (end - 2)) * 60 * 45; cy = 985; }
                if (script.Contains("plow") && t > end - 2 && t < end - 1) { cur = true; cx = 200 + (t - (end - 2)) * 60 * 25; cy = 1000; }
                if (script.Contains("still") && t > 2) { cur = true; cx = 700; cy = 600; }
                if (script.Contains("wipe") && t > end - 3 && t < end - 1) { cur = true; float u = (t - (end - 3)) / 2; cx = 30 + 330 * u; cy = 40 + 150 * (float)Math.Abs(Math.Sin(u * 9)); }
                if (script.Contains("circle") && t > end - 3 && t < end - 1.5f) { cur = true; float u = (t - (end - 3)) * 6.5f; cx = 700 + 110 * (float)Math.Cos(u); cy = 700 + 110 * (float)Math.Sin(u); }
                if (script.Contains("catch") && t > end - 2 && t < end - 1)
                {
                    if (catchTarget == null)
                        foreach (var l in world.Leaves) if (!l.Resting && l.Y > 100 && l.Y < 900) { catchTarget = l; break; }
                    bool pressed = t > end - 1.9f;
                    if (!pressed && catchTarget != null) { catchX = catchTarget.X; catchY = catchTarget.Y; }
                    cur = true; cx = catchX + (pressed ? (t - (end - 1.9f)) * 300 : 0); cy = catchY;
                    world.MouseDown = pressed;
                }
                else world.MouseDown = false;
                world.Step(dt, cur, cx, cy);
                if (frame == Math.Max(5, (int)(F(o, "at", 0) * 60)) && o.ContainsKey("event")) foreach (var ev in o["event"].Split('+')) world.Trigger(ev);
                if (shotIdx < shots.Count && t >= shots[shotIdx])
                {
                    shotIdx++;
                    int rest = 0, fall = 0;
                    foreach (var l in world.Leaves) if (l.Resting) rest++; else fall++;
                    var snow = world.Get<Snow>();
                    Console.WriteLine(string.Format("t={0,5:0.0}s {1} {2} {3:0.0}C sun={4:0.0} light={5},{6},{7} leaves rest={8} fall={9} flakes={10} wind={11:0}",
                        t, world.Sky.Season, world.Weather.Kind, world.Sky.Temperature, world.Sky.SunElev, world.Sky.Light.R, world.Sky.Light.G, world.Sky.Light.B,
                        rest, fall, snow != null ? snow.Flakes.Count : 0, world.Wind));
                    using (var bmp = new Bitmap(1920, 1080, PixelFormat.Format32bppPArgb))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.FromArgb(58, 66, 80));
                        for (int i = fw.Wins.Count - 1; i >= 0; i--)
                        {
                            var w = fw.Wins[i];
                            using (var br = new SolidBrush(w.IsTaskbar ? Color.FromArgb(28, 28, 32) : i == 1 ? Color.FromArgb(236, 236, 240) : Color.FromArgb(200, 204, 212)))
                                g.FillRectangle(br, w.Left, w.Top, w.Width, w.Bottom - w.Top);
                            using (var p = new Pen(Color.FromArgb(120, 120, 130)))
                                g.DrawRectangle(p, w.Left, w.Top, w.Width - 1, w.Bottom - w.Top - 1);
                        }
                        var t0 = sw.Elapsed.TotalMilliseconds;
                        using (var layer = RawLayer(1920, 1080, s => painter.Paint(s, world, new Rectangle(0, 0, 1920, 1080), new List<Rectangle>())))
                        {
                            paintMs += sw.Elapsed.TotalMilliseconds - t0; paintN++;
                            g.DrawImageUnscaled(layer, 0, 0);
                        }
                        if (cur) using (var p = new Pen(Color.Red, 2)) g.DrawEllipse(p, cx - 6, cy - 6, 12, 12);
                        bmp.Save(System.IO.Path.Combine(dir, name + "_" + ((int)t).ToString("000") + ".png"), ImageFormat.Png);
                    }
                }
            }
            Console.WriteLine(string.Format("{0}: {1} frames in {2:0} ms", name, frames, sw.Elapsed.TotalMilliseconds - paintMs));
            foreach (var e in world.Journal.Entries) Console.WriteLine("  journal: " + e.Value);
            foreach (var kv in world.Journal.Found) Console.WriteLine("  found: " + kv.Key);
        }

        // Every creature sprite, large, on a light and a dark strip.
        public static void Creatures(string path)
        {
            using (var bmp = new Bitmap(1200, 560, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(236, 234, 228));
                using (var dark = new SolidBrush(Color.FromArgb(52, 58, 72))) g.FillRectangle(dark, 0, 280, 1200, 280);
                using (var layer = RawLayer(1200, 560, s =>
                {
                    for (int sp = 0; sp < 3; sp++)
                        for (int pose = 0; pose < 6; pose++)
                        {
                            var p = BirdArt.Get(sp, pose, 60);
                            Blit.Draw(s, p, (int)(40 + pose * 95 - p.Cx), (int)(70 + sp * 80 - p.Cy), 256, Light.Full);
                        }
                    for (int sp = 0; sp < 4; sp++)
                        for (int f = 0; f < 4; f++)
                        {
                            var p = ButterflyArt.Get(sp, f, 70);
                            Blit.Draw(s, p, (int)(650 + f * 80 - p.Cx), (int)(60 + sp * 60 - p.Cy), 256, Light.Full);
                        }
                    for (int f = 0; f < 6; f++)
                    {
                        var p = BirdArt.Goose(f, 70);
                        Blit.Draw(s, p, (int)(60 + f * 100 - p.Cx), (int)(340 - p.Cy), 256, Light.Full);
                    }
                })) g.DrawImageUnscaled(layer, 0, 0);
                bmp.Save(path, ImageFormat.Png);
            }
            Console.WriteLine("wrote " + path);
        }

        public static void AlmanacShot(string path)
        {
            var fw = new FakeWindows();
            fw.Mons.Add(new Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 });
            var world = new World(fw, 1.5f, 3);
            Setup.AddSystems(world);
            var j = world.Journal;
            j.FirstRun = DateTime.Now.AddDays(-12);
            j.Count("leaves", 5234); j.Count("kicked", 377); j.Count("petals", 812); j.Count("snowflakes", 20391);
            j.Count("birds", 14); j.Count("butterflies", 9); j.Count("meteors", 6); j.Count("lightning", 11); j.Count("seconds", 3600 * 31.4);
            foreach (var id in new[] { "golden", "whirlwind", "shootingstar", "rainbow", "geese" }) j.Discover(world, id);
            j.Log(DateTime.Now.AddHours(-30), "First snow of the winter.");
            world.Step(0.016f, false, 0, 0);
            using (var a = new Almanac(world, delegate { }))
            {
                a.CreateControl();
                using (var bmp = new Bitmap(a.ClientSize.Width, a.ClientSize.Height))
                {
                    a.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    bmp.Save(path, ImageFormat.Png);
                }
            }
            Console.WriteLine("wrote " + path);
        }

        public static void Sheet(string path)
        {
            var rng = new Random(7);
            int cell = 150, cols = 6, rows = 7;
            using (var bmp = new Bitmap(cols * cell, rows * cell, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var m = new Matrix())
            {
                g.Clear(Color.FromArgb(238, 236, 230));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // Row 0: petals & blossoms; row 1: summer green; row 2: winter oak + golden; rows 3-6 autumn kinds.
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        LeafKind kind; int tone = 0; float size = 120;
                        if (r == 0) { kind = c < 3 ? LeafKind.Petal : LeafKind.Blossom; size = c < 3 ? 70 : 100; }
                        else if (r == 1) { kind = c % 2 == 0 ? LeafKind.Maple : LeafKind.Birch; tone = 1; }
                        else if (r == 2) { kind = c < 3 ? LeafKind.Oak : LeafKind.Maple; tone = c < 3 ? 2 : 3; }
                        else kind = (LeafKind)(r - 3);
                        var look = new LeafLook(kind, size, rng, tone);
                        LeafLook.Compose(m, c * cell + cell / 2, r * cell + cell / 2, c * 12 - 30, size, 0, 1, 1, 0);
                        g.Transform = m;
                        look.DrawLocal(g, false);
                        g.ResetTransform();
                        look.Dispose();
                    }
                bmp.Save(path, ImageFormat.Png);
            }
            Console.WriteLine("wrote " + path);
        }

        public static void Bench()
        {
            var rng = new Random(1);
            var looks = new List<LeafLook>();
            for (int i = 0; i < 40; i++) looks.Add(new LeafLook((LeafKind)rng.Next(4), 1.5f * (21 + (float)rng.NextDouble() * 14), rng));
            IntPtr mem = System.Runtime.InteropServices.Marshal.AllocHGlobal(1920 * 1080 * 4);
            var surf = new Surface { Bits = mem, W = 1920, H = 1080, Stride = 1920 * 4 };
            using (var m = new Matrix())
            {
                foreach (var l in looks) { l.Face(false); l.Face(true); }
                var night = Light.From(0.45f, 0.5f, 0.68f);
                for (int mode = 0; mode < 2; mode++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    int n = 0;
                    for (int f = 0; f < 60; f++)
                        foreach (var l in looks)
                        {
                            float x = 100 + (float)rng.NextDouble() * 1700, y = 100 + (float)rng.NextDouble() * 850;
                            var face = l.Face(false);
                            m.Reset(); m.Translate(x, y); m.Rotate(f * 3); m.Translate(-face.Cx, -face.Cy);
                            Blit.DrawAffine(surf, face, m.Elements, 256, mode == 0 ? Light.Full : night, false);
                            n++;
                        }
                    Console.WriteLine(string.Format("{0}: {1:0.0} us/leaf", mode == 0 ? "day  " : "night", sw.Elapsed.TotalMilliseconds * 1000 / n));
                }
            }
            System.Runtime.InteropServices.Marshal.FreeHGlobal(mem);
        }

        // Writes a crunch as .wav plus a waveform/spectrogram picture, since sound has to be checked by eye here.
        // Every synthesized sound as a .wav and a spectrogram picture.
        public static void Sounds(string dir)
        {
            var sy = new Synth();
            foreach (var n in new[] { "crunch", "snowcrunch", "thunder", "plop", "chime", "whoosh", "honk", "owl", "bird", "pop", "tinkle", "shimmer", "whistle" })
            {
                var all = new List<float>();
                for (int k = 0; k < (n == "bird" ? 4 : 2); k++) { all.AddRange(sy.Make(n, 0.8f)); all.AddRange(new float[44100 / 6]); }
                SoundPicture(all.ToArray(), dir, "snd_" + n);
            }
            foreach (var a in new Ambient[] { new RainNoise(), new WindNoise(), new Crickets() })
            {
                var mono = new float[44100 * 3];
                for (int off = 0; off < mono.Length; off += 1024)
                {
                    int n = Math.Min(1024, mono.Length - off);
                    var l = new float[n]; var r = new float[n];
                    a.Render(l, r, n, 0.9f);
                    Array.Copy(l, 0, mono, off, n);
                }
                SoundPicture(mono, dir, "amb_" + a.Name);
            }
        }

        public static void SoundPicture(float[] s, string dir, string name)
        {
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".wav"), Synth.Wav(s));
            int N = 512, hop = 128, frames = Math.Max(1, (s.Length - N) / hop);
            int H = 256, Wd = frames;
            using (var bmp = new Bitmap(Wd, H + 120))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                for (int x = 0; x < Wd; x++)
                {
                    float mn = 0, mx = 0;
                    for (int i = x * hop; i < Math.Min(s.Length, x * hop + hop); i++) { mn = Math.Min(mn, s[i]); mx = Math.Max(mx, s[i]); }
                    g.DrawLine(Pens.LightGreen, x, 60 - mx * 55, x, 60 - mn * 55);
                }
                var re = new double[N]; var im = new double[N];
                for (int f = 0; f < frames; f++)
                {
                    for (int i = 0; i < N; i++) { int j = f * hop + i; re[i] = (j < s.Length ? s[j] : 0) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N)); im[i] = 0; }
                    Fft(re, im);
                    for (int k = 0; k < N / 2; k++)
                    {
                        double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                        double db = 20 * Math.Log10(mag + 1e-9);
                        int v = (int)Math.Max(0, Math.Min(255, (db + 70) * 3.2));
                        bmp.SetPixel(f, 120 + H - 1 - k, Color.FromArgb(v, (int)(v * 0.8), v / 3));
                    }
                }
                bmp.Save(System.IO.Path.Combine(dir, name + ".png"), ImageFormat.Png);
            }
            float peak = 0; double rms = 0; foreach (var x in s) { peak = Math.Max(peak, Math.Abs(x)); rms += x * x; }
            Console.WriteLine(string.Format("{0}: samples={1} peak={2:0.000} rms={3:0.0000}", name, s.Length, peak, Math.Sqrt(rms / Math.Max(1, s.Length))));
        }

        static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                for (int i = 0; i < n; i += len)
                    for (int k = 0; k < len / 2; k++)
                    {
                        double wr = Math.Cos(ang * k), wi = Math.Sin(ang * k);
                        double ur = re[i + k], ui = im[i + k];
                        double vr = re[i + k + len / 2] * wr - im[i + k + len / 2] * wi, vi = re[i + k + len / 2] * wi + im[i + k + len / 2] * wr;
                        re[i + k] = ur + vr; im[i + k] = ui + vi; re[i + k + len / 2] = ur - vr; im[i + k + len / 2] = ui - vi;
                    }
            }
        }
    }

    // One place that decides which systems make up the world, shared by the app and the tests.
    static class Setup
    {
        public static void AddSystems(World w)
        {
            w.Systems.Add(new Snow());
            w.Systems.Add(new Rain());
            w.Systems.Add(new Sparks());
            w.Systems.Add(new Soundscape());
            w.Systems.Add(new Fireflies());
            w.Systems.Add(new Meteors());
            w.Systems.Add(new Aurora());
            w.Systems.Add(new Birds());
            w.Systems.Add(new Geese());
            w.Systems.Add(new Butterflies());
            w.Systems.Add(new CursorFx());
            w.Systems.Add(new Events());
            w.Systems.Add(new Festivals());
            w.Systems.Add(new Icicles());
            w.Systems.Add(new Frost());
            w.Systems.Add(new Moments());
            w.Systems.Sort((a, b) => a.Layer.CompareTo(b.Layer));
        }
    }
}
