using System;
using System.Collections.Generic;

namespace Autumn
{
    enum Season { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }

    // Per-channel light multiplier (0..256) applied to everything that isn't self-luminous.
    struct Light
    {
        public uint R, G, B;
        public bool White { get { return R >= 256 && G >= 256 && B >= 256; } }
        public static readonly Light Full = new Light { R = 256, G = 256, B = 256 };
        public static Light From(float r, float g, float b)
        {
            return new Light { R = (uint)Math.Max(0, Math.Min(256, r * 256)), G = (uint)Math.Max(0, Math.Min(256, g * 256)), B = (uint)Math.Max(0, Math.Min(256, b * 256)) };
        }
    }

    // Where the sun and moon are, what season it is, and how warm it is — from the real clock and a
    // place guessed from the Windows time zone (no location access). Everything can be overridden.
    class Sky
    {
        public double Lat = 35, Lon;
        public bool Southern;
        public string Place = "";
        public DateTime Local;
        public float SunElev, MoonPhase, MoonIllum, Temperature, YearPhase, Hour;
        public Season Season;
        public int SeasonOverride = -1;       // -1 follows the calendar
        public float HourOverride = -1;       // -1 follows the clock
        public float ColdOverride;            // forced snow in a warm season pulls this down
        public double LapseDaysPerSecond;     // >0 while the year is being fast-forwarded
        double lapseOffsetDays;
        public Light Light = Light.Full;

        public Sky()
        {
            Locate();
            Update(0);
        }

        static readonly Dictionary<string, double[]> Zones = new Dictionary<string, double[]>
        {
            { "India Standard Time", new[] { 22.5, 79.0 } }, { "Sri Lanka Standard Time", new[] { 7.0, 80.0 } },
            { "Nepal Standard Time", new[] { 27.7, 85.3 } }, { "Pakistan Standard Time", new[] { 30.0, 70.0 } },
            { "Bangladesh Standard Time", new[] { 23.8, 90.4 } }, { "Arabian Standard Time", new[] { 25.2, 55.3 } },
            { "GMT Standard Time", new[] { 51.5, -0.1 } }, { "W. Europe Standard Time", new[] { 50.0, 8.0 } },
            { "Romance Standard Time", new[] { 48.9, 2.3 } }, { "Central Europe Standard Time", new[] { 50.0, 15.0 } },
            { "E. Europe Standard Time", new[] { 45.0, 26.0 } }, { "Russian Standard Time", new[] { 55.8, 37.6 } },
            { "Eastern Standard Time", new[] { 40.7, -74.0 } }, { "Central Standard Time", new[] { 41.9, -87.6 } },
            { "Mountain Standard Time", new[] { 39.7, -105.0 } }, { "Pacific Standard Time", new[] { 37.8, -122.4 } },
            { "Alaskan Standard Time", new[] { 61.2, -149.9 } }, { "Hawaiian Standard Time", new[] { 21.3, -157.9 } },
            { "China Standard Time", new[] { 31.2, 121.5 } }, { "Tokyo Standard Time", new[] { 35.7, 139.7 } },
            { "Korea Standard Time", new[] { 37.6, 127.0 } }, { "Singapore Standard Time", new[] { 1.35, 103.8 } },
            { "SE Asia Standard Time", new[] { 13.8, 100.5 } }, { "AUS Eastern Standard Time", new[] { -33.9, 151.2 } },
            { "E. Australia Standard Time", new[] { -27.5, 153.0 } }, { "W. Australia Standard Time", new[] { -31.9, 115.9 } },
            { "New Zealand Standard Time", new[] { -41.3, 174.8 } }, { "South Africa Standard Time", new[] { -26.2, 28.0 } },
            { "E. South America Standard Time", new[] { -23.5, -46.6 } }, { "Argentina Standard Time", new[] { -34.6, -58.4 } },
        };

        void Locate()
        {
            var tz = TimeZoneInfo.Local;
            double[] ll;
            if (Zones.TryGetValue(tz.Id, out ll)) { Lat = ll[0]; Lon = ll[1]; Place = tz.Id; }
            else { Lat = 35; Lon = tz.BaseUtcOffset.TotalHours * 15; Place = tz.Id + " (approx.)"; }
            Southern = Lat < 0;
        }

        public void StartLapse(double daysPerSecond) { LapseDaysPerSecond = daysPerSecond; }
        public void StopLapse() { LapseDaysPerSecond = 0; lapseOffsetDays = 0; }
        public bool Lapsing { get { return LapseDaysPerSecond > 0; } }
        public double LapseDays { get { return lapseOffsetDays; } }

        public void Update(float dt)
        {
            if (LapseDaysPerSecond > 0) lapseOffsetDays += LapseDaysPerSecond * dt;
            DateTime now = DateTime.Now.AddDays(lapseOffsetDays);
            if (SeasonOverride >= 0 && LapseDaysPerSecond <= 0)
            {
                // Show the heart of the chosen season: its midpoint date, at the real time of day.
                int[] mid = Southern ? new[] { 10, 1, 4, 7 } : new[] { 4, 7, 10, 1 };
                int month = mid[SeasonOverride];
                int day = SeasonOverride == 2 && !Southern ? 20 : 15;
                now = new DateTime(now.Year, month, day, now.Hour, now.Minute, now.Second);
            }
            if (HourOverride >= 0)
                now = now.Date.AddHours(HourOverride);
            Local = now;
            Hour = (float)now.TimeOfDay.TotalHours;
            DateTime utc = now - TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            double jd = Julian(utc);

            SunElev = (float)SunElevation(jd, Lat, Lon);
            double lunation = (jd - 2451550.1) / 29.530588853;
            MoonPhase = (float)(lunation - Math.Floor(lunation));
            MoonIllum = (float)((1 - Math.Cos(2 * Math.PI * MoonPhase)) / 2);

            // Year phase in northern-hemisphere terms so one climate model serves both hemispheres.
            double doy = now.DayOfYear - 1 + now.TimeOfDay.TotalDays;
            double yp = doy / (DateTime.IsLeapYear(now.Year) ? 366.0 : 365.0);
            if (Southern) yp = (yp + 0.5) % 1.0;
            YearPhase = (float)yp;
            Season = SeasonOverride >= 0 && LapseDaysPerSecond <= 0 ? (Season)SeasonOverride : SeasonOf(yp);

            double annual = 11 + 13 * Math.Cos(2 * Math.PI * (yp - 0.55));
            double diurnal = 5 * Math.Cos(2 * Math.PI * (Hour - 15) / 24.0);
            Temperature = (float)(annual + diurnal) + ColdOverride;

            Light = LightFor(SunElev, MoonIllum);
        }

        public static Season SeasonOf(double yp)
        {
            // Astronomical seasons: ~Mar 20, Jun 21, Sep 22, Dec 21.
            if (yp < 0.2167) return Season.Winter;
            if (yp < 0.4712) return Season.Spring;
            if (yp < 0.7260) return Season.Summer;
            if (yp < 0.9699) return Season.Autumn;
            return Season.Winter;
        }

        // Bell-shaped seasonal intensity centred on a day of year (northern terms), width in days.
        public float Bell(double peakDoy, double widthDays, float floor)
        {
            double d = YearPhase * 365.0 - peakDoy;
            if (d > 182.5) d -= 365; if (d < -182.5) d += 365;
            return (float)(floor + (1 - floor) * Math.Exp(-(d * d) / (2 * widthDays * widthDays)));
        }

        public bool IsNight { get { return SunElev < -6; } }
        public bool IsDay { get { return SunElev > 4; } }
        public float Darkness { get { return Clamp01((2 - SunElev) / 10f); } }   // 0 by day, 1 in full night

        static Light LightFor(float elev, float moon)
        {
            float[] day = { 1f, 1f, 1f }, gold = { 1f, 0.84f, 0.68f };
            float[] night = { 0.40f + 0.12f * moon, 0.45f + 0.12f * moon, 0.62f + 0.10f * moon };
            float[] c;
            if (elev >= 10) c = day;
            else if (elev >= 0) c = Lerp(gold, day, elev / 10f);
            else if (elev >= -7) c = Lerp(night, gold, (elev + 7) / 7f);
            else c = night;
            return Light.From(c[0], c[1], c[2]);
        }

        static float[] Lerp(float[] a, float[] b, float t)
        {
            t = Clamp01(t);
            return new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t };
        }

        public static float Clamp01(float x) { return x < 0 ? 0 : x > 1 ? 1 : x; }

        public static double Julian(DateTime utc)
        {
            return utc.ToOADate() + 2415018.5;
        }

        // Low-precision solar position (good to a fraction of a degree), elevation in degrees.
        public static double SunElevation(double jd, double lat, double lon)
        {
            double n = jd - 2451545.0;
            double L = Deg(280.460 + 0.9856474 * n), g = Rad(Deg(357.528 + 0.9856003 * n));
            double lambda = Rad(L + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g));
            double eps = Rad(23.439 - 0.0000004 * n);
            double ra = Math.Atan2(Math.Cos(eps) * Math.Sin(lambda), Math.Cos(lambda));
            double dec = Math.Asin(Math.Sin(eps) * Math.Sin(lambda));
            double gmst = (18.697374558 + 24.06570982441908 * n) % 24;
            double ha = Rad(((gmst + lon / 15.0) * 15.0) % 360) - ra;
            double la = Rad(lat);
            return Math.Asin(Math.Sin(la) * Math.Sin(dec) + Math.Cos(la) * Math.Cos(dec) * Math.Cos(ha)) * 180 / Math.PI;
        }

        // Local times the sun crosses -0.833 degrees on the given local date (NaN if it doesn't).
        public void SunTimes(DateTime localDate, out DateTime rise, out DateTime set)
        {
            rise = set = DateTime.MinValue;
            var off = TimeZoneInfo.Local.GetUtcOffset(localDate);
            double prev = double.NaN;
            for (int m = 0; m <= 24 * 60; m += 2)
            {
                DateTime t = localDate.Date.AddMinutes(m);
                double e = SunElevation(Julian(t - off), Lat, Lon);
                if (!double.IsNaN(prev))
                {
                    if (prev < -0.833 && e >= -0.833 && rise == DateTime.MinValue) rise = t;
                    if (prev >= -0.833 && e < -0.833) set = t;
                }
                prev = e;
            }
        }

        // Next local date/time the moon reaches the given phase (0 new, 0.5 full).
        public DateTime NextPhase(float target)
        {
            double d = target - MoonPhase;
            if (d <= 0.001) d += 1;
            return Local.AddDays(d * 29.530588853);
        }

        public string MoonName
        {
            get
            {
                float p = MoonPhase;
                if (p < 0.03 || p > 0.97) return "New moon";
                if (p < 0.22) return "Waxing crescent";
                if (p < 0.28) return "First quarter";
                if (p < 0.47) return "Waxing gibbous";
                if (p < 0.53) return "Full moon";
                if (p < 0.72) return "Waning gibbous";
                if (p < 0.78) return "Last quarter";
                return "Waning crescent";
            }
        }

        static double Deg(double x) { x %= 360; return x < 0 ? x + 360 : x; }
        static double Rad(double d) { return d * Math.PI / 180; }
    }
}
