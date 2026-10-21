shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, fog_disabled, shadows_disabled;

// Marine snow (roadmap G3-04, UnderwaterView's particles): the flakes of
// organic matter that drift and sink through every coastal water, lit by
// the light at their depth. Billboards (each flake faces the camera, its
// particle scale kept), soft discs, added light, unfogged: the shader dims
// a flake by the water between it and the camera itself, and fades the
// nearest so none smears across the lens.

// The light a flake scatters back (linear radiance; UnderwaterView sets it
// from the light at the camera's depth), the fog's attenuation (1/m).
uniform vec3 snow_light = vec3(0.05);
uniform float attenuation = 0.4;
uniform float near_fade = 0.3;

varying float v_bright;

void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(
		vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
		vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
		vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0),
		vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	// Some flakes are larger aggregates that catch more light.
	vec3 p = MODEL_MATRIX[3].xyz;
	v_bright = 0.45 + 0.55 * fract(sin(dot(floor(p * 3.0), vec3(12.9898, 78.233, 37.719))) * 43758.5453);
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float r2 = dot(c, c);
	float disc = exp(-r2 * 3.0) * (1.0 - smoothstep(0.7, 1.0, r2));
	float dist = length(VERTEX);
	float seen = exp(-attenuation * dist) * smoothstep(near_fade, near_fade * 3.0, dist);
	ALBEDO = snow_light * (disc * seen * v_bright);
}
