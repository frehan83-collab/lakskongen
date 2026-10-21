class_name FacilityDrama
extends RefCounted
## Weather drama and incident poses of the farm types' facilities (roadmap
## F2-24; farm-types spec §4 "Weather and exposure display", §5.4 Animation;
## not the sky's weather drama of G3-05, WeatherDramaChecks): when the offshore
## unit's columns throw spray, when an incident lists it and when the land campus's roof
## vents steam, in one place. Pure rules: UnitSpray, OffshoreUnitVisual and
## RoofSteam read their inputs (the engine's sea at the unit, its open
## incident, the presentation weather's air) and ask these; nothing here
## reads or writes the sim.

## Spray rises at the columns above this significant wave height (m) at the
## unit's plot (spec §4 "Weather drama"), and is at its full share from
## SPRAY_FULL_HS_M. A sea just over the line gets SPRAY_MIN_LEVEL, a few
## drops, so 3.1 m reads as a start and not as nothing.
const SPRAY_FROM_HS_M := 3.0
const SPRAY_FULL_HS_M := 6.0
const SPRAY_MIN_LEVEL := 0.15
## The OF1-class's columns (blender/offshore_designs.py: a regular 12-gon of
## column centres, vertex k at TAU * k / 12 in Blender x/y, which the glb
## export turns into Godot x/-z) and how far over the waterline the spray
## leaves them (m): over the crests that hide anything lower.
const COLUMN_COUNT := 12
const SPRAY_EMIT_HEIGHT_M := 0.8

## A tilt incident lists the unit by this many degrees (spec §5.4: at most 3)
## until it is resolved; the pose eases in and out at LIST_RATE_DEG_S.
const LIST_DEG := 2.6
const LIST_RATE_DEG_S := 0.6
## The engine's name for it (SimBridge.get_offshore_handling_state:
## "active_incident_kind", OffshoreIncidentKind's name; "" when none is open).
const TILT_KIND := "tilt"

## Roof vents steam in air colder than this (deg C, spec §5.4), the denser
## the colder: STEAM_MIN_LEVEL just under the line, full from STEAM_FULL_C.
const STEAM_BELOW_C := -2.0
const STEAM_FULL_C := -14.0
const STEAM_MIN_LEVEL := 0.3
## The land kit's roof vents in their piece's local space (blender/land_designs.py,
## Blender x/y/z to Godot x/z/-y): a process hall bay's two roof units
## (x = -7.5 and 7.5 on the ridge line, their tops at ridge - 0.1 + 1.3 =
## 11.2 m), and the dark harvest hall's five (every 20 m from x = -40, y
## alternating -12 and -6, their tops at 15 - 1 + 1.8 = 15.8 m).
## tests/test_weather_drama.py pins both against the design and the glbs.
const BAY_VENTS: Array[Vector3] = [Vector3(-7.5, 11.2, 0.0), Vector3(7.5, 11.2, 0.0)]
const HARVEST_VENTS: Array[Vector3] = [
	Vector3(-40.0, 15.8, 12.0), Vector3(-20.0, 15.8, 6.0), Vector3(0.0, 15.8, 12.0),
	Vector3(20.0, 15.8, 6.0), Vector3(40.0, 15.8, 12.0),
]


## The spray share (0-1) of a sea of significant wave height `hs_m`: none at
## or under SPRAY_FROM_HS_M.
static func spray_level(hs_m: float) -> float:
	if hs_m <= SPRAY_FROM_HS_M:
		return 0.0
	return lerpf(SPRAY_MIN_LEVEL, 1.0, smoothstep(SPRAY_FROM_HS_M, SPRAY_FULL_HS_M, hs_m))


## The list (deg) a unit with `incident_kind` open stands at: LIST_DEG for a
## tilt, none for any other incident or none.
static func list_target_deg(incident_kind: String) -> float:
	return LIST_DEG if incident_kind.to_lower() == TILT_KIND else 0.0


## The steam share (0-1) in air of `air_c` deg C: none at or over STEAM_BELOW_C.
static func steam_level(air_c: float) -> float:
	if air_c >= STEAM_BELOW_C:
		return 0.0
	return lerpf(STEAM_MIN_LEVEL, 1.0, smoothstep(0.0, 1.0, inverse_lerp(STEAM_BELOW_C, STEAM_FULL_C, air_c)))


## The unit's columns at the waterline, spray height over it, in the unit's
## local space (its origin is its axis at the design waterline).
static func column_points(circumradius_m: float, count: int = COLUMN_COUNT) -> PackedVector3Array:
	var out := PackedVector3Array()
	for k in count:
		var a := TAU * float(k) / float(count)
		out.append(Vector3(circumradius_m * cos(a), SPRAY_EMIT_HEIGHT_M, -circumradius_m * sin(a)))
	return out


## The roof vents of a kit piece (`asset`) in the piece's local space; none
## for any other piece.
static func vents_of(asset: String) -> Array[Vector3]:
	match asset:
		"process_hall_bay":
			return BAY_VENTS
		"harvest_hall":
			return HARVEST_VENTS
	var none: Array[Vector3] = []
	return none
