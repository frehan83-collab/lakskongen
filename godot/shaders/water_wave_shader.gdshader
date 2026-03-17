shader_type canvas_item;

uniform float scroll_speed = 0.1;
uniform float wave_amplitude = 0.01;
uniform float wave_frequency = 5.0;
uniform vec4 water_color_top : source_color = vec4(0.10, 0.30, 0.55, 0.6); // Slightly lighter blue for top
uniform vec4 water_color_bottom : source_color = vec4(0.04, 0.10, 0.22, 0.8); // Deeper blue for bottom

void fragment() {
    vec2 uv = SCREEN_UV;
    float time = TIME * scroll_speed;

    // Simple sine wave displacement
    float offset = sin((uv.x + time) * wave_frequency) * wave_amplitude;
    uv.y += offset;

    // Gradient based on vertical position
    vec4 final_color = mix(water_color_top, water_color_bottom, uv.y);

    COLOR = final_color;
}
