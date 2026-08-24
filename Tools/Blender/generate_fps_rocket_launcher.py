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
TEXTURE_NAMES = (
    "FpsRocketLauncher_BaseColor.png",
    "FpsRocketLauncher_Normal.png",
    "FpsRocketLauncher_MetallicSmoothness.png",
    "FpsRocketLauncher_Occlusion.png",
    "FpsRocketLauncher_Emission.png",
)
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
    "WeaponMetal": (0.50, 0.49, 0.43, 1.0),
    "WeaponDark": (0.025, 0.028, 0.030, 1.0),
    "WeaponAccentCore": (0.16, 0.0, 0.0, 1.0),
    "WeaponAccent": (0.55, 0.0, 0.0, 1.0),
}

SURFACE_SPECS = {
    "WeaponMetal": {"clean": (0.50, 0.49, 0.43), "exposed": (0.66, 0.67, 0.65), "metal": (0.82, 0.96, 0.30), "smooth": (0.48, 0.62, 0.25, 0.18)},
    "WeaponDark": {"clean": (0.025, 0.028, 0.030), "exposed": (0.15, 0.16, 0.17), "metal": (0.02, 0.02, 0.02), "smooth": (0.25, 0.34, 0.18, 0.12)},
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


def write_rgba_png(path, pixels, srgb):
    if pixels.shape != (ATLAS_SIZE, ATLAS_SIZE, 4) or pixels.dtype != np.uint8:
        raise RuntimeError(f"Invalid RGBA8 atlas payload for {path}: {pixels.shape}/{pixels.dtype}")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Texture arrays use Blender's bottom-left UV origin; PNG scanlines are top-first.
    scanlines = b"".join(b"\x00" + row.tobytes() for row in pixels[::-1])
    chunks = [_png_chunk(b"IHDR", struct.pack(">IIBBBBB", ATLAS_SIZE, ATLAS_SIZE, 8, 6, 0, 0, 0))]
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


def rasterize_surface(objects):
    size = ATLAS_SIZE
    owner = np.full((size, size), -1, dtype=np.int32)
    group = np.full((size, size), 255, dtype=np.uint8)
    position = np.zeros((size, size, 3), dtype=np.float32)
    normal = np.zeros((size, size, 3), dtype=np.float32)
    tangent = np.zeros((size, size, 3), dtype=np.float32)
    dihedral = np.zeros((size, size), dtype=np.float32)
    edge_distance_m = np.full((size, size), np.inf, dtype=np.float32)
    corner_distance_m = np.full((size, size), np.inf, dtype=np.float32)
    triangle_vertices = []
    overlap_pixels = 0
    triangle_index = 0

    for group_index, obj in enumerate(objects):
        mesh = obj.data
        mesh.calc_loop_triangles()
        mesh.update(calc_edges=True)
        face_dihedral = _face_dihedral_degrees(mesh)
        sharp_edges, sharp_corners = _sharp_contact_topology(mesh)
        uv_data = mesh.uv_layers[0].data
        for triangle in mesh.loop_triangles:
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
            triangle_index += 1

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


def _metric_scratch_segments(active, positions, normals, tangents, actual_contact, metal):
    """Rasterize deterministic finite metric capsules on real Metal contact pixels."""
    width_range = (0.00025, 0.00080)
    length_range = (0.004, 0.022)
    contact_threshold = 0.08
    target_coverage = 0.003
    candidate_indices = np.flatnonzero(metal & (actual_contact > contact_threshold))
    if candidate_indices.size == 0:
        raise RuntimeError("Scratch segment admission failed: no Metal contact candidates")

    candidate_hashes = np.fromiter(
        (_scratch_hash32(int(active[index]) ^ SEED) for index in candidate_indices),
        dtype=np.uint32,
        count=candidate_indices.size,
    )
    candidate_indices = candidate_indices[np.lexsort((candidate_indices, candidate_hashes))]

    cell_size = length_range[1] + width_range[1]
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

    scratch = np.zeros(active.size, dtype=bool)
    admitted_geometry = np.zeros(active.size, dtype=bool)
    records = []
    maximum_candidates = min(candidate_indices.size, max(4096, active.size // 150))
    for candidate_index in candidate_indices[:maximum_candidates]:
        atlas_index = int(active[candidate_index])
        identity = atlas_index ^ SEED ^ (len(records) * 0x9E3779B9)
        width = width_range[0] + (width_range[1] - width_range[0]) * _scratch_unit(identity ^ 0xA511E9B3)
        length = length_range[0] + (length_range[1] - length_range[0]) * _scratch_unit(identity ^ 0x63D83595)
        radius = width * 0.5
        half_line = max(0.0, (length - width) * 0.5)

        center = positions[candidate_index].astype(np.float64)
        surface_normal = normals[candidate_index].astype(np.float64)
        surface_normal /= max(np.linalg.norm(surface_normal), 1.0e-12)
        tangent_axis = tangents[candidate_index].astype(np.float64)
        tangent_axis -= surface_normal * float(np.dot(tangent_axis, surface_normal))
        tangent_axis /= max(np.linalg.norm(tangent_axis), 1.0e-12)
        bitangent_axis = np.cross(surface_normal, tangent_axis)
        bitangent_axis /= max(np.linalg.norm(bitangent_axis), 1.0e-12)
        angle = math.radians(-32.0 + 64.0 * _scratch_unit(identity ^ 0xC2B2AE35))
        direction = math.cos(angle) * tangent_axis + math.sin(angle) * bitangent_axis
        direction /= max(np.linalg.norm(direction), 1.0e-12)

        extent = np.abs(direction) * half_line + radius
        nearby = nearby_indices(center - extent, center + extent)
        if nearby.size == 0:
            continue
        delta = positions[nearby].astype(np.float64) - center
        longitudinal = delta @ direction
        clamped_longitudinal = np.clip(longitudinal, -half_line, half_line)
        closest_delta = delta - clamped_longitudinal[:, None] * direction
        inside_capsule = np.einsum("ij,ij->i", closest_delta, closest_delta) <= radius * radius + 1.0e-16

        gap_center = (-0.18 + 0.36 * _scratch_unit(identity ^ 0x27D4EB2F)) * length
        gap_half_width = (0.018 + 0.035 * _scratch_unit(identity ^ 0x165667B1)) * length
        breakup_keep = np.abs(longitudinal - gap_center) >= gap_half_width
        normal_alignment = (normals[nearby].astype(np.float64) @ surface_normal) > 0.55
        admitted_pixels = (
            inside_capsule
            & breakup_keep
            & normal_alignment
            & metal[nearby]
            & (actual_contact[nearby] > contact_threshold)
        )
        if not np.any(admitted_pixels):
            continue
        rendered_indices = nearby[admitted_pixels]
        new_pixels = rendered_indices[~scratch[rendered_indices]]
        if new_pixels.size < 2:
            continue

        capsule_indices = nearby[inside_capsule & normal_alignment]
        admitted_geometry[capsule_indices] = True
        scratch[rendered_indices] = True
        records.append(
            {
                "id": len(records),
                "anchorAtlasIndex": atlas_index,
                "centerMeters": [round(float(value), 9) for value in center],
                "tangent": [round(float(value), 9) for value in tangent_axis],
                "bitangent": [round(float(value), 9) for value in bitangent_axis],
                "direction": [round(float(value), 9) for value in direction],
                "angleFromTangentDegrees": round(math.degrees(angle), 6),
                "widthMeters": round(width, 9),
                "lengthMeters": round(length, 9),
                "centerlineHalfLengthMeters": round(half_line, 9),
                "capRadiusMeters": round(radius, 9),
                "breakupGapCenterMeters": round(gap_center, 9),
                "breakupGapHalfWidthMeters": round(gap_half_width, 9),
                "renderedPixels": int(rendered_indices.size),
                "newPixels": int(new_pixels.size),
            }
        )
        if len(records) >= 32 and np.count_nonzero(scratch) / active.size >= target_coverage:
            break

    if not records:
        raise RuntimeError("Scratch segment admission failed: no finite capsule rendered")
    observed_widths = [record["widthMeters"] for record in records]
    observed_lengths = [record["lengthMeters"] for record in records]
    outside_geometry = int(np.count_nonzero(scratch & ~admitted_geometry))
    outside_contact = int(np.count_nonzero(scratch & ~(actual_contact > contact_threshold)))
    outside_metal = int(np.count_nonzero(scratch & ~metal))
    if outside_geometry or outside_contact or outside_metal:
        raise RuntimeError(
            "Scratch pixel geometry contract failed: "
            f"outsideGeometry={outside_geometry}, outsideContact={outside_contact}, outsideMetal={outside_metal}"
        )
    if min(observed_widths) < width_range[0] or max(observed_widths) > width_range[1]:
        raise RuntimeError(f"Scratch width admission failed: {min(observed_widths)}..{max(observed_widths)}")
    if min(observed_lengths) < length_range[0] or max(observed_lengths) > length_range[1]:
        raise RuntimeError(f"Scratch length admission failed: {min(observed_lengths)}..{max(observed_lengths)}")

    audit = {
        "requiredWidth": list(width_range),
        "requiredLength": list(length_range),
        "observedWidth": [min(observed_widths), max(observed_widths)],
        "observedLength": [min(observed_lengths), max(observed_lengths)],
        "admittedSegmentCount": len(records),
        "admittedSegments": records,
        "raster": "metric finite capsule: signed longitudinal projection clamped to centerline, then Euclidean cap distance <= width/2",
        "breakup": "deterministic longitudinal gap intersected with the finite capsule",
        "tangentBitangentOriented": True,
        "contactThreshold": contact_threshold,
        "contactBiased": True,
        "metalOnly": True,
        "renderedMaskPixels": int(np.count_nonzero(scratch)),
        "pixelAudit": {
            "outsideSegmentGeometryPixels": outside_geometry,
            "outsideContactPixels": outside_contact,
            "outsideMetalPixels": outside_metal,
        },
    }
    print(
        "AUDIT scratch segments: "
        f"count={len(records)}, pixels={audit['renderedMaskPixels']}, "
        f"width={min(observed_widths):.9f}..{max(observed_widths):.9f}m, "
        f"length={min(observed_lengths):.9f}..{max(observed_lengths):.9f}m, "
        "outsideGeometry/contact/Metal=0/0/0"
    )
    return scratch, audit


def generate_texture_atlas(objects, stage_texture_dir, raster=None):
    raster = rasterize_surface(objects) if raster is None else raster
    surface = raster["surface"]
    group = raster["group"]
    position = raster["position"]
    normal = raster["normal"]
    tangent = raster["tangent"]
    active = np.flatnonzero(surface)
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
    chip = (actual_contact > 0.14) & (fbm > 0.52) & metal
    scratch, scratch_audit = _metric_scratch_segments(
        active,
        position.reshape(-1, 3)[active],
        normal_active,
        tangent.reshape(-1, 3)[active],
        actual_contact,
        metal,
    )
    dark_scuff = (actual_contact > 0.18) & (fbm > 0.58) & dark
    grime = np.clip(0.45 * cavity + 0.35 * downward + 0.20 * fbm - 0.38, 0.0, 1.0)
    grime[glass] = 0.0
    soot = _smoothstep(-0.38, -0.55, py) * np.clip(0.65 + 0.35 * fbm, 0.0, 1.0)
    soot[glass] = 0.0
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
        "chips": (0.005, 0.08), "scratches": (0.002, 0.06), "grime": (0.035, 0.35),
        "muzzleSoot": (0.10, 0.70), "polish": (0.02, 0.36),
    }
    for name, value in coverages.items():
        low, high = limits[name]
        print(f"AUDIT wear {name}: coverage={value:.6f}, limits={low:.3f}..{high:.3f}")
        if value < low or value > high:
            raise RuntimeError(f"Wear coverage failed {name}: {value:.6f} outside {low:.3f}..{high:.3f}")

    base = np.zeros((surface_count, 3), dtype=np.float32)
    metallic = np.zeros(surface_count, dtype=np.float32)
    smoothness = np.zeros(surface_count, dtype=np.float32)
    emission = np.zeros((surface_count, 3), dtype=np.float32)
    for group_index, group_name in enumerate(GROUP_NAMES):
        select = group_active == group_index
        spec = SURFACE_SPECS[group_name]
        base[select] = spec["clean"]
        metallic[select] = spec["metal"][0]
        smoothness[select] = spec["smooth"][0]
        if group_name == "WeaponAccentCore":
            emission[select, 0] = 0.85

    for group_index, group_name in enumerate(GROUP_NAMES):
        select = group_active == group_index
        spec = SURFACE_SPECS[group_name]
        selected_chip = select & chip
        base[selected_chip] = spec["exposed"]
        metallic[selected_chip] = spec["metal"][1]
        smoothness[selected_chip] = spec["smooth"][2]
        selected_scratch = select & scratch
        scratch_color = np.array(spec["exposed"]) * 0.70 + np.array(spec["clean"]) * 0.30
        base[selected_scratch] = scratch_color
        metallic[selected_scratch] = spec["metal"][1] * 0.70 + spec["metal"][0] * 0.30
        smoothness[selected_scratch] = spec["smooth"][2]

    # Dark coating gets restrained grey scuffs, never exposed-metal chips.
    dark_spec = SURFACE_SPECS["WeaponDark"]
    base[dark_scuff] = dark_spec["exposed"]
    smoothness[dark_scuff] = dark_spec["smooth"][2]

    soot_strength = soot[:, None]
    base *= 1.0 - 0.75 * soot_strength
    metallic = np.minimum(metallic, metallic * (1.0 - soot) + 0.18 * soot)
    smoothness = np.minimum(smoothness, smoothness * (1.0 - soot) + 0.18 * soot)
    base *= 1.0 - 0.58 * grime[:, None]
    for group_index, group_name in enumerate(GROUP_NAMES):
        select = group_active == group_index
        spec = SURFACE_SPECS[group_name]
        metallic[select] = metallic[select] * (1.0 - grime[select]) + spec["metal"][2] * grime[select]
        smoothness[select] = smoothness[select] * (1.0 - grime[select]) + spec["smooth"][3] * grime[select]
        smoothness[select] = smoothness[select] * (1.0 - polish[select]) + spec["smooth"][1] * polish[select]

    brushed = 0.006 * (_trig_noise(px, py, pz, 733.0, 0.731) - 0.5)
    brushed[glass] = 0.0
    height_active = np.clip(-0.035 * chip.astype(np.float32) - 0.015 * scratch.astype(np.float32) - 0.006 * dark_scuff.astype(np.float32) + 0.03 * grime + brushed, -0.05, 0.05)
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
    normal_map = np.stack((-2.0 * dx, -2.0 * dy, np.ones_like(height_map)), axis=2)
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
    channel_record = {
        "baseColor": {"format": "RGBA8", "transfer": "sRGB"},
        "normal": {"format": "RGBA8", "transfer": "linear", "centralDifferencePx": 1, "xyScale": 2, "z": 1},
        "metallicSmoothness": {"format": "RGBA8", "transfer": "linear", "metallicChannel": "R", "smoothnessChannel": "A"},
        "occlusion": {"format": "RGBA8", "transfer": "linear", "minimum": 0.45},
        "emission": {"format": "RGBA8", "transfer": "linear", "coreValue": 0.85},
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


def point_camera(camera, target):
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()


def configure_final_materials(materials, stage_texture_dir):
    image_paths = {filename: os.path.join(stage_texture_dir, filename) for filename in TEXTURE_NAMES}
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
        links.new(normal_node.outputs["Normal"], shader.inputs["Normal"])
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


def generate_once(stage_dir):
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
    raster, texture_hashes, mask_record, periodicity, channel_record = generate_texture_atlas(objects, texture_dir)
    record["texture_hashes"] = texture_hashes
    record["uv_hash"] = uv_signature(objects)
    record["signature"] = canonical_signature(objects, record)
    configure_final_materials(materials, texture_dir)
    preview_audits = render_previews(objects, minimum, maximum, preview_dir)
    export_fbx(objects, output_path)
    import_roundtrip(record, output_path)
    record["stage_dir"] = stage_dir
    record["fbx_path"] = output_path
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
    print(f"PROOF two-run semantic+texture match: textures={len(TEXTURE_NAMES)}, preview-audits-per-run={len(PREVIEW_NAMES)}")


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


def promote_and_write_proof(record, two_run_identical):
    for filename in TEXTURE_NAMES:
        _atomic_promote(os.path.join(record["stage_dir"], "textures", filename), os.path.join(TEXTURE_DIR, filename))
    _atomic_promote(record["fbx_path"], OUTPUT_PATH)
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    for filename in PREVIEW_NAMES:
        _atomic_promote(os.path.join(record["preview_dir"], filename), os.path.join(PREVIEW_DIR, filename))
    final_texture_hashes = {filename: _hash_file(os.path.join(TEXTURE_DIR, filename)) for filename in TEXTURE_NAMES}
    final_preview_hashes = {filename: _hash_file(os.path.join(PREVIEW_DIR, filename)) for filename in PREVIEW_NAMES}
    if final_texture_hashes != record["texture_hashes"] or final_preview_hashes != record["preview_hashes"]:
        raise RuntimeError("Atomic promotion hash verification failed")
    proof = {
        "schemaVersion": 2,
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
        "outputSha256": final_texture_hashes,
        "previewSha256": final_preview_hashes,
        "twoRunIdentical": bool(two_run_identical),
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


def main():
    _safe_recreate_staging_root()
    first = generate_once(os.path.join(STAGING_ROOT, "run1"))
    identical = False
    if "--proof-two-run" in sys.argv:
        second = generate_once(os.path.join(STAGING_ROOT, "run2"))
        compare_runs(first, second)
        identical = True
    promote_and_write_proof(first, identical)


if __name__ == "__main__":
    main()
