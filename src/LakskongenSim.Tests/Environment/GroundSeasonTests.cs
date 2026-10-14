using System;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// G2-02: the fjord's snow and birch leaves follow the sim calendar and
    /// the scenario's latitude. The terrain shader showed the same snow from
    /// mid-October to mid-March and none at all on a February day at 63°N.
    /// </summary>
    public class GroundSeasonTests
    {
        private const double Bergen = 60.39;
        private const double Trondheim = 63.43;
        private const double Lofoten = 68.2;
        /// <summary>Highest summits of the procedural fjord (TerrainGenerator, seed 42: 1,051 m).</summary>
        private const double FjordSummitM = 1050.0;

        [Fact]
        public void Winter_BringsSnowToTheFjord_AndSummerClearsThePlateaus()
        {
            // Mid-February: snow down to the shore in the inner fjords.
            Assert.InRange(GroundSeasonModel.SnowLineM(Bergen, 46), 0.0, 150.0);
            Assert.InRange(GroundSeasonModel.SnowLineM(Trondheim, 46), 0.0, 100.0);
            Assert.Equal(0.0, GroundSeasonModel.SnowLineM(Lofoten, 46));
            // July: the snow line stands above the summits; only patches remain.
            Assert.True(GroundSeasonModel.SnowLineM(Bergen, 197) > FjordSummitM);
            Assert.True(GroundSeasonModel.SnowLineM(Trondheim, 210) > FjordSummitM);
            // Lofoten in August keeps snow only on the highest ground.
            Assert.InRange(GroundSeasonModel.SnowLineM(Lofoten, 228), 900.0, 1300.0);
        }

        [Fact]
        public void SnowLine_FallsFastInAutumn_AndClimbsSlowlyThroughTheMelt()
        {
            // Equal distance from the August high, the autumn line is lower:
            // snow arrives in weeks and takes months to melt.
            double autumn = GroundSeasonModel.SnowLineM(Bergen, 228 + 75);   // ~30 October
            double spring = GroundSeasonModel.SnowLineM(Bergen, 228 - 75);   // ~1 June
            Assert.True(autumn < spring, $"autumn {autumn:F0} m vs spring {spring:F0} m");
        }

        [Fact]
        public void SnowLine_NeverRisesGoingNorth()
        {
            for (int day = 1; day <= 365; day++)
            {
                double previous = double.MaxValue;
                for (double lat = 58.0; lat <= 71.0; lat += 0.5)
                {
                    double line = GroundSeasonModel.SnowLineM(lat, day);
                    Assert.True(line <= previous + 1e-9, $"day {day}: {line:F1} m at {lat}° above {previous:F1} m further south");
                    previous = line;
                }
            }
        }

        [Fact]
        public void SnowLine_IsContinuousThroughTheYearAndAcrossNewYear()
        {
            foreach (double lat in new[] { Bergen, Trondheim, Lofoten })
            {
                double last = GroundSeasonModel.SnowLineM(lat, 365.95);
                double first = GroundSeasonModel.SnowLineM(lat, 1.0);
                Assert.True(Math.Abs(last - first) < 15.0, $"{lat}°: {last:F1} m on 31 Dec, {first:F1} m on 1 Jan");
                double previous = GroundSeasonModel.SnowLineM(lat, 1.0);
                for (double day = 1.25; day < 366.0; day += 0.25)
                {
                    double line = GroundSeasonModel.SnowLineM(lat, day);
                    Assert.True(Math.Abs(line - previous) < 6.0, $"{lat}° day {day}: jump {line - previous:F2} m");
                    Assert.True(line >= 0.0);
                    previous = line;
                }
            }
        }

        [Fact]
        public void Birch_IsBareInWinter_GreenInSummer_AndYellowInAutumn()
        {
            var january = GroundSeasonModel.At(Bergen, 15);
            Assert.Equal(0.0, january.Foliage01);
            Assert.Equal(0.0, january.Autumn01);
            var july = GroundSeasonModel.At(Bergen, 197);
            Assert.Equal(1.0, july.Foliage01, 6);
            Assert.Equal(0.0, july.Autumn01);
            var early_october = GroundSeasonModel.At(Bergen, 282);
            Assert.True(early_october.Foliage01 > 0.9, $"foliage {early_october.Foliage01:F2}");
            Assert.True(early_october.Autumn01 > 0.9, $"autumn {early_october.Autumn01:F2}");
            var november = GroundSeasonModel.At(Bergen, 310);
            Assert.Equal(0.0, november.Foliage01);
        }

        [Fact]
        public void Birch_LeavesOutLaterAndTurnsEarlierInTheNorth()
        {
            // 20 May: lowland Vestland is greening, Lofoten is still bare.
            Assert.True(GroundSeasonModel.At(Bergen, 140).Foliage01 > GroundSeasonModel.At(Lofoten, 140).Foliage01);
            Assert.Equal(0.0, GroundSeasonModel.At(Lofoten, 140).Foliage01);
            // 20 September: Lofoten yellows first.
            Assert.True(GroundSeasonModel.At(Lofoten, 263).Autumn01 > GroundSeasonModel.At(Bergen, 263).Autumn01);
        }

        [Fact]
        public void Model_IsAPureFunctionOfLatitudeAndDay()
        {
            var a = GroundSeasonModel.At(Trondheim, 123.5);
            var b = GroundSeasonModel.At(Trondheim, 123.5 + 365.0);
            Assert.Equal(a, b);
            Assert.Equal(a, GroundSeasonModel.At(Trondheim, 123.5));
        }
    }
}
