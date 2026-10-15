class_name MoodMetrics
extends RefCounted
## Measures for the mood goldens (G3-05; ScreenshotHarness, MoodGoldens).
##
## - Regions: every sampled pixel of a frame is sky, water or land, from
##   the camera's rays against the terrain's collision and the water plane
##   (the frame's own colours never decide it: a storm's fog makes land and
##   sky look alike, which is what the silhouette check must catch).
## - Colour: a region's mean colour, averaged in CIELAB (D65): the colour
##   the region reads as, so a bright strip of far water mirroring the
##   horizon does not outweigh the water round the farm, and its CIEDE2000
##   distance to a spec colour or to the nearest point of a spec range
##   (EnvironmentMood.SPECS).
## - pHash: imagehash's perceptual hash (32x32 greyscale, 2-D DCT-II, the
##   8x8 low frequencies against their median) and the Hamming distance.

enum Region { SKY, WATER, LAND, OTHER }


## Region of every `step`-th pixel of a frame of `img_size` (row-major over
## the sampled grid). Water further than `water_reach_m` (the haze at the
## horizon) or within `exclude_radius_m` of a point in `exclude` (pens,
## boats) is OTHER.
static func classify(cam: Camera3D, img_size: Vector2i, step: int, water_y: float,
		water_reach_m: float, exclude: Array, exclude_radius_m: float) -> PackedByteArray:
	var out := PackedByteArray()
	var space := cam.get_world_3d().direct_space_state
	var view := cam.get_viewport().get_visible_rect().size
	var cols := img_size.x / step
	var rows := img_size.y / step
	out.resize(cols * rows)
	for j in rows:
		for i in cols:
			var vp := Vector2((i * step + step * 0.5) / float(img_size.x) * view.x,
				(j * step + step * 0.5) / float(img_size.y) * view.y)
			out[j * cols + i] = _classify_ray(space, cam.project_ray_origin(vp), cam.project_ray_normal(vp),
				water_y, water_reach_m, exclude, exclude_radius_m)
	return out


static func _classify_ray(space: PhysicsDirectSpaceState3D, from: Vector3, dir: Vector3, water_y: float,
		water_reach_m: float, exclude: Array, exclude_radius_m: float) -> int:
	var t_water := (water_y - from.y) / dir.y if dir.y < -1e-5 else INF
	var reach := minf(t_water, 30000.0)
	var query := PhysicsRayQueryParameters3D.create(from, from + dir * reach)
	var hit := space.intersect_ray(query)
	if not hit.is_empty():
		var at: Vector3 = hit["position"]
		if at.y > water_y + 0.3:
			return Region.LAND
	if t_water == INF:
		return Region.SKY if hit.is_empty() else Region.LAND
	if t_water > water_reach_m:
		return Region.OTHER
	var p := from + dir * t_water
	for e: Vector3 in exclude:
		if Vector2(p.x - e.x, p.z - e.z).length() < exclude_radius_m:
			return Region.OTHER
	return Region.WATER


## Mean CIELAB colour of a region's sampled pixels; `skip` (a Callable
## taking a Color) leaves pixels out. Returns Vector4(L, a, b, share of the
## frame's samples counted).
static func region_lab(img: Image, regions: PackedByteArray, step: int, region: int, skip: Callable = Callable()) -> Vector4:
	var cols := img.get_width() / step
	var sum := Vector3.ZERO
	var count := 0
	for k in regions.size():
		if regions[k] != region:
			continue
		var c := img.get_pixel((k % cols) * step + step / 2, (k / cols) * step + step / 2)
		if skip.is_valid() and bool(skip.call(c)):
			continue
		sum += srgb_to_lab(c)
		count += 1
	if count == 0:
		return Vector4(0.0, 0.0, 0.0, 0.0)
	var mean := sum / count
	return Vector4(mean.x, mean.y, mean.z, float(count) / float(maxi(regions.size(), 1)))


## Mean luminance (0-1, of the sRGB values) of a region's sampled pixels.
static func region_luma(img: Image, regions: PackedByteArray, step: int, region: int) -> float:
	var cols := img.get_width() / step
	var sum := 0.0
	var count := 0
	for k in regions.size():
		if regions[k] == region:
			sum += img.get_pixel((k % cols) * step + step / 2, (k / cols) * step + step / 2).get_luminance()
			count += 1
	return sum / count if count > 0 else 0.0


## The aurora's green (as the harness's 09 counts it): left out of a night
## sky's mean, which the spec gives as the sky behind the curtains.
static func is_aurora(c: Color) -> bool:
	return c.g > 0.12 and c.g - maxf(c.r, c.b) > 0.05


# ── CIELAB and CIEDE2000 ──────────────────────────────────────────────────────

static func srgb_to_lab(c: Color) -> Vector3:
	var lin := c.srgb_to_linear()
	# sRGB (D65) to XYZ, then to Lab against the D65 white.
	var x := (0.4124564 * lin.r + 0.3575761 * lin.g + 0.1804375 * lin.b) / 0.95047
	var y := 0.2126729 * lin.r + 0.7151522 * lin.g + 0.0721750 * lin.b
	var z := (0.0193339 * lin.r + 0.1191920 * lin.g + 0.9503041 * lin.b) / 1.08883
	var fx := _lab_f(x)
	var fy := _lab_f(y)
	var fz := _lab_f(z)
	return Vector3(116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz))


static func _lab_f(t: float) -> float:
	return pow(t, 1.0 / 3.0) if t > 216.0 / 24389.0 else (24389.0 / 27.0 * t + 16.0) / 116.0


## CIEDE2000 colour difference (Sharma, Wu and Dalal 2005), kL = kC = kH = 1.
static func delta_e2000(lab1: Vector3, lab2: Vector3) -> float:
	var c1 := sqrt(lab1.y * lab1.y + lab1.z * lab1.z)
	var c2 := sqrt(lab2.y * lab2.y + lab2.z * lab2.z)
	var c_bar := 0.5 * (c1 + c2)
	var c7 := pow(c_bar, 7.0)
	var g := 0.5 * (1.0 - sqrt(c7 / (c7 + pow(25.0, 7.0))))
	var a1 := (1.0 + g) * lab1.y
	var a2 := (1.0 + g) * lab2.y
	var cp1 := sqrt(a1 * a1 + lab1.z * lab1.z)
	var cp2 := sqrt(a2 * a2 + lab2.z * lab2.z)
	var h1 := 0.0 if (a1 == 0.0 and lab1.z == 0.0) else fposmod(rad_to_deg(atan2(lab1.z, a1)), 360.0)
	var h2 := 0.0 if (a2 == 0.0 and lab2.z == 0.0) else fposmod(rad_to_deg(atan2(lab2.z, a2)), 360.0)
	var dl := lab2.x - lab1.x
	var dc := cp2 - cp1
	var dh := 0.0
	if cp1 * cp2 != 0.0:
		dh = h2 - h1
		if dh > 180.0:
			dh -= 360.0
		elif dh < -180.0:
			dh += 360.0
	var d_big_h := 2.0 * sqrt(cp1 * cp2) * sin(deg_to_rad(dh * 0.5))
	var l_bar := 0.5 * (lab1.x + lab2.x)
	var cp_bar := 0.5 * (cp1 + cp2)
	var h_bar := h1 + h2
	if cp1 * cp2 != 0.0:
		if absf(h1 - h2) <= 180.0:
			h_bar = 0.5 * (h1 + h2)
		elif h1 + h2 < 360.0:
			h_bar = 0.5 * (h1 + h2 + 360.0)
		else:
			h_bar = 0.5 * (h1 + h2 - 360.0)
	var t := 1.0 - 0.17 * cos(deg_to_rad(h_bar - 30.0)) + 0.24 * cos(deg_to_rad(2.0 * h_bar)) \
		+ 0.32 * cos(deg_to_rad(3.0 * h_bar + 6.0)) - 0.20 * cos(deg_to_rad(4.0 * h_bar - 63.0))
	var d_theta := 30.0 * exp(-pow((h_bar - 275.0) / 25.0, 2.0))
	var cp7 := pow(cp_bar, 7.0)
	var r_c := 2.0 * sqrt(cp7 / (cp7 + pow(25.0, 7.0)))
	var l50 := (l_bar - 50.0) * (l_bar - 50.0)
	var s_l := 1.0 + 0.015 * l50 / sqrt(20.0 + l50)
	var s_c := 1.0 + 0.045 * cp_bar
	var s_h := 1.0 + 0.015 * cp_bar * t
	var r_t := -sin(deg_to_rad(2.0 * d_theta)) * r_c
	var tl := dl / s_l
	var tc := dc / s_c
	var th := d_big_h / s_h
	return sqrt(tl * tl + tc * tc + th * th + r_t * tc * th)


## CIELAB back to sRGB (D65; out-of-gamut values clamped), for reports.
static func lab_to_srgb(lab: Vector3) -> Color:
	var fy := (lab.x + 16.0) / 116.0
	var fx := fy + lab.y / 500.0
	var fz := fy - lab.z / 200.0
	var x := _lab_f_inv(fx) * 0.95047
	var y := _lab_f_inv(fy)
	var z := _lab_f_inv(fz) * 1.08883
	var r := 3.2404542 * x - 1.5371385 * y - 0.4985314 * z
	var g := -0.9692660 * x + 1.8760108 * y + 0.0415560 * z
	var b := 0.0556434 * x - 0.2040259 * y + 1.0572252 * z
	return Color(clampf(r, 0.0, 1.0), clampf(g, 0.0, 1.0), clampf(b, 0.0, 1.0)).linear_to_srgb()


static func _lab_f_inv(t: float) -> float:
	return t * t * t if t * t * t > 216.0 / 24389.0 else (116.0 * t - 16.0) * 27.0 / 24389.0


## CIEDE2000 from a CIELAB colour to a spec: one hex, or a range of two (the
## nearest of 41 points along the segment between them in CIELAB).
static func delta_e_to_spec(lab: Vector3, spec: Array) -> float:
	var a := srgb_to_lab(Color(String(spec[0])))
	if spec.size() < 2:
		return delta_e2000(lab, a)
	var b := srgb_to_lab(Color(String(spec[1])))
	var best := INF
	for i in 41:
		best = minf(best, delta_e2000(lab, a.lerp(b, i / 40.0)))
	return best


# ── Perceptual hash (imagehash.phash) ─────────────────────────────────────────

## 64-bit perceptual hash: greyscale (ITU-R 601 luma, as PIL's "L"), 32x32
## Lanczos, the 8x8 lowest frequencies of the 2-D DCT-II, each bit set
## where the coefficient is above their median.
static func phash(img: Image) -> PackedByteArray:
	var small := img.duplicate() as Image
	small.convert(Image.FORMAT_RGB8)
	small.resize(32, 32, Image.INTERPOLATE_LANCZOS)
	var px := PackedFloat32Array()
	px.resize(1024)
	for y in 32:
		for x in 32:
			var c := small.get_pixel(x, y)
			px[y * 32 + x] = roundf((c.r * 299.0 + c.g * 587.0 + c.b * 114.0) / 1000.0 * 255.0)
	var cos_table := PackedFloat32Array()
	cos_table.resize(8 * 32)
	for k in 8:
		for n in 32:
			cos_table[k * 32 + n] = cos(PI * k * (2.0 * n + 1.0) / 64.0)
	var coeffs := PackedFloat32Array()
	coeffs.resize(64)
	for u in 8:
		for v in 8:
			var sum := 0.0
			for y in 32:
				var cy := cos_table[u * 32 + y]
				for x in 32:
					sum += px[y * 32 + x] * cy * cos_table[v * 32 + x]
			coeffs[u * 8 + v] = sum
	var sorted := coeffs.duplicate()
	sorted.sort()
	var median := 0.5 * (sorted[31] + sorted[32])
	var bits := PackedByteArray()
	bits.resize(64)
	for i in 64:
		bits[i] = 1 if coeffs[i] > median else 0
	return bits


static func hamming(a: PackedByteArray, b: PackedByteArray) -> int:
	var d := 0
	for i in mini(a.size(), b.size()):
		if a[i] != b[i]:
			d += 1
	return d


## The hash as 16 hex digits (imagehash's order: row-major bits, MSB first).
static func phash_hex(bits: PackedByteArray) -> String:
	var out := ""
	for nibble in 16:
		var v := 0
		for b in 4:
			v = v * 2 + bits[nibble * 4 + b]
		out += "%x" % v
	return out
