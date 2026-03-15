# Contributing Code — Workflow Guide

> How to contribute code to Lakskongen, from fork to merged PR.

## Quick Reference

```bash
# 1. Fork → Clone → Branch → Code → Test → Push → PR
git clone https://github.com/YOUR_USERNAME/lakskongen.git
cd lakskongen
git checkout -b feature/my-feature
# ... make changes ...
cd src && dotnet test
git add -A && git commit -m "feat: add cool feature"
git push origin feature/my-feature
# Open PR on GitHub
```

## Step 1: Fork the Repository

1. Go to the Lakskongen repo on GitHub
2. Click **Fork** (top right)
3. Clone your fork locally:

```bash
git clone https://github.com/YOUR_USERNAME/lakskongen.git
cd lakskongen
git remote add upstream https://github.com/ORIGINAL_OWNER/lakskongen.git
```

## Step 2: Create a Feature Branch

```bash
git checkout main
git pull upstream main      # Sync with upstream
git checkout -b feature/my-feature
```

### Branch Naming Convention

| Prefix | Use For | Example |
|--------|---------|---------|
| `feature/` | New features | `feature/cleaner-fish-delousing` |
| `fix/` | Bug fixes | `fix/mortality-double-counting` |
| `docs/` | Documentation | `docs/api-reference` |
| `perf/` | Performance | `perf/cohort-simd-optimization` |
| `test/` | Test additions | `test/genetics-inbreeding` |

## Step 3: Make Your Changes

### Code Style

**C# (.NET 8.0):**
- `PascalCase` for classes, methods, properties
- `camelCase` for local variables, parameters
- `_camelCase` for private fields
- `SCREAMING_CASE` for constants
- Allman braces (new line)
- 120 char line max
- 4 spaces (no tabs)

**GDScript:**
- `snake_case` for functions and variables
- `PascalCase` for classes
- `SCREAMING_CASE` for constants
- Tab indentation (Godot default)

### Commit Message Format

```
type: short description

Longer explanation if needed.

Fixes #123
```

**Types:** `feat`, `fix`, `docs`, `test`, `perf`, `refactor`, `style`, `chore`

**Examples:**
```
feat: add cleaner fish delousing treatment
fix: prevent triple-stacking disease mortality
docs: add API reference for EventBus signals
test: add genetics inbreeding depression tests
perf: optimize cohort growth with in-place updates
```

## Step 4: Write Tests

Every change should have tests:

### For C# simulation code:
```csharp
[Fact]
public void NewFeature_Scenario_ExpectedResult()
{
    // Arrange
    var system = new MyNewSystem();

    // Act
    var result = system.DoThing();

    // Assert
    Assert.Equal(expected, result);
}
```

### For GDScript UI code:
- Manual testing with the Godot editor
- Scene validation via `tests/ui_scene_checks.gd`

### Run the full test suite:
```bash
cd src
dotnet test --verbosity normal
```

**All tests must pass before submitting a PR.**

## Step 5: Push and Create PR

```bash
git push origin feature/my-feature
```

Go to GitHub → your fork → Click **"Compare & pull request"**

### PR Template

```markdown
## What
Brief description of what this PR does.

## Why
Context: what problem does it solve? Link to issue if applicable.

## How
Technical approach. What systems were changed?

## Testing
- [ ] All existing tests pass (`dotnet test`)
- [ ] New tests added for new behavior
- [ ] Manual testing in Godot editor
- [ ] Performance impact assessed (if applicable)

## Screenshots
If UI changes, include before/after screenshots.
```

## Step 6: Review Process

1. **Automated checks** — Tests must pass
2. **Code review** — Maintainer reviews your code
3. **Feedback** — Address review comments with new commits
4. **Approval** — Maintainer approves
5. **Merge** — Squash-merge into main

### Responding to Review Comments

```bash
# Make changes based on feedback
git add -A
git commit -m "fix: address review feedback"
git push origin feature/my-feature
# PR updates automatically
```

## What Makes a Great Contribution

### ✅ Do
- Follow existing code patterns
- Write tests for new code
- Keep PRs focused (one feature/fix per PR)
- Document public APIs with XML comments
- Update relevant docs if behavior changes

### ❌ Don't
- Submit huge PRs with unrelated changes
- Break existing tests
- Add Godot dependencies to `LakskongenSim`
- Use `#region` blocks
- Commit build artifacts or IDE files

## Areas Needing Help

| Area | Difficulty | Description |
|------|-----------|-------------|
| Biology validation | Medium | Compare models against real aquaculture data |
| Performance | Hard | Optimize biology loop for 100k+ fish |
| Localization | Easy | Add translations (see [Localization Guide](../../godot/LOCALIZATION_GUIDE.md)) |
| UI Polish | Medium | Improve dashboard, panels, notifications |
| Test coverage | Easy-Medium | Add tests for uncovered systems |
| Documentation | Easy | Improve guides, fix errors |

## Next Steps

- 📖 [Architecture](../Architecture.md) — Understand the codebase
- 🧪 [Running Tests](running-tests.md) — Full test guide
- 🔧 [Creating Mods](creating-your-first-mod.md) — Mod development
- 📊 [API Reference](../API_Reference.md) — System APIs
