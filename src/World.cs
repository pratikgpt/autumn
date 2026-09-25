using System;
using System.Collections.Generic;
using System.Drawing;

namespace Autumn
{
    // A self-contained part of the world (snow, rain, fireflies...). Steps after the leaves, paints
    // in Layer order: 0 sky, 10 resting things, 20 snowpack, 30 airborne, 40 creatures, 50 light, 60 cursor, 70 flash.
    abstract class Sys
    {
        public int Layer;
        public virtual void Step(World w, float dt) { }
        public virtual void Paint(Surface s, World w, Rectangle area, List<Rectangle> drawn) { }
        public virtual bool Quiet { get { return true; } }     // nothing left on screen (used when ending)
        public virtual bool Trigger(World w, string name) { return false; }   // force an event (tests, commands)
    }

    // Sound requests from the simulation; the app routes them to the audio engine (tests ignore them).
    interface ISound
    {
        void Play(string name, float gain, float pan);
        void Ambience(string name, float level);
    }

    // The whole simulation: windows and ledges, sky and weather, wind, the cursor, leaves, and systems.
    partial class World
    {
        public List<WinInfo> Wins = new List<WinInfo>();
        public List<Ledge> LedgeList = new List<Ledge>();
        public readonly List<WinInfo> Grounds = new List<WinInfo>();
        readonly HashSet<WinInfo> alive = new HashSet<WinInfo>();
        readonly IWindowSource src;
        public readonly Random Rng;
        public readonly float S;                 // DPI scale: 1.0 at 96 dpi
        public readonly Sky Sky = new Sky();
        public readonly Weather Weather = new Weather();
        public readonly List<Sys> Systems = new List<Sys>();
        public readonly Journal Journal = new Journal();
        public ISound Sound;
        public float Time, Wind, Gust;
        public float Density = 1f;
        public bool Stopping;
        public double Idle;                       // seconds since the person last touched mouse or keyboard
        public bool MouseDown;                    // primary button held (state only; clicks still go to the windows below)
        public Rectangle Virt;
        float gustT = -1, gustAmp, nextGust, stopT, lastCrunch;

        // Cursor, shared by every system.
        public bool HaveCursor;
        public float CurX, CurY, CurVx, CurVy, CurSpeed, CurStill, PrevCurX, PrevCurY;

        public World(IWindowSource source, float scale, int seed)
        {
            src = source; S = scale; Rng = new Random(seed);
            nextGust = 40 + (float)Rng.NextDouble() * 40;
            RefreshMonitors();
        }

        public void RefreshMonitors()
        {
            Grounds.Clear();
            var mons = src.Monitors();
            int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
            for (int i = 0; i < mons.Count; i++)
            {
                var m = mons[i];
                Grounds.Add(new WinInfo { H = new IntPtr(-100 - i), Left = m.Left, Right = m.Right, Top = m.Bottom, Bottom = m.Bottom + 1, IsGround = true, CanHold = true });
                l = Math.Min(l, m.Left); t = Math.Min(t, m.Top); r = Math.Max(r, m.Right); b = Math.Max(b, m.Bottom);
            }
            Virt = Rectangle.FromLTRB(l, t, r, b);
            foreach (var leaf in Leaves) if (leaf.Resting && leaf.Host.IsGround) Detach(leaf, 0, 0);
        }

        public float Rand(float a, float b) { return a + (float)Rng.NextDouble() * (b - a); }
        public bool Chance(float p) { return Rng.NextDouble() < p; }
        public bool Alive(WinInfo w) { return w.IsGround || alive.Contains(w); }

        public T Get<T>() where T : Sys
        {
            foreach (var s in Systems) { var t = s as T; if (t != null) return t; }
            return null;
        }

        public void Step(float dt, bool cursorValid, float cx, float cy)
        {
            if (dt <= 0) return;
            dt = Math.Min(dt, 0.05f);
            Time += dt;
            Wins = src.Snapshot(dt);
            alive.Clear();
            FindFirstTarget();
            foreach (var w in Wins) alive.Add(w);
            foreach (var g in Grounds) alive.Add(g);
            LedgeList = Ledges.Build(Wins, Grounds);

            Sky.Update(dt);
            Weather.Step(Sky, dt, Rng);
            UpdateWind(dt);
            UpdateCursor(dt, cursorValid, cx, cy);
            StepLeaves(dt);
            if (KickEnergy > 0.05f && Time - lastCrunch > 0.12f)
            {
                if (Sound != null) Sound.Play("crunch", Math.Min(1f, KickEnergy), Math.Max(-1, Math.Min(1, (CurX - Virt.Left) / Math.Max(1f, Virt.Width) * 2 - 1)));
                KickEnergy = 0; lastCrunch = Time;
            }
            foreach (var s in Systems) s.Step(this, dt);
            Journal.Tick(this, dt);
        }

        // ---------------------------------------------------------------- wind

        void UpdateWind(float dt)
        {
            float t = Time;
            float g = Weather.Gustiness;
            float calm = S * (14f * (float)Math.Sin(t * 0.047) + 9f * (float)Math.Sin(t * 0.131 + 1.3) + 5f * (float)Math.Sin(t * 0.37 + 2.0)) * (0.6f + g);
            nextGust -= dt;
            if (nextGust <= 0 && gustT < 0 && !Stopping) StartGust(0);
            Gust = 0;
            if (gustT >= 0)
            {
                gustT += dt;
                float env = gustT < 1.4f ? Smooth(gustT / 1.4f) : gustT < 3.6f ? 1f : gustT < 7f ? 1f - Smooth((gustT - 3.6f) / 3.4f) : 0f;
                env *= 1f + 0.18f * (float)Math.Sin(gustT * 7.3) * (float)Math.Sin(gustT * 2.1);
                Gust = gustAmp * env;
                if (gustT >= 7f) gustT = -1;
            }
            Wind = Weather.WindDir * Weather.Wind * S + calm + Gust;
        }

        public static float Smooth(float x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }

        public void StartGust(float strength)
        {
            float dir = Weather.Wind > 30 ? Weather.WindDir : (Rng.NextDouble() < 0.5 ? -1 : 1);
            float g = Weather.Gustiness;
            gustAmp = dir * S * (strength > 0 ? strength : Rand(190, 330) * (0.6f + 0.6f * g));
            gustT = 0;
            nextGust = Rand(45, 100) / (0.3f + g);
            if (!Stopping) burst = 6 + Rng.Next(8);
            if (Sound != null && Math.Abs(gustAmp) > 250 * S) Sound.Play("whoosh", Math.Min(1f, Math.Abs(gustAmp) / (500 * S)), Math.Sign(gustAmp) * 0.5f);
        }

        public bool GustActive { get { return gustT >= 0; } }
        public float GustTime { get { return gustT; } }

        // ---------------------------------------------------------------- cursor

        void UpdateCursor(float dt, bool valid, float cx, float cy)
        {
            if (!valid) { HaveCursor = false; CurSpeed = 0; return; }
            if (!HaveCursor) { HaveCursor = true; CurX = PrevCurX = cx; CurY = PrevCurY = cy; CurStill = 0; return; }
            PrevCurX = CurX; PrevCurY = CurY;
            CurX = cx; CurY = cy;
            CurVx = (cx - PrevCurX) / dt; CurVy = (cy - PrevCurY) / dt;
            CurSpeed = (float)Math.Sqrt(CurVx * CurVx + CurVy * CurVy);
            if (Math.Abs(cx - PrevCurX) + Math.Abs(cy - PrevCurY) > 2) CurStill = 0;
            else CurStill += dt;
        }

        // Distance from p to the segment the cursor swept this frame, and where along it.
        public float CursorDistance(float x, float y)
        {
            float sx = CurX - PrevCurX, sy = CurY - PrevCurY, len2 = sx * sx + sy * sy;
            float t = len2 > 0 ? Math.Max(0, Math.Min(1, ((x - PrevCurX) * sx + (y - PrevCurY) * sy) / len2)) : 0;
            float dx = x - (PrevCurX + sx * t), dy = y - (PrevCurY + sy * t);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static float Clamp(float v, float m) { return Math.Max(-m, Math.Min(m, v)); }

        // ---------------------------------------------------------------- ledges for everyone

        // First ledge crossed moving from yPrev to yNow at x; 'extra' lets callers add pile/snow height.
        public bool HitLedge(float x, float yPrev, float yNow, bool withPiles, out Ledge hit, out float top)
        {
            hit = new Ledge(); top = float.MaxValue;
            bool found = false;
            foreach (var e in LedgeList)
            {
                if (x < e.X0 || x > e.X1) continue;
                float t = e.Y - (withPiles ? HeightAt(e.Host, x) : 0);
                if (yPrev <= t + 0.5f && yNow >= t && t < top) { top = t; hit = e; found = true; }
            }
            return found;
        }

        // Top of whatever is lying on a ledge at x: leaves and snow.
        public float HeightAt(WinInfo host, float x)
        {
            float h = LeafHeightAt(host, x);
            var snow = Get<Snow>();
            if (snow != null) h = Math.Max(h, snow.DepthAt(host, x));
            return h;
        }

        public bool Trigger(string name)
        {
            foreach (var s in Systems) if (s.Trigger(this, name)) return true;
            return false;
        }

        // ---------------------------------------------------------------- ending

        // Farewell: one big gust sweeps everything away; returns true once the screen is clear.
        public bool StepStopping(float dt)
        {
            if (!Stopping) { Stopping = true; StartGust(620); stopT = 0; }
            stopT += dt;
            if (stopT > 1.2f) foreach (var l in Leaves) if (!l.Resting && Math.Abs(l.Ix) < 400 * S) l.Ix += Math.Sign(gustAmp) * 900 * S * dt;
            bool quiet = Leaves.Count == 0;
            foreach (var s in Systems) quiet &= s.Quiet;
            return quiet || stopT > 9f;
        }
    }
}
