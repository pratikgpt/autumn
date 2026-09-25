using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Autumn
{
    // Nights the desktop celebrates: Diwali (the new moon of Kartika: diyas along the taskbar and
    // gentle fireworks), New Year's midnight fireworks, Halloween bats at dusk, Christmas lights.
    class Festivals : Sys
    {
        class Rocket { public float X, Y, Vx, Vy, Burst; public Color C; public int Kind; }
        class Bat { public float X, Y, Vx, Vy, Flap, Size; }
        readonly List<Rocket> rockets = new List<Rocket>();
        readonly List<Bat> bats = new List<Bat>();
        float rocketT = 3, batT;
        string today = "";
        string forced;
        float forcedLeft;
        Pixels diya, flame, bulb;
        Pixels[] batFrames;

        public Festivals() { Layer = 46; }

        // Which celebration (if any) is on right now, from the local calendar and the moon.
        public string Current(World w)
        {
            if (forcedLeft > 0) return forced;
            var t = w.Sky.Local;
            bool evening = w.Sky.SunElev < 2 || t.Hour >= 18 || t.Hour < 1;
            if ((t.Month == 12 && t.Day == 31 && t.Hour >= 23 && t.Minute >= 58) || (t.Month == 1 && t.Day == 1 && t.Hour == 0 && t.Minute < 12)) return "newyear";
            if (t.Month == 10 && t.Day == 31 && evening) return "halloween";
            if (t.Month == 12 && (t.Day == 24 || t.Day == 25) && w.Sky.SunElev < 0) return "christmas";
            if (evening && IsDiwali(w, t)) return "diwali";
            return null;
        }

        // Diwali falls on the new moon (amavasya) between mid-October and mid-November.
        static bool IsDiwali(World w, DateTime t)
        {
            if (TimeZoneInfo.Local.Id != "India Standard Time" && TimeZoneInfo.Local.Id != "Nepal Standard Time" && TimeZoneInfo.Local.Id != "Sri Lanka Standard Time") return false;
            var start = new DateTime(t.Year, 10, 12);
            double jd0 = Sky.Julian(start.ToUniversalTime());
            double lun = (jd0 - 2451550.1) / 29.530588853;
            double next = Math.Ceiling(lun);
            DateTime newMoon = DateTime.FromOADate(2451550.1 + next * 29.530588853 - 2415018.5).ToLocalTime();
            if (newMoon.Month == 11 && newMoon.Day > 16) return false;
            double days = (t - newMoon).TotalDays;
            return days > -1.6 && days < 1.4;
        }

        public override void Step(World w, float dt)
        {
            float S = w.S;
            if (forcedLeft > 0) forcedLeft -= dt;
            string fest = w.Stopping ? null : Current(w);
            if (fest != null && fest != today)
            {
                today = fest;
                string msg = fest == "diwali" ? "Happy Diwali. The desktop lit its lamps." : fest == "newyear" ? "Happy New Year!" : fest == "halloween" ? "Bats at dusk. Happy Halloween." : "Merry Christmas.";
                w.Journal.Log(DateTime.Now, msg);
                w.Journal.Discover(w, "festival");
            }
            if (fest == null) today = "";
            if (fest == "diwali" || fest == "newyear")
            {
                rocketT -= dt;
                if (rocketT <= 0)
                {
                    rocketT = fest == "newyear" ? w.Rand(0.5f, 1.8f) : w.Rand(5, 14);
                    Launch(w);
                }
            }
            if (w.Sound != null) w.Sound.Ambience("hearth", fest == "diwali" ? 0.25f : 0);
            for (int i = rockets.Count - 1; i >= 0; i--)
            {
                var r = rockets[i];
                r.Vy += 320 * S * dt;
                r.X += r.Vx * dt; r.Y += r.Vy * dt;
                var sp = w.Get<Sparks>();
                if (sp != null && w.Chance(dt * 60)) sp.Emit(w, r.X, r.Y, w.Rand(-20, 20) * S, w.Rand(20, 60) * S, w.Rand(0.2f, 0.5f), 0.35f, Color.FromArgb(255, 255, 190, 120), 80, 2);
                if (r.Vy > -r.Burst)
                {
                    Explode(w, r);
                    rockets.RemoveAt(i);
                }
            }
            // Bats on Halloween dusk.
            if (fest == "halloween" && w.Sky.SunElev < 3)
            {
                batT -= dt;
                if (batT <= 0 && bats.Count < 7) { batT = w.Rand(0.8f, 4); bats.Add(new Bat { X = w.Chance(0.5f) ? w.Virt.Left - 20 * S : w.Virt.Right + 20 * S, Y = w.Virt.Top + w.Rand(0.05f, 0.45f) * w.Virt.Height, Size = w.Rand(18, 26) * S }); bats[bats.Count - 1].Vx = bats[bats.Count - 1].X < w.Virt.Left ? 160 * S : -160 * S; }
            }
            for (int i = bats.Count - 1; i >= 0; i--)
            {
                var b = bats[i];
                b.Flap += dt * 9;
                b.Vx += w.Rand(-600, 600) * S * dt; b.Vy += w.Rand(-600, 600) * S * dt;
                b.Vx = Math.Max(-260 * S, Math.Min(260 * S, b.Vx)); b.Vy = Math.Max(-120 * S, Math.Min(120 * S, b.Vy)) * 0.98f;
                if (Math.Abs(b.Vx) < 60 * S) b.Vx = Math.Sign(b.Vx + 0.001f) * 60 * S;
                b.X += b.Vx * dt; b.Y += b.Vy * dt;
                if (b.X < w.Virt.Left - 60 * S || b.X > w.Virt.Right + 60 * S) bats.RemoveAt(i);
            }
        }

        static readonly Color[] Palette = {
            Color.FromArgb(255, 255, 210, 90), Color.FromArgb(255, 255, 90, 80), Color.FromArgb(255, 110, 255, 140),
            Color.FromArgb(255, 110, 170, 255), Color.FromArgb(255, 220, 120, 255), Color.FromArgb(255, 255, 255, 240) };

        void Launch(World w)
        {
            float S = w.S;
            var r = new Rocket();
            r.X = w.Rand(w.Virt.Left + 0.1f * w.Virt.Width, w.Virt.Right - 0.1f * w.Virt.Width);
            r.Y = w.Virt.Bottom - 40 * S;
            r.Vx = w.Rand(-60, 60) * S;
            // Launch just fast enough to burst in the upper part of the screen (v^2 = 2 g h).
            float burstY = w.Virt.Top + w.Virt.Height * w.Rand(0.12f, 0.42f);
            r.Burst = w.Rand(30, 90) * S;
            r.Vy = -(float)Math.Sqrt(2 * 320 * S * (r.Y - burstY) + r.Burst * r.Burst);
            r.C = Palette[w.Rng.Next(Palette.Length)];
            r.Kind = w.Rng.Next(3);
            rockets.Add(r);
            if (w.Sound != null) w.Sound.Play("whistle", 0.5f, Pan(w, r.X));
        }

        void Explode(World w, Rocket r)
        {
            var sp = w.Get<Sparks>();
            if (sp == null) return;
            float S = w.S;
            int n = r.Kind == 2 ? 70 : 110;
            for (int i = 0; i < n; i++)
            {
                double a = i / (double)n * Math.PI * 2 + w.Rand(-0.05f, 0.05f);
                float speed = (r.Kind == 1 ? w.Rand(0.85f, 1f) : w.Rand(0.3f, 1f)) * 340 * S;
                var c = r.Kind == 1 && i % 2 == 0 ? Palette[(Array.IndexOf(Palette, r.C) + 2) % Palette.Length] : r.C;
                var s = sp.Emit(w, r.X, r.Y, (float)Math.Cos(a) * speed, (float)Math.Sin(a) * speed, w.Rand(1.1f, 1.9f) * (r.Kind == 2 ? 1.6f : 1), r.Kind == 2 ? 0.5f : 0.7f, c, r.Kind == 2 ? 120 : 200, r.Kind == 2 ? 2.2f : 1.3f);
                if (s != null) { s.Twinkle = r.Kind == 2 || w.Chance(0.2f); s.Streak = true; s.Size *= 1.3f; }
            }
            if (w.Sound != null)
            {
                w.Sound.Play("pop", w.Rand(0.5f, 0.9f), Pan(w, r.X));
                if (r.Kind == 2) w.Sound.Play("crackle", 0.6f, Pan(w, r.X));
            }
        }

        static float Pan(World w, float x) { return Math.Max(-1, Math.Min(1, (x - w.Virt.Left) / Math.Max(1f, w.Virt.Width) * 2 - 1)); }

        public override bool Trigger(World w, string name)
        {
            if (name == "diwali" || name == "newyear" || name == "halloween" || name == "christmas")
            {
                forced = name; forcedLeft = 120;
                if (name != "halloween" && name != "christmas") for (int i = 0; i < 3; i++) Launch(w);
                return true;
            }
            if (name == "fireworks") { for (int i = 0; i < 4; i++) Launch(w); return true; }
            return false;
        }

        // ---------------------------------------------------------------- art

        void EnsureArt(World w)
        {
            if (diya != null) return;
            float S = w.S;
            int wd = (int)(22 * S), ht = (int)(12 * S);
            using (var b = new Bitmap(wd + 4, ht + 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var bowl = new GraphicsPath();
                    bowl.AddArc(2, -ht * 0.6f + 2, wd, ht * 1.6f, 0, 180);
                    bowl.CloseFigure();
                    using (var br = new LinearGradientBrush(new PointF(0, 2), new PointF(0, ht + 2), LeafLook.Hex("D0702E"), LeafLook.Hex("7A3514"))) g.FillPath(br, bowl);
                    using (var pen = new Pen(LeafLook.Hex("F2B24A"), Math.Max(1, S))) g.DrawArc(pen, 2 + wd * 0.2f, 2 + ht * 0.3f, wd * 0.6f, ht * 0.5f, 20, 140);
                    using (var oil = new SolidBrush(LeafLook.Hex("5A2A10"))) g.FillEllipse(oil, 2 + wd * 0.15f, 1, wd * 0.7f, ht * 0.3f);
                }
                diya = Pixels.From(b, (wd + 4) / 2f, ht + 2);
            }
            flame = Pixels.Dot(7 * S, Color.FromArgb(255, 255, 170, 60), 0.1f);
            bulb = Pixels.Dot(3.2f * S, Color.White, 0.5f);
            batFrames = new Pixels[3];
            for (int f = 0; f < 3; f++)
            {
                int sz = (int)(28 * S);
                using (var b = new Bitmap(sz, sz, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(b))
                    using (var br = new SolidBrush(Color.FromArgb(240, 24, 20, 28)))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TranslateTransform(sz / 2f, sz / 2f);
                        g.ScaleTransform(sz / 2.4f, sz / 2.4f);
                        float up = f == 0 ? -0.7f : f == 1 ? 0 : 0.5f;
                        foreach (float side in new[] { -1f, 1f })
                            g.FillPolygon(br, new[] { new PointF(0, -0.05f), new PointF(side * 0.45f, up - 0.1f), new PointF(side * 1.0f, up + 0.05f), new PointF(side * 0.8f, up * 0.4f + 0.2f), new PointF(side * 0.55f, up * 0.3f + 0.12f), new PointF(side * 0.35f, up * 0.2f + 0.2f), new PointF(side * 0.1f, 0.15f) });
                        g.FillEllipse(br, -0.14f, -0.2f, 0.28f, 0.42f);
                        g.FillPolygon(br, new[] { new PointF(-0.12f, -0.14f), new PointF(-0.08f, -0.34f), new PointF(-0.02f, -0.18f) });
                        g.FillPolygon(br, new[] { new PointF(0.12f, -0.14f), new PointF(0.08f, -0.34f), new PointF(0.02f, -0.18f) });
                    }
                    batFrames[f] = Pixels.From(b, sz / 2f, sz / 2f);
                }
            }
        }

        public override void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            string fest = w.Stopping ? null : Current(w);
            if (fest == null && bats.Count == 0) return;
            EnsureArt(w);
            float S = w.S;
            WinInfo tb = null;
            foreach (var h in w.Wins) if (h.IsTaskbar) tb = h;
            if (fest == "diwali" && tb != null)
            {
                float spacing = 150 * S;
                for (float x = tb.Left + spacing / 2; x < tb.Right; x += spacing)
                {
                    float y = tb.Top - w.HeightAt(tb, x) + 6 * S;
                    var r = Blit.Draw(s, diya, (int)(x - area.Left - diya.Cx), (int)(y - area.Top - diya.Cy), 256, w.Sky.Light);
                    if (!r.IsEmpty) drawn.Add(r);
                    float flick = 0.8f + 0.2f * (float)Math.Sin(w.Time * 13 + x) * (float)Math.Sin(w.Time * 7.3 + x * 0.3);
                    float fy = y - diya.Cy - 3 * S;
                    var r2 = Blit.DrawAt(s, flame, x - area.Left, fy - area.Top, 0.9f * flick, (uint)(200 * flick), Light.Full, true);
                    Blit.DrawAt(s, flame, x - area.Left, fy - 2 * S - area.Top, 0.35f * flick, 255, Light.From(1, 0.95f, 0.8f), true);
                    if (!r2.IsEmpty) drawn.Add(r2);
                }
            }
            if (fest == "christmas" && tb != null)
            {
                // A sagging string of bulbs along the taskbar edge.
                float hook = 200 * S, step = 28 * S;
                Color[] cols = { Color.FromArgb(255, 255, 70, 60), Color.FromArgb(255, 80, 230, 110), Color.FromArgb(255, 255, 200, 60), Color.FromArgb(255, 90, 150, 255) };
                int k = 0;
                for (float x = tb.Left + step / 2; x < tb.Right; x += step, k++)
                {
                    float u = ((x - tb.Left) % hook) / hook;
                    float y = tb.Top - 6 * S + (float)Math.Sin(Math.PI * u) * 7 * S;
                    float tw = 0.55f + 0.45f * (float)Math.Sin(w.Time * (1.3f + (k % 5) * 0.4f) + k * 1.7f);
                    var c = cols[k % cols.Length];
                    var r = Blit.DrawAt(s, flame, x - area.Left, y - area.Top, 0.7f, (uint)(160 * tw), Light.From(c.R / 255f, c.G / 255f, c.B / 255f), true);
                    Blit.DrawAt(s, bulb, x - area.Left, y - area.Top, 0.8f, 255, Light.From(c.R / 255f * 0.8f + 0.2f, c.G / 255f * 0.8f + 0.2f, c.B / 255f * 0.8f + 0.2f), false);
                    if (!r.IsEmpty) drawn.Add(r);
                }
            }
            float[] m = new float[6];
            foreach (var b in bats)
            {
                var p = batFrames[((int)b.Flap) % 3];
                float sc = b.Size / (28 * S);
                m[0] = sc; m[1] = 0; m[2] = 0; m[3] = sc; m[4] = b.X - area.Left - p.Cx * sc; m[5] = b.Y - area.Top - p.Cy * sc;
                var r = Blit.DrawAffine(s, p, m, 240, Light.Full, false);
                if (!r.IsEmpty) drawn.Add(r);
            }
        }

        public override bool Quiet { get { return rockets.Count == 0 && bats.Count == 0; } }
    }
}
