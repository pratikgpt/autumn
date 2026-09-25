using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Autumn
{
    class App : ApplicationContext
    {
        readonly List<Overlay> overlays = new List<Overlay>();
        readonly HashSet<IntPtr> own = new HashSet<IntPtr>();
        readonly Painter painter = new Painter();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly DesktopWindows desk;
        readonly World world;
        readonly Audio audio;
        readonly NotifyIcon tray;
        readonly Control ui;
        readonly EventWaitHandle gustEv, stopEv, dumpEv, cmdEv;
        readonly string logPath = Path.Combine(Path.GetTempPath(), "autumn.log");
        readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autumn");
        Thread pacer;
        volatile bool running = true;
        int pending;
        double last, statT, stepMs, paintMs, topT;
        int statFrames;
        bool paused, stopping, locked, restored;
        public int SoundLevel = 1;             // 0 off, 1 quiet, 2 full
        ToolStripMenuItem titleItem, pauseItem, densityItem, endItem;
        ToolStripMenuItem[] seasonItems, weatherItems, timeItems, soundItems;

        public App(IntPtr foreground, string id)
        {
            ui = new Control();
            ui.CreateControl();
            float scale = PrimaryScale();
            BuildOverlays();
            desk = new DesktopWindows(own);
            world = new World(desk, scale, Environment.TickCount);
            world.FirstTargetHandle = foreground;
            Setup.AddSystems(world);
            audio = new Audio();
            world.Sound = audio;

            world.Journal.Path = Path.Combine(dataDir, "almanac.txt");
            world.Journal.Load();
            LoadSettings();
            world.Journal.Announce += (title, body) => ui.BeginInvoke((Action)delegate { if (tray != null) tray.ShowBalloonTip(8000, title, body, ToolTipIcon.None); });
            if (world.Journal.Stat("runs") == 0) world.Journal.Log(DateTime.Now, "Autumn arrived on this desktop.");
            world.Journal.Count("runs", 1);

            tray = new NotifyIcon();
            int ico = SystemInformation.SmallIconSize.Width;
            using (var bmp = LeafLook.RenderIcon(ico)) tray.Icon = Icon.FromHandle(bmp.GetHicon());
            tray.Text = "Autumn";
            tray.ContextMenuStrip = BuildMenu();
            tray.DoubleClick += delegate { OpenAlmanac(); };
            tray.Visible = true;
            tray.ShowBalloonTip(9000, Greeting(), world.Journal.Stat("runs") <= 1
                ? "Drag a window. Give one a shake. Sweep your mouse through a pile.\nDouble-click the leaf down here for the Almanac."
                : "Your desktop kept its leaves while you were gone. Double-click the leaf for the Almanac.", ToolTipIcon.None);

            gustEv = new EventWaitHandle(false, EventResetMode.AutoReset, id + ".gust");
            stopEv = new EventWaitHandle(false, EventResetMode.AutoReset, id + ".stop");
            dumpEv = new EventWaitHandle(false, EventResetMode.AutoReset, id + ".dump");
            cmdEv = new EventWaitHandle(false, EventResetMode.AutoReset, id + ".cmd");

            try { var fi = new FileInfo(logPath); if (fi.Exists && fi.Length > 256 * 1024) fi.Delete(); } catch { }
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.SessionEnding += delegate { SaveState(); };
            Log("start scale=" + scale + " overlays=" + overlays.Count + " place=" + world.Sky.Place + " season=" + world.Sky.Season);

            pacer = new Thread(Pace);
            pacer.IsBackground = true;
            pacer.Start();
        }

        string Greeting()
        {
            world.Sky.Update(0);
            var sky = world.Sky;
            int h = DateTime.Now.Hour;
            string hello = h < 5 ? "Still up?" : h < 12 ? "Good morning." : h < 17 ? "Good afternoon." : h < 22 ? "Good evening." : "Good night.";
            return hello + " " + sky.Season + ", " + string.Format(CultureInfo.InvariantCulture, "{0:0}°C", sky.Temperature) + " on your desktop";
        }

        static float PrimaryScale()
        {
            float s = 1f;
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr h, IntPtr dc, ref Native.RECT r, IntPtr d)
            {
                var mi = new Native.MONITORINFO(); mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(mi);
                Native.GetMonitorInfo(h, ref mi);
                if ((mi.dwFlags & 1) != 0) { uint dx, dy; if (Native.GetDpiForMonitor(h, 0, out dx, out dy) == 0) s = dx / 96f; }
                return true;
            }, IntPtr.Zero);
            return s;
        }

        void BuildOverlays()
        {
            foreach (var o in overlays) { own.Remove(o.Handle); o.Close(); o.Dispose(); }
            overlays.Clear();
            var mons = new List<Native.RECT>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr h, IntPtr dc, ref Native.RECT r, IntPtr d) { mons.Add(r); return true; }, IntPtr.Zero);
            foreach (var m in mons)
            {
                var o = new Overlay(Rectangle.FromLTRB(m.Left, m.Top, m.Right, m.Bottom));
                o.Show();
                own.Add(o.Handle);
                overlays.Add(o);
            }
        }

        // ---------------------------------------------------------------- tray menu

        ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            titleItem = new ToolStripMenuItem("Autumn") { Enabled = false };
            titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);
            menu.Items.Add(titleItem);
            var alm = new ToolStripMenuItem("Almanac…", null, delegate { OpenAlmanac(); });
            alm.Font = new Font(alm.Font, FontStyle.Regular);
            menu.Items.Add(alm);
            menu.Items.Add(new ToolStripSeparator());

            var seasons = new ToolStripMenuItem("Season");
            string[] sn = { "Follow the calendar", "Spring", "Summer", "Autumn", "Winter" };
            seasonItems = new ToolStripMenuItem[sn.Length];
            for (int i = 0; i < sn.Length; i++)
            {
                int k = i - 1;
                seasonItems[i] = new ToolStripMenuItem(sn[i], null, delegate { SetSeason(k); });
                seasons.DropDownItems.Add(seasonItems[i]);
                if (i == 0) seasons.DropDownItems.Add(new ToolStripSeparator());
            }
            menu.Items.Add(seasons);

            var weather = new ToolStripMenuItem("Weather");
            string[] wn = { "Let it be", "Clear", "Breezy", "Rain", "Thunderstorm", "Snow", "Blizzard" };
            weatherItems = new ToolStripMenuItem[wn.Length];
            for (int i = 0; i < wn.Length; i++)
            {
                int k = i - 1;
                weatherItems[i] = new ToolStripMenuItem(wn[i], null, delegate { SetWeather(k); });
                weather.DropDownItems.Add(weatherItems[i]);
                if (i == 0) weather.DropDownItems.Add(new ToolStripSeparator());
            }
            menu.Items.Add(weather);

            var time = new ToolStripMenuItem("Time of day");
            string[] tn = { "Real time", "Dawn", "Noon", "Dusk", "Midnight" };
            timeItems = new ToolStripMenuItem[tn.Length];
            for (int i = 0; i < tn.Length; i++)
            {
                int k = i;
                timeItems[i] = new ToolStripMenuItem(tn[i], null, delegate { SetTime(k); });
                time.DropDownItems.Add(timeItems[i]);
                if (i == 0) time.DropDownItems.Add(new ToolStripSeparator());
            }
            menu.Items.Add(time);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("Summon a gust", null, delegate { world.StartGust(380); });
            densityItem = new ToolStripMenuItem("More leaves", null, delegate
            {
                world.Density = world.Density < 1.4f ? 1.9f : 1f;
                SaveSettings();
            });
            menu.Items.Add(densityItem);
            var sound = new ToolStripMenuItem("Sound");
            string[] so = { "Off", "Quiet", "Full" };
            soundItems = new ToolStripMenuItem[3];
            for (int i = 0; i < 3; i++) { int k = i; soundItems[i] = new ToolStripMenuItem(so[i], null, delegate { SoundLevel = k; SaveSettings(); }); sound.DropDownItems.Add(soundItems[i]); }
            menu.Items.Add(sound);
            pauseItem = new ToolStripMenuItem("Pause", null, delegate { paused = !paused; });
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            endItem = new ToolStripMenuItem("Let autumn end", null, delegate { BeginEnd(); });
            menu.Items.Add(endItem);
            menu.Opening += delegate { RefreshMenu(); };
            return menu;
        }

        void RefreshMenu()
        {
            var sky = world.Sky;
            string part = sky.IsNight ? "night" : sky.SunElev < 6 ? (sky.Hour < 12 ? "dawn" : "dusk") : sky.Hour < 12 ? "morning" : sky.Hour < 17 ? "afternoon" : "evening";
            titleItem.Text = sky.Season + " · " + Weather.Describe(world.Weather.Kind) + " · " + part;
            for (int i = 0; i < seasonItems.Length; i++) seasonItems[i].Checked = sky.SeasonOverride == i - 1;
            for (int i = 0; i < weatherItems.Length; i++) weatherItems[i].Checked = world.Weather.Override == i - 1;
            int t = sky.HourOverride < 0 ? 0 : timePreset;
            for (int i = 0; i < timeItems.Length; i++) timeItems[i].Checked = t == i;
            for (int i = 0; i < 3; i++) soundItems[i].Checked = SoundLevel == i;
            densityItem.Text = world.Density > 1.4f ? "Fewer leaves" : "More leaves";
            pauseItem.Checked = paused;
            endItem.Text = "Let " + sky.Season.ToString().ToLower() + " end";
        }

        int timePreset;
        string lastErr = "";
        double lastErrT;
        volatile bool onBattery;
        int vblank;
        double powerT;
        Almanac almanac;

        void OpenAlmanac()
        {
            if (almanac != null && !almanac.IsDisposed) { almanac.Activate(); return; }
            // Not added to our own windows: leaves may land on the Almanac too.
            almanac = new Almanac(world, delegate { var ev = world.Get<Events>(); if (ev != null) ev.StartYear(world); });
            almanac.Show();
        }

        void SetSeason(int k)
        {
            world.Sky.SeasonOverride = k;
            if (world.Weather.Override < 0) world.Weather.SpellLeft = Math.Min(world.Weather.SpellLeft, 3);
            SaveSettings();
        }

        void SetWeather(int k)
        {
            world.Weather.Force(k);
            bool cold = k == (int)WeatherKind.Snow || k == (int)WeatherKind.Blizzard;
            world.Sky.ColdOverride = 0;
            world.Sky.Update(0);
            if (cold && world.Sky.Temperature > -3) world.Sky.ColdOverride = -3 - world.Sky.Temperature;
        }

        void SetTime(int preset)
        {
            timePreset = preset;
            if (preset == 0) { world.Sky.HourOverride = -1; return; }
            DateTime rise, set;
            world.Sky.SunTimes(DateTime.Now, out rise, out set);
            float riseH = rise == DateTime.MinValue ? 6.5f : (float)rise.TimeOfDay.TotalHours;
            float setH = set == DateTime.MinValue ? 18.5f : (float)set.TimeOfDay.TotalHours;
            world.Sky.HourOverride = preset == 1 ? riseH + 0.1f : preset == 2 ? 12.5f : preset == 3 ? setH - 0.1f : 0.5f;
        }

        // ---------------------------------------------------------------- settings and saved state

        void LoadSettings()
        {
            var x = world.Journal.Extra;
            string v;
            if (x.TryGetValue("sound", out v)) int.TryParse(v, out SoundLevel);
            if (x.TryGetValue("density", out v)) { float d; if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) world.Density = d; }
            if (x.TryGetValue("season", out v)) { int s; if (int.TryParse(v, out s)) world.Sky.SeasonOverride = s; }
        }

        void SaveSettings()
        {
            var x = world.Journal.Extra;
            x["sound"] = SoundLevel.ToString();
            x["density"] = world.Density.ToString(CultureInfo.InvariantCulture);
            x["season"] = world.Sky.SeasonOverride.ToString();
            world.Journal.Dirty = true;
        }

        // Keep the scene as it was before the farewell gust blows it away.
        void BeginEnd()
        {
            if (stopping) return;
            SaveState(true);
            stopping = true;
        }

        void SaveState() { SaveState(!stopping); }

        void SaveState(bool scene)
        {
            try
            {
                SaveSettings();
                var tb = scene ? Taskbar() : null;
                var x = world.Journal.Extra;
                if (tb != null)
                {
                    var sb = new StringBuilder();
                    foreach (var l in world.Leaves)
                        if (l.Resting && l.Host == tb && !l.Dying)
                            sb.AppendFormat(CultureInfo.InvariantCulture, "{0}:{1}:{2:0.0}:{3}:{4:0.0}:{5};", (int)l.Look.Kind, l.Look.Tone, l.Look.Size / world.S, l.Seed, l.RelX / world.S, l.Golden ? 1 : 0);
                    x["taskbarLeaves"] = sb.ToString();
                    var snow = world.Get<Snow>();
                    x["taskbarSnow"] = snow != null ? snow.Save(world) : "";
                    x["savedAt"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                }
                world.Journal.Save(world);
            }
            catch (Exception ex) { Log("save error: " + ex.Message); }
        }

        WinInfo Taskbar()
        {
            foreach (var w in world.Wins) if (w.IsTaskbar) return w;
            return null;
        }

        // Put back what was lying on the taskbar last time, once the taskbar has been seen.
        void RestoreState()
        {
            restored = true;
            var tb = Taskbar();
            if (tb == null) return;
            var x = world.Journal.Extra;
            string v;
            if (x.TryGetValue("taskbarLeaves", out v) && !string.IsNullOrEmpty(v))
            {
                int n = 0;
                foreach (var item in v.Split(';'))
                {
                    var p = item.Split(':');
                    if (p.Length < 6) continue;
                    try
                    {
                        var l = world.RestoreLeaf((LeafKind)int.Parse(p[0]), int.Parse(p[1]), float.Parse(p[2], CultureInfo.InvariantCulture) * world.S, int.Parse(p[3]), p[5] == "1");
                        world.PlaceResting(l, tb, float.Parse(p[4], CultureInfo.InvariantCulture) * world.S);
                        n++;
                    }
                    catch { }
                }
                Log("restored " + n + " taskbar leaves");
            }
            var snow = world.Get<Snow>();
            if (snow != null && x.TryGetValue("taskbarSnow", out v)) snow.Load(world, tb, v);
        }

        // ---------------------------------------------------------------- the frame loop

        // Frames are paced to the compositor's vblank on a worker thread, executed on the UI thread.
        void Pace()
        {
            Action frame = Frame;
            var handles = new WaitHandle[] { gustEv, stopEv, dumpEv, cmdEv };
            while (running)
            {
                long t0 = clock.ElapsedTicks;
                if (Native.DwmFlush() != 0) Thread.Sleep(16);
                if ((clock.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency < 3) Thread.Sleep(12);
                int sig = WaitHandle.WaitAny(handles, 0);
                if (sig == 0) Post(delegate { world.StartGust(380); });
                else if (sig == 1) Post(BeginEnd);
                else if (sig == 2) Post(Dump);
                else if (sig == 3) Post(ReadCommand);
                // On battery, every other vblank is enough.
                if (onBattery && (++vblank & 1) == 1) continue;
                if (Interlocked.CompareExchange(ref pending, 1, 0) == 0 && !Post(frame)) break;
            }
        }

        bool Post(Action a)
        {
            try { ui.BeginInvoke(a); return true; } catch { return false; }
        }

        void Frame()
        {
            try
            {
                double now = clock.Elapsed.TotalSeconds;
                float dt = (float)Math.Min(0.05, now - last);
                last = now;
                bool hide = paused || locked || FullscreenForeground();
                audio.Master = SoundLevel == 0 ? 0 : SoundLevel == 1 ? 0.35f : 0.8f;
                audio.Idle = hide || Native.IdleSeconds() > 90;
                audio.CricketTemperature(world.Sky.Temperature);
                if (hide)
                {
                    foreach (var o in overlays) o.Frame(world, painter, true);
                    return;
                }
                double t0 = clock.Elapsed.TotalMilliseconds;
                world.Idle = Native.IdleSeconds();
                world.MouseDown = Native.PrimaryDown();
                Native.POINT p;
                bool cur = Native.GetCursorPos(out p);
                if (stopping && world.StepStopping(dt)) { Quit(); return; }
                world.Step(dt, cur, p.X, p.Y);
                if (!restored && world.Time > 0.2f) RestoreState();
                double t1 = clock.Elapsed.TotalMilliseconds;
                foreach (var o in overlays) o.Frame(world, painter, false);
                double t2 = clock.Elapsed.TotalMilliseconds;
                stepMs += t1 - t0; paintMs += t2 - t1; statFrames++;
                topT += dt;
                if (topT > 2) { topT = 0; foreach (var o in overlays) o.KeepOnTop(); }
                powerT += dt;
                if (powerT > 10) { powerT = 0; onBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline; }
                if (world.Journal.Dirty && world.Time % 30 < dt) SaveState();
                if (now - statT > (now < 120 ? 15 : 600))
                {
                    int rest = 0;
                    foreach (var l in world.Leaves) if (l.Resting) rest++;
                    var snow = world.Get<Snow>();
                    Log(string.Format("fps={0:0.0} step={1:0.00}ms paint+present={2:0.00}ms leaves={3} resting={4} flakes={5} {6} {7} {8:0.0}C sun={9:0.0}",
                        statFrames / (now - statT), stepMs / statFrames, paintMs / statFrames, world.Leaves.Count, rest, snow != null ? snow.Flakes.Count : 0,
                        world.Sky.Season, world.Weather.Kind, world.Sky.Temperature, world.Sky.SunElev) + " | " + Stats.Take(statFrames)
                        + string.Format(" | audio open={0} opens={1} idle={2:0}s {3}", audio.IsOpen, audio.Opens, world.Idle, audio.LastError));
                    statT = now; stepMs = paintMs = 0; statFrames = 0;
                }
            }
            catch (Exception ex)
            {
                // Log each distinct failure at most every ten seconds so a repeating fault can't flood the disk.
                string key = ex.GetType().Name + ex.TargetSite;
                double nowS = clock.Elapsed.TotalSeconds;
                if (key != lastErr || nowS - lastErrT > 10) { Log("frame error: " + ex); lastErr = key; lastErrT = nowS; }
            }
            finally { Interlocked.Exchange(ref pending, 0); }
        }

        bool FullscreenForeground()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || own.Contains(fg)) return false;
            string cls = Native.ClassOf(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
            Native.RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            foreach (var o in overlays)
                if (r.Left <= o.Area.Left && r.Top <= o.Area.Top && r.Right >= o.Area.Right && r.Bottom >= o.Area.Bottom) return true;
            return false;
        }

        // ---------------------------------------------------------------- commands (Autumn.exe --set ...)

        void ReadCommand()
        {
            string path = Path.Combine(Path.GetTempPath(), "autumn.cmd");
            string text;
            try { text = File.ReadAllText(path); File.Delete(path); } catch { return; }
            Log("command: " + text);
            foreach (var tok in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = tok.Split('=');
                string k = kv[0].ToLowerInvariant(), v = kv.Length > 1 ? kv[1].ToLowerInvariant() : "";
                if (k == "season") SetSeason(Array.IndexOf(new[] { "spring", "summer", "autumn", "winter" }, v));
                else if (k == "weather") SetWeather(Array.IndexOf(new[] { "clear", "breezy", "rain", "storm", "snow", "blizzard" }, v));
                else if (k == "hour") { float h; if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out h)) world.Sky.HourOverride = h; else world.Sky.HourOverride = -1; }
                else if (k == "gust") world.StartGust(400);
                else if (k == "save") SaveState();
                else if (k == "event") world.Trigger(v);
                else if (k == "sound") { int s; if (int.TryParse(v, out s)) SoundLevel = s; }
            }
        }

        // Debug aid: the overlay's own pixels (never screen content) over window outlines.
        void Dump()
        {
            string dir = Path.GetTempPath();
            var sb = new StringBuilder();
            foreach (var w in world.Wins)
                sb.AppendLine(string.Format("{0} {1},{2},{3},{4} hold={5}", Native.ClassOf(w.H), w.Left, w.Top, w.Right, w.Bottom, w.CanHold));
            foreach (var e in world.LedgeList) sb.AppendLine(string.Format("ledge y={0} {1}..{2}", e.Y, e.X0, e.X1));
            sb.AppendLine(string.Format("season={0} weather={1} temp={2:0.0} sun={3:0.0} moon={4:0.00} wind={5:0}", world.Sky.Season, world.Weather.Kind, world.Sky.Temperature, world.Sky.SunElev, world.Sky.MoonPhase, world.Wind));
            File.WriteAllText(Path.Combine(dir, "autumn_windows.txt"), sb.ToString());
            for (int i = 0; i < overlays.Count; i++)
            {
                var o = overlays[i];
                using (var leaves = o.Snapshot())
                using (var bmp = new Bitmap(o.Area.Width, o.Area.Height, PixelFormat.Format32bppPArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(40, 44, 52));
                    for (int k = world.Wins.Count - 1; k >= 0; k--)
                    {
                        var w = world.Wins[k];
                        var r = new Rectangle(w.Left - o.Area.Left, w.Top - o.Area.Top, w.Width, w.Bottom - w.Top);
                        using (var br = new SolidBrush(w.IsTaskbar ? Color.FromArgb(20, 20, 24) : Color.FromArgb(205, 208, 216))) g.FillRectangle(br, r);
                        using (var pen = new Pen(Color.FromArgb(90, 90, 100))) g.DrawRectangle(pen, r);
                    }
                    g.DrawImageUnscaled(leaves, 0, 0);
                    bmp.Save(Path.Combine(dir, "autumn_frame" + i + ".png"), ImageFormat.Png);
                }
            }
            Log("dumped frame");
        }

        void OnDisplayChanged(object s, EventArgs e)
        {
            ui.BeginInvoke((Action)delegate
            {
                BuildOverlays();
                desk.RefreshMonitors();
                world.RefreshMonitors();
                Log("display changed; overlays=" + overlays.Count);
            });
        }

        void OnSessionSwitch(object s, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect) { locked = true; SaveState(); }
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect) locked = false;
        }

        void Quit()
        {
            running = false;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SaveState();
            tray.Visible = false;
            tray.Dispose();
            audio.Dispose();
            foreach (var o in overlays) { o.Close(); o.Dispose(); }
            Log("goodbye");
            ExitThread();
        }

        void Log(string s)
        {
            try { File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss ") + s + Environment.NewLine); } catch { }
        }
    }
}
