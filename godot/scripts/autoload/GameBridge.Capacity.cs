using Godot;
using LakskongenSim.Core;
using LakskongenSim.Finance;

// S2-01a — capacity v1 (licences and MTB) for U2-01's expansion flow and the
// regulation hub. Thin Godot-facing projection: the engine owns the register,
// the production plan and the enforcement (SimulationEngine.Capacity.cs); this
// partial only maps them onto Godot dictionaries. Reasons and statuses travel
// as keys; their text is in translations.csv (CAPACITY_*), never here.
public partial class GameBridge : Node
{
    /// <summary>
    /// The company's capacity. Keys: "success", "regime" ("licences_v1" |
    /// "unregulated"), "fee_ceiling_nok", "areas" (Array of {production_area,
    /// code, licences, licence_mtb_t, standing_t, headroom_t, projected_peak_t,
    /// projected_peak_day, breach_open, order_deadline_day}), "sites" (Array of
    /// {site_id, label, production_area, locality_mtb_t, standing_t, headroom_t,
    /// projected_peak_t, licences_registered}), "licences" (Array of {id,
    /// production_area, mtb_t, site_id, origin, granted_day, price_paid_nok})
    /// and "breaches" (Array of {id, production_area, start_day, deadline_day,
    /// closed_day, status, status_key, peak_illegal_t, days_over, fine_nok,
    /// confiscated_nok, forced_harvest_t, forced_harvest_revenue_nok,
    /// area_cap_exceeded, sites_over_locality}).
    /// </summary>
    public Godot.Collections.Dictionary GetCapacityOverview()
    {
        var result = new Godot.Collections.Dictionary();
        var engine = SimulationEngine;
        if (engine?.State == null)
        {
            result["success"] = false;
            result["message"] = "Engine not initialized";
            result["areas"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            result["sites"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            result["licences"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            result["breaches"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            return result;
        }

        var overview = engine.GetCapacityOverview();
        var areas = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var a in overview.Areas)
            areas.Add(new Godot.Collections.Dictionary
            {
                ["production_area"] = a.ProductionArea,
                ["code"] = a.Code,
                ["licences"] = a.Licences,
                ["licence_mtb_t"] = a.LicenceMtbTonnes,
                ["standing_t"] = a.StandingTonnes,
                ["headroom_t"] = a.HeadroomTonnes,
                ["projected_peak_t"] = a.ProjectedPeakTonnes,
                ["projected_peak_day"] = a.ProjectedPeakDay,
                ["breach_open"] = a.BreachOpen,
                ["order_deadline_day"] = a.OrderDeadlineDay,
            });
        var sites = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var s in overview.Sites)
            sites.Add(new Godot.Collections.Dictionary
            {
                ["site_id"] = s.SiteId,
                ["label"] = s.Label,
                ["production_area"] = s.ProductionArea,
                ["locality_mtb_t"] = s.LocalityMtbTonnes,
                ["standing_t"] = s.StandingTonnes,
                ["headroom_t"] = s.HeadroomTonnes,
                ["projected_peak_t"] = s.ProjectedPeakTonnes,
                ["licences_registered"] = s.LicencesRegistered,
            });
        var licences = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var l in overview.Licences)
            licences.Add(new Godot.Collections.Dictionary
            {
                ["id"] = l.Id,
                ["production_area"] = l.PooledArea,
                ["mtb_t"] = l.MtbTonnes,
                ["site_id"] = l.RegisteredSiteId,
                ["origin"] = l.Origin,
                ["granted_day"] = l.GrantedDay,
                ["price_paid_nok"] = (double)l.PricePaidNok,
            });
        var breaches = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var b in overview.Breaches)
        {
            var over = new Godot.Collections.Array<string>();
            foreach (var id in b.SitesOverLocality) over.Add(id);
            breaches.Add(new Godot.Collections.Dictionary
            {
                ["id"] = b.Id,
                ["production_area"] = b.Area,
                ["start_day"] = b.StartDay,
                ["deadline_day"] = b.DeadlineDay,
                ["closed_day"] = b.ClosedDay,
                ["status"] = b.Status,
                ["status_key"] = "CAPACITY_BREACH_" + b.Status.ToUpperInvariant(),
                ["peak_illegal_t"] = b.PeakIllegalKg / 1000.0,
                ["days_over"] = b.DaysOver,
                ["fine_nok"] = (double)b.FineNok,
                ["confiscated_nok"] = (double)b.ConfiscatedNok,
                ["forced_harvest_t"] = b.ForcedHarvestKg / 1000.0,
                ["forced_harvest_revenue_nok"] = (double)b.ForcedHarvestRevenueNok,
                ["area_cap_exceeded"] = b.AreaCapExceeded,
                ["sites_over_locality"] = over,
            });
        }

        result["success"] = true;
        result["regime"] = overview.Regime == CapacityRegime.LicencesV1 ? "licences_v1" : "unregulated";
        result["fee_ceiling_nok"] = (double)engine.Calibration.Capacity.InfringementFeeCeilingNok;
        result["areas"] = areas;
        result["sites"] = sites;
        result["licences"] = licences;
        result["breaches"] = breaches;
        return result;
    }

    /// <summary>
    /// What stocking <paramref name="count"/> smolts at a site would do to the
    /// caps (the check <c>stock_smolts</c> runs). Keys: "success", "allowed",
    /// "reason_key" (engine key, "" when allowed), "label_key"
    /// (CAPACITY_REASON_*; "" when allowed), "message", "production_area",
    /// "area_mtb_t", "area_peak_t", "site_mtb_t", "site_peak_t", "peak_day",
    /// "max_count" (most smolts of the strain the site can take now).
    /// </summary>
    public Godot.Collections.Dictionary PreviewStocking(string siteId, int count, string strainId = "standard")
    {
        var result = new Godot.Collections.Dictionary();
        if (SimulationEngine?.State == null)
        {
            result["success"] = false;
            result["allowed"] = false;
            result["message"] = "Engine not initialized";
            return result;
        }
        var p = SimulationEngine.PreviewStocking(siteId, count, strainId);
        result["success"] = p.ReasonKey != "site_not_found";
        result["allowed"] = p.Allowed;
        result["reason_key"] = p.ReasonKey;
        result["label_key"] = string.IsNullOrEmpty(p.ReasonKey) ? "" : "CAPACITY_REASON_" + p.ReasonKey.ToUpperInvariant();
        result["message"] = p.Message;
        result["production_area"] = p.ProductionArea;
        result["area_mtb_t"] = p.AreaMtbTonnes;
        result["area_peak_t"] = p.AreaProjectedPeakTonnes;
        result["site_mtb_t"] = p.SiteMtbTonnes;
        result["site_peak_t"] = p.SiteProjectedPeakTonnes;
        result["peak_day"] = p.PeakDay;
        result["max_count"] = p.MaxCount;
        return result;
    }

    /// <summary>
    /// What building a site in a zone would cost and add (U2-01's "Bygg
    /// lokalitet" card). Keys: "success", "allowed", "reason_key", "reason",
    /// "cost_nok", "production_area", "locality_mtb_t",
    /// "licence_mtb_added_t" (always 0), "area_licence_mtb_t", "pens".
    /// </summary>
    public Godot.Collections.Dictionary PreviewBuildSeaSite(string zoneId)
    {
        var result = new Godot.Collections.Dictionary();
        if (SimulationEngine?.State == null)
        {
            result["success"] = false;
            result["allowed"] = false;
            result["reason"] = "Engine not initialized";
            return result;
        }
        var p = SimulationEngine.PreviewBuildSeaSite(zoneId);
        result["success"] = p.ReasonKey != "zone_not_found";
        result["allowed"] = p.Allowed;
        result["reason_key"] = p.ReasonKey;
        result["reason"] = p.Reason;
        result["cost_nok"] = (double)p.CostNok;
        result["production_area"] = p.ProductionArea;
        result["locality_mtb_t"] = p.LocalityMtbTonnes;
        result["licence_mtb_added_t"] = p.LicenceMtbAddedTonnes;
        result["area_licence_mtb_t"] = p.AreaLicenceMtbTonnes;
        result["pens"] = p.Pens;
        return result;
    }
}
