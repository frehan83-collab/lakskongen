# Localization — Lakskongen

The game ships with **English (`en`)** and **Norwegian Bokmål (`no` → `nb`)**
UI text. All runtime strings resolve through Godot's `tr()` / `TranslationServer`
using one CSV source of truth.

## Source of truth

```
godot/translations/translations.csv
```

Format: Godot's `csv_translation` layout — a header row naming the key column
plus one column per locale:

```csv
key,en,no
UI_MAINMENU_TITLE,LAKSKONGEN,LAKSKONGEN
UI_FINANCE_PL_INSURANCE,Insurance,Forsikring
```

Rules that matter:

- **First column is the lookup key.** `tr("KEY")` returns the cell for the
  active locale; if the key is missing Godot returns the key itself.
- The header column for Norwegian is `no`; Godot normalises it to the
  **`nb`** locale and emits `translations.nb.translation`.
- Every row must have exactly **3 columns**. Any field containing a comma,
  quote, or newline must be quoted (`"`, RFC 4180). Unquoted commas silently
  split a row and corrupt the import — validate before committing.
- Keys are unique. Never duplicate an existing key.

### Two key styles in this codebase

1. **Named keys** (`UI_*`, `TIP_*`, …) — preferred; referenced from code as
   `tr("UI_FINANCE_PL_INSURANCE")` or via the `_tr_or("KEY", "fallback")`
   helper, which degrades gracefully if a key is absent.
2. **Literal keys** — some panels call `tr("Ressursrente")` / `tr("Fisk")`
   directly, using the source string itself as the key. These rows are stored
   with the literal in the key column; the `en` cell holds the English text and
   the `no` cell the Norwegian text.

When adding UI text, prefer a named `UI_*` key. If you follow an existing
literal-`tr()` call, add the row keyed by that exact literal.

## Generated artifacts (NOT committed)

The importer compiles the CSV into binary translation resources:

```
godot/translations/translations.en.translation
godot/translations/translations.nb.translation
```

These are **gitignored** (`godot/.gitignore` → `*.translation`). They are build
output; only the CSV is version-controlled.

## Regenerating the translations

Godot reimports the CSV whenever the-editor opens the project. To regenerate
from the command line without opening the game:

```sh
# Mono/standard Godot 4.7 binary, headless import of the whole project:
godot --headless --path godot --import
```

Or, in isolation (no side effects on the rest of the project — this is how the
artifacts were last produced):

```sh
TMP=$(mktemp -d)
mkdir -p "$TMP/translations"
cp godot/translations/translations.csv "$TMP/translations/"
printf 'config_version=5\n\n[application]\nconfig/name="LocImport"\nconfig/features=PackedStringArray("4.7")\n' > "$TMP/project.godot"
godot --headless --path "$TMP" --import
cp "$TMP/translations/translations.en.translation" godot/translations/
cp "$TMP/translations/translations.nb.translation" godot/translations/
```

Then verify a couple of lookups headlessly:

```gdscript
# check.gd — run with: godot --headless --path godot --script res://check.gd
extends SceneTree
func _init():
    var nb = load("res://translations/translations.nb.translation")
    print(nb.get_message("Ressursrente"))
    quit()
```

> Note: `OptimizedTranslation` (produced when `compress=1`) does not store
> source texts, so `get_message_count()` returns `0` by design. Use
> `get_message(key)` to assert specific entries.

## Validating the CSV

Run before committing any change to `translations.csv`:

```sh
python3 - <<'PY'
import csv
from collections import Counter
p = "godot/translations/translations.csv"
rows = list(csv.reader(open(p, newline="", encoding="utf-8")))
assert rows[0] == ["key", "en", "no"], rows[0]
body = rows[1:]
assert all(len(r) == 3 for r in body), "inconsistent column counts"
keys = [r[0] for r in body]
assert not [k for k, c in Counter(keys).items() if c > 1], "duplicate keys"
assert all(r[0].strip() and r[1] and r[2] for r in body), "empty fields"
print(f"OK: {len(body)} rows, {len(set(keys))} unique keys, 3 columns each")
PY
```
