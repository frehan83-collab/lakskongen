using System;
using System.Security.Cryptography;

namespace LakskongenSim.Core;

/// <summary>
/// A deterministic, SNAPSHOT-ABLE RNG whose draw sequence is bit-identical to
/// System.Random(seed) — the Knuth subtractive generator as shipped in .NET
/// (SeedArray warm-up, inext/inexp cursors). Subsystems swap System.Random
/// for this class WITHOUT changing a single drawn value, and gain
/// ExportState/ImportState so a save can persist the exact stream position
/// (Phase 1 wave 6: the enabler for the exact save-hash round-trip test and
/// true mid-run determinism).
///
/// The in-test oracle (SimRandomTests) pins the equivalence against
/// System.Random itself, so any runtime drift is caught immediately.
/// </summary>
public sealed class SimRandom
{
    private const int MBIG = int.MaxValue;
    private const int MSEED = 161803398;

    private readonly int[] _seedArray = new int[56];
    private int _inext;
    private int _inextp;

    public SimRandom(int seed)
    {
        int subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
        int mj = MSEED - subtraction;
        _seedArray[55] = mj;
        int mk = 1;
        for (int i = 1; i < 55; i++)
        {
            int ii = 21 * i % 55;
            if (ii == 0) ii = 55; // net5+ compat detail: index 0 remaps to 55
            _seedArray[ii] = mk;
            mk = mj - mk;
            if (mk < 0) mk += MBIG;
            mj = _seedArray[ii];
        }
        for (int k = 1; k < 5; k++)
        {
            for (int i = 1; i < 56; i++)
            {
                _seedArray[i] -= _seedArray[1 + (i + 30) % 55];
                if (_seedArray[i] < 0) _seedArray[i] += MBIG;
            }
        }
        _inext = 0;
        _inextp = 21; // net5+ compat detail: the second cursor starts at 21, not 31
    }

    /// <summary>Unseeded variant: a crypto-derived seed (the live game always
    /// passes a seed; this exists for legacy entry points).</summary>
    public static SimRandom CreateUnseeded()
        => new(RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue));

    private SimRandom() { }

    public int Next() => InternalSample();

    public int Next(int maxValue)
    {
        if (maxValue < 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "'maxValue' must be non-negative");
        if (maxValue == 0) return 0;
        return (int)(Sample() * maxValue);
    }

    public int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue)
            throw new ArgumentOutOfRangeException(nameof(minValue), "'minValue' cannot be greater than maxValue");
        long range = (long)maxValue - minValue;
        if (range <= int.MaxValue)
            return (int)(Sample() * range) + minValue;
        return (int)((long)(GetSampleForLargeRange() * range) + minValue);
    }

    public double NextDouble() => Sample();

    public double Sample() => InternalSample() * (1.0 / MBIG);

    public float NextSingle() => (float)NextDouble();

    public void NextBytes(byte[] buffer)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (byte)(InternalSample() % 256);
    }

    private int InternalSample()
    {
        int locINext = _inext;
        int locINextp = _inextp;
        if (++locINext >= 56) locINext = 1;
        if (++locINextp >= 56) locINextp = 1;
        int retVal = _seedArray[locINext] - _seedArray[locINextp];
        if (retVal == MBIG) retVal--;
        if (retVal < 0) retVal += MBIG;
        _seedArray[locINext] = retVal;
        _inext = locINext;
        _inextp = locINextp;
        return retVal;
    }

    private double GetSampleForLargeRange()
    {
        int result = InternalSample();
        if (InternalSample() % 2 == 0)
            result = -result;
        double d = result;
        d += (int.MaxValue - 1);
        d /= 2 * (uint)int.MaxValue - 1;
        return d;
    }

    /// <summary>Full stream state: 56 seed-array entries + inext + inexp (58 ints).</summary>
    public int[] ExportState()
    {
        var state = new int[58];
        Array.Copy(_seedArray, 0, state, 0, 56);
        state[56] = _inext;
        state[57] = _inextp;
        return state;
    }

    public void ImportState(int[] state)
    {
        if (state == null || state.Length != 58)
            throw new ArgumentException("SimRandom state must be 58 ints", nameof(state));
        Array.Copy(state, 0, _seedArray, 0, 56);
        _inext = state[56];
        _inextp = state[57];
    }
}