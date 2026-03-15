namespace LakskongenSim.Data;

/// <summary>
/// All enumerations used throughout the salmon farming simulation
/// </summary>

/// <summary>
/// Seasons in the Norwegian fjords
/// </summary>
public enum Season
{
    Spring,
    Summer,
    Autumn,
    Winter
}

/// <summary>
/// Norwegian production zones (1-13) traffic light colors
/// </summary>
public enum ZoneColor
{
    Green,   // Can increase MAB by 6%
    Yellow,  // Maintain current MAB
    Red      // Must reduce MAB by 6%
}

/// <summary>
/// Major salmon diseases in Norwegian aquaculture
/// </summary>
public enum DiseaseType
{
    ISA,            // Infectious Salmon Anaemia - virus, up to 90% mortality
    PD,             // Pancreas Disease - virus (SAV), 10-20% mortality
    IHN,            // Infectious Hematopoietic Necrosis - virus, 5-70% mortality
    AGD,            // Amoebic Gill Disease - parasite, 5-15% mortality
    CMS,            // Cardiomyopathy Syndrome - virus, 5-20% mortality
    WinterUlcers    // Winter Ulcers - bacteria, 5-15% mortality
}

/// <summary>
/// Life stages of salmon from egg to harvest
/// </summary>
public enum LifeStage
{
    Egg,        // 0-60 days, temperature-dependent
    Alevin,     // Yolk sac stage, 4-6 weeks
    Fry,        // First feeding stage, 3-4 months
    Parr,       // Freshwater growth, 4-6 months
    Smolt,      // Seawater-ready juveniles
    Seawater,   // Growing in sea pens, 12-18 months
    Harvest     // Ready for processing at 4-6kg
}

/// <summary>
/// Types of salmon production licenses
/// </summary>
public enum LicenseType
{
    Standard,       // 780 tonnes MAB, NOK 150-250M cost
    Development,    // Free but must innovate, converts for NOK 10M
    Green,          // Sustainability bonus, cheaper
    Offshore,       // New category, larger MAB, exposed waters
    LandBased       // RAS facilities, exempt from resource rent tax
}

/// <summary>
/// Delousing treatment methods
/// </summary>
public enum DelousingMethod
{
    HydrogenPeroxide,   // Chemical bath, declining efficacy
    Thermolicer,        // Warm water, high mortality risk (3-5%)
    Hydrolicer,         // Pressure wash, mechanical
    CleanerFish,        // Wrasse/lumpfish, variable efficacy
    LaserDelousing,     // Stingray system, premium tech
    FreshwaterBath,     // Also treats AGD
    MedicatedFeed,      // Declining efficacy due to resistance
    LiceSkirts          // Preventive barrier around pen top
}

/// <summary>
/// Feed ingredient types
/// </summary>
public enum FeedIngredient
{
    Fishmeal,           // High protein, best FCR, expensive, limited supply
    FishOil,            // Omega-3 content, flesh color, expensive
    SoyProtein,         // Cheap but can cause gut inflammation
    RapeseedOil,        // Cheap, reduces omega-3
    InsectMeal,         // Sustainable, good protein, limited supply
    Microalgae,         // Premium omega-3 source, future-tech
    Astaxanthin,        // Pink flesh color, critical for market
    SingleCellProtein   // Novel, sustainability bonus
}

/// <summary>
/// Processing grades affecting market value
/// </summary>
public enum ProcessingGrade
{
    Superior,    // 100% base price, perfect condition
    Ordinary,    // -15 to -25%, minor defects
    Production   // -40 to -60%, significant defects
}

/// <summary>
/// Product types for value-added processing
/// </summary>
public enum ProductType
{
    WholeHOG,        // Head-on-gutted, basic processing
    FreshFillets,    // +20-30% margin
    SmokedSalmon,    // +40-60% margin
    SashimiGrade,    // +50-80% margin, Japan market
    Portions,        // +30-50% margin, retail
    FishOil,         // Byproduct revenue stream
    FishMeal,        // Byproduct revenue stream
    FishSkin         // Leather market (novelty)
}

/// <summary>
/// Export market destinations
/// </summary>
public enum ExportMarket
{
    EU,         // 65% volume share, France/Spain/Italy
    Poland,     // 15% volume share, processing hub
    USA,        // 8% volume share, premium fillets/smoked
    Japan,      // 5% volume share, sashimi grade
    China,      // 3% volume share, growing market
    SouthKorea, // 2% volume share, fresh market
    Brazil      // 2% volume share, volume market
}

/// <summary>
/// Containment system types
/// </summary>
public enum ContainmentType
{
    OpenPen,            // Traditional net pens, standard technology
    SemiClosed,         // Contained but in sea, lice protection
    ClosedContainment,  // Fully contained systems
    LandBasedRAS,       // Recirculating Aquaculture Systems on land
    FlowThrough,        // Land-based using natural seawater
    Offshore            // Deep water, exposed location systems
}

/// <summary>
/// Weather conditions affecting operations
/// </summary>
public enum WeatherCondition
{
    Calm,
    Moderate,
    Storm,
    Severe      // Prevents operations, risk of pen damage
}

/// <summary>
/// Genetic traits for salmon breeding
/// </summary>
public enum GeneticTrait
{
    GrowthRate,         // Faster time to harvest weight
    DiseaseResistance,  // Lower mortality from diseases
    SeaLiceResistance,  // Fewer lice per fish
    FilletQuality,      // Higher superior grade percentage
    FeedConversion,     // Better FCR (Feed Conversion Ratio)
    StressTolerance,    // Survives handling better
    LateMaturation,     // Prevents early sexual maturation
    ColdTolerance       // Better survival in northern zones
}

/// <summary>
/// Fish welfare scoring factors
/// </summary>
public enum WelfareFactor
{
    StockingDensity,    // Fish per m³
    HandlingStress,     // From treatments, transfers
    TreatmentMortality, // Deaths from delousing
    DiseaseLevel,       // Health status
    EnvironmentalQuality // Water conditions
}

/// <summary>
/// Automation levels for various systems
/// </summary>
public enum AutomationLevel
{
    Manual,         // Human-operated
    SemiAutomatic,  // Timer-based with human oversight
    Automatic,      // Sensor-based automation
    AIdriven        // Machine learning optimization
}

/// <summary>
/// Financial instrument types
/// </summary>
public enum FinancialInstrument
{
    SpotMarket,      // Immediate sale at current price
    ForwardContract, // Fixed price for future delivery
    Futures,         // Standardized futures contracts
    Options,         // Price insurance instruments
    BankLoan,        // Traditional debt financing
    EquityFinancing, // Stock issuance
    GreenBonds       // Sustainable investment bonds
}

/// <summary>
/// Regulatory compliance status
/// </summary>
public enum ComplianceStatus
{
    Compliant,
    Warning,        // Approaching limits
    Violation,      // Exceeded regulatory thresholds
    Penalty         // Under regulatory sanctions
}

/// <summary>
/// Research and development focus areas
/// </summary>
public enum ResearchArea
{
    Genetics,       // Breeding program improvements
    Nutrition,      // Feed optimization
    Health,         // Disease prevention
    Technology,     // Equipment and systems
    Sustainability, // Environmental impact reduction
    Processing      // Value-added product development
}

/// <summary>
/// Employee specializations
/// </summary>
public enum EmployeeType
{
    FarmManager,
    Biologist,
    Veterinarian,
    FeedingOperator,
    Diver,
    Engineer,
    ProcessingWorker,
    QualityControl,
    Logistics,
    Sales,
    Research
}

/// <summary>
/// Environmental monitoring parameters
/// </summary>
public enum EnvironmentalParameter
{
    Temperature,
    Oxygen,
    Salinity,
    CurrentSpeed,
    pH,
    Turbidity,
    AlgaeLevel,
    JellyfishDensity
}

/// <summary>
/// Norwegian production regions for salmon farming
/// </summary>
public enum ProductionRegion
{
    Nordland,
    Troms,
    Finnmark,
    Trøndelag,
    Vestland,
    Rogaland,
    MøreOgRomsdal,
    Sørlandet
}

/// <summary>
/// Win conditions for scenario objectives
/// </summary>
public enum WinCondition
{
    /// <summary>Complete all tutorial milestones</summary>
    Tutorial,
    /// <summary>Reach a target profit threshold</summary>
    Profit,
    /// <summary>Achieve Sustainability Leader certification (score ≥ 95)</summary>
    Sustainability,
    /// <summary>Survive N days without bankruptcy</summary>
    Survival,
    /// <summary>Outpace AI competitors to a shared profit target</summary>
    Competition
}