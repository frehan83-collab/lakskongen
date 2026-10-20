"""F2-12/F2-14 land lane bridge contract (wave4/sim-land-1).

Every label key the land bridge composes (GameBridge.LandSites.cs: reasons,
templates, permits and their statuses, construction phases, refusal drivers,
mitigations, contracts, grid projects) has English and Norwegian text, every
ledger category the land tick books has a display name, and every land verb
the bridge adds sits inside the land lane's marked blocks.
"""
from __future__ import annotations

import csv
import re
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SIM = REPO / "src" / "LakskongenSim"
GODOT = REPO / "godot"
TRANSLATIONS = GODOT / "translations" / "translations.csv"
BRIDGE = GODOT / "scripts" / "autoload" / "GameBridge.LandSites.cs"


def _texts() -> dict[str, tuple[str, str]]:
    with TRANSLATIONS.open(encoding="utf-8", newline="") as handle:
        return {row["key"]: (row["en"], row["no"]) for row in csv.DictReader(handle)}


def _upper_snake(value: str) -> str:
    s = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", value)
    return re.sub(r"[^A-Za-z0-9]", "_", s).upper()


def _reason_keys() -> list[str]:
    text = (SIM / "Core" / "SimulationEngine.LandSites.cs").read_text(encoding="utf-8")
    block = text[text.index("public static class LandSiteReasons"):text.index("/// <summary>\n/// Land sites")]
    keys = re.findall(r'public const string \w+ = "([a-z_]+)";', block)
    planner = (SIM / "Farm" / "Land" / "LandStockingPlanner.cs").read_text(encoding="utf-8")
    keys += re.findall(r'public const string DensityExceeded = "([a-z_]+)";', planner)
    return keys + ["engine_not_ready", "holding_empty"]


def _phase_keys() -> list[str]:
    text = (SIM / "Farm" / "Land" / "LandConstructionPhases.cs").read_text(encoding="utf-8")
    line = re.search(r"public const string SiteWorks = .*?;", text, re.S).group(0)
    return re.findall(r'"([a-z_]+)"', line)


def _driver_keys() -> list[str]:
    text = (SIM / "Finance" / "LandPermitSystem.cs").read_text(encoding="utf-8")
    keys = set(re.findall(r'new(?: PermitDriver)?\("([a-z_]+)"', text))
    keys |= set(re.findall(r'public const string (?:EnvironmentalSurvey|Denitrification) = "([a-z_]+)";', text))
    return sorted(keys)


def test_every_land_label_key_has_english_and_norwegian_text():
    texts = _texts()
    wanted = [f"LAND_REASON_{k.upper()}" for k in _reason_keys()]
    wanted += [f"LAND_PHASE_{k.upper()}" for k in _phase_keys()]
    wanted += [f"LAND_DRIVER_{k.upper()}" for k in _driver_keys()]
    wanted += [f"LAND_TEMPLATE_{_upper_snake(t)}" for t in ("HfsLot", "HfsPilotLot", "RasHall", "PostSmoltModule")]
    wanted += [f"LAND_PERMIT_{_upper_snake(k)}" for k in
               ("Zoning", "LandAquaculture", "Discharge", "SeaWorks", "Biosecurity", "GridConnection")]
    wanted += [f"LAND_PERMIT_STATUS_{s.upper()}" for s in ("approved", "pre_zoned", "pending", "refused", "needed")]
    wanted += ["LAND_MITIGATION_ENVIRONMENTAL_SURVEY", "LAND_MITIGATION_DENITRIFICATION",
               "LAND_CONTRACT_MANAGED", "LAND_CONTRACT_TURNKEY", "LAND_GRID_CAPACITY", "LAND_GRID_SECOND_FEED"]
    assert len(_reason_keys()) >= 20 and len(_phase_keys()) == 6 and len(_driver_keys()) >= 7, "harness parsed nothing"
    missing = [k for k in wanted if k not in texts or not texts[k][0].strip() or not texts[k][1].strip()]
    assert not missing, f"land label keys without en/no text: {missing}"


def test_the_land_ledger_lines_have_display_names():
    texts = _texts()
    text = (SIM / "Core" / "SimulationEngine.LandSites.cs").read_text(encoding="utf-8")
    cats = set(re.findall(r'CostBooking\.\w+\s*,\s*"(land_[a-z_]+)"', text))
    assert cats == {"land_energy", "land_oxygen", "land_staff"}, cats
    assert all(f"LEDGER_{c.upper()}" in texts for c in cats)


def test_the_land_verbs_sit_in_the_land_lane_blocks():
    bridge = BRIDGE.read_text(encoding="utf-8")
    begin = bridge.index("BEGIN sim·land lane block")
    end = bridge.index("END sim·land lane block")
    verbs = re.findall(r"public Godot\.Collections\.(?:Dictionary|Array<Godot\.Collections\.Dictionary>) (\w+)\(", bridge)
    for verb in verbs:
        at = bridge.index(f" {verb}(")
        if verb in ("GetLandSites", "GetLandPlots"):
            continue  # F2-03's list verbs, above the block
        assert begin < at < end, f"{verb} is outside the land lane's block"
    proxies = (GODOT / "scripts" / "bridge" / "SimProxies.gd").read_text(encoding="utf-8")
    facade = (GODOT / "scripts" / "SimBridge.gd").read_text(encoding="utf-8")
    for doc in (proxies, facade):
        block = doc[doc.index("BEGIN sim·land lane block"):doc.index("END sim·land lane block")]
        for verb in verbs:
            snake = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", verb).lower()
            assert f"func {snake}(" in block, f"{snake} not in the land block"


def test_no_land_bridge_signature_carries_decimal_or_a_default():
    bridge = BRIDGE.read_text(encoding="utf-8")
    for match in re.finditer(r"public Godot\.Collections\.[\w<>.]+ \w+\(([^)]*)\)", bridge):
        params = match.group(1)
        assert "decimal" not in params, match.group(0)
        assert "=" not in params, f"GDScript never fills a C# default: {match.group(0)}"
