using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Data;

namespace LakskongenSim.Biology
{
    /// <summary>Category of a genetically-engineered (GE) modification.</summary>
    public enum GeModification
    {
        /// <summary>Accelerated growth (e.g. growth-hormone transgene, AquAdvantage).</summary>
        Growth = 0,

        /// <summary>Engineered disease resistance (e.g. ISA/IPN resistance).</summary>
        DiseaseResistance = 1,

        /// <summary>Induced sterility used for reproductive containment.</summary>
        Sterility = 2,

        /// <summary>Engineered sea-lice resistance.</summary>
        LiceResistance = 3
    }

    /// <summary>Deployment regions with distinct GM/GE regulation.</summary>
    public enum GeRegion
    {
        /// <summary>Norway — Gene Technology Act; GE food animals effectively not approved.</summary>
        Norway = 0,

        /// <summary>European Union — strict GMO regime; GE salmon not approved.</summary>
        EuropeanUnion = 1,

        /// <summary>United States — FDA approved AquAdvantage salmon (2015).</summary>
        UnitedStates = 2,

        /// <summary>Canada — approved AquAdvantage for human consumption (2016).</summary>
        Canada = 3,

        /// <summary>Chile — major producer; GE salmon under review / not commercial.</summary>
        Chile = 4,

        /// <summary>Scotland — follows EU-style GM restrictions.</summary>
        Scotland = 5
    }

    /// <summary>Regulatory status of a GE trait in a given region.</summary>
    public enum RegulatoryStatus
    {
        /// <summary>No application filed.</summary>
        NotSubmitted = 0,

        /// <summary>Application filed, decision pending.</summary>
        UnderReview = 1,

        /// <summary>Approved for commercial deployment.</summary>
        Approved = 2,

        /// <summary>Prohibited in the region.</summary>
        Banned = 3
    }

    /// <summary>
    /// An immutable genetic-engineering trait: its biological effect, the regulatory
    /// pathway, the reputational cost of deployment, and the financial risk of a recall.
    /// Pure data + pure helper methods; no randomness, no I/O.
    ///
    /// Grounding:
    /// <list type="bullet">
    /// <item>AquAdvantage salmon is the only GE salmon approved for food — in the US
    /// (FDA, 2015) and Canada (2016) — and remains banned/not approved in Norway and the
    /// EU under their GMO regimes.</item>
    /// <item>Regulators require reproductive containment (sterility, triploidy) for GE
    /// fish; sterile lines are themselves a containment technology.</item>
    /// <item>Consumer/NGO opposition imposes a reputational cost, and any escape or
    /// regulatory reversal carries recall and penalty risk.</item>
    /// </list>
    /// </summary>
    public sealed record GeTrait(
        string Id,
        string Name,
        GeModification Modification,
        GeneticTrait TargetTrait,
        double EffectMagnitude,
        double ApprovalCost,
        double ReputationCost,
        double AnnualRecallRisk,
        double RecallPenalty,
        bool RequiresSterilityContainment,
        IReadOnlyDictionary<GeRegion, RegulatoryStatus> RegionStatus,
        string Evidence)
    {
        /// <summary>Regulatory status in a region (defaults to <see cref="RegulatoryStatus.NotSubmitted"/>).</summary>
        public RegulatoryStatus StatusIn(GeRegion region) =>
            RegionStatus.TryGetValue(region, out var status) ? status : RegulatoryStatus.NotSubmitted;

        /// <summary>True when the trait is approved for commercial deployment in a region.</summary>
        public bool IsApprovedIn(GeRegion region) => StatusIn(region) == RegulatoryStatus.Approved;

        /// <summary>Expected financial loss from recalls over a horizon of years.</summary>
        public double ExpectedRecallLoss(int horizonYears)
        {
            int years = Math.Max(0, horizonYears);
            return Math.Clamp(AnnualRecallRisk, 0.0, 1.0) * years * Math.Max(0.0, RecallPenalty);
        }

        /// <summary>Deterministic recall check: a uniform roll in [0,1) below the annual risk fires.</summary>
        public bool RecallOccurred(double uniformRoll) =>
            Math.Clamp(uniformRoll, 0.0, 1.0) < Math.Clamp(AnnualRecallRisk, 0.0, 1.0);
    }

    /// <summary>Outcome of assessing a GE deployment in a region.</summary>
    public sealed record GeDeploymentAssessment(
        bool Allowed,
        RegulatoryStatus Status,
        double ReputationCost,
        double ExpectedRecallLoss,
        bool SterilityContainmentSatisfied,
        string Rationale);

    /// <summary>
    /// Pure decision model for GE deployment: approval gating, reputational cost, and
    /// recall/penalty risk. No randomness, no I/O.
    /// </summary>
    public static class GeneticEngineering
    {
        /// <summary>Assess deploying a GE trait in a region.</summary>
        /// <param name="trait">The GE trait under consideration.</param>
        /// <param name="region">Deployment region.</param>
        /// <param name="currentReputation01">Current public reputation, 0..1.</param>
        /// <param name="hasSterilityContainment">Whether reproductive containment is in place.</param>
        /// <param name="horizonYears">Years of operation for recall-loss estimation.</param>
        public static GeDeploymentAssessment AssessDeployment(
            GeTrait trait,
            GeRegion region,
            double currentReputation01,
            bool hasSterilityContainment = true,
            int horizonYears = 1)
        {
            if (trait is null) throw new ArgumentNullException(nameof(trait));

            var status = trait.StatusIn(region);
            bool containmentOk = !trait.RequiresSterilityContainment || hasSterilityContainment;

            if (status != RegulatoryStatus.Approved)
            {
                string why = status switch
                {
                    RegulatoryStatus.Banned => $"Banned in {region}.",
                    RegulatoryStatus.UnderReview => $"Under regulatory review in {region}; not deployable yet.",
                    _ => $"Not approved in {region}; an application must be filed."
                };
                return new GeDeploymentAssessment(
                    Allowed: false, status, ReputationCost: 0.0, ExpectedRecallLoss: 0.0,
                    SterilityContainmentSatisfied: containmentOk, Rationale: why);
            }

            if (!containmentOk)
            {
                return new GeDeploymentAssessment(
                    Allowed: false, status, ReputationCost: 0.0, ExpectedRecallLoss: 0.0,
                    SterilityContainmentSatisfied: false,
                    Rationale: "Approved, but regulatory approval requires reproductive containment (sterility).");
            }

            double cost = Math.Clamp(trait.ReputationCost, 0.0, 1.0);
            double loss = trait.ExpectedRecallLoss(horizonYears);
            string rationale = $"Approved in {region}; deployment costs {cost:P0} reputation and "
                + $"carries expected recall loss of {loss:N0} over {Math.Max(0, horizonYears)} year(s).";

            return new GeDeploymentAssessment(
                Allowed: true, status, ReputationCost: cost, ExpectedRecallLoss: loss,
                SterilityContainmentSatisfied: true, Rationale: rationale);
        }

        /// <summary>Apply a reputational cost, returning reputation clamped to [0,1].</summary>
        public static double ApplyReputationImpact(double reputation01, double reputationCost)
        {
            double rep = Math.Clamp(reputation01, 0.0, 1.0);
            double cost = Math.Max(0.0, reputationCost);
            return Math.Clamp(rep - cost, 0.0, 1.0);
        }

        /// <summary>
        /// Effective trait effect available to the farm after inbreeding-independent
        /// approval gating: the raw effect when allowed, 0 when blocked.
        /// </summary>
        public static double EffectiveTraitEffect(GeTrait trait, double currentReputation01, GeRegion region)
        {
            var assessment = AssessDeployment(trait, region, currentReputation01);
            return assessment.Allowed ? trait.EffectMagnitude : 0.0;
        }
    }

    /// <summary>Static registry of GE traits (expandable; JSON later).</summary>
    public static class GeTraitRegistry
    {
        private static IReadOnlyDictionary<GeRegion, RegulatoryStatus> Statuses(
            params (GeRegion Region, RegulatoryStatus Status)[] entries)
            => entries.ToDictionary(e => e.Region, e => e.Status);

        /// <summary>AquAdvantage-style growth-hormone salmon (the only approved GE food animal).</summary>
        public static readonly GeTrait AquAdvantageGrowth = new(
            Id: "ge_growth_aquadvantage",
            Name: "AquAdvantage Growth",
            Modification: GeModification.Growth,
            TargetTrait: GeneticTrait.GrowthRate,
            EffectMagnitude: 2.0,
            ApprovalCost: 5_000_000,
            ReputationCost: 0.35,
            AnnualRecallRisk: 0.05,
            RecallPenalty: 12_000_000,
            RequiresSterilityContainment: true,
            RegionStatus: Statuses(
                (GeRegion.UnitedStates, RegulatoryStatus.Approved),
                (GeRegion.Canada, RegulatoryStatus.Approved),
                (GeRegion.Norway, RegulatoryStatus.Banned),
                (GeRegion.EuropeanUnion, RegulatoryStatus.Banned),
                (GeRegion.Scotland, RegulatoryStatus.Banned),
                (GeRegion.Chile, RegulatoryStatus.UnderReview)),
            Evidence: "FDA approved AquAdvantage salmon 2015; Health Canada 2016. "
                + "Banned/not approved in Norway (Gene Technology Act) and the EU.");

        /// <summary>Engineered ISA/IPN disease resistance (gene edit; still pre-commercial).</summary>
        public static readonly GeTrait EngineeredDiseaseResistance = new(
            Id: "ge_disease_resistance",
            Name: "Engineered Disease Resistance",
            Modification: GeModification.DiseaseResistance,
            TargetTrait: GeneticTrait.DiseaseResistance,
            EffectMagnitude: 1.5,
            ApprovalCost: 3_500_000,
            ReputationCost: 0.20,
            AnnualRecallRisk: 0.03,
            RecallPenalty: 8_000_000,
            RequiresSterilityContainment: true,
            RegionStatus: Statuses(
                (GeRegion.UnitedStates, RegulatoryStatus.UnderReview),
                (GeRegion.Canada, RegulatoryStatus.UnderReview),
                (GeRegion.Norway, RegulatoryStatus.UnderReview),
                (GeRegion.EuropeanUnion, RegulatoryStatus.UnderReview),
                (GeRegion.Scotland, RegulatoryStatus.UnderReview),
                (GeRegion.Chile, RegulatoryStatus.NotSubmitted)),
            Evidence: "Gene-edited disease-resistance candidates (e.g. ISA) remain in "
                + "research/regulatory review across all salmon-producing jurisdictions.");

        /// <summary>Induced sterility: a containment technology, widely permitted.</summary>
        public static readonly GeTrait InducedSterility = new(
            Id: "ge_sterility",
            Name: "Induced Sterility (Containment)",
            Modification: GeModification.Sterility,
            TargetTrait: GeneticTrait.LateMaturation,
            EffectMagnitude: 0.8,
            ApprovalCost: 1_500_000,
            ReputationCost: 0.05,
            AnnualRecallRisk: 0.01,
            RecallPenalty: 4_000_000,
            RequiresSterilityContainment: false,
            RegionStatus: Statuses(
                (GeRegion.Norway, RegulatoryStatus.Approved),
                (GeRegion.EuropeanUnion, RegulatoryStatus.Approved),
                (GeRegion.UnitedStates, RegulatoryStatus.Approved),
                (GeRegion.Canada, RegulatoryStatus.Approved),
                (GeRegion.Scotland, RegulatoryStatus.Approved),
                (GeRegion.Chile, RegulatoryStatus.UnderReview)),
            Evidence: "Sterile/triploid lines are an accepted reproductive-containment "
                + "tool and the usual precondition for any GE salmon approval.");

        /// <summary>Engineered sea-lice resistance (not submitted anywhere).</summary>
        public static readonly GeTrait EngineeredLiceResistance = new(
            Id: "ge_lice_resistance",
            Name: "Engineered Lice Resistance",
            Modification: GeModification.LiceResistance,
            TargetTrait: GeneticTrait.SeaLiceResistance,
            EffectMagnitude: 1.3,
            ApprovalCost: 4_000_000,
            ReputationCost: 0.15,
            AnnualRecallRisk: 0.04,
            RecallPenalty: 6_000_000,
            RequiresSterilityContainment: true,
            RegionStatus: Statuses(
                (GeRegion.UnitedStates, RegulatoryStatus.NotSubmitted),
                (GeRegion.Canada, RegulatoryStatus.NotSubmitted),
                (GeRegion.Norway, RegulatoryStatus.NotSubmitted),
                (GeRegion.EuropeanUnion, RegulatoryStatus.NotSubmitted),
                (GeRegion.Scotland, RegulatoryStatus.NotSubmitted),
                (GeRegion.Chile, RegulatoryStatus.NotSubmitted)),
            Evidence: "No commercial GE lice-resistance line; marker-assisted selective "
                + "breeding is the accepted non-GE route (see GenomicSelection).");

        /// <summary>All registered GE traits.</summary>
        public static readonly IReadOnlyList<GeTrait> All = new[]
        {
            AquAdvantageGrowth, EngineeredDiseaseResistance, InducedSterility, EngineeredLiceResistance
        };

        /// <summary>Look up a GE trait by id (case-insensitive); null when unknown.</summary>
        public static GeTrait? Get(string? id) =>
            All.FirstOrDefault(t => t.Id.Equals(id ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }
}
