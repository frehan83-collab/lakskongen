shader_type canvas_item;

// The region level's suitability heat (geo stages 2-3 plan Track D,
// docs/geo/GEODATA.md §2.1 and §7.3), drawn over an area's base map by a
// TextureRect that shows the base map's own texture, so UV spans the drawn
// base map. The raw suitability/poNN.png is sampled nearest, never filtered:
//   R  score byte: 0 no score, else score = (R - 1) / 254 -> cool-to-gold ramp
//   G  reason code: 4..12 hard exclusions -> a muted hatch; 13 outside the
//      planning area -> a quieter, sparser hatch; 1 land, 2 outside the area
//      and 3 no depth stay clear.
// The transform from base-map pixels to grid UV is GeoRegionMath's:
//   uv = UV * map_size * suit_scale + suit_offset
// and GeoRegionMath.heat_color is this fragment's CPU twin; the headless
// checks pin both on the CPU, since only GitHub's windowed step compiles
// shaders. Keep this minimal.

uniform sampler2D suit_tex : filter_nearest, repeat_disable;
uniform vec2 map_size = vec2(1.0, 1.0);
uniform vec2 suit_scale = vec2(1.0, 1.0);
uniform vec2 suit_offset = vec2(0.0, 0.0);
uniform float show_heat = 1.0;
uniform float show_hatch = 1.0;
uniform float hatch_spacing_px = 6.0;

// GeoRegionMath.RAMP_STOPS / RAMP_COLORS and HEAT_ALPHA_LOW / _HIGH (pinned
// equal by tests/test_geo_map_contract.py).
const float STOP0 = 0.5;
const float STOP1 = 0.7;
const float STOP2 = 0.85;
const float ALPHA_LOW = 0.30;
const float ALPHA_HIGH = 0.66;
const vec3 RAMP0 = vec3(0.122, 0.369, 0.451);
const vec3 RAMP1 = vec3(0.369, 0.635, 0.604);
const vec3 RAMP2 = vec3(0.776, 0.737, 0.373);
const vec3 RAMP3 = vec3(0.910, 0.725, 0.165);
// GeoRegionMath.HATCH_COLOR and HATCH_QUIET_COLOR.
const vec4 HATCH = vec4(0.70, 0.58, 0.56, 0.42);
const vec4 HATCH_QUIET = vec4(0.62, 0.66, 0.70, 0.18);

vec3 ramp(float t) {
	t = clamp(t, 0.0, 1.0);
	if (t <= STOP0) {
		return RAMP0;
	}
	if (t <= STOP1) {
		return mix(RAMP0, RAMP1, (t - STOP0) / (STOP1 - STOP0));
	}
	if (t <= STOP2) {
		return mix(RAMP1, RAMP2, (t - STOP1) / (STOP2 - STOP1));
	}
	return mix(RAMP2, RAMP3, (t - STOP2) / (1.0 - STOP2));
}

void fragment() {
	vec2 uv = UV * map_size * suit_scale + suit_offset;
	vec4 c = vec4(0.0);
	if (uv.x >= 0.0 && uv.y >= 0.0 && uv.x < 1.0 && uv.y < 1.0) {
		vec3 cell = texture(suit_tex, uv).rgb;
		int r = int(round(cell.r * 255.0));
		int g = int(round(cell.g * 255.0));
		float diagonal = mod(FRAGCOORD.x + FRAGCOORD.y, hatch_spacing_px);
		if (r > 0) {
			float t = float(r - 1) / 254.0;
			float a = mix(ALPHA_LOW, ALPHA_HIGH, smoothstep(STOP0, 1.0, t));
			c = vec4(ramp(t), a * show_heat);
		} else if (g >= 4 && g <= 12) {
			c = diagonal < 1.25 ? HATCH : vec4(0.0);
			c.a *= show_hatch;
		} else if (g == 13) {
			float sparse = mod(FRAGCOORD.x + FRAGCOORD.y, hatch_spacing_px * 2.0);
			c = sparse < 1.0 ? HATCH_QUIET : vec4(0.0);
			c.a *= show_hatch;
		}
	}
	COLOR = c;
}
