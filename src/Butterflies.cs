using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Autumn
{
    // Butterflies seen from above: wing frames from open to folded, four species.
    static class ButterflyArt
    {
        static readonly Dictionary<string, Pixels> cache = new Dictionary<string, Pixels>();

        public static Pixels Get(int species, int frame, float size)
        {
            string key = species + ":" + frame + ":" + (int)size;
            Pixels p;
            if (cache.TryGetValue(key, out p)) return p;
            int half = (int)Math.Ceiling(size * 0.7f) + 2;
            using (var b = new Bitmap(half * 2, half * 2, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TranslateTransform(half, half);
                    g.ScaleTransform(size, size);
                    float open = new[] { 1f, 0.72f, 0.38f, 0.12f }[frame];
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var st = g.Save();
                        g.ScaleTransform(side * open, 1);
                        Wings(g, species);
                        g.Restore(st);
                    }
                    using (var br = new SolidBrush(Color.FromArgb(255, 40, 32, 28)))
                    {
                        g.FillEllipse(br, -0.035f, -0.22f, 0.07f, 0.46f);
                        g.FillEllipse(br, -0.045f, -0.3f, 0.09f, 0.09f);
                    }
                    using (var pen = new Pen(Color.FromArgb(200, 40, 32, 28), 0.012f))
                    {
                        g.DrawBezier(pen, -0.02f, -0.28f, -0.06f, -0.4f, -0.1f, -0.44f, -0.13f, -0.46f);
                        g.DrawBezier(pen, 0.02f, -0.28f, 0.06f, -0.4f, 0.1f, -0.44f, 0.13f, -0.46f);
                    }
                }
                p = Pixels.From(b, half, half);
            }
            cache[key] = p;
            return p;
        }

        static void Wings(Graphics g, int sp)
        {
            // Forewing: a broad triangle whose leading edge runs out sideways to a rounded apex;
            // hindwing: a rounded fan tucked behind it. Wider than long, as seen from above.
            var fore = new GraphicsPath();
            fore.AddBezier(0.03f, -0.1f, 0.22f, -0.2f, 0.46f, -0.28f, 0.62f, -0.22f);
            fore.AddBezier(0.62f, -0.22f, 0.66f, -0.14f, 0.6f, 0.0f, 0.46f, 0.05f);
            fore.AddBezier(0.46f, 0.05f, 0.3f, 0.06f, 0.14f, 0.04f, 0.04f, 0.02f);
            fore.CloseFigure();
            var hind = new GraphicsPath();
            hind.AddBezier(0.04f, 0.0f, 0.22f, 0.02f, 0.44f, 0.04f, 0.48f, 0.14f);
            hind.AddBezier(0.48f, 0.14f, 0.5f, 0.28f, 0.34f, 0.36f, 0.2f, 0.33f);
            hind.AddBezier(0.2f, 0.33f, 0.1f, 0.3f, 0.05f, 0.24f, 0.04f, 0.16f);
            hind.CloseFigure();
            Color c1, c2, edge;
            if (sp == 0) { c1 = LeafLook.Hex("F08A24"); c2 = LeafLook.Hex("D96A12"); edge = LeafLook.Hex("1E1A18"); }
            else if (sp == 1) { c1 = LeafLook.Hex("FAFAF4"); c2 = LeafLook.Hex("EDEEE4"); edge = LeafLook.Hex("8A8A86"); }
            else if (sp == 2) { c1 = LeafLook.Hex("6FA4F0"); c2 = LeafLook.Hex("3F6FD0"); edge = LeafLook.Hex("20305A"); }
            else { c1 = LeafLook.Hex("F6E65A"); c2 = LeafLook.Hex("E8CF3A"); edge = LeafLook.Hex("9A8A30"); }
            using (var b1 = new LinearGradientBrush(new PointF(0, 0), new PointF(0.5f, -0.3f), c2, c1)) g.FillPath(b1, fore);
            using (var b2 = new LinearGradientBrush(new PointF(0, 0), new PointF(0.35f, 0.3f), c2, c1)) g.FillPath(b2, hind);
            using (var pen = new Pen(edge, sp == 0 ? 0.045f : 0.02f)) { g.DrawPath(pen, fore); g.DrawPath(pen, hind); }
            if (sp == 0)
            {
                // monarch veins and white margin dots
                using (var vp = new Pen(Color.FromArgb(220, 30, 26, 24), 0.018f))
                {
                    g.DrawLine(vp, 0.05f, -0.06f, 0.58f, -0.2f); g.DrawLine(vp, 0.05f, -0.03f, 0.56f, -0.06f); g.DrawLine(vp, 0.05f, 0.0f, 0.44f, 0.03f);
                    g.DrawLine(vp, 0.05f, 0.04f, 0.42f, 0.14f); g.DrawLine(vp, 0.05f, 0.06f, 0.34f, 0.28f); g.DrawLine(vp, 0.05f, 0.08f, 0.2f, 0.3f);
                }
                using (var wb = new SolidBrush(Color.White))
                    foreach (var pt in new[] { new PointF(0.6f, -0.2f), new PointF(0.62f, -0.12f), new PointF(0.58f, -0.04f), new PointF(0.44f, 0.24f), new PointF(0.36f, 0.31f), new PointF(0.47f, 0.15f) })
                        g.FillEllipse(wb, pt.X - 0.012f, pt.Y - 0.012f, 0.024f, 0.024f);
            }
            if (sp == 1)
                using (var tb = new SolidBrush(Color.FromArgb(220, 40, 40, 40))) { g.FillEllipse(tb, 0.5f, -0.25f, 0.13f, 0.12f); g.FillEllipse(tb, 0.3f, -0.08f, 0.06f, 0.06f); }
            fore.Dispose(); hind.Dispose();
        }
    }

    class Butterflies : Sys
    {
        public class Fly
        {
            public int Species, State;           // 0 flying, 1 resting on a ledge, 2 resting on the pointer, 3 leaving
            public float X, Y, Heading, Turn, Speed, Flap, Size, Rest, Bob;
            public WinInfo Host; public float RelX; public bool Seeking; public float Tx, Ty;
        }
        public readonly List<Fly> List = new List<Fly>();
        float nextT = 30, perchT;

        public Butterflies() { Layer = 43; }

        bool Welcome(World w)
        {
            var sky = w.Sky;
            bool warm = sky.Season == Season.Spring || sky.Season == Season.Summer || (sky.Season == Season.Autumn && sky.YearPhase < 0.8f);
            return warm && sky.IsDay && w.Weather.Precip < 0.05f && Math.Abs(w.Wind) < 120 * w.S && !w.Stopping;
        }

        public override void Step(World w, float dt)
        {
            float S = w.S;
            if (Welcome(w) && List.Count < 3)
            {
                nextT -= dt * (w.Sky.Lapsing ? 10 : 1);
                if (nextT <= 0) { nextT = w.Rand(90, 260); Spawn(w, w.Sky.Season == Season.Autumn ? 0 : w.Rng.Next(4)); }
            }
            // A still pointer on a sunny day invites a visitor.
            if (w.HaveCursor && w.CurStill > 4 && List.Count > 0)
            {
                bool someone = false;
                foreach (var f in List) if (f.Seeking || f.State == 2) someone = true;
                if (!someone) foreach (var f in List) if (f.State == 0) { f.Seeking = true; break; }
            }
            for (int i = List.Count - 1; i >= 0; i--)
            {
                var f = List[i];
                f.Flap += dt * (f.State == 0 || f.State == 3 ? 11 : 1.6f);
                f.Bob += dt;
                if (f.State == 0 || f.State == 3) Wander(w, f, dt);
                else if (f.State == 1)
                {
                    var h = f.Host;
                    bool disturbed = !w.Alive(h) || !h.CanHold || Math.Abs(h.Vx) + Math.Abs(h.Vy) > 300 * S || Near(w, f, 130 * S);
                    f.Rest -= dt;
                    if (disturbed || f.Rest <= 0 || w.Weather.Precip > 0.1f) { f.State = 0; f.Heading = -1.4f + w.Rand(-0.6f, 0.6f); continue; }
                    f.X = h.Left + f.RelX; f.Y = h.Top - w.HeightAt(h, f.X) - f.Size * 0.18f;
                }
                else if (f.State == 2)
                {
                    if (!w.HaveCursor || w.CurStill < 0.05f) { f.State = 0; f.Heading = -1.5f; f.Seeking = false; continue; }
                    f.X = w.CurX + 4 * S; f.Y = w.CurY - f.Size * 0.12f;
                    perchT += dt;
                    if (perchT > 1.5f) w.Journal.Discover(w, "perch");
                }
                if (f.State == 3 && (f.X < w.Virt.Left - 60 * S || f.X > w.Virt.Right + 60 * S || f.Y < w.Virt.Top - 60 * S)) List.RemoveAt(i);
            }
            if (!Welcome(w)) foreach (var f in List) if (f.State == 0) f.State = 3;
        }

        bool Near(World w, Fly f, float r)
        {
            if (!w.HaveCursor || w.CurSpeed < 40 * w.S) return false;
            float dx = w.CurX - f.X, dy = w.CurY - f.Y;
            return dx * dx + dy * dy < r * r;
        }

        public void Spawn(World w, int species)
        {
            float S = w.S;
            bool left = w.Chance(0.5f);
            var f = new Fly { Species = species, Size = w.Rand(22, 30) * S, Speed = w.Rand(60, 110) * S };
            f.X = left ? w.Virt.Left - 30 * S : w.Virt.Right + 30 * S;
            f.Y = w.Virt.Top + w.Rand(0.25f, 0.8f) * w.Virt.Height;
            f.Heading = left ? w.Rand(-0.4f, 0.4f) : (float)Math.PI + w.Rand(-0.4f, 0.4f);
            List.Add(f);
            w.Journal.Count("butterflies", 1);
        }

        void Wander(World w, Fly f, float dt)
        {
            float S = w.S;
            f.Turn += (w.Rand(-1, 1) * 9 - f.Turn * 1.5f) * dt;
            f.Heading += f.Turn * dt;
            float tx = 0, ty = 0; bool target = false;
            if (f.Seeking && w.HaveCursor && f.State == 0) { tx = w.CurX + 4 * S; ty = w.CurY - f.Size * 0.12f; target = true; }
            else if (f.State == 0 && f.Tx != 0) { tx = f.Tx; ty = f.Ty; target = true; }
            if (target)
            {
                double want = Math.Atan2(ty - f.Y, tx - f.X);
                double diff = Math.Atan2(Math.Sin(want - f.Heading), Math.Cos(want - f.Heading));
                f.Heading += (float)diff * Math.Min(1, dt * 2.5f);
                float d = (float)Math.Sqrt((tx - f.X) * (tx - f.X) + (ty - f.Y) * (ty - f.Y));
                if (d < 6 * S)
                {
                    if (f.Seeking) { f.State = 2; perchT = 0; f.Seeking = false; }
                    else if (f.Host != null && w.Alive(f.Host)) { f.State = 1; f.RelX = f.X - f.Host.Left; f.Rest = w.Rand(6, 20); }
                    f.Tx = 0;
                    return;
                }
            }
            else if (f.State == 0 && w.Chance(dt * 0.08f))
            {
                // Pick a ledge to visit.
                var opts = new List<Ledge>();
                foreach (var e in w.LedgeList) if (!e.Host.IsGround && e.X1 - e.X0 > 40 * S) opts.Add(e);
                if (opts.Count > 0)
                {
                    var e = opts[w.Rng.Next(opts.Count)];
                    f.Host = e.Host; f.Tx = w.Rand(e.X0 + 10 * S, e.X1 - 10 * S); f.Ty = e.Y - w.HeightAt(e.Host, f.Tx) - f.Size * 0.18f;
                }
            }
            // Stay on screen (unless leaving) and away from the very top.
            if (f.State == 0)
            {
                if (f.X < w.Virt.Left + 30 * S) f.Heading = Blend(f.Heading, 0, dt);
                if (f.X > w.Virt.Right - 30 * S) f.Heading = Blend(f.Heading, (float)Math.PI, dt);
                if (f.Y < w.Virt.Top + 0.12f * w.Virt.Height) f.Heading = Blend(f.Heading, (float)Math.PI / 2, dt);
                if (f.Y > w.Virt.Bottom - 0.1f * w.Virt.Height) f.Heading = Blend(f.Heading, -(float)Math.PI / 2, dt);
            }
            else f.Heading = Blend(f.Heading, f.X < (w.Virt.Left + w.Virt.Right) / 2 ? (float)Math.PI + 0.5f : -0.5f, dt);
            float sp = f.Speed * (0.75f + 0.35f * (float)Math.Sin(f.Flap * 0.9f));
            f.X += (float)Math.Cos(f.Heading) * sp * dt + w.Wind * 0.25f * dt;
            f.Y += (float)Math.Sin(f.Heading) * sp * dt + (float)Math.Sin(f.Bob * 5) * 18 * S * dt;
        }

        static float Blend(float heading, float want, float dt)
        {
            double diff = Math.Atan2(Math.Sin(want - heading), Math.Cos(want - heading));
            return heading + (float)diff * Math.Min(1, dt * 2);
        }

        public override bool Trigger(World w, string name)
        {
            if (name == "butterfly") { Spawn(w, w.Rng.Next(4)); return true; }
            if (name == "monarchs") { for (int i = 0; i < 12; i++) Spawn(w, 0); return true; }
            return false;
        }

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            var light = w.Sky.Light;
            using (var m = new Matrix())
                foreach (var f in List)
                {
                    int frame;
                    if (f.State == 1 || f.State == 2) frame = (int)((Math.Sin(f.Flap * 2) * 0.5 + 0.5) * 2.99);
                    else { double ph = (f.Flap % 1 + 1) % 1; frame = ph < 0.25 ? 0 : ph < 0.5 ? 1 : ph < 0.75 ? 3 : 2; }
                    var p = ButterflyArt.Get(f.Species, frame, f.Size);
                    m.Reset();
                    m.Translate(f.X - area.Left, f.Y - area.Top);
                    float deg = f.State == 1 || f.State == 2 ? 0 : f.Heading * 180 / (float)Math.PI + 90;
                    m.Rotate(deg);
                    m.Translate(-p.Cx, -p.Cy);
                    var r = Blit.DrawAffine(s, p, m.Elements, 256, light, false);
                    if (!r.IsEmpty) drawn.Add(r);
                }
        }

        public override bool Quiet { get { return List.Count == 0; } }
    }
}
