using Godot;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Integration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
// Namespace aliases for the Phase 5/6 features (sludge, carbon, cohort transfers,
// scenarios) so qualified references like Farm.SludgeValorization resolve from
// this global-namespace file.
using Farm = LakskongenSim.Farm;
using Events = LakskongenSim.Events;
using Processing = LakskongenSim.Processing;
using Logistics = LakskongenSim.Logistics;

// GameBridge.Economy.cs — partial class slice (see project docs).
// <summary>
// Economy slice: finance readouts, the empire systems (competitors, progression,
// brand reputation, crises, vertical integration, acquisitions, licenses,
// factions), loans, tax, investors, and dashboard economy projections
// (P&L, cash flow, ROI, cost breakdown, production costs, advisor).
// </summary>
/// <summary>
/// GameBridge partial — finance readouts, the empire systems (competitors, progression, (moved verbatim from GameBridge.cs).
/// </summary>
public partial class GameBridge : Node
{
    // ── Finance API ───────────────────────────────────────────────────────────

/// <summary>
    /// Total accumulated P&L revenue (Phase 1 wave 3: was the Balance mirror —
    /// the UI called a bank balance "revenue"). Harvests, insurance payouts and
    /// sludge valorization now maintain FinancialStatements.Revenue (D1).
    /// </summary>
    public double Revenue => (double)(SimulationEngine?.FinancialStatements?.Revenue ?? 0m);

    /// <summary>
    /// Total accumulated P&L expenses: COGS (feed, now booked in the engine's
    /// feed-cost path) + operating expenses. Deliberate game change: these
    /// used to read 0.0 and (Balance) respectively, which made IPO gates and
    /// profit displays lie.
    /// </summary>
    public double Expenses
    {
        get
        {
            var fs = SimulationEngine?.FinancialStatements;
            if (fs == null) return 0.0;
            return (double)(fs.COGS + fs.OperatingExpenses);
        }
    }

    /// <summary>Current profit (Revenue - Expenses).</summary>
    public double Profit => Revenue - Expenses;

    /// <summary>
    /// Causal P&L story (Phase 5.5 "feel" layer): the dominant cost drivers and
    /// revenue sources behind the current profit/loss, so the UI can answer
    /// "why did I lose money" with the top 2–3 causes.
    /// Keys: "net_income", "top_costs" (Array of {category, amount, share}),
    ///       "top_revenue" (Array of {category, amount, share}).
    /// </summary>
    public Godot.Collections.Dictionary GetProfitLossStory(int topN = 3)
    {
        var result = new Godot.Collections.Dictionary();
        var fs = SimulationEngine?.FinancialStatements;
        if (fs == null)
        {
            result["net_income"] = 0.0;
            result["top_costs"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            result["top_revenue"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            return result;
        }

        result["net_income"] = (double)fs.ComputeNetIncome();

        decimal totalCost = fs.COGS + fs.OperatingExpenses + fs.InterestExpense + fs.Taxes;
        var costs = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var (category, amount) in fs.GetTopCostDrivers(topN))
        {
            double share = totalCost > 0 ? (double)(amount / totalCost) : 0.0;
            costs.Add(new Godot.Collections.Dictionary
            {
                ["category"] = category,
                ["amount"] = (double)amount,
                ["share"] = share
            });
        }
        result["top_costs"] = costs;

        decimal totalRevenue = fs.Revenue;
        var revenues = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var (category, amount) in fs.GetTopRevenueSources(topN))
        {
            double share = totalRevenue > 0 ? (double)(amount / totalRevenue) : 0.0;
            revenues.Add(new Godot.Collections.Dictionary
            {
                ["category"] = category,
                ["amount"] = (double)amount,
                ["share"] = share
            });
        }
        result["top_revenue"] = revenues;

        return result;
    }



    // ── Empire Systems API (T65) ──────────────────────────────────────────────

    // ── CompetitorAI Methods ───────────────────────────────────────────────────

    /// <summary>
    /// Return all AI competitors as an Array of Dictionaries.
    /// Keys: "id", "name", "parody_of", "strategy", "positioning", "revenue", "market_share_norway".
    /// </summary>
    // NOTE: CompetitorAI, EmpireProgression, StockMarket methods stubbed
    // until the simulation engine implements the required interfaces.
    // These are advanced features — core gameplay works without them.

    public Godot.Collections.Array<Godot.Collections.Dictionary> GetCompetitors()
    {
        return new Godot.Collections.Array<Godot.Collections.Dictionary>();
    }

    public int GetCompetitorCount() => 0;

    public Godot.Collections.Dictionary GetMarketLeader()
    {
        return new Godot.Collections.Dictionary();
    }

    // ── EmpireProgression Methods (stubbed) ────────────────────────────────────

    public string GetEmpirePhase() => "SmallFarmer";
    public int GetEmpireScore() => 0;

    public Godot.Collections.Dictionary GetNextMilestone()
    {
        return new Godot.Collections.Dictionary();
    }

    public string GetGrowthStrategy() => "Organic";

    // ── BrandReputation Methods ────────────────────────────────────────────────

    /// <summary>Overall brand reputation score (0-100).</summary>
    public double GetBrandScore()
    {
        return (double)(SimulationEngine?.BrandReputation?.OverallScore ?? 50m);
    }

    /// <summary>Current brand strategy (Commodity, Organic, Premium, Luxury, Regional).</summary>
    public string GetBrandStrategy()
    {
        return SimulationEngine?.BrandReputation?.Strategy.ToString() ?? "Commodity";
    }

    /// <summary>
    /// Return active certifications as an Array of strings.
    /// Examples: "ASC", "MSC", "Organic", "GlobalGAP".
    /// </summary>
    public Godot.Collections.Array<string> GetCertifications()
    {
        var result = new Godot.Collections.Array<string>();
        var brand = SimulationEngine?.BrandReputation;
        if (brand == null) return result;

        foreach (var cert in brand.Certifications)
            result.Add(cert);
        return result;
    }

    /// <summary>Pricing power multiplier from brand reputation (1.0 = baseline, >1.0 = premium).</summary>
    public double GetPricingPower()
    {
        var brand = SimulationEngine?.BrandReputation;
        if (brand == null) return 1.0;
        
        // Return pricing power for FiletFersk as a baseline (FilletPremium not yet in ProductType)
        try { return (double)brand.GetPricingPowerMultiplier(LakskongenSim.Market.ProductType.Fillet); }
        catch { return 1.0; }
    }

    // ── CrisisCapitalism Methods ───────────────────────────────────────────────

    /// <summary>
    /// Return active competitor crises as an Array of Dictionaries.
    /// Keys: "id", "type", "competitor_name", "severity", "discount_multiplier".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveCrises()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var system = SimulationEngine?.CrisisCapitalism;
        if (system == null) return result;

        foreach (var crisis in system.GetActiveCrises(SimulationEngine.State.Day))
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = crisis.Id,
                ["title"] = $"{crisis.CompetitorName}: {CrisisTypeLabel(crisis.Type)}",
                ["description"] = $"{crisis.DurationMonths} måneder, eiendeler selges med " +
                    $"{crisis.DiscountMultiplier:P0} rabatt.",
                ["type"] = crisis.Type.ToString(),
                ["competitor_name"] = crisis.CompetitorName,
                ["severity"] = (double)crisis.SeverityScore,
                ["discount_multiplier"] = (double)crisis.DiscountMultiplier,
                ["duration_months"] = crisis.DurationMonths
            });
        }
        return result;
    }

    private static string CrisisTypeLabel(LakskongenSim.Events.CrisisType type) => type switch
    {
        LakskongenSim.Events.CrisisType.BANKRUPTCY => "konkurs",
        LakskongenSim.Events.CrisisType.DISEASE_OUTBREAK => "sykdomsutbrudd",
        LakskongenSim.Events.CrisisType.REGULATORY_SHUTDOWN => "pålagt stans",
        LakskongenSim.Events.CrisisType.MARKET_CRASH => "markedskrakk",
        LakskongenSim.Events.CrisisType.ENVIRONMENTAL_SCANDAL => "miljøskandale",
        _ => type.ToString()
    };

    /// <summary>
    /// Distressed assets on the market, in the empire panel's shape: "id",
    /// "title", "description", "price" (the discounted asking price), plus
    /// "seller" and "months_remaining". Was an always-empty stub; the listings
    /// come from rivals' bankruptcies (review F499).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAcquisitionOpportunities()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var system = SimulationEngine?.CrisisCapitalism;
        if (system == null) return result;
        foreach (var a in system.GetAllAvailableDistressedAssets(SimulationEngine!.State.Day))
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = a.Id,
                ["title"] = a.Name,
                ["description"] = $"{a.Description} Selger: {a.OwningCompetitor}. " +
                    $"{a.DiscountPercentage:P0} under verdi, {a.TimeRemainingMonths} mnd igjen.",
                ["price"] = (double)a.DiscountedPriceNOK,
                ["seller"] = a.OwningCompetitor,
                ["months_remaining"] = a.TimeRemainingMonths,
            });
        }
        return result;
    }

    /// <summary>Buy a distressed asset (delegates to <see cref="AcquireDistressedAsset"/>; was a stub).</summary>
    public Godot.Collections.Dictionary AttemptAcquisition(string assetId)
        => AcquireDistressedAsset(assetId);

    // ── VerticalIntegration Methods ────────────────────────────────────────────

    /// <summary>
    /// Return all integration tiers with ownership status as an Array of Dictionaries.
    /// Keys: "tier", "name", "is_owned", "capex", "cost_saving_pct", "quality_bonus".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetIntegrationTiers()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var system = SimulationEngine?.VerticalIntegration;
        if (system == null) return result;

        foreach (var asset in system.GetBuildableAssets())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["tier"] = asset.Tier.ToString(),
                ["name"] = asset.Name,
                ["is_owned"] = asset.IsOwned,
                ["capex"] = (double)asset.CapexNOK,
                ["annual_opex"] = (double)asset.AnnualOpexNOK,
                ["cost_saving_pct"] = (double)asset.CostSavingPctPerKg,
                ["quality_bonus"] = (double)asset.QualityBonus,
                ["description"] = asset.Description
            });
        }
        return result;
    }

    /// <summary>
    /// Return owned integration assets as an Array of Dictionaries.
    /// Keys: "tier", "name", "capacity", "maintenance_cost_pct".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetOwnedAssets()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var system = SimulationEngine?.VerticalIntegration;
        if (system == null) return result;

        foreach (var asset in system.GetOwnedAssets())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["tier"] = asset.Tier.ToString(),
                ["name"] = asset.Name,
                ["capacity"] = asset.Capacity,
                ["maintenance_cost_pct"] = (double)asset.MaintenanceCostPctPerYear,
                ["construction_completed"] = asset.ConstructionCompleted?.ToString() ?? "N/A"
            });
        }
        return result;
    }

    /// <summary>Overall vertical integration score (0.0-1.0).</summary>
    public double GetIntegrationScore()
    {
        return (double)(SimulationEngine?.VerticalIntegration?.GetIntegrationScore() ?? 0.0f);
    }

    /// <summary>
    /// Attempt to purchase/build a vertical integration asset by ID.
    /// Returns a Dictionary with keys: "success", "message", "capex_paid", "payback_years".
    /// </summary>
    public Godot.Collections.Dictionary PurchaseAsset(string assetId)
    {
        var result = new Godot.Collections.Dictionary();
        var system = SimulationEngine?.VerticalIntegration;
        if (system == null)
        {
            result["success"] = false;
            result["message"] = "Vertical integration system not initialized";
            return result;
        }

        // Find the asset
        var asset = system.GetBuildableAssets().FirstOrDefault(a => a.Tier.ToString() == assetId);
        if (asset == null)
        {
            result["success"] = false;
            result["message"] = $"Asset '{assetId}' not found or already owned";
            return result;
        }

        var integrationResult = system.BuildAsset(asset, (float)SimulationEngine.FinanceManager.Balance);
        result["success"] = integrationResult.Success;
        result["message"] = integrationResult.Message;
        result["capex_paid"] = (double)integrationResult.ActualCapexPaid;
        result["payback_years"] = (double)integrationResult.EstimatedPaybackYears;
        return result;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // WAVE 2 SYSTEMS API (T71) - LoanSystem, TaxSystem, InvestorSystem, etc.
    // ══════════════════════════════════════════════════════════════════════════════

    // ── LoanSystem Methods ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns active loans as an Array of Dictionaries.
    /// Keys: "id", "principal", "interest_rate", "term_months", "remaining_months", "monthly_payment".
    /// Note: Current LoanSystem is simplified and doesn't track individual loans.
    /// This returns a placeholder for future implementation.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveLoans()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var loans = SimulationEngine?.State?.ActiveLoans;
        if (loans == null) return result;
        foreach (var loan in loans)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = loan.Id,
                ["principal"] = (double)loan.Principal,
                ["original_principal"] = (double)(loan.OriginalPrincipal > 0m ? loan.OriginalPrincipal : loan.Principal),
                ["rate"] = (double)loan.InterestRate,
                ["days_left"] = Math.Max(0, loan.TermDays - loan.DaysTaken),
            });
        }
        return result;
    }

    /// <summary>
    /// Loan card summary (F943: the panel read a method that existed nowhere):
    /// has_loan, count, amount (principal borrowed), remaining (principal
    /// outstanding), monthly_payment, credit_available.
    /// </summary>
    public Godot.Collections.Dictionary GetLoanInfo()
    {
        var engine = SimulationEngine;
        var loans = engine?.State?.ActiveLoans;
        if (engine == null || loans == null)
            return new Godot.Collections.Dictionary { ["has_loan"] = false };
        decimal remaining = loans.Sum(l => Math.Max(0m, l.Principal));
        decimal borrowed = loans.Sum(l => l.OriginalPrincipal > 0m ? l.OriginalPrincipal : l.Principal);
        return new Godot.Collections.Dictionary
        {
            ["has_loan"] = remaining > 0m,
            ["count"] = loans.Count,
            ["amount"] = (double)borrowed,
            ["remaining"] = (double)remaining,
            ["monthly_payment"] = (double)engine.MonthlyLoanPaymentNok(),
            ["credit_available"] = (double)Math.Max(0m, engine.LoanCapacityNok() - remaining),
        };
    }

    /// <summary>Repay loan principal early, oldest first. {success, message, error, repaid}.</summary>
    public Godot.Collections.Dictionary RepayLoan(double amount)
    {
        var cmd = SimulationEngine?.RepayLoan((decimal)amount);
        if (cmd == null)
            return new Godot.Collections.Dictionary { ["success"] = false, ["error"] = "engine_not_ready", ["message"] = "Engine not initialized" };
        if (cmd.Ok) EmitSignal(SignalName.GameStateUpdated);
        return new Godot.Collections.Dictionary
        {
            ["success"] = cmd.Ok,
            ["error"] = cmd.ErrorKey,
            ["message"] = cmd.Message,
            ["repaid"] = cmd.Value,
        };
    }

    /// <summary>Total interest-bearing debt outstanding (NOK): loan principal plus bonds.</summary>
    public double GetTotalDebt()
        => (double)(SimulationEngine?.TotalDebtNok ?? 0m);

    /// <summary>Monthly loan payment (principal slice + interest) at today's balances (NOK).</summary>
    public double GetMonthlyPayment()
        => (double)(SimulationEngine?.MonthlyLoanPaymentNok() ?? 0m);

    /// <summary>
    /// Take out a new loan.
    /// Returns a Dictionary with keys: "success", "message", "loan_id", "principal", "monthly_payment".
    /// </summary>
    public Godot.Collections.Dictionary TakeLoan(double amount, int termMonths, double rate)
    {
        var result = new Godot.Collections.Dictionary();
        var engine = SimulationEngine;
        if (engine == null)
        {
            result["success"] = false;
            result["message"] = "Engine not initialized";
            return result;
        }

        // Phase 1: the verb lives in the library (SimulationEngine.Commands);
        // the bridge preserves the exact Dictionary contract the UI reads.
        var cmd = engine.TakeLoan(amount, termMonths, rate);
        result["success"] = cmd.Ok;
        result["message"] = cmd.Ok
            ? cmd.Message
            : (cmd.ErrorKey == "invalid_loan_params" ? "Invalid loan parameters" : cmd.Message);
        result["loan_id"] = cmd.Ok ? cmd.Id : "";
        result["principal"] = cmd.Ok ? amount : 0.0;

        // Calculate approximate monthly payment (presentation only; the
        // engine amortizes principal + interest daily). rate == 0 divides by
        // zero in the annuity formula, so fall back to linear amortization.
        double monthlyPayment = 0.0;
        if (cmd.Ok)
        {
            if (rate > 0)
            {
                double monthlyRate = rate / 12.0;
                monthlyPayment = amount * (monthlyRate * Math.Pow(1 + monthlyRate, termMonths)) /
                                 (Math.Pow(1 + monthlyRate, termMonths) - 1);
            }
            else
            {
                monthlyPayment = amount / Math.Max(1, termMonths);
            }
        }
        result["monthly_payment"] = monthlyPayment;

        return result;
    }

    // ── TaxSystem Methods ─────────────────────────────────────────────────────────

    /// <summary>Current corporate tax rate (0.22 = 22%).</summary>
    public double GetTaxRate()
    {
        return 0.22; // Norwegian corporate tax rate
    }

    /// <summary>Last tax payment amount (NOK). Returns 0 for now.</summary>
    public double GetLastTaxPaid()
    {
        // TODO: Track tax payments in TaxSystem
        return 0.0;
    }

    /// <summary>Estimated annual tax burden based on current profit (NOK).</summary>
    public double GetAnnualTaxBurden()
    {
        var profit = SimulationEngine?.FinanceManager?.Balance ?? 0m;
        if (profit <= 0m) return 0.0;
        
        return (double)(profit * 0.22m); // 22% corporate tax
    }

    // ── InvestorSystem Methods ────────────────────────────────────────────────────

    /// <summary>
    /// Returns all investors/shareholders as an Array of Dictionaries.
    /// Keys: "type", "amount", "shares", "equity_pct".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetInvestors()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var investorSystem = SimulationEngine?.InvestorSystem;
        if (investorSystem == null) return result;

        // Return bond holders
        if (investorSystem.OutstandingBondPrincipal > 0)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["type"] = "bond",
                ["amount"] = (double)investorSystem.OutstandingBondPrincipal,
                ["shares"] = 0.0,
                ["equity_pct"] = 0.0
            });
        }

        // Return equity holders (simplified - show total outstanding shares)
        if (investorSystem.OutstandingShares > 1_000_000m)
        {
            var dilutedShares = investorSystem.OutstandingShares - 1_000_000m;
            result.Add(new Godot.Collections.Dictionary
            {
                ["type"] = "equity",
                ["amount"] = (double)(dilutedShares * investorSystem.SharePrice),
                ["shares"] = (double)dilutedShares,
                ["equity_pct"] = (double)(dilutedShares / investorSystem.OutstandingShares)
            });
        }

        return result;
    }

    /// <summary>Total investment from bonds and equity (NOK).</summary>
    public double GetTotalInvestment()
    {
        var investorSystem = SimulationEngine?.InvestorSystem;
        if (investorSystem == null) return 0.0;
        
        return (double)(investorSystem.OutstandingBondPrincipal + 
                       (investorSystem.OutstandingShares - 1_000_000m) * investorSystem.SharePrice);
    }

    /// <summary>Current equity dilution (0.0-1.0, where 0.2 = 20% diluted).</summary>
    public double GetEquityDilution()
    {
        var investorSystem = SimulationEngine?.InvestorSystem;
        if (investorSystem == null) return 0.0;
        
        return (double)investorSystem.DilutionFraction();
    }

    // ── AcquisitionSystem Methods ─────────────────────────────────────────────────

    /// <summary>
    /// Returns available acquisition targets as an Array of Dictionaries.
    /// Keys: "id", "name", "asset_value", "license_value", "health_score", "is_distressed",
    ///       "asking_price", "region", "asset_type", "tonnage_capacity".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAvailableTargets()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var acquisitionSystem = SimulationEngine?.AcquisitionSystem;
        if (acquisitionSystem == null) return result;

        foreach (var target in acquisitionSystem.GetAvailableTargets())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = target.Id,
                ["name"] = target.Name,
                ["asset_value"] = (double)target.AssetValue,
                ["license_value"] = (double)target.LicenseValue,
                ["health_score"] = (double)target.HealthScore,
                ["is_distressed"] = target.IsDistressed,
                ["asking_price"] = (double)target.AskingPrice,
                ["regional_market_share"] = (double)target.RegionalMarketShare,
                ["region"] = target.Region,
                ["asset_type"] = target.AssetType,
                ["tonnage_capacity"] = (double)target.TonnageCapacity
            });
        }

        return result;
    }

    /// <summary>
    /// Returns owned acquisitions as an Array of Dictionaries.
    /// Keys: "id", "name", "acquired_price", "acquisition_date", "synergies_per_year".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetOwnedAcquisitions()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        // TODO: Track completed acquisitions in GameState
        // For now return empty array
        return result;
    }

    /// <summary>
    /// Attempt to acquire a target company.
    /// Returns a Dictionary with keys: "success", "message", "actual_price", "regulatory_delay_months", "synergies".
    /// </summary>
    public Godot.Collections.Dictionary AttemptAcquisitionTarget(string targetId)
    {
        var result = new Godot.Collections.Dictionary();
        var acquisitionSystem = SimulationEngine?.AcquisitionSystem;
        if (acquisitionSystem == null)
        {
            result["success"] = false;
            result["message"] = "Acquisition system not initialized";
            return result;
        }

        var targets = acquisitionSystem.GetAvailableTargets();
        var target = targets.FirstOrDefault(t => t.Id == targetId);
        if (target == null)
        {
            result["success"] = false;
            result["message"] = $"Target '{targetId}' not found";
            return result;
        }

        // Calculate acquisition price (90% of asking price as baseline offer)
        float offeredPrice = target.AskingPrice * 0.90f;
        float playerCash = (float)(SimulationEngine?.FinanceManager?.Balance ?? 0m);

        var acquisitionResult = acquisitionSystem.MakeOffer(target, offeredPrice, playerCash);
        
        if (acquisitionResult.Success)
        {
            // Deduct cost
            SimulationEngine?.PayCost((decimal)acquisitionResult.ActualPrice * 1_000_000m, LakskongenSim.Core.CostBooking.Capex, "acquisitions");
            
            // Calculate synergies (simplified - 10% of asset value per year)
            float synergies = (target.AssetValue + target.LicenseValue) * 0.10f;
            
            // Complete acquisition
            acquisitionSystem.CompleteAcquisition(target, acquisitionResult.ActualPrice, synergies);
            
            // Record event
            SimulationEngine?.State?.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = "Oppkjøp",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = LakskongenSim.Core.SimText.Nb($"Kjøpte {target.Name} for {acquisitionResult.ActualPrice:F1} MNOK"),
                BalanceChange = -(decimal)acquisitionResult.ActualPrice * 1_000_000m
            });
        }

        result["success"] = acquisitionResult.Success;
        result["message"] = acquisitionResult.Success ? 
            $"Successfully acquired {target.Name}" : 
            acquisitionResult.FailureReason;
        result["actual_price"] = (double)acquisitionResult.ActualPrice;
        result["regulatory_delay_months"] = acquisitionResult.RegulatoryDelayMonths;
        result["synergies_per_year"] = (double)acquisitionResult.SynergiesNOKPerYear;
        result["requires_antitrust"] = acquisitionResult.RequiresAntitrustApproval;

        return result;
    }

    // ── LicenseSystem Methods ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns active licenses as an Array of Dictionaries.
    /// Keys: "site_id", "annual_fee", "zone_id", "mab_tonnes".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveLicenses()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        if (sites == null) return result;

        foreach (var site in sites)
        {
            // F2-03: an offshore unit holds no sea licence until F2-09 binds its development licences.
            if (site.Kind != LakskongenSim.Farm.SiteKind.Fjord) continue;
            result.Add(new Godot.Collections.Dictionary
            {
                ["site_id"] = site.Id,
                ["annual_fee"] = 50000.0, // From LicenseSystem.AnnualFeePerSite
                ["zone_id"] = site.ZoneId,
                ["mab_tonnes"] = site.MABTonnes
            });
        }

        return result;
    }

    // ── StakeholderFactionSystem Methods ──────────────────────────────────────────

    /// <summary>
    /// Returns all stakeholder factions as an Array of Dictionaries.
    /// Keys: "type", "name", "description", "relationship_score", "influence", "weighted_score".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetFactions()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var factionSystem = SimulationEngine?.StakeholderFactionSystem;
        if (factionSystem == null) return result;

        foreach (var faction in factionSystem.GetAllFactions().Values)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["type"] = faction.Type.ToString(),
                ["name"] = faction.Name,
                ["description"] = faction.Description,
                ["relationship_score"] = (double)faction.RelationshipScore,
                ["influence"] = (double)faction.Influence,
                ["weighted_score"] = (double)faction.WeightedRelationshipScore,
                ["active_demands_count"] = faction.ActiveDemands.Count,
                ["active_sanctions_count"] = faction.ActiveSanctions.Count,
                ["active_bonuses_count"] = faction.ActiveBonuses.Count
            });
        }

        return result;
    }

    /// <summary>Get relationship score with a specific faction (-1.0 to +1.0).</summary>
    public double GetFactionRelation(string factionId)
    {
        var factionSystem = SimulationEngine?.StakeholderFactionSystem;
        if (factionSystem == null) return 0.0;

        if (!Enum.TryParse<LakskongenSim.Politics.FactionType>(factionId, true, out var factionType))
            return 0.0;

        var faction = factionSystem.GetFaction(factionType);
        return faction != null ? (double)faction.RelationshipScore : 0.0;
    }

    /// <summary>
    /// Returns all active demands from all factions as an Array of Dictionaries.
    /// Keys: "id", "faction", "description", "cost_nok", "duration_months", "expiry_date", "consequence".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveDemands()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var factionSystem = SimulationEngine?.StakeholderFactionSystem;
        if (factionSystem == null) return result;

        foreach (var demand in factionSystem.GetActiveDemands())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = demand.Id,
                ["faction"] = demand.FactionType.ToString(),
                ["description"] = demand.Description,
                ["cost_nok"] = (double)demand.CostNOK,
                ["duration_months"] = demand.DurationMonths,
                ["issued_date"] = demand.IssuedDate.ToString("yyyy-MM-dd"),
                ["expiry_date"] = demand.ExpiryDate.ToString("yyyy-MM-dd"),
                ["consequence"] = demand.ConsequenceIfIgnored,
                ["is_expired"] = demand.IsExpired
            });
        }

        return result;
    }

    // ── VisualMilestones Methods ──────────────────────────────────────────────────

    /// <summary>
    /// Returns all achieved milestones as an Array of Dictionaries.
    /// Keys: "type", "title", "description", "achieved_date", "cinematic_type".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAchievedMilestones()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var milestones = SimulationEngine?.VisualMilestones;
        if (milestones == null) return result;

        foreach (var milestone in milestones.GetAchievedMilestones())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["type"] = milestone.Type.ToString(),
                ["title"] = milestone.Title,
                ["description"] = milestone.Description,
                ["achieved_date"] = milestone.AchievedDate?.ToString("yyyy-MM-dd") ?? "",
                ["cinematic_type"] = milestone.CinematicType
            });
        }

        return result;
    }

    /// <summary>
    /// Returns the next unachieved visual milestone as a Dictionary.
    /// Keys: "type", "title", "description", "cinematic_type".
    /// Returns empty Dictionary if all milestones achieved.
    /// </summary>
    public Godot.Collections.Dictionary GetNextVisualMilestone()
    {
        var result = new Godot.Collections.Dictionary();
        var milestones = SimulationEngine?.VisualMilestones;
        if (milestones == null) return result;

        var next = milestones.GetNextMilestone();
        if (next != null)
        {
            result["type"] = next.Type.ToString();
            result["title"] = next.Title;
            result["description"] = next.Description;
            result["cinematic_type"] = next.CinematicType;
        }

        return result;
    }

    /// <summary>Milestone completion progress (0.0-1.0).</summary>
    public double GetMilestoneProgress()
    {
        var milestones = SimulationEngine?.VisualMilestones;
        return milestones?.GetProgress() ?? 0.0;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // DASHBOARD API - Financial Dashboards Integration
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns P&L statement data as a Godot Dictionary.
    /// Keys: "revenue", "cost_of_sales", "gross_profit", "operating_expenses",
    ///       "ebitda", "depreciation", "ebit", "interest", "tax", "net_profit",
    ///       "gross_margin_pct", "operating_margin_pct", "net_margin_pct".
    /// </summary>
    public Godot.Collections.Dictionary GetPLData()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Dictionary();
        }

        var plData = _dashboardDataProvider.GetPLData();
        var result = new Godot.Collections.Dictionary();

        foreach (var kvp in plData)
        {
            result[kvp.Key] = Variant.From(kvp.Value);
        }

        return result;
    }

    /// <summary>
    /// Returns 12-month cash flow forecast as a Godot Array of Dictionaries.
    /// Each entry: "month", "operating_cash", "investing_cash", "financing_cash", "net_cash", "cumulative_cash".
    /// </summary>
    public Godot.Collections.Array GetCashFlowForecast()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Array();
        }

        var forecast = _dashboardDataProvider.GetCashFlowForecast();
        var result = new Godot.Collections.Array();

        foreach (var month in forecast)
        {
            var dict = new Godot.Collections.Dictionary();
            foreach (var kvp in month)
            {
                dict[kvp.Key] = Variant.From(kvp.Value);
            }
            result.Add(dict);
        }

        return result;
    }

    /// <summary>
    /// Returns ROI analysis data as a Godot Dictionary.
    /// Keys: "total_investment", "annual_return", "roi_pct", "payback_months",
    ///       "npv", "irr_pct", "break_even_month".
    /// </summary>
    public Godot.Collections.Dictionary GetROIAnalysis()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Dictionary();
        }

        var roiData = _dashboardDataProvider.GetROIAnalysis();
        var result = new Godot.Collections.Dictionary();

        foreach (var kvp in roiData)
        {
            result[kvp.Key] = Variant.From(kvp.Value);
        }

        return result;
    }

    /// <summary>
    /// Returns detailed cost breakdown as a Godot Dictionary.
    /// Keys: "opex_feed", "opex_labor", "opex_maintenance", "opex_licenses",
    ///       "opex_delousing", "opex_other", "opex_total",
    ///       "capex_licenses", "capex_pens", "capex_equipment", "capex_ras",
    ///       "capex_offshore", "capex_total".
    /// </summary>
    public Godot.Collections.Dictionary GetCostBreakdown()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Dictionary();
        }

        var costData = _dashboardDataProvider.GetCostBreakdown();
        var result = new Godot.Collections.Dictionary();

        foreach (var kvp in costData)
        {
            result[kvp.Key] = Variant.From(kvp.Value);
        }

        return result;
    }

    /// <summary>
    /// Returns production cost per kg data as a Godot Dictionary.
    /// Keys: "total_cost_per_kg", "feed_cost_per_kg", "labor_cost_per_kg",
    ///       "maintenance_cost_per_kg", "other_cost_per_kg", "total_biomass_kg",
    ///       "fcr", "benchmark_cost_per_kg", "vs_benchmark_pct".
    /// </summary>
    public Godot.Collections.Dictionary GetProductionCosts()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Dictionary();
        }

        var costData = _dashboardDataProvider.GetProductionCosts();
        var result = new Godot.Collections.Dictionary();

        foreach (var kvp in costData)
        {
            result[kvp.Key] = Variant.From(kvp.Value);
        }

        return result;
    }

    /// <summary>
    /// Returns scenario comparison data as a Godot Array of Dictionaries.
    /// Each scenario: "name", "revenue", "cost", "profit", "roi_pct", "risk_score".
    /// </summary>
    public Godot.Collections.Array GetScenarioComparison()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Array();
        }

        var scenarios = _dashboardDataProvider.GetScenarioComparison();
        var result = new Godot.Collections.Array();

        foreach (var scenario in scenarios)
        {
            var dict = new Godot.Collections.Dictionary();
            foreach (var kvp in scenario)
            {
                dict[kvp.Key] = Variant.From(kvp.Value);
            }
            result.Add(dict);
        }

        return result;
    }

    /// <summary>
    /// Returns AI advisor recommendations as a Godot Array of strings.
    /// </summary>
    public Godot.Collections.Array<string> GetAdvisorRecommendations()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Array<string>();
        }

        var recommendations = _dashboardDataProvider.GetAdvisorRecommendations();
        var result = new Godot.Collections.Array<string>();

        foreach (var recommendation in recommendations)
        {
            result.Add(recommendation);
        }

        return result;
    }

}
