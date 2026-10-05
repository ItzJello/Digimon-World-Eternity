shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_disabled;

uniform sampler2D albedo_tex : source_color, repeat_enable;
uniform vec4 albedo_color : source_color = vec4(0.62, 0.62, 0.6, 1.0);
uniform bool has_albedo = false;

void fragment() {
	vec3 base = albedo_color.rgb;
	if (has_albedo) {
		base = texture(albedo_tex, UV).rgb;
	}
	ALBEDO = base;
	ROUGHNESS = 0.86;
	METALLIC = 0.0;
	// Imported city normals often point the wrong way, which kills the sun.
	vec3 geometric = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	NORMAL = FRONT_FACING ? geometric : -geometric;
}
