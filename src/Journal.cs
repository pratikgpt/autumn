using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Autumn
{
    class Secret
    {
        public string Id, Name, Hint, Text;
        public Secret(string id, string name, string hint, string text) { Id = id; Name = name; Hint = hint; Text = text; }
    }

    // Everything worth remembering: counters, discovered secrets, and a dated journal of moments.
    // Saved as plain text in %APPDATA%\Autumn so the desktop's year carries on between runs.
    class Journal
    {
        public static readonly Secret[] Secrets = {
            new Secret("golden", "The Golden Leaf", "One leaf in hundreds is gold. Catch it.", "You caught a golden leaf."),
            new Secret("whirlwind", "Whirlwind", "Stir the air in a circle.", "You stirred up a whirlwind."),
            new Secret("hitchhiker", "Hitchhiker", "A falling leaf may ride a patient pointer.", "A leaf rode on your pointer."),
            new Secret("shootingstar", "Wish", "Clear nights. Patient eyes.", "You saw a shooting star."),
            new Secret("meteors", "Meteor Shower", "Some nights of the year the sky is busier.", "You watched a meteor shower."),
            new Secret("rainbow", "Rainbow", "Sun after rain.", "A rainbow crossed your screen."),
            new Secret("storm", "Counting Seconds", "Flash, then thunder.", "You sat through a thunderstorm."),
            new Secret("fireflies", "Firefly Friends", "On summer nights, be still.", "Fireflies gathered around your pointer."),
            new Secret("perch", "Perch", "Something small might land on a still pointer.", "A butterfly rested on your pointer."),
            new Secret("geese", "The Long V", "Look up in the migrating months.", "Geese flew over, heading somewhere warmer."),
            new Secret("snowhat", "Snow Hat", "Stand still in the snow.", "Your pointer wore a hat of snow."),
            new Secret("icicles", "Icicle Crash", "What hangs in winter can fall.", "You knocked icicles loose."),
            new Secret("frost", "Sleeve on Glass", "Cold mornings leave their mark.", "You wiped the frost off the screen."),
            new Secret("snowman", "Snowman", "Leave the snow alone for a while.", "Someone built a snowman while you were away."),
            new Secret("aurora", "Aurora", "Rare, cold, and very late.", "The northern lights came out."),
            new Secret("owl", "Night Owl", "Autumn, after midnight.", "An owl kept you company."),
            new Secret("festival", "Festival", "Some nights the whole desktop celebrates.", "You celebrated with the desktop."),
            new Secret("heart", "Heartleaf", "Keep autumn company long enough.", "The leaves made you a heart."),
            new Secret("catch", "Quick Hands", "Some things can be caught mid-air.", "You caught five leaves mid-air."),
            new Secret("gentle", "Gentle", "Birds trust a slow, still hand.", "A bird let you come close."),
            new Secret("year", "The Year in a Minute", "Find eight secrets, then ask the Almanac.", "You watched a whole year go by."),
        };

        public readonly Dictionary<string, double> Stats = new Dictionary<string, double>();
        public readonly Dictionary<string, DateTime> Found = new Dictionary<string, DateTime>();
        public readonly List<KeyValuePair<DateTime, string>> Entries = new List<KeyValuePair<DateTime, string>>();
        public DateTime FirstRun = DateTime.Now;
        public event Action<string, string> Announce;          // title, body
        public string Path;
        double saveT;
        public bool Dirty;
        public Dictionary<string, string> Extra = new Dictionary<string, string>();   // other systems' saved state

        public void Count(string key, double n)
        {
            double v;
            Stats.TryGetValue(key, out v);
            Stats[key] = v + n;
        }

        public double Stat(string key) { double v; return Stats.TryGetValue(key, out v) ? v : 0; }

        public void Log(DateTime when, string text)
        {
            Entries.Add(new KeyValuePair<DateTime, string>(when, text));
            if (Entries.Count > 300) Entries.RemoveAt(0);
            Dirty = true;
        }

        public bool Has(string id) { return Found.ContainsKey(id); }

        public void Discover(World w, string id)
        {
            if (Found.ContainsKey(id)) return;
            Secret s = null;
            foreach (var x in Secrets) if (x.Id == id) s = x;
            if (s == null) return;
            DateTime now = DateTime.Now;
            Found[id] = now;
            Log(now, s.Text);
            Dirty = true;
            if (w != null && w.Sound != null) w.Sound.Play("chime", 0.7f, 0);
            if (Announce != null) Announce("Secret found: " + s.Name, s.Text + "  (" + Found.Count + " of " + Secrets.Length + ")");
        }

        public void Say(string title, string body) { if (Announce != null) Announce(title, body); }

        public void Tick(World w, float dt)
        {
            Count("seconds", dt);
            saveT += dt;
            if (Path != null && saveT > 60) { saveT = 0; Save(w); }
        }

        // ---------------------------------------------------------------- persistence

        public void Save(World w)
        {
            if (Path == null) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("autumn-almanac 2");
                sb.AppendLine("firstRun=" + FirstRun.ToString("o", CultureInfo.InvariantCulture));
                foreach (var kv in Stats) sb.AppendLine("stat." + kv.Key + "=" + kv.Value.ToString("R", CultureInfo.InvariantCulture));
                foreach (var kv in Found) sb.AppendLine("found." + kv.Key + "=" + kv.Value.ToString("o", CultureInfo.InvariantCulture));
                foreach (var kv in Extra) sb.AppendLine("x." + kv.Key + "=" + kv.Value);
                foreach (var e in Entries)
                    sb.AppendLine("log=" + e.Key.ToString("o", CultureInfo.InvariantCulture) + "|" + e.Value.Replace("\n", " "));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                string tmp = Path + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(Path)) File.Replace(tmp, Path, null);
                else File.Move(tmp, Path);
                Dirty = false;
            }
            catch { }
        }

        public void Load()
        {
            if (Path == null || !File.Exists(Path)) return;
            try
            {
                foreach (var raw in File.ReadAllLines(Path))
                {
                    int eq = raw.IndexOf('=');
                    if (eq < 0) continue;
                    string k = raw.Substring(0, eq), v = raw.Substring(eq + 1);
                    if (k == "firstRun") FirstRun = DateTime.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    else if (k.StartsWith("stat.")) Stats[k.Substring(5)] = double.Parse(v, CultureInfo.InvariantCulture);
                    else if (k.StartsWith("found.")) Found[k.Substring(6)] = DateTime.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    else if (k.StartsWith("x.")) Extra[k.Substring(2)] = v;
                    else if (k == "log")
                    {
                        int bar = v.IndexOf('|');
                        if (bar > 0) Entries.Add(new KeyValuePair<DateTime, string>(DateTime.Parse(v.Substring(0, bar), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), v.Substring(bar + 1)));
                    }
                }
            }
            catch { }
        }
    }
}
