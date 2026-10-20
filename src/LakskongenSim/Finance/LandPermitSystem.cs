using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// One driver of a permit case's refusal probability (spec §6.2: "each
    /// case shows its refusal probability, drivers and mitigations"): its key
    /// and what it adds to the probability (negative for a mitigation). The
    /// drivers of a quote add up to its probability.
    /// </summary>
    /// <param name="Key">"base", "nature_sensitivity", "scale", "environmental_survey", "denitrification", "appeal".</param>
    /// <param name="Effect">What the driver adds to the refusal probability.</param>
    public sealed record PermitDriver(string Key, double Effect);

    /// <summary>
    /// What one land permit case would be (roadmap F2-14, spec §6.2's permit
    /// timeline): its decision window, refusal probability with its drivers and
    /// fee. The terms the engine files are <see cref="Terms"/>, so the
    /// probability shown is the probability decided.
    /// </summary>
    /// <param name="Kind">The permit.</param>
    /// <param name="MinDays">Earliest decision, days after submission.</param>
    /// <param name="MaxDays">Latest decision, days after submission.</param>
    /// <param name="RefusalProbability">Probability of refusal (0-1).</param>
    /// <param name="Drivers">What the probability is made of (they add up to it).</param>
    /// <param name="FeeNok">Fee paid at submission (NOK).</param>
    public sealed record LandPermitQuote(PermitKind Kind, int MinDays, int MaxDays, double RefusalProbability,
        IReadOnlyList<PermitDriver> Drivers, decimal FeeNok)
    {
        /// <summary>The terms the engine files the case on.</summary>
        public PermitTerms Terms => new(MinDays, MaxDays, RefusalProbability, FeeNok);
    }

    /// <summary>
    /// The land permits (roadmap F2-14, docs/FARM_TYPES_SPEC.md §2C, §6.2): a
    /// land site needs zoning (unless its pad is zoned), the fee-free land
    /// aquaculture permit, a discharge permit, a sea-works permit for its
    /// intake and outfall, and Mattilsynet's biosecurity approval ([L:L1]-[L:L5],
    /// [L:R12], [L:R13]); a grid project may add capacity or a second feed.
    /// Pure: what a case's terms are, from the calibration, the plot and the
    /// application. The engine files them (<c>SimulationEngine.FacilityPermits.cs</c>).
    /// </summary>
    public static class LandPermitSystem
    {
        /// <summary>Mitigation: an environmental survey filed with the application (cuts the zoning, discharge and sea-works refusal).</summary>
        public const string EnvironmentalSurvey = "environmental_survey";

        /// <summary>Mitigation: a denitrification commitment (cuts the discharge refusal; adds to the build's budget).</summary>
        public const string Denitrification = "denitrification";

        /// <summary>Every mitigation an application may carry.</summary>
        public static readonly IReadOnlyList<string> Mitigations = new[] { EnvironmentalSurvey, Denitrification };

        /// <summary>The permits of a land site, in the order the timeline shows them.</summary>
        public static readonly IReadOnlyList<PermitKind> SiteKinds = new[]
        {
            PermitKind.Zoning, PermitKind.LandAquaculture, PermitKind.Discharge, PermitKind.SeaWorks, PermitKind.Biosecurity,
        };

        /// <summary>Condition key: the yearly production (t HOG) a permit covers.</summary>
        public const string MaxProductionKey = "maxProductionT";

        /// <summary>Condition key: the standing biomass (t) a permit covers.</summary>
        public const string MaxStandingKey = "maxStandingT";

        /// <summary>Condition key: the density limit (kg/m³) of a permit.</summary>
        public const string MaxDensityKey = "maxDensityKgM3";

        /// <summary>Condition key: grid capacity (MW) a grid project adds.</summary>
        public const string AddGridMwKey = "addMW";

        /// <summary>Condition key: grid feeds a grid project adds.</summary>
        public const string AddGridFeedsKey = "addFeeds";

        /// <summary>Grid project: more capacity.</summary>
        public const string GridCapacity = "capacity";

        /// <summary>Grid project: a second, independent feed.</summary>
        public const string GridSecondFeed = "second_feed";

        /// <summary>True for the land permit kinds the moratorium flag holds back (the aquaculture permit, spec §2C).</summary>
        public static bool HeldByMoratorium(PermitKind kind) => kind == PermitKind.LandAquaculture;

        /// <summary>The first unknown mitigation key in <paramref name="mitigations"/>, or null.</summary>
        public static string? UnknownMitigation(IEnumerable<string>? mitigations)
            => mitigations?.FirstOrDefault(m => !Mitigations.Contains(m));

        /// <summary>
        /// The scale factor on a case's refusal: (production / a full lot's
        /// production)^exponent (spec §6.2 "smaller first phase"); 1 when either
        /// is unknown.
        /// </summary>
        public static double ScaleFactor(double productionTonnes, double fullLotTonnes, LandPermitCalibration permits)
        {
            if (permits == null) throw new ArgumentNullException(nameof(permits));
            if (!(productionTonnes > 0.0) || !(fullLotTonnes > 0.0)) return 1.0;
            return Math.Pow(productionTonnes / fullLotTonnes, permits.ScaleExponent);
        }

        /// <summary>True when the plant's scale drives the kind's refusal (zoning, the land permit, discharge).</summary>
        public static bool ScalesWithProduction(PermitKind kind)
            => kind is PermitKind.Zoning or PermitKind.LandAquaculture or PermitKind.Discharge;

        /// <summary>
        /// The refusal probability of a <paramref name="kind"/> case on a plot of
        /// <paramref name="natureSensitivity"/> for a plant of
        /// <paramref name="scaleFactor"/> with <paramref name="mitigations"/>,
        /// and its drivers (which add up to it): (base + per-sensitivity ×
        /// sensitivity) × scale × (1 − survey cut) × (1 − denitrification cut),
        /// each factor where it applies.
        /// </summary>
        public static (double Probability, IReadOnlyList<PermitDriver> Drivers) RefusalProbability(PermitKind kind,
            double natureSensitivity, double scaleFactor, IReadOnlyCollection<string>? mitigations, LandPermitCalibration permits)
        {
            if (permits == null) throw new ArgumentNullException(nameof(permits));
            var (baseP, perSensitivity) = kind switch
            {
                PermitKind.Zoning => (permits.ZoningRefusalBase, permits.ZoningRefusalPerSensitivity),
                PermitKind.LandAquaculture => (permits.LandAquacultureRefusalBase, permits.LandAquacultureRefusalPerSensitivity),
                PermitKind.Discharge => (permits.DischargeRefusalBase, permits.DischargeRefusalPerSensitivity),
                PermitKind.SeaWorks => (permits.SeaWorksRefusalBase, permits.SeaWorksRefusalPerSensitivity),
                PermitKind.Biosecurity => (permits.BiosecurityRefusalBase, permits.BiosecurityRefusalPerSensitivity),
                _ => (0.0, 0.0),
            };
            double s = Math.Clamp(double.IsFinite(natureSensitivity) ? natureSensitivity : 0.0, 0.0, 1.0);
            var drivers = new List<PermitDriver> { new("base", baseP), new("nature_sensitivity", perSensitivity * s) };
            double p = baseP + perSensitivity * s;
            if (ScalesWithProduction(kind) && double.IsFinite(scaleFactor) && scaleFactor > 0.0 && scaleFactor != 1.0)
            {
                double scaled = p * scaleFactor;
                drivers.Add(new PermitDriver("scale", scaled - p));
                p = scaled;
            }
            var offered = mitigations ?? Array.Empty<string>();
            if (offered.Contains(EnvironmentalSurvey) && kind is PermitKind.Zoning or PermitKind.Discharge or PermitKind.SeaWorks)
            {
                double cut = p * Math.Clamp(permits.SurveyRefusalCut, 0.0, 1.0);
                drivers.Add(new PermitDriver(EnvironmentalSurvey, -cut));
                p -= cut;
            }
            if (offered.Contains(Denitrification) && kind == PermitKind.Discharge)
            {
                double cut = p * Math.Clamp(permits.DenitrificationRefusalCut, 0.0, 1.0);
                drivers.Add(new PermitDriver(Denitrification, -cut));
                p -= cut;
            }
            return (Math.Clamp(p, 0.0, 1.0), drivers);
        }

        /// <summary>The decision window of a <paramref name="kind"/> case (days after submission).</summary>
        public static (int Min, int Max) DecisionWindow(PermitKind kind, LandPermitCalibration permits)
        {
            if (permits == null) throw new ArgumentNullException(nameof(permits));
            if (kind == PermitKind.Zoning) return (permits.ZoningDaysMin, Math.Max(permits.ZoningDaysMin, permits.ZoningDaysMax));
            if (kind == PermitKind.GridConnection) return (permits.GridQueueDaysMin, Math.Max(permits.GridQueueDaysMin, permits.GridQueueDaysMax));
            int days = kind switch
            {
                PermitKind.LandAquaculture => permits.LandAquacultureDays,
                PermitKind.Discharge => permits.DischargeDays,
                PermitKind.SeaWorks => permits.SeaWorksDays,
                PermitKind.Biosecurity => permits.BiosecurityDays,
                _ => permits.LandAquacultureDays,
            };
            double spread = Math.Clamp(permits.DecisionSpread, 0.0, 0.9);
            int min = Math.Max(1, (int)Math.Round(days * (1.0 - spread), MidpointRounding.AwayFromZero));
            int max = Math.Max(min, (int)Math.Round(days * (1.0 + spread), MidpointRounding.AwayFromZero));
            return (min, max);
        }

        /// <summary>The fee of a <paramref name="kind"/> case (NOK): the sea-works permit's [L:R12]; the land permit is fee-free [L:L1].</summary>
        public static decimal Fee(PermitKind kind, LandPermitCalibration permits)
        {
            if (permits == null) throw new ArgumentNullException(nameof(permits));
            return kind == PermitKind.SeaWorks ? permits.SeaWorksFeeNok : 0m;
        }

        /// <summary>
        /// The quote of a <paramref name="kind"/> case: window, refusal and its
        /// drivers, fee (plus <paramref name="extraFeeNok"/>, the environmental
        /// survey filed with the package's first case).
        /// </summary>
        public static LandPermitQuote Quote(PermitKind kind, double natureSensitivity, double scaleFactor,
            IReadOnlyCollection<string>? mitigations, LandPermitCalibration permits, decimal extraFeeNok = 0m)
        {
            var (min, max) = DecisionWindow(kind, permits);
            var (p, drivers) = RefusalProbability(kind, natureSensitivity, scaleFactor, mitigations, permits);
            return new LandPermitQuote(kind, min, max, p, drivers, Fee(kind, permits) + Math.Max(0m, extraFeeNok));
        }

        /// <summary>
        /// The quote of an appeal of a case refused at <paramref name="refusedProbability"/>:
        /// decided in <see cref="LandPermitCalibration.AppealDays"/> (180 d) at
        /// the refused probability × the appeal factor.
        /// </summary>
        public static LandPermitQuote AppealQuote(PermitKind kind, double refusedProbability, LandPermitCalibration permits)
        {
            if (permits == null) throw new ArgumentNullException(nameof(permits));
            double p = Math.Clamp(refusedProbability, 0.0, 1.0);
            double appealed = p * Math.Clamp(permits.AppealRefusalFactor, 0.0, 1.0);
            var drivers = new List<PermitDriver> { new("refused_case", p), new("appeal", appealed - p) };
            return new LandPermitQuote(kind, permits.AppealDays, permits.AppealDays, appealed, drivers, 0m);
        }

        /// <summary>
        /// The quote of a grid project: a queue of 365-1,095 days, never refused,
        /// with the connection fee per 10 MW of what it connects (the added
        /// capacity, or for a second feed the pad's whole capacity on a new line).
        /// </summary>
        public static LandPermitQuote GridQuote(double connectedMW, LandPermitCalibration permits)
        {
            var (min, max) = DecisionWindow(PermitKind.GridConnection, permits);
            decimal fee = permits.GridFeeNokPer10MW * (decimal)Math.Max(0.0, connectedMW) / 10m;
            return new LandPermitQuote(PermitKind.GridConnection, min, max, 0.0, new[] { new PermitDriver("base", 0.0) }, fee);
        }
    }
}
