using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// The capacity rules a game runs under (roadmap S2-01a). The licence
    /// register opens when a game founds its company: a scenario start
    /// (<see cref="Core.ScenarioDefinition.ApplyStartingState"/>) or the
    /// migration of a save written before S2-01. A bare engine
    /// (<c>Initialize</c> alone, the headless test harness that places sites
    /// by hand) has no register and keeps the per-site stocking check only.
    /// </summary>
    public enum CapacityRegime
    {
        /// <summary>No licence register: sites carry their own cap, stocking checks it, nothing else.</summary>
        Unregulated = 0,

        /// <summary>
        /// Capacity v1: company MTB pooled per production area from the licences
        /// held there, a locality MTB per site, stocking checked against the
        /// projected peak and daily enforcement (fine, confiscation of the gain,
        /// forced-harvest order).
        /// </summary>
        LicencesV1 = 1,
    }

    /// <summary>Where a licence came from (save data; stable strings).</summary>
    public static class LicenceOrigin
    {
        /// <summary>Held from the scenario's start.</summary>
        public const string Scenario = "scenario";

        /// <summary>Won at a growth-licence auction.</summary>
        public const string Auction = "auction";

        /// <summary>Bought with a distressed rival's site or licences.</summary>
        public const string Acquisition = "acquisition";

        /// <summary>A save from before S2-01: each site's old MAB became one holding.</summary>
        public const string LegacySave = "legacy_save";

        /// <summary>F2-09: awarded free in a development-licence round.</summary>
        public const string DevelopmentRound = "development_round";
    }

    /// <summary>What kind of sea licence a holding is (F2-09, spec §3.2; save data, default commercial).</summary>
    public enum LicenceKind
    {
        /// <summary>An ordinary licence for salmon food fish: pooled in its production area, liable to the resource rent (a converted development licence once placed).</summary>
        Commercial = 0,
        /// <summary>A development licence (utviklingstillatelse): free, bound to its offshore unit, never pooled, no resource rent, at most 15 years (O:R40, V:2, V:3).</summary>
        Development = 1,
        /// <summary>An offshore-area licence (stage 3, F2-11).</summary>
        OffshoreArea = 2,
    }

    /// <summary>
    /// One aquaculture licence for salmon food fish in the sea (S2-01a): a
    /// tillatelse with its maximum allowed biomass. The biomass of every licence
    /// a company holds in one production area forms one biomass cap
    /// (akvakulturdriftsforskriften § 47 second paragraph, "ett biomassetak";
    /// FOR-2008-06-17-822), which the company's sea sites in that area share;
    /// each site is also held to its own locality MTB (§ 47 third paragraph).
    /// A licence clears no locality (NFD letter 20.09.2017, ref 15/4396-64,
    /// point 4), so building a site adds no licence and no MTB.
    /// <para>
    /// Land-based units hold a land permit, not a sea licence, and are outside
    /// MTB entirely: RAS and post-smolt facilities never count against a pool
    /// and never hold a <see cref="LicenceHolding"/>.
    /// </para>
    /// <para>
    /// Farm types (docs/FARM_TYPES_SPEC.md F2-09) extend this class additively:
    /// <c>Kind</c> (commercial / development / offshore-area, default
    /// commercial), <c>BoundUnitId</c>, <c>ExpiresDay</c> and
    /// <c>PlacedZoneId</c>. Pooling goes through one method,
    /// <see cref="IsPooledIn"/>, so a development licence bound to an offshore
    /// unit (not pooled, laksetildelingsforskriften § 4-13) or an unplaced
    /// converted licence only changes that method.
    /// </para>
    /// </summary>
    public sealed class LicenceHolding
    {
        /// <summary>Deterministic id: "tl_{area}_{sequence}".</summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Production area (1-13, or 0 for the national reference of a game with
        /// no area data) whose biomass cap this licence counts in.
        /// </summary>
        public int PooledArea { get; set; }

        /// <summary>
        /// Maximum allowed biomass of the licence (t): 780, or 945 in Troms and
        /// Finnmark (S2-02's <c>ProductionAreas.LicenceMtbTonnes</c>).
        /// </summary>
        public double MtbTonnes { get; set; }

        /// <summary>
        /// The locality the licence is registered to (informational: the cap is
        /// the area's, not the site's). Empty when it came without a site.
        /// </summary>
        public string RegisteredSiteId { get; set; } = "";

        /// <summary>Sim day the company acquired the licence.</summary>
        public int GrantedDay { get; set; }

        /// <summary>Where it came from (<see cref="LicenceOrigin"/>).</summary>
        public string Origin { get; set; } = "";

        /// <summary>Price paid (NOK); 0 for a scenario's own licences.</summary>
        public decimal PricePaidNok { get; set; }

        // ── F2-09 (additive; a save without them reads an ordinary placed licence) ──

        /// <summary>What kind of licence it is.</summary>
        public LicenceKind Kind { get; set; } = LicenceKind.Commercial;

        /// <summary>The offshore unit a development licence is bound to ("" when unbound: not yet built on, or released by a site withdrawal).</summary>
        public string BoundUnitId { get; set; } = "";

        /// <summary>The sim day a development licence expires unconverted (int.MaxValue for an ordinary licence).</summary>
        public int ExpiresDay { get; set; } = int.MaxValue;

        /// <summary>
        /// The zone a converted licence was placed in ("" = not placed by the
        /// company: an ordinary licence pools in its <see cref="PooledArea"/>; a
        /// converted one waits for <see cref="AwaitingPlacement"/> to end).
        /// </summary>
        public string PlacedZoneId { get; set; } = "";

        /// <summary>True for a converted development licence that must be placed in a production area before use (spec §2C).</summary>
        public bool AwaitingPlacement { get; set; }

        /// <summary>The development-licence award it came from ("" otherwise).</summary>
        public string AwardId { get; set; } = "";

        /// <summary>True while a development licence runs (before its expiry); an ordinary licence always.</summary>
        public bool IsInForce(int day) => day < ExpiresDay;

        /// <summary>
        /// True when this licence's MTB counts in <paramref name="productionArea"/>'s
        /// biomass cap. The one pooling rule: an ordinary licence in its area; a
        /// development licence never (laksetildelingsforskriften: bound to its
        /// project, F2-09 acceptance 3), nor an offshore-area licence, nor a
        /// converted licence before it is placed.
        /// </summary>
        public bool IsPooledIn(int productionArea)
            => Kind == LicenceKind.Commercial && !AwaitingPlacement && PooledArea == productionArea;
    }

    /// <summary>The company's licence register (pure functions over the saved list).</summary>
    public static class LicenceRegister
    {
        /// <summary>
        /// Add one licence and return it. Ids run on <paramref name="sequence"/>
        /// (the state's grant counter), so a replay grants the same ids.
        /// </summary>
        public static LicenceHolding Grant(List<LicenceHolding> holdings, int sequence, int productionArea,
            double mtbTonnes, string registeredSiteId, string origin, int day, decimal pricePaidNok = 0m)
        {
            var holding = new LicenceHolding
            {
                Id = $"tl_{productionArea}_{sequence}",
                PooledArea = productionArea,
                MtbTonnes = mtbTonnes,
                RegisteredSiteId = registeredSiteId ?? "",
                GrantedDay = day,
                Origin = origin ?? "",
                PricePaidNok = pricePaidNok,
            };
            holdings.Add(holding);
            return holding;
        }

        /// <summary>Licence MTB (t) pooled in a production area.</summary>
        public static double PooledMtbTonnes(IReadOnlyList<LicenceHolding> holdings, int productionArea)
        {
            double total = 0.0;
            for (int i = 0; i < holdings.Count; i++)
                if (holdings[i].IsPooledIn(productionArea))
                    total += holdings[i].MtbTonnes;
            return total;
        }

        /// <summary>Number of licences pooled in a production area.</summary>
        public static int CountIn(IReadOnlyList<LicenceHolding> holdings, int productionArea)
        {
            int count = 0;
            for (int i = 0; i < holdings.Count; i++)
                if (holdings[i].IsPooledIn(productionArea))
                    count++;
            return count;
        }
    }
}
