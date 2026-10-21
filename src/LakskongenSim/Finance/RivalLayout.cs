using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using LakskongenSim.Core;
using LakskongenSim.Environment.Geo;

namespace LakskongenSim.Finance
{
    /// <summary>One owner on the rival map: a major of the CompetitorAI roster or a generated independent.</summary>
    /// <param name="Id">The roster id of a major ("mowier"), or "ind_&lt;po2&gt;_&lt;nnn&gt;" for an independent.</param>
    /// <param name="Name">The display name (a parody, D17).</param>
    /// <param name="IsMajor">True for a roster company.</param>
    public sealed record RivalOwner(string Id, string Name, bool IsMajor);

    /// <summary>
    /// A rival layout: every pool locality's owner (<see cref="RivalLayout.Build"/>).
    /// Immutable; owners are the majors in roster order, then the independents
    /// by area, municipality and group.
    /// </summary>
    public sealed class RivalLayoutResult
    {
        private readonly Dictionary<int, int> _ownerOf;
        private readonly ReadOnlyCollection<int>[] _sites;

        internal RivalLayoutResult(string signature, IReadOnlyList<RivalOwner> owners, Dictionary<int, int> ownerOf)
        {
            Signature = signature;
            Owners = owners;
            _ownerOf = ownerOf;
            var sites = Enumerable.Range(0, owners.Count).Select(_ => new List<int>()).ToArray();
            foreach (var (locality, owner) in ownerOf.OrderBy(kv => kv.Key))
                sites[owner].Add(locality);
            _sites = sites.Select(s => s.AsReadOnly()).ToArray();
        }

        /// <summary>The roster and anchors this layout was built for (<see cref="RivalLayout.SignatureOf"/>).</summary>
        public string Signature { get; }

        /// <summary>Majors in roster order, then the independents.</summary>
        public IReadOnlyList<RivalOwner> Owners { get; }

        /// <summary>Localities with an owner.</summary>
        public int OwnedCount => _ownerOf.Count;

        /// <summary>The index into <see cref="Owners"/> of a locality's owner, or -1 (not in the pool, or an anchor).</summary>
        public int OwnerIndexOf(int localityId) => _ownerOf.TryGetValue(localityId, out int owner) ? owner : -1;

        /// <summary>A locality's owner, or null.</summary>
        public RivalOwner? OwnerOf(int localityId)
        {
            int index = OwnerIndexOf(localityId);
            return index >= 0 ? Owners[index] : null;
        }

        /// <summary>The localities of the owner at <paramref name="ownerIndex"/>, sorted by id.</summary>
        public IReadOnlyList<int> SitesOf(int ownerIndex) => _sites[ownerIndex];

        /// <summary>The index of the owner with this id, or -1.</summary>
        public int IndexOf(string ownerId)
        {
            for (int i = 0; i < Owners.Count; i++)
                if (Owners[i].Id == ownerId)
                    return i;
            return -1;
        }
    }

    /// <summary>
    /// The static, seed-independent rival layout over the real salmonid
    /// localities (STAGE23_PLAN track A, A3; rulings OD4 and OD10). Pure: ordinal
    /// sorts and StableHash only, no RNG, no game state, read on no tick. It
    /// places the CompetitorAI roster on the coast for the map and the 3D order;
    /// it changes none of the roster's numbers (sites, MAB, harvest), so no
    /// traced field moves.
    /// <list type="number">
    /// <item>The pool: the producing sea grow-out localities
    /// (<see cref="GeoLocalities.SeaGrowoutProducing"/>, 994), the land salmonid
    /// sites (309) and the offshore locality <see cref="OffshoreLocalityId"/>,
    /// minus the excluded ids (the game's own zone anchors).</item>
    /// <item>Each major gets round(TotalMABTonnes / 1,250,000 × 994) sea sites
    /// and round(TotalMABTonnes / 1,250,000 × 309) land sites (its share of the
    /// national MTB), drafted round-robin in roster order. A major takes the
    /// free site it prefers most: one in its home areas first (its
    /// headquarters town: Bergen → PO3 and PO4, Frøya → PO6, Trondheim → PO6,
    /// Stokmarknes → PO9), then by <c>StableHash.Of("rival_layout\u0001" + id, localityId)</c>.</item>
    /// <item><see cref="OffshoreLocalityId"/> goes to <see cref="OffshoreOwnerId"/>,
    /// or to the largest surviving major (by TotalMABTonnes, then roster
    /// order) after a merger removed it.</item>
    /// <item>The rest go to independents, grouped by (production area,
    /// municipality) and split evenly into groups of at most
    /// <see cref="MaxSitesPerIndependent"/>; ids ind_&lt;po2&gt;_&lt;nnn&gt; in
    /// order, names from <see cref="ParodyNames.Of"/>.</item>
    /// </list>
    /// </summary>
    public static class RivalLayout
    {
        /// <summary>The national MTB the roster's shares are measured against (t; CompetitorAI's own constant).</summary>
        public const double NationalMtbTonnes = 1_250_000.0;

        /// <summary>The one offshore salmonid locality (19,000 t, no production area).</summary>
        public const int OffshoreLocalityId = 45196;

        /// <summary>The roster company that owns <see cref="OffshoreLocalityId"/> while it exists.</summary>
        public const string OffshoreOwnerId = "offshore_optimists";

        /// <summary>The most sites one independent holds.</summary>
        public const int MaxSitesPerIndependent = 6;

        /// <summary>The land salmonid kind in localities.geojson.</summary>
        public const string LandSalmonidKind = "land_salmonid";

        /// <summary>Home production areas by headquarters town (the part of Headquarters before the comma).</summary>
        private static readonly Dictionary<string, int[]> HomeAreasByTown = new(StringComparer.Ordinal)
        {
            ["Bergen"] = new[] { 3, 4 },
            ["Frøya"] = new[] { 6 },
            ["Trondheim"] = new[] { 6 },
            ["Stokmarknes"] = new[] { 9 },
        };

        /// <summary>A company's home production areas, from its headquarters town (none when the town is not listed).</summary>
        public static IReadOnlyList<int> HomeAreasOf(CompetitorCompany company)
        {
            if (company == null) throw new ArgumentNullException(nameof(company));
            string town = (company.Headquarters ?? "").Split(',')[0].Trim();
            return HomeAreasByTown.TryGetValue(town, out var areas) ? areas : Array.Empty<int>();
        }

        /// <summary>A major's target number of sites in a pool of <paramref name="poolCount"/>: its MTB share, rounded half away from zero.</summary>
        public static int ShareOf(CompetitorCompany company, int poolCount)
        {
            if (company == null) throw new ArgumentNullException(nameof(company));
            double tonnes = Math.Max(0.0, company.TotalMABTonnes);
            return (int)Math.Round(tonnes / NationalMtbTonnes * poolCount, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// The layout's identity: every input of <see cref="Build"/>. For each
        /// major in roster order its id, its exact MAB (the shares and the
        /// offshore fallback read the exact tonnes, so two values in one whole
        /// tonne can deal different layouts), its home areas and its name (a
        /// loaded save keeps its stored names, ruling OD8, and the layout shows
        /// them); then the excluded locality ids. Ids and names are
        /// length-prefixed, so no name can imitate another roster. Equal
        /// signatures, equal layouts: the engine and the bridge serve a cached
        /// layout while the signature holds.
        /// </summary>
        public static string SignatureOf(IReadOnlyList<CompetitorCompany> roster, IReadOnlyCollection<int> excludedLocalityIds)
        {
            var text = new StringBuilder("rivals-v2");
            foreach (var c in roster ?? Array.Empty<CompetitorCompany>())
            {
                string id = c.Id ?? "", name = c.Name ?? "";
                text.Append('|').Append(id.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(id)
                    .Append(':').Append(c.TotalMABTonnes.ToString("R", CultureInfo.InvariantCulture))
                    .Append(':').Append(string.Join(",", HomeAreasOf(c).Select(po => po.ToString(CultureInfo.InvariantCulture))))
                    .Append(':').Append(name.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(name);
            }
            text.Append("|x");
            foreach (int id in (excludedLocalityIds ?? Array.Empty<int>()).Distinct().OrderBy(i => i))
                text.Append(':').Append(id.ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }

        /// <summary>Build the layout for a roster with some localities kept out (the game's anchors).</summary>
        public static RivalLayoutResult Build(IReadOnlyList<CompetitorCompany> roster, IReadOnlySet<int> excludedLocalityIds)
            => BuildFrom(roster, excludedLocalityIds, GeoLocalities.Default);

        /// <summary>The layout over given localities (tests).</summary>
        internal static RivalLayoutResult BuildFrom(IReadOnlyList<CompetitorCompany> roster, IReadOnlySet<int> excludedLocalityIds,
            GeoLocalities localities)
        {
            roster ??= Array.Empty<CompetitorCompany>();
            excludedLocalityIds ??= new HashSet<int>();
            var majors = roster.ToArray();
            var owners = majors.Select(c => new RivalOwner(c.Id, c.Name, true)).ToList();
            var ownerOf = new Dictionary<int, int>();

            var seaAll = localities.SeaGrowoutProducing;
            var landAll = localities.Salmonid.Where(l => l.Kind == LandSalmonidKind).ToArray();
            var seaPool = seaAll.Where(l => !excludedLocalityIds.Contains(l.Id)).ToArray();
            var landPool = landAll.Where(l => !excludedLocalityIds.Contains(l.Id)).ToArray();

            Draft(majors, seaPool, majors.Select(c => ShareOf(c, seaAll.Count)).ToArray(), ownerOf);
            Draft(majors, landPool, majors.Select(c => ShareOf(c, landAll.Length)).ToArray(), ownerOf);

            var offshore = localities.TryGet(OffshoreLocalityId, out var unit) && unit.IsSalmonid && !excludedLocalityIds.Contains(unit.Id)
                ? unit
                : null;
            int offshoreOwner = OffshoreOwnerIndex(majors);
            if (offshore != null && offshoreOwner >= 0)
                ownerOf[offshore.Id] = offshoreOwner;

            var rest = seaPool.Concat(landPool)
                .Concat(offshore != null ? new[] { offshore } : Array.Empty<GeoLocality>())
                .Where(l => !ownerOf.ContainsKey(l.Id));
            AddIndependents(rest, owners, ownerOf);

            return new RivalLayoutResult(SignatureOf(roster, excludedLocalityIds), owners.AsReadOnly(), ownerOf);
        }

        /// <summary>The roster index that takes the offshore locality: offshore_optimists, else the largest major.</summary>
        private static int OffshoreOwnerIndex(CompetitorCompany[] majors)
        {
            int index = Array.FindIndex(majors, c => c.Id == OffshoreOwnerId);
            if (index >= 0 || majors.Length == 0) return index;
            int largest = 0;
            for (int i = 1; i < majors.Length; i++)
                if (majors[i].TotalMABTonnes > majors[largest].TotalMABTonnes)
                    largest = i;
            return largest;
        }

        /// <summary>
        /// Round-robin draft in roster order: each round, each major short of
        /// its target takes its most preferred free site, until every target is
        /// met or the pool is empty.
        /// </summary>
        private static void Draft(CompetitorCompany[] majors, GeoLocality[] pool, int[] targets, Dictionary<int, int> ownerOf)
        {
            var preferences = majors.Select(c => Preference(c, pool)).ToArray();
            var cursor = new int[majors.Length];
            var taken = new int[majors.Length];
            var free = new HashSet<int>(pool.Select(l => l.Id).Where(id => !ownerOf.ContainsKey(id)));
            bool picked = true;
            while (picked && free.Count > 0)
            {
                picked = false;
                for (int m = 0; m < majors.Length && free.Count > 0; m++)
                {
                    if (taken[m] >= targets[m]) continue;
                    var list = preferences[m];
                    while (cursor[m] < list.Length && !free.Contains(list[cursor[m]])) cursor[m]++;
                    if (cursor[m] >= list.Length) continue;
                    int id = list[cursor[m]++];
                    free.Remove(id);
                    ownerOf[id] = m;
                    taken[m]++;
                    picked = true;
                }
            }
        }

        /// <summary>A major's pool in preference order: home areas first, then its StableHash order, then id.</summary>
        private static int[] Preference(CompetitorCompany company, GeoLocality[] pool)
        {
            var home = HomeAreasOf(company);
            string key = "rival_layout\u0001" + company.Id;
            return pool
                .Select(l => (l.Id, Home: home.Contains(l.Po) ? 0 : 1, Hash: StableHash.Of(key, l.Id)))
                .OrderBy(t => t.Home).ThenBy(t => t.Hash).ThenBy(t => t.Id)
                .Select(t => t.Id)
                .ToArray();
        }

        /// <summary>
        /// The unowned rest in groups by (area, municipality, ordinal), each
        /// split evenly into groups of at most six; numbered per area in order.
        /// </summary>
        private static void AddIndependents(IEnumerable<GeoLocality> rest, List<RivalOwner> owners, Dictionary<int, int> ownerOf)
        {
            var groups = rest
                .GroupBy(l => (l.Po, l.Municipality))
                .OrderBy(g => g.Key.Po).ThenBy(g => g.Key.Municipality, StringComparer.Ordinal);
            var numberInArea = new Dictionary<int, int>();
            foreach (var group in groups)
            {
                var sites = group.OrderBy(l => l.Id).ToArray();
                int chunks = (sites.Length + MaxSitesPerIndependent - 1) / MaxSitesPerIndependent;
                int offset = 0;
                for (int k = 0; k < chunks; k++)
                {
                    int size = sites.Length / chunks + (k < sites.Length % chunks ? 1 : 0);
                    int po = group.Key.Po;
                    int number = numberInArea.TryGetValue(po, out int n) ? n + 1 : 1;
                    numberInArea[po] = number;
                    string id = string.Create(CultureInfo.InvariantCulture, $"ind_{po:00}_{number:000}");
                    owners.Add(new RivalOwner(id, ParodyNames.Of(po, number - 1), false));
                    for (int i = 0; i < size; i++)
                        ownerOf[sites[offset + i].Id] = owners.Count - 1;
                    offset += size;
                }
            }
        }
    }
}
