shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

// A raindrop's motion-blurred streak (G3-05): the quad stands along the
// drop's velocity (the process material's align_y turns each particle's Y
// axis onto it) and turns about that axis to face the camera, so rain driven
// by a gale leans with the wind instead of standing upright. The streak
// fades near the camera (a drop a metre away is a blur, not a bar) and with
// distance (beyond a few tens of metres rain is a veil, which the fog draws).

uniform vec4 streak_color : source_color = vec4(0.72, 0.78, 0.86, 0.22);
// Quad size in metres: x across the streak, y along it.
uniform vec2 streak_size = vec2(0.012, 0.55);
uniform float fade_near_m = 2.5;
uniform float fade_far_m = 16.0;

varying float v_fade;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	vec3 axis = MODEL_MATRIX[1].xyz;
	float scale = length(axis);
	axis = scale > 1e-5 ? axis / scale : vec3(0.0, 1.0, 0.0);
	vec3 to_camera = CAMERA_POSITION_WORLD - origin;
	float dist = length(to_camera);
	vec3 side = cross(axis, to_camera / max(dist, 1e-4));
	float side_len = length(side);
	// Seen end-on the streak has no side: keep it a thin dot.
	side = side_len > 1e-3 ? side / side_len : normalize(cross(axis, vec3(1.0, 0.0, 0.0)));
	vec3 world = origin + side * VERTEX.x * streak_size.x * scale + axis * VERTEX.y * streak_size.y * scale;
	POSITION = PROJECTION_MATRIX * (VIEW_MATRIX * vec4(world, 1.0));
	v_fade = smoothstep(fade_near_m * 0.5, fade_near_m, dist) * (1.0 - smoothstep(fade_far_m * 0.6, fade_far_m, dist));
}

void fragment() {
	// Soft across the streak, tapered at both ends.
	float across = 1.0 - abs(UV.x * 2.0 - 1.0);
	float along = sin(UV.y * 3.14159265);
	ALBEDO = streak_color.rgb;
	ALPHA = streak_color.a * v_fade * smoothstep(0.0, 0.6, across) * along;
}
