using System;
using System.Collections.Generic;
using System.Drawing;

namespace Autumn
{
    // Decides what the world sounds like right now: wind that follows the gusts, crickets on warm
    // nights, a dawn chorus in spring and summer, the odd owl after dark in autumn and winter.
    class Soundscape : Sys
    {
        float birdT = 5, owlT = 90;
        public float Crickets;

        public override void Step(World w, float dt)
        {
            if (w.Sound == null) return;
            var sky = w.Sky;
            float wind = Math.Max(0, Math.Min(1, (Math.Abs(w.Wind) - 35 * w.S) / (260 * w.S)));
            w.Sound.Ambience("wind", w.Stopping ? 0.8f : wind);

            bool dry = w.Weather.Precip < 0.1f;
            bool warmNight = sky.Darkness > 0.6f && sky.Temperature > 12 && dry && (sky.Season == Season.Summer || sky.Season == Season.Spring || (sky.Season == Season.Autumn && sky.YearPhase < 0.8f));
            Crickets += ((warmNight ? 0.7f : 0) - Crickets) * Math.Min(1, dt * 0.3f);
            w.Sound.Ambience("crickets", Crickets);

            // Birds: busy at dawn in spring and summer, occasional through the day.
            bool birdSeason = sky.Season != Season.Winter && dry;
            bool dawn = sky.Hour < 11 && sky.SunElev > -5 && sky.SunElev < 14;
            if (birdSeason && sky.SunElev > -5)
            {
                birdT -= dt;
                if (birdT <= 0)
                {
                    bool chorus = dawn && (sky.Season == Season.Spring || sky.Season == Season.Summer);
                    birdT = chorus ? w.Rand(1.5f, 6f) : w.Rand(40, 120);
                    w.Sound.Play("bird", w.Rand(0.5f, 1f), w.Rand(-0.9f, 0.9f));
                }
            }

            // Owls in the long nights.
            if ((sky.Season == Season.Autumn || sky.Season == Season.Winter) && sky.IsNight && dry)
            {
                owlT -= dt;
                if (owlT <= 0)
                {
                    owlT = w.Rand(150, 420);
                    w.Sound.Play("owl", w.Rand(0.6f, 1f), w.Rand(-0.8f, 0.8f));
                    if (sky.Season == Season.Autumn && sky.Hour < 4) w.Journal.Discover(w, "owl");
                }
            }
        }

        public override bool Trigger(World w, string name)
        {
            if (name == "owl" && w.Sound != null) { w.Sound.Play("owl", 1, 0); return true; }
            if (name == "bird" && w.Sound != null) { w.Sound.Play("bird", 1, 0); return true; }
            return false;
        }
    }
}
