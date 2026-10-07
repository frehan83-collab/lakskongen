using Xunit;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 2.4: wellboat job DTO sanity (engine flow covered in-game).</summary>
    public class WellboatJobTests
    {
        [Fact]
        public void Job_Defaults()
        {
            var job = new Logistics.WellboatJob { SiteId = "s1", Kind = "harvest", DueDay = 10 };
            Assert.Equal("s1", job.SiteId);
            Assert.Equal(10, job.DueDay);
            Assert.False(string.IsNullOrEmpty(job.Id));
        }
    }
}
