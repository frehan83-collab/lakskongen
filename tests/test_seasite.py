import pytest
try:
    from godot_module import GodotEnvironment
except ImportError:
    pytest.skip("Skipping godot_module tests locally (requires CI harness)", allow_module_level=True)

# These tests run in a lightweight Godot-emulation harness available in CI


def test_stock_and_advance(tmp_path):
    env = GodotEnvironment()
    seasite = env.new('res://godot/scripts/SeaSite.gd')
    fish_cls = env.preload('res://godot/scripts/data/Fish.gd').Fish
    # create 5 fish juveniles
    fishes = [fish_cls.new(i, {}, 10.0, 100) for i in range(5)]
    assert seasite.list_pens() == []
    pid = seasite.create_pen()
    assert pid > 0
    moved = seasite.stock_multiple(fishes, pid)
    assert moved == 5
    assert seasite.pen_count(pid) == 5
    # advance days and ensure weights increase deterministically
    w_before = seasite.compute_pen_biomass_kg(pid)
    seasite.advance_days(30)
    w_after = seasite.compute_pen_biomass_kg(pid)
    assert w_after > w_before


def test_harvest_threshold(tmp_path):
    env = GodotEnvironment()
    seasite = env.new('res://godot/scripts/SeaSite.gd')
    fish_cls = env.preload('res://godot/scripts/data/Fish.gd').Fish
    pid = seasite.create_pen()
    # create two fish: one small, one large
    small = fish_cls.new(1, {}, 100.0, 80)
    large = fish_cls.new(2, {}, 2500.0, 400)
    seasite.stock_multiple([small, large], pid)
    res = seasite.harvest_pen(pid, min_weight_g=2000.0)
    assert res['count'] == 1
    assert pytest.approx(res['total_kg'], 0.001) == 2.5
    # pen should have one remaining
    assert seasite.pen_count(pid) == 1
