shader_type spatial;

uniform sampler2D albedo_texture : source_color, filter_nearest_mipmap_anisotropic;
uniform sampler2D lightmap_texture : hint_default_white, filter_linear_mipmap_anisotropic;

void fragment() {
    vec4 albedo = texture(albedo_texture, UV);
    vec4 light = texture(lightmap_texture, UV2);
    vec4 result = albedo * light * COLOR;
    ALBEDO = result.rgb;
}