"""Generate Rocket Fooxball's original low-poly armored footballer."""

import math
import os

import bpy
from mathutils import Vector


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BLEND_PATH = os.path.join(REPOSITORY_ROOT, "Tools", "Blender", "LowPolyCharacter.blend")
FBX_PATH = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models", "LowPolyCharacter.fbx")
PREVIEW_DIR = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "LowPolyCharacter")
MESH_NAMES = ("CharacterBody", "CharacterArmor", "CharacterHead", "CharacterEye")
ACTION_NAMES = ("Idle", "Run", "Jump", "Fall", "Land", "Kick")
ACTION_RANGES = {
    "Idle": (1, 30),
    "Run": (1, 20),
    "Jump": (1, 12),
    "Fall": (1, 15),
    "Land": (1, 10),
    "Kick": (1, 12),
}
LOOP_ACTIONS = {"Idle", "Run"}
CONTACT_SHEET_ACTIONS = ("Run", "Jump", "Fall", "Land", "Kick")
CONTACT_SHEET_FRAMES = {
    "Run": (1, 6, 11, 16, 20),
    "Jump": (1, 4, 7, 10, 12),
    "Fall": (1, 5, 9, 13, 15),
    "Land": (1, 3, 5, 8, 10),
    "Kick": (1, 3, 5, 8, 12),
}
TARGET_BOUNDS = (0.75, 0.45, 1.75)
MIN_OVERLAP = 0.005
KICK_START = 1
KICK_CONTACT = 5
KICK_END = 12
MIN_FORWARD_EXTENSION = 0.04

# Authored before geometry. Each pair names joined solids and required AABB overlap.
CONNECTION_MAP = (
    ("Pelvis", "Torso", "Z", MIN_OVERLAP),
    ("Torso", "Neck", "Z", MIN_OVERLAP),
    ("Neck", "Helmet", "Z", MIN_OVERLAP),
    ("Pelvis", "Thigh.R", "Z", MIN_OVERLAP),
    ("Thigh.R", "Shin.R", "Z", MIN_OVERLAP),
    ("Shin.R", "Boot.R", "Z", MIN_OVERLAP),
    ("Pelvis", "Thigh.L", "Z", MIN_OVERLAP),
    ("Thigh.L", "Shin.L", "Z", MIN_OVERLAP),
    ("Shin.L", "Boot.L", "Z", MIN_OVERLAP),
    ("Torso", "UpperArm.R", "X", MIN_OVERLAP),
    ("UpperArm.R", "Forearm.R", "Z", MIN_OVERLAP),
    ("Torso", "UpperArm.L", "X", MIN_OVERLAP),
    ("UpperArm.L", "Forearm.L", "Z", MIN_OVERLAP),
)


def make_material(name, color):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    return material


def add_uv_part(name, collection, bone, location, scale, material, bounds):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=12,
        ring_count=8,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name + "Mesh"
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    group = obj.vertex_groups.new(name=bone)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    if obj.data.uv_layers:
        obj.data.uv_layers.active.name = "UVMap"
    bounds[name] = world_bounds(obj)
    collection.append(obj)
    return obj


def add_box_part(name, collection, bone, location, scale, material, bounds, bevel=0.025):
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name + "Mesh"
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0.0:
        modifier = obj.modifiers.new("FacetedBevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.data.materials.append(material)
    group = obj.vertex_groups.new(name=bone)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    if obj.data.uv_layers:
        obj.data.uv_layers.active.name = "UVMap"
    bounds[name] = world_bounds(obj)
    collection.append(obj)
    return obj


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    low = Vector((min(v.x for v in corners), min(v.y for v in corners), min(v.z for v in corners)))
    high = Vector((max(v.x for v in corners), max(v.y for v in corners), max(v.z for v in corners)))
    return low, high


def join_parts(parts, object_name):
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = object_name
    obj.data.name = object_name + "Mesh"
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


def key_rotation(action, rig, bone_name, frame, rotation):
    rig.animation_data.action = action
    bpy.context.scene.frame_set(frame)
    pose = rig.pose.bones[bone_name]
    pose.rotation_mode = "XYZ"
    pose.rotation_euler = rotation
    pose.keyframe_insert("rotation_euler", frame=frame, group=bone_name)


def key_pose(action, rig, frame, rotations):
    """Key only deform-bone rotations; Root and Pelvis stay curve-free."""
    rig.animation_data.action = action
    bpy.context.scene.frame_set(frame)
    for pose in rig.pose.bones:
        if pose.name in {"Root", "Pelvis"}:
            continue
        pose.rotation_mode = "XYZ"
        pose.rotation_euler = rotations.get(pose.name, (0.0, 0.0, 0.0))
        pose.keyframe_insert("rotation_euler", frame=frame, group=pose.name)


def new_action(name, loop_intent):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    # These source-side flags make the intended Unity importer settings auditable
    # without changing the stable action names or generated FBX contract.
    action["loop_intent"] = bool(loop_intent)
    action["root_locked"] = True
    action["frame_rate"] = 30
    return action


def make_actions(rig):
    rig.animation_data_create()

    idle = new_action("Idle", True)
    for frame in ACTION_RANGES["Idle"]:
        key_pose(idle, rig, frame, {"Chest": (0.0, 0.0, 0.0)})

    run = new_action("Run", True)
    run_poses = {
        1: {
            "Thigh.R": (math.radians(-28), 0.0, 0.0),
            "Shin.R": (math.radians(20), 0.0, 0.0),
            "Foot.R": (math.radians(-8), 0.0, 0.0),
            "Thigh.L": (math.radians(28), 0.0, 0.0),
            "Shin.L": (math.radians(-20), 0.0, 0.0),
            "Foot.L": (math.radians(8), 0.0, 0.0),
            "UpperArm.R": (math.radians(26), 0.0, 0.0),
            "Forearm.R": (math.radians(-12), 0.0, 0.0),
            "UpperArm.L": (math.radians(-26), 0.0, 0.0),
            "Forearm.L": (math.radians(12), 0.0, 0.0),
        },
        6: {
            "Thigh.R": (math.radians(4), 0.0, 0.0),
            "Shin.R": (math.radians(4), 0.0, 0.0),
            "Foot.R": (math.radians(-2), 0.0, 0.0),
            "Thigh.L": (math.radians(-4), 0.0, 0.0),
            "Shin.L": (math.radians(-4), 0.0, 0.0),
            "Foot.L": (math.radians(2), 0.0, 0.0),
            "UpperArm.R": (math.radians(-4), 0.0, 0.0),
            "Forearm.R": (math.radians(-2), 0.0, 0.0),
            "UpperArm.L": (math.radians(4), 0.0, 0.0),
            "Forearm.L": (math.radians(2), 0.0, 0.0),
        },
        11: {
            "Thigh.R": (math.radians(28), 0.0, 0.0),
            "Shin.R": (math.radians(-20), 0.0, 0.0),
            "Foot.R": (math.radians(8), 0.0, 0.0),
            "Thigh.L": (math.radians(-28), 0.0, 0.0),
            "Shin.L": (math.radians(20), 0.0, 0.0),
            "Foot.L": (math.radians(-8), 0.0, 0.0),
            "UpperArm.R": (math.radians(-26), 0.0, 0.0),
            "Forearm.R": (math.radians(12), 0.0, 0.0),
            "UpperArm.L": (math.radians(26), 0.0, 0.0),
            "Forearm.L": (math.radians(-12), 0.0, 0.0),
        },
        16: {
            "Thigh.R": (math.radians(-4), 0.0, 0.0),
            "Shin.R": (math.radians(-4), 0.0, 0.0),
            "Foot.R": (math.radians(2), 0.0, 0.0),
            "Thigh.L": (math.radians(4), 0.0, 0.0),
            "Shin.L": (math.radians(4), 0.0, 0.0),
            "Foot.L": (math.radians(-2), 0.0, 0.0),
            "UpperArm.R": (math.radians(4), 0.0, 0.0),
            "Forearm.R": (math.radians(2), 0.0, 0.0),
            "UpperArm.L": (math.radians(-4), 0.0, 0.0),
            "Forearm.L": (math.radians(-2), 0.0, 0.0),
        },
        20: {
            "Thigh.R": (math.radians(-28), 0.0, 0.0),
            "Shin.R": (math.radians(20), 0.0, 0.0),
            "Foot.R": (math.radians(-8), 0.0, 0.0),
            "Thigh.L": (math.radians(28), 0.0, 0.0),
            "Shin.L": (math.radians(-20), 0.0, 0.0),
            "Foot.L": (math.radians(8), 0.0, 0.0),
            "UpperArm.R": (math.radians(26), 0.0, 0.0),
            "Forearm.R": (math.radians(-12), 0.0, 0.0),
            "UpperArm.L": (math.radians(-26), 0.0, 0.0),
            "Forearm.L": (math.radians(12), 0.0, 0.0),
        },
    }
    for frame, rotations in run_poses.items():
        key_pose(run, rig, frame, rotations)

    jump = new_action("Jump", False)
    jump_poses = {
        1: {
            "Chest": (math.radians(-6), 0.0, 0.0),
            "Thigh.R": (math.radians(-18), 0.0, 0.0),
            "Shin.R": (math.radians(34), 0.0, 0.0),
            "Foot.R": (math.radians(-12), 0.0, 0.0),
            "Thigh.L": (math.radians(-18), 0.0, 0.0),
            "Shin.L": (math.radians(34), 0.0, 0.0),
            "Foot.L": (math.radians(-12), 0.0, 0.0),
            "UpperArm.R": (math.radians(18), 0.0, 0.0),
            "UpperArm.L": (math.radians(18), 0.0, 0.0),
        },
        4: {
            "Chest": (math.radians(-10), 0.0, 0.0),
            "Thigh.R": (math.radians(-28), 0.0, 0.0),
            "Shin.R": (math.radians(48), 0.0, 0.0),
            "Foot.R": (math.radians(-18), 0.0, 0.0),
            "Thigh.L": (math.radians(-28), 0.0, 0.0),
            "Shin.L": (math.radians(48), 0.0, 0.0),
            "Foot.L": (math.radians(-18), 0.0, 0.0),
            "UpperArm.R": (math.radians(28), 0.0, 0.0),
            "UpperArm.L": (math.radians(28), 0.0, 0.0),
        },
        7: {
            "Chest": (math.radians(4), 0.0, 0.0),
            "Thigh.R": (math.radians(10), 0.0, 0.0),
            "Shin.R": (math.radians(-10), 0.0, 0.0),
            "Foot.R": (math.radians(4), 0.0, 0.0),
            "Thigh.L": (math.radians(10), 0.0, 0.0),
            "Shin.L": (math.radians(-10), 0.0, 0.0),
            "Foot.L": (math.radians(4), 0.0, 0.0),
            "UpperArm.R": (math.radians(-20), 0.0, 0.0),
            "UpperArm.L": (math.radians(-20), 0.0, 0.0),
        },
        10: {
            "Chest": (math.radians(6), 0.0, 0.0),
            "Thigh.R": (math.radians(4), 0.0, 0.0),
            "Shin.R": (math.radians(-4), 0.0, 0.0),
            "Foot.R": (math.radians(2), 0.0, 0.0),
            "Thigh.L": (math.radians(4), 0.0, 0.0),
            "Shin.L": (math.radians(-4), 0.0, 0.0),
            "Foot.L": (math.radians(2), 0.0, 0.0),
            "UpperArm.R": (math.radians(-24), 0.0, 0.0),
            "UpperArm.L": (math.radians(-24), 0.0, 0.0),
        },
        12: {
            "Chest": (math.radians(3), 0.0, 0.0),
            "Thigh.R": (math.radians(2), 0.0, 0.0),
            "Shin.R": (math.radians(-2), 0.0, 0.0),
            "Foot.R": (math.radians(1), 0.0, 0.0),
            "Thigh.L": (math.radians(2), 0.0, 0.0),
            "Shin.L": (math.radians(-2), 0.0, 0.0),
            "Foot.L": (math.radians(1), 0.0, 0.0),
            "UpperArm.R": (math.radians(-14), 0.0, 0.0),
            "UpperArm.L": (math.radians(-14), 0.0, 0.0),
        },
    }
    for frame, rotations in jump_poses.items():
        key_pose(jump, rig, frame, rotations)

    fall = new_action("Fall", False)
    fall_poses = {
        1: {
            "Chest": (math.radians(5), 0.0, 0.0),
            "Thigh.R": (math.radians(-14), 0.0, 0.0),
            "Shin.R": (math.radians(20), 0.0, 0.0),
            "Foot.R": (math.radians(-7), 0.0, 0.0),
            "Thigh.L": (math.radians(14), 0.0, 0.0),
            "Shin.L": (math.radians(-20), 0.0, 0.0),
            "Foot.L": (math.radians(7), 0.0, 0.0),
            "UpperArm.R": (math.radians(22), 0.0, 0.0),
            "UpperArm.L": (math.radians(-22), 0.0, 0.0),
        },
        5: {
            "Chest": (math.radians(5), 0.0, 0.0),
            "Thigh.R": (math.radians(-12), 0.0, 0.0),
            "Shin.R": (math.radians(17), 0.0, 0.0),
            "Foot.R": (math.radians(-6), 0.0, 0.0),
            "Thigh.L": (math.radians(12), 0.0, 0.0),
            "Shin.L": (math.radians(-17), 0.0, 0.0),
            "Foot.L": (math.radians(6), 0.0, 0.0),
            "UpperArm.R": (math.radians(20), 0.0, 0.0),
            "UpperArm.L": (math.radians(-20), 0.0, 0.0),
        },
        9: {
            "Chest": (math.radians(4), 0.0, 0.0),
            "Thigh.R": (math.radians(-10), 0.0, 0.0),
            "Shin.R": (math.radians(15), 0.0, 0.0),
            "Foot.R": (math.radians(-5), 0.0, 0.0),
            "Thigh.L": (math.radians(10), 0.0, 0.0),
            "Shin.L": (math.radians(-15), 0.0, 0.0),
            "Foot.L": (math.radians(5), 0.0, 0.0),
            "UpperArm.R": (math.radians(18), 0.0, 0.0),
            "UpperArm.L": (math.radians(-18), 0.0, 0.0),
        },
        13: {
            "Chest": (math.radians(3), 0.0, 0.0),
            "Thigh.R": (math.radians(-8), 0.0, 0.0),
            "Shin.R": (math.radians(12), 0.0, 0.0),
            "Foot.R": (math.radians(-4), 0.0, 0.0),
            "Thigh.L": (math.radians(8), 0.0, 0.0),
            "Shin.L": (math.radians(-12), 0.0, 0.0),
            "Foot.L": (math.radians(4), 0.0, 0.0),
            "UpperArm.R": (math.radians(16), 0.0, 0.0),
            "UpperArm.L": (math.radians(-16), 0.0, 0.0),
        },
        15: {
            "Chest": (math.radians(2), 0.0, 0.0),
            "Thigh.R": (math.radians(-7), 0.0, 0.0),
            "Shin.R": (math.radians(10), 0.0, 0.0),
            "Foot.R": (math.radians(-3), 0.0, 0.0),
            "Thigh.L": (math.radians(7), 0.0, 0.0),
            "Shin.L": (math.radians(-10), 0.0, 0.0),
            "Foot.L": (math.radians(3), 0.0, 0.0),
            "UpperArm.R": (math.radians(14), 0.0, 0.0),
            "UpperArm.L": (math.radians(-14), 0.0, 0.0),
        },
    }
    for frame, rotations in fall_poses.items():
        key_pose(fall, rig, frame, rotations)

    land = new_action("Land", False)
    land_poses = {
        1: {
            "Chest": (math.radians(-4), 0.0, 0.0),
            "Thigh.R": (math.radians(-24), 0.0, 0.0),
            "Shin.R": (math.radians(40), 0.0, 0.0),
            "Foot.R": (math.radians(-14), 0.0, 0.0),
            "Thigh.L": (math.radians(-24), 0.0, 0.0),
            "Shin.L": (math.radians(40), 0.0, 0.0),
            "Foot.L": (math.radians(-14), 0.0, 0.0),
            "UpperArm.R": (math.radians(14), 0.0, 0.0),
            "UpperArm.L": (math.radians(14), 0.0, 0.0),
        },
        3: {
            "Chest": (math.radians(-8), 0.0, 0.0),
            "Thigh.R": (math.radians(-32), 0.0, 0.0),
            "Shin.R": (math.radians(52), 0.0, 0.0),
            "Foot.R": (math.radians(-18), 0.0, 0.0),
            "Thigh.L": (math.radians(-32), 0.0, 0.0),
            "Shin.L": (math.radians(52), 0.0, 0.0),
            "Foot.L": (math.radians(-18), 0.0, 0.0),
            "UpperArm.R": (math.radians(22), 0.0, 0.0),
            "UpperArm.L": (math.radians(22), 0.0, 0.0),
        },
        5: {
            "Chest": (math.radians(-4), 0.0, 0.0),
            "Thigh.R": (math.radians(-18), 0.0, 0.0),
            "Shin.R": (math.radians(28), 0.0, 0.0),
            "Foot.R": (math.radians(-10), 0.0, 0.0),
            "Thigh.L": (math.radians(-18), 0.0, 0.0),
            "Shin.L": (math.radians(28), 0.0, 0.0),
            "Foot.L": (math.radians(-10), 0.0, 0.0),
            "UpperArm.R": (math.radians(10), 0.0, 0.0),
            "UpperArm.L": (math.radians(10), 0.0, 0.0),
        },
        8: {
            "Chest": (math.radians(-1), 0.0, 0.0),
            "Thigh.R": (math.radians(-5), 0.0, 0.0),
            "Shin.R": (math.radians(8), 0.0, 0.0),
            "Foot.R": (math.radians(-3), 0.0, 0.0),
            "Thigh.L": (math.radians(-5), 0.0, 0.0),
            "Shin.L": (math.radians(8), 0.0, 0.0),
            "Foot.L": (math.radians(-3), 0.0, 0.0),
            "UpperArm.R": (math.radians(3), 0.0, 0.0),
            "UpperArm.L": (math.radians(3), 0.0, 0.0),
        },
        10: {
            "Chest": (0.0, 0.0, 0.0),
            "Thigh.R": (0.0, 0.0, 0.0),
            "Shin.R": (0.0, 0.0, 0.0),
            "Foot.R": (0.0, 0.0, 0.0),
            "Thigh.L": (0.0, 0.0, 0.0),
            "Shin.L": (0.0, 0.0, 0.0),
            "Foot.L": (0.0, 0.0, 0.0),
            "UpperArm.R": (0.0, 0.0, 0.0),
            "Forearm.R": (0.0, 0.0, 0.0),
            "UpperArm.L": (0.0, 0.0, 0.0),
            "Forearm.L": (0.0, 0.0, 0.0),
        },
    }
    for frame, rotations in land_poses.items():
        key_pose(land, rig, frame, rotations)

    # Keep the existing kick timing and contact pose unchanged.
    kick = new_action("Kick", False)
    for frame, thigh, shin, foot in (
        (KICK_START, (0, 0, 0), (0, 0, 0), (0, 0, 0)),
        (3, (math.radians(-22), 0, 0), (math.radians(38), 0, 0), (math.radians(-10), 0, 0)),
        (KICK_CONTACT, (math.radians(-68), 0, 0), (math.radians(-42), 0, 0), (math.radians(18), 0, 0)),
        (8, (math.radians(22), 0, 0), (math.radians(-8), 0, 0), (0, 0, 0)),
        (KICK_END, (0, 0, 0), (0, 0, 0), (0, 0, 0)),
    ):
        key_pose(
            kick,
            rig,
            frame,
            {"Thigh.R": thigh, "Shin.R": shin, "Foot.R": foot},
        )

    actions = (idle, run, jump, fall, land, kick)
    rig.animation_data.action = idle
    for action in actions:
        track = rig.animation_data.nla_tracks.new()
        track.name = action.name
        strip = track.strips.new(action.name, int(action.frame_range[0]), action)
        strip.action_frame_start = action.frame_range[0]
        strip.action_frame_end = action.frame_range[1]
        track.mute = True
    return actions


def build_character():
    red = make_material("CharacterRed", (0.56, 0.025, 0.035))
    black = make_material("CharacterBlack", (0.018, 0.014, 0.018))
    cream = make_material("CharacterCream", (0.78, 0.67, 0.50))
    eye_red = make_material("CharacterEyeRed", (0.96, 0.04, 0.02))
    body, armor, head, eye, bounds = [], [], [], [], {}

    add_uv_part("Pelvis", body, "Pelvis", (0, 0.01, 0.85), (0.23, 0.15, 0.19), black, bounds)
    add_uv_part("Torso", body, "Spine", (0, 0.015, 1.18), (0.255, 0.17, 0.31), cream, bounds)
    add_uv_part("Neck", body, "Neck", (0, 0, 1.47), (0.09, 0.085, 0.10), black, bounds)
    for side, x in (("R", -0.16), ("L", 0.16)):
        add_uv_part(f"Thigh.{side}", body, f"Thigh.{side}", (x, 0.015, 0.67), (0.115, 0.115, 0.235), cream, bounds)
        add_uv_part(f"Shin.{side}", body, f"Shin.{side}", (x, 0.005, 0.30), (0.10, 0.10, 0.20), black, bounds)
        add_box_part(f"Boot.{side}", body, f"Foot.{side}", (x, -0.055, 0.075), (0.12, 0.19, 0.075), black, bounds, 0.025)
    for side, x in (("R", -0.290), ("L", 0.290)):
        add_uv_part(f"UpperArm.{side}", body, f"UpperArm.{side}", (x, 0.005, 1.23), (0.085, 0.09, 0.205), cream, bounds)
        add_uv_part(f"Forearm.{side}", body, f"Forearm.{side}", (x, -0.005, 0.94), (0.075, 0.085, 0.17), black, bounds)
        add_uv_part(f"Hand.{side}", body, f"Hand.{side}", (x, -0.04, 0.77), (0.075, 0.075, 0.09), cream, bounds)

    add_uv_part("ChestPlate", armor, "Chest", (0, -0.145, 1.26), (0.245, 0.065, 0.245), red, bounds)
    add_uv_part("BackPlate", armor, "Chest", (0, 0.145, 1.25), (0.235, 0.055, 0.23), black, bounds)
    add_box_part("ChestChevron", armor, "Chest", (0, -0.202, 1.30), (0.12, 0.025, 0.075), black, bounds, 0.015)
    for side, x in (("R", -0.285), ("L", 0.285)):
        add_uv_part(f"Shoulder.{side}", armor, f"UpperArm.{side}", (x, -0.005, 1.37), (0.09, 0.12, 0.12), red, bounds)
        add_uv_part(f"Knee.{side}", armor, f"Shin.{side}", (x * 0.56, -0.085, 0.46), (0.105, 0.05, 0.10), red, bounds)
        add_box_part(f"ShinPlate.{side}", armor, f"Shin.{side}", (x * 0.56, -0.082, 0.285), (0.075, 0.035, 0.11), red, bounds, 0.015)

    add_uv_part("Helmet", head, "Head", (0, 0.015, 1.595), (0.155, 0.145, 0.155), red, bounds)
    add_uv_part("FaceMask", head, "Head", (0, -0.125, 1.565), (0.125, 0.055, 0.115), black, bounds)
    add_box_part("Brow", head, "Head", (0, -0.188, 1.625), (0.125, 0.025, 0.035), cream, bounds, 0.012)
    add_box_part("EyeLens", eye, "Head", (0, -0.191, 1.575), (0.078, 0.018, 0.042), eye_red, bounds, 0.010)
    add_box_part("EyePupil", eye, "Head", (0, -0.210, 1.575), (0.021, 0.009, 0.021), black, bounds, 0.006)

    audit_connections(bounds)
    meshes = [join_parts(body, "CharacterBody"), join_parts(armor, "CharacterArmor"), join_parts(head, "CharacterHead"), join_parts(eye, "CharacterEye")]
    rig = make_rig()
    for mesh in meshes:
        attach_mesh(mesh, rig)
    actions = make_actions(rig)
    return meshes, rig, actions


def audit_connections(bounds):
    axis_index = {"X": 0, "Y": 1, "Z": 2}
    for first, second, axis, required in CONNECTION_MAP:
        low_a, high_a = bounds[first]
        low_b, high_b = bounds[second]
        overlap = min(high_a[axis_index[axis]], high_b[axis_index[axis]]) - max(low_a[axis_index[axis]], low_b[axis_index[axis]])
        if overlap + 1e-6 < required:
            raise RuntimeError(f"Connection {first}->{second} overlap {overlap:.6f}m below {required:.6f}m on {axis}")


def evaluated_group_bounds(meshes, rig, action, frame, group_name):
    """Return evaluated world bounds for vertices weighted to one deform bone."""
    rig.animation_data.action = action
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    points = []
    for obj in meshes:
        group_indices = {group.index for group in obj.vertex_groups if group.name == group_name}
        if not group_indices:
            continue
        vertex_indices = {
            vertex.index
            for vertex in obj.data.vertices
            if any(weight.group in group_indices and weight.weight > 1e-6 for weight in vertex.groups)
        }
        if not vertex_indices:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        deformed = evaluated.to_mesh()
        try:
            points.extend(evaluated.matrix_world @ deformed.vertices[index].co for index in vertex_indices)
        finally:
            evaluated.to_mesh_clear()
    if not points:
        raise RuntimeError(f"No evaluated vertices found for deform group {group_name}")
    low = Vector((min(point.x for point in points), min(point.y for point in points), min(point.z for point in points)))
    high = Vector((max(point.x for point in points), max(point.y for point in points), max(point.z for point in points)))
    return low, high


def audit(meshes, rig, actions):
    all_points = []
    total_vertices = 0
    total_triangles = 0
    bone_names = {bone.name for bone in rig.data.bones}
    if rig.name != "CharacterRig" or len(bone_names) != len(rig.data.bones):
        raise RuntimeError("Armature name/bone names invalid")
    required = {"Root", "Pelvis", "Thigh.R", "Shin.R", "Foot.R"}
    if not required.issubset(bone_names):
        raise RuntimeError("Required rig chain missing")
    if rig.data.bones["Pelvis"].parent.name != "Root" or rig.data.bones["Shin.R"].parent.name != "Thigh.R" or rig.data.bones["Foot.R"].parent.name != "Shin.R":
        raise RuntimeError("Required rig hierarchy invalid")

    for obj in meshes:
        if obj.name not in MESH_NAMES or any(abs(v) > 1e-6 for v in obj.rotation_euler) or any(abs(v - 1.0) > 1e-6 for v in obj.scale):
            raise RuntimeError(f"Transform/name audit failed: {obj.name}")
        if len(obj.data.uv_layers) != 1 or obj.data.uv_layers[0].name != "UVMap":
            raise RuntimeError(f"UVMap audit failed: {obj.name}")
        obj.data.calc_loop_triangles()
        total_vertices += len(obj.data.vertices)
        total_triangles += len(obj.data.loop_triangles)
        for vertex in obj.data.vertices:
            point = obj.matrix_world @ vertex.co
            if not all(math.isfinite(value) for value in point):
                raise RuntimeError(f"Non-finite vertex: {obj.name}")
            all_points.append(point)
            weights = [(group.group, group.weight) for group in vertex.groups if group.weight > 1e-7]
            if not weights or len(weights) > 4 or abs(sum(weight for _, weight in weights) - 1.0) > 1e-5:
                raise RuntimeError(f"Weight audit failed: {obj.name} vertex {vertex.index}")
        for edge in obj.data.edges:
            if (obj.data.vertices[edge.vertices[0]].co - obj.data.vertices[edge.vertices[1]].co).length <= 1e-7:
                raise RuntimeError(f"Zero-length edge: {obj.name}")
        for polygon in obj.data.polygons:
            if polygon.area <= 1e-10:
                raise RuntimeError(f"Zero-area face: {obj.name}")
        for edge in obj.data.edges:
            linked_faces = sum(1 for polygon in obj.data.polygons if edge.key in polygon.edge_keys)
            if linked_faces != 2:
                raise RuntimeError(f"Non-manifold edge: {obj.name}")

    low = Vector((min(p.x for p in all_points), min(p.y for p in all_points), min(p.z for p in all_points)))
    high = Vector((max(p.x for p in all_points), max(p.y for p in all_points), max(p.z for p in all_points)))
    dimensions = high - low
    if low.z < -1e-5 or low.z > 0.005 or high.z > TARGET_BOUNDS[2] + 0.005:
        raise RuntimeError(f"Ground/origin bounds invalid: {tuple(low)} to {tuple(high)}")
    if dimensions.x > TARGET_BOUNDS[0] + 0.005 or dimensions.y > TARGET_BOUNDS[1] + 0.005 or dimensions.z > TARGET_BOUNDS[2] + 0.005:
        raise RuntimeError(f"Character exceeds target bounds: {tuple(dimensions)}")
    if not 2000 <= total_triangles <= 4000:
        raise RuntimeError(f"Triangle budget invalid: {total_triangles}")
    if tuple(action.name for action in actions) != ACTION_NAMES or {action.name for action in bpy.data.actions} != set(ACTION_NAMES):
        raise RuntimeError("Declared action audit failed")

    for action in actions:
        expected_start, expected_end = ACTION_RANGES[action.name]
        actual_start, actual_end = (int(round(value)) for value in action.frame_range)
        if (actual_start, actual_end) != (expected_start, expected_end):
            raise RuntimeError(
                f"Action range invalid: {action.name} expected {expected_start}..{expected_end} "
                f"got {actual_start}..{actual_end}"
            )
        if bool(action.get("loop_intent", False)) != (action.name in LOOP_ACTIONS):
            raise RuntimeError(f"Loop intent invalid: {action.name}")
        if not bool(action.get("root_locked", False)) or int(action.get("frame_rate", 0)) != 30:
            raise RuntimeError(f"Action source metadata invalid: {action.name}")
        for curve in action.fcurves:
            if any(
                token in curve.data_path
                for token in (
                    'pose.bones["Root"].location',
                    'pose.bones["Pelvis"].location',
                    'pose.bones["Root"].rotation',
                    'pose.bones["Pelvis"].rotation',
                )
            ):
                raise RuntimeError(f"Root or pelvis motion authored: {action.name} {curve.data_path}")

        max_rotation = max(
            (abs(key.co[1]) for curve in action.fcurves for key in curve.keyframe_points),
            default=0.0,
        )
        if action.name != "Idle" and max_rotation < math.radians(3.0):
            raise RuntimeError(f"Meaningful limb displacement missing: {action.name}")

        if action.name in LOOP_ACTIONS:
            for curve in action.fcurves:
                if abs(curve.evaluate(expected_start) - curve.evaluate(expected_end)) > 1e-7:
                    raise RuntimeError(f"Cyclic endpoint mismatch: {action.name} {curve.data_path}[{curve.array_index}]")

    run = bpy.data.actions["Run"]
    required_run_curves = {
        'pose.bones["Thigh.R"].rotation_euler',
        'pose.bones["Thigh.L"].rotation_euler',
        'pose.bones["UpperArm.R"].rotation_euler',
        'pose.bones["UpperArm.L"].rotation_euler',
    }
    if not required_run_curves.issubset({curve.data_path for curve in run.fcurves}):
        raise RuntimeError("Run opposing limb curves missing")

    land = bpy.data.actions["Land"]
    for curve in land.fcurves:
        if abs(curve.evaluate(ACTION_RANGES["Land"][1])) > 1e-7:
            raise RuntimeError(f"Land end pose is not idle-compatible: {curve.data_path}[{curve.array_index}]")

    kick = bpy.data.actions["Kick"]
    duration = (kick.frame_range[1] - kick.frame_range[0]) / 30.0
    contact_ratio = (KICK_CONTACT - kick.frame_range[0]) / (kick.frame_range[1] - kick.frame_range[0])
    if not 0.30 <= duration <= 0.38 or not 0.35 <= contact_ratio <= 0.45:
        raise RuntimeError(f"Kick timing invalid: {duration:.3f}s contact {contact_ratio:.3f}")
    for bone_name in ("Thigh.R", "Shin.R", "Foot.R"):
        curves = [curve for curve in kick.fcurves if f'pose.bones["{bone_name}"]' in curve.data_path]
        for curve in curves:
            if abs(curve.evaluate(KICK_START) - curve.evaluate(KICK_END)) > 1e-7:
                raise RuntimeError(f"Kick final pose mismatch: {bone_name}")
    idle_foot_low, idle_foot_high = evaluated_group_bounds(meshes, rig, bpy.data.actions["Idle"], KICK_START, "Foot.R")
    contact_foot_low, contact_foot_high = evaluated_group_bounds(meshes, rig, kick, KICK_CONTACT, "Foot.R")
    forward_extension = idle_foot_low.y - contact_foot_low.y
    if forward_extension < MIN_FORWARD_EXTENSION:
        raise RuntimeError(
            "Kick contact does not extend right foot forward in Blender -Y: "
            f"idle_min_y={idle_foot_low.y:.4f} contact_min_y={contact_foot_low.y:.4f} "
            f"extension={forward_extension:.4f}m"
        )
    print(f"AUDIT bounds min={tuple(round(v, 4) for v in low)} max={tuple(round(v, 4) for v in high)} dimensions={tuple(round(v, 4) for v in dimensions)}")
    print(
        f"AUDIT vertices={total_vertices} triangles={total_triangles} "
        "actions=Idle[1..30](loop),Run[1..20](loop),Jump[1..12],Fall[1..15],Land[1..10],"
        f"Kick[1..12](non-loop) duration={duration:.3f}s contact={contact_ratio:.3f}"
    )
    print(
        "AUDIT contact_forward="
        f"idle_min_y={idle_foot_low.y:.6f} contact_min_y={contact_foot_low.y:.6f} "
        f"extension={forward_extension:.6f}m right_foot_bounds_contact="
        f"{tuple(round(v, 6) for v in contact_foot_low)}..{tuple(round(v, 6) for v in contact_foot_high)}"
    )
    print("AUDIT root_curves=none pelvis_curves=none loop_endpoints=equal weights=normalized max_influences=1 unweighted=0 connections=13 overlap>=0.005m")
    return low, high, total_vertices, total_triangles


def _require_preview(path, label):
    if not os.path.isfile(path) or os.path.getsize(path) == 0:
        raise RuntimeError(f"{label} render failed: {path}")


def _save_contact_sheet(scene, rig, camera, target, action_name, frames):
    """Render a side-view frame strip so each non-idle action is inspectable."""
    frame_width = 256
    frame_height = 256
    scene.render.resolution_x = frame_width
    scene.render.resolution_y = frame_height
    scene.render.resolution_percentage = 100
    camera.location = (3.4, 0.0, 0.95)
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    action = bpy.data.actions[action_name]
    frame_paths = []
    for index, frame in enumerate(frames):
        rig.animation_data.action = action
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        path = os.path.join(PREVIEW_DIR, f"{action_name.lower()}-{index + 1}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        _require_preview(path, f"{action_name} frame {frame}")
        frame_paths.append(path)

    sheet_width = frame_width * len(frame_paths)
    sheet = bpy.data.images.new(f"{action_name}ContactSheet", width=sheet_width, height=frame_height, alpha=True)
    sheet_pixels = [0.0] * (sheet_width * frame_height * 4)
    for index, path in enumerate(frame_paths):
        source = bpy.data.images.load(path, check_existing=False)
        source_pixels = [0.0] * (frame_width * frame_height * 4)
        source.pixels.foreach_get(source_pixels)
        for row in range(frame_height):
            source_start = row * frame_width * 4
            target_start = (row * sheet_width + index * frame_width) * 4
            sheet_pixels[target_start:target_start + frame_width * 4] = source_pixels[source_start:source_start + frame_width * 4]
        bpy.data.images.remove(source)
    sheet.pixels.foreach_set(sheet_pixels)
    sheet_path = os.path.join(PREVIEW_DIR, f"contact-{action_name.lower()}.png")
    sheet.filepath_raw = sheet_path
    sheet.file_format = "PNG"
    sheet.save()
    _require_preview(sheet_path, f"{action_name} contact sheet")
    bpy.data.images.remove(sheet)
    for path in frame_paths:
        os.remove(path)


def render_previews(meshes, rig, low, high):
    del meshes  # Geometry remains untouched; the evaluated rig drives only preview poses.
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    for filename in os.listdir(PREVIEW_DIR):
        if filename.lower().endswith(".png"):
            os.remove(os.path.join(PREVIEW_DIR, filename))

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.035, 0.04, 0.05)
    rig.animation_data.action = bpy.data.actions["Idle"]
    scene.frame_set(ACTION_RANGES["Idle"][0])
    bpy.context.view_layer.update()

    bpy.ops.object.light_add(type="AREA", location=(2.5, -3.5, 4.0))
    key = bpy.context.object
    key.name = "PreviewKey"
    key.data.energy = 900
    key.data.shape = "DISK"
    key.data.size = 4.0
    bpy.ops.object.light_add(type="AREA", location=(-3.0, 1.5, 2.0))
    fill = bpy.context.object
    fill.name = "PreviewFill"
    fill.data.energy = 550
    fill.data.size = 3.0
    bpy.ops.object.camera_add()
    camera = bpy.context.object
    camera.name = "PreviewCamera"
    camera.data.lens = 58
    scene.camera = camera
    target = Vector((0.0, 0.0, (low.z + high.z) * 0.5))
    views = {
        "front": (0.0, -3.4, 0.95),
        "rear": (0.0, 3.4, 0.95),
        "left": (-3.4, 0.0, 0.95),
        "right": (3.4, 0.0, 0.95),
        "top": (0.0, 0.0, 4.0),
        "three-quarter": (2.5, -2.8, 1.65),
    }
    for name, location in views.items():
        rig.animation_data.action = bpy.data.actions["Idle"]
        scene.frame_set(ACTION_RANGES["Idle"][0])
        camera.location = location
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.resolution_x = 512
        scene.render.resolution_y = 512
        scene.render.filepath = os.path.join(PREVIEW_DIR, name + ".png")
        bpy.ops.render.render(write_still=True)
        _require_preview(scene.render.filepath, name)

    for action_name in CONTACT_SHEET_ACTIONS:
        _save_contact_sheet(scene, rig, camera, target, action_name, CONTACT_SHEET_FRAMES[action_name])

    rig.animation_data.action = bpy.data.actions["Idle"]
    scene.frame_set(ACTION_RANGES["Idle"][0])
    bpy.context.view_layer.update()
    bpy.data.objects.remove(camera, do_unlink=True)
    bpy.data.objects.remove(key, do_unlink=True)
    bpy.data.objects.remove(fill, do_unlink=True)


def save_and_export(meshes, rig):
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)
    rig.animation_data.action = bpy.data.actions["Idle"]
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in (*meshes, rig):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
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
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
    )
    if not os.path.isfile(FBX_PATH) or os.path.getsize(FBX_PATH) == 0:
        raise RuntimeError("FBX export missing or empty")
    print(f"OUTPUT blend={BLEND_PATH} bytes={os.path.getsize(BLEND_PATH)}")
    print(f"OUTPUT fbx={FBX_PATH} bytes={os.path.getsize(FBX_PATH)}")
    print(
        f"OUTPUT previews={PREVIEW_DIR} count=11 required_views=6 "
        "contact_sheets=contact-run.png,contact-jump.png,contact-fall.png,contact-land.png,contact-kick.png"
    )


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.scene.render.fps = 30
    meshes, rig, actions = build_character()
    low, high, _, _ = audit(meshes, rig, actions)
    render_previews(meshes, rig, low, high)
    save_and_export(meshes, rig)


if __name__ == "__main__":
    main()
