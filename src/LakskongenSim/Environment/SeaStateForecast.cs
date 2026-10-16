using System;
using System.Collections.Generic;
using LakskongenSim.Calibration;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// The honest sea-state forecast of one plot (roadmap F2-06, spec §3.3 point
    /// 6), issued after the day's tick: for each of the next
    /// <see cref="OffshoreMetoceanCalibration.ForecastHorizonDays"/> days the
    /// exact conditional distribution of the plot's Hs given what the game knows
    /// today. Built by <see cref="MetoceanForecast.Issue(OffshoreMetocean, WeatherSystem, double, IReadOnlyList{double}, bool)"/>;
    /// further limits and quantiles can be asked of it without reissuing.
    /// </summary>
    public sealed class SeaStateForecast
    {
        private readonly MetoceanForecast.DayModel[] _models;
        private readonly double[][] _weatherByLead;

        internal SeaStateForecast(int issuedDay, double plotHsScale, double hsToday, SeaStateForecastDay[] days,
            SwellOutlook[] knownSwell, MetoceanForecast.DayModel[] models, double[][] weatherByLead)
        {
            IssuedDay = issuedDay;
            PlotHsScale = plotHsScale;
            HsTodayM = hsToday;
            Days = days;
            KnownSwell = knownSwell;
            _models = models;
            _weatherByLead = weatherByLead;
        }

        /// <summary>The day the forecast was issued (after that day's tick).</summary>
        public int IssuedDay { get; }

        /// <summary>The plot's Hs scale (its sea over the reference sea).</summary>
        public double PlotHsScale { get; }

        /// <summary>Today's Hs at the plot (m), known exactly.</summary>
        public double HsTodayM { get; }

        /// <summary>Lead 1 first.</summary>
        public IReadOnlyList<SeaStateForecastDay> Days { get; }

        /// <summary>Every swell event the game knows today, earliest onset first.</summary>
        public IReadOnlyList<SwellOutlook> KnownSwell { get; }

        /// <summary>Days ahead the forecast covers.</summary>
        public int HorizonDays => _models.Length;

        /// <summary>P(Hs ≤ <paramref name="limitM"/>) at the plot on the day <paramref name="leadDays"/> ahead (1 = tomorrow).</summary>
        public double ProbabilityAtOrBelow(int leadDays, double limitM)
            => Model(leadDays).Cdf(limitM / PlotHsScale);

        /// <summary>The <paramref name="p"/>-quantile of the plot's Hs (m) on the day <paramref name="leadDays"/> ahead.</summary>
        public double Quantile(int leadDays, double p) => PlotHsScale * Model(leadDays).Quantile(p);

        /// <summary>The fjord weather state distribution on the day <paramref name="leadDays"/> ahead (indexed by <see cref="WeatherState"/>).</summary>
        public IReadOnlyList<double> WeatherDistribution(int leadDays)
        {
            Model(leadDays);
            return _weatherByLead[leadDays - 1];
        }

        private MetoceanForecast.DayModel Model(int leadDays)
        {
            if (leadDays < 1 || leadDays > _models.Length)
                throw new ArgumentOutOfRangeException(nameof(leadDays), leadDays, $"Lead must be 1-{_models.Length} days");
            return _models[leadDays - 1];
        }
    }
}
