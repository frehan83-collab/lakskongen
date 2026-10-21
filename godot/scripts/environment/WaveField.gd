class_name WaveField
extends RefCounted
## The water's drawn surface on the CPU (roadmap G2-03): the vertex
## displacement of water_shader.gdshader (bake_at, component_amplitude,
## wave_phase, displacement and the grid terms of vertex()), mirrored term
## for term and evaluated from the very arrays EnvironmentDirector writes to
## the material's wave_k / wave_amp uniforms, the water grid's placement and
## the terrain's water bake. Pens (PenCage) and boats (VesselModel) ride it.
##
## - height_at(x, z): the surface over a world point. The shader moves each
##   grid vertex p0 to p0 + D(p0) (Gerstner: crests sharpen as the water runs
##   towards them), so the height over a fixed point is D.y at the p0 that
##   lands there, found by fixed-point iteration (the summed steepness stays
##   below EnvironmentDirector.WAVE_FOLD_LIMIT, so the map contracts).
## - ring(): the mean surface and its mean slope over a circle, what a pen
##   collar rides (per component the ring's J0 / J1 Bessel factors).
## - hull(): the mean surface and its mean slopes over a rectangle, what a
##   hull's waterplane rides (per component sinc factors).
## A rigid body cannot follow a wave shorter than itself: it settles on the
## least-squares plane of the water under it, not on the surface at its
## centre. These are that plane, in closed form, so riding costs one pass
## over the components per body and frame and allocates nothing. The
## closed forms carry each Gerstner component to second order in its
## steepness (over fixed ground the water sits Q k a^2 / 2 lower on average,
## with a 2k ripple), which keeps them within 0.1 m of the point-sampled
## plane even on the open coast in a 25 m/s gale (UiFlowTests).
##
## Only what the grid draws moves a body: like the shader, a component needs
## a wavelength of WAVE_MIN_SPACINGS grid spacings where the body is and
## fades in up to WAVE_FULL_SPACINGS, and the displacement fades flat over
## the grid's outer levels. Finer waves live in the normal maps and lift
## nothing.
##
## The open sea's swell (F2-21, water/water_swell.gdshaderinc) rides on top:
## the director hands its components over with set_swell() (the very arrays
## the shader's swell_k / swell_amp get), and every sum here runs over the
## wind sea's components, then the swell's, with the same rules.

## water_shader.gdshader displacement(): a component is drawn where its
## wavelength spans at least this many grid spacings, at full height from
## the second count.
const WAVE_MIN_SPACINGS := 3.0
const WAVE_FULL_SPACINGS := 6.0
## water_shader.gdshader component_amplitude(): over shallow water a wave
## fades as d / (d + SHALLOW_PER_WAVELENGTH * lambda + SHALLOW_OFFSET_M).
const SHALLOW_DEPTH_M := 40.0
const SHALLOW_PER_WAVELENGTH := 0.08
const SHALLOW_OFFSET_M := 0.5
const MIN_AMPLITUDE_M := 1e-4
## water_shader.gdshader bake_at(): beyond the bake lies the open ocean
## (depth, shore distance, exposure, slope), faded in over this uv margin.
const OPEN_OCEAN := Color(200.0, 8000.0, 1.0, 0.0)
const BAKE_EDGE_UV := 0.015
## water_shader.gdshader vertex(): the displacement fades flat from this
## fraction of the displaced extent out to its edge.
const FAR_FADE_FROM := 0.75
## height_at(): fixed-point iterations and the horizontal tolerance (m).
const INVERT_MAX_STEPS := 24
const INVERT_TOLERANCE_M := 1e-4

## The field the running fjord's EnvironmentDirector keeps current (null
## without one); floating bodies read it every frame.
static var active: WaveField = null

## The shader's wave_k uniform as written: (wavevector x, z, wavenumber,
## choppiness) per component, longest first.
var wave_k := PackedVector4Array()
## The shader's wave_amp uniform as written: (sheltered amplitude, exposed
## amplitude, phase, wavelength) per component.
var wave_amp := PackedVector4Array()
## The shader's swell_k / swell_amp uniforms as written (F2-21): the open
## sea's swell components, longest first, in the wave rows' layout.
var swell_k := PackedVector4Array()
var swell_amp := PackedVector4Array()
## The shader's grid uniforms (WaterGrid writes them on its material).
var grid_half_units := 128.0
var grid_base_spacing := 2.0
var displaced_extent := 4096.0
## The shader's bake uniforms.
var bake_origin := Vector2(-2000.0, -2000.0)
var bake_spacing := 7.827
var bake_texels := 512.0
var has_bake := 0.0
## Bumped whenever the bake changes, so callers can cache bake_at().
var bake_version := 0

var _grid: WaterGrid = null
var _bake: Image = null
var _ring_radius := -1.0
var _ring_mean := PackedFloat32Array()
var _ring_slope := PackedFloat32Array()
var _ring_mean2 := PackedFloat32Array()
var _ring_slope2 := PackedFloat32Array()
var _swell_ring_mean := PackedFloat32Array()
var _swell_ring_slope := PackedFloat32Array()
var _swell_ring_mean2 := PackedFloat32Array()
var _swell_ring_slope2 := PackedFloat32Array()


## Follow a water grid: its origin moves with the camera and sets how fine
## the drawn waves are where a body floats.
func bind_grid(grid: WaterGrid) -> void:
	_grid = grid
	if grid != null:
		grid_half_units = float(grid.grid_quads / 2)
		grid_base_spacing = grid.base_spacing
		displaced_extent = grid.displaced_extent()


## The wave uniforms the director has just written (the same arrays).
func set_waves(k: PackedVector4Array, amp: PackedVector4Array) -> void:
	if k.size() != wave_k.size() or (k.size() > 0 and k[0].z != wave_k[0].z):
		_ring_radius = -1.0
	wave_k = k
	wave_amp = amp


## The swell uniforms the director has just written (F2-21; the same arrays).
func set_swell(k: PackedVector4Array, amp: PackedVector4Array) -> void:
	if k.size() != swell_k.size() or (k.size() > 0 and k[0].z != swell_k[0].z):
		_ring_radius = -1.0
	swell_k = k
	swell_amp = amp


## The terrain's water bake as the shader samples it.
func set_bake(image: Image, origin: Vector2, spacing: float, texels: float) -> void:
	_bake = image
	bake_origin = origin
	bake_spacing = spacing
	bake_texels = texels
	has_bake = 1.0 if image != null else 0.0
	bake_version += 1


## The grid's origin (it snaps to the camera in whole base steps).
func grid_origin() -> Vector3:
	return _grid.global_position if _grid != null and _grid.is_inside_tree() else Vector3.ZERO


## The bake at a world point, filtered like the shader's sampler (bilinear
## between texel centres, clamped) and faded to the open ocean at its edge.
func bake_at(x: float, z: float) -> Color:
	var u := ((x - bake_origin.x) / bake_spacing + 0.5) / bake_texels
	var v := ((z - bake_origin.y) / bake_spacing + 0.5) / bake_texels
	var inside := smoothstep(0.0, BAKE_EDGE_UV, minf(minf(u, 1.0 - u), minf(v, 1.0 - v))) * has_bake
	if inside <= 0.0 or _bake == null:
		return OPEN_OCEAN
	var n := _bake.get_width()
	var fx := clampf(u * bake_texels - 0.5, 0.0, float(n - 1))
	var fz := clampf(v * bake_texels - 0.5, 0.0, float(n - 1))
	var ix := mini(int(fx), n - 2)
	var iz := mini(int(fz), n - 2)
	var tx := fx - float(ix)
	var tz := fz - float(iz)
	var top := _bake.get_pixel(ix, iz).lerp(_bake.get_pixel(ix + 1, iz), tx)
	var bottom := _bake.get_pixel(ix, iz + 1).lerp(_bake.get_pixel(ix + 1, iz + 1), tx)
	return OPEN_OCEAN.lerp(top.lerp(bottom, tz), inside)


## Grid spacing (m) the water has at a world point, and how much of the
## displacement survives the far fade there: Vector2(spacing, fade).
func grid_reach(x: float, z: float) -> Vector2:
	var o := grid_origin()
	var d := maxf(absf(x - o.x), absf(z - o.z))
	var spacing := grid_base_spacing * maxf(1.0, 2.0 * d / (grid_half_units * grid_base_spacing))
	return Vector2(spacing, 1.0 - smoothstep(FAR_FADE_FROM * displaced_extent, displaced_extent, d))


## A component's drawn amplitude for a bake sample (shader
## component_amplitude() times the grid's band limit).
func drawn_amplitude(i: int, bake: Color, spacing: float) -> float:
	return drawn_amplitude_of(wave_amp[i], bake, spacing)


## The drawn amplitude of an amplitude row (a wave's or the swell's).
static func drawn_amplitude_of(wa: Vector4, bake: Color, spacing: float) -> float:
	var a := lerpf(wa.x, wa.y, bake.b)
	if bake.r < SHALLOW_DEPTH_M:
		var d := maxf(bake.r, 0.0)
		a *= d / (d + SHALLOW_PER_WAVELENGTH * wa.w + SHALLOW_OFFSET_M)
	return a * smoothstep(WAVE_MIN_SPACINGS * spacing, WAVE_FULL_SPACINGS * spacing, wa.w)


## The shader's displacement of the grid vertex at (x, z), far fade
## included: Vector3(dx, dy, dz).
func displacement(x: float, z: float) -> Vector3:
	var bake := bake_at(x, z)
	var reach := grid_reach(x, z)
	return (_displacement_of(wave_k, wave_amp, x, z, bake, reach.x)
		+ _displacement_of(swell_k, swell_amp, x, z, bake, reach.x)) * reach.y


## One set of components' Gerstner displacement at (x, z) (the shader's
## displacement() loop and swell_displacement()), no far fade.
static func _displacement_of(ks: PackedVector4Array, amps: PackedVector4Array, x: float, z: float,
		bake: Color, spacing: float) -> Vector3:
	var d := Vector3.ZERO
	for i in mini(ks.size(), amps.size()):
		if amps[i].w < WAVE_MIN_SPACINGS * spacing:
			break
		var a := drawn_amplitude_of(amps[i], bake, spacing)
		if a < MIN_AMPLITUDE_M:
			continue
		var wk := ks[i]
		var ph := wk.x * x + wk.y * z - amps[i].z
		var c := cos(ph)
		d.y += a * sin(ph)
		d.x += wk.w * a * wk.x / wk.z * c
		d.z += wk.w * a * wk.y / wk.z * c
	return d


## The drawn surface height over the world point (x, z).
func height_at(x: float, z: float) -> float:
	var px := x
	var pz := z
	for step in INVERT_MAX_STEPS:
		var d := displacement(px, pz)
		var nx := x - d.x
		var nz := z - d.z
		var moved := absf(nx - px) + absf(nz - pz)
		px = nx
		pz = nz
		if moved < INVERT_TOLERANCE_M:
			break
	return grid_origin().y + displacement(px, pz).y


## The plane a rigid ring of `radius` floating at (x, z) settles on (the
## least-squares fit to the surface under the ring): Vector3(mean height,
## slope along world x, slope along world z). Pass a bake sample to skip the
## lookup (a moored pen caches its own).
func ring(x: float, z: float, radius: float, bake: Variant = null) -> Vector3:
	_ensure_ring(radius)
	var b: Color = bake if bake is Color else bake_at(x, z)
	var reach := grid_reach(x, z)
	var sum := _ring_of(wave_k, wave_amp, _ring_mean, _ring_slope, _ring_mean2, _ring_slope2, x, z, b, reach.x)
	sum += _ring_of(swell_k, swell_amp, _swell_ring_mean, _swell_ring_slope, _swell_ring_mean2, _swell_ring_slope2, x, z, b, reach.x)
	return Vector3(grid_origin().y + sum.x * reach.y, sum.y * reach.y, sum.z * reach.y)


## One set of components' ring plane (mean, slope x, slope z), from its
## per-component Bessel factors.
static func _ring_of(ks: PackedVector4Array, amps: PackedVector4Array, r_mean: PackedFloat32Array,
		r_slope: PackedFloat32Array, r_mean2: PackedFloat32Array, r_slope2: PackedFloat32Array,
		x: float, z: float, b: Color, spacing: float) -> Vector3:
	var mean := 0.0
	var sx := 0.0
	var sz := 0.0
	for i in mini(mini(ks.size(), amps.size()), r_mean.size()):
		if amps[i].w < WAVE_MIN_SPACINGS * spacing:
			break
		var a := drawn_amplitude_of(amps[i], b, spacing)
		if a < MIN_AMPLITUDE_M:
			continue
		var wk := ks[i]
		var ph := wk.x * x + wk.y * z - amps[i].z
		# Second order: the crests sharpen and the troughs widen, so the
		# water over fixed ground sits q lower on average, with a 2k ripple.
		var q := 0.5 * wk.w * wk.z * a * a
		mean += a * r_mean[i] * sin(ph) - q * (1.0 + r_mean2[i] * cos(2.0 * ph))
		# Along the wave's heading (wk.xy / k).
		var s := (a * r_slope[i] * cos(ph) + q * r_slope2[i] * sin(2.0 * ph)) / wk.z
		sx += s * wk.x
		sz += s * wk.y
	return Vector3(mean, sx, sz)


## The plane a rigid hull floating at (x, z) settles on: its waterplane is a
## rectangle `half_length` along `along` (a unit world x/z direction, the
## hull's +Z) and `half_beam` across it (`across`, the hull's +X).
## Vector3(mean height, slope along +along, slope along +across).
func hull(x: float, z: float, along: Vector2, across: Vector2, half_length: float, half_beam: float,
		bake: Variant = null) -> Vector3:
	var b: Color = bake if bake is Color else bake_at(x, z)
	var reach := grid_reach(x, z)
	var sum := _hull_of(wave_k, wave_amp, x, z, along, across, half_length, half_beam, b, reach.x)
	sum += _hull_of(swell_k, swell_amp, x, z, along, across, half_length, half_beam, b, reach.x)
	return Vector3(grid_origin().y + sum.x * reach.y, sum.y * reach.y, sum.z * reach.y)


## One set of components' hull plane (mean, slope along, slope across).
static func _hull_of(ks: PackedVector4Array, amps: PackedVector4Array, x: float, z: float, along: Vector2,
		across: Vector2, half_length: float, half_beam: float, b: Color, spacing: float) -> Vector3:
	var mean := 0.0
	var s_along := 0.0
	var s_across := 0.0
	for i in mini(ks.size(), amps.size()):
		if amps[i].w < WAVE_MIN_SPACINGS * spacing:
			break
		var a := drawn_amplitude_of(amps[i], b, spacing)
		if a < MIN_AMPLITUDE_M:
			continue
		var wk := ks[i]
		var k_along := wk.x * along.x + wk.y * along.y
		var k_across := wk.x * across.x + wk.y * across.y
		var u := k_along * half_length
		var w := k_across * half_beam
		var sinc_u := sinc(u)
		var sinc_w := sinc(w)
		var ph := wk.x * x + wk.y * z - amps[i].z
		var c := a * cos(ph)
		# Second order (see ring()): q lower on average, with a 2k ripple.
		var q := 0.5 * wk.w * wk.z * a * a
		var q2 := q * sin(2.0 * ph)
		var sinc_2u := sinc(2.0 * u)
		var sinc_2w := sinc(2.0 * w)
		mean += a * sinc_u * sinc_w * sin(ph) - q * (1.0 + sinc_2u * sinc_2w * cos(2.0 * ph))
		s_along += c * k_along * plane_fit(u) * sinc_w + q2 * 2.0 * k_along * plane_fit(2.0 * u) * sinc_2w
		s_across += c * k_across * plane_fit(w) * sinc_u + q2 * 2.0 * k_across * plane_fit(2.0 * w) * sinc_2u
	return Vector3(mean, s_along, s_across)


## Mean of cos(k x) over [-h, h] for u = k h: sin(u) / u.
static func sinc(u: float) -> float:
	if absf(u) < 1e-3:
		return 1.0 - u * u / 6.0
	return sin(u) / u


## Least-squares slope of sin(k x) over [-h, h], over k (u = k h):
## 3 (sin u - u cos u) / u^3, 1 for a wave much longer than the span.
static func plane_fit(u: float) -> float:
	if absf(u) < 1e-2:
		return 1.0 - u * u / 10.0
	return 3.0 * (sin(u) - u * cos(u)) / (u * u * u)


## Bessel J0 (Abramowitz & Stegun 9.4.1 / 9.4.3, |error| < 5e-8 / 1.6e-8).
static func bessel_j0(x: float) -> float:
	var ax := absf(x)
	if ax <= 3.0:
		var y := (x / 3.0) * (x / 3.0)
		return 1.0 + y * (-2.2499997 + y * (1.2656208 + y * (-0.3163866 + y * (0.0444479
			+ y * (-0.0039444 + y * 0.0002100)))))
	var z := 3.0 / ax
	var f0 := 0.79788456 + z * (-0.00000077 + z * (-0.00552740 + z * (-0.00009512 + z * (0.00137237
		+ z * (-0.00072805 + z * 0.00014476)))))
	var t0 := ax - 0.78539816 + z * (-0.04166397 + z * (-0.00003954 + z * (0.00262573 + z * (-0.00054125
		+ z * (-0.00029333 + z * 0.00013558)))))
	return f0 * cos(t0) / sqrt(ax)


## Bessel J1 (Abramowitz & Stegun 9.4.4 / 9.4.6, |error| < 1.3e-8 / 1.1e-8).
static func bessel_j1(x: float) -> float:
	var ax := absf(x)
	var r: float
	if ax <= 3.0:
		var y := (x / 3.0) * (x / 3.0)
		r = ax * (0.5 + y * (-0.56249985 + y * (0.21093573 + y * (-0.03954289 + y * (0.00443319
			+ y * (-0.00031761 + y * 0.00001109))))))
	else:
		var z := 3.0 / ax
		var f1 := 0.79788456 + z * (0.00000156 + z * (0.01659667 + z * (0.00017105 + z * (-0.00249511
			+ z * (0.00113653 + z * -0.00020033)))))
		var t1 := ax - 2.35619449 + z * (0.12499612 + z * (0.00005650 + z * (-0.00637879 + z * (0.00074348
			+ z * (0.00079824 + z * -0.00029166)))))
		r = f1 * cos(t1) / sqrt(ax)
	return r if x >= 0.0 else -r


## Per component, the ring's mean factor J0(kR) and its slope factor
## 2 J1(kR) / R (a wave's slope is a k cos; the ring's best plane tilts by
## a 2 J1(kR) / R cos, which tends to a k cos for a ring much smaller than
## the wave). Wavenumbers never change, so this runs once per ring radius.
func _ensure_ring(radius: float) -> void:
	if is_equal_approx(radius, _ring_radius) and _ring_mean.size() == wave_k.size() \
			and _swell_ring_mean.size() == swell_k.size():
		return
	_ring_radius = radius
	_bessel_factors(wave_k, radius, _ring_mean, _ring_slope, _ring_mean2, _ring_slope2)
	_bessel_factors(swell_k, radius, _swell_ring_mean, _swell_ring_slope, _swell_ring_mean2, _swell_ring_slope2)


## Per component the ring's Bessel factors (filled in place).
static func _bessel_factors(ks: PackedVector4Array, radius: float, r_mean: PackedFloat32Array,
		r_slope: PackedFloat32Array, r_mean2: PackedFloat32Array, r_slope2: PackedFloat32Array) -> void:
	r_mean.resize(ks.size())
	r_slope.resize(ks.size())
	r_mean2.resize(ks.size())
	r_slope2.resize(ks.size())
	for i in ks.size():
		var kr := ks[i].z * radius
		r_mean[i] = bessel_j0(kr)
		r_slope[i] = 2.0 * bessel_j1(kr) / radius
		r_mean2[i] = bessel_j0(2.0 * kr)
		r_slope2[i] = 2.0 * bessel_j1(2.0 * kr) / radius
