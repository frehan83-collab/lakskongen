using System;
using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>What kind of F2 unit an asset belongs to (it sets the asset's life).</summary>
    public enum FacilityAssetKind
    {
        /// <summary>A land site (grow-out lot, RAS hall or post-smolt module).</summary>
        LandSite,
        /// <summary>An offshore unit's steel.</summary>
        OffshoreUnit,
    }

    /// <summary>
    /// One F2 unit's capitalised cost on the books (roadmap F2-04,
    /// docs/FARM_TYPES_SPEC.md §3.2, §3.6): depreciated straight-line from its
    /// commissioning day over its life. Saved in <c>GameState.FacilityAssets</c>.
    /// A unit may hold several assets (a lot added later is its own asset).
    /// </summary>
    public sealed class FacilityAsset
    {
        /// <summary>The unit the asset belongs to (a land site's or an offshore sea site's id).</summary>
        public string UnitId { get; set; } = "";

        /// <summary>Land site or offshore unit.</summary>
        public FacilityAssetKind Kind { get; set; }

        /// <summary>Capitalised cost (NOK): what the company paid, its equity share only.</summary>
        public decimal CapexNok { get; set; }

        /// <summary>First day of depreciation: the day the unit is commissioned.</summary>
        public int StartDay { get; set; }

        /// <summary>Straight-line life (years of 365 days).</summary>
        public int LifeYears { get; set; }

        /// <summary>Depreciation booked so far (NOK).</summary>
        public decimal DepreciatedNok { get; set; }

        /// <summary>Last day whose depreciation is booked (-1 = none yet).</summary>
        public int DepreciatedThroughDay { get; set; } = -1;

        /// <summary>Book value (NOK): capex less depreciation booked.</summary>
        public decimal BookValueNok => CapexNok - DepreciatedNok;
    }

    /// <summary>
    /// The F2 units' fixed-asset register (spec §3.6): straight-line
    /// depreciation, a non-cash P&amp;L line (<c>FacilityDepreciation</c>,
    /// <see cref="FinancialStatements.Depreciation"/>) that lowers the corporate
    /// tax base and nothing else. Pure arithmetic on the saved assets, no RNG:
    /// the depreciation through day <c>d</c> is <c>capex × days / (life × 365)</c>,
    /// capped at the capex, so the daily amounts add up to the capex exactly and
    /// a booking that catches up several days books the same total as daily
    /// bookings would.
    /// <para>
    /// Only F2 units enter the register (land sites and offshore units built
    /// in this game); a sea site, a processing plant or a migrated unit whose
    /// capex was paid before F2 has no asset here (spec §8: depreciation of
    /// non-F2 capex is deliberately out of scope).
    /// </para>
    /// </summary>
    public static class FacilityAssetRegister
    {
        /// <summary>Days of one depreciation year.</summary>
        public const int DaysPerYear = Core.SimCalendar.DaysPerYear;

        /// <summary>Depreciation of one day of <paramref name="asset"/> (NOK; the last day may book less).</summary>
        public static decimal DailyNok(FacilityAsset asset)
        {
            ArgumentNullException.ThrowIfNull(asset);
            return asset.LifeYears <= 0 ? 0m : asset.CapexNok / (asset.LifeYears * (decimal)DaysPerYear);
        }

        /// <summary>
        /// Cumulative depreciation of <paramref name="asset"/> through
        /// <paramref name="day"/> (inclusive): zero before its start day, the
        /// whole capex once its life has run.
        /// </summary>
        public static decimal AccumulatedThrough(FacilityAsset asset, int day)
        {
            ArgumentNullException.ThrowIfNull(asset);
            if (asset.CapexNok <= 0m || asset.LifeYears <= 0 || day < asset.StartDay) return 0m;
            long days = (long)day - asset.StartDay + 1;
            long lifeDays = (long)asset.LifeYears * DaysPerYear;
            return days >= lifeDays ? asset.CapexNok : asset.CapexNok * days / lifeDays;
        }

        /// <summary>
        /// Book every asset's depreciation through <paramref name="day"/> that is
        /// not booked yet and return the total (NOK, never negative). Each asset
        /// keeps what it has booked, so calling it twice for a day books nothing
        /// the second time.
        /// </summary>
        public static decimal BookThrough(IList<FacilityAsset> assets, int day)
        {
            ArgumentNullException.ThrowIfNull(assets);
            decimal total = 0m;
            for (int i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];
                if (asset == null || day <= asset.DepreciatedThroughDay) continue;
                decimal due = AccumulatedThrough(asset, day) - asset.DepreciatedNok;
                if (due > 0m)
                {
                    asset.DepreciatedNok += due;
                    total += due;
                }
                asset.DepreciatedThroughDay = day;
            }
            return total;
        }
    }
}
