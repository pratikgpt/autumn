using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Autumn
{
    static class Program
    {
        const string Id = "Local\\Autumn.Leaves.7c1e";

        [STAThread]
        static int Main(string[] args)
        {
            Native.SetProcessDpiAwarenessContext(Native.DPI_PER_MONITOR_AWARE_V2);
            string mode = args.Length > 0 ? args[0] : "";
            if (mode == "--sheet" && args.Length > 1) { Tests.Sheet(args[1]); return 0; }
            if (mode == "--sim" && args.Length > 1) { Tests.Sim(args); return 0; }
            if (mode == "--icon" && args.Length > 1) { WriteIco(args[1]); return 0; }
            if (mode == "--probe") { Probe(); return 0; }
            if (mode == "--bench") { Tests.Bench(); return 0; }
            if (mode == "--sounds" && args.Length > 1) { Tests.Sounds(args[1]); return 0; }
            if (mode == "--almanac" && args.Length > 1) { Tests.AlmanacShot(args[1]); return 0; }
            if (mode == "--creatures" && args.Length > 1) { Tests.Creatures(args[1]); return 0; }

            // Talk to a running instance: launching again summons a gust; --stop ends it; --dump writes a debug frame.
            bool created;
            using (var mutex = new Mutex(true, Id, out created))
            {
                if (!created)
                {
                    if (mode == "--set" && args.Length > 1)
                    {
                        File.WriteAllText(Path.Combine(Path.GetTempPath(), "autumn.cmd"), string.Join(" ", args, 1, args.Length - 1));
                        mode = "--cmd";
                    }
                    string ev = mode == "--stop" ? ".stop" : mode == "--dump" ? ".dump" : mode == "--cmd" ? ".cmd" : ".gust";
                    EventWaitHandle h;
                    if (EventWaitHandle.TryOpenExisting(Id + ev, out h)) { h.Set(); h.Dispose(); }
                    return 0;
                }
                if (mode == "--stop" || mode == "--dump" || mode == "--set") return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Native.timeBeginPeriod(1);
                try { Application.Run(new App(Native.GetForegroundWindow(), Id)); }
                finally { Native.timeEndPeriod(1); }
            }
            return 0;
        }

        static void Probe()
        {
            var desk = new DesktopWindows(new HashSet<IntPtr>());
            foreach (var m in desk.Monitors()) Console.WriteLine(string.Format("monitor {0},{1} {2}x{3}", m.Left, m.Top, m.Width, m.Height));
            var wins = desk.Snapshot(0.016f);
            foreach (var w in wins)
                Console.WriteLine(string.Format("{0,-34} {1,5},{2,5} {3,5}x{4,-5} hold={5} taskbar={6}",
                    Trim(Native.ClassOf(w.H), 34), w.Left, w.Top, w.Width, w.Bottom - w.Top, w.CanHold, w.IsTaskbar));
            var world = new World(desk, 1.5f, 1);
            foreach (var e in Ledges.Build(wins, world.Grounds))
                Console.WriteLine(string.Format("ledge y={0,5} x={1,5}..{2,-5} on {3}", e.Y, e.X0, e.X1, e.Host.IsGround ? "ground" : Trim(Native.ClassOf(e.Host.H), 30)));
        }

        static string Trim(string s, int n) { return s.Length <= n ? s : s.Substring(0, n); }

        // Multi-size .ico with PNG-compressed images (Vista+ format).
        static void WriteIco(string path)
        {
            int[] sizes = { 256, 64, 48, 32, 24, 16 };
            var pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = LeafLook.RenderIcon(s))
                using (var ms = new MemoryStream())
                {
                    // PNG only for 256px; small sizes as classic 32bpp DIBs, which every reader accepts.
                    if (s >= 256) bmp.Save(ms, ImageFormat.Png);
                    else
                    {
                        var w = new BinaryWriter(ms);
                        int mask = ((s + 31) / 32) * 4 * s;
                        w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
                        w.Write(0); w.Write(s * s * 4 + mask); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                        for (int y = s - 1; y >= 0; y--)
                            for (int x = 0; x < s; x++)
                            {
                                Color px = bmp.GetPixel(x, y);
                                w.Write(px.B); w.Write(px.G); w.Write(px.R); w.Write(px.A);
                            }
                        w.Write(new byte[mask]);
                        w.Flush();
                    }
                    pngs.Add(ms.ToArray());
                }
            using (var f = new BinaryWriter(File.Create(path)))
            {
                f.Write((short)0); f.Write((short)1); f.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); f.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    f.Write((byte)0); f.Write((byte)0); f.Write((short)1); f.Write((short)32);
                    f.Write(pngs[i].Length); f.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) f.Write(p);
            }
        }
    }
}
