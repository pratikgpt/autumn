using System;
using System.Collections.Generic;
using System.Drawing;

namespace Autumn
{
    // Things that happen to and around the pointer: a leaf that hitches a ride on a still pointer,
    // a cap of snow that builds up in a snowfall, and a whirlwind stirred by circling the mouse.
    class CursorFx : Sys
    {
        Leaf rider;
        float rideT, cap;
        Pixels capDot, capHi;
        // gesture tracking
        readonly List<float[]> trail = new List<float[]>();     // x, y, time
        float cooldown;
        // whirlwind
        public bool Active;
        public float Wx, Wy, Wr, Wt, Wdir;

        public CursorFx() { Layer = 60; }

        public override void Step(World w, float dt)
        {
            float S = w.S;
            Hitchhiker(w, dt);
            SnowHat(w, dt);
            Gesture(w, dt);
            if (Active) Whirl(w, dt);
        }

        // ---------------------------------------------------------------- hitchhiker

        void Hitchhiker(World w, float dt)
        {
            float S = w.S;
            if (rider != null)
            {
                if (!w.HaveCursor || w.CurSpeed > 350 * S || !w.Leaves.Contains(rider))
                {
                    if (w.Leaves.Contains(rider))
                    {
                        rider.OnCursor = false;
                        rider.Ix = w.CurVx * 0.5f; rider.Iy = w.CurVy * 0.4f - 120 * S;
                        rider.Tumbler = true; rider.Spin = w.Rand(-400, 400); rider.PrevContact = rider.Y + rider.Look.Size * 0.26f;
                    }
                    rider = null; rideT = 0;
                    return;
                }
                rider.X = w.CurX + rider.Look.Size * 0.28f; rider.Y = w.CurY - rider.Look.Size * 0.12f;
                rideT += dt;
                if (rideT > 2) w.Journal.Discover(w, "hitchhiker");
                return;
            }
            if (!w.HaveCursor || w.CurStill < 1.2f) return;
            foreach (var l in w.Leaves)
            {
                if (l.Resting || l.OnCursor) continue;
                float dx = l.X - w.CurX, dy = l.Y + l.Look.Size * 0.2f - w.CurY;
                if (dx * dx + dy * dy < (14 * S) * (14 * S) && dy < 0)
                {
                    rider = l; l.OnCursor = true; l.Ix = l.Iy = 0; l.Tumbler = false; l.Tumble = 0; l.Angle = w.Rand(-30, 30) + 90;
                    rideT = 0;
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- snow hat

        void SnowHat(World w, float dt)
        {
            float S = w.S;
            var snow = w.Get<Snow>();
            if (cap > 0 && w.HaveCursor && w.CurSpeed > 380 * S)
            {
                if (snow != null) snow.Burst(w, w.CurX + 5 * S, w.CurY - 3 * S, 4 + (int)(cap / 6), w.CurVx * 0.4f);
                cap = 0;
                return;
            }
            if (snow == null || !w.HaveCursor) return;
            if (w.CurStill > 0.8f)
            {
                float hx = w.CurX + 5 * S, hy = w.CurY - 2 * S;
                for (int i = snow.Flakes.Count - 1; i >= 0; i--)
                {
                    var f = snow.Flakes[i];
                    if (f.Puff || f.Kind == 5) continue;
                    float dx = f.X - hx, dy = f.Y - hy;
                    if (dx * dx + dy * dy < (11 * S + cap * 0.15f * S) * (11 * S + cap * 0.15f * S) && f.Y < hy)
                    {
                        cap = Math.Min(60, cap + 1 + f.Kind);
                        snow.Flakes.RemoveAt(i);
                    }
                }
            }
            if (!w.Weather.Snowy(w.Sky) || w.Sky.Temperature > 2) cap = Math.Max(0, cap - dt * 2);
            if (cap > 30) w.Journal.Discover(w, "snowhat");
        }

        // ---------------------------------------------------------------- whirlwind gesture

        void Gesture(World w, float dt)
        {
            float S = w.S;
            cooldown -= dt;
            if (!w.HaveCursor) { trail.Clear(); return; }
            trail.Add(new[] { w.CurX, w.CurY, w.Time });
            while (trail.Count > 0 && w.Time - trail[0][2] > 1.3f) trail.RemoveAt(0);
            if (trail.Count < 12 || cooldown > 0) return;
            // Total turning of the path, its centre, and how round it is.
            double turn = 0, len = 0, cx = 0, cy = 0;
            foreach (var p in trail) { cx += p[0]; cy += p[1]; }
            cx /= trail.Count; cy /= trail.Count;
            double prevA = double.NaN;
            for (int i = 1; i < trail.Count; i++)
            {
                double dx = trail[i][0] - trail[i - 1][0], dy = trail[i][1] - trail[i - 1][1];
                double seg = Math.Sqrt(dx * dx + dy * dy);
                if (seg < 2) continue;
                len += seg;
                double a = Math.Atan2(dy, dx);
                if (!double.IsNaN(prevA)) turn += Math.Atan2(Math.Sin(a - prevA), Math.Cos(a - prevA));
                prevA = a;
            }
            double rSum = 0, rMin = double.MaxValue, rMax = 0;
            foreach (var p in trail)
            {
                double r = Math.Sqrt((p[0] - cx) * (p[0] - cx) + (p[1] - cy) * (p[1] - cy));
                rSum += r; rMin = Math.Min(rMin, r); rMax = Math.Max(rMax, r);
            }
            double rMean = rSum / trail.Count;
            double span = trail[trail.Count - 1][2] - trail[0][2];
            bool round = rMean > 25 * S && rMean < 300 * S && rMin > rMean * 0.35;
            if (Math.Abs(turn) > Math.PI * 2 * 1.2 && round && len / Math.Max(0.1, span) > 350 * S)
            {
                Start(w, (float)cx, (float)cy, (float)Math.Max(130 * S, rMean * 1.6), Math.Sign(turn));
                trail.Clear();
                cooldown = 4;
            }
        }

        public void Start(World w, float x, float y, float r, float dir)
        {
            Active = true; Wx = x; Wy = y; Wr = r; Wt = 0; Wdir = dir == 0 ? 1 : dir;
            if (w.Sound != null) w.Sound.Play("whoosh", 1f, Math.Max(-1, Math.Min(1, (x - w.Virt.Left) / w.Virt.Width * 2 - 1)));
            // The vortex brings its own debris: a handful of this season's leaves or petals already turning in it
            // (in winter, the snow system's powder does the job).
            if (w.Sky.Season != Season.Winter)
                for (int i = 0; i < 14; i++)
                {
                    double a = w.Rng.NextDouble() * Math.PI * 2;
                    float rr = r * w.Rand(0.4f, 1.1f);
                    var l = w.NewLeaf(x + (float)Math.Cos(a) * rr, y + (float)Math.Sin(a) * rr);
                    l.Ix = -(float)Math.Sin(a) * 500 * w.S * Wdir; l.Iy = (float)Math.Cos(a) * 500 * w.S * Wdir; l.Tumbler = true;
                    l.NoLandUntil = w.Time + 3;
                    w.Leaves.Add(l);
                }
            else
            {
                var snow = w.Get<Snow>();
                if (snow != null) for (int i = 0; i < 6; i++) snow.Burst(w, x + w.Rand(-r, r), y + w.Rand(-r, r) * 0.5f, 6, 0);
            }
            w.Journal.Count("whirlwinds", 1);
            w.Journal.Discover(w, "whirlwind");
        }

        void Whirl(World w, float dt)
        {
            float S = w.S;
            Wt += dt;
            float life = 6.5f;
            if (Wt > life) { Active = false; return; }
            float str = Wt < 0.4f ? Wt / 0.4f : Wt > life - 2 ? (life - Wt) / 2 : 1;
            Wx += w.Wind * 0.25f * dt;
            Wy -= 12 * S * dt;
            float R = Wr * 2.2f;
            foreach (var l in w.Leaves)
            {
                if (l.OnCursor) continue;
                float dx = l.X - Wx, dy = l.Y - Wy, d = (float)Math.Sqrt(dx * dx + dy * dy) + 1;
                if (d > R) continue;
                float f = str * (1 - d / R);
                if (l.Resting)
                {
                    if (f > 0.1f && w.Chance(dt * 8 * f)) { w.SyncRestPosition(l); w.Detach(l, -dy / d * 300 * S * Wdir, -w.Rand(300, 600) * S, false); }
                    continue;
                }
                // tangential spin, gentle pull inward, and lift
                float tvx = -dy / d * 620 * S * Wdir * f - dx / d * 160 * S * f;
                float tvy = dx / d * 620 * S * Wdir * f - dy / d * 160 * S * f - 260 * S * f;
                float k = 1f - (float)Math.Exp(-dt * 6);
                l.Ix += (tvx - l.Ix) * k; l.Iy += (tvy - l.Iy) * k;
                l.Tumbler = true;
            }
            var snow = w.Get<Snow>();
            if (snow != null)
                foreach (var fl in snow.Flakes)
                {
                    float dx = fl.X - Wx, dy = fl.Y - Wy, d = (float)Math.Sqrt(dx * dx + dy * dy) + 1;
                    if (d > R) continue;
                    float f = str * (1 - d / R);
                    fl.X += (-dy / d * 520 * S * Wdir * f) * dt;
                    fl.Y += (dx / d * 520 * S * Wdir * f - 200 * S * f) * dt;
                }
            // dust motes make the vortex visible even with nothing to lift
            var sp = w.Get<Sparks>();
            if (sp != null && w.Chance(dt * 40 * str))
            {
                double a = w.Rng.NextDouble() * Math.PI * 2;
                float rr = w.Rand(0.3f, 1f) * Wr;
                var s = sp.Emit(w, Wx + (float)Math.Cos(a) * rr, Wy + (float)Math.Sin(a) * rr, -(float)Math.Sin(a) * 500 * S * Wdir, (float)Math.Cos(a) * 500 * S * Wdir - 150 * S, w.Rand(0.4f, 0.9f), 0.35f, Color.FromArgb(255, 200, 190, 170), -60, 0.5f);
            }
        }

        public override bool Trigger(World w, string name)
        {
            if (name != "whirlwind") return false;
            Start(w, w.Virt.Left + w.Virt.Width * 0.5f, w.Virt.Bottom - 200 * w.S, 150 * w.S, 1);
            return true;
        }

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            if (cap <= 0.5f || !w.HaveCursor) return;
            float S = w.S;
            if (capDot == null) { capDot = Pixels.Dot(4 * S, Color.FromArgb(255, 246, 249, 255), 0.55f); capHi = Pixels.Dot(2 * S, Color.White, 0.5f); }
            float r = (float)Math.Sqrt(cap) / 7.7f;          // 0..1
            float hx = w.CurX + 5 * S - area.Left, hy = w.CurY - 2 * S - area.Top;
            var light = Light.From(0.8f + 0.2f * w.Sky.Light.R / 256f, 0.8f + 0.2f * w.Sky.Light.G / 256f, 0.85f + 0.15f * w.Sky.Light.B / 256f);
            // a lumpy little heap: three overlapping mounds and a highlight
            var rr = Blit.DrawAt(s, capDot, hx, hy - 1 * S * r, 0.6f + 1.2f * r, 250, light, false);
            Blit.DrawAt(s, capDot, hx - 4 * S * r, hy + 1 * S, 0.5f + 0.8f * r, 250, light, false);
            Blit.DrawAt(s, capDot, hx + 4 * S * r, hy + 1.5f * S, 0.5f + 0.8f * r, 250, light, false);
            Blit.DrawAt(s, capHi, hx - 1 * S, hy - 3 * S * r, 0.6f + 0.6f * r, 200, light, false);
            if (!rr.IsEmpty) { rr.Inflate((int)(8 * S), (int)(6 * S)); drawn.Add(rr); }
        }
    }
}
