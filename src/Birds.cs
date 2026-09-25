using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Autumn
{
    // Hand-drawn (by code) little birds: a few species, standing, pecking and flying frames.
    static class BirdArt
    {
        public static readonly string[] Species = { "robin", "sparrow", "bluetit" };
        static readonly Dictionary<string, Pixels> cache = new Dictionary<string, Pixels>();

        // pose: 0 stand, 1 peck, 2 hop, 3-5 flight (wings up, level, down)
        public static Pixels Get(int species, int pose, float size)
        {
            string key = species + ":" + pose + ":" + (int)size;
            Pixels p;
            if (cache.TryGetValue(key, out p)) return p;
            int half = (int)Math.Ceiling(size * 0.95f) + 2;
            using (var b = new Bitmap(half * 2, half * 2, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TranslateTransform(half, half);
                    g.ScaleTransform(size, size);
                    Draw(g, species, pose);
                }
                p = Pixels.From(b, half, half + size * 0.42f);   // anchor at the feet
            }
            cache[key] = p;
            return p;
        }

        static void Draw(Graphics g, int sp, int pose)
        {
            Color back, breast, belly, cap, wingC;
            if (sp == 0) { back = C("6E5C4B"); breast = C("D8703A"); belly = C("EEE6DA"); cap = C("5E4E40"); wingC = C("5A4A3C"); }
            else if (sp == 1) { back = C("8C6D4C"); breast = C("CDBCA3"); belly = C("E4DACB"); cap = C("7A7466"); wingC = C("6E5337"); }
            else { back = C("7E9C5A"); breast = C("F0D04A"); belly = C("F4E47E"); cap = C("3E78C0"); wingC = C("4E86C8"); }
            bool fly = pose >= 3, peck = pose == 1;
            float tilt = fly ? 0 : peck ? 28 : -18;
            var st = g.Save();
            g.RotateTransform(tilt);
            using (var pen = new Pen(C("3A3028"), 0.035f))
            using (var legPen = new Pen(C("5A4636"), 0.04f))
            {
                if (!fly)
                {
                    // legs and feet (drawn first, under the body)
                    g.DrawLine(legPen, 0.02f, 0.16f, 0.0f, 0.42f); g.DrawLine(legPen, -0.06f, 0.16f, -0.08f, 0.42f);
                    g.DrawLine(legPen, -0.02f, 0.42f, 0.08f, 0.42f); g.DrawLine(legPen, -0.12f, 0.42f, -0.02f, 0.42f);
                }
                // tail
                using (var br = new SolidBrush(wingC))
                    g.FillPolygon(br, new[] { new PointF(-0.26f, -0.02f), new PointF(-0.66f, 0.04f), new PointF(-0.64f, 0.14f), new PointF(-0.24f, 0.12f) });
                if (fly) Wing(g, pose, wingC, true);
                // body
                using (var br = new LinearGradientBrush(new PointF(0, -0.22f), new PointF(0, 0.22f), back, belly))
                    g.FillEllipse(br, -0.34f, -0.2f, 0.66f, 0.4f);
                using (var br = new SolidBrush(breast))
                    g.FillEllipse(br, 0.02f, -0.1f, 0.28f, 0.26f);
                if (sp == 1)
                    using (var sb = new SolidBrush(Color.FromArgb(140, 90, 66, 44)))
                        for (int k = 0; k < 5; k++) g.FillEllipse(sb, 0.04f + k * 0.035f, -0.02f + (k % 2) * 0.05f, 0.03f, 0.05f);
                if (!fly) Wing(g, 0, wingC, false);
                // head
                float hx = peck ? 0.34f : 0.3f, hy = peck ? -0.08f : -0.2f;
                using (var br = new SolidBrush(back)) g.FillEllipse(br, hx - 0.15f, hy - 0.15f, 0.3f, 0.3f);
                using (var br = new SolidBrush(cap)) g.FillPie(br, hx - 0.15f, hy - 0.15f, 0.3f, 0.3f, 180, 180);
                if (sp == 2) using (var br = new SolidBrush(Color.White)) g.FillEllipse(br, hx - 0.06f, hy - 0.06f, 0.16f, 0.12f);
                if (sp == 0) using (var br = new SolidBrush(breast)) g.FillEllipse(br, hx - 0.02f, hy - 0.02f, 0.15f, 0.14f);
                // beak and eye
                using (var br = new SolidBrush(C(sp == 0 ? "3B3024" : "4A3A28")))
                    g.FillPolygon(br, new[] { new PointF(hx + 0.12f, hy - 0.03f), new PointF(hx + 0.27f, hy + 0.01f), new PointF(hx + 0.12f, hy + 0.05f) });
                using (var br = new SolidBrush(Color.FromArgb(255, 20, 16, 12))) g.FillEllipse(br, hx + 0.03f, hy - 0.06f, 0.06f, 0.06f);
                using (var br = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.FillEllipse(br, hx + 0.055f, hy - 0.05f, 0.02f, 0.02f);
            }
            g.Restore(st);
        }

        static void Wing(Graphics g, int pose, Color c, bool spread)
        {
            using (var br = new SolidBrush(c))
            using (var dark = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                if (!spread)
                {
                    g.FillEllipse(br, -0.26f, -0.12f, 0.42f, 0.22f);
                    g.FillEllipse(dark, -0.24f, 0.0f, 0.3f, 0.06f);
                    return;
                }
                float tipX = pose == 3 ? -0.12f : pose == 4 ? -0.42f : -0.2f;
                float tipY = pose == 3 ? -0.72f : pose == 4 ? -0.12f : 0.46f;
                g.FillPolygon(br, new[] { new PointF(0.1f, -0.08f), new PointF(tipX + 0.14f, tipY * 0.7f), new PointF(tipX, tipY), new PointF(-0.22f, -0.02f) });
            }
        }

        static Color C(string hex) { return LeafLook.Hex(hex); }

        // Goose silhouette seen from below-ish, flap frame 0..5.
        public static Pixels Goose(int frame, float size)
        {
            string key = "goose:" + frame + ":" + (int)size;
            Pixels p;
            if (cache.TryGetValue(key, out p)) return p;
            int half = (int)Math.Ceiling(size * 0.8f) + 2;
            using (var b = new Bitmap(half * 2, half * 2, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(b))
                using (var br = new SolidBrush(Color.FromArgb(235, 38, 40, 44)))
                using (var pen = new Pen(Color.FromArgb(235, 38, 40, 44), 0.06f))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TranslateTransform(half, half);
                    g.ScaleTransform(size, size);
                    // Seen from below: body and outstretched neck along the flight line, both wings
                    // spread across it, their span breathing with the wingbeat.
                    g.FillEllipse(br, -0.28f, -0.075f, 0.5f, 0.15f);                   // body
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, 0.18f, 0f, 0.46f, 0f);                             // neck
                    g.FillEllipse(br, 0.43f, -0.045f, 0.13f, 0.09f);                   // head
                    g.FillPolygon(br, new[] { new PointF(-0.28f, -0.04f), new PointF(-0.4f, 0f), new PointF(-0.28f, 0.04f) });
                    double a = Math.Cos(frame / 6.0 * Math.PI * 2);
                    float span = (float)(0.46 + 0.2 * a), sweep = (float)(0.1 + 0.06 * a);
                    foreach (float side in new[] { -1f, 1f })
                        g.FillPolygon(br, new[] { new PointF(0.1f, side * 0.05f), new PointF(0.02f, side * span * 0.55f), new PointF(-0.06f - sweep, side * span),
                                                  new PointF(-0.1f - sweep, side * span * 0.96f), new PointF(-0.12f, side * span * 0.45f), new PointF(-0.12f, side * 0.05f) });
                }
                p = Pixels.From(b, half, half);
            }
            cache[key] = p;
            return p;
        }
    }

    // Songbirds that fly in, perch on window ledges, hop and peck, and leave when disturbed.
    class Birds : Sys
    {
        class Bird
        {
            public int Species, State;            // 0 flying in, 1 perched, 2 leaving
            public float X, Y, Vx, Vy, Flap, Size, T, NextAct, Stay, HopT, HopFrom, HopTo, PeckT;
            public bool FaceLeft, Trusted;
            public WinInfo Host; public float RelX, TargetX, Trust;
        }
        readonly List<Bird> birds = new List<Bird>();
        float nextT = 45;

        public Birds() { Layer = 42; }

        bool Welcome(World w)
        {
            var sky = w.Sky;
            return sky.Season != Season.Winter && sky.IsDay && w.Weather.Precip < 0.1f && Math.Abs(w.Wind) < 160 * w.S && !w.Stopping;
        }

        public override void Step(World w, float dt)
        {
            float S = w.S;
            if (Welcome(w))
            {
                nextT -= dt * (w.Sky.Lapsing ? 10 : 1);
                if (nextT <= 0)
                {
                    bool dawn = w.Sky.Hour < 10;
                    nextT = w.Rand(dawn ? 90 : 200, dawn ? 300 : 600);
                    int n = w.Chance(0.3f) ? 2 + w.Rng.Next(2) : 1;
                    for (int k = 0; k < n; k++) Arrive(w, k * 0.6f);
                }
            }
            for (int i = birds.Count - 1; i >= 0; i--)
            {
                var b = birds[i];
                b.T += dt;
                if (b.State == 0) FlyIn(w, b, dt);
                else if (b.State == 1) Perch(w, b, dt);
                else
                {
                    b.Flap += dt * 13;
                    b.Vy -= 260 * S * dt;
                    b.Vx += (b.FaceLeft ? -1 : 1) * 400 * S * dt;
                    b.X += b.Vx * dt; b.Y += b.Vy * dt;
                    if (b.X < w.Virt.Left - 80 * S || b.X > w.Virt.Right + 80 * S || b.Y < w.Virt.Top - 80 * S) birds.RemoveAt(i);
                }
            }
        }

        void Arrive(World w, float delay)
        {
            float S = w.S;
            // Choose somewhere to land: a visible stretch of ledge, windows preferred over the taskbar.
            var options = new List<Ledge>();
            foreach (var e in w.LedgeList) if (e.X1 - e.X0 > 60 * S && !e.Host.IsGround) options.Add(e);
            if (options.Count == 0) return;
            Ledge pick = options[w.Rng.Next(options.Count)];
            foreach (var e in options) if (!e.Host.IsTaskbar && w.Chance(0.5f)) pick = e;
            var b = new Bird { Species = w.Rng.Next(3), Size = w.Rand(22, 27) * S, Host = pick.Host };
            b.TargetX = w.Rand(pick.X0 + 20 * S, pick.X1 - 20 * S);
            bool fromLeft = b.TargetX > (w.Virt.Left + w.Virt.Right) / 2 ? w.Chance(0.3f) : w.Chance(0.7f);
            b.X = fromLeft ? w.Virt.Left - 40 * S - delay * 90 * S : w.Virt.Right + 40 * S + delay * 90 * S;
            b.Y = w.Virt.Top + w.Rand(0.05f, 0.4f) * w.Virt.Height;
            b.FaceLeft = !fromLeft;
            b.Stay = w.Rand(25, 70);
            birds.Add(b);
            w.Journal.Count("birds", 1);
        }

        void FlyIn(World w, Bird b, float dt)
        {
            float S = w.S;
            if (!w.Alive(b.Host) || !b.Host.CanHold) { b.State = 2; return; }
            float tx = b.TargetX + (b.Host.Left - 0), ty = b.Host.Top - w.HeightAt(b.Host, b.TargetX) - 1;
            float dx = tx - b.X, dy = ty - b.Y, d = (float)Math.Sqrt(dx * dx + dy * dy) + 0.01f;
            float speed = Math.Min(320 * S, d * 2.2f + 40 * S);
            float k = 1f - (float)Math.Exp(-dt * 3);
            b.Vx += (dx / d * speed - b.Vx) * k;
            b.Vy += (dy / d * speed - 40 * S * (d > 150 * S ? 1 : 0) - b.Vy) * k;
            b.X += b.Vx * dt; b.Y += b.Vy * dt;
            b.FaceLeft = b.Vx < 0;
            // flap in bursts, glide between them
            b.Flap += dt * (d < 120 * S ? 16 : ((int)(b.T * 1.3f) % 3 == 0 ? 2 : 13));
            if (d < 5 * S)
            {
                b.State = 1; b.RelX = b.TargetX - b.Host.Left; b.T = 0; b.NextAct = w.Rand(0.6f, 2f);
                if (w.Sound != null && w.Chance(0.5f)) w.Sound.Play("bird", 0.5f, Pan(w, b.X));
            }
        }

        void Perch(World w, Bird b, float dt)
        {
            float S = w.S;
            var h = b.Host;
            bool startled = !w.Alive(h) || !h.CanHold || (float)Math.Sqrt(h.Ax * h.Ax + h.Ay * h.Ay) > 4000 * S || Math.Abs(h.Vx) + Math.Abs(h.Vy) > 700 * S;
            if (w.HaveCursor)
            {
                float dx = w.CurX - b.X, dy = w.CurY - (b.Y - b.Size * 0.3f), d2 = dx * dx + dy * dy;
                if (d2 < (150 * S) * (150 * S) && w.CurSpeed > 60 * S) startled = true;
                else if (d2 < (45 * S) * (45 * S) && w.CurStill > 1.2f)
                {
                    // A slow, still hand is tolerated: the bird relaxes, sings, and stays a while.
                    b.Trust += dt;
                    if (b.Trust > 1.5f && !b.Trusted)
                    {
                        b.Trusted = true; b.Stay += 15;
                        b.HopFrom = b.RelX; b.HopTo = b.RelX; b.HopT = 0.28f;
                        if (w.Sound != null) w.Sound.Play("bird", 0.8f, Pan(w, b.X));
                        w.Journal.Discover(w, "gentle");
                    }
                }
            }
            if (b.T > b.Stay || startled || w.Weather.Precip > 0.2f || Math.Abs(w.Gust) > 250 * S)
            {
                b.State = 2; b.Vx = (b.FaceLeft ? -1 : 1) * 120 * S; b.Vy = -180 * S; b.Flap = 0;
                if (startled && w.Sound != null) w.Sound.Play("bird", 0.6f, Pan(w, b.X));
                return;
            }
            b.NextAct -= dt;
            if (b.HopT > 0)
            {
                b.HopT -= dt;
                float u = 1 - b.HopT / 0.28f;
                b.RelX = b.HopFrom + (b.HopTo - b.HopFrom) * u;
            }
            if (b.PeckT > 0) b.PeckT -= dt;
            if (b.NextAct <= 0)
            {
                b.NextAct = w.Rand(0.7f, 2.4f);
                double r = w.Rng.NextDouble();
                if (r < 0.35)
                {
                    float dir = b.FaceLeft ? -1 : 1;
                    if (w.Chance(0.3f)) { dir = -dir; b.FaceLeft = !b.FaceLeft; }
                    float to = b.RelX + dir * w.Rand(10, 26) * S;
                    if (to > 12 * S && to < h.Width - 12 * S) { b.HopFrom = b.RelX; b.HopTo = to; b.HopT = 0.28f; }
                }
                else if (r < 0.7) b.PeckT = 0.5f;
                else if (r < 0.8) b.FaceLeft = !b.FaceLeft;
                else if (r < 0.86 && w.Sound != null) w.Sound.Play("bird", 0.4f, Pan(w, b.X));
            }
            b.X = h.Left + b.RelX;
            float hop = b.HopT > 0 ? (float)Math.Sin(Math.PI * (1 - b.HopT / 0.28f)) * 8 * S : 0;
            b.Y = h.Top - w.HeightAt(h, b.X) - hop + (h.IsTaskbar ? 3 * S : 1);
        }

        static float Pan(World w, float x) { return Math.Max(-1, Math.Min(1, (x - w.Virt.Left) / Math.Max(1f, w.Virt.Width) * 2 - 1)); }

        public override bool Trigger(World w, string name)
        {
            if (name != "birds") return false;
            Arrive(w, 0); Arrive(w, 0.7f);
            return true;
        }

        readonly float[] m = new float[6];

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            var light = w.Sky.Light;
            foreach (var b in birds)
            {
                int pose;
                if (b.State == 1) pose = b.HopT > 0 ? 2 : b.PeckT > 0 && (int)(b.PeckT * 8) % 2 == 0 ? 1 : 0;
                else { int f = (int)b.Flap % 4; pose = f == 0 ? 3 : f == 1 ? 4 : f == 2 ? 5 : 4; }
                var p = BirdArt.Get(b.Species, pose, b.Size);
                float sx = b.FaceLeft ? -1 : 1;
                m[0] = sx; m[1] = 0; m[2] = 0; m[3] = 1;
                m[4] = b.X - area.Left - p.Cx * sx; m[5] = b.Y - area.Top - p.Cy;
                var r = Blit.DrawAffine(s, p, m, 256, light, false);
                if (!r.IsEmpty) drawn.Add(r);
            }
        }

        public override bool Quiet { get { return birds.Count == 0; } }
    }

    // Migrating geese crossing high up in a long V, honking now and then.
    class Geese : Sys
    {
        class Goose { public float Ox, Oy, Phase; }
        readonly List<Goose> flock = new List<Goose>();
        float x, y, vx, nextT = 900, honkT;
        bool flying;

        public Geese() { Layer = 44; }

        public override void Step(World w, float dt)
        {
            var sky = w.Sky;
            double doy = sky.YearPhase * 365;
            bool migrating = (doy > 70 && doy < 125) || (doy > 255 && doy < 325);
            if (!flying && migrating && sky.SunElev > -4 && w.Weather.Precip < 0.3f && !w.Stopping)
            {
                nextT -= dt * (sky.Lapsing ? 30 : 1);
                if (nextT <= 0) { nextT = w.Rand(2400, 5400); Launch(w); }
            }
            if (!flying) return;
            x += vx * dt;
            foreach (var g in flock) g.Phase += dt * (5.5f + 0.4f * (float)Math.Sin(g.Ox));
            honkT -= dt;
            if (honkT <= 0 && w.Sound != null)
            {
                honkT = w.Rand(0.4f, 1.8f);
                w.Sound.Play("honk", w.Rand(0.4f, 0.9f), Math.Max(-1, Math.Min(1, (x - w.Virt.Left) / w.Virt.Width * 2 - 1)));
            }
            float span = 400 * w.S;
            if ((vx > 0 && x - span > w.Virt.Right) || (vx < 0 && x + span < w.Virt.Left)) flying = false;
        }

        void Launch(World w)
        {
            float S = w.S;
            flying = true;
            bool ltr = w.Chance(0.5f);
            vx = (ltr ? 1 : -1) * w.Rand(170, 240) * S;
            x = ltr ? w.Virt.Left - 60 * S : w.Virt.Right + 60 * S;
            y = w.Virt.Top + w.Rand(0.06f, 0.2f) * w.Virt.Height;
            flock.Clear();
            int n = 7 + w.Rng.Next(7);
            flock.Add(new Goose());
            for (int i = 1; i < n; i++)
            {
                int rank = (i + 1) / 2; float side = i % 2 == 0 ? 1 : -1;
                if (w.Chance(0.25f)) side = -side;
                flock.Add(new Goose { Ox = -(ltr ? 1 : -1) * rank * 34 * S, Oy = side * rank * 22 * S + w.Rand(-3, 3) * S, Phase = w.Rand(0, 6) });
            }
            honkT = 0.3f;
            w.Journal.Count("geese", 1);
            w.Journal.Discover(w, "geese");
        }

        public override bool Trigger(World w, string name)
        {
            if (name != "geese") return false;
            Launch(w); return true;
        }

        readonly float[] m = new float[6];

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            if (!flying) return;
            bool night = w.Sky.Darkness > 0.6f;
            foreach (var g in flock)
            {
                var p = BirdArt.Goose(((int)(g.Phase * 6 / 6.283f) % 6 + 6) % 6, 30 * w.S);
                float sx = vx > 0 ? 1 : -1;
                float gx = x + g.Ox, gy = y + g.Oy + (float)Math.Sin(g.Phase * 0.5f) * 2 * w.S;
                m[0] = sx; m[1] = 0; m[2] = 0; m[3] = 1;
                m[4] = gx - area.Left - p.Cx * sx; m[5] = gy - area.Top - p.Cy;
                var r = Blit.DrawAffine(s, p, m, night ? 140u : 200u, Light.Full, false);
                if (!r.IsEmpty) drawn.Add(r);
            }
        }

        public override bool Quiet { get { return !flying; } }
    }
}
