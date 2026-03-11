using System;
using System.Collections.Generic;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Represents a sea site where fish are kept in pens.
    /// </summary>
    public class SeaSite
    {
        public string Id { get; init; }
        public List<Biology.FishPopulation> Pens { get; } = new();

        public event Action<Biology.FishPopulation>? OnStocked;

        public SeaSite(string id)
        {
            Id = id;
        }

        public void Stock(Biology.FishPopulation pop)
        {
            Pens.Add(pop);
            OnStocked?.Invoke(pop);
        }

        /// <summary>Advance all pens by days.</summary>
        public void AdvanceDays(int days)
        {
            foreach (var p in Pens) p.AdvanceDays(days);
        }
    }
}