using System;
using System.Collections.Generic;
using System.Drawing;

namespace Autumn
{
    class Spark
    {
        public float X, Y, Vx, Vy, Life, MaxLife, Size, Gravity, Drag;
        public Color C;
        public bool Twinkle, Streak;
    }

    // Self-luminous particles, added together so they glow: sparks, glints, embers, fireworks.
    class Sparks : Sys
    {
        public readonly List<Spark> List = new List<Spark>();
        Pixels glow, core;

        public Sparks() { Layer = 50; }

        public Spark Emit(World w, float x, float y, float vx, float vy, float life, float size, Color c, float gravity, float drag)
        {
            if (List.Count > 1500) return null;
            var s = new Spark { X = x, Y = y, Vx = vx, Vy = vy, Life = life, MaxLife = life, Size = size, C = c, Gravity = gravity * w.S, Drag = drag };
            List.Add(s);
            return s;
        }

        public void Burst(World w, float x, float y, int n, float speed, Color c, float life, float gravity)
        {
            for (int i = 0; i < n; i++)
            {
                double a = w.Rng.NextDouble() * Math.PI * 2;
                float sp = speed * w.S * (0.4f + 0.6f * (float)Math.Sqrt(w.Rng.NextDouble()));
                var s = Emit(w, x, y, (float)Math.Cos(a) * sp, (float)Math.Sin(a) * sp, life * w.Rand(0.6f, 1.2f), w.Rand(0.6f, 1.2f), c, gravity, 1.6f);
                if (s != null) s.Twinkle = w.Chance(0.3f);
            }
        }

        public override void Step(World w, float dt)
        {
            for (int i = List.Count - 1; i >= 0; i--)
            {
                var s = List[i];
                s.Life -= dt;
                if (s.Life <= 0) { List.RemoveAt(i); continue; }
                float drag = (float)Math.Exp(-dt * s.Drag);
                s.Vx *= drag; s.Vy = s.Vy * drag + s.Gravity * dt;
                s.X += s.Vx * dt; s.Y += s.Vy * dt;
            }
        }

        public override void Paint(Surface surf, World w, Rectangle area, List<Rectangle> drawn)
        {
            if (List.Count == 0) return;
            if (glow == null)
            {
                glow = Pixels.Dot(5.5f * w.S, Color.White, 0f);
                core = Pixels.Dot(1.4f * w.S, Color.White, 0.5f);
            }
            foreach (var s in List)
            {
                if (s.X < area.Left - 30 || s.X > area.Right + 30 || s.Y < area.Top - 30 || s.Y > area.Bottom + 30) continue;
                float f = s.Life / s.MaxLife;
                float a = f < 0.3f ? f / 0.3f : 1f;
                if (s.Twinkle) a *= 0.55f + 0.45f * (float)Math.Sin(w.Time * 31 + s.X);
                var tint = Light.From(s.C.R / 255f, s.C.G / 255f, s.C.B / 255f);
                if (s.Streak)
                {
                    // a short glowing trail behind fast sparks (fireworks)
                    float tx = s.X - s.Vx * 0.07f, ty = s.Y - s.Vy * 0.07f;
                    var tr = Blit.Line(surf, tx - area.Left, ty - area.Top, s.X - area.Left, s.Y - area.Top, s.C, 0, a * 0.9f, true);
                    if (!tr.IsEmpty) drawn.Add(tr);
                }
                var r = Blit.DrawAt(surf, glow, s.X - area.Left, s.Y - area.Top, s.Size, (uint)(a * 150), tint, true);
                if (!r.IsEmpty) drawn.Add(r);
                Blit.DrawAt(surf, core, s.X - area.Left, s.Y - area.Top, s.Size, (uint)(a * 256), Light.Full, true);
            }
        }

        public override bool Quiet { get { return List.Count == 0; } }
    }
}
