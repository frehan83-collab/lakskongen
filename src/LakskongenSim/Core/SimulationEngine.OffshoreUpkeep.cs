using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;

namespace LakskongenSim.Core;

/// <summary>
/// An offshore unit's upkeep within its day (roadmap F2-07, docs/FARM_TYPES_SPEC.md
/// §3.3 "OffshoreTickSystem"; called by <see cref="RunOffshoreUnitDay"/> in
/// SimulationEngine.OffshoreOps.cs): the day's feeding against the silo, the
/// ensilage tank, structure and net wear, the teething deck's reveals and fires,
/// what the player knows of the structure and nets, the unit's own ledger lines
/// at its equity share, its fuel, and its warnings. Draws nothing of its own:
/// the day's uniforms are <see cref="RunOffshoreUnitDay"/>'s.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The day's feeding into the recent rate; a day the fish could not feed for an empty silo is counted.</summary>
    private void TrackFeeding(OffshoreUnit unit, int seaDay, bool hasFish)
    {
        unit.RecentFeedTonnes.Add(unit.LastFeedDay == seaDay ? unit.FeedTodayTonnes : 0.0);
        int keep = Math.Max(1, OffshoreCal.Logistics.FeedAverageDays);
        if (unit.RecentFeedTonnes.Count > keep)
            unit.RecentFeedTonnes.RemoveRange(0, unit.RecentFeedTonnes.Count - keep);
        if (hasFish && unit.LastFeedDay != seaDay && unit.SiloTonnes <= 0.0)
            unit.SiloEmptyDays++;
    }

    /// <summary>The day's dead fish (the mortality ledger at the unit) into the ensilage tank, up to its capacity.</summary>
    private void CollectSilage(SeaSite site, OffshoreUnit unit)
    {
        double deadKg = MortalityLedger.ForSite(site.Id).Total.Kg;
        double fresh = Math.Max(0.0, deadKg - unit.DeadKgCollected);
        unit.DeadKgCollected = deadKg;
        unit.SilageTonnes = Math.Min(unit.SilageCapacityTonnes, unit.SilageTonnes + fresh / 1000.0 * OffshoreCal.Logistics.SilagePerDeadTonne);
    }

    /// <summary>
    /// The structure's fatigue, coefficient × (Hs / design Hs)³ a day, and the
    /// nets' wear, a base plus a term per metre above 3 m (spec §3.3); a
    /// firing cable-chain or net-system defect multiplies its term.
    /// </summary>
    private void WearStructureAndNets(SeaSite site, OffshoreUnit unit, int seaDay, bool hasFish)
    {
        var s = OffshoreCal.Structure;
        var t = OffshoreCal.Teething;
        double hs = unit.CurrentWaveHeight;
        double design = Math.Max(0.1, unit.DesignHsM + unit.DesignHsBonusM);
        double fatigue = s.FatigueCoeff * Math.Pow(hs / design, 3.0);
        if (Fires(unit, TeethingDefectKind.CableChainWear, seaDay, hasFish)) fatigue *= t.CableChainFatigueMultiplier;
        unit.StructuralIntegrity = Math.Max(0.0, unit.StructuralIntegrity - fatigue);
        double wear = s.NetDecayPctPerDay + s.NetDecayPerMetreOver * Math.Max(0.0, hs - s.NetDecayThresholdHsM);
        if (Fires(unit, TeethingDefectKind.NetSystemChange, seaDay, hasFish)) wear *= t.NetSystemWearMultiplier;
        site.NetIntegrityPct = Math.Max(0.0, site.NetIntegrityPct - wear);
    }

    /// <summary>True when a revealed, unfixed defect of <paramref name="kind"/> fires today (the unit holds fish).</summary>
    internal static bool Fires(OffshoreUnit unit, TeethingDefectKind kind, int day, bool hasFish)
        => hasFish && unit.TeethingDefects.Any(d => d.Kind == kind && d.IsRevealed && d.RevealedDay <= day && !d.IsFixedBy(day));

    /// <summary>
    /// The teething deck's day: a shakedown (no fish yet, before its end)
    /// reveals each hidden defect with the daily Poisson probability on its
    /// slot's uniform; with fish, a hidden defect reveals itself on its
    /// cycle-1 day; a revealed, unfixed defect with fish in the unit fires.
    /// </summary>
    private void AdvanceTeething(SeaSite site, OffshoreUnit unit, int seaDay, bool hasFish, double[] u)
    {
        if (unit.TeethingDefects.Count == 0 || unit.CommissionedDay == seaDay) return;
        bool shakedown = unit.FirstStockedDay < 0 && !hasFish && seaDay <= unit.ShakedownEndDay;
        double p = TeethingDeck.ShakedownRevealProbabilityPerDay(OffshoreCal.Teething);
        foreach (var defect in unit.TeethingDefects)
        {
            if (!defect.IsRevealed)
            {
                bool reveal = shakedown
                    ? u[Math.Clamp(defect.Slot, 1, TeethingDeck.Slots)] < p
                    : unit.FirstStockedDay >= 0 && seaDay >= unit.FirstStockedDay + defect.RevealOffsetDays;
                if (reveal)
                {
                    defect.RevealedDay = seaDay;
                    PublishGameEvent(new GameEventRequest(GameEventType.TeethingDefectRevealed, State.Day)
                    {
                        SiteId = site.Id,
                        UnitId = site.Id,
                        Variant = defect.Id,
                        Message = hasFish
                            ? SimText.Nb($"{site.Label}: barnesykdom funnet ({TeethingDefectName(defect.Kind)}), den rammer fisken til den er utbedret")
                            : SimText.Nb($"{site.Label}: barnesykdom funnet ({TeethingDefectName(defect.Kind)}), utbedre den før utsett"),
                    });
                }
            }
            if (defect.IsRevealed && defect.FiredDay < 0 && hasFish && !defect.IsFixedBy(seaDay))
                defect.FiredDay = seaDay;
        }
    }

    /// <summary>The Norwegian name of a teething defect (event texts).</summary>
    internal static string TeethingDefectName(TeethingDefectKind kind) => kind switch
    {
        TeethingDefectKind.HarvestZoneCrush => "klemskader i håndteringssonen",
        TeethingDefectKind.CableChainWear => "slitasje på kabelkjeden",
        TeethingDefectKind.NetSystemChange => "notsystemet må byttes",
        TeethingDefectKind.BallastPipeLeak => "lekkasje i ballastrør",
        _ => "biologien flytter ikke med (vintersår)",
    };

    /// <summary>The sensors keep the structure (motion reference unit) and nets (subsea cameras) known while serviced.</summary>
    private void UpdateKnownState(SeaSite site, OffshoreUnit unit, int seaDay)
    {
        int interval = OffshoreCal.Sensors.ServiceIntervalDays;
        if (unit.Sensors.Any(s => s.Kind == SensorPackageKind.Mru && !s.IsPastService(seaDay, interval)))
        {
            unit.StructureKnown = unit.StructuralIntegrity;
            unit.StructureKnownAsOfDay = seaDay;
        }
        if (unit.Sensors.Any(s => s.Kind == SensorPackageKind.SubseaCam && !s.IsPastService(seaDay, interval)))
        {
            unit.NetKnownPct = site.NetIntegrityPct;
            unit.NetKnownAsOfDay = seaDay;
        }
    }

    /// <summary>
    /// The unit's own lines of the day, at its equity share (spec §3.3 "Opex"):
    /// crew in rotation, energy, maintenance and insurance on its contract price.
    /// </summary>
    private void BookOffshoreUnitLines(SeaSite site, OffshoreUnit unit)
    {
        var c = OffshoreCal.Costs;
        decimal share = (decimal)unit.EquityShare;
        decimal perYear = 1m / SimCalendar.DaysPerYear;
        Book(site, unit.EmployeeCount * c.CrewSalaryNokPerYear * perYear * share, "offshore_crew");
        Book(site, (decimal)OffshoreFuelLitresToday(site) * c.DieselNokPerLitre * share, "offshore_energy");
        Book(site, unit.CAPEX * (decimal)c.MaintenanceShareOfCapexPerYear * perYear * share, "offshore_maintenance");
        Book(site, unit.CAPEX * (decimal)c.InsuranceShareOfCapexPerYear * perYear * share, "offshore_insurance");

        void Book(SeaSite s, decimal amount, string category)
        {
            if (amount <= 0m) return;
            PayCost(amount, CostBooking.Opex, category);
            RingFenceOwnLine(s, amount);
        }
    }

    /// <summary>The diesel an operational unit's generators burn today (litres): a base plus a share per tonne of fish.</summary>
    internal double OffshoreFuelLitresToday(SeaSite site)
    {
        var c = OffshoreCal.Costs;
        double biomassTonnes = site.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;
        return c.FuelLitresPerDay + c.FuelLitresPerTonnePerDay * biomassTonnes;
    }

    /// <summary>
    /// The unit's warnings on the bus (spec §3.5): the silo running low (deadline
    /// the day it runs dry), the crew change overdue, and a storm swell whose
    /// estimated peak reaches fish-handling height, the day it enters the
    /// forecast (deadline its onset).
    /// </summary>
    private void PublishOffshoreWarnings(SeaSite site, OffshoreUnit unit, int seaDay, bool hasFish)
    {
        double siloDays = SiloDaysOf(unit);
        if (hasFish && siloDays < OffshoreCal.Logistics.SiloLowWarningDays)
            PublishGameEvent(new GameEventRequest(GameEventType.SiloLow, State.Day)
            {
                SiteId = site.Id,
                UnitId = site.Id,
                DeadlineDay = seaDay + (int)Math.Floor(siloDays),
                Value = siloDays,
                Message = SimText.Nb($"Siloen på {site.Label} har fôr til {siloDays:F1} døgn: bestill påfyll i et værvindu"),
            });
        if (unit.CrewOverdueDays > 0)
            PublishGameEvent(new GameEventRequest(GameEventType.CrewOverdue, State.Day)
            {
                SiteId = site.Id,
                UnitId = site.Id,
                Value = unit.CrewOverdueDays,
                Message = SimText.Nb($"Mannskapsbyttet på {site.Label} er {unit.CrewOverdueDays} døgn på overtid"),
            });
        var sea = State.Metocean;
        if (sea == null) return;
        double scale = PlotHsScaleOf(site);
        var cal = sea.Calibration();
        foreach (var e in sea.Events.Where(e => e.CreatedDay == seaDay))
        {
            var (mean, _) = MetoceanForecast.PeakPosterior(cal, e, seaDay);
            double median = cal.SwellMedianM(SimCalendar.MonthIndex(e.CreatedDay));
            double estimate = scale * OffshoreMetoceanModel.SwellPeak(cal, median, mean);
            if (estimate < OffshoreCal.Operability.FishHandlingHsM) continue;
            PublishGameEvent(new GameEventRequest(GameEventType.StormSwellForecast, State.Day)
            {
                SiteId = site.Id,
                UnitId = site.Id,
                Variant = e.Serial.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DeadlineDay = e.OnsetDay,
                Value = estimate,
                Message = SimText.Nb($"Stormsjø ventes ved {site.Label} dag {e.OnsetDay} (rundt {estimate:F1} m): fyll siloen og gjør fiskehåndtering før den kommer"),
            });
        }
    }
}
