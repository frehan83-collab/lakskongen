using System;
using System.Collections.Generic;
using LakskongenSim.Core;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Phase 4.3: IPO and public company mechanics.
    /// - Pre-IPO: private, no guidance pressure
    /// - Post-IPO: quarterly earnings guidance, analyst expectations, share price
    /// - Missed guidance → share price drop, increased cost of capital
    /// - Lock-up periods for insiders
    /// </summary>
    public class IPOSession
    {
        public enum IPOStage
        {
            Private,
            Preparing,      // Hiring advisors, preparing prospectus
            Roadshow,       // Marketing to investors
            Listed          // Publicly traded
        }

        public IPOStage Stage { get; private set; } = IPOStage.Private;

        public event Action<IPOStage, decimal>? OnStageChanged; // new stage, amount raised
        public event Action<string>? OnGuidanceMissed; // message

        private int _sharesOutstanding = 0;
        public int SharesOutstanding => _sharesOutstanding;

        private decimal _sharePriceNOK = 0m;
        public decimal SharePriceNOK => _sharePriceNOK;

        private decimal _marketCapNOK => (decimal)_sharesOutstanding * _sharePriceNOK;
        public decimal MarketCapNOK => _marketCapNOK;

        private decimal _guidanceRevenue = 0m;
        private decimal _guidanceEBITDA = 0m;
        private int _quartersSinceIPO = 0;
        private readonly List<GuidanceRecord> _guidanceHistory = new();

        /// <summary>Initiate IPO process (preparation phase).</summary>
        public bool InitiateIPO(decimal preMoneyValuation, decimal sharesToIssue, decimal targetRaise)
        {
            if (Stage != IPOStage.Private) return false;
            if (preMoneyValuation <= 0m || sharesToIssue <= 0m || targetRaise <= 0m) return false;

            Stage = IPOStage.Preparing;
            OnStageChanged?.Invoke(Stage, 0m);
            return true;
        }

        /// <summary>Complete IPO and list shares.</summary>
        public decimal CompleteIPO(decimal offerPrice, decimal sharesIssued)
        {
            if (Stage != IPOStage.Preparing && Stage != IPOStage.Roadshow) return 0m;
            if (offerPrice <= 0m || sharesIssued <= 0m) return 0m;

            _sharesOutstanding = (int)sharesIssued;
            _sharePriceNOK = offerPrice;
            Stage = IPOStage.Listed;
            _quartersSinceIPO = 0;

            var raised = offerPrice * sharesIssued;
            OnStageChanged?.Invoke(Stage, raised);
            return raised;
        }

        /// <summary>Set quarterly guidance (only when listed).</summary>
        public void SetGuidance(decimal expectedRevenue, decimal expectedEBITDA)
        {
            if (Stage != IPOStage.Listed) return;
            _guidanceRevenue = expectedRevenue;
            _guidanceEBITDA = expectedEBITDA;
        }

        /// <summary>Report actuals vs guidance. Returns miss severity (0 = met, 1 = severe miss).</summary>
        public float ReportActuals(decimal actualRevenue, decimal actualEBITDA, int currentDay)
        {
            if (Stage != IPOStage.Listed) return 0f;
            if (_guidanceRevenue <= 0m && _guidanceEBITDA <= 0m) return 0f; // No guidance set

            _quartersSinceIPO++;

            var revenueMiss = _guidanceRevenue > 0m ? Math.Max(0m, _guidanceRevenue - actualRevenue) / _guidanceRevenue : 0m;
            var ebitdaMiss = _guidanceEBITDA > 0m ? Math.Max(0m, _guidanceEBITDA - actualEBITDA) / _guidanceEBITDA : 0m;

            var severity = (float)Math.Max(revenueMiss, ebitdaMiss);

            _guidanceHistory.Add(new GuidanceRecord
            {
                Quarter = _quartersSinceIPO,
                Day = currentDay,
                GuidedRevenue = _guidanceRevenue,
                GuidedEBITDA = _guidanceEBITDA,
                ActualRevenue = actualRevenue,
                ActualEBITDA = actualEBITDA,
                RevenueMissPct = revenueMiss,
                EBITDAMissPct = ebitdaMiss
            });

            // Missed guidance → share price impact
            if (severity > 0.05f) // >5% miss
            {
                var priceImpact = 1m - (decimal)(severity * 0.5f); // Up to 25% drop for 50% miss
                _sharePriceNOK = Math.Max(1m, _sharePriceNOK * priceImpact);
                OnGuidanceMissed?.Invoke($"Q{_quartersSinceIPO} miss: Revenue {revenueMiss:P1}, EBITDA {ebitdaMiss:P1} → Share price {_sharePriceNOK:N2} NOK");
            }
            else if (severity < -0.05f) // Beat guidance
            {
                var priceImpact = 1m + (decimal)(Math.Abs(severity) * 0.2f); // Up to 10% gain
                _sharePriceNOK *= priceImpact;
            }

            // Reset guidance for next quarter
            _guidanceRevenue = 0m;
            _guidanceEBITDA = 0m;

            return severity;
        }

        /// <summary>Apply market sentiment drift (daily).</summary>
        public void ApplyMarketDrift(SimRngStream rng, float sentimentFactor = 0f)
        {
            if (Stage != IPOStage.Listed) return;
            var drift = (decimal)(rng.NextDouble() - 0.5) * 0.02m; // ±1% daily
            drift += (decimal)sentimentFactor * 0.005m; // Sentiment influence
            _sharePriceNOK = Math.Max(1m, _sharePriceNOK * (1m + drift));
        }

        public IReadOnlyList<GuidanceRecord> GuidanceHistory => _guidanceHistory;

        public void Reset()
        {
            Stage = IPOStage.Private;
            _sharesOutstanding = 0;
            _sharePriceNOK = 0m;
            _guidanceRevenue = 0m;
            _guidanceEBITDA = 0m;
            _quartersSinceIPO = 0;
            _guidanceHistory.Clear();
        }

        public (IPOStage Stage, int SharesOutstanding, decimal SharePriceNOK, int QuartersSinceIPO, List<GuidanceRecord> History) ExportState()
            => (Stage, _sharesOutstanding, _sharePriceNOK, _quartersSinceIPO, new List<GuidanceRecord>(_guidanceHistory));

        internal void ImportState((IPOStage Stage, int SharesOutstanding, decimal SharePriceNOK, int QuartersSinceIPO, List<GuidanceRecord> History) s)
        {
            Stage = s.Stage;
            _sharesOutstanding = s.SharesOutstanding;
            _sharePriceNOK = s.SharePriceNOK;
            _quartersSinceIPO = s.QuartersSinceIPO;
            _guidanceHistory.Clear();
            _guidanceHistory.AddRange(s.History);
        }

        /// <summary>Export state using Core namespace state class for save/load compatibility.</summary>
        public Core.IPOSessionState ExportCoreState()
        {
            return new Core.IPOSessionState
            {
                Stage = Stage.ToString(),
                SharesOutstanding = SharesOutstanding,
                SharePriceNOK = SharePriceNOK,
                QuartersSinceIPO = _quartersSinceIPO,
                GuidanceRecords = _guidanceHistory.Select(g => new Core.GuidanceRecordState
                {
                    Quarter = g.Quarter,
                    Day = g.Day,
                    GuidedRevenue = g.GuidedRevenue,
                    GuidedEBITDA = g.GuidedEBITDA,
                    ActualRevenue = g.ActualRevenue,
                    ActualEBITDA = g.ActualEBITDA,
                    RevenueMissPct = g.RevenueMissPct,
                    EBITDAMissPct = g.EBITDAMissPct
                }).ToList()
            };
        }

        internal void ImportCoreState(Core.IPOSessionState state)
        {
            if (state == null) return;
            Stage = Enum.Parse<IPOStage>(state.Stage);
            _sharesOutstanding = state.SharesOutstanding;
            _sharePriceNOK = state.SharePriceNOK;
            _quartersSinceIPO = (int)state.QuartersSinceIPO;
            _guidanceHistory.Clear();
            var source = state.GuidanceRecords;
            if (source != null)
            {
                _guidanceHistory.AddRange(source.Select(g => new GuidanceRecord
                {
                    Quarter = g.Quarter,
                    Day = g.Day,
                    GuidedRevenue = g.GuidedRevenue,
                    GuidedEBITDA = g.GuidedEBITDA,
                    ActualRevenue = g.ActualRevenue,
                    ActualEBITDA = g.ActualEBITDA,
                    RevenueMissPct = g.RevenueMissPct,
                    EBITDAMissPct = g.EBITDAMissPct
                }));
            }
        }
    }

    public class GuidanceRecord
    {
        public int Quarter { get; set; }
        public int Day { get; set; }
        public decimal GuidedRevenue { get; set; }
        public decimal GuidedEBITDA { get; set; }
        public decimal ActualRevenue { get; set; }
        public decimal ActualEBITDA { get; set; }
        public decimal RevenueMissPct { get; set; }
        public decimal EBITDAMissPct { get; set; }
    }
}