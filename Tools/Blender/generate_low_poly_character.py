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
    ("HeadPod", "AntennaStem", "Z", MIN_OVERLAP),
    ("AntennaStem", "AntennaBall", "Z", MIN_OVERLAP),
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


def finish_part(obj, name, collection, bone_name, material, bounds):
    obj.name = name
    obj.data.name = name + "Mesh"
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    obj.data.materials.clear()
    obj.data.materials.append(material)
    group = obj.vertex_groups.new(name=bone_name)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    require(len(obj.data.uv_layers) == 1, f"Primitive UV contract failed: {name}")
    obj.data.uv_layers[0].name = "UVMap"
    bounds[name] = world_bounds(obj)
    collection.append(obj)
    return obj


def add_box(name, collection, bone_name, location, half_extents, material, bounds, bevel=0.012):
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=location)
    obj = bpy.context.object
    obj.scale = half_extents
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        modifier = obj.modifiers.new("EdgeChamfer", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return finish_part(obj, name, collection, bone_name, material, bounds)


def add_sphere(name, collection, bone_name, location, radii, material, bounds, segments=12, rings=8):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=location)
    obj = bpy.context.object
    obj.scale = radii
    return finish_part(obj, name, collection, bone_name, material, bounds)


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
    body_material = make_material("CyborgDarkMetal", (0.028, 0.035, 0.032), 0.88, 0.28)
    armor_material = make_material("CyborgOxideArmor", (0.060, 0.075, 0.028), 0.66, 0.46)
    head_material = make_material("CyborgHeadMetal", (0.120, 0.045, 0.015), 0.78, 0.38)
    eye_material = make_material("CyborgLensGlass", (0.48, 0.012, 0.004), 0.52, 0.13)
    body, armor, head, eye, bounds = [], [], [], [], {}

    # The dark core is mostly hidden by a deep barrel shell, leaving mechanisms visible at every limb gap.
    add_sphere("PelvisCore", body, "Pelvis", (0.0, 0.02, 0.83), (0.22, 0.145, 0.19), body_material, bounds, 14, 8)
    add_sphere("TorsoCore", body, "Spine", (0.01, 0.01, 1.18), (0.285, 0.19, 0.295), body_material, bounds, 18, 10)
    add_cylinder("NeckCore", body, "Neck", (0.075, -0.055, 1.35), (0.10, -0.15, 1.45), 0.055, body_material, bounds, 10)

    for side, x in (("R", -0.155), ("L", 0.155)):
        outward = -1.0 if side == "R" else 1.0
        add_cylinder(f"HipDisc.{side}", body, f"Thigh.{side}", (x, -0.105, 0.84), (x, 0.085, 0.84), 0.090, body_material, bounds, 14)
        add_cylinder(f"HipHub.{side}", head, f"Thigh.{side}", (x, -0.122, 0.84), (x, -0.094, 0.84), 0.052, head_material, bounds, 12)
        add_cylinder(f"Thigh.{side}", body, f"Thigh.{side}", (x + outward * 0.031, -0.020, 0.84), (x + outward * 0.031, -0.020, 0.50), 0.034, body_material, bounds, 10)
        add_cylinder(f"ThighPiston.{side}", body, f"Thigh.{side}", (x - outward * 0.026, 0.025, 0.82), (x - outward * 0.026, 0.025, 0.53), 0.024, body_material, bounds, 10)
        add_cylinder(f"Knee.{side}", body, f"Shin.{side}", (x, -0.112, 0.51), (x, 0.074, 0.51), 0.090, body_material, bounds, 14)
        add_cylinder(f"KneeHub.{side}", head, f"Shin.{side}", (x, -0.128, 0.51), (x, -0.100, 0.51), 0.053, head_material, bounds, 12)
        add_cylinder(f"Shin.{side}", body, f"Shin.{side}", (x + outward * 0.030, -0.020, 0.49), (x + outward * 0.030, -0.020, 0.135), 0.031, body_material, bounds, 10)
        add_cylinder(f"ShinPiston.{side}", body, f"Shin.{side}", (x - outward * 0.026, 0.025, 0.47), (x - outward * 0.026, 0.025, 0.16), 0.022, body_material, bounds, 10)
        add_cylinder(f"AnkleDisc.{side}", body, f"Foot.{side}", (x, -0.084, 0.15), (x, 0.060, 0.15), 0.066, body_material, bounds, 12)
        add_box(f"Boot.{side}", body, f"Foot.{side}", (x, -0.045, 0.075), (0.112, 0.155, 0.075), body_material, bounds, 0.018)
        add_box(f"BootHeel.{side}", body, f"Foot.{side}", (x, 0.112, 0.076), (0.094, 0.044, 0.058), body_material, bounds, 0.012)

    for side, x0, x1 in (("R", -0.235, -0.305), ("L", 0.235, 0.305)):
        outward = -1.0 if side == "R" else 1.0
        hand_x = -0.300 if side == "R" else 0.300
        add_cylinder(f"UpperArm.{side}", body, f"UpperArm.{side}", (x0 + outward * 0.018, -0.026, 1.35), (x1 + outward * 0.018, -0.026, 1.03), 0.034, body_material, bounds, 10)
        add_cylinder(f"UpperArmPiston.{side}", body, f"UpperArm.{side}", (x0 - outward * 0.022, 0.025, 1.33), (x1 - outward * 0.022, 0.025, 1.07), 0.023, body_material, bounds, 10)
        add_cylinder(f"Elbow.{side}", body, f"Forearm.{side}", (x1, -0.095, 1.05), (x1, 0.070, 1.05), 0.067, body_material, bounds, 14)
        add_cylinder(f"ElbowHub.{side}", head, f"Forearm.{side}", (x1, -0.111, 1.05), (x1, -0.084, 1.05), 0.041, head_material, bounds, 12)
        add_cylinder(f"Forearm.{side}", body, f"Forearm.{side}", (x1 + outward * 0.020, -0.022, 1.04), (hand_x + outward * 0.020, -0.040, 0.79), 0.032, body_material, bounds, 10)
        add_cylinder(f"ForearmPiston.{side}", body, f"Forearm.{side}", (x1 - outward * 0.020, 0.025, 1.02), (hand_x - outward * 0.020, 0.005, 0.81), 0.022, body_material, bounds, 10)
        add_cylinder(f"WristDisc.{side}", body, f"Hand.{side}", (hand_x, -0.078, 0.81), (hand_x, 0.050, 0.81), 0.057, body_material, bounds, 12)
        add_box(f"Fist.{side}", body, f"Hand.{side}", (hand_x, -0.045, 0.745), (0.074, 0.076, 0.082), body_material, bounds, 0.016)
        for finger_index in range(3):
            finger_x = hand_x + (finger_index - 1) * 0.045
            add_box(f"Knuckle{finger_index}.{side}", head, f"Hand.{side}", (finger_x, -0.132, 0.775), (0.019, 0.022, 0.027), head_material, bounds, 0.006)
            add_box(f"Finger{finger_index}.{side}", head, f"Hand.{side}", (finger_x, -0.129, 0.727), (0.019, 0.020, 0.018), head_material, bounds, 0.005)

    # A large rounded front shell and broad asymmetric cowl create the concept's dominant silhouette.
    add_profile_plate(
        "ChestPlate", armor, "Chest",
        ((-0.245, 0.985), (0.145, 0.965), (0.255, 1.035), (0.285, 1.255),
         (0.235, 1.430), (0.100, 1.475), (-0.185, 1.445), (-0.280, 1.300), (-0.290, 1.115)),
        -0.188, 0.027, armor_material, bounds, 0.014,
    )
    add_profile_plate(
        "ChestRib", head, "Chest",
        ((-0.125, 1.035), (0.155, 1.025), (0.195, 1.080), (0.175, 1.225), (-0.145, 1.215), (-0.165, 1.095)),
        -0.224, 0.010, head_material, bounds, 0.006,
    )
    add_sphere("WaistPlate", armor, "Pelvis", (-0.015, -0.115, 0.89), (0.225, 0.060, 0.105), armor_material, bounds, 14, 8)
    add_box("BackHousing", armor, "Chest", (0.015, 0.145, 1.23), (0.225, 0.045, 0.205), armor_material, bounds, 0.025)

    add_cylinder("RearTank", head, "Chest", (0.12, 0.120, 1.34), (0.325, 0.120, 1.34), 0.075, head_material, bounds, 14)
    add_sphere("RearTankCapInner", head, "Chest", (0.12, 0.120, 1.34), (0.040, 0.077, 0.077), head_material, bounds, 12, 8)
    add_sphere("RearTankCapOuter", head, "Chest", (0.337, 0.120, 1.34), (0.037, 0.077, 0.077), head_material, bounds, 12, 8)
    add_cylinder("RearTankBandInner", body, "Chest", (0.175, 0.120, 1.34), (0.195, 0.120, 1.34), 0.080, body_material, bounds, 14)
    add_cylinder("RearTankBandOuter", body, "Chest", (0.270, 0.120, 1.34), (0.290, 0.120, 1.34), 0.080, body_material, bounds, 14)

    add_cylinder("ExhaustLower", head, "Chest", (0.155, 0.120, 1.395), (0.065, 0.120, 1.515), 0.032, head_material, bounds, 10)
    add_sphere("ExhaustElbow", head, "Chest", (0.065, 0.120, 1.515), (0.043, 0.043, 0.043), head_material, bounds, 10, 6)
    add_cylinder("ExhaustUpper", head, "Chest", (0.065, 0.120, 1.515), (-0.015, 0.120, 1.620), 0.030, head_material, bounds, 10)
    add_sphere("ExhaustUpperBend", head, "Chest", (-0.015, 0.120, 1.620), (0.040, 0.040, 0.040), head_material, bounds, 10, 6)
    add_cylinder("ExhaustCollar", body, "Chest", (-0.015, 0.120, 1.620), (-0.070, 0.120, 1.690), 0.036, body_material, bounds, 10)

    add_profile_plate(
        "Cowl.R", armor, "Chest",
        ((-0.360, 1.250), (-0.065, 1.235), (0.020, 1.390), (-0.060, 1.560), (-0.285, 1.535), (-0.355, 1.420)),
        -0.115, 0.085, armor_material, bounds, 0.012,
    )
    add_cone("CowlSpikeRear", armor, "Chest", (-0.310, -0.120, 1.455), (-0.350, -0.115, 1.555), 0.026, armor_material, bounds, 10)
    add_cone("CowlSpikeFront", armor, "Chest", (-0.235, -0.145, 1.505), (-0.270, -0.150, 1.650), 0.026, armor_material, bounds, 10)
    add_cone("CowlSpikeCrown", armor, "Chest", (-0.135, -0.125, 1.535), (-0.145, -0.125, 1.690), 0.025, armor_material, bounds, 10)
    add_sphere("Cowl.L", head, "UpperArm.L", (0.295, -0.005, 1.345), (0.075, 0.105, 0.125), head_material, bounds, 12, 8)

    for side, x in (("R", -0.16), ("L", 0.16)):
        add_cylinder(f"KneePlate.{side}", armor, f"Shin.{side}", (x, -0.134, 0.51), (x, -0.102, 0.51), 0.075, armor_material, bounds, 12)
        add_box(f"ShinPlate.{side}", armor, f"Shin.{side}", (x, -0.070, 0.305), (0.070, 0.027, 0.115), armor_material, bounds, 0.012)
        add_box(f"BootSole.{side}", head, f"Foot.{side}", (x, -0.050, 0.018), (0.118, 0.160, 0.018), head_material, bounds, 0.006)
        add_box(f"BootToePlate.{side}", armor, f"Foot.{side}", (x, -0.203, 0.080), (0.103, 0.022, 0.050), armor_material, bounds, 0.010)
        hand_x = -0.300 if side == "R" else 0.300
        plate_inner = hand_x + (0.060 if side == "R" else -0.060)
        plate_outer = hand_x + (-0.055 if side == "R" else 0.055)
        add_profile_plate(
            f"ForearmPlate.{side}", armor, f"Forearm.{side}",
            ((plate_inner, 0.820), (plate_outer, 0.835), (plate_outer, 1.010), (plate_inner, 1.035)),
            -0.078, 0.032, armor_material, bounds, 0.010,
        )
        add_box(f"FistPlate.{side}", armor, f"Hand.{side}", (hand_x, -0.116, 0.750), (0.072, 0.017, 0.070), armor_material, bounds, 0.008)

    # The face is a recessed camera module cut into the upper shell, never a head perched on top.
    add_box("HeadPod", body, "Head", (0.105, -0.185, 1.420), (0.135, 0.048, 0.090), body_material, bounds, 0.018)
    add_box("Brow", head, "Head", (0.105, -0.226, 1.488), (0.125, 0.014, 0.026), head_material, bounds, 0.007)
    add_box("JawGuard", head, "Head", (0.105, -0.225, 1.350), (0.118, 0.014, 0.025), head_material, bounds, 0.007)
    for side, lens_x in (("R", 0.052), ("L", 0.158)):
        add_cylinder(f"LensCollar.{side}", head, "Head", (lens_x, -0.232, 1.425), (lens_x, -0.198, 1.425), 0.043, head_material, bounds, 14)
    add_cylinder("AntennaStem", body, "Head", (0.190, -0.170, 1.470), (0.235, -0.105, 1.685), 0.008, body_material, bounds, 8)
    add_sphere("AntennaBall", body, "Head", (0.238, -0.102, 1.690), (0.020, 0.020, 0.020), body_material, bounds, 10, 6)

    # Oversized fasteners make the construction legible at gameplay distance.
    for index, (x, y, z) in enumerate((
        (-0.325, -0.207, 1.315), (-0.290, -0.207, 1.435), (-0.205, -0.207, 1.505),
        (-0.105, -0.207, 1.520), (-0.205, -0.229, 1.125), (-0.105, -0.229, 1.085),
        (0.000, -0.229, 1.075), (0.105, -0.229, 1.095), (0.205, -0.229, 1.155),
    )):
        add_sphere(f"ArmorRivet{index:02d}", body, "Chest", (x, y, z), (0.014, 0.014, 0.014), body_material, bounds, 8, 6)
    add_sphere("Lens.R", eye, "Head", (0.052, -0.236, 1.425), (0.031, 0.013, 0.030), eye_material, bounds, 12, 8)
    add_sphere("Lens.L", eye, "Head", (0.158, -0.236, 1.425), (0.031, 0.013, 0.030), eye_material, bounds, 12, 8)

    for part_name, (part_low, part_high) in bounds.items():
        if part_low.x < -0.37 or part_high.x > 0.37:
            print(f"AUDIT silhouette_extreme part={part_name} x=({part_low.x:.6f},{part_high.x:.6f})")
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
    print(f"AUDIT connections={len(CONNECTION_MAP)} minimum_overlap={MIN_OVERLAP:.3f}m")
    return low, high


def require_preview(path, label):
    require(os.path.isfile(path) and os.path.getsize(path) > 0, f"Preview missing or empty: {label}/{path}")


def save_contact_sheet(scene, rig, camera, target, action_name):
    frame_width = 256
    frame_height = 256
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
        scene.render.resolution_x = 512
        scene.render.resolution_y = 512
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
