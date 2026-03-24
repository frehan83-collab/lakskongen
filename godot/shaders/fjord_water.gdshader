shader_type spatial;
render_mode depth_draw_always;

uniform vec3 water_color : source_color = vec3(0.024, 0.376, 0.522);
uniform vec3 water_color_deep : source_color = vec3(0.0, 0.2, 0.35);
uniform float wave_speed : hint_range(0.0, 2.0) = 0.5;
uniform float wave_height : hint_range(0.0, 1.0) = 0.3;
uniform float wave_scale : hint_range(1.0, 100.0) = 20.0;
uniform float roughness : hint_range(0.0, 1.0) = 0.02;

uniform sampler2D wave_noise : hint_default_noise;

varying vec3 world_position;
varying float wave_height_val;

float fresnel(float amount, vec3 normal, vec3 view) {
    return pow(1.0 - clamp(dot(normalize(normal), normalize(view)), 0.0, 1.0), amount);
}

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    
    // Sample noise for wave displacement
    vec2 noise_uv = world_position.xz / wave_scale + TIME * wave_speed;
    float height = texture(wave_noise, noise_uv).r * 0.5 + 0.5;
    
    noise_uv = world_position.xz / (wave_scale * 1.5) - TIME * wave_speed * 0.7;
    height += texture(wave_noise, noise_uv + vec2(0.5)).r * 0.5;
    
    wave_height_val = height * wave_height;
    VERTEX.y += wave_height_val;
    
    // Recalculate normal based on wave displacement
    vec3 tangent = normalize(vec3(1.0, 0.0, 0.0));
    vec3 bitangent = normalize(vec3(0.0, 0.0, 1.0));
    NORMAL = normalize(cross(tangent, bitangent));
}

void fragment() {
    // Calculate depth factor for color blending
    float depth_blend = fresnel(2.0, NORMAL, VIEW);
    
    // Mix colors based on depth and fresnel
    vec3 color = mix(water_color, water_color_deep, depth_blend * 0.6);
    
    // Add wave crest highlights
    float crest = pow(wave_height_val / wave_height, 3.0);
    color = mix(color, vec3(1.0), crest * 0.15);
    
    ALBEDO = color;
    METALLIC = 0.0;
    ROUGHNESS = roughness;
    
    // Specular highlight for water surface
    SPECULAR = 0.5 + crest * 0.3;
}
