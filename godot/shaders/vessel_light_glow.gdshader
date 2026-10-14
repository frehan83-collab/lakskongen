shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/light_glow.gdshaderinc"

// A vessel's lights after dark (VesselLights.gd): masthead, side lights and
// stern light, the feed barge's all-round mast light, yellow marker and deck
// lamps, each with its glitter on the water (light_glow.gdshaderinc). One
// glow per light in a MultiMesh on the vessel:
//   COLOR            the light's colour
//   INSTANCE_CUSTOM  x the middle of its arc (rad from dead ahead, + to
//                    starboard), y half its arc (rad; >= 3.1 all round),
//                    z its flash period (s; 0 steady), w the core's screen
//                    brightness
// A light shows only inside its arc (COLREGS: a side light 112.5 degrees
// from ahead, a masthead light 225 degrees forward, a stern light 135 aft),
// so a boat's red, green and white read the way it lies. Flashing lights
// share the global TIME with the site's corner lights: they flash together.
// Like the corner lights, the lights are on a photocell (`night`) and keep
// their screen brightness whatever the eye's exposure (`exposure_comp`,
// SiteLights.sync); the distance fog dims them like the water round them.
uniform float size_m = 0.45;
uniform float min_pixels = 6.0;
uniform float flash_s = 0.5;
uniform float night : hint_range(0.0, 1.0) = 1.0;
uniform float exposure_comp = 1.0;
uniform float fog_density = 0.0;

varying vec4 v_light;
varying float v_seen;
varying float v_dist;
varying float v_flash;
varying flat float v_part;
varying float v_seed;

void vertex() {
	vec3 lamp = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	// Where the camera lies in the vessel's frame (bow -Z, starboard +X).
	vec3 local = transpose(mat3(MODEL_MATRIX)) * (CAMERA_POSITION_WORLD - lamp);
	float bearing = atan(local.x, -local.z);
	float off = abs(mod(bearing - INSTANCE_CUSTOM.x + PI, TAU) - PI);
	float half_arc = INSTANCE_CUSTOM.y;
	v_seen = half_arc >= 3.1 ? 1.0 : 1.0 - smoothstep(half_arc - 0.04, half_arc + 0.04, off);
	v_light = vec4(COLOR.rgb, INSTANCE_CUSTOM.w);
	v_flash = INSTANCE_CUSTOM.z;
	v_part = UV2.x;
	v_seed = fract(lamp.x * 0.173 + lamp.z * 0.113) * 6.28;
	float dist;
	if (UV2.x < 0.5) {
		POSITION = glow_billboard(lamp, VERTEX.xy, size_m, min_pixels, VIEW_MATRIX, PROJECTION_MATRIX, VIEWPORT_SIZE.y, dist);
	} else {
		// The vessel's origin is its waterline.
		vec3 strip = glow_reflection(lamp, NODE_POSITION_WORLD.y, UV, CAMERA_POSITION_WORLD, abs(PROJECTION_MATRIX[1][1]), VIEWPORT_SIZE.y);
		POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(strip, 1.0);
		dist = length(CAMERA_POSITION_WORLD - strip);
	}
	v_dist = dist;
}

void fragment() {
	float on = 1.0;
	if (v_flash > 0.0) {
		float t = mod(TIME, v_flash);
		on = smoothstep(0.0, 0.05, t) * (1.0 - smoothstep(flash_s, flash_s + 0.08, t));
	}
	float a = v_part < 0.5 ? glow_cover(UV) : reflection_cover(UV, TIME, v_seed);
	ALBEDO = v_light.rgb * v_light.a * exposure_comp * exp(-fog_density * v_dist);
	ALPHA = clamp(a * v_seen * on * night, 0.0, 1.0);
}
