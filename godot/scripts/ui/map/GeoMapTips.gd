class_name GeoMapTips
extends RefCounted
## The national map's tooltip texts (geo stages 2-3 plan, Track C): one line
## or two per feature, every word from translations.csv (MAP_GEO_*), every
## number through SimFormatters, no ids and no emoji.

const STAGE_KEYS := {
	"tilradning_2019": "MAP_GEO_STAGE_2019",
	"anbefaling_2022": "MAP_GEO_STAGE_2022",
	"konsekvensvurdering_2024": "MAP_GEO_STAGE_2024",
	"kan_utlyses_2026": "MAP_GEO_STAGE_2026",
}
const PLACE_KEYS := {"sea": "MAP_GEO_PLACE_SEA", "land": "MAP_GEO_PLACE_LAND", "offshore": "MAP_GEO_PLACE_OFFSHORE"}
const GROUP_KEYS := {
	"salmonid": "MAP_GEO_GROUP_SALMONID", "marine_fish": "MAP_GEO_GROUP_MARINE_FISH",
	"shellfish": "MAP_GEO_GROUP_SHELLFISH", "algae": "MAP_GEO_GROUP_ALGAE", "other": "MAP_GEO_GROUP_OTHER",
}


## The tooltip of a GeoMapView.feature_at item.
static func feature(item: Dictionary, view: GeoMapView) -> String:
	match str(item.get("kind", "")):
		"player":
			var row: Dictionary = item["row"]
			var key := "MAP_GEO_TIP_PLAYER" if str(row.get("source", "")) == "candidate" else "MAP_GEO_TIP_PLAYER_APPROX"
			return _t(key) % [str(row.get("site_label", "")), str(row.get("label", ""))]
		"hexagon":
			var row: Dictionary = item["row"]
			var text := _t("MAP_GEO_TIP_HEXAGON") % [str(row.get("area", "")), str(row.get("label", row.get("near", ""))),
				SimFormatters.num(float(row.get("depth_m", 0.0)))]
			if item.get("bound", false):
				text += "\n" + _t("MAP_GEO_TIP_HAV1")
			return text
		"candidates":
			var best: Dictionary = item["best"]
			return _t("MAP_GEO_TIP_CANDIDATES") % [int(item["count"]), area_name(view, int(item["po"])),
				str(best.get("near", "")), SimFormatters.num(float(best.get("score", 0.0)), 2)]
		"site":
			return site(view.site_layer(), int(item["index"]))
		"cluster":
			return cluster(view.site_layer(), item["cluster"])
		"offshore_area":
			return _t("MAP_GEO_TIP_OFFSHORE_AREA") % [str(item.get("name", "")),
				_t(STAGE_KEYS.get(str(item.get("stage", "")), "MAP_GEO_STAGE_2019"))]
		"fjord":
			return _t("MAP_GEO_TIP_FJORD") % str(item.get("name", ""))
	return ""


## One site: its kind, its owner when it has one, its MTB when registered in tonnes.
static func site(layer: GeoSiteLayer, i: int) -> String:
	var sites := layer.sites()
	var parts := PackedStringArray([kind_label(GeoBridgeAdapter.kind_name(sites, i))])
	var o := layer.owner_of(i)
	var owners: Array = sites.get("owners", [])
	if o >= 0 and o < owners.size():
		parts.append(str((owners[o] as Dictionary).get("name", "")))
	var mtb: PackedFloat32Array = sites.get("mtb_t", PackedFloat32Array())
	if i < mtb.size() and mtb[i] > 0.0:
		parts.append(_t("MAP_GEO_TIP_MTB") % SimFormatters.num(mtb[i]))
	return " · ".join(parts)


## A cluster: how many farms, of which kinds, and the majors' share.
static func cluster(layer: GeoSiteLayer, c: Dictionary) -> String:
	var by: PackedInt32Array = c["by_class"]
	var text := _t("MAP_GEO_TIP_CLUSTER") % int(c["count"])
	text += "\n" + _t("MAP_GEO_LOCALITIES_SPLIT") % [by[0], by[1], by[2], by[3]]
	var majors := 0
	var owners: Dictionary = c["owners"]
	for o: int in owners:
		if layer.is_major_owner(o):
			majors += int(owners[o])
	if majors > 0:
		text += "\n" + _t("MAP_GEO_TIP_MAJORS") % SimFormatters.percent(float(majors) / float(c["count"]))
	return text


## "Sjøanlegg, laksefisk" for "sea_salmonid".
static func kind_label(kind: String) -> String:
	var cut := kind.find("_")
	if cut <= 0:
		return _t("MAP_GEO_GROUP_OTHER")
	var place := _t(PLACE_KEYS.get(kind.left(cut), "MAP_GEO_PLACE_SEA"))
	var group := _t(GROUP_KEYS.get(kind.substr(cut + 1), "MAP_GEO_GROUP_OTHER"))
	return _t("MAP_GEO_SITE_KIND") % [place, group]


static func area_name(view: GeoMapView, po: int) -> String:
	var areas := view.areas()
	return str(areas[po - 1].get("name", "")) if po >= 1 and po <= areas.size() else ""


static func _t(key: String) -> String:
	return String(TranslationServer.translate(key))
