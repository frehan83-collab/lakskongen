using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Very small weather system producing temperature anomalies.
    /// </summary>
    public class WeatherSystem
    {
        public double CurrentTemperatureC { get; private set; } = 8.0;

        public event Action<double>? OnTemperatureChanged;

        public void AdvanceDays(int days)
        {
            // crude seasonal cycle
            CurrentTemperatureC = 8.0 + 4.0 * Math.Sin(DateTime.Now.DayOfYear / 365.0 * 2.0 * Math.PI);
            OnTemperatureChanged?.Invoke(CurrentTemperatureC);
        }
    }
}