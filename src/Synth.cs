using System;

namespace Autumn
{
    // Every sound is made fresh from arithmetic: noise, sines, envelopes and simple filters.
    class Synth
    {
        const int Rate = Audio.Rate;
        readonly Random rng = new Random();
        float R(float a, float b) { lock (rng) return a + (float)rng.NextDouble() * (b - a); }
        float N() { lock (rng) return (float)(rng.NextDouble() * 2 - 1); }
        int Ri(int n) { lock (rng) return rng.Next(n); }

        public static byte[] Wav(float[] samples)
        {
            using (var ms = new System.IO.MemoryStream())
            using (var w = new System.IO.BinaryWriter(ms))
            {
                int bytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (float s in samples) w.Write((short)Math.Max(-32767, Math.Min(32767, s * 32767)));
                return ms.ToArray();
            }
        }

        public float[] Make(string name, float gain)
        {
            switch (name)
            {
                case "crunch": return Crunchy(Math.Min(1, gain), 700, 7500, 1f);
                case "snowcrunch": return Snowy(Math.Min(1, gain));
                case "thunder": return Thunder(gain);
                case "plop": return Plop();
                case "chime": return Chime();
                case "whoosh": return Whoosh(gain);
                case "honk": return Honk();
                case "owl": return Owl();
                case "bird": return Bird();
                case "pop": return Pop(gain);
                case "crackle": return Crunchy(0.5f, 1500, 9000, 0.6f);
                case "tinkle": return Tinkle();
                case "shimmer": return Shimmer();
                case "whistle": return Whistle();
            }
            return null;
        }

        float[] Buf(float seconds) { return new float[(int)(seconds * Rate)]; }

        static void Normalize(float[] b, float peak)
        {
            float m = 1e-6f;
            foreach (var x in b) m = Math.Max(m, Math.Abs(x));
            float k = peak / m;
            for (int i = 0; i < b.Length; i++) b[i] *= k;
        }

        static void Fade(float[] b, int samples)
        {
            for (int i = 0; i < samples && i < b.Length; i++) b[b.Length - 1 - i] *= i / (float)samples;
        }

        // Band-limit with one-pole high- and low-pass filters.
        static void Band(float[] b, float lo, float hi)
        {
            float hpA = (float)Math.Exp(-2 * Math.PI * lo / Rate), lpA = (float)Math.Exp(-2 * Math.PI * hi / Rate);
            float xp = 0, hp = 0, lp = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float x = b[i];
                hp = hpA * (hp + x - xp); xp = x;
                lp = lp * lpA + hp * (1 - lpA);
                b[i] = lp;
            }
        }

        // Dry leaves: clusters of tiny noise crackles over a soft rustle.
        float[] Crunchy(float e, float lo, float hi, float density)
        {
            float dur = 0.16f + 0.26f * e;
            var buf = Buf(dur + 0.01f);
            int n = buf.Length;
            int clusters = 1 + (int)(e * 3) + Ri(2);
            for (int c = 0; c < clusters; c++)
            {
                float start = c == 0 ? 0.004f : R(0.02f, dur * 0.6f);
                float len = R(0.045f, 0.13f), g = c == 0 ? 1f : R(0.45f, 0.9f);
                int count = (int)(R(1400, 3000) * (0.55f + 0.45f * e) * len * density);
                for (int k = 0; k < count; k++)
                {
                    float t = start + len * (float)Math.Pow(R(0, 1), 1.5);
                    float amp = g * (float)Math.Exp(-(t - start) / (len * 0.5f)) * (float)Math.Exp(R(-3.4f, 0));
                    int pos = (int)(t * Rate), blen = 8 + Ri(62);
                    float tau = blen * 0.3f, sg = N() < 0 ? -1 : 1;
                    for (int j = 0; j < blen && pos + j < n; j++) buf[pos + j] += sg * amp * N() * (float)Math.Exp(-j / tau);
                }
                int r0 = (int)(start * Rate), r1 = Math.Min(n, r0 + (int)(len * 1.4f * Rate));
                for (int i = r0; i < r1; i++) buf[i] += 0.06f * g * N() * (float)Math.Sin(Math.PI * Math.Sqrt((i - r0) / (float)(r1 - r0)));
            }
            Band(buf, lo, hi);
            Normalize(buf, 0.25f + 0.25f * e);
            Fade(buf, 300);
            return buf;
        }

        // Snow: softer, lower crunch with little squeaks of compressing crystals.
        float[] Snowy(float e)
        {
            var buf = Crunchy(0.4f + 0.4f * e, 250, 3000, 0.6f);
            int squeaks = 2 + Ri(4);
            for (int s = 0; s < squeaks; s++)
            {
                int pos = Ri(Math.Max(1, buf.Length - 2000));
                float f = R(900, 1700), len = R(0.012f, 0.03f);
                int m = (int)(len * Rate);
                for (int j = 0; j < m && pos + j < buf.Length; j++)
                {
                    float t = j / (float)Rate;
                    buf[pos + j] += 0.12f * (float)Math.Sin(2 * Math.PI * f * t * (1 + 0.6f * t / len)) * (float)Math.Sin(Math.PI * j / m);
                }
            }
            Normalize(buf, 0.3f + 0.2f * e);
            return buf;
        }

        // Thunder: a crack for near strikes, then a long rumble of low-passed brown noise with swells.
        float[] Thunder(float g)
        {
            float near = Math.Min(1, g);
            var buf = Buf(4.5f + 2 * near);
            float brown = 0, lp1 = 0, lp2 = 0;
            int swells = 3 + Ri(4);
            float[] at = new float[swells], wid = new float[swells], amp = new float[swells];
            for (int s = 0; s < swells; s++) { at[s] = R(0.05f, 3.2f); wid[s] = R(0.25f, 1.1f); amp[s] = R(0.4f, 1f); }
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                brown = brown * 0.995f + N() * 0.1f;
                lp1 += (brown - lp1) * 0.03f; lp2 += (lp1 - lp2) * 0.03f;
                float env = 0;
                for (int s = 0; s < swells; s++) { float d = (t - at[s]) / wid[s]; env += amp[s] * (float)Math.Exp(-d * d); }
                env *= Math.Min(1, t / 0.08f) * (float)Math.Exp(-t / (2.2f + near));
                buf[i] = lp2 * env * 6;
            }
            if (near > 0.6f)
            {
                int m = (int)(0.35f * Rate);
                for (int i = 0; i < m; i++) buf[i] += N() * 0.6f * (float)Math.Exp(-i / (0.06f * Rate)) * (near - 0.5f);
            }
            Normalize(buf, 0.35f + 0.45f * near);
            Fade(buf, 4000);
            return buf;
        }

        // A water drop: a quick upward-gliding sine.
        float[] Plop()
        {
            var buf = Buf(0.09f);
            float f0 = R(500, 800), phase = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float f = f0 * (1 + 2.2f * (float)Math.Min(1, t / 0.03f));
                phase += 2 * (float)Math.PI * f / Rate;
                buf[i] = (float)Math.Sin(phase) * (float)Math.Exp(-t / 0.025f) * Math.Min(1, t / 0.002f);
            }
            Normalize(buf, 0.18f);
            return buf;
        }

        // Bell partials (inharmonic), a rising pentatonic arpeggio.
        float[] Chime()
        {
            var buf = Buf(2.6f);
            double[] scale = { 523.25, 587.33, 659.25, 783.99, 880.0, 1046.5, 1174.7 };
            int start = Ri(3);
            double[] ratios = { 1, 2.76, 5.40, 8.93 };
            double[] decays = { 1.4, 0.7, 0.35, 0.2 };
            for (int note = 0; note < 3; note++)
            {
                double f0 = scale[start + note * 2];
                int off = (int)(note * 0.16 * Rate);
                for (int i = off; i < buf.Length; i++)
                {
                    double t = (i - off) / (double)Rate;
                    double s = 0;
                    for (int p = 0; p < 4; p++) s += Math.Sin(2 * Math.PI * f0 * ratios[p] * t) * Math.Exp(-t / decays[p]) / (p + 1);
                    buf[i] += (float)(s * Math.Min(1, t / 0.003));
                }
            }
            Normalize(buf, 0.22f);
            Fade(buf, 3000);
            return buf;
        }

        float[] Shimmer()
        {
            var buf = Buf(1.2f);
            for (int k = 0; k < 7; k++)
            {
                double f = R(2400, 5200);
                int off = Ri(buf.Length / 2);
                for (int i = off; i < buf.Length; i++)
                {
                    double t = (i - off) / (double)Rate;
                    buf[i] += (float)(Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t / 0.25) * Math.Min(1, t / 0.002));
                }
            }
            Normalize(buf, 0.1f);
            Fade(buf, 2000);
            return buf;
        }

        // A swelling rush of air.
        float[] Whoosh(float g)
        {
            var buf = Buf(1.6f);
            float b1 = 0, b2 = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate, u = t / 1.6f;
                float f = 250 + 900 * (float)Math.Sin(Math.PI * u);
                float w = (float)(2 * Math.Sin(Math.PI * f / Rate));
                b1 += w * (N() - b1 - 0.3f * b2); b2 += w * b1;
                buf[i] = b1 * (float)Math.Pow(Math.Sin(Math.PI * u), 2);
            }
            Normalize(buf, 0.12f * Math.Min(1, g));
            return buf;
        }

        // Goose: a nasal, falling "ha-onk" — a buzzy tone through two formants.
        float[] Honk()
        {
            var buf = Buf(0.28f);
            float f0 = R(330, 420), phase = 0;
            float a1 = 0, b1 = 0, a2 = 0, b2 = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)Rate, u = t / 0.28f;
                float f = f0 * (1.25f - 0.35f * u);
                phase += f / Rate; if (phase > 1) phase -= 1;
                float saw = 2 * phase - 1;
                float w1 = (float)(2 * Math.Sin(Math.PI * 800 / Rate)), w2 = (float)(2 * Math.Sin(Math.PI * 1500 / Rate));
                a1 += w1 * (saw - a1 - 0.12f * b1); b1 += w1 * a1;
                a2 += w2 * (saw - a2 - 0.15f * b2); b2 += w2 * a2;
                float env = Math.Min(1, t / 0.02f) * (float)Math.Pow(1 - u, 0.6);
                buf[i] = (a1 + 0.7f * a2) * env;
            }
            Normalize(buf, 0.16f);
            return buf;
        }

        // Owl: "hoo ... hoo-hoo".
        float[] Owl()
        {
            var buf = Buf(1.9f);
            float[] starts = { 0f, 0.75f, 1.12f }, lens = { 0.5f, 0.3f, 0.55f };
            float f0 = R(330, 390);
            for (int h = 0; h < 3; h++)
            {
                int off = (int)(starts[h] * Rate), m = (int)(lens[h] * Rate);
                double ph = 0;
                for (int i = 0; i < m && off + i < buf.Length; i++)
                {
                    double u = i / (double)m;
                    double f = f0 * (1 + 0.04 * Math.Sin(Math.PI * u)) * (h == 2 ? 0.97 : 1);
                    ph += 2 * Math.PI * f / Rate;
                    double env = Math.Pow(Math.Sin(Math.PI * Math.Min(1, u * 1.3)), 1.5) * (u > 0.77 ? Math.Max(0, 1 - (u - 0.77) / 0.23) : 1);
                    buf[off + i] += (float)((Math.Sin(ph) + 0.15 * Math.Sin(2 * ph)) * env + 0.01 * N() * env);
                }
            }
            Normalize(buf, 0.14f);
            return buf;
        }

        // Songbirds: a few species-like patterns built from pitch-swept sine notes.
        float[] Bird()
        {
            int kind = Ri(4);
            var buf = Buf(1.6f);
            double ph = 0;
            Func<int, int, double, double, double> note = null;
            note = (off, len, f1, f2) =>
            {
                for (int i = 0; i < len && off + i < buf.Length; i++)
                {
                    double u = i / (double)len;
                    double f = f1 + (f2 - f1) * u;
                    ph += 2 * Math.PI * f / Rate;
                    buf[off + i] += (float)(Math.Sin(ph) * Math.Sin(Math.PI * u));
                }
                return 0;
            };
            if (kind == 0)
            {   // chirps: quick downward sweeps
                int n = 3 + Ri(4);
                for (int k = 0; k < n; k++) note((int)(k * 0.11 * Rate), (int)(0.06 * Rate), R(4800, 5600), R(2600, 3200));
            }
            else if (kind == 1)
            {   // "fee-bee": two pure whistles
                note(0, (int)(0.3 * Rate), 3950, 3900);
                note((int)(0.38 * Rate), (int)(0.34 * Rate), 3350, 3300);
            }
            else if (kind == 2)
            {   // trill: many tiny notes
                int n = 12 + Ri(10);
                double f = R(3600, 4600);
                for (int k = 0; k < n; k++) note((int)(k * 0.045 * Rate), (int)(0.03 * Rate), f * 1.05, f * 0.92);
            }
            else
            {   // warble: a wandering phrase
                int n = 5 + Ri(4);
                int off = 0;
                for (int k = 0; k < n; k++)
                {
                    int len = (int)(R(0.06f, 0.14f) * Rate);
                    note(off, len, R(2500, 5000), R(2500, 5000));
                    off += len + (int)(R(0.01f, 0.05f) * Rate);
                }
            }
            Normalize(buf, 0.09f);
            Fade(buf, 1500);
            return buf;
        }

        // Firework: a low thump with a sharp crack.
        float[] Pop(float g)
        {
            var buf = Buf(1.2f);
            double ph = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)Rate;
                ph += 2 * Math.PI * (70 + 60 * Math.Exp(-t / 0.05)) / Rate;
                buf[i] = (float)(Math.Sin(ph) * Math.Exp(-t / 0.25) + N() * 0.5 * Math.Exp(-t / 0.02));
            }
            Band(buf, 30, 4000);
            Normalize(buf, 0.3f * Math.Min(1, g));
            Fade(buf, 2000);
            return buf;
        }

        float[] Whistle()
        {
            var buf = Buf(0.9f);
            double ph = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                double u = i / (double)buf.Length;
                ph += 2 * Math.PI * (900 + 1600 * u) / Rate;
                buf[i] = (float)(Math.Sin(ph) * Math.Sin(Math.PI * u) * 0.5 + N() * 0.15 * Math.Sin(Math.PI * u));
            }
            Normalize(buf, 0.07f);
            return buf;
        }

        // Ice breaking: glassy inharmonic partials and a spray of noise.
        float[] Tinkle()
        {
            var buf = Buf(0.8f);
            for (int k = 0; k < 9; k++)
            {
                double f = R(2200, 7200), tau = R(0.04f, 0.18f);
                int off = Ri((int)(0.25 * Rate));
                for (int i = off; i < buf.Length; i++)
                {
                    double t = (i - off) / (double)Rate;
                    buf[i] += (float)(Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t / tau) * 0.5);
                }
            }
            for (int i = 0; i < (int)(0.05 * Rate); i++) buf[i] += N() * 0.6f * (float)Math.Exp(-i / 300.0);
            Normalize(buf, 0.18f);
            Fade(buf, 2000);
            return buf;
        }
    }
}
