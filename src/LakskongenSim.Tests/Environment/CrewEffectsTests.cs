using Xunit;
using LakskongenSim.Core;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 3.1: crew effect matrix.</summary>
    public class CrewEffectsTests
    {
        private static GameState StateWith(params string[] roles)
        {
            var s = new GameState();
            foreach (var r in roles)
                s.Crew.Add(new CrewMember { Name = "T", Role = r });
            return s;
        }

        [Fact]
        public void NoCrew_NoEffects()
        {
            var s = StateWith();
            Assert.Equal(1.0, CrewEffects.StormLossMultiplier(s));
            Assert.Equal(1.0, CrewEffects.NetDecayMultiplier(s));
            Assert.Equal(1.0, CrewEffects.TreatmentMortalityMultiplier(s));
            Assert.Equal(1.0m, CrewEffects.WellboatHireMultiplier(s));
            Assert.Equal(0, CrewEffects.WellboatDelayReduction(s));
        }

        [Fact]
        public void FullCrew_AllEffects()
        {
            var s = StateWith("roekter", "veterinaer", "skipper");
            Assert.Equal(0.5, CrewEffects.StormLossMultiplier(s));
            Assert.Equal(0.75, CrewEffects.NetDecayMultiplier(s));
            Assert.Equal(0.5, CrewEffects.TreatmentMortalityMultiplier(s));
            Assert.Equal(0.75m, CrewEffects.WellboatHireMultiplier(s));
            Assert.Equal(1, CrewEffects.WellboatDelayReduction(s));
        }
    }
}
