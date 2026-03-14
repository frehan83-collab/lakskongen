using Xunit;
using LakskongenSim.Core;
using System.Linq;
using System.Collections.Generic;

namespace LakskongenSim.Tests.Core
{
    public class MultiplayerTests
    {
        [Fact]
        public void AddPlayer_FirstPlayerIsHost()
        {
            // Arrange
            var manager = new MultiplayerManager();

            // Act
            manager.AddPlayer("Player1", "Company1", "#FF0000");

            // Assert
            var player1 = manager.Players.Values.First();
            Assert.True(player1.IsHost);
            Assert.Single(manager.Players);
        }

        [Fact]
        public void RemovePlayer_HostMigrationWorks()
        {
            // Arrange
            var manager = new MultiplayerManager();
            manager.AddPlayer("HostPlayer", "HostCompany", "#FF0000"); // Player1 is host
            manager.AddPlayer("Player2", "Company2", "#00FF00");
            manager.AddPlayer("Player3", "Company3", "#0000FF");

            var hostPlayerId = manager.Players.Values.First(p => p.IsHost).Id;
            var player2Id = manager.Players.Values.First(p => p.Name == "Player2").Id;

            string newHostId = null;
            manager.OnHostMigrated += (id) => newHostId = id;

            // Act - Host leaves
            manager.RemovePlayer(hostPlayerId);

            // Assert
            Assert.Equal(2, manager.Players.Count);
            Assert.False(manager.Players.ContainsKey(hostPlayerId));
            
            var newHost = manager.Players.Values.First(p => p.IsHost);
            Assert.NotNull(newHost);
            Assert.Equal(player2Id, newHost.Id); // Player2 should be the new host
            Assert.Equal(newHostId, newHost.Id); // Event should fire with new host's ID
        }

        [Fact]
        public void SetPlayerReady_AllPlayersReadyEventFires()
        {
            // Arrange
            var manager = new MultiplayerManager();
            manager.AddPlayer("Player1", "Company1", "#FF0000");
            manager.AddPlayer("Player2", "Company2", "#00FF00");

            bool allReadyEventFired = false;
            manager.OnAllPlayersReady += () => allReadyEventFired = true;

            // Act
            manager.SetPlayerReady(manager.Players.Keys.First(), true); // Player1 ready
            Assert.False(allReadyEventFired); // Should not fire yet

            manager.SetPlayerReady(manager.Players.Keys.Last(), true); // Player2 ready

            // Assert
            Assert.True(allReadyEventFired); // Should fire now
            Assert.True(manager.Players.Values.All(p => p.IsReady));
        }

        [Fact]
        public void CreateAndRespondToTradeOffer_AcceptsAndTransfersBalance()
        {
            // Arrange
            var manager = new MultiplayerManager();
            manager.AddPlayer("PlayerA", "CompanyA", "#111111", 1000m);
            manager.AddPlayer("PlayerB", "CompanyB", "#222222", 500m);
            
            var playerAId = manager.Players.Values.First(p => p.Name == "PlayerA").Id;
            var playerBId = manager.Players.Values.First(p => p.Name == "PlayerB").Id;

            // Start game to allow trading
            manager.SetPlayerReady(playerAId, true);
            manager.SetPlayerReady(playerBId, true);
            manager.StartGame();

            TradeOffer receivedOffer = null;
            manager.OnTradeOffered += (offer) => receivedOffer = offer;
            
            // Act - PlayerA offers to sell fish to PlayerB
            var offer = manager.CreateTradeOffer(playerAId, playerBId, TradeItemType.Fish, 100, 200m);

            // Assert - Offer created and event fired
            Assert.NotNull(offer);
            Assert.Equal(TradeStatus.Pending, offer.Status);
            Assert.Equal(offer.OfferId, receivedOffer.OfferId);

            // Act - PlayerB accepts the offer
            TradeOffer resolvedOffer = null;
            manager.OnTradeResolved += (offer) => resolvedOffer = offer;
            bool accepted = manager.RespondToTradeOffer(offer.OfferId, true);

            // Assert - Offer accepted, event fired, balances updated
            Assert.True(accepted);
            Assert.Equal(TradeStatus.Accepted, offer.Status);
            Assert.Equal(offer.OfferId, resolvedOffer.OfferId);
            Assert.Equal(1200m, manager.Players[playerAId].Balance); // 1000 + 200
            Assert.Equal(300m, manager.Players[playerBId].Balance);  // 500 - 200
        }
    }
}
