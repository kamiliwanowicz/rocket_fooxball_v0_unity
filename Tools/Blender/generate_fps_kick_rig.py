"""Generate the camera-space kick leg matching the world cyborg's right leg."""

import math
import os
from pathlib import Path

import bpy
import bmesh
from mathutils import Matrix, Vector


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
BLEND_PATH = REPOSITORY_ROOT / "Tools" / "Blender" / "FpsKickRig.blend"
FBX_PATH = REPOSITORY_ROOT / "Assets" / "_Game" / "Models" / "FpsKickRig.fbx"
PREVIEW_DIR = REPOSITORY_ROOT / "Temp" / "BlenderPreviews" / "FpsKickRig"

FPS = 30
KICK_START = 1
KICK_CHAMBER = 3
KICK_CONTACT = 4
KICK_RECOVERY = 7
KICK_END = 11
KICK_SECONDS = (KICK_END - KICK_START) / FPS
CONTACT_SECONDS = (KICK_CONTACT - KICK_START) / FPS
MOUNT_UNITY = Vector((0.12, -0.42, 0.30))
MIN_OVERLAP = 0.005
ACTION_NAMES = {"Idle", "Kick"}
MATERIAL_NAMES = ("KickRed", "KickBlack", "KickCream")
MATERIAL_SPECS = (
    ("KickRed", (0.035, 0.047, 0.018), 0.66, 0.52),
    ("KickBlack", (0.018, 0.024, 0.023), 0.92, 0.24),
    ("KickCream", (0.110, 0.045, 0.016), 0.68, 0.45),
)
EXPECTED_PREVIEWS = {
    "front.png", "rear.png", "left.png", "right.png", "top.png", "three-quarter.png",
    "contact-kick.png", "unity-camera-kick.png",
}

# Declared before geometry. Parts meet through closed-solid overlap along named axis.
CONNECTION_MAP = (
    ("Knee", "ShinMain", "Z", MIN_OVERLAP),
    ("ShinMain", "ShinSleeve0", "Z", MIN_OVERLAP),
    ("ShinPiston", "ShinSleeve1", "Z", MIN_OVERLAP),
    ("ShinMain", "AnkleClevisLeft", "Z", MIN_OVERLAP),
    ("ShinPiston", "AnkleClevisRight", "Z", MIN_OVERLAP),
    ("FootLug", "AnkleYoke", "Z", MIN_OVERLAP),
    ("AnkleYoke", "BootShell", "Z", MIN_OVERLAP),
    ("BootShell", "BootToeBlock", "Y", MIN_OVERLAP),
    ("BootShell", "SoleUpper", "Z", MIN_OVERLAP),
    ("SoleUpper", "SoleLower", "Z", MIN_OVERLAP),
    ("BootShell", "Heel", "Y", MIN_OVERLAP),
    ("BootShell", "BootSidePlateLeft", "X", MIN_OVERLAP),
    ("BootShell", "BootSidePlateRight", "X", MIN_OVERLAP),
)


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def make_material(name, color, metallic, roughness):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    shader = material.node_tree.nodes["Principled BSDF"]
    shader.inputs["Base Color"].default_value = (*color, 1.0)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return material


def apply_transform(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    obj.select_set(False)


def add_beveled_box(
    name, location, dimensions, material, bone_name, bevel=0.018, segments=2,
    rotation=(0.0, 0.0, 0.0),
):
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    obj.scale = Vector(dimensions) * 0.5
    obj.rotation_euler = rotation
    apply_transform(obj)
    modifier = obj.modifiers.new("EdgeChamfer", "BEVEL")
    modifier.width = bevel
    modifier.segments = segments
    modifier.limit_method = "ANGLE"
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)
    obj.data.materials.append(material)
    group = obj.vertex_groups.new(name=bone_name)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    return obj


def add_cylinder(name, start, end, radius, vertices, material, bone_name, bevel=0.0):
    start = Vector(start)
    end = Vector(end)
    direction = end - start
    require(direction.length > 1e-5, f"Cylinder span invalid: {name}")
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=direction.length,
        end_fill_type="NGON",
        location=(start + end) * 0.5,
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(direction.normalized())
    apply_transform(obj)
    if bevel:
        modifier = obj.modifiers.new("EdgeChamfer", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.select_set(False)
    obj.data.materials.append(material)
    group = obj.vertex_groups.new(name=bone_name)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    return obj


def add_torus(name, location, major_radius, minor_radius, material, bone_name):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=16,
        minor_segments=5,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    apply_transform(obj)
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    group = obj.vertex_groups.new(name=bone_name)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    return obj


def add_sphere(name, location, radius, material, bone_name):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=10,
        ring_count=6,
        radius=radius,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    apply_transform(obj)
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    group = obj.vertex_groups.new(name=bone_name)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    return obj


def normalize_material_slots(mesh, materials):
    old_names = [slot.name for slot in mesh.data.materials]
    polygon_names = [old_names[polygon.material_index] for polygon in mesh.data.polygons]
    mesh.data.materials.clear()
    for material in materials:
        mesh.data.materials.append(material)
    index_by_name = {material.name: index for index, material in enumerate(materials)}
    for polygon, material_name in zip(mesh.data.polygons, polygon_names):
        polygon.material_index = index_by_name[material_name]


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        Vector((min(v.x for v in corners), min(v.y for v in corners), min(v.z for v in corners))),
        Vector((max(v.x for v in corners), max(v.y for v in corners), max(v.z for v in corners))),
    )


def audit_connections(parts):
    axis_index = {"X": 0, "Y": 1, "Z": 2}
    for first_name, second_name, axis_name, required in CONNECTION_MAP:
        first_min, first_max = world_bounds(parts[first_name])
        second_min, second_max = world_bounds(parts[second_name])
        overlaps = tuple(
            min(first_max[index], second_max[index]) - max(first_min[index], second_min[index])
            for index in range(3)
        )
        axis = axis_index[axis_name]
        require(
            min(overlaps) >= required - 1e-6,
            f"Connection {first_name}->{second_name} overlaps={overlaps} < {required:.6f}m",
        )
        print(
            f"CONNECTION {first_name}->{second_name} axis={axis_name} "
            f"overlap={overlaps[axis]:.6f}m xyz={tuple(round(value, 6) for value in overlaps)}"
        )


def create_geometry(materials):
    olive, gunmetal, tan = materials
    parts = {}

    # Exact world-character right shin/foot inventory mapped around its
    # (x=-0.155, z=0.500) origin into the fixed first-person envelope.
    parts["Knee"] = add_beveled_box(
        "Knee", (0.0, 0.0, -0.025), (0.068, 0.068, 0.096),
        gunmetal, "Shin.R", 0.012, 2,
    )
    parts["KneeFrontArmor"] = add_beveled_box(
        "KneeFrontArmor", (0.0, -0.050, -0.028), (0.082, 0.032, 0.076),
        tan, "Shin.R", 0.008, 2,
    )
    parts["KneeAxle"] = add_cylinder(
        "KneeAxle", (-0.090, 0.0, 0.015), (0.090, 0.0, 0.015),
        0.015, 12, gunmetal, "Shin.R",
    )
    for side_name, start, end in (
        ("Left", (-0.100, 0.0, 0.015), (-0.084, 0.0, 0.015)),
        ("Right", (0.084, 0.0, 0.015), (0.100, 0.0, 0.015)),
    ):
        parts[f"KneeAxleCap{side_name}"] = add_cylinder(
            f"KneeAxleCap{side_name}", start, end,
            0.023, 12, tan, "Shin.R",
        )

    parts["ShinMain"] = add_cylinder(
        "ShinMain", (-0.030, -0.024, 0.000), (-0.030, -0.024, -0.355),
        0.019, 14, gunmetal, "Shin.R",
    )
    parts["ShinPiston"] = add_cylinder(
        "ShinPiston", (0.028, 0.030, -0.010), (0.028, 0.030, -0.355),
        0.019, 14, gunmetal, "Shin.R",
    )
    for index, (x, y) in enumerate((
        (-0.030, -0.024),
        (0.028, 0.030),
    )):
        parts[f"ShinSleeve{index}"] = add_cylinder(
            f"ShinSleeve{index}", (x, y, -0.255), (x, y, -0.160),
            0.027, 14, tan, "Shin.R",
        )
        parts[f"ShinCap{index}"] = add_torus(
            f"ShinCap{index}", (x, y, -0.260),
            0.025, 0.006, gunmetal, "Shin.R",
        )
    parts["Shin"] = add_cylinder(
        "Shin", (-0.030, -0.024, 0.000), (-0.030, -0.024, -0.355),
        0.020, 14, gunmetal, "Shin.R",
    )
    parts["ShinBrace"] = add_cylinder(
        "ShinBrace", (-0.044, -0.004, -0.095), (0.043, -0.004, -0.215),
        0.011, 10, olive, "Shin.R",
    )
    parts["ShinPlate"] = add_beveled_box(
        "ShinPlate", (0.0, -0.060, -0.170), (0.116, 0.032, 0.104),
        olive, "Shin.R", 0.010, 2,
    )

    # Exact world squared clevis: lateral plates capture a narrow foot lug.
    parts["AnkleClevisLeft"] = add_beveled_box(
        "AnkleClevisLeft", (-0.047, 0.0, -0.345), (0.022, 0.084, 0.110),
        olive, "Shin.R", 0.008, 2,
    )
    parts["AnkleClevisRight"] = add_beveled_box(
        "AnkleClevisRight", (0.047, 0.0, -0.345), (0.022, 0.084, 0.110),
        olive, "Shin.R", 0.008, 2,
    )
    parts["FootLug"] = add_beveled_box(
        "FootLug", (0.0, -0.002, -0.355), (0.062, 0.062, 0.088),
        gunmetal, "Foot.R", 0.009, 2,
    )
    parts["AnkleAxle"] = add_cylinder(
        "AnkleAxle", (-0.068, -0.002, -0.345), (0.068, -0.002, -0.345),
        0.012, 12, gunmetal, "Foot.R",
    )
    for side_name, start, end in (
        ("Left", (-0.063, -0.002, -0.345), (-0.076, -0.002, -0.345)),
        ("Right", (0.063, -0.002, -0.345), (0.076, -0.002, -0.345)),
    ):
        parts[f"AnkleAxleCap{side_name}"] = add_cylinder(
            f"AnkleAxleCap{side_name}", start, end, 0.019, 12, tan, "Foot.R", 0.002,
        )
    left_gap = world_bounds(parts["FootLug"])[0].x - world_bounds(parts["AnkleClevisLeft"])[1].x
    right_gap = world_bounds(parts["AnkleClevisRight"])[0].x - world_bounds(parts["FootLug"])[1].x
    require(0.005 - 1e-6 <= left_gap <= 0.015, f"Ankle left lug clearance invalid: {left_gap:.6f}m")
    require(0.005 - 1e-6 <= right_gap <= 0.015, f"Ankle right lug clearance invalid: {right_gap:.6f}m")
    print(f"AUDIT ankle_mechanics axle=+X left_clearance={left_gap:.6f}m right_clearance={right_gap:.6f}m clevis=captured")

    parts["AnkleYoke"] = add_beveled_box(
        "AnkleYoke", (0.0, -0.005, -0.375), (0.156, 0.110, 0.104),
        olive, "Foot.R", 0.012, 2,
    )
    parts["BootShell"] = add_beveled_box(
        "BootShell", (0.0, -0.045, -0.422), (0.216, 0.300, 0.150),
        gunmetal, "Foot.R", 0.018, 3,
    )
    parts["BootUpperShell"] = add_beveled_box(
        "BootUpperShell", (0.0, -0.070, -0.395), (0.192, 0.208, 0.094),
        olive, "Foot.R", 0.014, 3, (math.radians(-10.0), 0.0, 0.0),
    )
    parts["BootToeBlock"] = add_beveled_box(
        "BootToeBlock", (0.0, -0.181, -0.438), (0.208, 0.076, 0.104),
        olive, "Foot.R", 0.012, 2,
    )
    parts["SoleUpper"] = add_beveled_box(
        "SoleUpper", (0.0, -0.045, -0.474), (0.228, 0.308, 0.036),
        tan, "Foot.R", 0.006, 2,
    )
    parts["SoleLower"] = add_beveled_box(
        "SoleLower", (0.0, -0.045, -0.491), (0.236, 0.316, 0.018),
        gunmetal, "Foot.R", 0.004, 2,
    )
    parts["Heel"] = add_beveled_box(
        "Heel", (0.0, 0.112, -0.432), (0.184, 0.090, 0.110),
        gunmetal, "Foot.R", 0.012, 2,
    )
    for side_name, x in (("Left", -0.108), ("Right", 0.108)):
        parts[f"BootSidePlate{side_name}"] = add_beveled_box(
            f"BootSidePlate{side_name}", (x, -0.070, -0.425), (0.018, 0.160, 0.070),
            tan, "Foot.R", 0.005, 1,
        )
        direction = -1.0 if x < 0.0 else 1.0
        parts[f"BootFastener{side_name}"] = add_sphere(
            f"BootFastener{side_name}",
            (x + direction * 0.006, -0.095, -0.422),
            0.010, gunmetal, "Foot.R",
        )

    armor_parts = {
        "ShinBrace", "ShinPlate", "AnkleClevisLeft", "AnkleClevisRight",
        "AnkleYoke", "BootUpperShell", "BootToeBlock",
    }
    body_parts = {
        "Knee", "KneeAxle", "ShinMain", "ShinPiston", "ShinCap0", "ShinCap1", "Shin",
        "FootLug", "AnkleAxle", "BootShell", "SoleLower", "Heel",
        "BootFastenerLeft", "BootFastenerRight",
    }
    head_parts = {
        "KneeFrontArmor", "KneeAxleCapLeft", "KneeAxleCapRight", "ShinSleeve0", "ShinSleeve1",
        "AnkleAxleCapLeft", "AnkleAxleCapRight", "SoleUpper", "BootSidePlateLeft", "BootSidePlateRight",
    }
    expected_materials = {
        **{name: olive.name for name in armor_parts},
        **{name: gunmetal.name for name in body_parts},
        **{name: tan.name for name in head_parts},
    }
    require(set(parts) == set(expected_materials), f"World lower-leg inventory mismatch: {sorted(set(parts) ^ set(expected_materials))}")
    for part_name, expected_material in expected_materials.items():
        actual_materials = tuple(slot.name for slot in parts[part_name].data.materials)
        require(actual_materials == (expected_material,), f"World material mismatch: {part_name}={actual_materials}")
    require(len(parts) == 31, f"World lower-leg part count invalid: {len(parts)}")
    print("AUDIT world_lower_leg_inventory=31 shin=16 foot=15 materials=exact")
    audit_connections(parts)

    bpy.ops.object.select_all(action="DESELECT")
    for part in parts.values():
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts["BootShell"]
    bpy.ops.object.join()
    mesh = bpy.context.object
    mesh.name = "FpsKickMesh"
    mesh.data.name = "FpsKickMeshData"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # The first-person-only envelope sits forward/up relative to its deform
    # bones so the complete contact silhouette stays inside the fixed camera.
    # This changes no dimensions, transforms, bone hierarchy, or animation keys.
    mesh.data.transform(Matrix.Translation((0.0, -0.120, 0.150)))
    normalize_material_slots(mesh, materials)

    # Stable single UV layer for Unity materials.
    while mesh.data.uv_layers:
        mesh.data.uv_layers.remove(mesh.data.uv_layers[0])
    mesh.data.uv_layers.new(name="UVMap")
    bpy.context.view_layer.objects.active = mesh
    mesh.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")
    mesh.select_set(False)
    return mesh


def create_armature(mesh):
    armature_data = bpy.data.armatures.new("FpsKickRigData")
    armature = bpy.data.objects.new("FpsKickRig", armature_data)
    bpy.context.collection.objects.link(armature)
    armature.show_in_front = True
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    root = armature.data.edit_bones.new("Root")
    root.head = (0.0, 0.0, 0.0)
    root.tail = (0.0, 0.0, 0.16)
    shin = armature.data.edit_bones.new("Shin.R")
    shin.head = (0.0, 0.0, 0.0)
    shin.tail = (0.0, 0.0, -0.47)
    shin.parent = root
    foot = armature.data.edit_bones.new("Foot.R")
    foot.head = (0.0, 0.0, -0.47)
    foot.tail = (0.0, -0.34, -0.55)
    foot.parent = shin
    foot.use_connect = True
    bpy.ops.object.mode_set(mode="OBJECT")
    armature.select_set(False)

    modifier = mesh.modifiers.new("FpsKickArmature", "ARMATURE")
    modifier.object = armature
    mesh.parent = armature
    return armature


def key_pose(armature, frame, shin_location, shin_x_degrees, foot_x_degrees):
    bpy.context.scene.frame_set(frame)
    shin = armature.pose.bones["Shin.R"]
    foot = armature.pose.bones["Foot.R"]
    for bone in (shin, foot):
        bone.rotation_mode = "XYZ"
    shin.location = shin_location
    shin.rotation_euler = (math.radians(shin_x_degrees), 0.0, 0.0)
    foot.location = (0.0, 0.0, 0.0)
    foot.rotation_euler = (math.radians(foot_x_degrees), 0.0, 0.0)
    for bone in (shin, foot):
        bone.keyframe_insert("location", frame=frame, group=bone.name)
        bone.keyframe_insert("rotation_euler", frame=frame, group=bone.name)


def create_actions(armature):
    animation_data = armature.animation_data_create()
    idle = bpy.data.actions.new("Idle")
    idle.use_fake_user = True
    idle.frame_range = (1.0, 31.0)
    animation_data.action = idle
    key_pose(armature, 1, (0.0, 0.0, 0.0), 0.0, 0.0)
    key_pose(armature, 31, (0.0, 0.0, 0.0), 0.0, 0.0)

    kick = bpy.data.actions.new("Kick")
    kick.use_fake_user = True
    kick.frame_range = (KICK_START, KICK_END)
    animation_data.action = kick
    key_pose(armature, KICK_START, (0.0, 0.0, 0.0), 0.0, 0.0)
    key_pose(armature, KICK_CHAMBER, (0.0, -0.04, -0.02), -25.0, 8.0)
    key_pose(armature, KICK_CONTACT, (-0.11, -0.15, -0.05), -68.0, 20.0)
    key_pose(armature, KICK_RECOVERY, (-0.05, -0.08, -0.02), -34.0, 8.0)
    key_pose(armature, KICK_END, (0.0, 0.0, 0.0), 0.0, 0.0)
    for action in (idle, kick):
        for fcurve in action.fcurves:
            for point in fcurve.keyframe_points:
                point.interpolation = "BEZIER"
                point.handle_left_type = "AUTO_CLAMPED"
                point.handle_right_type = "AUTO_CLAMPED"
    animation_data.action = idle
    bpy.context.scene.frame_set(1)
    return idle, kick


def evaluated_world_bounds(obj):
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    corners = [evaluated.matrix_world @ Vector(corner) for corner in evaluated.bound_box]
    return (
        Vector((min(v.x for v in corners), min(v.y for v in corners), min(v.z for v in corners))),
        Vector((max(v.x for v in corners), max(v.y for v in corners), max(v.z for v in corners))),
    )


def camera_contract_sample(mesh, armature, action, frame):
    armature.animation_data.action = action
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    lower, upper = evaluated_world_bounds(mesh)
    # Blender x/z map to Unity x/y. Blender -y maps to Unity +z.
    center = (lower + upper) * 0.5
    unity_center = Vector((MOUNT_UNITY.x + center.x, MOUNT_UNITY.y + center.z, MOUNT_UNITY.z - center.y))
    unity_lower = Vector((MOUNT_UNITY.x + lower.x, MOUNT_UNITY.y + lower.z, MOUNT_UNITY.z - upper.y))
    unity_upper = Vector((MOUNT_UNITY.x + upper.x, MOUNT_UNITY.y + upper.z, MOUNT_UNITY.z - lower.y))
    return lower, upper, unity_center, unity_lower, unity_upper


def action_bound_bones(action):
    prefix = 'pose.bones["'
    return {
        curve.data_path.split(prefix, 1)[1].split('"]', 1)[0]
        for curve in action.fcurves
        if curve.data_path.startswith(prefix)
    }


def keyed_frames(action):
    return {
        round(point.co.x)
        for curve in action.fcurves
        for point in curve.keyframe_points
    }


def assert_pose(armature, action, frame, shin_location, shin_x_degrees, foot_x_degrees):
    armature.animation_data.action = action
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    shin = armature.pose.bones["Shin.R"]
    foot = armature.pose.bones["Foot.R"]
    require((shin.location - Vector(shin_location)).length <= 1e-6, f"Shin location mismatch at frame {frame}")
    require(abs(shin.rotation_euler.x - math.radians(shin_x_degrees)) <= 1e-6, f"Shin X mismatch at frame {frame}")
    require(foot.location.length <= 1e-6, f"Foot translation mismatch at frame {frame}")
    require(abs(foot.rotation_euler.x - math.radians(foot_x_degrees)) <= 1e-6, f"Foot X mismatch at frame {frame}")
    require(max(abs(shin.rotation_euler.y), abs(shin.rotation_euler.z), abs(foot.rotation_euler.y), abs(foot.rotation_euler.z)) <= 1e-6, f"Off-axis rotation at frame {frame}")


def audit(mesh, armature, idle, kick):
    require([obj.name for obj in bpy.context.scene.objects if obj.type == "MESH"] == ["FpsKickMesh"], "Expected one mesh object")
    require([obj.name for obj in bpy.context.scene.objects if obj.type == "ARMATURE"] == ["FpsKickRig"], "Expected one armature")
    require(mesh.data.name == "FpsKickMeshData", "Unstable mesh data name")
    require(armature.data.name == "FpsKickRigData", "Unstable armature data name")
    require([bone.name for bone in armature.data.bones] == ["Root", "Shin.R", "Foot.R"], "Unexpected bones")
    require(armature.data.bones["Shin.R"].parent == armature.data.bones["Root"], "Shin parent invalid")
    require(armature.data.bones["Foot.R"].parent == armature.data.bones["Shin.R"], "Foot parent invalid")
    require(armature.data.bones["Foot.R"].use_connect, "Foot must be connected to Shin.R")
    require({action.name for action in bpy.data.actions} == ACTION_NAMES, "Unexpected exported action")
    require(tuple(round(value) for value in idle.frame_range) == (1, 31), "Idle range invalid")
    require(tuple(round(value) for value in kick.frame_range) == (1, 11), "Kick range invalid")
    require(keyed_frames(idle) == {1, 31}, f"Idle keys invalid: {sorted(keyed_frames(idle))}")
    require(keyed_frames(kick) == {1, 3, 4, 7, 11}, f"Kick keys invalid: {sorted(keyed_frames(kick))}")
    require(action_bound_bones(kick) == {"Shin.R", "Foot.R"}, f"Kick bindings invalid: {sorted(action_bound_bones(kick))}")
    require(not any('pose.bones["Root"]' in curve.data_path for curve in kick.fcurves), "Kick contains Root binding")
    require(len(mesh.data.uv_layers) == 1 and mesh.data.uv_layers[0].name == "UVMap", "UVMap contract failed")
    require(tuple(slot.name for slot in mesh.data.materials) == MATERIAL_NAMES, "Material slots unstable")
    for material, (name, color, metallic, roughness) in zip(mesh.data.materials, MATERIAL_SPECS):
        shader = material.node_tree.nodes["Principled BSDF"]
        require(material.name == name, f"Material name mismatch: {material.name}")
        require(max(abs(shader.inputs["Base Color"].default_value[index] - color[index]) for index in range(3)) <= 1e-6, f"Material color mismatch: {name}")
        require(abs(shader.inputs["Metallic"].default_value - metallic) <= 1e-6, f"Material metallic mismatch: {name}")
        require(abs(shader.inputs["Roughness"].default_value - roughness) <= 1e-6, f"Material roughness mismatch: {name}")
    require(len(mesh.modifiers) == 1 and mesh.modifiers[0].type == "ARMATURE" and mesh.modifiers[0].object == armature, "Skin modifier invalid")
    for obj in (mesh, armature):
        require(obj.location.length < 1e-6, f"{obj.name} location not zero")
        require(all(abs(value) < 1e-6 for value in obj.rotation_euler), f"{obj.name} rotation not zero")
        require(all(abs(value - 1.0) < 1e-6 for value in obj.scale), f"{obj.name} scale not unit")
    require(abs(KICK_SECONDS - (10.0 / 30.0)) <= 1e-9, "Kick duration is not 0.333 seconds")
    require(abs(CONTACT_SECONDS - 0.10) <= 1e-9, "Gameplay contact is not 0.10 seconds")

    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    require(all(math.isfinite(component) for vertex in bm.verts for component in vertex.co), "Non-finite vertex")
    require(all(edge.calc_length() > 1e-7 for edge in bm.edges), "Zero-length edge")
    require(all(face.calc_area() > 1e-9 for face in bm.faces), "Zero-area face")
    require(not [edge for edge in bm.edges if not edge.is_manifold], "Non-manifold mesh edge")
    require(not [vertex for vertex in bm.verts if not vertex.link_faces], "Loose mesh vertex")
    bm.normal_update()
    require(all(face.normal.length > 0.999 for face in bm.faces), "Invalid face normal")
    require(bm.calc_volume(signed=True) > 0.0, "Inverted or inconsistent winding")
    bm.free()

    group_names = {group.index: group.name for group in mesh.vertex_groups}
    require(set(group_names.values()) == {"Shin.R", "Foot.R"}, f"Weight groups invalid: {set(group_names.values())}")
    for vertex in mesh.data.vertices:
        total = sum(group.weight for group in vertex.groups)
        require(vertex.groups and abs(total - 1.0) < 1e-5, f"Vertex {vertex.index} weight sum {total}")
        require(len(vertex.groups) == 1, f"Vertex {vertex.index} must have one rigid influence")
        require(group_names[vertex.groups[0].group] in {"Shin.R", "Foot.R"}, "Unknown deform group")

    assert_pose(armature, kick, 1, (0.0, 0.0, 0.0), 0.0, 0.0)
    assert_pose(armature, kick, 3, (0.0, -0.04, -0.02), -25.0, 8.0)
    assert_pose(armature, kick, 4, (-0.11, -0.15, -0.05), -68.0, 20.0)
    assert_pose(armature, kick, 7, (-0.05, -0.08, -0.02), -34.0, 8.0)
    assert_pose(armature, kick, 11, (0.0, 0.0, 0.0), 0.0, 0.0)
    root = armature.pose.bones["Root"]
    for frame in range(KICK_START, KICK_END + 1):
        bpy.context.scene.frame_set(frame)
        require(root.location.length <= 1e-7 and max(abs(value) for value in root.rotation_euler) <= 1e-7, f"Root motion at frame {frame}")

    idle_bounds = camera_contract_sample(mesh, armature, idle, 1)
    contact_bounds = camera_contract_sample(mesh, armature, kick, KICK_CONTACT)
    idle_center = idle_bounds[2]
    contact_center = contact_bounds[2]
    idle_dimensions_blender = idle_bounds[1] - idle_bounds[0]
    idle_dimensions_unity = Vector((idle_dimensions_blender.x, idle_dimensions_blender.z, idle_dimensions_blender.y))
    maximum_dimensions = Vector((0.285, 0.619, 0.638))
    require(all(actual <= maximum + 1e-6 for actual, maximum in zip(idle_dimensions_unity, maximum_dimensions)), f"Idle bounds exceed contract: {tuple(idle_dimensions_unity)}")
    half_vertical_tangent = math.tan(math.radians(75.0 * 0.5))
    idle_screen_y = idle_center.y / (idle_center.z * half_vertical_tangent)
    contact_screen_y = contact_center.y / (contact_center.z * half_vertical_tangent)
    contact_screen_x = contact_center.x / (contact_center.z * half_vertical_tangent * (16.0 / 9.0))
    evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    unity_vertices = [
        Vector((MOUNT_UNITY.x + point.x, MOUNT_UNITY.y + point.z, MOUNT_UNITY.z - point.y))
        for point in (evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices)
    ]
    contact_projection_x = [
        point.x / (point.z * half_vertical_tangent * (16.0 / 9.0))
        for point in unity_vertices
    ]
    contact_projection_y = [
        point.y / (point.z * half_vertical_tangent)
        for point in unity_vertices
    ]
    require(idle_screen_y < -1.10, f"Idle center not below view: normalized y={idle_screen_y:.3f}")
    require(abs(contact_screen_x) <= 0.25 and -0.85 <= contact_screen_y <= -0.25, f"Contact misses lower-center: center={tuple(round(v, 3) for v in contact_center)} normalized=({contact_screen_x:.3f},{contact_screen_y:.3f})")
    require(contact_bounds[3].z > 0.03, f"Contact crosses near plane: minimum Unity z={contact_bounds[3].z:.3f}")
    require(min(contact_projection_x) >= -0.98 and max(contact_projection_x) <= 0.98, f"Contact silhouette clips horizontally: {min(contact_projection_x):.3f}..{max(contact_projection_x):.3f}")
    require(min(contact_projection_y) >= -0.98 and max(contact_projection_y) <= 0.98, f"Contact silhouette clips vertically: {min(contact_projection_y):.3f}..{max(contact_projection_y):.3f}")
    forbidden = ("rocket", "thruster", "nozzle", "exhaust", "jet", "particle", "emission")
    exported_names = [mesh.name, mesh.data.name, armature.name, armature.data.name, *MATERIAL_NAMES, *ACTION_NAMES]
    require(not any(token in name.lower() for token in forbidden for name in exported_names), "Forbidden propulsion name detected")

    armature.animation_data.action = idle
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    mesh.data.calc_loop_triangles()
    lower, upper = evaluated_world_bounds(mesh)
    print(f"AUDIT mesh={mesh.name} vertices={len(mesh.data.vertices)} triangles={len(mesh.data.loop_triangles)} counts=report-only")
    print(f"AUDIT bounds_min={tuple(round(v, 6) for v in lower)} bounds_max={tuple(round(v, 6) for v in upper)} unity_dimensions={tuple(round(v, 6) for v in idle_dimensions_unity)}")
    print(f"AUDIT actions=Idle[1,31] Kick[1,11] keys=1,3,4,7,11 fps={FPS} seconds={KICK_SECONDS:.6f} contact_seconds={CONTACT_SECONDS:.6f}")
    print("AUDIT hierarchy=Root>Shin.R>Foot.R bindings=Shin.R,Foot.R root=locked transforms=zero/unit")
    print(f"AUDIT camera_idle_center={tuple(round(v, 6) for v in idle_center)} normalized_y={idle_screen_y:.6f}")
    print(f"AUDIT camera_strike_center={tuple(round(v, 6) for v in contact_center)} normalized=({contact_screen_x:.6f},{contact_screen_y:.6f}) min_unity_z={contact_bounds[3].z:.6f}")
    print(f"AUDIT camera_strike_silhouette x={min(contact_projection_x):.6f}..{max(contact_projection_x):.6f} y={min(contact_projection_y):.6f}..{max(contact_projection_y):.6f} unclipped=true")


def point_camera(camera, target):
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()


def render_previews(mesh, armature, idle):
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    for stale in PREVIEW_DIR.glob("*.png"):
        stale.unlink()
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (0.018, 0.022, 0.024, 1.0)
    background.inputs["Strength"].default_value = 0.38
    scene.view_settings.look = "AgX - Medium High Contrast"

    idle_lower, _ = evaluated_world_bounds(mesh)
    bpy.ops.mesh.primitive_plane_add(size=8.0, location=(0.0, 0.0, idle_lower.z - 0.016))
    floor = bpy.context.object
    floor.name = "PreviewFloor"
    floor_material = make_material("PreviewFloorMaterial", (0.070, 0.078, 0.074), 0.12, 0.62)
    floor.data.materials.append(floor_material)

    bpy.ops.object.light_add(type="AREA", location=(2.2, -2.8, 2.8))
    key = bpy.context.object
    key.name = "PreviewKey"
    key.data.energy = 900
    key.data.shape = "DISK"
    key.data.size = 3.0
    point_camera(key, (0.0, -0.08, -0.28))
    bpy.ops.object.light_add(type="AREA", location=(-2.0, -0.8, 1.0))
    fill = bpy.context.object
    fill.name = "PreviewFill"
    fill.data.energy = 620
    fill.data.size = 2.5
    point_camera(fill, (0.0, -0.08, -0.28))
    bpy.ops.object.light_add(type="AREA", location=(0.4, 2.0, 1.6))
    rim = bpy.context.object
    rim.name = "PreviewRim"
    rim.data.energy = 760
    rim.data.size = 2.2
    point_camera(rim, (0.0, -0.08, -0.28))

    camera_data = bpy.data.cameras.new("PreviewCameraData")
    camera = bpy.data.objects.new("PreviewCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    camera.data.lens = 62
    target = Vector((0.0, -0.035, -0.255))
    views = {
        "front": (0.0, -1.55, -0.20),
        "rear": (0.0, 1.45, -0.20),
        "left": (-1.45, -0.02, -0.20),
        "right": (1.45, -0.02, -0.20),
        "top": (0.0, -0.02, 1.25),
        "three-quarter": (1.10, -1.20, 0.45),
    }
    armature.animation_data.action = idle
    scene.frame_set(1)
    for name, location in views.items():
        camera.location = location
        point_camera(camera, target)
        output = PREVIEW_DIR / f"{name}.png"
        scene.render.filepath = str(output)
        bpy.ops.render.render(write_still=True)
        require(output.exists() and output.stat().st_size > 0, f"Missing preview {output}")
        print(f"PREVIEW {name}={output} bytes={output.stat().st_size}")

    kick = bpy.data.actions["Kick"]
    armature.animation_data.action = kick
    scene.frame_set(KICK_CONTACT)
    bpy.context.view_layer.update()
    contact_lower, contact_upper = evaluated_world_bounds(mesh)
    contact_target = (contact_lower + contact_upper) * 0.5
    camera.location = contact_target + Vector((1.20, 0.55, 0.42))
    camera.data.lens = 58
    point_camera(camera, contact_target)
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    output = PREVIEW_DIR / "contact-kick.png"
    scene.render.filepath = str(output)
    bpy.ops.render.render(write_still=True)
    require(output.exists() and output.stat().st_size > 0, f"Missing preview {output}")
    print(f"PREVIEW contact-kick={output} bytes={output.stat().st_size}")

    # Exact camera equivalent of the Unity mount: Blender X/Z/-Y map to Unity X/Y/Z.
    camera.location = (-MOUNT_UNITY.x, MOUNT_UNITY.z, -MOUNT_UNITY.y)
    camera.rotation_euler = Vector((0.0, -1.0, 0.0)).to_track_quat("-Z", "Y").to_euler()
    camera.data.angle_y = math.radians(75.0)
    camera.data.clip_start = 0.03
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    output = PREVIEW_DIR / "unity-camera-kick.png"
    scene.render.filepath = str(output)
    bpy.ops.render.render(write_still=True)
    require(output.exists() and output.stat().st_size > 0, f"Missing preview {output}")
    print(f"PREVIEW unity-camera-kick={output} bytes={output.stat().st_size}")

    armature.animation_data.action = idle
    scene.frame_set(1)
    bpy.context.view_layer.update()
    actual = {path.name for path in PREVIEW_DIR.glob("*.png")}
    require(actual == EXPECTED_PREVIEWS, f"Preview inventory invalid: expected={sorted(EXPECTED_PREVIEWS)} actual={sorted(actual)}")
    for obj in (camera, key, fill, rim, floor):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.materials.remove(floor_material)
    print(f"AUDIT previews={len(actual)} orbit=6 contact=1 unity_camera=1")


def save_and_export(mesh, armature, idle):
    BLEND_PATH.parent.mkdir(parents=True, exist_ok=True)
    FBX_PATH.parent.mkdir(parents=True, exist_ok=True)
    armature.animation_data.action = idle
    bpy.context.scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), check_existing=False)

    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    # Blender's all-actions path normally force-keeps one constant key per bone.
    # Keep the official exporter but disable those empty bindings so the factory
    # round-trip retains only the two authored Kick tracks.
    from io_scene_fbx import export_fbx_bin
    original_animation_export = export_fbx_bin.fbx_animations_do

    def export_without_empty_bindings(scene_data, ref_id, f_start, f_end, start_zero, objects=None, force_keep=False):
        return original_animation_export(scene_data, ref_id, f_start, f_end, start_zero, objects, force_keep=False)

    export_fbx_bin.fbx_animations_do = export_without_empty_bindings
    try:
        bpy.ops.export_scene.fbx(
            filepath=str(FBX_PATH),
            use_selection=True,
            object_types={"MESH", "ARMATURE"},
            axis_forward="-Z",
            axis_up="Y",
            apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_UNITS",
            use_mesh_modifiers=True,
            mesh_smooth_type="FACE",
            add_leaf_bones=False,
            use_armature_deform_only=True,
            bake_anim=True,
            bake_anim_use_all_bones=False,
            bake_anim_use_nla_strips=False,
            bake_anim_use_all_actions=True,
            bake_anim_force_startend_keying=True,
            bake_anim_step=1.0,
            bake_anim_simplify_factor=0.001,
        )
    finally:
        export_fbx_bin.fbx_animations_do = original_animation_export
    require(BLEND_PATH.exists() and BLEND_PATH.stat().st_size > 0, "Blend source not saved")
    require(FBX_PATH.exists() and FBX_PATH.stat().st_size > 0, "FBX not exported")
    print(f"OUTPUT blend={BLEND_PATH} bytes={BLEND_PATH.stat().st_size}")
    print(f"OUTPUT fbx={FBX_PATH} bytes={FBX_PATH.stat().st_size}")


def roundtrip_fbx_audit():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(FBX_PATH), use_anim=True)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    rigs = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    require(len(meshes) == 1 and meshes[0].name == "FpsKickMesh", f"Round-trip mesh invalid: {[obj.name for obj in meshes]}")
    require(len(rigs) == 1 and rigs[0].name == "FpsKickRig", f"Round-trip rig invalid: {[obj.name for obj in rigs]}")
    rig = rigs[0]
    require([bone.name for bone in rig.data.bones] == ["Root", "Shin.R", "Foot.R"], "Round-trip bones invalid")
    require(rig.data.bones["Shin.R"].parent == rig.data.bones["Root"], "Round-trip Shin parent invalid")
    require(rig.data.bones["Foot.R"].parent == rig.data.bones["Shin.R"], "Round-trip Foot parent invalid")
    kick_actions = [action for action in bpy.data.actions if action.name == "Kick" or action.name.endswith("|Kick")]
    require(len(kick_actions) == 1, f"Round-trip Kick missing/ambiguous: {[action.name for action in bpy.data.actions]}")
    kick = kick_actions[0]
    bound_bones = action_bound_bones(kick)
    require(bound_bones == {"Shin.R", "Foot.R"}, f"Round-trip Kick bindings invalid: {sorted(bound_bones)}")
    require(not any('pose.bones["Root"]' in curve.data_path for curve in kick.fcurves), "Round-trip Kick gained Root binding")
    require(tuple(round(value) for value in kick.frame_range) == (1, 11), f"Round-trip Kick range invalid: {tuple(kick.frame_range)}")
    require(tuple(slot.name for slot in meshes[0].data.materials) == MATERIAL_NAMES, "Round-trip material slots invalid")
    for obj in (meshes[0], rig):
        require(obj.location.length <= 1e-6, f"Round-trip {obj.name} location not zero")
        require(all(abs(value) <= 1e-6 for value in obj.rotation_euler), f"Round-trip {obj.name} rotation not zero")
        require(all(abs(value - 1.0) <= 1e-6 for value in obj.scale), f"Round-trip {obj.name} scale not unit")
    print(f"ROUNDTRIP factory_empty=true mesh=FpsKickMesh rig=FpsKickRig Kick={kick.name} bindings={','.join(sorted(bound_bones))}")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = FPS
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    materials = tuple(make_material(*spec) for spec in MATERIAL_SPECS)
    mesh = create_geometry(materials)
    armature = create_armature(mesh)
    idle, kick = create_actions(armature)
    mesh.data.calc_loop_triangles()
    audit(mesh, armature, idle, kick)
    render_previews(mesh, armature, idle)
    save_and_export(mesh, armature, idle)
    roundtrip_fbx_audit()
    print("FPS_KICK_RIG_GENERATION_SUCCEEDED")


if __name__ == "__main__":
    main()
