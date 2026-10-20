using System.Text;

namespace LakskongenSim.Core;

/// <summary>
/// Process-stable hashing for simulation values. <see cref="System.HashCode"/>
/// and <c>string.GetHashCode()</c> are randomized per process, so anything the
/// simulation derives from them differs between launches (and between a save
/// and its load in a new process, review F453). This uses FNV-1a over UTF-8
/// and a SplitMix64 finalizer — the same primitives as <see cref="SimRng"/>.
/// </summary>
public static class StableHash
{
    private const ulong FnvOffset = 0xcbf29ce484222325UL;
    private const ulong FnvPrime = 0x100000001b3UL;

    /// <summary>Stable 64-bit hash of a string (FNV-1a, UTF-8).</summary>
    public static ulong Of(string value)
    {
        ulong hash = FnvOffset;
        foreach (var b in Encoding.UTF8.GetBytes(value ?? ""))
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return hash;
    }

    /// <summary>Stable hash of a string identity and an integer (e.g. zone id + day).</summary>
    public static ulong Of(string value, int salt) => SimRng.Mix(Of(value) ^ (ulong)(uint)salt);

    /// <summary>Stable, order-sensitive hash of two strings (e.g. a zone pair).</summary>
    public static ulong Of(string first, string second) => SimRng.Mix(Of(first) ^ SimRng.Mix(Of(second)));

    /// <summary>Map a stable hash onto [0, 1) with 1/1000 resolution.</summary>
    public static double Unit01(ulong hash) => (hash % 1000UL) / 1000.0;
}
