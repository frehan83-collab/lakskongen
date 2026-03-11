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

    public record ContractOffer(string Buyer, ContractType Type, decimal PricePerKg, int QuantityKg, QualityGrade Quality, decimal PenaltyPct = 0m);

    public record Contract(string Buyer, ContractType Type, decimal PricePerKg, int QuantityKg, QualityGrade Quality, decimal PenaltyPct, Guid Id)
    {
        public int FulfilledKg { get; init; } = 0;
    }

    /// <summary>
    /// Contract system supporting different contract types, acceptance/decline workflow, quality grading and penalty clauses.
    /// </summary>
    public class ContractSystem
    {
        public List<Contract> ActiveContracts { get; } = new();

        public event Action<Contract>? OnContractSigned;
        public event Action<Contract>? OnContractFulfilled;

        /// <summary>
        /// Propose a contract offer (returns an id that can be accepted/declined).
        /// </summary>
        public ContractOffer ProposeContract(string buyer, ContractType type, decimal pricePerKg, int quantityKg, QualityGrade quality = QualityGrade.Standard, decimal penaltyPct = 0m)
        {
            return new ContractOffer(buyer, type, pricePerKg, quantityKg, quality, penaltyPct);
        }

        /// <summary>
        /// Accept an offer and convert it to an active contract.
        /// </summary>
        public Contract AcceptOffer(ContractOffer offer)
        {
            var c = new Contract(offer.Buyer, offer.Type, offer.PricePerKg, offer.QuantityKg, offer.Quality, offer.PenaltyPct, Guid.NewGuid());
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

            // Payment is simple: price * applied kg. Quality premium adjustments could be applied here.
            decimal payment = toApply * c.PricePerKg;

            // If full quantity not delivered and contract has penalty, apply proportional penalty on shortfall value.
            decimal penalty = 0m;
            if (toApply < c.QuantityKg && c.PenaltyPct > 0m)
            {
                int shortfall = c.QuantityKg - toApply;
                decimal shortfallValue = shortfall * c.PricePerKg;
                penalty = shortfallValue * c.PenaltyPct;
            }

            // Update fulfilled amount by creating a new record (immutable record pattern); replace in list.
            var updated = c with { FulfilledKg = c.FulfilledKg + toApply };
            ActiveContracts[idx] = updated;

            OnContractFulfilled?.Invoke(updated);
            return (payment, penalty);
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
    }
}
