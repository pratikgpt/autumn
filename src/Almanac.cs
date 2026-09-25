using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace Autumn
{
    // A small book about this desktop's year: today's sky, the tallies, the journal, the secrets.
    class Almanac : Form
    {
        readonly World world;
        readonly Action windYear;
        readonly Timer timer = new Timer();
        Bitmap paper;
        float k = 1;                       // DPI scale
        RectangleF yearButton;
        const int W = 1000, H = 640;

        public Almanac(World w, Action windYear)
        {
            world = w; this.windYear = windYear;
            Text = "Almanac";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(243, 233, 210);
            using (var bmp = LeafLook.RenderIcon(32)) Icon = Icon.FromHandle(bmp.GetHicon());
            k = DeviceDpiScale();
            // Shrink to fit smaller screens (a 1080p laptop at 150% has only ~670 logical pixels of height).
            var work = Screen.PrimaryScreen.WorkingArea;
            k = Math.Min(k, Math.Min((work.Width - 40) / (float)W, (work.Height - 60) / (float)H));
            ClientSize = new Size((int)(W * k), (int)(H * k));
            timer.Interval = 1000;
            timer.Tick += delegate { Invalidate(); };
            timer.Start();
            MouseClick += OnClick;
            MouseMove += (s, e) => { Cursor = yearButton.Contains(e.X / k, e.Y / k) && Unlocked ? Cursors.Hand : Cursors.Default; };
        }

        float DeviceDpiScale()
        {
            try { return Native.GetDpiForWindow(Handle) / 96f; } catch { return 1; }
        }

        bool Unlocked { get { return world.Journal.Found.Count >= 8; } }

        void OnClick(object s, MouseEventArgs e)
        {
            if (Unlocked && yearButton.Contains(e.X / k, e.Y / k)) { windYear(); Close(); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop(); timer.Dispose();
            if (paper != null) paper.Dispose();
            base.OnFormClosed(e);
        }

        // Procedural parchment: warm base, fibres, speckles and a soft vignette.
        Bitmap MakePaper(int w, int h)
        {
            var b = new Bitmap(w, h);
            var rng = new Random(11);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(244, 235, 212));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < 2600; i++)
                {
                    int a = rng.Next(6, 22);
                    using (var br = new SolidBrush(Color.FromArgb(a, 120 + rng.Next(40), 90 + rng.Next(30), 50)))
                        g.FillEllipse(br, rng.Next(w), rng.Next(h), 1 + rng.Next(3), 1 + rng.Next(3));
                }
                for (int i = 0; i < 260; i++)
                {
                    int x = rng.Next(w), y = rng.Next(h), len = 10 + rng.Next(40);
                    using (var p = new Pen(Color.FromArgb(18, 255, 255, 240), 1))
                        g.DrawLine(p, x, y, x + len * (float)Math.Cos(rng.NextDouble() * 6), y + len * (float)Math.Sin(rng.NextDouble() * 6) * 0.3f);
                }
                using (var path = new GraphicsPath())
                {
                    path.AddRectangle(new Rectangle(-w / 3, -h / 4, w * 5 / 3, h * 3 / 2));
                    using (var pg = new PathGradientBrush(new[] { new PointF(0, 0), new PointF(w, 0), new PointF(w, h), new PointF(0, h) }))
                    {
                        pg.CenterColor = Color.FromArgb(0, 0, 0, 0);
                        pg.SurroundColors = new[] { Color.FromArgb(70, 110, 70, 30) };
                        pg.FocusScales = new PointF(0.82f, 0.86f);
                        g.FillRectangle(pg, 0, 0, w, h);
                    }
                }
            }
            return b;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (paper == null || paper.Width != ClientSize.Width) { if (paper != null) paper.Dispose(); paper = MakePaper(ClientSize.Width, ClientSize.Height); }
            g.DrawImageUnscaled(paper, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(k, k);
            // The spine: a soft shadow down the middle of the open book.
            using (var lb = new LinearGradientBrush(new PointF(W / 2 - 34, 0), new PointF(W / 2 + 34, 0), Color.FromArgb(0, 90, 60, 30), Color.FromArgb(0, 90, 60, 30)))
            {
                var blend = new ColorBlend(3);
                blend.Colors = new[] { Color.FromArgb(0, 90, 60, 30), Color.FromArgb(60, 90, 60, 30), Color.FromArgb(0, 90, 60, 30) };
                blend.Positions = new[] { 0f, 0.5f, 1f };
                lb.InterpolationColors = blend;
                g.FillRectangle(lb, W / 2 - 34, 0, 68, H);
            }
            var ink = Color.FromArgb(255, 58, 40, 26);
            var faded = Color.FromArgb(255, 150, 125, 98);
            var red = Color.FromArgb(255, 150, 46, 30);
            using (var title = new Font("Georgia", 30, FontStyle.Italic, GraphicsUnit.Pixel))
            using (var h2 = new Font("Georgia", 15, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var body = new Font("Georgia", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var small = new Font("Georgia", 11.5f, FontStyle.Italic, GraphicsUnit.Pixel))
            using (var inkB = new SolidBrush(ink))
            using (var fadedB = new SolidBrush(faded))
            using (var redB = new SolidBrush(red))
            using (var rule = new Pen(Color.FromArgb(120, 120, 88, 60), 1))
            {
                var sky = world.Sky;
                var j = world.Journal;
                const float L = 42, LW = 420, R = 540;

                // ------------------------------------------------ left page
                float y = 24;
                CenterIn(g, "The Almanac", title, inkB, L, LW, y); y += 40;
                CenterIn(g, "of this desktop, kept since " + j.FirstRun.ToString("d MMMM yyyy", CultureInfo.InvariantCulture), small, fadedB, L, LW, y); y += 24;
                Rule(g, rule, L, LW, y); y += 12;

                g.DrawString("Today", h2, redB, L, y);
                SeasonGlyph(g, sky.Season, L + LW - 26, y + 16);
                y += 22;
                g.DrawString(sky.Local.ToString("dddd, d MMMM", CultureInfo.InvariantCulture) + (sky.SeasonOverride >= 0 ? " (imagined)" : ""), body, inkB, L, y); y += 19;
                string temp = string.Format(CultureInfo.InvariantCulture, "{0:0}°C", sky.Temperature);
                g.DrawString(sky.Season + ", " + Weather.Describe(world.Weather.Kind).ToLower() + ", " + temp + " on the desktop", body, inkB, L, y); y += 19;
                DateTime rise, set;
                sky.SunTimes(DateTime.Now, out rise, out set);
                string sun = rise == DateTime.MinValue ? "The sun keeps its own hours today." : "Sunrise " + rise.ToString("HH:mm") + ", sunset " + set.ToString("HH:mm") + ", near " + ShortPlace(sky.Place);
                g.DrawString(sun, body, inkB, L, y); y += 19;
                string shower; Meteors.Activity(DateTime.Now, out shower);
                g.DrawString(shower != null ? "Tonight: the " + shower + " meteor shower." : NextShower(), body, inkB, L, y); y += 19;
                g.DrawString(sky.MoonName + ", " + (int)(sky.MoonIllum * 100) + "% lit.", body, inkB, L, y);
                g.DrawString("Full moon " + sky.NextPhase(0.5f).ToString("d MMM", CultureInfo.InvariantCulture) + ".", body, inkB, L, y + 19);
                Moon(g, L + LW - 30, y + 8, 22, sky.MoonPhase);
                y += 46;
                Rule(g, rule, L, LW, y); y += 12;

                g.DrawString("Tallies", h2, redB, L, y); y += 22;
                string[][] rows = {
                    new[] { "Leaves fallen", N(j.Stat("leaves")), "Leaves kicked", N(j.Stat("kicked")) },
                    new[] { "Petals", N(j.Stat("petals")), "Snowflakes", N(j.Stat("snowflakes")) },
                    new[] { "Birds visiting", N(j.Stat("birds")), "Butterflies", N(j.Stat("butterflies")) },
                    new[] { "Shooting stars", N(j.Stat("meteors")), "Lightning", N(j.Stat("lightning")) },
                    new[] { "Whirlwinds", N(j.Stat("whirlwinds")), "Hours of weather", (j.Stat("seconds") / 3600).ToString("0.0", CultureInfo.InvariantCulture) },
                };
                foreach (var r in rows)
                {
                    g.DrawString(r[0], body, fadedB, L, y); g.DrawString(r[1], body, inkB, L + 128, y);
                    g.DrawString(r[2], body, fadedB, L + 214, y); g.DrawString(r[3], body, inkB, L + 350, y);
                    y += 19;
                }
                y += 6; Rule(g, rule, L, LW, y); y += 12;

                g.DrawString("Journal", h2, redB, L, y); y += 22;
                int shown = 0;
                for (int i = j.Entries.Count - 1; i >= 0 && shown < 6 && y < H - 30; i--, shown++)
                {
                    var en = j.Entries[i];
                    g.DrawString(en.Key.ToString("d MMM, HH:mm", CultureInfo.InvariantCulture), small, fadedB, L, y + 1);
                    g.DrawString(en.Value, body, inkB, new RectangleF(L + 100, y, LW - 100, 18));
                    y += 19;
                }
                if (j.Entries.Count == 0) g.DrawString("Nothing written yet.", small, fadedB, L, y);

                // ------------------------------------------------ right page
                y = 30;
                g.DrawString("Secrets", h2, redB, R, y);
                g.DrawString(j.Found.Count + " of " + Journal.Secrets.Length, small, fadedB, R + 70, y + 2);
                y += 28;
                int col = 0; float rowY = y;
                foreach (var sec in Journal.Secrets)
                {
                    float x = R + col * 215;
                    DateTime when;
                    if (j.Found.TryGetValue(sec.Id, out when))
                    {
                        Seal(g, x + 7, rowY + 9);
                        g.DrawString(sec.Name, body, inkB, x + 18, rowY);
                        g.DrawString(when.ToString("d MMM yyyy", CultureInfo.InvariantCulture), small, fadedB, x + 18, rowY + 16);
                    }
                    else
                    {
                        g.DrawString("?", h2, fadedB, x + 2, rowY);
                        g.DrawString(sec.Hint, small, fadedB, new RectangleF(x + 18, rowY + 1, 192, 34));
                    }
                    col++;
                    if (col == 2) { col = 0; rowY += 50; }
                }

                string yb = Unlocked ? "Wind the year  \u25B8" : "Find eight secrets to wind the year.";
                var sz = g.MeasureString(yb, h2);
                float bx = R + (LW - sz.Width) / 2, by = H - 46;
                yearButton = new RectangleF(bx - 10, by - 6, sz.Width + 20, sz.Height + 12);
                if (Unlocked)
                    using (var p = new Pen(red, 1.4f)) g.DrawRectangle(p, yearButton.X, yearButton.Y, yearButton.Width, yearButton.Height);
                g.DrawString(yb, h2, Unlocked ? redB : fadedB, bx, by);
            }
        }

        void CenterIn(Graphics g, string s, Font f, Brush b, float x, float w, float y)
        {
            var sz = g.MeasureString(s, f);
            g.DrawString(s, f, b, x + (w - sz.Width) / 2, y);
        }

        void Rule(Graphics g, Pen p, float x, float w, float y)
        {
            float mid = x + w / 2;
            g.DrawLine(p, x, y, mid - 12, y);
            g.DrawLine(p, mid + 12, y, x + w, y);
            using (var b = new SolidBrush(p.Color))
                g.FillPolygon(b, new[] { new PointF(mid, y - 4), new PointF(mid + 6, y), new PointF(mid, y + 4), new PointF(mid - 6, y) });
        }
        static string N(double v) { return ((long)v).ToString("N0", CultureInfo.InvariantCulture); }

        static string ShortPlace(string tz)
        {
            if (tz.StartsWith("India")) return "central India";
            return tz.Replace(" Standard Time", "").Replace(" (approx.)", "");
        }

        static string NextShower()
        {
            string best = null; double bestDays = 999;
            string[] names = { "Quadrantids", "Lyrids", "Eta Aquariids", "Perseids", "Draconids", "Orionids", "Leonids", "Geminids", "Ursids" };
            int[][] dates = { new[] { 1, 3 }, new[] { 4, 22 }, new[] { 5, 6 }, new[] { 8, 12 }, new[] { 10, 8 }, new[] { 10, 21 }, new[] { 11, 17 }, new[] { 12, 14 }, new[] { 12, 22 } };
            var now = DateTime.Now;
            for (int i = 0; i < names.Length; i++)
            {
                var d = new DateTime(now.Year, dates[i][0], dates[i][1]);
                if (d < now.Date) d = d.AddYears(1);
                double days = (d - now.Date).TotalDays;
                if (days < bestDays) { bestDays = days; best = names[i]; }
            }
            return "Next meteor shower: the " + best + ", in " + (int)bestDays + " days.";
        }

        static void Seal(Graphics g, float x, float y)
        {
            using (var b = new SolidBrush(Color.FromArgb(255, 160, 40, 32))) g.FillEllipse(b, x - 6, y - 6, 12, 12);
            using (var p = new Pen(Color.FromArgb(200, 255, 220, 200), 1.2f)) { g.DrawLine(p, x - 3, y, x - 1, y + 3); g.DrawLine(p, x - 1, y + 3, x + 3, y - 3); }
        }

        // The moon as it looks tonight: a lit disc with the terminator where the phase puts it.
        static void Moon(Graphics g, float cx, float cy, float r, float phase)
        {
            var dark = Color.FromArgb(255, 70, 66, 78); var lit = Color.FromArgb(255, 246, 238, 206);
            using (var db = new SolidBrush(dark)) g.FillEllipse(db, cx - r, cy - r, 2 * r, 2 * r);
            float c = (float)Math.Cos(2 * Math.PI * phase);   // 1 new, -1 full
            bool waxing = phase < 0.5f;
            using (var lb = new SolidBrush(lit))
            using (var path = new GraphicsPath())
            {
                // Lit half on the waxing (right) or waning (left) side, then the terminator ellipse.
                path.AddArc(cx - r, cy - r, 2 * r, 2 * r, waxing ? -90 : 90, 180);
                float ew = Math.Abs(c) * r;
                path.AddArc(cx - ew, cy - r, 2 * ew + 0.01f, 2 * r, waxing ? 90 : -90, c > 0 ? -180 : 180);
                path.CloseFigure();
                g.FillPath(lb, path);
            }
            using (var p = new Pen(Color.FromArgb(120, 60, 50, 40), 1)) g.DrawEllipse(p, cx - r, cy - r, 2 * r, 2 * r);
        }

        static void SeasonGlyph(Graphics g, Season s, float cx, float cy)
        {
            var st = g.Save();
            if (s == Season.Summer)
            {
                using (var b = new SolidBrush(Color.FromArgb(255, 236, 170, 40)))
                using (var p = new Pen(Color.FromArgb(255, 236, 170, 40), 2.2f))
                {
                    g.FillEllipse(b, cx - 10, cy - 10, 20, 20);
                    for (int i = 0; i < 12; i++) { double a = i * Math.PI / 6; g.DrawLine(p, cx + (float)Math.Cos(a) * 14, cy + (float)Math.Sin(a) * 14, cx + (float)Math.Cos(a) * 20, cy + (float)Math.Sin(a) * 20); }
                }
            }
            else if (s == Season.Winter)
            {
                using (var p = new Pen(Color.FromArgb(255, 90, 130, 180), 2f))
                    for (int i = 0; i < 6; i++)
                    {
                        double a = i * Math.PI / 3;
                        float ex = cx + (float)Math.Cos(a) * 20, ey = cy + (float)Math.Sin(a) * 20;
                        g.DrawLine(p, cx, cy, ex, ey);
                        float bx = cx + (float)Math.Cos(a) * 12, by = cy + (float)Math.Sin(a) * 12;
                        g.DrawLine(p, bx, by, bx + (float)Math.Cos(a + 0.8) * 6, by + (float)Math.Sin(a + 0.8) * 6);
                        g.DrawLine(p, bx, by, bx + (float)Math.Cos(a - 0.8) * 6, by + (float)Math.Sin(a - 0.8) * 6);
                    }
            }
            else
            {
                var look = new LeafLook(s == Season.Spring ? LeafKind.Blossom : LeafKind.Maple, 44, new Random(4));
                if (s == Season.Autumn) look.Recolor(LeafLook.Hex("B8321A"), LeafLook.Hex("F29A2E"), true);
                using (var m = new Matrix())
                {
                    var t = g.Transform;
                    m.Multiply(t);
                    m.Translate(cx, cy);
                    m.Scale(44, 44);
                    m.Rotate(-10);
                    g.Transform = m;
                    look.DrawLocal(g, false);
                    g.Transform = t;
                }
                look.Dispose();
            }
            g.Restore(st);
        }
    }
}
