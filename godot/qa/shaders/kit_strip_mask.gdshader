shader_type spatial;
render_mode unshaded, cull_back;

// QA only (F2-25 land-kit preview): paints a facility-kit mesh by atlas
// strip so a mask frame can find one tank's lid, pale wall and band on
// screen. Lid strips cyan, the pale wall red, the band magenta, the rest
// black (it still occludes). Row numbers follow build_land_base.STRIPS.
uniform float atlas_strips = 32.0;

void fragment() {
	float row = floor(UV.y * atlas_strips);
	vec3 c = vec3(0.0);
	if (row == 2.0 || row == 3.0) {
		c = vec3(0.0, 1.0, 1.0);
	} else if (row == 0.0) {
		c = vec3(1.0, 0.0, 0.0);
	} else if (row == 1.0) {
		c = vec3(1.0, 0.0, 1.0);
	}
	ALBEDO = c;
}
