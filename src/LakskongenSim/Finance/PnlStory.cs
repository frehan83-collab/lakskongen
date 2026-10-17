using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Finance
{
    /// <summary>One calendar month of the P&amp;L story.</summary>
    public sealed class PnlMonth
    {
        /// <summary>Months since the calendar's epoch (<c>SimCalendar.MonthsSinceEpoch</c>).</summary>
        public int Month { get; set; }
        /// <summary>First game day the story saw in this month.</summary>
        public int FirstDay { get; set; }
        /// <summary>Income booked in the month (NOK).</summary>
        public decimal Revenue { get; set; }
        /// <summary>Costs booked in the month (NOK, positive): COGS, operating costs, interest and tax.</summary>
        public decimal Costs { get; set; }
        /// <summary>Cash at the end of the month's last simulated day.</summary>
        public decimal ClosingCash { get; set; }
    }

    /// <summary>
    /// A run of bookings in one ledger line: the income and costs of one
    /// category on days no more than <see cref="PnlStory.MergeGapDays"/> apart
    /// (a harvest's sales and its wellboat and slaughter cost are one run). A
    /// run longer than <see cref="PnlStory.MaxEpisodeDays"/> is a running cost
    /// (feed, wages, interest), never a turning point.
    /// </summary>
    public sealed class PnlEpisode
    {
        /// <summary>Ledger category the bookings were made under ("harvest", "smolts").</summary>
        public string Category { get; set; } = "";
        /// <summary>True when the run's net is income (zero or more), false for a net cost.</summary>
        public bool Income { get; set; }
        /// <summary>Income booked in the run (NOK, positive).</summary>
        public decimal Revenue { get; set; }
        /// <summary>Costs booked in the run (NOK, positive; a refund lowers them).</summary>
        public decimal Costs { get; set; }
        /// <summary>First day of the run.</summary>
        public int FirstDay { get; set; }
        /// <summary>Last day of the run.</summary>
        public int LastDay { get; set; }
        /// <summary>Net P&amp;L of the run (NOK): its income less its costs.</summary>
        public decimal Amount { get; set; }
        /// <summary>Bookings in the run.</summary>
        public int Bookings { get; set; }
        /// <summary>A running cost: it outlasted <see cref="PnlStory.MaxEpisodeDays"/>.</summary>
        public bool Running { get; set; }

        /// <summary>Copy (reports hand out copies, never the saved runs).</summary>
        public PnlEpisode Copy() => (PnlEpisode)MemberwiseClone();
    }

    /// <summary>
    /// Saved state of the P&amp;L story (U2-03, the end screen's "why did I make
    /// or lose money"): a month-by-month result, the largest one-off runs of
    /// the ledger (turning points) and the lifetime total of every line.
    /// Additive in the save.
    /// </summary>
    public sealed class PnlStoryState
    {
        /// <summary>Day the books opened (a new game or scenario), -1 before.</summary>
        public int OpenedDay { get; set; } = -1;
        /// <summary>Cash when the books opened.</summary>
        public decimal OpeningCashNok { get; set; }
        /// <summary>Lowest closing cash so far and its day.</summary>
        public decimal LowestCashNok { get; set; }
        /// <summary>Day of <see cref="LowestCashNok"/>.</summary>
        public int LowestCashDay { get; set; } = -1;
        /// <summary>Months in order.</summary>
        public List<PnlMonth> Months { get; set; } = new();
        /// <summary>Runs still open (at most one per ledger line).</summary>
        public List<PnlEpisode> Open { get; set; } = new();
        /// <summary>Closed one-off runs, largest first (at most <see cref="PnlStory.Capacity"/>).</summary>
        public List<PnlEpisode> Closed { get; set; } = new();
        /// <summary>
        /// Lifetime signed total per ledger line, keyed "+category" (income)
        /// and "-category" (cost); read through <see cref="PnlStory.Total"/>.
        /// </summary>
        public Dictionary<string, decimal> Totals { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// The P&amp;L story's rules (pure; the engine's ledger feeds it every
    /// P&amp;L booking, so its numbers are the ledger's own).
    /// </summary>
    public static class PnlStory
    {
        /// <summary>Bookings of one line this many days apart or closer belong to one run.</summary>
        public const int MergeGapDays = 3;
        /// <summary>A run longer than this is a running cost, not a turning point.</summary>
        public const int MaxEpisodeDays = 14;
        /// <summary>Closed one-off runs kept (the end screen shows three).</summary>
        public const int Capacity = 12;

        /// <summary>Open the books: a fresh story from <paramref name="day"/> with <paramref name="cash"/>.</summary>
        public static void Open(PnlStoryState story, int day, decimal cash)
        {
            ArgumentNullException.ThrowIfNull(story);
            story.OpenedDay = day;
            story.OpeningCashNok = cash;
            story.LowestCashNok = cash;
            story.LowestCashDay = day;
            story.Months.Clear();
            story.Open.Clear();
            story.Closed.Clear();
            story.Totals.Clear();
        }

        /// <summary>The lifetime total of one ledger line (income positive, costs negative).</summary>
        public static decimal Total(PnlStoryState story, string category, bool income)
            => story.Totals.TryGetValue(Key(category, income), out var v) ? v : 0m;

        /// <summary>Lifetime result: every income less every cost.</summary>
        public static decimal NetResult(PnlStoryState story) => story.Totals.Values.Sum();

        /// <summary>
        /// The <paramref name="count"/> largest ledger lines over the whole
        /// story (income and cost lines apart), largest first: where the money
        /// came from and went (a passive decline's story is its running costs).
        /// </summary>
        public static IReadOnlyList<(string Category, bool Income, decimal Amount)> LargestLines(PnlStoryState story, int count)
        {
            ArgumentNullException.ThrowIfNull(story);
            return story.Totals
                .Where(kv => kv.Value != 0m)
                .Select(kv => (Category: kv.Key[1..], Income: kv.Key[0] == '+', Amount: kv.Value))
                .OrderByDescending(l => Math.Abs(l.Amount))
                .ThenBy(l => l.Category, StringComparer.Ordinal)
                .ThenBy(l => l.Income)
                .Take(Math.Max(0, count))
                .ToList();
        }

        /// <summary>
        /// One P&amp;L booking: <paramref name="signedAmount"/> is positive for
        /// income and negative for a cost (a tax refund is a negative cost).
        /// </summary>
        public static void Record(PnlStoryState story, int day, int month, string category, decimal signedAmount, bool income)
        {
            ArgumentNullException.ThrowIfNull(story);
            if (signedAmount == 0m) return;
            if (story.OpenedDay < 0) story.OpenedDay = day;
            category = string.IsNullOrEmpty(category) ? "other" : category;

            var m = MonthOf(story, month, day);
            if (income) m.Revenue += signedAmount;
            else m.Costs -= signedAmount;

            string key = Key(category, income);
            story.Totals[key] = (story.Totals.TryGetValue(key, out var t) ? t : 0m) + signedAmount;

            var run = story.Open.FirstOrDefault(e => e.Category == category);
            if (run != null && day - run.LastDay > MergeGapDays)
            {
                Close(story, run);
                run = null;
            }
            if (run == null)
            {
                run = new PnlEpisode { Category = category, FirstDay = day, LastDay = day };
                story.Open.Add(run);
            }
            run.LastDay = Math.Max(run.LastDay, day);
            run.Amount += signedAmount;
            if (income) run.Revenue += signedAmount;
            else run.Costs -= signedAmount;
            run.Income = run.Amount >= 0m;
            run.Bookings++;
            if (run.LastDay - run.FirstDay + 1 > MaxEpisodeDays)
                run.Running = true;
        }

        /// <summary>
        /// End of a simulated day: the month's closing cash, the lowest cash,
        /// and every run that has gone quiet for longer than the merge gap
        /// closes (a one-off run joins the turning-point list).
        /// </summary>
        public static void EndOfDay(PnlStoryState story, int day, int month, decimal closingCash)
        {
            ArgumentNullException.ThrowIfNull(story);
            if (story.OpenedDay < 0) Open(story, day, closingCash);
            MonthOf(story, month, day).ClosingCash = closingCash;
            if (closingCash < story.LowestCashNok)
            {
                story.LowestCashNok = closingCash;
                story.LowestCashDay = day;
            }
            foreach (var run in story.Open.Where(r => day - r.LastDay > MergeGapDays).ToList())
                Close(story, run);
        }

        /// <summary>
        /// The <paramref name="count"/> largest one-off runs (closed and still
        /// open), by size, then in the order they happened.
        /// </summary>
        public static IReadOnlyList<PnlEpisode> TurningPoints(PnlStoryState story, int count)
        {
            ArgumentNullException.ThrowIfNull(story);
            return story.Closed.Concat(story.Open.Where(r => !r.Running))
                .OrderByDescending(r => Math.Abs(r.Amount))
                .ThenBy(r => r.FirstDay)
                .ThenBy(r => r.Category, StringComparer.Ordinal)
                .Take(Math.Max(0, count))
                .OrderBy(r => r.FirstDay)
                .ThenBy(r => r.Category, StringComparer.Ordinal)
                .Select(r => r.Copy())
                .ToList();
        }

        private static string Key(string category, bool income) => (income ? "+" : "-") + category;

        private static PnlMonth MonthOf(PnlStoryState story, int month, int day)
        {
            var last = story.Months.Count > 0 ? story.Months[^1] : null;
            if (last != null && last.Month == month) return last;
            if (last != null && last.Month > month)
                return story.Months.LastOrDefault(m => m.Month == month) ?? last;
            var created = new PnlMonth { Month = month, FirstDay = day, ClosingCash = last?.ClosingCash ?? story.OpeningCashNok };
            story.Months.Add(created);
            return created;
        }

        private static void Close(PnlStoryState story, PnlEpisode run)
        {
            story.Open.Remove(run);
            if (run.Running || run.Amount == 0m) return;
            story.Closed.Add(run);
            var ordered = story.Closed
                .OrderByDescending(r => Math.Abs(r.Amount))
                .ThenBy(r => r.FirstDay)
                .ThenBy(r => r.Category, StringComparer.Ordinal)
                .Take(Capacity)
                .ToList();
            story.Closed.Clear();
            story.Closed.AddRange(ordered);
        }
    }
}
