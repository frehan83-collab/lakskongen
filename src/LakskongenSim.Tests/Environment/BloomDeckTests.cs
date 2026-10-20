using Xunit;
using LakskongenSim.Environment;
using LakskongenSim.Farm;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 4.3: bloom deck math.</summary>
    public class BloomDeckTests
    {
        [Fact]
        public void Seasons_CorrectWindows()
        {
            Assert.True(BloomEventDeck.InJellySeason(300)); // Oct
            Assert.True(BloomEventDeck.InJellySeason(10)); // Jan
            Assert.False(BloomEventDeck.InJellySeason(150)); // May
            Assert.True(BloomEventDeck.InAlgaeSeason(150));
            Assert.False(BloomEventDeck.InAlgaeSeason(300));
        }

        [Fact]
        public void Bubble_BlocksJellyOnly()
        {
            var site = new SeaSite("s", "z") { HasBubbleCurtain = true };
            Assert.Equal(0.23, BloomEventDeck.CounterMult(site, "jellyfish"), precision: 6);
            Assert.Equal(1.0, BloomEventDeck.CounterMult(site, "hypoxia"));
        }

        [Fact]
        public void Closed_DodgesJellyButWorsensHypoxia()
        {
            var site = new SeaSite("s", "z") { Containment = ContainmentLevel.SemiClosed };
            Assert.Equal(0.0, BloomEventDeck.ContainmentMult(site, "jellyfish"));
            Assert.Equal(0.0, BloomEventDeck.ContainmentMult(site, "algae"));
            Assert.True(BloomEventDeck.ContainmentMult(site, "hypoxia") > 1.0);
        }
    }
}
