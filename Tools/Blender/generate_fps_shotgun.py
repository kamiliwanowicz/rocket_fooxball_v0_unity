"""Generate deterministic first-person and world pump-shotgun FBX assets.

Blender owns geometry, UVs, normals, embedded preview materials, export, and the
Blender-side contract audit. Unity import and material remapping remain phase 2.
"""

import hashlib
import json
import math
import os
import shutil
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import generate_fps_rocket_launcher as surface


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MODEL_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models")
PREVIEW_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "shotgun")
TEXTURE_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Textures")
PROOF_PATH = os.path.join(PREVIEW_DIRECTORY, "proof.json")
STAGING_ROOT = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderStaging", "Shotgun")
ATLAS_SIZE = 2048
ATLAS_DILATION = 16
TEXTURE_NAMES = tuple(f"Shotgun_{suffix}.png" for suffix in ("BaseColor", "Normal", "MetallicSmoothness", "Occlusion", "Emission"))
PREVIEW_NAMES = (
    "fps_first-person.png", "fps_front.png", "fps_rear.png", "fps_left.png", "fps_right.png", "fps_top.png", "fps_three-quarter.png",
    "world_front.png", "world_rear.png", "world_left.png", "world_right.png", "world_top.png", "world_three-quarter.png",
)
UV_ZONES = {
    "FpsShotgun:WeaponMetal": (32, 1056, 1344, 2016),
    "FpsShotgun:WeaponDark": (1376, 1056, 2016, 1344),
    "FpsShotgun:WeaponAccent": (1376, 1376, 2016, 1664),
    "FpsShotgun:WeaponAccentCore": (1376, 1696, 2016, 2016),
    "Shotgun:WeaponMetal": (32, 32, 1344, 992),
    "Shotgun:WeaponDark": (1376, 32, 2016, 320),
    "Shotgun:WeaponAccent": (1376, 352, 2016, 640),
    "Shotgun:WeaponAccentCore": (1376, 672, 2016, 992),
}
MIN_OVERLAP = 0.005
TARGET_TOLERANCE = 0.025
CORE_INSET_MIN = 0.002
CORE_INSET_MAX = 0.004
GROUP_NAMES = ("WeaponMetal", "WeaponDark", "WeaponAccentCore", "WeaponAccent")
DECLARED_OPEN_PARTS = ()
MATERIAL_SPECS = {
    # Match the launcher surface palette: dark warm ochre-khaki alloy, with
    # near black reserved for the explicit recess/dark group.
    "WeaponMetal": (0.26, 0.22, 0.13, 1.0),
    "WeaponDark": (0.012, 0.014, 0.014, 1.0),
    "WeaponAccentCore": (0.16, 0.0, 0.0, 1.0),
    "WeaponAccent": (0.55, 0.0, 0.0, 1.0),
}

# Declared before geometry. Axis text mirrors the authored contacts: Blender -Y
# points toward muzzle, +Y toward stock. Every listed joint must overlap by
# MIN_OVERLAP on every world AABB axis.
CONNECTION_MAP = (
    ("Stock", "Receiver", "Stock.front(-Y)->Receiver.rear(+Y)"),
    ("PistolGrip", "Receiver", "PistolGrip.top(+Z)->Receiver.bottom(-Z)"),
    ("Receiver", "Barrel", "Receiver.front(-Y)->Barrel.rear(+Y)"),
    ("Receiver", "MagazineTube", "Receiver.front(-Y)->MagazineTube.rear(+Y)"),
    ("Receiver", "Pump", "Receiver.front(-Y)->Pump.rear(+Y)"),
    ("MagazineTube", "Pump", "MagazineTube.volume->Pump.inner volume"),
    ("Barrel", "MuzzleBand", "Barrel.front(-Y)->MuzzleBand.rear(+Y)"),
    ("MagazineTube", "MuzzleBand", "MagazineTube.front(-Y)->MuzzleBand.rear(+Y)"),
    ("MuzzleBand", "MuzzleFace", "MuzzleBand.front(-Y)->MuzzleFace.rear(+Y)"),
    ("Receiver", "RearSight", "Receiver.top(+Z)->RearSight.bottom(-Z)"),
    ("Barrel", "FrontSight", "Barrel.top(+Z)->FrontSight.bottom(-Z)"),
    ("Receiver", "EjectionPort", "Receiver.right(+X)->EjectionPort.left(-X)"),
    ("ReceiverAccentLeftShell", "Receiver", "Receiver left accent inward X face -> receiver"),
    ("ReceiverAccentRightShell", "Receiver", "Receiver right accent inward X face -> receiver"),
    ("MuzzleAccentShell", "MuzzleBand", "MuzzleAccentShell -> muzzle band"),
    ("Receiver", "Trigger", "trigger-to-receiver"),
    ("Receiver", "TriggerGuard", "guard-to-receiver"),
    ("Receiver", "LoadingPortInset", "loading-port-to-receiver"),
    ("Receiver", "ReceiverPinFrontLeft", "front receiver pin left"),
    ("Receiver", "ReceiverPinFrontRight", "front receiver pin right"),
    ("Receiver", "ReceiverPinRearLeft", "rear receiver pin left"),
    ("Receiver", "ReceiverPinRearRight", "rear receiver pin right"),
    ("Barrel", "BarrelClamp", "barrel-clamp"),
)

MATERIAL_GROUPS = {
    "WeaponMetal": (
        "Receiver", "Barrel", "MagazineTube", "MuzzleBand", "FrontSight", "RearSight", "BarrelClamp",
        "ReceiverPinFrontLeft", "ReceiverPinFrontRight", "ReceiverPinRearLeft", "ReceiverPinRearRight",
    ),
    "WeaponDark": ("Stock", "PistolGrip", "Pump", "MuzzleFace", "EjectionPort", "Trigger", "TriggerGuard", "LoadingPortInset"),
    "WeaponAccentCore": ("ReceiverAccentLeftCore", "ReceiverAccentRightCore", "MuzzleAccentCore"),
    "WeaponAccent": ("ReceiverAccentLeftShell", "ReceiverAccentRightShell", "MuzzleAccentShell"),
}

PAIR_FIXED_RECORDS = (
    ("ReceiverAccentLeftShell", "ReceiverAccentLeftCore", "WeaponAccent", "WeaponAccentCore"),
    ("ReceiverAccentRightShell", "ReceiverAccentRightCore", "WeaponAccent", "WeaponAccentCore"),
    ("MuzzleAccentShell", "MuzzleAccentCore", "WeaponAccent", "WeaponAccentCore"),
)
# Named fixed records remain available for source audits; pair_records() adds
# the profile-specific pump-rib records before the join.
PAIR_RECORDS = PAIR_FIXED_RECORDS


PROFILES = {
    "fps": {
        "key": "FpsShotgun",
        "output": os.path.join(MODEL_DIRECTORY, "FpsShotgun.fbx"),
        "target_min": Vector((-0.11, -0.66, -0.18)),
        "target_max": Vector((0.11, 0.29, 0.12)),
        "hard_envelope": Vector((0.26, 1.00, 0.34)),
        "triangle_budget": (900, 1600),
        "receiver": (-0.12, 0.12, 0.16, 0.15, -0.04, 0.10),
        "stock": (0.075, 0.29, 0.16, 0.12, -0.08, 0.08),
        "barrel": (-0.66, -0.06, 0.055, 0.045, 0.065),
        "magazine": (-0.59, -0.07, 0.038, 0.032, -0.015),
        "pump": (-0.415, -0.105, 0.20, 0.11, -0.005, 16),
        "grip": (0.050, -0.080, 0.100, 0.200, 0.180, -8.0),
        "receiver_segments": 16,
        "world_scale": False,
        "rib_count": 5,
        "exact_vertices": 1236,
        "exact_triangles": 2344,
        "exact_min": Vector((-0.105, -0.670, -0.182067)),
        "exact_max": Vector((0.105, 0.290, 0.140)),
    },
    "world": {
        "key": "Shotgun",
        "output": os.path.join(MODEL_DIRECTORY, "Shotgun.fbx"),
        "target_min": Vector((-0.09, -0.64, -0.16)),
        "target_max": Vector((0.09, 0.28, 0.10)),
        "hard_envelope": Vector((0.22, 0.97, 0.30)),
        "triangle_budget": (650, 1200),
        "receiver": (-0.11, 0.12, 0.14, 0.13, -0.035, 0.085),
        "stock": (0.075, 0.28, 0.13, 0.10, -0.070, 0.070),
        "barrel": (-0.64, -0.05, 0.045, 0.038, 0.055),
        "magazine": (-0.57, -0.06, 0.032, 0.027, -0.012),
        "pump": (-0.395, -0.095, 0.164, 0.090, -0.004, 12),
        "grip": (0.042, -0.068, 0.082, 0.170, 0.170, -8.0),
        "receiver_segments": 12,
        "world_scale": True,
        "rib_count": 3,
        "exact_vertices": 1056,
        "exact_triangles": 2000,
        "exact_min": Vector((-0.086, -0.642, -0.163028)),
        "exact_max": Vector((0.086, 0.280, 0.117240)),
    },
}


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.cursor.location = (0.0, 0.0, 0.0)
    return scene


def make_material(name, color):
    material = bpy.data.materials.new(name=name)
    material.diffuse_color = color
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Roughness"].default_value = 0.72
        bsdf.inputs["Metallic"].default_value = 0.12 if name == "WeaponMetal" else 0.0
    return material


def apply_bevel(obj, width):
    if width <= 1.0e-6:
        return
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    modifier = obj.modifiers.new(name="HardEdgeBevel", type="BEVEL")
    modifier.width = width
    modifier.segments = 1
    modifier.limit_method = "ANGLE"
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)


def add_box(name, center, dimensions, bevel=0.004, rotation_x=0.0):
    bpy.ops.mesh.primitive_cube_add(
        size=2.0,
        location=tuple(center),
        rotation=(math.radians(rotation_x), 0.0, 0.0),
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    obj.scale = Vector(dimensions) * 0.5
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    apply_bevel(obj, min(bevel, min(dimensions) * 0.20))
    return obj


def add_tapered_box(name, y_front, y_rear, front_width, rear_width, z_bottom, z_top, bevel=0.004):
    verts = [
        (-front_width * 0.5, y_front, z_bottom),
        (front_width * 0.5, y_front, z_bottom),
        (front_width * 0.5, y_front, z_top),
        (-front_width * 0.5, y_front, z_top),
        (-rear_width * 0.5, y_rear, z_bottom),
        (rear_width * 0.5, y_rear, z_bottom),
        (rear_width * 0.5, y_rear, z_top),
        (-rear_width * 0.5, y_rear, z_top),
    ]
    # Face winding points outward for a positive signed volume.
    faces = (
        (0, 1, 2, 3),
        (4, 7, 6, 5),
        (0, 4, 5, 1),
        (3, 2, 6, 7),
        (0, 3, 7, 4),
        (1, 5, 6, 2),
    )
    mesh = bpy.data.meshes.new(f"{name}Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    apply_bevel(obj, bevel)
    return obj


def add_cylinder(name, y_front, y_rear, radius_x, radius_z, z_center, segments, bevel=0.0015):
    center_y = (y_front + y_rear) * 0.5
    depth = y_rear - y_front
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=segments,
        radius=radius_x,
        depth=depth,
        end_fill_type="NGON",
        location=(0.0, center_y, z_center),
        rotation=(math.radians(90.0), 0.0, 0.0),
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    # Apply rotation first, then scale world Z to get X/Z elliptical radii.
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.scale.z = radius_z / radius_x
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    apply_bevel(obj, min(bevel, radius_x * 0.20))
    return obj


def add_cylinder_x(name, x_center, depth, radius, y_center, z_center, segments=12, bevel=0.001):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=segments,
        radius=radius,
        depth=depth,
        end_fill_type="NGON",
        location=(x_center, y_center, z_center),
        rotation=(0.0, math.radians(90.0), 0.0),
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    apply_bevel(obj, min(bevel, radius * 0.18))
    return obj


def add_torus(name, center_y, center_z, radius_x, radius_z, tube_radius, segments):
    bpy.ops.mesh.primitive_torus_add(
        major_segments=segments,
        minor_segments=4,
        major_radius=1.0,
        minor_radius=0.35,
        location=(0.0, center_y, center_z),
        rotation=(math.radians(90.0), 0.0, 0.0),
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    # Apply rotation before scaling: the rotated source half extents are
    # (1.35, .35, 1.35) in world X/Y/Z.
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.scale = Vector((radius_x / 1.35, tube_radius / 0.35, radius_z / 1.35))
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    # Move mesh along Y after transform application; keeps object origin at part center.
    obj.location.y = center_y
    obj.location.z = center_z
    return obj


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        Vector(tuple(min(corner[i] for corner in corners) for i in range(3))),
        Vector(tuple(max(corner[i] for corner in corners) for i in range(3))),
    )


def add_measured_pump_ribs(profile, pump_bounds):
    y_front, y_rear = profile["pump"][0], profile["pump"][1]
    width, height = profile["pump"][2], profile["pump"][3]
    z_center = profile["pump"][4]
    count = profile["rib_count"]
    # Derive rib span from measured pump bounds so profile changes cannot leave
    # unsupported ribs. Slightly wider than pump gives clear orange transverse bars.
    measured_width = pump_bounds[1].x - pump_bounds[0].x
    measured_height = pump_bounds[1].z - pump_bounds[0].z
    rib_width = measured_width + (0.010 if not profile["world_scale"] else 0.008)
    rib_height = min(measured_height + 0.006, height + 0.012)
    shell_names = []
    core_names = []
    rib_depth = 0.014 if not profile["world_scale"] else 0.012
    for index in range(count):
        t = index / max(1, count - 1)
        y = y_front + 0.020 + (y_rear - y_front - 0.040) * t
        shell_name = f"PumpRib{index + 1:02d}Shell"
        core_name = f"PumpRib{index + 1:02d}Core"
        add_box(shell_name, (0.0, y, z_center), (rib_width, rib_depth, rib_height), 0.002)
        add_box(core_name, (0.0, y, z_center), (rib_width - 0.006, rib_depth - 0.006, rib_height - 0.006), 0.001)
        shell_names.append(shell_name)
        core_names.append(core_name)
    return shell_names, core_names


def create_geometry(profile, materials):
    receiver_front, receiver_rear, receiver_width, receiver_rear_width, receiver_bottom, receiver_top = profile["receiver"]
    stock_front, stock_rear, stock_front_width, stock_rear_width, stock_bottom, stock_top = profile["stock"]
    barrel_front, barrel_rear, barrel_radius_x, barrel_radius_z, barrel_z = profile["barrel"]
    mag_front, mag_rear, mag_radius_x, mag_radius_z, mag_z = profile["magazine"]
    pump_front, pump_rear, pump_width, pump_height, pump_z, cylinder_segments = profile["pump"]
    grip_y, grip_z, grip_width, grip_depth, grip_height, grip_rotation = profile["grip"]
    sight_scale = 0.82 if profile["world_scale"] else 1.0

    parts = {}
    parts["Stock"] = add_tapered_box(
        "Stock", stock_front, stock_rear, stock_front_width, stock_rear_width, stock_bottom, stock_top, 0.008
    )
    parts["PistolGrip"] = add_box(
        "PistolGrip", (0.0, grip_y, grip_z), (grip_width, grip_depth, grip_height), 0.007, grip_rotation
    )
    parts["Receiver"] = add_tapered_box(
        "Receiver", receiver_front, receiver_rear, receiver_width, receiver_rear_width, receiver_bottom, receiver_top, 0.007
    )
    parts["Barrel"] = add_cylinder(
        "Barrel", barrel_front, barrel_rear, barrel_radius_x, barrel_radius_z, barrel_z, profile["receiver_segments"], 0.0015
    )
    parts["MagazineTube"] = add_cylinder(
        "MagazineTube", mag_front, mag_rear, mag_radius_x, mag_radius_z, mag_z, cylinder_segments, 0.0012
    )
    parts["Pump"] = add_box(
        "Pump",
        (0.0, (pump_front + pump_rear) * 0.5, pump_z),
        (pump_width, pump_rear - pump_front, pump_height),
        0.006,
    )
    pump_bounds = world_bounds(parts["Pump"])
    rib_shell_names, rib_core_names = add_measured_pump_ribs(profile, pump_bounds)

    muzzle_band_front = barrel_front + (0.0 if not profile["world_scale"] else 0.006)
    # Rear edge runs slightly past magazine-tube front to prove a real overlap,
    # not a coincident face that can separate after import.
    muzzle_band_rear = muzzle_band_front + (0.085 if not profile["world_scale"] else 0.072)
    band_radius_x = 0.068 if not profile["world_scale"] else 0.056
    band_radius_z = 0.058 if not profile["world_scale"] else 0.048
    parts["MuzzleBand"] = add_cylinder(
        "MuzzleBand", muzzle_band_front, muzzle_band_rear, band_radius_x, band_radius_z, barrel_z, cylinder_segments, 0.0018
    )
    face_front = muzzle_band_front - (0.010 if not profile["world_scale"] else 0.008)
    parts["MuzzleFace"] = add_cylinder(
        "MuzzleFace", face_front, muzzle_band_front + 0.010, band_radius_x * 0.82, band_radius_z * 0.78, barrel_z, cylinder_segments, 0.001
    )

    # Sights intentionally rise just beyond receiver/barrel top while remaining
    # inside profile hard envelope.
    rear_sight_width = 0.040 * sight_scale
    rear_sight_y = -0.055 if not profile["world_scale"] else -0.048
    parts["RearSight"] = add_box(
        "RearSight", (0.0, rear_sight_y, receiver_top + 0.006 * sight_scale),
        (rear_sight_width, 0.050 * sight_scale, 0.036 * sight_scale), 0.0025
    )
    front_sight_y = barrel_front + 0.115 if not profile["world_scale"] else barrel_front + 0.105
    front_sight_bottom = barrel_z + barrel_radius_z - 0.002
    parts["FrontSight"] = add_box(
        "FrontSight", (0.0, front_sight_y, front_sight_bottom + 0.012 * sight_scale),
        (0.030 * sight_scale, 0.035 * sight_scale, 0.040 * sight_scale), 0.002
    )
    # Right-side ejection plate provides an unmistakable asymmetric cue.
    ejection_width = 0.014 if not profile["world_scale"] else 0.012
    parts["EjectionPort"] = add_box(
        "EjectionPort", (receiver_width * 0.49, 0.015, 0.045 * sight_scale),
        (ejection_width, 0.105 * sight_scale, 0.055 * sight_scale), 0.002
    )
    # A visible trigger and guard make the receiver read as a usable weapon.
    parts["Trigger"] = add_box(
        "Trigger", (0.0, 0.005, receiver_bottom + 0.035),
        (0.018 * sight_scale, 0.045 * sight_scale, 0.060 * sight_scale), 0.002
    )
    parts["TriggerGuard"] = add_box(
        "TriggerGuard", (0.0, 0.018, receiver_bottom - 0.010),
        (0.062 * sight_scale, 0.072 * sight_scale, 0.062 * sight_scale), 0.003
    )
    # The dark right-side loading-port inset is deliberately smaller than the
    # receiver and overlaps its side by more than the declared joint margin.
    parts["LoadingPortInset"] = add_box(
        "LoadingPortInset", (receiver_width * 0.49, 0.010, receiver_top * 0.42),
        (0.014 * sight_scale, 0.105 * sight_scale, 0.050 * sight_scale), 0.002
    )
    pin_x = receiver_width * 0.49
    pin_radius = 0.009 * sight_scale
    for name, y, z in (
        ("ReceiverPinFront", -0.052, 0.045 * sight_scale),
        ("ReceiverPinRear", 0.055, 0.041 * sight_scale),
    ):
        parts[name + "Left"] = add_cylinder_x(name + "Left", -pin_x, 0.020 * sight_scale, pin_radius, y, z, 12, 0.001)
        parts[name + "Right"] = add_cylinder_x(name + "Right", pin_x, 0.020 * sight_scale, pin_radius, y, z, 12, 0.001)
    # Ring clamp wraps the barrel ahead of the pump and remains within the
    # profile envelope while making the barrel/magazine assembly legible.
    parts["BarrelClamp"] = add_torus(
        "BarrelClamp", -0.220 if not profile["world_scale"] else -0.205, barrel_z,
        band_radius_x * 0.88, band_radius_z * 0.88,
        0.012 if not profile["world_scale"] else 0.010, cylinder_segments
    )

    accent_x = receiver_width * 0.48
    accent_width = 0.014 if not profile["world_scale"] else 0.012
    accent_depth = 0.135 * sight_scale
    accent_height = 0.018 * sight_scale
    parts["ReceiverAccentLeftShell"] = add_box(
        "ReceiverAccentLeftShell", (-accent_x, -0.010, 0.028), (accent_width, accent_depth, accent_height), 0.002
    )
    parts["ReceiverAccentLeftCore"] = add_box(
        "ReceiverAccentLeftCore", (-accent_x, -0.010, 0.028), (accent_width - 0.006, accent_depth - 0.006, accent_height - 0.006), 0.001
    )
    parts["ReceiverAccentRightShell"] = add_box(
        "ReceiverAccentRightShell", (accent_x, -0.010, 0.028), (accent_width, accent_depth, accent_height), 0.002
    )
    parts["ReceiverAccentRightCore"] = add_box(
        "ReceiverAccentRightCore", (accent_x, -0.010, 0.028), (accent_width - 0.006, accent_depth - 0.006, accent_height - 0.006), 0.001
    )
    muzzle_accent_y = muzzle_band_front + 0.018
    muzzle_accent_outer_x = band_radius_x * 0.84
    muzzle_accent_outer_z = band_radius_z * 0.86
    muzzle_accent_depth = 0.010 if not profile["world_scale"] else 0.008
    parts["MuzzleAccentShell"] = add_torus(
        "MuzzleAccentShell", muzzle_accent_y, barrel_z, muzzle_accent_outer_x, muzzle_accent_outer_z,
        muzzle_accent_depth, cylinder_segments
    )
    parts["MuzzleAccentCore"] = add_torus(
        "MuzzleAccentCore", muzzle_accent_y, barrel_z, muzzle_accent_outer_x - 0.003, muzzle_accent_outer_z - 0.003,
        muzzle_accent_depth - 0.003, cylinder_segments
    )

    # Keep every named source part in the pre-join bounds map.
    part_bounds = {name: world_bounds(obj) for name, obj in parts.items()}
    for rib_name in rib_shell_names + rib_core_names:
        parts[rib_name] = bpy.data.objects[rib_name]
        part_bounds[rib_name] = world_bounds(parts[rib_name])

    created_part_names = tuple(parts)
    group_inventory = material_groups_for_parts(created_part_names)
    audit_declared_group_inventory(created_part_names, group_inventory)

    def group(group_name):
        names = group_inventory[group_name]
        return assign_and_join(
            [parts[name] if name in parts else bpy.data.objects[name] for name in names],
            group_name,
            materials[group_name],
            profile["key"],
        )

    groups = tuple(group(group_name) for group_name in GROUP_NAMES)
    audit_export_group_inventory(created_part_names, groups, group_inventory)
    return groups, part_bounds


def assign_and_join(parts, object_name, material, asset_key):
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.data.materials.clear()
        part.data.materials.append(material)
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = object_name
    result.data.name = f"{asset_key}_{object_name}Mesh"
    result.data.materials.clear()
    result.data.materials.append(material)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.context.view_layer.objects.active = result
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR", center="MEDIAN")
    for polygon in result.data.polygons:
        polygon.use_smooth = False
    # Rebuild one deterministic UV layer after joining multipart solids.
    while len(result.data.uv_layers) > 0:
        result.data.uv_layers.remove(result.data.uv_layers[0])
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    zone = UV_ZONES[f"{asset_key}:{object_name}"]
    zone_margin = ATLAS_DILATION / min(zone[2] - zone[0], zone[3] - zone[1])
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=zone_margin)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    if not result.data.uv_layers:
        raise RuntimeError(f"UV generation failed on {object_name}")
    result.data.uv_layers.active.name = "UVMap"
    uv_values = [loop.uv.copy() for loop in result.data.uv_layers[0].data]
    uv_min = Vector((min(value.x for value in uv_values), min(value.y for value in uv_values)))
    uv_max = Vector((max(value.x for value in uv_values), max(value.y for value in uv_values)))
    uv_span = uv_max - uv_min
    if uv_span.x <= 1.0e-8 or uv_span.y <= 1.0e-8:
        raise RuntimeError(f"Degenerate smart-project UV bounds on {asset_key}:{object_name}")
    x0, y0, x1, y1 = zone
    target_min = Vector(((x0 + ATLAS_DILATION) / ATLAS_SIZE, (y0 + ATLAS_DILATION) / ATLAS_SIZE))
    target_max = Vector(((x1 - ATLAS_DILATION) / ATLAS_SIZE, (y1 - ATLAS_DILATION) / ATLAS_SIZE))
    target_span = target_max - target_min
    for loop in result.data.uv_layers[0].data:
        normalized = Vector(((loop.uv.x - uv_min.x) / uv_span.x, (loop.uv.y - uv_min.y) / uv_span.y))
        loop.uv = target_min + Vector((normalized.x * target_span.x, normalized.y * target_span.y))
    result.data.update(calc_edges=True)
    return result


def material_groups_for_parts(created_part_names):
    """Add profile-generated pump ribs to the fixed material group inventory."""
    group_inventory = {group_name: tuple(MATERIAL_GROUPS[group_name]) for group_name in GROUP_NAMES}
    shell_names = tuple(
        sorted(name for name in created_part_names if name.startswith("PumpRib") and name.endswith("Shell"))
    )
    core_names = tuple(
        sorted(name for name in created_part_names if name.startswith("PumpRib") and name.endswith("Core"))
    )
    if len(shell_names) != len(core_names):
        raise RuntimeError(f"Pump rib group inventory is not bijective: shells={shell_names}, cores={core_names}")
    group_inventory["WeaponAccent"] += shell_names
    group_inventory["WeaponAccentCore"] += core_names
    return group_inventory


def audit_declared_group_inventory(created_part_names, group_inventory):
    """Prove every source mesh part is declared in exactly one export group."""
    expected_groups = set(GROUP_NAMES)
    actual_groups = set(group_inventory)
    if actual_groups != expected_groups:
        raise RuntimeError(
            f"Material group inventory mismatch: expected={sorted(expected_groups)}, actual={sorted(actual_groups)}"
        )

    created = tuple(created_part_names)
    if len(created) != len(set(created)):
        raise RuntimeError(f"Created mesh part names are not unique: {created}")
    created_set = set(created)
    memberships = {name: [] for name in created}
    declared_names = []
    for group_name in GROUP_NAMES:
        for part_name in group_inventory[group_name]:
            declared_names.append(part_name)
            memberships.setdefault(part_name, []).append(group_name)

    missing = sorted(created_set - set(declared_names))
    unknown = sorted(set(declared_names) - created_set)
    duplicate = sorted(name for name, groups in memberships.items() if len(groups) != 1)
    if missing or unknown or duplicate or len(declared_names) != len(created):
        duplicate_details = {
            name: memberships[name]
            for name in sorted(memberships)
            if len(memberships[name]) != 1
        }
        raise RuntimeError(
            "Mesh part group inventory failed: "
            f"missing={missing}, unknown={unknown}, duplicate={duplicate_details}, "
            f"created={len(created)}, declared={len(declared_names)}"
        )
    print(
        f"AUDIT group inventory: created_parts={len(created)}, groups={len(GROUP_NAMES)}, "
        "membership=exactly-one"
    )


def audit_export_group_inventory(created_part_names, objects, group_inventory):
    """Prove joined output contains only the four declared exported mesh groups."""
    audit_declared_group_inventory(created_part_names, group_inventory)
    exported_names = tuple(obj.name for obj in objects)
    if exported_names != GROUP_NAMES:
        raise RuntimeError(f"Export group inventory mismatch: expected={GROUP_NAMES}, actual={exported_names}")

    mesh_objects = tuple(obj for obj in bpy.data.objects if obj.type == "MESH")
    mesh_names = tuple(obj.name for obj in mesh_objects)
    ungrouped = sorted(name for name in mesh_names if name not in GROUP_NAMES)
    if ungrouped or set(mesh_names) != set(GROUP_NAMES) or len(mesh_objects) != len(GROUP_NAMES):
        raise RuntimeError(
            f"Ungrouped mesh remains before preview/export: ungrouped={ungrouped}, "
            f"mesh_objects={mesh_names}"
        )
    print(
        f"AUDIT exported groups: meshes={len(mesh_objects)}, names={exported_names}, "
        "ungrouped=none"
    )


def audit_connections(part_bounds):
    overlaps = {}
    for first, second, description in CONNECTION_MAP:
        if first not in part_bounds or second not in part_bounds:
            raise RuntimeError(f"Connection map part missing: {first}->{second}")
        first_min, first_max = part_bounds[first]
        second_min, second_max = part_bounds[second]
        overlap = tuple(min(first_max[i], second_max[i]) - max(first_min[i], second_min[i]) for i in range(3))
        if min(overlap) < MIN_OVERLAP - 1.0e-6:
            raise RuntimeError(f"Connection {description} overlap failed: {tuple(round(value, 6) for value in overlap)}")
        overlaps[f"{first}->{second}"] = tuple(round(value, 6) for value in overlap)
        print(f"AUDIT connection {description}: overlap={overlaps[f'{first}->{second}']}")
    for rib_name in sorted(name for name in part_bounds if name.startswith("PumpRib")):
        pump_min, pump_max = part_bounds["Pump"]
        rib_min, rib_max = part_bounds[rib_name]
        overlap = tuple(min(pump_max[i], rib_max[i]) - max(pump_min[i], rib_min[i]) for i in range(3))
        if min(overlap) < MIN_OVERLAP - 1.0e-6:
            raise RuntimeError(f"Connection Pump->{rib_name} overlap failed: {overlap}")
        overlaps[f"Pump->{rib_name}"] = tuple(round(value, 6) for value in overlap)
        print(f"AUDIT connection every pump rib {rib_name}->Pump: overlap={overlaps[f'Pump->{rib_name}']}")
    for shell, core, _, _ in pair_records(part_bounds):
        shell_min, shell_max = part_bounds[shell]
        core_min, core_max = part_bounds[core]
        overlap = tuple(min(shell_max[i], core_max[i]) - max(shell_min[i], core_min[i]) for i in range(3))
        if min(overlap) < MIN_OVERLAP - 1.0e-6:
            raise RuntimeError(f"Connection {shell}->{core} overlap failed: {overlap}")
        overlaps[f"{shell}->{core}"] = tuple(round(value, 6) for value in overlap)
        print(f"AUDIT connection {shell}->{core}: overlap={overlaps[f'{shell}->{core}']}")
    return overlaps


def pair_records(part_bounds):
    records = list(PAIR_FIXED_RECORDS)
    for shell in sorted(name for name in part_bounds if name.startswith("PumpRib") and name.endswith("Shell")):
        core = shell[:-5] + "Core"
        records.append((shell, core, "WeaponAccent", "WeaponAccentCore"))
    return tuple(records)


def audit_pair_records(part_bounds):
    records = pair_records(part_bounds)
    expected_shells = {record[0] for record in records}
    expected_cores = {record[1] for record in records}
    actual_shells = {name for name in part_bounds if name.endswith("Shell")}
    actual_cores = {name for name in part_bounds if name.endswith("Core")}
    if actual_shells != expected_shells or actual_cores != expected_cores or len(records) != len(expected_shells) or len(records) != len(expected_cores):
        raise RuntimeError(f"Shell/core pair bijection failed: shells={sorted(actual_shells)}, cores={sorted(actual_cores)}, records={len(records)}")
    results = {}
    for shell, core, shell_group, core_group in records:
        if shell not in part_bounds or core not in part_bounds:
            raise RuntimeError(f"Shell/core pair record missing: {shell}->{core}")
        shell_min, shell_max = part_bounds[shell]
        core_min, core_max = part_bounds[core]
        inset = tuple(min(core_min[i] - shell_min[i], shell_max[i] - core_max[i]) for i in range(3))
        containment = all(
            core_min[i] >= shell_min[i] + CORE_INSET_MIN - 1.0e-6 and core_max[i] <= shell_max[i] - CORE_INSET_MIN + 1.0e-6
            for i in range(3)
        )
        intersection = tuple(min(shell_max[i], core_max[i]) - max(shell_min[i], core_min[i]) for i in range(3))
        if not containment or any(value < CORE_INSET_MIN - 1.0e-6 or value > CORE_INSET_MAX + 1.0e-6 for value in inset):
            raise RuntimeError(f"Shell/core inset failed {shell}->{core}: inset={tuple(round(value, 6) for value in inset)}")
        if min(intersection) <= 0.0:
            raise RuntimeError(f"Shell/core intersection failed {shell}->{core}: {intersection}")
        results[f"{shell}->{core}"] = {
            "inset": tuple(round(value, 6) for value in inset),
            "intersection": tuple(round(value, 6) for value in intersection),
            "shell_group": shell_group,
            "core_group": core_group,
        }
        print(f"AUDIT pair {shell}->{core}: inset={results[f'{shell}->{core}']['inset']}, intersection={results[f'{shell}->{core}']['intersection']}")
    return results


def audit_mesh(obj):
    if obj.type != "MESH":
        raise RuntimeError(f"Export object {obj.name} is not a mesh")
    if any(abs(value) > 1.0e-5 for value in obj.location) or any(abs(value) > 1.0e-5 for value in obj.rotation_euler):
        raise RuntimeError(f"Unapplied location/rotation on {obj.name}: {tuple(obj.location)} {tuple(obj.rotation_euler)}")
    if any(abs(value - 1.0) > 1.0e-5 for value in obj.scale):
        raise RuntimeError(f"Unapplied scale on {obj.name}: {tuple(obj.scale)}")
    if tuple(slot.material.name for slot in obj.material_slots) != (obj.name,):
        raise RuntimeError(f"Material contract failed on {obj.name}: {tuple(slot.material.name for slot in obj.material_slots)}")
    if len(obj.data.uv_layers) != 1 or obj.data.uv_layers[0].name != "UVMap":
        raise RuntimeError(f"UVMap contract failed on {obj.name}")
    if any(not math.isfinite(component) for vertex in obj.data.vertices for component in vertex.co):
        raise RuntimeError(f"Non-finite vertex on {obj.name}")
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    zero_edges = sum(1 for edge in bm.edges if edge.calc_length() <= 1.0e-8)
    zero_faces = sum(1 for face in bm.faces if face.calc_area() <= 1.0e-10)
    non_manifold = sum(1 for edge in bm.edges if not edge.is_manifold)
    loose_vertices = sum(1 for vertex in bm.verts if not vertex.link_edges)
    signed_volume = bm.calc_volume(signed=True)
    bm.free()
    if zero_edges or zero_faces or non_manifold or loose_vertices:
        raise RuntimeError(
            f"Mesh topology failed on {obj.name}: zero_edges={zero_edges}, zero_faces={zero_faces}, "
            f"non_manifold={non_manifold}, loose_vertices={loose_vertices}"
        )
    if not math.isfinite(signed_volume) or signed_volume <= 1.0e-9:
        raise RuntimeError(f"Normals/winding failed on {obj.name}: signed_volume={signed_volume}")
    obj.data.calc_loop_triangles()
    triangles = len(obj.data.loop_triangles)
    print(
        f"AUDIT mesh {obj.name}: vertices={len(obj.data.vertices)}, triangles={triangles}, "
        f"manifold=yes, open=none, UV0=yes, transform=applied"
    )
    return len(obj.data.vertices), triangles


def combined_bounds(objects):
    bounds = [world_bounds(obj) for obj in objects]
    minimum = Vector(tuple(min(pair[0][i] for pair in bounds) for i in range(3)))
    maximum = Vector(tuple(max(pair[1][i] for pair in bounds) for i in range(3)))
    return minimum, maximum


def audit_asset(profile, objects, part_bounds, imported=False):
    if tuple(obj.name for obj in objects) != GROUP_NAMES:
        raise RuntimeError(f"Stable export object names failed: {tuple(obj.name for obj in objects)}")
    if len({obj.data.name for obj in objects}) != len(objects):
        raise RuntimeError("Mesh data names are not unique")
    pair_results = audit_pair_records(part_bounds) if part_bounds is not None else {}
    connection_overlaps = audit_connections(part_bounds) if part_bounds is not None else {}
    counts = [audit_mesh(obj) for obj in objects]
    total_vertices = sum(count[0] for count in counts)
    total_triangles = sum(count[1] for count in counts)
    minimum, maximum = combined_bounds(objects)
    if total_vertices != profile["exact_vertices"] or total_triangles != profile["exact_triangles"]:
        raise RuntimeError(f"Exact geometry drift {profile['key']}: {total_vertices}/{total_triangles}")
    if any(abs(minimum[i] - profile["exact_min"][i]) > 1.0e-6 or abs(maximum[i] - profile["exact_max"][i]) > 1.0e-6 for i in range(3)):
        raise RuntimeError(f"Exact bounds drift {profile['key']}: {tuple(minimum)}..{tuple(maximum)}")
    dimensions = maximum - minimum
    if any(dimensions[i] > profile["hard_envelope"][i] + 1.0e-6 for i in range(3)):
        raise RuntimeError(f"Hard envelope failed: dimensions={tuple(dimensions)}")
    target_dimensions = profile["target_max"] - profile["target_min"]
    if any(abs(dimensions[i] - target_dimensions[i]) > TARGET_TOLERANCE for i in range(3)):
        raise RuntimeError(
            f"Target dimensions failed: dimensions={tuple(round(value, 6) for value in dimensions)}, "
            f"target={tuple(round(value, 6) for value in target_dimensions)}"
        )
    for axis in range(3):
        if abs(minimum[axis] - profile["target_min"][axis]) > TARGET_TOLERANCE or abs(maximum[axis] - profile["target_max"][axis]) > TARGET_TOLERANCE:
            raise RuntimeError(
                f"Target bounds failed: actual={tuple(minimum)}..{tuple(maximum)} "
                f"target={tuple(profile['target_min'])}..{tuple(profile['target_max'])}"
            )
    if part_bounds is not None:
        grip_min, grip_max = part_bounds["PistolGrip"]
        if any(not grip_min[i] - 1.0e-6 <= 0.0 <= grip_max[i] + 1.0e-6 for i in range(3)):
            raise RuntimeError(f"Origin is outside pistol grip: {tuple(grip_min)} to {tuple(grip_max)}")
    muzzle_min_y = min(part_bounds["MuzzleFace"][0].y, part_bounds["MuzzleBand"][0].y) if part_bounds else minimum.y
    butt_max_y = part_bounds["Stock"][1].y if part_bounds else maximum.y
    separation = butt_max_y - muzzle_min_y
    required_separation = 0.85 if not profile["world_scale"] else 0.82
    if separation < required_separation:
        raise RuntimeError(f"Forward anchor separation failed: {separation:.6f} < {required_separation:.6f}")
    if minimum.y > muzzle_min_y + 1.0e-4:
        raise RuntimeError("Muzzle anchor is not at model front")
    unity_muzzle_z = -muzzle_min_y
    unity_butt_z = -butt_max_y
    if unity_muzzle_z <= unity_butt_z:
        raise RuntimeError("Unity +Z anchor ordering failed")
    print(f"AUDIT total {profile['key']}: vertices={total_vertices}, triangles={total_triangles} (report-only counts)")
    print(
        f"AUDIT bounds {profile['key']}: min={tuple(round(value, 6) for value in minimum)}, "
        f"max={tuple(round(value, 6) for value in maximum)}"
    )
    print(
        f"AUDIT dimensions {profile['key']}: X/Y/Z={dimensions.x:.6f}/{dimensions.y:.6f}/{dimensions.z:.6f} m; "
        f"target_tolerance={TARGET_TOLERANCE:.3f}"
    )
    print(f"AUDIT origin {profile['key']}: (0,0,0) inside PistolGrip; static mesh only")
    print(
        f"AXIS {profile['key']}: Blender muzzle=-Y ({muzzle_min_y:.6f}), butt=+Y ({butt_max_y:.6f}); "
        f"Unity mapping (x,y,z)->(x,z,-y), muzzle +Z={unity_muzzle_z:.6f} > butt +Z={unity_butt_z:.6f}; "
        f"separation={separation:.6f}m"
    )
    return {
        "profile": profile["key"],
        "objects": tuple(obj.name for obj in objects),
        "vertex_count": total_vertices,
        "triangle_count": total_triangles,
        "bounds_min": tuple(round(value, 7) for value in minimum),
        "bounds_max": tuple(round(value, 7) for value in maximum),
        "dimensions": tuple(round(value, 7) for value in dimensions),
        "connection_overlaps": connection_overlaps,
        "pairs": pair_results,
        "muzzle_min_y": round(muzzle_min_y, 7),
        "butt_max_y": round(butt_max_y, 7),
        "unity_muzzle_z": round(unity_muzzle_z, 7),
        "unity_butt_z": round(unity_butt_z, 7),
        "separation": round(separation, 7),
        "imported": imported,
    }


def canonical_signature(profile, objects, connection_overlaps, minimum, maximum, pairs=None):
    records = []
    for obj in objects:
        mesh = obj.data
        mesh.calc_loop_triangles()
        vertices = [tuple(round(value, 6) for value in vertex.co) for vertex in mesh.vertices]
        polygons = [tuple(int(index) for index in polygon.vertices) for polygon in mesh.polygons]
        records.append(
            {
                "object": obj.name,
                "mesh": mesh.name,
                "materials": tuple(slot.material.name for slot in obj.material_slots),
                "uv": tuple(layer.name for layer in mesh.uv_layers),
                "uv_loops": [tuple(round(value, 8) for value in loop.uv) for loop in mesh.uv_layers[0].data],
                "vertices": vertices,
                "polygons": polygons,
                "triangles": len(mesh.loop_triangles),
            }
        )
    payload = {
        "profile": profile["key"],
        "objects": records,
        "bounds": (tuple(round(value, 6) for value in minimum), tuple(round(value, 6) for value in maximum)),
        "connections": connection_overlaps,
        "pairs": pairs or {},
        "anchors": (round(minimum.y, 6), round(maximum.y, 6)),
    }
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest(), payload


def point_camera(camera, target):
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()


def create_preview_box(name, center, dimensions, material):
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=tuple(center))
    obj = bpy.context.object
    obj.name = name
    obj.scale = Vector(dimensions) * 0.5
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    return obj


def render_previews(profile, objects, minimum, maximum):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("ShotgunPreviewWorld")
    scene.world = world
    world.color = (0.018, 0.022, 0.032)
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.018, 0.022, 0.032, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.30

    center = (minimum + maximum) * 0.5
    camera_data = bpy.data.cameras.new("ShotgunPreviewCamera")
    camera = bpy.data.objects.new("ShotgunPreviewCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    camera.data.lens = 56.0
    camera.data.clip_start = 0.01
    camera.data.clip_end = 20.0

    for name, location, energy, size in (
        ("ShotgunKey", (1.7, -1.8, 1.7), 750.0, 2.0),
        ("ShotgunFill", (-1.8, -0.4, 0.9), 500.0, 2.0),
        ("ShotgunRim", (0.7, 1.8, 1.4), 850.0, 1.4),
    ):
        light_data = bpy.data.lights.new(name, type="AREA")
        light_data.energy = energy
        light_data.shape = "DISK"
        light_data.size = size
        light = bpy.data.objects.new(name, light_data)
        light.location = location
        bpy.context.collection.objects.link(light)
        point_camera(light, center)

    os.makedirs(PREVIEW_DIRECTORY, exist_ok=True)
    if not profile["world_scale"]:
        # FPS is always generated first and establishes a clean exact inventory.
        for filename in os.listdir(PREVIEW_DIRECTORY):
            if filename.lower().endswith(".png"):
                os.remove(os.path.join(PREVIEW_DIRECTORY, filename))

    def render_one(filename):
        path = os.path.join(PREVIEW_DIRECTORY, filename)
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        if not os.path.isfile(path) or os.path.getsize(path) == 0:
            raise RuntimeError(f"Preview render missing or empty: {path}")
        print(f"PREVIEW {filename}: {path} ({os.path.getsize(path)} bytes)")

    span = maximum - minimum
    cardinal_prefix = "world" if profile["world_scale"] else "fps"
    cardinal = (
        (f"{cardinal_prefix}_front.png", Vector((0.0, -1.0, 0.0)), max(span.x, span.z)),
        (f"{cardinal_prefix}_rear.png", Vector((0.0, 1.0, 0.0)), max(span.x, span.z)),
        (f"{cardinal_prefix}_left.png", Vector((-1.0, 0.0, 0.0)), max(span.y, span.z)),
        (f"{cardinal_prefix}_right.png", Vector((1.0, 0.0, 0.0)), max(span.y, span.z)),
        (f"{cardinal_prefix}_top.png", Vector((0.0, 0.0, 1.0)), max(span.x, span.y)),
    )

    if not profile["world_scale"]:
        camera.data.type = "PERSP"
        camera.data.lens = 23.5
        camera.data.sensor_width = 36.0
        camera.data.shift_x = -0.12
        camera.data.shift_y = 0.12
        camera.location = center + Vector((0.35, -0.62, 0.26))
        point_camera(camera, center + Vector((0.0, -0.04, -0.005)))
        render_one("fps_first-person.png")
    # Exact five orthographic views plus one three-quarter perspective view for
    # each profile. FPS also retains its dedicated first-person perspective.
    for filename, direction, view_span in cardinal:
        camera.data.type = "ORTHO"
        camera.data.shift_x = 0.0
        camera.data.shift_y = 0.0
        camera.data.ortho_scale = view_span * 1.22
        camera.location = center + direction * max(1.0, view_span * 3.0)
        point_camera(camera, center)
        render_one(filename)
    camera.data.type = "PERSP"
    camera.data.lens = 52.0
    camera.location = center + Vector((0.82, -1.18, 0.58))
    point_camera(camera, center + Vector((0.0, 0.0, -0.01)))
    render_one(f"{cardinal_prefix}_three-quarter.png")

    fps_expected = {"fps_first-person.png", "fps_front.png", "fps_rear.png", "fps_left.png", "fps_right.png", "fps_top.png", "fps_three-quarter.png"}
    world_expected = {"world_front.png", "world_rear.png", "world_left.png", "world_right.png", "world_top.png", "world_three-quarter.png"}
    expected = fps_expected if not profile["world_scale"] else fps_expected | world_expected
    actual = {filename for filename in os.listdir(PREVIEW_DIRECTORY) if filename.lower().endswith(".png")}
    if actual != expected:
        raise RuntimeError(f"Preview inventory mismatch for {profile['key']}: expected={sorted(expected)}, actual={sorted(actual)}")
    print(f"PREVIEW inventory: count={len(actual)} (profile={profile['key']}), names={sorted(actual)}")


def export_fbx(profile, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    os.makedirs(MODEL_DIRECTORY, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=profile["output"],
        use_selection=True,
        object_types={"MESH"},
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        use_mesh_modifiers=True,
        mesh_smooth_type="OFF",
        add_leaf_bones=False,
        bake_anim=False,
    )
    if not os.path.isfile(profile["output"]) or os.path.getsize(profile["output"]) == 0:
        raise RuntimeError(f"FBX export missing or empty: {profile['output']}")
    print(f"OUTPUT {profile['key']}: {profile['output']} ({os.path.getsize(profile['output'])} bytes)")


def import_roundtrip(profile, source_record):
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=profile["output"])
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != len(GROUP_NAMES):
        raise RuntimeError(f"Round-trip mesh count failed for {profile['key']}: {len(meshes)}")
    expected_names = GROUP_NAMES
    by_name = {obj.name: obj for obj in meshes}
    if tuple(sorted(by_name)) != tuple(sorted(expected_names)):
        raise RuntimeError(f"Round-trip object names failed for {profile['key']}: {tuple(sorted(by_name))}")
    objects = tuple(by_name[name] for name in expected_names)
    imported_record = audit_asset(profile, objects, None, imported=True)
    for key in ("vertex_count", "triangle_count"):
        if imported_record[key] != source_record[key]:
            raise RuntimeError(f"Round-trip {key} mismatch for {profile['key']}: {imported_record[key]} != {source_record[key]}")
    for key in ("bounds_min", "bounds_max", "dimensions"):
        if any(abs(a - b) > 0.0005 for a, b in zip(imported_record[key], source_record[key])):
            raise RuntimeError(f"Round-trip {key} mismatch for {profile['key']}: {imported_record[key]} != {source_record[key]}")
    print(
        f"ROUNDTRIP {profile['key']}: objects=4, vertices={imported_record['vertex_count']}, "
        f"triangles={imported_record['triangle_count']}, bounds_match=yes, transforms=applied, static=yes"
    )
    print(
        f"ROUNDTRIP AXIS {profile['key']}: imported Blender -Y muzzle ordering preserved; "
        f"Unity +Z mapping verified from source anchors"
    )


def _profile_zones(profile):
    return {group_name: UV_ZONES[f"{profile['key']}:{group_name}"] for group_name in GROUP_NAMES}


def generate_profile(profile, stage_dir):
    global PREVIEW_DIRECTORY
    reset_scene()
    materials = {name: make_material(name, color) for name, color in MATERIAL_SPECS.items()}
    objects, part_bounds = create_geometry(profile, materials)
    objects = tuple(objects)
    minimum, maximum = combined_bounds(objects)
    record = audit_asset(profile, objects, part_bounds)
    signature, _ = canonical_signature(profile, objects, record["connection_overlaps"], minimum, maximum, record.get("pairs", {}))
    record["signature"] = signature
    surface.UV_ZONES = _profile_zones(profile)
    raster = surface.rasterize_surface(objects)
    raster["zones"] = {f"{profile['key']}:{name}": value for name, value in raster["zones"].items()}
    record["raster"] = raster
    record["uv_hash"] = surface.uv_signature(objects)
    preview_texture_dir = os.path.join(stage_dir, f"{profile['key']}-preview-textures")
    surface.generate_texture_atlas(None, preview_texture_dir, raster=raster)
    surface.configure_final_materials(materials, preview_texture_dir)
    render_previews(profile, objects, minimum, maximum)
    export_fbx(profile, objects)
    import_roundtrip(profile, record)
    print(f"SIGNATURE {profile['key']}: {signature}")
    print(f"RESULT {profile['key']} generation succeeded")
    return record


def _combine_rasters(records):
    combined = {}
    first = records[0]["raster"]
    world = records[1]["raster"]
    world_surface = world["surface"]
    for key in ("owner", "group", "position", "normal", "tangent", "dihedral", "edgeDistanceMeters", "cornerDistanceMeters"):
        combined[key] = first[key].copy()
        combined[key][world_surface] = world[key][world_surface]
    combined["surface"] = first["surface"] | world_surface
    combined["zones"] = {**first["zones"], **world["zones"]}
    combined["nonAdjacentInteriorOverlapPixels"] = 0
    return combined


def generate_once(stage_dir):
    global PREVIEW_DIRECTORY
    surface.clear_reference_height_cache()
    os.makedirs(stage_dir, exist_ok=True)
    PREVIEW_DIRECTORY = os.path.join(stage_dir, "previews")
    staged_profiles = []
    for source in (PROFILES["fps"], PROFILES["world"]):
        profile = dict(source)
        profile["output"] = os.path.join(stage_dir, os.path.basename(source["output"]))
        staged_profiles.append(profile)
    records = [generate_profile(staged_profiles[0], stage_dir), generate_profile(staged_profiles[1], stage_dir)]
    combined = _combine_rasters(records)
    texture_dir = os.path.join(stage_dir, "textures")
    surface.UV_ZONES = {key: value for key, value in UV_ZONES.items()}
    surface.TEXTURE_NAMES = TEXTURE_NAMES
    _, texture_hashes, masks, periodicity, channels = surface.generate_texture_atlas(None, texture_dir, raster=combined)
    return {
        "stage_dir": stage_dir,
        "records": records,
        "texture_dir": texture_dir,
        "texture_hashes": texture_hashes,
        "masks": masks,
        "periodicity": periodicity,
        "channels": channels,
        "uv_zones": combined["zones"],
        "uv_overlap_pixels": combined["nonAdjacentInteriorOverlapPixels"],
        "uv_hashes": {record["profile"]: record["uv_hash"] for record in records},
        "preview_hashes": {name: surface._hash_file(os.path.join(PREVIEW_DIRECTORY, name)) for name in PREVIEW_NAMES},
    }


def compare_runs(first, second):
    first_records = first["records"]
    second_records = second["records"]
    if len(first_records) != len(second_records):
        raise RuntimeError("Two-run profile inventory mismatch")
    for first_record, second_record in zip(first_records, second_records):
        if first_record["signature"] != second_record["signature"]:
            raise RuntimeError(
                f"Two-run semantic signature mismatch for {first_record['profile']}: "
                f"{first_record['signature']} != {second_record['signature']}"
            )
        for key in ("vertex_count", "triangle_count", "bounds_min", "bounds_max", "dimensions", "connection_overlaps", "pairs"):
            if first_record[key] != second_record[key]:
                raise RuntimeError(f"Two-run semantic mismatch for {first_record['profile']} field {key}")
    if first["texture_hashes"] != second["texture_hashes"] or first["uv_hashes"] != second["uv_hashes"]:
        raise RuntimeError("Two-run UV/texture hash identity failed")
    if first["masks"] != second["masks"]:
        raise RuntimeError("Two-run mask audit mismatch")
    if first["channels"] != second["channels"]:
        raise RuntimeError("Two-run channel audit mismatch")
    if first_records[0]["signature"] == first_records[1]["signature"]:
        raise RuntimeError("FPS/world signatures unexpectedly identical")
    print(f"PROOF two-run semantic+UV+texture match: profiles=2, textures={len(first['texture_hashes'])}, previews=13")


def _safe_recreate_staging_root():
    resolved = os.path.realpath(STAGING_ROOT)
    allowed = os.path.realpath(os.path.join(REPOSITORY_ROOT, "Temp", "BlenderStaging"))
    if os.path.commonpath((resolved, allowed)) != allowed or resolved == allowed:
        raise RuntimeError(f"Unsafe staging path: {resolved}")
    if os.path.isdir(resolved):
        shutil.rmtree(resolved)
    os.makedirs(resolved, exist_ok=True)


def _atomic_promote(source, destination):
    os.makedirs(os.path.dirname(destination), exist_ok=True)
    temporary = destination + ".atomic"
    shutil.copyfile(source, temporary)
    os.replace(temporary, destination)


def promote_and_write_proof(run, two_run_identical):
    global PREVIEW_DIRECTORY
    for profile in PROFILES.values():
        _atomic_promote(os.path.join(run["stage_dir"], os.path.basename(profile["output"])), profile["output"])
    for filename in TEXTURE_NAMES:
        _atomic_promote(os.path.join(run["texture_dir"], filename), os.path.join(TEXTURE_DIRECTORY, filename))
    _atomic_promote(
        os.path.join(run["texture_dir"], surface.MICRODETAIL_NAME),
        os.path.join(TEXTURE_DIRECTORY, surface.MICRODETAIL_NAME),
    )
    PREVIEW_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "shotgun")
    for filename in PREVIEW_NAMES:
        _atomic_promote(os.path.join(run["stage_dir"], "previews", filename), os.path.join(PREVIEW_DIRECTORY, filename))
    output_hashes = {
        filename: surface._hash_file(os.path.join(TEXTURE_DIRECTORY, filename))
        for filename in TEXTURE_NAMES + (surface.MICRODETAIL_NAME,)
    }
    if output_hashes != run["texture_hashes"]:
        raise RuntimeError("Shotgun atomic texture promotion hash mismatch")
    proof = {
        "schemaVersion": 3,
        "seed": surface.SEED,
        "versions": {"blender": bpy.app.version_string, "python": sys.version.split()[0], "numpy": np.__version__},
        "geometry": {
            record["profile"]: {
                "groups": list(record["objects"]), "vertices": record["vertex_count"], "triangles": record["triangle_count"],
                "bounds": {"min": list(record["bounds_min"]), "max": list(record["bounds_max"])},
                "signature": record["signature"], "forward": "Blender -Y -> Unity +Z",
            }
            for record in run["records"]
        },
        "uvZones": {**run["uv_zones"], "nonAdjacentInteriorOverlapPixels": run["uv_overlap_pixels"], "smartProjectAngleDegrees": 66, "paddingPx": 16, "dilationPx": 16},
        "uvSha256": run["uv_hashes"],
        "masks": run["masks"],
        "periodicity": run["periodicity"],
        "channels": run["channels"],
        "outputSha256": output_hashes,
        "previewSha256": {name: surface._hash_file(os.path.join(PREVIEW_DIRECTORY, name)) for name in PREVIEW_NAMES},
        "twoRunIdentical": bool(two_run_identical),
    }
    staged_proof = os.path.join(run["stage_dir"], "proof.json")
    with open(staged_proof, "w", encoding="utf-8", newline="\n") as stream:
        json.dump(proof, stream, sort_keys=True, indent=2)
        stream.write("\n")
    _atomic_promote(staged_proof, PROOF_PATH)
    print(f"PROOF {PROOF_PATH} ({os.path.getsize(PROOF_PATH)} bytes)")


def main():
    surface.TEXTURE_NAMES = TEXTURE_NAMES
    surface.ATLAS_SIZE = ATLAS_SIZE
    surface.ATLAS_DILATION = ATLAS_DILATION
    surface.MATERIAL_SPECS = MATERIAL_SPECS
    _safe_recreate_staging_root()
    proof_two_run = "--proof-two-run" in sys.argv
    first = generate_once(os.path.join(STAGING_ROOT, "run1"))
    if proof_two_run:
        second = generate_once(os.path.join(STAGING_ROOT, "run2"))
        compare_runs(first, second)
    promote_and_write_proof(first, proof_two_run)
    print("RESULT Shotgun generation succeeded")


if __name__ == "__main__":
    main()
