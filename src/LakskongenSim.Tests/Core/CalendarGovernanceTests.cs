using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Finance;
using LakskongenSim.Market;
using Xunit;

namespace LakskongenSim.Tests.Core
{
    /// <summary>
    /// Roadmap S2-02 acceptance 1: grep finds no "% 360" and no 30-day months, and
    /// one calendar helper (<see cref="SimCalendar"/>) serves the market, the
    /// weather, the lice rule and finance.
    /// </summary>
    public class CalendarGovernanceTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        /// <summary>Calendar arithmetic outside SimCalendar: what the old three calendars looked like.</summary>
        private static readonly (string Name, Regex Pattern)[] CSharpBans =
        {
            ("% 360", new Regex(@"%\s*360\b")),
            ("360-day year", new Regex(@"/\s*360(\.0)?[mdf]?\b")),
            ("day of year by % 365", new Regex(@"%\s*365\b")),
            ("30-day month (day / 30, day % 30)", new Regex(@"\b\w*[Dd]ays?\w*\)?\s*[/%]\s*30(\.0)?[mdf]?\b")),
            ("30-day month (months * 30)", new Regex(@"\b\w*[Mm]onths?\w*\s*\*\s*30(\.0)?[mdf]?\b")),
            ("30-day month (30 * months)", new Regex(@"\b30(\.0)?[mdf]?\s*\*\s*\w*[Mm]onths?")),
            ("30-day month (monthly / 30)", new Regex(@"[Mm]onthly\w*(\([^)]*\))?\s*/\s*30(\.0)?[mdf]?\b")),
        };

        private static readonly (string Name, Regex Pattern)[] GdScriptBans =
        {
            ("% 360", new Regex(@"%\s*360\b")),
            ("30-day month (day / 30, day % 30)", new Regex(@"\b\w*[Dd]ays?\w*\)?\s*[/%]\s*30(\.0)?\b")),
            ("30-day month (premium / 30)", new Regex(@"(premium|monthly)\w*\s*/\s*30(\.0)?\b")),
        };

        private static string StripComment(string line, string marker)
        {
            int i = line.IndexOf(marker, StringComparison.Ordinal);
            return i >= 0 ? line.Substring(0, i) : line;
        }

        private static List<string> Violations(string root, string searchPattern, string commentMarker,
            (string Name, Regex Pattern)[] bans, params string[] allowedFiles)
        {
            var found = new List<string>();
            foreach (var file in Directory.EnumerateFiles(root, searchPattern, SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/") || allowedFiles.Contains(rel))
                    continue;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripComment(lines[i], commentMarker);
                    foreach (var (name, pattern) in bans)
                        if (pattern.IsMatch(code))
                            found.Add($"{rel}:{i + 1}: {name}: {lines[i].Trim()}");
                }
            }
            return found;
        }

        [Fact]
        public void TheEngine_HasNoCalendarArithmetic_OutsideSimCalendar()
        {
            var found = Violations(Path.Combine(RepoRoot(), "src", "LakskongenSim"), "*.cs", "//", CSharpBans,
                "src/LakskongenSim/Core/SimCalendar.cs");
            Assert.True(found.Count == 0, "calendar arithmetic outside SimCalendar:\n" + string.Join("\n", found));
        }

        [Fact]
        public void TheGame_HasNo30DayMonths_OrModulo360()
        {
            var found = Violations(Path.Combine(RepoRoot(), "godot", "scripts"), "*.gd", "#", GdScriptBans);
            Assert.True(found.Count == 0, "30-day months in GDScript:\n" + string.Join("\n", found));
        }

        [Fact]
        public void TheBanList_CatchesTheOldCalendars()
        {
            // The patterns above must still recognise what S2-02 removed.
            string[] old =
            {
                "int month = dayOfYear / 30; // Approximate month (0-11)",
                "double dayInMonth = (dayOfYear % 30) / 30.0;",
                "int month = ((simulationDay / 30) % 12 + 12) % 12;",
                "if (State.Day % 30 == 0)",
                "TermDays = Math.Max(30, termMonths * 30),",
                "currentDay < StartDay + DurationMonths * 30;",
                "dailyWages += member.MonthlySalaryNok / 30m;",
                "int weekOfYear = (State.Day % 365) / 7;",
                "return (baseDir + tidalVariation + 360) % 360;",
                "CalibrationLevel = 0.5 + (DaysSinceInstallation / 360.0) * 0.45;",
                "var currentMonth = (_engine.Time.CurrentDay / 30) % 12 + 1;",
            };
            foreach (var line in old)
                Assert.True(CSharpBans.Any(b => b.Pattern.IsMatch(line)), $"not caught: {line}");
            Assert.True(GdScriptBans.Any(b => b.Pattern.IsMatch("if day % 30 == 1:")));
            Assert.True(GdScriptBans.Any(b => b.Pattern.IsMatch("var auto_cost: int = int(premium / 30.0)")));
        }

        [Fact]
        public void Market_Weather_Lice_AndFinance_AllReadSimCalendar()
        {
            string root = RepoRoot();
            string[] consumers =
            {
                "src/LakskongenSim/Market/MarketSimulation.cs",          // market seasonality
                "src/LakskongenSim/Environment/WeatherSystem.cs",        // weather and storm season
                "src/LakskongenSim/Environment/ProductionAreas.cs",      // lice spring weeks, climatology
                "src/LakskongenSim/Core/SimulationEngine.cs",            // daily finance, lice census
                "src/LakskongenSim/Core/InDayTickSystems.cs",            // quarterly and monthly books
                "src/LakskongenSim/Core/SimulationEngine.Commands.cs",   // loan terms
            };
            foreach (var rel in consumers)
                Assert.Contains("SimCalendar.", File.ReadAllText(Path.Combine(root, rel)));
        }

        [Fact]
        public void MarketSeasonality_ChangesMonth_OnTheCalendarsFirsts()
        {
            var market = new MarketSimulation(new SimRng(3).Stream(SimRngStreams.Market));
            var offsets = (decimal[])typeof(MarketSimulation)
                .GetField("_monthlySeasonalOffsets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(market)!;
            var offsetFor = typeof(MarketSimulation)
                .GetMethod("GetSeasonalOffsetForSimulationDay", BindingFlags.Instance | BindingFlags.NonPublic)!;
            decimal Offset(int day) => (decimal)offsetFor.Invoke(market, new object[] { day })!;

            // 31 January is still January (a 30-day month put day 30 in February),
            // 1 March is March (it was 2 March), 31 December is December.
            Assert.Equal(offsets[0], Offset(30));
            Assert.Equal(offsets[1], Offset(31));
            Assert.Equal(offsets[1], Offset(58));
            Assert.Equal(offsets[2], Offset(59));
            Assert.Equal(offsets[11], Offset(364));
            Assert.Equal(offsets[0], Offset(365));
        }

        [Fact]
        public void StormSeason_IsOctoberThroughMarch_OnTheCalendar()
        {
            for (int day = 0; day < SimCalendar.DaysPerYear; day++)
            {
                int month = SimCalendar.Month(day);
                Assert.Equal(month >= 10 || month <= 3, WeatherSystem.IsStormSeason(day));
            }
        }

        [Fact]
        public void TheFinancialYear_ClosesOn31December_AndQuartersOnTheirLastDays()
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(50_000_000m) };
            engine.Initialize(enableEvents: false, seed: 5);
            var closes = new List<(int Day, string Message)>();
            engine.OnEventTriggered += message =>
            {
                // Corporate finance: "📊 Q1 2027: Covenants OK" or "📋 YEAR 2027 CLOSE: ..."
                // (the rivals' "Industry Report" fires on the same quarter ends).
                if (message.Contains(" CLOSE:", StringComparison.Ordinal)
                    || (message.StartsWith("📊 Q", StringComparison.Ordinal) && message.Contains(": Covenants", StringComparison.Ordinal)))
                    closes.Add((engine.State.Day, message));
            };

            engine.Advance(2 * SimCalendar.DaysPerYear);

            // Q1-Q3 and the year close on the quarter's last day, twice; the
            // 90-day cadence closed "years" on days 360 and 720.
            Assert.Equal(new[] { 89, 180, 272, 364, 454, 545, 637, 729 }, closes.Select(c => c.Day));
            Assert.Contains("CLOSE", closes[3].Message);
            Assert.Contains("CLOSE", closes[7].Message);
        }
    }
}
