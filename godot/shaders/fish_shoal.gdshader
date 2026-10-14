shader_type spatial;
render_mode blend_premul_alpha, depth_draw_never, cull_back, diffuse_burley, specular_disabled, fog_disabled, shadows_disabled;

// The salmon school seen through the water surface (roadmap G3-01). The
// water is opaque (it writes depth, reads no screen), so nothing under it is
// ever drawn from above. This pass draws after it, as a decal on the water
// the pen rides: every vertex goes to where the camera sees it through the
// surface (Snell's refraction, the image lifted to `decal_lift` over the
// water along the camera ray), so the hardware depth test still lets the
// collar, the handrail, the nets and the boats hide it, while the water
// itself does not. What a pixel shows is the water shader's own optics
// (Beer-Lambert over the path down and back, absorption and scatter copied
// from the water material by FishSchool.sync): a fish is a darker shape
// that fades with depth; nothing makes the water itself see-through.
//
// One mesh, three parts (UV2.x): 0 the fish (the Blender salmon's LOD1/LOD2),
// 1 a splash ring (a jumper leaving or hitting the water, the boil of a
// feeding pen), 2 the pen's water column (instance 0 only): the school as a
// whole, the net and the lice skirt seen down into the pen (the teal of a
// stocked pen in an aerial photo), added to the water's light. Premultiplied
// blending lets the column add light while the fish and the splashes cover.
#include "res://shaders/fish_school.gdshaderinc"

uniform sampler2D skin : source_color, hint_default_white, filter_linear_mipmap;
// The water's optics (water_shader.gdshader absorption and deep_scatter,
// bloom included), synced by FishSchool.sync.
uniform vec3 absorption = vec3(0.40, 0.09, 0.14);
uniform vec3 deep_scatter = vec3(0.0022, 0.0085, 0.0105);
// The environment's distance fog (Environment.fog_density).
uniform float fog_density = 0.0;
// Fish are drawn within this camera distance (m); the column always.
uniform float fish_range = 400.0;
uniform float decal_lift = 0.12;
uniform float refraction_index = 1.333;
// The water inside a stocked pen scatters more (feed fines, mucus, the
// net's growth): its deep scatter, linear, at full stock. A dense school
// shades it: what the school's mass reflects seen from above (dark backs
// in the shadow of the fish above them), linear.
uniform vec3 pen_water_scatter = vec3(0.006, 0.030, 0.033);
uniform vec3 school_albedo = vec3(0.0012, 0.003, 0.0035);
// A dense school near the surface darkens the water by at most this much.
uniform float max_darken : hint_range(0.0, 1.0) = 0.55;
// What the net and the lice skirt reflect (albedo).
uniform vec3 net_albedo : source_color = vec3(0.16, 0.20, 0.13);
uniform float net_cover = 0.3;
uniform vec3 skirt_albedo : source_color = vec3(0.788, 0.8, 0.776);
uniform vec3 foam_color : source_color = vec3(0.86, 0.9, 0.92);
// How opaque a fish is right under the surface; it fades with depth.
uniform float fish_opacity : hint_range(0.0, 1.0) = 0.92;
// How much of a silver flank mirrors the water round it (camouflage).
uniform float flank_mirror : hint_range(0.0, 1.0) = 0.85;
// A dark back seen down through the water reads darker than its dry
// albedo; the water's scatter blurs a fish wider than it is.
uniform float back_shade : hint_range(0.0, 1.0) = 0.45;
uniform float blur_width = 1.8;
// Debug: 1 draws every fish pixel magenta (the harness's fish mask).
uniform float mask_view = 0.0;
// x: the pen has a lice skirt (0/1), y: how stocked it is (0 empty .. 1
// full; the pen water's scatter follows it); zw unused.
instance uniform vec4 school_pen = vec4(0.0);

const float NET_WALL_DEPTH = 30.0;
const float SKIRT_DEPTH = 10.0;
const int COLUMN_STEPS = 7;
// Half the salmon mesh's width (m, blender/build_salmon.py: 0.12 m).
const float FISH_HALF_WIDTH = 0.06;

varying flat float v_part;
varying float v_lateral;
varying float v_depth;
varying float v_slant;
varying float v_fade;
varying float v_flash;
varying float v_above;
varying vec3 v_model;
varying vec3 v_camera;
varying vec4 v_ring;

void vertex() {
	mat3 rot = mat3(MODEL_MATRIX);
	vec3 camera = transpose(rot) * (CAMERA_POSITION_WORLD - MODEL_MATRIX[3].xyz);
	v_camera = camera;
	v_part = UV2.x;
	v_flash = 0.0;
	v_above = 0.0;
	v_depth = 0.0;
	v_slant = 0.0;
	v_ring = vec4(0.0);
	v_lateral = 0.0;
	float fade = 1.0;
	bool hide = false;
	vec3 at = vec3(0.0);
	int id = INSTANCE_ID;
	bool above = CAMERA_POSITION_WORLD.y > 0.0 && camera.y > decal_lift + 0.2;
	if (!above) {
		hide = true;
	} else if (UV2.x > 0.5 && UV2.x < 1.5 && id >= jumper_count && school_state.w <= 0.25) {
		// Only a jumper or a feeding fish splashes.
		hide = true;
	} else if (UV2.x > 1.5) {
		// The pen's water column: a disc on the water inside the net.
		hide = id != 0;
		at = vec3((UV.x * 2.0 - 1.0) * FISH_NET_R, decal_lift, (UV.y * 2.0 - 1.0) * FISH_NET_R);
	} else {
		vec3 pos;
		vec3 fwd;
		float roll;
		float flash;
		fish_pose(INSTANCE_CUSTOM, id, pos, fwd, roll, flash);
		vec3 centre = (MODEL_MATRIX * vec4(pos, 1.0)).xyz;
		bool in_range = length(CAMERA_POSITION_WORLD - centre) < fish_range;
		if (float(id) >= school_body.w || !in_range) {
			hide = true;
		} else if (UV2.x > 0.5) {
			// A splash ring on the water: a jumper's, or a feeding fish
			// breaking the surface.
			vec3 splash = fish_jump_splash(INSTANCE_CUSTOM, id, pos);
			float size = 1.4;
			if (splash.z < 0.0 && school_state.w > 0.25 && pos.y > -0.9) {
				float period = 5.0 + 7.0 * fish_rand(INSTANCE_CUSTOM.w, 2.0);
				float t = mod(school_clock.z + fish_rand(INSTANCE_CUSTOM.w, 3.0) * period, period);
				splash = t < 0.9 ? vec3(pos.x, pos.z, t) : vec3(0.0, 0.0, -1.0);
				size = 0.6 * school_state.w;
			}
			hide = splash.z < 0.0;
			vec2 corner = UV * 2.0 - 1.0;
			at = vec3(splash.x + corner.x * size, decal_lift + 0.02, splash.y + corner.y * size);
			v_ring = vec4(corner, splash.z / 1.2, INSTANCE_CUSTOM.w);
			fade = 1.0 - smoothstep(FISH_NET_R - 1.0, FISH_NET_R, length(at.xz));
		} else {
			float scale = fish_scale(centre, CAMERA_POSITION_WORLD, abs(PROJECTION_MATRIX[1][1]), VIEWPORT_SIZE.y, true);
			vec3 body = fish_body_vertex(VERTEX, INSTANCE_CUSTOM.w, roll, scale);
			v_lateral = VERTEX.x / FISH_HALF_WIDTH;
			body.x *= blur_width;
			vec3 v = pos + fish_basis(fwd) * body;
			// Where the camera sees the vertex through the surface: the
			// refracted ray from it reaches the water `run` metres towards the
			// camera (two fixed-point steps of Snell's law from the straight
			// line), and the image lies on the camera's ray through that point.
			float depth = max(-v.y, 0.0);
			vec2 to_camera = camera.xz - v.xz;
			float span = length(to_camera);
			vec2 heading = span > 1e-4 ? to_camera / span : vec2(0.0, 1.0);
			float sin_air = span / max(length(vec2(span, camera.y + depth)), 1e-4);
			float run = 0.0;
			for (int k = 0; k < 2; k++) {
				float sin_w = sin_air / refraction_index;
				run = depth * sin_w / sqrt(max(1.0 - sin_w * sin_w, 1e-4));
				float left = max(span - run, 0.0);
				sin_air = left / max(length(vec2(left, camera.y)), 1e-4);
			}
			vec3 surface = vec3(v.x + heading.x * run, 0.0, v.z + heading.y * run);
			at = camera + (surface - camera) * ((camera.y - decal_lift) / max(camera.y, 1e-3));
			v_depth = depth;
			v_slant = length(vec2(run, depth));
			v_above = v.y > 0.0 ? 1.0 : 0.0;
			v_flash = flash;
			// Fish stay in their pen: none seen past the net.
			fade = 1.0 - smoothstep(FISH_NET_R - 1.5, FISH_NET_R - 0.3, length(at.xz));
		}
	}
	VERTEX = hide ? vec3(0.0) : at;
	v_model = VERTEX;
	v_fade = hide ? 0.0 : fade;
	NORMAL = vec3(0.0, 1.0, 0.0);
}

// Round-trip transmittance to a point `depth` down along a `slant` path.
vec3 water_path(float depth, float slant) {
	return exp(-absorption * (depth + slant));
}

// The school's density across the pen frame: a ring band at the school's
// depths, patchy, the patches turning with the fish.
float school_density(vec3 p) {
	float outer = school_body.z;
	float feed = school_state.w;
	float r = length(p.xz);
	float depth = -p.y;
	float turn = school_body.y * school_clock.x * outer / max(r, 4.0);
	float a = atan(p.z, p.x) - turn;
	// The ring's edges wander, so the school has no drawn outline.
	float edge = 1.0 + 0.1 * sin(a * 3.0 + school_clock.z * 0.05) + 0.06 * sin(a * 5.0 - depth * 0.3 + 2.1);
	float lo = fish_ring_radius(0.0, feed, outer) * edge;
	float hi = fish_ring_radius(1.0, feed, outer) * edge;
	float band = smoothstep(lo - 3.0, lo + 2.0, r) * (1.0 - smoothstep(hi - 3.5, hi + 3.0, r));
	float top = school_state.x - school_state.y;
	float bottom = school_state.x + school_state.y;
	float layer = smoothstep(top - 1.0, top + 1.0, depth) * (1.0 - smoothstep(bottom - 1.0, bottom + 1.5, depth));
	// Denser and thinner stretches along the ring, drifting; a feeding crowd
	// is denser and more even.
	float n = 0.4 * sin(a * 3.0 + r * 0.21 + depth * 0.37 + 1.3)
		+ 0.35 * sin(a * 7.0 - r * 0.47 + school_clock.z * 0.031)
		+ 0.25 * sin(a * 13.0 + r * 0.83 - depth * 0.6 + school_clock.z * 0.07);
	float contrast = mix(1.0, 0.4, feed);
	float k = smoothstep(0.5 - 0.3 * contrast, 0.5 + 0.3 * contrast, 0.5 + 0.5 * n);
	return band * layer * mix(1.0, 0.15 + 1.7 * k, contrast);
}

// What the pen's water column sends back up, as the water shader's body
// colour (deep_scatter for open deep water): the pen water's scatter, the
// net (or the lice skirt) and the open water beyond it, or the deep under
// the net; where the school swims the dark of its backs covers that, as far
// as the water above it lets it show. rgb: the light it adds to the water;
// a: how much it darkens it (a dense school near the surface, up to
// max_darken).
vec4 column_light() {
	vec3 surface = vec3(v_model.x, 0.0, v_model.z);
	vec3 view = normalize(surface - v_camera);
	vec3 ray = refract(view, vec3(0.0, 1.0, 0.0), 1.0 / refraction_index);
	// Out through the net wall: |surface.xz + t ray.xz| = net radius.
	float qa = max(dot(ray.xz, ray.xz), 1e-5);
	float qb = 2.0 * dot(surface.xz, ray.xz);
	float qc = dot(surface.xz, surface.xz) - FISH_NET_R * FISH_NET_R;
	float t_wall = (-qb + sqrt(max(qb * qb - 4.0 * qa * qc, 0.0))) / (2.0 * qa);
	float down = max(-ray.y, 0.05);
	float wall_depth = down * t_wall;
	float t_end = min(t_wall, (school_state.x + school_state.y + 1.5) / down);
	float dt = t_end / float(COLUMN_STEPS);
	float thick = school_clock.w / max(2.0 * school_state.y, 1.0);
	vec3 pen_scatter = mix(deep_scatter, max(pen_water_scatter, deep_scatter), school_pen.y);
	// The empty pen's column, and how much of it the school covers (each
	// slice of fish weighted by how much of it shows through the water).
	vec3 empty = pen_scatter * (1.0 - water_path(down * t_wall, t_wall));
	float covered = 0.0;
	float seen = 1.0;
	for (int i = 0; i < COLUMN_STEPS; i++) {
		float t = (float(i) + 0.5) * dt;
		vec3 q = surface + ray * t;
		float rho = school_density(q);
		if (rho > 0.0 && thick > 0.0) {
			float ext = 1.0 - exp(-thick * rho * dt);
			covered += seen * ext * pow(water_path(-q.y, t).g, 0.6);
			seen *= 1.0 - ext;
		}
	}
	if (wall_depth < NET_WALL_DEPTH) {
		bool skirt = school_pen.x > 0.5 && wall_depth < SKIRT_DEPTH;
		vec3 wall = skirt ? skirt_albedo : net_albedo;
		float cover = skirt ? 1.0 : net_cover;
		empty += water_path(wall_depth, t_wall) * (cover * wall + (1.0 - cover) * deep_scatter);
	} else {
		empty += pen_scatter * water_path(down * t_wall, t_wall);
	}
	vec3 body = mix(empty, school_albedo, clamp(covered, 0.0, 1.0));
	float fog = exp(-fog_density * length(surface - v_camera));
	float dark = clamp(1.0 - (body.g + body.b) / (deep_scatter.g + deep_scatter.b), 0.0, max_darken);
	return vec4(max(body - deep_scatter, vec3(0.0)) * fog, dark * fog);
}

// A splash ring's foam cover (0..1).
float splash_cover() {
	float d = length(v_ring.xy);
	float age = clamp(v_ring.z, 0.0, 1.0);
	float seed = v_ring.w * 61.0;
	// A broken, widening ring of foam round a burst of spray that settles.
	float angle = atan(v_ring.y, v_ring.x);
	float broken = 0.45 + 0.35 * sin(angle * 5.0 + seed) + 0.2 * sin(angle * 11.0 - seed * 1.7);
	float radius = mix(0.18, 0.92, sqrt(age));
	float width = mix(0.06, 0.16, age);
	float ring = exp(-pow((d - radius) / width, 2.0)) * clamp(broken, 0.0, 1.0);
	float burst = exp(-d * d * 14.0) * (1.0 - smoothstep(0.0, 0.45, age)) * (0.7 + 0.3 * sin(angle * 7.0 + seed));
	return clamp(ring * 0.4 + burst * 0.85, 0.0, 1.0) * pow(1.0 - age, 1.6) * v_fade;
}

// A fish seen through the water: rgb its albedo, a its cover.
vec4 fish_seen(vec4 skin_texel) {
	vec3 through = water_path(v_depth, v_slant);
	// The silver flank mirrors the water round the fish (it hides), unless
	// the fish rolls it up into the light: a flash.
	vec3 around = deep_scatter * 2.0;
	float silver = skin_texel.a;
	vec3 seen = mix(skin_texel.rgb * back_shade, around, silver * flank_mirror * (1.0 - v_flash)) + v_flash * silver * vec3(0.55, 0.6, 0.62);
	// Under the top of a dense school the fish above take the light.
	float top = school_state.x - school_state.y;
	float shade = exp(-max(v_depth - top, 0.0) * school_clock.w / max(2.0 * school_state.y, 1.0));
	vec3 body = deep_scatter * (1.0 - through) + seen * shade * through;
	float a = clamp(through.g * 1.6, 0.0, fish_opacity) * v_fade;
	// Soft sides: the scatter blurs the fish's edge.
	a *= 1.0 - smoothstep(0.35, 1.0, abs(v_lateral));
	a *= exp(-fog_density * length(v_model - v_camera));
	return vec4(body, a);
}

void fragment() {
	NORMAL = normalize((VIEW_MATRIX * vec4(normalize(MODEL_MATRIX[1].xyz), 0.0)).xyz);
	vec3 colour = vec3(0.0);
	float cover = 0.0;
	float premul = 1.0;
	if (v_part > 1.5) {
		if (length(v_model.xz) > FISH_NET_R) {
			discard;
		}
		vec4 light = column_light();
		colour = light.rgb;
		cover = light.a;
		if (mask_view > 0.5) {
			cover = step(0.002, max(colour.g, colour.b)) * 0.5;
			colour = vec3(0.0, 1.0, 0.0);
			premul = cover;
		}
	} else if (v_part > 0.5) {
		cover = splash_cover();
		if (cover < 0.004) {
			discard;
		}
		colour = mask_view > 0.5 ? vec3(1.0, 0.0, 1.0) : foam_color;
		premul = cover;
	} else {
		if (v_above > 0.5 || v_fade <= 0.0) {
			discard;
		}
		vec4 f = fish_seen(texture(skin, UV));
		if (f.a < 0.004) {
			discard;
		}
		colour = mask_view > 0.5 ? vec3(1.0, 0.0, 1.0) : f.rgb;
		cover = mask_view > 0.5 ? 1.0 : f.a;
		premul = cover;
	}
	ALBEDO = colour;
	ALPHA = cover;
	PREMUL_ALPHA_FACTOR = premul;
}
