using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Autumn
{
    // Icicles that grow under snowy windows in the cold, drip and shrink in a thaw, and snap off
    // and shatter when the window is yanked.
    class Icicles : Sys
    {
        class Icicle { public WinInfo Host; public float RelX, Len, Max, Width; }
        class Shard { public float X, Y, Vy, Len, Width, Angle, Spin, Prev; }
        readonly List<Icicle> list = new List<Icicle>();
        readonly List<Shard> shards = new List<Shard>();
        readonly Dictionary<WinInfo, float> seeded = new Dictionary<WinInfo, float>();

        public Icicles() { Layer = 22; }

        public override void Step(World w, float dt)
        {
            float S = w.S;
            var snow = w.Get<Snow>();
            float temp = w.Sky.Temperature;
            bool cold = temp < 0 && !w.Stopping;
            // New icicles along windows that carry snow.
            if (cold && snow != null && w.Chance(dt * 0.5f))
                foreach (var h in w.Wins)
                {
                    if (h.IsTaskbar || !h.CanHold) continue;
                    float depth = snow.DepthAt(h, h.Left + h.Width / 2);
                    if (depth < 4 * S) continue;
                    int count = 0;
                    foreach (var ic in list) if (ic.Host == h) count++;
                    if (count >= Math.Min(10, h.Width / (60 * S))) continue;
                    float rel = w.Rand(10 * S, h.Width - 10 * S);
                    bool clash = false;
                    foreach (var ic in list) if (ic.Host == h && Math.Abs(ic.RelX - rel) < 22 * S) clash = true;
                    if (!clash) list.Add(new Icicle { Host = h, RelX = rel, Max = w.Rand(10, 34) * S, Width = w.Rand(4, 7) * S });
                    break;
                }
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var ic = list[i];
                var h = ic.Host;
                bool gone = !w.Alive(h) || !h.CanHold;
                float a = (float)Math.Sqrt(h.Ax * h.Ax + h.Ay * h.Ay), v = Math.Abs(h.Vx) + Math.Abs(h.Vy);
                if (gone || ((a > 9000 * S || v > 1100 * S) && w.Chance(0.35f + ic.Len / (40 * S))))
                {
                    if (ic.Len > 3 * S)
                        shards.Add(new Shard { X = h.Left + ic.RelX, Y = h.Bottom, Prev = h.Bottom, Vy = Math.Max(0, h.Vy) * 0.5f, Len = ic.Len, Width = ic.Width, Angle = 0, Spin = w.Rand(-200, 200) });
                    list.RemoveAt(i);
                    continue;
                }
                if (cold) ic.Len = Math.Min(ic.Max, ic.Len + dt * S * (w.Sky.Lapsing ? 2 : 0.07f));
                else { ic.Len -= dt * S * 0.12f; if (ic.Len <= 0) { list.RemoveAt(i); continue; } }
            }
            for (int i = shards.Count - 1; i >= 0; i--)
            {
                var sh = shards[i];
                sh.Prev = sh.Y;
                sh.Vy += 1500 * S * dt;
                sh.Y += sh.Vy * dt;
                sh.Angle += sh.Spin * dt;
                Ledge hit; float top;
                if (w.HitLedge(sh.X, sh.Prev, sh.Y + sh.Len * 0.5f, true, out hit, out top) || sh.Y > w.Virt.Bottom)
                {
                    var sp = w.Get<Sparks>();
                    if (sp != null) sp.Burst(w, sh.X, Math.Min(top, w.Virt.Bottom), 10, 260, Color.FromArgb(255, 210, 235, 255), 0.5f, 900);
                    if (w.Sound != null) w.Sound.Play("tinkle", Math.Min(1, sh.Len / (20 * S)), Math.Max(-1, Math.Min(1, (sh.X - w.Virt.Left) / w.Virt.Width * 2 - 1)));
                    w.Journal.Count("icicles", 1);
                    w.Journal.Discover(w, "icicles");
                    shards.RemoveAt(i);
                }
            }
        }

        public override bool Trigger(World w, string name)
        {
            if (name != "icicles") return false;
            foreach (var h in w.Wins)
            {
                if (h.IsTaskbar || !h.CanHold) continue;
                for (float x = 24 * w.S; x < h.Width - 20 * w.S; x += w.Rand(40, 90) * w.S)
                    list.Add(new Icicle { Host = h, RelX = x, Max = w.Rand(12, 34) * w.S, Len = w.Rand(12, 30) * w.S, Width = w.Rand(4, 7) * w.S });
            }
            return true;
        }

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            float S = w.S;
            var L = w.Sky.Light;
            float lf = 0.7f + 0.3f * L.G / 256f;
            foreach (var ic in list)
            {
                var h = ic.Host;
                float x = h.Left + ic.RelX;
                // Hidden when the underside of the window is covered by one in front.
                bool hidden = false;
                foreach (var o in w.Wins) { if (o == h) break; if (x >= o.Left && x < o.Right && h.Bottom >= o.Top && h.Bottom < o.Bottom) { hidden = true; break; } }
                if (hidden || ic.Len < 1) continue;
                drawn.Add(DrawIcicle(s, x - area.Left, h.Bottom - area.Top, ic.Len, ic.Width, 0, lf));
            }
            foreach (var sh in shards)
                drawn.Add(DrawIcicle(s, sh.X - area.Left, sh.Y - area.Top, sh.Len, sh.Width, sh.Angle, lf));
        }

        // A tapered spike of ice: translucent body, a bright edge, a clear tip.
        static Rectangle DrawIcicle(Surface s, float x, float y, float len, float width, float angleDeg, float light)
        {
            double a = angleDeg * Math.PI / 180;
            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (float t = 0; t <= len; t += 0.7f)
            {
                float u = t / len;
                float half = width * 0.5f * (float)Math.Pow(1 - u, 1.25);
                for (float q = -half; q <= half; q += 0.7f)
                {
                    float px = x - sa * t + ca * q, py = y + ca * t + sa * q;
                    float edge = 1 - Math.Abs(q) / Math.Max(0.5f, half);
                    float alpha = (0.55f + 0.35f * (1 - edge)) * (0.7f + 0.3f * (1 - u));
                    bool hi = q < -half * 0.2f && q > -half * 0.7f;
                    int r = (int)((hi ? 255 : 215) * light), g = (int)((hi ? 255 : 232) * light), b = (int)(255 * Math.Min(1, light + 0.1f));
                    uint c = Pixels.Premul(Color.FromArgb(255, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b)), hi ? 0.85f : alpha);
                    int ix = (int)px, iy = (int)py;
                    Blit.Pixel(s, ix, iy, c, false);
                    if (ix < minX) minX = ix; if (ix > maxX) maxX = ix; if (iy < minY) minY = iy; if (iy > maxY) maxY = iy;
                }
            }
            return minX > maxX ? Rectangle.Empty : Rectangle.FromLTRB(minX - 1, minY - 1, maxX + 2, maxY + 2);
        }

        public override bool Quiet { get { return shards.Count == 0; } }
    }

    // Frost that creeps in from the screen corners on cold mornings. Moving the pointer through it
    // wipes it away, like a sleeve across a cold window; it slowly grows back.
    class Frost : Sys
    {
        class Corner { public int Cx, Cy, Sx, Sy; public float[] Mask; }
        Pixels tex;              // frost for the top-left corner; mirrored for the others
        int size, cell, gw;
        readonly List<Corner> corners = new List<Corner>();
        float level, wiped;
        bool forced;

        public Frost() { Layer = 58; }

        void Init(World w)
        {
            if (tex != null) return;
            float S = w.S;
            size = (int)(190 * S); cell = Math.Max(3, (int)(2 * S)); gw = (size + cell - 1) / cell;
            tex = Render(size, new Random(5));
            foreach (var g in w.Grounds)
            {
                int l = g.Left, r = g.Right, b = g.Top;
                corners.Add(new Corner { Cx = l, Cy = w.Virt.Top, Sx = 1, Sy = 1, Mask = Full() });
                corners.Add(new Corner { Cx = r, Cy = w.Virt.Top, Sx = -1, Sy = 1, Mask = Full() });
                corners.Add(new Corner { Cx = l, Cy = b, Sx = 1, Sy = -1, Mask = Full() });
                corners.Add(new Corner { Cx = r, Cy = b, Sx = -1, Sy = -1, Mask = Full() });
            }
        }

        float[] Full() { var m = new float[gw * gw]; for (int i = 0; i < m.Length; i++) m[i] = 1; return m; }

        // Fern frost: feathery dendrites that grow in from both edges, thickest near the corner,
        // over a faint milky haze.
        static Pixels Render(int size, Random rng)
        {
            using (var b = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(-size * 0.7f, -size * 0.7f, size * 1.4f, size * 1.4f);
                        using (var haze = new PathGradientBrush(path))
                        {
                            haze.CenterPoint = new PointF(0, 0);
                            haze.CenterColor = Color.FromArgb(80, 232, 242, 252);
                            haze.SurroundColors = new[] { Color.FromArgb(0, 232, 242, 252) };
                            g.FillEllipse(haze, -size * 0.7f, -size * 0.7f, size * 1.4f, size * 1.4f);
                        }
                    }
                    for (int k = 0; k < 70; k++)
                    {
                        bool top = rng.NextDouble() < 0.5;
                        float d = (float)Math.Pow(rng.NextDouble(), 1.6) * size * 0.95f;      // distance along the edge
                        float x = top ? d : 0, y = top ? 0 : d;
                        double baseA = top ? Math.PI / 2 : 0;                                    // grow into the screen
                        double a = baseA + (rng.NextDouble() - 0.5) * 1.3;
                        float len = (1 - d / size) * size * (0.25f + 0.35f * (float)rng.NextDouble()) + 8;
                        Fern(g, rng, x, y, a, len, 4, size);
                    }
                }
                return Pixels.From(b, 0, 0);
            }
        }

        static void Fern(Graphics g, Random rng, float x, float y, double a, float len, int depth, int size)
        {
            if (depth <= 0 || len < 3) return;
            // Walk the stem in short, slightly wandering steps, sprouting side barbs at +-60 degrees.
            int steps = Math.Max(2, (int)(len / 4));
            float px = x, py = y;
            for (int i = 0; i < steps; i++)
            {
                a += (rng.NextDouble() - 0.5) * 0.18;
                float nx = px + (float)Math.Cos(a) * len / steps, ny = py + (float)Math.Sin(a) * len / steps;
                float dist = (float)Math.Sqrt(nx * nx + ny * ny) / size;
                int alpha = (int)(185 * Math.Max(0, 1 - dist * 1.05f) * (0.55f + 0.15f * depth));
                if (alpha > 3)
                    using (var p = new Pen(Color.FromArgb(Math.Min(255, alpha), 242, 249, 255), Math.Max(0.5f, depth * 0.3f)))
                        g.DrawLine(p, px, py, nx, ny);
                if (depth > 1 && i % 2 == 1)
                {
                    float barb = len * (0.3f - 0.2f * i / steps) * (0.7f + 0.6f * (float)rng.NextDouble());
                    Fern(g, rng, nx, ny, a + Math.PI / 3, barb, depth - 1, size);
                    Fern(g, rng, nx, ny, a - Math.PI / 3, barb, depth - 1, size);
                }
                px = nx; py = ny;
            }
        }
        public override void Step(World w, float dt)
        {
            Init(w);
            var sky = w.Sky;
            bool frosty = forced || (sky.Season == Season.Winter && sky.Temperature < 1 && (sky.Hour < 11 || sky.Temperature < -6)) && !w.Stopping;
            level += ((frosty ? 1 : 0) - level) * Math.Min(1, dt / (frosty ? 90f : 30f) * (sky.Lapsing ? 20 : 1));
            if (level < 0.02f) return;
            // Wipe along the pointer's path.
            if (w.HaveCursor)
            {
                float S = w.S, R = 24 * S;
                foreach (var c in corners)
                {
                    float lx = (w.CurX - c.Cx) * c.Sx, ly = (w.CurY - c.Cy) * c.Sy;
                    if (lx < -R || ly < -R || lx > size + R || ly > size + R) continue;
                    int i0 = (int)((lx - R) / cell), i1 = (int)((lx + R) / cell), j0 = (int)((ly - R) / cell), j1 = (int)((ly + R) / cell);
                    for (int j = Math.Max(0, j0); j <= Math.Min(gw - 1, j1); j++)
                        for (int i = Math.Max(0, i0); i <= Math.Min(gw - 1, i1); i++)
                        {
                            float dx = (i + 0.5f) * cell - lx, dy = (j + 0.5f) * cell - ly;
                            float d = (float)Math.Sqrt(dx * dx + dy * dy) / R;
                            if (d >= 1) continue;
                            float keep = Math.Max(0, (d - 0.55f) / 0.45f);        // soft-edged swipe
                            float was = c.Mask[j * gw + i];
                            if (keep < was) { c.Mask[j * gw + i] = keep; if (was > 0.5f && keep < 0.5f) wiped += 1; }
                        }
                }
                if (wiped > gw * gw * 0.25f) w.Journal.Discover(w, "frost");
            }
            foreach (var c in corners) for (int i = 0; i < c.Mask.Length; i++) if (c.Mask[i] < 1) c.Mask[i] = Math.Min(1, c.Mask[i] + dt * 0.01f);
            if (forced && level > 0.95f) forced = false;
        }

        public override bool Trigger(World w, string name)
        {
            if (name != "frost") return false;
            Init(w); forced = true; level = Math.Max(level, 0.6f);
            return true;
        }

        public override unsafe void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            if (tex == null || level < 0.02f) return;
            uint lv = (uint)(level * 256);
            fixed (uint* tp = tex.Data)
                foreach (var c in corners)
                {
                    int ox = c.Cx - area.Left, oy = c.Cy - area.Top;
                    if (ox < -size || ox > area.Width + size || oy < -size || oy > area.Height + size) continue;
                    bool any = false;
                    for (int j = 0; j < gw; j++)
                        for (int i = 0; i < gw; i++)
                        {
                            float m = c.Mask[j * gw + i];
                            if (m < 0.03f) continue;
                            uint k = (uint)(m * lv);
                            for (int yy = j * cell; yy < Math.Min(size, (j + 1) * cell); yy++)
                            {
                                int sy = oy + c.Sy * yy - (c.Sy < 0 ? 1 : 0);
                                if (sy < 0 || sy >= s.H) continue;
                                uint* row = (uint*)((byte*)s.Bits + (long)sy * s.Stride);
                                for (int xx = i * cell; xx < Math.Min(size, (i + 1) * cell); xx++)
                                {
                                    uint px = tp[yy * size + xx];
                                    if (px == 0) continue;
                                    int sx = ox + c.Sx * xx - (c.Sx < 0 ? 1 : 0);
                                    if (sx < 0 || sx >= s.W) continue;
                                    px = Blit.Mul(px, k);
                                    uint a = px >> 24;
                                    uint* d = row + sx;
                                    *d = px + Blit.Mul(*d, 256 - (a + (a >> 7)));
                                }
                            }
                            any = true;
                        }
                    if (any)
                    {
                        var r = Rectangle.FromLTRB(c.Sx > 0 ? ox : ox - size, c.Sy > 0 ? oy : oy - size, c.Sx > 0 ? ox + size : ox, c.Sy > 0 ? oy + size : oy);
                        r.Intersect(new Rectangle(0, 0, area.Width, area.Height));
                        if (!r.IsEmpty) drawn.Add(r);
                    }
                }
        }

        public override bool Quiet { get { return level < 0.02f; } }
    }
}
