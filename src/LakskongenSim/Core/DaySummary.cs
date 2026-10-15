using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Finance;
using LakskongenSim.Biology;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Entry category for day summary grouping.
    /// </summary>
    public enum SummaryCategory
    {
        Finance,
        Biology,
        Market,
        Event,
        Upcoming,
        Achievement,
        Empire
    }

    /// <summary>
    /// Sentiment indicator for summary entries.
    /// </summary>
    public enum SummarySentiment
    {
        Positive,
        Neutral,
        Negative
    }

    /// <summary>
    /// Priority level for ordering entries.
    /// </summary>
    public enum SummaryPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// A single entry in the day summary.
    /// </summary>
    public class SummaryEntry
    {
        public SummaryCategory Category { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public SummarySentiment Sentiment { get; set; }
        public SummaryPriority Priority { get; set; }
        public string Icon { get; set; } = "";

        public SummaryEntry(
            SummaryCategory category,
            string title,
            string description,
            SummarySentiment sentiment = SummarySentiment.Neutral,
            SummaryPriority priority = SummaryPriority.Medium,
            string icon = "")
        {
            Category = category;
            Title = title;
            Description = description;
            Sentiment = sentiment;
            Priority = priority;
            Icon = icon;
        }
    }

    /// <summary>
    /// Complete daily summary report with categorized entries.
    /// </summary>
    public class DaySummaryReport
    {
        public int Day { get; set; }
        public List<SummaryEntry> Entries { get; set; } = new();

        // Financial summary
        public decimal DailyRevenue { get; set; }
        public decimal DailyExpenses { get; set; }
        public decimal NetProfit { get; set; }
        public decimal TotalBalance { get; set; }

        // Quick stats
        public int TotalFishCount { get; set; }
        public double AverageFishWeight { get; set; }
        public int HarvestReadyCount { get; set; }
        public decimal MarketPrice { get; set; }

        public DaySummaryReport(int day)
        {
            Day = day;
        }

        /// <summary>
        /// Get entries by category, sorted by priority (critical first).
        /// </summary>
        public List<SummaryEntry> GetByCategory(SummaryCategory category)
        {
            return Entries
                .Where(e => e.Category == category)
                .OrderByDescending(e => e.Priority)
                .ToList();
        }

        /// <summary>
        /// Get highest priority entries across all categories.
        /// </summary>
        public List<SummaryEntry> GetTopPriority(int count = 5)
        {
            return Entries
                .OrderByDescending(e => e.Priority)
                .ThenBy(e => e.Category)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Generates compelling end-of-day summaries that create "one more turn" syndrome.
    /// Collects events, financial changes, fish growth, market movements from the day
    /// and packages them into a structured, psychologically compelling summary.
    /// </summary>
    public class DaySummary
    {
        private readonly SimulationEngine _engine;

        // Track state from previous day for delta calculations
        private decimal _previousBalance = 0;
        private int _previousFishCount = 0;
        private decimal _previousMarketPrice = 0;
        private readonly List<string> _dayEvents = new();

        public DaySummary(SimulationEngine engine)
        {
            _engine = engine;
        }

        /// <summary>
        /// Start tracking a new day. Called at the beginning of TickDay().
        /// </summary>
        public void BeginDay()
        {
            _previousBalance = _engine.FinanceManager.Balance;
            _previousFishCount = CountAllFish();
            _previousMarketPrice = _engine.MarketSimulation.PricePerKg;
            _dayEvents.Clear();
        }

        /// <summary>
        /// Log an event that occurred during the day.
        /// </summary>
        public void LogEvent(string eventDescription)
        {
            _dayEvents.Add(eventDescription);
        }

        /// <summary>
        /// Generate the complete day summary. Called at the end of TickDay().
        /// </summary>
        public DaySummaryReport GenerateSummary()
        {
            var report = new DaySummaryReport(_engine.Time.CurrentDay);
            var currentBalance = _engine.FinanceManager.Balance;
            var currentFishCount = CountAllFish();
            var currentMarketPrice = _engine.MarketSimulation.PricePerKg;

            // ─── Financial Summary ───
            var dailyChange = currentBalance - _previousBalance;
            report.DailyRevenue = dailyChange > 0 ? dailyChange : 0;
            report.DailyExpenses = dailyChange < 0 ? Math.Abs(dailyChange) : 0;
            report.NetProfit = dailyChange;
            report.TotalBalance = currentBalance;

            AddFinanceEntries(report, dailyChange);

            // ─── Biology Summary ───
            report.TotalFishCount = currentFishCount;
            report.AverageFishWeight = CalculateAverageFishWeight();
            report.HarvestReadyCount = CountHarvestReadyFish();

            AddBiologyEntries(report, currentFishCount);

            // ─── Market Summary ───
            report.MarketPrice = currentMarketPrice;
            AddMarketEntries(report, currentMarketPrice);

            // ─── Events Summary ───
            AddEventEntries(report);

            // ─── Upcoming Summary ───
            AddUpcomingEntries(report);

            // ─── Achievement Summary ───
            AddAchievementEntries(report);

            // ─── Empire Summary ───
            AddEmpireEntries(report);

            return report;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Finance
        // ═══════════════════════════════════════════════════════════════════════

        private void AddFinanceEntries(DaySummaryReport report, decimal dailyChange)
        {
            if (dailyChange > 0)
            {
                var sentiment = dailyChange > 100000 ? SummarySentiment.Positive : SummarySentiment.Neutral;
                var priority = dailyChange > 500000 ? SummaryPriority.High : SummaryPriority.Medium;

                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Finance,
                    "Daglig resultat",
                    $"Inntjening: {FormatCurrency(dailyChange)} NOK",
                    sentiment,
                    priority,
                    "💰"
                ));

                if (dailyChange > 1_000_000)
                {
                    report.Entries.Add(new SummaryEntry(
                        SummaryCategory.Finance,
                        "Stor inntekt!",
                        $"Du tjente over 1 million NOK i dag! Momentum bygger seg.",
                        SummarySentiment.Positive,
                        SummaryPriority.High,
                        "🚀"
                    ));
                }
            }
            else if (dailyChange < 0)
            {
                var sentiment = SummarySentiment.Negative;
                var priority = dailyChange < -100000 ? SummaryPriority.High : SummaryPriority.Medium;

                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Finance,
                    "Daglig tap",
                    $"Utgifter: {FormatCurrency(Math.Abs(dailyChange))} NOK",
                    sentiment,
                    priority,
                    "⚠️"
                ));
            }

            // Balance status
            var balanceStatus = GetBalanceStatus(report.TotalBalance);
            if (balanceStatus != null)
                report.Entries.Add(balanceStatus);

            // Cash flow warnings
            if (report.TotalBalance < 1_000_000)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Finance,
                    "Lav likviditet!",
                    $"Kun {FormatCurrency(report.TotalBalance)} NOK igjen. Vurder høsting eller salg.",
                    SummarySentiment.Negative,
                    SummaryPriority.Critical,
                    "🚨"
                ));
            }
        }

        private SummaryEntry? GetBalanceStatus(decimal balance)
        {
            if (balance > 50_000_000)
            {
                return new SummaryEntry(
                    SummaryCategory.Finance,
                    "Sterk økonomi",
                    $"Total saldo: {FormatCurrency(balance)} NOK. Du har rom for ekspansjon!",
                    SummarySentiment.Positive,
                    SummaryPriority.Medium,
                    "💎"
                );
            }
            else if (balance < 5_000_000)
            {
                return new SummaryEntry(
                    SummaryCategory.Finance,
                    "Stram økonomi",
                    $"Saldo: {FormatCurrency(balance)} NOK. Vær forsiktig med store investeringer.",
                    SummarySentiment.Neutral,
                    SummaryPriority.Medium,
                    "⚖️"
                );
            }

            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Biology
        // ═══════════════════════════════════════════════════════════════════════

        private void AddBiologyEntries(DaySummaryReport report, int currentFishCount)
        {
            var fishDelta = currentFishCount - _previousFishCount;

            if (fishDelta > 0)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Biology,
                    "Bestandsvekst",
                    $"+{fishDelta} fisk. Total: {currentFishCount:N0} fisk.",
                    SummarySentiment.Positive,
                    SummaryPriority.Medium,
                    "🐟"
                ));
            }
            else if (fishDelta < 0)
            {
                var isHarvest = _dayEvents.Any(e => e.Contains("høstet") || e.Contains("harvest"));
                var sentiment = isHarvest ? SummarySentiment.Positive : SummarySentiment.Negative;
                var icon = isHarvest ? "📦" : "💀";

                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Biology,
                    isHarvest ? "Høstet fisk" : "Fisketap",
                    $"{Math.Abs(fishDelta)} fisk. Total: {currentFishCount:N0} fisk.",
                    sentiment,
                    SummaryPriority.Medium,
                    icon
                ));
            }

            // Harvest-ready fish
            if (report.HarvestReadyCount > 0)
            {
                var priority = report.HarvestReadyCount > 1000 ? SummaryPriority.High : SummaryPriority.Medium;

                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Biology,
                    "Klar for høsting!",
                    $"{report.HarvestReadyCount:N0} fisk er over 4 kg. Optimal høstetid.",
                    SummarySentiment.Positive,
                    priority,
                    "🎯"
                ));
            }

            // Growth milestones
            AddGrowthMilestones(report);
        }

        private void AddGrowthMilestones(DaySummaryReport report)
        {
            var avgWeight = report.AverageFishWeight;

            if (avgWeight >= 1.0 && avgWeight < 1.2 && _previousFishCount > 0)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Biology,
                    "Vekstmilepæl: 1 kg",
                    "Fisken din passerte 1 kg gjennomsnittsvekt!",
                    SummarySentiment.Positive,
                    SummaryPriority.Medium,
                    "🎉"
                ));
            }
            else if (avgWeight >= 4.0 && avgWeight < 4.5 && _previousFishCount > 0)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Biology,
                    "Vekstmilepæl: 4 kg",
                    "Markedsmodne fisk! Tid for å vurdere høsting.",
                    SummarySentiment.Positive,
                    SummaryPriority.High,
                    "🏆"
                ));
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Market
        // ═══════════════════════════════════════════════════════════════════════

        private void AddMarketEntries(DaySummaryReport report, decimal currentPrice)
        {
            var priceDelta = currentPrice - _previousMarketPrice;
            var priceDeltaPercent = _previousMarketPrice > 0
                ? (double)(priceDelta / _previousMarketPrice) * 100
                : 0;

            if (Math.Abs(priceDeltaPercent) > 2)
            {
                var sentiment = priceDelta > 0 ? SummarySentiment.Positive : SummarySentiment.Negative;
                var icon = priceDelta > 0 ? "📈" : "📉";
                var priority = Math.Abs(priceDeltaPercent) > 5 ? SummaryPriority.High : SummaryPriority.Medium;

                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Market,
                    "Markedspris endret",
                    $"{FormatCurrency(currentPrice)} NOK/kg ({priceDeltaPercent:+0.0;-0.0}%). " +
                    (priceDelta > 0 ? "Bra tid for salg!" : "Vurder å vente."),
                    sentiment,
                    priority,
                    icon
                ));
            }

            // Price opportunity alerts
            if (currentPrice > 80 && report.HarvestReadyCount > 0)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Market,
                    "Høy pris - høst nå!",
                    $"Prisen er {FormatCurrency(currentPrice)} NOK/kg og du har {report.HarvestReadyCount} klar fisk. Perfekt timing!",
                    SummarySentiment.Positive,
                    SummaryPriority.Critical,
                    "💎"
                ));
            }
            else if (currentPrice < 50)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Market,
                    "Lav markedspris",
                    $"Prisen er kun {FormatCurrency(currentPrice)} NOK/kg. Vurder å vente med salg.",
                    SummarySentiment.Negative,
                    SummaryPriority.Medium,
                    "⏳"
                ));
            }

            // Market trends
            AddMarketTrends(report, currentPrice);
        }

        private void AddMarketTrends(DaySummaryReport report, decimal currentPrice)
        {
            var isUptrend = currentPrice > _previousMarketPrice;
            var trendStrength = _previousMarketPrice > 0
                ? Math.Abs((double)(currentPrice - _previousMarketPrice) / (double)_previousMarketPrice)
                : 0;

            if (trendStrength > 0.05)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Market,
                    isUptrend ? "Stigende marked" : "Fallende marked",
                    $"Markedet viser en {(isUptrend ? "positiv" : "negativ")} trend. " +
                    (isUptrend ? "Mulig fortsatt oppgang." : "Mulig lavere priser fremover."),
                    isUptrend ? SummarySentiment.Positive : SummarySentiment.Negative,
                    SummaryPriority.Medium,
                    isUptrend ? "🔼" : "🔽"
                ));
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Events
        // ═══════════════════════════════════════════════════════════════════════

        private void AddEventEntries(DaySummaryReport report)
        {
            foreach (var eventDesc in _dayEvents)
            {
                var sentiment = ClassifyEventSentiment(eventDesc);
                var priority = ClassifyEventPriority(eventDesc);
                var icon = GetEventIcon(eventDesc);

                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Event,
                    "Hendelse",
                    eventDesc,
                    sentiment,
                    priority,
                    icon
                ));
            }
        }

        private SummarySentiment ClassifyEventSentiment(string eventDesc)
        {
            var lower = eventDesc.ToLowerInvariant();

            if (lower.Contains("sykdom") || lower.Contains("død") || lower.Contains("tap") ||
                lower.Contains("disease") || lower.Contains("mortality"))
                return SummarySentiment.Negative;

            if (lower.Contains("vekst") || lower.Contains("høstet") || lower.Contains("kontrakt") ||
                lower.Contains("growth") || lower.Contains("harvest") || lower.Contains("contract"))
                return SummarySentiment.Positive;

            return SummarySentiment.Neutral;
        }

        private SummaryPriority ClassifyEventPriority(string eventDesc)
        {
            var lower = eventDesc.ToLowerInvariant();

            if (lower.Contains("kritisk") || lower.Contains("critical") || lower.Contains("massedød"))
                return SummaryPriority.Critical;

            if (lower.Contains("sykdom") || lower.Contains("tap") || lower.Contains("varsel"))
                return SummaryPriority.High;

            return SummaryPriority.Medium;
        }

        private string GetEventIcon(string eventDesc)
        {
            var lower = eventDesc.ToLowerInvariant();

            if (lower.Contains("sykdom") || lower.Contains("disease")) return "🦠";
            if (lower.Contains("død") || lower.Contains("mortality")) return "💀";
            if (lower.Contains("vekst") || lower.Contains("growth")) return "📈";
            if (lower.Contains("høst") || lower.Contains("harvest")) return "📦";
            if (lower.Contains("kontrakt") || lower.Contains("contract")) return "📝";
            if (lower.Contains("vær") || lower.Contains("weather")) return "🌊";

            return "ℹ️";
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Upcoming
        // ═══════════════════════════════════════════════════════════════════════

        private void AddUpcomingEntries(DaySummaryReport report)
        {
            // Harvest window opportunities
            var daysUntilHarvestWindow = CalculateDaysUntilNextHarvestWindow();
            if (daysUntilHarvestWindow > 0 && daysUntilHarvestWindow <= 5)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Upcoming,
                    "Høstevindu nærmer seg",
                    $"Om {daysUntilHarvestWindow} dager har du fisk klar for høsting.",
                    SummarySentiment.Positive,
                    SummaryPriority.Medium,
                    "⏰"
                ));
            }

            // Seasonal events
            AddSeasonalUpcoming(report);
        }

        private int CalculateDaysUntilNextHarvestWindow()
        {
            var avgGrowthRate = 0.05; // kg/day (simplified)
            var currentWeight = CalculateAverageFishWeight();
            var targetWeight = 4.0;

            if (currentWeight >= targetWeight)
                return 0;

            var daysNeeded = (targetWeight - currentWeight) / avgGrowthRate;
            return (int)Math.Ceiling(daysNeeded);
        }

        private void AddSeasonalUpcoming(DaySummaryReport report)
        {
            var currentMonth = SimCalendar.Month(_engine.Time.CurrentDay);

            if (currentMonth >= 6 && currentMonth <= 8)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Upcoming,
                    "Sommervarsel",
                    "Høye temperaturer fremover. Økt risiko for lakselus og sykdom.",
                    SummarySentiment.Neutral,
                    SummaryPriority.Medium,
                    "☀️"
                ));
            }

            if (currentMonth >= 11 || currentMonth <= 2)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Upcoming,
                    "Vinterfordel",
                    "Lave temperaturer gir færre lakselus og bedre fiskehelse.",
                    SummarySentiment.Positive,
                    SummaryPriority.Low,
                    "❄️"
                ));
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Achievements
        // ═══════════════════════════════════════════════════════════════════════

        private void AddAchievementEntries(DaySummaryReport report)
        {
            var currentFishCount = CountAllFish();

            if (currentFishCount >= 10000 && _previousFishCount < 10000)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Achievement,
                    "🏆 Milepæl: 10,000 fisk!",
                    "Du har nådd 10,000 fisk i bestanden. Storskalaproduksjon!",
                    SummarySentiment.Positive,
                    SummaryPriority.High,
                    "🏆"
                ));
            }

            if (_engine.FinanceManager.Balance >= 100_000_000 && _previousBalance < 100_000_000)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Achievement,
                    "💰 Milepæl: 100M NOK!",
                    "Du har passert 100 millioner NOK i kapital. Vellykket operasjon!",
                    SummarySentiment.Positive,
                    SummaryPriority.High,
                    "💰"
                ));
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Empire
        // ═══════════════════════════════════════════════════════════════════════

        private void AddEmpireEntries(DaySummaryReport report)
        {
            // F2-03: the sea sites hold the offshore units and the land sites the
            // post-smolt sites, so these two lists are every unit the company has.
            var siteCount = _engine.State.SeaSites.Count + _engine.State.RASFacilities.Count;

            if (siteCount < 5 && report.TotalBalance > 20_000_000)
            {
                report.Entries.Add(new SummaryEntry(
                    SummaryCategory.Empire,
                    "Ekspansjonsmulighet",
                    $"Du har {siteCount} anlegg og sterk økonomi. Vurder å ekspandere.",
                    SummarySentiment.Positive,
                    SummaryPriority.Medium,
                    "🏗️"
                ));
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Helpers
        // ═══════════════════════════════════════════════════════════════════════

        private int CountAllFish()
        {
            var total = 0;

            foreach (var site in _engine.State.SeaSites)
            {
                foreach (var pen in site.Pens)
                {
                    total += pen.TotalCount;
                }
            }

            foreach (var ras in _engine.State.RASFacilities)
            {
                foreach (var tank in ras.Tanks)
                {
                    total += tank.TotalCount;
                }
            }

            return total;
        }

        private double CalculateAverageFishWeight()
        {
            var totalWeight = 0.0;
            var totalFish = 0;

            foreach (var site in _engine.State.SeaSites)
            {
                foreach (var pop in site.Pens)
                {
                    totalWeight += pop.GetTotalBiomassKg();
                    totalFish += pop.TotalCount;
                }
            }

            foreach (var ras in _engine.State.RASFacilities)
            {
                foreach (var pop in ras.Tanks)
                {
                    totalWeight += pop.GetTotalBiomassKg();
                    totalFish += pop.TotalCount;
                }
            }

            return totalFish > 0 ? totalWeight / totalFish : 0.0;
        }

        private int CountHarvestReadyFish()
        {
            var count = 0;

            foreach (var site in _engine.State.SeaSites)
            {
                foreach (var pop in site.Pens)
                {
                    foreach (var cohort in pop.Cohorts)
                    {
                        if (cohort.MeanWeightKg >= 4.0)
                        {
                            count += cohort.Count;
                        }
                    }
                }
            }

            // F2-03: offshore units are sea sites (counted above); a grow-out
            // land site's fish are harvest-ready at the same weight.
            foreach (var land in _engine.State.RASFacilities)
            {
                if (land.Kind != Farm.LandSiteKind.GrowOut) continue;
                foreach (var pop in land.Tanks)
                {
                    foreach (var cohort in pop.Cohorts)
                    {
                        if (cohort.MeanWeightKg >= 4.0)
                        {
                            count += cohort.Count;
                        }
                    }
                }
            }

            return count;
        }

        private string FormatCurrency(decimal amount)
        {
            if (amount >= 1_000_000_000)
                return $"{amount / 1_000_000_000:F2} mrd";
            else if (amount >= 1_000_000)
                return $"{amount / 1_000_000:F1} mill";
            else if (amount >= 1_000)
                return $"{amount / 1_000:F0}k";
            else
                return $"{amount:F0}";
        }
    }
}
