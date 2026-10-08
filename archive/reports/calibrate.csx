// Quick calibration script
// Run with: dotnet script calibrate.csx

double AllometricExponent = 0.67;
double OptimalTemperature = 11.0;
double ThermalTolerance = 3.0;
double GrowthRateScalingFactor = 0.0015;

double temperature = 11.0;
double asymptoticWeightK = 5.5;
double geneticGrowthRate = 2.5;
double variance = 0.02;
double stageModifier = 1.2; // Seawater

double maxGrowthRate = geneticGrowthRate * GrowthRateScalingFactor * stageModifier;
double alphaT = maxGrowthRate; // At optimal temp, exp() = 1.0

Console.WriteLine($"Effective α_max = {maxGrowthRate:F6}");
Console.WriteLine($"Variance = {variance:F4}");
Console.WriteLine($"Ratio (deterministic vs stochastic) = {maxGrowthRate / variance:F2}");

// Simulate 540 days deterministically (no noise)
double weight = 0.08;
for (int d = 0; d < 540; d++)
{
    double det = alphaT * Math.Pow(weight, AllometricExponent) * (1.0 - weight / asymptoticWeightK);
    weight += det;
    if (weight < 0) weight = 0.01;
}
Console.WriteLine($"\nDeterministic only (540d): {weight:F3} kg");

// Now with different growth rates
foreach (double rate in new[] { 1.0, 2.0, 3.0, 5.0, 10.0, 20.0, 50.0 })
{
    double w = 0.08;
    double effectiveAlpha = rate * GrowthRateScalingFactor * stageModifier;
    for (int d = 0; d < 540; d++)
    {
        double det = effectiveAlpha * Math.Pow(w, AllometricExponent) * (1.0 - w / asymptoticWeightK);
        w += det;
        if (w < 0) w = 0.01;
    }
    Console.WriteLine($"Rate={rate,5:F1} → effective α={effectiveAlpha:F5} → {w:F2} kg");
}
