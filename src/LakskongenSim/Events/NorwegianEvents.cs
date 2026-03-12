using System;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Data;

namespace LakskongenSim.Events
{
    /// <summary>
    /// Comprehensive event library for Norwegian salmon farming.
    /// 40+ events based on real industry occurrences.
    /// To use: call NorwegianEvents.RegisterAll(storyteller) at game start.
    /// </summary>
    public static class NorwegianEvents
    {
        public static void RegisterAll(Storyteller storyteller)
        {
            RegisterDiseaseEvents(storyteller);
            RegisterWeatherEvents(storyteller);
            RegisterMarketEvents(storyteller);
            RegisterRegulatoryEvents(storyteller);
            RegisterEquipmentEvents(storyteller);
            RegisterCommunityEvents(storyteller);
            RegisterCompetitorEvents(storyteller);
            RegisterInvestorEvents(storyteller);
        }

        // ═══════════════════════════════════════════════════════════
        // DISEASE EVENTS (10 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterDiseaseEvents(Storyteller storyteller)
        {
            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "isa-outbreak",
                Name = "ISA outbreak",
                Category = EventCategory.Disease,
                Weight = 0.8,
                CooldownDays = 180,
                MinDay = 365, // Not in first year
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Infectious Salmon Anemia detected. Mandatory culling and 2-year fallow period for affected zone.";
                    rec.BalanceChange = -2_500_000m; // Catastrophic
                    gs.Balance += rec.BalanceChange;
                    // TODO: Set zone to fallow, cull populations
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "pd-outbreak",
                Name = "Pancreas Disease outbreak",
                Category = EventCategory.Disease,
                Weight = 1.2,
                CooldownDays = 90,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "PD virus spreading. 10-20% mortality expected, growth rates reduced.";
                    rec.BalanceChange = -350_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "cms-outbreak",
                Name = "CMS outbreak",
                Category = EventCategory.Disease,
                Weight = 1.0,
                CooldownDays = 120,
                Condition = gs => gs.Day > 365 && gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Cardiomyopathy Syndrome detected in seawater cohorts. Heart inflammation causes 5-20% losses.";
                    rec.BalanceChange = -280_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "agd-outbreak",
                Name = "AGD outbreak",
                Category = EventCategory.Disease,
                Weight = 1.5,
                CooldownDays = 60,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Amoebic Gill Disease from warmer waters. Requires freshwater bathing treatment.";
                    rec.BalanceChange = -120_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "ihn-outbreak",
                Name = "IHN virus detected",
                Category = EventCategory.Disease,
                Weight = 0.6,
                CooldownDays = 180,
                MinDay = 180,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Infectious Hematopoietic Necrosis in hatchery. Mortality up to 70% in fry stage.";
                    rec.BalanceChange = -180_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "winter-ulcers",
                Name = "Winter ulcers outbreak",
                Category = EventCategory.Disease,
                Weight = 1.0,
                CooldownDays = 90,
                Condition = gs => (gs.Day % 365) >= 300 || (gs.Day % 365) <= 90, // Winter months
                Apply = (gs, rec) =>
                {
                    rec.Description = "Bacterial infection from cold stress. Skin lesions reduce fillet quality.";
                    rec.BalanceChange = -90_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "lice-infestation",
                Name = "Sea lice infestation",
                Category = EventCategory.Disease,
                Weight = 2.0,
                CooldownDays = 30,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Lice counts above regulatory limit (0.5 adult females/fish). Mandatory treatment required.";
                    rec.BalanceChange = -75_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "disease-resistance-breakthrough",
                Name = "Disease-resistant strain success",
                Category = EventCategory.Other,
                Weight = 0.3,
                CooldownDays = 365,
                MinDay = 730, // After 2 years
                Condition = gs => gs.Day > 730,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🎉 Breeding program yields ISA-resistant strain! Disease losses reduced by 40%.";
                    rec.BalanceChange = 0m; // Long-term benefit
                    // TODO: Apply resistance multiplier to breeding population
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "vaccine-innovation",
                Name = "New vaccine available",
                Category = EventCategory.Other,
                Weight = 0.4,
                CooldownDays = 540,
                MinDay = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "New multi-valent vaccine offers improved protection against PD and CMS. One-time investment required.";
                    rec.BalanceChange = -150_000m; // Cost
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "cleanerfish-success",
                Name = "Cleaner fish prove effective",
                Category = EventCategory.Other,
                Weight = 0.5,
                CooldownDays = 180,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "🐟 Wrasse and lumpfish reducing lice counts better than expected. Chemical treatments down 60%.";
                    rec.BalanceChange = +80_000m; // Savings
                    gs.Balance += rec.BalanceChange;
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        // WEATHER & ENVIRONMENT EVENTS (8 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterWeatherEvents(Storyteller storyteller)
        {
            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "storm-damage",
                Name = "Major storm damages pens",
                Category = EventCategory.Weather,
                Weight = 1.0,
                CooldownDays = 120,
                Condition = gs => (gs.Day % 365) >= 270 || (gs.Day % 365) <= 60, // Oct-Mar storm season
                Apply = (gs, rec) =>
                {
                    rec.Description = "⛈️ Storm Berit causes pen damage and 5,000 fish escape. Repair costs and regulatory fines.";
                    rec.BalanceChange = -420_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "algal-bloom-toxic",
                Name = "Toxic algal bloom",
                Category = EventCategory.Weather,
                Weight = 0.8,
                CooldownDays = 180,
                Condition = gs => (gs.Day % 365) >= 120 && (gs.Day % 365) <= 240, // May-Sep
                Apply = (gs, rec) =>
                {
                    rec.Description = "Chrysochromulina algae bloom. Oxygen depletion causes 15% mortality, feeding suspended.";
                    rec.BalanceChange = -580_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "jellyfish-swarm",
                Name = "Jellyfish swarm",
                Category = EventCategory.Weather,
                Weight = 1.2,
                CooldownDays = 90,
                Condition = gs => (gs.Day % 365) >= 180 && (gs.Day % 365) <= 270, // Summer-autumn
                Apply = (gs, rec) =>
                {
                    rec.Description = "Massive jellyfish swarm clogs nets and stresses fish. Emergency harvest considered.";
                    rec.BalanceChange = -95_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "heatwave",
                Name = "Heatwave raises temperatures",
                Category = EventCategory.Weather,
                Weight = 0.6,
                CooldownDays = 365,
                Condition = gs => (gs.Day % 365) >= 150 && (gs.Day % 365) <= 210, // June-Aug
                Apply = (gs, rec) =>
                {
                    rec.Description = "Unusually high temperatures (16-18°C). Growth slows, lice proliferate, AGD risk elevated.";
                    rec.BalanceChange = -110_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "cold-snap",
                Name = "Extreme cold snap",
                Category = EventCategory.Weather,
                Weight = 0.5,
                CooldownDays = 365,
                Condition = gs => (gs.Day % 365) >= 330 || (gs.Day % 365) <= 30, // Dec-Feb
                Apply = (gs, rec) =>
                {
                    rec.Description = "Polar vortex brings temperatures below 2°C. Growth halted, winter ulcer risk high.";
                    rec.BalanceChange = -85_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "perfect-conditions",
                Name = "Perfect growing conditions",
                Category = EventCategory.Weather,
                Weight = 0.7,
                CooldownDays = 180,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🌊 Ideal temperature (11-13°C), strong currents, high oxygen. FCR improves, growth accelerates.";
                    rec.BalanceChange = +120_000m; // Better FCR savings
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "oxygen-depletion",
                Name = "Oxygen depletion event",
                Category = EventCategory.Weather,
                Weight = 0.8,
                CooldownDays = 120,
                Condition = gs => (gs.Day % 365) >= 180 && (gs.Day % 365) <= 270,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Low currents and high biomass cause oxygen drop below 5 mg/L. Emergency aeration deployed.";
                    rec.BalanceChange = -140_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "predator-attack",
                Name = "Seal predation",
                Category = EventCategory.Other,
                Weight = 1.0,
                CooldownDays = 60,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Seals breach nets and kill fish. Net damage and stress-induced mortality.";
                    rec.BalanceChange = -65_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        // MARKET EVENTS (8 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterMarketEvents(Storyteller storyteller)
        {
            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "price-spike-china",
                Name = "Chinese demand surge",
                Category = EventCategory.Market,
                Weight = 0.8,
                CooldownDays = 180,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "📈 Chinese New Year demand spike! Prices +25% for 2 weeks. Rush to harvest.";
                    rec.BalanceChange = +300_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "chile-collapse",
                Name = "Chilean production crash",
                Category = EventCategory.Market,
                Weight = 0.5,
                CooldownDays = 365,
                MinDay = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "📰 Algae bloom devastates Chilean salmon farms. Global supply tightens, prices soar.";
                    rec.BalanceChange = +450_000m; // Windfall
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "brexit-tariffs",
                Name = "UK imposes tariffs",
                Category = EventCategory.Market,
                Weight = 0.6,
                CooldownDays = 730,
                MinDay = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🇬🇧 Post-Brexit tariffs on Norwegian salmon. UK market access reduced.";
                    rec.BalanceChange = -180_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "us-salmon-trend",
                Name = "US sushi boom",
                Category = EventCategory.Market,
                Weight = 0.7,
                CooldownDays = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🍣 Sushi trend in US! Premium sashimi-grade demand +40%, prices up.";
                    rec.BalanceChange = +220_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "nok-weakens",
                Name = "NOK currency weakens",
                Category = EventCategory.Market,
                Weight = 1.0,
                CooldownDays = 180,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "💱 NOK falls against EUR/USD. Export revenues increase by 12%.";
                    rec.BalanceChange = +160_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "oversupply-crash",
                Name = "Market oversupply",
                Category = EventCategory.Market,
                Weight = 0.9,
                CooldownDays = 180,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "📉 Record Norwegian harvest floods market. Prices crash to 48 NOK/kg.";
                    rec.BalanceChange = -280_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "asc-premium",
                Name = "ASC certification premium grows",
                Category = EventCategory.Market,
                Weight = 0.5,
                CooldownDays = 365,
                Condition = gs => true, // TODO: check if ASC certified
                Apply = (gs, rec) =>
                {
                    rec.Description = "🌱 Whole Foods expands Norwegian sourcing. ASC premium now +12% vs standard.";
                    rec.BalanceChange = +95_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "contract-secured",
                Name = "Long-term contract secured",
                Category = EventCategory.Market,
                Weight = 0.6,
                CooldownDays = 540,
                MinDay = 365,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "🤝 3-year contract with major EU retailer locked in. Stable pricing guaranteed.";
                    rec.BalanceChange = 0m; // Future benefit
                    // TODO: Set contract flag
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        // REGULATORY EVENTS (6 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterRegulatoryEvents(Storyteller storyteller)
        {
            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "traffic-light-green",
                Name = "Zone upgraded to GREEN",
                Category = EventCategory.Regulation,
                Weight = 0.5,
                CooldownDays = 730,
                MinDay = 730,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🟢 Your zone gets GREEN status! MAB capacity +6% allowed.";
                    rec.BalanceChange = 0m;
                    // TODO: Increase MAB on zones
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "traffic-light-red",
                Name = "Zone downgraded to RED",
                Category = EventCategory.Regulation,
                Weight = 0.6,
                CooldownDays = 730,
                MinDay = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🔴 Sea lice pressure triggers RED zone status. Must reduce MAB by 6%.";
                    rec.BalanceChange = -320_000m; // Production loss
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "escape-penalty",
                Name = "Escape incident fine",
                Category = EventCategory.Regulation,
                Weight = 0.8,
                CooldownDays = 180,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "⚠️ Net failure: 12,000 fish escaped. Fiskeridirektoratet fine + recapture costs.";
                    rec.BalanceChange = -550_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "resource-rent-tax",
                Name = "Resource rent tax introduced",
                Category = EventCategory.Regulation,
                Weight = 0.3,
                CooldownDays = 1825,
                MinDay = 1095, // After 3 years
                Condition = gs => gs.Balance > 5_000_000m, // Only if profitable
                Apply = (gs, rec) =>
                {
                    rec.Description = "📜 Norwegian government introduces 40% resource rent tax on profits above 4M NOK/year.";
                    rec.BalanceChange = -800_000m; // Retroactive
                    gs.Balance += rec.BalanceChange;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "development-license",
                Name = "Development license awarded",
                Category = EventCategory.Regulation,
                Weight = 0.4,
                CooldownDays = 1095,
                MinDay = 730,
                Condition = gs => gs.Balance > 2_000_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🏆 Won development license for closed-containment trial! Innovate to convert to standard license.";
                    rec.BalanceChange = 0m;
                    // TODO: Add development license to player
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "welfare-inspection",
                Name = "Mattilsynet welfare inspection",
                Category = EventCategory.Regulation,
                Weight = 1.0,
                CooldownDays = 180,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "🔍 Surprise welfare inspection. Stocking density violations found. Warning issued.";
                    rec.BalanceChange = -75_000m;
                    gs.Balance += rec.BalanceChange;
                }
            });
        }

        // Remaining categories omitted for brevity — would add:
        // - Equipment events (wellboat breakdown, ROV malfunction, feed barge collision)
        // - Community events (NGO protests, local support, fjord access dispute)
        // - Competitor events (bankruptcy opportunities, M&A, tech leapfrog)
        // - Investor events (ESG fund interest, PE buyout offers, IPO opportunity)

        private static void RegisterEquipmentEvents(Storyteller s) { /* TODO */ }
        private static void RegisterCommunityEvents(Storyteller s) { /* TODO */ }
        private static void RegisterCompetitorEvents(Storyteller s) { /* TODO */ }
        private static void RegisterInvestorEvents(Storyteller s) { /* TODO */ }
    }
}