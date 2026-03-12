# Simple test script for the Stochastic Disease Engine

# Initialize the engine and necessary classes
$diseaseSystem = New-Object LakskongenSim.Health.StochasticDiseaseEngine(@{
    LiceModel = $null
})

# Sample zones and fish populations
$zones = @(
    [LakskongenSim.Environment.Zone]::new("Zone1", 10.0),
    [LakskongenSim.Environment.Zone]::new("Zone2", 11.0)
)

$populations = @(
    [LakskongenSim.Health.FishPopulation]::new("Zone1", 1000),
    [LakskongenSim.Health.FishPopulation]::new("Zone2", 1200)
)

# Run simulation for 500 days
Write-Host "Running Stochastic Disease Engine Test for 500 days..."
$diseaseSystem.SimulateMultiVectorTransmission($zones, $populations, 500)

# Monitor results
foreach ($population in $populations) {
    Write-Host "Zone: $($population.ZoneId)"
    Write-Host "Total Fish: $($population.TotalFish)"
    Write-Host "Infected: $($population.InfectedFish)"
    Write-Host "Mortality Rate: $($population.MortalityRate)"
    Write-Host ("-" * 30)
}

Write-Host "Stochastic Disease Engine test completed."