// =============================================================================
// ContractSystem.cs - SIMULATION CORE
// =============================================================================
// This is the authoritative C# simulation layer for contract economics.
// Handles: contract workflow, payment calculations, penalty logic, quality grades.
//
// RELATIONSHIP TO GDScript ContractSystem.gd:
// - This C# class is the SIMULATION ENGINE (business logic, calculations)
// - GDScript ContractSystem.gd is the GAME WRAPPER (UI, templates, gameplay)
// - Data flows: GDScript calls C# via Godot's C# interop for economic calculations
// - See docs/CONTRACT_SYSTEM_ARCHITECTURE.md for full data flow documentation
// =============================================================================

using System;
using System.Collections.Generic;

namespace LakskongenSim.Market
{
    public enum ContractType
    {
        Spot,
        Tolling,
        Exclusive,
        Forward
    }

    public enum QualityGrade
    {
        Standard,
        ASC,
        Premium
    }

    public record ContractOffer(string Buyer, ContractType Type, decimal PricePerKg, int QuantityKg, QualityGrade Quality, decimal PenaltyPct = 0m)
    {
        /// <summary>Duration in weeks for forward contracts.</summary>
        public int DurationWeeks { get; init; } = 4;
        /// <summary>Minimum fish quality grade accepted by buyer.</summary>
        public FishQualityGrade MinQualityGrade { get; init; } = FishQualityGrade.Standard;
        /// <summary>Export market destination (null = domestic).</summary>
        public string? DestinationMarket { get; init; }
    }

    public record Contract(string Buyer, ContractType Type, decimal PricePerKg, int QuantityKg, QualityGrade Quality, decimal PenaltyPct, Guid Id)
    {
        public int FulfilledKg { get; init; } = 0;
        /// <summary>Week the contract was signed (game week).</summary>
        public int SignedAtWeek { get; init; } = 0;
        /// <summary>Duration in weeks. Contract expires after this.</summary>
        public int DurationWeeks { get; init; } = 4;
        /// <summary>Minimum quality grade the buyer accepts.</summary>
        public FishQualityGrade MinQualityGrade { get; init; } = FishQualityGrade.Standard;
        /// <summary>Whether the contract is expired (past duration).</summary>
        public bool IsExpired(int currentWeek) => currentWeek > SignedAtWeek + DurationWeeks;
        /// <summary>Whether the contract is fully fulfilled.</summary>
        public bool IsComplete => FulfilledKg >= QuantityKg;
        /// <summary>Remaining kg to deliver.</summary>
        public int RemainingKg => Math.Max(0, QuantityKg - FulfilledKg);
    }

    /// <summary>
    /// Contract system supporting different contract types, acceptance/decline workflow,
    /// quality grading, penalty clauses, forward contract negotiation, and portfolio management.
    /// </summary>
    public class ContractSystem
    {
        public List<Contract> ActiveContracts { get; } = new();
        private readonly List<Contract> _completedContracts = new();
        private readonly Random _rng = new();
        private int _currentWeek = 0;

        /// <summary>Completed/expired contracts for history and analytics.</summary>
        public IReadOnlyList<Contract> CompletedContracts => _completedContracts;

        public event Action<Contract>? OnContractSigned;
        public event Action<Contract>? OnContractFulfilled;
        public event Action<Contract>? OnContractExpired;

        /// <summary>
        /// Propose a contract offer (returns an id that can be accepted/declined).
        /// </summary>
        public ContractOffer ProposeContract(string buyer, ContractType type, decimal pricePerKg, int quantityKg, QualityGrade quality = QualityGrade.Standard, decimal penaltyPct = 0m)
        {
            return new ContractOffer(buyer, type, pricePerKg, quantityKg, quality, penaltyPct);
        }

        /// <summary>
        /// Generate random buyer offers based on current market conditions.
        /// Call weekly; returns 1-3 offers of varying attractiveness.
        /// </summary>
        public List<ContractOffer> GenerateOffers(decimal currentSpotPrice, double brandReputation)
        {
            var offers = new List<ContractOffer>();
            int numOffers = 1 + _rng.Next(3);  // 1-3 offers per week

            string[] buyers = { "SjømatKompaniet", "FiskExport AS", "Nordic Seafood", "EuroFish GmbH",
                                "Tokyo Fish Market", "Whole Foods", "Coop Mega", "Lerøy Markets", "REMA 1000" };

            for (int i = 0; i < numOffers; i++)
            {
                var buyer = buyers[_rng.Next(buyers.Length)];
                var type = _rng.NextDouble() < 0.7 ? ContractType.Forward : ContractType.Exclusive;

                // Price: forward contracts typically offer 5-15% discount vs spot for price certainty
                var discountPct = 0.05m + (decimal)_rng.NextDouble() * 0.10m;
                // Higher reputation = better offers
                var reputationBonus = (decimal)(brandReputation / 200.0);  // Max +0.50 = +50%... no, +5%
                var offerPrice = currentSpotPrice * (1.0m - discountPct + reputationBonus);

                // Volume: 10-200 tonnes
                var volumeTonnes = 10 + _rng.Next(191);
                var volumeKg = volumeTonnes * 1000;

                // Duration: 4-16 weeks
                var duration = 4 + _rng.Next(13);

                // Penalty: 5-15% for forward contracts
                var penalty = type == ContractType.Forward ? 0.05m + (decimal)_rng.NextDouble() * 0.10m : 0m;

                // Quality requirement
                var minGrade = _rng.NextDouble() < 0.3 ? FishQualityGrade.Superior : FishQualityGrade.Standard;

                offers.Add(new ContractOffer(buyer, type, Math.Round(offerPrice, 2), volumeKg,
                    QualityGrade.Standard, Math.Round(penalty, 3))
                {
                    DurationWeeks = duration,
                    MinQualityGrade = minGrade
                });
            }

            return offers;
        }

        /// <summary>
        /// Negotiate an offer — player proposes a counter-price. Returns adjusted offer or null if rejected.
        /// </summary>
        public ContractOffer? NegotiatePrice(ContractOffer original, decimal counterPricePerKg)
        {
            // Buyer has a walk-away price (original + 5-10% max)
            var maxAcceptable = original.PricePerKg * 1.08m;

            if (counterPricePerKg > maxAcceptable)
                return null;  // Buyer walks away

            // Meet in the middle with some randomness
            var midpoint = (original.PricePerKg + counterPricePerKg) / 2.0m;
            var buyerFlexibility = (decimal)(_rng.NextDouble() * 0.4 + 0.3);  // 30-70% towards player's ask
            var agreedPrice = original.PricePerKg + (counterPricePerKg - original.PricePerKg) * buyerFlexibility;

            // Buyer may reduce volume or increase penalty in response to higher price
            var volumeReduction = counterPricePerKg > original.PricePerKg * 1.03m ? 0.9 : 1.0;
            var penaltyIncrease = counterPricePerKg > original.PricePerKg * 1.05m ? 0.02m : 0m;

            return new ContractOffer(
                original.Buyer,
                original.Type,
                Math.Round(agreedPrice, 2),
                (int)(original.QuantityKg * volumeReduction),
                original.Quality,
                Math.Min(0.20m, original.PenaltyPct + penaltyIncrease))
            {
                DurationWeeks = original.DurationWeeks,
                MinQualityGrade = original.MinQualityGrade,
                DestinationMarket = original.DestinationMarket
            };
        }

        /// <summary>
        /// Accept an offer and convert it to an active contract.
        /// </summary>
        public Contract AcceptOffer(ContractOffer offer)
        {
            var c = new Contract(offer.Buyer, offer.Type, offer.PricePerKg, offer.QuantityKg, offer.Quality, offer.PenaltyPct, Guid.NewGuid())
            {
                SignedAtWeek = _currentWeek,
                DurationWeeks = offer.DurationWeeks,
                MinQualityGrade = offer.MinQualityGrade
            };
            ActiveContracts.Add(c);
            OnContractSigned?.Invoke(c);
            return c;
        }

        /// <summary>
        /// Decline an offer (no-op for now, placeholder for negotiation penalty or reputation effects).
        /// </summary>
        public void DeclineOffer(ContractOffer offer)
        {
            // In future: track declined offers, apply reputation changes, etc.
        }

        /// <summary>
        /// Record delivery towards a contract. Returns the payment due for the delivered kg and any penalty applied if delivery is short.
        /// </summary>
        public (decimal payment, decimal penalty) FulfillContract(Guid contractId, int deliveredKg)
        {
            var idx = ActiveContracts.FindIndex(c => c.Id == contractId);
            if (idx == -1) throw new ArgumentException("Contract not found", nameof(contractId));
            var c = ActiveContracts[idx];
            int remaining = Math.Max(0, c.QuantityKg - c.FulfilledKg);
            int toApply = Math.Min(remaining, deliveredKg);

            // Payment is simple: price * applied kg
            decimal payment = toApply * c.PricePerKg;

            // If full quantity not delivered and contract has penalty, apply proportional penalty on shortfall value
            decimal penalty = 0m;
            if (toApply < c.QuantityKg && c.PenaltyPct > 0m)
            {
                int shortfall = c.QuantityKg - toApply;
                decimal shortfallValue = shortfall * c.PricePerKg;
                penalty = shortfallValue * c.PenaltyPct;
            }

            // Update fulfilled amount
            var updated = c with { FulfilledKg = c.FulfilledKg + toApply };
            ActiveContracts[idx] = updated;

            OnContractFulfilled?.Invoke(updated);
            return (payment, penalty);
        }

        /// <summary>
        /// Advance contract system by one week. Checks for expirations and penalties.
        /// </summary>
        public List<(Contract contract, decimal penalty)> AdvanceWeek()
        {
            _currentWeek++;
            var expirations = new List<(Contract, decimal)>();

            for (int i = ActiveContracts.Count - 1; i >= 0; i--)
            {
                var c = ActiveContracts[i];

                if (c.IsComplete)
                {
                    _completedContracts.Add(c);
                    ActiveContracts.RemoveAt(i);
                    continue;
                }

                if (c.IsExpired(_currentWeek))
                {
                    // Calculate penalty for unfulfilled portion
                    decimal penalty = 0m;
                    if (c.RemainingKg > 0 && c.PenaltyPct > 0m)
                    {
                        penalty = c.RemainingKg * c.PricePerKg * c.PenaltyPct;
                    }

                    expirations.Add((c, penalty));
                    _completedContracts.Add(c);
                    ActiveContracts.RemoveAt(i);
                    OnContractExpired?.Invoke(c);
                }
            }

            return expirations;
        }

        /// <summary>
        /// Remove a contract (e.g., after settlement or cancellation).
        /// </summary>
        public bool RemoveContract(Guid contractId)
        {
            var idx = ActiveContracts.FindIndex(c => c.Id == contractId);
            if (idx == -1) return false;
            ActiveContracts.RemoveAt(idx);
            return true;
        }

        // ─── Portfolio Analytics ───

        /// <summary>
        /// Get total committed volume across all active contracts.
        /// </summary>
        public int GetTotalCommittedVolumeKg()
            => ActiveContracts.Sum(c => c.RemainingKg);

        /// <summary>
        /// Get weighted average contracted price.
        /// </summary>
        public decimal GetWeightedAveragePrice()
        {
            var totalQty = ActiveContracts.Sum(c => c.QuantityKg);
            return totalQty > 0
                ? ActiveContracts.Sum(c => c.PricePerKg * c.QuantityKg) / totalQty
                : 0m;
        }

        /// <summary>
        /// Get portfolio summary: how much is hedged vs exposed to spot.
        /// </summary>
        public (int hedgedKg, decimal avgHedgePrice, int contractCount) GetPortfolioSummary()
        {
            var forwardContracts = ActiveContracts.Where(c => c.Type == ContractType.Forward).ToList();
            var hedgedKg = forwardContracts.Sum(c => c.RemainingKg);
            var avgPrice = hedgedKg > 0
                ? forwardContracts.Sum(c => c.PricePerKg * c.RemainingKg) / hedgedKg
                : 0m;
            return (hedgedKg, avgPrice, forwardContracts.Count);
        }

        /// <summary>
        /// Calculate total potential penalty exposure across all active contracts.
        /// </summary>
        public decimal GetTotalPenaltyExposure()
            => ActiveContracts.Sum(c => c.RemainingKg * c.PricePerKg * c.PenaltyPct);
    }
}
