using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Autumn
{
    class Flake
    {
        public float X, Y, Vx, Vy, Phase, Freq, Amp, Fall, Angle, Spin, Prev;
        public int Kind;          // 0-3 dots by size, 4 crystal, 5 clump
        public float Scale = 1, Life = 1;
        public bool Puff;         // kicked-up powder: falls with gravity, never lands
    }

    // Snowfall that accumulates on the visible stretches of window ledges as a smooth snowpack,
    // rides along with gentle drags, slides off when shaken, can be plowed by the pointer, and melts.
    class Snow : Sys
    {
        public readonly List<Flake> Flakes = new List<Flake>();
        readonly Dictionary<WinInfo, float[]> depth = new Dictionary<WinInfo, float[]>();
        Pixels[] dots;
        Pixels[] crystals;
        Pixels clump;
        float spawnAcc, bw;
        public float CrunchEnergy;
        int seasonYearLogged = -1;

        public Snow() { Layer = 20; }

        void Init(World w)
        {
            if (dots != null) return;
            float s = w.S;
            bw = 2 * s;
            var white = Color.FromArgb(255, 250, 252, 255);
            dots = new[] { Pixels.Dot(0.9f * s, white, 0.2f), Pixels.Dot(1.3f * s, white, 0.35f), Pixels.Dot(1.8f * s, white, 0.4f), Pixels.Dot(2.5f * s, white, 0.35f) };
            crystals = new Pixels[3];
            for (int i = 0; i < 3; i++) crystals[i] = Crystal((5.5f + i * 1.8f) * s, i);
            clump = Pixels.Dot(3.4f * s, Color.FromArgb(255, 246, 249, 255), 0.55f);
        }

        // A six-armed dendrite drawn once per variant.
        static Pixels Crystal(float r, int variant)
        {
            int half = (int)Math.Ceiling(r) + 2;
            using (var b = new Bitmap(half * 2, half * 2, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(b))
                using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(1f, r * 0.13f)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    for (int k = 0; k < 6; k++)
                    {
                        double a = k * Math.PI / 3;
                        float ex = half + (float)Math.Cos(a) * r, ey = half + (float)Math.Sin(a) * r;
                        g.DrawLine(pen, half, half, ex, ey);
                        foreach (float t in new[] { 0.45f, 0.72f })
                        {
                            float bx = half + (float)Math.Cos(a) * r * t, by = half + (float)Math.Sin(a) * r * t;
                            float bl = r * (variant == 1 ? 0.34f : 0.26f) * (1.1f - t);
                            foreach (double side in new[] { -1.0, 1.0 })
                            {
                                double ba = a + side * Math.PI / 3;
                                g.DrawLine(pen, bx, by, bx + (float)Math.Cos(ba) * bl, by + (float)Math.Sin(ba) * bl);
                            }
                        }
                    }
                }
                return Pixels.From(b, half, half);
            }
        }

        public float DepthAt(WinInfo host, float x)
        {
            float[] d;
            if (!depth.TryGetValue(host, out d) || bw <= 0) return 0;
            int i = (int)((x - host.Left) / bw);
            return i >= 0 && i < d.Length ? d[i] : 0;
        }

        float[] Profile(WinInfo host)
        {
            float[] d;
            int n = Math.Max(1, (int)Math.Ceiling(host.Width / bw) + 1);
            if (!depth.TryGetValue(host, out d)) { d = new float[n]; depth[host] = d; }
            else if (d.Length != n)
            {
                var nd = new float[n];
                Array.Copy(d, nd, Math.Min(n, d.Length));
                d = nd; depth[host] = d;
            }
            return d;
        }

        public float MaxDepth(World w, WinInfo host) { return (host.IsTaskbar || host.IsGround ? 20 : 15) * w.S; }

        public override void Step(World w, float dt)
        {
            Init(w);
            float S = w.S;
            bool snowing = w.Weather.Snowy(w.Sky) && w.Weather.Precip > 0.03f && !w.Stopping;
            float intensity = snowing ? w.Weather.Precip : 0;
            if (snowing && w.Sky.Season == Season.Winter)
            {
                int yr = w.Sky.Local.Month >= 7 ? w.Sky.Local.Year : w.Sky.Local.Year - 1;
                if (seasonYearLogged < 0) { string v; if (w.Journal.Extra.TryGetValue("firstSnow", out v)) int.TryParse(v, out seasonYearLogged); }
                if (seasonYearLogged != yr && !w.Sky.Lapsing && w.Weather.Override < 0)
                {
                    seasonYearLogged = yr;
                    w.Journal.Extra["firstSnow"] = yr.ToString();
                    w.Journal.Log(DateTime.Now, "First snow of the winter.");
                }
            }

            // Spawning: denser and more sideways in a blizzard; flakes enter upwind too.
            bool blizzard = w.Weather.Kind == WeatherKind.Blizzard;
            float target = intensity * (blizzard ? 650 : 360) * Math.Min(1.4f, w.Density);
            int alive = 0;
            foreach (var f in Flakes) if (!f.Puff && f.Kind != 5) alive++;
            if (alive < target)
            {
                float travel = w.Virt.Height / (60 * S);
                spawnAcc += target / travel * dt;
                while (spawnAcc >= 1)
                {
                    spawnAcc -= 1;
                    var f = NewFlake(w);
                    if (Math.Abs(w.Wind) > 90 * S && w.Chance(0.45f))
                    {
                        f.X = w.Wind > 0 ? w.Virt.Left - 10 * S : w.Virt.Right + 10 * S;
                        f.Y = w.Virt.Top + w.Rand(0, 0.9f) * w.Virt.Height;
                    }
                    else
                    {
                        f.X = w.Rand(w.Virt.Left - 0.1f * w.Virt.Width, w.Virt.Right + 0.1f * w.Virt.Width) - w.Wind * travel * 0.5f;
                        f.Y = w.Virt.Top - w.Rand(4, 30) * S;
                    }
                    f.Prev = f.Y;
                    Flakes.Add(f);
                    w.Journal.Count("snowflakes", 1);
                }
            }

            StepFlakes(w, dt);
            StepPack(w, dt);
            Plow(w, dt);
            if (CrunchEnergy > 0.05f && w.Sound != null) { w.Sound.Play("snowcrunch", Math.Min(1f, CrunchEnergy), 0); }
            CrunchEnergy = 0;
        }

        Flake NewFlake(World w)
        {
            var f = new Flake();
            double k = w.Rng.NextDouble();
            f.Kind = k < 0.3 ? 0 : k < 0.62 ? 1 : k < 0.86 ? 2 : k < 0.97 ? 3 : 4;
            float S = w.S;
            f.Fall = S * (f.Kind == 4 ? w.Rand(32, 48) : 38 + f.Kind * 9 + w.Rand(-6, 10));
            f.Freq = w.Rand(0.25f, 0.8f);
            f.Amp = S * w.Rand(8, 28);
            f.Phase = w.Rand(0, 6.28f);
            f.Vx = w.Wind;
            f.Angle = w.Rand(0, 360);
            f.Spin = w.Rand(-40, 40);
            if (f.Kind == 4) f.Scale = 1;
            return f;
        }

        void StepFlakes(World w, float dt)
        {
            float S = w.S;
            for (int i = Flakes.Count - 1; i >= 0; i--)
            {
                var f = Flakes[i];
                f.Prev = f.Y;
                if (f.Puff || f.Kind == 5)
                {
                    // Powder and clumps: ballistic with a little drag.
                    f.Vy += (f.Kind == 5 ? 900 : 520) * S * dt;
                    float drag = (float)Math.Exp(-dt * (f.Kind == 5 ? 0.6f : 2.2f));
                    f.Vx = f.Vx * drag + w.Wind * (1 - drag) * 0.5f;
                    f.X += f.Vx * dt; f.Y += f.Vy * dt;
                    f.Life -= dt * (f.Puff ? 0.9f : 0);
                    if (f.Kind == 5 && f.Vy > 0)
                    {
                        Ledge hit; float top;
                        if (w.HitLedge(f.X, f.Prev, f.Y, true, out hit, out top)) { Deposit(w, hit.Host, f.X, 5.5f * S * f.Scale, hit); Burst(w, f.X, top, 5, f.Vx * 0.2f); Flakes.RemoveAt(i); continue; }
                    }
                }
                else
                {
                    f.Phase += 6.283f * f.Freq * dt;
                    float resp = 1f - (float)Math.Exp(-dt * (f.Kind == 4 ? 0.9f : 1.6f));
                    f.Vx += (w.Wind * 0.95f - f.Vx) * resp;
                    f.X += (f.Vx + f.Amp * (float)Math.Sin(f.Phase)) * dt;
                    f.Y += f.Fall * dt;
                    f.Angle += f.Spin * dt;
                    Ledge hit; float top;
                    if (w.HitLedge(f.X, f.Prev, f.Y, true, out hit, out top))
                    {
                        Deposit(w, hit.Host, f.X, (0.35f + 0.25f * f.Kind) * S, hit);
                        Flakes.RemoveAt(i);
                        continue;
                    }
                }
                if (f.Life <= 0 || f.Y > w.Virt.Bottom + 20 * S || f.X < w.Virt.Left - 200 * S || f.X > w.Virt.Right + 200 * S) Flakes.RemoveAt(i);
            }
        }

        // Add snow volume (area in px^2 / bucket width) at x, spread a little so it settles smoothly.
        public void Deposit(World w, WinInfo host, float x, float amount, Ledge visible)
        {
            if (host == null || !host.CanHold) return;
            var d = Profile(host);
            float max = MaxDepth(w, host);
            int c = (int)((x - host.Left) / bw);
            for (int k = -2; k <= 2; k++)
            {
                int i = c + k;
                if (i < 0 || i >= d.Length) continue;
                float wgt = k == 0 ? 0.4f : Math.Abs(k) == 1 ? 0.2f : 0.1f;
                d[i] = Math.Min(max, d[i] + amount * wgt);
            }
        }

        void StepPack(World w, float dt)
        {
            float S = w.S;
            float temp = w.Sky.Temperature;
            float melt = S * (0.05f * Math.Max(0, temp - 0.5f) + (w.Weather.Precip > 0.05f && !w.Weather.Snowy(w.Sky) ? 0.5f * w.Weather.Precip : 0) + (w.Sky.IsDay && temp > -2 ? 0.015f : 0));
            if (w.Stopping) melt += 6 * S;

            // The flakes on screen stand in for millions more: every visible stretch of ledge gathers
            // a steady blanket, heavier toward the downwind end in strong wind.
            bool snowing = w.Weather.Snowy(w.Sky) && w.Weather.Precip > 0.03f && !w.Stopping;
            if (snowing)
            {
                float rate = S * w.Weather.Precip * (w.Weather.Kind == WeatherKind.Blizzard ? 0.3f : 0.2f) * (w.Sky.Lapsing ? 6 : 1);
                float drift = Math.Abs(w.Wind) > 60 * S ? Math.Max(-1, Math.Min(1, w.Wind / (200 * S))) : 0;
                foreach (var e in w.LedgeList)
                {
                    if (!e.Host.CanHold) continue;
                    var d = Profile(e.Host);
                    float max = MaxDepth(w, e.Host);
                    int i0 = Math.Max(0, (int)((e.X0 - e.Host.Left) / bw)), i1 = Math.Min(d.Length - 1, (int)((e.X1 - e.Host.Left) / bw));
                    for (int i = i0; i <= i1; i++)
                    {
                        float u = d.Length > 1 ? i / (float)(d.Length - 1) : 0.5f;
                        float f = 1 + 0.8f * drift * (u - 0.5f) * 2;
                        d[i] = Math.Min(max, d[i] + rate * f * dt);
                    }
                }
            }
            var drop = new List<WinInfo>();
            foreach (var kv in depth)
            {
                var host = kv.Key; var d = kv.Value;
                bool any = false;
                if (!w.Alive(host) || !host.CanHold) { Shed(w, host, d, 0.3f, true); drop.Add(host); continue; }
                float a = (float)Math.Sqrt(host.Ax * host.Ax + host.Ay * host.Ay);
                if (!host.IsGround && (a > 14000 * S || host.Vy < -1250 * S)) Shed(w, host, d, Math.Min(1f, a / (40000 * S) + 0.35f), false);
                // Gusts lift powder off the top.
                if (Math.Abs(w.Gust) > 200 * S && w.Chance(dt * 3))
                {
                    int i = w.Rng.Next(d.Length);
                    if (d[i] > 2 * S) { d[i] -= 1.2f * S; Burst(w, host.Left + i * bw, host.Top - d[i], 2, w.Gust * 0.6f); }
                }
                // Soften bumps (snow creeps), settle toward a gentle angle of repose, and melt.
                for (int pass = 0; pass < 2; pass++)
                {
                    float prev = d[0];
                    for (int i = 1; i + 1 < d.Length; i++)
                    {
                        float cur = d[i];
                        d[i] = cur + 0.08f * (prev - 2 * cur + d[i + 1]);
                        prev = cur;
                    }
                }
                float slope = 1.1f * bw;
                for (int i = 0; i + 1 < d.Length; i++)
                {
                    float diff = d[i] - d[i + 1];
                    if (Math.Abs(diff) > slope)
                    {
                        float mv = (Math.Abs(diff) - slope) * 0.25f * Math.Sign(diff);
                        d[i] -= mv; d[i + 1] += mv;
                    }
                }
                for (int i = 0; i < d.Length; i++)
                {
                    if (melt > 0) d[i] = Math.Max(0, d[i] - melt * dt);
                    if (d[i] > 0.05f) any = true;
                }
                if (!any) drop.Add(host);
            }
            foreach (var h in drop) depth.Remove(h);
        }

        // Turn some or all of a ledge's snow into falling clumps.
        void Shed(World w, WinInfo host, float[] d, float fraction, bool all)
        {
            float S = w.S;
            float acc = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float take = all ? d[i] : d[i] * fraction;
                d[i] -= take;
                acc += take * bw;
                if (acc > 30 * S * S)
                {
                    acc = 0;
                    var f = new Flake { Kind = 5, X = host.Left + i * bw, Y = host.Top - 3 * S, Scale = w.Rand(0.7f, 1.3f) };
                    f.Vx = host.Vx * w.Rand(0.5f, 0.9f) + w.Rand(-40, 40) * S;
                    f.Vy = Math.Min(0, host.Vy) * w.Rand(0.4f, 0.8f) - w.Rand(0, 80) * S;
                    f.Prev = f.Y;
                    Flakes.Add(f);
                }
            }
            CrunchEnergy += 0.3f;
        }

        public void Burst(World w, float x, float y, int n, float vx)
        {
            for (int k = 0; k < n; k++)
            {
                var f = new Flake { Puff = true, Kind = w.Rng.Next(3), X = x + w.Rand(-4, 4) * w.S, Y = y, Life = w.Rand(0.6f, 1.1f) };
                f.Vx = vx + w.Rand(-90, 90) * w.S; f.Vy = -w.Rand(60, 220) * w.S; f.Prev = f.Y;
                Flakes.Add(f);
            }
        }

        // The pointer plows through snowpack, throwing powder the way it's moving.
        void Plow(World w, float dt)
        {
            if (!w.HaveCursor || w.CurSpeed < 60 * w.S) return;
            float S = w.S, R = 16 * S;
            foreach (var kv in depth)
            {
                var host = kv.Key; var d = kv.Value;
                float hMax = 0;
                foreach (var v in d) hMax = Math.Max(hMax, v);
                if (w.CurY < host.Top - hMax - 8 * S || w.CurY > host.Top + 6 * S) continue;
                if (w.CurX < host.Left - R || w.CurX > host.Right + R) continue;
                int c = (int)((w.CurX - host.Left) / bw), r = (int)(R / bw) + 1;
                float removed = 0;
                for (int i = Math.Max(0, c - r); i <= Math.Min(d.Length - 1, c + r); i++)
                {
                    float top = host.Top - d[i];
                    if (w.CurY < top - 6 * S) continue;
                    float f = 1 - Math.Abs(i - c) / (float)(r + 1);
                    float take = Math.Min(d[i], d[i] * 0.8f * f + 0.5f * S);
                    d[i] -= take; removed += take;
                }
                if (removed > 0.5f * S)
                {
                    int n = Math.Min(14, (int)(removed / (1.5f * S)) + 1);
                    for (int k = 0; k < n; k++)
                    {
                        var p = new Flake { Puff = true, Kind = w.Rng.Next(4), X = w.CurX + w.Rand(-R, R), Y = host.Top - w.Rand(0, 8) * S, Life = w.Rand(0.5f, 1.2f) };
                        p.Vx = w.CurVx * w.Rand(0.2f, 0.55f) + w.Rand(-60, 60) * S;
                        p.Vy = -Math.Abs(w.CurVy) * 0.15f - w.Rand(80, 300) * S;
                        p.Prev = p.Y;
                        Flakes.Add(p);
                    }
                    CrunchEnergy += Math.Min(1f, removed / (20 * S));
                    w.Journal.Count("snowPlowed", removed / S);
                }
            }
        }

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            if (dots == null) return;
            PaintPack(s, w, area, drawn);
            var light = w.Sky.Light;
            // Snow stays bright at night (moonlit) rather than going as dark as leaves.
            var snowLight = Light.From(0.55f + 0.45f * light.R / 256f, 0.6f + 0.4f * light.G / 256f, 0.7f + 0.3f * light.B / 256f);
            foreach (var f in Flakes)
            {
                if (f.X < area.Left - 20 || f.X > area.Right + 20 || f.Y < area.Top - 20 || f.Y > area.Bottom + 20) continue;
                Pixels p = f.Kind == 5 ? clump : f.Kind == 4 ? crystals[(int)(Math.Abs(f.Phase * 10)) % 3] : dots[f.Kind];
                uint a = f.Puff ? (uint)(Math.Max(0, Math.Min(1, f.Life)) * 256) : 256;
                Rectangle r;
                if (f.Kind == 4)
                {
                    using (var m = new Matrix())
                    {
                        m.Translate(f.X - area.Left, f.Y - area.Top); m.Rotate(f.Angle); m.Translate(-p.Cx, -p.Cy);
                        r = Blit.DrawAffine(s, p, m.Elements, a, snowLight, false);
                    }
                }
                else r = Blit.DrawAt(s, p, f.X - area.Left, f.Y - area.Top, f.Kind == 5 ? f.Scale : 1, a, snowLight, false);
                if (!r.IsEmpty) drawn.Add(r);
            }
        }

        void PaintPack(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            float S = w.S;
            var light = w.Sky.Light;
            float lr = 0.62f + 0.38f * light.R / 256f, lg = 0.66f + 0.34f * light.G / 256f, lb = 0.76f + 0.24f * light.B / 256f;
            Color body = Color.FromArgb(255, (int)(243 * lr), (int)(247 * lg), (int)(255 * lb));
            Color crest = Color.FromArgb(255, (int)(255 * lr), (int)(255 * lg), (int)(255 * lb));
            Color shade = Color.FromArgb(255, (int)(205 * lr), (int)(218 * lg), (int)(238 * lb));
            foreach (var e in w.LedgeList)
            {
                float[] d;
                if (!depth.TryGetValue(e.Host, out d)) continue;
                var host = e.Host;
                int sink = (int)Math.Max(1, S);
                int x0 = (int)Math.Max(e.X0, area.Left), x1 = (int)Math.Min(e.X1, area.Right);
                int minY = int.MaxValue;
                for (int x = x0; x < x1; x++)
                {
                    float u = (x - host.Left) / bw;
                    int i = (int)u;
                    if (i < 0 || i >= d.Length) continue;
                    float h = i + 1 < d.Length ? d[i] + (d[i + 1] - d[i]) * (u - i) : d[i];
                    if (h < 0.3f) continue;
                    float top = host.Top - h;
                    int yTop = (int)Math.Ceiling(top), yBase = host.Top + sink;
                    int lx = x - area.Left;
                    // Anti-aliased crest pixel, bright crest line, body, then a shaded base.
                    float frac = yTop - top;
                    if (frac > 0.02f) Blit.Pixel(s, lx, yTop - 1 - area.Top, Pixels.Premul(crest, frac), false);
                    int crestEnd = Math.Min(yBase, yTop + (int)Math.Max(1, S));
                    Blit.Span(s, lx, yTop - area.Top, crestEnd - area.Top, Pixels.Premul(crest, 1f), false);
                    int shadeStart = h > 3 * S ? Math.Max(crestEnd, yBase - (int)(1.5f * S)) : yBase;
                    Blit.Span(s, lx, crestEnd - area.Top, shadeStart - area.Top, Pixels.Premul(body, 0.97f), false);
                    Blit.Span(s, lx, shadeStart - area.Top, yBase - area.Top, Pixels.Premul(shade, 0.95f), false);
                    // Moonlit glints.
                    if (w.Sky.Darkness > 0.5f && ((x * 7919 + (int)(w.Time * 3) * 104729) % 97) == 0)
                        Blit.Pixel(s, lx, yTop + 1 - area.Top, Pixels.Premul(Color.White, 1f), true);
                    minY = Math.Min(minY, yTop - 1);
                }
                if (minY != int.MaxValue) drawn.Add(Rectangle.FromLTRB(x0 - area.Left, minY - area.Top, x1 - area.Left, host.Top + sink - area.Top));
            }
        }

        public override bool Quiet { get { return Flakes.Count == 0 && depth.Count == 0; } }

        // ---------------------------------------------------------------- persistence of the taskbar's snow

        public string Save(World w)
        {
            foreach (var kv in depth)
                if (kv.Key.IsTaskbar)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < kv.Value.Length; i += 2) sb.Append(((int)(kv.Value[i] / w.S * 10)).ToString()).Append(',');
                    return sb.ToString();
                }
            return "";
        }

        public void Load(World w, WinInfo taskbar, string data)
        {
            if (taskbar == null || string.IsNullOrEmpty(data)) return;
            Init(w);
            var d = Profile(taskbar);
            var parts = data.Split(',');
            for (int k = 0; k < parts.Length; k++)
            {
                int v;
                if (!int.TryParse(parts[k], out v)) continue;
                for (int j = 0; j < 2; j++) if (k * 2 + j < d.Length) d[k * 2 + j] = v / 10f * w.S;
            }
        }
    }
}
