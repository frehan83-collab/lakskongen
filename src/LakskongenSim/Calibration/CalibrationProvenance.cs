using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Calibration
{
    /// <summary>
    /// Where a calibration value comes from (roadmap F2-04, docs/FARM_TYPES_SPEC.md
    /// §3.9): one or more source keys of <c>docs/research/FARM_TYPES_RESEARCH.md</c>
    /// (<c>L:R2</c>, <c>O:R4</c>, the realism review's <c>V:1</c> …), <c>code</c>
    /// for a value the game already holds (its registry section is named in the
    /// value's doc comment), or a cited publication. Sources are machine-checked
    /// attributes, not comments: <c>CalibrationGuardTests</c> requires every leaf
    /// of the <c>facilities</c> section to carry a <c>[Range]</c> and exactly one
    /// of this attribute or <see cref="GameBalanceAttribute"/>, and the farm-type
    /// catalogue hands the keys to the player's comparison card.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class SourceAttribute : Attribute
    {
        /// <summary>A value with one or more sources.</summary>
        /// <param name="keys">Source keys (at least one, none blank).</param>
        public SourceAttribute(params string[] keys)
        {
            if (keys == null || keys.Length == 0 || keys.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("A [Source] needs at least one non-blank key", nameof(keys));
            Keys = keys;
        }

        /// <summary>The source keys, in the order written.</summary>
        public IReadOnlyList<string> Keys { get; }
    }

    /// <summary>
    /// A value chosen for the game with no direct source (spec "[GB]"): a starting
    /// point whose reason names what it stands in for or the acceptance test that
    /// decides it. The catalogue shows such a value with the key
    /// <see cref="Key"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class GameBalanceAttribute : Attribute
    {
        /// <summary>The source key a game-balance value carries on the player's card.</summary>
        public const string Key = "GB";

        /// <summary>A game-balance value.</summary>
        /// <param name="reason">Why this value (not blank).</param>
        public GameBalanceAttribute(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A [GameBalance] needs a reason", nameof(reason));
            Reason = reason;
        }

        /// <summary>Why this value.</summary>
        public string Reason { get; }
    }
}
