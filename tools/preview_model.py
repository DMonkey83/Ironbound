"""Import a .glb into an empty scene and render it, to see what actually came out.

Run:  blender -b --python preview.py -- <in.glb> <out.png>
"""
import sys, math, bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
GLB, OUT = argv[0], argv[1]
POSE = argv[2] if len(argv) > 2 else None

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

bpy.ops.import_scene.gltf(filepath=GLB)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
print("PREVIEW: imported", [(o.name, len(o.data.vertices)) for o in meshes])

# Posed, so the render shows what the animations actually do rather than the rest pose.
if POSE:
    rig = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    match = next((a for a in bpy.data.actions if POSE in a.name), None)
    if rig and match:
        if not rig.animation_data:
            rig.animation_data_create()
        rig.animation_data.action = match
        bpy.context.scene.frame_set(int(match.frame_range[0]) + 5)
        print("PREVIEW: posed on", match.name)
    else:
        print("PREVIEW: !! no action matching", POSE)

dg = bpy.context.evaluated_depsgraph_get()
lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
for o in meshes:
    for v in o.evaluated_get(dg).data.vertices:
        p = o.matrix_world @ v.co
        for i in range(3):
            lo[i] = min(lo[i], p[i]); hi[i] = max(hi[i], p[i])
print(f"PREVIEW: glTF bounds x[{lo.x:.3f},{hi.x:.3f}] y[{lo.y:.3f},{hi.y:.3f}] z[{lo.z:.3f},{hi.z:.3f}]")

mid = (lo + hi) / 2
span = max(hi - lo)

# A checker floor, so a figure that sank or floats is obvious.
bpy.ops.mesh.primitive_plane_add(size=span * 6, location=(mid.x, mid.y, lo.z))

# Three-quarter and front, the two views that show silhouette and face.
for name, angle in (("34", math.radians(35)), ("front", 0.0)):
    cam_data = bpy.data.cameras.new(name)
    cam = bpy.data.objects.new(name, cam_data)
    bpy.context.scene.collection.objects.link(cam)
    back = span * 2.4
    cam.location = (mid.x + math.sin(angle) * back, mid.y - math.cos(angle) * back, mid.z + span * 0.35)
    direction = mid - Vector(cam.location)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    cam_data.lens = 70

sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", type="SUN"))
sun.data.energy = 4.0
sun.location = (span, -span, span * 2)
sun.rotation_euler = (math.radians(50), 0, math.radians(35))
bpy.context.scene.collection.objects.link(sun)

scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 24
scene.render.resolution_x = 520
scene.render.resolution_y = 780
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new("W")
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.6

for name in ("34", "front"):
    scene.camera = bpy.data.objects[name]
    scene.render.filepath = OUT.replace(".png", f"_{name}.png")
    bpy.ops.render.render(write_still=True)
    print("PREVIEW: wrote", scene.render.filepath)
