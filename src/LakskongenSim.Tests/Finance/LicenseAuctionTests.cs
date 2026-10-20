using System.Linq;
using System.Text.Json;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Finance;

namespace LakskongenSim.Tests
{
    /// <summary>
    /// Phase 4.1 — green-zone growth-licence auctions.
    /// Locks: calibration reserve (150M) / build cost (6M), auction window and
    /// bid rules, settlement outcomes (player win, rival win, forfeit, expiry),
    /// rival raise policy bounds, the zone-entry build gate, save round-trip,
    /// and twin-engine determinism of the whole auction history.
    /// </summary>
    public class LicenseAuctionTests
    {
        private const decimal Reserve = 150_000_000m;
        private const decimal BuildCost = 6_000_000m;

        private static SimulationEngine NewEngine(decimal cash = 400_000_000m, int seed = 90411)
        {
            var engine = new SimulationEngine();
            engine.FinanceManager = new FinanceManager(cash);
            engine.Initialize(enableEvents: false, seed: seed);
            engine.State.Zones.Add(new Zone("zone-1"));
            engine.State.Zones.Add(new Zone("zone-2"));
            engine.State.Zones.Add(new Zone("zone-3"));
            return engine;
        }

        /// <summary>Pin rivals so they cannot outbid the player: broke + distressed.</summary>
        private static void SilenceRivals(SimulationEngine engine)
        {
            foreach (var rival in engine.CompetitorAI.Competitors)
            {
                rival.IsDistressed = true;
                rival.CashReserves = 0m;
            }
        }

        // ─── Calibration ─────────────────────────────────────────────────────

        [Fact]
        public void Calibration_ReserveAndBuildCostComeFromCalibrationSpine()
        {
            var engine = NewEngine();
            Assert.Equal(Reserve, engine.LicenseReservePriceNOK);
            Assert.Equal(BuildCost, engine.SiteBuildCostNOK);
        }

        // ─── Opening an auction ──────────────────────────────────────────────

        [Fact]
        public void ApplyForGrowthLicence_OpensSevenDayAuctionAtReserve()
        {
            var engine = NewEngine();
            var result = engine.ApplyForGrowthLicence("zone-1");
            Assert.True(result.Ok, result.Message);

            var auction = Assert.Single(engine.State.LicenseAuctions);
            Assert.Equal("zone-1", auction.ZoneId);
            Assert.Equal("open", auction.Status);
            Assert.Equal(engine.State.Day, auction.OpensDay);
            Assert.Equal(engine.State.Day + LicenseAuctionSystem.AuctionLengthDays, auction.ClosesDay);
            Assert.Equal(Reserve, auction.ReservePriceNOK);
            Assert.Equal(0m, auction.CurrentBidNOK);
            Assert.Equal("none", auction.LeaderKind);
            Assert.True(auction.PlayerEntered);
            Assert.False(auction.ConsumedByBuild);
            Assert.StartsWith($"lic_zone-1_d{engine.State.Day}_", auction.Id);
            Assert.Contains(engine.State.EventLog, e => e.Name == "Growth Licence Auction");
        }

        [Fact]
        public void ApplyForGrowthLicence_RefusesIneligibleZones()
        {
            var engine = NewEngine();

            Assert.Equal("zone_not_found", engine.ApplyForGrowthLicence("nope").ErrorKey);

            engine.TrafficLightSystem.SetZoneColor("zone-3", ZoneColor.Red);
            Assert.Equal("zone_red", engine.ApplyForGrowthLicence("zone-3").ErrorKey);

            // Already operating in the zone → build directly, no auction.
            engine.State.SeaSites.Add(new SeaSite("s1", "zone-1"));
            Assert.Equal("already_operating", engine.ApplyForGrowthLicence("zone-1").ErrorKey);

            // Holds an unconsumed won licence already.
            engine.State.LicenseAuctions.Add(new LicenseAuctionState
            {
                Id = "lic_zone-2_d0_0",
                ZoneId = "zone-2",
                OpensDay = 0,
                ClosesDay = LicenseAuctionSystem.AuctionLengthDays,
                ReservePriceNOK = Reserve,
                Status = "won_player"
            });
            Assert.Equal("licence_held", engine.ApplyForGrowthLicence("zone-2").ErrorKey);

            // One open auction per zone at a time (zone-3 is red → use a fresh zone).
            engine.State.Zones.Add(new Zone("zone-4"));
            Assert.True(engine.ApplyForGrowthLicence("zone-4").Ok);
            Assert.Equal("auction_open", engine.ApplyForGrowthLicence("zone-4").ErrorKey);
        }

        // ─── Bidding rules ───────────────────────────────────────────────────

        [Fact]
        public void MinBid_ReserveFirstThenFivePercentRoundedToWholeMillions()
        {
            var auction = LicenseAuctionSystem.Start("id", "zone-1", 0, Reserve, true);
            Assert.Equal(Reserve, LicenseAuctionSystem.MinBid(auction));

            auction.CurrentBidNOK = Reserve;
            Assert.Equal(158_000_000m, LicenseAuctionSystem.MinBid(auction)); // 157.5M → 158M

            auction.CurrentBidNOK = 200_000_000m;
            Assert.Equal(210_000_000m, LicenseAuctionSystem.MinBid(auction)); // 210M exact
        }

        [Fact]
        public void PlayerBid_RejectsBelowMinOverBalanceUnknownAuctionAndClosed()
        {
            var engine = NewEngine(cash: 160_000_000m);
            Assert.True(engine.ApplyForGrowthLicence("zone-1").Ok);

            Assert.Equal("bid_too_low", engine.BidOnLicenseAuction("zone-1", 149_999_999m).ErrorKey);
            Assert.Equal("insufficient_funds", engine.BidOnLicenseAuction("zone-1", 500_000_000m).ErrorKey);
            Assert.Equal("auction_not_found", engine.BidOnLicenseAuction("zone-2", Reserve).ErrorKey);

            Assert.True(engine.BidOnLicenseAuction("zone-1", Reserve).Ok);
            var auction = Assert.Single(engine.State.LicenseAuctions);
            Assert.Equal("player", auction.LeaderKind);
            Assert.Equal(Reserve, auction.CurrentBidNOK);

            // 150M + 5% rounds up to 158M — a 151M follow-up misses the minimum.
            Assert.Equal("bid_too_low", engine.BidOnLicenseAuction("zone-1", 151_000_000m).ErrorKey);
            Assert.True(engine.BidOnLicenseAuction("zone-1", 158_000_000m).Ok);

            auction.Status = "expired";
            Assert.Equal("auction_closed", engine.BidOnLicenseAuction("zone-1", 200_000_000m).ErrorKey);
        }

        // ─── Settlement outcomes ─────────────────────────────────────────────

        [Fact]
        public void PlayerWin_ChargesBalanceGrantsLicenceAndLogsHistory()
        {
            var engine = NewEngine();
            SilenceRivals(engine);
            Assert.True(engine.ApplyForGrowthLicence("zone-1").Ok);
            Assert.True(engine.BidOnLicenseAuction("zone-1", Reserve).Ok);

            var auction = Assert.Single(engine.State.LicenseAuctions);
            decimal balanceBefore = engine.FinanceManager.Balance;

            engine.State.Day = auction.ClosesDay + 1;
            engine.TickLicenseAuctions();

            Assert.Equal("won_player", auction.Status);
            Assert.Equal(balanceBefore - Reserve, engine.FinanceManager.Balance);
            Assert.True(engine.HasGrowthLicence("zone-1"));
            Assert.Contains(engine.State.EventLog, e => e.Name == "Growth Licence Won");
            Assert.Contains(engine.DrainDayNotes(), n => n.Kind == "license_auction" && n.Success);
        }

        [Fact]
        public void RivalWin_DebitsCashAndGrowsRivalFootprint()
        {
            var engine = NewEngine();
            Assert.True(engine.ApplyForGrowthLicence("zone-1").Ok);
            var auction = Assert.Single(engine.State.LicenseAuctions);

            var rival = engine.CompetitorAI.Competitors.First();
            rival.CashReserves = 500_000_000m;
            decimal cashBefore = rival.CashReserves;
            int licBefore = rival.LicenseCount;
            int sitesBefore = rival.SiteCount;
            float mabBefore = rival.TotalMABTonnes;

            // Inject the rival's best (and only) bid deterministically.
            auction.Bids.Add(new LicenseBid
            {
                Day = engine.State.Day,
                Kind = "rival",
                BidderId = rival.Id,
                AmountNOK = 160_000_000m
            });
            auction.CurrentBidNOK = 160_000_000m;
            auction.LeaderKind = "rival";
            auction.LeaderId = rival.Id;

            engine.State.Day = auction.ClosesDay + 1;
            engine.TickLicenseAuctions();

            Assert.Equal("won_rival", auction.Status);
            Assert.Equal(cashBefore - 160_000_000m, rival.CashReserves);
            Assert.Equal(licBefore + 1, rival.LicenseCount);
            Assert.Equal(sitesBefore + 1, rival.SiteCount);
            Assert.Equal(mabBefore + 780f, rival.TotalMABTonnes);
            Assert.False(engine.HasGrowthLicence("zone-1"));
            Assert.Contains(engine.State.EventLog, e => e.Category == EventCategory.Competitor);
        }

        [Fact]
        public void Forfeits_WhenNoBidderCanActuallyPay()
        {
            var engine = NewEngine(cash: Reserve);
            Assert.True(engine.ApplyForGrowthLicence("zone-1").Ok);
            Assert.True(engine.BidOnLicenseAuction("zone-1", Reserve).Ok);
            var auction = Assert.Single(engine.State.LicenseAuctions);

            // A rival tops the player but does not exist in the roster → cannot pay.
            auction.Bids.Add(new LicenseBid
            {
                Day = engine.State.Day,
                Kind = "rival",
                BidderId = "ghost_rival",
                AmountNOK = 170_000_000m
            });
            auction.CurrentBidNOK = 170_000_000m;
            auction.LeaderKind = "rival";
            auction.LeaderId = "ghost_rival";

            // The player is drained below their own bid → cannot pay either.
            engine.FinanceManager.Spend(engine.FinanceManager.Balance);

            engine.State.Day = auction.ClosesDay + 1;
            engine.TickLicenseAuctions();

            Assert.Equal("forfeited", auction.Status);
            Assert.False(engine.HasGrowthLicence("zone-1"));
        }

        [Fact]
        public void Expires_WhenNobodyBid()
        {
            var engine = NewEngine();
            Assert.True(engine.ApplyForGrowthLicence("zone-1").Ok);
            var auction = Assert.Single(engine.State.LicenseAuctions);

            engine.State.Day = auction.ClosesDay + 1;
            engine.TickLicenseAuctions();

            Assert.Equal("expired", auction.Status);
            Assert.False(engine.HasGrowthLicence("zone-1"));
        }

        // ─── Rival bidding policy ────────────────────────────────────────────

        [Fact]
        public void RivalBids_AtMostOneRaisePerCallWithinCashAndAppetiteBounds()
        {
            var engine = NewEngine();
            var system = engine.LicenseAuctionSystem;
            var auction = LicenseAuctionSystem.Start("a", "zone-1", day: 10, Reserve, false);
            var rivals = new[]
            {
                new CompetitorCompany
                {
                    Id = "r1", CashReserves = 2_000_000_000m,
                    ExpansionAppetite = 0.8f, LobbyingPower = 0.5f
                },
                new CompetitorCompany
                {
                    Id = "r2", CashReserves = 2_000_000_000m,
                    ExpansionAppetite = 0.9f, LobbyingPower = 0.6f
                }
            };

            system.RivalBids(new[] { auction }, rivals, day: 11);

            Assert.InRange(auction.Bids.Count, 0, 1);
            if (auction.Bids.Count == 1)
            {
                var bid = auction.Bids[0];
                Assert.Equal(11, bid.Day);
                Assert.Equal("rival", bid.Kind);
                Assert.Equal("rival", auction.LeaderKind);
                Assert.Equal(bid.BidderId, auction.LeaderId);
                Assert.True(bid.AmountNOK >= Reserve, $"bid {bid.AmountNOK} below reserve");
                var ceiling = LicenseAuctionSystem.MaxRivalPrice(auction,
                    rivals.First(r => r.Id == bid.BidderId));
                Assert.True(bid.AmountNOK <= ceiling,
                    $"bid {bid.AmountNOK} exceeds policy ceiling {ceiling}");
            }
        }

        [Fact]
        public void RivalBids_BrokeRivalsNeverEngage()
        {
            var engine = NewEngine();
            var auction = LicenseAuctionSystem.Start("a", "zone-1", day: 10, Reserve, false);
            var broke = new[]
            {
                new CompetitorCompany { Id = "poor", CashReserves = 100_000_000m, ExpansionAppetite = 1f }
            };

            engine.LicenseAuctionSystem.RivalBids(new[] { auction }, broke, day: 11);

            Assert.Empty(auction.Bids);
            Assert.Equal("none", auction.LeaderKind);
            Assert.Equal(0m, auction.CurrentBidNOK);
        }

        [Fact]
        public void Settle_PicksHighestSolventBidder_ElseFallsThrough()
        {
            var rich = new CompetitorCompany { Id = "rich", CashReserves = 1_000_000_000m };
            var broke = new CompetitorCompany { Id = "broke", CashReserves = 1_000_000m };
            var roster = new[] { rich, broke };

            static LicenseAuctionState AuctionWith(params (string Kind, string Id, decimal Amount)[] bids)
            {
                var a = LicenseAuctionSystem.Start("a", "zone-1", 0, Reserve, false);
                foreach (var b in bids)
                    a.Bids.Add(new LicenseBid
                    { Day = 0, Kind = b.Kind, BidderId = b.Id, AmountNOK = b.Amount });
                a.CurrentBidNOK = bids.Max(b => b.Amount);
                return a;
            }

            // The highest bid (broke rival) cannot pay, nor can the drained
            // player → settlement falls through to the best solvent rival.
            var solvable = AuctionWith(
                ("player", "player", 150_000_000m),
                ("rival", "rich", 165_000_000m),
                ("rival", "broke", 190_000_000m));
            var settleFallsThrough = LicenseAuctionSystem.Settle(solvable, 10, 0m, roster);
            Assert.Equal("won_rival", settleFallsThrough.Status);
            Assert.Equal("rich", settleFallsThrough.WinnerId);
            Assert.Equal(165_000_000m, settleFallsThrough.AmountNOK);

            // Player's bid is the highest solvable one.
            var playerOutbids = AuctionWith(
                ("player", "player", 200_000_000m),
                ("rival", "rich", 190_000_000m));
            var playerWins = LicenseAuctionSystem.Settle(playerOutbids, 10, 400_000_000m, roster);
            Assert.Equal("won_player", playerWins.Status);
            Assert.Equal(200_000_000m, playerWins.AmountNOK);

            // Everyone solvency-broke → forfeited.
            var nobody = AuctionWith(("player", "player", 170_000_000m), ("rival", "rich", 165_000_000m));
            var forfeited = LicenseAuctionSystem.Settle(nobody, 10, 0m,
                new[] { new CompetitorCompany { Id = "rich", CashReserves = 1m } });
            Assert.Equal("forfeited", forfeited.Status);
        }

        // ─── Build gate ──────────────────────────────────────────────────────

        [Fact]
        public void CanBuild_FirstSiteIsDevelopmentRight_ThenLicenceGatesNewZones()
        {
            var engine = NewEngine();

            // No sites yet: the first site may go anywhere (development right).
            Assert.True(engine.CanBuildSeaSite("zone-1", out _));
            Assert.True(engine.CanBuildSeaSite("zone-2", out _));

            Assert.True(engine.BuildSeaSite("zone-1", "site_1").Ok);
            Assert.Equal(BuildCost, 400_000_000m - engine.FinanceManager.Balance);

            // Organic growth inside an operated zone stays free-form.
            Assert.True(engine.CanBuildSeaSite("zone-1", out _));
            // A new zone now requires a won growth licence.
            Assert.False(engine.CanBuildSeaSite("zone-2", out var reason));
            Assert.Contains("growth licence", reason);
            Assert.Equal("growth_licence_required", engine.BuildSeaSite("zone-2", "site_2").ErrorKey);

            // Winning the auction unblocks the build and the licence is consumed.
            engine.State.LicenseAuctions.Add(new LicenseAuctionState
            {
                Id = "lic_zone-2_d0_0",
                ZoneId = "zone-2",
                OpensDay = engine.State.Day,
                ClosesDay = engine.State.Day + LicenseAuctionSystem.AuctionLengthDays,
                ReservePriceNOK = Reserve,
                Status = "won_player"
            });
            Assert.True(engine.HasGrowthLicence("zone-2"));
            Assert.True(engine.CanBuildSeaSite("zone-2", out _));
            Assert.True(engine.BuildSeaSite("zone-2", "site_2").Ok);
            Assert.False(engine.HasGrowthLicence("zone-2"));
            Assert.Equal(2, engine.State.SeaSites.Count);
            Assert.True(engine.CanBuildSeaSite("zone-2", out _)); // now operated

            // Third zone still gated, unknown zone rejected outright.
            Assert.False(engine.CanBuildSeaSite("zone-3", out _));
            Assert.Equal("growth_licence_required", engine.BuildSeaSite("zone-3", "site_3").ErrorKey);
            Assert.False(engine.CanBuildSeaSite("zone-x", out var missing));
            Assert.Contains("not found", missing);
        }

        [Fact]
        public void BuildSeaSite_RequiresConstructionFunds()
        {
            var engine = NewEngine(cash: 1_000_000m);
            Assert.Equal("insufficient_funds", engine.BuildSeaSite("zone-1", "site_1").ErrorKey);
            Assert.Empty(engine.State.SeaSites);
        }

        // ─── Determinism & persistence ───────────────────────────────────────

        [Fact]
        public void TwinEngines_ProduceIdenticalAuctionHistoryAndRivalState()
        {
            static string Run()
            {
                var engine = new SimulationEngine();
                engine.FinanceManager = new FinanceManager(400_000_000m);
                engine.Initialize(enableEvents: false, seed: 777001);
                engine.State.Zones.Add(new Zone("zone-1"));
                engine.State.Zones.Add(new Zone("zone-2"));

                engine.ApplyForGrowthLicence("zone-1");
                engine.BidOnLicenseAuction("zone-1", Reserve);
                engine.Advance(60);

                var rivals = string.Join(";", engine.CompetitorAI.Competitors.Select(
                    c => $"{c.Id}:{c.CashReserves}:{c.LicenseCount}:{c.SiteCount}:{c.TotalMABTonnes}"));
                return JsonSerializer.Serialize(engine.State.LicenseAuctions, SaveJson.Options)
                    + "|" + rivals
                    + "|" + JsonSerializer.Serialize(engine.State.SeaSites, SaveJson.Options);
            }

            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void SaveRoundTrip_PreservesAuctionWindowBidsAndWinners()
        {
            var engine = NewEngine();
            SilenceRivals(engine);
            Assert.True(engine.ApplyForGrowthLicence("zone-1").Ok);
            Assert.True(engine.BidOnLicenseAuction("zone-1", Reserve).Ok);

            var json = JsonSerializer.Serialize(engine.State, SaveJson.Options);
            var snapshot = JsonSerializer.Deserialize<GameState>(json, SaveJson.Options)!;

            var loaded = new SimulationEngine();
            loaded.Initialize(enableEvents: false, seed: 90411);
            loaded.AdoptSavedState(snapshot);

            var auction = Assert.Single(loaded.State.LicenseAuctions);
            Assert.Equal("zone-1", auction.ZoneId);
            Assert.Equal("open", auction.Status);
            Assert.Equal(engine.State.Day, auction.OpensDay);
            Assert.Equal(engine.State.Day + LicenseAuctionSystem.AuctionLengthDays, auction.ClosesDay);
            Assert.Equal(Reserve, auction.CurrentBidNOK);
            Assert.Equal("player", auction.LeaderKind);
            Assert.True(auction.PlayerEntered);
            var bid = Assert.Single(auction.Bids);
            Assert.Equal("player", bid.Kind);
            Assert.Equal(Reserve, bid.AmountNOK);
        }

        [Fact]
        public void RivalInitiatedAuctions_AppearOverLongHorizonWithoutPlayerEntry()
        {
            var engine = new SimulationEngine();
            engine.FinanceManager = new FinanceManager(5_000_000_000m);
            engine.Initialize(enableEvents: false, seed: 41022);
            engine.State.Zones.Add(new Zone("zone-1"));
            engine.State.Zones.Add(new Zone("zone-2"));

            engine.Advance(400);

            Assert.True(engine.State.LicenseAuctions.Count > 0,
                "rivals should have opened at least one auction in 400 days");
            Assert.All(engine.State.LicenseAuctions,
                a => Assert.False(a.PlayerEntered, "the player never entered these auctions"));
        }
    }
}
