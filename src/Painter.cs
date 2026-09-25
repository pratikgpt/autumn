using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Autumn
{
    // Draws the world into one monitor-sized surface, layer by layer. Records every rectangle it
    // touches so the overlay can clear and upload only those pixels next frame.
    class Painter : IDisposable
    {
        readonly Matrix m = new Matrix();
        readonly Dictionary<WinInfo, int> z = new Dictionary<WinInfo, int>();
        readonly List<Rectangle> hide = new List<Rectangle>();

        public void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            z.Clear();
            for (int i = 0; i < w.Wins.Count; i++) z[w.Wins[i]] = i;
            bool restDone = false, airDone = false;
            foreach (var sys in w.Systems)
            {
                if (!restDone && sys.Layer >= 10) { PaintResting(s, w, area, drawn); restDone = true; }
                if (!airDone && sys.Layer >= 30) { PaintAirborne(s, w, area, drawn); airDone = true; }
                sys.Paint(s, w, area, drawn);
            }
            if (!restDone) PaintResting(s, w, area, drawn);
            if (!airDone) PaintAirborne(s, w, area, drawn);
        }

        // Windows in front of a ledge that straddle it, in area-local coordinates.
        public List<Rectangle> Occluders(World w, WinInfo host, Rectangle area)
        {
            hide.Clear();
            int hz;
            if (!z.TryGetValue(host, out hz)) hz = host.IsGround ? int.MaxValue : -1;
            var band = Rectangle.FromLTRB(host.Left - 60, host.Top - 160, host.Right + 60, host.Top + 12);
            for (int i = 0; i < w.Wins.Count && i < hz; i++)
            {
                var o = w.Wins[i];
                if (!Ledges.Covers(o, host)) continue;
                var or = Rectangle.FromLTRB(o.Left, o.Top, o.Right, o.Bottom);
                if (!or.IntersectsWith(band)) continue;
                or.Offset(-area.Left, -area.Top);
                hide.Add(or);
            }
            return hide;
        }

        void PaintResting(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            var light = w.Sky.Light;
            foreach (var kv in w.Piles)
            {
                var host = kv.Key;
                var pile = kv.Value;
                if (pile.Count == 0) continue;
                if (!z.ContainsKey(host) && !host.IsGround) continue;
                var band = Rectangle.FromLTRB(host.Left - 60, host.Top - 160, host.Right + 60, host.Top + 12);
                if (!band.IntersectsWith(area)) continue;
                var hidden = Occluders(w, host, area);
                foreach (var l in pile)
                {
                    if (l.Rest == null) continue;
                    int sx = (int)Math.Round(l.X - area.Left - l.Rest.Cx);
                    int sy = (int)Math.Round(l.Y - area.Top - l.Rest.Cy);
                    uint alpha = l.Fade < 0.999f ? (uint)(Math.Max(0, l.Fade) * 256) : 256;
                    var lt = l.Golden ? Light.Full : light;
                    var r = hidden.Count == 0 ? Blit.Draw(s, l.Rest, sx, sy, alpha, lt) : Blit.DrawClipped(s, l.Rest, sx, sy, alpha, lt, hidden);
                    if (!r.IsEmpty) drawn.Add(r);
                }
            }
        }

        void PaintAirborne(Surface s, World w, Rectangle area, List<Rectangle> drawn)
        {
            var light = w.Sky.Light;
            foreach (var l in w.Leaves)
            {
                if (l.Resting) continue;
                float ext = l.Look.Size * 0.9f + 3;
                if (l.X + ext < area.Left || l.X - ext > area.Right || l.Y + ext < area.Top || l.Y - ext > area.Bottom) continue;
                float ts = l.TumbleScale;
                var face = l.Look.Face(ts < 0);
                m.Reset();
                m.Translate(l.X - area.Left, l.Y - area.Top);
                m.Rotate(l.Angle);
                m.Rotate(l.TumbleAxis);
                m.Scale(Math.Abs(ts) < 0.04f ? (ts < 0 ? -0.04f : 0.04f) : ts, 1);
                m.Rotate(-l.TumbleAxis);
                m.Translate(-face.Cx, -face.Cy);
                uint alpha = l.Fade < 0.999f ? (uint)(Math.Max(0, l.Fade) * 256) : 256;
                var r = Blit.DrawAffine(s, face, m.Elements, alpha, l.Golden ? Light.Full : light, false);
                if (!r.IsEmpty) drawn.Add(r);
            }
        }

        public void Dispose() { m.Dispose(); }
    }
}
