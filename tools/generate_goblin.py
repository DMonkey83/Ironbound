import bpy
import math
from mathutils import Vector

OUTPUT_BLEND = "ironbound_goblin.blend"
OUTPUT_GLB = "ironbound_goblin.glb"

def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (
        bpy.data.meshes,
        bpy.data.curves,
        bpy.data.materials,
        bpy.data.armatures,
        bpy.data.cameras,
        bpy.data.lights,
    ):
        pass

def mat(name, color, metallic=0.0, roughness=0.7):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    return m

SKIN = mat("Goblin_Skin", (0.24, 0.38, 0.12), 0.0, 0.85)
SKIN_DARK = mat("Goblin_Skin_Dark", (0.12, 0.20, 0.06), 0.0, 0.9)
EYE = mat("Goblin_Eye", (0.55, 0.42, 0.08), 0.0, 0.35)
PUPIL = mat("Goblin_Pupil", (0.01, 0.006, 0.002), 0.0, 0.25)
TOOTH = mat("Goblin_Teeth", (0.72, 0.62, 0.38), 0.0, 0.65)
LEATHER = mat("Leather", (0.12, 0.055, 0.025), 0.0, 0.9)
LEATHER_LIGHT = mat("Leather_Light", (0.28, 0.13, 0.055), 0.0, 0.85)
CLOTH = mat("Cloth", (0.16, 0.12, 0.09), 0.0, 1.0)
METAL = mat("Worn_Metal", (0.16, 0.17, 0.15), 0.75, 0.55)
WOOD = mat("Shield_Wood", (0.25, 0.10, 0.035), 0.0, 0.9)
BONE = mat("Bone", (0.55, 0.48, 0.34), 0.0, 0.8)

def smooth(obj):
    if obj.type == "MESH":
        for p in obj.data.polygons:
            p.use_smooth = True

def apply_mat(obj, material):
    obj.data.materials.append(material)

def uv(name, loc, scale, material, segments=20, rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, location=loc
    )
    o = bpy.context.object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    smooth(o)
    apply_mat(o, material)
    return o

def cube(name, loc, scale, material, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(location=loc)
    o = bpy.context.object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = o.modifiers.new("EdgeBevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
    apply_mat(o, material)
    return o

def cyl(name, loc, radius, depth, material, rotation=(0, 0, 0), vertices=16):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=loc, rotation=rotation
    )
    o = bpy.context.object
    o.name = name
    smooth(o)
    apply_mat(o, material)
    return o

def cone(name, loc, r1, r2, depth, material, rotation=(0, 0, 0), vertices=16):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=r1, radius2=r2, depth=depth,
        location=loc, rotation=rotation
    )
    o = bpy.context.object
    o.name = name
    smooth(o)
    apply_mat(o, material)
    return o

def orient_between(obj, a, b):
    direction = Vector(b) - Vector(a)
    obj.location = (Vector(a) + Vector(b)) / 2
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = direction.to_track_quat("Z", "Y")
    obj.dimensions.z = direction.length
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

def limb(name, a, b, radius, material):
    o = cyl(name, (0, 0, 0), radius, 1.0, material)
    orient_between(o, a, b)
    return o

def create_body():
    parts = []

    parts.append(uv("Body", (0, 0, 1.02), (0.29, 0.19, 0.48), SKIN))
    parts.append(uv("Chest", (0, -0.015, 1.34), (0.34, 0.22, 0.32), SKIN))
    parts.append(uv("Pelvis", (0, 0, 0.72), (0.27, 0.18, 0.23), SKIN))

    parts.append(uv("Head", (0, -0.005, 1.88), (0.31, 0.26, 0.31), SKIN))
    parts.append(uv("Jaw", (0, -0.055, 1.77), (0.25, 0.21, 0.18), SKIN_DARK))
    parts.append(cone("Nose", (0, -0.275, 1.84), 0.085, 0.045, 0.18, SKIN_DARK, (math.pi / 2, 0, 0)))

    for side in (-1, 1):
        ear = cone(
            f"Ear_{'L' if side < 0 else 'R'}",
            (side * 0.30, 0.0, 1.92),
            0.16, 0.025, 0.48, SKIN,
            (0, side * math.radians(78), math.radians(8 * side))
        )
        parts.append(ear)

        eye = uv(f"Eye_{'L' if side < 0 else 'R'}",
                 (side * 0.115, -0.245, 1.91),
                 (0.075, 0.035, 0.055), EYE, 16, 8)
        parts.append(eye)
        pupil = uv(f"Pupil_{'L' if side < 0 else 'R'}",
                   (side * 0.115, -0.277, 1.91),
                   (0.025, 0.012, 0.035), PUPIL, 12, 6)
        parts.append(pupil)

        tusk = cone(f"Tusk_{'L' if side < 0 else 'R'}",
                    (side * 0.085, -0.255, 1.72),
                    0.035, 0.006, 0.15, TOOTH,
                    (math.radians(18), 0, side * math.radians(12)))
        parts.append(tusk)

    for side in (-1, 1):
        parts.append(limb(
            f"UpperArm_{'L' if side < 0 else 'R'}",
            (side * 0.28, 0, 1.39),
            (side * 0.43, -0.01, 1.10),
            0.10, SKIN
        ))
        parts.append(limb(
            f"Forearm_{'L' if side < 0 else 'R'}",
            (side * 0.43, -0.01, 1.10),
            (side * 0.48, -0.04, 0.82),
            0.085, SKIN
        ))
        parts.append(uv(
            f"Hand_{'L' if side < 0 else 'R'}",
            (side * RIGHT_HAND[0], RIGHT_HAND[1], RIGHT_HAND[2]),
            (0.11, 0.08, 0.12), SKIN
        ))

        parts.append(limb(
            f"Thigh_{'L' if side < 0 else 'R'}",
            (side * 0.15, 0, 0.72),
            (side * 0.18, -0.01, 0.38),
            0.12, SKIN
        ))
        parts.append(limb(
            f"Shin_{'L' if side < 0 else 'R'}",
            (side * 0.18, -0.01, 0.38),
            (side * 0.19, -0.08, 0.10),
            0.095, SKIN
        ))
        parts.append(uv(
            f"Foot_{'L' if side < 0 else 'R'}",
            (side * 0.19, -0.13, 0.07),
            (0.14, 0.27, 0.07), SKIN
        ))

    return parts

# Where the right hand sits, so the hand and the thing it holds are written down once.
RIGHT_HAND = (0.48, -0.05, 0.78)


def create_equipment():
    parts = []

    parts.append(cube("Torso_Leather", (0, -0.205, 1.22), (0.30, 0.035, 0.28), LEATHER, 0.025))

    for side in (-1, 1):
        parts.append(cube(
            f"ShoulderArmor_{'L' if side < 0 else 'R'}",
            (side * 0.30, -0.01, 1.38),
            (0.14, 0.13, 0.07), METAL, 0.035
        ))
        parts.append(cyl(
            f"Bracer_{'L' if side < 0 else 'R'}",
            (side * 0.45, -0.03, 0.98),
            0.10, 0.17, LEATHER,
            (0, math.radians(90), 0)
        ))
        parts.append(cyl(
            f"Boot_{'L' if side < 0 else 'R'}",
            (side * 0.19, -0.10, 0.20),
            0.105, 0.22, LEATHER,
            (math.radians(8), 0, 0)
        ))

    for z in (0.57, 0.49, 0.41):
        parts.append(cube("Cloth_Loincloth", (0, 0.0, z), (0.22, 0.16, 0.07), CLOTH, 0.02))

    # One belt. The loop that used to be here ignored `side` in the position, so it built two
    # of them in exactly the same place and they z-fought.
    parts.append(cyl("Belt", (0, 0, 0.70), 0.30, 0.08, LEATHER, (math.pi / 2, 0, 0)))

    # Sword, in the right hand. It used to stand in the air in front of the hip, passing
    # through the loincloth, with the grip a foot below the fingers — `side` had leaked out of
    # the loop above and was multiplying by zero.
    grip = (RIGHT_HAND[0], RIGHT_HAND[1], RIGHT_HAND[2])
    parts.append(cyl("Sword_Grip", grip, 0.035, 0.20, LEATHER))
    parts.append(cyl(
        "Sword_Guard", (grip[0], grip[1], grip[2] + 0.12),
        0.075, 0.025, METAL, (0, math.pi / 2, 0)
    ))
    blade = cube(
        "Sword_Blade", (grip[0], grip[1], grip[2] + 0.52), (0.035, 0.012, 0.40), METAL, 0.01
    )
    parts.append(blade)

    # Shield, strapped to the left forearm rather than floating a hand's width off it.
    parts.append(cyl("Shield", (-0.44, -0.17, 1.00), 0.28, 0.07, WOOD, (math.pi / 2, 0, 0), 20))
    parts.append(cyl(
        "Shield_Boss", (-0.44, -0.23, 1.00), 0.10, 0.06, METAL, (math.pi / 2, 0, 0), 16
    ))

    return parts

def create_armature():
    bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
    arm = bpy.context.object
    arm.name = "Goblin_Armature"
    arm.data.name = "Goblin_ArmatureData"
    eb = arm.data.edit_bones
    eb.remove(eb[0])

    def bone(name, head, tail, parent=None):
        b = eb.new(name)
        b.head = head
        b.tail = tail
        if parent:
            b.parent = eb.get(parent)
        return b

    bone("root", (0, 0, 0), (0, 0, 0.15))
    bone("pelvis", (0, 0, 0.62), (0, 0, 0.80), "root")
    bone("spine", (0, 0, 0.80), (0, 0, 1.15), "pelvis")
    bone("chest", (0, 0, 1.15), (0, 0, 1.48), "spine")
    bone("neck", (0, 0, 1.48), (0, 0, 1.67), "chest")
    bone("head", (0, 0, 1.67), (0, 0, 2.00), "neck")

    for side, s in (("L", -1), ("R", 1)):
        bone(f"upper_arm_{side}", (s * 0.25, 0, 1.40), (s * 0.43, 0, 1.10), "chest")
        bone(f"forearm_{side}", (s * 0.43, 0, 1.10), (s * 0.48, 0, 0.82), f"upper_arm_{side}")
        bone(f"hand_{side}", (s * 0.48, 0, 0.82), (s * 0.48, 0, 0.70), f"forearm_{side}")
        bone(f"thigh_{side}", (s * 0.14, 0, 0.70), (s * 0.18, 0, 0.38), "pelvis")
        bone(f"shin_{side}", (s * 0.18, 0, 0.38), (s * 0.19, 0, 0.10), f"thigh_{side}")
        bone(f"foot_{side}", (s * 0.19, 0, 0.10), (s * 0.19, -0.20, 0.05), f"shin_{side}")

    bpy.ops.object.mode_set(mode="POSE")
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm

def rig_body(parts, arm):
    deformable = [o for o in parts if o.type == "MESH" and not o.name.startswith(("Eye_", "Pupil_", "Tusk_"))]
    bpy.ops.object.select_all(action="DESELECT")
    for o in deformable:
        o.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    except RuntimeError:
        for o in deformable:
            o.parent = arm

def add_export_collection():
    coll = bpy.data.collections.new("IRONBOUND_GOBLIN")
    bpy.context.scene.collection.children.link(coll)
    for o in list(bpy.context.scene.objects):
        if o.users_collection and coll not in o.users_collection:
            for c in list(o.users_collection):
                c.objects.unlink(o)
            coll.objects.link(o)

def setup_scene():
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    # Renamed between Blender versions, and assigning a name that is not in the enum throws
    # rather than warning — which killed this script outright on 5.2.
    engines = scene.render.bl_rna.properties["engine"].enum_items.keys()
    for wanted in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        if wanted in engines:
            scene.render.engine = wanted
            break
    scene["ironbound_asset"] = True
    scene["godot_target"] = "Godot 4.x"
    scene["export_format"] = "glTF 2.0 / GLB"
    scene["character_role"] = "Modular Goblin Base"

def export_glb(path):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_animations=True,
        export_skins=True,
        export_morph=False,
        export_materials="EXPORT",
    )

def main():
    clear()
    setup_scene()
    body = create_body()
    equipment = create_equipment()
    arm = create_armature()
    rig_body(body, arm)

    # Equipment stays separate so Godot can later replace armor/weapons.
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)

    blend_path = bpy.path.abspath("//" + OUTPUT_BLEND)
    glb_path = bpy.path.abspath("//" + OUTPUT_GLB)

    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    export_glb(glb_path)
    print("IRONBOUND GOBLIN CREATED")
    print(blend_path)
    print(glb_path)

if __name__ == "__main__":
    main()
