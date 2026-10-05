shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D iris_tex : source_color;
uniform bool clip_alpha = false;

void fragment() {
	vec4 tex = texture(iris_tex, UV);
	if (clip_alpha && tex.a < 0.25) {
		discard;
	}
	ALBEDO = tex.rgb;
	ROUGHNESS = 0.28;
	METALLIC = 0.0;
	EMISSION = tex.rgb * 0.08;
}
