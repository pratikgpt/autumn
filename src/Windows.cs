using System;
using System.Collections.Generic;

namespace Autumn
{
    // A top-level window as the leaves see it: a rectangle in z-order with a velocity.
    class WinInfo
    {
        public IntPtr H;
        public int Left, Top, Right, Bottom;
        public bool IsTaskbar, IsGround, CanHold;   // CanHold: its top edge is a ledge leaves can land on
        public float Vx, Vy, Ax, Ay;                // px/s and px/s^2, smoothed
        public int Width { get { return Right - Left; } }

        // Drags arrive in uneven steps, so smooth velocity before differentiating it.
        public void Track(int l, int t, int r, int b, float dt, bool fresh)
        {
            if (!fresh && dt > 0)
            {
                float k = 1f - (float)Math.Exp(-dt / 0.06f);
                float vx = Vx + ((l - Left) / dt - Vx) * k;
                float vy = Vy + ((t - Top) / dt - Vy) * k;
                float ka = 1f - (float)Math.Exp(-dt / 0.04f);
                Ax += ((vx - Vx) / dt - Ax) * ka;
                Ay += ((vy - Vy) / dt - Ay) * ka;
                Vx = vx; Vy = vy;
            }
            Left = l; Top = t; Right = r; Bottom = b;
        }
    }

    // A visible stretch of some window's top edge.
    struct Ledge { public WinInfo Host; public float Y, X0, X1; }

    interface IWindowSource
    {
        // Top-level windows, topmost first. Implementations reuse WinInfo objects per handle.
        List<WinInfo> Snapshot(float dt);
        List<Native.RECT> Monitors();
    }

    class DesktopWindows : IWindowSource
    {
        readonly HashSet<IntPtr> ignore;
        readonly Dictionary<IntPtr, WinInfo> known = new Dictionary<IntPtr, WinInfo>();
        List<Native.RECT> monitors;
        List<WinInfo> list = new List<WinInfo>();
        readonly Native.EnumWindowsProc cb;
        HashSet<IntPtr> seen = new HashSet<IntPtr>();
        float dtNow;

        public DesktopWindows(HashSet<IntPtr> ownWindows)
        {
            ignore = ownWindows;
            cb = Visit;
            RefreshMonitors();
        }

        public void RefreshMonitors()
        {
            var m = new List<Native.RECT>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr h, IntPtr dc, ref Native.RECT r, IntPtr d) { m.Add(r); return true; }, IntPtr.Zero);
            monitors = m;
        }

        public List<Native.RECT> Monitors() { return monitors; }

        public List<WinInfo> Snapshot(float dt)
        {
            dtNow = dt;
            list = new List<WinInfo>();
            seen.Clear();
            Native.EnumWindows(cb, IntPtr.Zero);
            if (known.Count > seen.Count * 2 + 64)
            {
                var dead = new List<IntPtr>();
                foreach (var k in known.Keys) if (!seen.Contains(k)) dead.Add(k);
                foreach (var k in dead) known.Remove(k);
            }
            return list;
        }

        bool Visit(IntPtr h, IntPtr lp)
        {
            if (ignore.Contains(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
            long ex = Native.ExStyle(h);
            if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true;          // click-through overlays
            string cls = Native.ClassOf(h);
            if (cls == "Progman" || cls == "WorkerW") return true;
            bool taskbar = cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
            if (!taskbar)
            {
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;
                if (Native.GetWindowTextLength(h) == 0) return true;
            }
            if (Native.IsCloaked(h)) return true;
            Native.RECT r;
            if (!Native.FrameBounds(h, out r)) return true;
            if (r.Width < 60 || r.Height < 30) return true;

            WinInfo w;
            bool fresh = !known.TryGetValue(h, out w);
            if (fresh) { w = new WinInfo(); w.H = h; known[h] = w; }
            w.Track(r.Left, r.Top, r.Right, r.Bottom, dtNow, fresh);
            w.IsTaskbar = taskbar;
            w.CanHold = taskbar || !TouchesMonitorTop(r);
            seen.Add(h);
            list.Add(w);
            return true;
        }

        bool TouchesMonitorTop(Native.RECT r)
        {
            int cx = (r.Left + r.Right) / 2;
            foreach (var m in monitors)
                if (cx >= m.Left && cx < m.Right && r.Top <= m.Top + 2 && r.Bottom > m.Top) return true;
            return false;
        }
    }

    static class Ledges
    {
        // Visible parts of each holding window's top edge, plus a ground ledge along every monitor bottom.
        public static List<Ledge> Build(List<WinInfo> wins, List<WinInfo> grounds)
        {
            var result = new List<Ledge>();
            var spans = new List<float[]>();
            for (int i = 0; i < wins.Count; i++)
            {
                var w = wins[i];
                if (!w.CanHold) continue;
                spans.Clear();
                spans.Add(new float[] { w.Left, w.Right });
                for (int j = 0; j < i && spans.Count > 0; j++)
                {
                    var o = wins[j];
                    if (Covers(o, w)) Subtract(spans, o.Left, o.Right);
                }
                foreach (var s in spans)
                    if (s[1] - s[0] >= 8) result.Add(new Ledge { Host = w, Y = w.Top, X0 = s[0], X1 = s[1] });
            }
            foreach (var g in grounds)
            {
                spans.Clear();
                spans.Add(new float[] { g.Left, g.Right });
                foreach (var o in wins) if (Covers(o, g)) Subtract(spans, o.Left, o.Right);
                foreach (var s in spans)
                    if (s[1] - s[0] >= 8) result.Add(new Ledge { Host = g, Y = g.Top, X0 = s[0], X1 = s[1] });
            }
            return result;
        }

        // A window hides a ledge only if it straddles the ledge line; merely touching it (a maximized
        // window resting on the taskbar) does not.
        // The ground (monitor bottom) is covered by anything that reaches down to it, like the taskbar.
        public static bool Covers(WinInfo o, WinInfo host)
        {
            int y = host.Top;
            return host.IsGround ? o.Top < y && o.Bottom >= y : o.Top <= y && o.Bottom > y;
        }

        static void Subtract(List<float[]> spans, float a, float b)
        {
            for (int k = spans.Count - 1; k >= 0; k--)
            {
                float s0 = spans[k][0], s1 = spans[k][1];
                if (b <= s0 || a >= s1) continue;
                spans.RemoveAt(k);
                if (a > s0) spans.Add(new float[] { s0, a });
                if (b < s1) spans.Add(new float[] { b, s1 });
            }
        }
    }

    // Scripted windows for offscreen tests.
    class FakeWindows : IWindowSource
    {
        public List<WinInfo> Wins = new List<WinInfo>();
        public List<Native.RECT> Mons = new List<Native.RECT>();
        readonly Dictionary<WinInfo, int[]> target = new Dictionary<WinInfo, int[]>();
        public List<WinInfo> Snapshot(float dt)
        {
            foreach (var w in Wins)
            {
                int[] t;
                if (!target.TryGetValue(w, out t)) t = new[] { w.Left, w.Top, w.Right, w.Bottom };
                w.Track(t[0], t[1], t[2], t[3], dt, false);
            }
            return new List<WinInfo>(Wins);
        }
        public List<Native.RECT> Monitors() { return Mons; }

        public WinInfo Add(int l, int t, int r, int b, bool taskbar)
        {
            var w = new WinInfo { H = new IntPtr(1000 + Wins.Count), Left = l, Top = t, Right = r, Bottom = b, IsTaskbar = taskbar };
            w.CanHold = true;
            Wins.Add(w);
            return w;
        }

        // Scripted move: applied on the next Snapshot, like a real drag step.
        public void Move(WinInfo w, int dx, int dy)
        {
            int[] t;
            if (!target.TryGetValue(w, out t)) t = new[] { w.Left, w.Top, w.Right, w.Bottom };
            target[w] = new[] { t[0] + dx, t[1] + dy, t[2] + dx, t[3] + dy };
        }
    }
}
