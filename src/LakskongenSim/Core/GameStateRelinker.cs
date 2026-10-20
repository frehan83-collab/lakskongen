using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Repairs the duplicated fish-population graph after JSON deserialization (S2).
    ///
    /// At runtime each FishPopulation lives in exactly one place logically but
    /// is reachable two ways: SeaSite.Pens and GameState.Populations hold the
    /// SAME instances. System.Text.Json has no reference tracking here, so a
    /// save/load round-trip produces TWO independent copies per fish group:
    /// biology ticks State.Populations while harvest/delousing/storms act on
    /// the frozen SeaSite.Pens copies (and Populations.Remove silently misses).
    ///
    /// RelinkIds makes both lists share the canonical instances again, keyed
    /// by FishPopulation.Id. Pens with no matching population (very old saves)
    /// are adopted into Populations rather than dropped.
    /// </summary>
    public static class GameStateRelinker
    {
        public static void RelinkIds(GameState state)
        {
            if (state.Populations == null || state.SeaSites == null)
                return;

            var byId = new Dictionary<string, Biology.FishPopulation>();
            foreach (var pop in state.Populations)
            {
                if (pop == null || string.IsNullOrEmpty(pop.Id))
                    continue;
                if (!byId.ContainsKey(pop.Id))
                    byId[pop.Id] = pop;
            }

            foreach (var site in state.SeaSites)
            {
                if (site?.Pens == null)
                    continue;
                for (int i = 0; i < site.Pens.Count; i++)
                {
                    var pen = site.Pens[i];
                    if (pen == null)
                        continue;
                    if (!string.IsNullOrEmpty(pen.Id) && byId.TryGetValue(pen.Id, out var canonical))
                    {
                        site.Pens[i] = canonical;
                        canonical.SiteId = site.Id;
                        if (!string.IsNullOrEmpty(site.ZoneId))
                            canonical.ZoneId = site.ZoneId;
                    }
                    else
                    {
                        // Orphan pen copy: adopt it as the canonical instance.
                        if (!string.IsNullOrEmpty(pen.Id))
                            byId[pen.Id] = pen;
                        pen.SiteId = site.Id;
                        if (!string.IsNullOrEmpty(site.ZoneId))
                            pen.ZoneId = site.ZoneId;
                        if (!state.Populations.Contains(pen))
                            state.Populations.Add(pen);
                    }
                }
            }

            // Facility tanks and pens hold the same instances as Populations too.
            // A land site's fish are in its pad's zone (F2-03; "" before the
            // migration places a pre-F2 facility, which leaves the zone alone).
            foreach (var ras in state.RASFacilities ?? new List<Farm.RASFacility>())
                RelinkFacility(ras.Tanks, ras.Id, ras.ZoneId, state, byId);
            // A pre-F2 offshore farm's pens (load-only: the migration moves them
            // into its offshore sea site, which the sea-site loop above relinks).
            foreach (var farm in state.OffshoreFarms ?? new List<Farm.OffshoreFarm>())
                RelinkFacility(farm.Pens, farm.Id, "", state, byId);
        }

        private static void RelinkFacility(List<Biology.FishPopulation>? pens, string facilityId, string? zoneId,
            GameState state, Dictionary<string, Biology.FishPopulation> byId)
        {
            if (pens == null) return;
            for (int i = 0; i < pens.Count; i++)
            {
                var pen = pens[i];
                if (pen == null) continue;
                if (!string.IsNullOrEmpty(pen.Id) && byId.TryGetValue(pen.Id, out var canonical))
                {
                    pens[i] = canonical;
                    canonical.SiteId = facilityId;
                    if (!string.IsNullOrEmpty(zoneId))
                        canonical.ZoneId = zoneId;
                }
                else
                {
                    if (!string.IsNullOrEmpty(pen.Id))
                        byId[pen.Id] = pen;
                    pen.SiteId = facilityId;
                    if (!string.IsNullOrEmpty(zoneId))
                        pen.ZoneId = zoneId;
                    if (!state.Populations.Contains(pen))
                        state.Populations.Add(pen);
                }
            }
        }
    }
}
