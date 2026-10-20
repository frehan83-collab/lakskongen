// =============================================================================
// MarketChannel.cs - Sales Channel System
// =============================================================================
// Implements multiple sales channels with different risk/reward profiles:
// - Spot market with bid-ask spread and volume slippage
// - Forward contracts with negotiation
// - Export markets (EU, Asia, North America) with base price differentials
// - Direct sales (retail, higher margin, lower volume)
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Sales channel type determining pricing mechanics.
    /// </summary>
    public enum SalesChannelType
    {
        SpotMarket,
        ForwardContract,
        ExportEU,
        ExportAsia,
        ExportNorthAmerica,
        DirectRetail
    }

    /// <summary>
    /// Configuration for a sales channel including pricing, capacity, and requirements.
    /// </summary>
    public class SalesChannel
    {
        public SalesChannelType Type { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";

        /// <summary>Base price multiplier relative to Norwegian spot (FPI).</summary>
        public decimal BasePriceMultiplier { get; init; } = 1.0m;

        /// <summary>Bid-ask spread as fraction (e.g., 0.02 = 2% spread). Player sells at bid.</summary>
        public decimal BidAskSpread { get; init; } = 0.0m;

        /// <summary>Volume slippage: price impact per 100 tonnes sold. Larger volumes = worse price.</summary>
        public decimal SlippagePerHundredTonnes { get; init; } = 0.0m;

        /// <summary>Maximum volume (kg) sellable per week through this channel.</summary>
        public int MaxWeeklyVolumeKg { get; init; } = int.MaxValue;

        /// <summary>Minimum volume (kg) per transaction.</summary>
        public int MinTransactionKg { get; init; } = 0;

        /// <summary>Logistics cost as fraction of sale price.</summary>
        public decimal LogisticsCostFraction { get; init; } = 0.0m;

        /// <summary>Payment delay in days (spot = immediate, export = 30-60 days).</summary>
        public int PaymentDelayDays { get; init; } = 0;

        /// <summary>Whether this channel is currently accessible to the player.</summary>
        public bool IsAccessible { get; set; } = true;

        /// <summary>Required certifications for this channel.</summary>
        public List<string> RequiredCertifications { get; init; } = new();

        /// <summary>Minimum quality grade accepted.</summary>
        public FishQualityGrade MinQualityGrade { get; init; } = FishQualityGrade.Inferior;
    }

    /// <summary>
    /// Result of a sale through a channel.
    /// </summary>
    public record SaleResult
    {
        public SalesChannelType Channel { get; init; }
        public int VolumeKg { get; init; }
        public decimal GrossRevenue { get; init; }
        public decimal LogisticsCost { get; init; }
        public decimal SlippageCost { get; init; }
        public decimal SpreadCost { get; init; }
        public decimal NetRevenue => GrossRevenue - LogisticsCost - SlippageCost - SpreadCost;
        public decimal EffectivePricePerKg => VolumeKg > 0 ? NetRevenue / VolumeKg : 0m;
        public int PaymentDelayDays { get; init; }
        public string Message { get; init; } = "";
    }

    /// <summary>
    /// Manages all sales channels and executes sales with realistic pricing mechanics.
    /// </summary>
    public class MarketChannelSystem
    {
        private readonly Dictionary<SalesChannelType, SalesChannel> _channels = new();
        private readonly Dictionary<SalesChannelType, int> _weeklyVolumeUsed = new();

        public event Action<SaleResult>? OnSaleCompleted;
        public event Action<SalesChannelType, bool>? OnChannelAccessChanged;

        public MarketChannelSystem()
        {
            InitializeChannels();
        }

        private void InitializeChannels()
        {
            // Spot market: immediate sale, volatile, bid-ask spread, volume slippage
            _channels[SalesChannelType.SpotMarket] = new SalesChannel
            {
                Type = SalesChannelType.SpotMarket,
                Name = "Spotmarked",
                Description = "Immediate sale at current market price. Large volumes move the price.",
                BasePriceMultiplier = 1.0m,
                BidAskSpread = 0.025m,      // 2.5% spread (player gets bid price)
                SlippagePerHundredTonnes = 0.015m,  // 1.5% per 100t
                MaxWeeklyVolumeKg = 500_000,  // 500 tonnes/week
                MinTransactionKg = 1_000,     // 1 tonne minimum
                LogisticsCostFraction = 0.02m,
                PaymentDelayDays = 3,
                MinQualityGrade = FishQualityGrade.Production
            };

            // Export EU: steady demand, moderate premium, low logistics cost (EEA)
            _channels[SalesChannelType.ExportEU] = new SalesChannel
            {
                Type = SalesChannelType.ExportEU,
                Name = "EU Export (France, Spain, Germany)",
                Description = "Reliable European demand. EEA advantage means low tariffs.",
                BasePriceMultiplier = 1.08m,  // 8% premium vs domestic
                BidAskSpread = 0.015m,
                SlippagePerHundredTonnes = 0.005m,
                MaxWeeklyVolumeKg = 300_000,
                MinTransactionKg = 5_000,
                LogisticsCostFraction = 0.06m,
                PaymentDelayDays = 30,
                MinQualityGrade = FishQualityGrade.Standard
            };

            // Export Asia: highest premium, demanding quality, high logistics
            _channels[SalesChannelType.ExportAsia] = new SalesChannel
            {
                Type = SalesChannelType.ExportAsia,
                Name = "Asia Export (Japan, South Korea, China)",
                Description = "Premium sashimi market. Strict quality, highest prices.",
                BasePriceMultiplier = 1.25m,  // 25% premium
                BidAskSpread = 0.03m,
                SlippagePerHundredTonnes = 0.02m,
                MaxWeeklyVolumeKg = 100_000,
                MinTransactionKg = 2_000,
                LogisticsCostFraction = 0.12m,
                PaymentDelayDays = 45,
                RequiredCertifications = new List<string> { "ASC" },
                MinQualityGrade = FishQualityGrade.Superior,
                IsAccessible = false  // Must be unlocked
            };

            // Export North America: good premium, moderate logistics
            _channels[SalesChannelType.ExportNorthAmerica] = new SalesChannel
            {
                Type = SalesChannelType.ExportNorthAmerica,
                Name = "North America Export (USA, Canada)",
                Description = "Growing demand for Norwegian salmon. Sustainability-focused buyers.",
                BasePriceMultiplier = 1.15m,  // 15% premium
                BidAskSpread = 0.02m,
                SlippagePerHundredTonnes = 0.01m,
                MaxWeeklyVolumeKg = 200_000,
                MinTransactionKg = 5_000,
                LogisticsCostFraction = 0.10m,
                PaymentDelayDays = 45,
                RequiredCertifications = new List<string> { "ASC" },
                MinQualityGrade = FishQualityGrade.Standard,
                IsAccessible = false  // Must be unlocked
            };

            // Direct retail: highest margin, small volumes, quality-sensitive
            _channels[SalesChannelType.DirectRetail] = new SalesChannel
            {
                Type = SalesChannelType.DirectRetail,
                Name = "Direct Retail Sales",
                Description = "Sell direct to restaurants and retailers. Best margins, smallest volume.",
                BasePriceMultiplier = 1.35m,  // 35% premium
                BidAskSpread = 0.0m,          // No spread (you set the price)
                SlippagePerHundredTonnes = 0.0m,
                MaxWeeklyVolumeKg = 20_000,   // Only 20 tonnes/week
                MinTransactionKg = 100,        // Small batches OK
                LogisticsCostFraction = 0.04m,
                PaymentDelayDays = 14,
                MinQualityGrade = FishQualityGrade.Superior,
                IsAccessible = false  // Must build brand reputation
            };
        }

        /// <summary>
        /// Execute a sale through a specific channel.
        /// </summary>
        public SaleResult Sell(SalesChannelType channelType, int volumeKg, decimal baseSpotPrice, GradingResult grading)
        {
            if (!_channels.TryGetValue(channelType, out var channel))
                return new SaleResult { Channel = channelType, Message = "Invalid channel" };

            if (!channel.IsAccessible)
                return new SaleResult { Channel = channelType, Message = $"{channel.Name} is not yet accessible" };

            if (grading.Grade > channel.MinQualityGrade)
                return new SaleResult { Channel = channelType, Message = $"Quality grade {grading.Grade} below minimum {channel.MinQualityGrade} for {channel.Name}" };

            if (volumeKg < channel.MinTransactionKg)
                return new SaleResult { Channel = channelType, Message = $"Minimum transaction: {channel.MinTransactionKg} kg" };

            // Check weekly volume cap
            var usedThisWeek = _weeklyVolumeUsed.GetValueOrDefault(channelType, 0);
            var availableVolume = Math.Max(0, channel.MaxWeeklyVolumeKg - usedThisWeek);
            var actualVolume = Math.Min(volumeKg, availableVolume);

            if (actualVolume <= 0)
                return new SaleResult { Channel = channelType, Message = $"Weekly volume cap reached for {channel.Name}" };

            // Calculate price with all adjustments
            var channelPrice = baseSpotPrice * channel.BasePriceMultiplier;

            // Apply quality grading
            var qualityPrice = channelPrice * grading.EffectiveMultiplier;

            // Apply yield (output kg after processing)
            var outputKg = (int)(actualVolume * grading.YieldFactor);

            // Gross revenue before fees
            var grossRevenue = qualityPrice * outputKg;

            // Bid-ask spread cost
            var spreadCost = grossRevenue * channel.BidAskSpread;

            // Volume slippage (increases with volume)
            var hundredTonnes = actualVolume / 100_000.0m;
            var slippageCost = grossRevenue * channel.SlippagePerHundredTonnes * hundredTonnes;

            // Logistics cost
            var logisticsCost = grossRevenue * channel.LogisticsCostFraction;

            // Update weekly volume tracking
            _weeklyVolumeUsed[channelType] = usedThisWeek + actualVolume;

            var result = new SaleResult
            {
                Channel = channelType,
                VolumeKg = actualVolume,
                GrossRevenue = grossRevenue,
                LogisticsCost = logisticsCost,
                SlippageCost = slippageCost,
                SpreadCost = spreadCost,
                PaymentDelayDays = channel.PaymentDelayDays,
                Message = $"Sold {actualVolume:N0} kg via {channel.Name} at {(grossRevenue > 0 && outputKg > 0 ? grossRevenue / outputKg : 0):F2} NOK/kg gross"
            };

            OnSaleCompleted?.Invoke(result);
            return result;
        }

        /// <summary>
        /// Reset weekly volume caps (call at start of each game week).
        /// </summary>
        public void ResetWeeklyVolumes()
        {
            _weeklyVolumeUsed.Clear();
        }

        /// <summary>
        /// Unlock a sales channel.
        /// </summary>
        public void UnlockChannel(SalesChannelType type)
        {
            if (_channels.TryGetValue(type, out var channel))
            {
                channel.IsAccessible = true;
                OnChannelAccessChanged?.Invoke(type, true);
            }
        }

        /// <summary>
        /// Get all channels with their current status.
        /// </summary>
        public IReadOnlyDictionary<SalesChannelType, SalesChannel> GetChannels() => _channels;

        /// <summary>
        /// Get remaining weekly capacity for a channel.
        /// </summary>
        public int GetRemainingWeeklyCapacity(SalesChannelType type)
        {
            if (!_channels.TryGetValue(type, out var channel)) return 0;
            var used = _weeklyVolumeUsed.GetValueOrDefault(type, 0);
            return Math.Max(0, channel.MaxWeeklyVolumeKg - used);
        }

        /// <summary>
        /// Calculate expected price for a channel without executing a sale (for UI/decision-making).
        /// </summary>
        public decimal EstimatePrice(SalesChannelType channelType, decimal baseSpotPrice, GradingResult grading, int volumeKg)
        {
            if (!_channels.TryGetValue(channelType, out var channel)) return 0m;

            var channelPrice = baseSpotPrice * channel.BasePriceMultiplier;
            var qualityPrice = channelPrice * grading.EffectiveMultiplier;

            // Deduct spread + estimated slippage + logistics
            var netFactor = 1.0m - channel.BidAskSpread - channel.LogisticsCostFraction;
            var hundredTonnes = volumeKg / 100_000.0m;
            netFactor -= channel.SlippagePerHundredTonnes * hundredTonnes;

            return qualityPrice * Math.Max(0.5m, netFactor);
        }
    }
}
