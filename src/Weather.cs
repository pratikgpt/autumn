using System;

namespace Autumn
{
    enum WeatherKind { Clear = 0, Breezy = 1, Rain = 2, Storm = 3, Snow = 4, Blizzard = 5 }

    // A slow Markov weather: each spell lasts several minutes, then the next is drawn from the
    // season's odds. Parameters glide toward their targets so rain builds and fades naturally.
    class Weather
    {
        public WeatherKind Kind = WeatherKind.Breezy;
        public int Override = -1;
        public float Precip, Wind, Gustiness = 0.5f, Storminess;
        public float WindDir = 1;
        public float SpellLeft = 360;
        public float SinceRain = 9999;          // seconds since precipitation last stopped
        public bool WasRaining;
        public event Action<WeatherKind, WeatherKind> Changed;

        float tPrecip, tWind = 20, tGust = 0.5f, tStorm, fastT;

        static readonly float[][] Odds = {
            //            Clear Breezy Rain Storm Snow Blizzard
            new float[] { 0.34f, 0.24f, 0.30f, 0.10f, 0.02f, 0.00f },   // spring
            new float[] { 0.50f, 0.20f, 0.12f, 0.18f, 0.00f, 0.00f },   // summer
            new float[] { 0.28f, 0.36f, 0.26f, 0.06f, 0.04f, 0.00f },   // autumn
            new float[] { 0.26f, 0.14f, 0.06f, 0.00f, 0.42f, 0.12f },   // winter
        };

        public bool Snowy(Sky sky)
        {
            if (Kind == WeatherKind.Snow || Kind == WeatherKind.Blizzard) return true;
            return sky.Temperature < 1.0f;
        }

        public void Force(int kind)
        {
            Override = kind;
            if (kind >= 0) { Begin((WeatherKind)kind, 1e9f); fastT = 12; }
            else SpellLeft = 0;       // back to nature: draw a fresh spell right away
        }

        public void Step(Sky sky, float dt, Random rng)
        {
            if (Override < 0)
            {
                SpellLeft -= dt * (sky.Lapsing ? 40 : 1);
                if (SpellLeft <= 0)
                {
                    var o = Odds[(int)sky.Season];
                    double r = rng.NextDouble(), acc = 0;
                    int pick = 0;
                    for (int i = 0; i < o.Length; i++) { acc += o[i]; if (r < acc) { pick = i; break; } }
                    var k = (WeatherKind)pick;
                    // Warm air turns snow into rain and cold air turns rain into snow.
                    if ((k == WeatherKind.Snow || k == WeatherKind.Blizzard) && sky.Temperature > 4) k = WeatherKind.Rain;
                    if ((k == WeatherKind.Rain) && sky.Temperature < 0.5f) k = WeatherKind.Snow;
                    if ((k == WeatherKind.Storm) && sky.Temperature < 0.5f) k = WeatherKind.Blizzard;
                    float minutes = k == WeatherKind.Storm ? 4 + (float)rng.NextDouble() * 6
                                  : k == WeatherKind.Clear ? 8 + (float)rng.NextDouble() * 14
                                  : 6 + (float)rng.NextDouble() * 12;
                    if (rng.NextDouble() < 0.35) WindDir = -WindDir;
                    Begin(k, minutes * 60);
                }
            }
            if (fastT > 0) fastT -= dt;
            // A thaw turns falling snow to rain and a freeze turns rain to snow (unless someone chose it).
            if (Override < 0)
            {
                if ((Kind == WeatherKind.Snow || Kind == WeatherKind.Blizzard) && sky.Temperature > 6) Begin(WeatherKind.Rain, SpellLeft);
                else if ((Kind == WeatherKind.Rain || Kind == WeatherKind.Storm) && sky.Temperature < -2) Begin(Kind == WeatherKind.Rain ? WeatherKind.Snow : WeatherKind.Blizzard, SpellLeft);
            }
            float k1 = 1f - (float)Math.Exp(-dt / (sky.Lapsing ? 1.5f : fastT > 0 ? 3f : 40f));
            Precip += (tPrecip - Precip) * k1;
            Wind += (tWind - Wind) * k1;
            Gustiness += (tGust - Gustiness) * k1;
            Storminess += (tStorm - Storminess) * k1;

            bool raining = Precip > 0.05f;
            if (raining) SinceRain = 0;
            else SinceRain += dt;
            WasRaining = raining;
        }

        void Begin(WeatherKind k, float seconds)
        {
            var old = Kind;
            Kind = k;
            SpellLeft = seconds;
            switch (k)
            {
                case WeatherKind.Clear: tPrecip = 0; tWind = 10; tGust = 0.25f; tStorm = 0; break;
                case WeatherKind.Breezy: tPrecip = 0; tWind = 42; tGust = 0.85f; tStorm = 0; break;
                case WeatherKind.Rain: tPrecip = 0.55f; tWind = 24; tGust = 0.4f; tStorm = 0; break;
                case WeatherKind.Storm: tPrecip = 1f; tWind = 85; tGust = 1f; tStorm = 1; break;
                case WeatherKind.Snow: tPrecip = 0.6f; tWind = 14; tGust = 0.3f; tStorm = 0; break;
                case WeatherKind.Blizzard: tPrecip = 1f; tWind = 165; tGust = 1f; tStorm = 0; break;
            }
            if (old != k && Changed != null) Changed(old, k);
        }

        public static string Describe(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Clear: return "Clear";
                case WeatherKind.Breezy: return "Breezy";
                case WeatherKind.Rain: return "Rain";
                case WeatherKind.Storm: return "Thunderstorm";
                case WeatherKind.Snow: return "Snow";
                default: return "Blizzard";
            }
        }
    }
}
