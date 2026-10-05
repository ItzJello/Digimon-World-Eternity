shader_type spatial;
render_mode cull_disabled, unshaded;

uniform sampler2D iris_tex : source_color, filter_linear;

void vertex() {
	// A couple of millimetres clears the socket. The old 1.8 cm push
	// sat the card in front of the face.
	VERTEX.z += 0.002;
}

void fragment() {
	vec4 tex = texture(iris_tex, UV);
	float hi = max(tex.r, max(tex.g, tex.b));
	float lo = min(tex.r, min(tex.g, tex.b));
	// The atlas pads the iris with a flat light gray. That plate was
	// what made different eyes look like the same pale sticker.
	if (hi - lo < 0.08 && hi > 0.55) {
		discard;
	}
	ALBEDO = tex.rgb;
}
