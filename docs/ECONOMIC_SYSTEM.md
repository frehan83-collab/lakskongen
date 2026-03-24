# Lakskongen — Economic System Documentation

**Document Version:** 1.0  
**Last Updated:** 2026-03-24  
**Phase:** Phase 6 Complete (Economic Simulation and Strategic Optimization)

---

## Table of Contents

1. [Overview](#overview)
2. [Finance Manager](#finance-manager)
3. [Market Simulation](#market-simulation)
4. [Contract System](#contract-system)
5. [International Markets](#international-markets)
6. [Cost Model (CAPEX/OPEX)](#cost-model-capexopex)
7. [Revenue Model](#revenue-model)
8. [Tax System](#tax-system)
9. [Financing (Loans, Investors, IPO)](#financing-loans-investors-ipo)
10. [Strategic Optimizers](#strategic-optimizers)
11. [Currency System](#currency-system)
12. [License System](#license-system)
13. [Economic Gameplay Loop](#economic-gameplay-loop)

---

## Overview

The Lakskongen economic system models **realistic Norwegian salmon aquaculture economics** with:

- **Cash flow management** — Single source of truth for balance (FinanceManager)
- **Dynamic pricing** — Ornstein-Uhlenbeck mean reversion calibrated to Fish Pool FPI
- **Contract strategy** — Spot, forward, tolling, exclusive contracts with quality grading
- **International trade** — 18 export markets with tariffs, FX rates, quality premiums
- **Financial instruments** — Loans, equity investors, IPO, stock market
- **Strategic optimization** — Feeding, harvest timing, pen management, treatment decisions
- **Norwegian taxation** — 22% corporate tax + 40% resource rent tax on profits >70M NOK

**Design Philosophy:**
> "Complex systems, simple interface. The player makes strategic decisions, the simulation handles the math."

---

## Finance Manager

**File:** `src/LakskongenSim/Finance/FinanceManager.cs`

### Purpose
**Single source of truth** for player cash balance. All revenue and expenses flow through this class.

### Key Responsibilities
- Track current balance (NOK)
- Apply revenue from sales, grants, investments
- Deduct expenses (feed, labor, CAPEX, taxes)
- Enforce affordability checks (prevent negative balance unless debt is intended)
- Integrate with sustainability system (ESG bonuses)

### Core API

```csharp
public class FinanceManager
{
    // ─── State ───
    public decimal Balance { get; private set; } // Current cash (NOK)
    
    // ─── Revenue ───
    public void AddRevenue(decimal amount); // Add income (sales, grants, investors)
    
    // ─── Expenses ───
    public bool Spend(decimal amount); // Deduct expense (returns false if insufficient funds)
    public void ForceSpend(decimal amount); // Spend even if balance goes negative (for debt)
    public bool CanAfford(decimal amount); // Check if purchase is affordable
    
    // ─── Sustainability Integration ───
    public decimal ApplyESGDiscount(decimal baseCost); // 0-30% discount on green CAPEX (ESG score >= 80)
    public decimal ApplyGovernmentGrant(decimal baseGrant); // 1.0-1.5x grant multiplier
    
    // ─── Events ───
    public event Action<decimal> OnBalanceChanged; // Fired on revenue/expense
}
```

### Sustainability Bonuses

**ESG Investment Discount** (ESG score >= 80):
- 0-30% discount on green technology CAPEX
- Applies to: RAS systems, renewable energy, eco-certified feed
- Formula: `discountedCost = baseCost * (1 - ESGInvestmentDiscount)`

**Government Grant Multiplier** (ESG score >= 70):
- 1.0-1.5x multiplier on research/innovation grants
- Applies to: R&D projects, sustainability certifications, technology upgrades
- Formula: `totalGrant = baseGrant * GovernmentGrantMultiplier`

### Example: Purchase Flow

```csharp
// Player wants to buy 10,000 kg of feed at 15 NOK/kg
decimal feedCost = 10_000m * 15m; // 150,000 NOK

// Check affordability (optional, for UX)
if (!financeManager.CanAfford(feedCost))
{
    ShowErrorMessage("Insufficient funds!");
    return;
}

// Attempt purchase
if (financeManager.Spend(feedCost))
{
    feedInventory.Add(10_000); // Add feed to inventory
    LogTransaction("Feed purchase", feedCost);
}
else
{
    // Should not happen if CanAfford was checked, but defensive
    ShowErrorMessage("Purchase failed!");
}
```

---

## Market Simulation

**File:** `src/LakskongenSim/Market/MarketSimulation.cs`

### Purpose
Realistic salmon market price simulation based on **Norwegian Fish Pool FPI data (2020-2025)**.

### Model: Ornstein-Uhlenbeck Mean Reversion

**Formula:**
```
dP_t = κ(μ - P_t)dt + σdW_t

Where:
  P_t = current price (NOK/kg)
  μ = long-term mean price (~85 NOK/kg for Fish Pool FPI 2020-2025 average)
  κ = mean reversion speed (~0.62 annualized, 0.012 weekly)
  σ = volatility (~9 NOK/kg weekly)
  W_t = Wiener process (random walk component)
```

**Interpretation:**
- **Mean reversion:** Prices drift toward long-term mean (85 NOK/kg)
- **Volatility:** Random weekly shocks (±5-12 NOK/kg typical)
- **Bounded:** Price floor = 40 NOK/kg, ceiling = 160 NOK/kg

### Calibration to Fish Pool FPI

**Historical Context (2020-2025):**
- 2020 COVID trough: ~45 NOK/kg (March-May 2020)
- 2022 peak: ~140 NOK/kg (Ukraine war, supply disruption)
- 2023-2025 elevated: ~85-95 NOK/kg average (new normal post-pandemic)

**Seasonal Patterns:**
```csharp
// Monthly offsets (NOK/kg) — based on Fish Pool seasonal decomposition
private static readonly decimal[] MonthlySeasonalOffsets =
{
     5m, -2m, -5m,  // Jan (Chinese New Year), Feb (post-CNY dip), Mar (low)
    -3m,  2m,  8m,  // Apr (Easter variable), May (rising), Jun (BBQ season starts)
    12m, 10m,  3m,  // Jul (peak BBQ), Aug (still high), Sep (harvest surge → drop)
    -4m, -2m,  6m   // Oct (oversupply), Nov (stable), Dec (Christmas demand)
};
```

**Key Seasonal Events:**
- **Chinese New Year (Jan):** +5 NOK/kg (demand spike for fresh salmon)
- **Easter (Apr):** Variable, +2-5 NOK/kg (Norwegian tradition)
- **BBQ Season (Jun-Aug):** +8-12 NOK/kg (peak global demand)
- **Harvest Surge (Sep-Oct):** -4 to -5 NOK/kg (oversupply from summer harvest)
- **Christmas (Dec):** +6 NOK/kg (holiday demand, smoked salmon)

### Core API

```csharp
public class MarketSimulation
{
    // ─── Current State ───
    public decimal BasePricePerKg { get; private set; } // O-U model output
    public decimal PricePerKg { get; private set; } // Base + seasonal + supply/demand
    public decimal WeeklyAvgPrice { get; private set; } // Rolling 7-day average
    
    // ─── Calibration ───
    public decimal LongTermMean { get; private set; } // μ (default 85 NOK/kg)
    public decimal MeanReversionSpeedWeekly { get; private set; } // κ (default 0.012)
    public decimal VolatilityWeekly { get; private set; } // σ (default 9 NOK/kg)
    public decimal PriceFloorPerKg => 40m; // Historical low
    public decimal PriceCeilingPerKg => 160m; // Historical high
    
    // ─── Update ───
    public void AdvanceOneDay(); // Update O-U model, apply seasonality
    
    // ─── Supply/Demand Shocks ───
    public void ApplySupplyShock(double magnitude, int durationDays); // Manual event (e.g., disease outbreak)
    public void ApplyDemandShock(double magnitude, int durationDays); // Manual event (e.g., trade embargo)
    
    // ─── Price Queries ───
    public decimal GetPriceForGrade(QualityGrade grade); // Base price + quality premium
    public decimal GetForecastPrice(int daysAhead); // For forward contracts
    
    // ─── Market Access ───
    public void UnlockMarket(string marketName); // Add export destination (e.g., "USA")
    public void ApplyCertification(decimal multiplier); // ASC/MSC/Organic bonus
    
    // ─── Subsystems ───
    public MarketChannelSystem Channels { get; } // Spot/export/direct retail channels
    public MarketIntelligenceSystem Intelligence { get; } // Forecasting, news, competitor tracking
    
    // ─── Events ───
    public event Action<decimal> OnPriceChanged; // Fired daily
    public event Action<string> OnMarketEvent; // News events
}
```

### Quality Premiums

```csharp
public enum QualityGrade
{
    Economy,   // -10 NOK/kg (low fillet quality, stress marks)
    Standard,  // Base price
    ASC,       // +8 NOK/kg (ASC certified)
    Premium,   // +15 NOK/kg (superior fillet, high omega-3)
    Organic    // +25 NOK/kg (organic feed, low density)
}
```

### Example: Daily Price Update

```csharp
// Called by SimulationEngine.AdvanceOneDay()
public void AdvanceOneDay()
{
    // 1. O-U mean reversion step
    decimal drift = MeanReversionSpeedWeekly * (LongTermMean - BasePricePerKg);
    decimal diffusion = VolatilityWeekly * (decimal)Gaussian(0, 1); // Random shock
    BasePricePerKg += drift + diffusion;
    
    // 2. Apply bounds
    BasePricePerKg = Math.Clamp(BasePricePerKg, PriceFloor, PriceCeiling);
    
    // 3. Seasonal offset
    int month = currentDate.Month - 1; // 0-indexed
    decimal seasonalOffset = MonthlySeasonalOffsets[month];
    
    // 4. Supply/demand shocks
    decimal supplyDemandAdjustment = CalculateSupplyDemandAdjustment();
    
    // 5. Final price
    PricePerKg = BasePricePerKg + seasonalOffset + supplyDemandAdjustment;
    PricePerKg = Math.Clamp(PricePerKg, PriceFloor, PriceCeiling);
    
    // 6. Update rolling average
    UpdateWeeklyAverage(PricePerKg);
    
    // 7. Fire event
    OnPriceChanged?.Invoke(PricePerKg);
}
```

---

## Contract System

**File:** `src/LakskongenSim/Market/ContractSystem.cs`

### Purpose
Strategic contract management for fish sales. Players choose between **spot market** (current price, no commitment) and **forward contracts** (fixed price, future delivery).

### Contract Types

```csharp
public enum ContractType
{
    Spot,       // Immediate sale at current market price (no contract)
    Forward,    // Fixed price for delivery 4-12 weeks ahead
    Tolling,    // Processing fee-based (buyer provides processing facility)
    Exclusive   // Long-term exclusive buyer (premium price, volume commitment)
}
```

**Spot Market:**
- Immediate sale at current market price
- No commitment, no penalty
- Use when: price is high, no better contract available

**Forward Contract:**
- Lock in price for delivery 4-12 weeks ahead
- Fixed price protects against future price drops
- Penalty if unfulfilled (typically 10-20% of contract value)
- Use when: current price is low, expect future harvest

**Tolling Contract:**
- Buyer processes fish at their facility (player delivers live fish)
- Lower per-kg price but no processing CAPEX/OPEX
- Use when: no processing plant, small-scale operation

**Exclusive Contract:**
- Long-term relationship (12-52 weeks)
- Premium price (+5-10 NOK/kg) for guaranteed volume
- Heavy penalty for breach (30-50%)
- Use when: stable production, predictable harvest schedule

### Core API

```csharp
public class ContractSystem
{
    // ─── Active Contracts ───
    public List<Contract> ActiveContracts { get; }
    public IReadOnlyList<Contract> CompletedContracts { get; }
    
    // ─── Contract Workflow ───
    public ContractOffer ProposeContract(ContractType type, int quantityKg, int durationWeeks);
    public Contract SignContract(ContractOffer offer);
    public decimal FulfillContract(Contract contract, int deliveredKg, QualityGrade actualGrade);
    public void ApplyPenalty(Contract contract); // Unfulfilled contract
    
    // ─── Queries ───
    public List<ContractOffer> GetCurrentOffers(); // Available contracts (refreshed weekly)
    public int GetTotalCommittedVolume(); // Sum of active contract volumes
    public decimal GetWeightedAvgContractPrice(); // Average price across active contracts
    
    // ─── Events ───
    public event Action<Contract> OnContractSigned;
    public event Action<Contract> OnContractFulfilled;
    public event Action<Contract> OnContractExpired;
}
```

### Contract Structure

```csharp
public record Contract(
    string Buyer,               // "Mowi", "Lerøy", "SalMar", etc.
    ContractType Type,
    decimal PricePerKg,         // Fixed price
    int QuantityKg,             // Total volume committed
    QualityGrade MinQuality,    // Minimum accepted quality
    decimal PenaltyPct,         // % of contract value if unfulfilled
    Guid Id
)
{
    public int FulfilledKg { get; init; } = 0;
    public int SignedAtWeek { get; init; }
    public int DurationWeeks { get; init; }
    public bool IsExpired(int currentWeek) => currentWeek > SignedAtWeek + DurationWeeks;
    public bool IsComplete => FulfilledKg >= QuantityKg;
    public int RemainingKg => Math.Max(0, QuantityKg - FulfilledKg);
}
```

### Example: Forward Contract Workflow

```csharp
// Week 10: Player sees current price is 75 NOK/kg but expects it to rise
var offers = contractSystem.GetCurrentOffers();

// Find a forward contract
var forwardOffer = offers.FirstOrDefault(o => o.Type == ContractType.Forward);
// ForwardOffer: 8 weeks ahead, 100,000 kg, 82 NOK/kg, 10% penalty

// Player signs contract (locks in 82 NOK/kg for week 18 delivery)
var contract = contractSystem.SignContract(forwardOffer);

// Week 18: Player harvests fish
var deliveredKg = 100_000;
var actualGrade = QualityGrade.Standard;

// Fulfill contract (returns actual revenue)
decimal revenue = contractSystem.FulfillContract(contract, deliveredKg, actualGrade);
// Revenue = 100,000 kg * 82 NOK/kg = 8,200,000 NOK

financeManager.AddRevenue(revenue);
```

### Quality Grade Enforcement

```csharp
// If delivered fish quality is below contract minimum:
public decimal FulfillContract(Contract contract, int deliveredKg, QualityGrade actualGrade)
{
    if (actualGrade < contract.MinQualityGrade)
    {
        // Downgrade price by 15-30%
        decimal priceReduction = contract.PricePerKg * 0.20m;
        decimal adjustedPrice = contract.PricePerKg - priceReduction;
        return deliveredKg * adjustedPrice;
    }
    
    return deliveredKg * contract.PricePerKg;
}
```

---

## International Markets

**File:** `src/LakskongenSim/Markets/InternationalMarketSystem.cs`

### Purpose
Model **18 export destinations** with realistic trade dynamics: tariffs, quality preferences, FX rates, shipping costs.

### Export Destinations

```csharp
// Major Markets (by volume)
EU: France, UK, Germany, Spain, Italy, Poland
Asia: Japan, China, South Korea, Thailand, Vietnam
Americas: USA, Canada, Brazil
Other: Russia, Australia, Middle East
```

**Market Characteristics:**

| Market | Volume | Quality Pref | Tariff | Notes |
|--------|--------|--------------|--------|-------|
| **EU (France)** | High | Premium/ASC | 0% | EEA member, largest importer |
| **USA** | High | Standard/Premium | 0% | Prefers Atlantic salmon, strong demand |
| **Japan** | Medium | Premium/Sashimi | 3.5% | Sashimi-grade only, high margins |
| **China** | Very High | Standard/Economy | 7-10% | Price-sensitive, whole fish |
| **UK** | High | Premium/ASC | 0% | Post-Brexit tariff-free, smoked salmon |
| **Russia** | Low | Economy | 15% | Sanctions/geopolitics, volatility |

### Core API

```csharp
public class InternationalMarketSystem
{
    // ─── Market Access ───
    public void UnlockMarket(string marketName); // Unlock via license/certification
    public bool IsMarketAccessible(string marketName);
    public List<string> GetAccessibleMarkets();
    
    // ─── Pricing ───
    public decimal GetMarketPrice(string marketName, QualityGrade grade); // Base price + market premium + tariff
    public decimal GetShippingCost(string marketName, int quantityKg); // Air freight vs sea freight
    
    // ─── Export Workflow ───
    public decimal SellToMarket(string marketName, int quantityKg, QualityGrade grade);
    
    // ─── Market Intelligence ───
    public MarketDemandForecast GetDemandForecast(string marketName, int weeksAhead);
    public List<string> GetGrowingMarkets(); // High growth potential
    public List<string> GetDecreasingMarkets(); // Trade barriers, sanctions
    
    // ─── Currency ───
    public decimal ConvertToNOK(decimal amount, string currency); // FX conversion
}
```

### Example: Export to Japan

```csharp
// Player wants to export 20,000 kg sashimi-grade salmon to Japan
string market = "Japan";
int quantityKg = 20_000;
var grade = QualityGrade.Premium; // Sashimi-grade

// Check market access
if (!internationalMarketSystem.IsMarketAccessible(market))
{
    ShowMessage("Japan market locked. Requires sashimi certification.");
    return;
}

// Get Japan market price (base + premium + tariff)
decimal pricePerKg = internationalMarketSystem.GetMarketPrice(market, grade);
// Japan premium: +30 NOK/kg for sashimi-grade
// Tariff: -3.5% = ~3 NOK/kg
// Net: base (85) + premium (30) - tariff (3) = 112 NOK/kg

// Shipping cost (air freight for fresh sashimi)
decimal shippingCost = internationalMarketSystem.GetShippingCost(market, quantityKg);
// Air freight: ~8 NOK/kg

// Net revenue
decimal grossRevenue = quantityKg * pricePerKg; // 2,240,000 NOK
decimal netRevenue = grossRevenue - shippingCost; // 2,080,000 NOK

financeManager.AddRevenue(netRevenue);
```

---

## Cost Model (CAPEX/OPEX)

### Capital Expenditures (CAPEX)

**Initial Setup:**
- Hatchery: 5-10M NOK
- Net pen (120m circumference): 2-3M NOK
- Feed barge: 500k NOK
- Delousing system: 1-2M NOK
- Processing plant (small): 15-20M NOK

**Expansion:**
- Additional pen: 2-3M NOK
- RAS facility: 50-100M NOK
- Offshore farm: 200-500M NOK

**Technology Upgrades:**
- Automated feeding system: 500k-1M NOK
- Real-time monitoring (sensors): 200-500k NOK
- Delousing robot: 3-5M NOK
- Renewable energy (solar/wind): 5-10M NOK

### Operating Expenditures (OPEX)

**Daily Costs (per pen, ~200k fish):**
- Feed: 10,000-15,000 NOK/day (at 12 NOK/kg feed, 1.25 FCR)
- Labor: 5,000 NOK/day (2-3 workers)
- Insurance: 1,000 NOK/day (mortality, storm damage)
- Maintenance: 500 NOK/day (nets, equipment)

**Periodic Costs:**
- Sea lice treatment: 50,000-200,000 NOK (every 4-8 weeks)
- Disease medication: 100,000-500,000 NOK (as needed)
- Net cleaning: 50,000 NOK (quarterly)
- License renewal: 100,000 NOK/year (per site)

**Annual Fixed Costs:**
- Lease (sea site): 500k-1M NOK/year
- Utilities: 200k NOK/year
- Administration: 1M NOK/year (small farm)
- R&D: 500k-2M NOK/year (optional)

---

## Revenue Model

### Product Types

**Fresh Whole Fish:**
- Base price: Current market price (85 NOK/kg avg)
- Yield: 100% (no processing)
- Markets: Export (EU, Asia), domestic wholesale

**Head-On Gutted (HOG):**
- Base price: +5 NOK/kg premium
- Yield: 88% (12% waste: guts, blood)
- Markets: Retail, export

**Fillets:**
- Base price: +15 NOK/kg premium (per kg fillet)
- Yield: 55-65% (depends on fish quality, fillet skill)
- Markets: Retail, food service

**Smoked Salmon:**
- Base price: +40 NOK/kg premium
- Yield: 70% (after smoking, trimming)
- Processing time: 3-7 days
- Markets: Premium retail, export (UK, France)

**Sashimi-Grade:**
- Base price: +30 NOK/kg premium
- Yield: 50% (strict quality grading)
- Markets: Japan, high-end restaurants

**Value-Added Products (VAP):**
- Burgers/patties: +20 NOK/kg
- Canned salmon: +10 NOK/kg
- Pet food: -30 NOK/kg (low-grade)
- Roe: +200 NOK/kg

### Revenue Calculation Example

```csharp
// Player harvests 100,000 kg fish (4.5 kg average)
int totalKg = 100_000;
decimal basePrice = 85m; // NOK/kg

// Option 1: Sell whole fish to spot market
decimal spotRevenue = totalKg * basePrice; // 8,500,000 NOK

// Option 2: Process into fillets, sell to retail
decimal filletYield = 0.60m; // 60% yield
decimal filletKg = totalKg * filletYield; // 60,000 kg fillets
decimal filletPrice = basePrice + 15m; // 100 NOK/kg
decimal filletRevenue = filletKg * filletPrice; // 6,000,000 NOK
// Lower total revenue, but higher margin (no middleman)

// Option 3: Forward contract (signed 8 weeks ago at 82 NOK/kg)
decimal contractPrice = 82m;
decimal contractRevenue = totalKg * contractPrice; // 8,200,000 NOK
// Spot price is now 85, but contract locked at 82 (lost 300k NOK)
```

---

## Tax System

**File:** `src/LakskongenSim/Finance/TaxSystem.cs`

### Norwegian Aquaculture Taxation

**1. Corporate Income Tax:**
- Rate: 22% (standard Norwegian corporate tax)
- Applied to: Profit (revenue - OPEX - CAPEX depreciation)

**2. Resource Rent Tax (Grunnrenteskatt):**
- Rate: 40% on profits exceeding NOK 70M annually
- Applied to: (Profit - 70M NOK) * 0.40
- Introduced: 2023 (controversial industry policy)

**3. Production Tax (Proposed 2025):**
- Rate: ~1% of gross sales
- Status: Proposed, not yet implemented (political debate)

### Example: Tax Calculation

```csharp
// Annual financials
decimal revenue = 100_000_000m;      // 100M NOK gross revenue
decimal opex = 50_000_000m;          // 50M NOK operating costs
decimal capexDepreciation = 5_000_000m; // 5M NOK depreciation
decimal profit = revenue - opex - capexDepreciation; // 45M NOK

// 1. Corporate income tax (22%)
decimal corporateTax = profit * 0.22m; // 9.9M NOK

// 2. Resource rent tax (40% on profit > 70M NOK)
decimal resourceRentTax = 0m; // Profit < 70M, no resource rent tax

// Total tax
decimal totalTax = corporateTax + resourceRentTax; // 9.9M NOK
decimal netProfit = profit - totalTax; // 35.1M NOK

// Effective tax rate
decimal effectiveRate = totalTax / profit; // 22%
```

**High-Profit Scenario (>70M NOK profit):**
```csharp
// Large operation
decimal revenue = 300_000_000m;      // 300M NOK
decimal opex = 180_000_000m;         // 180M NOK
decimal capexDepreciation = 20_000_000m; // 20M NOK
decimal profit = 100_000_000m;       // 100M NOK profit

// 1. Corporate tax (22%)
decimal corporateTax = profit * 0.22m; // 22M NOK

// 2. Resource rent tax (40% on profit > 70M)
decimal excessProfit = profit - 70_000_000m; // 30M NOK
decimal resourceRentTax = excessProfit * 0.40m; // 12M NOK

// Total tax
decimal totalTax = corporateTax + resourceRentTax; // 34M NOK
decimal netProfit = profit - totalTax; // 66M NOK

// Effective tax rate
decimal effectiveRate = totalTax / profit; // 34%
```

---

## Financing (Loans, Investors, IPO)

### Loan System

**File:** `src/LakskongenSim/Finance/LoanSystem.cs`

**Loan Types:**
- **Short-term (1-2 years):** 6-8% interest, for OPEX, feed purchases
- **Long-term (5-10 years):** 4-6% interest, for CAPEX, expansion
- **Credit line:** Revolving credit, 7-9% interest, flexible repayment

**Example:**
```csharp
// Player needs 10M NOK for hatchery construction
var loan = loanSystem.TakeLoan(10_000_000m, 10, 0.05m); // 10M, 10 years, 5% APR
// Monthly payment: ~106,000 NOK
```

### Investor System

**File:** `src/LakskongenSim/Finance/InvestorSystem.cs`

**Investment Rounds:**
- **Seed:** 1-5M NOK, 20-30% equity
- **Series A:** 10-50M NOK, 10-20% equity
- **Series B:** 50-200M NOK, 5-15% equity

**Investor Requirements:**
- Minimum ESG score (e.g., 60 for Series A)
- Revenue milestones (e.g., 10M NOK annual revenue for Series B)
- Profitability (optional, depends on investor)

**Example:**
```csharp
// Player seeks Series A investment
var offer = investorSystem.RequestInvestment(InvestmentRound.SeriesA);
// Offer: 25M NOK for 15% equity, ESG score >= 60 required

if (esgScore >= 60)
{
    investorSystem.AcceptInvestment(offer);
    financeManager.AddRevenue(25_000_000m);
    // Player now owns 85% of company
}
```

### Stock Market / IPO

**File:** `src/LakskongenSim/Finance/StockMarketSystem.cs`

**IPO Requirements:**
- Revenue: >100M NOK annually
- Profitability: 3 consecutive profitable quarters
- ESG score: >70 (for Oslo Børs listing)

**Stock Price Factors:**
- Quarterly earnings (EPS)
- Revenue growth (YoY)
- ESG score
- Industry trends (salmon price, disease outbreaks)

**Example:**
```csharp
// Player goes public
stockMarket.IPO(initialSharePrice: 50m, sharesIssued: 10_000_000);
// Market cap: 500M NOK
// Player retains 60% (6M shares)
// Public owns 40% (4M shares)

// Quarterly update (good earnings)
stockMarket.UpdateStockPrice(earnings: 15_000_000m, revenueGrowth: 0.25m);
// Stock price rises to 58 NOK (+16%)

// Pay dividends
stockMarket.PayDividend(dividendPerShare: 2m);
// Player receives: 6M shares * 2 NOK = 12M NOK
```

---

## Strategic Optimizers

### 1. Feeding Optimizer

**File:** `src/LakskongenSim/Optimization/FeedingOptimizer.cs`

**Purpose:** Calculate optimal daily feed ration based on temperature, oxygen, economics.

**Algorithm:**
```csharp
double baseRate = 1.2; // % body weight/day (optimal)

// Temperature response (Gaussian centered on 11°C)
double tempMultiplier = Math.Exp(-Math.Pow(tempC - 11, 2) / 50);

// Oxygen response (linear drop below 7 mg/L)
double oxygenMultiplier = Math.Clamp((oxygenMgL - 4) / 3, 0, 1);

// Economic response (feed more when margin is high)
double margin = marketPricePerKg - feedCostPerKg * 1.25; // 1.25 FCR
double economicMultiplier = margin > 30 ? 1.1 : margin > 15 ? 1.0 : 0.9;

// Size response (smaller fish eat more relative to weight)
double sizeMultiplier = fishWeightKg < 0.5 ? 1.3 : fishWeightKg > 5.0 ? 0.85 : 1.0;

// Final ration
double ration = fishWeightKg * baseRate * tempMultiplier * oxygenMultiplier * economicMultiplier * sizeMultiplier / 100.0;
```

**Output:** 30-day feeding schedule with expected growth, cost, FCR.

---

### 2. Harvest Optimizer

**File:** `src/LakskongenSim/Optimization/HarvestOptimizer.cs`

**Purpose:** Determine optimal harvest timing based on size distribution, market price, contracts.

**Factors:**
- **Size distribution:** Maximize % of fish in target size range (4-6 kg)
- **Market price:** Harvest when price is high (unless forward contract)
- **Contract deadlines:** Fulfill contracts before expiration
- **Pen capacity:** Avoid overstocking (welfare penalties)

**Algorithm:**
```csharp
// Calculate harvest score (0-100)
double sizeScore = PercentInTargetRange(4.0, 6.0); // 0-100
double priceScore = (currentPrice - longTermMean) / volatility * 20; // -50 to +50
double contractScore = HasUrgentContract() ? 50 : 0; // 0 or 50
double capacityScore = biomass > 0.9 * maxBiomass ? 30 : 0; // 0 or 30

double totalScore = sizeScore + priceScore + contractScore + capacityScore;

// Recommend harvest if score > 100
return totalScore > 100;
```

**Output:** Harvest recommendation (Yes/No), expected revenue, optimal quantity.

---

### 3. Pen Management Optimizer

**File:** `src/LakskongenSim/Optimization/PenManagementOptimizer.cs`

**Purpose:** Optimize stocking density (welfare vs growth tradeoff).

**Tradeoff:**
- **Low density (10 kg/m³):** High welfare, slower growth (stress-free)
- **Medium density (15-20 kg/m³):** Balanced (typical commercial)
- **High density (25+ kg/m³):** Faster growth, welfare penalties, disease risk

**Algorithm:**
```csharp
// Calculate optimal density based on zone, season, genetics
double baseOptimalDensity = 18.0; // kg/m³

// Adjust for environmental stress
if (temperatureC > 16) baseOptimalDensity *= 0.8; // High temp → reduce density
if (oxygenMgL < 6) baseOptimalDensity *= 0.85; // Low oxygen → reduce density

// Adjust for fish genetics (stress tolerance)
if (avgStressTolerance > 70) baseOptimalDensity *= 1.1; // Robust fish → can handle higher density

// Clamp to regulatory limits (Norwegian standard: 25 kg/m³ max)
return Math.Clamp(baseOptimalDensity, 10, 25);
```

**Output:** Recommended stocking density, welfare score, growth multiplier.

---

### 4. Treatment Cost-Benefit Analyzer

**File:** `src/LakskongenSim/Optimization/TreatmentCostBenefitAnalyzer.cs`

**Purpose:** Evaluate ROI of disease treatment (medication vs mortality cost).

**Algorithm:**
```csharp
// Option 1: Treat disease
decimal treatmentCost = 200_000m; // Medication + labor
double treatedMortalityPct = 2.0; // 2% mortality with treatment
decimal treatedRevenueLoss = totalBiomassKg * treatedMortalityPct / 100 * marketPricePerKg;

// Option 2: No treatment
double untreatedMortalityPct = 15.0; // 15% mortality without treatment
decimal untreatedRevenueLoss = totalBiomassKg * untreatedMortalityPct / 100 * marketPricePerKg;

// ROI of treatment
decimal benefit = untreatedRevenueLoss - treatedRevenueLoss; // Revenue saved
decimal roi = (benefit - treatmentCost) / treatmentCost * 100; // % ROI

// Recommend treatment if ROI > 50%
return roi > 50;
```

**Output:** Treatment recommendation (Yes/No), expected ROI, mortality comparison.

---

## Currency System

**File:** `src/LakskongenSim/Market/CurrencySystem.cs`

**Supported Currencies:**
- NOK (Norwegian Krone) — Base currency
- USD (US Dollar)
- EUR (Euro)
- GBP (British Pound)
- JPY (Japanese Yen)
- CNY (Chinese Yuan)

**Exchange Rates:**
- Real-time FX rates (updated daily from API or static dataset)
- Historical volatility: ±2-5% monthly

**Example:**
```csharp
// Export to USA: 50,000 kg at 10 USD/kg
decimal usdRevenue = 50_000 * 10; // 500,000 USD

// Convert to NOK (assume 1 USD = 10.5 NOK)
decimal nokRevenue = currencySystem.ConvertToNOK(usdRevenue, "USD");
// 5,250,000 NOK

financeManager.AddRevenue(nokRevenue);
```

---

## License System

**File:** `src/LakskongenSim/Finance/LicenseSystem.cs`

### Norwegian Regulatory Framework

**MAB (Maximum Allowed Biomass):**
- Each license specifies max biomass (e.g., 780 tons)
- Exceeding MAB → fines, license suspension

**Traffic Light System:**
- **Green Zone:** Unlimited expansion allowed (low sea lice impact)
- **Yellow Zone:** Moderate restrictions (6% growth cap)
- **Red Zone:** No new licenses, reduce production

**License Types:**
- **Commercial:** Standard production license (780-2340 tons MAB)
- **Development:** R&D license (experimental systems, lower MAB)
- **Organic:** Organic production license (stricter welfare/density)

**Example:**
```csharp
// Player applies for new license in PO06 (Møre og Romsdal)
var licenseOffer = licenseSystem.ApplyForLicense("PO06", LicenseType.Commercial);
// Cost: 20M NOK, MAB: 780 tons, Zone: Yellow (6% growth cap)

if (financeManager.CanAfford(20_000_000m))
{
    licenseSystem.PurchaseLicense(licenseOffer);
    financeManager.Spend(20_000_000m);
    // Player now owns license for PO06, can stock up to 780 tons
}
```

---

## Economic Gameplay Loop

### Beginner Strategy (Years 1-3)

1. **Start small:** 1-2 pens, domestic market only
2. **Spot market sales:** No contracts (too risky for beginners)
3. **Basic genetics:** Buy standard smolt, no breeding program
4. **Feed optimization:** Use FeedingOptimizer to minimize FCR
5. **Avoid debt:** Bootstrap with savings, no loans until profitable

**Target Metrics:**
- Revenue: 5-10M NOK/year
- Profit margin: 15-20%
- FCR: <1.3
- Mortality: <5%

---

### Intermediate Strategy (Years 3-7)

1. **Expand capacity:** 4-6 pens, multiple sites
2. **Forward contracts:** Lock in prices for 30-50% of production
3. **Export markets:** Unlock EU (France, UK), USA
4. **Processing plant:** Small plant for fillet production (+15 NOK/kg margin)
5. **Genetics program:** Breed for growth rate, disease resistance
6. **Loans for CAPEX:** Expand with 5-10M NOK loans

**Target Metrics:**
- Revenue: 50-100M NOK/year
- Profit margin: 20-25%
- FCR: <1.25
- Mortality: <3%
- ESG score: >60

---

### Advanced Strategy (Years 7+)

1. **Vertical integration:** Hatchery → sea site → processing → export
2. **International dominance:** Export to Japan (sashimi), China (volume)
3. **Technology investment:** RAS, offshore farms, automated feeding
4. **Stock market:** IPO for 200-500M NOK capital injection
5. **Acquisitions:** Buy out competitors, consolidate market share
6. **Sustainability leadership:** Organic certification, ASC/MSC, ESG >80

**Target Metrics:**
- Revenue: 500M+ NOK/year
- Profit margin: 25-30%
- FCR: <1.20
- Mortality: <2%
- ESG score: >80
- Market cap: 2-5B NOK (if public)

---

## Conclusion

The Lakskongen economic system provides **depth without overwhelming complexity**:

- **Simple inputs:** Buy feed, harvest fish, sell at market price
- **Complex outputs:** Forward contracts, international markets, tax optimization, stock market dynamics
- **Strategic depth:** Players choose between safety (spot market) and speculation (forward contracts), domestic simplicity vs export complexity

**Key Design Principles:**
1. **Realism:** Calibrated to real Norwegian data (Fish Pool FPI, tax rates, license costs)
2. **Strategic choice:** Multiple valid strategies (volume vs quality, domestic vs export, spot vs contracts)
3. **Risk/reward:** Higher margins require higher investment (processing, export, certifications)
4. **Progression:** Mechanics unlock gradually (tutorials, milestones, capital requirements)

For technical implementation details, see:
- **Architecture:** [ARCHITECTURE.md](ARCHITECTURE.md)
- **Environmental Data:** [ENVIRONMENTAL_DATA.md](ENVIRONMENTAL_DATA.md)
- **API Reference:** [API_Reference.md](API_Reference.md)

---

**Document Status:** ✅ Complete  
**Last Verified:** 2026-03-24  
**Next Review:** Phase 7 completion
