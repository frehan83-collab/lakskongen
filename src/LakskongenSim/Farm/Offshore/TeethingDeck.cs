using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm.Offshore
{
    /// <summary>
    /// The teething deck of a new offshore unit (F2-07 part of spec §3.3
    /// "Teething deck"; F2-08 adds the incidents its defects feed): drawn at
    /// commissioning from the unit's daily uniforms by inverse CDF, revealed by
    /// a shakedown at a Poisson rate or on its pre-drawn day in cycle 1, fixed
    /// by the player. Pure: the draws are the caller's.
    /// </summary>
    public static class TeethingDeck
    {
        /// <summary>Deck slots (and so the most defects a unit can draw): one daily uniform each.</summary>
        public const int Slots = 4;

        /// <summary>The OF1-class deck, in the spec's order.</summary>
        public static readonly IReadOnlyList<TeethingDefectKind> Of1Deck = new[]
        {
            TeethingDefectKind.HarvestZoneCrush, TeethingDefectKind.CableChainWear, TeethingDefectKind.NetSystemChange,
            TeethingDefectKind.BallastPipeLeak, TeethingDefectKind.WinterUlcers,
        };

        /// <summary>
        /// Draw the deck of a unit at commissioning: <paramref name="countDraw"/>
        /// picks the number of defects (uniform over the class's band, the first
        /// unit of a class the full deck, a later one the halved deck),
        /// <paramref name="kindDraw"/> one of the deck's orderings (each equally
        /// likely; the first defects of it are drawn), <paramref name="revealDraws"/>
        /// each slot's reveal day in cycle 1, <paramref name="severityDraw"/> the
        /// winter ulcers' extra mortality.
        /// </summary>
        public static List<TeethingDefect> Draw(string unitId, bool firstOfClass, OffshoreTeethingCalibration cal,
            double countDraw, double kindDraw, IReadOnlyList<double> revealDraws, double severityDraw)
        {
            if (cal == null) throw new ArgumentNullException(nameof(cal));
            if (revealDraws == null || revealDraws.Count < Slots)
                throw new ArgumentException($"{Slots} reveal draws are needed", nameof(revealDraws));
            int min = firstOfClass ? cal.FirstUnitDefectsMin : cal.LaterUnitDefectsMin;
            int max = firstOfClass ? cal.FirstUnitDefectsMax : cal.LaterUnitDefectsMax;
            min = Math.Clamp(min, 0, Slots);
            max = Math.Clamp(Math.Max(min, max), 0, Slots);
            int count = min + Index(countDraw, max - min + 1);
            var order = Permutation(Of1Deck.Count, kindDraw);
            var defects = new List<TeethingDefect>(count);
            for (int slot = 1; slot <= count; slot++)
            {
                var kind = Of1Deck[order[slot - 1]];
                defects.Add(new TeethingDefect
                {
                    Id = $"{unitId}/td_{slot}",
                    Kind = kind,
                    Slot = slot,
                    RevealOffsetDays = 1 + Index(revealDraws[slot - 1], Math.Max(1, cal.Cycle1RevealWindowDays)),
                    MortalityPp = kind switch
                    {
                        TeethingDefectKind.HarvestZoneCrush => cal.HarvestZoneCrushPp,
                        TeethingDefectKind.WinterUlcers => cal.BiologyPpMin + severityDraw * (cal.BiologyPpMax - cal.BiologyPpMin),
                        _ => 0.0,
                    },
                });
            }
            return defects;
        }

        /// <summary>Days the shakedown's reveal rate is given per (spec §3.3 "0.5 per 30 days"; a rate window, not a calendar month).</summary>
        public const double RevealRateWindowDays = 30.0;

        /// <summary>The daily probability a shakedown reveals one hidden defect: 1 − exp(−rate / the rate's window).</summary>
        public static double ShakedownRevealProbabilityPerDay(OffshoreTeethingCalibration cal)
            => 1.0 - Math.Exp(-Math.Max(0.0, cal.ShakedownRevealPer30Days) / RevealRateWindowDays);

        /// <summary>
        /// The daily hazard a firing defect adds to its unit's fish: its extra
        /// percentage points spread over a 365-day cycle as a constant hazard.
        /// </summary>
        public static double DailyMortalityFraction(TeethingDefect defect)
            => defect.MortalityPp <= 0.0 ? 0.0 : 1.0 - Math.Pow(1.0 - Math.Min(0.99, defect.MortalityPp / 100.0), 1.0 / 365.0);

        private static int Index(double u, int count) => Math.Clamp((int)(u * count), 0, Math.Max(0, count - 1));

        /// <summary>The <paramref name="u"/>-th of the n! orderings of 0..n-1 (Lehmer code), each equally likely.</summary>
        internal static int[] Permutation(int n, double u)
        {
            long total = 1;
            for (int i = 2; i <= n; i++) total *= i;
            long index = Math.Clamp((long)(u * total), 0, total - 1);
            var pool = Enumerable.Range(0, n).ToList();
            var result = new int[n];
            for (int i = 0; i < n; i++)
            {
                long block = 1;
                for (int j = 2; j <= n - 1 - i; j++) block *= j;
                int pick = (int)(index / block);
                index %= block;
                result[i] = pool[pick];
                pool.RemoveAt(pick);
            }
            return result;
        }
    }
}
