using System;
using System.Collections.Generic;
using System.Drawing;

namespace Autumn
{
    class Leaf
    {
        public LeafLook Look;
        public bool Resting, Dying, Golden;
        public float X, Y;                       // centre, global device pixels
        public float Ix, Iy, WindVx;             // kicked velocity (decays) and lagged wind drift
        public float Phase, Freq, SwayAmp, Fall, TiltAmp, Spin, NatSpin, Angle, Resp;
        public float Tumble, TumbleSpeed, TumbleAxis;
        public bool Tumbler;
        public float PrevContact, NoLandUntil;
        public WinInfo NoLandHost;
        // resting
        public WinInfo Host;
        public float RelX, Lift, TargetLift, RestSince, Grip;
        public long Seq;
        public Pixels Rest;
        public float RestBottom, RestHalf, Thick;
        public float Fade = 1f;
        public bool OnCursor;                    // riding the mouse pointer (see CursorFx)
        public int Seed;                         // regenerates the same look when restored

        public float TumbleScale { get { return Tumbler ? (float)Math.Cos(Tumble) : 0.78f + 0.22f * (float)Math.Cos(Tumble); } }
    }

    // Leaves, petals and blossoms: seasonal spawning, flight, landing on ledges, piles, kicks.
    partial class World
    {
        public readonly List<Leaf> Leaves = new List<Leaf>();
        public readonly Dictionary<WinInfo, List<Leaf>> Piles = new Dictionary<WinInfo, List<Leaf>>();
        readonly Dictionary<WinInfo, float[]> heights = new Dictionary<WinInfo, float[]>();
        public int MaxResting = 220, MaxFalling = 30;
        public float MaxPile { get { return 10 * S; } }
        public float KickEnergy;                  // accumulated for the crunch sound; consumer resets it
        public IntPtr FirstTargetHandle;          // the first few leaves drift down onto this window
        public float GoldenOdds = 1f / 650f;
        WinInfo FirstTarget;
        float spawnAcc;
        int burst, firstLeaves = 3, opening = 14;
        long seq;

        void FindFirstTarget()
        {
            FirstTarget = null;
            if (FirstTargetHandle == IntPtr.Zero) return;
            foreach (var w in Wins) if (w.H == FirstTargetHandle) FirstTarget = w;
        }

        void StepLeaves(float dt)
        {
            SpawnLeaves(dt);
            KickLeaves(dt);
            foreach (var leaf in Leaves)
            {
                if (leaf.OnCursor) continue;
                if (leaf.Resting) UpdateResting(leaf, dt);
                else UpdateFalling(leaf, dt);
                if (leaf.Dying) leaf.Fade -= dt / 3.5f;
            }
            Settle(dt);
            CullLeaves();
        }

        // ---------------------------------------------------------------- what falls this season

        // Leaves per second at full density, from the calendar and the weather.
        public float LeafRate()
        {
            float r;
            switch (Sky.Season)
            {
                case Season.Autumn: r = 1.5f * Sky.Bell(293, 30, 0.3f); break;
                case Season.Spring: r = 1.4f * Sky.Bell(100, 22, 0.25f); break;
                case Season.Summer: r = 0.06f + 0.9f * Weather.Storminess; break;
                default: r = 0.04f + 0.2f * (Math.Abs(Wind) > 120 * S ? 1 : 0); break;
            }
            switch (Weather.Kind)
            {
                case WeatherKind.Breezy: r *= 1.25f; break;
                case WeatherKind.Storm: r *= 1.5f; break;
                case WeatherKind.Snow: r *= 0.45f; break;
                case WeatherKind.Blizzard: r *= 0.3f; break;
            }
            return r;
        }

        void SpawnLeaves(float dt)
        {
            if (Stopping) return;
            int falling = 0;
            foreach (var l in Leaves) if (!l.Resting) falling++;
            float rate = LeafRate();
            if (burst > 0 && gustT > 0.3f && gustT < 3.5f && Rng.NextDouble() < dt * 6 * Math.Min(1f, rate + 0.2f))
            {
                burst--;
                float x = Gust > 0 ? Virt.Left - 40 * S : Virt.Right + 40 * S;
                var lf = NewLeaf(x, Virt.Top + Rand(0.05f, 0.55f) * Virt.Height);
                lf.WindVx = Wind;
                Leaves.Add(lf);
            }
            if (opening > 0 && Time > 0.4f && Rng.NextDouble() < dt * 3.5)
            {
                // Arrival: a short flurry across the whole top of the screen.
                opening--;
                if (rate > 0.3f) Leaves.Add(NewLeaf(Rand(Virt.Left, Virt.Right) - Wind * 4, Virt.Top - 40 * S - Rand(0, 120) * S));
            }
            if (falling >= MaxFalling * Density) return;
            float ramp = Math.Min(1f, Time / 25f);
            float r = Density * rate * (0.3f + 0.7f * ramp) * (0.75f + 0.35f * (float)Math.Sin(Time * 0.09));
            if (Sky.Lapsing) r *= 2.5f;
            if (Time < 0.6f) return;
            spawnAcc += r * dt;
            if (firstLeaves > 0 && rate > 0.3f && Time > 0.6f + (3 - firstLeaves) * 2.2f) spawnAcc = Math.Max(spawnAcc, 1);
            while (spawnAcc >= 1)
            {
                spawnAcc -= 1;
                float x;
                if (firstLeaves > 0 && FirstTarget != null && alive.Contains(FirstTarget) && FirstTarget.CanHold)
                {
                    // Aim the opening leaves so they drift onto the window the person is looking at.
                    float travel = (FirstTarget.Top - Virt.Top) / (S * 75f);
                    x = Rand(FirstTarget.Left + 0.25f * FirstTarget.Width, FirstTarget.Right - 0.25f * FirstTarget.Width) - Wind * travel * 0.8f;
                }
                else
                {
                    float travel = Virt.Height / (S * 75f);
                    x = Rand(Virt.Left - 0.05f * Virt.Width, Virt.Right + 0.05f * Virt.Width) - Wind * travel * 0.55f;
                }
                if (firstLeaves > 0) firstLeaves--;
                Leaves.Add(NewLeaf(x, Virt.Top - 40 * S));
            }
        }

        public Leaf NewLeaf(float x, float y)
        {
            LeafKind kind; int tone = 0;
            double k = Rng.NextDouble();
            switch (Sky.Season)
            {
                case Season.Spring: kind = k < 0.84 ? LeafKind.Petal : LeafKind.Blossom; break;
                case Season.Summer: kind = k < 0.5 ? LeafKind.Maple : LeafKind.Birch; tone = 1; break;
                case Season.Winter: kind = LeafKind.Oak; tone = 2; break;
                default: kind = k < 0.42 ? LeafKind.Maple : k < 0.62 ? LeafKind.Oak : k < 0.86 ? LeafKind.Birch : LeafKind.Ginkgo; break;
            }
            bool golden = Sky.Season == Season.Autumn && Rng.NextDouble() < GoldenOdds;
            if (golden) { kind = LeafKind.Maple; tone = 3; }
            bool petal = kind == LeafKind.Petal || kind == LeafKind.Blossom;
            float size = S * (kind == LeafKind.Petal ? Rand(9, 15) : kind == LeafKind.Blossom ? Rand(14, 20) : Rand(21, 35))
                       * (kind == LeafKind.Ginkgo ? 0.9f : kind == LeafKind.Oak ? 1.08f : 1f);
            Journal.Count(petal ? "petals" : "leaves", 1);
            if (golden) Journal.Count("goldenSeen", 1);
            return MakeLeaf(kind, tone, size, Rng.Next(), golden, x, y);
        }

        public Leaf RestoreLeaf(LeafKind kind, int tone, float size, int seed, bool golden)
        {
            return MakeLeaf(kind, tone, size, seed, golden, 0, 0);
        }

        Leaf MakeLeaf(LeafKind kind, int tone, float size, int seed, bool golden, float x, float y)
        {
            bool petal = kind == LeafKind.Petal || kind == LeafKind.Blossom;
            var l = new Leaf();
            l.Golden = golden;
            l.Seed = seed;
            l.Look = new LeafLook(kind, size, new Random(seed), tone);
            l.X = x; l.Y = y;
            l.Fall = S * (petal ? Rand(26, 48) : Rand(50, 88));
            l.Freq = petal ? Rand(0.4f, 0.95f) : Rand(0.3f, 0.75f);
            l.SwayAmp = S * (petal ? Rand(30, 70) : Rand(28, 80));
            l.TiltAmp = Rand(16, 42);
            l.Tumbler = Rng.NextDouble() < (petal ? 0.55 : 0.3);
            l.NatSpin = l.Tumbler ? (Rng.NextDouble() < 0.5 ? -1 : 1) * Rand(70, petal ? 320 : 220) : 0;
            l.Spin = l.NatSpin;
            l.Tumble = Rand(0, 6.283f);
            l.TumbleSpeed = l.Tumbler ? Rand(2f, 4.5f) : Rand(1.1f, 2.6f);
            l.TumbleAxis = Rand(0, 180);
            l.Angle = Rand(0, 360);
            l.Phase = Rand(0, 6.283f);
            l.Resp = petal ? Rand(0.9f, 1.8f) : Rand(0.5f, 1.4f);
            l.WindVx = Wind;
            l.Grip = S * Rand(9000, 26000);
            l.PrevContact = y;
            return l;
        }

        // ---------------------------------------------------------------- flight

        void UpdateFalling(Leaf l, float dt)
        {
            l.Phase += 6.2832f * l.Freq * dt;
            float c = (float)Math.Cos(l.Phase);
            float sway = l.SwayAmp * c;
            float fallV = l.Fall * (0.45f + 1.1f * c * c);
            l.WindVx += (Wind - l.WindVx) * (1f - (float)Math.Exp(-dt * l.Resp));
            float decay = (float)Math.Exp(-dt * 1.5f);
            if (l.Iy < 0) l.Iy += 650f * S * dt;
            l.Ix *= decay; l.Iy *= decay;
            l.Spin += (l.NatSpin - l.Spin) * (1f - (float)Math.Exp(-dt * 0.8f));
            l.Angle += (l.Spin + (l.Tumbler ? 0 : l.TiltAmp * 6.2832f * l.Freq * c)) * dt;
            float kick = Math.Abs(l.Ix) + Math.Abs(l.Iy);
            l.Tumble += l.TumbleSpeed * dt * (1f + kick / (350f * S));
            l.X += (sway + l.WindVx + l.Ix) * dt;
            l.Y += (fallV + l.Iy) * dt;

            float contact = l.Y + l.Look.Size * 0.26f;
            if (fallV + l.Iy > 0 && Time >= l.NoLandUntil)
            {
                float best = float.MaxValue;
                Ledge hit = new Ledge();
                bool found = false;
                foreach (var e in LedgeList)
                {
                    if (l.X < e.X0 || l.X > e.X1) continue;
                    if (e.Host == l.NoLandHost && Time < l.NoLandUntil + 0.5f) continue;
                    float top = e.Y - HeightAt(e.Host, l.X);
                    if (l.PrevContact <= top + 0.5f && contact >= top && top < best) { best = top; hit = e; found = true; }
                }
                if (found) Land(l, hit);
            }
            l.PrevContact = contact;
        }

        void Land(Leaf l, Ledge e)
        {
            l.Resting = true;
            l.Host = e.Host;
            float relX = l.X - e.Host.Left;
            float h = HeightAt(e.Host, l.X);
            if (h > 8 * S)
            {
                // Too steep: roll a little way to the lower side, like a real heap.
                float bestH = h, bestX = relX;
                for (float d = 4 * S; d < l.Look.Size * 1.6f; d += 3 * S)
                    foreach (float sgn in new float[] { -1, 1 })
                    {
                        float x = relX + sgn * d;
                        if (e.Host.Left + x < e.X0 || e.Host.Left + x > e.X1) continue;
                        float hx = HeightAt(e.Host, e.Host.Left + x);
                        if (hx < bestH - 3 * S) { bestH = hx; bestX = x; }
                    }
                relX = bestX; h = bestH;
            }
            if (h > MaxPile)
            {
                // Keep drifts low (they sit over the bottom of other windows): the oldest leaf here crumbles.
                List<Leaf> near;
                if (Piles.TryGetValue(e.Host, out near))
                {
                    Leaf oldest = null;
                    foreach (var o in near)
                        if (!o.Dying && Math.Abs(o.RelX - relX) < l.Look.Size && (oldest == null || o.Seq < oldest.Seq)) oldest = o;
                    if (oldest != null) oldest.Dying = true;
                }
            }
            l.RelX = relX;
            MakeRestSprite(l);
            l.Lift = h + 5 * S;
            l.TargetLift = h;
            l.Seq = ++seq;
            l.RestSince = Time;
            l.Ix = l.Iy = 0;
            List<Leaf> pile;
            if (!Piles.TryGetValue(l.Host, out pile)) { pile = new List<Leaf>(); Piles[l.Host] = pile; }
            pile.Add(l);
        }

        public void MakeRestSprite(Leaf l)
        {
            PointF org; float bottom, half;
            bool flipped = Rng.NextDouble() < 0.35;
            using (var bmp = l.Look.RenderRest(RestYaw(), Rand(0.4f, 0.62f), Rand(-8, 8), flipped, out org, out bottom, out half))
                l.Rest = Pixels.From(bmp, org.X, org.Y);
            l.RestBottom = bottom; l.RestHalf = half;
            l.Thick = bottom * 2 * 0.3f;
        }

        // Put an already-made leaf straight onto a ledge (restoring saved piles).
        public void PlaceResting(Leaf l, WinInfo host, float relX)
        {
            l.Resting = true; l.Host = host; l.RelX = relX;
            MakeRestSprite(l);
            l.Lift = l.TargetLift = 0;
            l.Seq = ++seq; l.RestSince = Time;
            List<Leaf> pile;
            if (!Piles.TryGetValue(host, out pile)) { pile = new List<Leaf>(); Piles[host] = pile; }
            pile.Add(l);
            Leaves.Add(l);
            SyncRestPosition(l);
        }

        public void Detach(Leaf l, float vx, float vy) { Detach(l, vx, vy, true); }

        public void Detach(Leaf l, float vx, float vy, bool shaken)
        {
            if (!l.Resting) return;
            List<Leaf> pile;
            if (Piles.TryGetValue(l.Host, out pile)) pile.Remove(l);
            // Resting leaves sit partly below the ledge line; start the flight just above the pile
            // so a kicked leaf can come back down onto the same ledge.
            float surface = l.Host.Top - HeightAt(l.Host, l.X) - 1;
            l.Resting = false;
            // Shaken-off leaves must clear their own (moving) ledge; kicked ones may land right back.
            l.NoLandHost = shaken ? l.Host : null;
            l.NoLandUntil = Time + (shaken ? 0.25f : 0.06f);
            l.Host = null;
            l.Rest = null;
            l.Ix = vx; l.Iy = vy;
            l.Tumbler = true;
            float dir = Rng.NextDouble() < 0.5 ? -1 : 1;
            l.NatSpin = dir * Rand(60, 200);
            l.Spin = dir * Rand(180, 520);
            l.Phase = Rand(0, 6.283f);
            l.WindVx = Wind * 0.5f;
            l.PrevContact = Math.Min(l.Y + l.Look.Size * 0.26f, surface);
        }

        void UpdateResting(Leaf l, float dt)
        {
            var h = l.Host;
            if (!h.IsGround)
            {
                if (!alive.Contains(h) || !h.CanHold) { Detach(l, Rand(-40, 40) * S, Rand(-60, 0) * S); return; }
                float a = (float)Math.Sqrt(h.Ax * h.Ax + h.Ay * h.Ay);
                if (a > l.Grip || h.Vy < -1250 * S)
                {
                    // Shaken off or flung upward: keep the window's momentum.
                    SyncRestPosition(l);
                    Detach(l, h.Vx * Rand(0.6f, 1f), Math.Min(h.Vy, 0) * Rand(0.6f, 1f) - Rand(20, 140) * S);
                    return;
                }
                if (l.RelX < -l.RestHalf * 0.4f || l.RelX > h.Width + l.RestHalf * 0.4f)
                {
                    SyncRestPosition(l);
                    Detach(l, (l.RelX < 0 ? -1 : 1) * Rand(20, 60) * S, 0);
                    return;
                }
            }
            if (Gust != 0 && Math.Abs(Gust) > 140 * S)
            {
                float g = Math.Abs(Gust) / (300 * S);
                float p = dt * 0.9f * g * (0.25f + l.Lift / (14 * S)) * (Stopping ? 6 : 1);
                if (Rng.NextDouble() < p && Time - l.RestSince > 0.5f)
                {
                    SyncRestPosition(l);
                    Detach(l, Math.Sign(Gust) * Rand(80, 220) * S, -Rand(90, 260) * S, false);
                    return;
                }
            }
            l.Lift += (l.TargetLift - l.Lift) * (1f - (float)Math.Exp(-dt * 12));
            SyncRestPosition(l);
        }

        // Leaves settle mostly lengthwise along a ledge, rarely end-on.
        float RestYaw()
        {
            float a = Rand(-35, 35) + (Rng.NextDouble() < 0.5 ? 90 : 270);
            return Rng.NextDouble() < 0.2 ? Rand(0, 360) : a;
        }

        public void SyncRestPosition(Leaf l)
        {
            l.X = l.Host.Left + l.RelX;
            l.Y = l.Host.Top - l.Lift - l.RestBottom + (l.Host.IsTaskbar ? 7f : 1.2f) * S;
        }

        // ---------------------------------------------------------------- piles

        public float LeafHeightAt(WinInfo host, float x)
        {
            float[] hs;
            if (!heights.TryGetValue(host, out hs)) return 0;
            int i = (int)((x - host.Left + 40 * S) / (3 * S));
            if (i < 0 || i >= hs.Length) return 0;
            return hs[i];
        }

        void Settle(float dt)
        {
            var dead = new List<WinInfo>();
            foreach (var kv in Piles)
            {
                var host = kv.Key;
                var pile = kv.Value;
                if (pile.Count == 0) { dead.Add(host); continue; }
                float bw = 3 * S, margin = 40 * S;
                int n = Math.Max(1, (int)((host.Width + 2 * margin) / bw) + 1);
                float[] hs;
                if (!heights.TryGetValue(host, out hs) || hs.Length != n) { hs = new float[n]; heights[host] = hs; }
                else Array.Clear(hs, 0, n);
                pile.Sort((a, b) => a.Seq.CompareTo(b.Seq));
                foreach (var l in pile)
                {
                    float f0 = l.RelX - l.RestHalf * 0.75f, f1 = l.RelX + l.RestHalf * 0.75f;
                    int i0 = Math.Max(0, (int)((f0 + margin) / bw)), i1 = Math.Min(n - 1, (int)((f1 + margin) / bw));
                    if (i1 < i0) continue;
                    float mx = 0, sum = 0;
                    for (int i = i0; i <= i1; i++) { mx = Math.Max(mx, hs[i]); sum += hs[i]; }
                    float baseH = 0.6f * mx + 0.4f * sum / (i1 - i0 + 1);
                    l.TargetLift = baseH;
                    for (int i = i0; i <= i1; i++)
                    {
                        float u = (i - i0) / (float)Math.Max(1, i1 - i0) * 2 - 1;
                        hs[i] = Math.Max(hs[i], baseH + l.Thick * (0.45f + 0.55f * (1 - Math.Abs(u))));
                    }
                }
            }
            foreach (var d in dead) { Piles.Remove(d); heights.Remove(d); }
        }

        // ---------------------------------------------------------------- kicks

        void KickLeaves(float dt)
        {
            if (!HaveCursor || CurSpeed < 150 * S) return;
            float R = 80 * S, speed = CurSpeed;
            float sx = CurX - PrevCurX, sy = CurY - PrevCurY;
            foreach (var l in Leaves)
            {
                if (l.OnCursor) continue;
                if (Math.Abs(l.X - CurX) > R + Math.Abs(sx) + 4 || Math.Abs(l.Y - CurY) > R + Math.Abs(sy) + 4) continue;
                float d = CursorDistance(l.X, l.Y);
                if (d >= R) continue;
                float f = (1 - d / R); f *= f;
                if (l.Resting)
                {
                    if (speed * f > 480 * S && Time - l.RestSince > 0.2f)
                    {
                        KickEnergy += f * Math.Min(speed, 4000 * S) / (1000 * S);
                        Journal.Count("kicked", 1);
                        if (l.Golden) Journal.Discover(this, "golden");
                        SyncRestPosition(l);
                        Detach(l, Clamp(CurVx * 0.45f * f, 1500 * S), Clamp(CurVy * 0.35f * f - speed * 0.3f * f, 1500 * S), false);
                    }
                }
                else
                {
                    float k = 1f - (float)Math.Exp(-dt * 22 * f);
                    l.Ix += (Clamp(CurVx * 0.6f * f, 1600 * S) - l.Ix) * k;
                    l.Iy += (Clamp(CurVy * 0.5f * f - speed * 0.12f * f, 1600 * S) - l.Iy) * k;
                    if (!l.Tumbler && f > 0.3f) { l.Tumbler = true; l.NatSpin = Rand(-160, 160); l.Spin = Rand(-420, 420); }
                    if (l.Golden && f > 0.2f) Journal.Discover(this, "golden");
                }
            }
        }

        // ---------------------------------------------------------------- lifecycle

        void CullLeaves()
        {
            int resting = 0;
            foreach (var l in Leaves) if (l.Resting && !l.Dying) resting++;
            int excess = resting - (int)(MaxResting * Density);
            if (excess > 0)
            {
                var rest = new List<Leaf>();
                foreach (var l in Leaves) if (l.Resting && !l.Dying) rest.Add(l);
                rest.Sort((a, b) => a.Seq.CompareTo(b.Seq));
                for (int i = 0; i < excess && i < rest.Count; i++) rest[i].Dying = true;
            }
            for (int i = Leaves.Count - 1; i >= 0; i--)
            {
                var l = Leaves[i];
                if (l.OnCursor) continue;
                bool gone = l.Fade <= 0
                    || (!l.Resting && (l.X < Virt.Left - 300 * S || l.X > Virt.Right + 300 * S || l.Y > Virt.Bottom + 120 * S));
                if (!gone) continue;
                if (l.Resting) { List<Leaf> pile; if (Piles.TryGetValue(l.Host, out pile)) pile.Remove(l); }
                l.Look.Dispose();
                Leaves.RemoveAt(i);
            }
        }
    }
}
