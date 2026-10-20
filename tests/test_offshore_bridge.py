"""F2-06/F2-07: the offshore unit's verbs reach GDScript through the sea lane's
blocks only (FARM_TYPES_SPEC §7.6), and the legacy offshore verbs go through
the sea (roadmap F2-07: fish handling is never done above its limit)."""

import re
from pathlib import Path

REPO = Path(__file__).parent.parent
SCRIPTS = REPO / "godot" / "scripts"
BRIDGE_DIR = SCRIPTS / "autoload"

# GameBridge verb -> GDScript func, each in the sea lane's block of all three files.
SEA_VERBS = {
    "GetSeaStateForecast": "get_sea_state_forecast",
    "PreviewOffshoreBuild": "preview_offshore_build",
    "BuildOffshoreUnit": "build_offshore_unit",
    "GetOffshoreConstruction": "get_offshore_construction",
    "GetOffshoreUnitState": "get_offshore_unit_state",
    "PreviewOffshoreBooking": "preview_offshore_booking",
    "BookOffshoreOperation": "book_offshore_operation",
    "BookOffshoreFirstWindow": "book_offshore_first_window",
    "CancelOffshoreBooking": "cancel_offshore_booking",
    "PreviewOffshoreTow": "preview_offshore_tow",
    "StartOffshoreTow": "start_offshore_tow",
    "ChooseOffshoreShakedown": "choose_offshore_shakedown",
    "FixTeethingDefect": "fix_teething_defect",
    "InstallOffshoreSensors": "install_offshore_sensors",
    "ServiceOffshoreSensors": "service_offshore_sensors",
    "RepairOffshoreUnit": "repair_offshore_unit",
    "BuyOutOffshorePartner": "buy_out_offshore_partner",
}


def _sea_block(src: str) -> str:
    begin = src.index("BEGIN sim·sea lane block")
    end = src.index("END sim·sea lane block")
    assert begin < end
    return src[begin:end]


def test_every_offshore_verb_is_in_the_sea_lane_block_of_each_bridge_file():
    partial = _sea_block((BRIDGE_DIR / "GameBridge.Offshore.cs").read_text(encoding="utf-8"))
    proxies = _sea_block((SCRIPTS / "bridge" / "SimProxies.gd").read_text(encoding="utf-8"))
    facade = _sea_block((SCRIPTS / "SimBridge.gd").read_text(encoding="utf-8"))
    for verb, func in SEA_VERBS.items():
        assert re.search(rf"public Godot\.Collections\.Dictionary {verb}\(", partial), f"GameBridge.Offshore.cs sea block lacks {verb}"
        assert re.search(rf"func {func}\([^)]*\) -> Dictionary:\s*\n\s*return game_bridge\.{verb}\(", proxies), \
            f"SimProxies sea block lacks {func} -> {verb}"
        assert re.search(rf"func {func}\([^)]*\) -> Dictionary: return _proxies\.{func}\(", facade), \
            f"SimBridge sea block lacks {func}"


def test_the_offshore_verbs_carry_no_decimal_and_no_farm_type_number():
    partial = _sea_block((BRIDGE_DIR / "GameBridge.Offshore.cs").read_text(encoding="utf-8"))
    for m in re.finditer(r"public [^\n(]+\(([^)]*)\)", partial):
        assert "decimal" not in m.group(0), f"Godot cannot marshal decimal: {m.group(0)}"
    # Prices and limits come from the engine's views: no NOK amount or Hs limit is written in the bridge.
    assert not re.search(r"\b\d[\d_]{4,}m?\b", partial), "a number in the offshore bridge block"


def test_the_legacy_offshore_verbs_go_through_the_sea():
    farm = (BRIDGE_DIR / "GameBridge.Farm.cs").read_text(encoding="utf-8")
    for verb, op in (("StockOffshoreFarm", "Stocking"), ("HarvestOffshoreFarm", "Harvest")):
        m = re.search(rf"public Godot\.Collections\.Dictionary {verb}\([^)]*\)\s*=>(.*?);\n", farm, re.S)
        assert m, f"{verb} is missing"
        assert f"OffshoreFishHandlingRefusal(farmId, LakskongenSim.Environment.OffshoreOp.{op})" in m.group(1), \
            f"{verb} must ask the sea before handling fish"
    m = re.search(r"public float RepairOffshore\(string farmId\)\s*\{(.*?)\n    \}", farm, re.S)
    assert m and 'SimulationEngine.RepairOffshore(farmId, "all")' in m.group(1), "RepairOffshore must be the engine's repair"
    assert "50_000_000" not in m.group(1)
