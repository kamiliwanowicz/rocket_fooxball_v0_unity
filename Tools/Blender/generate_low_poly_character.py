"""Generate the industrial cyborg used by Rocket Fooxball's world presentation."""

import math
import os

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BLEND_PATH = os.path.join(REPOSITORY_ROOT, "Tools", "Blender", "LowPolyCharacter.blend")
FBX_PATH = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models", "LowPolyCharacter.fbx")
PREVIEW_DIR = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "LowPolyCharacter")
MESH_NAMES = ("CharacterArmor", "CharacterBody", "CharacterHead", "CharacterEye")
BONE_NAMES = (
    "Root", "Pelvis", "Spine", "Chest", "Neck", "Head",
    "Thigh.R", "Shin.R", "Foot.R", "Thigh.L", "Shin.L", "Foot.L",
    "UpperArm.R", "Forearm.R", "Hand.R", "UpperArm.L", "Forearm.L", "Hand.L",
)
LEG_BONES = ("Thigh.R", "Shin.R", "Foot.R", "Thigh.L", "Shin.L", "Foot.L")
ACTION_NAMES = ("Idle", "Run", "Jump", "Fall", "Land", "Kick")
ACTION_RANGES = {
    "Idle": (1, 30), "Run": (1, 20), "Jump": (1, 12),
    "Fall": (1, 15), "Land": (1, 10), "Kick": (1, 11),
}
ACTION_KEYS = {
    "Idle": (1, 8, 16, 23, 30), "Run": (1, 6, 11, 16, 20),
    "Jump": (1, 4, 7, 10, 12), "Fall": (1, 5, 9, 13, 15),
    "Land": (1, 3, 5, 8, 10), "Kick": (1, 3, 4, 7, 11),
}
LOOP_ACTIONS = {"Idle", "Run"}
CONTACT_SHEET_ACTIONS = ("Run", "Jump", "Fall", "Land", "Kick")
TARGET_BOUNDS = Vector((0.75, 0.45, 1.75))
MIN_OVERLAP = 0.005
FORBIDDEN_TERMS = ("jet", "thruster", "flame", "fire", "plume", "propulsion", "nozzle")
KNEE_MECHANICS = {}

# Authored before geometry. Each closed-solid pair names its contact axis and overlap.
CONNECTION_MAP = (
    ("PelvisCore", "TorsoCore", "Z", MIN_OVERLAP),
    ("TorsoCore", "NeckCore", "Z", MIN_OVERLAP),
    ("NeckCore", "HeadPod", "Z", MIN_OVERLAP),
    ("PelvisCore", "Thigh.R", "Z", MIN_OVERLAP),
    ("Thigh.R", "Knee.R", "Z", MIN_OVERLAP),
    ("Knee.R", "Shin.R", "Z", MIN_OVERLAP),
    ("Shin.R", "Boot.R", "Z", MIN_OVERLAP),
    ("PelvisCore", "Thigh.L", "Z", MIN_OVERLAP),
    ("Thigh.L", "Knee.L", "Z", MIN_OVERLAP),
    ("Knee.L", "Shin.L", "Z", MIN_OVERLAP),
    ("Shin.L", "Boot.L", "Z", MIN_OVERLAP),
    ("TorsoCore", "UpperArm.R", "X", MIN_OVERLAP),
    ("UpperArm.R", "Elbow.R", "Z", MIN_OVERLAP),
    ("Elbow.R", "Forearm.R", "Z", MIN_OVERLAP),
    ("Forearm.R", "Fist.R", "Z", MIN_OVERLAP),
    ("TorsoCore", "UpperArm.L", "X", MIN_OVERLAP),
    ("UpperArm.L", "Elbow.L", "Z", MIN_OVERLAP),
    ("Elbow.L", "Forearm.L", "Z", MIN_OVERLAP),
    ("Forearm.L", "Fist.L", "Z", MIN_OVERLAP),
    ("TorsoCore", "ChestPlate", "Y", MIN_OVERLAP),
    ("TorsoCore", "BackHousing", "Y", MIN_OVERLAP),
    ("BackHousing", "RearTank", "Y", MIN_OVERLAP),
    ("RearTank", "ExhaustLower", "X", MIN_OVERLAP),
    ("ExhaustLower", "ExhaustElbow", "Z", MIN_OVERLAP),
    ("ExhaustElbow", "ExhaustUpper", "X", MIN_OVERLAP),
    ("UpperArm.R", "Cowl.R", "X", MIN_OVERLAP),
    ("Cowl.R", "CowlSpikeRear", "X", MIN_OVERLAP),
    ("Cowl.R", "CowlSpikeFront", "X", MIN_OVERLAP),
    ("UpperArm.L", "Cowl.L", "X", MIN_OVERLAP),
    ("HeadPod", "Brow", "Y", MIN_OVERLAP),
    ("RearTank", "AntennaBase", "Z", MIN_OVERLAP),
    ("AntennaBase", "AntennaStem", "Z", MIN_OVERLAP),
    ("AntennaStem", "AntennaKnuckle", "Z", MIN_OVERLAP),
    ("AntennaKnuckle", "AntennaUpperStem", "Z", MIN_OVERLAP),
    ("AntennaUpperStem", "AntennaBall", "Z", MIN_OVERLAP),
    ("HeadPod", "Lens.R", "Y", MIN_OVERLAP),
    ("HeadPod", "Lens.L", "Y", MIN_OVERLAP),
)


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def make_material(name, color, metallic, roughness):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Base Color"].default_value = (*color, 1.0)
    return material


def world_bounds(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points))),
        Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points))),
    )


def finish_part(obj, name, collection, bone_name, material, bounds, smooth=False):
    obj.name = name
    obj.data.name = name + "Mesh"
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    obj.data.materials.clear()
    obj.data.materials.append(material)
    group = obj.vertex_groups.new(name=bone_name)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    for polygon in obj.data.polygons:
        polygon.use_smooth = smooth
        require(
            polygon.area > 1e-10 and all(math.isfinite(value) for value in polygon.normal),
            f"Primitive face invalid: {name}/{polygon.index}/{polygon.area:.12f}",
        )
    require(len(obj.data.uv_layers) == 1, f"Primitive UV contract failed: {name}")
    obj.data.uv_layers[0].name = "UVMap"
    bounds[name] = world_bounds(obj)
    collection.append(obj)
    return obj


def add_box(name, collection, bone_name, location, half_extents, material, bounds, bevel=0.012,
            rotation=(0.0, 0.0, 0.0), bevel_segments=1):
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=location)
    obj = bpy.context.object
    obj.scale = half_extents
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        modifier = obj.modifiers.new("EdgeChamfer", "BEVEL")
        modifier.width = min(bevel, min(half_extents) * 0.45)
        modifier.segments = bevel_segments
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.rotation_euler = rotation
    return finish_part(obj, name, collection, bone_name, material, bounds)


def add_sphere(name, collection, bone_name, location, radii, material, bounds, segments=12, rings=8, smooth=False):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=location)
    obj = bpy.context.object
    obj.scale = radii
    return finish_part(obj, name, collection, bone_name, material, bounds, smooth=smooth)


def add_cylinder(name, collection, bone_name, start, end, radius, material, bounds, vertices=12):
    start = Vector(start)
    end = Vector(end)
    direction = end - start
    require(direction.length > 1e-5, f"Cylinder span invalid: {name}")
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=direction.length, location=(start + end) * 0.5)
    obj = bpy.context.object
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(direction.normalized())
    return finish_part(obj, name, collection, bone_name, material, bounds)


def add_cone(name, collection, bone_name, base, tip, radius, material, bounds, vertices=8):
    base = Vector(base)
    tip = Vector(tip)
    direction = tip - base
    require(direction.length > 1e-5, f"Cone span invalid: {name}")
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius, radius2=0.006, depth=direction.length, location=(base + tip) * 0.5)
    obj = bpy.context.object
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(direction.normalized())
    return finish_part(obj, name, collection, bone_name, material, bounds)


def add_torus(name, collection, bone_name, location, axis, major_radius, minor_radius,
              material, bounds, major_segments=20, minor_segments=6, radial_scale=(1.0, 1.0)):
    """Create a closed ring whose local Z axis follows an endpoint-derived axis."""
    axis = Vector(axis)
    require(axis.length > 1e-5, f"Torus axis invalid: {name}")
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=major_segments,
        minor_segments=minor_segments,
        location=location,
    )
    obj = bpy.context.object
    obj.scale = (radial_scale[0], radial_scale[1], 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(axis.normalized())
    return finish_part(obj, name, collection, bone_name, material, bounds, smooth=True)


def add_rivet_row(prefix, collection, bone_name, start, end, count, radius, material, bounds):
    """Place deterministic fasteners along a measured line."""
    require(count >= 2, f"Rivet row count invalid: {prefix}")
    start = Vector(start)
    end = Vector(end)
    for index in range(count):
        point = start.lerp(end, index / (count - 1))
        add_sphere(
            f"{prefix}{index:02d}", collection, bone_name, point,
            (radius, radius, radius), material, bounds, 10, 6, True,
        )


def add_ring_stack(prefix, collection, bone_name, start, end, radii, material, bounds, vertices=20):
    """Build concentric capped discs along a shared measured axis."""
    start = Vector(start)
    end = Vector(end)
    direction = end - start
    require(direction.length > 1e-5, f"Ring stack span invalid: {prefix}")
    step = direction / max(1, len(radii))
    for index, radius in enumerate(radii):
        ring_start = start + step * index
        ring_end = ring_start + step * 1.25
        add_cylinder(f"{prefix}{index:02d}", collection, bone_name, ring_start, ring_end, radius, material, bounds, vertices)


def add_segmented_pipe(prefix, collection, bone_name, points, radius, material, collar_material,
                       collar_collection, bounds):
    """Build a visibly jointed pipe from endpoint-derived segments and collars."""
    require(len(points) >= 3, f"Segmented pipe requires bends: {prefix}")
    points = [Vector(point) for point in points]
    for index in range(len(points) - 1):
        add_cylinder(
            f"{prefix}Section{index:02d}", collection, bone_name,
            points[index], points[index + 1], radius, material, bounds, 16,
        )
        if index:
            incoming = (points[index] - points[index - 1]).normalized()
            outgoing = (points[index + 1] - points[index]).normalized()
            axis = (incoming + outgoing).normalized()
            add_cylinder(
                f"{prefix}Collar{index:02d}", collar_collection, bone_name,
                points[index] - axis * 0.018, points[index] + axis * 0.018,
                radius * 1.32, collar_material, bounds, 16,
            )


def add_profile_plate(name, collection, bone_name, profile, y_center, half_depth, material, bounds, bevel=0.008):
    """Extrude an X/Z silhouette into a closed, low-poly armored plate."""
    require(len(profile) >= 3, f"Plate profile invalid: {name}")
    front_y = y_center - half_depth
    rear_y = y_center + half_depth
    vertices = [(x, front_y, z) for x, z in profile] + [(x, rear_y, z) for x, z in profile]
    count = len(profile)
    faces = [tuple(range(count)), tuple(reversed(range(count, count * 2)))]
    for index in range(count):
        following = (index + 1) % count
        faces.append((index, following, count + following, count + index))
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.uv_layers.new(name="UVMap")
    mesh.validate(verbose=True)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if bevel:
        modifier = obj.modifiers.new("EdgeChamfer", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return finish_part(obj, name, collection, bone_name, material, bounds)


def join_parts(parts, name):
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name + "Mesh"
    while len(obj.data.materials) > 1:
        obj.data.materials.pop(index=len(obj.data.materials) - 1)
    for polygon in obj.data.polygons:
        polygon.material_index = 0
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


def make_rig():
    bpy.ops.object.armature_add(enter_editmode=True, location=(0.0, 0.0, 0.0))
    rig = bpy.context.object
    rig.name = "CharacterRig"
    rig.data.name = "CharacterRig"
    edit = rig.data.edit_bones
    edit.remove(edit[0])

    def bone(name, head, tail, parent=None):
        item = edit.new(name)
        item.head = head
        item.tail = tail
        if parent:
            item.parent = edit[parent]
        return item

    bone("Root", (0, 0, 0.00), (0, 0, 0.10))
    bone("Pelvis", (0, 0, 0.78), (0, 0, 0.98), "Root")
    bone("Spine", (0, 0, 0.94), (0, 0, 1.22), "Pelvis")
    bone("Chest", (0, 0, 1.18), (0, 0, 1.42), "Spine")
    bone("Neck", (0, 0, 1.39), (0, 0, 1.50), "Chest")
    bone("Head", (0, 0, 1.48), (0, 0, 1.70), "Neck")
    bone("Thigh.R", (-0.16, 0, 0.86), (-0.16, 0, 0.52), "Pelvis")
    bone("Shin.R", (-0.16, 0, 0.52), (-0.16, 0, 0.16), "Thigh.R")
    bone("Foot.R", (-0.16, 0, 0.16), (-0.16, -0.20, 0.08), "Shin.R")
    bone("Thigh.L", (0.16, 0, 0.86), (0.16, 0, 0.52), "Pelvis")
    bone("Shin.L", (0.16, 0, 0.52), (0.16, 0, 0.16), "Thigh.L")
    bone("Foot.L", (0.16, 0, 0.16), (0.16, -0.20, 0.08), "Shin.L")
    bone("UpperArm.R", (-0.25, 0, 1.35), (-0.31, 0, 1.05), "Chest")
    bone("Forearm.R", (-0.31, 0, 1.05), (-0.29, -0.03, 0.82), "UpperArm.R")
    bone("Hand.R", (-0.29, -0.03, 0.82), (-0.29, -0.08, 0.72), "Forearm.R")
    bone("UpperArm.L", (0.25, 0, 1.35), (0.31, 0, 1.05), "Chest")
    bone("Forearm.L", (0.31, 0, 1.05), (0.29, -0.03, 0.82), "UpperArm.L")
    bone("Hand.L", (0.29, -0.03, 0.82), (0.29, -0.08, 0.72), "Forearm.L")
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def attach_mesh(obj, rig):
    modifier = obj.modifiers.new("CharacterSkin", "ARMATURE")
    modifier.object = rig
    obj.parent = rig


def reset_pose(rig):
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.rotation_quaternion = Quaternion((1.0, 0.0, 0.0, 0.0))
        pose_bone.location = Vector((0.0, 0.0, 0.0))
        pose_bone.scale = Vector((1.0, 1.0, 1.0))
    bpy.context.view_layer.update()


def set_action_frame(rig, action, frame):
    rig.animation_data.action = action
    bpy.context.scene.frame_set(frame, subframe=0.125)
    bpy.context.scene.frame_set(frame, subframe=0.0)
    bpy.context.view_layer.update()


def degrees_pose(values):
    return {name: tuple(math.radians(value) for value in rotation) for name, rotation in values.items()}


def key_euler_pose(action, rig, frame, rotations):
    rig.animation_data.action = action
    bpy.context.scene.frame_set(frame)
    for pose_bone in rig.pose.bones:
        if pose_bone.name in {"Root", "Pelvis"}:
            continue
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.rotation_quaternion = Euler(rotations.get(pose_bone.name, (0.0, 0.0, 0.0)), "XYZ").to_quaternion()
        pose_bone.keyframe_insert("rotation_quaternion", frame=frame, group=pose_bone.name)


def new_action(name):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action["loop_intent"] = name in LOOP_ACTIONS
    action["root_locked"] = True
    action["frame_rate"] = 30
    return action


def solve_leg_rays(rig, targets):
    """Solve local pose quaternions from normalized armature-space segment rays."""
    reset_pose(rig)
    result = {}
    for bone_name in LEG_BONES:
        pose_bone = rig.pose.bones[bone_name]
        if bone_name in targets:
            target = Vector(targets[bone_name])
            require(abs(target.length - 1.0) <= 1e-3, f"Kick ray is not normalized: {bone_name}/{target.length}")
            current = (pose_bone.tail - pose_bone.head).normalized()
            correction = current.rotation_difference(target.normalized())
            pivot = pose_bone.head.copy()
            pose_bone.matrix = (
                Matrix.Translation(pivot)
                @ correction.to_matrix().to_4x4()
                @ Matrix.Translation(-pivot)
                @ pose_bone.matrix
            )
            bpy.context.view_layer.update()
        pose_bone.rotation_mode = "QUATERNION"
        result[bone_name] = pose_bone.rotation_quaternion.copy().normalized()
    return result


def shortest_slerp(first, second, factor):
    first = first.normalized()
    second = second.normalized()
    if first.dot(second) < 0.0:
        second = -second
    return first.slerp(second, factor).normalized()


def key_quaternion_pose(action, rig, frame, rotations):
    rig.animation_data.action = action
    bpy.context.scene.frame_set(frame)
    for bone_name in LEG_BONES:
        pose_bone = rig.pose.bones[bone_name]
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.rotation_quaternion = rotations[bone_name].normalized()
        pose_bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone_name)


def make_actions(rig):
    rig.animation_data_create()

    idle = new_action("Idle")
    idle_poses = {
        1: degrees_pose({"Chest": (0, 0, -1.0), "Head": (0.5, 0, 0), "UpperArm.R": (1.0, 0, 0), "UpperArm.L": (-1.0, 0, 0)}),
        8: degrees_pose({"Chest": (1.5, 0, 0.5), "Head": (-0.5, 0, 0), "UpperArm.R": (-1.5, 0, 0), "UpperArm.L": (1.5, 0, 0)}),
        16: degrees_pose({"Chest": (0, 0, 1.0), "Head": (0.5, 0, 0), "UpperArm.R": (1.0, 0, 0), "UpperArm.L": (-1.0, 0, 0)}),
        23: degrees_pose({"Chest": (-1.5, 0, -0.5), "Head": (-0.5, 0, 0), "UpperArm.R": (-1.5, 0, 0), "UpperArm.L": (1.5, 0, 0)}),
        30: degrees_pose({"Chest": (0, 0, -1.0), "Head": (0.5, 0, 0), "UpperArm.R": (1.0, 0, 0), "UpperArm.L": (-1.0, 0, 0)}),
    }
    for frame, pose in idle_poses.items():
        key_euler_pose(idle, rig, frame, pose)

    run = new_action("Run")
    run_poses = {
        1: degrees_pose({
            "Chest": (0, 0, 3), "Thigh.R": (-35, 0, 0), "Shin.R": (18, 0, 0), "Foot.R": (-10, 0, 0),
            "Thigh.L": (35, 0, 0), "Shin.L": (48, 0, 0), "Foot.L": (10, 0, 0),
            "UpperArm.R": (24, 0, 0), "UpperArm.L": (-24, 0, 0), "Forearm.R": (-8, 0, 0), "Forearm.L": (8, 0, 0),
        }),
        6: degrees_pose({
            "Chest": (0, 0, 0), "Thigh.R": (20, 0, 0), "Shin.R": (35, 0, 0), "Foot.R": (5, 0, 0),
            "Thigh.L": (-5, 0, 0), "Shin.L": (20, 0, 0), "Foot.L": (-5, 0, 0),
        }),
        11: degrees_pose({
            "Chest": (0, 0, -3), "Thigh.R": (35, 0, 0), "Shin.R": (48, 0, 0), "Foot.R": (10, 0, 0),
            "Thigh.L": (-35, 0, 0), "Shin.L": (18, 0, 0), "Foot.L": (-10, 0, 0),
            "UpperArm.R": (-24, 0, 0), "UpperArm.L": (24, 0, 0), "Forearm.R": (8, 0, 0), "Forearm.L": (-8, 0, 0),
        }),
        16: degrees_pose({
            "Chest": (0, 0, 0), "Thigh.R": (-5, 0, 0), "Shin.R": (20, 0, 0), "Foot.R": (-5, 0, 0),
            "Thigh.L": (20, 0, 0), "Shin.L": (35, 0, 0), "Foot.L": (5, 0, 0),
        }),
    }
    run_poses[20] = run_poses[1]
    for frame, pose in run_poses.items():
        key_euler_pose(run, rig, frame, pose)

    jump = new_action("Jump")
    jump_poses = {
        1: degrees_pose({}),
        4: degrees_pose({"Chest": (-8, 0, 0), "Thigh.R": (-28, 0, 0), "Shin.R": (50, 0, 0), "Foot.R": (-16, 0, 0), "Thigh.L": (-28, 0, 0), "Shin.L": (50, 0, 0), "Foot.L": (-16, 0, 0), "UpperArm.R": (24, 0, 0), "UpperArm.L": (24, 0, 0)}),
        7: degrees_pose({"Chest": (3, 0, 0), "Thigh.R": (8, 0, 0), "Shin.R": (-8, 0, 0), "Thigh.L": (8, 0, 0), "Shin.L": (-8, 0, 0), "UpperArm.R": (-18, 0, 0), "UpperArm.L": (-18, 0, 0)}),
        10: degrees_pose({"Chest": (5, 0, 0), "Thigh.R": (-20, 0, 0), "Shin.R": (38, 0, 0), "Foot.R": (-12, 0, 0), "Thigh.L": (-20, 0, 0), "Shin.L": (38, 0, 0), "Foot.L": (-12, 0, 0), "UpperArm.R": (-24, 0, 0), "UpperArm.L": (-24, 0, 0)}),
        12: degrees_pose({}),
    }
    for frame, pose in jump_poses.items():
        key_euler_pose(jump, rig, frame, pose)

    fall = new_action("Fall")
    fall_values = {
        1: (14, 22, 20, 5), 5: (13, 21, 19, 4), 9: (12, 20, 18, 3),
        13: (13, 21, 19, 4), 15: (14, 22, 20, 5),
    }
    for frame, (thigh, shin, arm, chest) in fall_values.items():
        pose = degrees_pose({
            "Chest": (chest, 0, 0), "Thigh.R": (-thigh, 0, 0), "Shin.R": (shin, 0, 0),
            "Foot.R": (-7, 0, 0), "Thigh.L": (thigh - 2, 0, 0), "Shin.L": (-(shin - 2), 0, 0),
            "Foot.L": (7, 0, 0), "UpperArm.R": (arm, 0, 7), "UpperArm.L": (-(arm - 2), 0, -7),
            "Forearm.R": (-10, 0, 0), "Forearm.L": (10, 0, 0),
        })
        key_euler_pose(fall, rig, frame, pose)

    land = new_action("Land")
    land_poses = {
        1: degrees_pose({"Chest": (-5, 0, 0), "Thigh.R": (-22, 0, 0), "Shin.R": (38, 0, 0), "Foot.R": (-12, 0, 0), "Thigh.L": (-22, 0, 0), "Shin.L": (38, 0, 0), "Foot.L": (-12, 0, 0), "UpperArm.R": (15, 0, 0), "UpperArm.L": (15, 0, 0)}),
        3: degrees_pose({"Chest": (-9, 0, 0), "Thigh.R": (-34, 0, 0), "Shin.R": (58, 0, 0), "Foot.R": (-20, 0, 0), "Thigh.L": (-34, 0, 0), "Shin.L": (58, 0, 0), "Foot.L": (-20, 0, 0), "UpperArm.R": (24, 0, 0), "UpperArm.L": (24, 0, 0)}),
        5: degrees_pose({"Chest": (-5, 0, 0), "Thigh.R": (-20, 0, 0), "Shin.R": (34, 0, 0), "Foot.R": (-12, 0, 0), "Thigh.L": (-20, 0, 0), "Shin.L": (34, 0, 0), "Foot.L": (-12, 0, 0), "UpperArm.R": (12, 0, 0), "UpperArm.L": (12, 0, 0)}),
        8: degrees_pose({"Chest": (-2, 0, 0), "Thigh.R": (-7, 0, 0), "Shin.R": (10, 0, 0), "Foot.R": (-3, 0, 0), "Thigh.L": (-7, 0, 0), "Shin.L": (10, 0, 0), "Foot.L": (-3, 0, 0)}),
        10: degrees_pose({}),
    }
    for frame, pose in land_poses.items():
        key_euler_pose(land, rig, frame, pose)

    kick = new_action("Kick")
    neutral = {name: Quaternion((1.0, 0.0, 0.0, 0.0)) for name in LEG_BONES}
    chamber = solve_leg_rays(rig, {
        "Thigh.R": (0.0, 0.342, -0.940), "Shin.R": (0.0, -0.766, 0.643),
        "Thigh.L": (0.0, -0.174, -0.985), "Shin.L": (0.0, 0.0, -1.0),
        "Foot.R": (0.0, -0.928477, -0.371391), "Foot.L": (0.0, -0.928477, -0.371391),
    })
    strike = solve_leg_rays(rig, {
        "Thigh.R": (0.0, -1.0, 0.0), "Shin.R": (0.0, -1.0, 0.0), "Foot.R": (0.0, -1.0, 0.0),
        "Thigh.L": (0.0, 0.342, -0.940), "Shin.L": (0.0, -0.766, 0.643),
        "Foot.L": (0.0, -0.928477, -0.371391),
    })
    recovery = {name: shortest_slerp(strike[name], neutral[name], 0.5) for name in LEG_BONES}
    for frame, pose in ((1, neutral), (3, chamber), (4, strike), (7, recovery), (11, neutral)):
        key_quaternion_pose(kick, rig, frame, pose)
    reset_pose(rig)

    actions = (idle, run, jump, fall, land, kick)
    rig.animation_data.action = idle
    return actions, {"chamber": chamber, "strike": strike, "recovery": recovery}


def build_character():
    KNEE_MECHANICS.clear()
    body_material = make_material("CyborgDarkMetal", (0.018, 0.024, 0.023), 0.92, 0.24)
    armor_material = make_material("CyborgOxideArmor", (0.035, 0.047, 0.018), 0.66, 0.52)
    head_material = make_material("CyborgHeadMetal", (0.110, 0.045, 0.016), 0.68, 0.45)
    eye_material = make_material("CyborgLensGlass", (0.80, 0.008, 0.002), 0.20, 0.16)
    body, armor, head, eye, bounds = [], [], [], [], {}

    # One curved boiler remains, but its lower structure is a faceted cradle and layered plate shell.
    add_box("PelvisCore", body, "Pelvis", (0.0, 0.020, 0.835), (0.205, 0.115, 0.145), body_material, bounds, 0.035, bevel_segments=3)
    add_sphere("TorsoCore", armor, "Spine", (0.005, 0.005, 1.185), (0.282, 0.185, 0.305), armor_material, bounds, 28, 16, True)
    add_profile_plate("TorsoLowerPlate", armor, "Spine", ((-0.250, 0.990), (0.245, 0.990), (0.270, 1.145), (0.220, 1.205), (-0.220, 1.205), (-0.270, 1.145)), -0.130, 0.040, armor_material, bounds, 0.018)
    add_profile_plate("TorsoUpperPlate", armor, "Chest", ((-0.235, 1.245), (0.225, 1.245), (0.260, 1.390), (0.205, 1.455), (-0.205, 1.455), (-0.260, 1.390)), -0.120, 0.038, armor_material, bounds, 0.016)
    add_torus("TorsoWaistSeam", body, "Spine", (0.005, -0.005, 1.035), (0, 0, 1), 0.245, 0.010, body_material, bounds, 28, 6, (1.0, 0.69))
    add_torus("TorsoCrownSeam", body, "Chest", (-0.005, 0.000, 1.365), (0, 0, 1), 0.238, 0.009, body_material, bounds, 28, 6, (1.0, 0.70))
    add_cylinder("NeckCore", body, "Neck", (0.075, -0.045, 1.355), (0.105, -0.145, 1.455), 0.052, body_material, bounds, 14)

    # Broken side panels and deep gaps reveal actual rods instead of painted-on recesses.
    for side, x, normal in (("R", -0.278, -1.0), ("L", 0.278, 1.0)):
        add_box(f"SideCavity.{side}", body, "Chest", (x, -0.005, 1.185), (0.016, 0.090, 0.125), body_material, bounds, 0.012, bevel_segments=3)
        add_cylinder(f"SideMechanismA.{side}", body, "Chest", (x - normal * 0.010, -0.060, 1.095), (x - normal * 0.010, 0.055, 1.300), 0.012, body_material, bounds, 12)
        add_cylinder(f"SideMechanismB.{side}", head, "Chest", (x - normal * 0.008, 0.055, 1.105), (x - normal * 0.008, -0.040, 1.285), 0.010, head_material, bounds, 12)
        add_box(f"SidePanelUpper.{side}", armor, "Chest", (x, 0.030, 1.355), (0.024, 0.095, 0.070), armor_material, bounds, 0.010, bevel_segments=2)
        add_box(f"SidePanelLower.{side}", armor, "Spine", (x, 0.025, 1.015), (0.024, 0.090, 0.066), armor_material, bounds, 0.010, bevel_segments=2)

    # Legs use paired rods and true X-axis clevis hinges: side plates capture a narrow child lug.
    for side, x in (("R", -0.155), ("L", 0.155)):
        outward = -1.0 if side == "R" else 1.0
        add_box(f"HipCradle.{side}", armor, "Pelvis", (x, 0.012, 0.850), (0.076, 0.058, 0.078), armor_material, bounds, 0.018, bevel_segments=3)
        add_box(f"HipBearing.{side}", body, f"Thigh.{side}", (x, -0.010, 0.840), (0.040, 0.042, 0.060), body_material, bounds, 0.014, bevel_segments=2)
        add_cylinder(f"HipAxle.{side}", body, f"Thigh.{side}", (x - 0.087, -0.010, 0.840), (x + 0.087, -0.010, 0.840), 0.015, body_material, bounds, 12)
        for cap_side in (-1.0, 1.0):
            cap_x = x + cap_side * 0.086
            add_cylinder(f"HipAxleCap{int(cap_side):+d}.{side}", head, f"Thigh.{side}", (cap_x - cap_side * 0.006, -0.010, 0.840), (cap_x + cap_side * 0.010, -0.010, 0.840), 0.024, head_material, bounds, 12)
        add_box(f"HipFrontBracket.{side}", armor, f"Thigh.{side}", (x, -0.066, 0.840), (0.052, 0.018, 0.050), armor_material, bounds, 0.010, bevel_segments=2)
        for rod_index, (dx, y) in enumerate(((outward * 0.032, -0.026), (-outward * 0.030, 0.030))):
            add_cylinder(f"Thigh{'Main' if rod_index == 0 else 'Piston'}.{side}", body, f"Thigh.{side}", (x + dx, y, 0.84), (x + dx, y, 0.50), 0.021, body_material, bounds, 14)
            add_cylinder(f"ThighSleeve{rod_index}.{side}", head, f"Thigh.{side}", (x + dx, y, 0.70), (x + dx, y, 0.79), 0.029, head_material, bounds, 14)
            add_torus(f"ThighCap{rod_index}.{side}", body, f"Thigh.{side}", (x + dx, y, 0.695), (0, 0, 1), 0.027, 0.006, body_material, bounds, 16, 5)
        # Preserve the connection-map owner name on the dominant rod.
        add_cylinder(f"Thigh.{side}", body, f"Thigh.{side}", (x + outward * 0.032, -0.026, 0.84), (x + outward * 0.032, -0.026, 0.50), 0.022, body_material, bounds, 14)
        add_cylinder(f"ThighBrace.{side}", armor, f"Thigh.{side}", (x - outward * 0.044, -0.005, 0.735), (x + outward * 0.045, -0.005, 0.620), 0.012, armor_material, bounds, 10)
        add_box(f"ThighMount.{side}", armor, f"Thigh.{side}", (x, -0.058, 0.675), (0.058, 0.018, 0.040), armor_material, bounds, 0.010, bevel_segments=2)

        plate_extents = Vector((0.014, 0.088, 0.078))
        lug_extents = Vector((0.034, 0.034, 0.048))
        bridge_extents = Vector((0.078, 0.019, 0.020))
        plate_centers = []
        for plate_side in (-1.0, 1.0):
            plate_center = Vector((x + plate_side * 0.060, 0.0, 0.515))
            plate_centers.append(plate_center)
            add_box(f"KneeClevisPlate{int(plate_side):+d}.{side}", armor, f"Thigh.{side}", plate_center, plate_extents, armor_material, bounds, 0.010, bevel_segments=2)
        bridge_center = Vector((x, -0.103, 0.590))
        add_box(f"KneeBridge.{side}", armor, f"Thigh.{side}", bridge_center, bridge_extents, armor_material, bounds, 0.009, bevel_segments=2)
        lug_center = Vector((x, 0.0, 0.475))
        add_box(f"Knee.{side}", body, f"Shin.{side}", lug_center, lug_extents, body_material, bounds, 0.012, bevel_segments=2)
        add_box(f"KneeFrontArmor.{side}", head, f"Shin.{side}", (x, -0.050, 0.472), (0.041, 0.016, 0.038), head_material, bounds, 0.008, bevel_segments=2)
        axle_start = Vector((x - 0.090, 0.0, 0.515))
        axle_end = Vector((x + 0.090, 0.0, 0.515))
        add_cylinder(f"KneeAxle.{side}", body, f"Shin.{side}", axle_start, axle_end, 0.015, body_material, bounds, 12)
        for cap_side in (-1.0, 1.0):
            cap_x = x + cap_side * 0.090
            add_cylinder(f"KneeAxleCap{int(cap_side):+d}.{side}", head, f"Shin.{side}", (cap_x - cap_side * 0.006, 0.0, 0.515), (cap_x + cap_side * 0.010, 0.0, 0.515), 0.023, head_material, bounds, 12)
        KNEE_MECHANICS[side] = {
            "axle_bone": f"Shin.{side}", "axle_start": axle_start, "axle_end": axle_end,
            "plates": [(f"Thigh.{side}", center, plate_extents) for center in plate_centers],
            "bridge": (f"Thigh.{side}", bridge_center, bridge_extents),
            "lug": (f"Shin.{side}", lug_center, lug_extents),
        }
        for rod_index, (dx, y) in enumerate(((outward * 0.030, -0.024), (-outward * 0.028, 0.030))):
            add_cylinder(f"Shin{'Main' if rod_index == 0 else 'Piston'}.{side}", body, f"Shin.{side}", (x + dx, y, 0.50), (x + dx, y, 0.145), 0.019, body_material, bounds, 14)
            add_cylinder(f"ShinSleeve{rod_index}.{side}", head, f"Shin.{side}", (x + dx, y, 0.245), (x + dx, y, 0.340), 0.027, head_material, bounds, 14)
            add_torus(f"ShinCap{rod_index}.{side}", body, f"Shin.{side}", (x + dx, y, 0.240), (0, 0, 1), 0.025, 0.006, body_material, bounds, 16, 5)
        add_cylinder(f"Shin.{side}", body, f"Shin.{side}", (x + outward * 0.030, -0.024, 0.50), (x + outward * 0.030, -0.024, 0.145), 0.020, body_material, bounds, 14)
        add_cylinder(f"ShinBrace.{side}", armor, f"Shin.{side}", (x + outward * 0.044, -0.004, 0.405), (x - outward * 0.043, -0.004, 0.285), 0.011, armor_material, bounds, 10)
        add_box(f"ShinPlate.{side}", armor, f"Shin.{side}", (x, -0.060, 0.330), (0.058, 0.016, 0.052), armor_material, bounds, 0.010, bevel_segments=2)

        for plate_side in (-1.0, 1.0):
            add_box(f"AnkleClevisPlate{int(plate_side):+d}.{side}", armor, f"Shin.{side}", (x + plate_side * 0.047, 0.0, 0.155), (0.011, 0.042, 0.055), armor_material, bounds, 0.008, bevel_segments=2)
        add_box(f"AnkleBearing.{side}", body, f"Foot.{side}", (x, -0.002, 0.145), (0.031, 0.031, 0.044), body_material, bounds, 0.009, bevel_segments=2)
        add_cylinder(f"AnkleAxle.{side}", body, f"Foot.{side}", (x - 0.068, -0.002, 0.155), (x + 0.068, -0.002, 0.155), 0.012, body_material, bounds, 12)
        for cap_side in (-1.0, 1.0):
            cap_x = x + cap_side * 0.068
            add_cylinder(f"AnkleAxleCap{int(cap_side):+d}.{side}", head, f"Foot.{side}", (cap_x - cap_side * 0.005, -0.002, 0.155), (cap_x + cap_side * 0.008, -0.002, 0.155), 0.019, head_material, bounds, 12)
        add_box(f"AnkleYoke.{side}", armor, f"Foot.{side}", (x, -0.005, 0.125), (0.078, 0.055, 0.052), armor_material, bounds, 0.012, bevel_segments=2)
        add_box(f"Boot.{side}", body, f"Foot.{side}", (x, -0.045, 0.078), (0.108, 0.150, 0.075), body_material, bounds, 0.018, bevel_segments=3)
        add_box(f"BootUpperShell.{side}", armor, f"Foot.{side}", (x, -0.070, 0.105), (0.096, 0.104, 0.047), armor_material, bounds, 0.014, rotation=(math.radians(-10.0), 0.0, 0.0), bevel_segments=3)
        add_box(f"BootToeBlock.{side}", armor, f"Foot.{side}", (x, -0.181, 0.062), (0.104, 0.038, 0.052), armor_material, bounds, 0.012, bevel_segments=2)
        add_box(f"BootHeel.{side}", body, f"Foot.{side}", (x, 0.112, 0.068), (0.092, 0.045, 0.055), body_material, bounds, 0.012, bevel_segments=2)
        add_box(f"BootSoleUpper.{side}", head, f"Foot.{side}", (x, -0.045, 0.026), (0.114, 0.154, 0.018), head_material, bounds, 0.006, bevel_segments=2)
        add_box(f"BootSoleLower.{side}", body, f"Foot.{side}", (x, -0.045, 0.009), (0.118, 0.158, 0.009), body_material, bounds, 0.004, bevel_segments=2)
        for plate_side in (-1.0, 1.0):
            plate_x = x + plate_side * 0.108
            add_box(f"BootSidePlate{int(plate_side):+d}.{side}", head, f"Foot.{side}", (plate_x, -0.070, 0.075), (0.009, 0.080, 0.035), head_material, bounds, 0.005)
            add_sphere(f"BootFastener{int(plate_side):+d}.{side}", body, f"Foot.{side}", (plate_x + plate_side * 0.006, -0.095, 0.078), (0.010, 0.010, 0.010), body_material, bounds, 10, 6, True)

    # Arm construction is deliberately asymmetric, with layered block bearings instead of facade wheels.
    for side, x0, x1 in (("R", -0.235, -0.305), ("L", 0.235, 0.305)):
        outward = -1.0 if side == "R" else 1.0
        hand_x = -0.298 if side == "R" else 0.298
        add_box(f"ShoulderBearing.{side}", body, f"UpperArm.{side}", (x0, -0.005, 1.35), (0.062, 0.060, 0.075), body_material, bounds, 0.018, bevel_segments=3)
        add_box(f"ShoulderArmor.{side}", armor if side == "R" else head, f"UpperArm.{side}", (x0, -0.073, 1.35), (0.054, 0.018, 0.063), armor_material if side == "R" else head_material, bounds, 0.010, bevel_segments=2)
        add_cylinder(f"ShoulderAxle.{side}", body, f"UpperArm.{side}", (x0 - 0.075, -0.008, 1.35), (x0 + 0.075, -0.008, 1.35), 0.012, body_material, bounds, 12)
        for rod_index, (dx, y) in enumerate(((outward * 0.018, -0.030), (-outward * 0.023, 0.027))):
            start = (x0 + dx, y, 1.35 - rod_index * 0.015)
            end = (x1 + dx, y, 1.04 + rod_index * 0.018)
            add_cylinder(f"UpperArmRod{rod_index}.{side}", body, f"UpperArm.{side}", start, end, 0.020, body_material, bounds, 14)
            midpoint = Vector(start).lerp(Vector(end), 0.52)
            direction = (Vector(end) - Vector(start)).normalized()
            add_cylinder(f"UpperArmSleeve{rod_index}.{side}", head, f"UpperArm.{side}", midpoint - direction * 0.045, midpoint + direction * 0.045, 0.028, head_material, bounds, 14)
        add_cylinder(f"UpperArm.{side}", body, f"UpperArm.{side}", (x0 + outward * 0.018, -0.030, 1.35), (x1 + outward * 0.018, -0.030, 1.04), 0.021, body_material, bounds, 14)
        add_box(f"UpperArmHinge.{side}", armor, f"UpperArm.{side}", ((x0 + x1) * 0.5, -0.070, 1.195), (0.052, 0.018, 0.042), armor_material, bounds, 0.009, bevel_segments=2)

        add_box(f"ElbowClevis.{side}", armor, f"UpperArm.{side}", (x1, 0.0, 1.055), (0.058, 0.050, 0.068), armor_material, bounds, 0.014, bevel_segments=3)
        add_box(f"Elbow.{side}", body, f"Forearm.{side}", (x1, -0.012, 1.045), (0.037, 0.036, 0.050), body_material, bounds, 0.010, bevel_segments=2)
        add_cylinder(f"ElbowAxle.{side}", body, f"Forearm.{side}", (x1 - 0.055, -0.006, 1.05), (x1 + 0.055, -0.006, 1.05), 0.013, body_material, bounds, 12)
        for cap_side in (-1.0, 1.0):
            cap_x = x1 + cap_side * 0.055
            add_cylinder(f"ElbowAxleCap{int(cap_side):+d}.{side}", head, f"Forearm.{side}", (cap_x - cap_side * 0.005, -0.006, 1.05), (cap_x + cap_side * 0.008, -0.006, 1.05), 0.020, head_material, bounds, 12)
        for rod_index, (dx, y) in enumerate(((outward * 0.019, -0.028), (-outward * 0.021, 0.025))):
            start = (x1 + dx, y, 1.04)
            end = (hand_x + dx, y - 0.010, 0.805)
            add_cylinder(f"ForearmRod{rod_index}.{side}", body, f"Forearm.{side}", start, end, 0.018, body_material, bounds, 14)
            midpoint = Vector(start).lerp(Vector(end), 0.58)
            direction = (Vector(end) - Vector(start)).normalized()
            add_cylinder(f"ForearmSleeve{rod_index}.{side}", head, f"Forearm.{side}", midpoint - direction * 0.038, midpoint + direction * 0.038, 0.026, head_material, bounds, 14)
        add_cylinder(f"Forearm.{side}", body, f"Forearm.{side}", (x1 + outward * 0.019, -0.028, 1.04), (hand_x + outward * 0.019, -0.038, 0.805), 0.019, body_material, bounds, 14)
        add_box(f"ForearmShell.{side}", armor, f"Forearm.{side}", ((x1 + hand_x) * 0.5 + outward * 0.010, -0.070, 0.900), (0.048, 0.022, 0.055), armor_material, bounds, 0.012, rotation=(0.0, math.radians(outward * 4.0), 0.0), bevel_segments=3)
        add_box(f"ForearmRail.{side}", head, f"Forearm.{side}", ((x1 + hand_x) * 0.5 - outward * 0.044, -0.060, 0.900), (0.012, 0.018, 0.082), head_material, bounds, 0.005)
        add_rivet_row(f"ForearmFastener.{side}.", body, f"Forearm.{side}", ((x1 + hand_x) * 0.5 + outward * 0.045, -0.100, 0.845), ((x1 + hand_x) * 0.5 + outward * 0.045, -0.100, 0.955), 3, 0.008, body_material, bounds)

        add_box(f"WristClamp.{side}", armor, f"Forearm.{side}", (hand_x, -0.010, 0.81), (0.052, 0.042, 0.048), armor_material, bounds, 0.010, bevel_segments=2)
        add_box(f"WristBearing.{side}", body, f"Hand.{side}", (hand_x, -0.025, 0.805), (0.033, 0.032, 0.038), body_material, bounds, 0.008, bevel_segments=2)
        add_cylinder(f"WristAxle.{side}", body, f"Hand.{side}", (hand_x - 0.061, -0.018, 0.81), (hand_x + 0.061, -0.018, 0.81), 0.010, body_material, bounds, 12)
        add_box(f"Fist.{side}", body, f"Hand.{side}", (hand_x, -0.048, 0.748), (0.066, 0.067, 0.074), body_material, bounds, 0.014, bevel_segments=3)
        # Four stepped fingers and a side thumb remain separated at gameplay distance.
        for finger_index in range(4):
            finger_x = hand_x + (finger_index - 1.5) * 0.033
            knuckle_z = 0.786 - finger_index * 0.006
            add_box(f"Knuckle{finger_index}.{side}", head, f"Hand.{side}", (finger_x, -0.124, knuckle_z), (0.014, 0.021, 0.025), head_material, bounds, 0.005, rotation=(math.radians(finger_index * 2.0), 0.0, 0.0), bevel_segments=2)
            add_box(f"Finger{finger_index}.{side}", head, f"Hand.{side}", (finger_x, -0.110, knuckle_z - 0.040), (0.013, 0.023, 0.016), head_material, bounds, 0.004, rotation=(math.radians(68.0 + finger_index * 2.0), 0.0, 0.0), bevel_segments=2)
        thumb_x = hand_x - outward * 0.052
        add_box(f"ThumbBase.{side}", head, f"Hand.{side}", (thumb_x, -0.100, 0.748), (0.020, 0.025, 0.024), head_material, bounds, 0.006, rotation=(0.0, math.radians(outward * 24.0), 0.0), bevel_segments=2)
        add_box(f"ThumbTip.{side}", head, f"Hand.{side}", (thumb_x - outward * 0.010, -0.124, 0.727), (0.016, 0.019, 0.017), head_material, bounds, 0.005, bevel_segments=2)

    # Large offset cowl overlaps the boiler shell with a thick dark underside and explicit seams.
    add_sphere("ChestPlate", armor, "Chest", (-0.005, -0.030, 1.225), (0.270, 0.160, 0.270), armor_material, bounds, 28, 16, True)
    add_profile_plate(
        "CowlUnderside", body, "Chest",
        ((-0.355, 1.238), (-0.060, 1.225), (0.025, 1.385), (-0.055, 1.558),
         (-0.282, 1.536), (-0.360, 1.418)),
        -0.140, 0.075, body_material, bounds, 0.012,
    )
    add_profile_plate(
        "Cowl.R", armor, "Chest",
        ((-0.355, 1.245), (-0.060, 1.235), (0.020, 1.392), (-0.058, 1.558), (-0.285, 1.535), (-0.360, 1.420)),
        -0.130, 0.090, armor_material, bounds, 0.014,
    )
    spike_specs = (
        ("CowlSpikeRear", (-0.323, -0.200, 1.420), (-0.357, -0.202, 1.510), 0.022),
        ("CowlSpikeFront", (-0.270, -0.208, 1.493), (-0.292, -0.212, 1.620), 0.024),
        ("CowlSpikeCrown", (-0.190, -0.205, 1.525), (-0.198, -0.207, 1.688), 0.025),
        ("CowlSpike04", (-0.105, -0.205, 1.530), (-0.100, -0.208, 1.655), 0.021),
        ("CowlSpike05", (-0.040, -0.199, 1.485), (-0.020, -0.202, 1.585), 0.019),
        ("CowlSpike06", (-0.338, -0.202, 1.345), (-0.370, -0.204, 1.390), 0.018),
    )
    for name, base, tip, radius in spike_specs:
        add_cone(name, armor, "Chest", base, tip, radius, armor_material, bounds, 12)
    add_rivet_row("CowlTopRivet", body, "Chest", (-0.310, -0.233, 1.478), (-0.072, -0.233, 1.525), 7, 0.009, body_material, bounds)
    add_rivet_row("CowlLowerRivet", body, "Chest", (-0.330, -0.233, 1.292), (-0.060, -0.233, 1.270), 8, 0.009, body_material, bounds)
    add_box("CowlLowerLip", head, "Chest", (-0.185, -0.226, 1.258), (0.137, 0.010, 0.018), head_material, bounds, 0.006, rotation=(0.0, math.radians(-2.5), 0.0), bevel_segments=2)

    # Opposite shoulder retains its asymmetry through stacked plates and a small side pin.
    add_box("Cowl.L", head, "UpperArm.L", (0.292, -0.004, 1.345), (0.076, 0.090, 0.105), head_material, bounds, 0.024, bevel_segments=3)
    add_box("LeftShoulderArmorPlate", armor, "UpperArm.L", (0.292, -0.103, 1.345), (0.066, 0.018, 0.082), armor_material, bounds, 0.012, bevel_segments=2)
    add_box("LeftShoulderCenterBlock", body, "UpperArm.L", (0.292, -0.128, 1.345), (0.036, 0.010, 0.045), body_material, bounds, 0.008, bevel_segments=2)
    add_rivet_row("LeftShoulderRivet", body, "UpperArm.L", (0.250, -0.144, 1.390), (0.334, -0.144, 1.390), 4, 0.008, body_material, bounds)

    # Deep access hatch: proud frame, dark seam, inset door, hinge barrels, latch, dense fasteners.
    add_box("HatchRecess", body, "Chest", (0.045, -0.205, 1.115), (0.158, 0.014, 0.105), body_material, bounds, 0.012, bevel_segments=3)
    add_box("HatchDoor", head, "Chest", (0.045, -0.222, 1.115), (0.137, 0.010, 0.086), head_material, bounds, 0.010, bevel_segments=2)
    for index, (location, extents) in enumerate((
        ((0.045, -0.238, 1.211), (0.160, 0.008, 0.010)),
        ((0.045, -0.238, 1.019), (0.160, 0.008, 0.010)),
        ((-0.113, -0.238, 1.115), (0.010, 0.008, 0.096)),
        ((0.203, -0.238, 1.115), (0.010, 0.008, 0.096)),
    )):
        add_box(f"HatchFrame{index}", armor, "Chest", location, extents, armor_material, bounds, 0.004, bevel_segments=2)
    for hinge_index, z in enumerate((1.075, 1.155)):
        add_cylinder(f"HatchHinge{hinge_index}", body, "Chest", (-0.100, -0.240, z - 0.026), (-0.100, -0.240, z + 0.026), 0.012, body_material, bounds, 12)
    add_box("HatchLatchBase", body, "Chest", (0.157, -0.241, 1.114), (0.021, 0.008, 0.030), body_material, bounds, 0.005)
    add_box("HatchLatchHandle", armor, "Chest", (0.157, -0.240, 1.114), (0.008, 0.005, 0.022), armor_material, bounds, 0.003, rotation=(math.radians(18.0), 0.0, 0.0))
    add_rivet_row("HatchTopFastener", body, "Chest", (-0.080, -0.244, 1.190), (0.170, -0.244, 1.190), 6, 0.008, body_material, bounds)
    add_rivet_row("HatchBottomFastener", body, "Chest", (-0.080, -0.244, 1.040), (0.170, -0.244, 1.040), 6, 0.008, body_material, bounds)

    add_profile_plate("WaistPlate", armor, "Pelvis", ((-0.215, 0.800), (0.200, 0.800), (0.225, 0.900), (0.170, 0.970), (-0.180, 0.970), (-0.235, 0.900)), -0.120, 0.026, armor_material, bounds, 0.014)
    add_profile_plate("PelvisApron", head, "Pelvis", ((-0.180, 0.735), (0.175, 0.735), (0.205, 0.815), (0.165, 0.875), (-0.170, 0.875), (-0.210, 0.815)), -0.153, 0.018, head_material, bounds, 0.010)
    add_box("BackHousing", armor, "Chest", (0.010, 0.135, 1.235), (0.215, 0.055, 0.190), armor_material, bounds, 0.030, bevel_segments=3)
    add_box("BackCenterSeam", body, "Chest", (0.010, 0.188, 1.235), (0.012, 0.006, 0.168), body_material, bounds, 0.004)

    # Large tan tank with end domes, ribs, straps, brackets, plumbing, and a jointed exhaust.
    add_cylinder("RearTank", head, "Chest", (0.105, 0.105, 1.345), (0.325, 0.105, 1.345), 0.082, head_material, bounds, 24)
    add_sphere("RearTankCapInner", head, "Chest", (0.105, 0.105, 1.345), (0.040, 0.082, 0.082), head_material, bounds, 18, 10, True)
    add_sphere("RearTankCapOuter", head, "Chest", (0.335, 0.105, 1.345), (0.035, 0.082, 0.082), head_material, bounds, 18, 10, True)
    for band_index, x in enumerate((0.145, 0.215, 0.285)):
        add_cylinder(f"RearTankRib{band_index}", body, "Chest", (x - 0.012, 0.105, 1.345), (x + 0.012, 0.105, 1.345), 0.088, body_material, bounds, 24)
        add_cylinder(f"RearTankStrap{band_index}", armor, "Chest", (x - 0.006, 0.105, 1.345), (x + 0.006, 0.105, 1.345), 0.091, armor_material, bounds, 24)
    add_cylinder("RearTankBandInner", body, "Chest", (0.143, 0.105, 1.345), (0.158, 0.105, 1.345), 0.089, body_material, bounds, 20)
    add_cylinder("RearTankBandOuter", body, "Chest", (0.277, 0.105, 1.345), (0.292, 0.105, 1.345), 0.089, body_material, bounds, 20)
    add_cylinder("TankBracketUpper", body, "Chest", (0.125, 0.075, 1.390), (0.040, 0.165, 1.405), 0.013, body_material, bounds, 12)
    add_cylinder("TankBracketLower", body, "Chest", (0.125, 0.075, 1.300), (0.040, 0.165, 1.285), 0.013, body_material, bounds, 12)
    add_segmented_pipe(
        "TankHose", body, "Chest",
        ((0.300, 0.145, 1.300), (0.270, 0.175, 1.250), (0.210, 0.175, 1.220), (0.160, 0.150, 1.185)),
        0.011, body_material, head_material, head, bounds,
    )

    add_cylinder("ExhaustLower", head, "Chest", (0.150, 0.105, 1.390), (0.090, 0.115, 1.490), 0.030, head_material, bounds, 16)
    add_sphere("ExhaustElbow", head, "Chest", (0.090, 0.115, 1.490), (0.037, 0.037, 0.037), head_material, bounds, 16, 8, True)
    add_cylinder("ExhaustUpper", head, "Chest", (0.090, 0.115, 1.490), (0.000, 0.100, 1.555), 0.030, head_material, bounds, 16)
    add_sphere("ExhaustUpperBend", head, "Chest", (0.000, 0.100, 1.555), (0.036, 0.036, 0.036), head_material, bounds, 16, 8, True)
    add_cylinder("ExhaustAngled", head, "Chest", (0.000, 0.100, 1.555), (-0.050, 0.065, 1.635), 0.029, head_material, bounds, 16)
    add_sphere("ExhaustFinalElbow", head, "Chest", (-0.050, 0.065, 1.635), (0.035, 0.035, 0.035), head_material, bounds, 16, 8, True)
    add_cylinder("ExhaustCollar", body, "Chest", (-0.050, 0.065, 1.635), (-0.080, 0.035, 1.675), 0.036, body_material, bounds, 16)
    add_cylinder("ExhaustMouth", head, "Chest", (-0.080, 0.035, 1.675), (-0.096, 0.015, 1.695), 0.031, head_material, bounds, 18)
    add_cylinder("ExhaustDarkOpening", body, "Chest", (-0.097, 0.014, 1.696), (-0.101, 0.009, 1.701), 0.022, body_material, bounds, 18)
    for collar_index, (start, end) in enumerate((((0.112, 0.111, 1.455), (0.097, 0.114, 1.480)), ((0.018, 0.103, 1.542), (-0.005, 0.098, 1.560)), ((-0.060, 0.055, 1.646), (-0.076, 0.039, 1.668)))):
        add_cylinder(f"ExhaustJointCollar{collar_index}", body, "Chest", start, end, 0.037, body_material, bounds, 16)

    # Antenna is mechanically mounted and segmented, not a wire sprouting from the shell.
    add_cylinder("AntennaBase", head, "Head", (0.205, 0.105, 1.415), (0.218, 0.085, 1.455), 0.026, head_material, bounds, 16)
    add_sphere("AntennaBaseJoint", body, "Head", (0.218, 0.085, 1.455), (0.031, 0.031, 0.031), body_material, bounds, 16, 8, True)
    add_cylinder("AntennaStem", body, "Head", (0.218, 0.085, 1.455), (0.250, 0.060, 1.560), 0.008, body_material, bounds, 10)
    add_sphere("AntennaKnuckle", head, "Head", (0.250, 0.060, 1.560), (0.014, 0.014, 0.014), head_material, bounds, 12, 6, True)
    add_cylinder("AntennaUpperStem", body, "Head", (0.250, 0.060, 1.560), (0.286, 0.030, 1.675), 0.007, body_material, bounds, 10)
    add_sphere("AntennaBall", body, "Head", (0.290, 0.027, 1.687), (0.018, 0.018, 0.018), body_material, bounds, 14, 8, True)

    # Recessed camera pod: deep armored cavity, concentric barrels, six real grille slats, hood and brackets.
    add_box("HeadPod", body, "Head", (0.105, -0.195, 1.420), (0.137, 0.046, 0.100), body_material, bounds, 0.020, bevel_segments=3)
    add_box("HeadCavity", body, "Head", (0.105, -0.229, 1.423), (0.120, 0.017, 0.082), body_material, bounds, 0.016, bevel_segments=3)
    add_box("Brow", head, "Head", (0.105, -0.238, 1.505), (0.134, 0.010, 0.025), head_material, bounds, 0.007, rotation=(0.0, 0.0, math.radians(-2.0)), bevel_segments=2)
    add_box("JawGuard", head, "Head", (0.105, -0.238, 1.337), (0.128, 0.010, 0.024), head_material, bounds, 0.007, bevel_segments=2)
    add_box("HeadBracket.R", armor, "Head", (-0.023, -0.230, 1.420), (0.015, 0.012, 0.079), armor_material, bounds, 0.006, rotation=(0.0, 0.0, math.radians(-4.0)), bevel_segments=2)
    add_box("HeadBracket.L", armor, "Head", (0.233, -0.230, 1.420), (0.015, 0.012, 0.079), armor_material, bounds, 0.006, rotation=(0.0, 0.0, math.radians(4.0)), bevel_segments=2)
    for side, lens_x in (("R", 0.052), ("L", 0.158)):
        add_cylinder(f"LensOuterBarrel.{side}", body, "Head", (lens_x, -0.238, 1.445), (lens_x, -0.205, 1.445), 0.044, body_material, bounds, 24)
        add_cylinder(f"LensCollar.{side}", head, "Head", (lens_x, -0.240, 1.445), (lens_x, -0.224, 1.445), 0.037, head_material, bounds, 24)
        add_cylinder(f"LensInnerRim.{side}", body, "Head", (lens_x, -0.242, 1.445), (lens_x, -0.234, 1.445), 0.029, body_material, bounds, 24)
    for slat_index in range(6):
        slat_x = 0.047 + slat_index * 0.023
        add_box(f"GrilleSlat{slat_index}", head, "Head", (slat_x, -0.246, 1.391), (0.007, 0.003, 0.012), head_material, bounds, 0.002, rotation=(0.0, 0.0, math.radians(-5.0)), bevel_segments=2)
    add_box("GrilleTopRail", head, "Head", (0.105, -0.245, 1.408), (0.079, 0.004, 0.005), head_material, bounds, 0.002)
    add_box("GrilleBottomRail", head, "Head", (0.105, -0.245, 1.374), (0.079, 0.004, 0.005), head_material, bounds, 0.002)
    add_sphere("Lens.R", eye, "Head", (0.052, -0.242, 1.445), (0.023, 0.006, 0.023), eye_material, bounds, 20, 10, True)
    add_sphere("Lens.L", eye, "Head", (0.158, -0.242, 1.445), (0.023, 0.006, 0.023), eye_material, bounds, 20, 10, True)

    # Secondary seams and rivet rows break every broad remaining armor surface.
    add_rivet_row("ChestLeftSeam", body, "Chest", (-0.235, -0.205, 1.055), (-0.255, -0.205, 1.205), 5, 0.008, body_material, bounds)
    add_rivet_row("ChestRightSeam", body, "Chest", (0.238, -0.205, 1.075), (0.255, -0.205, 1.240), 5, 0.008, body_material, bounds)
    add_box("ChestVerticalSeam", body, "Chest", (-0.185, -0.207, 1.115), (0.006, 0.006, 0.105), body_material, bounds, 0.003)
    add_box("ChestLowerSeam", body, "Chest", (0.030, -0.199, 1.000), (0.170, 0.006, 0.007), body_material, bounds, 0.003)

    for part_name, (part_low, part_high) in bounds.items():
        if part_low.x < -0.37 or part_high.x > 0.37:
            print(f"AUDIT silhouette_extreme part={part_name} x=({part_low.x:.6f},{part_high.x:.6f})")
        if part_low.y < -0.245 or part_high.y > 0.190:
            print(f"AUDIT depth_extreme part={part_name} y=({part_low.y:.6f},{part_high.y:.6f})")
    audit_connections(bounds)
    meshes = (
        join_parts(armor, "CharacterArmor"),
        join_parts(body, "CharacterBody"),
        join_parts(head, "CharacterHead"),
        join_parts(eye, "CharacterEye"),
    )
    rig = make_rig()
    for mesh in meshes:
        attach_mesh(mesh, rig)
    actions, kick_source = make_actions(rig)
    return meshes, rig, actions, kick_source, bounds


def audit_connections(bounds):
    axis_index = {"X": 0, "Y": 1, "Z": 2}
    for first, second, contact_axis, required in CONNECTION_MAP:
        require(first in bounds and second in bounds, f"Connection-map part missing: {first}->{second}")
        low_a, high_a = bounds[first]
        low_b, high_b = bounds[second]
        overlap = Vector((
            min(high_a[i], high_b[i]) - max(low_a[i], low_b[i])
            for i in range(3)
        ))
        require(min(overlap) > 0.0, f"Disconnected solids: {first}->{second} overlap={tuple(round(v, 6) for v in overlap)}")
        axis_overlap = overlap[axis_index[contact_axis]]
        require(axis_overlap + 1e-6 >= required, f"Connection overlap below {required:.3f}m: {first}->{second}/{contact_axis}={axis_overlap:.6f}")


def evaluated_group_bounds(meshes, rig, action, frame, group_name):
    set_action_frame(rig, action, frame)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    points = []
    for obj in meshes:
        group = obj.vertex_groups.get(group_name)
        if group is None:
            continue
        indices = {
            vertex.index for vertex in obj.data.vertices
            if any(item.group == group.index and item.weight > 1e-6 for item in vertex.groups)
        }
        if not indices:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        deformed = evaluated.to_mesh()
        try:
            points.extend(evaluated.matrix_world @ deformed.vertices[index].co for index in indices)
        finally:
            evaluated.to_mesh_clear()
    require(points, f"No evaluated vertices for group: {group_name}")
    return (
        Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points))),
        Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points))),
    )


def pose_euler_component(rig, action, bone_name, frame, index):
    set_action_frame(rig, action, frame)
    return rig.pose.bones[bone_name].rotation_quaternion.to_euler("XYZ")[index]


def angle_degrees(first, second):
    return math.degrees(Vector(first).normalized().angle(Vector(second).normalized()))


def pose_ray(rig, bone_name):
    bone = rig.pose.bones[bone_name]
    return (bone.tail - bone.head).normalized()


def rigid_part_obb(rig, part):
    """Return a rigid-weighted box as an armature-space oriented bounding box."""
    bone_name, bind_center, half_extents = part
    pose_bone = rig.pose.bones[bone_name]
    deform = pose_bone.matrix @ rig.data.bones[bone_name].matrix_local.inverted()
    rotation = deform.to_3x3()
    axes = tuple((rotation @ axis).normalized() for axis in (
        Vector((1.0, 0.0, 0.0)), Vector((0.0, 1.0, 0.0)), Vector((0.0, 0.0, 1.0)),
    ))
    return deform @ bind_center, axes, half_extents


def obb_separating_clearance(first, second):
    """Return the strongest separating-axis gap; positive means definite clearance."""
    center_a, axes_a, extents_a = first
    center_b, axes_b, extents_b = second
    delta = center_b - center_a
    candidates = [*axes_a, *axes_b]
    candidates.extend(axis_a.cross(axis_b) for axis_a in axes_a for axis_b in axes_b)
    best_gap = -float("inf")
    for axis in candidates:
        if axis.length <= 1e-8:
            continue
        axis.normalize()
        radius_a = sum(extents_a[index] * abs(axes_a[index].dot(axis)) for index in range(3))
        radius_b = sum(extents_b[index] * abs(axes_b[index].dot(axis)) for index in range(3))
        best_gap = max(best_gap, abs(delta.dot(axis)) - radius_a - radius_b)
    return best_gap


def audit_knee_mechanics(rig, actions):
    require(set(KNEE_MECHANICS) == {"R", "L"}, f"Knee mechanics records invalid: {set(KNEE_MECHANICS)}")
    tested_poses = 0
    minimum_clearance = float("inf")
    maximum_axis_error = 0.0
    for action in actions:
        if action.name not in {"Run", "Jump", "Land", "Kick"}:
            continue
        for frame in ACTION_KEYS[action.name]:
            set_action_frame(rig, action, frame)
            for side, mechanics in KNEE_MECHANICS.items():
                axle_bone = mechanics["axle_bone"]
                deform = rig.pose.bones[axle_bone].matrix @ rig.data.bones[axle_bone].matrix_local.inverted()
                axle_ray = (deform @ mechanics["axle_end"] - deform @ mechanics["axle_start"]).normalized()
                axis_error = angle_degrees(axle_ray, (1.0, 0.0, 0.0))
                maximum_axis_error = max(maximum_axis_error, axis_error)
                require(axis_error <= 3.0, f"Knee axle axis invalid: {action.name}/{frame}/{side}/{axis_error:.4f}deg")

                lug = rigid_part_obb(rig, mechanics["lug"])
                obstacles = [*mechanics["plates"], mechanics["bridge"]]
                for obstacle in obstacles:
                    clearance = obb_separating_clearance(rigid_part_obb(rig, obstacle), lug)
                    minimum_clearance = min(minimum_clearance, clearance)
                    require(clearance >= 0.003, f"Knee clevis clearance invalid: {action.name}/{frame}/{side}/{clearance:.6f}m")
                tested_poses += 1

        # The child lug must be narrowly captured laterally, not floating between decorative plates.
        for side, mechanics in KNEE_MECHANICS.items():
            lug_center = mechanics["lug"][1]
            lug_extent = mechanics["lug"][2].x
            plate_centers = sorted(item[1].x for item in mechanics["plates"])
            plate_extent = mechanics["plates"][0][2].x
            left_gap = (lug_center.x - lug_extent) - (plate_centers[0] + plate_extent)
            right_gap = (plate_centers[1] - plate_extent) - (lug_center.x + lug_extent)
            require(0.003 <= left_gap <= 0.015 and 0.003 <= right_gap <= 0.015, f"Knee lug capture invalid: {side}/{left_gap:.6f}/{right_gap:.6f}")

    print(f"AUDIT knee_mechanics axle=+X max_error={maximum_axis_error:.4f}deg poses={tested_poses} minimum_clearance={minimum_clearance:.6f}m clevis=captured")


def audit_meshes(meshes, rig):
    require(tuple(obj.name for obj in meshes) == MESH_NAMES, f"Export mesh names/order invalid: {tuple(obj.name for obj in meshes)}")
    all_points = []
    total_vertices = 0
    total_triangles = 0
    for obj in meshes:
        require(obj.type == "MESH", f"Export object is not a mesh: {obj.name}")
        require(obj.parent == rig, f"Mesh parent invalid: {obj.name}")
        require(obj.location.length <= 1e-7, f"Mesh origin is not rig root: {obj.name}/{tuple(obj.location)}")
        require(obj.rotation_euler.to_matrix().to_quaternion().angle <= 1e-7, f"Mesh rotation unapplied: {obj.name}")
        require((obj.scale - Vector((1, 1, 1))).length <= 1e-7, f"Mesh scale unapplied: {obj.name}")
        require(len(obj.data.materials) == 1 and obj.data.materials[0] is not None, f"Material-slot contract invalid: {obj.name}")
        require(len(obj.data.uv_layers) == 1 and obj.data.uv_layers[0].name == "UVMap", f"UVMap contract invalid: {obj.name}")
        modifier = next((item for item in obj.modifiers if item.type == "ARMATURE"), None)
        require(modifier is not None and modifier.object == rig, f"Armature modifier invalid: {obj.name}")
        obj.data.calc_loop_triangles()
        total_vertices += len(obj.data.vertices)
        total_triangles += len(obj.data.loop_triangles)
        edge_faces = {edge.key: 0 for edge in obj.data.edges}
        signed_volume = 0.0
        for polygon in obj.data.polygons:
            require(polygon.area > 1e-10 and all(math.isfinite(v) for v in polygon.normal), f"Invalid face: {obj.name}/{polygon.index}")
            for key in polygon.edge_keys:
                edge_faces[key] += 1
        require(all(count == 2 for count in edge_faces.values()), f"Closed-solid manifold audit failed: {obj.name}")
        for edge in obj.data.edges:
            first, second = (obj.data.vertices[index].co for index in edge.vertices)
            require((first - second).length > 1e-7, f"Zero-length edge: {obj.name}/{edge.index}")
        for triangle in obj.data.loop_triangles:
            first, second, third = (obj.data.vertices[index].co for index in triangle.vertices)
            signed_volume += first.dot(second.cross(third)) / 6.0
        require(signed_volume > 1e-7, f"Normals/winding volume invalid: {obj.name}/{signed_volume}")
        for vertex in obj.data.vertices:
            point = obj.matrix_world @ vertex.co
            require(all(math.isfinite(value) for value in point), f"Non-finite vertex: {obj.name}/{vertex.index}")
            all_points.append(point)
            weights = [item.weight for item in vertex.groups if item.weight > 1e-7]
            require(1 <= len(weights) <= 4 and abs(sum(weights) - 1.0) <= 1e-5, f"Weight audit failed: {obj.name}/{vertex.index}")
    low = Vector((min(p.x for p in all_points), min(p.y for p in all_points), min(p.z for p in all_points)))
    high = Vector((max(p.x for p in all_points), max(p.y for p in all_points), max(p.z for p in all_points)))
    dimensions = high - low
    require(abs(low.z) <= 1e-5, f"Feet must rest at z=0: {low.z:.7f}")
    require(
        all(dimensions[index] <= TARGET_BOUNDS[index] + 1e-5 for index in range(3)),
        f"Character exceeds bounds: low={tuple(low)} high={tuple(high)} dimensions={tuple(dimensions)}",
    )
    print(f"AUDIT geometry meshes=4 closed_connected_solids=true UVMap=1 slots=1 transforms=applied origin=rig_root")
    print(f"AUDIT bounds min={tuple(round(v, 6) for v in low)} max={tuple(round(v, 6) for v in high)} dimensions={tuple(round(v, 6) for v in dimensions)}")
    print(f"AUDIT counts vertices={total_vertices} triangles={total_triangles} report_only=true")
    return low, high


def audit_rig(rig):
    require(rig.name == "CharacterRig" and rig.data.name == "CharacterRig", "Armature name invalid")
    actual_bones = tuple(bone.name for bone in rig.data.bones)
    require(len(actual_bones) == len(BONE_NAMES) and set(actual_bones) == set(BONE_NAMES), f"Exact 18-bone contract invalid: {actual_bones}")
    parents = {
        "Root": None, "Pelvis": "Root", "Spine": "Pelvis", "Chest": "Spine", "Neck": "Chest", "Head": "Neck",
        "Thigh.R": "Pelvis", "Shin.R": "Thigh.R", "Foot.R": "Shin.R",
        "Thigh.L": "Pelvis", "Shin.L": "Thigh.L", "Foot.L": "Shin.L",
        "UpperArm.R": "Chest", "Forearm.R": "UpperArm.R", "Hand.R": "Forearm.R",
        "UpperArm.L": "Chest", "Forearm.L": "UpperArm.L", "Hand.L": "Forearm.L",
    }
    for bone_name, parent_name in parents.items():
        parent = rig.data.bones[bone_name].parent
        require((parent.name if parent else None) == parent_name, f"Bone hierarchy invalid: {bone_name}")
    require(all(bone.length > 1e-4 for bone in rig.data.bones), "Zero-length bind segment")
    print("AUDIT rig=CharacterRig bones=18 hierarchy=exact Hand.R=present bind_segments=valid")


def audit_names_and_systems():
    named_blocks = [*bpy.data.objects, *bpy.data.meshes, *bpy.data.materials, *bpy.data.armatures, *bpy.data.actions]
    for block in named_blocks:
        lowered = block.name.lower()
        require(not any(term in lowered for term in FORBIDDEN_TERMS), f"Forbidden propulsion term in datablock: {block.name}")
    require(len(bpy.data.particles) == 0, "Particle system datablock forbidden")
    for material in bpy.data.materials:
        require(material.use_nodes, f"Material nodes missing: {material.name}")
        for node in material.node_tree.nodes:
            for socket in node.inputs:
                if "Emission Strength" in socket.name and hasattr(socket, "default_value"):
                    require(float(socket.default_value) == 0.0, f"Emission forbidden: {material.name}")
    print("AUDIT forbidden_terms=none particles=none emission=none")


def audit_actions(meshes, rig, actions, kick_source):
    require(bpy.context.scene.render.fps == 30, "Scene frame rate must be 30fps")
    require(tuple(action.name for action in actions) == ACTION_NAMES, "Declared action names/order invalid")
    require({action.name for action in bpy.data.actions} == set(ACTION_NAMES), "Unexpected action datablock")
    for action in actions:
        start, end = (int(round(value)) for value in action.frame_range)
        require((start, end) == ACTION_RANGES[action.name], f"Action range invalid: {action.name}/{start}..{end}")
        require(int(action.get("frame_rate", 0)) == 30 and bool(action.get("root_locked", False)), f"Action metadata invalid: {action.name}")
        require(bool(action.get("loop_intent", False)) == (action.name in LOOP_ACTIONS), f"Loop metadata invalid: {action.name}")
        expected_frames = ACTION_KEYS[action.name]
        for curve in action.fcurves:
            require('pose.bones["Root"]' not in curve.data_path, f"Root curve forbidden: {action.name}/{curve.data_path}")
            require('pose.bones["Pelvis"].location' not in curve.data_path, f"Pelvis translation forbidden: {action.name}")
            actual_frames = tuple(int(round(point.co[0])) for point in curve.keyframe_points)
            require(actual_frames == expected_frames, f"Key contract invalid: {action.name}/{curve.data_path}/{actual_frames}")
            for point in curve.keyframe_points:
                point.interpolation = "BEZIER"
                point.handle_left_type = "AUTO_CLAMPED"
                point.handle_right_type = "AUTO_CLAMPED"
            if action.name in LOOP_ACTIONS:
                require(abs(curve.evaluate(start) - curve.evaluate(end)) <= 1e-7, f"Loop endpoint mismatch: {action.name}/{curve.data_path}")

    idle = bpy.data.actions["Idle"]
    idle_max = 0.0
    for frame in ACTION_KEYS["Idle"]:
        set_action_frame(rig, idle, frame)
        idle_max = max(idle_max, *(rig.pose.bones[bone_name].rotation_quaternion.angle for bone_name in BONE_NAMES[2:]))
    require(idle_max <= math.radians(2.0) + 1e-6, f"Idle exceeds subtle motion cap: {math.degrees(idle_max):.3f}")

    run = bpy.data.actions["Run"]
    require(abs(pose_euler_component(rig, run, "Thigh.R", 1, 0) - math.radians(-35)) <= 1e-6, "Run thigh amplitude invalid")
    require(abs(pose_euler_component(rig, run, "Shin.R", 1, 0) - math.radians(18)) <= 1e-6, "Run contact shin invalid")
    require(abs(pose_euler_component(rig, run, "Shin.L", 1, 0) - math.radians(48)) <= 1e-6, "Run swing shin invalid")
    require(abs(pose_euler_component(rig, run, "Foot.R", 1, 0) - math.radians(-10)) <= 1e-6, "Run foot counter invalid")
    require(abs(pose_euler_component(rig, run, "UpperArm.R", 1, 0) - math.radians(24)) <= 1e-6, "Run arm amplitude invalid")
    require(abs(pose_euler_component(rig, run, "Chest", 1, 2) - math.radians(3)) <= 1e-6, "Run chest twist invalid")
    contact_lows = []
    passing_lows = []
    for frame in (1, 11):
        for bone_name in ("Foot.R", "Foot.L"):
            contact_lows.append(evaluated_group_bounds(meshes, rig, run, frame, bone_name)[0].z)
    for frame in (6, 16):
        for bone_name in ("Foot.R", "Foot.L"):
            passing_lows.append(evaluated_group_bounds(meshes, rig, run, frame, bone_name)[0].z)
    require(max(passing_lows) >= min(contact_lows) + 0.015, f"Run boot clearance missing: contact={contact_lows} passing={passing_lows}")

    jump = bpy.data.actions["Jump"]
    require(abs(pose_euler_component(rig, jump, "Thigh.R", 4, 0) - math.radians(-28)) <= 1e-6, "Jump compression thigh invalid")
    require(abs(pose_euler_component(rig, jump, "Shin.R", 4, 0) - math.radians(50)) <= 1e-6, "Jump compression shin invalid")
    require(abs(math.degrees(pose_euler_component(rig, jump, "Thigh.R", 7, 0))) <= 8.001, "Jump extension exceeds cap")
    require(abs(pose_euler_component(rig, jump, "Thigh.R", 10, 0) - math.radians(-20)) <= 1e-6, "Jump tuck thigh invalid")
    require(abs(pose_euler_component(rig, jump, "Shin.R", 10, 0) - math.radians(38)) <= 1e-6, "Jump tuck shin invalid")

    fall = bpy.data.actions["Fall"]
    for bone_name, maximum_variance in (("Thigh.R", 4.0), ("Shin.R", 4.0), ("UpperArm.R", 4.0), ("Chest", 4.0)):
        values = [math.degrees(pose_euler_component(rig, fall, bone_name, frame, 0)) for frame in ACTION_KEYS["Fall"]]
        require(max(values) - min(values) <= maximum_variance + 1e-4, f"Fall pose variance invalid: {bone_name}/{values}")

    land = bpy.data.actions["Land"]
    require(abs(pose_euler_component(rig, land, "Thigh.R", 3, 0) - math.radians(-34)) <= 1e-6, "Land thigh absorption invalid")
    require(abs(pose_euler_component(rig, land, "Shin.R", 3, 0) - math.radians(58)) <= 1e-6, "Land shin absorption invalid")
    require(abs(pose_euler_component(rig, land, "Chest", 3, 0) - math.radians(-9)) <= 1e-6, "Land chest absorption invalid")
    set_action_frame(rig, land, 10)
    require(all(rig.pose.bones[name].rotation_quaternion.rotation_difference(Quaternion((1, 0, 0, 0))).angle <= 1e-7 for name in BONE_NAMES[2:]), "Land frame 10 is not exact neutral")

    audit_kick(rig, bpy.data.actions["Kick"], kick_source)
    print("AUDIT actions=Idle[1,30] Run[1,20] Jump[1,12] Fall[1,15] Land[1,10] Kick[1,11] fps=30")
    print("AUDIT loops=Idle,Run root_translation=none pelvis_translation=none root_rotation=none land_end=neutral")


def audit_kick(rig, kick, source):
    paths = {curve.data_path for curve in kick.fcurves}
    bound_bones = {
        path.split('pose.bones["', 1)[1].split('"]', 1)[0]
        for path in paths if path.startswith('pose.bones["')
    }
    require(bound_bones == set(LEG_BONES), f"Kick bindings must be exactly six leg bones: {bound_bones}")
    require(len(kick.fcurves) == 24 and all(path.endswith(".rotation_quaternion") for path in paths), "Kick must contain quaternion rotation curves only")
    neutral = Quaternion((1.0, 0.0, 0.0, 0.0))
    for frame in (1, 11):
        set_action_frame(rig, kick, frame)
        for bone_name in LEG_BONES:
            require(rig.pose.bones[bone_name].rotation_quaternion.rotation_difference(neutral).angle <= 1e-6, f"Kick frame {frame} not exact neutral: {bone_name}")

    expected_rays = {
        3: {
            "Thigh.R": (0.0, 0.342, -0.940), "Shin.R": (0.0, -0.766, 0.643),
            "Thigh.L": (0.0, -0.174, -0.985), "Shin.L": (0.0, 0.0, -1.0),
        },
        4: {
            "Thigh.R": (0.0, -1.0, 0.0), "Shin.R": (0.0, -1.0, 0.0), "Foot.R": (0.0, -1.0, 0.0),
            "Thigh.L": (0.0, 0.342, -0.940), "Shin.L": (0.0, -0.766, 0.643),
        },
    }
    bind_lengths = {name: rig.data.bones[name].length for name in LEG_BONES}
    for frame, rays in expected_rays.items():
        set_action_frame(rig, kick, frame)
        for bone_name in LEG_BONES:
            pose_length = (rig.pose.bones[bone_name].tail - rig.pose.bones[bone_name].head).length
            require(abs(pose_length - bind_lengths[bone_name]) <= 0.0001, f"Kick bind length drift: {frame}/{bone_name}")
        for bone_name, expected in rays.items():
            require(angle_degrees(pose_ray(rig, bone_name), expected) <= 0.25, f"Kick ray mismatch: {frame}/{bone_name}/{tuple(pose_ray(rig, bone_name))}")

    set_action_frame(rig, kick, 4)
    right_rays = [pose_ray(rig, name) for name in ("Thigh.R", "Shin.R", "Foot.R")]
    require(max(angle_degrees(ray, (0, -1, 0)) for ray in right_rays) <= 5.0, "Kick forward direction exceeds 5 degrees")
    require(max(angle_degrees(right_rays[index], right_rays[index + 1]) for index in (0, 1)) <= 5.0, "Kick right chain is not collinear")
    hip = rig.pose.bones["Thigh.R"].head.copy()
    ankle = rig.pose.bones["Shin.R"].tail.copy()
    toe = rig.pose.bones["Foot.R"].tail.copy()
    require(abs(ankle.z - hip.z) <= 0.05, f"Kick ankle elevation invalid: {ankle.z - hip.z:.6f}")
    toe_extension = (toe - hip).dot(Vector((0, -1, 0)))
    require(toe_extension >= 0.55, f"Kick toe extension invalid: {toe_extension:.6f}")
    left_bend = angle_degrees(pose_ray(rig, "Thigh.L"), pose_ray(rig, "Shin.L"))
    left_ankle = rig.pose.bones["Shin.L"].tail.copy()
    pelvis = rig.pose.bones["Pelvis"].head.copy()
    require(left_bend >= 145.0, f"Kick support-leg bend invalid: {left_bend:.4f}")
    require((left_ankle - pelvis).length <= 0.35, f"Kick support ankle too far from pelvis: {(left_ankle - pelvis).length:.6f}")

    set_action_frame(rig, kick, 7)
    for bone_name in LEG_BONES:
        actual = rig.pose.bones[bone_name].rotation_quaternion.normalized()
        expected = source["recovery"][bone_name]
        require(actual.rotation_difference(expected).angle <= 1e-5, f"Kick recovery is not shortest slerp 0.5: {bone_name}")
    print(f"AUDIT kick bindings=6 keys=1,3,4,7,11 rays=normalized lengths<=0.0001m right_collinear<=5deg forward=-Y toe={toe_extension:.6f}m left_bend={left_bend:.3f}deg")


def audit(meshes, rig, actions, kick_source):
    audit_names_and_systems()
    audit_rig(rig)
    low, high = audit_meshes(meshes, rig)
    audit_actions(meshes, rig, actions, kick_source)
    audit_knee_mechanics(rig, actions)
    print(f"AUDIT connections={len(CONNECTION_MAP)} minimum_overlap={MIN_OVERLAP:.3f}m")
    return low, high


def require_preview(path, label):
    require(os.path.isfile(path) and os.path.getsize(path) > 0, f"Preview missing or empty: {label}/{path}")


def save_contact_sheet(scene, rig, camera, target, action_name):
    frame_width = 320
    frame_height = 320
    scene.render.resolution_x = frame_width
    scene.render.resolution_y = frame_height
    camera.location = (3.2, -0.15, 0.95)
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    frame_paths = []
    for index, frame in enumerate(ACTION_KEYS[action_name]):
        rig.animation_data.action = bpy.data.actions[action_name]
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        path = os.path.join(PREVIEW_DIR, f"_{action_name.lower()}-{index}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        require_preview(path, f"{action_name}/{frame}")
        frame_paths.append(path)
    sheet_width = frame_width * len(frame_paths)
    sheet = bpy.data.images.new(f"{action_name}Contact", width=sheet_width, height=frame_height, alpha=True)
    pixels = [0.0] * (sheet_width * frame_height * 4)
    for index, path in enumerate(frame_paths):
        source = bpy.data.images.load(path, check_existing=False)
        source_pixels = [0.0] * (frame_width * frame_height * 4)
        source.pixels.foreach_get(source_pixels)
        for row in range(frame_height):
            source_start = row * frame_width * 4
            target_start = (row * sheet_width + index * frame_width) * 4
            pixels[target_start:target_start + frame_width * 4] = source_pixels[source_start:source_start + frame_width * 4]
        bpy.data.images.remove(source)
    sheet.pixels.foreach_set(pixels)
    path = os.path.join(PREVIEW_DIR, f"contact-{action_name.lower()}.png")
    sheet.filepath_raw = path
    sheet.file_format = "PNG"
    sheet.save()
    require_preview(path, f"{action_name} contact sheet")
    bpy.data.images.remove(sheet)
    for frame_path in frame_paths:
        os.remove(frame_path)


def render_previews(rig, low, high):
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    for filename in os.listdir(PREVIEW_DIR):
        if filename.lower().endswith(".png"):
            os.remove(os.path.join(PREVIEW_DIR, filename))
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new("CyborgPreviewWorld")
    scene.world.color = (0.025, 0.03, 0.038)
    rig.animation_data.action = bpy.data.actions["Idle"]
    scene.frame_set(1)
    bpy.context.view_layer.update()

    bpy.ops.mesh.primitive_plane_add(size=8.0, location=(0, 0, -0.006))
    floor = bpy.context.object
    floor.name = "CyborgPreviewFloor"
    floor_material = make_material("CyborgPreviewFloorMaterial", (0.075, 0.085, 0.095), 0.15, 0.55)
    floor.data.materials.append(floor_material)
    bpy.ops.object.light_add(type="AREA", location=(2.2, -3.0, 3.8))
    key = bpy.context.object
    key.name = "CyborgPreviewKey"
    key.data.energy = 1000
    key.data.shape = "DISK"
    key.data.size = 3.2
    key.rotation_euler = (math.radians(24), 0, math.radians(36))
    bpy.ops.object.light_add(type="AREA", location=(-2.4, -1.2, 2.1))
    fill = bpy.context.object
    fill.name = "CyborgPreviewFill"
    fill.data.energy = 650
    fill.data.size = 2.5
    bpy.ops.object.light_add(type="AREA", location=(0.5, 2.5, 2.8))
    rim = bpy.context.object
    rim.name = "CyborgPreviewRim"
    rim.data.energy = 850
    rim.data.size = 2.0
    bpy.ops.object.camera_add()
    camera = bpy.context.object
    camera.name = "CyborgPreviewCamera"
    camera.data.lens = 58
    scene.camera = camera
    target = Vector((0.0, 0.0, (low.z + high.z) * 0.5))
    views = {
        "front": (0.0, -3.2, 0.92), "rear": (0.0, 3.2, 0.92),
        "left": (-3.2, 0.0, 0.92), "right": (3.2, 0.0, 0.92),
        "top": (0.0, -0.08, 3.8), "three-quarter": (2.35, -2.65, 1.55),
    }
    for name, location in views.items():
        rig.animation_data.action = bpy.data.actions["Idle"]
        scene.frame_set(1)
        camera.location = location
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.resolution_x = 768
        scene.render.resolution_y = 768
        scene.render.filepath = os.path.join(PREVIEW_DIR, name + ".png")
        bpy.ops.render.render(write_still=True)
        require_preview(scene.render.filepath, name)
    for action_name in CONTACT_SHEET_ACTIONS:
        save_contact_sheet(scene, rig, camera, target, action_name)
    rig.animation_data.action = bpy.data.actions["Idle"]
    scene.frame_set(1)
    bpy.context.view_layer.update()
    for obj in (camera, key, fill, rim, floor):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.materials.remove(floor_material)
    expected = {f"{name}.png" for name in views} | {f"contact-{name.lower()}.png" for name in CONTACT_SHEET_ACTIONS}
    actual = {name for name in os.listdir(PREVIEW_DIR) if name.lower().endswith(".png")}
    require(actual == expected, f"Preview inventory invalid: expected={sorted(expected)} actual={sorted(actual)}")
    print(f"AUDIT previews={len(actual)} static=6 contacts=5 directory={PREVIEW_DIR}")


def save_and_export(meshes, rig):
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)
    rig.animation_data.action = bpy.data.actions["Idle"]
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in (*meshes, rig):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    # Blender's all-actions path force-keeps one constant key on every bone. The
    # official exporter already knows how to omit those empty bindings; use that
    # path so Kick round-trips with only its six authored leg-bone bindings.
    from io_scene_fbx import export_fbx_bin
    original_animation_export = export_fbx_bin.fbx_animations_do

    def export_without_empty_bindings(scene_data, ref_id, f_start, f_end, start_zero, objects=None, force_keep=False):
        return original_animation_export(scene_data, ref_id, f_start, f_end, start_zero, objects, force_keep=False)

    export_fbx_bin.fbx_animations_do = export_without_empty_bindings
    try:
        bpy.ops.export_scene.fbx(
            filepath=FBX_PATH,
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
    require(os.path.isfile(BLEND_PATH) and os.path.getsize(BLEND_PATH) > 0, "Blend output missing or empty")
    require(os.path.isfile(FBX_PATH) and os.path.getsize(FBX_PATH) > 0, "FBX output missing or empty")
    print(f"OUTPUT blend={BLEND_PATH} bytes={os.path.getsize(BLEND_PATH)}")
    print(f"OUTPUT fbx={FBX_PATH} bytes={os.path.getsize(FBX_PATH)}")


def roundtrip_fbx_audit():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=FBX_PATH, use_anim=True)
    meshes = tuple(sorted(obj.name for obj in bpy.context.scene.objects if obj.type == "MESH"))
    require(meshes == tuple(sorted(MESH_NAMES)), f"Round-trip mesh names invalid: {meshes}")
    rigs = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    require(len(rigs) == 1 and rigs[0].name == "CharacterRig", f"Round-trip armature invalid: {[obj.name for obj in rigs]}")
    kick_actions = [action for action in bpy.data.actions if action.name == "Kick" or action.name.endswith("|Kick")]
    require(len(kick_actions) == 1, f"Round-trip Kick action missing/ambiguous: {[action.name for action in bpy.data.actions]}")
    kick = kick_actions[0]
    bound_bones = set()
    for curve in kick.fcurves:
        if curve.data_path.startswith('pose.bones["'):
            bound_bones.add(curve.data_path.split('pose.bones["', 1)[1].split('"]', 1)[0])
    require(bound_bones == set(LEG_BONES), f"Round-trip Kick bindings invalid: {sorted(bound_bones)}")
    require(not any('pose.bones["Root"]' in curve.data_path for curve in kick.fcurves), "Round-trip Kick gained Root binding")
    print(f"ROUNDTRIP factory_empty=true mesh_names=exact rig=CharacterRig Kick={kick.name} bindings={','.join(sorted(bound_bones))} count=6")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.scene.render.fps = 30
    meshes, rig, actions, kick_source, _ = build_character()
    low, high = audit(meshes, rig, actions, kick_source)
    render_previews(rig, low, high)
    save_and_export(meshes, rig)
    roundtrip_fbx_audit()
    print("RESULT LowPolyCharacter industrial cyborg generation PASSED")


if __name__ == "__main__":
    main()
