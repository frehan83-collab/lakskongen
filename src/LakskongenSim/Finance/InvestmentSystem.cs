using System;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Allows investing in capacity, equipment, and R&amp;D. Tracks book value and simple depreciation.
    /// </summary>
    public class InvestmentSystem
    {
        public decimal TotalInvested { get; private set; }
        public decimal CapacityUnits { get; private set; }
        public decimal EquipmentValue { get; private set; }
        public decimal RAndDValue { get; private set; }

        public event Action<decimal, string>? OnInvested; // amount, category

        public void InvestInCapacity(decimal amount, decimal capacityPerCurrency = 1000m)
        {
            if (amount <= 0) return;
            TotalInvested += amount;
            CapacityUnits += amount / capacityPerCurrency;
            OnInvested?.Invoke(amount, "capacity");
        }

        public void InvestInEquipment(decimal amount)
        {
            if (amount <= 0) return;
            TotalInvested += amount;
            EquipmentValue += amount;
            OnInvested?.Invoke(amount, "equipment");
        }

        public void InvestInRAndD(decimal amount)
        {
            if (amount <= 0) return;
            TotalInvested += amount;
            RAndDValue += amount;
            OnInvested?.Invoke(amount, "rnd");
        }

        public decimal DepreciateEquipment(decimal annualRate, int days)
        {
            var fraction = (decimal)days / LakskongenSim.Core.SimCalendar.DaysPerYear;
            var depreciation = EquipmentValue * annualRate * fraction;
            EquipmentValue = Math.Max(0m, EquipmentValue - depreciation);
            return depreciation;
        }
    }
}
