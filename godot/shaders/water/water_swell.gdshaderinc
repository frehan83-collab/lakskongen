// The open sea's swell (roadmap F2-21, farm-types spec §4 "Wave driver"),
// included by water_shader.gdshader after component_amplitude() and
// wave_phase(), whose rules it shares.
//
// Offshore, the sea is a wind sea plus a long swell from distant storms.
// EnvironmentDirector (OpenSea.gd) splits the Hs of the offshore plot the
// camera is at into the two (Σa² = Hs²/8 across them) and writes the swell
// here: two bands beyond the wind sea's longest (280 m and 160 m, at the
// offshore model's 9-14 s swell periods), each as two long-crested Gerstner
// components, detuned (152-168 m and 265-295 m). Its sheltered amplitude
// (swell_amp .x) is 0: the baked exposure blends it in, so the open coast
// heaves on it and the sheltered fjord stays calm. Phases are integrated on
// the CPU; WaveField.gd carries the same components, so the units ride the
// swell the player sees.
//
// The displacement rides the grid like the wind sea's (the band limit at
// 3-6 grid spacings, the shallow-water fade). Its slope varies over
// hundreds of metres, so it is evaluated per vertex (swell_slope, the
// v_swell varying) and costs the fragment stage nothing; it fades out of
// the normal only where a pixel's footprint nears the swell's wavelength.

#define SWELL_WAVES 4

// swell_k[i] = (wavevector x, wavevector z, wavenumber k, choppiness Q);
// swell_amp[i] = (sheltered amplitude 0, exposed amplitude m, phase rad, wavelength m).
uniform vec4 swell_k[SWELL_WAVES];
uniform vec4 swell_amp[SWELL_WAVES];
// The largest exposed amplitude in swell_amp (m; EnvironmentDirector, 0 with
// no swell). A component draws a = exposure x its amplitude (x a shallow
// fade <= 1) and is skipped under 0.1 mm, so where exposure x swell_peak is
// under that every component is: the vertex skips the loops at once. A game
// with no offshore unit and the sheltered fjord pay one multiply, not the
// loops (wave4b int review: they cost +0.33-0.50 ms p50 at 1080p High with
// every amplitude 0). Drawing is unchanged: the same skip, made early.
uniform float swell_peak = 0.0;

// True when no swell component can reach 0.1 mm at this exposure.
bool swell_skips(float exposure) {
	return exposure * swell_peak < 1e-4;
}

// The swell's Gerstner displacement at a grid point (water_shader displacement()'s terms).
vec3 swell_displacement(vec2 p, float exposure, float depth, float grid_spacing) {
	vec3 d = vec3(0.0);
	if (swell_skips(exposure)) {
		return d;
	}
	for (int i = 0; i < SWELL_WAVES; i++) {
		vec4 wk = swell_k[i];
		vec4 wa = swell_amp[i];
		if (wa.w < 3.0 * grid_spacing) {
			continue;
		}
		float a = component_amplitude(wa, exposure, depth) * smoothstep(3.0 * grid_spacing, 6.0 * grid_spacing, wa.w);
		if (a < 1e-4) {
			continue;
		}
		float ph = wave_phase(wk, wa, p);
		d.y += a * sin(ph);
		d.xz += wk.w * a * (wk.xy / wk.z) * cos(ph);
	}
	return d;
}

// The swell's analytic normal terms at a grid point, laid out as the normal
// is (x: dh/dx, y: the Gerstner vertical term, z: dh/dz): what the fragment
// subtracts from the normal, as it does for each wind component.
vec3 swell_slope(vec2 p, float exposure, float depth) {
	vec3 s = vec3(0.0);
	if (swell_skips(exposure)) {
		return s;
	}
	for (int i = 0; i < SWELL_WAVES; i++) {
		vec4 wk = swell_k[i];
		vec4 wa = swell_amp[i];
		float a = component_amplitude(wa, exposure, depth);
		if (a < 1e-4) {
			continue;
		}
		float ph = wave_phase(wk, wa, p);
		float c = cos(ph);
		s.x += wk.x * a * c;
		s.y += wk.w * wk.z * a * sin(ph);
		s.z += wk.y * a * c;
	}
	return s;
}

// How much of the swell's slope a pixel keeps: all of it until its
// footprint (m) nears the swell's shortest wavelength.
float swell_keep(float footprint) {
	return 1.0 - smoothstep(40.0, 80.0, footprint);
}
