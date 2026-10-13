shader_type sky;

uniform vec3 sky_top : source_color = vec3(0.337, 0.514, 0.729);
uniform vec3 sky_horizon : source_color = vec3(0.647, 0.741, 0.835);
uniform vec3 sky_bottom : source_color = vec3(0.125, 0.18, 0.243);

uniform float time_of_day : hint_range(0.0, 1.0) = 0.5; // 0=midnight, 0.5=noon
uniform float season : hint_range(0.0, 1.0) = 0.5; // 0=winter, 0.5=summer

uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.3;
uniform float cloud_softness : hint_range(0.0, 1.0) = 0.3;

// Godot has no noise default (hint_default_noise does not exist, review
// F958): assign a NoiseTexture2D. Unassigned, the black default means no clouds.
uniform sampler2D cloud_noise : hint_default_black, repeat_enable;

void sky() {
    vec3 view_dir = EYEDIR;
    float horizon_factor = abs(view_dir.y);
    
    // Day/night cycle
    float day_intensity = sin(time_of_day * 6.28318 - 1.5708) * 0.5 + 0.5;
    day_intensity = clamp(day_intensity, 0.0, 1.0);
    
    // Color based on time of day and view angle
    vec3 sky_color;
    if (view_dir.y > 0.0) {
        // Upper sky
        sky_color = mix(sky_bottom, sky_top, view_dir.y);
        sky_color = mix(sky_color, sky_horizon, 1.0 - horizon_factor);
    } else {
        // Below horizon (ground/fog)
        sky_color = mix(sky_bottom, vec3(0.05, 0.08, 0.12), -view_dir.y);
    }
    
    // Apply day/night cycle
    vec3 night_color = vec3(0.02, 0.03, 0.06);
    sky_color = mix(night_color, sky_color, day_intensity);
    
    // Add clouds
    vec2 cloud_uv = view_dir.xz / (view_dir.y + 1.0) + TIME * 0.01;
    float cloud_val = texture(cloud_noise, cloud_uv).r;
    cloud_val = smoothstep(1.0 - cloud_coverage - cloud_softness, 1.0 - cloud_coverage, cloud_val);
    
    vec3 cloud_color = mix(vec3(1.0), vec3(0.7, 0.75, 0.8), cloud_coverage);
    sky_color = mix(sky_color, cloud_color, cloud_val * 0.5);
    
    COLOR = sky_color;
}
