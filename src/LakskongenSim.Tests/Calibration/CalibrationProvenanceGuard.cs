using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using LakskongenSim.Calibration;

namespace LakskongenSim.Tests.Calibration
{
    /// <summary>One leaf of a calibration section: its dotted path, its property and its value.</summary>
    internal sealed record CalibrationLeaf(string Path, PropertyInfo Property, object? Value)
    {
        /// <summary>The leaf's <c>[Range]</c>, or null.</summary>
        public RangeAttribute? Range => Property.GetCustomAttribute<RangeAttribute>();

        /// <summary>The leaf's <c>[Source]</c>, or null.</summary>
        public SourceAttribute? Source => Property.GetCustomAttribute<SourceAttribute>();

        /// <summary>The leaf's <c>[GameBalance]</c>, or null.</summary>
        public GameBalanceAttribute? GameBalance => Property.GetCustomAttribute<GameBalanceAttribute>();
    }

    /// <summary>
    /// The reflection guard of spec §3.9 (roadmap F2-04): every leaf of a
    /// calibration section carries a <c>[Range]</c> that holds its value and
    /// exactly one of <c>[Source]</c> or <c>[GameBalance]</c>. A leaf is a
    /// number, a bool, a string or an enum (or an array of them); a record
    /// in <c>LakskongenSim.Calibration</c> is walked into. Anything else is
    /// reported, so a new property type cannot slip past the guard.
    /// </summary>
    internal static class CalibrationProvenanceGuard
    {
        /// <summary>Every leaf under <paramref name="section"/>, depth first in declaration order.</summary>
        public static IReadOnlyList<CalibrationLeaf> Leaves(object section, string path)
        {
            var leaves = new List<CalibrationLeaf>();
            Walk(section, path, leaves, new List<string>());
            return leaves;
        }

        /// <summary>Every rule a section breaks, one line each (empty when it keeps them all).</summary>
        public static IReadOnlyList<string> Violations(object section, string path)
        {
            var problems = new List<string>();
            var leaves = new List<CalibrationLeaf>();
            Walk(section, path, leaves, problems);
            foreach (var leaf in leaves)
            {
                int provenance = (leaf.Source != null ? 1 : 0) + (leaf.GameBalance != null ? 1 : 0);
                if (provenance != 1)
                    problems.Add($"{leaf.Path}: needs exactly one [Source] or [GameBalance] (has {provenance})");
                var range = leaf.Range;
                if (range == null)
                {
                    problems.Add($"{leaf.Path}: no [Range]");
                    continue;
                }
                foreach (var value in Values(leaf.Value))
                {
                    double min = Convert.ToDouble(range.Minimum, CultureInfo.InvariantCulture);
                    double max = Convert.ToDouble(range.Maximum, CultureInfo.InvariantCulture);
                    if (value < min || value > max || double.IsNaN(value))
                        problems.Add($"{leaf.Path}: {value.ToString(CultureInfo.InvariantCulture)} outside [Range({min}, {max})]");
                }
            }
            return problems;
        }

        private static IEnumerable<double> Values(object? value) => value switch
        {
            null => Array.Empty<double>(),
            string => Array.Empty<double>(),
            bool b => new[] { b ? 1.0 : 0.0 },
            Enum e => new[] { Convert.ToDouble(e, CultureInfo.InvariantCulture) },
            IEnumerable list => list.Cast<object?>().SelectMany(Values),
            _ => new[] { Convert.ToDouble(value, CultureInfo.InvariantCulture) },
        };

        private static bool IsLeafType(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t.IsArray) return IsLeafType(t.GetElementType()!);
            return t.IsPrimitive || t.IsEnum || t == typeof(decimal) || t == typeof(string);
        }

        /// <summary>A calibration section is a record (the compiler gives records a <c>&lt;Clone&gt;$</c> method).</summary>
        private static bool IsSectionType(Type type)
            => type.IsClass && !type.IsArray && type.GetMethod("<Clone>$") != null;

        private static void Walk(object section, string path, List<CalibrationLeaf> leaves, List<string> problems)
        {
            foreach (var property in section.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || property.GetMethod == null) continue;
                if (property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null) continue;
                string child = path + "." + property.Name;
                object? value = property.GetValue(section);
                if (IsLeafType(property.PropertyType))
                    leaves.Add(new CalibrationLeaf(child, property, value));
                else if (IsSectionType(property.PropertyType) && value != null)
                    Walk(value, child, leaves, problems);
                else
                    problems.Add($"{child}: type {property.PropertyType.Name} is neither a leaf nor a calibration record");
            }
        }
    }
}
