shader_type spatial;
render_mode skip_vertex_transform, unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

// A bank of low cloud on a fjord wall (MistBanks.gd, G3-05). The 1 x 1 quad
// of each MultiMesh instance stands upright at its contour point, scaled to
// the bank's width and height, and turns about the vertical to face the
// camera. Ragged wisps of value-noise FBM drift along it; the bank is cut
// flat at the cloud base (nothing under it, thickening over it), fades
// where it meets the slope behind or in front (the depth buffer, so no hard
// line where the card crosses the rock) and close to the camera. It is the
// deck's own light (scene-linear, as the fog is): it draws unfogged, being
// cloud the far fog would only turn its own colour.

uniform float amount : hint_range(0.0, 1.0) = 0.0;
uniform float cloud_base = 420.0;
// Metres over the base the bank takes to thicken.
uniform float base_soft = 110.0;
// The deck's light on the mist (scene-linear).
uniform vec3 mist_light = vec3(0.5);
// Metres the wisps have drifted along the bank.
uniform float drift = 0.0;
// Metres over which a bank fades into the slope it meets.
uniform float soft_m = 80.0;
// Distance (m) under which a bank fades out (the camera inside a cloud).
uniform float near_m = 260.0;
uniform sampler2D depth_tex : hint_depth_texture, filter_linear, repeat_disable;

varying vec3 v_world;
varying vec2 v_local;
varying float v_seed;

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
	float s = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		s += a * vnoise(p);
		p = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * p * 2.07 + vec2(3.7, 1.3);
		a *= 0.5;
	}
	return s / 0.9375;
}

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	vec2 to_cam = CAMERA_POSITION_WORLD.xz - origin.xz;
	vec2 flat_dir = to_cam / max(length(to_cam), 1e-3);
	vec3 right = vec3(flat_dir.y, 0.0, -flat_dir.x);
	float w = length(MODEL_MATRIX[0].xyz);
	float h = length(MODEL_MATRIX[1].xyz);
	v_local = vec2(VERTEX.x * w, VERTEX.y * h);
	v_world = origin + right * v_local.x + vec3(0.0, v_local.y, 0.0);
	v_seed = INSTANCE_CUSTOM.x;
	VERTEX = (VIEW_MATRIX * vec4(v_world, 1.0)).xyz;
	NORMAL = (VIEW_MATRIX * vec4(vec3(flat_dir.x, 0.0, flat_dir.y), 0.0)).xyz;
}

void fragment() {
	vec2 q = (UV - 0.5) * 2.0;
	// The bank's body: wide and soft at the ends, rounded over the top.
	float body = (1.0 - smoothstep(0.35, 1.0, abs(q.x))) * (1.0 - smoothstep(0.1, 1.0, abs(q.y)));
	float base = smoothstep(cloud_base - 0.15 * base_soft, cloud_base + base_soft, v_world.y);
	float shape = body * base * amount;
	if (shape < 0.003) {
		discard;
	}
	// Wisps: stretched along the bank, drifting with the wind.
	vec2 p = (v_local + vec2(drift + v_seed * 911.0, v_seed * 337.0)) / vec2(150.0, 62.0);
	float n = fbm(p);
	float wisps = smoothstep(0.32, 0.78, n * 0.8 + body * 0.35);
	// Soft where the bank meets the slope, and away from the camera.
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 scene = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene_dist = -scene.z / scene.w;
	float frag_dist = -VERTEX.z;
	float soft = clamp((scene_dist - frag_dist) / soft_m, 0.0, 1.0);
	float near = smoothstep(near_m * 0.4, near_m, frag_dist);
	ALBEDO = mist_light;
	ALPHA = clamp(shape * wisps * soft * near, 0.0, 1.0);
}
