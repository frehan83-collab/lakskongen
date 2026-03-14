using System;
using System.Collections.Generic;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Difficulty levels for the salmon farming simulator.
    /// </summary>
    public enum DifficultyLevel
    {
        /// <summary>Beginner-friendly mode with accelerated growth and reduced challenges.</summary>
        Easy,
        
        /// <summary>Balanced gameplay with standard simulation parameters.</summary>
        Normal,
        
        /// <summary>Challenging mode with slower growth and increased risks.</summary>
        Hard,
        
        /// <summary>Realistic Norwegian aquaculture industry parameters.</summary>
        Realistic,
        
        /// <summary>User-defined custom difficulty settings.</summary>
        Custom
    }

    /// <summary>
    /// Difficulty settings that scale simulation parameters.
    /// </summary>
    public class DifficultySettings
    {
        /// <summary>Current difficulty level.</summary>
        public DifficultyLevel Level { get; set; } = DifficultyLevel.Normal;

        /// <summary>Fish growth rate multiplier (Easy=1.3x, Normal=1.0x, Hard=0.8x, Realistic=0.9x).</summary>
        public double FishGrowthRateMultiplier { get; set; } = 1.0;

        /// <summary>Disease frequency multiplier (Easy=0.5x, Normal=1.0x, Hard=1.5x, Realistic=1.2x).</summary>
        public double DiseaseFrequencyMultiplier { get; set; } = 1.0;

        /// <summary>Market price volatility multiplier (Easy=0.5x, Normal=1.0x, Hard=1.5x, Realistic=1.0x).</summary>
        public double MarketVolatilityMultiplier { get; set; } = 1.0;

        /// <summary>Starting capital multiplier (Easy=1.5x, Normal=1.0x, Hard=0.7x, Realistic=0.8x).</summary>
        public double StartingCapitalMultiplier { get; set; } = 1.0;

        /// <summary>Lice growth rate multiplier (Easy=0.5x, Normal=1.0x, Hard=1.5x, Realistic=1.3x).</summary>
        public double LiceGrowthRateMultiplier { get; set; } = 1.0;

        /// <summary>Regulatory strictness multiplier (Easy=0.5x, Normal=1.0x, Hard=1.2x, Realistic=1.0x).</summary>
        public double RegulatoryStrictnessMultiplier { get; set; } = 1.0;

        /// <summary>Feed cost multiplier (Easy=0.8x, Normal=1.0x, Hard=1.2x, Realistic=1.0x).</summary>
        public double FeedCostMultiplier { get; set; } = 1.0;

        /// <summary>Description of this difficulty preset (Norwegian).</summary>
        public string Description { get; set; } = "Standard vanskelighetsgrad";

        /// <summary>
        /// Creates a preset difficulty configuration.
        /// </summary>
        public static DifficultySettings CreatePreset(DifficultyLevel level)
        {
            return level switch
            {
                DifficultyLevel.Easy => new DifficultySettings
                {
                    Level = DifficultyLevel.Easy,
                    FishGrowthRateMultiplier = 1.3,
                    DiseaseFrequencyMultiplier = 0.5,
                    MarketVolatilityMultiplier = 0.5,
                    StartingCapitalMultiplier = 1.5,
                    LiceGrowthRateMultiplier = 0.5,
                    RegulatoryStrictnessMultiplier = 0.5,
                    FeedCostMultiplier = 0.8,
                    Description = "Lett - Perfekt for nybegynnere. Raskere vekst, færre utfordringer, og mer startkapital."
                },
                DifficultyLevel.Normal => new DifficultySettings
                {
                    Level = DifficultyLevel.Normal,
                    FishGrowthRateMultiplier = 1.0,
                    DiseaseFrequencyMultiplier = 1.0,
                    MarketVolatilityMultiplier = 1.0,
                    StartingCapitalMultiplier = 1.0,
                    LiceGrowthRateMultiplier = 1.0,
                    RegulatoryStrictnessMultiplier = 1.0,
                    FeedCostMultiplier = 1.0,
                    Description = "Normal - Balansert spillopplevelse med realistiske utfordringer."
                },
                DifficultyLevel.Hard => new DifficultySettings
                {
                    Level = DifficultyLevel.Hard,
                    FishGrowthRateMultiplier = 0.8,
                    DiseaseFrequencyMultiplier = 1.5,
                    MarketVolatilityMultiplier = 1.5,
                    StartingCapitalMultiplier = 0.7,
                    LiceGrowthRateMultiplier = 1.5,
                    RegulatoryStrictnessMultiplier = 1.2,
                    FeedCostMultiplier = 1.2,
                    Description = "Vanskelig - For erfarne spillere. Tøffe markedsforhold og høyere risiko."
                },
                DifficultyLevel.Realistic => new DifficultySettings
                {
                    Level = DifficultyLevel.Realistic,
                    FishGrowthRateMultiplier = 0.9,
                    DiseaseFrequencyMultiplier = 1.2,
                    MarketVolatilityMultiplier = 1.0,
                    StartingCapitalMultiplier = 0.8,
                    LiceGrowthRateMultiplier = 1.3,
                    RegulatoryStrictnessMultiplier = 1.0,
                    FeedCostMultiplier = 1.0,
                    Description = "Realistisk - Basert på virkelige norske oppdrettsforhold. Autentisk simulering."
                },
                DifficultyLevel.Custom => new DifficultySettings
                {
                    Level = DifficultyLevel.Custom,
                    Description = "Tilpasset - Juster hver parameter individuelt."
                },
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Ugyldig vanskelighetsgrad")
            };
        }

        /// <summary>
        /// Creates a copy of this settings object.
        /// </summary>
        public DifficultySettings Clone()
        {
            return new DifficultySettings
            {
                Level = this.Level,
                FishGrowthRateMultiplier = this.FishGrowthRateMultiplier,
                DiseaseFrequencyMultiplier = this.DiseaseFrequencyMultiplier,
                MarketVolatilityMultiplier = this.MarketVolatilityMultiplier,
                StartingCapitalMultiplier = this.StartingCapitalMultiplier,
                LiceGrowthRateMultiplier = this.LiceGrowthRateMultiplier,
                RegulatoryStrictnessMultiplier = this.RegulatoryStrictnessMultiplier,
                FeedCostMultiplier = this.FeedCostMultiplier,
                Description = this.Description
            };
        }
    }

    /// <summary>
    /// Manages difficulty settings and applies them to game state.
    /// </summary>
    public class DifficultyManager
    {
        private DifficultySettings _currentSettings;

        /// <summary>Current active difficulty settings.</summary>
        public DifficultySettings CurrentSettings => _currentSettings;

        /// <summary>Event raised when difficulty settings change.</summary>
        public event Action<DifficultySettings>? OnDifficultyChanged;

        public DifficultyManager()
        {
            _currentSettings = DifficultySettings.CreatePreset(DifficultyLevel.Normal);
        }

        /// <summary>
        /// Sets the difficulty to a preset level.
        /// </summary>
        public void SetDifficulty(DifficultyLevel level)
        {
            _currentSettings = DifficultySettings.CreatePreset(level);
            OnDifficultyChanged?.Invoke(_currentSettings);
        }

        /// <summary>
        /// Applies custom difficulty settings.
        /// </summary>
        public void SetCustomDifficulty(DifficultySettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _currentSettings = settings.Clone();
            _currentSettings.Level = DifficultyLevel.Custom;
            OnDifficultyChanged?.Invoke(_currentSettings);
        }

        /// <summary>
        /// Gets the adjusted growth rate based on difficulty.
        /// </summary>
        public double GetAdjustedGrowthRate(double baseRate)
        {
            return baseRate * _currentSettings.FishGrowthRateMultiplier;
        }

        /// <summary>
        /// Gets the adjusted disease frequency based on difficulty.
        /// </summary>
        public double GetAdjustedDiseaseFrequency(double baseFrequency)
        {
            return baseFrequency * _currentSettings.DiseaseFrequencyMultiplier;
        }

        /// <summary>
        /// Gets the adjusted market volatility based on difficulty.
        /// </summary>
        public double GetAdjustedMarketVolatility(double baseVolatility)
        {
            return baseVolatility * _currentSettings.MarketVolatilityMultiplier;
        }

        /// <summary>
        /// Gets the adjusted starting capital based on difficulty.
        /// </summary>
        public decimal GetAdjustedStartingCapital(decimal baseCapital)
        {
            return baseCapital * (decimal)_currentSettings.StartingCapitalMultiplier;
        }

        /// <summary>
        /// Gets the adjusted lice growth rate based on difficulty.
        /// </summary>
        public double GetAdjustedLiceGrowthRate(double baseRate)
        {
            return baseRate * _currentSettings.LiceGrowthRateMultiplier;
        }

        /// <summary>
        /// Gets the adjusted regulatory strictness based on difficulty.
        /// </summary>
        public double GetAdjustedRegulatoryStrictness(double baseStrictness)
        {
            return baseStrictness * _currentSettings.RegulatoryStrictnessMultiplier;
        }

        /// <summary>
        /// Gets the adjusted feed cost based on difficulty.
        /// </summary>
        public decimal GetAdjustedFeedCost(decimal baseCost)
        {
            return baseCost * (decimal)_currentSettings.FeedCostMultiplier;
        }

        /// <summary>
        /// Builder pattern for creating custom difficulty settings.
        /// </summary>
        public class CustomDifficultyBuilder
        {
            private readonly DifficultySettings _settings;

            public CustomDifficultyBuilder()
            {
                _settings = DifficultySettings.CreatePreset(DifficultyLevel.Normal);
                _settings.Level = DifficultyLevel.Custom;
            }

            public CustomDifficultyBuilder WithFishGrowthRate(double multiplier)
            {
                _settings.FishGrowthRateMultiplier = multiplier;
                return this;
            }

            public CustomDifficultyBuilder WithDiseaseFrequency(double multiplier)
            {
                _settings.DiseaseFrequencyMultiplier = multiplier;
                return this;
            }

            public CustomDifficultyBuilder WithMarketVolatility(double multiplier)
            {
                _settings.MarketVolatilityMultiplier = multiplier;
                return this;
            }

            public CustomDifficultyBuilder WithStartingCapital(double multiplier)
            {
                _settings.StartingCapitalMultiplier = multiplier;
                return this;
            }

            public CustomDifficultyBuilder WithLiceGrowthRate(double multiplier)
            {
                _settings.LiceGrowthRateMultiplier = multiplier;
                return this;
            }

            public CustomDifficultyBuilder WithRegulatoryStrictness(double multiplier)
            {
                _settings.RegulatoryStrictnessMultiplier = multiplier;
                return this;
            }

            public CustomDifficultyBuilder WithFeedCost(double multiplier)
            {
                _settings.FeedCostMultiplier = multiplier;
                return this;
            }

            public CustomDifficultyBuilder WithDescription(string description)
            {
                _settings.Description = description;
                return this;
            }

            public DifficultySettings Build()
            {
                return _settings.Clone();
            }
        }
    }
}
