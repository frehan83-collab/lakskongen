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
        public static void RegisterAll(Storyteller storyteller, Finance.FinanceManager? finance = null, Finance.TrafficLightSystem? trafficLight = null)
        {
            RegisterDiseaseEvents(storyteller, finance);
            RegisterWeatherEvents(storyteller, finance);
            RegisterMarketEvents(storyteller, finance);
            RegisterRegulatoryEvents(storyteller, finance, trafficLight);
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
        public static void SettleBalanceChange(Finance.FinanceManager? finance, GameState gs, EventRecord rec)
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

        /// <summary>The first population that sits in a sea site's pens (null when none).</summary>
        internal static Biology.FishPopulation? FirstSeaPen(GameState gs)
            => gs.Populations.FirstOrDefault(p => gs.SeaSites.Any(s => s.Pens.Contains(p)));

        // ═══════════════════════════════════════════════════════════
        // DISEASE EVENTS (10 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterDiseaseEvents(Storyteller storyteller, Finance.FinanceManager? finance)
        {
            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "isa-outbreak",
                Name = "ILA-utbrudd",
                Category = EventCategory.Disease,
                Weight = 0.8,
                CooldownDays = 180,
                MinDay = 365, // Not in first year
                // ISA is a sea-pen disease: land-based RAS tanks are biosecure. The
                // cull used to take whatever population came first, which wiped a
                // RAS facility's whole stock.
                Condition = gs => FirstSeaPen(gs) != null,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Infeksiøs lakseanemi (ILA) påvist. Påbudt utslakting og to års brakklegging i det berørte området.";
                    rec.BalanceChange = -2_500_000m; // Catastrophic
                    SettleBalanceChange(finance, gs, rec);

                    // Cull the first sea pen: every fish is destroyed on the
                    // cull order, booked as an infectious-disease death (S2-05).
                    var culled = FirstSeaPen(gs);
                    if (culled != null)
                    {
                        foreach (var cohort in culled.Cohorts)
                            culled.RemoveDead(cohort, cohort.Count, Biology.MortalityCause.DiseaseCull);
                        culled.Cohorts.Clear();
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
                Name = "Utbrudd av pankreassykdom (PD)",
                Category = EventCategory.Disease,
                Weight = 1.2,
                CooldownDays = 90,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "PD-viruset sprer seg. Ventet dødelighet 10–20 %, og veksten går ned.";
                    rec.BalanceChange = -350_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "cms-outbreak",
                Name = "Utbrudd av hjertesprekk (CMS)",
                Category = EventCategory.Disease,
                Weight = 1.0,
                CooldownDays = 120,
                Condition = gs => gs.Day > 365 && gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Hjertesprekk (CMS) påvist i fisken i sjøen. Betennelse i hjertet gir 5–20 % tap.";
                    rec.BalanceChange = -280_000m;
                    // Phase 5.1: vaccination coverage blunts CMS (no cure exists).
                    double vmult = Health.VaccinationProgram.OutbreakCostMult(gs,
                        Core.CrewEffects.HasRole(gs, "veterinaer"));
                    if (vmult < 1.0)
                    {
                        rec.BalanceChange = -(decimal)(280_000.0 * vmult);
                        rec.Description += " (Vaksinert bestand — tap begrenset.)";
                    }
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "agd-outbreak",
                Name = "Utbrudd av amøbegjellesykdom (AGD)",
                Category = EventCategory.Disease,
                Weight = 1.5,
                CooldownDays = 60,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Amøbegjellesykdom (AGD) fra varmere vann. Krever behandling med ferskvannsbad.";
                    rec.BalanceChange = -120_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "ihn-outbreak",
                Name = "IHN-virus påvist",
                Category = EventCategory.Disease,
                Weight = 0.6,
                CooldownDays = 180,
                MinDay = 180,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "IHN påvist i settefiskanlegget. Opptil 70 % dødelighet på yngelstadiet.";
                    rec.BalanceChange = -180_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "winter-ulcers",
                Name = "Utbrudd av vintersår",
                Category = EventCategory.Disease,
                Weight = 1.0,
                CooldownDays = 90,
                Condition = gs => LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) >= 300 || LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) <= 90, // Winter months
                Apply = (gs, rec) =>
                {
                    rec.Description = "Bakterieinfeksjon etter kuldestress. Sår i huden gir dårligere filetkvalitet.";
                    rec.BalanceChange = -90_000m;
                    // Phase 5.1: vaccination blunts winter ulcers.
                    double vmult = Health.VaccinationProgram.OutbreakCostMult(gs,
                        Core.CrewEffects.HasRole(gs, "veterinaer"));
                    if (vmult < 1.0)
                    {
                        rec.BalanceChange = -(decimal)(90_000.0 * vmult);
                        rec.Description += " (Vaksinert bestand — tap begrenset.)";
                    }
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "lice-infestation",
                Name = "Lakselusangrep",
                Category = EventCategory.Disease,
                Weight = 2.0,
                CooldownDays = 30,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Lusetallet er over grensen (0,5 voksne hunnlus per fisk). Behandling er påbudt.";
                    rec.BalanceChange = -75_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "disease-resistance-breakthrough",
                Name = "Gjennombrudd for sykdomsresistent stamme",
                Category = EventCategory.Other,
                Weight = 0.3,
                CooldownDays = 365,
                MinDay = 730, // After 2 years
                Condition = gs => gs.Day > 730,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Avlsprogrammet gir en ILA-resistent stamme. Sykdomstapene går ned med 40 %.";
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
                Name = "Ny vaksine tilgjengelig",
                Category = EventCategory.Other,
                Weight = 0.4,
                CooldownDays = 540,
                MinDay = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "En ny kombinasjonsvaksine gir bedre vern mot PD og CMS. Krever en engangsinvestering.";
                    rec.BalanceChange = -150_000m; // Cost
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "cleanerfish-success",
                Name = "Rensefisken virker",
                Category = EventCategory.Other,
                Weight = 0.5,
                CooldownDays = 180,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Leppefisk og rognkjeks holder lusa nede bedre enn ventet. Bruken av kjemiske midler faller med 60 %.";
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
                Name = "Kraftig storm skader merdene",
                Category = EventCategory.Weather,
                Weight = 1.0,
                CooldownDays = 120,
                Condition = gs => LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) >= 270 || LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) <= 60, // Oct-Mar storm season
                Apply = (gs, rec) =>
                {
                    rec.Description = "Stormen Berit skader merdene, og 5 000 fisk rømmer. Reparasjonskostnader og bøter.";
                    rec.BalanceChange = -420_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "algal-bloom-toxic",
                Name = "Giftig algeoppblomstring",
                Category = EventCategory.Weather,
                Weight = 0.8,
                CooldownDays = 180,
                Condition = gs => LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) >= 120 && LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) <= 240, // May-Sep
                Apply = (gs, rec) =>
                {
                    rec.Description = "Oppblomstring av Chrysochromulina-alger. Oksygensvikt gir 15 % dødelighet, og fôringen stanses.";
                    rec.BalanceChange = -580_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "jellyfish-swarm",
                Name = "Manetinvasjon",
                Category = EventCategory.Weather,
                Weight = 1.2,
                CooldownDays = 90,
                Condition = gs => LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) >= 180 && LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) <= 270, // Summer-autumn
                Apply = (gs, rec) =>
                {
                    rec.Description = "En stor maneteinvasjon tetter nøtene og stresser fisken. Nødslakting vurderes.";
                    rec.BalanceChange = -95_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "heatwave",
                Name = "Hetebølge varmer opp sjøen",
                Category = EventCategory.Weather,
                Weight = 0.6,
                CooldownDays = 365,
                Condition = gs => LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) >= 150 && LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) <= 210, // June-Aug
                Apply = (gs, rec) =>
                {
                    rec.Description = "Uvanlig høye temperaturer (16–18 °C). Veksten bremser, lusa formerer seg, og faren for AGD øker.";
                    rec.BalanceChange = -110_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "cold-snap",
                Name = "Ekstrem kuldeperiode",
                Category = EventCategory.Weather,
                Weight = 0.5,
                CooldownDays = 365,
                Condition = gs => LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) >= 330 || LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) <= 30, // Dec-Feb
                Apply = (gs, rec) =>
                {
                    rec.Description = "Arktisk kulde gir sjøtemperatur under 2 °C. Veksten stopper, og faren for vintersår er høy.";
                    rec.BalanceChange = -85_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "perfect-conditions",
                Name = "Perfekte vekstforhold",
                Category = EventCategory.Weather,
                Weight = 0.7,
                CooldownDays = 180,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Ideell temperatur (11–13 °C), god strøm og mye oksygen. Fôrfaktoren bedres, og veksten øker.";
                    rec.BalanceChange = +120_000m; // Better FCR savings
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "oxygen-depletion",
                Name = "Oksygensvikt",
                Category = EventCategory.Weather,
                Weight = 0.8,
                CooldownDays = 120,
                Condition = gs => LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) >= 180 && LakskongenSim.Core.SimCalendar.DayOfYear(gs.Day) <= 270,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Svak strøm og høy biomasse gir oksygen under 5 mg/L. Nødlufting er satt i gang.";
                    rec.BalanceChange = -140_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "predator-attack",
                Name = "Selangrep",
                Category = EventCategory.Other,
                Weight = 1.0,
                CooldownDays = 60,
                // Phase 1.5: fully deterrent-protected farms are skipped.
                Condition = gs => gs.Populations.Any() && gs.SeaSites
                    .Where(s => s.Pens.Any(p => p.TotalCount > 0))
                    .Any(s => !s.HasSealDeterrent),
                Apply = (gs, rec) =>
                {
                    // Damage scales with the unprotected share of stock.
                    var stocked = gs.SeaSites.Where(s => s.Pens.Any(p => p.TotalCount > 0)).ToList();
                    int total = stocked.SelectMany(s => s.Pens).Sum(p => p.TotalCount);
                    int unprotected = stocked.Where(s => !s.HasSealDeterrent).SelectMany(s => s.Pens).Sum(p => p.TotalCount);
                    double share = total > 0 ? (double)unprotected / total : 1.0;
                    rec.Description = "Sel bryter gjennom nøtene og dreper fisk. Notskader og stressrelatert dødelighet."
                        + (share < 1.0 ? $" (Deterrents protected {1.0 - share:P0} of stock.)" : "");
                    rec.BalanceChange = -(decimal)(65_000.0 * share);
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
                Name = "Etterspørselen fra Kina skyter fart",
                Category = EventCategory.Market,
                Weight = 0.8,
                CooldownDays = 180,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Kinesisk nyttår gir en topp i etterspørselen: prisene stiger 25 % i to uker. Gode tider for høsting.";
                    rec.BalanceChange = +300_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "chile-collapse",
                Name = "Produksjonskollaps i Chile",
                Category = EventCategory.Market,
                Weight = 0.5,
                CooldownDays = 365,
                MinDay = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Algeoppblomstring ødelegger lakseoppdrett i Chile. Verdenstilbudet strammes inn, og prisene stiger kraftig.";
                    rec.BalanceChange = +450_000m; // Windfall
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "brexit-tariffs",
                Name = "Storbritannia innfører toll",
                Category = EventCategory.Market,
                Weight = 0.6,
                CooldownDays = 730,
                MinDay = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Toll på norsk laks etter brexit. Adgangen til det britiske markedet blir dårligere.";
                    rec.BalanceChange = -180_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "us-salmon-trend",
                Name = "Sushiboom i USA",
                Category = EventCategory.Market,
                Weight = 0.7,
                CooldownDays = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Sushitrend i USA: etterspørselen etter laks i sashimikvalitet øker 40 %, og prisene stiger.";
                    rec.BalanceChange = +220_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "nok-weakens",
                Name = "Kronen svekkes",
                Category = EventCategory.Market,
                Weight = 1.0,
                CooldownDays = 180,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Kronen faller mot euro og dollar. Eksportinntektene øker med 12 %.";
                    rec.BalanceChange = +160_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "oversupply-crash",
                Name = "Overskudd i markedet",
                Category = EventCategory.Market,
                Weight = 0.9,
                CooldownDays = 180,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Rekordstor norsk slakting oversvømmer markedet. Prisen faller til 48 kr/kg.";
                    rec.BalanceChange = -280_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "asc-premium",
                Name = "ASC-premien øker",
                Category = EventCategory.Market,
                Weight = 0.5,
                CooldownDays = 365,
                Condition = gs => gs.HasASCCertification,
                Apply = (gs, rec) =>
                {
                    rec.Description = "En stor amerikansk dagligvarekjede kjøper mer norsk laks. ASC-premien er nå +12 % mot standard.";
                    rec.BalanceChange = +95_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "contract-secured",
                Name = "Langsiktig kontrakt sikret",
                Category = EventCategory.Market,
                Weight = 0.6,
                CooldownDays = 540,
                MinDay = 365,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Treårskontrakt med en stor europeisk dagligvarekjede er i havn. Stabile priser er sikret.";
                    rec.BalanceChange = 0m; // Future benefit
                    gs.ContractActive = true;
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        // REGULATORY EVENTS (6 events)
        // ═══════════════════════════════════════════════════════════

        private static void RegisterRegulatoryEvents(Storyteller storyteller, Finance.FinanceManager? finance, Finance.TrafficLightSystem? trafficLight = null)
        {
            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "traffic-light-green",
                Name = "Området oppgradert til GRØNT",
                Category = EventCategory.Regulation,
                Weight = 0.5,
                CooldownDays = 730,
                MinDay = 730,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Området ditt får grønt lys. MTB kan økes med 6 %.";
                    rec.BalanceChange = 0m;

                    // Gated (default off): with no TrafficLightSystem, or gate
                    // off, this is the exact legacy ×1.06 loop — default
                    // outcomes identical. Gate on routes through the machine.
                    (trafficLight ?? new Finance.TrafficLightSystem()).ApplyGreenStatus(gs);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "traffic-light-red",
                Name = "Området nedgradert til RØDT",
                Category = EventCategory.Regulation,
                Weight = 0.6,
                CooldownDays = 730,
                MinDay = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Lusepresset gir rødt lys i området. MTB må reduseres med 6 %.";
                    rec.BalanceChange = -320_000m; // Production loss
                    SettleBalanceChange(finance, gs, rec);

                    // Gated: legacy leaves MAB untouched (fine only). Gate on
                    // additionally tracks RED with the symmetric ×0.94 MAB cut.
                    trafficLight?.ApplyRedStatus(gs);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "escape-penalty",
                Name = "Bot for rømming",
                Category = EventCategory.Regulation,
                Weight = 0.8,
                CooldownDays = 180,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Notsvikt: 12 000 fisk har rømt. Bot fra Fiskeridirektoratet og kostnader til gjenfangst.";
                    rec.BalanceChange = -550_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "resource-rent-tax",
                Name = "Grunnrenteskatt innført",
                Category = EventCategory.Regulation,
                Weight = 0.3,
                CooldownDays = 1825,
                MinDay = 1095, // After 3 years
                Condition = gs => gs.Balance > 5_000_000m, // Only if profitable
                Apply = (gs, rec) =>
                {
                    rec.Description = "Regjeringen innfører 40 % grunnrenteskatt på overskudd over 4 millioner kroner i året.";
                    rec.BalanceChange = -800_000m; // Retroactive
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "development-license",
                Name = "Utviklingstillatelse tildelt",
                Category = EventCategory.Regulation,
                Weight = 0.4,
                CooldownDays = 1095,
                MinDay = 730,
                Condition = gs => gs.Balance > 2_000_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Utviklingstillatelse tildelt for forsøk med lukket oppdrett. Lykkes innovasjonen, kan den gjøres om til en ordinær tillatelse.";
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
                Name = "Velferdstilsyn fra Mattilsynet",
                Category = EventCategory.Regulation,
                Weight = 1.0,
                CooldownDays = 180,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Uanmeldt velferdstilsyn. Brudd på tetthetsreglene ble funnet, og du har fått en advarsel.";
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
                Name = "Motorstopp på brønnbåten",
                Category = EventCategory.Equipment,
                Weight = 1.2,
                CooldownDays = 120,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Brønnbåten fikk motorstopp under slaktetransporten. Nødreparasjon og forsinket levering.";
                    rec.BalanceChange = -185_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "rov-malfunction",
                Name = "Feil på undervannsdronen",
                Category = EventCategory.Equipment,
                Weight = 1.0,
                CooldownDays = 90,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Undervannsdronen svikter. Nøtene må inspiseres manuelt, og lønnskostnadene øker.";
                    rec.BalanceChange = -95_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "feed-barge-collision",
                Name = "Fôrflåten kolliderer",
                Category = EventCategory.Equipment,
                Weight = 0.6,
                CooldownDays = 180,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Fôrflåten kolliderer med en merd i dårlig vær. Notskader, og 2 000 fisk rømmer.";
                    rec.BalanceChange = -320_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "net-damage",
                Name = "Notskader oppdaget",
                Category = EventCategory.Equipment,
                Weight = 1.3,
                CooldownDays = 60,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "En rutineinspeksjon avdekker mye slitasje og små hull i nøtene. De må skiftes straks.";
                    rec.BalanceChange = -145_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "sensor-failure",
                Name = "Sensorsvikt",
                Category = EventCategory.Equipment,
                Weight = 0.9,
                CooldownDays = 90,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Oksygen- og temperatursensorene svikter. Et oksygenfall ble ikke varslet i tide, og det ga tap.";
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
                Name = "Miljøorganisasjoner protesterer",
                Category = EventCategory.Community,
                Weight = 0.7,
                CooldownDays = 180,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Miljøorganisasjoner protesterer mot lusepåvirkningen på villaksen. Mediedekningen skader omdømmet.";
                    rec.BalanceChange = -125_000m; // Lost contracts + PR costs
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "local-community-support",
                Name = "Støtte fra lokalsamfunnet",
                Category = EventCategory.Community,
                Weight = 0.8,
                CooldownDays = 365,
                MinDay = 365,
                Condition = gs => gs.Balance > 1_000_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Kommunen roser arbeidsplassene og skatteinntektene du bidrar med. Det blir lettere å fornye tillatelser og å få godkjent utvidelser.";
                    rec.BalanceChange = +85_000m; // Reduced bureaucracy costs
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "fjord-access-dispute",
                Name = "Strid om fjordbruken",
                Category = EventCategory.Community,
                Weight = 0.6,
                CooldownDays = 540,
                Condition = gs => gs.SeaSites.Count >= 2,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Det lokale fiskerlaget utfordrer retten din til å bruke fjorden. Advokatkostnader og mulig flytting av lokaliteten.";
                    rec.BalanceChange = -210_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "tourism-conflict",
                Name = "Konflikt med reiselivet",
                Category = EventCategory.Community,
                Weight = 0.5,
                CooldownDays = 365,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Reiselivsaktører klager på at merdene ødelegger fjordutsikten. Kommunalt høringsmøte og et kompromiss kreves.";
                    rec.BalanceChange = -95_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "school-visit-pr",
                Name = "Skolebesøk gir god omtale",
                Category = EventCategory.Community,
                Weight = 0.9,
                CooldownDays = 180,
                Condition = gs => gs.SeaSites.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "Omvisninger for lokale skoler gir god omtale. Historien «fra merd til middagsbord» styrker merkevaren.";
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
                Name = "Konkurs hos en konkurrent",
                Category = EventCategory.Competitor,
                Weight = 0.4,
                CooldownDays = 730,
                MinDay = 365,
                Condition = gs => gs.Balance > 1_500_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "En lokal konkurrent går konkurs. Vil du kjøpe tillatelsene og lokalitetene med 60 % rabatt?";
                    rec.BalanceChange = -1_200_000m; // Acquisition cost
                    SettleBalanceChange(finance, gs, rec);
                    gs.AcquisitionCount++;
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "ma-offer",
                Name = "Oppkjøpstilbud",
                Category = EventCategory.Competitor,
                Weight = 0.3,
                CooldownDays = 1095,
                MinDay = 730,
                Condition = gs => gs.Balance > 3_000_000m && gs.SeaSites.Count >= 2,
                Apply = (gs, rec) =>
                {
                    rec.Description = "En av de store konkurrentene vil kjøpe selskapet ditt. God pris, men du mister selvstendigheten.";
                    rec.BalanceChange = +2_500_000m; // Buyout windfall
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "tech-leapfrog",
                Name = "Konkurrent tar teknologisprang",
                Category = EventCategory.Competitor,
                Weight = 0.6,
                CooldownDays = 365,
                MinDay = 540,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "En konkurrent tar i bruk et banebrytende lukket anlegg. De tradisjonelle merdene dine virker gammeldagse for kjøperne.";
                    rec.BalanceChange = -180_000m; // Lost premium contracts
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "price-war",
                Name = "Priskrig",
                Category = EventCategory.Competitor,
                Weight = 0.8,
                CooldownDays = 180,
                Condition = gs => gs.Populations.Any(),
                Apply = (gs, rec) =>
                {
                    rec.Description = "De store produsentene oversvømmer markedet for å presse ut de små. Prisen faller til 42 kr/kg.";
                    rec.BalanceChange = -350_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "competitor-scandal",
                Name = "Skandale hos konkurrenten",
                Category = EventCategory.Competitor,
                Weight = 0.5,
                CooldownDays = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "En stor konkurrent er tatt for ulovlige plantevernmidler. Kjøperne flytter bestillingene til det sertifiserte anlegget ditt.";
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
                Name = "ESG-fond vil investere",
                Category = EventCategory.Investor,
                Weight = 0.5,
                CooldownDays = 540,
                MinDay = 730,
                Condition = gs => gs.HasASCCertification && gs.Balance > 1_500_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Et nordisk ESG-fond tilbyr vekstkapital til bærekraftig utvidelse: 5 millioner kroner for 15 % av aksjene.";
                    rec.BalanceChange = +5_000_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "pe-buyout-offer",
                Name = "Oppkjøpstilbud fra oppkjøpsfond",
                Category = EventCategory.Investor,
                Weight = 0.3,
                CooldownDays = 1095,
                MinDay = 1095,
                Condition = gs => gs.Balance > 5_000_000m && gs.AcquisitionCount >= 1,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Et oppkjøpsfond byr åtte ganger driftsresultatet (EBITDA). Pensjonere deg rik eller fortsette å bygge imperiet?";
                    rec.BalanceChange = +8_000_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "ipo-opportunity",
                Name = "Mulighet for børsnotering",
                Category = EventCategory.Investor,
                Weight = 0.2,
                CooldownDays = 1825,
                MinDay = 1460,
                Condition = gs => gs.Balance > 10_000_000m && gs.SeaSites.Count >= 3,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Børsvinduet står åpent. Børsnoter selskapet, hent kapital til vekst — og lev med kvartalspresset.";
                    rec.BalanceChange = +12_000_000m; // IPO proceeds
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "angel-investor",
                Name = "Engleinvestor vil bidra",
                Category = EventCategory.Investor,
                Weight = 0.7,
                CooldownDays = 365,
                MinDay = 180,
                Condition = gs => gs.Balance < 2_000_000m && gs.Balance > 500_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "En formuende sjømatveteran tilbyr veiledning og 1,5 millioner kroner for 10 % av aksjene.";
                    rec.BalanceChange = +1_500_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });

            storyteller.RegisterEvent(new EventDefinition
            {
                Id = "innovation-grant",
                Name = "Statlig innovasjonstilskudd",
                Category = EventCategory.Investor,
                Weight = 0.6,
                CooldownDays = 730,
                MinDay = 365,
                Condition = gs => gs.UnlockedResearchIds.Count >= 3,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Innovasjon Norge gir tilskudd til forskning på bærekraftig havbruk: 2 millioner kroner uten å avgi eierandeler.";
                    rec.BalanceChange = +2_000_000m;
                    SettleBalanceChange(finance, gs, rec);
                }
            });
        }
    }
    #pragma warning restore CS0618
}