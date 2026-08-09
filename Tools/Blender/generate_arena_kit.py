"""Generate the deterministic modular arena visual kit used by MovementLab."""

import json
import math
import os
import sys
import traceback
from collections import OrderedDict

import bmesh
import bpy
from mathutils import Vector


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUTPUT_PATH = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models", "ArenaKit.fbx")
PREVIEW_DIR = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "ArenaKit")
SEMANTIC_AUDIT_PATH = os.path.join(PREVIEW_DIR, "semantic_audit.json")

UNIT_METERS = 1.0
SNAP_GRID = 0.25
MIN_OVERLAP = 0.005
BOUNDS_TOLERANCE = 0.01
AGGREGATE_TRIANGLE_MAX = 4300
FORWARD = Vector((0.0, -1.0, 0.0))
VIEW_NAMES = ("front", "rear", "left", "right", "top", "three-quarter")

MATERIAL_ORDER = ("ArenaPrimary", "ArenaTrim", "ArenaHazard", "ArenaGlow")
MATERIAL_COLORS = {
    "ArenaPrimary": (0.055, 0.12, 0.20, 1.0),
    "ArenaTrim": (0.12, 0.55, 0.78, 1.0),
    "ArenaHazard": (0.96, 0.24, 0.08, 1.0),
    "ArenaGlow": (0.18, 0.95, 0.88, 1.0),
}

# Contract declared before geometry. Dimensions use Blender X width, Z height,
# Y depth. Unity imports Blender -Y as local +Z with the export settings below.
MODULE_CONTRACTS = OrderedDict(
    (
        (
            "ArenaGoalShell",
            {
                "minimum": (-19.0, -10.0, 0.0),
                "maximum": (19.0, 0.0, 8.0),
                "pivot": "opening-plane ground center",
                "slots": MATERIAL_ORDER,
                "triangle_max": 1800,
                "opening": {"half_width": 18.0, "height": 7.0, "plane_y": 0.0},
            },
        ),
        (
            "ArenaRampRails",
            {
                "minimum": (-9.0, -10.0, -0.5),
                "maximum": (9.0, 10.0, 0.5),
                "pivot": "ramp collider center",
                "slots": ("ArenaPrimary", "ArenaTrim", "ArenaHazard", "ArenaGlow"),
                "triangle_max": 1200,
                "walkable_clear_half_width": 8.65,
                "collider_top_z": 0.25,
            },
        ),
        (
            "ArenaWallPylon",
            {
                "minimum": (-0.75, -0.5, 0.0),
                "maximum": (0.75, 0.5, 8.0),
                "pivot": "ground center",
                "slots": ("ArenaPrimary", "ArenaTrim", "ArenaGlow"),
                "triangle_max": 300,
            },
        ),
        (
            "ArenaPerimeterTruss",
            {
                "minimum": (-6.0, -0.5, -0.4),
                "maximum": (6.0, 0.5, 0.4),
                "pivot": "center",
                "slots": ("ArenaPrimary", "ArenaTrim"),
                "triangle_max": 500,
            },
        ),
        (
            "ArenaScoreboard",
            {
                "minimum": (-4.0, -0.2, -1.5),
                "maximum": (4.0, 0.2, 1.5),
                "pivot": "center",
                "slots": ("ArenaPrimary", "ArenaTrim", "ArenaGlow"),
                "triangle_max": 500,
            },
        ),
    )
)

# Multipart joints use measured world-space AABBs and require overlap on all axes.
CONNECTION_MAP = {
    "ArenaGoalShell": (
        ("GoalPostLeft", "GoalTopFrame"),
        ("GoalPostRight", "GoalTopFrame"),
        ("GoalPostLeft", "GoalSideLeft"),
        ("GoalPostRight", "GoalSideRight"),
        ("GoalSideLeft", "GoalFloorPanel"),
        ("GoalSideRight", "GoalFloorPanel"),
        ("GoalSideLeft", "GoalBackPanel"),
        ("GoalSideRight", "GoalBackPanel"),
        ("GoalFloorPanel", "GoalBackPanel"),
        ("GoalBackPanel", "GoalForwardMarker"),
        ("GoalPostLeft", "GoalHazardLeft"),
        ("GoalPostRight", "GoalHazardRight"),
        ("GoalTopFrame", "GoalGlowBar"),
    ),
    "ArenaRampRails": (
        ("RampRailLeft", "RampUnderBraceFront"),
        ("RampRailRight", "RampUnderBraceFront"),
        ("RampRailLeft", "RampUnderBraceRear"),
        ("RampRailRight", "RampUnderBraceRear"),
        ("RampRailLeft", "RampHazardLeft"),
        ("RampRailRight", "RampHazardRight"),
        ("RampUnderBraceFront", "RampGlowFront"),
    ),
    "ArenaWallPylon": (
        ("PylonShaft", "PylonBase"),
        ("PylonShaft", "PylonTop"),
        ("PylonShaft", "PylonBandLow"),
        ("PylonShaft", "PylonBandHigh"),
        ("PylonShaft", "PylonGlow"),
    ),
    "ArenaPerimeterTruss": (
        ("TrussBottom", "TrussEndLeft"),
        ("TrussTop", "TrussEndLeft"),
        ("TrussBottom", "TrussEndRight"),
        ("TrussTop", "TrussEndRight"),
        ("TrussBottom", "TrussDiagonalLeft"),
        ("TrussTop", "TrussDiagonalLeft"),
        ("TrussBottom", "TrussDiagonalRight"),
        ("TrussTop", "TrussDiagonalRight"),
    ),
    "ArenaScoreboard": (
        ("ScoreFrameLeft", "ScoreFrameTop"),
        ("ScoreFrameRight", "ScoreFrameTop"),
        ("ScoreFrameLeft", "ScoreFrameBottom"),
        ("ScoreFrameRight", "ScoreFrameBottom"),
        ("ScoreBacking", "ScoreFrameLeft"),
        ("ScoreBacking", "ScoreFrameRight"),
        ("ScoreScreen", "ScoreBacking"),
        ("ScoreScreen", "ScoreGlow"),
    ),
}


def make_material(name, color):
    material = bpy.data.materials.new(name=name)
    material.diffuse_color = color
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Roughness"].default_value = 0.66
    bsdf.inputs["Metallic"].default_value = 0.15 if name == "ArenaTrim" else 0.02
    if name == "ArenaGlow":
        bsdf.inputs["Emission Color"].default_value = color
        bsdf.inputs["Emission Strength"].default_value = 1.8
    return material


def apply_bevel(obj, width):
    if width <= 0.0:
        return
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    modifier = obj.modifiers.new(name="HardEdgeBevel", type="BEVEL")
    modifier.width = width
    modifier.segments = 1
    modifier.limit_method = "ANGLE"
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)


def add_box(name, center, dimensions, material, parts, bevel=0.035, rotation=None):
    if any(value <= 0.0 for value in dimensions):
        raise RuntimeError(f"Invalid dimensions for {name}: {dimensions}")
    rotation = rotation or (0.0, 0.0, 0.0)
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=center, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    obj.scale = Vector(dimensions) * 0.5
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    apply_bevel(obj, min(bevel, min(dimensions) * 0.18))
    obj.data.materials.append(material)
    parts[name] = obj
    return obj


def add_beam_between(name, start, end, depth, thickness, material, parts, bevel=0.025):
    start = Vector(start)
    end = Vector(end)
    direction = end - start
    length = direction.length
    if length <= 0.0:
        raise RuntimeError(f"Zero-length beam {name}")
    rotation = (0.0, -math.atan2(direction.z, direction.x), 0.0)
    return add_box(
        name,
        (start + end) * 0.5,
        (length, depth, thickness),
        material,
        parts,
        bevel,
        rotation,
    )


def add_forward_wedge(name, material, parts):
    # Asymmetric rear marker: tip points Blender -Y, which imports as Unity +Z.
    verts = (
        (7.0, -9.42, 5.45),
        (10.0, -9.42, 5.45),
        (8.5, -10.0, 5.45),
        (7.0, -9.42, 6.55),
        (10.0, -9.42, 6.55),
        (8.5, -10.0, 6.55),
    )
    faces = (
        (0, 1, 2),
        (3, 5, 4),
        (0, 3, 4, 1),
        (1, 4, 5, 2),
        (2, 5, 3, 0),
    )
    mesh = bpy.data.meshes.new(f"{name}Mesh")
    mesh.from_pydata(verts, (), faces)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)
    parts[name] = obj
    return obj


def create_goal_shell(materials):
    parts = OrderedDict()
    add_box("GoalPostLeft", (-18.5, -0.5, 3.505), (1.0, 1.0, 7.01), materials["ArenaPrimary"], parts)
    add_box("GoalPostRight", (18.5, -0.5, 3.505), (1.0, 1.0, 7.01), materials["ArenaPrimary"], parts)
    add_box("GoalTopFrame", (0.0, -0.5, 7.5), (38.0, 1.0, 1.0), materials["ArenaTrim"], parts)
    add_box("GoalSideLeft", (-18.5, -5.25, 3.5), (1.0, 9.5, 7.0), materials["ArenaPrimary"], parts)
    add_box("GoalSideRight", (18.5, -5.25, 3.5), (1.0, 9.5, 7.0), materials["ArenaPrimary"], parts)
    add_box("GoalFloorPanel", (0.0, -5.25, 0.175), (36.04, 9.5, 0.35), materials["ArenaPrimary"], parts)
    add_box("GoalBackPanel", (0.0, -9.25, 3.625), (36.04, 0.5, 6.75), materials["ArenaPrimary"], parts)
    add_forward_wedge("GoalForwardMarker", materials["ArenaTrim"], parts)
    add_box("GoalHazardLeft", (-18.5, -0.14, 3.5), (0.58, 0.28, 2.0), materials["ArenaHazard"], parts, 0.02)
    add_box("GoalHazardRight", (18.5, -0.14, 3.5), (0.58, 0.28, 2.0), materials["ArenaHazard"], parts, 0.02)
    add_box("GoalGlowBar", (0.0, -0.14, 7.5), (12.0, 0.28, 0.36), materials["ArenaGlow"], parts, 0.02)
    return parts


def create_ramp_rails(materials):
    parts = OrderedDict()
    add_box("RampRailLeft", (-8.85, 0.0, 0.0), (0.3, 20.0, 1.0), materials["ArenaPrimary"], parts)
    add_box("RampRailRight", (8.85, 0.0, 0.0), (0.3, 20.0, 1.0), materials["ArenaPrimary"], parts)
    add_box("RampUnderBraceFront", (0.0, -9.85, -0.4), (17.72, 0.3, 0.2), materials["ArenaTrim"], parts, 0.025)
    add_box("RampUnderBraceRear", (0.0, 9.85, -0.4), (17.72, 0.3, 0.2), materials["ArenaTrim"], parts, 0.025)
    add_box("RampHazardLeft", (-8.71, 0.0, 0.0), (0.28, 4.0, 0.36), materials["ArenaHazard"], parts, 0.02)
    add_box("RampHazardRight", (8.71, 0.0, 0.0), (0.28, 4.0, 0.36), materials["ArenaHazard"], parts, 0.02)
    add_box("RampGlowFront", (0.0, -9.87, -0.34), (8.0, 0.24, 0.12), materials["ArenaGlow"], parts, 0.015)
    return parts


def create_wall_pylon(materials):
    parts = OrderedDict()
    add_box("PylonShaft", (0.0, 0.0, 4.0), (1.1, 0.7, 7.24), materials["ArenaPrimary"], parts, 0.06)
    add_box("PylonBase", (0.0, 0.0, 0.25), (1.5, 1.0, 0.5), materials["ArenaTrim"], parts, 0.045)
    add_box("PylonTop", (0.0, 0.0, 7.8), (1.5, 1.0, 0.4), materials["ArenaTrim"], parts, 0.045)
    add_box("PylonBandLow", (0.0, 0.0, 2.35), (1.3, 0.9, 0.25), materials["ArenaTrim"], parts, 0.025)
    add_box("PylonBandHigh", (0.0, 0.0, 5.65), (1.3, 0.9, 0.25), materials["ArenaTrim"], parts, 0.025)
    add_box("PylonGlow", (0.0, -0.39, 4.0), (0.34, 0.18, 5.4), materials["ArenaGlow"], parts, 0.02)
    return parts


def create_perimeter_truss(materials):
    parts = OrderedDict()
    add_box("TrussBottom", (0.0, 0.0, -0.3), (12.0, 1.0, 0.2), materials["ArenaPrimary"], parts, 0.035)
    add_box("TrussTop", (0.0, 0.0, 0.3), (12.0, 1.0, 0.2), materials["ArenaPrimary"], parts, 0.035)
    add_box("TrussEndLeft", (-5.75, 0.0, 0.0), (0.5, 0.82, 0.62), materials["ArenaTrim"], parts, 0.03)
    add_box("TrussEndRight", (5.75, 0.0, 0.0), (0.5, 0.82, 0.62), materials["ArenaTrim"], parts, 0.03)
    add_beam_between(
        "TrussDiagonalLeft",
        (-5.55, 0.0, -0.27),
        (-0.10, 0.0, 0.27),
        0.72,
        0.16,
        materials["ArenaTrim"],
        parts,
        0.025,
    )
    add_beam_between(
        "TrussDiagonalRight",
        (0.10, 0.0, 0.27),
        (5.55, 0.0, -0.27),
        0.72,
        0.16,
        materials["ArenaTrim"],
        parts,
        0.025,
    )
    return parts


def create_scoreboard(materials):
    parts = OrderedDict()
    add_box("ScoreFrameLeft", (-3.8, 0.0, 0.0), (0.4, 0.4, 3.0), materials["ArenaTrim"], parts, 0.035)
    add_box("ScoreFrameRight", (3.8, 0.0, 0.0), (0.4, 0.4, 3.0), materials["ArenaTrim"], parts, 0.035)
    add_box("ScoreFrameTop", (0.0, 0.0, 1.3), (7.6, 0.4, 0.4), materials["ArenaTrim"], parts, 0.035)
    add_box("ScoreFrameBottom", (0.0, 0.0, -1.3), (7.6, 0.4, 0.4), materials["ArenaTrim"], parts, 0.035)
    add_box("ScoreBacking", (0.0, 0.025, 0.0), (7.24, 0.35, 2.24), materials["ArenaPrimary"], parts, 0.025)
    add_box("ScoreScreen", (0.0, -0.17, 0.0), (6.8, 0.06, 1.82), materials["ArenaGlow"], parts, 0.012)
    add_box("ScoreGlow", (0.0, -0.175, 0.88), (2.6, 0.05, 0.10), materials["ArenaGlow"], parts, 0.008)
    return parts


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        Vector(tuple(min(corner[index] for corner in corners) for index in range(3))),
        Vector(tuple(max(corner[index] for corner in corners) for index in range(3))),
    )


def audit_connections(module_name, part_bounds):
    results = []
    for first, second in CONNECTION_MAP[module_name]:
        first_min, first_max = part_bounds[first]
        second_min, second_max = part_bounds[second]
        overlaps = tuple(
            min(first_max[index], second_max[index]) - max(first_min[index], second_min[index])
            for index in range(3)
        )
        if min(overlaps) < MIN_OVERLAP - 1e-6:
            raise RuntimeError(f"Connection {module_name}/{first}->{second} failed: {overlaps}")
        rounded = tuple(round(value, 6) for value in overlaps)
        print(f"AUDIT connection {module_name}/{first}->{second}: overlap={rounded}")
        results.append({"first": first, "second": second, "overlap": rounded})
    return results


def normalize_material_slots(obj, required_slots):
    polygon_materials = []
    for polygon in obj.data.polygons:
        slot = obj.material_slots[polygon.material_index]
        if slot.material is None:
            raise RuntimeError(f"Missing source material on {obj.name}")
        polygon_materials.append(slot.material.name)
    obj.data.materials.clear()
    for material_name in required_slots:
        obj.data.materials.append(bpy.data.materials[material_name])
    lookup = {name: index for index, name in enumerate(required_slots)}
    for polygon, material_name in zip(obj.data.polygons, polygon_materials):
        if material_name not in lookup:
            raise RuntimeError(f"Unexpected material {material_name} on {obj.name}")
        polygon.material_index = lookup[material_name]


def add_uv_and_vertex_color(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")
    if len(obj.data.uv_layers) != 1:
        raise RuntimeError(f"Expected one UV layer on {obj.name}, got {len(obj.data.uv_layers)}")
    obj.data.uv_layers[0].name = "UVMap"

    color_attribute = obj.data.color_attributes.new(
        name="ArenaVariation", type="BYTE_COLOR", domain="CORNER"
    )
    minimum, maximum = world_bounds(obj)
    height = max(maximum.z - minimum.z, 1e-6)
    for loop in obj.data.loops:
        vertex = obj.data.vertices[loop.vertex_index]
        height_factor = (vertex.co.z - minimum.z) / height
        normal_factor = max(0.0, min(1.0, vertex.normal.z * 0.5 + 0.5))
        variation = 0.58 + 0.24 * height_factor + 0.18 * normal_factor
        color_attribute.data[loop.index].color = (variation, variation, variation, 1.0)
    obj.select_set(False)


def join_module(module_name, parts):
    contract = MODULE_CONTRACTS[module_name]
    part_bounds = {name: world_bounds(obj) for name, obj in parts.items()}
    connection_results = audit_connections(module_name, part_bounds)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts.values():
        obj.select_set(True)
    first = next(iter(parts.values()))
    bpy.context.view_layer.objects.active = first
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = module_name
    result.data.name = f"{module_name}Mesh"
    normalize_material_slots(result, contract["slots"])
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR", center="MEDIAN")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for polygon in result.data.polygons:
        polygon.use_smooth = False
    add_uv_and_vertex_color(result)
    result["declared_part_count"] = len(parts)
    return result, part_bounds, connection_results


def connected_component_count(bm):
    remaining = set(bm.verts)
    count = 0
    while remaining:
        count += 1
        stack = [remaining.pop()]
        while stack:
            vertex = stack.pop()
            for edge in vertex.link_edges:
                other = edge.other_vert(vertex)
                if other in remaining:
                    remaining.remove(other)
                    stack.append(other)
    return count


def audit_component_winding(obj):
    """Prove closed-component orientation instead of relying on aggregate volume."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    remaining = set(bm.verts)
    components = []
    while remaining:
        seed = remaining.pop()
        component_vertices = {seed}
        component_faces = set()
        stack = [seed]
        while stack:
            vertex = stack.pop()
            for edge in vertex.link_edges:
                for face in edge.link_faces:
                    component_faces.add(face)
                other = edge.other_vert(vertex)
                if other in remaining:
                    remaining.remove(other)
                    component_vertices.add(other)
                    stack.append(other)

        edge_directions = {}
        for face in component_faces:
            for loop in face.loops:
                edge_directions.setdefault(loop.edge, []).append(
                    (loop.vert, loop.link_loop_next.vert)
                )
        inconsistent_edges = []
        for edge, directions in edge_directions.items():
            if len(directions) != 2:
                inconsistent_edges.append((edge.index, len(directions)))
                continue
            first_start, first_end = directions[0]
            second_start, second_end = directions[1]
            if first_start is not second_end or first_end is not second_start:
                inconsistent_edges.append((edge.index, 2))
        if inconsistent_edges:
            raise RuntimeError(
                f"Inconsistent face winding on {obj.name} component: edges={inconsistent_edges[:8]}"
            )

        ordered_vertices = sorted(component_vertices, key=lambda vertex: vertex.index)
        ordered_faces = sorted(component_faces, key=lambda face: face.index)
        reference = sum((vertex.co for vertex in ordered_vertices), Vector()) / len(
            ordered_vertices
        )
        signed_volume = 0.0
        for face in ordered_faces:
            vertices = [loop.vert.co - reference for loop in face.loops]
            anchor = vertices[0]
            for index in range(1, len(vertices) - 1):
                signed_volume += anchor.dot(vertices[index].cross(vertices[index + 1])) / 6.0
        minimum = Vector(
            tuple(min(vertex.co[index] for vertex in component_vertices) for index in range(3))
        )
        maximum = Vector(
            tuple(max(vertex.co[index] for vertex in component_vertices) for index in range(3))
        )
        if not math.isfinite(signed_volume) or signed_volume <= 1e-9:
            raise RuntimeError(
                f"Normals/winding failed on {obj.name} component: signed_volume={signed_volume}"
            )
        component = {
            "vertices": len(component_vertices),
            "faces": len(component_faces),
            "minimum": tuple(round(value, 6) for value in minimum),
            "maximum": tuple(round(value, 6) for value in maximum),
            "signed_volume": round(signed_volume, 6),
        }
        components.append(component)

    components.sort(key=lambda component: (component["minimum"], component["maximum"]))
    for index, component in enumerate(components):
        component["index"] = index
        print(
            f"AUDIT component {obj.name}[{component['index']}]: "
            f"bounds_min={component['minimum']}, bounds_max={component['maximum']}, "
            f"vertices={component['vertices']}, faces={component['faces']}, "
            f"signed_volume={component['signed_volume']:.6f}, winding=outward"
        )
    bm.free()
    return components


def audit_goal_opening(part_bounds):
    opening_half_width = MODULE_CONTRACTS["ArenaGoalShell"]["opening"]["half_width"]
    opening_height = MODULE_CONTRACTS["ArenaGoalShell"]["opening"]["height"]
    plane_y = MODULE_CONTRACTS["ArenaGoalShell"]["opening"]["plane_y"]
    blockers = []
    for name, (minimum, maximum) in part_bounds.items():
        touches_plane = minimum.y - 1e-6 <= plane_y <= maximum.y + 1e-6
        overlaps_width = minimum.x < opening_half_width - 1e-6 and maximum.x > -opening_half_width + 1e-6
        overlaps_height = minimum.z < opening_height - 1e-6 and maximum.z > 0.0 + 1e-6
        if touches_plane and overlaps_width and overlaps_height:
            blockers.append(name)
    if blockers:
        raise RuntimeError(f"Goal opening 36x7m obstructed at plane Y=0 by {blockers}")
    marker_min, marker_max = part_bounds["GoalForwardMarker"]
    if abs(marker_min.y + 10.0) > 1e-6 or marker_max.y > -9.4 + 1e-6:
        raise RuntimeError(f"Forward marker direction failed: {tuple(marker_min)} to {tuple(marker_max)}")
    print("AUDIT goal opening: 36.000000x7.000000 m clear at Y=0")
    print("AUDIT forward marker: asymmetric rear tip Blender Y=-10.000000 -> Unity local +Z")


def audit_ramp_clearance(parts):
    contract = MODULE_CONTRACTS["ArenaRampRails"]
    half_width = contract["walkable_clear_half_width"]
    collider_top = contract["collider_top_z"]
    violations = []
    for name, obj in parts.items():
        for corner in obj.bound_box:
            world_corner = obj.matrix_world @ Vector(corner)
            if world_corner.z > collider_top + 1e-6 and abs(world_corner.x) < half_width - 1e-6:
                violations.append(name)
                break
    if violations:
        raise RuntimeError(f"Ramp visual enters clear walkable top: {violations}")
    print(
        f"AUDIT ramp clearance: abs(X)<{half_width:.6f} clear above collider top Z={collider_top:.6f}"
    )


def audit_module(obj, expected_part_count):
    contract = MODULE_CONTRACTS[obj.name]
    if obj.type != "MESH":
        raise RuntimeError(f"{obj.name} is not a mesh")
    if obj.location.length > 1e-6:
        raise RuntimeError(f"Pivot failed on {obj.name}: location={tuple(obj.location)}")
    if any(abs(value) > 1e-6 for value in obj.rotation_euler):
        raise RuntimeError(f"Unapplied rotation on {obj.name}")
    if any(abs(value - 1.0) > 1e-6 for value in obj.scale):
        raise RuntimeError(f"Unapplied scale on {obj.name}")
    if tuple(slot.material.name for slot in obj.material_slots) != contract["slots"]:
        raise RuntimeError(f"Material slots failed on {obj.name}")
    used_slots = {polygon.material_index for polygon in obj.data.polygons}
    if used_slots != set(range(len(contract["slots"]))):
        raise RuntimeError(f"Unused material slot on {obj.name}: used={used_slots}")
    if len(obj.data.uv_layers) != 1 or obj.data.uv_layers[0].name != "UVMap":
        raise RuntimeError(f"UVMap contract failed on {obj.name}")
    colors = obj.data.color_attributes
    if len(colors) != 1 or colors[0].name != "ArenaVariation" or colors[0].domain != "CORNER":
        raise RuntimeError(f"Vertex color contract failed on {obj.name}")
    if any(polygon.use_smooth for polygon in obj.data.polygons):
        raise RuntimeError(f"Smooth normals found on faceted module {obj.name}")
    if any(not math.isfinite(component) for vertex in obj.data.vertices for component in vertex.co):
        raise RuntimeError(f"Non-finite vertex on {obj.name}")

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    zero_edges = sum(1 for edge in bm.edges if edge.calc_length() <= 1e-8)
    zero_faces = sum(1 for face in bm.faces if face.calc_area() <= 1e-10)
    non_manifold = sum(1 for edge in bm.edges if not edge.is_manifold)
    loose_vertices = sum(1 for vertex in bm.verts if not vertex.link_edges)
    components = connected_component_count(bm)
    signed_volume = bm.calc_volume(signed=True)
    bm.free()
    if zero_edges or zero_faces or non_manifold or loose_vertices:
        raise RuntimeError(
            f"Topology failed on {obj.name}: zero_edges={zero_edges}, zero_faces={zero_faces}, "
            f"non_manifold={non_manifold}, loose_vertices={loose_vertices}"
        )
    if components != expected_part_count:
        raise RuntimeError(
            f"Unexpected loose shell count on {obj.name}: {components} != declared {expected_part_count}"
        )
    component_winding = audit_component_winding(obj)
    if len(component_winding) != components:
        raise RuntimeError(
            f"Winding component count failed on {obj.name}: "
            f"{len(component_winding)} != {components}"
        )
    if not math.isfinite(signed_volume) or signed_volume <= 1e-9:
        raise RuntimeError(f"Aggregate signed volume failed on {obj.name}: signed_volume={signed_volume}")
    if obj.name == "ArenaGoalShell":
        marker_minimum = (7.0, -10.0, 5.45)
        marker_maximum = (10.0, -9.42, 6.55)
        marker_components = [
            component
            for component in component_winding
            if all(
                abs(component["minimum"][index] - marker_minimum[index]) <= 1e-6
                and abs(component["maximum"][index] - marker_maximum[index]) <= 1e-6
                for index in range(3)
            )
        ]
        if len(marker_components) != 1:
            raise RuntimeError(
                f"GoalForwardMarker winding component not uniquely identified: {marker_components}"
            )
        marker = marker_components[0]
        print(
            f"AUDIT component GoalForwardMarker: signed_volume={marker['signed_volume']:.6f}, "
            "winding=outward"
        )

    obj.data.calc_loop_triangles()
    triangles = len(obj.data.loop_triangles)
    if triangles > contract["triangle_max"]:
        raise RuntimeError(f"Triangle budget failed on {obj.name}: {triangles}>{contract['triangle_max']}")
    minimum, maximum = world_bounds(obj)
    expected_minimum = Vector(contract["minimum"])
    expected_maximum = Vector(contract["maximum"])
    if any(abs(minimum[index] - expected_minimum[index]) > BOUNDS_TOLERANCE for index in range(3)):
        raise RuntimeError(f"Minimum bounds failed on {obj.name}: {tuple(minimum)}")
    if any(abs(maximum[index] - expected_maximum[index]) > BOUNDS_TOLERANCE for index in range(3)):
        raise RuntimeError(f"Maximum bounds failed on {obj.name}: {tuple(maximum)}")

    dimensions = maximum - minimum
    print(
        f"AUDIT module {obj.name}: bounds_min={tuple(round(v, 6) for v in minimum)}, "
        f"bounds_max={tuple(round(v, 6) for v in maximum)}, "
        f"dimensions_WxHxD={dimensions.x:.6f}x{dimensions.z:.6f}x{dimensions.y:.6f} m"
    )
    print(
        f"AUDIT mesh {obj.name}: vertices={len(obj.data.vertices)}, triangles={triangles}/{contract['triangle_max']}, "
        f"components={components} declared, manifold=yes, normals=outward+faceted, "
        f"UVMap=1, vertex_color=ArenaVariation, transform=applied"
    )
    print(f"AUDIT slots {obj.name}: {tuple(slot.material.name for slot in obj.material_slots)}")
    print(f"AUDIT pivot {obj.name}: origin=(0,0,0), contract={contract['pivot']}")
    return {
        "name": obj.name,
        "mesh": obj.data.name,
        "minimum": [round(value, 6) for value in minimum],
        "maximum": [round(value, 6) for value in maximum],
        "dimensions_w_h_d": [round(dimensions.x, 6), round(dimensions.z, 6), round(dimensions.y, 6)],
        "vertices": len(obj.data.vertices),
        "triangles": triangles,
        "triangle_max": contract["triangle_max"],
        "components": components,
        "component_winding": component_winding,
        "slots": list(contract["slots"]),
        "uv": "UVMap",
        "vertex_color": "ArenaVariation",
        "pivot": contract["pivot"],
    }


def semantic_snapshot(objects):
    snapshot = []
    for obj in objects:
        obj.data.calc_loop_triangles()
        minimum, maximum = world_bounds(obj)
        snapshot.append(
            (
                obj.name,
                obj.data.name,
                tuple(round(value, 6) for value in minimum),
                tuple(round(value, 6) for value in maximum),
                len(obj.data.vertices),
                len(obj.data.loop_triangles),
                tuple(slot.material.name for slot in obj.material_slots),
                tuple(round(value, 6) for value in obj.location),
                tuple(round(value, 6) for value in obj.rotation_euler),
                tuple(round(value, 6) for value in obj.scale),
            )
        )
    return tuple(snapshot)


def point_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def render_previews(objects):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.image_settings.color_mode = "RGBA"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("ArenaPreviewWorld")
    scene.world.color = (0.012, 0.018, 0.032)

    camera_data = bpy.data.cameras.new("ArenaPreviewCamera")
    camera = bpy.data.objects.new("ArenaPreviewCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.clip_start = 0.01
    camera.data.clip_end = 1000.0
    scene.camera = camera

    lights = []
    for name, energy, size in (("ArenaPreviewKey", 1300.0, 8.0), ("ArenaPreviewFill", 700.0, 10.0)):
        light_data = bpy.data.lights.new(name, type="AREA")
        light_data.energy = energy
        light_data.shape = "DISK"
        light_data.size = size
        light = bpy.data.objects.new(name, light_data)
        bpy.context.collection.objects.link(light)
        lights.append(light)

    os.makedirs(PREVIEW_DIR, exist_ok=True)
    view_vectors = OrderedDict(
        (
            ("front", Vector((0.0, -1.0, 0.08))),
            ("rear", Vector((0.0, 1.0, 0.08))),
            ("left", Vector((-1.0, 0.0, 0.08))),
            ("right", Vector((1.0, 0.0, 0.08))),
            ("top", Vector((0.0, 0.0, 1.0))),
            ("three-quarter", Vector((1.0, -1.0, 0.68))),
        )
    )

    original_hidden = {obj.name: obj.hide_render for obj in objects}
    for target in objects:
        for obj in objects:
            obj.hide_render = obj != target
        minimum, maximum = world_bounds(target)
        center = (minimum + maximum) * 0.5
        dimensions = maximum - minimum
        max_dimension = max(dimensions)
        lights[0].location = center + Vector((max_dimension, -max_dimension, max_dimension * 1.2))
        lights[1].location = center + Vector((-max_dimension, max_dimension * 0.6, max_dimension * 0.5))
        point_at(lights[0], center)
        point_at(lights[1], center)
        module_dir = os.path.join(PREVIEW_DIR, target.name)
        os.makedirs(module_dir, exist_ok=True)
        for view_name, raw_direction in view_vectors.items():
            direction = raw_direction.normalized()
            camera.location = center + direction * max(8.0, max_dimension * 2.4)
            point_at(camera, center)
            if view_name in ("front", "rear"):
                projected_span = max(dimensions.x, dimensions.z)
            elif view_name in ("left", "right"):
                projected_span = max(dimensions.y, dimensions.z)
            elif view_name == "top":
                projected_span = max(dimensions.x, dimensions.y)
            else:
                projected_span = max_dimension * 1.15
            camera.data.ortho_scale = max(1.0, projected_span * 1.22)
            output = os.path.join(module_dir, f"{view_name}.png")
            scene.render.filepath = output
            bpy.ops.render.render(write_still=True)
            if not os.path.isfile(output) or os.path.getsize(output) == 0:
                raise RuntimeError(f"Preview missing or empty: {output}")
            print(f"PREVIEW {target.name}/{view_name}: {output} ({os.path.getsize(output)} bytes)")
    for obj in objects:
        obj.hide_render = original_hidden[obj.name]


def write_semantic_audit(module_results, connection_results, aggregate_triangles):
    payload = {
        "asset": "ArenaKit",
        "aggregate_triangle_max": AGGREGATE_TRIANGLE_MAX,
        "aggregate_triangles": aggregate_triangles,
        "axis_forward": "-Z",
        "axis_up": "Y",
        "blender_forward": "-Y",
        "connections": connection_results,
        "material_order": list(MATERIAL_ORDER),
        "minimum_overlap": MIN_OVERLAP,
        "modules": module_results,
        "snap_grid": SNAP_GRID,
        "unit_meters": UNIT_METERS,
    }
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    with open(SEMANTIC_AUDIT_PATH, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")
    print(f"SEMANTIC_AUDIT {SEMANTIC_AUDIT_PATH} ({os.path.getsize(SEMANTIC_AUDIT_PATH)} bytes)")


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
        raise RuntimeError(f"FBX missing or empty: {OUTPUT_PATH}")
    print(f"OUTPUT {OUTPUT_PATH} ({os.path.getsize(OUTPUT_PATH)} bytes)")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = UNIT_METERS
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)

    materials = {name: make_material(name, MATERIAL_COLORS[name]) for name in MATERIAL_ORDER}
    creators = OrderedDict(
        (
            ("ArenaGoalShell", create_goal_shell),
            ("ArenaRampRails", create_ramp_rails),
            ("ArenaWallPylon", create_wall_pylon),
            ("ArenaPerimeterTruss", create_perimeter_truss),
            ("ArenaScoreboard", create_scoreboard),
        )
    )
    objects = []
    source_parts = {}
    all_connection_results = {}
    for module_name, creator in creators.items():
        parts = creator(materials)
        if module_name == "ArenaGoalShell":
            audit_goal_opening({name: world_bounds(obj) for name, obj in parts.items()})
        if module_name == "ArenaRampRails":
            audit_ramp_clearance(parts)
        obj, part_bounds, connection_results = join_module(module_name, parts)
        objects.append(obj)
        source_parts[module_name] = len(part_bounds)
        all_connection_results[module_name] = connection_results

    if tuple(obj.name for obj in objects) != tuple(MODULE_CONTRACTS):
        raise RuntimeError(f"Stable object names failed: {tuple(obj.name for obj in objects)}")
    if len({obj.data.name for obj in objects}) != len(objects):
        raise RuntimeError("Mesh data names are not unique")
    if any(obj.name != module_name for obj, module_name in zip(objects, MODULE_CONTRACTS)):
        raise RuntimeError("Module order failed")

    module_results = [audit_module(obj, source_parts[obj.name]) for obj in objects]
    aggregate_triangles = sum(result["triangles"] for result in module_results)
    if aggregate_triangles > AGGREGATE_TRIANGLE_MAX:
        raise RuntimeError(
            f"Aggregate triangle budget failed: {aggregate_triangles}>{AGGREGATE_TRIANGLE_MAX}"
        )
    print(f"AUDIT aggregate: modules=5, triangles={aggregate_triangles}/{AGGREGATE_TRIANGLE_MAX}")
    print(
        f"AUDIT contract: units={UNIT_METERS:.1f}m, snap={SNAP_GRID:.2f}m, "
        f"overlap>={MIN_OVERLAP:.3f}m, Blender forward=-Y"
    )

    pre_preview_snapshot = semantic_snapshot(objects)
    render_previews(objects)
    post_preview_snapshot = semantic_snapshot(objects)
    if pre_preview_snapshot != post_preview_snapshot:
        raise RuntimeError("Preview setup changed export mesh semantics")
    print("AUDIT preview/export divergence: none")

    write_semantic_audit(module_results, all_connection_results, aggregate_triangles)
    export_fbx(objects)
    print("RESULT ArenaKit generation succeeded")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        sys.exit(1)
