"""Generate the deterministic concrete arena render kit used by MovementLab."""

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
AGGREGATE_TRIANGLE_REVIEW_TARGET = 24000
VIEW_NAMES = ("front", "rear", "left", "right", "top", "three-quarter")
UV_LAYER_NAMES = ("UVMap", "LightmapUV")
UV0_ISLAND_MARGIN = 0.018
UV1_ISLAND_MARGIN = 0.04

MATERIAL_ORDER = ("ArenaPrimary", "ArenaTrim", "ArenaHazard", "ArenaGlow")
MATERIAL_COLORS = {
    "ArenaPrimary": (0.34, 0.35, 0.36, 1.0),
    "ArenaTrim": (0.08, 0.075, 0.065, 1.0),
    "ArenaHazard": (0.84, 0.48, 0.06, 1.0),
    "ArenaGlow": (1.0, 0.58, 0.18, 1.0),
}

# Blender X is width, Y is depth, and Z is height. Exporting forward -Z/up Y
# converts authored Blender -Y into imported Unity local +Z.
MODULE_CONTRACTS = OrderedDict(
    (
        (
            "ArenaGoalRecess",
            {
                "minimum": (-21.0, -10.0, 0.0),
                "maximum": (21.0, 0.0, 12.0),
                "pivot": "opening-plane ground center",
                "slots": MATERIAL_ORDER,
                "triangle_review_target": 20000,
                "opening": {"half_width": 18.0, "height": 11.0, "plane_y": 0.0},
                "recess_depth": 10.0,
            },
        ),
        (
            "ArenaWallSconce",
            {
                "minimum": (-0.6, -0.35, -0.3),
                "maximum": (0.6, 0.0, 0.3),
                "pivot": "wall-contact center",
                "slots": ("ArenaTrim", "ArenaGlow"),
                "triangle_review_target": 4000,
            },
        ),
    )
)

# Every declared joint is checked as a three-axis AABB overlap of at least 5 mm.
CONNECTION_MAP = {
    "ArenaGoalRecess": (
        ("GoalCheekLeft", "GoalShoulderLeft"),
        ("GoalCheekRight", "GoalShoulderRight"),
        ("GoalShoulderLeft", "GoalLintel"),
        ("GoalShoulderRight", "GoalLintel"),
        ("GoalCheekLeft", "GoalBackWall"),
        ("GoalCheekRight", "GoalBackWall"),
        ("GoalFloor", "GoalBackWall"),
        ("GoalBackWall", "GoalRearMarker"),
        ("GoalBackWall", "GoalHazardStripe"),
        ("GoalBackWall", "GoalGlowStrip"),
    ),
    "ArenaWallSconce": (
        ("SconceBackplate", "SconceBracket"),
        ("SconceBracket", "SconceLens"),
    ),
}


def snap(value):
    """Return a module coordinate snapped to the arena grid."""
    return round(value / SNAP_GRID) * SNAP_GRID


def make_material(name, color):
    material = bpy.data.materials.new(name=name)
    material.diffuse_color = color
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Roughness"].default_value = 0.82 if name == "ArenaPrimary" else 0.58
    bsdf.inputs["Metallic"].default_value = 0.2 if name == "ArenaTrim" else 0.0
    if name == "ArenaGlow":
        bsdf.inputs["Emission Color"].default_value = color
        bsdf.inputs["Emission Strength"].default_value = 2.2
    return material


def apply_bevel(obj, width):
    if width <= 0.0:
        return
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    modifier = obj.modifiers.new(name="ConcreteEdgeBevel", type="BEVEL")
    modifier.width = width
    modifier.segments = 1
    modifier.limit_method = "ANGLE"
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)


def add_box(name, center, dimensions, material, parts, bevel=0.02):
    if any(value <= 0.0 for value in dimensions):
        raise RuntimeError(f"Invalid dimensions for {name}: {dimensions}")
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=center)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    obj.scale = Vector(dimensions) * 0.5
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    apply_bevel(obj, min(bevel, min(dimensions) * 0.18))
    obj.data.materials.append(material)
    parts[name] = obj
    return obj


def add_triangular_prism(name, cross_section, y_minimum, y_maximum, material, parts):
    """Create a closed Y-extruded prism from a counter-clockwise X/Z triangle."""
    if len(cross_section) != 3 or y_maximum <= y_minimum:
        raise RuntimeError(f"Invalid triangular prism contract for {name}")
    vertices = tuple((x, y_maximum, z) for x, z in cross_section) + tuple(
        (x, y_minimum, z) for x, z in cross_section
    )
    faces = (
        (0, 2, 1),
        (3, 4, 5),
        (0, 1, 4, 3),
        (1, 2, 5, 4),
        (2, 0, 3, 5),
    )
    mesh = bpy.data.meshes.new(f"{name}Mesh")
    mesh.from_pydata(vertices, (), faces)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)
    parts[name] = obj
    return obj


def add_rear_marker(name, material, parts):
    """Add a rear-pointing wedge whose -Y tip imports as Unity local +Z."""
    vertices = (
        (7.0, -9.4, 5.45),
        (10.0, -9.4, 5.45),
        (8.5, -10.0, 5.45),
        (7.0, -9.4, 6.55),
        (10.0, -9.4, 6.55),
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
    mesh.from_pydata(vertices, (), faces)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)
    parts[name] = obj
    return obj


def create_goal_recess(materials):
    parts = OrderedDict()
    depth = snap(10.0)
    add_box(
        "GoalCheekLeft",
        (-19.5, -depth * 0.5, 5.505),
        (3.0, depth, 11.01),
        materials["ArenaPrimary"],
        parts,
        0.04,
    )
    add_box(
        "GoalCheekRight",
        (19.5, -depth * 0.5, 5.505),
        (3.0, depth, 11.01),
        materials["ArenaPrimary"],
        parts,
        0.04,
    )
    add_triangular_prism(
        "GoalShoulderLeft",
        ((-21.0, 11.0), (-18.0, 11.0), (-18.0, 12.0)),
        -depth,
        0.0,
        materials["ArenaPrimary"],
        parts,
    )
    add_triangular_prism(
        "GoalShoulderRight",
        ((18.0, 11.0), (21.0, 11.0), (18.0, 12.0)),
        -depth,
        0.0,
        materials["ArenaPrimary"],
        parts,
    )
    add_box(
        "GoalLintel",
        (0.0, -depth * 0.5, 11.5),
        (36.02, depth, 1.0),
        materials["ArenaPrimary"],
        parts,
        0.035,
    )
    add_box(
        "GoalBackWall",
        (0.0, -9.8, 5.505),
        (36.02, 0.4, 11.01),
        materials["ArenaPrimary"],
        parts,
        0.025,
    )
    add_box(
        "GoalFloor",
        (0.0, -5.05, 0.125),
        (36.02, 9.9, 0.25),
        materials["ArenaPrimary"],
        parts,
        0.018,
    )
    add_rear_marker("GoalRearMarker", materials["ArenaTrim"], parts)
    add_box(
        "GoalHazardStripe",
        (0.0, -9.58, 0.75),
        (8.0, 0.08, 0.34),
        materials["ArenaHazard"],
        parts,
        0.01,
    )
    add_box(
        "GoalGlowStrip",
        (0.0, -9.58, 10.0),
        (12.0, 0.08, 0.3),
        materials["ArenaGlow"],
        parts,
        0.01,
    )
    return parts


def create_wall_sconce(materials):
    parts = OrderedDict()
    add_box(
        "SconceBackplate",
        (0.0, -0.025, 0.0),
        (1.2, 0.05, 0.6),
        materials["ArenaTrim"],
        parts,
        0.025,
    )
    add_box(
        "SconceBracket",
        (0.0, -0.13, 0.0),
        (0.36, 0.2, 0.2),
        materials["ArenaTrim"],
        parts,
        0.025,
    )
    add_box(
        "SconceLens",
        (0.0, -0.275, 0.0),
        (0.84, 0.15, 0.44),
        materials["ArenaGlow"],
        parts,
        0.035,
    )
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
        first_minimum, first_maximum = part_bounds[first]
        second_minimum, second_maximum = part_bounds[second]
        overlaps = tuple(
            min(first_maximum[index], second_maximum[index])
            - max(first_minimum[index], second_minimum[index])
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


def unwrap_layer(obj, layer_name, island_margin):
    layer = obj.data.uv_layers.new(name=layer_name)
    obj.data.uv_layers.active = layer
    obj.data.uv_layers.active_index = len(obj.data.uv_layers) - 1
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(
        angle_limit=math.radians(66.0),
        island_margin=island_margin,
        area_weight=0.0,
        correct_aspect=True,
        scale_to_bounds=False,
    )
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)


def add_uv_and_vertex_color(obj):
    while obj.data.uv_layers:
        obj.data.uv_layers.remove(obj.data.uv_layers[0])
    unwrap_layer(obj, "UVMap", UV0_ISLAND_MARGIN)
    unwrap_layer(obj, "LightmapUV", UV1_ISLAND_MARGIN)
    color_attribute = obj.data.color_attributes.new(
        name="ArenaVariation", type="BYTE_COLOR", domain="CORNER"
    )
    minimum, maximum = world_bounds(obj)
    height = max(maximum.z - minimum.z, 1e-6)
    for loop in obj.data.loops:
        vertex = obj.data.vertices[loop.vertex_index]
        height_factor = (vertex.co.z - minimum.z) / height
        variation = 0.62 + 0.25 * height_factor
        color_attribute.data[loop.index].color = (variation, variation, variation, 1.0)


def join_module(module_name, parts):
    contract = MODULE_CONTRACTS[module_name]
    part_bounds = {name: world_bounds(obj) for name, obj in parts.items()}
    connection_results = audit_connections(module_name, part_bounds)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts.values():
        obj.select_set(True)
    bpy.context.view_layer.objects.active = next(iter(parts.values()))
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
    return result, len(parts), connection_results


def connected_components(bm):
    remaining = set(bm.verts)
    components = []
    while remaining:
        seed = remaining.pop()
        vertices = {seed}
        stack = [seed]
        while stack:
            vertex = stack.pop()
            for edge in vertex.link_edges:
                other = edge.other_vert(vertex)
                if other in remaining:
                    remaining.remove(other)
                    vertices.add(other)
                    stack.append(other)
        faces = {face for vertex in vertices for face in vertex.link_faces}
        components.append((vertices, faces))
    return components


def audit_component_winding(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    results = []
    for index, (vertices, faces) in enumerate(connected_components(bm)):
        reference = sum((vertex.co for vertex in vertices), Vector()) / len(vertices)
        signed_volume = 0.0
        for face in faces:
            relative = [loop.vert.co - reference for loop in face.loops]
            anchor = relative[0]
            for triangle_index in range(1, len(relative) - 1):
                signed_volume += (
                    anchor.dot(relative[triangle_index].cross(relative[triangle_index + 1]))
                    / 6.0
                )
        if not math.isfinite(signed_volume) or signed_volume <= 1e-9:
            raise RuntimeError(
                f"Normals/winding failed on {obj.name} component {index}: {signed_volume}"
            )
        results.append(round(signed_volume, 6))
        print(
            f"AUDIT component {obj.name}[{index}]: vertices={len(vertices)}, "
            f"faces={len(faces)}, signed_volume={signed_volume:.6f}, winding=outward"
        )
    bm.free()
    return results


def orient_2d(first, second, third):
    return (second.x - first.x) * (third.y - first.y) - (
        second.y - first.y
    ) * (third.x - first.x)


def point_in_triangle_strict(point, triangle, epsilon=1e-9):
    signs = [
        orient_2d(triangle[index], triangle[(index + 1) % 3], point)
        for index in range(3)
    ]
    return all(value > epsilon for value in signs) or all(value < -epsilon for value in signs)


def segments_cross_strict(first_start, first_end, second_start, second_end, epsilon=1e-9):
    first_a = orient_2d(first_start, first_end, second_start)
    first_b = orient_2d(first_start, first_end, second_end)
    second_a = orient_2d(second_start, second_end, first_start)
    second_b = orient_2d(second_start, second_end, first_end)
    return first_a * first_b < -epsilon and second_a * second_b < -epsilon


def triangles_overlap_positive(first, second):
    first_centroid = (first[0] + first[1] + first[2]) / 3.0
    second_centroid = (second[0] + second[1] + second[2]) / 3.0
    if point_in_triangle_strict(first_centroid, second):
        return True
    if point_in_triangle_strict(second_centroid, first):
        return True
    if any(point_in_triangle_strict(point, second) for point in first):
        return True
    if any(point_in_triangle_strict(point, first) for point in second):
        return True
    return any(
        segments_cross_strict(
            first[first_index],
            first[(first_index + 1) % 3],
            second[second_index],
            second[(second_index + 1) % 3],
        )
        for first_index in range(3)
        for second_index in range(3)
    )


def audit_uv_layer(obj, layer_name):
    layer = obj.data.uv_layers[layer_name]
    obj.data.calc_loop_triangles()
    triangles = []
    for loop_triangle in obj.data.loop_triangles:
        uv_triangle = tuple(Vector(layer.data[index].uv) for index in loop_triangle.loops)
        if any(not math.isfinite(component) for point in uv_triangle for component in point):
            raise RuntimeError(f"Non-finite {layer_name} coordinate on {obj.name}")
        if any(component < -1e-6 or component > 1.0 + 1e-6 for point in uv_triangle for component in point):
            raise RuntimeError(f"Out-of-range {layer_name} coordinate on {obj.name}")
        if abs(orient_2d(*uv_triangle)) <= 1e-12:
            raise RuntimeError(f"Zero-area {layer_name} triangle on {obj.name}")
        triangles.append((loop_triangle.polygon_index, uv_triangle))
    for first_index, (first_polygon, first) in enumerate(triangles):
        for second_polygon, second in triangles[first_index + 1 :]:
            if first_polygon == second_polygon:
                continue
            if triangles_overlap_positive(first, second):
                raise RuntimeError(
                    f"Positive-area {layer_name} overlap on {obj.name}: "
                    f"polygons {first_polygon}/{second_polygon}"
                )
    print(f"AUDIT UV {obj.name}/{layer_name}: triangles={len(triangles)}, nonoverlap=yes")
    return {"layer": layer_name, "triangles": len(triangles), "nonoverlap": True}


def audit_goal_opening(part_bounds):
    contract = MODULE_CONTRACTS["ArenaGoalRecess"]
    half_width = contract["opening"]["half_width"]
    height = contract["opening"]["height"]
    plane_y = contract["opening"]["plane_y"]
    opening_width = half_width * 2.0
    blockers = []
    for name, (minimum, maximum) in part_bounds.items():
        touches_plane = minimum.y - 1e-6 <= plane_y <= maximum.y + 1e-6
        overlaps_width = minimum.x < half_width - 1e-6 and maximum.x > -half_width + 1e-6
        overlaps_height = minimum.z < height - 1e-6 and maximum.z > 1e-6
        if touches_plane and overlaps_width and overlaps_height:
            blockers.append(name)
    if blockers:
        raise RuntimeError(
            f"Goal opening {opening_width:g}x{height:g}m obstructed "
            f"at Y={plane_y:g} by {blockers}"
        )
    marker_minimum, marker_maximum = part_bounds["GoalRearMarker"]
    if abs(marker_minimum.y + 10.0) > 1e-6 or abs(marker_maximum.y + 9.4) > 1e-6:
        raise RuntimeError(
            f"Rear marker direction failed: {tuple(marker_minimum)} to {tuple(marker_maximum)}"
        )
    if abs(contract["recess_depth"] - (plane_y - marker_minimum.y)) > 1e-6:
        raise RuntimeError("Goal recess depth contract failed")
    print(
        f"AUDIT goal opening: {opening_width:.6f}x{height:.6f} m clear "
        f"at Y={plane_y:g}"
    )
    print("AUDIT rear marker: asymmetric tip Blender Y=-10.000000 -> Unity local +Z")


def audit_module(obj, expected_components):
    contract = MODULE_CONTRACTS[obj.name]
    if obj.type != "MESH" or obj.data.name != f"{obj.name}Mesh":
        raise RuntimeError(f"Stable renderer/mesh name failed on {obj.name}")
    if obj.location.length > 1e-6:
        raise RuntimeError(f"Pivot failed on {obj.name}: {tuple(obj.location)}")
    if any(abs(value) > 1e-6 for value in obj.rotation_euler):
        raise RuntimeError(f"Unapplied rotation on {obj.name}")
    if any(abs(value - 1.0) > 1e-6 for value in obj.scale):
        raise RuntimeError(f"Unapplied scale on {obj.name}")
    if tuple(slot.material.name for slot in obj.material_slots) != contract["slots"]:
        raise RuntimeError(f"Material slots failed on {obj.name}")
    used_slots = {polygon.material_index for polygon in obj.data.polygons}
    if used_slots != set(range(len(contract["slots"]))):
        raise RuntimeError(f"Unused material slot on {obj.name}: {used_slots}")
    if tuple(layer.name for layer in obj.data.uv_layers) != UV_LAYER_NAMES:
        raise RuntimeError(f"UV layer contract failed on {obj.name}")
    if any(polygon.use_smooth for polygon in obj.data.polygons):
        raise RuntimeError(f"Smooth face found on faceted module {obj.name}")
    if any(not math.isfinite(value) for vertex in obj.data.vertices for value in vertex.co):
        raise RuntimeError(f"Non-finite vertex on {obj.name}")

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    zero_edges = sum(1 for edge in bm.edges if edge.calc_length() <= 1e-8)
    zero_faces = sum(1 for face in bm.faces if face.calc_area() <= 1e-10)
    non_manifold = sum(1 for edge in bm.edges if not edge.is_manifold)
    loose_vertices = sum(1 for vertex in bm.verts if not vertex.link_edges)
    component_count = len(connected_components(bm))
    bm.free()
    if zero_edges or zero_faces or non_manifold or loose_vertices:
        raise RuntimeError(
            f"Topology failed on {obj.name}: zero_edges={zero_edges}, zero_faces={zero_faces}, "
            f"non_manifold={non_manifold}, loose_vertices={loose_vertices}"
        )
    if component_count != expected_components:
        raise RuntimeError(
            f"Component count failed on {obj.name}: {component_count} != {expected_components}"
        )
    component_volumes = audit_component_winding(obj)
    uv_audits = [audit_uv_layer(obj, layer_name) for layer_name in UV_LAYER_NAMES]

    minimum, maximum = world_bounds(obj)
    expected_minimum = Vector(contract["minimum"])
    expected_maximum = Vector(contract["maximum"])
    if any(abs(minimum[index] - expected_minimum[index]) > BOUNDS_TOLERANCE for index in range(3)):
        raise RuntimeError(f"Minimum bounds failed on {obj.name}: {tuple(minimum)}")
    if any(abs(maximum[index] - expected_maximum[index]) > BOUNDS_TOLERANCE for index in range(3)):
        raise RuntimeError(f"Maximum bounds failed on {obj.name}: {tuple(maximum)}")
    obj.data.calc_loop_triangles()
    triangles = len(obj.data.loop_triangles)
    dimensions = maximum - minimum
    print(
        f"AUDIT module {obj.name}: bounds_min={tuple(round(v, 6) for v in minimum)}, "
        f"bounds_max={tuple(round(v, 6) for v in maximum)}, "
        f"dimensions_WxHxD={dimensions.x:.6f}x{dimensions.z:.6f}x{dimensions.y:.6f} m"
    )
    print(
        f"AUDIT mesh {obj.name}: vertices={len(obj.data.vertices)}, "
        f"triangles={triangles}/{contract['triangle_review_target']} report-only, "
        f"components={component_count}, manifold=yes, winding=outward, UV0+UV1=nonoverlap"
    )
    return {
        "name": obj.name,
        "mesh": obj.data.name,
        "minimum": [round(value, 6) for value in minimum],
        "maximum": [round(value, 6) for value in maximum],
        "dimensions_w_h_d": [round(dimensions.x, 6), round(dimensions.z, 6), round(dimensions.y, 6)],
        "vertices": len(obj.data.vertices),
        "triangles": triangles,
        "triangle_review_target": contract["triangle_review_target"],
        "components": component_count,
        "component_signed_volumes": component_volumes,
        "finite_vertices": True,
        "topology": {
            "zero_length_edges": zero_edges,
            "zero_area_faces": zero_faces,
            "non_manifold_edges": non_manifold,
            "loose_vertices": loose_vertices,
        },
        "transform": {
            "location": [round(value, 6) for value in obj.location],
            "rotation_euler": [round(value, 6) for value in obj.rotation_euler],
            "scale": [round(value, 6) for value in obj.scale],
        },
        "slots": list(contract["slots"]),
        "uv_layers": list(UV_LAYER_NAMES),
        "uv_audits": uv_audits,
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
    scene.eevee.taa_render_samples = 32
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("ArenaPreviewWorld")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.035, 0.038, 0.042, 1.0)
    background.inputs["Strength"].default_value = 0.4
    scene.view_settings.look = "AgX - Medium High Contrast"

    camera_data = bpy.data.cameras.new("ArenaPreviewCamera")
    camera = bpy.data.objects.new("ArenaPreviewCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.clip_start = 0.01
    camera.data.clip_end = 1000.0
    scene.camera = camera

    lights = []
    for name, energy, size in (
        ("ArenaPreviewKey", 2400.0, 8.0),
        ("ArenaPreviewFill", 1300.0, 10.0),
    ):
        light_data = bpy.data.lights.new(name, type="AREA")
        light_data.energy = energy
        light_data.shape = "DISK"
        light_data.size = size
        light = bpy.data.objects.new(name, light_data)
        bpy.context.collection.objects.link(light)
        lights.append(light)

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
    if tuple(view_vectors) != VIEW_NAMES:
        raise RuntimeError("Preview view contract failed")

    os.makedirs(PREVIEW_DIR, exist_ok=True)
    original_hidden = {obj.name: obj.hide_render for obj in objects}
    for target in objects:
        for obj in objects:
            obj.hide_render = obj != target
        minimum, maximum = world_bounds(target)
        center = (minimum + maximum) * 0.5
        dimensions = maximum - minimum
        max_dimension = max(dimensions)
        light_scale = max(1.0, max_dimension / 8.0)
        lights[0].data.energy = 2400.0 * light_scale
        lights[1].data.energy = 1300.0 * light_scale
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
            projected_span = max_dimension
            if view_name in ("front", "rear"):
                projected_span = max(dimensions.x, dimensions.z)
            elif view_name in ("left", "right"):
                projected_span = max(dimensions.y, dimensions.z)
            elif view_name == "top":
                projected_span = max(dimensions.x, dimensions.y)
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
        "aggregate_triangle_review_target": AGGREGATE_TRIANGLE_REVIEW_TARGET,
        "aggregate_triangles": aggregate_triangles,
        "animation": "disabled",
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
    original_names = tuple(obj.name for obj in objects)
    try:
        # Unity derives imported mesh subasset names from exported Blender object names.
        for obj in objects:
            obj.name = obj.data.name
        bpy.ops.export_scene.fbx(
            filepath=OUTPUT_PATH,
            use_selection=True,
            object_types={"MESH"},
            axis_forward="-Z",
            axis_up="Y",
            bake_space_transform=True,
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
    finally:
        for obj, original_name in zip(objects, original_names):
            obj.name = original_name


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = UNIT_METERS
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)

    materials = {name: make_material(name, MATERIAL_COLORS[name]) for name in MATERIAL_ORDER}
    creators = OrderedDict(
        (
            ("ArenaGoalRecess", create_goal_recess),
            ("ArenaWallSconce", create_wall_sconce),
        )
    )
    objects = []
    component_counts = {}
    all_connection_results = {}
    for module_name, creator in creators.items():
        parts = creator(materials)
        part_bounds = {name: world_bounds(obj) for name, obj in parts.items()}
        if module_name == "ArenaGoalRecess":
            audit_goal_opening(part_bounds)
        obj, component_count, connection_results = join_module(module_name, parts)
        objects.append(obj)
        component_counts[module_name] = component_count
        all_connection_results[module_name] = connection_results

    if tuple(obj.name for obj in objects) != tuple(MODULE_CONTRACTS):
        raise RuntimeError(f"Stable renderer names failed: {tuple(obj.name for obj in objects)}")
    if tuple(obj.data.name for obj in objects) != tuple(
        f"{name}Mesh" for name in MODULE_CONTRACTS
    ):
        raise RuntimeError(f"Stable mesh names failed: {tuple(obj.data.name for obj in objects)}")
    if bpy.data.actions or any(obj.type == "ARMATURE" for obj in bpy.data.objects):
        raise RuntimeError("Static ArenaKit unexpectedly contains animation data")

    module_results = [audit_module(obj, component_counts[obj.name]) for obj in objects]
    aggregate_triangles = sum(result["triangles"] for result in module_results)
    print(
        f"AUDIT aggregate: modules=2, triangles={aggregate_triangles}/"
        f"{AGGREGATE_TRIANGLE_REVIEW_TARGET} report-only"
    )
    print(
        f"AUDIT contract: units={UNIT_METERS:.1f}m, snap={SNAP_GRID:.2f}m, "
        f"overlap>={MIN_OVERLAP:.3f}m, Blender -Y -> Unity local +Z"
    )

    pre_preview_snapshot = semantic_snapshot(objects)
    render_previews(objects)
    if pre_preview_snapshot != semantic_snapshot(objects):
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
