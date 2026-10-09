using System;
using LakskongenSim.Core;
using Xunit;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// Bit-equivalence oracle: SimRandom must draw EXACTLY what System.Random
/// draws, and ExportState/ImportState must restore the precise position so
/// a saved stream continues identically. This is the golden foundation for
/// the exact save-hash round-trip test.
/// </summary>
public class SimRandomTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(1234567)]
    [InlineData(-5)]
    [InlineData(int.MaxValue - 1)]
    [InlineData(int.MinValue + 1)]
    public void Draws_BitIdentical_ToSystemRandom(int seed)
    {
        var sys = new Random(seed);
        var sim = new SimRandom(seed);

        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(sys.Next(), sim.Next());
            Assert.Equal(sys.Next(100), sim.Next(100));
            Assert.Equal(sys.Next(1), sim.Next(1));
            Assert.Equal(sys.Next(-60, 777), sim.Next(-60, 777));
            Assert.Equal(sys.Next(int.MaxValue), sim.Next(int.MaxValue));
            Assert.Equal(sys.NextDouble(), sim.NextDouble());
            Assert.Equal(sys.NextSingle(), sim.NextSingle());
            var bs = new byte[8];
            var bm = new byte[8];
            sys.NextBytes(bs);
            sim.NextBytes(bm);
            Assert.Equal(bs, bm);
        }
    }

    [Fact]
    public void LargeRange_Draws_BitIdentical_ToSystemRandom()
    {
        var sys = new Random(7);
        var sim = new SimRandom(7);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(sys.Next(int.MinValue, int.MaxValue), sim.Next(int.MinValue, int.MaxValue));
            Assert.Equal(sys.Next(-2_000_000_000, 2_000_000_000), sim.Next(-2_000_000_000, 2_000_000_000));
        }
    }

    [Fact]
    public void ExportImport_ResumesTheExactSequence()
    {
        var sim = new SimRandom(42);
        var sys = new Random(42);

        // Consume 100 draws, snapshot, consume 100 more from the ORACLE...
        for (int i = 0; i < 100; i++)
        {
            _ = sim.NextDouble();
            _ = sys.NextDouble();
        }
        int[] snapshot = sim.ExportState();

        // ...while the restored stream continues from the snapshot point:
        var resumed = new SimRandom(0);
        resumed.ImportState(snapshot);
        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(sys.NextDouble(), resumed.NextDouble());
        }
    }

    [Fact]
    public void ImportState_Rejects_MalformedStates()
    {
        var sim = new SimRandom(1);
        Assert.Throws<ArgumentException>(() => sim.ImportState(new int[57]));
        Assert.Throws<ArgumentException>(() => sim.ImportState(null!));
    }
}