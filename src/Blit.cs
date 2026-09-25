using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Autumn
{
    // Premultiplied-ARGB pixels a sprite blitter can read.
    class Pixels
    {
        public uint[] Data;
        public int W, H;
        public float Cx, Cy;      // where the sprite's anchor (leaf centre etc.) sits inside it

        public static Pixels From(Bitmap b, float cx, float cy)
        {
            var p = new Pixels { W = b.Width, H = b.Height, Cx = cx, Cy = cy, Data = new uint[b.Width * b.Height] };
            var bd = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            var row = new int[b.Width];
            for (int y = 0; y < b.Height; y++)
            {
                Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, b.Width);
                Buffer.BlockCopy(row, 0, p.Data, y * b.Width * 4, b.Width * 4);
            }
            b.UnlockBits(bd);
            return p;
        }

        // A soft round dot: premultiplied colour with a smooth radial falloff (snow, glows, sparks).
        public static Pixels Dot(float radius, Color c, float hardness)
        {
            int half = (int)Math.Ceiling(radius) + 2;
            var p = new Pixels { W = half * 2, H = half * 2, Cx = half, Cy = half, Data = new uint[half * half * 4] };
            for (int y = 0; y < p.H; y++)
                for (int x = 0; x < p.W; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy) / radius;
                    if (d >= 1) continue;
                    float a = d < hardness ? 1f : 1f - (d - hardness) / (1f - hardness);
                    a = a * a * (3 - 2 * a);
                    p.Data[y * p.W + x] = Premul(c, a);
                }
            return p;
        }

        public static uint Premul(Color c, float alpha)
        {
            float a = c.A / 255f * alpha;
            uint A = (uint)(a * 255 + 0.5f);
            return (A << 24) | ((uint)(c.R * a + 0.5f) << 16) | ((uint)(c.G * a + 0.5f) << 8) | (uint)(c.B * a + 0.5f);
        }
    }

    // A 32bpp premultiplied target the blitter writes into directly.
    class Surface
    {
        public IntPtr Bits;
        public int W, H, Stride;
    }

    static unsafe class Blit
    {
        // Scale all four 8-bit channels by k/256 (k in 0..256) using two 16-bit lanes.
        public static uint Mul(uint c, uint k)
        {
            uint rb = ((c & 0x00FF00FFu) * k >> 8) & 0x00FF00FFu;
            uint ag = (((c >> 8) & 0x00FF00FFu) * k) & 0xFF00FF00u;
            return rb | ag;
        }

        static uint Tint(uint c, Light l)
        {
            uint r = ((c >> 16) & 0xFF) * l.R >> 8, g = ((c >> 8) & 0xFF) * l.G >> 8, b = (c & 0xFF) * l.B >> 8;
            return (c & 0xFF000000u) | (r << 16) | (g << 8) | b;
        }

        static void Over(uint* d, uint c)
        {
            uint a = c >> 24;
            if (a >= 255) { *d = c; return; }
            *d = c + Mul(*d, 256 - (a + (a >> 7)));
        }

        // Light adds: channels and alpha accumulate and saturate, so glows brighten each other.
        static void Add(uint* d, uint c)
        {
            uint s = *d;
            uint a = Math.Min(255u, (s >> 24) + (c >> 24));
            uint r = Math.Min(255u, ((s >> 16) & 0xFF) + ((c >> 16) & 0xFF));
            uint g = Math.Min(255u, ((s >> 8) & 0xFF) + ((c >> 8) & 0xFF));
            uint b = Math.Min(255u, (s & 0xFF) + (c & 0xFF));
            r = Math.Min(r, a); g = Math.Min(g, a); b = Math.Min(b, a);
            *d = (a << 24) | (r << 16) | (g << 8) | b;
        }

        static uint* Row(Surface s, int y) { return (uint*)((byte*)s.Bits + (long)y * s.Stride); }

        // Straight copy-with-blend at an integer offset (resting leaves, cached sprites).
        public static Rectangle Draw(Surface s, Pixels p, int dx, int dy, uint alpha, Light l)
        {
            int x0 = Math.Max(0, dx), y0 = Math.Max(0, dy), x1 = Math.Min(s.W, dx + p.W), y1 = Math.Min(s.H, dy + p.H);
            if (x0 >= x1 || y0 >= y1) return Rectangle.Empty;
            bool tint = !l.White;
            fixed (uint* sp = p.Data)
                for (int y = y0; y < y1; y++)
                {
                    uint* d = Row(s, y) + x0;
                    uint* q = sp + (y - dy) * p.W + (x0 - dx);
                    for (int x = x0; x < x1; x++, d++, q++)
                    {
                        uint c = *q;
                        if (c == 0) continue;
                        if (alpha < 256) c = Mul(c, alpha);
                        if (tint) c = Tint(c, l);
                        Over(d, c);
                    }
                }
            return Rectangle.FromLTRB(x0, y0, x1, y1);
        }

        // As Draw, but leaves out pixels inside any of the hidden rectangles (windows in front).
        public static Rectangle DrawClipped(Surface s, Pixels p, int dx, int dy, uint alpha, Light l, List<Rectangle> hidden)
        {
            int x0 = Math.Max(0, dx), y0 = Math.Max(0, dy), x1 = Math.Min(s.W, dx + p.W), y1 = Math.Min(s.H, dy + p.H);
            if (x0 >= x1 || y0 >= y1) return Rectangle.Empty;
            var box = Rectangle.FromLTRB(x0, y0, x1, y1);
            foreach (var h in hidden) if (h.Contains(box)) return Rectangle.Empty;
            bool tint = !l.White;
            fixed (uint* sp = p.Data)
                for (int y = y0; y < y1; y++)
                {
                    uint* d = Row(s, y) + x0;
                    uint* q = sp + (y - dy) * p.W + (x0 - dx);
                    for (int x = x0; x < x1; x++, d++, q++)
                    {
                        uint c = *q;
                        if (c == 0) continue;
                        bool covered = false;
                        foreach (var h in hidden) if (x >= h.Left && x < h.Right && y >= h.Top && y < h.Bottom) { covered = true; break; }
                        if (covered) continue;
                        if (alpha < 256) c = Mul(c, alpha);
                        if (tint) c = Tint(c, l);
                        Over(d, c);
                    }
                }
            return box;
        }

        // Bilinear, affine-transformed blend. The matrix maps sprite pixel coordinates to surface
        // coordinates, in System.Drawing's element order. additive=true accumulates light instead.
        public static Rectangle DrawAffine(Surface s, Pixels p, float[] e, uint alpha, Light l, bool additive)
        {
            float m11 = e[0], m12 = e[1], m21 = e[2], m22 = e[3], tx = e[4], ty = e[5];
            float det = m11 * m22 - m12 * m21;
            if (Math.Abs(det) < 1e-4f) return Rectangle.Empty;
            float i11 = m22 / det, i12 = -m12 / det, i21 = -m21 / det, i22 = m11 / det;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                float u = (k & 1) * p.W, v = (k >> 1) * p.H;
                float x = m11 * u + m21 * v + tx, y = m12 * u + m22 * v + ty;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            int x0 = Math.Max(0, (int)Math.Floor(minX)), y0 = Math.Max(0, (int)Math.Floor(minY));
            int x1 = Math.Min(s.W, (int)Math.Ceiling(maxX) + 1), y1 = Math.Min(s.H, (int)Math.Ceiling(maxY) + 1);
            if (x0 >= x1 || y0 >= y1) return Rectangle.Empty;

            bool tint = !l.White;
            int W = p.W, H = p.H;
            fixed (uint* sp = p.Data)
                for (int y = y0; y < y1; y++)
                {
                    float fy = y + 0.5f - ty, fx = x0 + 0.5f - tx;
                    float u = i11 * fx + i21 * fy - 0.5f, v = i12 * fx + i22 * fy - 0.5f;
                    uint* d = Row(s, y) + x0;
                    for (int x = x0; x < x1; x++, d++, u += i11, v += i12)
                    {
                        if (u <= -1 || v <= -1 || u >= W || v >= H) continue;
                        int iu = (int)(u + 1) - 1, iv = (int)(v + 1) - 1;
                        uint fu = (uint)((u - iu) * 256), fv = (uint)((v - iv) * 256);
                        uint c00 = 0, c10 = 0, c01 = 0, c11 = 0;
                        bool u0 = iu >= 0, u1 = iu + 1 < W, v0 = iv >= 0, v1 = iv + 1 < H;
                        uint* row = sp + iv * W + iu;
                        if (v0) { if (u0) c00 = row[0]; if (u1) c10 = row[1]; }
                        if (v1) { if (u0) c01 = row[W]; if (u1) c11 = row[W + 1]; }
                        if ((c00 | c10 | c01 | c11) == 0) continue;
                        uint top = Mul(c00, 256 - fu) + Mul(c10, fu);
                        uint bot = Mul(c01, 256 - fu) + Mul(c11, fu);
                        uint c = Mul(top, 256 - fv) + Mul(bot, fv);
                        if (alpha < 256) c = Mul(c, alpha);
                        if (c == 0) continue;
                        if (tint) c = Tint(c, l);
                        if (additive) Add(d, c); else Over(d, c);
                    }
                }
            return Rectangle.FromLTRB(x0, y0, x1, y1);
        }

        static readonly float[] tmp = new float[6];
        static int lineArgb;
        static uint lineFull;

        // Sub-pixel placement without rotation (snowflakes, glows): anchor lands exactly on (x, y).
        public static Rectangle DrawAt(Surface s, Pixels p, float x, float y, float scale, uint alpha, Light l, bool additive)
        {
            tmp[0] = scale; tmp[1] = 0; tmp[2] = 0; tmp[3] = scale;
            tmp[4] = x - p.Cx * scale; tmp[5] = y - p.Cy * scale;
            return DrawAffine(s, p, tmp, alpha, l, additive);
        }

        // Anti-aliased line whose alpha ramps from a0 at the start to a1 at the end (rain streaks,
        // meteor tails, lightning). colour is straight RGB; alphas are 0..1.
        public static Rectangle Line(Surface s, float x0, float y0, float x1, float y1, Color c, float a0, float a1, bool additive)
        {
            float dx = x1 - x0, dy = y1 - y0;
            bool steep = Math.Abs(dy) > Math.Abs(dx);
            int n = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
            if (n < 1) n = 1;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float px = x0 + dx * t, py = y0 + dy * t;
                float a = a0 + (a1 - a0) * t;
                if (a <= 0.004f) continue;
                if (steep)
                {
                    int yy = (int)Math.Floor(py);
                    if (yy < 0 || yy >= s.H) continue;
                    int xx = (int)Math.Floor(px - 0.5f);
                    float f = px - 0.5f - xx;
                    Plot(s, xx, yy, c, a * (1 - f), additive, ref minX, ref minY, ref maxX, ref maxY);
                    Plot(s, xx + 1, yy, c, a * f, additive, ref minX, ref minY, ref maxX, ref maxY);
                }
                else
                {
                    int xx = (int)Math.Floor(px);
                    if (xx < 0 || xx >= s.W) continue;
                    int yy = (int)Math.Floor(py - 0.5f);
                    float f = py - 0.5f - yy;
                    Plot(s, xx, yy, c, a * (1 - f), additive, ref minX, ref minY, ref maxX, ref maxY);
                    Plot(s, xx, yy + 1, c, a * f, additive, ref minX, ref minY, ref maxX, ref maxY);
                }
            }
            if (minX > maxX) return Rectangle.Empty;
            return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        static void Plot(Surface s, int x, int y, Color c, float a, bool additive, ref int minX, ref int minY, ref int maxX, ref int maxY)
        {
            if (x < 0 || y < 0 || x >= s.W || y >= s.H || a <= 0.004f) return;
            if (c.ToArgb() != lineArgb) { lineArgb = c.ToArgb(); lineFull = Pixels.Premul(c, 1f); }
            uint pc = Mul(lineFull, (uint)(Math.Min(1f, a) * 256));
            uint* d = Row(s, y) + x;
            if (additive) Add(d, pc); else Over(d, pc);
            if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
        }

        // Vertical run of one colour with alpha (snowpack columns, aurora curtains, flashes).
        public static void Span(Surface s, int x, int yTop, int yBottom, uint premul, bool additive)
        {
            if (x < 0 || x >= s.W) return;
            yTop = Math.Max(0, yTop); yBottom = Math.Min(s.H, yBottom);
            for (int y = yTop; y < yBottom; y++)
            {
                uint* d = Row(s, y) + x;
                if (additive) Add(d, premul); else Over(d, premul);
            }
        }

        // Additive vertical gradient: lut[i] is a premultiplied colour for v = i / scale - offset, where
        // v = (yBase - y) / height. k scales the whole column (0..256). Used for aurora curtains.
        public static void AddColumn(Surface s, int x, int width, int yTop, int yBottom, float yBase, float height, uint[] lut, float scale, float offset, uint k)
        {
            yTop = Math.Max(0, yTop); yBottom = Math.Min(s.H, yBottom);
            int x0 = Math.Max(0, x), x1 = Math.Min(s.W, x + width);
            if (x0 >= x1) return;
            for (int y = yTop; y < yBottom; y++)
            {
                int i = (int)(((yBase - y) / height + offset) * scale);
                if (i < 0 || i >= lut.Length) continue;
                uint c = lut[i];
                if (c == 0) continue;
                if (k < 256) c = Mul(c, k);
                uint* d = Row(s, y) + x0;
                for (int xx = x0; xx < x1; xx++, d++) Add(d, c);
            }
        }

        public static void Pixel(Surface s, int x, int y, uint premul, bool additive)
        {
            if (x < 0 || y < 0 || x >= s.W || y >= s.H) return;
            uint* d = Row(s, y) + x;
            if (additive) Add(d, premul); else Over(d, premul);
        }

        // Whole-rectangle wash (lightning flash). Returns the touched rectangle.
        public static Rectangle Fill(Surface s, Rectangle r, uint premul)
        {
            r.Intersect(new Rectangle(0, 0, s.W, s.H));
            if (r.IsEmpty) return r;
            for (int y = r.Top; y < r.Bottom; y++)
            {
                uint* d = Row(s, y) + r.Left;
                for (int x = r.Left; x < r.Right; x++, d++) Over(d, premul);
            }
            return r;
        }
    }
}
