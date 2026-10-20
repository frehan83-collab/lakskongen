using System.ComponentModel.DataAnnotations;

namespace LakskongenSim.Calibration
{
    #region F2-09 development licences, conversion, placement

    // Roadmap F2-09 (docs/FARM_TYPES_SPEC.md §2C "Licence / permit path",
    // §3.9 facilities_offshore.json "licences", §6.1 act 3 and act 4). The
    // licence size, the licences of an OF1-class award and the conversion fee of
    // January 2026 are the farm-type catalogue's (facilities.common.catalog:
    // LicenceSizeTonnesMtb, Of1.Licences, ConversionFeeNokPerLicence, V:2); this
    // record holds the round, the score, the rival field, the index and the
    // conversion's terms. Every leaf carries [Range] and exactly one [Source] or
    // [GameBalance]. Sources cited by name:
    // - Pengepolitikkforskriften 2018: forskrift om pengepolitikken
    //   (FOR-2018-03-02-171) § 1: "Det operative målet for pengepolitikken skal
    //   være en årsvekst i konsumprisene som over tid er nær 2 prosent"
    //   (https://lovdata.no/dokument/SF/forskrift/2018-03-02-171).

    /// <summary>
    /// The development-licence round, the application's score, the rival field,
    /// and the conversion of development licences to commercial ones (spec §2C,
    /// §6.1).
    /// </summary>
    public sealed record OffshoreLicencesCalibration
    {
        /// <summary>Years a development licence runs before it expires unconverted: 15.</summary>
        [Range(1, 50)]
        [Source("O:R40", "V:2")]
        public int DevelopmentLicenceYears { get; init; } = 15;

        /// <summary>Days a round stays open once it opens: 730 (two in-game years).</summary>
        [Range(30, 3_650)]
        [GameBalance("spec §6.1: the round stays open for 2 in-game years (UX-U2)")]
        public int RoundOpenDays { get; init; } = 730;

        /// <summary>Days from an application to its award decision: 180.</summary>
        [Range(1, 730)]
        [GameBalance("spec §6.1: the award arrives after 180 d")]
        public int AwardDelayDays { get; init; } = 180;

        /// <summary>Days from the award by which construction must start, else the award lapses: 365.</summary>
        [Range(30, 1_825)]
        [GameBalance("spec §6.1: construction must start within 365 d")]
        public int ConstructionStartDays { get; init; } = 365;

        /// <summary>Score the first application of its class in the round gets for novelty: 0.3.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §6.1: novelty +0.3 for the first of its class")]
        public double NoveltyBonus { get; init; } = 0.3;

        /// <summary>Most score from the company's escape-free months: 0.2.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §6.1: competence (escape-free months, lice compliance)")]
        public double EscapeFreeWeight { get; init; } = 0.2;

        /// <summary>Escape-free months that earn the full escape-free score: 24.</summary>
        [Range(1, 120)]
        [GameBalance("two full years without an escape: a cycle and its fallow")]
        public int EscapeFreeFullMonths { get; init; } = 24;

        /// <summary>Most score from lice compliance (the share of the company's stocked fjord sites under the limit at their last count): 0.2.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §6.1: competence (escape-free months, lice compliance)")]
        public double LiceComplianceWeight { get; init; } = 0.2;

        /// <summary>Most score from financing: 0.3.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §6.1: financing (cash + credit ≥ 30 % of the equity share of capex)")]
        public double FinancingWeight { get; init; } = 0.3;

        /// <summary>Share of the company's equity share of the unit's capex its cash must cover for the full financing score: 0.30.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §6.1: cash + credit ≥ 30 % of the equity share of capex")]
        public double FinancingShareOfEquityCapex { get; init; } = 0.30;

        /// <summary>Lowest score Fiskeridirektoratet awards at all, whatever the rival field: 0.45.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("an application must show real innovation and the means to build it (O:R40); about half the score")]
        public double AwardFloorScore { get; init; } = 0.45;

        /// <summary>Most rival applications in a round: 3.</summary>
        [Range(0, 10)]
        [GameBalance("a handful of parody rivals apply in each round; the game's rival roster is seven companies")]
        public int RivalApplicantsMax { get; init; } = 3;

        /// <summary>Share of rounds with no rival application at all: 0.10 (at least one rival in about 90 % of seeds).</summary>
        [Range(0.0, 1.0)]
        [GameBalance("F2-09 acceptance 1: at least one rival application in at least 80 % of seeds")]
        public double RivalNoApplicantShare { get; init; } = 0.10;

        /// <summary>Lowest score a rival application has: 0.35.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("the rival field spans weak to strong dossiers around the award floor")]
        public double RivalScoreMin { get; init; } = 0.35;

        /// <summary>Highest score a rival application has: 0.85.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("the rival field spans weak to strong dossiers around the award floor")]
        public double RivalScoreMax { get; init; } = 0.85;

        /// <summary>Days after a round opens within which the rivals file their applications: 365.</summary>
        [Range(1, 1_825)]
        [GameBalance("rivals file through the round's first year, so an early player application can be first of its class")]
        public int RivalSubmissionWindowDays { get; init; } = 365;

        /// <summary>
        /// Yearly rise of the consumer price index the conversion fee is indexed
        /// by from January 2026: 0.02, Norges Bank's operational target
        /// (Pengepolitikkforskriften 2018 § 1).
        /// </summary>
        [Range(0.0, 0.2)]
        [Source("Pengepolitikkforskriften 2018")]
        public double CpiAnnualRate { get; init; } = 0.02;

        /// <summary>Days the company has to pay the conversion fee after the decision, else the conversion lapses: 90.</summary>
        [Range(1, 365)]
        [Source("V:2")]
        public int ConversionPaymentDays { get; init; } = 90;

        /// <summary>Fixed price of growth taken on development licences, paid at conversion (NOK per t of MTB): 170,000, the 2024 fixed price.</summary>
        [Range(0, 1_000_000)]
        [Source("V:2")]
        public decimal FixedGrowthPriceNokPerTonne { get; init; } = 170_000m;

        /// <summary>Fewest days to a conversion decision: 60.</summary>
        [Range(1, 730)]
        [GameBalance("Fiskeridirektoratet's case handling: AOF's conversion was decided about three months after its application (V:2)")]
        public int ConversionDecisionDaysMin { get; init; } = 60;

        /// <summary>Most days to a conversion decision: 120.</summary>
        [Range(1, 730)]
        [GameBalance("Fiskeridirektoratet's case handling: AOF's conversion was decided about three months after its application (V:2)")]
        public int ConversionDecisionDaysMax { get; init; } = 120;

        /// <summary>Days an appeal of a refused conversion takes; it is decided on the criteria met by then: 180.</summary>
        [Range(30, 1_095)]
        [GameBalance("spec §3.9 licences.appealDays: Havfarm-class was refused in 2021 and granted on appeal in 2022 (O:R22, V:12)")]
        public int ConversionAppealDays { get; init; } = 180;

        /// <summary>Production cycles the unit must have completed before a conversion: 1.</summary>
        [Range(0, 10)]
        [GameBalance("O:R4: OF1 converted after its first generation; Havfarm-class after its second (O:R22)")]
        public int ConversionMinCompletedCycles { get; init; } = 1;

        /// <summary>Days without an escape at the unit a conversion needs: 365.</summary>
        [Range(0, 1_825)]
        [GameBalance("the project's target criteria include sound containment; a year without escapes")]
        public int ConversionEscapeFreeDays { get; init; } = 365;

        /// <summary>
        /// The production area's mortality when the company has no fjord fish
        /// there to compare with (a share of the fish that left a site): 0.142,
        /// the national sea-phase mortality of 2025.
        /// </summary>
        [Range(0.01, 0.6)]
        [Source("O:R55", "V:14")]
        public double FallbackAreaMortality { get; init; } = 0.142;
    }

    #endregion

    /// <summary>The sea lane's F2-09 section of the offshore calibration (facilities_offshore.json).</summary>
    public sealed partial record OffshoreCalibration
    {
        /// <summary>F2-09: development licences, the round, conversion and placement.</summary>
        public OffshoreLicencesCalibration Licences { get; init; } = new();
    }
}
