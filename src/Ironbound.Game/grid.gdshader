// The five-foot grid, drawn on the ground rather than built out of meshes.
//
// One Godot unit is one square, and squares sit on integer world coordinates, so the line is
// simply "near a whole number". A shader rather than geometry for two reasons: it costs one
// material regardless of how large the battlefield is, and it cannot z-fight with the hover
// cursor at y=0.02 or the turn ring at y=0.04, because it is not a separate surface at all.
//
// World position rather than UV: the ground plane is centred on the middle of the board, so its
// UV origin is the centre and not the corner, and the lines would sit half a square out.
//
// The same plane serves two jobs. With no terrain laid it is the floor, opaque, its base colour
// showing between the lines. With terrain under it, floor_alpha goes to zero and everything but
// the lines themselves becomes glass, so the grid is painted over the ground rather than
// replacing it.

shader_type spatial;

uniform vec3 base_colour : source_color = vec3(0.20, 0.22, 0.20);
uniform vec3 line_colour : source_color = vec3(0.34, 0.38, 0.34);
uniform vec3 edge_colour : source_color = vec3(0.46, 0.50, 0.46);
uniform float line_width = 0.03;

// Faded out entirely once the fighting stops. The grid exists to answer questions about reach
// and range, and between chapters there are none to ask.
uniform float strength : hint_range(0.0, 1.0) = 1.0;

// One where this plane is the floor; zero where something else is, and only the lines show.
uniform float floor_alpha : hint_range(0.0, 1.0) = 1.0;

varying vec3 ground_position;

void vertex() {
    ground_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    vec2 cell = fract(ground_position.xz);
    vec2 edge = min(cell, vec2(1.0) - cell);

    // Every square, and then every fifth one a little brighter — twenty-five feet is the unit
    // a person actually counts in when judging whether something is in range.
    float fine = 1.0 - smoothstep(0.0, line_width, min(edge.x, edge.y));

    vec2 coarse_cell = fract(ground_position.xz / 5.0);
    vec2 coarse_edge = min(coarse_cell, vec2(1.0) - coarse_cell) * 5.0;
    float coarse = 1.0 - smoothstep(0.0, line_width, min(coarse_edge.x, coarse_edge.y));

    float drawn = max(fine, coarse) * strength;

    ALBEDO = mix(mix(base_colour, line_colour, fine * strength), edge_colour, coarse * strength);
    ALPHA = max(floor_alpha, drawn);
    ROUGHNESS = 0.95;
    SPECULAR = 0.1;
}
