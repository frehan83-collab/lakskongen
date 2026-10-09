using System;
using System.Collections.Generic;
using LakskongenSim.Processing;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Carbon ledger (Phase 5.4).
    /// Tracks Scope 1 (direct fuel), Scope 2 (electricity), Scope 3 (feed, transport)
    /// and credits from biogas, kelp, avoided emissions.
    /// Units: kg CO2e. Norwegian grid ~15 g CO2e/kWh; diesel 2.68 kg CO2e/L.
    /// Feed ~1.5-2.0 kg CO2e/kg (varies by marine/plant ingredients).
    /// </summary>
    public static class CarbonLedger
    {
        // ─── Emission Factors (kg CO2e per unit) ───
        public const double DieselKgCO2ePerLiter = 2.68;
        public const double GridElectricityKgCO2ePerKwh = 0.015; // Norwegian average
        public const double FeedKgCO2ePerKg = 1.8; // weighted average
        public const double TransportKgCO2ePerKgKm = 0.00015; // truck ~150g CO2e/tonne-km
        public const double HelicopterKgCO2ePerHour = 350.0; // crew transfer
        public const double BoatKgCO2ePerHour = 80.0; // supply vessel

        // ─── Credit Factors ───
        public const double BiogasCreditKgCO2ePerNm3 = 0.45; // displaced natural gas
        public const double KelpCreditKgCO2ePerKgDry = 1.2; // sequestered
        public const double AvoidedSoyCreditKgCO2ePerKgFeed = 0.8; // insect/algae meal vs soy

        public sealed record EmissionEntry(
            string Category,    // "Scope1_Fuel", "Scope2_Electricity", "Scope3_Feed", "Scope3_Transport"
            string Source,      // site id, facility id, "wellboat", etc.
            double KgCO2e,
            int Day);

        public sealed record CreditEntry(
            string Category,    // "Biogas", "Kelp", "AvoidedSoy"
            string Source,
            double KgCO2e,
            int Day);

        /// <summary>Daily fuel consumption for a sea site (liters/day).</summary>
        public static double DailySiteFuelLiters(SeaSite site, double biomassTonnes)
        {
            // Base: 50 L/day + 0.5 L/tonne biomass (feeding, nets, lights)
            return 50.0 + biomassTonnes * 0.5;
        }

        /// <summary>Daily electricity for a sea site (kWh/day).</summary>
        public static double DailySiteElectricityKwh(SeaSite site, double biomassTonnes)
        {
            // Base: 200 kWh + 1 kWh/tonne (oxygen, lights, cameras)
            // Containment multiplier
            double mult = site.Containment switch
            {
                ContainmentLevel.OpenNet => 1.0,
                ContainmentLevel.Skirt => 1.1,
                ContainmentLevel.SemiClosed => 1.8,
                ContainmentLevel.ClosedFloat => 2.2,
                ContainmentLevel.Submersible => 2.0,
                ContainmentLevel.OffshoreClosed => 1.5,
                _ => 1.0
            };
            return (200.0 + biomassTonnes * 1.0) * mult;
        }

        /// <summary>Feed emissions for kg feed used.</summary>
        public static double FeedEmissionsKgCO2e(double feedKg, double replacementPct)
        {
            // Replacement with insect/algae meal avoids soy emissions
            double avoided = feedKg * (replacementPct / 100.0) * AvoidedSoyCreditKgCO2ePerKgFeed;
            return feedKg * FeedKgCO2ePerKg - avoided;
        }

        /// <summary>Wellboat transfer emissions.</summary>
        public static double WellboatEmissionsKgCO2e(double distanceKm, double tonnes)
        {
            return distanceKm * tonnes * TransportKgCO2ePerKgKm * 1000.0;
        }

        /// <summary>Offshore crew rotation emissions (helicopter).</summary>
        public static double OffshoreCrewRotationEmissionsKgCO2e(int crewCount)
        {
            // 2h round trip per 10 crew
            double hours = Math.Ceiling(crewCount / 10.0) * 2.0;
            return hours * HelicopterKgCO2ePerHour;
        }

        /// <summary>Supply vessel emissions for offshore.</summary>
        public static double OffshoreSupplyVesselEmissionsKgCO2e(int tripsPerWeek)
        {
            return tripsPerWeek / 7.0 * 12.0 * BoatKgCO2ePerHour; // 12h round trip
        }

        /// <summary>Processing plant daily emissions.</summary>
        public static double ProcessingPlantEmissionsKgCO2e(int throughputKg, ProcessingTier tier)
        {
            double baseKwh = tier switch
            {
                ProcessingTier.Small => 500,
                ProcessingTier.Medium => 3000,
                ProcessingTier.Large => 10000,
                _ => 500
            };
            double perKg = 0.05; // kWh/kg processed
            double electricity = baseKwh + throughputKg * perKg;
            return electricity * GridElectricityKgCO2ePerKwh;
        }

        /// <summary>Biogas credit from sludge valorization.</summary>
        public static double BiogasCreditKgCO2e(double nm3Biogas)
        {
            return nm3Biogas * BiogasCreditKgCO2ePerNm3;
        }

        /// <summary>Kelp credit (if kelp farming researched).</summary>
        public static double KelpCreditKgCO2e(double kgDryWeight)
        {
            return kgDryWeight * KelpCreditKgCO2ePerKgDry;
        }
    }

    /// <summary>
    /// Tracks carbon ledger for a site or facility.
    /// </summary>
    public class CarbonTracker
    {
        public string SourceId { get; init; } = "";
        /// <summary>Emission journal. Public setter so saves restore it
        /// (System.Text.Json skips getter-only collections on read —
        /// a load used to silently drop the whole carbon history).</summary>
        public List<CarbonLedger.EmissionEntry> Emissions { get; set; } = new();
        /// <summary>Credit journal (same persistence note as Emissions).</summary>
        public List<CarbonLedger.CreditEntry> Credits { get; set; } = new();

        public void AddEmission(string category, string source, double kgCO2e, int day)
        {
            Emissions.Add(new CarbonLedger.EmissionEntry(category, source, kgCO2e, day));
        }

        public void AddCredit(string category, string source, double kgCO2e, int day)
        {
            Credits.Add(new CarbonLedger.CreditEntry(category, source, kgCO2e, day));
        }

        public double TotalEmissions(int fromDay = 0, int toDay = int.MaxValue)
        {
            return Emissions.Where(e => e.Day >= fromDay && e.Day <= toDay).Sum(e => e.KgCO2e);
        }

        public double TotalCredits(int fromDay = 0, int toDay = int.MaxValue)
        {
            return Credits.Where(c => c.Day >= fromDay && c.Day <= toDay).Sum(c => c.KgCO2e);
        }

        public double NetEmissions(int fromDay = 0, int toDay = int.MaxValue)
        {
            return TotalEmissions(fromDay, toDay) - TotalCredits(fromDay, toDay);
        }

        public double EmissionsThisYear(int currentDay)
        {
            int yearStart = (currentDay / 365) * 365;
            return TotalEmissions(yearStart, currentDay);
        }

        public double CreditsThisYear(int currentDay)
        {
            int yearStart = (currentDay / 365) * 365;
            return TotalCredits(yearStart, currentDay);
        }

        public double NetThisYear(int currentDay) => EmissionsThisYear(currentDay) - CreditsThisYear(currentDay);

        /// <summary>Scope breakdown for reporting.</summary>
        public Dictionary<string, double> ScopeBreakdown(int fromDay, int toDay)
        {
            var result = new Dictionary<string, double>
            {
                ["Scope1"] = 0, ["Scope2"] = 0, ["Scope3"] = 0
            };
            foreach (var e in Emissions.Where(x => x.Day >= fromDay && x.Day <= toDay))
            {
                if (e.Category.StartsWith("Scope1")) result["Scope1"] += e.KgCO2e;
                else if (e.Category.StartsWith("Scope2")) result["Scope2"] += e.KgCO2e;
                else if (e.Category.StartsWith("Scope3")) result["Scope3"] += e.KgCO2e;
            }
            return result;
        }

        /// <summary>Top emission sources for dashboard.</summary>
        public List<(string Source, double KgCO2e)> TopSources(int fromDay, int toDay, int count = 5)
        {
            return Emissions
                .Where(e => e.Day >= fromDay && e.Day <= toDay)
                .GroupBy(e => e.Source)
                .Select(g => (g.Key, g.Sum(x => x.KgCO2e)))
                .OrderByDescending(x => x.Item2)
                .Take(count)
                .ToList();
        }
    }
}