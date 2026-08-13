"""Generate deterministic first-person and world pump-shotgun FBX assets.

Blender owns geometry, UVs, normals, embedded preview materials, export, and the
Blender-side contract audit. Unity import and material remapping remain phase 2.
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
MODEL_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models")
PREVIEW_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "shotgun")
MIN_OVERLAP = 0.005
TARGET_TOLERANCE = 0.025
MATERIAL_SPECS = {
    "WeaponMetal": (0.38, 0.055, 0.045, 1.0),
    "WeaponDark": (0.018, 0.012, 0.014, 1.0),
    "WeaponAccent": (0.82, 0.70, 0.48, 1.0),
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
    ("ReceiverAccentLeft", "Receiver", "Receiver accent inward X face -> receiver left side"),
    ("ReceiverAccentRight", "Receiver", "Receiver accent inward X face -> receiver right side"),
    ("MuzzleAccent", "MuzzleBand", "MuzzleAccent -> muzzle band"),
)

MATERIAL_GROUPS = {
    "WeaponMetal": ("Receiver", "Barrel", "MagazineTube", "MuzzleBand", "FrontSight", "RearSight"),
    "WeaponDark": ("Stock", "PistolGrip", "Pump", "MuzzleFace", "EjectionPort"),
}


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
    # Non-degenerate source torus has outer radius 1.35 and tube radius 0.35.
    # Scale after rotation in world axes to hit declared muzzle-band bounds.
    obj.scale = Vector((radius_x / 1.35, tube_radius / 0.35, radius_z / 1.35))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
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
    names = []
    for index in range(count):
        t = index / max(1, count - 1)
        y = y_front + 0.020 + (y_rear - y_front - 0.040) * t
        name = f"PumpRib{index + 1:02d}"
        add_box(name, (0.0, y, z_center), (rib_width, 0.014 if not profile["world_scale"] else 0.012, rib_height), 0.002)
        names.append(name)
    return names


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
    rib_names = add_measured_pump_ribs(profile, pump_bounds)

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
    accent_x = receiver_width * 0.48
    accent_width = 0.014 if not profile["world_scale"] else 0.012
    parts["ReceiverAccentLeft"] = add_box(
        "ReceiverAccentLeft", (-accent_x, -0.010, 0.028), (accent_width, 0.135 * sight_scale, 0.018 * sight_scale), 0.002
    )
    parts["ReceiverAccentRight"] = add_box(
        "ReceiverAccentRight", (accent_x, -0.010, 0.028), (accent_width, 0.135 * sight_scale, 0.018 * sight_scale), 0.002
    )
    parts["MuzzleAccent"] = add_torus(
        "MuzzleAccent", muzzle_band_front + 0.018, barrel_z, band_radius_x * 0.84, band_radius_z * 0.86,
        0.010 if not profile["world_scale"] else 0.008, cylinder_segments
    )

    part_bounds = {name: world_bounds(obj) for name, obj in parts.items()}
    for rib_name in rib_names:
        part_bounds[rib_name] = world_bounds(bpy.data.objects[rib_name])

    def group(names, object_name, material_name):
        return assign_and_join(
            [parts[name] if name in parts else bpy.data.objects[name] for name in names],
            object_name,
            materials[material_name],
            profile["key"],
        )

    metal = group(("Receiver", "Barrel", "MagazineTube", "MuzzleBand", "FrontSight", "RearSight"), "WeaponMetal", "WeaponMetal")
    dark = group(("Stock", "PistolGrip", "Pump", "MuzzleFace", "EjectionPort"), "WeaponDark", "WeaponDark")
    accent = group(("ReceiverAccentLeft", "ReceiverAccentRight", "MuzzleAccent", *rib_names), "WeaponAccent", "WeaponAccent")
    return (metal, dark, accent), part_bounds


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
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
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
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    if not result.data.uv_layers:
        raise RuntimeError(f"UV generation failed on {object_name}")
    result.data.uv_layers.active.name = "UVMap"
    result.data.update(calc_edges=True)
    return result


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
    return overlaps


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
        f"manifold=yes, normals=outward, UVMap=yes, transform=applied"
    )
    return len(obj.data.vertices), triangles


def combined_bounds(objects):
    bounds = [world_bounds(obj) for obj in objects]
    minimum = Vector(tuple(min(pair[0][i] for pair in bounds) for i in range(3)))
    maximum = Vector(tuple(max(pair[1][i] for pair in bounds) for i in range(3)))
    return minimum, maximum


def audit_asset(profile, objects, part_bounds, imported=False):
    expected_names = ("WeaponMetal", "WeaponDark", "WeaponAccent")
    if tuple(obj.name for obj in objects) != expected_names:
        raise RuntimeError(f"Stable export object names failed: {tuple(obj.name for obj in objects)}")
    if len({obj.data.name for obj in objects}) != len(objects):
        raise RuntimeError("Mesh data names are not unique")
    connection_overlaps = audit_connections(part_bounds) if part_bounds is not None else {}
    counts = [audit_mesh(obj) for obj in objects]
    total_vertices = sum(count[0] for count in counts)
    total_triangles = sum(count[1] for count in counts)
    minimum, maximum = combined_bounds(objects)
    dimensions = maximum - minimum
    if any(dimensions[i] > profile["hard_envelope"][i] + 1.0e-6 for i in range(3)):
        raise RuntimeError(f"Hard envelope failed: dimensions={tuple(dimensions)}")
    target_dimensions = profile["target_max"] - profile["target_min"]
    if any(abs(dimensions[i] - target_dimensions[i]) > TARGET_TOLERANCE for i in range(3)):
        raise RuntimeError(
            f"Target dimensions failed: dimensions={tuple(round(value, 6) for value in dimensions)}, "
            f"target={tuple(round(value, 6) for value in target_dimensions)}"
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
    print(f"AUDIT total {profile['key']}: vertices={total_vertices}, triangles={total_triangles}")
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
        "muzzle_min_y": round(muzzle_min_y, 7),
        "butt_max_y": round(butt_max_y, 7),
        "unity_muzzle_z": round(unity_muzzle_z, 7),
        "unity_butt_z": round(unity_butt_z, 7),
        "separation": round(separation, 7),
        "imported": imported,
    }


def canonical_signature(profile, objects, connection_overlaps, minimum, maximum):
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
    expected = ("fps_first-person.png",) if not profile["world_scale"] else (
        "world_front.png", "world_rear.png", "world_left.png", "world_right.png", "world_top.png", "world_three-quarter.png"
    )
    for filename in expected:
        path = os.path.join(PREVIEW_DIRECTORY, filename)
        if os.path.exists(path):
            os.remove(path)

    def render_one(filename):
        path = os.path.join(PREVIEW_DIRECTORY, filename)
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        if not os.path.isfile(path) or os.path.getsize(path) == 0:
            raise RuntimeError(f"Preview render missing or empty: {path}")
        print(f"PREVIEW {filename}: {path} ({os.path.getsize(path)} bytes)")

    if not profile["world_scale"]:
        camera.data.type = "PERSP"
        # 23.5mm on 36mm sensor approximates requested 75 degree horizontal FOV.
        camera.data.lens = 23.5
        camera.data.sensor_width = 36.0
        camera.data.shift_x = -0.12
        camera.data.shift_y = 0.12
        camera.location = center + Vector((0.35, -0.62, 0.26))
        point_camera(camera, center + Vector((0.0, -0.04, -0.005)))
        render_one("fps_first-person.png")
    else:
        span = maximum - minimum
        cardinal = (
            ("world_front.png", Vector((0.0, -1.0, 0.0)), max(span.x, span.z)),
            ("world_rear.png", Vector((0.0, 1.0, 0.0)), max(span.x, span.z)),
            ("world_left.png", Vector((-1.0, 0.0, 0.0)), max(span.y, span.z)),
            ("world_right.png", Vector((1.0, 0.0, 0.0)), max(span.y, span.z)),
            ("world_top.png", Vector((0.0, 0.0, 1.0)), max(span.x, span.y)),
        )
        for filename, direction, view_span in cardinal:
            camera.data.type = "ORTHO"
            camera.data.shift_x = 0.0
            camera.data.shift_y = 0.0
            camera.data.ortho_scale = view_span * 1.22
            camera.location = center + direction * max(1.0, view_span * 3.0)
            point_camera(camera, center)
            render_one(filename)

        preview_material = make_material("ShotgunPreviewScale", (0.08, 0.12, 0.18, 1.0))
        post_x = maximum.x + 0.070
        post = create_preview_box("PreviewScalePost", (post_x, center.y + 0.07, 0.015), (0.012, 0.012, 0.38), preview_material)
        tick_objects = [post]
        for index, z in enumerate((-0.14, -0.04, 0.06, 0.16)):
            tick_objects.append(create_preview_box(
                f"PreviewScaleTick{index}", (post_x, center.y + 0.07, z), (0.050, 0.016, 0.008), preview_material
            ))
        camera.data.type = "PERSP"
        camera.data.lens = 52.0
        camera.location = center + Vector((0.82, -1.18, 0.58))
        point_camera(camera, center + Vector((0.0, 0.0, -0.01)))
        render_one("world_three-quarter.png")
        for helper in tick_objects:
            bpy.data.objects.remove(helper, do_unlink=True)
        bpy.data.materials.remove(preview_material)

    actual = sorted(filename for filename in os.listdir(PREVIEW_DIRECTORY) if filename.lower().endswith(".png"))
    all_expected = sorted(("fps_first-person.png", "world_front.png", "world_rear.png", "world_left.png", "world_right.png", "world_top.png", "world_three-quarter.png"))
    if any(filename not in all_expected for filename in actual):
        raise RuntimeError(f"Preview inventory contains unexpected PNG: {actual}")
    if profile["world_scale"] and actual != all_expected:
        raise RuntimeError(f"Preview inventory mismatch: expected={all_expected}, actual={actual}")
    if not profile["world_scale"] and "fps_first-person.png" not in actual:
        raise RuntimeError(f"FPS preview inventory missing required image: {actual}")
    print(f"PREVIEW inventory: count={len(actual)} (profile={profile['key']}), names={actual}")


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
    if len(meshes) != 3:
        raise RuntimeError(f"Round-trip mesh count failed for {profile['key']}: {len(meshes)}")
    expected_names = ("WeaponMetal", "WeaponDark", "WeaponAccent")
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
        f"ROUNDTRIP {profile['key']}: objects=3, vertices={imported_record['vertex_count']}, "
        f"triangles={imported_record['triangle_count']}, bounds_match=yes, transforms=applied, static=yes"
    )
    print(
        f"ROUNDTRIP AXIS {profile['key']}: imported Blender -Y muzzle ordering preserved; "
        f"Unity +Z mapping verified from source anchors"
    )


def generate_profile(profile):
    reset_scene()
    materials = {name: make_material(name, color) for name, color in MATERIAL_SPECS.items()}
    objects, part_bounds = create_geometry(profile, materials)
    objects = tuple(objects)
    minimum, maximum = combined_bounds(objects)
    record = audit_asset(profile, objects, part_bounds)
    signature, _ = canonical_signature(profile, objects, record["connection_overlaps"], minimum, maximum)
    record["signature"] = signature
    render_previews(profile, objects, minimum, maximum)
    export_fbx(profile, objects)
    import_roundtrip(profile, record)
    print(f"SIGNATURE {profile['key']}: {signature}")
    print(f"RESULT {profile['key']} generation succeeded")
    return record


def compare_runs(first, second):
    if tuple(first) != tuple(second):
        raise RuntimeError("Two-run profile inventory mismatch")
    for first_record, second_record in zip(first, second):
        if first_record["signature"] != second_record["signature"]:
            raise RuntimeError(
                f"Two-run semantic signature mismatch for {first_record['profile']}: "
                f"{first_record['signature']} != {second_record['signature']}"
            )
        for key in ("vertex_count", "triangle_count", "bounds_min", "bounds_max", "dimensions", "connection_overlaps"):
            if first_record[key] != second_record[key]:
                raise RuntimeError(f"Two-run semantic mismatch for {first_record['profile']} field {key}")
    expected = sorted(("fps_first-person.png", "world_front.png", "world_rear.png", "world_left.png", "world_right.png", "world_top.png", "world_three-quarter.png"))
    actual = sorted(filename for filename in os.listdir(PREVIEW_DIRECTORY) if filename.lower().endswith(".png"))
    if actual != expected:
        raise RuntimeError(f"Two-run preview inventory mismatch: {actual}")
    if first[0]["signature"] == first[1]["signature"]:
        raise RuntimeError("FPS/world signatures unexpectedly identical")
    print(f"PROOF two-run semantic match: profiles={len(first)}, signatures_distinct=yes, previews={len(actual)}")


def main():
    proof_two_run = "--proof-two-run" in sys.argv
    first = [generate_profile(PROFILES["fps"]), generate_profile(PROFILES["world"])]
    if proof_two_run:
        second = [generate_profile(PROFILES["fps"]), generate_profile(PROFILES["world"])]
        compare_runs(first, second)
    print("RESULT Shotgun generation succeeded")


if __name__ == "__main__":
    main()
