using System;
using LakskongenSim.Core;
using Xunit;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// Contract tests for the named deterministic RNG service. The order-
/// independence test is the guard against the stream-sharing bug class:
/// drawing per-identity streams must never be influenced by the order in
/// which identities are created.
/// </summary>
public class SimRngTests
{
    private static double[] Draw3(SimRngStream stream)
        => new[] { stream.NextDouble(), stream.NextDouble(), stream.NextDouble() };

    [Fact]
    public void SameSeed_AndSameStreamNames_ProduceIdenticalSequences()
    {
        var r1 = new SimRng(42);
        var r2 = new SimRng(42);
        Assert.Equal(Draw3(r1.Stream("a")), Draw3(r2.Stream("a")));
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var r1 = new SimRng(1);
        var r2 = new SimRng(2);
        Assert.NotEqual(Draw3(r1.Stream("a")), Draw3(r2.Stream("a")));
    }

    [Fact]
    public void DifferentScopes_AreIndependent()
    {
        var r = new SimRng(7);
        var streamA = r.Stream("a");
        var expectedA = Draw3(streamA);

        var rCompare = new SimRng(7);
        _ = Draw3(rCompare.Stream("b"));   // interleaved draws in another scope
        var streamA2 = rCompare.Stream("a");
        Assert.Equal(expectedA, Draw3(streamA2));
    }

    [Fact]
    public void PerIdentityStreams_AreOrderIndependent()
    {
        // Creating (scope,x) then (scope,y) must not change either sequence
        // compared to creating them in the reversed order.
        var r1 = new SimRng(42);
        var a1 = Draw3(r1.Stream("s", "a"));
        var b1 = Draw3(r1.Stream("s", "b"));

        var r2 = new SimRng(42);
        var b2 = Draw3(r2.Stream("s", "b"));
        var a2 = Draw3(r2.Stream("s", "a"));

        Assert.Equal(a1, a2);
        Assert.Equal(b1, b2);
    }

    [Fact]
    public void RepeatedStreamLookup_ReturnsSameStreamInstance_ContinuingDraws()
    {
        var r = new SimRng(5);
        var s1 = r.Stream("a");
        _ = s1.NextDouble();
        var s2 = r.Stream("a");
        Assert.Same(s1, s2);
    }

    [Fact]
    public void Next_RespectsBounds()
    {
        var r = new SimRng(99);
        var s = r.Stream("bounds");
        for (int i = 0; i < 10_000; i++)
        {
            int v = s.Next(0, 21);
            Assert.InRange(v, 0, 20);
        }
    }

    [Fact]
    public void NextDouble_IsInsideUnitInterval()
    {
        var r = new SimRng(1234);
        var s = r.Stream("unit");
        for (int i = 0; i < 10_000; i++)
        {
            double v = s.NextDouble();
            Assert.True(v >= 0.0 && v < 1.0, $"value out of range: {v}");
        }
    }

    [Fact]
    public void InvalidRanges_Throw()
    {
        var r = new SimRng(1);
        var s = r.Stream("x");
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Next(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Next(5, 5));
    }
}