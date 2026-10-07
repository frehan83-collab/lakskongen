using System.Collections.Generic;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Finance;

namespace LakskongenSim.Tests
{
    /// <summary>
    /// Norwegian traffic-light regulatory state machine.
    /// Locks: per-zone green/yellow/red tracking, ±6% MAB consequences with
    /// numeric parity to the legacy event-only tweaks, and gate-off default
    /// outcomes identical to legacy behavior.
    /// </summary>
    public class TrafficLightSystemTests
    {
        private static Zone ZoneWithMab(string id, double mab = 1000.0)
        {
            var zone = new Zone(id) { CarryingCapacityTonnes = mab };
            return zone;
        }

        [Fact]
        public void Gate_DefaultsToOff()
        {
            Assert.False(new TrafficLightSystem().UseStateMachineForEvents);
        }

        [Fact]
        public void UnknownZone_DefaultsToYellowHold()
        {
            var tls = new TrafficLightSystem();
            Assert.Equal(ZoneColor.Yellow, tls.GetZoneColor("PO99"));
            Assert.Equal(1.0, TrafficLightSystem.MabFactorFor(ZoneColor.Yellow));
        }

        [Fact]
        public void MabFactors_MatchLegacyEventTweaks()
        {
            // Parity lock: the machine must never change game balance by itself.
            Assert.Equal(1.06, TrafficLightSystem.GreenMabFactor);
            Assert.Equal(1.0, TrafficLightSystem.YellowMabFactor);
            Assert.Equal(0.94, TrafficLightSystem.RedMabFactor);
            Assert.Equal(1.06, TrafficLightSystem.MabFactorFor(ZoneColor.Green));
            Assert.Equal(0.94, TrafficLightSystem.MabFactorFor(ZoneColor.Red));
        }

        [Theory]
        [InlineData(0.0, ZoneColor.Green)]
        [InlineData(0.5, ZoneColor.Green)] // at/≤0.5 regulatory limit → green
        [InlineData(0.51, ZoneColor.Yellow)]
        [InlineData(0.9, ZoneColor.Yellow)]
        [InlineData(1.0, ZoneColor.Red)] // ≥1.0 → red
        [InlineData(2.5, ZoneColor.Red)]
        public void AssessZone_MapsLiceLoadToColor(double licePerFish, ZoneColor expected)
        {
            var tls = new TrafficLightSystem();
            Assert.Equal(expected, tls.AssessZone("PO04", licePerFish));
            Assert.Equal(expected, tls.GetZoneColor("PO04"));
        }

        [Fact]
        public void SetZoneColor_RaisesChangeEventOnlyOnChange()
        {
            var tls = new TrafficLightSystem();
            var changes = new List<(string, ZoneColor, ZoneColor)>();
            tls.OnZoneStatusChanged += (id, oldC, newC) => changes.Add((id, oldC, newC));

            tls.SetZoneColor("PO04", ZoneColor.Green);
            tls.SetZoneColor("PO04", ZoneColor.Green); // no-op, no event
            tls.SetZoneColor("PO04", ZoneColor.Red);

            Assert.Equal(2, changes.Count);
            Assert.Equal((("PO04"), ZoneColor.Yellow, ZoneColor.Green), changes[0]);
            Assert.Equal((("PO04"), ZoneColor.Green, ZoneColor.Red), changes[1]);
        }

        [Fact]
        public void Zones_TrackedIndependently()
        {
            var tls = new TrafficLightSystem();
            tls.SetZoneColor("PO04", ZoneColor.Green);
            tls.SetZoneColor("PO05", ZoneColor.Red);
            Assert.Equal(ZoneColor.Green, tls.GetZoneColor("PO04"));
            Assert.Equal(ZoneColor.Red, tls.GetZoneColor("PO05"));
            Assert.Equal(ZoneColor.Yellow, tls.GetZoneColor("PO06"));
        }

        [Theory]
        [InlineData(ZoneColor.Green, 1060.0)]
        [InlineData(ZoneColor.Yellow, 1000.0)]
        [InlineData(ZoneColor.Red, 940.0)]
        public void ApplyMABConsequence_ScalesCarryingCapacity(ZoneColor color, double expectedMab)
        {
            var tls = new TrafficLightSystem();
            var zone = ZoneWithMab("PO04");
            tls.SetZoneColor("PO04", color);
            Assert.Equal(TrafficLightSystem.MabFactorFor(color), tls.ApplyMABConsequence(zone), precision: 10);
            Assert.Equal(expectedMab, zone.CarryingCapacityTonnes, precision: 6);
        }

        [Fact]
        public void GateOff_GreenStatus_MatchesLegacyExactly()
        {
            var tls = new TrafficLightSystem(); // gate off
            var gs = new GameState();
            gs.Zones.Add(ZoneWithMab("PO04"));
            gs.Zones.Add(ZoneWithMab("PO05", 780.0));

            tls.ApplyGreenStatus(gs);

            Assert.Equal(1060.0, gs.Zones[0].CarryingCapacityTonnes, precision: 6);
            Assert.Equal(780.0 * 1.06, gs.Zones[1].CarryingCapacityTonnes, precision: 6);
            // Passive observer: no color state tracked when gated off.
            Assert.Equal(ZoneColor.Yellow, tls.GetZoneColor("PO04"));
        }

        [Fact]
        public void GateOn_GreenStatus_TracksGreenWithSameNumericOutcome()
        {
            var tls = new TrafficLightSystem { UseStateMachineForEvents = true };
            var gs = new GameState();
            gs.Zones.Add(ZoneWithMab("PO04"));

            tls.ApplyGreenStatus(gs);

            Assert.Equal(ZoneColor.Green, tls.GetZoneColor("PO04"));
            Assert.Equal(1060.0, gs.Zones[0].CarryingCapacityTonnes, precision: 6);
        }

        [Fact]
        public void GateOff_RedStatus_LeavesMABUntouched()
        {
            var tls = new TrafficLightSystem(); // gate off = legacy: fine only, no MAB change
            var gs = new GameState();
            gs.Zones.Add(ZoneWithMab("PO04"));

            tls.ApplyRedStatus(gs);

            Assert.Equal(1000.0, gs.Zones[0].CarryingCapacityTonnes, precision: 6);
            Assert.Equal(ZoneColor.Yellow, tls.GetZoneColor("PO04"));
        }

        [Fact]
        public void GateOn_RedStatus_AppliesSymmetricCut()
        {
            var tls = new TrafficLightSystem { UseStateMachineForEvents = true };
            var gs = new GameState();
            gs.Zones.Add(ZoneWithMab("PO04"));

            tls.ApplyRedStatus(gs);

            Assert.Equal(ZoneColor.Red, tls.GetZoneColor("PO04"));
            Assert.Equal(940.0, gs.Zones[0].CarryingCapacityTonnes, precision: 6);
        }

        [Fact]
        public void Engine_OwnsTrafficLightSystem_GateOffByDefault()
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false);
            Assert.NotNull(engine.TrafficLightSystem);
            Assert.False(engine.TrafficLightSystem.UseStateMachineForEvents);
        }
    }
}
