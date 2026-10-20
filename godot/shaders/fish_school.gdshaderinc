// A pen's salmon school on the GPU (roadmap G3-01).
//
// Every fish's own numbers are written into the MultiMesh custom data once,
// when the school is made (FishSchool.gd): x its angle round the pen (turns),
// y where it swims across the school's ring (0 inner .. 1 outer), z its depth
// rank (0 top .. 1 bottom), w a random seed. Its instance transform is the
// identity and is never written again. Everything that moves comes from three
// per-pen instance uniforms that FishSchool.gd sets (two of them a frame):
//
//   school_clock  x the angle (rad) a fish at school_body.z has swum round the
//                   pen, y the tail-beat phase (rad), z the event clock (s,
//                   faster while feeding: jumps, rolls, splashes), w the
//                   school's optical thickness seen from above
//   school_state  x mean depth (m), y depth spread (m, half the band),
//                 z speed (1 = cruising; low oxygen slows it), w feeding 0..1
//   school_body   x fish fork length (m), y swim direction (+1 / -1),
//                 z the school's outer radius (m), w fish shown
//
// The clocks are integrated on the CPU, so a change of speed or feeding never
// makes a fish jump. The whole school turns round the pen: a fish's angle is
// its own start plus the school's angle scaled by its own pace and radius.
// The pen frame is the model frame: y = 0 the water the pen rides, the net
// hangs inside FISH_NET_R.

instance uniform vec4 school_clock = vec4(0.0, 0.0, 0.0, 1.5);
instance uniform vec4 school_state = vec4(7.0, 4.0, 1.0, 0.0);
instance uniform vec4 school_body = vec4(0.6, 1.0, 22.0, 0.0);

// The first jumper_count fish of a school leap now and then (more often while
// feeding); the rest stay under.
uniform int jumper_count = 6;
// Read at a distance: a fish never draws shorter than min_fish_pixels on
// screen above the water (up to max_fish_inflate times its real length), the
// way the site gear keeps its minimum size. Under the water: true size.
uniform float min_fish_pixels = 9.0;
uniform float max_fish_inflate = 3.0;
// Tail beat: sweep at the fork (m of the 0.9 m mesh) and body wavelength.
uniform float swim_amplitude = 0.05;
uniform float swim_wavelength = 0.95;

const float FISH_MESH_LENGTH = 0.9;
const float FISH_TAU = 6.28318530718;
const float FISH_NET_R = 24.95;
const float FISH_G = 9.81;
// A jump (s of the event clock and m/s): up from the school in RISE_S, out
// of the water at JUMP_V (about 0.6 m high), forward at JUMP_RUN, back down
// to its place in DIVE_S.
const float FISH_RISE_S = 0.9;
const float FISH_JUMP_V = 3.4;
const float FISH_JUMP_RUN = 2.2;
const float FISH_DIVE_S = 1.4;

float fish_rand(float seed, float k) {
	return fract(sin(seed * 127.1 + k * 311.7) * 43758.5453);
}

// The ring a fish keeps across the school: off the net and off the middle,
// pulled in towards the feed spreader while the pen is fed.
float fish_ring_radius(float across, float feed, float outer) {
	float calm = mix(0.32, 0.92, across);
	float fed = mix(0.12, 0.62, across);
	return outer * mix(calm, fed, feed);
}

float fish_air_time() {
	return 2.0 * FISH_JUMP_V / FISH_G;
}

// Where a jumper is in its jump: x time since the jump began (s), y period.
vec2 fish_jump_time(float seed) {
	float period = 16.0 + 18.0 * fish_rand(seed, 3.0);
	return vec2(mod(school_clock.z + fish_rand(seed, 1.0) * period, period), period);
}

// A fish's pose in the pen frame: position, heading (unit, the nose), the
// roll about its long axis (rad) and how much its flank flashes (0..1).
void fish_pose(vec4 custom, int id, out vec3 pos, out vec3 fwd, out float roll, out float flash) {
	float seed = custom.w;
	float h1 = fish_rand(seed, 1.0);
	float h2 = fish_rand(seed, 2.0);
	float h3 = fish_rand(seed, 3.0);
	float feed = school_state.w;
	float outer = school_body.z;
	float dir = school_body.y;
	// The pace round the pen comes from the fish's calm ring, which never
	// changes, so feeding can pull the ring in without a jump in angle.
	float calm_r = max(fish_ring_radius(custom.y, 0.0, outer), 1.0);
	float pace = (0.85 + 0.3 * h1) * outer / calm_r;
	float theta = FISH_TAU * custom.x + dir * school_clock.x * pace
		+ 0.05 * sin(school_clock.z * (0.11 + 0.07 * h2) + h3 * FISH_TAU);
	float r = fish_ring_radius(custom.y, feed, outer) + 1.2 * sin(school_clock.z * (0.07 + 0.05 * h2) + h3 * FISH_TAU);
	r = clamp(r, 1.0, FISH_NET_R - 1.0);
	float depth = school_state.x + school_state.y * (2.0 * custom.z - 1.0)
		+ 0.35 * sin(school_clock.z * (0.15 + 0.2 * h3) + h1 * FISH_TAU);
	depth = max(depth, 0.35);
	vec3 tangent = dir * vec3(-sin(theta), 0.0, cos(theta));
	pos = vec3(r * cos(theta), -depth, r * sin(theta));
	fwd = tangent;
	// A roll every few seconds: the silver flank turns up and flashes.
	float roll_period = 7.0 + 11.0 * h3;
	float tr = mod(school_clock.z * (1.0 + 0.5 * h1) + h2 * roll_period, roll_period);
	float env = smoothstep(0.0, 0.2, tr) * (1.0 - smoothstep(0.3, 0.8, tr));
	roll = env * 1.1 * (h1 > 0.5 ? 1.0 : -1.0);
	flash = env;
	if (id < jumper_count) {
		vec2 jt = fish_jump_time(seed);
		float t = jt.x;
		float air = fish_air_time();
		float vh = FISH_JUMP_RUN + 0.7;
		float lift_y = pos.y;
		float run = 0.0;
		float vy = 0.0;
		if (t < FISH_RISE_S) {
			float x = t / FISH_RISE_S;
			lift_y = mix(pos.y, -0.25, smoothstep(0.0, 1.0, x));
			vy = (-0.25 - pos.y) * 6.0 * x * (1.0 - x) / FISH_RISE_S;
		} else if (t < FISH_RISE_S + air) {
			float ta = t - FISH_RISE_S;
			lift_y = -0.25 + FISH_JUMP_V * ta - 0.5 * FISH_G * ta * ta;
			vy = FISH_JUMP_V - FISH_G * ta;
			run = FISH_JUMP_RUN * ta;
			roll = 0.25 * sin(ta * 9.0);
			flash = 0.0;
		} else if (t < FISH_RISE_S + air + FISH_DIVE_S) {
			float x = (t - FISH_RISE_S - air) / FISH_DIVE_S;
			float u = smoothstep(0.0, 1.0, x);
			lift_y = mix(-0.25, pos.y, u);
			run = FISH_JUMP_RUN * air * (1.0 - u);
			vy = (pos.y + 0.25) * 6.0 * x * (1.0 - x) / FISH_DIVE_S;
		}
		pos = vec3(pos.x, lift_y, pos.z) + tangent * run;
		// The nose follows the flight path.
		fwd = normalize(tangent * vh + vec3(0.0, vy, 0.0));
	}
}

// Where a jumper's last splash is (pen frame xz) and how old it is (s);
// age < 0: none showing.
vec3 fish_jump_splash(vec4 custom, int id, vec3 pos) {
	if (id >= jumper_count) {
		return vec3(0.0, 0.0, -1.0);
	}
	vec2 jt = fish_jump_time(custom.w);
	float air = fish_air_time();
	float out_age = jt.x - FISH_RISE_S;
	float in_age = jt.x - FISH_RISE_S - air;
	if (in_age >= 0.0 && in_age < 1.2) {
		return vec3(pos.x, pos.z, in_age);
	}
	if (out_age >= 0.0 && out_age < 1.2) {
		return vec3(pos.x, pos.z, out_age);
	}
	return vec3(0.0, 0.0, -1.0);
}

// The fish's length on screen: never under min_fish_pixels from above the
// water. `world` is its centre, `above` whether the camera is over the sea.
float fish_scale(vec3 world, vec3 camera, float proj11, float viewport_h, bool above) {
	float length_m = school_body.x;
	if (!above) {
		return length_m / FISH_MESH_LENGTH;
	}
	float m_per_px = 2.0 * length(camera - world) / (abs(proj11) * viewport_h);
	float inflate = clamp(min_fish_pixels * m_per_px / max(length_m, 0.05), 1.0, max_fish_inflate);
	return length_m * inflate / FISH_MESH_LENGTH;
}

// The mesh vertex (0.9 m fish, nose along -Z) bent by the tail beat, rolled
// and scaled, in the fish's own frame.
vec3 fish_body_vertex(vec3 v, float seed, float roll, float scale) {
	float s = clamp(v.z / FISH_MESH_LENGTH + 0.5, 0.0, 1.1);
	float k = FISH_TAU / swim_wavelength;
	float w = school_clock.y * (0.9 + 0.2 * fish_rand(seed, 2.0)) + fish_rand(seed, 1.0) * FISH_TAU - k * v.z;
	float amp = swim_amplitude * clamp(school_state.z, 0.3, 1.8);
	v.x += amp * (0.12 + s * s) * sin(w);
	float c = cos(roll);
	float sn = sin(roll);
	v.xy = vec2(c * v.x - sn * v.y, sn * v.x + c * v.y);
	return v * scale;
}

// The fish's frame from its heading: -Z the nose, +Y up.
mat3 fish_basis(vec3 fwd) {
	vec3 z = -normalize(fwd);
	vec3 up = abs(z.y) > 0.98 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
	vec3 x = normalize(cross(up, z));
	vec3 y = cross(z, x);
	return mat3(x, y, z);
}
