shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, ambient_light_disabled;

uniform sampler2D card_tex : source_color, filter_linear, repeat_disable;
uniform float fade = 1.0;

void fragment() {
	vec4 tex = texture(card_tex, UV);
	ALBEDO = tex.rgb * fade;
	ALPHA = 1.0;
}
