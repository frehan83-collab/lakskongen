using System;
using System.Collections.Generic;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Sludge valorization system (Phase 5.3).
    /// Converts sea-site sludge and processing waste into biogas (energy credit) and
    /// fertilizer (revenue). Based on Norwegian pilot projects: each tonne dry solids
    /// yields ~400 Nm³ biogas (~4 MWh) + 800 kg N-rich fertilizer.
    /// CAPEX: 15M NOK per site for collection + digestion (semi-closed) or 40M for
    /// centralized hub serving 4-6 sites. OPEX: ~500 NOK/tonne DS treated.
    /// Revenue: biogas offsets energy cost (~0.8 NOK/kWh), fertilizer ~3 NOK/kg N.
    /// </summary>
    public static class SludgeValorization
    {
        /// <summary>Per-site sludge generation parameters.</summary>
        public sealed record SludgeParams(
            double KgDrySolidsPerTonneFeed,  // ~15-25 kg DS / tonne feed
            double CollectionEfficiency,     // 0.6-0.9 depending on containment
            double BiogasYieldNm3PerKgDs,    // ~0.4 Nm³/kg DS
            double FertilizerKgPerKgDs,      // ~0.8 kg fertilizer/kg DS
            decimal BiogasValuePerNm3,       // ~0.35 NOK/Nm³ (energy offset)
            decimal FertilizerValuePerKg,    // ~3.0 NOK/kg N
            decimal OpexPerTonneDs           // ~500 NOK/tonne DS
        );

        /// <summary>Default parameters (mid-range Norwegian data).</summary>
        public static readonly SludgeParams Default = new(
            KgDrySolidsPerTonneFeed: 20.0,
            CollectionEfficiency: 0.75,
            BiogasYieldNm3PerKgDs: 0.4,
            FertilizerKgPerKgDs: 0.8,
            BiogasValuePerNm3: 0.35m,
            FertilizerValuePerKg: 3.0m,
            OpexPerTonneDs: 500m
        );

        /// <summary>Containment-level collection efficiency multipliers.</summary>
        public static readonly Dictionary<ContainmentLevel, double> ContainmentCollectionMult = new()
        {
            [ContainmentLevel.OpenNet] = 0.30,
            [ContainmentLevel.Skirt] = 0.40,
            [ContainmentLevel.SemiClosed] = 0.70,
            [ContainmentLevel.ClosedFloat] = 0.85,
            [ContainmentLevel.Submersible] = 0.80,
            [ContainmentLevel.OffshoreClosed] = 0.60
        };

        /// <summary>
        /// Calculate daily sludge production (kg dry solids) for a sea site
        /// based on feed used and containment level.
        /// </summary>
        public static double DailySludgeKgDs(SeaSite site, double feedUsedKgToday)
        {
            var eff = ContainmentCollectionMult.TryGetValue(site.Containment, out var m) ? m : Default.CollectionEfficiency;
            return feedUsedKgToday / 1000.0 * Default.KgDrySolidsPerTonneFeed * eff;
        }

        /// <summary>
        /// Valorize accumulated sludge: returns (biogasRevenue, fertilizerRevenue, opexCost).
        /// </summary>
        public static (decimal BiogasRevenue, decimal FertilizerRevenue, decimal OpexCost) Valorize(
            double totalKgDs, SludgeParams? ps = null)
        {
            var p = ps ?? Default;
            double biogasNm3 = totalKgDs * p.BiogasYieldNm3PerKgDs;
            double fertilizerKg = totalKgDs * p.FertilizerKgPerKgDs;
            decimal biogasRev = (decimal)biogasNm3 * p.BiogasValuePerNm3;
            decimal fertRev = (decimal)fertilizerKg * p.FertilizerValuePerKg;
            decimal opex = (decimal)(totalKgDs / 1000.0) * p.OpexPerTonneDs;
            return (biogasRev, fertRev, opex);
        }

        /// <summary>
        /// Centralized hub serving multiple sites (economy of scale).
        /// CAPEX 40M NOK, serves up to 6 sites, collection eff 0.85, OPEX 400 NOK/t DS.
        /// </summary>
        public sealed record HubConfig(
            decimal CapexNok = 40_000_000m,
            int MaxSites = 6,
            double CollectionEfficiency = 0.85,
            decimal OpexPerTonneDs = 400m
        );
    }

    /// <summary>
    /// Tracks sludge accumulation and valorization state per sea site.
    /// </summary>
    public class SludgeTracker
    {
        public string SiteId { get; init; } = "";
        public double AccumulatedKgDs { get; private set; } = 0.0;
        public double LastValorizedKgDs { get; private set; } = 0.0;
        public int LastValorizationDay { get; private set; } = -1;
        public decimal TotalBiogasRevenue { get; private set; } = 0m;
        public decimal TotalFertilizerRevenue { get; private set; } = 0m;
        public decimal TotalOpexPaid { get; private set; } = 0m;
        public bool HasCentralHub { get; set; } = false;

        public void AddSludge(double kgDs)
        {
            AccumulatedKgDs += kgDs;
        }

        /// <summary>
        /// Run valorization on accumulated sludge. Returns (biogasRev, fertRev, opex).
        /// </summary>
        public (decimal BiogasRevenue, decimal FertilizerRevenue, decimal OpexCost) Valorize(int currentDay, SludgeValorization.SludgeParams? ps = null)
        {
            if (AccumulatedKgDs < 100.0) // minimum batch ~100 kg DS
                return (0m, 0m, 0m);

            var (bio, fert, opex) = SludgeValorization.Valorize(AccumulatedKgDs, ps);
            TotalBiogasRevenue += bio;
            TotalFertilizerRevenue += fert;
            TotalOpexPaid += opex;
            LastValorizedKgDs = AccumulatedKgDs;
            LastValorizationDay = currentDay;
            AccumulatedKgDs = 0.0;
            return (bio, fert, opex);
        }

        public double ProjectedDailySludgeKgDs(double dailyFeedKg, ContainmentLevel containment)
        {
            return SludgeValorization.DailySludgeKgDs(new SeaSite { Containment = containment }, dailyFeedKg);
        }
    }
}