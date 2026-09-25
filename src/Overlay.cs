using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Autumn
{
    static class Stats
    {
        public static long Clear, Paint, Present, DirtyPx;
        public static long Now() { return System.Diagnostics.Stopwatch.GetTimestamp(); }
        public static double Ms(long ticks) { return ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
        public static string Take(int frames)
        {
            frames = System.Math.Max(1, frames);
            string s = string.Format("clear={0:0.00} paint={1:0.00} present={2:0.00} dirty={3:0.00}MP",
                Ms(Clear) / frames, Ms(Paint) / frames, Ms(Present) / frames, DirtyPx / 1e6 / frames);
            Clear = Paint = Present = DirtyPx = 0;
            return s;
        }
    }

    // One per monitor: a borderless, click-through, topmost window whose pixels come from a
    // premultiplied-alpha DIB pushed with UpdateLayeredWindowIndirect. Only dirty pixels are touched.
    class Overlay : Form
    {
        public Rectangle Area { get; private set; }
        IntPtr memDC, hBmp, oldBmp, bits;
        Bitmap canvas;
        Surface surface;
        List<Rectangle> prev = new List<Rectangle>(), cur = new List<Rectangle>();
        bool blank = true;

        public Overlay(Rectangle area)
        {
            Area = area;
            Text = "Autumn";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = area;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= (int)(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST);
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // Never let a stray message resize us to a DPI-scaled size.
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            base.SetBoundsCore(Area.X, Area.Y, Area.Width, Area.Height, BoundsSpecified.All);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var bi = new Native.BITMAPINFOHEADER();
            bi.biSize = 40; bi.biWidth = Area.Width; bi.biHeight = -Area.Height; bi.biPlanes = 1; bi.biBitCount = 32;
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            memDC = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            hBmp = Native.CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = Native.SelectObject(memDC, hBmp);
            canvas = new Bitmap(Area.Width, Area.Height, Area.Width * 4, PixelFormat.Format32bppPArgb, bits);
            surface = new Surface { Bits = bits, W = Area.Width, H = Area.Height, Stride = Area.Width * 4 };
            Present(new Rectangle(0, 0, Area.Width, Area.Height));
        }

        public void KeepOnTop()
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void Frame(World w, Painter painter, bool hidden)
        {
            if (surface == null) return;
            long t0 = Stats.Now();
            foreach (var r in prev) Clear(r);
            cur.Clear();
            long t1 = Stats.Now();
            if (!hidden) painter.Paint(surface, w, Area, cur);
            long t2 = Stats.Now();
            if (prev.Count == 0 && cur.Count == 0 && blank) return;
            // Upload changed pixels as a few tight clusters rather than one screen-spanning box.
            clusters.Clear();
            foreach (var r in prev) MarkDirty(r);
            foreach (var r in cur) MarkDirty(r);
            BuildClusters(8);
            foreach (var d in clusters) { Present(d); Stats.DirtyPx += (long)d.Width * d.Height; }
            long t3 = Stats.Now();
            Stats.Clear += t1 - t0; Stats.Paint += t2 - t1; Stats.Present += t3 - t2;
            blank = cur.Count == 0;
            var t = prev; prev = cur; cur = t;
        }

        readonly List<Rectangle> clusters = new List<Rectangle>();

        // Dirty pixels are gathered on a coarse tile grid: linear in the number of rectangles no matter
        // how many raindrops or flakes there are, then uploaded as a handful of row runs.
        const int Tile = 96;
        bool[] tiles;
        int tw, th;

        void MarkDirty(Rectangle r)
        {
            if (tiles == null) { tw = (Area.Width + Tile - 1) / Tile; th = (Area.Height + Tile - 1) / Tile; tiles = new bool[tw * th]; }
            r.Intersect(new Rectangle(0, 0, Area.Width, Area.Height));
            if (r.IsEmpty) return;
            int x0 = r.Left / Tile, x1 = (r.Right - 1) / Tile, y0 = r.Top / Tile, y1 = (r.Bottom - 1) / Tile;
            for (int ty = y0; ty <= y1; ty++)
                for (int tx = x0; tx <= x1; tx++) tiles[ty * tw + tx] = true;
        }

        void BuildClusters(int maxCount)
        {
            clusters.Clear();
            if (tiles == null) return;
            var rows = new List<Rectangle>[th];
            int total = 0;
            for (int ty = 0; ty < th; ty++)
            {
                rows[ty] = new List<Rectangle>();
                int tx = 0;
                while (tx < tw)
                {
                    if (!tiles[ty * tw + tx]) { tx++; continue; }
                    int s = tx;
                    while (tx < tw && tiles[ty * tw + tx]) { tiles[ty * tw + tx] = false; tx++; }
                    rows[ty].Add(Rectangle.FromLTRB(s * Tile, ty * Tile, Math.Min(Area.Width, tx * Tile), Math.Min(Area.Height, (ty + 1) * Tile)));
                    total++;
                }
            }
            if (total > maxCount)
                for (int ty = 0; ty < th; ty++)
                    if (rows[ty].Count > 1)
                    {
                        var u = rows[ty][0];
                        foreach (var r in rows[ty]) u = Rectangle.Union(u, r);
                        rows[ty].Clear(); rows[ty].Add(u);
                    }
            // Stack identical runs in consecutive rows into taller rectangles.
            foreach (var row in rows)
                foreach (var r in row)
                {
                    int k = clusters.FindIndex(c => c.Left == r.Left && c.Right == r.Right && c.Bottom == r.Top);
                    if (k >= 0) clusters[k] = Rectangle.Union(clusters[k], r); else clusters.Add(r);
                }
            while (clusters.Count > maxCount)
            {
                // Merge the two vertically closest.
                clusters.Sort((a, b) => a.Top.CompareTo(b.Top));
                int best = 0; long cost = long.MaxValue;
                for (int i = 0; i + 1 < clusters.Count; i++)
                {
                    var u = Rectangle.Union(clusters[i], clusters[i + 1]);
                    long cst = (long)u.Width * u.Height - (long)clusters[i].Width * clusters[i].Height - (long)clusters[i + 1].Width * clusters[i + 1].Height;
                    if (cst < cost) { cost = cst; best = i; }
                }
                clusters[best] = Rectangle.Union(clusters[best], clusters[best + 1]);
                clusters.RemoveAt(best + 1);
            }
        }
        public Bitmap Snapshot() { return Tests.DeepCopy(canvas); }

        unsafe void Clear(Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, Area.Width, Area.Height));
            if (r.IsEmpty) return;
            int stride = Area.Width * 4;
            for (int y = r.Top; y < r.Bottom; y++)
            {
                ulong* p = (ulong*)((byte*)bits + (long)y * stride + r.Left * 4);
                int n = r.Width;
                for (int i = 0; i < n / 2; i++) p[i] = 0;
                if ((n & 1) != 0) *(uint*)(p + n / 2) = 0;
            }
        }

        unsafe void Present(Rectangle dirty)
        {
            Native.GdiFlush();
            var dst = new Native.POINT(Area.X, Area.Y);
            var size = new Native.SIZE(Area.Width, Area.Height);
            var src = new Native.POINT(0, 0);
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            var rc = new Native.RECT { Left = dirty.Left, Top = dirty.Top, Right = dirty.Right, Bottom = dirty.Bottom };
            var info = new Native.UPDATELAYEREDWINDOWINFO();
            info.cbSize = sizeof(Native.UPDATELAYEREDWINDOWINFO);
            info.pptDst = (IntPtr)(&dst);
            info.psize = (IntPtr)(&size);
            info.hdcSrc = memDC;
            info.pptSrc = (IntPtr)(&src);
            info.pblend = (IntPtr)(&blend);
            info.dwFlags = Native.ULW_ALPHA;
            info.prcDirty = (IntPtr)(&rc);
            Native.UpdateLayeredWindowIndirect(Handle, ref info);
        }

        protected override void Dispose(bool disposing)
        {
            surface = null;
            if (canvas != null) { canvas.Dispose(); canvas = null; }
            if (memDC != IntPtr.Zero) { Native.SelectObject(memDC, oldBmp); Native.DeleteDC(memDC); memDC = IntPtr.Zero; }
            if (hBmp != IntPtr.Zero) { Native.DeleteObject(hBmp); hBmp = IntPtr.Zero; }
            base.Dispose(disposing);
        }
    }
}
