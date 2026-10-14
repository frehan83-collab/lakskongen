using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LakskongenSim.Core;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// One of Norway's 13 production areas for salmon farming (roadmap S2-02).
    /// </summary>
    /// <param name="Number">Area number, 1-13 (PO1-PO13).</param>
    /// <param name="Name">Official Norwegian name (produksjonsområdeforskriften, FOR-2017-01-16-61, § 4).</param>
    /// <param name="SeaTemperatureMonthlyC">Normal monthly mean sea temperature at cage depth (3 m), January..December, °C.</param>
    /// <param name="LiceSpringFirstIsoWeek">First ISO week of the spring period with the 0.2 lice limit (luseforskriften § 8).</param>
    /// <param name="LiceSpringLastIsoWeek">Last ISO week of that spring period.</param>
    /// <param name="InPdZone">True inside the national PD zone (FOR-2017-08-29-1318); false in a surveillance zone.</param>
    /// <param name="LicenceMtbTonnes">Normal maximum allowed biomass of one standard licence held in the area (t).</param>
    public sealed record ProductionAreaInfo(
        int Number,
        string Name,
        IReadOnlyList<double> SeaTemperatureMonthlyC,
        int LiceSpringFirstIsoWeek,
        int LiceSpringLastIsoWeek,
        bool InPdZone,
        int LicenceMtbTonnes)
    {
        /// <summary>Short id, "PO1" to "PO13".</summary>
        public string Code => "PO" + Number;
    }

    /// <summary>
    /// The 13 production areas (produksjonsområder) and their offline data:
    /// sea-temperature climatology, the lice spring period, the PD zone and the
    /// licence size. Embedded at build time; the engine never fetches data
    /// (roadmap D18).
    ///
    /// <para><b>Sea temperature.</b> Monthly means at 3 m, the depth farms report
    /// (luseforskriften FOR-2012-12-05-1140 § 6). Derived by
    /// <c>tools/derive_po_sea_temperature.py</c>: the level is Havforskningsinstituttet's
    /// normal winter and summer temperature per area (NorKyst v3, 2012-2024; Albretsen
    /// et al., Rapport fra havforskningen 2025-69, 2025,
    /// https://www.hi.no/hi/nettrapporter/rapport-fra-havforskningen-2025-69), the
    /// month-by-month shape a representative coastal station's 10-year monthly
    /// means (seatemperature.info, read 2026-10-14). <c>tests/test_po_sea_temperature.py</c>
    /// fails when this table and the script disagree.</para>
    ///
    /// <para><b>Lice spring period.</b> Luseforskriften § 8: 0.2 adult female lice
    /// from Monday of week 16 to Sunday of week 21 "i Nord-Trøndelag og sørover"
    /// (PO1-PO7) and weeks 21-26 "i Nordland, Troms og Finnmark" (PO8-PO13);
    /// 0.5 the rest of the year.</para>
    ///
    /// <para><b>PD zone.</b> The national PD regulation (FOR-2017-08-29-1318) puts
    /// PO2-PO6 (Jæren to Nord-Trøndelag) in the PD zone, where PD (SAV) is endemic,
    /// and the rest of the coast in two surveillance zones (Mattilsynet,
    /// https://mattilsynet.no/fisk-og-akvakultur/fiskesykdommer/pancreas-disease-pd/om-nasjonal-forskrift-for-pd-bekjempelse).</para>
    ///
    /// <para><b>Licence size.</b> A licence is normally 780 t, 945 t in Troms and
    /// Finnmark (NFD consultation notes on laksetildelingsforskriften; roadmap S2-01).
    /// PO11-PO13 lie in Troms and Finnmark. PO10 (Andøya til Senja) is mostly Troms
    /// (Senja, Harstad, Kvæfjord); its Andøy sites (Nordland) hold 780 t licences,
    /// which an area-level value cannot express, so the area counts as Troms.</para>
    /// </summary>
    public static class ProductionAreas
    {
        /// <summary>Number of production areas.</summary>
        public const int Count = 13;

        /// <summary>
        /// Area for a zone that names none (tests, legacy saves) when the game has
        /// no home area either: PO8 Helgeland til Bodø, whose climatology is the
        /// closest (RMS 0.91 °C) to the single national curve the engine used before
        /// S2-02, so bands measured on that curve stay comparable.
        /// </summary>
        public const int DefaultArea = 8;

        /// <summary>Freezing point of 35 PSU seawater (°C): the only floor the sea temperature has.</summary>
        public const double SeawaterFreezingPointC = -1.9;

        /// <summary>ISO weeks of the spring period south of Nordland (luseforskriften § 8 first paragraph).</summary>
        public const int SouthLiceSpringFirstWeek = 16, SouthLiceSpringLastWeek = 21;

        /// <summary>ISO weeks of the spring period in Nordland, Troms and Finnmark (§ 8 second paragraph).</summary>
        public const int NorthLiceSpringFirstWeek = 21, NorthLiceSpringLastWeek = 26;

        /// <summary>Normal licence size (t) outside Troms and Finnmark.</summary>
        public const int StandardLicenceMtbTonnes = 780;

        /// <summary>Normal licence size (t) in Troms and Finnmark.</summary>
        public const int NorthernLicenceMtbTonnes = 945;

        // BEGIN derive_po_sea_temperature.py — monthly means at 3 m, Jan..Dec, °C
        private static readonly Dictionary<int, double[]> SeaTemperatureTable = new()
        {
            [1] = new[] { 4.93, 3.60, 3.50, 5.65, 9.64, 13.93, 16.49, 17.00, 15.26, 12.09, 9.33, 6.67 },
            [2] = new[] { 5.83, 4.69, 4.50, 6.02, 8.67, 11.80, 14.27, 15.50, 14.36, 11.90, 9.72, 7.34 },
            [3] = new[] { 6.78, 5.73, 5.50, 6.73, 9.15, 11.98, 14.50, 15.50, 14.45, 12.26, 10.16, 8.19 },
            [4] = new[] { 6.76, 5.60, 5.50, 6.76, 9.38, 12.49, 15.31, 15.50, 13.95, 11.62, 9.58, 7.93 },
            [5] = new[] { 6.75, 5.79, 5.50, 6.56, 8.86, 11.64, 14.04, 15.00, 13.85, 11.64, 9.63, 7.99 },
            [6] = new[] { 7.05, 6.18, 6.00, 6.79, 9.07, 11.70, 13.71, 14.50, 13.27, 11.35, 9.51, 7.93 },
            [7] = new[] { 6.40, 5.66, 5.50, 6.32, 8.44, 11.13, 13.09, 13.50, 12.11, 10.23, 8.52, 7.21 },
            [8] = new[] { 5.85, 5.05, 5.00, 5.52, 7.47, 10.41, 12.98, 13.50, 12.08, 10.32, 8.56, 7.04 },
            [9] = new[] { 6.17, 5.29, 5.00, 5.36, 7.04, 9.73, 11.99, 12.50, 11.19, 9.73, 8.42, 7.26 },
            [10] = new[] { 6.15, 4.76, 4.50, 5.28, 6.93, 9.88, 12.22, 13.00, 11.18, 9.62, 8.40, 7.19 },
            [11] = new[] { 5.36, 4.36, 4.00, 4.36, 5.90, 8.43, 10.69, 11.50, 10.33, 8.79, 7.61, 6.53 },
            [12] = new[] { 5.50, 4.46, 4.00, 4.00, 5.04, 8.04, 10.58, 11.50, 10.58, 9.19, 8.04, 6.54 },
            [13] = new[] { 5.09, 4.26, 4.00, 4.11, 5.65, 8.12, 10.63, 11.00, 9.50, 8.08, 6.99, 5.87 },
        };
        // END derive_po_sea_temperature.py

        private static readonly string[] Names =
        {
            "Svenskegrensen til Jæren",
            "Ryfylke",
            "Karmøy til Sotra",
            "Nordhordland til Stadt",
            "Stadt til Hustadvika",
            "Nordmøre og Sør-Trøndelag",
            "Nord-Trøndelag med Bindal",
            "Helgeland til Bodø",
            "Vestfjorden og Vesterålen",
            "Andøya til Senja",
            "Kvaløya til Loppa",
            "Vest-Finnmark",
            "Øst-Finnmark",
        };

        private static readonly ProductionAreaInfo[] Areas = BuildAreas();

        private static ProductionAreaInfo[] BuildAreas()
        {
            var areas = new ProductionAreaInfo[Count];
            for (int po = 1; po <= Count; po++)
            {
                bool north = po >= 8;   // Nordland, Troms and Finnmark
                bool tromsFinnmark = po >= 10;
                areas[po - 1] = new ProductionAreaInfo(
                    po,
                    Names[po - 1],
                    Array.AsReadOnly(SeaTemperatureTable[po]),
                    north ? NorthLiceSpringFirstWeek : SouthLiceSpringFirstWeek,
                    north ? NorthLiceSpringLastWeek : SouthLiceSpringLastWeek,
                    InPdZone: po >= 2 && po <= 6,
                    tromsFinnmark ? NorthernLicenceMtbTonnes : StandardLicenceMtbTonnes);
            }
            return areas;
        }

        /// <summary>All 13 areas, PO1 first.</summary>
        public static IReadOnlyList<ProductionAreaInfo> All => Areas;

        /// <summary>True for 1-13.</summary>
        public static bool IsValid(int productionArea) => productionArea >= 1 && productionArea <= Count;

        /// <summary>The area's data. Throws for a number outside 1-13.</summary>
        public static ProductionAreaInfo Get(int productionArea)
        {
            if (!IsValid(productionArea))
                throw new ArgumentOutOfRangeException(nameof(productionArea), productionArea, "Production areas are numbered 1-13");
            return Areas[productionArea - 1];
        }

        /// <summary>
        /// Climatological sea temperature (°C, 3 m) of an area on a sim day: the
        /// monthly means sit at mid-month and the day is interpolated linearly
        /// between the two nearest, across the turn of the year.
        /// </summary>
        public static double ClimatologyC(int productionArea, int day)
        {
            var monthly = Get(productionArea).SeaTemperatureMonthlyC;
            double doy = SimCalendar.DayOfYear(day);
            int month = SimCalendar.MonthIndex(day);
            double mid = SimCalendar.MonthMidDayOfYear(month);
            int other = doy >= mid ? month + 1 : month - 1;
            double otherMid = SimCalendar.MonthMidDayOfYear(other);
            // Months on the other side of the new year sit a year away.
            if (other > 11) otherMid += SimCalendar.DaysPerYear;
            if (other < 0) otherMid -= SimCalendar.DaysPerYear;
            double t = (doy - mid) / (otherMid - mid);
            double a = monthly[month];
            double b = monthly[(other + SimCalendar.MonthsPerYear) % SimCalendar.MonthsPerYear];
            return a + (b - a) * t;
        }

        private static readonly Regex ZoneIdPattern =
            new(@"^po[\s_\-]?0?(\d{1,2})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The area a zone id names, for ids that start with it: "PO04", "po4",
        /// "PO-12", "po3_nord". False for every other id.
        /// </summary>
        public static bool TryParseZoneId(string? zoneId, out int productionArea)
        {
            productionArea = 0;
            if (string.IsNullOrWhiteSpace(zoneId)) return false;
            var match = ZoneIdPattern.Match(zoneId.Trim());
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int po) || !IsValid(po))
                return false;
            productionArea = po;
            return true;
        }

        /// <summary>
        /// The area a scenario's starting region stands for when its sites name no
        /// area (mods, legacy entries): the region's largest or best-known farming
        /// area.
        /// </summary>
        public static int DefaultForRegion(Data.ProductionRegion region) => region switch
        {
            Data.ProductionRegion.Sørlandet => 1,
            Data.ProductionRegion.Rogaland => 2,
            Data.ProductionRegion.Vestland => 3,
            Data.ProductionRegion.MøreOgRomsdal => 5,
            Data.ProductionRegion.Trøndelag => 6,
            Data.ProductionRegion.Nordland => 8,
            Data.ProductionRegion.Troms => 11,
            Data.ProductionRegion.Finnmark => 12,
            _ => DefaultArea,
        };

        /// <summary>
        /// The lice limit (adult females per fish) a site in the area must stay
        /// under on a sim day: 0.2 in the area's spring weeks, 0.5 otherwise.
        /// </summary>
        public static double LiceLimitAdultFemales(int productionArea, int day)
            => IsLiceSpringPeriod(productionArea, day) ? SpringLiceLimit : StandardLiceLimit;

        /// <summary>True in the area's spring weeks (luseforskriften § 8).</summary>
        public static bool IsLiceSpringPeriod(int productionArea, int day)
        {
            var area = Get(productionArea);
            return SimCalendar.InIsoWeeks(day, area.LiceSpringFirstIsoWeek, area.LiceSpringLastIsoWeek);
        }

        /// <summary>Spring-period lice limit, adult female lice per fish (luseforskriften § 8).</summary>
        public const double SpringLiceLimit = 0.2;

        /// <summary>Lice limit outside the spring period (luseforskriften § 8).</summary>
        public const double StandardLiceLimit = 0.5;
    }
}
