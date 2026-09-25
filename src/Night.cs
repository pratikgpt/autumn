using System;
using System.Collections.Generic;
using System.Drawing;

namespace Autumn
{
    // Fireflies on warm nights. They drift, blink in their own rhythms, and if the pointer stays
    // still long enough they gather around it; move and they scatter.
    class Fireflies : Sys
    {
        class Fly { public float X, Y, Vx, Vy, P1, P2, P3, Period, Phase, On; public bool Double; }
        readonly List<Fly> flies = new List<Fly>();
        Pixels halo, core;
        float gatheredT;

        public Fireflies() { Layer = 52; }

        bool InSeason(World w)
        {
            var sky = w.Sky;
            bool warm = sky.Season == Autumn.Season.Summer || (sky.Season == Autumn.Season.Spring && sky.YearPhase > 0.38f) || (sky.Season == Autumn.Season.Autumn && sky.YearPhase < 0.78f);
            return warm && sky.Darkness > 0.55f && w.Weather.Precip < 0.08f && !w.Stopping;
        }

        public override void Step(World w, float dt)
        {
            float S = w.S;
            int target = InSeason(w) ? (int)(18 * Math.Min(1.6f, w.Density)) : 0;
            if (flies.Count < target && w.Chance(dt * 1.5f))
            {
                var f = new Fly();
                f.X = w.Rand(w.Virt.Left, w.Virt.Right);
                f.Y = w.Virt.Top + w.Virt.Height * w.Rand(0.35f, 0.95f);
                f.P1 = w.Rand(0, 100); f.P2 = w.Rand(0, 100); f.P3 = w.Rand(0.6f, 1.4f);
                f.Period = w.Rand(2.2f, 5.5f); f.Phase = w.Rand(0, 1); f.Double = w.Chance(0.3f);
                flies.Add(f);
            }
            bool gather = w.HaveCursor && w.CurStill > 3 && target > 0;
            int near = 0;
            for (int i = flies.Count - 1; i >= 0; i--)
            {
                var f = flies[i];
                f.P1 += dt * 0.35f * f.P3; f.P2 += dt * 0.27f * f.P3;
                float wx = (float)(Math.Sin(f.P1) + 0.5 * Math.Sin(f.P2 * 2.3 + 1)), wy = (float)(Math.Cos(f.P2) + 0.5 * Math.Sin(f.P1 * 1.7));
                float tvx = wx * 38 * S + w.Wind * 0.15f, tvy = wy * 26 * S;
                float dx = w.CurX - f.X, dy = w.CurY - f.Y, d = (float)Math.Sqrt(dx * dx + dy * dy) + 1;
                if (gather && d < 600 * S)
                {
                    // Circle the pointer at a respectful distance.
                    float want = 70 * S * (0.7f + 0.5f * f.P3);
                    float radial = (d - want) / d;
                    tvx = dx * radial * 0.9f + (-dy / d) * 45 * S * (f.P3 > 1 ? 1 : -1) + wx * 12 * S;
                    tvy = dy * radial * 0.9f + (dx / d) * 45 * S * (f.P3 > 1 ? 1 : -1) + wy * 12 * S;
                    if (d < 130 * S) near++;
                }
                if (w.HaveCursor && w.CurSpeed > 900 * S && d < 220 * S) { f.Vx -= dx / d * 400 * S; f.Vy -= dy / d * 400 * S; }
                float k = 1f - (float)Math.Exp(-dt * 1.2f);
                f.Vx += (tvx - f.Vx) * k; f.Vy += (tvy - f.Vy) * k;
                f.X += f.Vx * dt; f.Y += f.Vy * dt;
                // Keep them low and on screen.
                if (f.Y < w.Virt.Top + w.Virt.Height * 0.25f) f.Vy += 30 * S * dt;
                if (f.X < w.Virt.Left - 40 * S) f.X = w.Virt.Right + 30 * S;
                if (f.X > w.Virt.Right + 40 * S) f.X = w.Virt.Left - 30 * S;
                f.Y = Math.Max(w.Virt.Top, Math.Min(w.Virt.Bottom - 5 * S, f.Y));
                f.Phase += dt / f.Period;
                float ph = f.Phase % 1f;
                float on = Pulse(ph, 0.12f);
                if (f.Double) on = Math.Max(on, Pulse(ph, 0.24f));
                f.On = on;
                if (target == 0 && on < 0.02f && w.Chance(dt * 0.5f)) flies.RemoveAt(i);
                else if (flies.Count > target + 2 && on < 0.02f) flies.RemoveAt(i);
            }
            if (near >= 5) { gatheredT += dt; if (gatheredT > 3) w.Journal.Discover(w, "fireflies"); } else gatheredT = 0;
        }

        static float Pulse(float ph, float at)
        {
            float d = (ph - at) / 0.07f;
            return (float)Math.Exp(-d * d);
        }

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            if (flies.Count == 0) return;
            if (halo == null) { halo = Pixels.Dot(15 * w.S, Color.FromArgb(255, 190, 255, 80), 0f); core = Pixels.Dot(2.4f * w.S, Color.FromArgb(255, 250, 255, 200), 0.45f); }
            foreach (var f in flies)
            {
                float a = 0.08f + 0.92f * f.On;
                var r = Blit.DrawAt(s, halo, f.X - area.Left, f.Y - area.Top, 1, (uint)(a * 200), Light.Full, true);
                if (!r.IsEmpty) drawn.Add(r);
                Blit.DrawAt(s, core, f.X - area.Left, f.Y - area.Top, 1, (uint)(Math.Max(0.25f, a) * 256), Light.Full, true);
            }
        }

        public override bool Trigger(World w, string name)
        {
            if (name != "fireflies") return false;
            for (int i = 0; i < 20; i++)
                flies.Add(new Fly { X = w.Rand(w.Virt.Left, w.Virt.Right), Y = w.Virt.Top + w.Virt.Height * w.Rand(0.4f, 0.95f), P1 = w.Rand(0, 100), P2 = w.Rand(0, 100), P3 = w.Rand(0.6f, 1.4f), Period = w.Rand(2.2f, 5.5f), Phase = w.Rand(0, 1) });
            return true;
        }

        public override bool Quiet { get { return flies.Count == 0; } }
    }

    // Shooting stars on clear nights, and meteor showers on the real nights they peak.
    class Meteors : Sys
    {
        class Meteor { public float X, Y, Vx, Vy, Life, Max, Tail; }
        readonly List<Meteor> list = new List<Meteor>();
        float nextT = 60;
        int seenTonight; DateTime night;
        Pixels head;

        // Peak dates and relative strength of the major annual showers.
        static readonly int[][] Showers = {
            new[] { 1, 3, 20 }, new[] { 4, 22, 6 }, new[] { 5, 6, 6 }, new[] { 8, 12, 20 }, new[] { 10, 8, 3 },
            new[] { 10, 21, 7 }, new[] { 11, 17, 6 }, new[] { 12, 14, 22 }, new[] { 12, 22, 3 } };
        static readonly string[] ShowerNames = { "Quadrantids", "Lyrids", "Eta Aquariids", "Perseids", "Draconids", "Orionids", "Leonids", "Geminids", "Ursids" };

        public Meteors() { Layer = 5; }

        // How busy tonight's sky is (1 = ordinary night) and which shower, if any.
        public static float Activity(DateTime local, out string name)
        {
            name = null;
            float best = 1;
            for (int i = 0; i < Showers.Length; i++)
            {
                var peak = new DateTime(local.Year, Showers[i][0], Showers[i][1], 3, 0, 0);
                double days = Math.Abs((local - peak).TotalDays);
                if (days > 180) days = 365 - days;
                float a = 1 + (Showers[i][2] - 1) * (float)Math.Exp(-days * days / (2 * 1.2 * 1.2));
                if (a > best) { best = a; if (a > 2) name = ShowerNames[i]; }
            }
            return best;
        }

        public override void Step(World w, float dt)
        {
            var sky = w.Sky;
            bool clear = w.Weather.Precip < 0.05f && (w.Weather.Kind == WeatherKind.Clear || w.Weather.Kind == WeatherKind.Breezy);
            if (sky.Local.Date != night.Date && sky.Hour > 12) { night = sky.Local.Date; seenTonight = 0; }
            if (sky.Darkness > 0.85f && clear && !w.Stopping)
            {
                string shower;
                float act = Activity(sky.Local, out shower);
                nextT -= dt * act * (sky.Lapsing ? 20 : 1);
                if (nextT <= 0)
                {
                    nextT = w.Rand(300, 1100);
                    Launch(w);
                    seenTonight++;
                    w.Journal.Count("meteors", 1);
                    w.Journal.Discover(w, "shootingstar");
                    if (shower != null && seenTonight >= 5)
                    {
                        if (!w.Journal.Has("meteors")) w.Journal.Log(DateTime.Now, "Meteor shower: the " + shower + ".");
                        w.Journal.Discover(w, "meteors");
                    }
                }
            }
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var m = list[i];
                m.Life += dt;
                m.X += m.Vx * dt; m.Y += m.Vy * dt;
                if (m.Life > m.Max + 0.4f) list.RemoveAt(i);
                else if (m.Life < m.Max && w.Chance(dt * 25))
                {
                    var sp = w.Get<Sparks>();
                    if (sp != null) sp.Emit(w, m.X, m.Y, m.Vx * 0.05f, m.Vy * 0.05f, w.Rand(0.3f, 0.7f), 0.5f, Color.FromArgb(255, 200, 220, 255), 40, 1);
                }
            }
        }

        void Launch(World w)
        {
            float S = w.S;
            var m = new Meteor();
            m.X = w.Rand(w.Virt.Left + 0.1f * w.Virt.Width, w.Virt.Right - 0.1f * w.Virt.Width);
            m.Y = w.Virt.Top + w.Rand(0.02f, 0.3f) * w.Virt.Height;
            float dir = w.Chance(0.5f) ? 1 : -1;
            float sp = w.Rand(1300, 2300) * S;
            float ang = w.Rand(0.3f, 0.75f);
            m.Vx = dir * sp * (float)Math.Cos(ang); m.Vy = sp * (float)Math.Sin(ang);
            m.Max = w.Rand(0.45f, 1.0f);
            m.Tail = w.Rand(160, 300) * S;
            list.Add(m);
            if (w.Sound != null) w.Sound.Play("shimmer", 0.35f, dir * 0.4f);
        }

        public override bool Trigger(World w, string name)
        {
            if (name != "meteor") return false;
            Launch(w); w.Journal.Discover(w, "shootingstar");
            return true;
        }

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            foreach (var m in list)
            {
                float sp = (float)Math.Sqrt(m.Vx * m.Vx + m.Vy * m.Vy);
                float fade = m.Life < 0.1f ? m.Life / 0.1f : m.Life > m.Max ? Math.Max(0, 1 - (m.Life - m.Max) / 0.4f) : 1;
                float grow = Math.Min(1, m.Life / 0.25f);
                float tail = m.Tail * grow;
                float hx = m.X - area.Left, hy = m.Y - area.Top;
                float tx = hx - m.Vx / sp * tail, ty = hy - m.Vy / sp * tail;
                var glow = Color.FromArgb(255, 150, 180, 255);
                for (int o = 1; o <= 2; o++)
                {
                    Blit.Line(s, tx, ty - o * w.S, hx, hy - o * w.S, glow, 0, 0.3f / o * fade, true);
                    Blit.Line(s, tx, ty + o * w.S, hx, hy + o * w.S, glow, 0, 0.3f / o * fade, true);
                }
                Blit.Line(s, tx + 0.5f, ty, hx + 0.5f, hy, Color.White, 0, fade, true);
                var r = Blit.Line(s, tx, ty, hx, hy, Color.White, 0, fade, true);
                if (head == null) head = Pixels.Dot(6 * w.S, Color.FromArgb(255, 220, 230, 255), 0.1f);
                Blit.DrawAt(s, head, hx, hy, 1, (uint)(fade * 230), Light.Full, true);
                if (!r.IsEmpty) { r.Inflate((int)(8 * w.S), (int)(8 * w.S)); drawn.Add(r); }
            }
        }

        public override bool Quiet { get { return list.Count == 0; } }
    }

    // Northern lights: rare, on cold clear winter nights, green curtains with violet tops.
    class Aurora : Sys
    {
        float level, t, showLeft;
        uint[] lut;
        int decidedFor = -1;
        bool tonight;
        float startHour;

        public Aurora() { Layer = 2; }

        public override void Step(World w, float dt)
        {
            var sky = w.Sky;
            int key = sky.Local.Year * 1000 + sky.Local.DayOfYear - (sky.Hour < 12 ? 1 : 0);
            if (key != decidedFor)
            {
                decidedFor = key;
                var r = new Random(key * 7919);
                tonight = r.NextDouble() < 0.12;
                startHour = 22 + (float)r.NextDouble() * 5;
            }
            bool clear = w.Weather.Precip < 0.05f && w.Weather.Kind != WeatherKind.Storm;
            float h = sky.Hour < 12 ? sky.Hour + 24 : sky.Hour;
            bool due = tonight && sky.Season == Season.Winter && sky.Darkness > 0.9f && clear && h >= startHour && h < startHour + 0.4f;
            if (showLeft > 0) { showLeft -= dt; due = true; }
            float want = due && !w.Stopping ? 1 : 0;
            level += (want - level) * Math.Min(1, dt / 25f * (w.Stopping ? 8 : 1));
            t += dt;
            if (level > 0.3f) w.Journal.Discover(w, "aurora");
        }

        public override bool Trigger(World w, string name)
        {
            if (name != "aurora") return false;
            showLeft = 240; level = Math.Max(level, 0.05f);
            return true;
        }

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            if (level < 0.01f) return;
            float H = w.Virt.Height;
            if (lut == null)
            {
                // Colour and brightness up the curtain: green at the lower edge, violet at the top.
                lut = new uint[135];
                for (int i = 0; i < lut.Length; i++)
                {
                    double v = i / 100.0 - 0.34;
                    double a = (v < 0 ? Math.Max(0, 1 + v * 3) : Math.Pow(1 - Math.Min(1, v), 1.4)) * 0.4;
                    double vc = Math.Max(0, v);
                    int g = (int)(255 * (1 - vc * 0.55)), r = (int)(90 + 140 * vc), b = (int)(150 + 105 * vc);
                    lut[i] = Pixels.Premul(Color.FromArgb(255, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b)), (float)a);
                }
            }
            int step = Math.Max(1, (int)w.S);
            int minY = int.MaxValue, maxY = 0;
            for (int x = area.Left; x < area.Right; x += step)
            {
                float u = x - w.Virt.Left;
                double bottom = H * (0.2 + 0.05 * Math.Sin(u * 0.0035 + t * 0.06) + 0.03 * Math.Sin(u * 0.011 - t * 0.11) + 0.015 * Math.Sin(u * 0.03 + t * 0.4));
                double rays = (0.55 + 0.45 * Math.Sin(u * 0.045 + t * 0.9 + 3 * Math.Sin(u * 0.004 + t * 0.05))) * (0.65 + 0.35 * Math.Sin(u * 0.13 + t * 1.7));
                double sheet = 0.5 + 0.5 * Math.Sin(u * 0.0021 - t * 0.045) * Math.Sin(u * 0.0057 + t * 0.07);
                double inten = level * rays * (0.25 + 0.75 * sheet);
                if (inten < 0.02) continue;
                float height = (float)(H * (0.1 + 0.06 * sheet));
                float yb = (float)(w.Virt.Top + bottom) - area.Top;
                int yt = (int)(yb - height);
                Blit.AddColumn(s, x - area.Left, step, yt, (int)yb + 4, yb, height, lut, 100, 0.34f, (uint)(Math.Min(1, inten) * 256));
                minY = Math.Min(minY, Math.Max(0, yt)); maxY = Math.Max(maxY, (int)yb + 4);
            }
            if (minY < maxY) drawn.Add(Rectangle.FromLTRB(0, minY, area.Width, Math.Min(area.Height, maxY)));
        }

        public override bool Quiet { get { return level < 0.01f; } }
    }
}
