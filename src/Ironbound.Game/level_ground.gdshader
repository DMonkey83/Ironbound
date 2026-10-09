// A level's ground and rock in one surface: flagstones in the caves, grass and dirt outside,
// and rising out of both the rock the walls are made of, darker the higher it climbs, so the
// mass between the rooms reads as mountain and the rooms as hollows in it.
//
// The mesh is a heightfield built by LevelLook.cs. The vertex colour carries the one fact the
// height cannot: red is how much of the ground round that point is grass. Everything else —
// stone or soil, seam or slab, rock face or rock top — is worked out here from the world
// position, the height and the slope, so it is continuous across the chunks the mesh is cut into.

shader_type spatial;
render_mode cull_back;

// One under the mountain, where the stone is laid flagstone; nought out in the open, where the
// same ground is dry earth and gravel and the odd stone poking through.
uniform float paved : hint_range(0.0, 1.0) = 1.0;

varying vec3 world;
varying vec3 world_normal;

float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
               mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
    float sum = 0.0;
    float amp = 0.5;
    for (int k = 0; k < 4; k++) {
        sum += amp * noise(p);
        p = p * 2.03 + vec2(1.7, 9.2);
        amp *= 0.5;
    }
    return sum;
}

// Irregular flagstones: the nearest of a jittered lattice of seeds is the stone, and the gap
// between the nearest and the next nearest is how far from its edge this point lies.
vec3 flagstones(vec2 p) {
    vec2 cell = floor(p);
    vec2 f = fract(p);
    float first = 8.0;
    float second = 8.0;
    vec2 id = vec2(0.0);
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 o = vec2(float(x), float(y));
            vec2 seed = o + vec2(hash(cell + o), hash(cell + o + 17.0)) * 0.8 + 0.1;
            float d = length(seed - f);
            if (d < first) {
                second = first;
                first = d;
                id = cell + o;
            } else if (d < second) {
                second = d;
            }
        }
    }
    return vec3(second - first, hash(id), hash(id + 3.1));
}

void vertex() {
    world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    vec2 p = world.xz;
    float grain = fbm(p * 3.0);

    // Stone floor: slabs about half a square across, each its own shade, the seams dark.
    vec3 slab = flagstones(p * 2.1);
    float seam = smoothstep(0.015, 0.06, slab.x);
    vec3 stone = mix(vec3(0.20, 0.19, 0.18), vec3(0.33, 0.30, 0.27), slab.y);
    stone = mix(stone, stone * vec3(1.05, 0.98, 0.90), slab.z);
    stone *= 0.80 + 0.35 * grain;
    stone *= mix(0.45, 1.0, seam);

    // Open rocky ground: earth in two tones, and pebbles where a small cell's middle is.
    vec3 grit = flagstones(p * 6.0);
    float pebble = smoothstep(0.24, 0.32, grit.x) * step(0.88, grit.y);
    vec3 earth = mix(vec3(0.24, 0.20, 0.15), vec3(0.34, 0.29, 0.21), fbm(p * 0.8));
    earth *= 0.82 + 0.3 * grain;
    earth = mix(earth, vec3(0.34, 0.32, 0.29) * (0.8 + 0.4 * grit.z), pebble);
    stone = mix(earth, stone, paved);

    // Grass: two greens in broad patches, worn to dirt in places.
    float patch = fbm(p * 0.45);
    float worn = fbm(p * 1.3 + vec2(11.0, 4.0));
    vec3 grass = mix(vec3(0.09, 0.15, 0.05), vec3(0.19, 0.24, 0.08), patch);
    grass *= 0.78 + 0.4 * fbm(p * 9.0);
    grass = mix(grass, vec3(0.22, 0.17, 0.11) * (0.8 + 0.4 * grain), smoothstep(0.58, 0.72, worn));

    float green = smoothstep(0.35, 0.65, COLOR.r + (fbm(p * 1.8) - 0.5) * 0.5);
    vec3 ground = mix(stone, grass, green);

    // Rock: banded along its height and broken by noise, the faces lighter than the tops,
    // and the tops falling away toward black the higher they are.
    float h = world.y;
    float steep = 1.0 - clamp(world_normal.y, 0.0, 1.0);
    float strata = fbm(vec2(p.x + p.y, h * 6.0) * 1.5);
    vec3 rock = mix(vec3(0.17, 0.155, 0.15), vec3(0.32, 0.29, 0.26), strata);
    rock *= 0.75 + 0.45 * fbm(p * 4.0 + h);
    rock *= mix(1.0, 0.30, smoothstep(0.35, 1.0, h) * (1.0 - steep * 0.6));
    float raised = smoothstep(0.04, 0.16, h);
    vec3 colour = mix(ground, rock, raised);

    // Below the floor is a drop: black by the time it is half a square down.
    colour *= mix(1.0, 0.02, 1.0 - smoothstep(-0.55, -0.05, h));

    ALBEDO = colour;
    ROUGHNESS = mix(0.95, 0.85, raised);
    SPECULAR = 0.25;
}
