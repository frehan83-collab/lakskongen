using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;

namespace LakskongenSim.Farm.Offshore
{
    /// <summary>
    /// "Behandle ved krav" (F2-08, spec §3.3 "Handling requirement", §6.6; the
    /// policy F2-08 acceptance 5 measures): when the unit stands under a
    /// handling requirement, book what answers it into the first window at P ≥
    /// <c>min_probability</c>: a delousing with the chosen fish-handling vessel
    /// (a standard wellboat, 2.0 m, or an offshore-class wellboat, 2.5 m at 1.5×
    /// the day rate), a planned harvest of the heaviest zone for MTB relief, the
    /// ROV for an overdue net inspection; a harvest order already placed waits
    /// for its wellboat. Off by default, as the fjord's treatment order is
    /// (F2-05): the player decides to hand the exception to a standing order.
    /// Reads only the gauges the player sees and never draws.
    /// </summary>
    public sealed class HandlingPolicy : IDriftsplanPolicy
    {
        /// <summary>The fish-handling vessel: a standard wellboat or an offshore-class wellboat (2.5 m, 1.5× the day rate).</summary>
        public static readonly DriftsplanSetting HandlingVessel = new()
        {
            Key = "handling_vessel",
            Kind = DriftsplanSettingKind.Choice,
            Choices = new[] { OffshorePolicies.VesselStandard, OffshorePolicies.VesselOffshoreWellboat },
            DefaultChoice = OffshorePolicies.VesselStandard,
            Source = "O:R37: a wellboat handles fish to 2.0 m; GB: an offshore-class wellboat to 2.5 m at 1.5x the day rate (spec §3.3)",
        };

        private static readonly IReadOnlyList<DriftsplanSetting> AllSettings =
            new[] { HandlingVessel, OffshorePolicies.MinProbabilitySetting() };

        public string Kind => OffshorePolicies.Handling;
        public string Title => "behandling ved krav";
        public IReadOnlyList<DriftsplanUnitKind> AppliesTo => OffshorePolicies.Units;
        public IReadOnlyList<DriftsplanSetting> Settings => AllSettings;
        public bool IsDefaultFor(DriftsplanUnitKind kind) => false;

        /// <summary>The treatment the policy books: the wellboat's pressure-water treatment (the booking verb's default).</summary>
        internal const LakskongenSim.Data.DelousingMethod DelousingMethod = LakskongenSim.Data.DelousingMethod.Hydrolicer;

        /// <summary>Its name as the booking verb takes it.</summary>
        internal const string DelousingMethodName = "hydrolicer";

        /// <summary>The fish-handling vessel of a "handling_vessel" choice.</summary>
        internal static VesselClass HandlingVesselOf(string choice)
            => choice == OffshorePolicies.VesselOffshoreWellboat ? VesselClass.OffshoreWellboat : VesselClass.Standard;

        public DriftsplanJob? Plan(DriftsplanContext context)
        {
            const string verb = "book_first_green_window";
            string command = $"{verb} {context.UnitId} handling";
            var site = OffshorePolicies.Commissioned(context.Engine, context.UnitId);
            if (site == null)
                return OffshorePolicies.Waiting(Kind, context.UnitId, verb, command, OffshorePolicies.TriggerUnitCommissioned);
            var unit = site.Offshore!;
            string id = site.Id;
            double p = context.Number("min_probability");
            switch (unit.Requirement)
            {
                case HandlingRequirement.Delouse:
                    if (unit.Bookings.Any(b => b.IsOpen && b.Op == OffshoreOp.Delousing))
                        return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerVesselBooked);
                    // The wellboat's mechanical treatment works from its minimum water temperature
                    // (a booking in colder water would only fail on its window day): wait for it.
                    if (context.Engine.SeaTemperatureAtSite(site) < DelousingSystem.GetProfile(DelousingMethod).MinTemperatureC)
                        return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerWaterTooCold);
                    var vessel = HandlingVesselOf(context.Choice(HandlingVessel.Key));
                    return new DriftsplanJob
                    {
                        Policy = Kind,
                        UnitId = id,
                        Day = context.Today,
                        Verb = verb,
                        Command = $"{verb} {id} delousing {vessel.ToString().ToLowerInvariant()}",
                        Issue = engine => engine.BookFirstGreenWindow(id, OffshoreOp.Delousing, vessel, p, detail: DelousingMethodName),
                    };
                case HandlingRequirement.MtbRelief:
                    if (context.Engine.HarvestOrders.Any(o => o.HoldingId == id))
                        return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerHarvestOrdered);
                    string? heaviest = site.PhysicalPens
                        .Select(pen => (Pen: pen, Pop: site.PopulationIn(pen)))
                        .Where(z => z.Pop != null && z.Pop.TotalCount > 0)
                        .OrderByDescending(z => z.Pop!.GetTotalBiomassKg()).ThenBy(z => z.Pen.Number)
                        .Select(z => z.Pen.Id).FirstOrDefault();
                    if (heaviest == null)
                        return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerRequirementOpen);
                    const string harvestVerb = "order_harvest";
                    string zone = heaviest;
                    return new DriftsplanJob
                    {
                        Policy = Kind,
                        UnitId = id,
                        Day = context.Today,
                        Verb = harvestVerb,
                        Command = $"{harvestVerb} {id} {zone}",
                        Issue = engine => engine.OrderHarvest(id, new[] { zone }),
                    };
                case HandlingRequirement.Harvest:
                    return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerHarvestOrdered);
                case HandlingRequirement.NetInspection:
                    if (unit.Bookings.Any(b => b.IsOpen && b.Op == OffshoreOp.NetInspection))
                        return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerVesselBooked);
                    return OffshorePolicies.BookGreenWindow(Kind, id, context.Today, OffshoreOp.NetInspection, VesselClass.Standard, p);
                default:
                    return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerRequirementOpen);
            }
        }
    }
}
