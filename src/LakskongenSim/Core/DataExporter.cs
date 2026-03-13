using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LakskongenSim.Events;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Exports simulation data to CSV files for external analysis (Excel, Python, R, etc.).
    ///
    /// Number formatting uses Norwegian locale conventions:
    ///   - Decimal separator: comma (,)
    ///   - Thousands separator: space ( )
    ///   - CSV field separator: semicolon (;) to avoid conflicts with decimal comma
    ///
    /// All monetary values are in NOK.
    /// </summary>
    public class DataExporter
    {
        private static readonly CultureInfo NorwegianCulture =
            CultureInfo.GetCultureInfo("nb-NO");

        // CSV column separator — semicolon because Norwegian locale uses comma as decimal mark
        private const char Sep = ';';

        private readonly GameState _state;

        public DataExporter(GameState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        // ── Public export methods ─────────────────────────────────────────────

        /// <summary>
        /// Exports all recorded daily snapshots to CSV.
        /// Columns: Dag;Fiskeantall;Totalbiomasse_kg;Gjennomsnittsvekt_kg;
        ///          Temperatur_C;Markedspris_NOK;Saldo_NOK;FCR;Lus_per_fisk;
        ///          Baerekraftsscore;Doedsrate
        /// </summary>
        /// <param name="path">Full file path for the output CSV.</param>
        /// <returns>Number of rows written (excluding header).</returns>
        public int ExportDailySnapshots(string path)
        {
            EnsureDirectory(path);

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(Sep,
                "Dag",
                "Fiskeantall",
                "Totalbiomasse_kg",
                "Gjennomsnittsvekt_kg",
                "Temperatur_C",
                "Markedspris_NOK",
                "Saldo_NOK",
                "FCR",
                "Lus_per_fisk",
                "Baerekraftsscore",
                "Doedsrate"
            ));

            var snapshots = _state.DailySnapshots;
            foreach (var snap in snapshots)
            {
                sb.AppendLine(string.Join(Sep,
                    snap.Day,
                    snap.FishCount,
                    FormatDecimal(snap.TotalBiomassKg),
                    FormatDecimal(snap.AvgWeightKg, 4),
                    FormatDecimal(snap.TemperatureC, 2),
                    FormatDecimal(snap.MarketPriceNok, 2),
                    FormatDecimal(snap.BalanceNok, 0),
                    FormatDecimal(snap.FCR, 3),
                    FormatDecimal(snap.LicePerFish, 3),
                    snap.SustainabilityScore,
                    FormatDecimal(snap.MortalityRate, 6)
                ));
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"[DataExporter] Daglige øyeblikksbilder: {snapshots.Count} rader → {path}");
            return snapshots.Count;
        }

        /// <summary>
        /// Exports a financial summary report derived from daily snapshots.
        /// Groups data by 30-day periods and calculates revenue proxies,
        /// balance changes, and trend indicators.
        ///
        /// Columns: Periode;FraDag;TilDag;Saldo_start_NOK;Saldo_slutt_NOK;
        ///          Endring_NOK;Gj_markedspris_NOK;Gj_biomasse_kg;
        ///          Gj_baerekraftsscore;Gj_FCR
        /// </summary>
        /// <param name="path">Full file path for the output CSV.</param>
        /// <returns>Number of period rows written.</returns>
        public int ExportFinancialReport(string path)
        {
            EnsureDirectory(path);

            var snapshots = _state.DailySnapshots;
            if (snapshots.Count == 0)
            {
                File.WriteAllText(path, "Ingen data tilgjengelig." + System.Environment.NewLine, Encoding.UTF8);
                return 0;
            }

            const int periodDays = 30;
            var periods = snapshots
                .GroupBy(s => s.Day / periodDays)
                .OrderBy(g => g.Key)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(Sep,
                "Periode",
                "FraDag",
                "TilDag",
                "Saldo_start_NOK",
                "Saldo_slutt_NOK",
                "Endring_NOK",
                "Gj_markedspris_NOK",
                "Gj_biomasse_kg",
                "Gj_baerekraftsscore",
                "Gj_FCR"
            ));

            int periodIndex = 1;
            foreach (var group in periods)
            {
                var list      = group.OrderBy(s => s.Day).ToList();
                var first     = list.First();
                var last      = list.Last();
                double change = last.BalanceNok - first.BalanceNok;
                double avgPrice = list.Average(s => s.MarketPriceNok);
                double avgBiomass = list.Average(s => s.TotalBiomassKg);
                double avgSust  = list.Average(s => s.SustainabilityScore);
                double avgFCR   = list.Average(s => s.FCR);

                sb.AppendLine(string.Join(Sep,
                    periodIndex,
                    first.Day,
                    last.Day,
                    FormatDecimal(first.BalanceNok, 0),
                    FormatDecimal(last.BalanceNok, 0),
                    FormatDecimal(change, 0),
                    FormatDecimal(avgPrice, 2),
                    FormatDecimal(avgBiomass, 0),
                    FormatDecimal(avgSust, 1),
                    FormatDecimal(avgFCR, 3)
                ));
                periodIndex++;
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"[DataExporter] Finansrapport: {periods.Count} perioder → {path}");
            return periods.Count;
        }

        /// <summary>
        /// Exports detailed fish population data from daily snapshots.
        /// Focuses on biological performance metrics.
        ///
        /// Columns: Dag;Fiskeantall;Totalbiomasse_kg;Gjennomsnittsvekt_kg;
        ///          Temperatur_C;Lus_per_fisk;Doedsrate;Baerekraftsscore
        /// </summary>
        /// <param name="path">Full file path for the output CSV.</param>
        /// <returns>Number of rows written.</returns>
        public int ExportPopulationData(string path)
        {
            EnsureDirectory(path);

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(Sep,
                "Dag",
                "Fiskeantall",
                "Totalbiomasse_kg",
                "Gjennomsnittsvekt_kg",
                "Temperatur_C",
                "Lus_per_fisk",
                "Doedsrate",
                "Baerekraftsscore",
                "FCR"
            ));

            var snapshots = _state.DailySnapshots;
            foreach (var snap in snapshots)
            {
                sb.AppendLine(string.Join(Sep,
                    snap.Day,
                    snap.FishCount,
                    FormatDecimal(snap.TotalBiomassKg, 1),
                    FormatDecimal(snap.AvgWeightKg, 4),
                    FormatDecimal(snap.TemperatureC, 2),
                    FormatDecimal(snap.LicePerFish, 3),
                    FormatDecimal(snap.MortalityRate, 6),
                    snap.SustainabilityScore,
                    FormatDecimal(snap.FCR, 3)
                ));
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"[DataExporter] Populasjonsdata: {snapshots.Count} rader → {path}");
            return snapshots.Count;
        }

        /// <summary>
        /// Exports the full event history log.
        ///
        /// Columns: Dag;Hendelses-ID;Navn;Kategori;Beskrivelse;Saldoendring_NOK
        /// </summary>
        /// <param name="path">Full file path for the output CSV.</param>
        /// <returns>Number of event rows written.</returns>
        public int ExportEventHistory(string path)
        {
            EnsureDirectory(path);

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(Sep,
                "Dag",
                "Hendelses-ID",
                "Navn",
                "Kategori",
                "Beskrivelse",
                "Saldoendring_NOK"
            ));

            var events = _state.EventLog;
            foreach (var ev in events)
            {
                sb.AppendLine(string.Join(Sep,
                    ev.Day,
                    EscapeCsvField(ev.Id),
                    EscapeCsvField(ev.Name),
                    ev.Category.ToString(),
                    EscapeCsvField(ev.Description),
                    FormatDecimal((double)ev.BalanceChange, 0)
                ));
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"[DataExporter] Hendelseslogg: {events.Count} hendelser → {path}");
            return events.Count;
        }

        /// <summary>
        /// Exports all four datasets to the given directory in one call.
        /// Files are named with the current simulation day for easy versioning.
        /// </summary>
        /// <param name="directory">Target directory path.</param>
        public ExportSummary ExportAll(string directory)
        {
            Directory.CreateDirectory(directory);
            int day = _state.Day;

            var summary = new ExportSummary
            {
                DailySnapshotsPath  = Path.Combine(directory, $"daglig_oversikt_dag{day:D4}.csv"),
                FinancialReportPath = Path.Combine(directory, $"finansrapport_dag{day:D4}.csv"),
                PopulationDataPath  = Path.Combine(directory, $"populasjon_dag{day:D4}.csv"),
                EventHistoryPath    = Path.Combine(directory, $"hendelseslogg_dag{day:D4}.csv"),
            };

            summary.DailySnapshotsRows  = ExportDailySnapshots(summary.DailySnapshotsPath);
            summary.FinancialReportRows = ExportFinancialReport(summary.FinancialReportPath);
            summary.PopulationDataRows  = ExportPopulationData(summary.PopulationDataPath);
            summary.EventHistoryRows    = ExportEventHistory(summary.EventHistoryPath);

            Console.WriteLine($"[DataExporter] Eksport fullført: 4 filer i {directory}");
            return summary;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Format a double using Norwegian locale (comma decimal, space thousands).
        /// </summary>
        private static string FormatDecimal(double value, int decimals = 2) =>
            value.ToString($"N{decimals}", NorwegianCulture);

        /// <summary>
        /// Escape a CSV field value: wrap in quotes and double any internal quotes.
        /// Used for free-text fields like descriptions and names.
        /// </summary>
        private static string EscapeCsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Contains(Sep) || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        private static void EnsureDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
    }

    /// <summary>Summary of a multi-file export operation.</summary>
    public class ExportSummary
    {
        public string DailySnapshotsPath  { get; set; } = string.Empty;
        public string FinancialReportPath { get; set; } = string.Empty;
        public string PopulationDataPath  { get; set; } = string.Empty;
        public string EventHistoryPath    { get; set; } = string.Empty;

        public int DailySnapshotsRows  { get; set; }
        public int FinancialReportRows { get; set; }
        public int PopulationDataRows  { get; set; }
        public int EventHistoryRows    { get; set; }

        public int TotalRowsExported =>
            DailySnapshotsRows + FinancialReportRows + PopulationDataRows + EventHistoryRows;
    }
}
