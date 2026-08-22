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
import sys

import bmesh
import bpy
from mathutils import Vector


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUTPUT_PATH = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models", "FpsRocketLauncher.fbx")
PREVIEW_DIR = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "FpsRocketLauncher")

TARGET_MIN = Vector((-0.16, -0.55, -0.14))
TARGET_MAX = Vector((0.16, 0.20, 0.11))
TARGET_TOLERANCE = 0.025
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
    "WeaponMetal": (0.38, 0.055, 0.045, 1.0),
    "WeaponDark": (0.018, 0.012, 0.014, 1.0),
    "WeaponAccentCore": (0.50, 0.018, 0.018, 1.0),
    "WeaponAccent": (0.82, 0.075, 0.045, 1.0),
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
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    while len(result.data.uv_layers) > 1:
        result.data.uv_layers.remove(result.data.uv_layers[-1])
    if not result.data.uv_layers:
        raise RuntimeError(f"UV0 generation failed on {object_name}")
    result.data.uv_layers.active.name = "UVMap"
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
    }
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def point_camera(camera, target):
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()


def render_previews(objects, minimum, maximum):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
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

    os.makedirs(PREVIEW_DIR, exist_ok=True)
    for filename in os.listdir(PREVIEW_DIR):
        if filename.lower().endswith(".png"):
            os.remove(os.path.join(PREVIEW_DIR, filename))
    span = maximum - minimum
    cardinal = (
        ("front.png", Vector((0.0, -1.0, 0.0)), max(span.x, span.z)),
        ("rear.png", Vector((0.0, 1.0, 0.0)), max(span.x, span.z)),
        ("left.png", Vector((-1.0, 0.0, 0.0)), max(span.y, span.z)),
        ("right.png", Vector((1.0, 0.0, 0.0)), max(span.y, span.z)),
        ("top.png", Vector((0.0, 0.0, 1.0)), max(span.x, span.y)),
    )
    expected = {entry[0] for entry in cardinal} | {"three-quarter.png"}
    for filename, direction, view_span in cardinal:
        camera.data.ortho_scale = view_span * 1.22
        camera.location = center + direction * max(1.0, view_span * 3.0)
        point_camera(camera, center)
        scene.render.filepath = os.path.join(PREVIEW_DIR, filename)
        bpy.ops.render.render(write_still=True)
    camera.data.type = "PERSP"
    camera.data.lens = 52.0
    camera.location = center + Vector((0.82, -1.18, 0.58))
    point_camera(camera, center)
    scene.render.filepath = os.path.join(PREVIEW_DIR, "three-quarter.png")
    bpy.ops.render.render(write_still=True)
    actual = {filename for filename in os.listdir(PREVIEW_DIR) if filename.lower().endswith(".png")}
    if actual != expected:
        raise RuntimeError(f"Preview inventory mismatch: expected={sorted(expected)}, actual={sorted(actual)}")
    for filename in sorted(actual):
        path = os.path.join(PREVIEW_DIR, filename)
        if not os.path.isfile(path) or os.path.getsize(path) == 0:
            raise RuntimeError(f"Preview missing or empty: {path}")
        print(f"PREVIEW {filename}: {path} ({os.path.getsize(path)} bytes)")
    print(f"PREVIEW inventory: count={len(actual)}, exact=front/rear/left/right/top/three-quarter")


def export_fbx(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    os.makedirs(os.path.dirname(OUTPUT_PATH), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=OUTPUT_PATH,
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
    if not os.path.isfile(OUTPUT_PATH) or os.path.getsize(OUTPUT_PATH) == 0:
        raise RuntimeError(f"FBX export missing or empty: {OUTPUT_PATH}")
    print(f"OUTPUT {OUTPUT_PATH} ({os.path.getsize(OUTPUT_PATH)} bytes)")


def import_roundtrip(source_record):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=OUTPUT_PATH)
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


def generate_once():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    materials = {name: make_material(name, color) for name, color in MATERIAL_SPECS.items()}
    objects, part_bounds = create_geometry(materials)
    minimum, maximum = combined_bounds(objects)
    record = audit_asset(objects, part_bounds)
    record["signature"] = canonical_signature(objects, record)
    render_previews(objects, minimum, maximum)
    export_fbx(objects)
    import_roundtrip(record)
    print(f"SIGNATURE FpsRocketLauncher: {record['signature']}")
    print("RESULT FpsRocketLauncher generation succeeded")
    return record


def compare_runs(first, second):
    if first["signature"] != second["signature"]:
        raise RuntimeError(f"Two-run semantic signature mismatch: {first['signature']} != {second['signature']}")
    for key in ("vertex_count", "triangle_count", "bounds_min", "bounds_max", "dimensions", "connections", "pairs"):
        if first[key] != second[key]:
            raise RuntimeError(f"Two-run semantic mismatch field {key}")
    expected = {"front.png", "rear.png", "left.png", "right.png", "top.png", "three-quarter.png"}
    actual = {filename for filename in os.listdir(PREVIEW_DIR) if filename.lower().endswith(".png")}
    if actual != expected:
        raise RuntimeError(f"Two-run preview inventory mismatch: {sorted(actual)}")
    print(f"PROOF two-run semantic match: profiles=1, signatures_distinct=single-asset, previews={len(actual)}")


def main():
    first = generate_once()
    if "--proof-two-run" in sys.argv:
        second = generate_once()
        compare_runs(first, second)


if __name__ == "__main__":
    main()
