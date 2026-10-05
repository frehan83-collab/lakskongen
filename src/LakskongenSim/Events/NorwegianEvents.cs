using System;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Events
{
    /// <summary>
    /// Comprehensive event library for Norwegian salmon farming.
    /// 40+ events based on real industry occurrences.
    /// To use: call NorwegianEvents.RegisterAll(storyteller) at game start.
    /// 
    /// NOTE: Cash settles through FinanceManager when provided (production);
    /// falls back to GameState.Balance for tests without finance (same
    /// convention as Storyteller). GameState.Balance is otherwise a
    /// read-only mirror maintained by SimulationEngine.
    /// </summary>
    #pragma warning disable CS0618 // Type or member is obsolete
    public static class NorwegianEvents
    {
        public static void RegisterAll(Storyteller storyteller, Finance.FinanceManager? finance = null)
        {
            RegisterDiseaseEvents(storyteller, finance);
            RegisterWeatherEvents(storyteller, finance);
            RegisterMarketEvents(storyteller, finance);
            RegisterRegulatoryEvents(storyteller, finance);
            RegisterEquipmentEvents(storyteller, finance);
            RegisterCommunityEvents(storyteller, finance);
            RegisterCompetitorEvents(storyteller, finance);
            RegisterInvestorEvents(storyteller, finance);
        }

        /// <summary>
        /// Settle an event's BalanceChange. Unconditional (ForceSpend for costs:
        /// fines and catastrophes apply even into debt — debt scenarios allow
        /// negative balances), matching the original direct-mutation semantics.
        /// </summary>
        private static void SettleBalanceChange(Finance.FinanceManager? finance, GameState gs, EventRecord rec)
        {
            if (rec.BalanceChange == 0m) return;
            if (finance != null)
            {
                if (rec.BalanceChange >= 0m)
                    finance.AddRevenue(rec.BalanceChange);
                else
                    finance.ForceSpend(-rec.BalanceChange);
            }
            else
            {
                gs.Balance += rec.BalanceChange; // test fallback (no finance)
            }
        }

        // ═══════════════════════════════════════════════════════════
        // DISEASE EVENTS (10 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterDiseaseEvents(Storyteller storyteller, Finance.FinanceManager? finance)
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
                    SettleBalanceChange(finance, gs, rec);
                    
                    // Cull all populations in the first zone (or random zone if multiple)
                    if (gs.Populations.Any())
                    {
                        var firstPop = gs.Populations.First();
                        firstPop.Cohorts.Clear();
                    }
                    
                    // Set first zone to fallow (reduce capacity to effectively disable it)
                    if (gs.Zones.Any())
                    {
                        var zone = gs.Zones.First();
                        zone.CarryingCapacityTonnes *= 0.1; // Drastically reduce capacity during fallow
                    }
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    
                    // Update best disease resistance breeding value
                    gs.BestDiseaseResistanceBreedingValue = Math.Max(
                        gs.BestDiseaseResistanceBreedingValue, 
                        0.8 // Significant resistance breakthrough
                    );
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        // WEATHER & ENVIRONMENT EVENTS (8 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterWeatherEvents(Storyteller storyteller, Finance.FinanceManager? finance)
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        // MARKET EVENTS (8 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterMarketEvents(Storyteller storyteller, Finance.FinanceManager? finance)
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "asc-premium",
                Name = "ASC certification premium grows",
                Category = EventCategory.Market,
                Weight = 0.5,
                CooldownDays = 365,
                Condition = gs => gs.HasASCCertification,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🌱 Whole Foods expands Norwegian sourcing. ASC premium now +12% vs standard.";
                    rec.BalanceChange = +95_000m;
                    SettleBalanceChange(finance, gs, rec);
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
                    gs.ContractActive = true;
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        // REGULATORY EVENTS (6 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterRegulatoryEvents(Storyteller storyteller, Finance.FinanceManager? finance)
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
                    
                    // Increase MAB (Maximum Allowable Biomass) on all zones by 6%
                    foreach (var zone in gs.Zones)
                    {
                        zone.CarryingCapacityTonnes *= 1.06;
                    }
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    SettleBalanceChange(finance, gs, rec);
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
                    
                    // Add development license to unlocked research/licenses
                    if (!gs.UnlockedResearchIds.Contains("development-license"))
                    {
                        gs.UnlockedResearchIds.Add("development-license");
                    }
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
                    SettleBalanceChange(finance, gs, rec);
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        // EQUIPMENT EVENTS (5 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterEquipmentEvents(Storyteller storyteller, Finance.FinanceManager? finance)
        {
            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "wellboat-breakdown",
                Name = "Wellboat breakdown",
                Category = EventCategory.Equipment,
                Weight = 1.2,
                CooldownDays = 120,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "⚙️ Main wellboat engine failure during harvest transport. Emergency repairs and delayed delivery.";
                    rec.BalanceChange = -185_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "rov-malfunction",
                Name = "ROV malfunction",
                Category = EventCategory.Equipment,
                Weight = 1.0,
                CooldownDays = 90,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "🤖 Underwater ROV inspection system fails. Manual net inspections required, labor costs up.";
                    rec.BalanceChange = -95_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "feed-barge-collision",
                Name = "Feed barge collision",
                Category = EventCategory.Equipment,
                Weight = 0.6,
                CooldownDays = 180,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "🚢 Feed barge collides with pen structure in rough weather. Net damage and 2,000 fish escape.";
                    rec.BalanceChange = -320_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "net-damage",
                Name = "Net damage discovered",
                Category = EventCategory.Equipment,
                Weight = 1.3,
                CooldownDays = 60,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "🎣 Routine inspection reveals extensive net wear and small tears. Emergency net replacement needed.";
                    rec.BalanceChange = -145_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "sensor-failure",
                Name = "Monitoring sensor failure",
                Category = EventCategory.Equipment,
                Weight = 0.9,
                CooldownDays = 90,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "📊 Oxygen and temperature sensors malfunction. Missed early warning of low-oxygen event causes losses.";
                    rec.BalanceChange = -110_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        // COMMUNITY EVENTS (5 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterCommunityEvents(Storyteller storyteller, Finance.FinanceManager? finance)
        {
            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "ngo-protest",
                Name = "Environmental NGO protest",
                Category = EventCategory.Community,
                Weight = 0.7,
                CooldownDays = 180,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "📢 Greenpeace/WWF protest over sea lice impacts on wild salmon. Media coverage damages brand reputation.";
                    rec.BalanceChange = -125_000m; // Lost contracts + PR costs
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "local-community-support",
                Name = "Local community support",
                Category = EventCategory.Community,
                Weight = 0.8,
                CooldownDays = 365,
                MinDay = 365,
                Condition = gs => gs.Balance > 1_000_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🤝 Local municipality praises your employment and tax contribution. Easier permit renewals and expansion approval.";
                    rec.BalanceChange = +85_000m; // Reduced bureaucracy costs
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "fjord-access-dispute",
                Name = "Fjord access dispute",
                Category = EventCategory.Community,
                Weight = 0.6,
                CooldownDays = 540,
                Condition = gs => gs.SeaSites.Count >= 2,
                Apply = (gs, rec) =>
                {
                    rec.Description = "⚖️ Local fishing association challenges your fjord access rights. Legal fees and potential site relocation.";
                    rec.BalanceChange = -210_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "tourism-conflict",
                Name = "Tourism industry conflict",
                Category = EventCategory.Community,
                Weight = 0.5,
                CooldownDays = 365,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "🏔️ Tourism operators complain your pens ruin scenic fjord views. Municipal hearing and compromise required.";
                    rec.BalanceChange = -95_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "school-visit-pr",
                Name = "School visit PR success",
                Category = EventCategory.Community,
                Weight = 0.9,
                CooldownDays = 180,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "🎓 Educational tours for local schools generate positive press. 'Farm-to-table' story boosts brand value.";
                    rec.BalanceChange = +65_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        // COMPETITOR EVENTS (5 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterCompetitorEvents(Storyteller storyteller, Finance.FinanceManager? finance)
        {
            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "competitor-bankruptcy",
                Name = "Competitor bankruptcy opportunity",
                Category = EventCategory.Competitor,
                Weight = 0.4,
                CooldownDays = 730,
                MinDay = 365,
                Condition = gs => gs.Balance > 1_500_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "💼 Local competitor goes bankrupt. Acquire their licenses and sites at 60% discount?";
                    rec.BalanceChange = -1_200_000m; // Acquisition cost
                    SettleBalanceChange(finance, gs, rec);
                    gs.AcquisitionCount++;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "ma-offer",
                Name = "M&A acquisition offer",
                Category = EventCategory.Competitor,
                Weight = 0.3,
                CooldownDays = 1095,
                MinDay = 730,
                Condition = gs => gs.Balance > 3_000_000m && gs.SeaSites.Count >= 2,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🏢 Mowi/SalMar makes acquisition offer for your operation. Premium valuation but loss of independence.";
                    rec.BalanceChange = +2_500_000m; // Buyout windfall
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "tech-leapfrog",
                Name = "Competitor tech leapfrog",
                Category = EventCategory.Competitor,
                Weight = 0.6,
                CooldownDays = 365,
                MinDay = 540,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🚀 Rival deploys breakthrough closed-containment system. Your traditional pens look outdated to buyers.";
                    rec.BalanceChange = -180_000m; // Lost premium contracts
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "price-war",
                Name = "Price war initiated",
                Category = EventCategory.Competitor,
                Weight = 0.8,
                CooldownDays = 180,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "💥 Major producers flood market to drive out smaller operators. Prices crash to 42 NOK/kg.";
                    rec.BalanceChange = -350_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "competitor-scandal",
                Name = "Competitor scandal boost",
                Category = EventCategory.Competitor,
                Weight = 0.5,
                CooldownDays = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "📰 Major competitor caught using illegal pesticides. Buyers shift orders to your certified operation.";
                    rec.BalanceChange = +275_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        // INVESTOR EVENTS (5 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterInvestorEvents(Storyteller storyteller, Finance.FinanceManager? finance)
        {
            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "esg-fund-interest",
                Name = "ESG fund investment interest",
                Category = EventCategory.Investor,
                Weight = 0.5,
                CooldownDays = 540,
                MinDay = 730,
                Condition = gs => gs.HasASCCertification && gs.Balance > 1_500_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🌱 Nordic ESG fund offers growth capital for sustainable expansion. 15% equity for 5M NOK.";
                    rec.BalanceChange = +5_000_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "pe-buyout-offer",
                Name = "Private equity buyout offer",
                Category = EventCategory.Investor,
                Weight = 0.3,
                CooldownDays = 1095,
                MinDay = 1095,
                Condition = gs => gs.Balance > 5_000_000m && gs.AcquisitionCount >= 1,
                Apply = (gs, rec) =>
                {
                    rec.Description = "💰 PE firm offers 8x EBITDA buyout. Retire rich or keep building your empire?";
                    rec.BalanceChange = +8_000_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "ipo-opportunity",
                Name = "IPO opportunity",
                Category = EventCategory.Investor,
                Weight = 0.2,
                CooldownDays = 1825,
                MinDay = 1460,
                Condition = gs => gs.Balance > 10_000_000m && gs.SeaSites.Count >= 3,
                Apply = (gs, rec) =>
                {
                    rec.Description = "📈 Oslo Børs IPO window opens. List on exchange, raise expansion capital, face quarterly earnings pressure.";
                    rec.BalanceChange = +12_000_000m; // IPO proceeds
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "angel-investor",
                Name = "Angel investor backing",
                Category = EventCategory.Investor,
                Weight = 0.7,
                CooldownDays = 365,
                MinDay = 180,
                Condition = gs => gs.Balance < 2_000_000m && gs.Balance > 500_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "👼 Wealthy seafood industry veteran offers mentorship + 1.5M NOK for 10% equity.";
                    rec.BalanceChange = +1_500_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "innovation-grant",
                Name = "Government innovation grant",
                Category = EventCategory.Investor,
                Weight = 0.6,
                CooldownDays = 730,
                MinDay = 365,
                Condition = gs => gs.UnlockedResearchIds.Count >= 3,
                Apply = (gs, rec) =>
                {
                    rec.Description = "🏛️ Innovation Norway awards grant for sustainable aquaculture R&D. 2M NOK non-dilutive funding.";
                    rec.BalanceChange = +2_000_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });
        }
    }
    #pragma warning restore CS0618
}