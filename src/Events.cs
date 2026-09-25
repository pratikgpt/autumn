using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Autumn
{
    // Rare moments: rainbows after a daytime shower, golden-leaf glints, a snowman built while you
    // were away, leaves that briefly gather into a heart, and the whole year in a minute.
    class Events : Sys
    {
        // rainbow
        float rainbowT = -1, rainbowCool, rbCx, rbCy, rbR;
        bool rainEndHandled = true;
        // snowman
        public bool SnowmanUp; public float SnowmanRelX, SnowmanFall; public WinInfo SnowmanHost;
        Pixels snowmanArt;
        double idleWas;
        // heart
        float heartT = -1, heartCool = 2400;
        readonly List<Leaf> heart = new List<Leaf>();
        readonly List<PointF> heartPts = new List<PointF>();
        // the year
        float lapseDays;
        int lapseHourWas;

        public Events() { Layer = 21; }

        public override void Step(World w, float dt)
        {
            float S = w.S;
            Rainbow(w, dt);
            Glints(w, dt);
            Snowman(w, dt);
            Heart(w, dt);
            Year(w, dt);
        }

        // ---------------------------------------------------------------- rainbow

        void Rainbow(World w, float dt)
        {
            rainbowCool -= dt;
            var wx = w.Weather;
            if (wx.Precip > 0.1f) rainEndHandled = false;
            bool justStopped = !rainEndHandled && wx.Precip < 0.05f && wx.SinceRain > 4;
            if (justStopped)
            {
                rainEndHandled = true;
                if (!wx.Snowy(w.Sky) && w.Sky.SunElev > 4 && w.Sky.SunElev < 42 && rainbowCool <= 0 && w.Chance(0.6f)) StartRainbow(w);
            }
            if (rainbowT >= 0) { rainbowT += dt; if (rainbowT > 70 || w.Stopping) rainbowT = -1; }
        }

        void StartRainbow(World w)
        {
            rainbowT = 0; rainbowCool = 1800;
            rbR = w.Virt.Width * w.Rand(0.55f, 0.8f);
            rbCx = w.Virt.Left + w.Virt.Width * w.Rand(0.3f, 0.7f);
            rbCy = w.Virt.Bottom + rbR * w.Rand(0.25f, 0.45f);
            w.Journal.Discover(w, "rainbow");
            w.Journal.Count("rainbows", 1);
        }

        static readonly Color[] Bands = {
            Color.FromArgb(255, 255, 60, 50), Color.FromArgb(255, 255, 150, 40), Color.FromArgb(255, 255, 235, 60),
            Color.FromArgb(255, 70, 220, 90), Color.FromArgb(255, 60, 140, 255), Color.FromArgb(255, 90, 70, 220), Color.FromArgb(255, 150, 70, 210) };

        void PaintRainbow(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            if (rainbowT < 0) return;
            float fade = rainbowT < 8 ? rainbowT / 8 : rainbowT > 58 ? Math.Max(0, (70 - rainbowT) / 12) : 1;
            float bw = 7 * w.S;
            float outer = rbR, inner = rbR - bw * Bands.Length;
            int minY = int.MaxValue, maxY = int.MinValue;
            for (int x = area.Left; x < area.Right; x++)
            {
                float dx = x - rbCx;
                if (Math.Abs(dx) >= outer) continue;
                for (int b = 0; b < Bands.Length; b++)
                {
                    float r0 = outer - b * bw, r1 = r0 - bw;
                    float y0 = rbCy - (float)Math.Sqrt(r0 * r0 - dx * dx);
                    float y1 = Math.Abs(dx) < r1 ? rbCy - (float)Math.Sqrt(r1 * r1 - dx * dx) : rbCy;
                    // soft outer and inner edges
                    float edge = b == 0 || b == Bands.Length - 1 ? 0.55f : 1f;
                    float heightFade = Math.Min(1, Math.Max(0, (w.Virt.Bottom - y0) / (w.Virt.Height * 0.5f)));
                    uint c = Pixels.Premul(Bands[b], 0.17f * fade * edge * heightFade);
                    int ya = (int)y0 - area.Top, yb = (int)Math.Min(y1, w.Virt.Bottom) - area.Top;
                    if (yb <= ya) yb = ya + 1;
                    Blit.Span(s, x - area.Left, ya, yb, c, true);
                    minY = Math.Min(minY, ya); maxY = Math.Max(maxY, yb);
                }
            }
            if (minY < maxY) drawn.Add(Rectangle.FromLTRB(0, Math.Max(0, minY), area.Width, Math.Min(area.Height, maxY)));
        }

        // ---------------------------------------------------------------- golden glints

        void Glints(World w, float dt)
        {
            var sp = w.Get<Sparks>();
            if (sp == null) return;
            foreach (var l in w.Leaves)
                if (l.Golden && w.Chance(dt * (l.Resting ? 1.5f : 6)))
                {
                    var g = sp.Emit(w, l.X + w.Rand(-0.4f, 0.4f) * l.Look.Size, l.Y + w.Rand(-0.4f, 0.4f) * l.Look.Size, 0, -20 * w.S, w.Rand(0.4f, 0.9f), 0.45f, Color.FromArgb(255, 255, 225, 120), 0, 1);
                    if (g != null) g.Twinkle = true;
                }
        }

        // ---------------------------------------------------------------- snowman

        void Snowman(World w, float dt)
        {
            float S = w.S;
            var snow = w.Get<Snow>();
            double idle = w.Idle;
            // Coming back after a long absence to a snowy taskbar: someone built a snowman.
            if (idleWas > 600 && idle < 5 && !SnowmanUp && snow != null)
            {
                foreach (var h in w.Wins)
                    if (h.IsTaskbar)
                    {
                        float x = w.Rand(h.Left + 0.15f * h.Width, h.Right - 0.15f * h.Width);
                        if (snow.DepthAt(h, x) > 7 * S) { Build(w, h, x - h.Left); }
                    }
            }
            idleWas = idle;
            if (!SnowmanUp) return;
            if (!w.Alive(SnowmanHost) || snow == null) { SnowmanUp = false; return; }
            float sx = SnowmanHost.Left + SnowmanRelX;
            if (SnowmanFall > 0)
            {
                SnowmanFall += dt;
                if (SnowmanFall > 0.5f) SnowmanUp = false;
                return;
            }
            // Knocked over by a fast sweep, or melted away.
            if (w.HaveCursor && w.CurSpeed > 700 * S && Math.Abs(w.CurX - sx) < 30 * S && w.CurY > SnowmanHost.Top - 70 * S && w.CurY < SnowmanHost.Top + 10 * S)
            {
                SnowmanFall = 0.01f;
                snow.Burst(w, sx, SnowmanHost.Top - 30 * S, 22, w.CurVx * 0.4f);
                if (w.Sound != null) w.Sound.Play("snowcrunch", 1f, 0);
            }
            if (snow.DepthAt(SnowmanHost, sx) < 2 * S && w.Sky.Temperature > 2) SnowmanUp = false;
        }

        public void Build(World w, WinInfo host, float relX)
        {
            SnowmanUp = true; SnowmanHost = host; SnowmanRelX = relX; SnowmanFall = 0;
            w.Journal.Discover(w, "snowman");
        }

        static Pixels SnowmanArt(float size)
        {
            int wpx = (int)(size * 1.3f), hpx = (int)(size * 1.25f);
            using (var b = new Bitmap(wpx, hpx, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TranslateTransform(wpx / 2f, hpx - 2);
                    g.ScaleTransform(size, size);
                    using (var stick = new Pen(LeafLook.Hex("5A3E26"), 0.035f))
                    {
                        stick.StartCap = LineCap.Round; stick.EndCap = LineCap.Round;
                        g.DrawLine(stick, -0.16f, -0.6f, -0.52f, -0.78f); g.DrawLine(stick, -0.42f, -0.73f, -0.48f, -0.88f);
                        g.DrawLine(stick, 0.16f, -0.6f, 0.5f, -0.8f); g.DrawLine(stick, 0.4f, -0.74f, 0.52f, -0.7f);
                    }
                    foreach (var ball in new[] { new[] { 0f, -0.2f, 0.24f }, new[] { 0f, -0.55f, 0.18f }, new[] { 0f, -0.84f, 0.13f } })
                        using (var br = new PathGradientBrush(Circle(ball[0], ball[1], ball[2])))
                        {
                            br.CenterPoint = new PointF(ball[0] - ball[2] * 0.35f, ball[1] - ball[2] * 0.35f);
                            br.CenterColor = Color.White; br.SurroundColors = new[] { LeafLook.Hex("CAD6E6") };
                            g.FillEllipse(br, ball[0] - ball[2], ball[1] - ball[2], ball[2] * 2, ball[2] * 2);
                        }
                    using (var coal = new SolidBrush(LeafLook.Hex("202020")))
                    {
                        g.FillEllipse(coal, -0.06f, -0.9f, 0.035f, 0.035f); g.FillEllipse(coal, 0.03f, -0.9f, 0.035f, 0.035f);
                        for (int k = 0; k < 3; k++) g.FillEllipse(coal, -0.018f, -0.64f + k * 0.08f, 0.036f, 0.036f);
                        for (int k = 0; k < 5; k++) g.FillEllipse(coal, -0.07f + k * 0.035f, -0.8f + (float)Math.Abs(k - 2) * -0.008f, 0.018f, 0.018f);
                    }
                    using (var carrot = new SolidBrush(LeafLook.Hex("EE7A22")))
                        g.FillPolygon(carrot, new[] { new PointF(0, -0.87f), new PointF(0.2f, -0.845f), new PointF(0, -0.83f) });
                    using (var scarf = new SolidBrush(LeafLook.Hex("C8323A")))
                    {
                        g.FillRectangle(scarf, -0.14f, -0.73f, 0.28f, 0.05f);
                        g.FillPolygon(scarf, new[] { new PointF(0.06f, -0.7f), new PointF(0.12f, -0.7f), new PointF(0.16f, -0.55f), new PointF(0.1f, -0.55f) });
                    }
                    using (var hat = new SolidBrush(LeafLook.Hex("2A2A30")))
                    {
                        g.FillRectangle(hat, -0.14f, -0.98f, 0.28f, 0.03f);
                        g.FillRectangle(hat, -0.09f, -1.12f, 0.18f, 0.15f);
                    }
                }
                return Pixels.From(b, wpx / 2f, hpx - 2);
            }
        }

        static GraphicsPath Circle(float x, float y, float r) { var p = new GraphicsPath(); p.AddEllipse(x - r, y - r, r * 2, r * 2); return p; }

        // ---------------------------------------------------------------- the heart

        void Heart(World w, float dt)
        {
            float S = w.S;
            heartCool -= dt;
            bool season = w.Sky.Season == Season.Autumn || w.Sky.Season == Season.Spring;
            if (heartT < 0 && heartCool <= 0 && season && w.Weather.Precip < 0.05f && !w.Stopping && w.Idle < 30)
            {
                heartCool = w.Rand(3600, 7200);
                StartHeart(w);
            }
            if (heartT < 0) return;
            heartT += dt;
            float gather = 3.2f, hold = 2.2f;
            for (int i = 0; i < heart.Count; i++)
            {
                var l = heart[i];
                if (!w.Leaves.Contains(l) || l.Resting || l.OnCursor) continue;
                var p = heartPts[i];
                float wob = (float)Math.Sin(w.Time * 3 + i) * 3 * S;
                if (heartT < gather + hold)
                {
                    float k = heartT < gather ? 2.2f : 6f;
                    float tvx = (p.X + wob - l.X) * k, tvy = (p.Y - l.Y) * k - l.Fall * (0.45f + 1.1f * 0.5f);
                    tvx -= l.WindVx + l.SwayAmp * (float)Math.Cos(l.Phase);
                    float kk = 1f - (float)Math.Exp(-dt * 5);
                    l.Ix += (tvx - l.Ix) * kk; l.Iy += (tvy - l.Iy) * kk;
                }
            }
            if (heartT >= gather + hold && heartT < gather + hold + dt * 1.5f)
            {
                // Let go: a soft outward burst.
                float cx = 0, cy = 0; foreach (var p in heartPts) { cx += p.X; cy += p.Y; } cx /= heartPts.Count; cy /= heartPts.Count;
                foreach (var l in heart) if (w.Leaves.Contains(l) && !l.Resting) { l.Ix = (l.X - cx) * 3; l.Iy = (l.Y - cy) * 3 - 100 * S; l.Tumbler = true; }
                w.Journal.Discover(w, "heart");
                if (w.Sound != null) w.Sound.Play("chime", 0.5f, 0);
            }
            if (heartT > gather + hold + 3) { heartT = -1; heart.Clear(); heartPts.Clear(); }
        }

        public void StartHeart(World w)
        {
            float S = w.S;
            heart.Clear(); heartPts.Clear();
            int n = 34;
            float cx = w.Virt.Left + w.Virt.Width * 0.5f, cy = w.Virt.Top + w.Virt.Height * 0.4f, sc = 9.5f * S;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)n * Math.PI * 2;
                double x = 16 * Math.Pow(Math.Sin(t), 3), y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                heartPts.Add(new PointF(cx + (float)x * sc, cy - (float)y * sc));
                // Leaves swirl in from the upwind edge.
                var l = w.NewLeaf(w.Wind >= 0 ? w.Virt.Left - w.Rand(20, 300) * S : w.Virt.Right + w.Rand(20, 300) * S, w.Virt.Top + w.Rand(0.05f, 0.7f) * w.Virt.Height);
                l.NoLandUntil = w.Time + 9;
                w.Leaves.Add(l);
                heart.Add(l);
            }
            heartT = 0;
            if (w.Sound != null) w.Sound.Play("whoosh", 0.8f, 0);
        }

        // ---------------------------------------------------------------- the year in a minute

        void Year(World w, float dt)
        {
            if (!w.Sky.Lapsing) return;
            lapseDays = (float)w.Sky.LapseDays;
            if (lapseDays >= 365)
            {
                w.Sky.StopLapse();
                w.Sky.HourOverride = lapseHourWas;
                w.Journal.Discover(w, "year");
                w.Journal.Log(DateTime.Now, "A whole year went by in a minute.");
            }
        }

        public void StartYear(World w)
        {
            if (w.Sky.Lapsing) return;
            lapseHourWas = (int)w.Sky.HourOverride;
            w.Sky.HourOverride = 13;           // hold the time of day: no strobing day/night
            w.Sky.SeasonOverride = -1;
            w.Sky.StartLapse(365.0 / 64.0);
            if (w.Sound != null) w.Sound.Play("chime", 0.8f, 0);
        }

        public override bool Trigger(World w, string name)
        {
            switch (name)
            {
                case "rainbow": StartRainbow(w); return true;
                case "heart": StartHeart(w); return true;
                case "year": StartYear(w); return true;
                case "snowman":
                    foreach (var h in w.Wins) if (h.IsTaskbar) { Build(w, h, h.Width * 0.3f); return true; }
                    return false;
            }
            return false;
        }

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            PaintRainbow(s, w, area, drawn);
            if (SnowmanUp && SnowmanHost != null)
            {
                if (snowmanArt == null) snowmanArt = SnowmanArt(52 * w.S);
                var snow = w.Get<Snow>();
                float x = SnowmanHost.Left + SnowmanRelX;
                float y = SnowmanHost.Top - (snow != null ? snow.DepthAt(SnowmanHost, x) : 0) + 2 * w.S;
                uint a = SnowmanFall > 0 ? (uint)(Math.Max(0, 1 - SnowmanFall * 2) * 256) : 256;
                var light = Light.From(0.6f + 0.4f * w.Sky.Light.R / 256f, 0.62f + 0.38f * w.Sky.Light.G / 256f, 0.7f + 0.3f * w.Sky.Light.B / 256f);
                var r = Blit.Draw(s, snowmanArt, (int)(x - area.Left - snowmanArt.Cx), (int)(y - area.Top - snowmanArt.Cy), a, light);
                if (!r.IsEmpty) drawn.Add(r);
            }
        }

        public override bool Quiet { get { return rainbowT < 0 && !SnowmanUp; } }
    }
}
