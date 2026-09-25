using System;
using System.Collections.Generic;
using System.Drawing;

namespace Autumn
{
    // Rain streaks that splash on ledges, wet ledges that keep dripping from their corners after the
    // shower passes, and thunderstorms: gentle flashes, a forked bolt, thunder that arrives late.
    class Rain : Sys
    {
        class Drop { public float X, Y, Vx, Vy, Prev; }
        class Bit { public float X, Y, Vx, Vy, Life; }
        class Drip { public WinInfo Host; public bool Right; public float Size, X, Y, Vy, Prev; public bool Falling; }

        readonly List<Drop> drops = new List<Drop>();
        readonly List<Bit> bits = new List<Bit>();
        readonly List<Drip> drips = new List<Drip>();
        readonly Dictionary<WinInfo, float> wet = new Dictionary<WinInfo, float>();
        float spawnAcc;
        // lightning
        float nextStrike = 8, flashT = -1, boltLife;
        List<PointF[]> bolt = new List<PointF[]>();
        readonly List<float[]> thunder = new List<float[]>();   // [time left, gain, pan]
        int strikes;
        Pixels dripDot;
        public float Flash;       // current flash strength (0..1), read by other systems

        public Rain() { Layer = 30; }

        public override void Step(World w, float dt)
        {
            float S = w.S;
            bool raining = !w.Weather.Snowy(w.Sky) && w.Weather.Precip > 0.03f && !w.Stopping;
            float intensity = raining ? w.Weather.Precip : 0;
            bool storm = w.Weather.Storminess > 0.4f;
            if (w.Sound != null) w.Sound.Ambience("rain", intensity);

            float target = intensity * (storm ? 420 : 260) * Math.Min(1.5f, w.Density);
            if (drops.Count < target)
            {
                float speed = 900 * S;
                float travel = w.Virt.Height / speed;
                spawnAcc += target / travel * dt;
                while (spawnAcc >= 1)
                {
                    spawnAcc -= 1;
                    var d = new Drop();
                    d.Vy = speed * w.Rand(0.85f, 1.15f);
                    d.Vx = w.Wind * 1.15f;
                    d.X = w.Rand(w.Virt.Left - 0.1f * w.Virt.Width, w.Virt.Right + 0.1f * w.Virt.Width) - d.Vx * travel * 0.5f;
                    d.Y = w.Virt.Top - w.Rand(0, 60) * S;
                    d.Prev = d.Y;
                    drops.Add(d);
                }
            }

            for (int i = drops.Count - 1; i >= 0; i--)
            {
                var d = drops[i];
                d.Prev = d.Y;
                d.Vx += (w.Wind * 1.15f - d.Vx) * Math.Min(1, dt * 3);
                d.X += d.Vx * dt; d.Y += d.Vy * dt;
                Ledge hit; float top;
                if (w.HitLedge(d.X, d.Prev, d.Y, true, out hit, out top))
                {
                    int n = w.Chance(0.5f) ? 2 : 1;
                    for (int k = 0; k < n; k++)
                        bits.Add(new Bit { X = d.X, Y = top - 1, Vx = w.Rand(-150, 150) * S + d.Vx * 0.2f, Vy = -w.Rand(70, 230) * S, Life = w.Rand(0.18f, 0.32f) });
                    float v; wet.TryGetValue(hit.Host, out v);
                    wet[hit.Host] = Math.Min(1, v + 0.015f);
                    drops.RemoveAt(i);
                    continue;
                }
                if (d.Y > w.Virt.Bottom + 10 || d.X < w.Virt.Left - 300 * S || d.X > w.Virt.Right + 300 * S) drops.RemoveAt(i);
            }

            for (int i = bits.Count - 1; i >= 0; i--)
            {
                var b = bits[i];
                b.Life -= dt;
                b.Vy += 1700 * S * dt;
                b.X += b.Vx * dt; b.Y += b.Vy * dt;
                if (b.Life <= 0) bits.RemoveAt(i);
            }

            StepDrips(w, dt, raining);
            StepLightning(w, dt, storm && raining);
        }

        void StepDrips(World w, float dt, bool raining)
        {
            float S = w.S;
            var keys = new List<WinInfo>(wet.Keys);
            foreach (var h in keys)
            {
                float v = wet[h];
                v -= dt * (raining ? 0.004f : 0.012f);
                if (v <= 0 || !w.Alive(h) || h.IsGround || h.IsTaskbar) { wet.Remove(h); continue; }
                wet[h] = v;
                // A drop gathers at each visible end of the ledge.
                foreach (bool right in new[] { false, true })
                {
                    float ex = right ? h.Right - 1 : h.Left + 1;
                    bool visible = false;
                    foreach (var e in w.LedgeList) if (e.Host == h && ex >= e.X0 - 1 && ex <= e.X1 + 1) visible = true;
                    if (!visible) continue;
                    Drip hanging = null;
                    foreach (var dr in drips) if (dr.Host == h && dr.Right == right && !dr.Falling) hanging = dr;
                    if (hanging == null) { drips.Add(new Drip { Host = h, Right = right, Size = 0.2f }); continue; }
                    hanging.Size += dt * v * w.Rand(0.4f, 1.2f);
                    if (hanging.Size >= 1)
                    {
                        hanging.Falling = true;
                        hanging.X = right ? h.Right + 1.5f * S : h.Left - 1.5f * S;
                        hanging.Y = hanging.Prev = h.Top + 3 * S;
                        hanging.Vy = 0;
                    }
                }
            }
            for (int i = drips.Count - 1; i >= 0; i--)
            {
                var dr = drips[i];
                if (!dr.Falling)
                {
                    if (!wet.ContainsKey(dr.Host) || !w.Alive(dr.Host)) { drips.RemoveAt(i); continue; }
                    dr.X = dr.Right ? dr.Host.Right + 1.5f * S : dr.Host.Left - 1.5f * S;
                    dr.Y = dr.Host.Top + 2 * S + dr.Size * 2 * S;
                    continue;
                }
                dr.Prev = dr.Y;
                dr.Vy += 1500 * S * dt;
                dr.Y += dr.Vy * dt;
                Ledge hit; float top;
                if (w.HitLedge(dr.X, dr.Prev, dr.Y, true, out hit, out top))
                {
                    for (int k = 0; k < 3; k++) bits.Add(new Bit { X = dr.X, Y = top - 1, Vx = w.Rand(-110, 110) * S, Vy = -w.Rand(90, 200) * S, Life = w.Rand(0.2f, 0.35f) });
                    if (w.Sound != null && w.Chance(0.7f)) w.Sound.Play("plop", 0.35f, Pan(w, dr.X));
                    drips.RemoveAt(i);
                    continue;
                }
                if (dr.Y > w.Virt.Bottom + 10) drips.RemoveAt(i);
            }
        }

        static float Pan(World w, float x) { return Math.Max(-1, Math.Min(1, (x - w.Virt.Left) / Math.Max(1, w.Virt.Width) * 2 - 1)); }

        // ---------------------------------------------------------------- lightning

        void StepLightning(World w, float dt, bool active)
        {
            for (int i = thunder.Count - 1; i >= 0; i--)
            {
                thunder[i][0] -= dt;
                if (thunder[i][0] <= 0) { if (w.Sound != null) w.Sound.Play("thunder", thunder[i][1], thunder[i][2]); thunder.RemoveAt(i); }
            }
            if (flashT >= 0)
            {
                flashT += dt;
                // Two soft pulses and a fade; never more than a gentle brightening.
                float t = flashT, f = 0;
                if (t < 0.06f) f = 0.09f;
                else if (t < 0.12f) f = 0.02f;
                else if (t < 0.2f) f = 0.12f;
                else if (t < 0.55f) f = 0.12f * (1 - (t - 0.2f) / 0.35f);
                else flashT = -1;
                Flash = f;
            }
            else Flash = 0;
            if (boltLife > 0) boltLife -= dt;

            if (!active) { nextStrike = Math.Max(nextStrike, 4); return; }
            nextStrike -= dt * Math.Max(0.3f, w.Weather.Storminess);
            if (nextStrike <= 0) Strike(w);
        }

        public void Strike(World w)
        {
            float S = w.S;
            nextStrike = w.Rand(7, 22);
            flashT = 0;
            strikes++;
            w.Journal.Count("lightning", 1);
            if (strikes >= 3) w.Journal.Discover(w, "storm");
            float dist = w.Rand(0.15f, 1f);                   // 0 near .. 1 far
            thunder.Add(new[] { 0.4f + dist * 3.6f, 1.1f - dist * 0.6f, w.Rand(-0.7f, 0.7f) });
            if (dist > 0.55f) { boltLife = 0; bolt.Clear(); return; }  // far away: flash only

            // Aim at a ledge half of the time: the strike scatters what's lying there.
            float x0 = w.Rand(w.Virt.Left + 0.1f * w.Virt.Width, w.Virt.Right - 0.1f * w.Virt.Width), y0 = w.Virt.Top - 5;
            float x1, y1;
            Ledge target = new Ledge(); bool onLedge = false;
            if (w.LedgeList.Count > 0 && w.Chance(0.5f))
            {
                target = w.LedgeList[w.Rng.Next(w.LedgeList.Count)];
                x1 = w.Rand(target.X0, target.X1); y1 = target.Y - w.HeightAt(target.Host, x1); onLedge = true;
            }
            else { x1 = x0 + w.Rand(-0.25f, 0.25f) * w.Virt.Width; y1 = w.Virt.Top + w.Rand(0.35f, 0.7f) * w.Virt.Height; }
            bolt.Clear();
            var main = Fork(w, x0, y0, x1, y1, 0.32f);
            bolt.Add(main);
            for (int b = 0; b < 2; b++)
            {
                var p = main[w.Rng.Next(main.Length / 5, main.Length * 3 / 5)];
                float len = w.Rand(0.12f, 0.25f) * w.Virt.Height;
                double a = Math.PI / 2 + w.Rand(-0.9f, 0.9f);
                bolt.Add(Fork(w, p.X, p.Y, p.X + (float)Math.Cos(a) * len, p.Y + (float)Math.Sin(a) * len, 0.38f));
            }
            boltLife = 0.32f;
            if (onLedge)
            {
                var sp = w.Get<Sparks>();
                if (sp != null) sp.Burst(w, x1, y1, 26, 420, Color.FromArgb(255, 200, 210, 255), 0.5f, 900);
                foreach (var l in w.Leaves)
                    if (l.Resting && l.Host == target.Host && Math.Abs(l.X - x1) < 90 * S)
                    { w.SyncRestPosition(l); w.Detach(l, (l.X - x1) * 6, -w.Rand(250, 600) * S, false); }
                var snow = w.Get<Snow>();
                if (snow != null) snow.Burst(w, x1, y1, 12, 0);
            }
        }

        // Midpoint displacement: a jagged path between two points.
        static PointF[] Fork(World w, float x0, float y0, float x1, float y1, float rough)
        {
            var pts = new List<PointF> { new PointF(x0, y0), new PointF(x1, y1) };
            float disp = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * rough;
            for (int level = 0; level < 6; level++)
            {
                var next = new List<PointF>();
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var a = pts[i]; var b = pts[i + 1];
                    next.Add(a);
                    next.Add(new PointF((a.X + b.X) / 2 + w.Rand(-disp, disp), (a.Y + b.Y) / 2 + w.Rand(-disp, disp) * 0.3f));
                }
                next.Add(pts[pts.Count - 1]);
                pts = next;
                disp *= 0.52f;
            }
            return pts.ToArray();
        }

        public override bool Trigger(World w, string name)
        {
            if (name == "lightning") { Strike(w); return true; }
            return false;
        }

        // ---------------------------------------------------------------- drawing

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            float S = w.S;
            var L = w.Sky.Light;
            float lf = 0.45f + 0.55f * L.G / 256f;
            var rainCol = Color.FromArgb(255, (int)(190 * lf + 20), (int)(205 * lf + 20), (int)(228 * lf + 25));
            var darkCol = Color.FromArgb(255, 52, 64, 84);
            foreach (var d in drops)
            {
                if (d.X < area.Left - 60 || d.X > area.Right + 60 || d.Y < area.Top - 60 || d.Y > area.Bottom + 60) continue;
                float sp = (float)Math.Sqrt(d.Vx * d.Vx + d.Vy * d.Vy), len = 0.024f * sp;
                float tx = d.X - d.Vx / sp * len, ty = d.Y - d.Vy / sp * len;
                // A faint dark twin keeps streaks visible over bright windows as well as dark desktops.
                Blit.Line(s, tx - area.Left + S, ty - area.Top, d.X - area.Left + S, d.Y - area.Top, darkCol, 0f, 0.16f, false);
                var r = Blit.Line(s, tx - area.Left, ty - area.Top, d.X - area.Left, d.Y - area.Top, rainCol, 0f, 0.46f, false);
                if (!r.IsEmpty) { r.Width += (int)S + 1; drawn.Add(r); }
            }
            foreach (var b in bits)
            {
                var r = Blit.Line(s, b.X - area.Left, b.Y - area.Top, b.X - b.Vx * 0.012f - area.Left, b.Y - b.Vy * 0.012f - area.Top, rainCol, 0.6f * Math.Min(1, b.Life * 5), 0.1f, false);
                if (!r.IsEmpty) drawn.Add(r);
            }
            if (dripDot == null) dripDot = Pixels.Dot(1.8f * S, Color.FromArgb(255, 205, 220, 240), 0.45f);
            foreach (var dr in drips)
            {
                if (!dr.Falling && dr.Size < 0.25f) continue;
                float sc = dr.Falling ? 1 : 0.35f + 0.65f * dr.Size;
                var r = Blit.DrawAt(s, dripDot, dr.X - area.Left, dr.Y - area.Top, sc, 230, L, false);
                if (!r.IsEmpty) drawn.Add(r);
                if (dr.Falling)
                {
                    var r2 = Blit.Line(s, dr.X - area.Left, dr.Y - area.Top - dr.Vy * 0.02f, dr.X - area.Left, dr.Y - area.Top, rainCol, 0, 0.5f, false);
                    if (!r2.IsEmpty) drawn.Add(r2);
                }
            }
            if (boltLife > 0 && bolt.Count > 0)
            {
                float a = Math.Min(1, boltLife / 0.12f) * (Flash > 0.05f ? 1 : 0.55f);
                foreach (var path in bolt)
                {
                    bool main = path == bolt[0];
                    for (int i = 0; i + 1 < path.Length; i++)
                    {
                        float ax = path[i].X - area.Left, ay = path[i].Y - area.Top, bx = path[i + 1].X - area.Left, by = path[i + 1].Y - area.Top;
                        var glowC = Color.FromArgb(255, 170, 160, 255);
                        for (int o = -2; o <= 2; o++)
                            if (o != 0) Blit.Line(s, ax + o * S, ay, bx + o * S, by, glowC, a * 0.22f, a * 0.22f, true);
                        var r = Blit.Line(s, ax, ay, bx, by, Color.White, a * (main ? 1 : 0.7f), a * (main ? 1 : 0.7f), true);
                        if (!r.IsEmpty) { r.Inflate((int)(3 * S), 0); drawn.Add(r); }
                    }
                }
            }
            if (Flash > 0.004f)
                drawn.Add(Blit.Fill(s, new Rectangle(0, 0, area.Width, area.Height), Pixels.Premul(Color.FromArgb(255, 235, 238, 255), Flash)));
        }

        public override bool Quiet { get { return drops.Count == 0 && bits.Count == 0 && drips.Count == 0; } }
    }
}
