shader_type canvas_item;
render_mode unshaded;

// The underwater lens (roadmap G3-04; it replaced underwater_post.gdshader,
// which blurred and tinted the whole screen and painted a caustic pattern
// over it: the water was never really there). The water itself is 3D now:
// the fog of the sim's visibility, the light filtered by depth, the
// surface's underside with Snell's window, caustics on the net, marine snow
// and light shafts. This overlay only adds what a camera housing adds, and
// reads no screen texture (nothing is copied, nothing is blurred):
//   vignette  the corners a little darker under water (a dome port's falloff);
//   veil      a moment of the water's own colour as the camera crosses the
//             surface (UnderwaterView: up at once, gone in 0.45 s), which hides
//             the frame where the surface and the fog change over.

uniform float vignette : hint_range(0.0, 1.0) = 0.0;
uniform float veil : hint_range(0.0, 1.0) = 0.0;
uniform vec4 veil_color : source_color = vec4(0.05, 0.12, 0.12, 1.0);

void fragment() {
	vec2 centred = (SCREEN_UV - 0.5) * vec2(SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x, 1.0);
	// Corners at about 0.9 of the half-diagonal; a wide, soft falloff.
	float r = length(centred) / 0.9;
	float dark = vignette * smoothstep(0.45, 1.05, r);
	// Darken toward black at the corners; the veil covers in the water's colour.
	vec3 rgb = mix(vec3(0.0), veil_color.rgb, veil / max(veil + dark, 1e-4));
	COLOR = vec4(rgb, clamp(dark + veil - dark * veil, 0.0, 1.0));
}
