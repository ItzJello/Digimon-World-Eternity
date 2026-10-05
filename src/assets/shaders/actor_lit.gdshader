shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D albedo_tex : source_color, hint_default_white;
uniform vec4 albedo_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform bool has_albedo = false;

void fragment() {
	vec4 tex = has_albedo ? texture(albedo_tex, UV) : albedo_color;
	if (tex.a < 0.04) {
		discard;
	}
	ALBEDO = tex.rgb;
	ROUGHNESS = 0.62;
	METALLIC = 0.0;
}
