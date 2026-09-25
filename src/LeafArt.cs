using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Autumn
{
    enum LeafKind { Maple = 0, Oak = 1, Birch = 2, Ginkgo = 3, Petal = 4, Blossom = 5 }

    // Procedural leaf geometry. Every shape is built in "raw" space (petiole attachment at the origin,
    // leaf pointing up) and then normalized so its largest extent is 1 and its box is centered on 0,0.
    class LeafShape
    {
        public PointF[] Outline;
        public List<PointF[]> Veins = new List<PointF[]>();
        public PointF[] Stem;
        public PointF Base, Tip;          // gradient runs base -> tip
        public float HalfW, HalfH;        // normalized half extents of the outline
        public bool IsFlower;
        public PointF Center;             // flower centre after normalization

        static LeafShape[] shapes;
        public static LeafShape Get(LeafKind k)
        {
            if (shapes == null)
                shapes = new LeafShape[] { BuildMaple(), BuildOak(), BuildBirch(), BuildGinkgo(), BuildPetal(), BuildBlossom() };
            return shapes[(int)k];
        }

        static PointF Polar(double deg, double r)
        {
            double a = deg * Math.PI / 180.0;
            return new PointF((float)(r * Math.Sin(a)), (float)(-r * Math.Cos(a)));
        }

        // Densify a polar keyframe list so lobes read as straight serrated edges rather than spokes.
        static List<PointF> PolarPath(double[] kf, bool mirror)
        {
            var right = new List<PointF>();
            for (int i = 0; i + 3 < kf.Length; i += 2)
            {
                for (int s = 0; s < 3; s++)
                {
                    double t = s / 3.0;
                    PointF p0 = Polar(kf[i], kf[i + 1]), p1 = Polar(kf[i + 2], kf[i + 3]);
                    right.Add(new PointF((float)(p0.X + (p1.X - p0.X) * t), (float)(p0.Y + (p1.Y - p0.Y) * t)));
                }
            }
            right.Add(Polar(kf[kf.Length - 2], kf[kf.Length - 1]));
            if (!mirror) return right;
            var all = new List<PointF>(right);
            for (int i = right.Count - 2; i > 0; i--) all.Add(new PointF(-right[i].X, right[i].Y));
            return all;
        }

        static LeafShape BuildMaple()
        {
            // angle-from-up (deg), radius. Right half, from the central tip clockwise to the petiole notch.
            double[] kf = {
                0, 1.00,  5, 0.81,  8.5, 0.87,  12, 0.69,  15, 0.73,  19, 0.54,  22, 0.45,  25, 0.44,
                28, 0.48,  33, 0.67,  36, 0.71,  40, 0.73,  46, 0.86,  49, 0.81,  54, 0.97,  59, 0.79,
                63, 0.81,  68, 0.65,  72, 0.63,  78, 0.43,  82, 0.37,  86, 0.39,  92, 0.51,  95, 0.48,
                104, 0.62,  110, 0.46,  118, 0.39,  126, 0.29,  140, 0.17,  160, 0.09,  180, 0.06 };
            var s = new LeafShape();
            var outline = PolarPath(kf, true);
            var veins = new List<PointF[]>();
            foreach (double a in new double[] { 0, 54, -54, 104, -104 })
            {
                double r = Math.Abs(a) > 90 ? 0.55 : (a == 0 ? 0.93 : 0.88);
                veins.Add(new[] { new PointF(0, 0), Polar(a * 0.35, r * 0.35), Polar(a, r) });
            }
            foreach (double sign in new double[] { 1, -1 })
            {
                veins.Add(new[] { Polar(0, 0.42), Polar(sign * 14, 0.68) });
                veins.Add(new[] { Polar(sign * 54, 0.42), Polar(sign * 40, 0.69) });
                veins.Add(new[] { Polar(sign * 54, 0.48), Polar(sign * 66, 0.62) });
            }
            s.Finish(outline, veins, MakeStem(0.34, 0.05), new PointF(0, 0), Polar(0, 1));
            return s;
        }

        static LeafShape BuildOak()
        {
            var right = new List<PointF>();
            var left = new List<PointF>();
            const int N = 90;
            for (int i = 0; i <= N; i++)
            {
                double t = i / (double)N;
                double env = Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.85)), 0.8) * (1 - 0.15 * t);
                double lobeR = 0.48 + 0.52 * Math.Pow(Math.Abs(Math.Sin(Math.PI * (4.0 * t + 0.15))), 0.55);
                double lobeL = 0.48 + 0.52 * Math.Pow(Math.Abs(Math.Sin(Math.PI * (4.0 * t + 0.32))), 0.55);
                double y = -t;
                right.Add(new PointF((float)(0.34 * env * lobeR), (float)y));
                left.Add(new PointF((float)(-0.34 * env * lobeL), (float)y));
            }
            var outline = new List<PointF>(right);
            for (int i = left.Count - 2; i > 0; i--) outline.Add(left[i]);
            var veins = new List<PointF[]>();
            veins.Add(new[] { new PointF(0, 0), new PointF(0.005f, -0.5f), new PointF(0, -0.95f) });
            for (int k = 0; k < 4; k++)
            {
                double tr = (k + 0.5 - 0.15) / 4.0, tl = (k + 0.5 - 0.32) / 4.0;
                if (tr > 0.06 && tr < 0.95) veins.Add(new[] { new PointF(0, (float)(-tr + 0.07)), right[(int)(tr * N)] });
                if (tl > 0.06 && tl < 0.95) veins.Add(new[] { new PointF(0, (float)(-tl + 0.07)), left[(int)(tl * N)] });
            }
            var s = new LeafShape();
            s.Finish(outline, veins, MakeStem(0.12, 0.02), new PointF(0, 0), new PointF(0, -1));
            return s;
        }

        static LeafShape BuildBirch()
        {
            var right = new List<PointF>();
            const int N = 80;
            for (int i = 0; i <= N; i++)
            {
                double t = i / (double)N;
                double w = 0.31 * Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.72)), 0.85) * (1 - 0.1 * t);
                double saw = (t * 24) % 1.0;             // forward-leaning teeth
                w += 0.022 * Math.Sin(Math.PI * t) * (saw < 0.7 ? saw / 0.7 : (1 - saw) / 0.3) - 0.011;
                right.Add(new PointF((float)Math.Max(0, w), (float)-t));
            }
            var outline = new List<PointF>(right);
            for (int i = right.Count - 2; i > 0; i--) outline.Add(new PointF(-right[i].X, right[i].Y));
            var veins = new List<PointF[]>();
            veins.Add(new[] { new PointF(0, 0), new PointF(0, -0.93f) });
            for (int k = 1; k <= 6; k++)
            {
                float t = 0.1f + k * 0.12f;
                int j = Math.Min(N, (int)((t + 0.12f) * N));
                float wx = right[j].X * 0.85f;
                veins.Add(new[] { new PointF(0, -t), new PointF(wx, -(t + 0.12f)) });
                veins.Add(new[] { new PointF(0, -t), new PointF(-wx, -(t + 0.12f)) });
            }
            var s = new LeafShape();
            s.Finish(outline, veins, MakeStem(0.22, 0.03), new PointF(0, 0), new PointF(0, -1));
            return s;
        }

        static LeafShape BuildGinkgo()
        {
            var right = new List<PointF>();
            right.Add(Polar(0, 0.64));                                  // the notch between the two lobes
            for (double a = 2.5; a <= 64; a += 1.5)
            {
                double r = 1.0 - 0.30 * Math.Exp(-Math.Pow((a - 2.5) / 4.5, 2)) + 0.035 * Math.Sin(a * 0.42);
                r -= 0.06 * Math.Pow(a / 64.0, 3);
                right.Add(Polar(a, r));
            }
            right.Add(Polar(64.5, 0.62));
            right.Add(Polar(62, 0.32));
            right.Add(Polar(52, 0.12));
            right.Add(new PointF(0.012f, 0.0f));
            var outline = new List<PointF>(right);
            for (int i = right.Count - 1; i > 0; i--) outline.Add(new PointF(-right[i].X, right[i].Y));
            var veins = new List<PointF[]>();
            for (int k = -5; k <= 5; k++)
            {
                if (k == 0) continue;
                double a = k * 11.5;
                veins.Add(new[] { new PointF(0, 0), Polar(a * 0.9, 0.45), Polar(a, 0.9) });
            }
            var s = new LeafShape();
            s.Finish(outline, veins, MakeStem(0.46, 0.06), new PointF(0, 0), Polar(0, 1));
            return s;
        }

        static LeafShape BuildPetal()
        {
            // Cherry petal: narrow claw at the base, broad rounded top with the characteristic notch.
            var right = new List<PointF> {
                new PointF(0, 0), new PointF(0.07f, -0.07f), new PointF(0.17f, -0.22f), new PointF(0.25f, -0.42f),
                new PointF(0.29f, -0.60f), new PointF(0.285f, -0.74f), new PointF(0.25f, -0.86f), new PointF(0.18f, -0.95f),
                new PointF(0.10f, -0.99f), new PointF(0.04f, -0.985f), new PointF(0, -0.92f) };
            var outline = new List<PointF>(right);
            for (int i = right.Count - 2; i > 0; i--) outline.Add(new PointF(-right[i].X, right[i].Y));
            var veins = new List<PointF[]> {
                new[] { new PointF(0, -0.05f), new PointF(0, -0.8f) },
                new[] { new PointF(0, -0.05f), new PointF(0.12f, -0.72f) },
                new[] { new PointF(0, -0.05f), new PointF(-0.12f, -0.72f) } };
            var s = new LeafShape();
            s.Finish(outline, veins, MakeStem(0.02, 0), new PointF(0, 0), new PointF(0, -1));
            return s;
        }

        static LeafShape BuildBlossom()
        {
            // Five notched petals as one outline, around the flower's centre.
            var outline = new List<PointF>();
            for (int d = 0; d < 360; d += 2)
            {
                double delta = ((d + 36) % 72) - 36;
                double r = 0.38 + 0.62 * Math.Pow(Math.Cos(delta / 36.0 * Math.PI / 2), 0.55);
                r -= 0.12 * Math.Exp(-Math.Pow(delta / 4.5, 2));
                outline.Add(Polar(d, r));
            }
            var veins = new List<PointF[]>();
            for (int k = 0; k < 5; k++) veins.Add(new[] { Polar(k * 72, 0.16), Polar(k * 72, 0.72) });
            var s = new LeafShape();
            s.Finish(outline, veins, MakeStem(0.01, 0), new PointF(0, 0), Polar(0, 1));
            s.IsFlower = true;
            return s;
        }

        static PointF[] MakeStem(double len, double bend)
        {
            return new[] { new PointF(0, 0), new PointF((float)(bend * 0.3), (float)(len * 0.5)), new PointF((float)bend, (float)len) };
        }

        void Finish(List<PointF> outline, List<PointF[]> veins, PointF[] stem, PointF basePt, PointF tipPt)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in outline)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            float k = 1f / Math.Max(maxX - minX, maxY - minY);
            float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            Func<PointF, PointF> T = p => new PointF((p.X - cx) * k, (p.Y - cy) * k);
            Outline = outline.ConvertAll(p => T(p)).ToArray();
            foreach (var v in veins) Veins.Add(Array.ConvertAll(v, p => T(p)));
            Stem = Array.ConvertAll(stem, p => T(p));
            Base = T(basePt); Tip = T(tipPt); Center = T(new PointF(0, 0));
            HalfW = (maxX - minX) * k / 2; HalfH = (maxY - minY) * k / 2;
        }
    }

    // One leaf's colouring: gradients for both faces, veins, blemishes, and cached brushes/pens.
    class LeafLook : IDisposable
    {
        public LeafKind Kind;
        public LeafShape Shape;
        public float Size;                 // largest extent, device pixels
        public Color BaseColor, TipColor;
        Brush front, back;
        Pen edge, vein, stem;
        struct Spot { public float X, Y, R; public Color C; }
        Spot[] spots;

        static readonly string[][] Palettes = {
            new[] { "8E1B12:D6361E", "B8321A:F0662A", "C4501A:F29A2E", "A62C18:E8B03A", "7A8A2A:E07A24", "9C1A1A:E8452C" },
            new[] { "5E3317:9A5A2A", "7A3E12:B8682A", "6B2A14:A8452A", "6E4A1E:B88A3A" },
            new[] { "C9A21A:F5D84A", "B8901A:F2C94C", "A89A2A:F0D050", "C47E14:F4C43C" },
            new[] { "D9A91C:FBE36A", "C99A10:F7D84A", "B9A41E:F6DC5A" },
            new[] { "F4A7BB:FFE3EA", "F7B9C9:FFF0F4", "EE9FB5:FFD9E3", "F9C9D6:FFFFFF" },
            new[] { "EF8AA6:FFF3F6", "F5A3B8:FFFFFF", "F7B3C4:FFF8FA" },
        };
        static readonly string[] Summer = { "2E6B1F:6FA83A", "3B7A26:8DBA4A", "2F5E22:5E9A34", "4A8A2C:A3C95A" };
        static readonly string[] Winter = { "6B5A45:A08B6A", "5C4A38:8E7657", "7A6650:B09C7C" };
        public int Tone;       // 0 seasonal, 1 summer green, 2 dry winter oak, 3 golden

        public LeafLook(LeafKind kind, float sizePx, Random rng) : this(kind, sizePx, rng, 0) { }

        public LeafLook(LeafKind kind, float sizePx, Random rng, int tone)
        {
            Kind = kind; Shape = LeafShape.Get(kind); Size = sizePx; Tone = tone;
            string[] pal = tone == 1 ? Summer : tone == 2 ? Winter : tone == 3 ? new[] { "B8860B:FFF1A8" } : Palettes[(int)kind];
            string[] pair = pal[rng.Next(pal.Length)].Split(':');
            int jit = tone == 3 ? 0 : 1;
            BaseColor = Jitter(Hex(pair[0]), rng, 14 * jit); TipColor = Jitter(Hex(pair[1]), rng, 18 * jit);

            bool delicate = kind == LeafKind.Petal || kind == LeafKind.Blossom || tone == 3;
            int nSpots = !delicate && rng.NextDouble() < 0.35 ? 1 + rng.Next(3) : 0;
            spots = new Spot[nSpots];
            for (int i = 0; i < nSpots; i++)
            {
                spots[i].X = (float)(rng.NextDouble() - 0.5) * Shape.HalfW * 1.1f;
                spots[i].Y = (float)(rng.NextDouble() - 0.5) * Shape.HalfH * 1.1f;
                spots[i].R = (float)(0.025 + rng.NextDouble() * 0.05);
                spots[i].C = kind == LeafKind.Maple && rng.NextDouble() < 0.5 ? Color.FromArgb(150, 30, 18, 10) : Color.FromArgb(90, 70, 35, 10);
            }
            BuildPaint(255);
        }

        int builtAlpha = -1;
        void BuildPaint(int alpha)
        {
            DisposePaint();
            builtAlpha = alpha;
            Color b = WithA(BaseColor, alpha), t = WithA(TipColor, alpha);
            if (Shape.IsFlower)
            {
                // Blossoms: deep pink at the heart fading to white at the petal edges.
                var path = new GraphicsPath();
                path.AddPolygon(Shape.Outline);
                var pg = new PathGradientBrush(path);
                pg.CenterPoint = Shape.Center;
                pg.CenterColor = b;
                pg.SurroundColors = new[] { t };
                front = pg;
            }
            else
            {
                var lg = new LinearGradientBrush(Shape.Base, Shape.Tip, b, t);
                lg.WrapMode = WrapMode.TileFlipXY;
                front = lg;
            }
            Color bb = Mix(BaseColor, Color.FromArgb(214, 196, 160), 0.38f), bt = Mix(TipColor, Color.FromArgb(226, 208, 170), 0.42f);
            var lb = new LinearGradientBrush(Shape.Base, Shape.Tip, WithA(bb, alpha), WithA(bt, alpha));
            lb.WrapMode = WrapMode.TileFlipXY;
            back = lb;
            float px = 1f / Size;                                    // one device pixel in leaf units
            bool soft = Kind == LeafKind.Petal || Kind == LeafKind.Blossom;
            Color edgeColor = Tone == 3 ? Hex("8A5A00") : soft ? Mix(BaseColor, Color.FromArgb(160, 60, 90), 0.35f) : Mix(BaseColor, Color.Black, 0.45f);
            edge = new Pen(WithA(edgeColor, alpha * (soft ? 90 : 150) / 255), (soft ? 0.7f : 0.9f) * px);
            edge.LineJoin = LineJoin.Round;
            bool lightVeins = Kind != LeafKind.Oak;
            vein = new Pen(lightVeins ? WithA(Mix(TipColor, Color.FromArgb(255, 236, 170), 0.55f), alpha * 150 / 255)
                                      : WithA(Mix(BaseColor, Color.Black, 0.35f), alpha * 140 / 255),
                           (Kind == LeafKind.Ginkgo ? 0.6f : 0.9f) * px);
            vein.StartCap = LineCap.Round; vein.EndCap = LineCap.Round;
            stem = new Pen(WithA(Mix(BaseColor, Color.FromArgb(90, 60, 30), 0.5f), alpha), 1.5f * px);
            stem.StartCap = LineCap.Round; stem.EndCap = LineCap.Round;
        }

        public void Recolor(Color baseColor, Color tipColor, bool clearSpots)
        {
            BaseColor = baseColor; TipColor = tipColor;
            if (clearSpots) spots = new Spot[0];
            BuildPaint(255);
        }

        // A single upright leaf, used for the tray and program icons.
        public static Bitmap RenderIcon(int px)
        {
            var look = new LeafLook(LeafKind.Maple, px * 0.94f, new Random(3));
            look.Recolor(Hex("B8321A"), Hex("F29A2E"), true);
            var bmp = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var m = new Matrix())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Compose(m, px / 2f, px / 2f, -8, px * 0.94f, 0, 1, 1, 0);
                g.Transform = m;
                look.DrawLocal(g, false);
            }
            look.Dispose();
            return bmp;
        }

        public void SetAlpha(int alpha)
        {
            alpha = Math.Max(0, Math.Min(255, alpha)) & ~15 | (alpha >= 255 ? 15 : 0);
            if (alpha != builtAlpha) BuildPaint(alpha);
        }

        // Draw with the caller-provided transform already applied (leaf units -> device pixels).
        public void DrawLocal(Graphics g, bool backFace)
        {
            g.DrawLines(stem, Shape.Stem);
            g.FillPolygon(backFace ? back : front, Shape.Outline);
            foreach (var s in spots)
                using (var br = new SolidBrush(builtAlpha >= 255 ? s.C : WithA(s.C, s.C.A * builtAlpha / 255)))
                    g.FillEllipse(br, s.X - s.R, s.Y - s.R, s.R * 2, s.R * 2);
            foreach (var v in Shape.Veins) g.DrawLines(vein, v);
            g.DrawPolygon(edge, Shape.Outline);
            if (Shape.IsFlower)
            {
                float r = 0.1f, cx = Shape.Center.X, cy = Shape.Center.Y;
                using (var yb = new SolidBrush(WithA(Hex("F2C14E"), builtAlpha)))
                using (var sb = new SolidBrush(WithA(Hex("B5485F"), builtAlpha)))
                {
                    g.FillEllipse(yb, cx - r, cy - r, r * 2, r * 2);
                    for (int k = 0; k < 8; k++)
                    {
                        double a = k * Math.PI / 4 + 0.2;
                        float sx = cx + (float)Math.Cos(a) * 0.19f, sy = cy + (float)Math.Sin(a) * 0.19f;
                        g.FillEllipse(sb, sx - 0.028f, sy - 0.028f, 0.056f, 0.056f);
                    }
                }
            }
        }

        // Screen-space transform for a leaf at (x,y): in-plane rotation, then a tumble about an axis
        // in the leaf's own frame (cos of the tumble angle squashes it; negative shows the back).
        public static void Compose(Matrix m, float x, float y, float angleDeg, float size, float tumbleAxisDeg, float tumbleScale, float squashY, float tiltDeg)
        {
            m.Reset();
            m.Translate(x, y);
            if (tiltDeg != 0) m.Rotate(tiltDeg);
            if (squashY != 1) m.Scale(1, squashY);
            m.Rotate(angleDeg);
            m.Scale(size, size);
            if (tumbleScale != 1)
            {
                m.Rotate(tumbleAxisDeg);
                m.Scale(Math.Abs(tumbleScale) < 0.04f ? 0.04f * Math.Sign(tumbleScale + 1e-6f) : tumbleScale, 1);
                m.Rotate(-tumbleAxisDeg);
            }
        }

        // Pre-render a resting leaf (lying on a ledge, seen from a low angle) into its own sprite.
        public Bitmap RenderRest(float yawDeg, float squash, float tiltDeg, bool flipped, out PointF origin, out float bottom, out float halfSpan)
        {
            var m = new Matrix();
            Compose(m, 0, 0, yawDeg, Size, 90, flipped ? -1 : 1, squash, tiltDeg);
            var pts = (PointF[])Shape.Outline.Clone();
            m.TransformPoints(pts);
            var stemPts = (PointF[])Shape.Stem.Clone();
            m.TransformPoints(stemPts);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
            foreach (var p in stemPts) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
            float leafBottom = float.MinValue, leafMinX = float.MaxValue, leafMaxX = float.MinValue;
            foreach (var p in pts) { leafBottom = Math.Max(leafBottom, p.Y); leafMinX = Math.Min(leafMinX, p.X); leafMaxX = Math.Max(leafMaxX, p.X); }
            int w = (int)Math.Ceiling(maxX - minX) + 4, h = (int)Math.Ceiling(maxY - minY) + 4;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            origin = new PointF(-minX + 2, -minY + 2);       // where the leaf center lands inside the sprite
            bottom = leafBottom;                              // leaf's lowest point relative to its center
            halfSpan = (leafMaxX - leafMinX) / 2;
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                m.Reset();
                Compose(m, origin.X, origin.Y, yawDeg, Size, 90, flipped ? -1 : 1, squash, tiltDeg);
                g.Transform = m;
                DrawLocal(g, flipped);
            }
            m.Dispose();
            return bmp;
        }

        // The leaf pre-rendered face-on at its real size; airborne leaves are drawn from these with
        // Blit.DrawAffine, several times cheaper than re-rasterizing the vector outline every frame.
        Pixels frontFace, backFace;
        public Pixels Face(bool back)
        {
            Pixels p = back ? backFace : frontFace;
            if (p != null) return p;
            float ext = 0;
            foreach (var q in Shape.Outline) ext = Math.Max(ext, Math.Max(Math.Abs(q.X), Math.Abs(q.Y)));
            foreach (var q in Shape.Stem) ext = Math.Max(ext, Math.Max(Math.Abs(q.X), Math.Abs(q.Y)));
            int half = (int)Math.Ceiling(ext * Size) + 3;
            using (var b = new Bitmap(half * 2, half * 2, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(b))
                using (var m = new Matrix())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    Compose(m, half, half, 0, Size, 0, 1, 1, 0);
                    g.Transform = m;
                    DrawLocal(g, back);
                }
                p = Pixels.From(b, half, half);
            }
            if (back) backFace = p; else frontFace = p;
            return p;
        }
        void DisposePaint()
        {
            if (front != null) front.Dispose();
            if (back != null) back.Dispose();
            if (edge != null) edge.Dispose();
            if (vein != null) vein.Dispose();
            if (stem != null) stem.Dispose();
            front = back = null; edge = vein = stem = null;
        }

        public void Dispose()
        {
            DisposePaint();
            frontFace = backFace = null;
        }

        public static Color Hex(string s)
        {
            int v = Convert.ToInt32(s, 16);
            return Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
        }
        public static Color WithA(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B); }
        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        static Color Jitter(Color c, Random r, int amt)
        {
            Func<int, int> j = v => Math.Max(0, Math.Min(255, v + r.Next(-amt, amt + 1)));
            return Color.FromArgb(255, j(c.R), j(c.G), j(c.B));
        }
    }
}
