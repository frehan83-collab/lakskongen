extends RefCounted
## Geo stages 2-3, the captain's C2 merge (docs/geo/STAGE23_PLAN.md §4 step 3):
## the review of geo/map-national-2's lower findings, pinned in the running
## game. GeoMapNationalChecks.gd runs these on the national map at the
## default layout; GeoMapHitChecks.gd holds the pointer's checks.
## 1. Counts of one read in the singular, in the split by kind and in an
##    owner's row ("1 laksefisklokalitet i sjø", "1 lokalitet").

var t: Node = null


func run(runner: Node, map: Control, chart: GeoMapView) -> void:
	t = runner
	_singular_forms(map, chart)


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


## The split and an owner's row read right for a count of 1 and of many
## (Norwegian, the suite's locale).
func _singular_forms(map: Control, chart: GeoMapView) -> void:
	var one := GeoMapTips.split_text([1, 2, 0, 1])
	var many := GeoMapTips.split_text([153, 45, 0, 60])
	_check(one == "1 laksefisklokalitet i sjø · 2 på land · 0 til havs · 1 annen art"
			and many == "153 laksefisklokaliteter i sjø · 45 på land · 0 til havs · 60 andre arter",
		"geo map: the split by kind reads '%s' for one and '%s' for many" % [one, many])
	var facts: GeoAreaFacts = map.area_facts()
	var geo: Dictionary = chart.areas()[2]
	facts.show_area(3, geo, {"sites_by_kind": {"sea_salmonid": 1, "land_other": 1}, "sea_growout_mtb_t": 3120.0,
		"top_owners": [{"id": "a", "name": "Prøveeier", "is_major": false, "sites": 1, "mtb_t": 3120.0},
			{"id": "b", "name": "Prøveeier to", "is_major": false, "sites": 4, "mtb_t": 940.0}]})
	var row0 := facts.find_child("Owner_0", true, false)
	var row1 := facts.find_child("Owner_1", true, false)
	var split := facts.find_child("LocalitiesSplit", true, false) as Label
	var first := (row0.get_node("Sites") as Label).text if row0 != null else ""
	var second := (row1.get_node("Sites") as Label).text if row1 != null else ""
	_check(first == "1 lokalitet · " + SimFormatters.num(3120.0) + " t" and second == "4 lokaliteter · 940 t"
			and split != null and split.text == "1 laksefisklokalitet i sjø · 0 på land · 0 til havs · 1 annen art",
		"geo map: the detail column says '%s', '%s' and '%s'" % [first, second, split.text if split else "missing"])
	map.select_area(3)
