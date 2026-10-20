using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Finance
{
    /// <summary>Outcome of settling one licence auction (Phase 4.1).</summary>
    public sealed class AuctionSettlement
    {
        /// <summary>"won_player" | "won_rival" | "expired" | "forfeited".</summary>
        public string Status { get; set; } = "expired";
        /// <summary>"player" | "rival" | "" (no winner).</summary>
        public string WinnerKind { get; set; } = "";
        /// <summary>Competitor id when the winner is a rival.</summary>
        public string WinnerId { get; set; } = "";
        /// <summary>Amount the winner pays (0 when nobody could pay).</summary>
        public decimal AmountNOK { get; set; }
    }

    /// <summary>
    /// Complete state of a single licence auction (Phase 4.1).
    /// Immutable save/load: all fields serializable via System.Text.Json.
    /// </summary>
    public sealed class LicenseAuctionState
    {
        /// <summary>Unique auction id (e.g., "lic_po3_red_120").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Zone id this auction is for (e.g., "po3_red").</summary>
        public string ZoneId { get; set; } = string.Empty;

        /// <summary>Simulation day the auction opened.</summary>
        public int OpensDay { get; set; } = 0;

        /// <summary>Day the auction closes (OpensDay + AuctionLengthDays).</summary>
        public int ClosesDay { get; set; } = 0;

        /// <summary>Minimum acceptable bid (reserve price, NOK).</summary>
        public decimal ReservePriceNOK { get; set; } = 150_000_000m;

        /// <summary>Current highest bid (NOK). 0 = no bids yet.</summary>
        public decimal CurrentBidNOK { get; set; } = 0m;

        /// <summary>Bidder id of the current high bidder ("player" or rival id).</summary>
        public string CurrentBidderId { get; set; } = string.Empty;

        /// <summary>All bids placed in this auction.</summary>
        public List<LicenseBid> Bids { get; set; } = new();

        /// <summary>Auction status: "open" | "won_player" | "won_rival" | "expired" | "forfeited".</summary>
        public string Status { get; set; } = "open";

        /// <summary>Leader kind: "player" | "rival" | ""</summary>
        public string LeaderKind { get; set; } = string.Empty;

        /// <summary>Leader ID: "player" or rival id</summary>
        public string LeaderId { get; set; } = string.Empty;

        /// <summary>Whether the player has placed a bid in this auction.</summary>
        public bool PlayerEntered { get; set; } = false;

        /// <summary>Whether this auction has been consumed by building a site.</summary>
        public bool ConsumedByBuild { get; set; } = false;

        /// <summary>Deterministic RNG stream position captured at creation for reproducibility.</summary>
        public long RngStreamPosition { get; set; } = 0;
    }

    /// <summary>Individual bid placed in a licence auction.</summary>
    public sealed class LicenseBid
    {
        public string Kind { get; set; } = string.Empty; // "player" | "rival"
        public string BidderId { get; set; } = string.Empty;
        public decimal AmountNOK { get; set; }
        public int Day { get; set; }
    }

    /// <summary>Serializable bid record for save/load (Phase 4.1).</summary>
    public sealed class LicenseBidRecord
    {
        public string Kind { get; set; } = string.Empty;
        public string BidderId { get; set; } = string.Empty;
        public decimal AmountNOK { get; set; }
        public int Day { get; set; }
    }

    /// <summary>
    /// Phase 4.1 — green-zone growth-licence auctions.
    ///
    /// One deterministic RNG stream (created eagerly in Initialize so its
    /// position round-trips through save/load) drives rival engagement, raise
    /// sizing and rival-initiated openings. All draws happen in a fixed,
    /// state-derived order: identical code + identical state = identical
    /// sequences, which is what the twin-run and save-hash gates assert.
    ///
    /// Rival pricing policy: a rival will go up to
    /// reserve × (1.08 + 0.55·ExpansionAppetite + 0.15·LobbyingPower),
    /// never more than 60% of its cash reserves, and raises by at most ~20%
    /// above the minimum per day so auctions escalate over the window instead
    /// of snapping to the cap on day one.
    /// </summary>
    public class LicenseAuctionSystem
    {
        /// <summary>Auction length in days (opens day D, settles after D+7).</summary>
        public const int AuctionLengthDays = 7;

        /// <summary>Chance per day an eligible rival engages with an auction.</summary>
        public const double RivalEngageBaseChance = 0.30;

        /// <summary>Extra engage chance added by a maxed ExpansionAppetite.</summary>
        public const double RivalEngageAppetiteBonus = 0.50;

        /// <summary>Daily chance a rival initiates an auction when none is open.</summary>
        public const double RivalOpenChance = 0.02;

        /// <summary>Share of cash reserves a rival may bid above (liquidity guard).</summary>
        public const decimal RivalCashReserveFraction = 0.60m;

        /// <summary>
        /// Days a bid on the closing day keeps the auction open (U2-01 activity
        /// rule): one more round for the rivals to answer. Fiskeridirektoratet
        /// runs the capacity auctions as simultaneous clock auctions (2018,
        /// 2020, 2022, 2024) that close only once a round brings no new demand
        /// (regjeringen.no, "Auksjon av oppdrettstillatelser holdes 24. juni", 2024).
        /// </summary>
        public const int ClosingRoundExtensionDays = 1;

        /// <summary>
        /// Average price per tonne of MTB in the state's 2022 capacity auction
        /// (NOK/t): 24,644 t for about NOK 3.8 bn (regjeringen.no / iLaks,
        /// October 2022). The low end of the band the auction card quotes.
        /// </summary>
        public const decimal StateAuction2022AveragePerTonneNok = 154_000m;

        /// <summary>
        /// Average price per tonne of MTB in the state's 2024 capacity auction
        /// (NOK/t): 17,330 t for NOK 5,288,602,600 (regjeringen.no, "Resultater
        /// fra auksjon av nye oppdrettstillatelser 2024", 25 June 2024).
        /// </summary>
        public const decimal StateAuction2024AveragePerTonneNok = 305_000m;

        /// <summary>
        /// The highest production-area average of the 2024 auction (NOK/t; PO9,
        /// Vestfjorden og Vesterålen, from the same results page).
        /// </summary>
        public const decimal StateAuction2024HighestAreaPerTonneNok = 348_400m;

        private readonly SimRngStream _rng;

        public LicenseAuctionSystem(SimRngStream rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// Minimum next bid: the reserve while no bids exist, otherwise the
        /// current high bid plus at least 5%, rounded up to whole millions.
        /// </summary>
        public static decimal MinBid(LicenseAuctionState auction)
        {
            if (auction.CurrentBidNOK <= 0m) return auction.ReservePriceNOK;
            return CeilMillion(auction.CurrentBidNOK * 1.05m);
        }

        private static decimal CeilMillion(decimal value)
            => System.Math.Ceiling(value / 1_000_000m) * 1_000_000m;

        /// <summary>Create a fresh open auction (id assigned by the caller).</summary>
        public static LicenseAuctionState Start(
            string id, string zoneId, int day, decimal reservePriceNOK, bool playerEntered)
        {
            return new LicenseAuctionState
            {
                Id = id,
                ZoneId = zoneId,
                OpensDay = day,
                ClosesDay = day + AuctionLengthDays,
                ReservePriceNOK = reservePriceNOK,
                CurrentBidNOK = 0m,
                LeaderKind = "none",
                LeaderId = "",
                Status = "open",
                PlayerEntered = playerEntered
            };
        }

        /// <summary>
        /// Place the player's bid. Returns false with a stable error key when
        /// the auction is closed, the bid misses the minimum, or the balance
        /// cannot cover it (bids are not escrowed — affordability is re-checked
        /// at settlement).
        /// </summary>
        public static bool TryPlayerBid(
            LicenseAuctionState auction,
            decimal amountNOK,
            decimal playerBalance,
            int day,
            out string errorKey,
            out string message)
        {
            errorKey = "";
            message = "";
            if (auction.Status != "open")
            {
                errorKey = "auction_closed";
                message = $"Auction '{auction.Id}' is closed";
                return false;
            }
            if (day > auction.ClosesDay)
            {
                errorKey = "auction_closed";
                message = $"Auction '{auction.Id}' closed on day {auction.ClosesDay}";
                return false;
            }
            decimal min = MinBid(auction);
            if (amountNOK < min)
            {
                errorKey = "bid_too_low";
                message = $"Minimum bid is {min:N0} NOK";
                return false;
            }
            if (amountNOK > playerBalance)
            {
                errorKey = "insufficient_funds";
                message = $"Insufficient funds: balance is {playerBalance:N0} NOK";
                return false;
            }
            auction.CurrentBidNOK = amountNOK;
            auction.LeaderKind = "player";
            auction.LeaderId = "player";
            auction.PlayerEntered = true;
            // Activity rule (U2-01): the state's capacity auctions are clock
            // auctions that run while anyone still bids, so a bid on the last
            // day keeps the auction open one more round and the rivals answer
            // it; a last-second bid cannot end the auction unanswered.
            if (day >= auction.ClosesDay)
                auction.ClosesDay = day + ClosingRoundExtensionDays;
            auction.Bids.Add(new LicenseBid
            {
                Day = day,
                Kind = "player",
                BidderId = "player",
                AmountNOK = amountNOK
            });
            return true;
        }

        /// <summary>
        /// One daily raise pass over every open auction. Each auction gets at
        /// most one successful raise per day (first rival in roster order that
        /// engages wins the slot). Draws are taken for every non-distressed,
        /// non-leading rival so stream consumption is a pure function of state.
        /// </summary>
        public void RivalBids(
            IReadOnlyList<LicenseAuctionState> auctions,
            IReadOnlyList<CompetitorCompany> rivals,
            int day)
        {
            foreach (var auction in auctions)
            {
                if (auction.Status != "open" || day > auction.ClosesDay) continue;
                foreach (var rival in rivals)
                {
                    if (rival.IsDistressed) continue;
                    if (auction.LeaderKind == "rival" && auction.LeaderId == rival.Id) continue;

                    decimal minNext = MinBid(auction);
                    decimal maxPrice = MaxRivalPrice(auction, rival);
                    if (maxPrice < minNext) continue; // cannot cover the minimum raise

                    double engageChance = RivalEngageBaseChance
                        + RivalEngageAppetiteBonus * rival.ExpansionAppetite;
                    if (_rng.NextDouble() >= engageChance) continue;

                    decimal stretch = CeilMillion(
                        minNext * (decimal)(1.05 + 0.15 * _rng.NextDouble()));
                    decimal bid = System.Math.Min(maxPrice, stretch);
                    if (bid < minNext) bid = minNext;

                    auction.CurrentBidNOK = bid;
                    auction.LeaderKind = "rival";
                    auction.LeaderId = rival.Id;
                    auction.Bids.Add(new LicenseBid
                    {
                        Day = day,
                        Kind = "rival",
                        BidderId = rival.Id,
                        AmountNOK = bid
                    });
                    break; // one raise per auction per day
                }
            }
        }

        /// <summary>
        /// A rival's absolute ceiling for an auction: appetite/lobby-scaled
        /// premium over the reserve, never above the cash-liquidity guard.
        /// </summary>
        public static decimal MaxRivalPrice(LicenseAuctionState auction, CompetitorCompany rival)
        {
            double premium = 1.08
                + 0.55 * rival.ExpansionAppetite
                + 0.15 * rival.LobbyingPower;
            decimal ceiling = auction.ReservePriceNOK * (decimal)premium;
            decimal cashCap = rival.CashReserves * RivalCashReserveFraction;
            return ceiling < cashCap ? ceiling : cashCap;
        }

        /// <summary>
        /// Settle a closed auction: walk the best bid per bidder highest-first
        /// and the first bidder who can actually pay wins at that amount. No
        /// bids → expired; bids but nobody solvent → forfeited.
        /// </summary>
        public static AuctionSettlement Settle(
            LicenseAuctionState auction,
            int day,
            decimal playerBalance,
            IReadOnlyList<CompetitorCompany> rivals)
        {
            if (auction.Bids.Count == 0)
            {
                auction.Status = "expired";
                return new AuctionSettlement { Status = "expired" };
            }

            var candidates = new List<(string Kind, string Id, decimal Amount)>();
            decimal playerBest = auction.Bids
                .Where(b => b.Kind == "player")
                .Select(b => b.AmountNOK)
                .DefaultIfEmpty(0m)
                .Max();
            if (playerBest > 0m)
                candidates.Add(("player", "player", playerBest));

            foreach (var group in auction.Bids.Where(b => b.Kind == "rival").GroupBy(b => b.BidderId))
            {
                decimal best = group.Max(b => b.AmountNOK);
                candidates.Add(("rival", group.Key, best));
            }

            candidates.Sort((x, y) => y.Amount.CompareTo(x.Amount));

            foreach (var candidate in candidates)
            {
                if (candidate.Kind == "player")
                {
                    if (playerBalance >= candidate.Amount)
                    {
                        auction.Status = "won_player";
                        return new AuctionSettlement
                        {
                            Status = "won_player",
                            WinnerKind = "player",
                            AmountNOK = candidate.Amount
                        };
                    }
                    continue;
                }

                var rival = rivals.FirstOrDefault(r => r.Id == candidate.Id);
                if (rival != null && rival.CashReserves >= candidate.Amount)
                {
                    auction.Status = "won_rival";
                    return new AuctionSettlement
                    {
                        Status = "won_rival",
                        WinnerKind = "rival",
                        WinnerId = rival.Id,
                        AmountNOK = candidate.Amount
                    };
                }
            }

            auction.Status = "forfeited";
            return new AuctionSettlement { Status = "forfeited" };
        }

        /// <summary>
        /// Rival-initiated opening: with a small daily chance while no auction
        /// is open and at least one eligible zone exists, open a fresh auction.
        /// Returns null when the market stays quiet.
        /// </summary>
        public LicenseAuctionState? MaybeOpenRivalAuction(
            string id,
            IReadOnlyList<string> eligibleZoneIds,
            int day,
            decimal reservePriceNOK)
        {
            if (eligibleZoneIds.Count == 0) return null;
            if (_rng.NextDouble() >= RivalOpenChance) return null;
            string zoneId = eligibleZoneIds[_rng.Next(eligibleZoneIds.Count)];
            return Start(id, zoneId, day, reservePriceNOK, playerEntered: false);
        }
    }
}
