shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
uniform float amount = 0.25;

void fragment() {
	vec2 pixel = SCREEN_PIXEL_SIZE;
	vec3 center = texture(screen_tex, SCREEN_UV).rgb;
	vec3 blur = (
		texture(screen_tex, SCREEN_UV + vec2(pixel.x, 0.0)).rgb +
		texture(screen_tex, SCREEN_UV - vec2(pixel.x, 0.0)).rgb +
		texture(screen_tex, SCREEN_UV + vec2(0.0, pixel.y)).rgb +
		texture(screen_tex, SCREEN_UV - vec2(0.0, pixel.y)).rgb
	) * 0.25;
	COLOR = vec4(clamp(center + (center - blur) * amount, 0.0, 1.0), 1.0);
}
