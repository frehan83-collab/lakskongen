class_name PuffKit
extends RefCounted
## The soft white puffs the weather drama's particles are drawn with (roadmap
## F2-24): UnitSpray's spray and RoofSteam's steam share these helpers, as
## PenSpray draws its own. A puff is a camera-facing quad, lit like the foam
## (white in daylight, grey under a deck, dark at night), whose alpha is the
## particle's colour ramp.

static var _puff_texture: ImageTexture = null

## The puff's size in texels, and how its cloud noise looks (a fixed seed: every
## puff is the same cloud, and the particles' random turn tells them apart).
const PUFF_TEXELS := 64
const PUFF_NOISE_FREQUENCY := 0.07
const PUFF_NOISE_SEED := 7


## A cloud of vapour: opaque in the middle, clear at the rim, torn by noise.
static func puff_texture() -> ImageTexture:
	if _puff_texture == null:
		var noise := FastNoiseLite.new()
		noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
		noise.frequency = PUFF_NOISE_FREQUENCY
		noise.fractal_octaves = 3
		noise.seed = PUFF_NOISE_SEED
		var image := Image.create(PUFF_TEXELS, PUFF_TEXELS, false, Image.FORMAT_RGBA8)
		var half := PUFF_TEXELS * 0.5
		for y in PUFF_TEXELS:
			for x in PUFF_TEXELS:
				var r := Vector2(x + 0.5 - half, y + 0.5 - half).length() / half
				var body := smoothstep(1.0, 0.2, r)
				var torn := 0.75 + 0.55 * noise.get_noise_2d(x, y)
				image.set_pixel(x, y, Color(1.0, 1.0, 1.0, clampf(body * torn, 0.0, 1.0)))
		_puff_texture = ImageTexture.create_from_image(image)
	return _puff_texture


## The quad a puff is drawn on: `size_m` across, `tint` over the white.
static func puff_mesh(size_m: float, tint: Color) -> QuadMesh:
	var mat := StandardMaterial3D.new()
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.billboard_mode = BaseMaterial3D.BILLBOARD_PARTICLES
	mat.vertex_color_use_as_albedo = true
	mat.albedo_color = tint
	mat.albedo_texture = puff_texture()
	mat.roughness = 1.0
	mat.specular_mode = BaseMaterial3D.SPECULAR_DISABLED
	mat.disable_receive_shadows = true
	var quad := QuadMesh.new()
	quad.size = Vector2(size_m, size_m)
	quad.material = mat
	return quad


## A particle colour ramp: clear at birth, `peak` alpha by `rise` of its life
## (0-1), clear again at its end.
static func fade_ramp(peak: float, rise: float) -> GradientTexture1D:
	var fade := Gradient.new()
	fade.offsets = PackedFloat32Array([0.0, rise, 1.0])
	fade.colors = PackedColorArray([Color(1, 1, 1, 0.0), Color(1, 1, 1, peak), Color(1, 1, 1, 0.0)])
	var tex := GradientTexture1D.new()
	tex.gradient = fade
	return tex


## A scale over a particle's life: `from` at birth, `to` at its end.
static func grow_curve(from: float, to: float) -> CurveTexture:
	var curve := Curve.new()
	curve.add_point(Vector2(0.0, from))
	curve.add_point(Vector2(1.0, to))
	var tex := CurveTexture.new()
	tex.curve = curve
	return tex


## The texture ParticleProcessMaterial.EMISSION_SHAPE_POINTS reads: one texel
## (three floats) per point, in the emitter's local space.
static func points_texture(points: PackedVector3Array) -> ImageTexture:
	var floats := PackedFloat32Array()
	for p in points:
		floats.append(p.x)
		floats.append(p.y)
		floats.append(p.z)
	# A texture has at least one texel; the emitter's point count says how many are read.
	var texels := maxi(points.size(), 1)
	if points.is_empty():
		floats.append_array(PackedFloat32Array([0.0, 0.0, 0.0]))
	var image := Image.create_from_data(texels, 1, false, Image.FORMAT_RGBF, floats.to_byte_array())
	return ImageTexture.create_from_image(image)
