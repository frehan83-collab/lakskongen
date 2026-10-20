using System;
using System.Collections.Generic;
using System.Globalization;
using LakskongenSim.Events;
using LakskongenSim.Market;

namespace LakskongenSim.Core;

/// <summary>
/// Contract offers and signing as engine verbs (previously generated and
/// accepted in the Godot bridge). Offers for a day are drawn from a stream
/// derived from (seed, day) that is NOT part of the persisted RNG state, so
/// opening the contracts panel can never change the simulation, and the same
/// day always shows the same offers — including after a load.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Scope for the per-day offer stream (ephemeral, never saved).</summary>
    internal const string ContractOfferStreamScope = "contract_offers";

    /// <summary>Today's contract offers (generated once per game day).</summary>
    public IReadOnlyList<ContractOffer> GetContractOffers()
    {
        if (State == null || ContractSystem == null || MarketSimulation == null)
            return Array.Empty<ContractOffer>();
        if (ContractSystem.OfferDay != State.Day)
        {
            double brand = (double)(BrandReputation?.QualityReputation ?? 50m) / 100.0;
            var dayStream = new SimRng(State.RandomSeed).Stream(ContractOfferStreamScope,
                State.Day.ToString(CultureInfo.InvariantCulture));
            ContractSystem.SetOffers(State.Day,
                ContractSystem.GenerateOffers(MarketSimulation.PricePerKg, brand, dayStream));
        }
        return ContractSystem.CurrentOffers;
    }

    /// <summary>Sign the offer at <paramref name="index"/> of <see cref="GetContractOffers"/>.</summary>
    public CommandResult AcceptContractOffer(int index)
    {
        if (State == null || ContractSystem == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var offers = GetContractOffers();
        if (index < 0 || index >= offers.Count)
            return CommandResult.Fail("offer_not_found", $"No contract offer at index {index}");

        var contract = ContractSystem.AcceptOfferAt(index);
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Kontrakt signert",
            Category = EventCategory.Market,
            Description = $"Kontrakt med {contract.Buyer}: {contract.QuantityKg:N0} kg @ {contract.PricePerKg:N0} NOK/kg ({contract.DurationWeeks} uker)",
            BalanceChange = 0m
        });
        State.TrimEventLog();
        return CommandResult.Success(
            $"Contract signed with {contract.Buyer}: {contract.QuantityKg:N0} kg @ {contract.PricePerKg:N0} NOK/kg",
            value: contract.QuantityKg, value2: (double)contract.PricePerKg, id: contract.Id.ToString());
    }
}
