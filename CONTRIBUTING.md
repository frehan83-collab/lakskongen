# Contributing to Lakskongen

Thank you for your interest in contributing to **Lakskongen**! Whether you're a developer, designer, domain expert (fish farmer!), translator, or player with feedback, we welcome your contributions.

---

## 🌟 Ways to Contribute

1. **Code** — Bug fixes, features, optimizations
2. **Documentation** — Improve guides, fix typos, add examples
3. **Testing** — Report bugs, test edge cases, write unit tests
4. **Localization** — Translate UI and game text
5. **Art & Audio** — Sprites, models, music, sound effects *(planned)*
6. **Domain Expertise** — Validate biological models, regulations, market dynamics
7. **Feedback** — Suggest features, balance tweaks, UX improvements

---

## 🐛 Reporting Bugs

### Before Submitting
1. **Search existing issues** — Your bug might already be reported
2. **Reproduce the issue** — Ensure it's consistent
3. **Gather information** — Save files, logs, screenshots

### Bug Report Template
```markdown
**Description:**
Clear description of what went wrong.

**Steps to Reproduce:**
1. Start new game
2. Advance to day 50
3. Harvest fish
4. Crash occurs

**Expected Behavior:**
What should have happened.

**Actual Behavior:**
What actually happened.

**Environment:**
- OS: Windows 11 / macOS 14 / Ubuntu 22.04
- Godot Version: 4.3.1
- .NET Version: 8.0.2
- Game Version: 0.4.0-alpha

**Logs/Screenshots:**
Attach relevant files.
```

---

## 💡 Suggesting Features

We love new ideas! Use this format:

```markdown
**Feature Name:**
Technology Research Tree Expansion

**Problem:**
Current tech tree has limited depth, players unlock everything too quickly.

**Proposed Solution:**
Add 3 more tiers with 15 new technologies (laser delousing, AI feeding, offshore farms).

**Benefits:**
- Longer progression curve
- More strategic choices
- Aligns with VISION.md roadmap

**Alternatives Considered:**
Simplify tree instead (rejected — less engaging).
```

---

## 🔧 Development Setup

### Prerequisites
- **.NET 8.0 SDK** — [Download](https://dotnet.microsoft.com/download)
- **Godot 4.3+ (with .NET support)** — [Download](https://godotengine.org/)
- **Git** — [Download](https://git-scm.com/)
- **IDE:** Visual Studio, Rider, or VS Code with C# extension

### Setup Steps
```bash
# 1. Fork and clone the repository
git clone https://github.com/yourusername/lakskongen.git
cd lakskongen

# 2. Create a feature branch
git checkout -b feature/my-awesome-feature

# 3. Build C# simulation
cd src
dotnet restore
dotnet build

# 4. Run tests
cd LakskongenSim.Tests
dotnet test

# 5. Open Godot project
cd ../../godot
# Open project.godot in Godot Editor

# 6. Make changes, test, commit
```

---

## 📝 Code Style Guidelines

### C# (.NET 8.0)

**Conventions:**
- **Naming:**
  - `PascalCase` for classes, methods, properties
  - `camelCase` for local variables, parameters
  - `_camelCase` for private fields
  - `SCREAMING_CASE` for constants
- **Braces:** Allman style (braces on new line)
- **Line length:** 120 characters max
- **Indentation:** 4 spaces (no tabs)

**Example:**
```csharp
namespace LakskongenSim.Biology;

public class FishPopulation
{
    private const double DEFAULT_MORTALITY_RATE = 0.001;
    private readonly Random _random;
    private int _totalFishCount;

    public int TotalFish
    {
        get => _totalFishCount;
        private set => _totalFishCount = value;
    }

    public void UpdatePopulation(int days)
    {
        foreach (var cohort in Cohorts)
        {
            ApplyGrowth(cohort, days);
            ApplyMortality(cohort, days);
        }
    }
}
```

**Rules:**
- **Use `readonly` for immutable fields**
- **Prefer records for data classes** (`public record FishData(...)`)
- **Use `var` when type is obvious**
- **Avoid `null` — use `?.`, `??`, or `ArgumentNullException`**
- **Document public APIs with XML comments**
- **No regions (`#region`) — organize with classes/files instead**

**Testing:**
- **xUnit** for C# unit tests
- **Test naming:** `MethodName_Scenario_ExpectedBehavior`
- **Arrange-Act-Assert** pattern
- **Use `[Fact]` and `[Theory]`**

**Example Test:**
```csharp
[Fact]
public void ApplyMortality_WithZeroRate_ReturnsOriginalCount()
{
    // Arrange
    var cohort = new Cohort(Count: 10000, MortalityRate: 0.0);
    var model = new MortalityModel();

    // Act
    var result = model.ApplyMortality(cohort, days: 10);

    // Assert
    Assert.Equal(10000, result.Count);
}
```

---

### GDScript (Godot UI Layer)

**Conventions:**
- **Naming:**
  - `snake_case` for variables, functions, signals
  - `PascalCase` for class names
  - `SCREAMING_CASE` for constants
- **Indentation:** Tabs (Godot default)
- **Line length:** 100 characters max
- **Type hints:** Use where possible

**Example:**
```gdscript
extends Control

signal fish_harvested(amount: int)

const MAX_FISH_PER_PEN: int = 200000
const HARVEST_THRESHOLD: float = 4.5 # kg

@onready var harvest_button: Button = %HarvestButton
var _total_biomass: float = 0.0

func _ready() -> void:
    harvest_button.pressed.connect(_on_harvest_pressed)
    GameBridge.DayAdvanced.connect(_update_biomass)

func _on_harvest_pressed() -> void:
    var fish_count = _calculate_harvestable_fish()
    GameBridge.HarvestFish(fish_count)
    fish_harvested.emit(fish_count)
```

**Rules:**
- **Use signals for decoupled communication**
- **Prefer `@onready` over `_ready()` initialization**
- **Use `%NodeName` for unique node references** (less fragile than paths)
- **Type hint return values and parameters**
- **Document complex functions with comments**

---

### Python (Integration Tests)

**Conventions:**
- **PEP 8** style
- **Naming:** `snake_case` for everything
- **Line length:** 88 characters (Black formatter)
- **Type hints:** Use where helpful

**Example:**
```python
import pytest
from genetics import Genome, breed

def test_mendelian_inheritance_basic():
    """Test basic Mendelian 3:1 ratio for single heterozygous trait."""
    parent1 = Genome(alleles=["Aa"])
    parent2 = Genome(alleles=["Aa"])
    
    offspring = [breed(parent1, parent2) for _ in range(1000)]
    dominant_count = sum(1 for g in offspring if "A" in g.alleles[0])
    
    # Expect ~750/1000 with dominant trait (75% ± 5%)
    assert 700 <= dominant_count <= 800
```

---

## 📦 Commit Message Format

We follow **Conventional Commits** format:

```
<type>(<scope>): <subject>

<body>

<footer>
```

### Types
- `feat:` — New feature
- `fix:` — Bug fix
- `docs:` — Documentation changes
- `style:` — Code formatting (no logic change)
- `refactor:` — Code restructuring (no behavior change)
- `perf:` — Performance improvement
- `test:` — Add/update tests
- `chore:` — Build, tooling, dependencies

### Examples
```bash
feat(genetics): add genomic selection algorithm

Implements GBLUP (Genomic Best Linear Unbiased Prediction) for
advanced breeding. Players can now select superior fish based on
SNP markers.

Closes #42
```

```bash
fix(market): correct Easter price peak timing

Seasonal curve now peaks at day ~100 (Easter) instead of day 91.
Adjusted sine wave offset.

Fixes #58
```

```bash
docs(readme): add build instructions for macOS

Added Homebrew installation steps and clarified .NET SDK requirement.
```

---

## 🔀 Pull Request Process

### Before Submitting
1. **Create an issue first** (for features) to discuss approach
2. **Branch from `main`** — Use `feature/`, `fix/`, or `docs/` prefix
3. **Keep PRs focused** — One feature/fix per PR
4. **Write tests** — All new code should have tests
5. **Update docs** — If API changes, update relevant docs
6. **Test locally** — Ensure all tests pass before pushing

### PR Checklist
- [ ] Code follows style guidelines (C#/GDScript)
- [ ] All tests pass (`dotnet test`, `pytest -v`)
- [ ] New tests added for new features
- [ ] Documentation updated (if applicable)
- [ ] Commit messages follow conventions
- [ ] No merge conflicts with `main`
- [ ] Build succeeds in Godot (no errors/warnings)

### PR Template
```markdown
**Description:**
Brief summary of changes.

**Motivation:**
Why is this change needed? (Link to issue if applicable)

**Changes:**
- Added genomic selection algorithm
- Refactored genetics UI
- Added 12 unit tests for GBLUP

**Testing:**
- All xUnit tests pass (21/21)
- Manual testing: Verified breeding UI shows genomic scores
- Edge cases tested: Inbreeding, mutation, edge genomes

**Screenshots/Videos:**
(If UI changes)

**Related Issues:**
Closes #42
```

---

## 🧪 Testing Requirements

### Unit Tests
- **Minimum coverage:** 70% for new code
- **Test all public APIs**
- **Test edge cases** (null, empty, extremes)
- **Use mocks for dependencies** (avoid file I/O, network in tests)

### Integration Tests
- **Test system interactions** (e.g., breeding → genetics → growth)
- **Use pytest for Python tests**
- **Keep tests fast** (&lt;1 second per test)

### Manual Testing Checklist
Before submitting a PR:
- [ ] Game launches without errors
- [ ] New feature works as expected
- [ ] No regression in existing features
- [ ] UI is responsive and intuitive
- [ ] No console errors/warnings
- [ ] Save/load works with changes

---

## 🌍 Localization Guidelines

### Adding Translations
1. **Extract strings:** Run `python extract_strings.py` (from `godot/` directory)
2. **Edit CSV:** Add translations in `translations/<lang>.csv`
3. **Import:** Godot → Project Settings → Localization → Import
4. **Test:** Change language in-game and verify strings

### Translation Keys
- Use descriptive keys: `ui_harvest_button`, `tooltip_feed_quality`
- Keep English as fallback
- Avoid hardcoded strings in code (use `tr("key")`)

**Example:**
```gdscript
# Bad
label.text = "Harvest Fish"

# Good
label.text = tr("ui_harvest_button")
```

---

## 📄 Documentation Standards

### Code Comments
- **Explain "why", not "what"**
- **Use XML comments for C# public APIs**
- **Avoid obvious comments**

**Good:**
```csharp
// Disease resistance affects infection rate logarithmically
// because empirical data shows diminishing returns above 80%
var infectionRate = Math.Log(1.0 + diseaseResistance);
```

**Bad:**
```csharp
// Set infection rate
var infectionRate = Math.Log(1.0 + diseaseResistance);
```

### Documentation Files
- **Use Markdown** for all docs
- **Keep line length ~80-100 characters**
- **Use headers for structure**
- **Include code examples** where helpful

---

## ❓ Questions?

- **Discord:** *(TBD)*
- **GitHub Discussions:** Use for design questions
- **Issues:** For bug reports and feature requests
- **Email:** *(TBD)*

---

## 📜 Code of Conduct

Be respectful, constructive, and inclusive. We're building this together.

- **No harassment, discrimination, or toxic behavior**
- **Assume good intent** — Clarify before criticizing
- **Help newcomers** — We were all beginners once
- **Focus on the work** — Critique ideas, not people

---

## 🎉 Recognition

Contributors will be credited in:
- **CHANGELOG.md** — Feature authors
- **README.md** — Major contributors
- **In-game credits** — All contributors

---

**Thank you for helping make Lakskongen the definitive salmon farming simulation!** 🐟
