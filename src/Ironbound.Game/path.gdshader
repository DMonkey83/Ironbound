// The way somebody is about to walk, drawn as a glowing pipe along the ground from their feet to
// the cursor. The vertex colour says what each stretch is — within this move, beyond it, the
// walk between fights — and light runs along it toward the far end, so which end is which reads
// at a glance even where the path doubles back on itself.
//
// UV.x is the distance along the path in squares, laid down by PathPreview.cs.

shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;

uniform float speed = 1.6;

void fragment() {
    float run = fract(UV.x * 0.9 - TIME * speed);
    float pulse = smoothstep(0.0, 0.18, run) * (1.0 - smoothstep(0.18, 0.55, run));

    // Brighter along the top of the pipe than its flanks, so it reads as round from above.
    float crown = 1.0 - abs(UV.y - 0.5) * 1.4;

    ALBEDO = COLOR.rgb * (0.70 + 0.35 * crown + 0.55 * pulse);
    ALPHA = COLOR.a;
}
