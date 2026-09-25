using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace Autumn
{
    // Small human touches: a note of what happened while you were away, the turning points of the
    // year, and leaves that can be caught mid-air with a click.
    class Moments : Sys
    {
        Dictionary<string, double> awaySnapshot;
        DateTime awaySince, lastWelcome = DateTime.MinValue;
        bool away;
        string lastMark = "";
        Leaf caught;
        bool wasDown;
        float catchT;

        public Moments() { Layer = 61; }

        public override void Step(World w, float dt)
        {
            Away(w);
            Turning(w);
            Catch(w, dt);
        }

        // ---------------------------------------------------------------- while you were away

        void Away(World w)
        {
            if (!away && w.Idle > 300)
            {
                away = true;
                awaySince = DateTime.Now.AddSeconds(-w.Idle);
                awaySnapshot = new Dictionary<string, double>(w.Journal.Stats);
            }
            else if (away && w.Idle < 2)
            {
                away = false;
                var gone = DateTime.Now - awaySince;
                if (gone.TotalMinutes < 30 || awaySnapshot == null || (DateTime.Now - lastWelcome).TotalHours < 3) return;
                lastWelcome = DateTime.Now;
                var parts = new List<string>();
                Add(parts, w, "leaves", "{0} leaves fell", "a leaf fell");
                Add(parts, w, "petals", "{0} petals drifted down", "a petal drifted down");
                Add(parts, w, "snowflakes", "about {0} snowflakes came down", null);
                Add(parts, w, "birds", "{0} birds stopped by", "a bird stopped by");
                Add(parts, w, "butterflies", "{0} butterflies passed", "a butterfly passed");
                Add(parts, w, "lightning", "lightning struck {0} times", "lightning struck once");
                Add(parts, w, "meteors", "{0} shooting stars went by unseen", "a shooting star went by unseen");
                Add(parts, w, "geese", "geese flew over", "geese flew over");
                if (parts.Count == 0) return;
                string span = gone.TotalHours >= 1.5 ? ((int)Math.Round(gone.TotalHours)) + " hours" : ((int)gone.TotalMinutes) + " minutes";
                string text = "In the last " + span + ": " + Join(parts) + ".";
                w.Journal.Log(DateTime.Now, "While you were away, " + Join(parts) + ".");
                w.Journal.Say("Welcome back", text);
            }
        }

        void Add(List<string> parts, World w, string key, string many, string one)
        {
            double before; awaySnapshot.TryGetValue(key, out before);
            long n = (long)(w.Journal.Stat(key) - before);
            if (n <= 0) return;
            if (n == 1 && one != null) parts.Add(one);
            else if (key != "geese") parts.Add(string.Format(CultureInfo.InvariantCulture, many, n.ToString("N0", CultureInfo.InvariantCulture)));
            else parts.Add(many);
        }

        static string Join(List<string> p)
        {
            if (p.Count == 1) return p[0];
            return string.Join(", ", p.GetRange(0, p.Count - 1).ToArray()) + " and " + p[p.Count - 1];
        }

        // ---------------------------------------------------------------- the year's turning points

        void Turning(World w)
        {
            if (w.Sky.Lapsing || w.Sky.SeasonOverride >= 0) return;
            var t = DateTime.Now;
            string mark = null;
            bool south = w.Sky.Southern;
            if (t.Month == 3 && t.Day == 20) mark = south ? "The autumn equinox: day and night are equal today." : "The spring equinox: day and night are equal today.";
            if (t.Month == 6 && t.Day == 21) mark = south ? "The winter solstice: the longest night of the year." : "The summer solstice: the longest day of the year.";
            if (t.Month == 9 && t.Day == 22) mark = south ? "The spring equinox: day and night are equal today." : "The autumn equinox: day and night are equal today.";
            if (t.Month == 12 && t.Day == 21) mark = south ? "The summer solstice: the longest day of the year." : "The winter solstice: the longest night of the year.";
            if (mark == null) return;
            string key = t.ToString("yyyy-MM-dd");
            string v;
            if (lastMark == key || (w.Journal.Extra.TryGetValue("mark", out v) && v == key)) { lastMark = key; return; }
            lastMark = key;
            w.Journal.Extra["mark"] = key;
            w.Journal.Log(t, mark);
            w.Journal.Say("Today", mark);
        }

        // ---------------------------------------------------------------- catching leaves

        void Catch(World w, float dt)
        {
            float S = w.S;
            bool down = w.MouseDown;
            if (down && !wasDown && caught == null && w.HaveCursor)
            {
                // Grab the airborne leaf nearest the pointer, if one is right there.
                Leaf best = null; float bd = 22 * S;
                foreach (var l in w.Leaves)
                {
                    if (l.Resting || l.OnCursor) continue;
                    float dx = l.X - w.CurX, dy = l.Y - w.CurY, d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d < bd + l.Look.Size * 0.3f) { bd = d; best = l; }
                }
                if (best != null)
                {
                    caught = best; best.OnCursor = true; best.Tumbler = false; best.Tumble = 0;
                    catchT = 0;
                    w.Journal.Count("caught", 1);
                    if (w.Journal.Stat("caught") >= 5) w.Journal.Discover(w, "catch");
                    if (w.Sound != null) w.Sound.Play("crunch", 0.15f, 0);
                }
            }
            if (caught != null)
            {
                if (!w.Leaves.Contains(caught)) caught = null;
                else if (!down)
                {
                    caught.OnCursor = false;
                    caught.Ix = w.CurVx * 0.6f; caught.Iy = w.CurVy * 0.6f - 60 * S;
                    caught.Tumbler = true; caught.Spin = w.Rand(-300, 300);
                    caught.PrevContact = caught.Y + caught.Look.Size * 0.26f;
                    caught = null;
                }
                else
                {
                    catchT += dt;
                    caught.X = w.CurX + caught.Look.Size * 0.1f;
                    caught.Y = w.CurY + caught.Look.Size * 0.25f;
                    caught.Angle += (float)Math.Sin(catchT * 5) * 20 * dt;
                }
            }
            wasDown = down;
        }

        public override bool Trigger(World w, string name)
        {
            if (name != "away") return false;
            away = true; awaySince = DateTime.Now.AddMinutes(-42);
            awaySnapshot = new Dictionary<string, double>(w.Journal.Stats);
            awaySnapshot["leaves"] = w.Journal.Stat("leaves") - 214; awaySnapshot["birds"] = w.Journal.Stat("birds") - 2;
            return true;
        }
    }
}
