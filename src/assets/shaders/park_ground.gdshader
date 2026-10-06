shader_type spatial;
render_mode diffuse_burley, specular_disabled;

uniform sampler2D albedo_tex : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D normal_tex : repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D detail_normal_tex : repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D rough_tex : repeat_enable, filter_linear_mipmap_anisotropic;
uniform bool has_detail_normal = false;
uniform bool has_rough = false;
uniform float detail_scale = 4.0;
uniform float normal_depth = 1.0;
uniform float sharpen = 0.8;
uniform float lod_bias = -0.6;

vec3 sample_albedo(vec2 uv) {
	return texture(albedo_tex, uv, lod_bias).rgb;
}

void fragment() {
	vec2 texel = 1.0 / vec2(textureSize(albedo_tex, 0));
	vec3 center = sample_albedo(UV);
	vec3 blur = (
		sample_albedo(UV + vec2(texel.x, 0.0)) +
		sample_albedo(UV - vec2(texel.x, 0.0)) +
		sample_albedo(UV + vec2(0.0, texel.y)) +
		sample_albedo(UV - vec2(0.0, texel.y))
	) * 0.25;
	ALBEDO = max(center + (center - blur) * sharpen, vec3(0.0));

	vec3 normal_ts = texture(normal_tex, UV).rgb * 2.0 - 1.0;
	if (has_detail_normal) {
		vec3 detail = texture(detail_normal_tex, UV * detail_scale).rgb * 2.0 - 1.0;
		normal_ts = vec3(normal_ts.xy + detail.xy, normal_ts.z);
	}
	NORMAL_MAP = normalize(normal_ts) * 0.5 + 0.5;
	NORMAL_MAP_DEPTH = normal_depth;
	ROUGHNESS = has_rough ? texture(rough_tex, UV).r : 0.9;
	METALLIC = 0.0;
}
