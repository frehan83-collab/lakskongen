# Running Tests

> How to run unit, integration, and stress tests for the Lakskongen simulation.

## Test Stack

| Type | Framework | Location | Purpose |
|------|-----------|----------|---------|
| **Unit Tests** | xUnit (.NET) | `src/LakskongenSim.Tests/` | Core simulation logic |
| **Save/Load Tests** | xUnit (.NET) | `src/LakskongenSim.SaveLoadTests/` | Serialization integrity |
| **Logistics Tests** | xUnit (.NET) | `src/LakskongenSim.LogisticsTests/` | Supply chain logic |
| **Health Tests** | xUnit (.NET) | `tests/` | Disease system specifics |
| **Integration Tests** | Python pytest | `tests/test_*.py` | Cross-system validation |
| **Performance Tests** | .NET Console | `src/PerformanceBenchmark/` | FPS and tick timing |
| **GDScript Tests** | GDScript | `tests/ui_scene_checks.gd` | UI scene validation |

## Quick Start: Run All C# Tests

```bash
cd src
dotnet test --verbosity normal
```

Expected: All tests pass in < 30 seconds.

## Unit Tests (xUnit)

### Run All Unit Tests
```bash
cd src/LakskongenSim.Tests
dotnet test
```

### Run Specific Test Category
```bash
# Biology tests only
dotnet test --filter "FullyQualifiedName~Biology"

# Health/Disease tests only
dotnet test --filter "FullyQualifiedName~Health"

# Economics tests only
dotnet test --filter "FullyQualifiedName~Economics"
```

### Run a Single Test
```bash
dotnet test --filter "MethodName=ApplyGrowth_WithOptimalTemperature_IncreasesWeight"
```

### Test Naming Convention
```
MethodName_Scenario_ExpectedBehavior

Examples:
  ApplyGrowth_WithOptimalTemperature_IncreasesWeight
  Breed_TwoParents_ProducesOffspringWithMixedTraits
  SIRModel_HighDensity_IncreasesTransmission
```

## Integration Tests (Python)

### Setup
```bash
cd tests
python -m venv venv
# Windows:
venv\Scripts\activate
# macOS/Linux:
source venv/bin/activate

pip install pytest
```

### Run All Integration Tests
```bash
pytest -v
```

### Available Test Files

| File | Tests |
|------|-------|
| `test_genetics.py` | Breeding, heritability, inbreeding |
| `test_growth.py` | von Bertalanffy growth curves |
| `test_environment.py` | Zone conditions, water chemistry |
| `test_feed.py` | FCR calculations, feed costs |
| `test_seasite.py` | Sea site operations |
| `test_constants.py` | Game constant validation |

### Run Specific Test File
```bash
pytest tests/test_genetics.py -v
pytest tests/test_growth.py -v
```

## Save/Load Stress Tests

### Run Save/Load Tests
```bash
cd src/LakskongenSim.SaveLoadTests
dotnet test --verbosity normal
```

### What They Test
- ✅ Full production cycle (905 days) serialize → deserialize
- ✅ Backup rotation (3 generations)
- ✅ Corruption detection and recovery
- ✅ Backward compatibility (missing fields use defaults)
- ✅ Large save files (100k+ fish populations)

## Performance Benchmarks

### Run Benchmark
```bash
cd src/PerformanceBenchmark
dotnet run -c Release
```

### Benchmark Scenarios

| Scenario | Fish Count | Duration | Target |
|----------|-----------|----------|--------|
| Small Farm | 10,000 | 100 days | < 2ms/tick |
| Medium Farm | 50,000 | 100 days | < 8ms/tick |
| Large Farm | 100,000 | 100 days | < 16ms/tick |

### Interpreting Results
```
Tick budget for 60 FPS = 16.67ms
Current bottlenecks:
  Biology loop: ~30ms @ 100k fish (needs optimization)
  Disease SIR:  ~10ms @ 100k fish (LOD helps)
  Market:       ~2ms  (no issue)
```

## Writing New Tests

### C# Unit Test Template
```csharp
using Xunit;
using LakskongenSim.Biology;

namespace LakskongenSim.Tests.Biology;

public class MyNewTests
{
    [Fact]
    public void MethodName_Scenario_ExpectedBehavior()
    {
        // Arrange
        var model = new GrowthModel();
        var cohort = new Cohort { Count = 10000, MeanWeightKg = 0.5 };

        // Act
        var result = model.ApplyGrowth(cohort, days: 30, tempC: 10.0);

        // Assert
        Assert.True(result.MeanWeightKg > 0.5);
    }

    [Theory]
    [InlineData(8.0, true)]    // Optimal temp
    [InlineData(2.0, false)]   // Too cold
    [InlineData(22.0, false)]  // Too warm
    public void Growth_VariousTemperatures_MatchExpectations(double temp, bool shouldGrow)
    {
        // ...
    }
}
```

### Python Integration Test Template
```python
import pytest

def test_genetics_breed_produces_offspring():
    """Offspring should have traits between parents."""
    parent1 = {"growth_rate": 0.8, "disease_resistance": 0.3}
    parent2 = {"growth_rate": 0.4, "disease_resistance": 0.7}
    
    offspring = breed(parent1, parent2)
    
    assert 0.3 <= offspring["growth_rate"] <= 0.9
    assert 0.2 <= offspring["disease_resistance"] <= 0.8
```

## CI/CD Integration

Tests are designed to run without Godot (pure C#), making them ideal for CI:

```yaml
# GitHub Actions example
- name: Run Tests
  run: |
    cd src
    dotnet test --verbosity normal --logger "trx"
```

## Next Steps

- 🏗️ [Building from Source](building-from-source.md) — Full build guide
- 🏛️ [Architecture](../Architecture.md) — Understand test structure
- 🤝 [Contributing Code](contributing-code.md) — PR workflow
