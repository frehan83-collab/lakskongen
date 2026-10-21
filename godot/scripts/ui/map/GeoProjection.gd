class_name GeoProjection
extends RefCounted
## ETRS89 / UTM 33N (EPSG:25833) for the map UI (docs/geo/GEODATA.md §3,
## geo stages 2-3 plan Track C1). Every raster in godot/data/geo is a
## north-up UTM 33 grid, so a lon/lat point reaches a pixel in two steps:
## utm33() to metres, then to_px() into an image's pixels.
##
## Display only: nothing here feeds the simulation. utm33() is the Krüger
## series to n^4 (GRS80, k0 0.9996, central meridian 15°E, false easting
## 500 000 m), transcribed from tests/test_geodata.py::utm33, the reference
## that manifest.json's projection_check_25833 points pin to 1 cm.
##
## Precision: GDScript's float is 64-bit, and these functions keep it so. A
## UTM northing is about 8e6 m, where a 32-bit float's step is 0.5 m, so the
## origin is subtracted in 64-bit before anything narrows to a Vector2.
## atanh, sinh and cosh are written through log and exp, so no builtin whose
## precision is unverified sits in the chain.

const SEMI_MAJOR_M := 6378137.0
const FLATTENING := 1.0 / 298.257222101
const SCALE_K0 := 0.9996
const CENTRAL_MERIDIAN_DEG := 15.0
const FALSE_EASTING_M := 500000.0

## n, A and the four Krüger alpha coefficients, computed once.
static var _series: PackedFloat64Array = PackedFloat64Array()


## lon/lat (degrees, EPSG:4326) -> [x, y] in EPSG:25833 metres, 64-bit.
static func utm33(lon: float, lat: float) -> PackedFloat64Array:
	var s := _coefficients()
	var n: float = s[0]
	var big_a: float = s[1]
	var phi := deg_to_rad(lat)
	var lam := deg_to_rad(lon) - deg_to_rad(CENTRAL_MERIDIAN_DEG)
	var e := 2.0 * sqrt(n) / (1.0 + n)
	var t := _sinh(_atanh(sin(phi)) - e * _atanh(e * sin(phi)))
	var xi := atan2(t, cos(lam))
	var eta := _atanh(sin(lam) / sqrt(1.0 + t * t))
	var x := eta
	var y := xi
	for j in range(1, 5):
		var alpha: float = s[1 + j]
		x += alpha * cos(2.0 * j * xi) * _sinh(2.0 * j * eta)
		y += alpha * sin(2.0 * j * xi) * _cosh(2.0 * j * eta)
	return PackedFloat64Array([FALSE_EASTING_M + SCALE_K0 * big_a * x, SCALE_K0 * big_a * y])


## EPSG:25833 metres -> pixel position in a north-up image whose bounds are
## [x0, y0, x1, y1] (basemap.json bounds_25833) at res_m metres per pixel:
## px = (x - x0) / res, py = (y1 - y) / res. The pixel's top-left corner is
## its integer position; the origin is subtracted in 64-bit before narrowing.
static func to_px(x: float, y: float, bounds_25833: Array, res_m: float) -> Vector2:
	var px: float = (x - float(bounds_25833[0])) / res_m
	var py: float = (float(bounds_25833[3]) - y) / res_m
	return Vector2(px, py)


## The integer cell (column, row) under a point, floored in 64-bit: the same
## lookup as tests/test_geodata.py ((x - x0) // res, (y1 - y) // res). The cell
## may lie outside the image; callers check it against the image size.
static func to_cell(x: float, y: float, bounds_25833: Array, res_m: float) -> Vector2i:
	var px: float = (x - float(bounds_25833[0])) / res_m
	var py: float = (float(bounds_25833[3]) - y) / res_m
	return Vector2i(int(floor(px)), int(floor(py)))


## The inverse of to_px: a pixel position -> [x, y] in EPSG:25833 metres.
static func to_utm(px: Vector2, bounds_25833: Array, res_m: float) -> PackedFloat64Array:
	return PackedFloat64Array([
		float(bounds_25833[0]) + float(px.x) * res_m,
		float(bounds_25833[3]) - float(px.y) * res_m])


static func _coefficients() -> PackedFloat64Array:
	if not _series.is_empty():
		return _series
	var n := FLATTENING / (2.0 - FLATTENING)
	var n2 := n * n
	var n3 := n2 * n
	var n4 := n3 * n
	var big_a := SEMI_MAJOR_M / (1.0 + n) * (1.0 + n2 / 4.0 + n4 / 64.0)
	_series = PackedFloat64Array([
		n,
		big_a,
		n / 2.0 - 2.0 * n2 / 3.0 + 5.0 * n3 / 16.0 + 41.0 * n4 / 180.0,
		13.0 * n2 / 48.0 - 3.0 * n3 / 5.0 + 557.0 * n4 / 1440.0,
		61.0 * n3 / 240.0 - 103.0 * n4 / 140.0,
		49561.0 * n4 / 161280.0])
	return _series


static func _atanh(v: float) -> float:
	return 0.5 * log((1.0 + v) / (1.0 - v))


static func _sinh(v: float) -> float:
	return 0.5 * (exp(v) - exp(-v))


static func _cosh(v: float) -> float:
	return 0.5 * (exp(v) + exp(-v))
