using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LakskongenSim.Biology
{
    /// <summary>Fish, kilos and value booked to one cause or removal kind.</summary>
    public sealed class MortalityTally
    {
        /// <summary>Number of fish.</summary>
        public long Fish { get; private set; }

        /// <summary>Live weight of those fish when they died or left (kg).</summary>
        public double Kg { get; private set; }

        /// <summary>Their value at the day's spot price (NOK).</summary>
        public decimal ValueNok { get; private set; }

        internal void Add(long fish, double kg, decimal valueNok)
        {
            Fish += fish;
            Kg += kg;
            ValueNok += valueNok;
        }

        internal void Add(MortalityTally other) => Add(other.Fish, other.Kg, other.ValueNok);
    }

    /// <summary>
    /// One line of a breakdown for presentation (the bridge's P&amp;L story):
    /// a stable id, its category id and NVI code, the translation key of its
    /// label, the fish, kilos and value, and its share of all deaths.
    /// </summary>
    public sealed record MortalityRow(string Id, string Category, string NviCode, string LabelKey,
        long Fish, double Kg, decimal ValueNok, double Share);

    /// <summary>A cohort's deaths and live removals on the ledger's open day.</summary>
    public sealed class CohortDayRecord
    {
        internal CohortDayRecord(FishPopulation population)
        {
            Population = population;
        }

        /// <summary>The population (pen or tank) the cohort lives in.</summary>
        public FishPopulation Population { get; }

        /// <summary>Deaths per <see cref="MortalityCause"/> (indexed by the enum value).</summary>
        public int[] DeathsByCause { get; } = new int[MortalityCauses.All.Length];

        /// <summary>Live removals per <see cref="FishRemoval"/> (indexed by the enum value).</summary>
        public int[] RemovedByKind { get; } = new int[MortalityCauses.AllRemovals.Length];

        /// <summary>All deaths of the cohort today.</summary>
        public int TotalDeaths => DeathsByCause.Sum();

        /// <summary>All live removals of the cohort today.</summary>
        public int TotalRemoved => RemovedByKind.Sum();
    }

    /// <summary>
    /// Totals of a ledger slice: deaths by cause and category, and the fish
    /// that left alive (escapes, harvests, transfers).
    /// </summary>
    public sealed class MortalityBreakdown
    {
        private readonly MortalityTally[] _byCause;
        private readonly MortalityTally[] _byCategory;
        private readonly MortalityTally[] _removed;

        internal MortalityBreakdown(MortalityTally[] byCause, MortalityTally[] removed)
        {
            _byCause = byCause;
            _removed = removed;
            _byCategory = MortalityCauses.AllCategories.Select(_ => new MortalityTally()).ToArray();
            Total = new MortalityTally();
            foreach (var cause in MortalityCauses.All)
            {
                var tally = _byCause[(int)cause];
                _byCategory[(int)MortalityCauses.CategoryOf(cause)].Add(tally);
                Total.Add(tally);
            }
        }

        /// <summary>All deaths in the slice.</summary>
        public MortalityTally Total { get; }

        /// <summary>Deaths booked to one cause.</summary>
        public MortalityTally Deaths(MortalityCause cause) => _byCause[(int)cause];

        /// <summary>Deaths booked to one category (the sum of its causes).</summary>
        public MortalityTally Deaths(MortalityCategory category) => _byCategory[(int)category];

        /// <summary>Fish that left alive in one way.</summary>
        public MortalityTally Removed(FishRemoval removal) => _removed[(int)removal];

        /// <summary>One row per category (NVI order, zeros included), share of all deaths.</summary>
        public IReadOnlyList<MortalityRow> CategoryRows()
            => MortalityCauses.AllCategories.Select(c => Row(MortalityCauses.Id(c), MortalityCauses.Id(c),
                MortalityCauses.NviCode(c), MortalityCauses.TranslationKey(c), Deaths(c))).ToList();

        /// <summary>One row per cause (declaration order, zeros included), share of all deaths.</summary>
        public IReadOnlyList<MortalityRow> CauseRows()
            => MortalityCauses.All.Select(c =>
            {
                var category = MortalityCauses.CategoryOf(c);
                return Row(MortalityCauses.Id(c), MortalityCauses.Id(category), MortalityCauses.NviCode(category),
                    MortalityCauses.TranslationKey(c), Deaths(c));
            }).ToList();

        /// <summary>One row per way of leaving alive (no share: these are not deaths).</summary>
        public IReadOnlyList<MortalityRow> RemovalRows()
            => MortalityCauses.AllRemovals.Select(r => new MortalityRow(MortalityCauses.Id(r), "", "",
                MortalityCauses.TranslationKey(r), Removed(r).Fish, Removed(r).Kg, Removed(r).ValueNok, 0.0)).ToList();

        private MortalityRow Row(string id, string category, string nviCode, string labelKey, MortalityTally t)
            => new(id, category, nviCode, labelKey, t.Fish, t.Kg, t.ValueNok,
                Total.Fish > 0 ? (double)t.Fish / Total.Fish : 0.0);

        /// <summary>
        /// The breakdown as a fixed-width text table: one row per category
        /// (NVI code, fish, share of deaths, kg, value) with its causes
        /// indented below, then the fish that left alive. Used by the probe.
        /// </summary>
        public string FormatTable(string title)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine(string.Format(inv, "  {0,-34} {1,4} {2,12} {3,8} {4,14} {5,16}",
                "category / cause", "NVI", "fish", "share", "kg", "value NOK"));
            long total = Total.Fish;
            foreach (var category in MortalityCauses.AllCategories)
            {
                var tally = Deaths(category);
                sb.AppendLine(Row(inv, MortalityCauses.Id(category), MortalityCauses.NviCode(category), tally, total, 2));
                foreach (var cause in MortalityCauses.All)
                {
                    if (MortalityCauses.CategoryOf(cause) != category || Deaths(cause).Fish == 0) continue;
                    sb.AppendLine(Row(inv, "  " + MortalityCauses.Id(cause), "", Deaths(cause), total, 2));
                }
            }
            sb.AppendLine(Row(inv, "all deaths", "", Total, total, 2));
            foreach (var removal in MortalityCauses.AllRemovals)
                sb.AppendLine(Row(inv, "left alive: " + MortalityCauses.Id(removal), "", Removed(removal), 0, 2));
            return sb.ToString();
        }

        private static string Row(IFormatProvider inv, string label, string code, MortalityTally t, long total, int indent)
        {
            string share = total > 0 ? (100.0 * t.Fish / total).ToString("F1", inv) + " %" : "";
            return new string(' ', indent) + string.Format(inv, "{0,-34} {1,4} {2,12:N0} {3,8} {4,14:N0} {5,16:N0}",
                label, code, t.Fish, share, t.Kg, t.ValueNok);
        }
    }

    /// <summary>
    /// The mortality ledger (S2-05, roadmap D4): every fish that leaves a
    /// cohort is booked here, either as a death with one
    /// <see cref="MortalityCause"/> or as a live <see cref="FishRemoval"/>
    /// (escapes are not deaths). Bookings go through
    /// <see cref="FishPopulation.RemoveDead"/> and
    /// <see cref="FishPopulation.RemoveAlive"/>, which decrement the cohort
    /// and book in one step, so the deaths of a cohort on a day equal the sum
    /// of its deaths by cause exactly.
    /// <para>
    /// The ledger keeps the open day's bookings per cohort (in memory, for the
    /// exact per-cohort check), cumulative totals by cause and per site, and a
    /// rolling <see cref="HistoryDays"/>-day history by day. The cumulative
    /// totals, site totals and history are saved with the
    /// <see cref="MortalityModel"/> snapshot. No RNG is used.
    /// </para>
    /// </summary>
    public sealed class MortalityLedger
    {
        /// <summary>Days of per-day history kept (and saved): more than a year, for the yearly story and seasonal views.</summary>
        public const int HistoryDays = 400;

        /// <summary>Site key used for populations that belong to no site.</summary>
        public const string UnassignedSiteId = "";

        private MortalityTally[] _deaths = NewCauseTallies();
        private MortalityTally[] _removed = NewRemovalTallies();
        private readonly SortedDictionary<string, SiteBook> _sites = new(StringComparer.Ordinal);
        private readonly List<DayBook> _history = new();
        private readonly Dictionary<FishPopulation.Cohort, CohortDayRecord> _today =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>The open day: bookings are dated with it (the engine opens each simulated day).</summary>
        public int Day { get; private set; }

        /// <summary>Spot price (NOK/kg) the open day's losses are valued at.</summary>
        public decimal PriceNokPerKg { get; private set; }

        /// <summary>
        /// Open a new day: later bookings carry <paramref name="day"/> and are
        /// valued at <paramref name="priceNokPerKg"/>; the per-cohort detail of
        /// the previous day is dropped (its totals stay in the history).
        /// </summary>
        public void BeginDay(int day, decimal priceNokPerKg)
        {
            Day = day;
            PriceNokPerKg = priceNokPerKg;
            _today.Clear();
        }

        /// <summary>Per-cohort bookings of the open day (reference identity of the cohort).</summary>
        public IReadOnlyDictionary<FishPopulation.Cohort, CohortDayRecord> TodayByCohort => _today;

        /// <summary>All deaths since the game started.</summary>
        public MortalityBreakdown Cumulative() => new(Copy(_deaths), Copy(_removed));

        /// <summary>All deaths at one site (sea site, RAS facility or offshore farm id) since the game started.</summary>
        public MortalityBreakdown ForSite(string siteId)
            => _sites.TryGetValue(siteId ?? UnassignedSiteId, out var book)
                ? new MortalityBreakdown(Copy(book.Deaths), Copy(book.Removed))
                : new MortalityBreakdown(NewCauseTallies(), NewRemovalTallies());

        /// <summary>Site ids with at least one booking, ordinal order.</summary>
        public IReadOnlyCollection<string> SiteIds => _sites.Keys;

        /// <summary>
        /// Deaths and removals booked on days <paramref name="fromDay"/> to
        /// <paramref name="toDay"/> (inclusive), from the rolling history.
        /// </summary>
        public MortalityBreakdown ForDays(int fromDay, int toDay)
        {
            var deaths = NewCauseTallies();
            var removed = NewRemovalTallies();
            foreach (var day in _history)
            {
                if (day.Day < fromDay || day.Day > toDay) continue;
                AddInto(deaths, day.Deaths);
                AddInto(removed, day.Removed);
            }
            return new MortalityBreakdown(deaths, removed);
        }

        /// <summary>Days currently held in the rolling history (ascending).</summary>
        public IEnumerable<int> HistoryDaysHeld => _history.Select(d => d.Day);

        /// <summary>Book <paramref name="fish"/> deaths of <paramref name="cohort"/> to <paramref name="cause"/>.</summary>
        internal void BookDeaths(FishPopulation population, FishPopulation.Cohort cohort, int fish, MortalityCause cause)
        {
            if (fish <= 0) return;
            var (kg, value) = Weigh(fish, cohort);
            _deaths[(int)cause].Add(fish, kg, value);
            Site(population.SiteId).Deaths[(int)cause].Add(fish, kg, value);
            OpenDay().Deaths[(int)cause].Add(fish, kg, value);
            CohortRecord(population, cohort).DeathsByCause[(int)cause] += fish;
        }

        /// <summary>Book <paramref name="fish"/> of <paramref name="cohort"/> leaving alive as <paramref name="removal"/>.</summary>
        internal void BookRemoval(FishPopulation population, FishPopulation.Cohort cohort, int fish, FishRemoval removal)
        {
            if (fish <= 0) return;
            var (kg, value) = Weigh(fish, cohort);
            _removed[(int)removal].Add(fish, kg, value);
            Site(population.SiteId).Removed[(int)removal].Add(fish, kg, value);
            OpenDay().Removed[(int)removal].Add(fish, kg, value);
            CohortRecord(population, cohort).RemovedByKind[(int)removal] += fish;
        }

        /// <summary>
        /// Split <paramref name="deaths"/> over hazards in proportion to
        /// <paramref name="weights"/> with whole fish that sum exactly to
        /// <paramref name="deaths"/> (largest remainder; ties go to the earlier
        /// hazard). Competing risks over one day: a death is attributed to a
        /// cause with probability hazard / total hazard.
        /// </summary>
        public static int[] Split(int deaths, IReadOnlyList<double> weights)
        {
            var result = new int[weights.Count];
            if (deaths <= 0 || weights.Count == 0) return result;
            double total = 0.0;
            for (int i = 0; i < weights.Count; i++) total += Math.Max(0.0, weights[i]);
            if (!(total > 0.0))
            {
                result[0] = deaths;
                return result;
            }
            var remainders = new double[weights.Count];
            int assigned = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                double exact = deaths * (Math.Max(0.0, weights[i]) / total);
                int whole = (int)Math.Floor(exact);
                result[i] = whole;
                remainders[i] = exact - whole;
                assigned += whole;
            }
            for (int left = deaths - assigned; left > 0; left--)
            {
                int best = 0;
                for (int i = 1; i < remainders.Length; i++)
                    if (remainders[i] > remainders[best]) best = i;
                result[best]++;
                remainders[best] = -1.0;
            }
            return result;
        }

        // ── Save / load ─────────────────────────────────────────────────────

        /// <summary>Saveable state: open day, price, cumulative and per-site totals, rolling history.</summary>
        public MortalityLedgerSnapshot GetSaveSnapshot() => new()
        {
            Day = Day,
            PriceNokPerKg = PriceNokPerKg,
            Deaths = ToEntries(_deaths, i => MortalityCauses.Id(MortalityCauses.All[i])),
            Removed = ToEntries(_removed, i => MortalityCauses.Id(MortalityCauses.AllRemovals[i])),
            Sites = _sites.Select(kvp => new SiteMortalitySnapshot
            {
                SiteId = kvp.Key,
                Deaths = ToEntries(kvp.Value.Deaths, i => MortalityCauses.Id(MortalityCauses.All[i])),
                Removed = ToEntries(kvp.Value.Removed, i => MortalityCauses.Id(MortalityCauses.AllRemovals[i])),
            }).ToList(),
            History = _history.Select(d => new DailyMortalitySnapshot
            {
                Day = d.Day,
                Deaths = ToEntries(d.Deaths, i => MortalityCauses.Id(MortalityCauses.All[i])),
                Removed = ToEntries(d.Removed, i => MortalityCauses.Id(MortalityCauses.AllRemovals[i])),
            }).ToList(),
        };

        /// <summary>Restore a saved ledger; null (a save written before S2-05) starts an empty one.</summary>
        public void RestoreFromSave(MortalityLedgerSnapshot? snapshot)
        {
            _deaths = NewCauseTallies();
            _removed = NewRemovalTallies();
            _sites.Clear();
            _history.Clear();
            _today.Clear();
            Day = snapshot?.Day ?? 0;
            PriceNokPerKg = snapshot?.PriceNokPerKg ?? 0m;
            if (snapshot == null) return;

            FillCauses(_deaths, snapshot.Deaths);
            FillRemovals(_removed, snapshot.Removed);
            if (snapshot.Sites != null)
                foreach (var site in snapshot.Sites)
                {
                    if (site == null) continue;
                    var book = Site(site.SiteId ?? UnassignedSiteId);
                    FillCauses(book.Deaths, site.Deaths);
                    FillRemovals(book.Removed, site.Removed);
                }
            if (snapshot.History != null)
                foreach (var day in snapshot.History.Where(d => d != null).OrderBy(d => d.Day))
                {
                    var book = new DayBook(day.Day);
                    FillCauses(book.Deaths, day.Deaths);
                    FillRemovals(book.Removed, day.Removed);
                    _history.Add(book);
                }
        }

        // ── Internals ───────────────────────────────────────────────────────

        private SiteBook Site(string? siteId)
        {
            string key = siteId ?? UnassignedSiteId;
            if (!_sites.TryGetValue(key, out var book))
            {
                book = new SiteBook();
                _sites[key] = book;
            }
            return book;
        }

        private DayBook OpenDay()
        {
            if (_history.Count > 0 && _history[^1].Day == Day) return _history[^1];
            var book = new DayBook(Day);
            _history.Add(book);
            _history.RemoveAll(d => d.Day <= Day - HistoryDays);
            return book;
        }

        private CohortDayRecord CohortRecord(FishPopulation population, FishPopulation.Cohort cohort)
        {
            if (!_today.TryGetValue(cohort, out var record))
            {
                record = new CohortDayRecord(population);
                _today[cohort] = record;
            }
            return record;
        }

        private (double Kg, decimal ValueNok) Weigh(int fish, FishPopulation.Cohort cohort)
        {
            double kg = fish * cohort.MeanWeightKg;
            if (!double.IsFinite(kg) || kg < 0.0) kg = 0.0;
            return (kg, (decimal)kg * PriceNokPerKg);
        }

        private static MortalityTally[] NewCauseTallies()
            => MortalityCauses.All.Select(_ => new MortalityTally()).ToArray();

        private static MortalityTally[] NewRemovalTallies()
            => MortalityCauses.AllRemovals.Select(_ => new MortalityTally()).ToArray();

        private static MortalityTally[] Copy(MortalityTally[] source)
        {
            var copy = source.Select(_ => new MortalityTally()).ToArray();
            AddInto(copy, source);
            return copy;
        }

        private static void AddInto(MortalityTally[] target, MortalityTally[] source)
        {
            for (int i = 0; i < target.Length; i++) target[i].Add(source[i]);
        }

        private static List<MortalityTallyEntry> ToEntries(MortalityTally[] tallies, Func<int, string> key)
        {
            var list = new List<MortalityTallyEntry>();
            for (int i = 0; i < tallies.Length; i++)
            {
                if (tallies[i].Fish == 0 && tallies[i].Kg == 0.0 && tallies[i].ValueNok == 0m) continue;
                list.Add(new MortalityTallyEntry
                {
                    Key = key(i),
                    Fish = tallies[i].Fish,
                    Kg = tallies[i].Kg,
                    ValueNok = tallies[i].ValueNok,
                });
            }
            return list;
        }

        private static void FillCauses(MortalityTally[] target, List<MortalityTallyEntry>? entries)
        {
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e == null) continue;
                int index = Array.FindIndex(MortalityCauses.All, c => MortalityCauses.Id(c) == e.Key);
                if (index >= 0) target[index].Add(e.Fish, e.Kg, e.ValueNok);
            }
        }

        private static void FillRemovals(MortalityTally[] target, List<MortalityTallyEntry>? entries)
        {
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e == null) continue;
                int index = Array.FindIndex(MortalityCauses.AllRemovals, r => MortalityCauses.Id(r) == e.Key);
                if (index >= 0) target[index].Add(e.Fish, e.Kg, e.ValueNok);
            }
        }

        private sealed class SiteBook
        {
            public MortalityTally[] Deaths { get; } = NewCauseTallies();
            public MortalityTally[] Removed { get; } = NewRemovalTallies();
        }

        private sealed class DayBook
        {
            public DayBook(int day) => Day = day;
            public int Day { get; }
            public MortalityTally[] Deaths { get; } = NewCauseTallies();
            public MortalityTally[] Removed { get; } = NewRemovalTallies();
        }
    }

    /// <summary>One saved tally: a cause or removal id and its fish, kilos and value.</summary>
    public sealed class MortalityTallyEntry
    {
        public string Key { get; set; } = "";
        public long Fish { get; set; }
        public double Kg { get; set; }
        public decimal ValueNok { get; set; }
    }

    /// <summary>Saved per-site ledger totals.</summary>
    public sealed class SiteMortalitySnapshot
    {
        public string SiteId { get; set; } = "";
        public List<MortalityTallyEntry> Deaths { get; set; } = new();
        public List<MortalityTallyEntry> Removed { get; set; } = new();
    }

    /// <summary>Saved ledger totals of one day.</summary>
    public sealed class DailyMortalitySnapshot
    {
        public int Day { get; set; }
        public List<MortalityTallyEntry> Deaths { get; set; } = new();
        public List<MortalityTallyEntry> Removed { get; set; } = new();
    }

    /// <summary>Saved mortality ledger (inside <see cref="MortalityModelSnapshot"/>; null in older saves).</summary>
    public sealed class MortalityLedgerSnapshot
    {
        public int Day { get; set; }
        public decimal PriceNokPerKg { get; set; }
        public List<MortalityTallyEntry> Deaths { get; set; } = new();
        public List<MortalityTallyEntry> Removed { get; set; } = new();
        public List<SiteMortalitySnapshot> Sites { get; set; } = new();
        public List<DailyMortalitySnapshot> History { get; set; } = new();
    }
}
