using System;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Data;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Delousing treatment system with multiple method types, efficacy modeling,
    /// treatment mortality risk, resistance tracking, and regulatory compliance.
    /// Norwegian regulations: mandatory treatment above 0.5 adult female lice/fish.
    /// </summary>
    public class DelousingSystem
    {
        // ─── Treatment methods and their characteristics ───
        // Based on Norwegian aquaculture industry data

        /// <summary>Get treatment parameters for a given method.</summary>
        public static TreatmentProfile GetProfile(DelousingMethod method) => method switch
        {
            DelousingMethod.HydrogenPeroxide => new TreatmentProfile
            {
                Method = method,
                Efficacy = 0.70,          // Declining due to resistance
                MortalityRisk = 0.005,    // 0.5% treatment mortality
                CostPerFish = 1.5m,       // NOK
                DurationDays = 1,
                RequiresWellboat = false,
                ResistanceRiskIncrease = 0.03, // High resistance development
                WelfareImpact = 0.15,     // Moderate stress
                MinTemperatureC = 4.0,
                MaxTemperatureC = 14.0
            },
            DelousingMethod.Thermolicer => new TreatmentProfile
            {
                Method = method,
                Efficacy = 0.95,
                MortalityRisk = 0.035,    // 3-5% — highest mortality risk
                CostPerFish = 3.0m,
                DurationDays = 1,
                RequiresWellboat = true,
                ResistanceRiskIncrease = 0.0, // No resistance (mechanical)
                WelfareImpact = 0.30,     // High stress
                MinTemperatureC = 6.0,
                MaxTemperatureC = 18.0
            },
            DelousingMethod.Hydrolicer => new TreatmentProfile
            {
                Method = method,
                Efficacy = 0.85,
                MortalityRisk = 0.015,    // 1-2% mortality
                CostPerFish = 2.5m,
                DurationDays = 1,
                RequiresWellboat = true,
                ResistanceRiskIncrease = 0.0,
                WelfareImpact = 0.20,
                MinTemperatureC = 4.0,
                MaxTemperatureC = 18.0
            },
            DelousingMethod.CleanerFish => new TreatmentProfile
            {
                Method = method,
                Efficacy = 0.50,          // Variable, depends on species/season
                MortalityRisk = 0.001,    // Minimal
                CostPerFish = 5.0m,       // High upfront (wrasse/lumpfish)
                DurationDays = 30,        // Continuous effect
                RequiresWellboat = false,
                ResistanceRiskIncrease = 0.0,
                WelfareImpact = 0.02,     // Very low stress
                MinTemperatureC = 4.0,
                MaxTemperatureC = 16.0
            },
            DelousingMethod.LaserDelousing => new TreatmentProfile
            {
                Method = method,
                Efficacy = 0.90,
                MortalityRisk = 0.002,    // Very low
                CostPerFish = 8.0m,       // Premium technology
                DurationDays = 1,
                RequiresWellboat = false,
                ResistanceRiskIncrease = 0.0,
                WelfareImpact = 0.05,     // Minimal stress
                MinTemperatureC = 2.0,
                MaxTemperatureC = 20.0
            },
            DelousingMethod.FreshwaterBath => new TreatmentProfile
            {
                Method = method,
                Efficacy = 0.80,          // Also treats AGD
                MortalityRisk = 0.01,
                CostPerFish = 2.0m,
                DurationDays = 1,
                RequiresWellboat = true,
                ResistanceRiskIncrease = 0.01,
                WelfareImpact = 0.10,
                MinTemperatureC = 4.0,
                MaxTemperatureC = 16.0
            },
            DelousingMethod.MedicatedFeed => new TreatmentProfile
            {
                Method = method,
                Efficacy = 0.60,          // Declining significantly
                MortalityRisk = 0.001,
                CostPerFish = 1.0m,
                DurationDays = 7,         // Week-long treatment
                RequiresWellboat = false,
                ResistanceRiskIncrease = 0.05, // Highest resistance risk
                WelfareImpact = 0.05,
                MinTemperatureC = 2.0,
                MaxTemperatureC = 20.0
            },
            DelousingMethod.LiceSkirts => new TreatmentProfile
            {
                Method = method,
                Efficacy = 0.40,          // Preventive only
                MortalityRisk = 0.0,
                CostPerFish = 0.5m,       // Low per-fish cost
                DurationDays = 90,        // Seasonal deployment
                RequiresWellboat = false,
                ResistanceRiskIncrease = 0.0,
                WelfareImpact = 0.01,
                MinTemperatureC = 0.0,
                MaxTemperatureC = 25.0
            },
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };

        // ─── Events ───
        public event Action<decimal>? OnTreatmentCost;
        public event Action<int>? OnTreatmentMortality;
        public event Action<string>? OnTreatmentEvent;

        /// <summary>
        /// Apply delousing treatment to a population.
        /// Returns treatment result with mortality, cost, and efficacy data.
        /// The lice model is company-wide: pass applyCompanyLiceKill=true for
        /// exactly ONE population per treatment operation so a multi-pen
        /// order does not nuke global lice N times (S8).
        /// </summary>
        public TreatmentResult Treat(Biology.FishPopulation pop, DelousingMethod method,
            Health.SeaLiceModel liceModel, Health.IntegratedDiseaseSystem diseaseSystem,
            double temperatureC = 10.0, double mortalityMultiplier = 1.0,
            bool applyCompanyLiceKill = true, SimRngStream? rng = null)
        {
            var profile = GetProfile(method);
            int totalFish = pop.Cohorts.Sum(c => c.Count);

            // Temperature check
            if (temperatureC < profile.MinTemperatureC || temperatureC > profile.MaxTemperatureC)
            {
                OnTreatmentEvent?.Invoke($"⚠️ Cannot use {method} at {temperatureC:F1}°C (range: {profile.MinTemperatureC}-{profile.MaxTemperatureC}°C)");
                return TreatmentResult.Cancelled(method, "Temperature out of range");
            }

// Calculate actual efficacy (may be reduced by resistance)
            double actualEfficacy = profile.Efficacy;

            // Temperature modifier on chemical treatments
            if (method == DelousingMethod.HydrogenPeroxide || method == DelousingMethod.MedicatedFeed)
            {
                double tempModifier = 1.0 - Math.Abs(temperatureC - 10.0) * 0.02;
                actualEfficacy *= Math.Max(0.5, tempModifier);
            }

            string treatmentType = method switch
            {
                DelousingMethod.HydrogenPeroxide => "hydrogen_peroxide",
                DelousingMethod.MedicatedFeed => "organophosphate",
                DelousingMethod.Thermolicer => "thermal",
                DelousingMethod.Hydrolicer => "mechanical",
                DelousingMethod.CleanerFish => "cleaner_fish",
                DelousingMethod.LaserDelousing => "laser",
                DelousingMethod.FreshwaterBath => "freshwater",
                DelousingMethod.LiceSkirts => "preventive",
                _ => "mechanical"
            };

            // Apply lice reduction: the kill is the method's efficacy x
            // (1 - resistance), once, and only for the flagged population —
            // the model is company-wide (S8). Normalising the efficacy by the
            // model's per-type base capped Laser at 0.50 and Thermolicer and
            // Hydrolicer at 0.82 (F480).
            double efficacyResult = 0.0;
            if (applyCompanyLiceKill)
            {
                efficacyResult = liceModel.ApplyTreatmentEfficacy(treatmentType, actualEfficacy);
            }
            else
            {
                efficacyResult = actualEfficacy;
            }

            // Treatment mortality (veterinarian supervision scales it).
            // P4: handling harm compounds on repeat treatments — the harm
            // model's immediate rate (handling + injury-linked) floors the
            // profile rate, so back-to-back delousings kill extra fish.
            double profileRate = profile.MortalityRisk * (1.0 + (temperatureC - 10.0) * 0.05) * mortalityMultiplier;
            var harm = Health.DelousingHarmModel.EvaluateFor(method.ToString(), actualEfficacy);
            pop.TreatmentHarm = Health.DelousingHarmModel.ApplyTreatment(
                pop.TreatmentHarm, Health.DelousingHarmModel.MethodFor(method.ToString()), actualEfficacy);
            double actualMortalityRate = Math.Max(0, Math.Max(profileRate, harm.TotalMortalityContribution));
            // Stochastic handling deaths from the per-site treatment stream
            // (SimRngStreams.Delouse). Mean-preserving: expected deaths equal
            // the deterministic value; null stream keeps legacy behavior.
            int deaths = rng != null
                ? DrawBinomialDeaths(rng, totalFish, Math.Clamp(actualMortalityRate, 0.0, 1.0))
                : (int)(totalFish * actualMortalityRate);

            // Apply mortality to cohorts
            if (deaths > 0)
            {
                int remaining = deaths;
                for (int i = 0; i < pop.Cohorts.Count && remaining > 0; i++)
                {
                    var cohort = pop.Cohorts[i];
                    int take = Math.Min(remaining, (int)(cohort.Count * actualMortalityRate));
                    remaining -= take;
                    // In place: rebuilding the cohort reset its smolt quality tier
                    // to Standard (F386).
                    cohort.Count -= take;
                }
                pop.Cohorts.RemoveAll(c => c.Count <= 0);
            }

            // Update disease system — treatment affects disease prevalence.
            // The per-population infected-fraction bookkeeping always runs;
            // the company-wide lice kill never does here (applied above, once).
            if (diseaseSystem != null)
            {
                diseaseSystem.ApplyTreatment(pop, "mechanical", actualEfficacy, actualEfficacy * 0.5, temperatureC, applyLiceModel: false);
            }

            // Mark population as recently treated (for resistance evolution)
            pop.HasRecentTreatment = true;
            pop.TreatmentIntensity = efficacyResult;

            // Calculate cost
            decimal totalCost = totalFish * profile.CostPerFish;

            // Fire events
            OnTreatmentCost?.Invoke(totalCost);
            if (deaths > 0) OnTreatmentMortality?.Invoke(deaths);
            OnTreatmentEvent?.Invoke($"🐟 {method}: Treated {totalFish} fish, {deaths} deaths ({actualMortalityRate:P1}), efficacy {efficacyResult:P0}, cost {totalCost:N0} NOK");

            return new TreatmentResult
            {
                Method = method,
                FishTreated = totalFish,
                Deaths = deaths,
                Efficacy = efficacyResult,
                Cost = totalCost,
                Success = true,
                Message = $"Treatment complete: {actualEfficacy:P0} lice reduction",
                GillDamage = harm.GillDamageContribution,
                HarmWelfareCost = harm.WelfareCost
            };
        }

        /// <summary>
        /// Map UI/legacy method names to <see cref="DelousingMethod"/> (Phase 2.2).
        /// Owner of the method-name contract since Phase 1 wave 2 — the Godot
        /// bridge delegates to this instead of keeping its own table.
        /// </summary>
        /// <summary>
        /// Mean-preserving binomial draw. Exact Bernoulli loop for small means,
        /// normal approximation (Box-Muller) for large ones where looping up
        /// to hundreds of thousands of fish per pen would stall the tick.
        /// </summary>
        private static int DrawBinomialDeaths(SimRngStream rng, int n, double p)
        {
            if (n <= 0 || p <= 0.0) return 0;
            if (p >= 1.0) return n;
            double mean = n * p;
            if (mean < 25.0)
            {
                int deaths = 0;
                for (int i = 0; i < n; i++)
                    if (rng.NextDouble() < p) deaths++;
                return deaths;
            }
            double u1 = Math.Max(1e-10, rng.NextDouble());
            double u2 = rng.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            double approx = mean + Math.Sqrt(mean * (1.0 - p)) * z;
            return Math.Clamp((int)Math.Round(approx), 0, n);
        }

        public static bool TryParseMethodName(string name, out DelousingMethod method)
        {
            switch ((name ?? "").ToLower())
            {
                case "thermolicer":
                case "thermal": method = DelousingMethod.Thermolicer; return true;
                case "hydrolicer":
                case "mechanical": method = DelousingMethod.Hydrolicer; return true;
                case "cleanerfish":
                case "cleaner_fish":
                case "biological": method = DelousingMethod.CleanerFish; return true;
                case "laser":
                case "laserdelousing":
                case "stingray": method = DelousingMethod.LaserDelousing; return true;
                case "freshwater":
                case "freshwaterbath": method = DelousingMethod.FreshwaterBath; return true;
                case "peroxide":
                case "hydrogen_peroxide":
                case "chemical": method = DelousingMethod.HydrogenPeroxide; return true;
                case "medicatedfeed":
                case "medicated_feed": method = DelousingMethod.MedicatedFeed; return true;
                case "skirt":
                case "liceskirts": method = DelousingMethod.LiceSkirts; return true;
                default: method = default; return false;
            }
        }
    }

    /// <summary>Treatment method characteristics.</summary>
    public struct TreatmentProfile
    {
        public DelousingMethod Method;
        public double Efficacy;           // 0-1 lice reduction
        public double MortalityRisk;      // 0-1 fish death probability
        public decimal CostPerFish;       // NOK per fish
        public int DurationDays;          // Treatment duration
        public bool RequiresWellboat;
        public double ResistanceRiskIncrease; // Contribution to resistance development
        public double WelfareImpact;      // 0-1 welfare score reduction
        public double MinTemperatureC;
        public double MaxTemperatureC;
    }

    /// <summary>Result of a treatment operation.</summary>
    public struct TreatmentResult
    {
        public DelousingMethod Method;
        public int FishTreated;
        public int Deaths;
        public double Efficacy;
        public decimal Cost;
        public bool Success;
        public string Message;
        /// <summary>Gill damage added by this treatment (P4 harm model).</summary>
        public double GillDamage;
        /// <summary>Welfare cost scalar of this treatment (P4 harm model).</summary>
        public double HarmWelfareCost;

        public static TreatmentResult Cancelled(DelousingMethod method, string reason) => new TreatmentResult
        {
            Method = method, Success = false, Message = reason
        };
    }
}