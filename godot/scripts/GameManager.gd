# GameManager - orchestrates core systems and exposes simple API

extends Node
class_name GameManager

onready var Genetics = preload("res://godot/scripts/Genetics.gd")
onready var Hatchery = preload("res://godot/scripts/Hatchery.gd")
onready var TimeSystem = preload("res://godot/scripts/TimeSystem.gd")
onready var FeedManager = preload("res://godot/scripts/FeedManager.gd")

var genetics: Node
var hatchery: Node
var time: Node
var feed: Node

func _ready() -> void:
    genetics = Genetics.new()
    hatchery = Hatchery.new()
    time = TimeSystem.new()
    feed = FeedManager.new()
    add_child(genetics)
    add_child(hatchery)
    add_child(time)
    add_child(feed)
    # connect day tick
    time.connect("day_advanced", self, "_on_day_advanced")

func _on_day_advanced(days: int) -> void:
    hatchery.incubate_eggs(days)

func spawn_family(mother_genetics: Dictionary, father_genetics: Dictionary, clutch: int = 20, incubation_days: int =30, rng: RandomNumberGenerator = null) -> Array:
    var out = []
    if rng == null:
        rng = RandomNumberGenerator.new()
        rng.randomize()
    for i in range(clutch):
        var child_g = genetics.mate(mother_genetics, father_genetics, rng)
        hatchery.spawn_egg(child_g, incubation_days)
        out.append(child_g)
    return out

func advance_days(days: int = 1) -> void:
    time.advance_days(days)

func set_feed_quality(q: float) -> void:
    feed.set_feed_quality(q)

func compute_feed_cost_for_growth(gain_g: float) -> float:
    var kg = feed.feed_needed_for_growth(gain_g)
    return feed.cost_for_feed_kg(kg)
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
