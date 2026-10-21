using System;
using LakskongenSim.Core;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// How far one sees through a site's water on a sim day (G3-04, the
    /// underwater view).
    /// </summary>
    /// <param name="VisibilityM">Horizontal sighting range of a black target (m): 4.8 / α.</param>
    /// <param name="AttenuationPerM">Photopic beam attenuation α (m⁻¹): the share of a line
    /// of sight's light lost per metre (a black target's contrast falls as e^(−α r)).</param>
    /// <param name="SeasonalVisibilityM">The season's visibility without a bloom at the site (m).</param>
    /// <param name="BloomChlorophyllUgL">Chlorophyll a an algal bloom at the site adds (µg/L).</param>
    public readonly record struct WaterClarityConditions(
        double VisibilityM,
        double AttenuationPerM,
        double SeasonalVisibilityM,
        double BloomChlorophyllUgL);

    /// <summary>
    /// The water's clarity (roadmap G3-04: the underwater fog follows the sim's
    /// visibility, a bloom's below a clear day's). Pure: no saved state and no
    /// RNG stream, so it changes no save, no golden trace and no other system's
    /// draws. The underwater view reads it through the site's details
    /// (<c>GameBridge.GetSeaSiteDetails</c>, <c>water_visibility_m</c>).
    ///
    /// The measure is the horizontal sighting range of a black target, the
    /// underwater visibility standard Zaneveld &amp; Pegau (2003, "Robust
    /// underwater visibility parameter", Optics Express 11(23):2997-3009)
    /// propose: y = 4.8 / α, with α the photopic beam attenuation (Davies-Colley
    /// 1988's black-disk constant, within 10 % over coastal, estuarine and
    /// inland waters). The fog the underwater view draws is that α.
    ///
    /// <list type="bullet">
    /// <item>The season: a Norwegian fjord's Secchi depth through the year
    /// (<see cref="MonthlySecchiM"/>), converted to a horizontal visibility by
    /// <see cref="HorizontalPerSecchi"/>. The normal plankton cycle is in it
    /// (the spring bloom's and the summer's murk, the clear winter water).</item>
    /// <item>A harmful algal bloom at the site (an "algae" <see cref="BloomEvent"/>)
    /// adds chlorophyll a in proportion to its severity
    /// (<see cref="BloomChlorophyllAtFullSeverityUgL"/>), and chlorophyll adds
    /// attenuation by Voss (1992, Limnol. Oceanogr. 37(3):501-509):
    /// c(490) − c_w(490) = 0.39 Chl^0.57, carried to 532 nm by his spectral
    /// model (× (1.563 − 1.149e-3 × 532) = 0.952) and to photopic attenuation by
    /// Zaneveld &amp; Pegau's proxy α ≈ 0.9 c_pg(532) + α_w:
    /// Δα = 0.9 × 0.952 × 0.39 Chl^0.57 = 0.334 Chl^0.57 (m⁻¹, Chl in µg/L).</item>
    /// </list>
    /// The zone plankton model (<c>Biology.PlanktonBloom</c>) is not read: its
    /// intensity is a bloom-risk index that stays at 0.7-0.8 through the winter
    /// (po3_red_sone, seed 20261012: 0.77 on 1 January of the second year),
    /// when fjord water is at its clearest (Secchi 14-20 m, below).
    /// </summary>
    public static class WaterClarity
    {
        /// <summary>Black-target visibility × photopic beam attenuation (Zaneveld &amp; Pegau 2003).</summary>
        public const double BlackTargetConstant = 4.8;

        /// <summary>
        /// Horizontal black-target visibility per metre of Secchi depth: 0.75.
        /// Derived: y = 4.8 / α and Z_SD = Γ / (α + K_d) (Tyler 1968, Limnol.
        /// Oceanogr. 13(1):1-6; Preisendorfer 1986, Limnol. Oceanogr.
        /// 31(5):909-926) give y / Z_SD = 4.8 (1 + K_d/α) / Γ. Γ = 8 (the
        /// Ocean Optics Web Book's Secchi-disk page and HydroLight's default;
        /// field values 6-9) and K_d/α = 0.25, what the common approximation
        /// K_d ≈ (a + b_b)/μ_d (e.g. Kirk 1994, Light and Photosynthesis in
        /// Aquatic Ecosystems, 2nd ed.) gives for coastal water with
        /// a ≈ 0.12 m⁻¹, b ≈ 0.6 m⁻¹, 2 % backscatter and μ_d ≈ 0.8
        /// (α ≈ 0.72, K_d ≈ 0.17). Γ over 6-9 and K_d/α over 0.15-0.35 move it
        /// within 0.6-1.0. Year 2026 (derivation).
        /// </summary>
        public const double HorizontalPerSecchi = 0.75;

        /// <summary>
        /// Secchi depth (m) through the year, January to December, at mid-month:
        /// 15.7, 14.3, 11.3, 8.3, 7.0, 7.3, 8.2, 7.8, 7.2, 6.0, 9.0, 12.3. The
        /// three-month running mean (across the new year) of the monthly means
        /// at station OH-10 in Hardangerfjorden in 2024 (Statsforvalteren i
        /// Vestland / Rambøll, "Overvåkingsprogram for Hardangerfjorden
        /// 2024-2026, årsrapport 2024", Vedlegg 5; published 2025,
        /// https://inte.statsforvalteren.no/contentassets/1356d24c117846cfafa805b2b4376446/overvakingsprogram-hardangerfjorden-arsrapport-2024-med-vedlegg.pdf):
        /// 18 m on 29 January, 20 and 10 m in February, 10 m in March, 9 m in
        /// April, 6 m in May and June, 11 and 9 m in July, 9 and 8 m in August,
        /// 5 m in September, 8 m in October, 5 m in November, 14 m in December
        /// (summer mean 8.25 m at a summer chlorophyll a of 1.1 µg/L).
        /// Cross-check: ØKOKYST Nordsjøen sør (Miljødirektoratet M-1604|2020,
        /// Rambøll, Tabell 17) summer Secchi depths of 7.7-9.7 m in the Ryfylke
        /// fjords (VT8 Hidlefjorden, VR48 Hjelmelandsfjorden, VR49/VR50
        /// Jøsenfjorden), 2016-2019.
        /// </summary>
        public static readonly double[] MonthlySecchiM =
            { 15.7, 14.3, 11.3, 8.3, 7.0, 7.3, 8.2, 7.8, 7.2, 6.0, 9.0, 12.3 };

        /// <summary>
        /// Chlorophyll a (µg/L) of a harmful algal bloom at full severity: 20.
        /// Game estimate (2026): a dense flagellate bloom, about twice the
        /// plankton model's seasonal bloom at capacity (the calibration
        /// registry's BloomChlorophyllAtCapacityUgL, 10 µg/L, the inner
        /// Oslofjord's high seasonal peak). The 1988 Chrysochromulina polylepis
        /// bloom reached 1e8 cells/L (Dundas, Johannessen, Berge &amp; Heimdal
        /// 1989, "Toxic algal bloom in Scandinavian waters, May-June 1988",
        /// Oceanography 2(1), https://tos.org/oceanography/assets/docs/2-1_dundas.pdf);
        /// at about 0.2 pg chlorophyll a in a cell 6-8 µm across that is 20 µg/L.
        /// </summary>
        public const double BloomChlorophyllAtFullSeverityUgL = 20.0;

        /// <summary>Photopic attenuation per (µg/L)^0.57 of chlorophyll a (m⁻¹): 0.9 × 0.952 × 0.39 (Voss 1992; Zaneveld &amp; Pegau 2003).</summary>
        public const double ChlorophyllAttenuation = 0.334;

        /// <summary>Voss's (1992) chlorophyll exponent.</summary>
        public const double ChlorophyllExponent = 0.57;

        /// <summary>Bloom kind (<see cref="BloomEvent.Kind"/>) that clouds the water.</summary>
        public const string AlgaeBloomKind = "algae";

        /// <summary>
        /// The season's Secchi depth (m) on a sim day: the monthly values sit at
        /// mid-month and the day is interpolated between the two nearest, across
        /// the turn of the year (as <c>ProductionAreas.ClimatologyC</c>).
        /// </summary>
        public static double SeasonalSecchiM(int day)
        {
            double doy = SimCalendar.DayOfYear(day);
            int month = SimCalendar.MonthIndex(day);
            double mid = SimCalendar.MonthMidDayOfYear(month);
            int other = doy >= mid ? month + 1 : month - 1;
            double otherMid = SimCalendar.MonthMidDayOfYear(other);
            if (other > SimCalendar.MonthsPerYear - 1) otherMid += SimCalendar.DaysPerYear;
            if (other < 0) otherMid -= SimCalendar.DaysPerYear;
            double t = (doy - mid) / (otherMid - mid);
            double a = MonthlySecchiM[month];
            double b = MonthlySecchiM[(other + SimCalendar.MonthsPerYear) % SimCalendar.MonthsPerYear];
            return a + (b - a) * t;
        }

        /// <summary>The season's horizontal black-target visibility (m) on a sim day, without a bloom.</summary>
        public static double SeasonalVisibilityM(int day) => SeasonalSecchiM(day) * HorizontalPerSecchi;

        /// <summary>Photopic attenuation (m⁻¹) chlorophyll a (µg/L) adds (Voss 1992).</summary>
        public static double ChlorophyllAttenuationPerM(double chlorophyllUgL)
            => chlorophyllUgL <= 0.0 ? 0.0 : ChlorophyllAttenuation * Math.Pow(chlorophyllUgL, ChlorophyllExponent);

        /// <summary>
        /// The water on a sim day with an algal bloom of <paramref name="bloomSeverity01"/>
        /// (0 none, 1 full) at the site.
        /// </summary>
        public static WaterClarityConditions Derive(int day, double bloomSeverity01)
        {
            double seasonal = SeasonalVisibilityM(day);
            double chlorophyll = Math.Clamp(bloomSeverity01, 0.0, 1.0) * BloomChlorophyllAtFullSeverityUgL;
            double attenuation = BlackTargetConstant / seasonal + ChlorophyllAttenuationPerM(chlorophyll);
            return new WaterClarityConditions(BlackTargetConstant / attenuation, attenuation, seasonal, chlorophyll);
        }
    }
}
