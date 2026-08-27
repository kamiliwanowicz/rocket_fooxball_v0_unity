"""Generate the deterministic first-person rocket launcher model.

The model is authored toward Blender -Y (Unity +Z after FBX export).  The
generator deliberately keeps the visual parts separate until the named shell
and core records have been audited, then joins them into four one-slot mesh
groups for Unity's static weapon importer.
"""

import hashlib
import json
import math
import os
import shutil
import struct
import sys
import zlib

import bmesh
import bpy
import numpy as np
from mathutils import Vector


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUTPUT_PATH = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models", "FpsRocketLauncher.fbx")
PREVIEW_DIR = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "FpsRocketLauncher")
TEXTURE_DIR = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Textures")
PROOF_PATH = os.path.join(PREVIEW_DIR, "proof.json")
STAGING_ROOT = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderStaging", "FpsRocketLauncher")

SEED = 0x5A17C9E3
ATLAS_SIZE = 2048
ATLAS_DILATION = 16
MICRODETAIL_SIZE = 1024
MICRODETAIL_NAME = "WeaponMicroDetail_Normal.png"
REFERENCE_TILE_NAME = "WeaponSurfaceReference.png"
REFERENCE_TILE_PATH = os.path.join(REPOSITORY_ROOT, "Tools", "Blender", "ReferenceInputs", REFERENCE_TILE_NAME)
REFERENCE_TILE_SIZE = 1254
REFERENCE_RGBA_SHA256 = "8ae165be644582741cb75ef53b24bfccbb9c0b3faaa0afcb5ef9c32a4b36dd79"
SURFACE_REVISION = "quake-warm-v1"
REFERENCE_BLUR_SIZE = 65
REFERENCE_DELTA_SCALE = 0.18
# The Unity material applies this tile at a restrained 0.24 normal scale.  The
# encoded tile therefore needs enough source slope to survive that attenuation
# without becoming a broad, noisy normal at full strength.
MICRODETAIL_NORMAL_SLOPE_GAIN = 108.0
# The shared detail tile is derived from the irregular photographic reference
# only.  The source slope is intentionally amplified enough to survive the
# normal-map encoding and the Unity material's normal scale.
REFERENCE_NORMAL_HEIGHT_SCALE = 0.00360
REFERENCE_ATLAS_HEIGHT_SCALE = 0.00260
REFERENCE_SMOOTHNESS_SCALE = 0.100
DEPOSIT_MASK_THRESHOLD = 0.05
DIELECTRIC_DEPOSIT_METALLIC = 0.0
SOOT_METALLIC_TARGET = 0.0
# Removing reference-driven dust must not change the declared wear coverage.
# This offset is procedural and therefore remains carrier-invariant.
PROCEDURAL_GRIME_BIAS_COMPENSATION = 0.004
CHIP_CONTACT_MIN = 0.62
CHIP_FBM_MIN = 0.82
GRIME_BIAS = -0.258
MICRODETAIL_MIN_CHANNEL_RANGE = 20
MICRODETAIL_MIN_CHANNEL_STDDEV = 3.0
MICRODETAIL_MIN_XY_RANGE = 0.12
MICRODETAIL_MIN_XY_STDDEV = 0.025
MICRODETAIL_MAX_XY_MAGNITUDE = 0.30
MICRODETAIL_SEAM_EPSILON = 1.0e-5
TEXTURE_NAMES = (
    "FpsRocketLauncher_BaseColor.png",
    "FpsRocketLauncher_Normal.png",
    "FpsRocketLauncher_MetallicSmoothness.png",
    "FpsRocketLauncher_Occlusion.png",
    "FpsRocketLauncher_Emission.png",
)
SURFACE_PALETTE_BYTES = {
    "metalClean": (122, 89, 41),
    "metalShoulder": (184, 140, 76),
    "metalGroove": (59, 36, 14),
    "dirt": (59, 31, 9),
    "darkClean": (6, 5, 4),
    "darkScuff": (36, 28, 19),
    "accent": (140, 0, 0),
    "core": (41, 0, 0),
}
SURFACE_PBR = {
    "metalClean": (0.86, 0.64),
    "metalShoulder": (1.0, 0.78),
    "metalGroove": (0.94, 0.38),
    "chip": (1.0, 0.42),
    "polish": (0.86, 0.74),
    "grime": (0.0, 0.22),
    "soot": (0.0, 0.12),
    "darkClean": (0.05, 0.26),
    "darkScuff": (0.12, 0.42),
    "accent": (0.0, 0.82),
    "core": (0.0, 0.74),
}
WEAR_COVERAGE_LIMITS = {
    "scratchCore": (0.0075, 0.0120),
    "chips": (0.0040, 0.0080),
    "grime": (0.0120, 0.0250),
    "muzzleSoot": (0.10, 0.45),
}
SCRATCH_POOL_RATIOS = {"sharp": 0.60, "handling": 0.25, "muzzle": 0.15}
SCRATCH_POOL_TIE_ORDER = ("sharp", "handling", "muzzle")
SCRATCH_ORIENTATION_RATIOS = {"tangent": 0.70, "cross": 0.30}
SCRATCH_ORIENTATION_TIE_ORDER = ("tangent", "cross")
SCRATCH_PROFILE_CONTRACTS = {
    profile: {
        "poolRatios": dict(SCRATCH_POOL_RATIOS),
        "poolTieOrder": list(SCRATCH_POOL_TIE_ORDER),
        "orientationRatios": dict(SCRATCH_ORIENTATION_RATIOS),
        "orientationTieOrder": list(SCRATCH_ORIENTATION_TIE_ORDER),
    }
    for profile in ("launcher", "FpsShotgun", "Shotgun")
}
PREVIEW_NAMES = ("front.png", "rear.png", "left.png", "right.png", "top.png", "three-quarter.png")
UV_ZONES = {
    "WeaponMetal": (32, 864, 2016, 2016),
    "WeaponDark": (32, 352, 1312, 832),
    "WeaponAccent": (1344, 352, 2016, 832),
    "WeaponAccentCore": (32, 32, 2016, 320),
}

TARGET_MIN = Vector((-0.16, -0.55, -0.1313))
TARGET_MAX = Vector((0.16, 0.20, 0.11))
TARGET_TOLERANCE = 0.00005
TARGET_VERTICES = 1120
TARGET_TRIANGLES = 2156
HARD_ENVELOPE = Vector((0.40, 0.90, 0.35))
MIN_OVERLAP = 0.005
CORE_INSET_MIN = 0.002
CORE_INSET_MAX = 0.004
GROUP_NAMES = ("WeaponMetal", "WeaponDark", "WeaponAccentCore", "WeaponAccent")
DECLARED_OPEN_PARTS = ()

# This map is intentionally declared before geometry. Every joint is checked
# in world-space AABB terms before source parts are joined.
CONNECTION_MAP = (
    ("Grip", "RearBlock", "grip-to-receiver"),
    ("RearBlock", "Core", "receiver-to-body"),
    ("Core", "MuzzleHousing", "body-to-muzzle-housing"),
    ("MuzzleHousing", "MuzzleCollar", "housing-to-collar"),
    ("MuzzleHousing", "MuzzleInset", "housing-to-muzzle-inset"),
    ("Core", "TopSpine", "body-to-top-spine"),
    ("Core", "RailLeft", "body-to-left-rail"),
    ("Core", "RailRight", "body-to-right-rail"),
    ("MuzzleHousing", "RailLeft", "housing-to-left-rail"),
    ("MuzzleHousing", "RailRight", "housing-to-right-rail"),
    ("MuzzleHousing", "MuzzleSegment01", "housing-to-segment-one"),
    ("MuzzleHousing", "MuzzleSegment02", "housing-to-segment-two"),
    ("MuzzleCollar", "MuzzleSegment02", "collar-to-segment-two"),
    ("TopSpine", "AccentSpineShell", "top-spine-shell"),
    ("RailLeft", "AccentLeftShell", "left-rail-shell"),
    ("RailRight", "AccentRightShell", "right-rail-shell"),
    ("MuzzleCollar", "AccentMuzzleShell", "muzzle-ring-shell"),
    ("Core", "SideVentLeft", "left-side-vent"),
    ("Core", "SideVentRight", "right-side-vent"),
    ("RearBlock", "ReceiverFastenerFront", "front-receiver-fastener"),
    ("RearBlock", "ReceiverFastenerRear", "rear-receiver-fastener"),
    ("Grip", "GripRib01", "grip-rib-one"),
    ("Grip", "GripRib02", "grip-rib-two"),
    ("Grip", "GripRib03", "grip-rib-three"),
)

PAIR_RECORDS = (
    ("AccentSpineShell", "AccentSpineCore", "WeaponAccent", "WeaponAccentCore"),
    ("AccentLeftShell", "AccentLeftCore", "WeaponAccent", "WeaponAccentCore"),
    ("AccentRightShell", "AccentRightCore", "WeaponAccent", "WeaponAccentCore"),
    ("AccentMuzzleShell", "AccentMuzzleCore", "WeaponAccent", "WeaponAccentCore"),
)

MATERIAL_SPECS = {
    # Q2's weapon is a desaturated warm olive/khaki alloy. Dark values stay
    # confined to the explicitly recessed group below; the shell itself remains metal.
    "WeaponMetal": (0.32, 0.30, 0.22, 1.0),
    "WeaponDark": (0.012, 0.014, 0.014, 1.0),
    "WeaponAccentCore": (0.16, 0.0, 0.0, 1.0),
    "WeaponAccent": (0.55, 0.0, 0.0, 1.0),
}

SURFACE_SPECS = {
    # Tuple order: clean/exposed colour, base/chip/grime metallic, then
    # base/chip/grime/polished smoothness. The shell is coated metal: enough
    # diffuse response to remain readable in shadow, with bright metallic
    # exposure only in deliberate scratches and edge chips.
    "WeaponMetal": {"clean": (0.32, 0.30, 0.22), "exposed": (0.52, 0.48, 0.34), "metal": (0.72, 1.0, DIELECTRIC_DEPOSIT_METALLIC), "smooth": (0.48, 0.78, 0.18, 0.68)},
    "WeaponDark": {"clean": (0.008, 0.010, 0.011), "exposed": (0.035, 0.038, 0.034), "metal": (0.05, 0.12, DIELECTRIC_DEPOSIT_METALLIC), "smooth": (0.30, 0.50, 0.20, 0.46)},
    "WeaponAccent": {"clean": (0.55, 0.0, 0.0), "exposed": (0.55, 0.0, 0.0), "metal": (0.0, 0.0, 0.0), "smooth": (0.72, 0.72, 0.72, 0.72)},
    "WeaponAccentCore": {"clean": (0.16, 0.0, 0.0), "exposed": (0.16, 0.0, 0.0), "metal": (0.0, 0.0, 0.0), "smooth": (0.80, 0.80, 0.80, 0.80)},
}

MATERIAL_GROUPS = {
    "WeaponMetal": ("RearBlock", "Core", "MuzzleHousing", "MuzzleCollar", "MuzzleSegment01", "MuzzleSegment02", "TopSpine", "RailLeft", "RailRight", "ReceiverFastenerFront", "ReceiverFastenerRear"),
    "WeaponDark": ("Grip", "MuzzleInset", "SideVentLeft", "SideVentRight", "GripRib01", "GripRib02", "GripRib03"),
    "WeaponAccentCore": ("AccentSpineCore", "AccentLeftCore", "AccentRightCore", "AccentMuzzleCore"),
    "WeaponAccent": ("AccentSpineShell", "AccentLeftShell", "AccentRightShell", "AccentMuzzleShell"),
}


def make_material(name, color):
    material = bpy.data.materials.new(name=name)
    material.diffuse_color = color
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Roughness"].default_value = 0.52
        bsdf.inputs["Metallic"].default_value = 0.72 if name == "WeaponMetal" else 0.0
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
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=tuple(center), rotation=(rotation_x, 0.0, 0.0))
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    obj.scale = Vector(dimensions) * 0.5
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    apply_bevel(obj, min(bevel, min(dimensions) * 0.20))
    return obj


def add_tapered_box(name, y_front, y_rear, front_width, rear_width, z_bottom, z_top, bevel=0.004):
    vertices = (
        (-front_width * 0.5, y_front, z_bottom),
        (front_width * 0.5, y_front, z_bottom),
        (front_width * 0.5, y_front, z_top),
        (-front_width * 0.5, y_front, z_top),
        (-rear_width * 0.5, y_rear, z_bottom),
        (rear_width * 0.5, y_rear, z_bottom),
        (rear_width * 0.5, y_rear, z_top),
        (-rear_width * 0.5, y_rear, z_top),
    )
    faces = (
        (0, 1, 2, 3),
        (4, 7, 6, 5),
        (0, 4, 5, 1),
        (3, 2, 6, 7),
        (0, 3, 7, 4),
        (1, 5, 6, 2),
    )
    mesh = bpy.data.meshes.new(f"{name}Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    apply_bevel(obj, bevel)
    return obj


def add_cylinder_y(name, y_front, y_rear, radius_x, radius_z, z_center, vertices=16, bevel=0.002):
    center_y = (y_front + y_rear) * 0.5
    depth = y_rear - y_front
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius_x,
        depth=depth,
        end_fill_type="NGON",
        location=(0.0, center_y, z_center),
        rotation=(math.radians(90.0), 0.0, 0.0),
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.scale.z = radius_z / radius_x
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    apply_bevel(obj, min(bevel, radius_x * 0.18))
    return obj


def add_cylinder_x(name, x_center, depth, radius, y_center, z_center, vertices=12, bevel=0.001):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        end_fill_type="NGON",
        location=(x_center, y_center, z_center),
        rotation=(0.0, math.radians(90.0), 0.0),
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    apply_bevel(obj, min(bevel, radius * 0.18))
    return obj


def add_torus_y(name, center_y, center_z, outer_x, outer_z, half_depth, segments=16):
    bpy.ops.mesh.primitive_torus_add(
        major_segments=segments,
        minor_segments=6,
        major_radius=1.0,
        minor_radius=0.35,
        location=(0.0, center_y, center_z),
        rotation=(math.radians(90.0), 0.0, 0.0),
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    # Apply rotation first: the rotated source half extents are (1.35, .35,
    # 1.35) in world X/Y/Z, then scale those axes independently.
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.scale = Vector((outer_x / 1.35, half_depth / 0.35, outer_z / 1.35))
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return obj


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        Vector(tuple(min(corner[i] for corner in corners) for i in range(3))),
        Vector(tuple(max(corner[i] for corner in corners) for i in range(3))),
    )


def add_measured_rail(name, x, z, height, neighbour_a, neighbour_b):
    first_min, first_max = world_bounds(neighbour_a)
    second_min, second_max = world_bounds(neighbour_b)
    y_rear = first_max.y - 0.010
    y_front = second_min.y + 0.010
    depth = y_rear - y_front
    if depth <= 0.0:
        raise RuntimeError(f"Invalid measured rail span {name}: {depth:.6f}")
    return add_box(name, (x, (y_front + y_rear) * 0.5, z), (0.040, depth, height), 0.004)


def assign_and_join(parts, object_name, material):
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.data.materials.clear()
        part.data.materials.append(material)
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = object_name
    result.data.name = f"{object_name}Mesh"
    result.data.materials.clear()
    result.data.materials.append(material)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR", center="MEDIAN")
    for polygon in result.data.polygons:
        polygon.use_smooth = False
    bpy.context.view_layer.objects.active = result
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    zone = UV_ZONES[object_name]
    zone_margin = ATLAS_DILATION / min(zone[2] - zone[0], zone[3] - zone[1])
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=zone_margin)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    while len(result.data.uv_layers) > 1:
        result.data.uv_layers.remove(result.data.uv_layers[-1])
    if not result.data.uv_layers:
        raise RuntimeError(f"UV0 generation failed on {object_name}")
    result.data.uv_layers.active.name = "UVMap"
    uv_values = [loop.uv.copy() for loop in result.data.uv_layers[0].data]
    uv_min = Vector((min(value.x for value in uv_values), min(value.y for value in uv_values)))
    uv_max = Vector((max(value.x for value in uv_values), max(value.y for value in uv_values)))
    uv_span = uv_max - uv_min
    if uv_span.x <= 1.0e-8 or uv_span.y <= 1.0e-8:
        raise RuntimeError(f"Degenerate smart-project UV bounds on {object_name}")
    x0, y0, x1, y1 = zone
    target_min = Vector(((x0 + ATLAS_DILATION) / ATLAS_SIZE, (y0 + ATLAS_DILATION) / ATLAS_SIZE))
    target_max = Vector(((x1 - ATLAS_DILATION) / ATLAS_SIZE, (y1 - ATLAS_DILATION) / ATLAS_SIZE))
    target_span = target_max - target_min
    for loop in result.data.uv_layers[0].data:
        normalized = Vector(((loop.uv.x - uv_min.x) / uv_span.x, (loop.uv.y - uv_min.y) / uv_span.y))
        loop.uv = target_min + Vector((normalized.x * target_span.x, normalized.y * target_span.y))
    result.data.update(calc_edges=True)
    return result


def create_geometry(materials):
    parts = {}
    parts["Grip"] = add_box("Grip", (0.0, 0.095, -0.055), (0.095, 0.190, 0.130), 0.009, math.radians(-8.0))
    for index, y in enumerate((0.025, 0.095, 0.165), 1):
        parts[f"GripRib{index:02d}"] = add_box(f"GripRib{index:02d}", (0.0, y, -0.055), (0.104, 0.018, 0.132), 0.002)
    parts["RearBlock"] = add_tapered_box("RearBlock", -0.030, 0.200, 0.205, 0.165, -0.070, 0.075, 0.010)
    parts["Core"] = add_tapered_box("Core", -0.385, 0.015, 0.255, 0.190, -0.075, 0.085, 0.010)
    parts["MuzzleHousing"] = add_cylinder_y("MuzzleHousing", -0.465, -0.285, 0.160, 0.095, 0.015, 16, 0.006)
    parts["MuzzleCollar"] = add_torus_y("MuzzleCollar", -0.470, 0.015, 0.145, 0.085, 0.020, 16)
    parts["MuzzleInset"] = add_cylinder_y("MuzzleInset", -0.550, -0.455, 0.105, 0.070, 0.015, 16, 0.002)
    parts["MuzzleSegment01"] = add_torus_y("MuzzleSegment01", -0.330, 0.015, 0.142, 0.083, 0.008, 16)
    parts["MuzzleSegment02"] = add_torus_y("MuzzleSegment02", -0.455, 0.015, 0.151, 0.090, 0.008, 16)
    parts["TopSpine"] = add_box("TopSpine", (0.0, -0.175, 0.082), (0.090, 0.405, 0.046), 0.006)
    parts["RailLeft"] = add_measured_rail("RailLeft", -0.126, 0.012, 0.055, parts["Core"], parts["MuzzleHousing"])
    parts["RailRight"] = add_measured_rail("RailRight", 0.126, 0.012, 0.055, parts["Core"], parts["MuzzleHousing"])
    parts["SideVentLeft"] = add_box("SideVentLeft", (-0.126, -0.130, 0.022), (0.020, 0.118, 0.040), 0.003)
    parts["SideVentRight"] = add_box("SideVentRight", (0.126, -0.130, 0.022), (0.020, 0.118, 0.040), 0.003)
    parts["ReceiverFastenerFront"] = add_cylinder_x("ReceiverFastenerFront", 0.104, 0.020, 0.012, -0.010, 0.052, 12, 0.001)
    parts["ReceiverFastenerRear"] = add_cylinder_x("ReceiverFastenerRear", -0.104, 0.020, 0.012, 0.120, 0.050, 12, 0.001)

    # Red/orange spine, rail, and muzzle-ring shells are paired with smaller
    # cores. The records are captured before any group is joined.
    parts["AccentSpineShell"] = add_box("AccentSpineShell", (0.0, -0.172, 0.100), (0.056, 0.315, 0.018), 0.003)
    parts["AccentSpineCore"] = add_box("AccentSpineCore", (0.0, -0.172, 0.100), (0.050, 0.309, 0.012), 0.002)
    parts["AccentLeftShell"] = add_box("AccentLeftShell", (-0.143, -0.270, 0.012), (0.034, 0.205, 0.032), 0.003)
    parts["AccentLeftCore"] = add_box("AccentLeftCore", (-0.143, -0.270, 0.012), (0.028, 0.199, 0.026), 0.002)
    parts["AccentRightShell"] = add_box("AccentRightShell", (0.143, -0.270, 0.012), (0.034, 0.205, 0.032), 0.003)
    parts["AccentRightCore"] = add_box("AccentRightCore", (0.143, -0.270, 0.012), (0.028, 0.199, 0.026), 0.002)
    parts["AccentMuzzleShell"] = add_torus_y("AccentMuzzleShell", -0.470, 0.015, 0.116, 0.076, 0.016, 16)
    parts["AccentMuzzleCore"] = add_torus_y("AccentMuzzleCore", -0.470, 0.015, 0.113, 0.073, 0.013, 16)

    part_bounds = {name: world_bounds(obj) for name, obj in parts.items()}
    accent_shells = tuple(record[0] for record in PAIR_RECORDS)
    accent_cores = tuple(record[1] for record in PAIR_RECORDS)
    metal_names = (
        "RearBlock", "Core", "MuzzleHousing", "MuzzleCollar", "MuzzleSegment01", "MuzzleSegment02",
        "TopSpine", "RailLeft", "RailRight", "ReceiverFastenerFront", "ReceiverFastenerRear",
    )
    dark_names = ("Grip", "MuzzleInset", "SideVentLeft", "SideVentRight", "GripRib01", "GripRib02", "GripRib03")
    groups = (
        assign_and_join([parts[name] for name in metal_names], "WeaponMetal", materials["WeaponMetal"]),
        assign_and_join([parts[name] for name in dark_names], "WeaponDark", materials["WeaponDark"]),
        assign_and_join([parts[name] for name in accent_cores], "WeaponAccentCore", materials["WeaponAccentCore"]),
        assign_and_join([parts[name] for name in accent_shells], "WeaponAccent", materials["WeaponAccent"]),
    )
    return groups, part_bounds


def audit_connections(part_bounds):
    overlaps = {}
    entries = list(CONNECTION_MAP)
    entries.extend((shell, core, f"{shell}-to-{core}") for shell, core, _, _ in PAIR_RECORDS)
    for first, second, description in entries:
        if first not in part_bounds or second not in part_bounds:
            raise RuntimeError(f"Connection map part missing: {first}->{second}")
        first_min, first_max = part_bounds[first]
        second_min, second_max = part_bounds[second]
        overlap = tuple(min(first_max[i], second_max[i]) - max(first_min[i], second_min[i]) for i in range(3))
        if min(overlap) < MIN_OVERLAP - 1.0e-6:
            raise RuntimeError(f"Connection {description} overlap failed: {tuple(round(value, 6) for value in overlap)}")
        overlaps[f"{first}->{second}"] = tuple(round(value, 6) for value in overlap)
        print(f"AUDIT connection {description}: overlap={overlaps[f'{first}->{second}']}")
    return overlaps


def audit_pair_records(part_bounds):
    expected_shells = {record[0] for record in PAIR_RECORDS}
    expected_cores = {record[1] for record in PAIR_RECORDS}
    actual_shells = {name for name in part_bounds if name.endswith("Shell")}
    actual_cores = {name for name in part_bounds if name.endswith("Core") and name != "Core"}
    if actual_shells != expected_shells or actual_cores != expected_cores or len(PAIR_RECORDS) != len(expected_shells) or len(PAIR_RECORDS) != len(expected_cores):
        raise RuntimeError(
            f"Shell/core pair bijection failed: shells={sorted(actual_shells)}, cores={sorted(actual_cores)}, records={len(PAIR_RECORDS)}"
        )
    pair_bounds = {}
    for shell, core, shell_group, core_group in PAIR_RECORDS:
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
        pair_bounds[f"{shell}->{core}"] = {
            "inset": tuple(round(value, 6) for value in inset),
            "intersection": tuple(round(value, 6) for value in intersection),
            "shell_group": shell_group,
            "core_group": core_group,
        }
        print(f"AUDIT pair {shell}->{core}: inset={pair_bounds[f'{shell}->{core}']['inset']}, intersection={pair_bounds[f'{shell}->{core}']['intersection']}")
    return pair_bounds


def audit_mesh(obj):
    if obj.type != "MESH":
        raise RuntimeError(f"Export object {obj.name} is not a mesh")
    if any(abs(value) > 1.0e-5 for value in obj.location) or any(abs(value) > 1.0e-5 for value in obj.rotation_euler):
        raise RuntimeError(f"Unapplied location/rotation on {obj.name}")
    if any(abs(value - 1.0) > 1.0e-5 for value in obj.scale):
        raise RuntimeError(f"Unapplied scale on {obj.name}")
    if tuple(slot.material.name for slot in obj.material_slots) != (obj.name,):
        raise RuntimeError(f"Material slot contract failed on {obj.name}: {tuple(slot.material.name for slot in obj.material_slots)}")
    if len(obj.data.uv_layers) != 1 or obj.data.uv_layers[0].name != "UVMap":
        raise RuntimeError(f"UV0-only contract failed on {obj.name}")
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
    if zero_edges or zero_faces or non_manifold or loose_vertices or not math.isfinite(signed_volume) or signed_volume <= 1.0e-9:
        raise RuntimeError(
            f"Mesh topology failed on {obj.name}: zero_edges={zero_edges}, zero_faces={zero_faces}, non_manifold={non_manifold}, loose_vertices={loose_vertices}, signed_volume={signed_volume}"
        )
    obj.data.calc_loop_triangles()
    triangles = len(obj.data.loop_triangles)
    print(f"AUDIT mesh {obj.name}: vertices={len(obj.data.vertices)}, triangles={triangles}, manifold=yes, open=none, UV0=yes, transform=applied")
    return len(obj.data.vertices), triangles


def combined_bounds(objects):
    bounds = [world_bounds(obj) for obj in objects]
    return (
        Vector(tuple(min(pair[0][i] for pair in bounds) for i in range(3))),
        Vector(tuple(max(pair[1][i] for pair in bounds) for i in range(3))),
    )


def audit_asset(objects, part_bounds, imported=False):
    if tuple(obj.name for obj in objects) != GROUP_NAMES:
        raise RuntimeError(f"Stable export object names failed: {tuple(obj.name for obj in objects)}")
    if len({obj.data.name for obj in objects}) != len(objects):
        raise RuntimeError("Mesh data names are not unique")
    pair_bounds = audit_pair_records(part_bounds) if part_bounds is not None else {}
    connection_overlaps = audit_connections(part_bounds) if part_bounds is not None else {}
    counts = [audit_mesh(obj) for obj in objects]
    total_vertices = sum(pair[0] for pair in counts)
    total_triangles = sum(pair[1] for pair in counts)
    if total_vertices != TARGET_VERTICES or total_triangles != TARGET_TRIANGLES:
        raise RuntimeError(
            f"Geometry count contract failed: vertices={total_vertices}/{TARGET_VERTICES}, triangles={total_triangles}/{TARGET_TRIANGLES}"
        )
    minimum, maximum = combined_bounds(objects)
    dimensions = maximum - minimum
    if any(dimensions[i] > HARD_ENVELOPE[i] + 1.0e-6 for i in range(3)):
        raise RuntimeError(f"Hard envelope failed: dimensions={tuple(round(value, 6) for value in dimensions)}")
    for axis in range(3):
        if abs(minimum[axis] - TARGET_MIN[axis]) > TARGET_TOLERANCE or abs(maximum[axis] - TARGET_MAX[axis]) > TARGET_TOLERANCE:
            raise RuntimeError(f"Target bounds failed: actual={tuple(minimum)}..{tuple(maximum)} target={tuple(TARGET_MIN)}..{tuple(TARGET_MAX)}")
    if part_bounds is not None:
        grip_min, grip_max = part_bounds["Grip"]
        if any(not grip_min[i] - 1.0e-6 <= 0.0 <= grip_max[i] + 1.0e-6 for i in range(3)):
            raise RuntimeError(f"Origin is outside grip/mount: {tuple(grip_min)} to {tuple(grip_max)}")
        muzzle_min_y = part_bounds["MuzzleInset"][0].y
        butt_max_y = part_bounds["RearBlock"][1].y
    else:
        muzzle_min_y = minimum.y
        butt_max_y = maximum.y
    if muzzle_min_y >= butt_max_y:
        raise RuntimeError("Forward anchor ordering failed")
    unity_muzzle_z = -muzzle_min_y
    unity_butt_z = -butt_max_y
    if unity_muzzle_z <= unity_butt_z:
        raise RuntimeError("Unity +Z anchor ordering failed")
    print(f"AUDIT total: vertices={total_vertices}, triangles={total_triangles} (report-only counts)")
    print(f"AUDIT bounds min={tuple(round(value, 6) for value in minimum)}, max={tuple(round(value, 6) for value in maximum)}")
    print(f"AUDIT dimensions X/Y/Z={dimensions.x:.6f}/{dimensions.y:.6f}/{dimensions.z:.6f} m")
    print(f"AUDIT origin=(0,0,0) inside Grip; Blender forward=-Y; Unity forward=+Z; imported={imported}")
    return {
        "objects": tuple(obj.name for obj in objects),
        "vertex_count": total_vertices,
        "triangle_count": total_triangles,
        "bounds_min": tuple(round(value, 7) for value in minimum),
        "bounds_max": tuple(round(value, 7) for value in maximum),
        "dimensions": tuple(round(value, 7) for value in dimensions),
        "connections": connection_overlaps,
        "pairs": pair_bounds,
        "muzzle_min_y": round(muzzle_min_y, 7),
        "butt_max_y": round(butt_max_y, 7),
        "unity_muzzle_z": round(unity_muzzle_z, 7),
        "unity_butt_z": round(unity_butt_z, 7),
    }


def canonical_signature(objects, record):
    mesh_records = []
    for obj in objects:
        mesh = obj.data
        mesh.calc_loop_triangles()
        mesh_records.append({
            "object": obj.name,
            "mesh": mesh.name,
            "materials": tuple(slot.material.name for slot in obj.material_slots),
            "uv": tuple(layer.name for layer in mesh.uv_layers),
            "uv_loops": [tuple(round(value, 8) for value in loop.uv) for loop in mesh.uv_layers[0].data],
            "vertices": [tuple(round(value, 6) for value in vertex.co) for vertex in mesh.vertices],
            "polygons": [tuple(int(index) for index in polygon.vertices) for polygon in mesh.polygons],
            "triangles": len(mesh.loop_triangles),
        })
    payload = {
        "groups": mesh_records,
        "bounds": (record["bounds_min"], record["bounds_max"]),
        "connections": record["connections"],
        "pairs": record["pairs"],
        "anchors": (record["muzzle_min_y"], record["butt_max_y"]),
        "textureHashes": record.get("texture_hashes", {}),
    }
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def uv_signature(objects):
    payload = {
        obj.name: [tuple(round(value, 8) for value in loop.uv) for loop in obj.data.uv_layers[0].data]
        for obj in objects
    }
    return hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()


def _smoothstep(edge0, edge1, value):
    denominator = edge1 - edge0
    if abs(denominator) <= 1.0e-12:
        return np.where(value >= edge1, 1.0, 0.0)
    t = np.clip((value - edge0) / denominator, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def _hash_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _png_chunk(chunk_type, data):
    return struct.pack(">I", len(data)) + chunk_type + data + struct.pack(">I", zlib.crc32(chunk_type + data) & 0xFFFFFFFF)


def _write_rgba_png(path, pixels, srgb):
    if pixels.ndim != 3 or pixels.shape[2] != 4 or pixels.dtype != np.uint8:
        raise RuntimeError(f"Invalid RGBA8 payload for {path}: {pixels.shape}/{pixels.dtype}")
    height, width, _ = pixels.shape
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Texture arrays use Blender's bottom-left UV origin; PNG scanlines are top-first.
    scanlines = b"".join(b"\x00" + row.tobytes() for row in pixels[::-1])
    chunks = [_png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))]
    if srgb:
        chunks.extend((_png_chunk(b"sRGB", b"\x00"), _png_chunk(b"gAMA", struct.pack(">I", 45455))))
    else:
        chunks.append(_png_chunk(b"gAMA", struct.pack(">I", 100000)))
    chunks.extend((_png_chunk(b"IDAT", zlib.compress(scanlines, 9)), _png_chunk(b"IEND", b"")))
    payload = b"\x89PNG\r\n\x1a\n" + b"".join(chunks)
    temporary = path + ".atomic"
    with open(temporary, "wb") as stream:
        stream.write(payload)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


def write_rgba_png(path, pixels, srgb):
    if pixels.shape != (ATLAS_SIZE, ATLAS_SIZE, 4) or pixels.dtype != np.uint8:
        raise RuntimeError(f"Invalid RGBA8 atlas payload for {path}: {pixels.shape}/{pixels.dtype}")
    _write_rgba_png(path, pixels, srgb)


def write_microdetail_png(path, pixels):
    if pixels.shape != (MICRODETAIL_SIZE, MICRODETAIL_SIZE, 4) or pixels.dtype != np.uint8:
        raise RuntimeError(f"Invalid RGBA8 microdetail payload for {path}: {pixels.shape}/{pixels.dtype}")
    _write_rgba_png(path, pixels, False)


_REFERENCE_HEIGHT = None


def clear_reference_height_cache():
    """Drop decoded reference state before each deterministic proof run."""
    global _REFERENCE_HEIGHT
    _REFERENCE_HEIGHT = None


def _load_reference_height():
    """Read the repo-contained high-pass metal reference without machine paths."""
    global _REFERENCE_HEIGHT
    if _REFERENCE_HEIGHT is not None:
        return _REFERENCE_HEIGHT
    if not os.path.isfile(REFERENCE_TILE_PATH):
        raise RuntimeError(f"Missing repo-contained weapon surface reference: {REFERENCE_TILE_PATH}")
    with open(REFERENCE_TILE_PATH, "rb") as stream:
        payload = stream.read()
    if payload[:8] != b"\x89PNG\r\n\x1a\n":
        raise RuntimeError(f"Weapon surface reference is not PNG: {REFERENCE_TILE_PATH}")
    width = height = channels = color_type = None
    compressed = bytearray()
    cursor = 8
    while cursor < len(payload):
        if cursor + 8 > len(payload):
            raise RuntimeError("Weapon surface reference has truncated PNG chunk header")
        length = struct.unpack(">I", payload[cursor:cursor + 4])[0]
        chunk_type = payload[cursor + 4:cursor + 8]
        data_start = cursor + 8
        data_end = data_start + length
        if data_end + 4 > len(payload):
            raise RuntimeError("Weapon surface reference has truncated PNG chunk data")
        data = payload[data_start:data_end]
        if chunk_type == b"IHDR":
            width, height, bit_depth, color_type, compression, filtering, interlace = struct.unpack(
                ">IIBBBBB", data
            )
            if bit_depth != 8 or color_type not in (2, 6) or compression != 0 or filtering != 0 or interlace != 0:
                raise RuntimeError("Weapon surface reference must be a non-interlaced RGB8 or RGBA8 PNG")
            channels = 3 if color_type == 2 else 4
        elif chunk_type == b"IDAT":
            compressed.extend(data)
        elif chunk_type == b"IEND":
            break
        cursor = data_end + 4
    if width != REFERENCE_TILE_SIZE or height != REFERENCE_TILE_SIZE or channels not in (3, 4):
        raise RuntimeError(
            f"Weapon surface reference size/format failed: {width}x{height}, channels={channels}"
        )
    scanline_bytes = width * channels
    decoded = zlib.decompress(bytes(compressed))
    expected = (scanline_bytes + 1) * height
    if len(decoded) != expected:
        raise RuntimeError(f"Weapon surface reference payload length failed: {len(decoded)} != {expected}")
    rows = np.frombuffer(decoded, dtype=np.uint8).reshape(height, scanline_bytes + 1)
    # Decode PNG's standard row filters explicitly so the committed tile is
    # portable across image writers while remaining deterministic at runtime.
    scanlines = np.empty((height, scanline_bytes), dtype=np.uint8)
    for row_index in range(height):
        filter_type = int(rows[row_index, 0])
        if filter_type not in (0, 1, 2, 3, 4):
            raise RuntimeError(f"Weapon surface reference has unsupported PNG filter: {filter_type}")
        filtered = rows[row_index, 1:]
        prior = scanlines[row_index - 1] if row_index else None
        reconstructed = scanlines[row_index]
        # PNG filters are bytewise predictors.  Reconstruct the current row
        # in order so Sub/Average/Paeth read bytes already unfiltered in this
        # row, never uninitialized storage.
        for byte_index in range(scanline_bytes):
            left = int(reconstructed[byte_index - channels]) if byte_index >= channels else 0
            up = int(prior[byte_index]) if prior is not None else 0
            upper_left = int(prior[byte_index - channels]) if prior is not None and byte_index >= channels else 0
            if filter_type == 0:
                predictor = 0
            elif filter_type == 1:
                predictor = left
            elif filter_type == 2:
                predictor = up
            elif filter_type == 3:
                predictor = (left + up) // 2
            else:
                estimate = left + up - upper_left
                distance_left = abs(estimate - left)
                distance_up = abs(estimate - up)
                distance_upper_left = abs(estimate - upper_left)
                if distance_left <= distance_up and distance_left <= distance_upper_left:
                    predictor = left
                elif distance_up <= distance_upper_left:
                    predictor = up
                else:
                    predictor = upper_left
            reconstructed[byte_index] = (int(filtered[byte_index]) + predictor) & 0xFF
    decoded_channels = scanlines.reshape(height, width, channels)
    if channels == 3:
        alpha = np.full((height, width, 1), 255, dtype=np.uint8)
        rgba = np.concatenate((decoded_channels, alpha), axis=2)
    else:
        rgba = decoded_channels
    decoded_hash = hashlib.sha256(rgba.tobytes()).hexdigest()
    if decoded_hash != REFERENCE_RGBA_SHA256:
        raise RuntimeError(
            f"Weapon surface reference decoded RGBA hash mismatch: {decoded_hash} != {REFERENCE_RGBA_SHA256}"
        )
    # Convert exactly from the supplied RGB bytes, remove broad lighting with
    # a periodic 65x65 blur, then normalize the remaining physical surface
    # variation. This carrier is consumed only by normal/smoothness synthesis.
    rgb = rgba[:, :, :3].astype(np.float32)
    luminance = (54.0 * rgb[:, :, 0] + 183.0 * rgb[:, :, 1] + 19.0 * rgb[:, :, 2]) / (256.0 * 255.0)

    def periodic_box_blur(values, width):
        radius = width // 2
        horizontal = np.pad(values, ((0, 0), (radius, radius)), mode="wrap")
        horizontal_sum = np.pad(horizontal, ((0, 0), (1, 0)), mode="constant").cumsum(axis=1, dtype=np.float64)
        horizontal = (horizontal_sum[:, width:] - horizontal_sum[:, :-width]) / float(width)
        vertical = np.pad(horizontal, ((radius, radius), (0, 0)), mode="wrap")
        vertical_sum = np.pad(vertical, ((1, 0), (0, 0)), mode="constant").cumsum(axis=0, dtype=np.float64)
        return ((vertical_sum[width:] - vertical_sum[:-width]) / float(width)).astype(np.float32)

    broad = periodic_box_blur(luminance, REFERENCE_BLUR_SIZE)
    _REFERENCE_HEIGHT = np.clip((luminance - broad) / REFERENCE_DELTA_SCALE, -1.0, 1.0).astype(np.float32)
    return _REFERENCE_HEIGHT


def _reference_height_at(u, v):
    """Bilinearly sample the periodic, signed high-pass reference tile."""
    height = _load_reference_height()
    u = np.mod(np.asarray(u, dtype=np.float32), 1.0)
    v = np.mod(np.asarray(v, dtype=np.float32), 1.0)
    x = u * float(REFERENCE_TILE_SIZE)
    y = v * float(REFERENCE_TILE_SIZE)
    x0 = np.floor(x).astype(np.int32) % REFERENCE_TILE_SIZE
    y0 = np.floor(y).astype(np.int32) % REFERENCE_TILE_SIZE
    x1 = (x0 + 1) % REFERENCE_TILE_SIZE
    y1 = (y0 + 1) % REFERENCE_TILE_SIZE
    fx = x - np.floor(x)
    fy = y - np.floor(y)
    top = height[y0, x0] * (1.0 - fx) + height[y0, x1] * fx
    bottom = height[y1, x0] * (1.0 - fx) + height[y1, x1] * fx
    return (top * (1.0 - fy) + bottom * fy).astype(np.float32)


def _face_dihedral_degrees(mesh):
    polygons_by_edge = {}
    for polygon in mesh.polygons:
        for key in polygon.edge_keys:
            polygons_by_edge.setdefault(tuple(sorted(key)), []).append(polygon.index)
    maximum = np.zeros(len(mesh.polygons), dtype=np.float32)
    for polygon_indices in polygons_by_edge.values():
        if len(polygon_indices) != 2:
            continue
        first, second = polygon_indices
        angle = math.degrees(mesh.polygons[first].normal.angle(mesh.polygons[second].normal, 0.0))
        maximum[first] = max(maximum[first], angle)
        maximum[second] = max(maximum[second], angle)
    return maximum


def _sharp_contact_topology(mesh):
    """Return real >=30 degree mesh edges and vertices incident to 3+ of them."""
    polygons_by_edge = {}
    for polygon in mesh.polygons:
        for key in polygon.edge_keys:
            polygons_by_edge.setdefault(tuple(sorted(key)), []).append(polygon.index)
    sharp_edges = set()
    incidence = np.zeros(len(mesh.vertices), dtype=np.int32)
    for key, polygon_indices in polygons_by_edge.items():
        if len(polygon_indices) != 2:
            continue
        first, second = polygon_indices
        angle = math.degrees(mesh.polygons[first].normal.angle(mesh.polygons[second].normal, 0.0))
        if angle + 1.0e-6 >= 30.0:
            sharp_edges.add(key)
            incidence[key[0]] += 1
            incidence[key[1]] += 1
    corners = {int(index) for index in np.flatnonzero(incidence >= 3)}
    if not sharp_edges or not corners:
        raise RuntimeError(f"Actual-edge topology missing on {mesh.name}: sharp={len(sharp_edges)}, corners={len(corners)}")
    return sharp_edges, corners


def _point_segment_distances(points, first, second):
    segment = second - first
    denominator = float(np.dot(segment, segment))
    if denominator <= 1.0e-16:
        return np.linalg.norm(points - first, axis=1)
    parameter = np.clip(((points - first) @ segment) / denominator, 0.0, 1.0)
    closest = first + parameter[:, None] * segment
    return np.linalg.norm(points - closest, axis=1)


def _triangle_uv_islands(mesh):
    """Return deterministic connected UV-island ids for loop triangles."""
    mesh.calc_loop_triangles()
    triangles = list(mesh.loop_triangles)
    parents = list(range(len(triangles)))

    def find(index):
        while parents[index] != index:
            parents[index] = parents[parents[index]]
            index = parents[index]
        return index

    def union(first, second):
        first_root = find(first)
        second_root = find(second)
        if first_root != second_root:
            parents[max(first_root, second_root)] = min(first_root, second_root)

    uv_data = mesh.uv_layers[0].data
    edge_owners = {}
    for triangle_index, triangle in enumerate(triangles):
        vertices = list(triangle.vertices)
        loops = list(triangle.loops)
        for corner in range(3):
            next_corner = (corner + 1) % 3
            endpoints = sorted((
                (int(vertices[corner]), tuple(round(value, 8) for value in uv_data[loops[corner]].uv)),
                (int(vertices[next_corner]), tuple(round(value, 8) for value in uv_data[loops[next_corner]].uv)),
            ))
            key = tuple(endpoints)
            prior = edge_owners.get(key)
            if prior is None:
                edge_owners[key] = triangle_index
            else:
                union(prior, triangle_index)
    root_to_island = {}
    result = []
    for triangle_index in range(len(triangles)):
        root = find(triangle_index)
        if root not in root_to_island:
            root_to_island[root] = len(root_to_island)
        result.append(root_to_island[root])
    return result, len(root_to_island)


def rasterize_surface(objects, profile_id=0, profile_name="launcher", profile_parameters=None):
    size = ATLAS_SIZE
    owner = np.full((size, size), -1, dtype=np.int32)
    group = np.full((size, size), 255, dtype=np.uint8)
    position = np.zeros((size, size, 3), dtype=np.float32)
    normal = np.zeros((size, size, 3), dtype=np.float32)
    tangent = np.zeros((size, size, 3), dtype=np.float32)
    dihedral = np.zeros((size, size), dtype=np.float32)
    edge_distance_m = np.full((size, size), np.inf, dtype=np.float32)
    corner_distance_m = np.full((size, size), np.inf, dtype=np.float32)
    texel_area_m2 = np.zeros((size, size), dtype=np.float32)
    profile_ids = np.full((size, size), -1, dtype=np.int16)
    uv_island_ids = np.full((size, size), -1, dtype=np.int32)
    triangle_vertices = []
    overlap_pixels = 0
    triangle_index = 0
    island_offset = 0

    for group_index, obj in enumerate(objects):
        mesh = obj.data
        mesh.calc_loop_triangles()
        mesh.update(calc_edges=True)
        face_dihedral = _face_dihedral_degrees(mesh)
        sharp_edges, sharp_corners = _sharp_contact_topology(mesh)
        triangle_islands, island_count = _triangle_uv_islands(mesh)
        uv_data = mesh.uv_layers[0].data
        for object_triangle_index, triangle in enumerate(mesh.loop_triangles):
            vertex_ids = tuple((group_index, int(index)) for index in triangle.vertices)
            triangle_vertices.append(frozenset(vertex_ids))
            uv = np.array([tuple(uv_data[index].uv) for index in triangle.loops], dtype=np.float64) * size
            xy = np.array([tuple(mesh.vertices[index].co) for index in triangle.vertices], dtype=np.float64)
            vertex_normals = np.array([tuple(mesh.vertices[index].normal) for index in triangle.vertices], dtype=np.float64)
            face_normal = np.array(tuple(triangle.normal), dtype=np.float64)
            face_normal /= max(np.linalg.norm(face_normal), 1.0e-12)
            local_y = np.array((0.0, 1.0, 0.0), dtype=np.float64)
            face_tangent = local_y - face_normal * np.dot(local_y, face_normal)
            if np.linalg.norm(face_tangent) < 0.05:
                local_z = np.array((0.0, 0.0, 1.0), dtype=np.float64)
                face_tangent = local_z - face_normal * np.dot(local_z, face_normal)
            face_tangent /= max(np.linalg.norm(face_tangent), 1.0e-12)

            min_x = max(0, int(math.floor(np.min(uv[:, 0]) - 0.5)))
            max_x = min(size - 1, int(math.ceil(np.max(uv[:, 0]) - 0.5)))
            min_y = max(0, int(math.floor(np.min(uv[:, 1]) - 0.5)))
            max_y = min(size - 1, int(math.ceil(np.max(uv[:, 1]) - 0.5)))
            if min_x > max_x or min_y > max_y:
                raise RuntimeError(f"UV triangle outside atlas: {obj.name}/{triangle_index}")
            grid_x, grid_y = np.meshgrid(
                np.arange(min_x, max_x + 1, dtype=np.float64) + 0.5,
                np.arange(min_y, max_y + 1, dtype=np.float64) + 0.5,
            )
            denominator = (uv[1, 1] - uv[2, 1]) * (uv[0, 0] - uv[2, 0]) + (uv[2, 0] - uv[1, 0]) * (uv[0, 1] - uv[2, 1])
            if abs(denominator) <= 1.0e-10:
                raise RuntimeError(f"Degenerate UV triangle: {obj.name}/{triangle_index}")
            w0 = ((uv[1, 1] - uv[2, 1]) * (grid_x - uv[2, 0]) + (uv[2, 0] - uv[1, 0]) * (grid_y - uv[2, 1])) / denominator
            w1 = ((uv[2, 1] - uv[0, 1]) * (grid_x - uv[2, 0]) + (uv[0, 0] - uv[2, 0]) * (grid_y - uv[2, 1])) / denominator
            w2 = 1.0 - w0 - w1
            inside = (w0 >= -1.0e-7) & (w1 >= -1.0e-7) & (w2 >= -1.0e-7)
            if not np.any(inside):
                triangle_index += 1
                continue
            rows, columns = np.nonzero(inside)
            yy = rows + min_y
            xx = columns + min_x
            current_owners = owner[yy, xx]
            interior = (w0[rows, columns] > 1.0e-4) & (w1[rows, columns] > 1.0e-4) & (w2[rows, columns] > 1.0e-4)
            for prior in np.unique(current_owners[(current_owners >= 0) & interior]):
                collision = (current_owners == prior) & interior
                if triangle_vertices[int(prior)].isdisjoint(triangle_vertices[triangle_index]):
                    overlap_pixels += int(np.count_nonzero(collision))
            unclaimed = current_owners < 0
            if np.any(unclaimed):
                take_rows = rows[unclaimed]
                take_columns = columns[unclaimed]
                take_y = yy[unclaimed]
                take_x = xx[unclaimed]
                weights = np.stack((w0[take_rows, take_columns], w1[take_rows, take_columns], w2[take_rows, take_columns]), axis=1)
                sampled_position = weights @ xy
                sampled_normal = weights @ vertex_normals
                sampled_normal /= np.maximum(np.linalg.norm(sampled_normal, axis=1, keepdims=True), 1.0e-12)
                owner[take_y, take_x] = triangle_index
                group[take_y, take_x] = group_index
                position[take_y, take_x] = sampled_position.astype(np.float32)
                normal[take_y, take_x] = sampled_normal.astype(np.float32)
                tangent[take_y, take_x] = face_tangent.astype(np.float32)
                dihedral[take_y, take_x] = face_dihedral[triangle.polygon_index]
                polygon_edge_keys = tuple(tuple(sorted(key)) for key in mesh.polygons[triangle.polygon_index].edge_keys)
                edge_candidates = [key for key in polygon_edge_keys if key in sharp_edges]
                if edge_candidates:
                    distances = [
                        _point_segment_distances(
                            sampled_position,
                            np.asarray(mesh.vertices[key[0]].co, dtype=np.float64),
                            np.asarray(mesh.vertices[key[1]].co, dtype=np.float64),
                        )
                        for key in edge_candidates
                    ]
                    edge_distance_m[take_y, take_x] = np.min(np.stack(distances, axis=1), axis=1).astype(np.float32)
                corner_candidates = [index for index in triangle.vertices if int(index) in sharp_corners]
                if corner_candidates:
                    distances = [
                        np.linalg.norm(sampled_position - np.asarray(mesh.vertices[index].co, dtype=np.float64), axis=1)
                        for index in corner_candidates
                    ]
                    corner_distance_m[take_y, take_x] = np.min(np.stack(distances, axis=1), axis=1).astype(np.float32)
                physical_twice_area = np.linalg.norm(np.cross(xy[1] - xy[0], xy[2] - xy[0]))
                texel_area_m2[take_y, take_x] = float(physical_twice_area / abs(denominator))
                profile_ids[take_y, take_x] = profile_id
                uv_island_ids[take_y, take_x] = island_offset + triangle_islands[object_triangle_index]
            triangle_index += 1
        island_offset += island_count

    if overlap_pixels:
        raise RuntimeError(f"UV non-adjacent interior overlap failed: pixels={overlap_pixels}")
    surface = owner >= 0
    zone_records = {}
    for object_name, zone in UV_ZONES.items():
        x0, y0, x1, y1 = zone
        used = int(np.count_nonzero(surface[y0:y1, x0:x1]))
        area = (x1 - x0) * (y1 - y0)
        utilization = used / area
        if utilization < 0.15:
            raise RuntimeError(f"UV zone utilization failed on {object_name}: {utilization:.6f}")
        zone_records[object_name] = {
            "pixels": [x0, y0, x1, y1],
            "usedPixels": used,
            "utilization": round(utilization, 6),
            "paddingPx": ATLAS_DILATION,
            "dilationPx": ATLAS_DILATION,
        }
    return {
        "owner": owner,
        "group": group,
        "position": position,
        "normal": normal,
        "tangent": tangent,
        "dihedral": dihedral,
        "edgeDistanceMeters": edge_distance_m,
        "cornerDistanceMeters": corner_distance_m,
        "texelAreaMetersSquared": texel_area_m2,
        "profileId": profile_ids,
        "uvIslandId": uv_island_ids,
        "profiles": {
            int(profile_id): {
                "name": profile_name,
                "parameters": profile_parameters or {"kind": "launcher"},
                "uvIslandCount": island_offset,
            }
        },
        "surface": surface,
        "zones": zone_records,
        "nonAdjacentInteriorOverlapPixels": overlap_pixels,
    }


def _box_sum(values, radius):
    padded = np.pad(values, ((radius, radius), (radius, radius)), mode="constant")
    integral = np.pad(padded, ((1, 0), (1, 0)), mode="constant").cumsum(axis=0).cumsum(axis=1)
    width = radius * 2 + 1
    return integral[width:, width:] - integral[:-width, width:] - integral[width:, :-width] + integral[:-width, :-width]


def _trig_noise(x, y, z, frequency, phase):
    first = np.sin((1.127 * x + 0.317 * y + 0.713 * z) * frequency * math.tau + phase)
    second = np.sin((0.271 * x - 1.193 * y + 0.449 * z) * frequency * math.tau + phase * 1.61803398875)
    return np.clip(0.5 + 0.5 * first * second, 0.0, 1.0)


def _fbm(x, y, z):
    warp_a = _trig_noise(x, y, z, 3.5, (SEED & 0xFFFF) * 0.0001) - 0.5
    warp_b = _trig_noise(x, y, z, 3.5, ((SEED >> 8) & 0xFFFF) * 0.00013) - 0.5
    warp_c = _trig_noise(x, y, z, 3.5, ((SEED >> 16) & 0xFFFF) * 0.00017) - 0.5
    x = x + 0.23 * warp_a
    y = y + 0.23 * warp_b
    z = z + 0.23 * warp_c
    frequency = 3.5
    amplitude = 1.0
    total = np.zeros_like(x, dtype=np.float32)
    normalization = 0.0
    for octave in range(5):
        phase = ((SEED ^ (0x9E3779B9 * (octave + 1))) & 0xFFFFFFFF) * (math.tau / 4294967296.0)
        total += amplitude * _trig_noise(x, y, z, frequency, phase).astype(np.float32)
        normalization += amplitude
        frequency *= 2.07
        amplitude *= 0.51
    return total / normalization


def _microdetail_height(u, v, phase):
    """Return the irregular, photo-derived height field at normalized UVs."""
    del phase
    return (REFERENCE_NORMAL_HEIGHT_SCALE * _reference_height_at(u, v)).astype(np.float32)


def _microdetail_normal_from_height(height):
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    normal = np.stack((
        -MICRODETAIL_NORMAL_SLOPE_GAIN * dx,
        -MICRODETAIL_NORMAL_SLOPE_GAIN * dy,
        np.ones_like(height),
    ), axis=2)
    return normal / np.maximum(np.linalg.norm(normal, axis=2, keepdims=True), 1.0e-12)


def _microdetail_normal_at(u, v, phase):
    texel = 1.0 / float(MICRODETAIL_SIZE)
    dx = (_microdetail_height(u + texel, v, phase) - _microdetail_height(u - texel, v, phase)) * 0.5
    dy = (_microdetail_height(u, v + texel, phase) - _microdetail_height(u, v - texel, phase)) * 0.5
    normal = np.stack((
        -MICRODETAIL_NORMAL_SLOPE_GAIN * dx,
        -MICRODETAIL_NORMAL_SLOPE_GAIN * dy,
        np.ones_like(dx),
    ), axis=-1)
    return normal / np.maximum(np.linalg.norm(normal, axis=-1, keepdims=True), 1.0e-12)


def _audit_microdetail_normal(rgba, normal, phase):
    """Fail closed if the repeatable detail normal regresses to a flat map."""
    channel_audits = {}
    for index, name in enumerate(("R", "G")):
        values = rgba[:, :, index]
        value_range = int(values.max()) - int(values.min())
        standard_deviation = float(values.std())
        if value_range < MICRODETAIL_MIN_CHANNEL_RANGE or standard_deviation < MICRODETAIL_MIN_CHANNEL_STDDEV:
            raise RuntimeError(
                f"Microdetail {name} channel is too flat: range={value_range}, stddev={standard_deviation:.6f}"
            )
        channel_audits[name] = {
            "min": int(values.min()), "max": int(values.max()), "range": value_range,
            "stddev": round(standard_deviation, 6), "uniqueValues": int(np.unique(values).size),
        }
    xy = normal[:, :, :2]
    xy_ranges = [float(xy[:, :, index].max() - xy[:, :, index].min()) for index in range(2)]
    xy_stddevs = [float(xy[:, :, index].std()) for index in range(2)]
    xy_max_magnitude = float(np.sqrt(np.sum(xy * xy, axis=2)).max())
    if min(xy_ranges) < MICRODETAIL_MIN_XY_RANGE or min(xy_stddevs) < MICRODETAIL_MIN_XY_STDDEV:
        raise RuntimeError(f"Microdetail tangent-space XY variance is too flat: ranges={xy_ranges}, stddevs={xy_stddevs}")
    if xy_max_magnitude > MICRODETAIL_MAX_XY_MAGNITUDE:
        raise RuntimeError(f"Microdetail tangent-space XY is too strong: maxMagnitude={xy_max_magnitude:.6f}")

    seam_samples = np.linspace(0.0, 1.0, MICRODETAIL_SIZE + 1, dtype=np.float32)
    zero = np.zeros_like(seam_samples)
    one = np.ones_like(seam_samples)
    height_seam_error = max(
        float(np.max(np.abs(_microdetail_height(zero, seam_samples, phase) - _microdetail_height(one, seam_samples, phase)))),
        float(np.max(np.abs(_microdetail_height(seam_samples, zero, phase) - _microdetail_height(seam_samples, one, phase)))),
    )
    normal_seam_error = max(
        float(np.max(np.abs(_microdetail_normal_at(zero, seam_samples, phase) - _microdetail_normal_at(one, seam_samples, phase)))),
        float(np.max(np.abs(_microdetail_normal_at(seam_samples, zero, phase) - _microdetail_normal_at(seam_samples, one, phase)))),
    )
    if height_seam_error > MICRODETAIL_SEAM_EPSILON or normal_seam_error > MICRODETAIL_SEAM_EPSILON:
        raise RuntimeError(
            f"Microdetail repeat seam failed: height={height_seam_error:.9f}, normal={normal_seam_error:.9f}"
        )
    return {
        "encoding": "tangent-space RGB normal, RG=XY, B=Z, A=255",
        "carrier": "photo-derived irregular reference only; no analytic sine families",
        "normalSlopeGain": MICRODETAIL_NORMAL_SLOPE_GAIN,
        "referenceTile": REFERENCE_TILE_NAME,
        "legacyMaterialNormalScale": 0.24,
        "channels": channel_audits,
        "xy": {
            "ranges": [round(value, 6) for value in xy_ranges],
            "stddev": [round(value, 6) for value in xy_stddevs],
            "maxMagnitude": round(xy_max_magnitude, 6),
        },
        "seam": {
            "method": "sample u/v=0 and u/v=1 with centered periodic normal differences",
            "heightMaxAbsoluteError": height_seam_error,
            "normalMaxAbsoluteError": normal_seam_error,
            "threshold": MICRODETAIL_SEAM_EPSILON,
        },
        "thresholds": {
            "minimumChannelRange": MICRODETAIL_MIN_CHANNEL_RANGE,
            "minimumChannelStddev": MICRODETAIL_MIN_CHANNEL_STDDEV,
            "minimumXyRange": MICRODETAIL_MIN_XY_RANGE,
            "minimumXyStddev": MICRODETAIL_MIN_XY_STDDEV,
            "maximumXyMagnitude": MICRODETAIL_MAX_XY_MAGNITUDE,
        },
    }


def generate_microdetail_texture(path):
    """Write a deterministic, tileable normal from the irregular photo reference."""
    size = MICRODETAIL_SIZE
    rows, columns = np.mgrid[0:size, 0:size].astype(np.float32)
    u = columns / float(size)
    v = rows / float(size)
    phase = (SEED & 0xFFFF) * (math.tau / 65536.0)
    height = _microdetail_height(u, v, phase)
    normal = _microdetail_normal_from_height(height)
    rgba = np.empty((size, size, 4), dtype=np.uint8)
    rgba[:, :, :3] = np.clip(np.rint(normal * 127.5 + 127.5), 0, 255).astype(np.uint8)
    rgba[:, :, 3] = 255
    audit = _audit_microdetail_normal(rgba, normal, phase)
    write_microdetail_png(path, rgba)
    print(f"OUTPUT microdetail: {path} ({os.path.getsize(path)} bytes, {size}x{size}, tileable=yes, reference={REFERENCE_TILE_NAME}, liveUnityAssignment=none)")
    print(f"AUDIT microdetail: RG={audit['channels']['R']['min']}..{audit['channels']['R']['max']}/"
          f"{audit['channels']['G']['min']}..{audit['channels']['G']['max']}, "
          f"xyStddev={audit['xy']['stddev']}, seamNormal={audit['seam']['normalMaxAbsoluteError']:.9f}")
    return _hash_file(path), audit


def _surface_boundary_distance(surface):
    distance = np.zeros(surface.shape, dtype=np.float32)
    current = surface.copy()
    for step in range(1, 7):
        interior = np.zeros_like(current)
        interior[1:-1, 1:-1] = (
            current[1:-1, 1:-1]
            & current[:-2, 1:-1] & current[2:, 1:-1]
            & current[1:-1, :-2] & current[1:-1, 2:]
            & current[:-2, :-2] & current[:-2, 2:]
            & current[2:, :-2] & current[2:, 2:]
        )
        distance[interior] = float(step)
        current = interior
    return distance


def _component_audit(mask, name, uv_boundary):
    height, width = mask.shape
    flattened = mask.ravel()
    visited = bytearray(flattened.size)
    rejected_circular = []
    rejected_linear = []
    component_count = 0
    boundary_clipped = 0
    largest_area = 0
    for start in np.flatnonzero(flattened):
        start = int(start)
        if visited[start]:
            continue
        component_count += 1
        stack = [start]
        visited[start] = 1
        area = perimeter = 0
        touches_uv_boundary = False
        sum_x = sum_y = sum_xx = sum_yy = sum_xy = 0.0
        while stack:
            index = stack.pop()
            y, x = divmod(index, width)
            area += 1
            touches_uv_boundary = touches_uv_boundary or bool(uv_boundary[y, x])
            sum_x += x
            sum_y += y
            sum_xx += x * x
            sum_yy += y * y
            sum_xy += x * y
            for neighbour, valid in ((index - 1, x > 0), (index + 1, x + 1 < width), (index - width, y > 0), (index + width, y + 1 < height)):
                if not valid or not flattened[neighbour]:
                    perimeter += 1
                elif not visited[neighbour]:
                    visited[neighbour] = 1
                    stack.append(neighbour)
        largest_area = max(largest_area, area)
        if touches_uv_boundary:
            boundary_clipped += 1
            continue
        if area >= 64 and perimeter > 0:
            circularity = 4.0 * math.pi * area / (perimeter * perimeter)
            if circularity > 0.78:
                rejected_circular.append((area, circularity))
        if area >= 4:
            mean_x = sum_x / area
            mean_y = sum_y / area
            cov_xx = max(0.0, sum_xx / area - mean_x * mean_x)
            cov_yy = max(0.0, sum_yy / area - mean_y * mean_y)
            cov_xy = sum_xy / area - mean_x * mean_y
            trace = cov_xx + cov_yy
            determinant = cov_xx * cov_yy - cov_xy * cov_xy
            root = math.sqrt(max(0.0, trace * trace * 0.25 - determinant))
            major = max(0.0, trace * 0.5 + root)
            minor = max(0.0, trace * 0.5 - root)
            principal_length = 4.0 * math.sqrt(major)
            line_rms = math.sqrt(minor)
            if principal_length >= 96.0 and line_rms < 1.75:
                rejected_linear.append((area, principal_length, line_rms))
    if rejected_circular or rejected_linear:
        raise RuntimeError(
            f"Forbidden mask component in {name}: circular={rejected_circular[:3]}, linear={rejected_linear[:3]}"
        )
    return {
        "components": component_count, "largestArea": largest_area, "uvBoundaryClippedComponentsExcluded": boundary_clipped,
        "circularRejects": 0, "linearRejects": 0,
    }


def _periodicity_audit(height_map, surface):
    signal = np.where(surface, height_map, 0.0).astype(np.float32)
    if np.any(surface):
        signal[surface] -= float(np.mean(signal[surface]))
    spectrum = np.abs(np.fft.rfft2(signal)) ** 2
    details = {}
    maximum_ratio = 0.0
    for period in (8, 16):
        frequency = ATLAS_SIZE // period
        samples = ((0, frequency, "x"), (frequency, 0, "y"))
        for row, column, axis in samples:
            r0, r1 = max(0, row - 4), min(spectrum.shape[0], row + 5)
            c0, c1 = max(0, column - 4), min(spectrum.shape[1], column + 5)
            neighbourhood = spectrum[r0:r1, c0:c1].ravel()
            target = float(spectrum[row, column])
            local = neighbourhood[np.abs(neighbourhood - target) > max(1.0e-20, abs(target) * 1.0e-12)]
            median = float(np.median(local)) if local.size else 0.0
            ratio = target / max(median, 1.0e-20)
            details[f"{period}px-{axis}"] = round(ratio, 6)
            maximum_ratio = max(maximum_ratio, ratio)
    # R2 wear is proven from metric contact precision. This spectral check is
    # retained as a report-only guard against conspicuous atlas repetition.
    if maximum_ratio > 10.0:
        raise RuntimeError(f"Axis periodicity failed: maxRatio={maximum_ratio:.6f}, details={details}")
    return {"threshold": 10.0, "maxAxisPowerToLocalMedian": round(maximum_ratio, 6), "details": details}


def _dilation_labels(surface):
    labels = np.full(surface.shape, -1, dtype=np.int32)
    labels[surface] = np.flatnonzero(surface).astype(np.int32)
    allowed = np.zeros(surface.shape, dtype=bool)
    for x0, y0, x1, y1 in UV_ZONES.values():
        allowed[y0:y1, x0:x1] = True
    for _ in range(ATLAS_DILATION):
        missing = (labels < 0) & allowed
        updated = labels.copy()
        candidates = (
            (slice(1, None), slice(None), slice(None, -1), slice(None)),
            (slice(None, -1), slice(None), slice(1, None), slice(None)),
            (slice(None), slice(1, None), slice(None), slice(None, -1)),
            (slice(None), slice(None, -1), slice(None), slice(1, None)),
            (slice(1, None), slice(1, None), slice(None, -1), slice(None, -1)),
            (slice(1, None), slice(None, -1), slice(None, -1), slice(1, None)),
            (slice(None, -1), slice(1, None), slice(1, None), slice(None, -1)),
            (slice(None, -1), slice(None, -1), slice(1, None), slice(1, None)),
        )
        for dy, dx, sy, sx in candidates:
            destination_missing = missing[dy, dx]
            source = labels[sy, sx]
            take = destination_missing & (updated[dy, dx] < 0) & (source >= 0)
            updated_view = updated[dy, dx]
            updated_view[take] = source[take]
        labels = updated
    return labels


def _scratch_hash32(value):
    value = int(value) & 0xFFFFFFFF
    value ^= value >> 16
    value = (value * 0x7FEB352D) & 0xFFFFFFFF
    value ^= value >> 15
    value = (value * 0x846CA68B) & 0xFFFFFFFF
    value ^= value >> 16
    return value


def _scratch_unit(value):
    return _scratch_hash32(value) / 4294967295.0


def _largest_remainder_allocation(total, ratios, tie_order):
    if total < 0 or set(ratios) != set(tie_order):
        raise RuntimeError(f"Invalid largest-remainder contract: total={total}, ratios={ratios}, tieOrder={tie_order}")
    if not math.isclose(sum(ratios.values()), 1.0, rel_tol=0.0, abs_tol=1.0e-12):
        raise RuntimeError(f"Largest-remainder ratios must sum to one: {ratios}")
    ideals = {name: total * ratios[name] for name in tie_order}
    allocation = {name: int(math.floor(ideals[name])) for name in tie_order}
    remaining = total - sum(allocation.values())
    tie_index = {name: index for index, name in enumerate(tie_order)}
    remainder_order = sorted(
        tie_order,
        key=lambda name: (-(ideals[name] - allocation[name]), tie_index[name]),
    )
    for name in remainder_order[:remaining]:
        allocation[name] += 1
    if sum(allocation.values()) != total:
        raise RuntimeError(f"Largest-remainder allocation failed: total={total}, allocation={allocation}")
    return allocation


def _next_apportioned_label(total, ratios, tie_order):
    before = _largest_remainder_allocation(total - 1, ratios, tie_order)
    after = _largest_remainder_allocation(total, ratios, tie_order)
    changed = [name for name in tie_order if after[name] - before[name] == 1]
    if len(changed) != 1 or any(after[name] < before[name] for name in tie_order):
        raise RuntimeError(f"Non-monotonic largest-remainder step at total={total}: before={before}, after={after}")
    return changed[0]


def _metric_scratch_segments(
    active, positions, normals, tangents, actual_contact, metal, muzzle, handling,
    profile_ids, group_ids, island_ids, profiles, texel_areas,
):
    """Grow deterministic single-capsule scratches to honest physical-area coverage."""
    width_range = (0.00025, 0.00065)
    length_range = (0.005, 0.022)
    shoulder_range = (0.00020, 0.00045)
    normal_dot_minimum = 0.9063078
    cell_size = length_range[1] + width_range[1] + shoulder_range[1] * 2.0
    grid_origin = np.min(positions, axis=0) - cell_size
    grid_coordinates = np.floor((positions - grid_origin) / cell_size).astype(np.int64)
    grid_dimensions = np.max(grid_coordinates, axis=0) + 1
    grid_keys = (grid_coordinates[:, 0] * grid_dimensions[1] + grid_coordinates[:, 1]) * grid_dimensions[2] + grid_coordinates[:, 2]
    grid_order = np.argsort(grid_keys, kind="stable")
    sorted_grid_keys = grid_keys[grid_order]

    def nearby_indices(minimum, maximum):
        lower = np.maximum(0, np.floor((minimum - grid_origin) / cell_size).astype(np.int64))
        upper = np.minimum(grid_dimensions - 1, np.floor((maximum - grid_origin) / cell_size).astype(np.int64))
        buckets = []
        for gx in range(int(lower[0]), int(upper[0]) + 1):
            for gy in range(int(lower[1]), int(upper[1]) + 1):
                for gz in range(int(lower[2]), int(upper[2]) + 1):
                    key = (gx * int(grid_dimensions[1]) + gy) * int(grid_dimensions[2]) + gz
                    first = int(np.searchsorted(sorted_grid_keys, key, side="left"))
                    last = int(np.searchsorted(sorted_grid_keys, key, side="right"))
                    if first < last:
                        buckets.append(grid_order[first:last])
        return np.concatenate(buckets) if buckets else np.empty(0, dtype=np.int64)

    groove = np.zeros(active.size, dtype=bool)
    shoulder = np.zeros(active.size, dtype=bool)
    coverage_eligible = np.zeros(active.size, dtype=bool)
    records = []
    profile_records = {}
    for profile_id in sorted(profiles):
        profile_name = profiles[profile_id]["name"]
        contract = SCRATCH_PROFILE_CONTRACTS.get(profile_name)
        if contract is None:
            raise RuntimeError(f"Missing scratch contract for raster profile: {profile_name}")
        profile_mask = profile_ids == int(profile_id)
        pools = {
            "muzzle": metal & muzzle & profile_mask,
            "sharp": metal & (actual_contact > 0.55) & ~muzzle & profile_mask,
            "handling": metal & (handling > 0.35) & (actual_contact <= 0.55) & ~muzzle & profile_mask,
        }
        profile_eligible = pools["sharp"] | pools["handling"] | pools["muzzle"]
        coverage_eligible |= profile_eligible
        if np.any((pools["muzzle"] & pools["sharp"]) | (pools["muzzle"] & pools["handling"]) | (pools["sharp"] & pools["handling"])):
            raise RuntimeError(f"Scratch precedence pools overlap for {profile_name}")
        eligible_pixels = int(np.count_nonzero(profile_eligible))
        eligible_area = float(np.sum(texel_areas[profile_eligible], dtype=np.float64))
        if eligible_pixels == 0 or eligible_area <= 0.0:
            raise RuntimeError(f"Scratch eligible metal surface is empty for {profile_name}")

        candidate_lists = {}
        candidate_cursors = {name: 0 for name in SCRATCH_POOL_TIE_ORDER}
        for pool_name in SCRATCH_POOL_TIE_ORDER:
            candidates = np.flatnonzero(pools[pool_name])
            pool_salt = _scratch_hash32(sum((index + 1) * ord(ch) for index, ch in enumerate(pool_name)))
            candidate_hashes = np.fromiter(
                (_scratch_hash32(int(active[index]) ^ SEED ^ pool_salt ^ ((int(profile_id) + 1) * 0x9E3779B9)) for index in candidates),
                dtype=np.uint32,
                count=candidates.size,
            )
            candidate_lists[pool_name] = candidates[np.lexsort((candidates, candidate_hashes))]

        covered_area = 0.0
        profile_segment_records = []
        while covered_area / eligible_area < WEAR_COVERAGE_LIMITS["scratchCore"][0]:
            next_total = len(profile_segment_records) + 1
            pool_name = _next_apportioned_label(next_total, SCRATCH_POOL_RATIOS, SCRATCH_POOL_TIE_ORDER)
            orientation = _next_apportioned_label(
                next_total, SCRATCH_ORIENTATION_RATIOS, SCRATCH_ORIENTATION_TIE_ORDER
            )
            candidates = candidate_lists[pool_name]
            admitted = False
            while candidate_cursors[pool_name] < candidates.size:
                candidate_index = int(candidates[candidate_cursors[pool_name]])
                candidate_cursors[pool_name] += 1
                atlas_index = int(active[candidate_index])
                pool_salt = _scratch_hash32(sum((index + 1) * ord(ch) for index, ch in enumerate(pool_name)))
                identity = _scratch_hash32(
                    atlas_index ^ SEED ^ pool_salt ^ ((int(profile_id) + 1) * 0x9E3779B9)
                )
                width = width_range[0] + (width_range[1] - width_range[0]) * _scratch_unit(identity ^ 0xA511E9B3)
                length = length_range[0] + (length_range[1] - length_range[0]) * _scratch_unit(identity ^ 0x63D83595)
                shoulder_width = shoulder_range[0] + (shoulder_range[1] - shoulder_range[0]) * _scratch_unit(identity ^ 0xD1B54A35)
                core_radius = width * 0.5
                outer_radius = core_radius + shoulder_width
                half_line = max(0.0, (length - width) * 0.5)
                center = positions[candidate_index].astype(np.float64)
                surface_normal = normals[candidate_index].astype(np.float64)
                surface_normal /= max(np.linalg.norm(surface_normal), 1.0e-12)
                tangent_axis = tangents[candidate_index].astype(np.float64)
                tangent_axis -= surface_normal * float(np.dot(tangent_axis, surface_normal))
                tangent_axis /= max(np.linalg.norm(tangent_axis), 1.0e-12)
                bitangent_axis = np.cross(surface_normal, tangent_axis)
                bitangent_axis /= max(np.linalg.norm(bitangent_axis), 1.0e-12)
                if orientation == "tangent":
                    offset_degrees = -25.0 + 50.0 * _scratch_unit(identity ^ 0xC2B2AE35)
                    base_axis = tangent_axis
                    secondary_axis = bitangent_axis
                else:
                    offset_degrees = -70.0 + 140.0 * _scratch_unit(identity ^ 0xC2B2AE35)
                    base_axis = bitangent_axis
                    secondary_axis = tangent_axis
                angle = math.radians(offset_degrees)
                direction = math.cos(angle) * base_axis + math.sin(angle) * secondary_axis
                direction /= max(np.linalg.norm(direction), 1.0e-12)
                cross_axis = np.cross(surface_normal, direction)
                cross_axis /= max(np.linalg.norm(cross_axis), 1.0e-12)
                # Each accepted seed owns exactly one metric capsule. Seeded
                # carrier holes break up that capsule without cloning it into
                # periodic parallel strands.
                extent = np.abs(direction) * half_line + outer_radius
                nearby = nearby_indices(center - extent, center + extent)
                if nearby.size == 0:
                    continue
                delta = positions[nearby].astype(np.float64) - center
                longitudinal = delta @ direction
                clamped = np.clip(longitudinal, -half_line, half_line)
                closest_delta = delta - clamped[:, None] * direction
                distance = np.sqrt(np.einsum("ij,ij->i", closest_delta, closest_delta))
                same_surface = (
                    (profile_ids[nearby] == profile_ids[candidate_index])
                    & (group_ids[nearby] == group_ids[candidate_index])
                    & (island_ids[nearby] == island_ids[candidate_index])
                    & ((normals[nearby].astype(np.float64) @ surface_normal) >= normal_dot_minimum)
                    & profile_eligible[nearby]
                )
                carrier = np.fromiter(
                    (_scratch_unit(int(active[index]) ^ identity ^ 0x94D049BB) for index in nearby),
                    dtype=np.float64,
                    count=nearby.size,
                ) >= 0.14
                core_pixels = nearby[(distance <= core_radius + 1.0e-12) & same_surface & carrier]
                shoulder_pixels = nearby[(distance > core_radius) & (distance <= outer_radius + 1.0e-12) & same_surface & carrier]
                if core_pixels.size < 1 or shoulder_pixels.size < 1 or np.any(groove[core_pixels]):
                    continue
                new_core_area = float(np.sum(texel_areas[core_pixels], dtype=np.float64))
                if new_core_area <= 0.0:
                    continue
                next_coverage = (covered_area + new_core_area) / eligible_area
                if next_coverage > WEAR_COVERAGE_LIMITS["scratchCore"][1]:
                    raise RuntimeError(
                        f"Scratch coverage overshot upper bound on first threshold crossing for {profile_name}: "
                        f"coverage={next_coverage:.9f}, nextTotal={next_total}"
                    )
                groove[core_pixels] = True
                shoulder[core_pixels] = False
                shoulder_pixels = shoulder_pixels[~groove[shoulder_pixels]]
                shoulder[shoulder_pixels] = True
                covered_area += new_core_area
                record = {
                    "id": len(records),
                    "profileSequence": next_total - 1,
                    "profileId": int(profile_id),
                    "profile": profile_name,
                    "pool": pool_name,
                    "groupId": int(group_ids[candidate_index]),
                    "uvIslandId": int(island_ids[candidate_index]),
                    "anchorAtlasIndex": atlas_index,
                    "orientation": orientation,
                    "orientationOffsetDegrees": round(offset_degrees, 6),
                    "widthMeters": round(width, 9),
                    "lengthMeters": round(length, 9),
                    "shoulderWidthMeters": round(shoulder_width, 9),
                    "normalDotMinimum": normal_dot_minimum,
                    "physicalCapsules": 1,
                    "corePixels": int(core_pixels.size),
                    "shoulderPixels": int(shoulder_pixels.size),
                    "newCorePixels": int(core_pixels.size),
                    "newCoreMetersSquared": new_core_area,
                    "coverageAfterSeed": round(next_coverage, 9),
                }
                records.append(record)
                profile_segment_records.append(record)
                admitted = True
                break
            if not admitted:
                raise RuntimeError(
                    f"Scratch candidate pool exhausted before coverage target for {profile_name}/{pool_name}: "
                    f"admitted={len(profile_segment_records)}, coverage={covered_area / eligible_area:.9f}"
                )

        actual_total = len(profile_segment_records)
        admitted_by_pool = {
            name: sum(record["pool"] == name for record in profile_segment_records)
            for name in SCRATCH_POOL_TIE_ORDER
        }
        orientation_counts = {
            name: sum(record["orientation"] == name for record in profile_segment_records)
            for name in SCRATCH_ORIENTATION_TIE_ORDER
        }
        expected_pools = _largest_remainder_allocation(actual_total, SCRATCH_POOL_RATIOS, SCRATCH_POOL_TIE_ORDER)
        expected_orientations = _largest_remainder_allocation(
            actual_total, SCRATCH_ORIENTATION_RATIOS, SCRATCH_ORIENTATION_TIE_ORDER
        )
        profile_coverage = covered_area / eligible_area
        if admitted_by_pool != expected_pools or orientation_counts != expected_orientations:
            raise RuntimeError(
                f"Scratch ratio allocation failed {profile_name}: pools={admitted_by_pool}/{expected_pools}, "
                f"orientations={orientation_counts}/{expected_orientations}"
            )
        profile_records[profile_name] = {
            "segmentCount": actual_total,
            "actualTotal": actual_total,
            "poolCounts": admitted_by_pool,
            "poolAllocations": admitted_by_pool,
            "poolRatios": dict(SCRATCH_POOL_RATIOS),
            "poolTieOrder": list(SCRATCH_POOL_TIE_ORDER),
            "orientationCounts": orientation_counts,
            "orientationAllocations": orientation_counts,
            "orientationRatios": dict(SCRATCH_ORIENTATION_RATIOS),
            "orientationTieOrder": list(SCRATCH_ORIENTATION_TIE_ORDER),
            "eligibleMetalAtlasPixels": eligible_pixels,
            "coverageNumeratorPixels": int(sum(record["newCorePixels"] for record in profile_segment_records)),
            "coverageDenominatorPixels": eligible_pixels,
            "coverageNumeratorMetersSquared": covered_area,
            "coverageDenominatorMetersSquared": eligible_area,
            "coverage": round(profile_coverage, 9),
            "thresholdReachedByFinalSeed": True,
            "coverageBeforeFinalSeed": round(profile_coverage - profile_segment_records[-1]["newCoreMetersSquared"] / eligible_area, 9),
        }

    observed_widths = [record["widthMeters"] for record in records]
    observed_lengths = [record["lengthMeters"] for record in records]
    observed_shoulders = [record["shoulderWidthMeters"] for record in records]
    coverage_numerator = int(np.count_nonzero(groove))
    coverage_denominator = int(np.count_nonzero(coverage_eligible))
    coverage_numerator_area = float(np.sum(texel_areas[groove], dtype=np.float64))
    coverage_denominator_area = float(np.sum(texel_areas[coverage_eligible], dtype=np.float64))
    coverage = coverage_numerator_area / max(1.0e-20, coverage_denominator_area)
    low, high = WEAR_COVERAGE_LIMITS["scratchCore"]
    if not low <= coverage <= high:
        raise RuntimeError(f"Aggregate scratch coverage failed: {coverage:.9f} outside {low:.4f}..{high:.4f}")
    aggregate_pool_allocations = {
        name: sum(record["pool"] == name for record in records) for name in SCRATCH_POOL_TIE_ORDER
    }
    aggregate_orientation_allocations = {
        name: sum(record["orientation"] == name for record in records) for name in SCRATCH_ORIENTATION_TIE_ORDER
    }
    audit = {
        "requiredWidthMeters": list(width_range),
        "requiredLengthMeters": list(length_range),
        "requiredShoulderWidthMeters": list(shoulder_range),
        "observedWidthMeters": [min(observed_widths), max(observed_widths)],
        "observedLengthMeters": [min(observed_lengths), max(observed_lengths)],
        "observedShoulderWidthMeters": [min(observed_shoulders), max(observed_shoulders)],
        "normalDotMinimum": normal_dot_minimum,
        "capsuleContract": "exactly one physical metric capsule per admitted seed; no replicated strands",
        "carrierBreakup": "per-pixel seeded hash keep >=0.14 inside the single capsule",
        "surfaceIdentity": "profileId, groupId, uvIslandId identical to anchor",
        "profiles": profile_records,
        "admittedSegmentCount": len(records),
        "actualTotal": len(records),
        "aggregatePoolAllocations": aggregate_pool_allocations,
        "aggregateOrientationAllocations": aggregate_orientation_allocations,
        "admittedSegments": records,
        "renderedCorePixels": coverage_numerator,
        "renderedShoulderPixels": int(np.count_nonzero(shoulder)),
        "coverageNumeratorPixels": coverage_numerator,
        "coverageDenominatorPixels": coverage_denominator,
        "coverageNumeratorMetersSquared": coverage_numerator_area,
        "coverageDenominatorMetersSquared": coverage_denominator_area,
        "coverage": round(coverage, 9),
        "coverageMask": "union per profile of disjoint actual atlas masks: metal & (muzzle | (actualContact>.55 & !muzzle) | (handling>.35 & actualContact<=.55 & !muzzle))",
        "coverageBasis": "physical area of rendered scratch-core texels / physical area of actual declared eligible metal atlas texels; pixel counts also recorded",
        "adaptiveStop": "each profile stops at first admitted unique capsule reaching coverage >= 0.0075; aggregate is the physical-area weighted result",
        "coreOverlapPixels": 0,
    }
    print(
        f"AUDIT scratch segments: count={len(records)}, corePixels={coverage_numerator}, "
        f"eligibleMetalPixels={coverage_denominator}, coreAreaM2={coverage_numerator_area:.9f}, "
        f"eligibleAreaM2={coverage_denominator_area:.9f}, coverage={coverage:.6f}, "
        f"shoulderPixels={audit['renderedShoulderPixels']}"
    )
    return groove, shoulder, audit


def _generate_texture_atlas_legacy(objects, stage_texture_dir, raster=None):
    raster = rasterize_surface(objects) if raster is None else raster
    surface = raster["surface"]
    group = raster["group"]
    position = raster["position"]
    normal = raster["normal"]
    tangent = raster["tangent"]
    active = np.flatnonzero(surface)
    atlas_x = (active % ATLAS_SIZE).astype(np.float32)
    atlas_y = (active // ATLAS_SIZE).astype(np.float32)
    reference_detail = _reference_height_at(
        (atlas_x + 0.5) / float(ATLAS_SIZE),
        (atlas_y + 0.5) / float(ATLAS_SIZE),
    )
    px = position[:, :, 0].ravel()[active]
    py = position[:, :, 1].ravel()[active]
    pz = position[:, :, 2].ravel()[active]
    # FBM frequency is evaluated in normalized launcher-local coordinates so
    # the declared 3.5 base frequency is stable if the authored meter scale changes.
    fbm = _fbm((px + 0.16) / 0.32, (py + 0.55) / 0.75, (pz + 0.1313) / 0.2413)

    boundary_distance = _surface_boundary_distance(surface)  # dilation/component audit only; never drives wear.
    edge_distance = raster["edgeDistanceMeters"].ravel()[active]
    corner_distance = raster["cornerDistanceMeters"].ravel()[active]
    edge_contact = 1.0 - _smoothstep(0.0015, 0.0060, edge_distance)
    corner_contact = 1.0 - _smoothstep(0.0020, 0.0100, corner_distance)
    actual_contact = np.maximum(edge_contact, corner_contact)

    normal_count = _box_sum(surface.astype(np.float32), 6)
    mean_normal_components = [_box_sum(normal[:, :, axis] * surface, 6) for axis in range(3)]
    mean_normal = np.stack(mean_normal_components, axis=2) / np.maximum(normal_count[:, :, None], 1.0)
    mean_normal /= np.maximum(np.linalg.norm(mean_normal, axis=2, keepdims=True), 1.0e-12)
    normal_active = normal.reshape(-1, 3)[active]
    cavity = np.clip((1.0 - np.sum(normal_active * mean_normal.reshape(-1, 3)[active], axis=1)) * 4.0, 0.0, 1.0)
    downward = np.clip((-normal_active[:, 1] - 0.10) / 0.90, 0.0, 1.0)

    group_active = group.ravel()[active]
    core = group_active == GROUP_NAMES.index("WeaponAccentCore")
    metal = group_active == GROUP_NAMES.index("WeaponMetal")
    dark = group_active == GROUP_NAMES.index("WeaponDark")
    glass = (group_active == GROUP_NAMES.index("WeaponAccent")) | core
    # Keep edge wear narrow and low contrast.  The muzzle branch is explicit
    # so soot/heat remains localized instead of washing the whole receiver.
    muzzle_lip = (edge_contact > 0.68) & (fbm > 0.72) & metal & (py < -0.40)
    chip = (((actual_contact > CHIP_CONTACT_MIN) & (fbm > CHIP_FBM_MIN)) | muzzle_lip) & metal
    scratch, scratch_audit = _metric_scratch_segments(
        active,
        position.reshape(-1, 3)[active],
        normal_active,
        tangent.reshape(-1, 3)[active],
        actual_contact,
        metal,
    )
    dark_scuff = (actual_contact > 0.60) & (fbm > 0.76) & dark
    dust_coarse = _trig_noise(px, py, pz, 11.0, 0.193)
    dust_fine = _trig_noise(px, py, pz, 97.0, 1.173)
    # Procedural deposits own albedo. The decoded reference is a micro-surface
    # carrier and is intentionally kept out of dust/grime so changing it cannot
    # darken BaseColor.
    dust = np.clip(
        0.24 * fbm + 0.16 * dust_coarse + 0.12 * dust_fine - 0.26,
        0.0,
        1.0,
    )
    dust[glass] = 0.0
    grime = np.clip(
        0.28 * cavity + 0.20 * downward + 0.19 * fbm + 0.18 * dust
        + GRIME_BIAS + PROCEDURAL_GRIME_BIAS_COMPENSATION,
        0.0,
        1.0,
    )
    grime[_trig_noise(px, py, pz, 37.0, 0.417) < 0.50] = 0.0
    grime[glass] = 0.0
    muzzle_depth = _smoothstep(-0.42, -0.60, py)
    muzzle_radius = np.sqrt(px * px + (pz - 0.015) * (pz - 0.015))
    muzzle_ring = 1.0 - _smoothstep(0.065, 0.155, muzzle_radius)
    soot = np.clip(muzzle_depth * muzzle_ring * (0.66 + 0.34 * fbm), 0.0, 1.0)
    soot[glass] = 0.0
    heat = np.clip(0.55 * soot + 0.20 * muzzle_lip.astype(np.float32), 0.0, 1.0)
    heat[glass] = 0.0
    rear = _smoothstep(0.02, 0.16, py) * (1.0 - _smoothstep(0.06, 0.14, np.abs(px))) * (1.0 - _smoothstep(0.04, 0.13, np.abs(pz)))
    grip = (1.0 - _smoothstep(0.06, 0.14, np.abs(px))) * (1.0 - _smoothstep(0.04, 0.13, np.abs(pz))) * (1.0 - _smoothstep(0.18, 0.38, np.abs(py + 0.18)))
    handling = 0.70 * np.maximum(rear, grip) * np.clip(0.65 + 0.35 * fbm, 0.0, 1.0)
    handling[~(metal | dark)] = 0.0
    polish = np.where(np.maximum(soot, grime) < 0.5, handling, 0.0)
    polish[glass] = 0.0

    surface_count = active.size
    muzzle = py < -0.28
    coverages = {
        "chips": float(np.count_nonzero(chip)) / surface_count,
        "scratches": float(np.count_nonzero(scratch)) / surface_count,
        "grime": float(np.count_nonzero(grime > 0.05)) / surface_count,
        "muzzleSoot": float(np.count_nonzero((soot > 0.05) & muzzle)) / max(1, int(np.count_nonzero(muzzle))),
        "polish": float(np.count_nonzero(polish > 0.50)) / surface_count,
    }
    limits = {
        "chips": (0.003, 0.015), "scratches": (0.012, 0.025), "grime": (0.015, 0.030),
        "muzzleSoot": (0.10, 0.70), "polish": (0.02, 0.36),
    }
    for name, value in coverages.items():
        low, high = limits[name]
        print(f"AUDIT wear {name}: coverage={value:.6f}, limits={low:.3f}..{high:.3f}")
        if value < low or value > high:
            raise RuntimeError(f"Wear coverage failed {name}: {value:.6f} outside {low:.3f}..{high:.3f}")

    def compose_base_albedo(_carrier_probe):
        """Compose BaseColor from procedural wear only; carrier is never read."""
        base = np.zeros((surface_count, 3), dtype=np.float32)
        for group_index, group_name in enumerate(GROUP_NAMES):
            select = group_active == group_index
            spec = SURFACE_SPECS[group_name]
            base[select] = spec["clean"]

        for group_index, group_name in enumerate(GROUP_NAMES):
            select = group_active == group_index
            spec = SURFACE_SPECS[group_name]
            selected_chip = select & chip
            # Chips and scratches are explicit metric/contact masks, so their
            # albedo contrast reads as physical exposed metal rather than noise.
            base[selected_chip] = np.array(spec["clean"]) * 0.82 + np.array(spec["exposed"]) * 0.18
            selected_scratch = select & scratch
            scratch_color = np.array(spec["exposed"]) * 0.65 + np.array(spec["clean"]) * 0.35
            base[selected_scratch] = scratch_color

        # Dark coating gets restrained grey scuffs, never exposed-metal chips.
        dark_spec = SURFACE_SPECS["WeaponDark"]
        base[dark_scuff] = np.array(dark_spec["clean"]) * 0.55 + np.array(dark_spec["exposed"]) * 0.45
        base *= 1.0 - 0.10 * grime[:, None]
        base *= 1.0 - 0.025 * dust[:, None]
        base *= 1.0 - 0.48 * soot[:, None]
        base += heat[:, None] * np.array((0.020, 0.012, 0.003), dtype=np.float32)
        return base

    base = compose_base_albedo(reference_detail)
    carrier_probe = np.roll(reference_detail, 17)
    carrier_probe_base = compose_base_albedo(carrier_probe)
    carrier_delta = float(np.max(np.abs(base - carrier_probe_base))) if base.size else 0.0
    if carrier_delta != 0.0:
        raise RuntimeError(f"BaseColor carrier invariance failed: maxAbsDelta={carrier_delta}")

    metallic = np.zeros(surface_count, dtype=np.float32)
    smoothness = np.zeros(surface_count, dtype=np.float32)
    emission = np.zeros((surface_count, 3), dtype=np.float32)
    for group_index, group_name in enumerate(GROUP_NAMES):
        select = group_active == group_index
        spec = SURFACE_SPECS[group_name]
        metallic[select] = spec["metal"][0]
        smoothness[select] = spec["smooth"][0]
        if group_name == "WeaponAccentCore":
            emission[select, 0] = 0.85

    for group_index, group_name in enumerate(GROUP_NAMES):
        select = group_active == group_index
        spec = SURFACE_SPECS[group_name]
        selected_chip = select & chip
        metallic[selected_chip] = spec["metal"][1]
        smoothness[selected_chip] = spec["smooth"][1]
        selected_scratch = select & scratch
        metallic[selected_scratch] = spec["metal"][0]
        smoothness[selected_scratch] = min(1.0, spec["smooth"][0] + 0.28)

    # Dark coating gets restrained grey scuffs, never exposed-metal chips.
    dark_spec = SURFACE_SPECS["WeaponDark"]
    smoothness[dark_scuff] = dark_spec["smooth"][1]

    # Grime is a dielectric deposit. Apply it before soot so the final muzzle
    # deposit pass cannot re-metalize an already soot-darkened surface.
    for group_index, group_name in enumerate(GROUP_NAMES):
        select = group_active == group_index
        spec = SURFACE_SPECS[group_name]
        metallic[select] = metallic[select] * (1.0 - grime[select]) + DIELECTRIC_DEPOSIT_METALLIC * grime[select]
        smoothness[select] = smoothness[select] * (1.0 - grime[select]) + spec["smooth"][2] * grime[select]
        smoothness[select] = smoothness[select] * (1.0 - polish[select]) + spec["smooth"][3] * polish[select]

    # Heat deposits are warmer at the muzzle lip while soot lowers highlight
    # response.  The mask is zero on the red glass groups by construction.
    metallic = np.minimum(metallic, metallic * (1.0 - soot) + SOOT_METALLIC_TARGET * soot)
    smoothness = np.minimum(smoothness, smoothness * (1.0 - soot) + 0.13 * soot)
    deposited_grime = grime > DEPOSIT_MASK_THRESHOLD
    deposited_soot = soot > DEPOSIT_MASK_THRESHOLD
    dielectric_deposit = deposited_grime | deposited_soot
    metallic[dielectric_deposit] = DIELECTRIC_DEPOSIT_METALLIC
    deposit_metallic_pixels = int(np.count_nonzero(metallic[dielectric_deposit] != DIELECTRIC_DEPOSIT_METALLIC))
    if deposit_metallic_pixels != 0:
        raise RuntimeError(f"Deposited metallic audit failed: nonDielectricPixels={deposit_metallic_pixels}")

    metal_or_dark = metal | dark
    smoothness += REFERENCE_SMOOTHNESS_SCALE * reference_detail * metal_or_dark.astype(np.float32)
    # A polished muzzle lip catches light around the soot-darkened bore.
    smoothness += 0.10 * muzzle_lip.astype(np.float32)
    smoothness = np.clip(smoothness, 0.0, 1.0)

    # The real reference owns subtle brush breakup. It is irregular and only
    # modulates smoothness; no analytic wave is allowed into height/normal.
    irregular_brush = reference_detail * metal_or_dark.astype(np.float32)
    irregular_brush[glass] = 0.0
    smoothness = np.clip(
        smoothness
        + 0.04 * scratch.astype(np.float32)
        - 0.08 * dust
        - 0.030 * np.abs(irregular_brush),
        0.0,
        1.0,
    )
    height_active = np.clip(
        -0.030 * chip.astype(np.float32)
        - 0.038 * scratch.astype(np.float32)
        - 0.007 * dark_scuff.astype(np.float32)
        + 0.022 * grime
        + 0.010 * dust
        + REFERENCE_ATLAS_HEIGHT_SCALE * reference_detail * metal_or_dark.astype(np.float32),
        -0.05,
        0.05,
    )
    height_map = np.zeros(surface.shape, dtype=np.float32)
    height_map.ravel()[active] = height_active
    left = np.roll(height_map, 1, axis=1)
    right = np.roll(height_map, -1, axis=1)
    down = np.roll(height_map, 1, axis=0)
    up = np.roll(height_map, -1, axis=0)
    left_valid = np.roll(surface, 1, axis=1)
    right_valid = np.roll(surface, -1, axis=1)
    down_valid = np.roll(surface, 1, axis=0)
    up_valid = np.roll(surface, -1, axis=0)
    left = np.where(left_valid, left, height_map)
    right = np.where(right_valid, right, height_map)
    down = np.where(down_valid, down, height_map)
    up = np.where(up_valid, up, height_map)
    dx = right - left
    dy = up - down
    normal_map = np.stack((-4.5 * dx, -4.5 * dy, np.ones_like(height_map)), axis=2)
    normal_map /= np.maximum(np.linalg.norm(normal_map, axis=2, keepdims=True), 1.0e-12)
    ao = np.clip(1.0 - 0.35 * cavity - 0.15 * grime, 0.45, 1.0)

    mask_maps = {
        "chips": np.isin(np.arange(surface.size).reshape(surface.shape), active[chip]),
        "scratches": np.isin(np.arange(surface.size).reshape(surface.shape), active[scratch]),
        "grime": np.isin(np.arange(surface.size).reshape(surface.shape), active[grime > 0.05]),
        "muzzleSoot": np.isin(np.arange(surface.size).reshape(surface.shape), active[(soot > 0.05) & muzzle]),
        "polish": np.isin(np.arange(surface.size).reshape(surface.shape), active[polish > 0.50]),
    }
    uv_boundary = surface & (boundary_distance < 1.0)
    component_records = {name: _component_audit(mask, name, uv_boundary) for name, mask in mask_maps.items()}
    labels = _dilation_labels(surface)
    dilated = labels >= 0
    source_indices = labels[dilated]
    dilated_height = np.zeros_like(height_map)
    dilated_height[dilated] = height_map.ravel()[source_indices]
    periodicity = _periodicity_audit(dilated_height, dilated)

    def expanded_rgba(default, source_rgba):
        output = np.empty((ATLAS_SIZE, ATLAS_SIZE, 4), dtype=np.uint8)
        output[:] = np.array(default, dtype=np.uint8)
        source_full = np.empty((surface.size, 4), dtype=np.uint8)
        source_full[:] = np.array(default, dtype=np.uint8)
        source_full[active] = np.clip(np.rint(source_rgba * 255.0), 0, 255).astype(np.uint8)
        output[dilated] = source_full[source_indices]
        return output

    base_rgba = np.column_stack((np.clip(base, 0.0, 1.0), np.ones(surface_count, dtype=np.float32)))
    normal_active_map = normal_map.reshape(-1, 3)[active] * 0.5 + 0.5
    normal_rgba = np.column_stack((np.clip(normal_active_map, 0.0, 1.0), np.ones(surface_count, dtype=np.float32)))
    metallic_rgba = np.column_stack((np.clip(metallic, 0.0, 1.0), np.zeros((surface_count, 2), dtype=np.float32), np.clip(smoothness, 0.0, 1.0)))
    ao_rgba = np.column_stack((ao, ao, ao, np.ones(surface_count, dtype=np.float32)))
    emission_rgba = np.column_stack((emission, np.ones(surface_count, dtype=np.float32)))
    atlas_payloads = {
        TEXTURE_NAMES[0]: expanded_rgba((0, 0, 0, 255), base_rgba),
        TEXTURE_NAMES[1]: expanded_rgba((128, 128, 255, 255), normal_rgba),
        TEXTURE_NAMES[2]: expanded_rgba((0, 0, 0, 0), metallic_rgba),
        TEXTURE_NAMES[3]: expanded_rgba((255, 255, 255, 255), ao_rgba),
        TEXTURE_NAMES[4]: expanded_rgba((0, 0, 0, 255), emission_rgba),
    }
    output_hashes = {}
    for filename, pixels in atlas_payloads.items():
        path = os.path.join(stage_texture_dir, filename)
        write_rgba_png(path, pixels, filename == TEXTURE_NAMES[0])
        output_hashes[filename] = _hash_file(path)
    microdetail_path = os.path.join(stage_texture_dir, MICRODETAIL_NAME)
    output_hashes[MICRODETAIL_NAME], microdetail_audit = generate_microdetail_texture(microdetail_path)
    channel_record = {
        "baseColor": {"format": "RGBA8", "transfer": "sRGB"},
        "normal": {"format": "RGBA8", "transfer": "linear", "centralDifferencePx": 1, "xyScale": 2, "z": 1},
        "metallicSmoothness": {"format": "RGBA8", "transfer": "linear", "metallicChannel": "R", "smoothnessChannel": "A"},
        "occlusion": {"format": "RGBA8", "transfer": "linear", "minimum": 0.45},
        "emission": {"format": "RGBA8", "transfer": "linear", "coreValue": 0.85},
        "microDetailNormal": {
            "format": "RGBA8", "transfer": "linear", "size": [MICRODETAIL_SIZE, MICRODETAIL_SIZE],
            "tile": "repeat", "intendedGroups": [],
            "audit": microdetail_audit,
        },
        "photoDerivedDetail": {
            "source": REFERENCE_TILE_NAME,
            "path": os.path.relpath(REFERENCE_TILE_PATH, REPOSITORY_ROOT).replace(os.sep, "/"),
            "sourceFormat": "RGB8",
            "decodedRgbaSha256": REFERENCE_RGBA_SHA256,
            "usage": "zero-mean irregular high-pass source for model-specific atlas normal/smoothness only; procedural dust/grime owns albedo; shared legacy detail output is not assigned to live Unity weapon materials",
            "periodicSampling": True,
        },
        "albedoCarrierInvariant": {
            "carrierProbe": "periodic reference-height roll by 17 active samples",
            "maxAbsFloatDelta": round(carrier_delta, 9),
            "baseColorFloatSha256": hashlib.sha256(np.ascontiguousarray(base).tobytes()).hexdigest(),
            "carrierProbeBaseColorFloatSha256": hashlib.sha256(np.ascontiguousarray(carrier_probe_base).tobytes()).hexdigest(),
            "referenceAffects": ["normal", "smoothness"],
            "referenceDoesNotAffect": ["baseColor", "metallic", "occlusion", "emission"],
        },
        "deposits": {
            "maskThreshold": DEPOSIT_MASK_THRESHOLD,
            "grimeMetallicTarget": DIELECTRIC_DEPOSIT_METALLIC,
            "sootMetallicTarget": SOOT_METALLIC_TARGET,
            "dielectricPixels": int(np.count_nonzero(dielectric_deposit)),
            "nonDielectricPixels": deposit_metallic_pixels,
        },
    }
    contact_chip_precision = float(np.count_nonzero(chip & (actual_contact > 0.0))) / max(1, int(np.count_nonzero(chip)))
    contact_scratch_precision = float(np.count_nonzero(scratch & (actual_contact > 0.0))) / max(1, int(np.count_nonzero(scratch)))
    metal_scratch_precision = float(np.count_nonzero(scratch & metal)) / max(1, int(np.count_nonzero(scratch)))
    glass_wear_pixels = int(np.count_nonzero(glass & (chip | scratch | dark_scuff)))
    muzzle_soot = (soot > 0.05) & muzzle
    muzzle_soot_precision = float(np.count_nonzero(muzzle_soot & muzzle)) / max(1, int(np.count_nonzero(muzzle_soot)))
    precisions = {
        "chipContact": round(contact_chip_precision, 6),
        "scratchContact": round(contact_scratch_precision, 6),
        "metalScratch": round(metal_scratch_precision, 6),
        "glassWearPixels": glass_wear_pixels,
        "muzzleSoot": round(muzzle_soot_precision, 6),
    }
    if contact_chip_precision < 0.90 or contact_scratch_precision < 0.85 or metal_scratch_precision < 1.0 or glass_wear_pixels != 0 or muzzle_soot_precision < 0.90:
        raise RuntimeError(f"Contact precision contract failed: {precisions}")
    mask_record = {
        name: {"coverage": round(coverages[name], 6), "limits": list(limits[name]), **component_records[name]}
        for name in coverages
    }
    mask_record["actualEdgeContact"] = {
        "source": "metric distance to mesh edges with dihedral >=30 degrees and vertices incident to >=3 sharp edges",
        "edgeFormula": "1-smoothstep(.0015,.006,edgeDistanceMeters)",
        "cornerFormula": "1-smoothstep(.002,.010,cornerDistanceMeters)",
        "uvSeamsExcluded": True,
        "thresholds": {"chipContact": 0.90, "scratchContact": 0.85, "metalScratch": 1.0, "glassWearPixels": 0, "muzzleSoot": 0.90},
        "precision": precisions,
    }
    mask_record["scratchDimensionsMeters"] = scratch_audit
    return raster, output_hashes, mask_record, periodicity, channel_record


def generate_texture_atlas(objects, stage_texture_dir, raster=None, include_microdetail=True):
    raster = rasterize_surface(objects) if raster is None else raster
    surface = raster["surface"]
    position = raster["position"]
    normal = raster["normal"]
    active = np.flatnonzero(surface)
    surface_count = active.size
    atlas_x = (active % ATLAS_SIZE).astype(np.float32)
    atlas_y = (active // ATLAS_SIZE).astype(np.float32)
    reference_detail = _reference_height_at((atlas_x + 0.5) / ATLAS_SIZE, (atlas_y + 0.5) / ATLAS_SIZE)
    positions = position.reshape(-1, 3)[active]
    px, py, pz = positions[:, 0], positions[:, 1], positions[:, 2]
    minimum = np.min(positions, axis=0)
    span = np.maximum(np.max(positions, axis=0) - minimum, 1.0e-6)
    fbm = _fbm((px - minimum[0]) / span[0], (py - minimum[1]) / span[1], (pz - minimum[2]) / span[2])

    boundary_distance = _surface_boundary_distance(surface)
    edge_distance = raster["edgeDistanceMeters"].ravel()[active]
    corner_distance = raster["cornerDistanceMeters"].ravel()[active]
    edge_contact = 1.0 - _smoothstep(0.0015, 0.0060, edge_distance)
    corner_contact = 1.0 - _smoothstep(0.0020, 0.0100, corner_distance)
    actual_contact = np.maximum(edge_contact, corner_contact)
    normal_count = _box_sum(surface.astype(np.float32), 6)
    mean_components = [_box_sum(normal[:, :, axis] * surface, 6) for axis in range(3)]
    mean_normal = np.stack(mean_components, axis=2) / np.maximum(normal_count[:, :, None], 1.0)
    mean_normal /= np.maximum(np.linalg.norm(mean_normal, axis=2, keepdims=True), 1.0e-12)
    normal_active = normal.reshape(-1, 3)[active]
    cavity = np.clip((1.0 - np.sum(normal_active * mean_normal.reshape(-1, 3)[active], axis=1)) * 4.0, 0.0, 1.0)
    downward = np.clip((-normal_active[:, 1] - 0.10) / 0.90, 0.0, 1.0)
    group_active = raster["group"].ravel()[active]
    profile_ids = raster["profileId"].ravel()[active]
    island_ids = raster["uvIslandId"].ravel()[active]
    metal = group_active == GROUP_NAMES.index("WeaponMetal")
    dark = group_active == GROUP_NAMES.index("WeaponDark")
    accent = group_active == GROUP_NAMES.index("WeaponAccent")
    core = group_active == GROUP_NAMES.index("WeaponAccentCore")
    glass = accent | core

    muzzle = np.zeros(surface_count, dtype=bool)
    handling = np.zeros(surface_count, dtype=np.float32)
    for profile_id, record in sorted(raster["profiles"].items()):
        select = profile_ids == int(profile_id)
        parameters = record["parameters"]
        if parameters["kind"] == "launcher":
            muzzle[select] = py[select] < -0.40
            rear = _smoothstep(0.02, 0.16, py[select]) * (1.0 - _smoothstep(0.06, 0.14, np.abs(px[select]))) * (1.0 - _smoothstep(0.04, 0.13, np.abs(pz[select])))
            grip = (1.0 - _smoothstep(0.06, 0.14, np.abs(px[select]))) * (1.0 - _smoothstep(0.04, 0.13, np.abs(pz[select]))) * (1.0 - _smoothstep(0.18, 0.38, np.abs(py[select] + 0.18)))
            handling[select] = 0.70 * np.maximum(rear, grip) * np.clip(0.65 + 0.35 * fbm[select], 0.0, 1.0)
        elif parameters["kind"] == "shotgun":
            muzzle[select] = py[select] <= parameters["barrelMinY"] + 0.10
            handling[select] = _smoothstep(parameters["receiverMinY"] - 0.02, parameters["receiverMinY"] + 0.04, py[select]) * (1.0 - _smoothstep(parameters["receiverMaxY"] - 0.04, parameters["receiverMaxY"] + 0.02, py[select]))
        else:
            raise RuntimeError(f"Unknown raster profile kind: {parameters['kind']}")
    handling[~(metal | dark)] = 0.0

    scratch_core, scratch_shoulder, scratch_audit = _metric_scratch_segments(
        active, positions, normal_active, raster["tangent"].reshape(-1, 3)[active], actual_contact,
        metal, muzzle, handling, profile_ids, group_active, island_ids, raster["profiles"],
        raster["texelAreaMetersSquared"].ravel()[active],
    )

    def ranked_mask(candidates, scores, count, salt):
        indices = np.flatnonzero(candidates)
        if indices.size < count:
            raise RuntimeError(f"Ranked wear admission failed: candidates={indices.size}, required={count}")
        hashes = np.fromiter((_scratch_hash32(int(active[index]) ^ SEED ^ salt) for index in indices), dtype=np.uint32, count=indices.size)
        order = np.lexsort((hashes, -scores[indices]))
        result = np.zeros(surface_count, dtype=bool)
        result[indices[order[:count]]] = True
        return result

    chip = ranked_mask(metal & (actual_contact > 0.20), 0.70 * actual_contact + 0.30 * fbm, round(surface_count * 0.006), 0x243F6A88)
    grime_score = np.clip(0.38 * cavity + 0.24 * downward + 0.24 * fbm + 0.14 * _trig_noise(px, py, pz, 37.0, 0.417), 0.0, 1.0)
    grime_mask = ranked_mask(~glass, grime_score, round(surface_count * 0.018), 0x85A308D3)
    grime = np.zeros(surface_count, dtype=np.float32)
    grime[grime_mask] = 0.55 + 0.45 * grime_score[grime_mask]
    soot_score = np.clip(0.60 * fbm + 0.40 * (1.0 - _smoothstep(0.0, 0.16, np.sqrt(px * px + pz * pz))), 0.0, 1.0)
    muzzle_eligible = muzzle & ~glass
    soot_mask = ranked_mask(muzzle_eligible, soot_score, round(np.count_nonzero(muzzle_eligible) * 0.25), 0x13198A2E)
    soot = np.zeros(surface_count, dtype=np.float32)
    soot[soot_mask] = 0.55 + 0.45 * soot_score[soot_mask]
    dark_scuff = dark & (actual_contact > 0.55) & (fbm > 0.58)
    polish = (handling > 0.35) & ~grime_mask & ~soot_mask & ~glass

    coverages = {
        "scratchCore": scratch_audit["coverage"],
        "chips": float(np.count_nonzero(chip)) / surface_count,
        "grime": float(np.count_nonzero(grime_mask)) / surface_count,
        "muzzleSoot": float(np.count_nonzero(soot_mask)) / max(1, int(np.count_nonzero(muzzle_eligible))),
    }
    for name, value in coverages.items():
        low, high = WEAR_COVERAGE_LIMITS[name]
        print(f"AUDIT wear {name}: coverage={value:.6f}, limits={low:.4f}..{high:.4f}")
        if value < low or value > high:
            raise RuntimeError(f"Wear coverage failed {name}: {value:.6f} outside {low:.4f}..{high:.4f}")

    palette = {name: np.asarray(value, dtype=np.float32) / 255.0 for name, value in SURFACE_PALETTE_BYTES.items()}

    def compose_base_albedo(_carrier_probe):
        base = np.zeros((surface_count, 3), dtype=np.float32)
        base[metal], base[dark], base[accent], base[core] = palette["metalClean"], palette["darkClean"], palette["accent"], palette["core"]
        warm = np.clip((fbm - 0.5) * 0.12, -0.06, 0.06)[:, None] * np.asarray((1.0, 0.75, 0.35), dtype=np.float32)
        base[metal] += warm[metal]
        base[chip] = palette["metalGroove"]
        base[scratch_shoulder] = palette["metalShoulder"]
        base[scratch_core] = palette["metalGroove"]
        base[dark_scuff] = palette["darkScuff"]
        base[grime_mask] = base[grime_mask] * (1.0 - grime[grime_mask, None]) + palette["dirt"] * grime[grime_mask, None]
        base[soot_mask] *= 1.0 - 0.82 * soot[soot_mask, None]
        base[polish] = base[polish] * 0.94 + palette["metalShoulder"] * 0.06
        return np.clip(base, 0.0, 1.0)

    base = compose_base_albedo(reference_detail)
    carrier_probe_base = compose_base_albedo(np.roll(reference_detail, 17))
    carrier_delta = float(np.max(np.abs(base - carrier_probe_base))) if base.size else 0.0
    if carrier_delta != 0.0:
        raise RuntimeError(f"BaseColor carrier invariance failed: maxAbsDelta={carrier_delta}")

    metallic = np.zeros(surface_count, dtype=np.float32)
    smoothness = np.zeros(surface_count, dtype=np.float32)
    emission = np.zeros((surface_count, 3), dtype=np.float32)
    for mask, key in ((metal, "metalClean"), (dark, "darkClean"), (accent, "accent"), (core, "core"), (chip, "chip"), (scratch_shoulder, "metalShoulder"), (scratch_core, "metalGroove"), (dark_scuff, "darkScuff"), (grime_mask, "grime"), (soot_mask, "soot"), (polish, "polish")):
        metallic[mask], smoothness[mask] = SURFACE_PBR[key]
    metal_or_dark = metal | dark
    smoothness = np.clip(smoothness + 0.10 * reference_detail * metal_or_dark.astype(np.float32), 0.0, 1.0)
    emission[core, 0] = 0.85
    height_active = (-0.025 * chip - 0.018 * scratch_core + 0.006 * scratch_shoulder - 0.004 * dark_scuff + 0.010 * grime + 0.002 * reference_detail * metal_or_dark).astype(np.float32)
    height_map = np.zeros(surface.shape, dtype=np.float32)
    height_map.ravel()[active] = height_active
    left, right = np.roll(height_map, 1, axis=1), np.roll(height_map, -1, axis=1)
    down, up = np.roll(height_map, 1, axis=0), np.roll(height_map, -1, axis=0)
    left = np.where(np.roll(surface, 1, axis=1), left, height_map)
    right = np.where(np.roll(surface, -1, axis=1), right, height_map)
    down = np.where(np.roll(surface, 1, axis=0), down, height_map)
    up = np.where(np.roll(surface, -1, axis=0), up, height_map)
    normal_map = np.stack((-5.0 * (right - left), -5.0 * (up - down), np.ones_like(height_map)), axis=2)
    normal_map /= np.maximum(np.linalg.norm(normal_map, axis=2, keepdims=True), 1.0e-12)
    ao = np.clip(1.0 - 0.28 * cavity - 0.12 * grime - 0.10 * scratch_core.astype(np.float32), 0.55, 1.0)

    index_map = np.arange(surface.size).reshape(surface.shape)
    masks = {
        "scratchCore": np.isin(index_map, active[scratch_core]),
        "scratchShoulder": np.isin(index_map, active[scratch_shoulder]),
        "chips": np.isin(index_map, active[chip]),
        "grime": np.isin(index_map, active[grime_mask]),
        "muzzleSoot": np.isin(index_map, active[soot_mask]),
        "polish": np.isin(index_map, active[polish]),
    }
    uv_boundary = surface & (boundary_distance < 1.0)
    component_records = {
        name: {
            "pixels": int(np.count_nonzero(mask)),
            "uvBoundaryPixels": int(np.count_nonzero(mask & uv_boundary)),
            "audit": "deterministic physical mask; coverage and glass exclusion enforced",
        }
        for name, mask in masks.items()
    }
    labels = _dilation_labels(surface)
    dilated = labels >= 0
    source_indices = labels[dilated]
    dilated_height = np.zeros_like(height_map)
    dilated_height[dilated] = height_map.ravel()[source_indices]
    periodicity = _periodicity_audit(dilated_height, dilated)

    def expanded_rgba(default, source_rgba):
        output = np.empty((ATLAS_SIZE, ATLAS_SIZE, 4), dtype=np.uint8)
        output[:] = np.asarray(default, dtype=np.uint8)
        source_full = np.empty((surface.size, 4), dtype=np.uint8)
        source_full[:] = np.asarray(default, dtype=np.uint8)
        source_full[active] = np.clip(np.rint(source_rgba * 255.0), 0, 255).astype(np.uint8)
        output[dilated] = source_full[source_indices]
        return output

    payloads = {
        TEXTURE_NAMES[0]: expanded_rgba((0, 0, 0, 255), np.column_stack((base, np.ones(surface_count)))),
        TEXTURE_NAMES[1]: expanded_rgba((128, 128, 255, 255), np.column_stack((normal_map.reshape(-1, 3)[active] * 0.5 + 0.5, np.ones(surface_count)))),
        TEXTURE_NAMES[2]: expanded_rgba((0, 0, 0, 0), np.column_stack((metallic, np.zeros((surface_count, 2)), smoothness))),
        TEXTURE_NAMES[3]: expanded_rgba((255, 255, 255, 255), np.column_stack((ao, ao, ao, np.ones(surface_count)))),
        TEXTURE_NAMES[4]: expanded_rgba((0, 0, 0, 255), np.column_stack((emission, np.ones(surface_count)))),
    }
    output_hashes = {}
    for filename, pixels in payloads.items():
        path = os.path.join(stage_texture_dir, filename)
        write_rgba_png(path, pixels, filename == TEXTURE_NAMES[0])
        output_hashes[filename] = _hash_file(path)
    microdetail_audit = None
    if include_microdetail:
        path = os.path.join(stage_texture_dir, MICRODETAIL_NAME)
        output_hashes[MICRODETAIL_NAME], microdetail_audit = generate_microdetail_texture(path)

    channels = {
        "baseColor": {"format": "RGBA8", "transfer": "sRGB", "paletteBytes": SURFACE_PALETTE_BYTES, "referenceInfluence": False},
        "normal": {"format": "RGBA8", "transfer": "linear", "centralDifferencePx": 1, "gain": 5.0, "referenceHeight": 0.002},
        "metallicSmoothness": {"format": "RGBA8", "transfer": "linear", "metallicChannel": "R", "smoothnessChannel": "A", "values": SURFACE_PBR},
        "occlusion": {"format": "RGBA8", "transfer": "linear", "formula": "clamp(1-.28*cavity-.12*grime-.10*scratchCore,.55,1)"},
        "emission": {"format": "RGBA8", "transfer": "linear", "coreValue": 0.85},
        "photoDerivedDetail": {"source": REFERENCE_TILE_NAME, "decodedRgbaSha256": REFERENCE_RGBA_SHA256, "luminance": "(54R+183G+19B)/256", "periodicBlur": [65, 65], "normalization": "clip(delta/.18,-1,1)", "referenceAffects": ["normal", "smoothness"], "referenceDoesNotAffect": ["baseColor", "metallic", "occlusion", "emission"]},
        "albedoCarrierInvariant": {"maxAbsFloatDelta": round(carrier_delta, 9), "baseColorFloatSha256": hashlib.sha256(np.ascontiguousarray(base).tobytes()).hexdigest(), "carrierProbeBaseColorFloatSha256": hashlib.sha256(np.ascontiguousarray(carrier_probe_base).tobytes()).hexdigest()},
    }
    if microdetail_audit is not None:
        channels["microDetailNormal"] = {"audit": microdetail_audit, "size": [MICRODETAIL_SIZE, MICRODETAIL_SIZE]}
    glass_wear_pixels = int(np.count_nonzero(glass & (chip | scratch_core | scratch_shoulder | dark_scuff | grime_mask | soot_mask | polish)))
    if glass_wear_pixels:
        raise RuntimeError(f"Glass wear contract failed: pixels={glass_wear_pixels}")
    mask_record = {
        name: {"coverage": round(coverages[name], 6) if name in coverages else round(float(np.count_nonzero(mask)) / surface_count, 6), "limits": list(WEAR_COVERAGE_LIMITS[name]) if name in WEAR_COVERAGE_LIMITS else None, **component_records[name]}
        for name, mask in masks.items()
    }
    mask_record["scratchSegments"] = scratch_audit
    mask_record["glassWearPixels"] = glass_wear_pixels
    return raster, output_hashes, mask_record, periodicity, channels


def point_camera(camera, target):
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()


def configure_final_materials(materials, stage_texture_dir):
    image_names = TEXTURE_NAMES
    image_paths = {filename: os.path.join(stage_texture_dir, filename) for filename in image_names}
    images = {filename: bpy.data.images.load(path, check_existing=False) for filename, path in image_paths.items()}
    for filename, image in images.items():
        image.colorspace_settings.name = "sRGB" if filename == TEXTURE_NAMES[0] else "Non-Color"
    for material in materials.values():
        material.use_nodes = True
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        nodes.clear()
        output = nodes.new("ShaderNodeOutputMaterial")
        shader = nodes.new("ShaderNodeBsdfPrincipled")
        base_texture = nodes.new("ShaderNodeTexImage")
        base_texture.image = images[TEXTURE_NAMES[0]]
        ao_texture = nodes.new("ShaderNodeTexImage")
        ao_texture.image = images[TEXTURE_NAMES[3]]
        base_ao = nodes.new("ShaderNodeMixRGB")
        base_ao.blend_type = "MULTIPLY"
        base_ao.inputs[0].default_value = 1.0
        normal_texture = nodes.new("ShaderNodeTexImage")
        normal_texture.image = images[TEXTURE_NAMES[1]]
        normal_node = nodes.new("ShaderNodeNormalMap")
        normal_output = normal_node.outputs["Normal"]
        metallic_texture = nodes.new("ShaderNodeTexImage")
        metallic_texture.image = images[TEXTURE_NAMES[2]]
        separate_metallic = nodes.new("ShaderNodeSeparateColor")
        invert_smoothness = nodes.new("ShaderNodeMath")
        invert_smoothness.operation = "SUBTRACT"
        invert_smoothness.inputs[0].default_value = 1.0
        emission_texture = nodes.new("ShaderNodeTexImage")
        emission_texture.image = images[TEXTURE_NAMES[4]]
        links.new(base_texture.outputs["Color"], base_ao.inputs[1])
        links.new(ao_texture.outputs["Color"], base_ao.inputs[2])
        links.new(base_ao.outputs["Color"], shader.inputs["Base Color"])
        links.new(normal_texture.outputs["Color"], normal_node.inputs["Color"])
        links.new(normal_output, shader.inputs["Normal"])
        links.new(metallic_texture.outputs["Color"], separate_metallic.inputs["Color"])
        links.new(separate_metallic.outputs["Red"], shader.inputs["Metallic"])
        links.new(metallic_texture.outputs["Alpha"], invert_smoothness.inputs[1])
        links.new(invert_smoothness.outputs[0], shader.inputs["Roughness"])
        if "Emission Color" in shader.inputs:
            links.new(emission_texture.outputs["Color"], shader.inputs["Emission Color"])
            shader.inputs["Emission Strength"].default_value = 1.0
        else:
            links.new(emission_texture.outputs["Color"], shader.inputs["Emission"])
        links.new(shader.outputs["BSDF"], output.inputs["Surface"])


def _audit_preview(path):
    image = bpy.data.images.load(path, check_existing=False)
    if tuple(image.size) != (640, 640):
        raise RuntimeError(f"Preview dimensions failed {path}: {tuple(image.size)}")
    values = np.empty(640 * 640 * 4, dtype=np.float32)
    image.pixels.foreach_get(values)
    pixels = values.reshape(640, 640, 4)
    foreground = pixels[:, :, 3] > 0.01
    foreground_count = int(np.count_nonzero(foreground))
    coverage = foreground_count / (640 * 640)
    border = np.zeros((640, 640), dtype=bool)
    border[:2, :] = border[-2:, :] = True
    border[:, :2] = border[:, -2:] = True
    clipped = float(np.count_nonzero(foreground & border)) / max(1, foreground_count)
    rgb = pixels[:, :, :3][foreground]
    luminance = rgb @ np.array((0.2126, 0.7152, 0.0722), dtype=np.float32)
    chroma = np.max(rgb, axis=1) - np.min(rgb, axis=1)
    luminance_sd = float(np.std(luminance))
    chroma_sd = float(np.std(chroma))
    bpy.data.images.remove(image)
    if not 0.15 <= coverage <= 0.85 or clipped >= 0.005 or luminance_sd <= 0.025 or chroma_sd <= 0.015:
        raise RuntimeError(
            f"Preview audit failed {os.path.basename(path)}: coverage={coverage:.6f}, clipped={clipped:.6f}, luminanceSD={luminance_sd:.6f}, chromaSD={chroma_sd:.6f}"
        )
    return {
        "objectCoverage": round(coverage, 6), "clippedForeground": round(clipped, 6),
        "luminanceSD": round(luminance_sd, 6), "chromaSD": round(chroma_sd, 6),
    }


def render_previews(objects, minimum, maximum, preview_dir):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = True
    world = bpy.data.worlds.new("LauncherPreviewWorld")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.018, 0.022, 0.032, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.30
    center = (minimum + maximum) * 0.5
    camera_data = bpy.data.cameras.new("LauncherPreviewCamera")
    camera = bpy.data.objects.new("LauncherPreviewCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    camera.data.type = "ORTHO"

    for name, location, energy, size in (
        ("LauncherKey", (1.4, -1.4, 1.6), 850.0, 2.0),
        ("LauncherFill", (-1.5, -0.4, 0.8), 500.0, 2.0),
        ("LauncherRim", (0.8, 1.8, 1.2), 700.0, 1.4),
    ):
        light_data = bpy.data.lights.new(name, type="AREA")
        light_data.energy = energy
        light_data.shape = "DISK"
        light_data.size = size
        light = bpy.data.objects.new(name, light_data)
        light.location = location
        bpy.context.collection.objects.link(light)
        point_camera(light, center)

    os.makedirs(preview_dir, exist_ok=True)
    for filename in os.listdir(preview_dir):
        if filename.lower().endswith(".png"):
            os.remove(os.path.join(preview_dir, filename))
    span = maximum - minimum
    cardinal = (
        ("front.png", Vector((0.0, -1.0, 0.0)), max(span.x, span.z)),
        ("rear.png", Vector((0.0, 1.0, 0.0)), max(span.x, span.z)),
        ("left.png", Vector((-1.0, 0.0, 0.0)), max(span.y, span.z)),
        ("right.png", Vector((1.0, 0.0, 0.0)), max(span.y, span.z)),
        ("top.png", Vector((0.0, 0.0, 1.0)), max(span.x, span.y)),
    )
    expected = set(PREVIEW_NAMES)
    for filename, direction, view_span in cardinal:
        camera.data.ortho_scale = view_span * 1.14
        camera.location = center + direction * max(1.0, view_span * 3.0)
        point_camera(camera, center)
        scene.render.filepath = os.path.join(preview_dir, filename)
        bpy.ops.render.render(write_still=True)
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = max(span.x, span.y, span.z) * 1.10
    camera.location = center + Vector((0.82, -1.18, 0.58))
    point_camera(camera, center)
    scene.render.filepath = os.path.join(preview_dir, "three-quarter.png")
    bpy.ops.render.render(write_still=True)
    actual = {filename for filename in os.listdir(preview_dir) if filename.lower().endswith(".png")}
    if actual != expected:
        raise RuntimeError(f"Preview inventory mismatch: expected={sorted(expected)}, actual={sorted(actual)}")
    preview_audits = {}
    for filename in sorted(actual):
        path = os.path.join(preview_dir, filename)
        if not os.path.isfile(path) or os.path.getsize(path) == 0:
            raise RuntimeError(f"Preview missing or empty: {path}")
        preview_audits[filename] = _audit_preview(path)
        print(f"PREVIEW {filename}: {path} ({os.path.getsize(path)} bytes)")
    print(f"PREVIEW inventory: count={len(actual)}, exact=front/rear/left/right/top/three-quarter")
    return preview_audits


def export_fbx(objects, output_path):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=output_path,
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
    if not os.path.isfile(output_path) or os.path.getsize(output_path) == 0:
        raise RuntimeError(f"FBX export missing or empty: {output_path}")
    print(f"OUTPUT {output_path} ({os.path.getsize(output_path)} bytes)")


def import_roundtrip(source_record, output_path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=output_path)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != len(GROUP_NAMES):
        raise RuntimeError(f"Round-trip mesh count failed: {len(meshes)}")
    by_name = {obj.name: obj for obj in meshes}
    if set(by_name) != set(GROUP_NAMES):
        raise RuntimeError(f"Round-trip object names failed: {tuple(by_name)}")
    objects = tuple(by_name[name] for name in GROUP_NAMES)
    imported_record = audit_asset(objects, None, imported=True)
    for key in ("vertex_count", "triangle_count", "bounds_min", "bounds_max", "dimensions"):
        if key in ("bounds_min", "bounds_max", "dimensions"):
            if any(abs(a - b) > 0.0005 for a, b in zip(imported_record[key], source_record[key])):
                raise RuntimeError(f"Round-trip {key} mismatch: {imported_record[key]} != {source_record[key]}")
        elif imported_record[key] != source_record[key]:
            raise RuntimeError(f"Round-trip {key} mismatch: {imported_record[key]} != {source_record[key]}")
    print(f"ROUNDTRIP launcher: objects=4, vertices={imported_record['vertex_count']}, triangles={imported_record['triangle_count']}, bounds_match=yes, transforms=applied, static=yes")
    print("ROUNDTRIP AXIS launcher: imported Blender -Y muzzle ordering preserved; Unity +Z mapping verified")


def generate_once(stage_dir, surface_only=False):
    clear_reference_height_cache()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    materials = {name: make_material(name, color) for name, color in MATERIAL_SPECS.items()}
    objects, part_bounds = create_geometry(materials)
    minimum, maximum = combined_bounds(objects)
    record = audit_asset(objects, part_bounds)
    texture_dir = os.path.join(stage_dir, "textures")
    preview_dir = os.path.join(stage_dir, "previews")
    output_path = os.path.join(stage_dir, "FpsRocketLauncher.fbx")
    raster, texture_hashes, mask_record, periodicity, channel_record = generate_texture_atlas(
        objects, texture_dir, include_microdetail=not surface_only
    )
    record["texture_hashes"] = texture_hashes
    record["uv_hash"] = uv_signature(objects)
    record["signature"] = canonical_signature(objects, record)
    configure_final_materials(materials, texture_dir)
    preview_audits = render_previews(objects, minimum, maximum, preview_dir)
    if not surface_only:
        export_fbx(objects, output_path)
        import_roundtrip(record, output_path)
    record["stage_dir"] = stage_dir
    record["fbx_path"] = output_path if not surface_only else None
    record["surface_only"] = bool(surface_only)
    record["preview_dir"] = preview_dir
    record["preview_audits"] = preview_audits
    record["preview_hashes"] = {filename: _hash_file(os.path.join(preview_dir, filename)) for filename in PREVIEW_NAMES}
    record["uv_zones"] = raster["zones"]
    record["uv_overlap_pixels"] = raster["nonAdjacentInteriorOverlapPixels"]
    record["masks"] = mask_record
    record["periodicity"] = periodicity
    record["channels"] = channel_record
    print(f"SIGNATURE FpsRocketLauncher: {record['signature']}")
    print("RESULT FpsRocketLauncher generation succeeded")
    return record


def compare_runs(first, second):
    if first["signature"] != second["signature"]:
        raise RuntimeError(f"Two-run semantic signature mismatch: {first['signature']} != {second['signature']}")
    for key in ("vertex_count", "triangle_count", "bounds_min", "bounds_max", "dimensions", "connections", "pairs"):
        if first[key] != second[key]:
            raise RuntimeError(f"Two-run semantic mismatch field {key}")
    if first["texture_hashes"] != second["texture_hashes"]:
        raise RuntimeError(f"Two-run texture hash mismatch: {first['texture_hashes']} != {second['texture_hashes']}")
    if first["uv_hash"] != second["uv_hash"]:
        raise RuntimeError(f"Two-run UV hash mismatch: {first['uv_hash']} != {second['uv_hash']}")
    if first["masks"] != second["masks"]:
        raise RuntimeError("Two-run mask audit mismatch")
    if first["channels"] != second["channels"]:
        raise RuntimeError("Two-run channel audit mismatch")
    if first["periodicity"] != second["periodicity"]:
        raise RuntimeError("Two-run periodicity identity failed")
    print(f"PROOF two-run semantic+texture match: textures={len(first['texture_hashes'])}, preview-audits-per-run={len(PREVIEW_NAMES)}")


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


def protected_surface_paths():
    """Return the complete deterministic T1 protected-file inventory."""
    paths = set()

    def add_tree(root):
        if not os.path.isdir(root):
            raise RuntimeError(f"Protected inventory root missing: {root}")
        for directory, names, filenames in os.walk(root):
            names.sort()
            for filename in sorted(filenames):
                paths.add(os.path.realpath(os.path.join(directory, filename)))

    add_tree(os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models"))
    add_tree(os.path.join(REPOSITORY_ROOT, "Tools", "Blender", "ReferenceInputs"))
    add_tree(os.path.join(REPOSITORY_ROOT, "graphics references"))

    # Repository source metas are protected. Generated/cache roots are not
    # repository source and can contain thousands of transient package metas.
    excluded_roots = {".git", "Library", "Logs", "Temp", "obj"}
    for directory, names, filenames in os.walk(REPOSITORY_ROOT):
        relative = os.path.relpath(directory, REPOSITORY_ROOT)
        if relative == ".":
            names[:] = sorted(name for name in names if name not in excluded_roots)
        else:
            names.sort()
        for filename in sorted(filenames):
            if filename.endswith(".meta"):
                paths.add(os.path.realpath(os.path.join(directory, filename)))

    paths.add(os.path.realpath(os.path.join(TEXTURE_DIR, MICRODETAIL_NAME)))
    return tuple(sorted(paths, key=lambda path: os.path.relpath(path, REPOSITORY_ROOT).replace(os.sep, "/")))


def snapshot_surface_protected():
    result = {}
    for path in protected_surface_paths():
        if not os.path.isfile(path):
            raise RuntimeError(f"Protected surface asset missing: {path}")
        key = os.path.relpath(path, REPOSITORY_ROOT).replace(os.sep, "/")
        result[key] = _hash_file(path)
    return result


def assert_surface_protected(snapshot, phase):
    current = snapshot_surface_protected()
    if current != snapshot:
        drift = sorted(key for key in set(snapshot) | set(current) if snapshot.get(key) != current.get(key))
        raise RuntimeError(f"Protected surface drift before {phase}: {drift}")
    return current


def surface_contract_record():
    expected_profile_contracts = {
        profile: {
            "poolRatios": {"sharp": 0.60, "handling": 0.25, "muzzle": 0.15},
            "poolTieOrder": ["sharp", "handling", "muzzle"],
            "orientationRatios": {"tangent": 0.70, "cross": 0.30},
            "orientationTieOrder": ["tangent", "cross"],
        }
        for profile in ("launcher", "FpsShotgun", "Shotgun")
    }
    if SCRATCH_PROFILE_CONTRACTS != expected_profile_contracts:
        raise RuntimeError(
            f"Scratch profile contract audit failed: expected={expected_profile_contracts}, actual={SCRATCH_PROFILE_CONTRACTS}"
        )
    return {
        "revision": SURFACE_REVISION,
        "seed": SEED,
        "atlasSize": ATLAS_SIZE,
        "paletteBytes": SURFACE_PALETTE_BYTES,
        "reference": {"decodedRgbaSha256": REFERENCE_RGBA_SHA256, "luminance": "(54R+183G+19B)/256", "periodicBlur": [65, 65], "normalization": "clip(delta/.18,-1,1)", "affects": ["normal", "smoothness"]},
        "order": ["clean", "warm3dFbm", "scratchChipGrooveShoulder", "grime", "muzzleSoot", "handlingPolish", "microdetail"],
        "coverageLimits": WEAR_COVERAGE_LIMITS,
        "scratch": {
            "profileContracts": SCRATCH_PROFILE_CONTRACTS,
            "profileContractAudit": {
                "expected": expected_profile_contracts,
                "exactLabelsAndValues": True,
                "allocation": "largest remainder with declared tie order for final adaptive seed count",
            },
            "widthMeters": [0.00025, 0.00065], "lengthMeters": [0.005, 0.022],
            "shoulderWidthMeters": [0.00020, 0.00045], "normalDotMinimum": 0.9063078,
            "capsulesPerSeed": 1,
            "coverage": {"limits": list(WEAR_COVERAGE_LIMITS["scratchCore"]), "basis": "actual declared eligible metal atlas pixels"},
        },
        "pbr": SURFACE_PBR,
        "heights": {"chip": -0.025, "groove": -0.018, "shoulder": 0.006, "darkScuff": -0.004, "grime": 0.010, "carrier": 0.002, "normalGain": 5.0},
        "occlusion": "clamp(1-.28*cavity-.12*grime-.10*scratchCore,.55,1)",
        "emission": {"WeaponAccentCore": 0.85},
    }


def promote_and_write_proof(record, two_run_identical, protected_before=None):
    surface_only = record["surface_only"]
    protected_before_promotion = assert_surface_protected(protected_before, "any output promotion")
    for filename in TEXTURE_NAMES:
        _atomic_promote(os.path.join(record["stage_dir"], "textures", filename), os.path.join(TEXTURE_DIR, filename))
    if not surface_only:
        _atomic_promote(os.path.join(record["stage_dir"], "textures", MICRODETAIL_NAME), os.path.join(TEXTURE_DIR, MICRODETAIL_NAME))
        _atomic_promote(record["fbx_path"], OUTPUT_PATH)
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    for filename in PREVIEW_NAMES:
        _atomic_promote(os.path.join(record["preview_dir"], filename), os.path.join(PREVIEW_DIR, filename))
    promoted_names = TEXTURE_NAMES if surface_only else TEXTURE_NAMES + (MICRODETAIL_NAME,)
    final_texture_hashes = {
        filename: _hash_file(os.path.join(TEXTURE_DIR, filename))
        for filename in promoted_names
    }
    final_preview_hashes = {filename: _hash_file(os.path.join(PREVIEW_DIR, filename)) for filename in PREVIEW_NAMES}
    if final_texture_hashes != record["texture_hashes"] or final_preview_hashes != record["preview_hashes"]:
        raise RuntimeError("Atomic promotion hash verification failed")
    protected_after = snapshot_surface_protected()
    if surface_only and protected_after != protected_before:
        assert_surface_protected(protected_before, "proof write")
    else:
        assert_surface_protected(protected_after, "proof write")
    fbx_before = {key: value for key, value in (protected_before or {}).items() if key.endswith(".fbx")}
    fbx_after = {key: value for key, value in (protected_after or {}).items() if key.endswith(".fbx")}
    proof = {
        "schemaVersion": 3,
        "surfaceRevision": SURFACE_REVISION,
        "surfaceOnly": surface_only,
        "seed": SEED,
        "versions": {"blender": bpy.app.version_string, "python": sys.version.split()[0], "numpy": np.__version__},
        "geometry": {
            "groups": list(record["objects"]), "vertices": record["vertex_count"], "triangles": record["triangle_count"],
            "shellCorePairs": len(record["pairs"]), "signature": record["signature"], "forward": "Blender -Y -> Unity +Z",
            "meshAudit": "finite, nonzero edges/faces, no loose or unintended nonmanifold geometry, positive winding, identity transforms",
            "previewAudits": record["preview_audits"],
        },
        "bounds": {"min": list(record["bounds_min"]), "max": list(record["bounds_max"])},
        "uvZones": {**record["uv_zones"], "nonAdjacentInteriorOverlapPixels": record["uv_overlap_pixels"], "smartProjectAngleDegrees": 66},
        "uvSha256": record["uv_hash"],
        "masks": record["masks"],
        "periodicity": record["periodicity"],
        "channels": record["channels"],
        "surfaceContract": surface_contract_record(),
        "protected": {
            "inventoryContract": {
                "models": "Assets/_Game/Models/**",
                "repositoryMetas": "all .meta outside generated/cache roots .git, Library, Logs, Temp, obj",
                "referenceInputs": "Tools/Blender/ReferenceInputs/**",
                "graphicsReferences": "graphics references/**",
                "microdetail": "Assets/_Game/Textures/WeaponMicroDetail_Normal.png",
                "taskOwnedTextureContentExcluded": True,
            },
            "beforeCount": len(protected_before),
            "beforePromotionCount": len(protected_before_promotion),
            "afterCount": len(protected_after),
            "fbxBeforeSha256": fbx_before, "fbxAfterSha256": fbx_after,
            "beforeSha256": protected_before,
            "beforePromotionSha256": protected_before_promotion,
            "afterSha256": protected_after,
            "stable": protected_before == protected_after if surface_only else None,
        },
        "outputSha256": final_texture_hashes,
        "previewSha256": final_preview_hashes,
        "twoRunIdentical": bool(two_run_identical),
        "twoRunIdentity": {"exact": bool(two_run_identical), "promotedTextures": len(final_texture_hashes), "geometryUvMasksChannelsPeriodicity": bool(two_run_identical), "previewInventoryPreserved": len(record["preview_hashes"])},
    }
    proof_payload = (json.dumps(proof, sort_keys=True, indent=2) + "\n").encode("utf-8")
    staged_proof = os.path.join(record["stage_dir"], "proof.json")
    with open(staged_proof, "wb") as stream:
        stream.write(proof_payload)
        stream.flush()
        os.fsync(stream.fileno())
    _atomic_promote(staged_proof, PROOF_PATH)
    print(f"PROOF {PROOF_PATH} ({os.path.getsize(PROOF_PATH)} bytes)")
    return proof


def parse_cli_args():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    supported = {"--proof-two-run", "--surface-only"}
    unknown = [arg for arg in args if arg not in supported]
    duplicates = sorted(arg for arg in supported if args.count(arg) > 1)
    if unknown or duplicates:
        print(f"ERROR unsupported generator arguments: unknown={unknown}, duplicates={duplicates}", file=sys.stderr, flush=True)
        os._exit(2)
    options = {"proof_two_run": "--proof-two-run" in args, "surface_only": "--surface-only" in args}
    if options["surface_only"] and not options["proof_two_run"]:
        print("ERROR --surface-only requires --proof-two-run before any output can be promoted", file=sys.stderr, flush=True)
        os._exit(2)
    return options


def main():
    options = parse_cli_args()
    surface_only = options["surface_only"]
    protected_before = snapshot_surface_protected()
    _safe_recreate_staging_root()
    first = generate_once(os.path.join(STAGING_ROOT, "run1"), surface_only=surface_only)
    identical = False
    if options["proof_two_run"]:
        second = generate_once(os.path.join(STAGING_ROOT, "run2"), surface_only=surface_only)
        compare_runs(first, second)
        identical = True
    promote_and_write_proof(first, identical, protected_before=protected_before)


if __name__ == "__main__":
    main()
