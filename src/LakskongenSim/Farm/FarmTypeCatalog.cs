using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LakskongenSim.Calibration;
using LakskongenSim.Core;

namespace LakskongenSim.Farm
{
    /// <summary>The farm-type catalogue's ids (families, variants, axes, gates).</summary>
    public static class FarmTypeIds
    {
        /// <summary>Family: open and closed sea pens ("Sjø").</summary>
        public const string Sjo = "sjo";
        /// <summary>Family: land-based farming ("Land").</summary>
        public const string Land = "land";
        /// <summary>Family: offshore farming ("Hav").</summary>
        public const string Hav = "hav";

        /// <summary>Variant: the open net pen ("Åpen merd").</summary>
        public const string OpenPen = "open_pen";
        /// <summary>Variant: the semi-closed or closed pen upgrade.</summary>
        public const string ClosedPen = "closed_pen";
        /// <summary>Variant: the post-smolt module.</summary>
        public const string PostSmolt = "post_smolt";
        /// <summary>Variant: the hybrid flow-through land farm.</summary>
        public const string Hfs = "hfs";
        /// <summary>Variant: the RAS land farm (stage 3).</summary>
        public const string Ras = "ras";
        /// <summary>Variant: the OF1-class semi-submersible.</summary>
        public const string Of1 = "of1";
        /// <summary>Variant: the Havfarm-class ship (stage 3).</summary>
        public const string Havfarm = "havfarm";

        /// <summary>Every variant id, in catalogue order.</summary>
        public static readonly IReadOnlyList<string> Variants = new[] { OpenPen, ClosedPen, PostSmolt, Hfs, Ras, Of1, Havfarm };

        /// <summary>Axis: capacity cost, NOK per tonne of yearly capacity including the licence.</summary>
        public const string CapexPerTonneYear = "capex_nok_per_t_year";
        /// <summary>Axis: farming cost, NOK per kg HOG.</summary>
        public const string OpexPerKg = "opex_nok_per_kg_hog";
        /// <summary>Axis: survival per cycle (median and bad case).</summary>
        public const string Survival = "survival";
        /// <summary>Axis: external lice pressure against an open fjord pen.</summary>
        public const string ExternalLice = "external_lice_factor";
        /// <summary>Axis: electricity per kg produced.</summary>
        public const string Energy = "energy_kwh_per_kg";
        /// <summary>Axis: days from the order to a working unit.</summary>
        public const string BuildDays = "build_days";
        /// <summary>Axis: yearly capacity of one unit (tonnes).</summary>
        public const string Capacity = "capacity_t_year";

        /// <summary>Every axis, in card order: the same for every variant.</summary>
        public static readonly IReadOnlyList<string> Axes = new[] { CapexPerTonneYear, OpexPerKg, Survival, ExternalLice, Energy, BuildDays, Capacity };
    }

    /// <summary>What a variant's unlock gate asks for (docs/FARM_TYPES_SPEC.md §2C, FR3: every gate, money included).</summary>
    public enum FarmTypeGateKind
    {
        /// <summary>Open from the start.</summary>
        Start,
        /// <summary>A research node unlocked (<see cref="FarmTypeGate.Subject"/> = node id).</summary>
        Research,
        /// <summary>At least one harvest sold (the end of the first sea cycle).</summary>
        FirstHarvest,
        /// <summary>An operating post-smolt module.</summary>
        OperatingPostSmoltModule,
        /// <summary>A zoned land pad free to build on.</summary>
        ZonedPad,
        /// <summary>Days without an escape (<see cref="FarmTypeGate.Required"/> days).</summary>
        EscapeFreeDays,
        /// <summary>A development licence awarded (F2-09's round).</summary>
        DevelopmentLicence,
        /// <summary>One of the company's own units of a variant has completed a cycle (<see cref="FarmTypeGate.Subject"/> = variant id).</summary>
        OwnUnitCompletedCycle,
        /// <summary>Cash for the entry rung (<see cref="FarmTypeGate.Required"/> NOK).</summary>
        Funds,
    }

    /// <summary>One unlock gate of a variant.</summary>
    public sealed record FarmTypeGate(FarmTypeGateKind Kind, string Subject = "", double Required = 0.0);

    /// <summary>
    /// One axis value of a variant's card: the median and the bad case (null
    /// where the axis has no bad case or no published figure, with the reason
    /// in <see cref="NoteKey"/>), its unit, its source keys (the registry's
    /// [Source] keys, "GB" for game balance, "code" for the game's own values)
    /// and whether it is computed from several inputs.
    /// </summary>
    public sealed record FarmTypeAxisValue(string Axis, double? Median, double? Bad, string Unit,
        IReadOnlyList<string> Sources, bool Derived, string NoteKey = "");

    /// <summary>One farm-type variant: its family, name key, stage, the axes of its card and its unlock gates.</summary>
    public sealed record FarmTypeVariant(string Id, string FamilyId, string NameKey, int Stage, string CapacityBasis,
        IReadOnlyList<FarmTypeAxisValue> Axes, IReadOnlyList<FarmTypeGate> Gates)
    {
        /// <summary>The value on <paramref name="axis"/>.</summary>
        public FarmTypeAxisValue Axis(string axis) => Axes.Single(a => a.Axis == axis);
    }

    /// <summary>One farm-type family and its variants.</summary>
    public sealed record FarmTypeFamily(string Id, string NameKey, IReadOnlyList<FarmTypeVariant> Variants);

    /// <summary>
    /// The farm-type catalogue (roadmap F2-04, docs/FARM_TYPES_SPEC.md §1, §2, §6.2):
    /// three player-facing families (Sjø, Land, Hav) and seven variants, each
    /// with the same axes, computed from the registry's reference inputs
    /// (<see cref="FarmTypeCatalogCalibration"/>) and carrying their source
    /// keys, and with every unlock gate (the research gates come from the
    /// research tree's <c>unlocks</c>). Pure: no state, no RNG. The engine adds
    /// each gate's status (<c>SimulationEngine.GetFarmTypeCatalog</c>); the UI
    /// shows these numbers and holds none of its own. After F2-17 the probe's
    /// measured medians replace the reference values.
    /// </summary>
    public static class FarmTypeCatalog
    {
        /// <summary>Source key of a value the game already holds outside this section.</summary>
        public const string CodeKey = "code";

        /// <summary>Note of an axis with no published figure.</summary>
        public const string NoFigureNote = "no_published_figure";

        /// <summary>Capacity basis of a grow-out variant: tonnes HOG a year.</summary>
        public const string HogBasis = "hog";

        /// <summary>Capacity basis of the post-smolt module: tonnes of live post-smolt a year.</summary>
        public const string PostSmoltBasis = "live_post_smolt";

        /// <summary>The catalogue on <paramref name="registry"/>'s reference values and <paramref name="research"/>'s unlocks.</summary>
        public static IReadOnlyList<FarmTypeFamily> Build(CalibrationRegistry registry, ResearchSystem research)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(research);
            var v = new Variants(registry, research);
            return new[]
            {
                new FarmTypeFamily(FarmTypeIds.Sjo, "FARM_FAMILY_SJO", new[] { v.OpenPen(), v.ClosedPen() }),
                new FarmTypeFamily(FarmTypeIds.Land, "FARM_FAMILY_LAND", new[] { v.PostSmolt(), v.Hfs(), v.Ras() }),
                new FarmTypeFamily(FarmTypeIds.Hav, "FARM_FAMILY_HAV", new[] { v.Of1(), v.Havfarm() }),
            };
        }

        /// <summary>The name translation key of a variant id ("FARM_VARIANT_OPEN_PEN").</summary>
        public static string NameKeyOf(string variantId) => "FARM_VARIANT_" + variantId.ToUpperInvariant();

        /// <summary>The source keys of a calibration property: its [Source] keys, "GB" for [GameBalance], "code" otherwise.</summary>
        public static IReadOnlyList<string> SourcesOf(Type record, string property)
        {
            var p = record.GetProperty(property) ?? throw new ArgumentException($"{record.Name} has no {property}", nameof(property));
            var source = p.GetCustomAttribute<SourceAttribute>();
            if (source != null) return source.Keys;
            return p.GetCustomAttribute<GameBalanceAttribute>() != null ? new[] { GameBalanceAttribute.Key } : new[] { CodeKey };
        }

        /// <summary>The variants, built on one registry and one research tree.</summary>
        private sealed class Variants
        {
            private readonly FarmTypeCatalogCalibration _k;
            private readonly FacilityConstructionCalibration _build;
            private readonly CalibrationRegistry _registry;
            private readonly ResearchSystem _research;

            public Variants(CalibrationRegistry registry, ResearchSystem research)
            {
                _registry = registry;
                _research = research;
                _k = registry.Facilities.Common.Catalog;
                _build = registry.Facilities.Common.Construction;
            }

            private static IReadOnlyList<string> Keys(params IEnumerable<string>[] sets)
                => sets.SelectMany(s => s).Distinct(StringComparer.Ordinal).ToList();

            private static IReadOnlyList<string> Of<T>(params string[] properties)
                => Keys(properties.Select(p => SourcesOf(typeof(T), p)).ToArray());

            private IReadOnlyList<string> Hog => SourcesOf(typeof(FarmTypeCatalogCalibration), nameof(FarmTypeCatalogCalibration.HogTonnesPerMtbTonneYear));
            private IReadOnlyList<string> BuildRisk => Of<FacilityConstructionCalibration>(nameof(FacilityConstructionCalibration.OverrunP90), nameof(FacilityConstructionCalibration.DurationSlipP90));

            private double OverrunBad => 1.0 + _build.OverrunP90;
            private double SlipBad => 1.0 + _build.DurationSlipP90;

            /// <summary>The open pen's cost per kg HOG: round-weight cost over the registry's live-to-gutted factor.</summary>
            private double OpenOpexPerKgHog => _k.OpenPen.CostNokPerKgRound / _registry.Capacity.LiveToGuttedFactor;

            private static double CycleSurvival(double annualMortality, double months) => Math.Pow(1.0 - annualMortality, months / 12.0);

            /// <summary>The research gates of a variant: every node whose unlocks name it.</summary>
            private IEnumerable<FarmTypeGate> ResearchGates(string variant)
                => _research.NodesUnlocking(variant).Select(n => new FarmTypeGate(FarmTypeGateKind.Research, n.Id, 1));

            private static FarmTypeAxisValue Capex(double median, double? bad, IReadOnlyList<string> sources)
                => new(FarmTypeIds.CapexPerTonneYear, median, bad, "NOK/t/yr", sources, true);

            private static FarmTypeAxisValue Days(double median, double bad, IReadOnlyList<string> sources, bool derived)
                => new(FarmTypeIds.BuildDays, median, bad, "days", sources, derived);

            public FarmTypeVariant OpenPen()
            {
                var o = _k.OpenPen;
                double capacity = o.LocalityMtbTonnes * _k.HogTonnesPerMtbTonneYear;
                double capex = ((double)o.SiteCapexNok + o.LocalityMtbTonnes * (double)o.LicencePriceNokPerTonneMtb) / capacity;
                var capexKeys = Keys(Of<OpenPenReference>(nameof(o.SiteCapexNok), nameof(o.LocalityMtbTonnes), nameof(o.LicencePriceNokPerTonneMtb)), Hog);
                var survivalKeys = Of<OpenPenReference>(nameof(o.AnnualMortality), nameof(o.AnnualMortalityWorstArea), nameof(o.CycleMonths));
                return new FarmTypeVariant(FarmTypeIds.OpenPen, FarmTypeIds.Sjo, NameKeyOf(FarmTypeIds.OpenPen), 1, HogBasis, new[]
                {
                    Capex(capex, null, capexKeys),
                    new FarmTypeAxisValue(FarmTypeIds.OpexPerKg, OpenOpexPerKgHog, null, "NOK/kg",
                        Keys(Of<OpenPenReference>(nameof(o.CostNokPerKgRound)), new[] { CodeKey }), true),
                    new FarmTypeAxisValue(FarmTypeIds.Survival, CycleSurvival(o.AnnualMortality, o.CycleMonths),
                        CycleSurvival(o.AnnualMortalityWorstArea, o.CycleMonths), "share", survivalKeys, true),
                    new FarmTypeAxisValue(FarmTypeIds.ExternalLice, o.ExternalLiceFactor, null, "factor", Of<OpenPenReference>(nameof(o.ExternalLiceFactor)), false),
                    new FarmTypeAxisValue(FarmTypeIds.Energy, o.EnergyKWhPerKg, null, "kWh/kg", Of<OpenPenReference>(nameof(o.EnergyKWhPerKg)), false),
                    Days(o.BuildDays, o.BuildDays, Of<OpenPenReference>(nameof(o.BuildDays)), false),
                    new FarmTypeAxisValue(FarmTypeIds.Capacity, capacity, null, "t/yr", Keys(Of<OpenPenReference>(nameof(o.LocalityMtbTonnes)), Hog), true),
                }, new[]
                {
                    new FarmTypeGate(FarmTypeGateKind.Start, "", 1),
                    new FarmTypeGate(FarmTypeGateKind.Funds, "", (double)o.SiteCapexNok),
                });
            }

            public FarmTypeVariant ClosedPen()
            {
                var c = _k.ClosedPen;
                var o = _k.OpenPen;
                double penMtb = PenGeometry.Standard(_registry.Pens).VolumeM3 * _registry.Capacity.MaxStockingDensityKgPerM3 / 1000.0;
                double capacity = penMtb * _k.HogTonnesPerMtbTonneYear;
                double licence = penMtb * (double)o.LicencePriceNokPerTonneMtb;
                double capex = ((double)c.UpgradeCapexNokPerPen + licence) / capacity;
                double capexBad = ((double)c.UpgradeCapexNokPerPen * OverrunBad + licence) / capacity;
                var capexKeys = Keys(Of<ClosedPenReference>(nameof(c.UpgradeCapexNokPerPen)), Of<OpenPenReference>(nameof(o.LicencePriceNokPerTonneMtb)),
                    new[] { CodeKey }, Hog, BuildRisk);
                double opex = OpenOpexPerKgHog * c.ProductionCostNokPerKg / c.ControlPenCostNokPerKg;
                return new FarmTypeVariant(FarmTypeIds.ClosedPen, FarmTypeIds.Sjo, NameKeyOf(FarmTypeIds.ClosedPen), 1, HogBasis, new[]
                {
                    Capex(capex, capexBad, capexKeys),
                    new FarmTypeAxisValue(FarmTypeIds.OpexPerKg, opex, null, "NOK/kg",
                        Keys(Of<OpenPenReference>(nameof(o.CostNokPerKgRound)), Of<ClosedPenReference>(nameof(c.ProductionCostNokPerKg), nameof(c.ControlPenCostNokPerKg)), new[] { CodeKey }), true),
                    new FarmTypeAxisValue(FarmTypeIds.Survival, 1.0 - c.CycleMortality, c.BadCaseSurvival, "share",
                        Of<ClosedPenReference>(nameof(c.CycleMortality), nameof(c.BadCaseSurvival)), true),
                    new FarmTypeAxisValue(FarmTypeIds.ExternalLice, c.ExternalLiceFactor, null, "factor", Of<ClosedPenReference>(nameof(c.ExternalLiceFactor)), false),
                    new FarmTypeAxisValue(FarmTypeIds.Energy, c.EnergyKWhPerKg, null, "kWh/kg", Of<ClosedPenReference>(nameof(c.EnergyKWhPerKg)), false),
                    Days(c.RefitDays, c.RefitDays * SlipBad, Keys(Of<ClosedPenReference>(nameof(c.RefitDays)), BuildRisk), false),
                    new FarmTypeAxisValue(FarmTypeIds.Capacity, capacity, null, "t/yr", Keys(new[] { CodeKey }, Hog), true),
                }, new[]
                {
                    new FarmTypeGate(FarmTypeGateKind.FirstHarvest, "", 1),
                    new FarmTypeGate(FarmTypeGateKind.Funds, "", (double)c.UpgradeCapexNokPerPen),
                });
            }

            public FarmTypeVariant PostSmolt()
            {
                var p = _k.PostSmolt;
                double capacity = p.ModuleVolumeM3 * p.ProductionIndexKgPerM3Year / 1000.0;
                double capex = (double)p.ModuleCapexNok / capacity;
                int days = p.ConstructionDays + p.BiofilterMaturationDays;
                return new FarmTypeVariant(FarmTypeIds.PostSmolt, FarmTypeIds.Land, NameKeyOf(FarmTypeIds.PostSmolt), 1, PostSmoltBasis, new[]
                {
                    Capex(capex, capex * OverrunBad, Keys(Of<PostSmoltReference>(nameof(p.ModuleCapexNok), nameof(p.ModuleVolumeM3), nameof(p.ProductionIndexKgPerM3Year)), BuildRisk)),
                    new FarmTypeAxisValue(FarmTypeIds.OpexPerKg, null, null, "NOK/kg", new[] { GameBalanceAttribute.Key }, false, NoFigureNote),
                    new FarmTypeAxisValue(FarmTypeIds.Survival, p.Survival, p.BadCaseSurvival, "share", Of<PostSmoltReference>(nameof(p.Survival), nameof(p.BadCaseSurvival)), false),
                    new FarmTypeAxisValue(FarmTypeIds.ExternalLice, 0.0, null, "factor", Of<HfsReference>(nameof(HfsReference.ExternalLiceFactor)), false),
                    new FarmTypeAxisValue(FarmTypeIds.Energy, p.EnergyKWhPerKg, null, "kWh/kg", Of<PostSmoltReference>(nameof(p.EnergyKWhPerKg)), false),
                    Days(days, days * SlipBad, Keys(Of<PostSmoltReference>(nameof(p.ConstructionDays), nameof(p.BiofilterMaturationDays)), BuildRisk), true),
                    new FarmTypeAxisValue(FarmTypeIds.Capacity, capacity, null, "t/yr", Of<PostSmoltReference>(nameof(p.ModuleVolumeM3), nameof(p.ProductionIndexKgPerM3Year)), true),
                }, ResearchGates(FarmTypeIds.PostSmolt).Concat(new[]
                {
                    new FarmTypeGate(FarmTypeGateKind.FirstHarvest, "", 1),
                    new FarmTypeGate(FarmTypeGateKind.Funds, "", (double)p.ModuleCapexNok),
                }).ToList());
            }

            public FarmTypeVariant Hfs()
            {
                var h = _k.Hfs;
                double capex = (double)h.CapexNokPerTonneHog;
                return new FarmTypeVariant(FarmTypeIds.Hfs, FarmTypeIds.Land, NameKeyOf(FarmTypeIds.Hfs), 1, HogBasis, new[]
                {
                    new FarmTypeAxisValue(FarmTypeIds.CapexPerTonneYear, capex, capex * OverrunBad, "NOK/t/yr",
                        Keys(Of<HfsReference>(nameof(h.CapexNokPerTonneHog)), BuildRisk), false),
                    new FarmTypeAxisValue(FarmTypeIds.OpexPerKg, h.CostNokPerKgHog, null, "NOK/kg", Of<HfsReference>(nameof(h.CostNokPerKgHog)), false),
                    new FarmTypeAxisValue(FarmTypeIds.Survival, h.Survival, h.BadCaseSurvival, "share", Of<HfsReference>(nameof(h.Survival), nameof(h.BadCaseSurvival)), false),
                    new FarmTypeAxisValue(FarmTypeIds.ExternalLice, h.ExternalLiceFactor, null, "factor", Of<HfsReference>(nameof(h.ExternalLiceFactor)), false),
                    new FarmTypeAxisValue(FarmTypeIds.Energy, h.EnergyKWhPerKg, null, "kWh/kg", Of<HfsReference>(nameof(h.EnergyKWhPerKg)), false),
                    Days(h.BuildDays, h.BuildDays * SlipBad, Keys(Of<HfsReference>(nameof(h.BuildDays)), BuildRisk), false),
                    new FarmTypeAxisValue(FarmTypeIds.Capacity, h.LotCapacityTonnesHog, null, "t/yr", Of<HfsReference>(nameof(h.LotCapacityTonnesHog)), false),
                }, ResearchGates(FarmTypeIds.Hfs).Concat(new[]
                {
                    new FarmTypeGate(FarmTypeGateKind.OperatingPostSmoltModule, FarmTypeIds.PostSmolt, 1),
                    new FarmTypeGate(FarmTypeGateKind.ZonedPad, "", 1),
                    new FarmTypeGate(FarmTypeGateKind.Funds, "", (double)h.PilotLotCapexNok),
                }).ToList());
            }

            public FarmTypeVariant Ras()
            {
                var r = _k.Ras;
                double capex = (double)(r.CapexUsdPerTonne * r.NokPerUsd);
                double capacity = r.HallTanks * r.TankVolumeM3 * r.ProductionIndexKgPerM3Year / 1000.0 * _registry.Capacity.LiveToGuttedFactor;
                int days = r.ConstructionDays + r.BiofilterMaturationDays;
                return new FarmTypeVariant(FarmTypeIds.Ras, FarmTypeIds.Land, NameKeyOf(FarmTypeIds.Ras), 3, HogBasis, new[]
                {
                    Capex(capex, capex * OverrunBad, Keys(Of<RasReference>(nameof(r.CapexUsdPerTonne), nameof(r.NokPerUsd)), BuildRisk)),
                    new FarmTypeAxisValue(FarmTypeIds.OpexPerKg, _k.Hfs.CostNokPerKgHog * (1.0 + r.CostPremiumOverHfs), null, "NOK/kg",
                        Keys(Of<HfsReference>(nameof(HfsReference.CostNokPerKgHog)), Of<RasReference>(nameof(r.CostPremiumOverHfs))), true),
                    new FarmTypeAxisValue(FarmTypeIds.Survival, r.Survival, r.BadCaseSurvival, "share", Of<RasReference>(nameof(r.Survival), nameof(r.BadCaseSurvival)), false),
                    new FarmTypeAxisValue(FarmTypeIds.ExternalLice, r.ExternalLiceFactor, null, "factor", Of<RasReference>(nameof(r.ExternalLiceFactor)), false),
                    new FarmTypeAxisValue(FarmTypeIds.Energy, r.EnergyKWhPerKg, null, "kWh/kg", Of<RasReference>(nameof(r.EnergyKWhPerKg)), false),
                    Days(days, days * SlipBad, Keys(Of<RasReference>(nameof(r.ConstructionDays), nameof(r.BiofilterMaturationDays)), BuildRisk), true),
                    new FarmTypeAxisValue(FarmTypeIds.Capacity, capacity, null, "t/yr",
                        Keys(Of<RasReference>(nameof(r.HallTanks), nameof(r.TankVolumeM3), nameof(r.ProductionIndexKgPerM3Year)), new[] { CodeKey }), true),
                }, ResearchGates(FarmTypeIds.Ras).Concat(new[]
                {
                    new FarmTypeGate(FarmTypeGateKind.OwnUnitCompletedCycle, FarmTypeIds.Hfs, 1),
                    new FarmTypeGate(FarmTypeGateKind.Funds, "", capex * capacity),
                }).ToList());
            }

            private double OffshoreLicencePerTonne => (double)_k.ConversionFeeNokPerLicence / _k.LicenceSizeTonnesMtb;

            private IReadOnlyList<string> ConversionKeys => Of<FarmTypeCatalogCalibration>(
                nameof(FarmTypeCatalogCalibration.ConversionFeeNokPerLicence), nameof(FarmTypeCatalogCalibration.LicenceSizeTonnesMtb));

            public FarmTypeVariant Of1()
            {
                var u = _k.Of1;
                double mtb = u.Licences * _k.LicenceSizeTonnesMtb;
                double capacity = mtb * _k.HogTonnesPerMtbTonneYear;
                double licence = u.Licences * (double)_k.ConversionFeeNokPerLicence;
                double capex = ((double)u.UnitCapexNok + licence) / capacity;
                double capexBad = ((double)u.UnitCapexNok * OverrunBad + licence) / capacity;
                int days = u.YardDays + u.VoyageDays + u.MooringAndCertificationDays;
                return new FarmTypeVariant(FarmTypeIds.Of1, FarmTypeIds.Hav, NameKeyOf(FarmTypeIds.Of1), 1, HogBasis, new[]
                {
                    Capex(capex, capexBad, Keys(Of<Of1Reference>(nameof(u.UnitCapexNok), nameof(u.Licences)), ConversionKeys, Hog, BuildRisk)),
                    new FarmTypeAxisValue(FarmTypeIds.OpexPerKg, OpenOpexPerKgHog * u.CostRatioToCoastal, null, "NOK/kg",
                        Keys(Of<OpenPenReference>(nameof(OpenPenReference.CostNokPerKgRound)), Of<Of1Reference>(nameof(u.CostRatioToCoastal)), new[] { CodeKey }), true),
                    new FarmTypeAxisValue(FarmTypeIds.Survival, u.Survival, u.BadCaseSurvival, "share", Of<Of1Reference>(nameof(u.Survival), nameof(u.BadCaseSurvival)), false),
                    new FarmTypeAxisValue(FarmTypeIds.ExternalLice, u.ExternalLiceFactor, null, "factor", Of<Of1Reference>(nameof(u.ExternalLiceFactor)), false),
                    new FarmTypeAxisValue(FarmTypeIds.Energy, null, null, "kWh/kg", new[] { GameBalanceAttribute.Key }, false, NoFigureNote),
                    Days(days, days * SlipBad, Keys(Of<Of1Reference>(nameof(u.YardDays), nameof(u.VoyageDays), nameof(u.MooringAndCertificationDays)), BuildRisk), true),
                    new FarmTypeAxisValue(FarmTypeIds.Capacity, capacity, null, "t/yr", Keys(Of<Of1Reference>(nameof(u.Licences)), ConversionKeys, Hog), true),
                }, ResearchGates(FarmTypeIds.Of1).Concat(new[]
                {
                    new FarmTypeGate(FarmTypeGateKind.FirstHarvest, "", 1),
                    new FarmTypeGate(FarmTypeGateKind.EscapeFreeDays, "", u.RoundEscapeFreeDays),
                    new FarmTypeGate(FarmTypeGateKind.DevelopmentLicence, FarmTypeIds.Of1, 1),
                    new FarmTypeGate(FarmTypeGateKind.Funds, "", (double)u.UnitCapexNok * u.ConsortiumEquityShare),
                }).ToList());
            }

            public FarmTypeVariant Havfarm()
            {
                var h = _k.Havfarm;
                double capacity = h.MtbTonnes * _k.HogTonnesPerMtbTonneYear;
                double licence = h.Licences * (double)_k.ConversionFeeNokPerLicence;
                double capex = ((double)h.UnitCapexNok + licence) / capacity;
                double capexBad = ((double)h.UnitCapexNok * OverrunBad + licence) / capacity;
                return new FarmTypeVariant(FarmTypeIds.Havfarm, FarmTypeIds.Hav, NameKeyOf(FarmTypeIds.Havfarm), 3, HogBasis, new[]
                {
                    Capex(capex, capexBad, Keys(Of<HavfarmReference>(nameof(h.UnitCapexNok), nameof(h.Licences), nameof(h.MtbTonnes)),
                        Of<FarmTypeCatalogCalibration>(nameof(FarmTypeCatalogCalibration.ConversionFeeNokPerLicence)), Hog, BuildRisk)),
                    new FarmTypeAxisValue(FarmTypeIds.OpexPerKg, OpenOpexPerKgHog * h.CostRatioToCoastal, null, "NOK/kg",
                        Keys(Of<OpenPenReference>(nameof(OpenPenReference.CostNokPerKgRound)), Of<HavfarmReference>(nameof(h.CostRatioToCoastal)), new[] { CodeKey }), true),
                    new FarmTypeAxisValue(FarmTypeIds.Survival, h.Survival, h.BadCaseSurvival, "share", Of<HavfarmReference>(nameof(h.Survival), nameof(h.BadCaseSurvival)), false),
                    new FarmTypeAxisValue(FarmTypeIds.ExternalLice, h.ExternalLiceFactor, null, "factor", Of<HavfarmReference>(nameof(h.ExternalLiceFactor)), false),
                    new FarmTypeAxisValue(FarmTypeIds.Energy, null, null, "kWh/kg", new[] { GameBalanceAttribute.Key }, false, NoFigureNote),
                    Days(h.BuildDays, h.BuildDays * SlipBad, Keys(Of<HavfarmReference>(nameof(h.BuildDays)), BuildRisk), false),
                    new FarmTypeAxisValue(FarmTypeIds.Capacity, capacity, null, "t/yr", Keys(Of<HavfarmReference>(nameof(h.MtbTonnes)), Hog), true),
                }, ResearchGates(FarmTypeIds.Havfarm).Concat(new[]
                {
                    new FarmTypeGate(FarmTypeGateKind.OwnUnitCompletedCycle, FarmTypeIds.Of1, 1),
                    new FarmTypeGate(FarmTypeGateKind.Funds, "", (double)h.UnitCapexNok),
                }).ToList());
            }
        }
    }
}
