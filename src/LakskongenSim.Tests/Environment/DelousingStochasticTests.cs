using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Health;
using DelousingMethod = LakskongenSim.Data.DelousingMethod;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Treatment handling deaths draw from the per-site delouse
    /// stream: deterministic per seed, mean-preserving vs the legacy
    /// expected-value deaths.</summary>
    public class DelousingStochasticTests
    {
        private static FishPopulation StockedPop(int count = 100_000)
        {
            var pop = new FishPopulation();
            pop.AddCohort(new FishPopulation.Cohort(400, count, 4.2, LakskongenSim.Data.LifeStage.Harvest));
            return pop;
        }

        private static (DelousingSystem, SeaLiceModel, IntegratedDiseaseSystem) Setup()
        {
            var lice = new SeaLiceModel();
            lice.SeedExternalInfection(1.0);
            var disease = new IntegratedDiseaseSystem(lice);
            return (new DelousingSystem(), lice, disease);
        }

        [Fact]
        public void SameStreamSeed_SameDeaths()
        {
            var (d1, lice1, dis1) = Setup();
            var (d2, lice2, dis2) = Setup();
            var s1 = new SimRng(42).Stream(SimRngStreams.Delouse, "site-a", "0");
            var s2 = new SimRng(42).Stream(SimRngStreams.Delouse, "site-a", "0");
            var r1 = d1.Treat(StockedPop(), DelousingMethod.Hydrolicer, lice1, dis1, 10.0, 1.0, rng: s1);
            var r2 = d2.Treat(StockedPop(), DelousingMethod.Hydrolicer, lice2, dis2, 10.0, 1.0, rng: s2);
            Assert.Equal(r1.Deaths, r2.Deaths);
        }

        [Fact]
        public void DifferentSites_Diverge()
        {
            var (d, lice, dis) = Setup();
            var sa = new SimRng(42).Stream(SimRngStreams.Delouse, "site-a", "0");
            var sb = new SimRng(42).Stream(SimRngStreams.Delouse, "site-b", "0");
            var ra = d.Treat(StockedPop(), DelousingMethod.Hydrolicer, lice, dis, 10.0, 1.0, rng: sa);
            var rb = d.Treat(StockedPop(), DelousingMethod.Hydrolicer, lice, dis, 10.0, 1.0, rng: sb);
            // Per-site streams must not march in lockstep (overwhelmingly likely).
            Assert.NotEqual(ra.Deaths, rb.Deaths);
        }

        [Fact]
        public void MeanPreserving_AgainstLegacyExpectedValue()
        {
            var (dLegacy, liceL, disL) = Setup();
            var legacy = dLegacy.Treat(StockedPop(), DelousingMethod.Hydrolicer, liceL, disL, 10.0, 1.0);
            double sum = 0;
            const int draws = 200;
            for (int i = 0; i < draws; i++)
            {
                var (d, lice, dis) = Setup();
                var s = new SimRng(1000 + i).Stream(SimRngStreams.Delouse, "site-m", "0");
                sum += d.Treat(StockedPop(), DelousingMethod.Hydrolicer, lice, dis, 10.0, 1.0, rng: s).Deaths;
            }
            double mean = sum / draws;
            // Normal-approx draws: sample mean within 10% of expectation.
            Assert.InRange(mean, legacy.Deaths * 0.9, legacy.Deaths * 1.1 + 1.0);
        }
    }
}
