using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Historical weekly salmon spot prices used for market model calibration.
    /// Values represent Norwegian salmon spot/export benchmarks in NOK/kg.
    /// </summary>
    public sealed record HistoricalPrice(string Label, decimal PricePerKg, int? Year = null, int? Week = null)
    {
        public static IReadOnlyList<HistoricalPrice> LoadFromMarkdown(string markdownPath)
        {
            if (!File.Exists(markdownPath))
                return Array.Empty<HistoricalPrice>();

            var prices = new List<HistoricalPrice>();
            var lines = File.ReadAllLines(markdownPath);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("|", StringComparison.Ordinal) || line.Contains("--------", StringComparison.Ordinal))
                    continue;

                var cells = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (cells.Length < 2 || cells[0].Equals("Period", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryParsePrice(cells[1], out var price))
                    continue;

                var label = cells[0];
                prices.Add(new HistoricalPrice(label, price, ParseYear(label), ParseWeek(label)));
            }

            return prices;
        }

        private static bool TryParsePrice(string cell, out decimal price)
        {
            price = 0m;
            var match = Regex.Match(cell, @"-?\d+(?:[\.,]\d+)?");
            if (!match.Success)
                return false;

            var normalized = match.Value.Replace(',', '.');
            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out price);
        }

        private static int? ParseYear(string label)
        {
            var match = Regex.Match(label, @"(20\d{2})");
            return match.Success && int.TryParse(match.Groups[1].Value, out var year) ? year : null;
        }

        private static int? ParseWeek(string label)
        {
            var match = Regex.Match(label, @"Week\s+(\d{1,2})", RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out var week) ? week : null;
        }

        public static decimal Average(IReadOnlyList<HistoricalPrice> prices)
            => prices.Count == 0 ? 0m : prices.Average(p => p.PricePerKg);
    }
}
