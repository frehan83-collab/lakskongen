using Godot;
using LakskongenSim.Core;

/// <summary>
/// Global signal bus for decoupled communication between UI and game systems.
/// All UI components can connect to these signals to update when game state changes.
/// </summary>
public partial class EventBus : Node
{
    // === Time & Progress Events ===
    [Signal]
    public delegate void DayAdvancedEventHandler(int day, string season);
    
    [Signal]
    public delegate void TimeSpeedChangedEventHandler(float speed);
    
    [Signal]
    public delegate void GamePausedEventHandler(bool isPaused);

    // === Financial Events ===
    [Signal]
    public delegate void FinanceUpdatedEventHandler(long cash, long netWorth);
    
    [Signal]
    public delegate void MarketPriceChangedEventHandler(float pricePerKg, string trend);
    
    [Signal]
    public delegate void RevenueEarnedEventHandler(long amount, string source);
    
    [Signal]
    public delegate void ExpenseIncurredEventHandler(long amount, string source);

    // === Production Events ===
    [Signal]
    public delegate void HarvestCompletedEventHandler(int farmId, int fishCount, float avgWeight, long revenue);
    
    [Signal]
    public delegate void FishStockedEventHandler(int farmId, int fishCount, float avgWeight);
    
    [Signal]
    public delegate void GrowthReportEventHandler(int farmId, float avgGrowthRate, int totalFish);

    // === Health & Disease Events ===
    [Signal]
    public delegate void DiseaseOutbreakEventHandler(int farmId, string diseaseName, int affectedFish);
    
    [Signal]
    public delegate void LiceWarningEventHandler(int farmId, float liceCount, string severity);
    
    [Signal]
    public delegate void MortalityReportEventHandler(int farmId, int deaths, string cause);
    
    [Signal]
    public delegate void TreatmentAppliedEventHandler(int farmId, string treatmentType, long cost);

    // === Environmental Events ===
    [Signal]
    public delegate void WeatherChangedEventHandler(string weatherType, float temperature, string impact);
    
    [Signal]
    public delegate void WaterQualityChangedEventHandler(int farmId, float oxygenLevel, float temperature);
    
    [Signal]
    public delegate void SeasonChangedEventHandler(string newSeason, string effects);

    // === Farm Management Events ===
    [Signal]
    public delegate void FarmBuiltEventHandler(int farmId, string location, long cost);
    
    [Signal]
    public delegate void FarmUpgradedEventHandler(int farmId, string upgradeType, long cost);
    
    [Signal]
    public delegate void FarmSoldEventHandler(int farmId, long salePrice);
    
    [Signal]
    public delegate void CapacityChangedEventHandler(int farmId, int newCapacity);

    // === Research & Technology Events ===
    [Signal]
    public delegate void ResearchCompletedEventHandler(string researchName, string benefits);
    
    [Signal]
    public delegate void TechnologyUnlockedEventHandler(string techName, string description);

    // === Market & Business Events ===
    [Signal]
    public delegate void ContractOfferedEventHandler(string contractType, long value, string terms);
    
    [Signal]
    public delegate void ContractCompletedEventHandler(string contractType, long payout, bool successful);
    
    [Signal]
    public delegate void LoanOfferedEventHandler(long amount, float interestRate, string terms);
    
    [Signal]
    public delegate void LoanStatusChangedEventHandler(long totalDebt, long monthlyPayment);

    // === Regulatory & Compliance Events ===
    [Signal]
    public delegate void InspectionScheduledEventHandler(int farmId, string inspectionType, int daysUntil);
    
    [Signal]
    public delegate void ComplianceIssueEventHandler(int farmId, string issue, long fine);
    
    [Signal]
    public delegate void LicenseExpiringEventHandler(int farmId, string licenseType, int daysUntil);

    // === UI Events ===
    [Signal]
    public delegate void NotificationPostedEventHandler(string title, string message, string type, float duration);
    
    [Signal]
    public delegate void DialogRequestedEventHandler(string dialogType, Variant data);
    
    [Signal]
    public delegate void ViewChangedEventHandler(string newView, string previousView);

    public override void _Ready()
    {
        GD.Print("EventBus initialized - Global signals ready");
    }

    // === Convenience Methods for Common Events ===
    
    /// <summary>
    /// Post a notification to the UI
    /// </summary>
    public void PostNotification(string title, string message, string type = "info", float duration = 5.0f)
    {
        EmitSignal(SignalName.NotificationPosted, title, message, type, duration);
    }

    /// <summary>
    /// Request a dialog to be shown
    /// </summary>
    public void RequestDialog(string dialogType, Variant data = default)
    {
        EmitSignal(SignalName.DialogRequested, dialogType, data);
    }

    /// <summary>
    /// Emit financial update signal
    /// </summary>
    public void UpdateFinances(long cash, long netWorth)
    {
        EmitSignal(SignalName.FinanceUpdated, cash, netWorth);
    }

    /// <summary>
    /// Report day advancement
    /// </summary>
    public void AdvanceDay(int day, string season)
    {
        EmitSignal(SignalName.DayAdvanced, day, season);
    }

    /// <summary>
    /// Report harvest completion
    /// </summary>
    public void CompleteHarvest(int farmId, int fishCount, float avgWeight, long revenue)
    {
        EmitSignal(SignalName.HarvestCompleted, farmId, fishCount, avgWeight, revenue);
        PostNotification("Harvest Complete", 
            $"Farm {farmId}: {fishCount} fish harvested ({avgWeight:F1}kg avg) for {revenue:C}",
            "success");
    }

    /// <summary>
    /// Report disease outbreak
    /// </summary>
    public void ReportDisease(int farmId, string diseaseName, int affectedFish)
    {
        EmitSignal(SignalName.DiseaseOutbreak, farmId, diseaseName, affectedFish);
        PostNotification("Disease Outbreak", 
            $"Farm {farmId}: {diseaseName} affecting {affectedFish} fish",
            "warning");
    }

    /// <summary>
    /// Report lice warning
    /// </summary>
    public void WarnLice(int farmId, float liceCount, string severity)
    {
        EmitSignal(SignalName.LiceWarning, farmId, liceCount, severity);
        string type = severity == "High" ? "danger" : "warning";
        PostNotification("Lice Alert", 
            $"Farm {farmId}: {severity} lice levels ({liceCount:F1} per fish)",
            type);
    }
}