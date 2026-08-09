"""Generate the PBR low-poly rocket visual used by Unity.

Asset contract: origin at world zero, applied transforms, Blender -Y nose,
Unity +Z forward after FBX axis conversion, and dynamic meshes with UV0 only.
"""

import json
import math
import os
from collections import Counter

import bpy
from mathutils import Vector


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUTPUT_PATH = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models", "LowPolyRocket.fbx")
TEXTURE_DIR = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Textures")
PREVIEW_DIR = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "LowPolyRocket")
SEMANTIC_AUDIT_PATH = os.path.join(PREVIEW_DIR, "semantic_snapshot.json")

SEGMENTS = 12
ATLAS_SIZE = 1024.0
MIN_OVERLAP = 0.005
TRIANGLE_RANGE = (120, 300)
EPSILON = 1.0e-8

# Accepted atlas inner pixel bounds. Sampling adds another half-pixel inset.
ATLAS_INNER_PIXELS = {
    "body": (32, 32, 480, 992),
    "hot": (544, 32, 992, 480),
    "fins": (544, 544, 736, 992),
    "nozzle": (800, 544, 992, 992),
}

CONNECTIONS = (
    ("Body", "Nose", "y"),
    ("Body", "FinLeft", "radial"),
    ("Body", "FinRight", "radial"),
    ("Body", "FinTop", "radial"),
    ("Body", "FinBottom", "radial"),
    ("Body", "NozzleShell", "y"),
    ("NozzleShell", "Throat", "y/radial"),
    ("Throat", "Exhaust", "y"),
)


def atlas_bounds(region):
    left, bottom, right, top = ATLAS_INNER_PIXELS[region]
    return (
        (left + 0.5) / ATLAS_SIZE,
        (bottom + 0.5) / ATLAS_SIZE,
        (right - 0.5) / ATLAS_SIZE,
        (top - 0.5) / ATLAS_SIZE,
    )


def triangle_uvs(region, index):
    u0, v0, u1, v1 = atlas_bounds(region)
    if index % 2 == 0:
        return ((u0, v0), (u1, v0), (u1, v1))
    return ((u0, v0), (u1, v1), (u0, v1))


def signed_volume(vertices, faces):
    volume = 0.0
    for a, b, c in faces:
        va, vb, vc = Vector(vertices[a]), Vector(vertices[b]), Vector(vertices[c])
        volume += va.dot(vb.cross(vc)) / 6.0
    return volume


def orient_outward(part):
    volume = signed_volume(part["vertices"], part["faces"])
    if volume < 0.0:
        part["faces"] = [(a, c, b) for a, b, c in part["faces"]]
        part["uvs"] = [(uv[0], uv[2], uv[1]) for uv in part["uvs"]]
        volume = -volume
    part["volume"] = volume
    return part


def closed_frustum(name, region, y0, y1, radius0, radius1):
    vertices = []
    for y, radius in ((y0, radius0), (y1, radius1)):
        for index in range(SEGMENTS):
            angle = math.tau * index / SEGMENTS
            vertices.append((radius * math.cos(angle), y, radius * math.sin(angle)))
    vertices.extend(((0.0, y0, 0.0), (0.0, y1, 0.0)))
    lower_center, upper_center = SEGMENTS * 2, SEGMENTS * 2 + 1
    faces = []
    uvs = []
    u0, v0, u1, v1 = atlas_bounds(region)
    for index in range(SEGMENTS):
        following = (index + 1) % SEGMENTS
        ua = u0 + (u1 - u0) * index / SEGMENTS
        ub = u0 + (u1 - u0) * (index + 1) / SEGMENTS
        faces.extend(
            (
                (index, SEGMENTS + index, SEGMENTS + following),
                (index, SEGMENTS + following, following),
                (lower_center, index, following),
                (upper_center, SEGMENTS + following, SEGMENTS + index),
            )
        )
        uvs.extend(
            (
                ((ua, v0), (ua, v1), (ub, v1)),
                ((ua, v0), (ub, v1), (ub, v0)),
                triangle_uvs(region, index),
                triangle_uvs(region, index + 1),
            )
        )
    return orient_outward(
        {
            "name": name,
            "region": region,
            "vertices": vertices,
            "faces": faces,
            "uvs": uvs,
            "parameters": {"y0": y0, "y1": y1, "radius0": radius0, "radius1": radius1},
        }
    )


def pointed_cone(name, region, base_y, tip_y, radius):
    vertices = []
    for index in range(SEGMENTS):
        angle = math.tau * index / SEGMENTS
        vertices.append((radius * math.cos(angle), base_y, radius * math.sin(angle)))
    vertices.extend(((0.0, tip_y, 0.0), (0.0, base_y, 0.0)))
    tip, center = SEGMENTS, SEGMENTS + 1
    faces = []
    uvs = []
    u0, v0, u1, v1 = atlas_bounds(region)
    middle_u = (u0 + u1) * 0.5
    for index in range(SEGMENTS):
        following = (index + 1) % SEGMENTS
        ua = u0 + (u1 - u0) * index / SEGMENTS
        ub = u0 + (u1 - u0) * (index + 1) / SEGMENTS
        faces.extend(((tip, index, following), (center, following, index)))
        uvs.extend(
            (
                ((middle_u, v0), (ua, v1), (ub, v1)),
                triangle_uvs(region, index),
            )
        )
    return orient_outward(
        {
            "name": name,
            "region": region,
            "vertices": vertices,
            "faces": faces,
            "uvs": uvs,
            "parameters": {"base_y": base_y, "tip_y": tip_y, "radius": radius},
        }
    )


def annular_nozzle(name, region, y0, y1, outer0, outer1, inner0, inner1):
    vertices = []
    rings = ((y0, outer0), (y1, outer1), (y0, inner0), (y1, inner1))
    for y, radius in rings:
        for index in range(SEGMENTS):
            angle = math.tau * index / SEGMENTS
            vertices.append((radius * math.cos(angle), y, radius * math.sin(angle)))
    faces = []
    uvs = []
    for index in range(SEGMENTS):
        following = (index + 1) % SEGMENTS
        outer_low, outer_high = index, SEGMENTS + index
        outer_low_next, outer_high_next = following, SEGMENTS + following
        inner_low, inner_high = SEGMENTS * 2 + index, SEGMENTS * 3 + index
        inner_low_next, inner_high_next = SEGMENTS * 2 + following, SEGMENTS * 3 + following
        faces.extend(
            (
                (outer_low, outer_high, outer_high_next),
                (outer_low, outer_high_next, outer_low_next),
                (inner_low, inner_high_next, inner_high),
                (inner_low, inner_low_next, inner_high_next),
                (outer_low, outer_low_next, inner_low_next),
                (outer_low, inner_low_next, inner_low),
                (outer_high, inner_high_next, outer_high_next),
                (outer_high, inner_high, inner_high_next),
            )
        )
        uvs.extend(triangle_uvs(region, index + offset) for offset in range(8))
    return orient_outward(
        {
            "name": name,
            "region": region,
            "vertices": vertices,
            "faces": faces,
            "uvs": uvs,
            "parameters": {
                "y0": y0,
                "y1": y1,
                "outer0": outer0,
                "outer1": outer1,
                "inner0": inner0,
                "inner1": inner1,
            },
        }
    )


def wedge_fin(name, radial_angle):
    radial = Vector((math.cos(radial_angle), 0.0, math.sin(radial_angle)))
    tangent = Vector((-math.sin(radial_angle), 0.0, math.cos(radial_angle)))
    profile = ((0.315, 0.15), (0.315, 0.84), (0.72, 0.72))
    vertices = []
    for tangent_offset in (-0.055, 0.055):
        for radius, y in profile:
            point = radial * radius + tangent * tangent_offset
            vertices.append((point.x, y, point.z))
    faces = [
        (0, 2, 1),
        (3, 4, 5),
        (0, 3, 5),
        (0, 5, 2),
        (0, 1, 4),
        (0, 4, 3),
        (1, 2, 5),
        (1, 5, 4),
    ]
    uvs = [triangle_uvs("fins", index) for index in range(len(faces))]
    return orient_outward(
        {
            "name": name,
            "region": "fins",
            "vertices": vertices,
            "faces": faces,
            "uvs": uvs,
            "parameters": {"radial_inner": profile[0][0], "radial_outer": profile[2][0]},
        }
    )


def combine_parts(object_name, parts, material):
    vertices = []
    faces = []
    face_uvs = []
    face_components = []
    for part in parts:
        offset = len(vertices)
        vertices.extend(part["vertices"])
        faces.extend(tuple(index + offset for index in face) for face in part["faces"])
        face_uvs.extend(part["uvs"])
        face_components.extend((part["name"], part["region"]) for _ in part["faces"])

    mesh = bpy.data.meshes.new(object_name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update(calc_edges=True)
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for polygon, uvs in zip(mesh.polygons, face_uvs):
        polygon.material_index = 0
        polygon.use_smooth = False
        for loop_index, uv in zip(polygon.loop_indices, uvs):
            uv_layer.data[loop_index].uv = uv

    obj = bpy.data.objects.new(object_name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)
    obj["component_regions"] = json.dumps(face_components)
    return obj


def image_texture(nodes, filename, colorspace):
    path = os.path.join(TEXTURE_DIR, filename)
    if not os.path.isfile(path):
        raise RuntimeError(f"Missing accepted rocket texture: {path}")
    node = nodes.new("ShaderNodeTexImage")
    node.image = bpy.data.images.load(path, check_existing=True)
    node.image.colorspace_settings.name = colorspace
    node.interpolation = "Linear"
    node.extension = "CLIP"
    return node


def create_pbr_material(name, emission_strength):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    base = image_texture(nodes, "RetroRocket.png", "sRGB")
    normal = image_texture(nodes, "RetroRocket_Normal.png", "Non-Color")
    metallic = image_texture(nodes, "RetroRocket_MetallicSmoothness.png", "Non-Color")
    occlusion = image_texture(nodes, "RetroRocket_Occlusion.png", "Non-Color")
    emission = image_texture(nodes, "RetroRocket_Emission.png", "sRGB")
    separate_metallic = nodes.new("ShaderNodeSeparateColor")
    separate_ao = nodes.new("ShaderNodeSeparateColor")
    invert_smoothness = nodes.new("ShaderNodeMath")
    invert_smoothness.operation = "SUBTRACT"
    invert_smoothness.inputs[0].default_value = 1.0
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.inputs["Strength"].default_value = 0.8
    multiply_ao = nodes.new("ShaderNodeMixRGB")
    multiply_ao.blend_type = "MULTIPLY"
    multiply_ao.inputs[0].default_value = 1.0
    links.new(base.outputs["Color"], multiply_ao.inputs[1])
    links.new(occlusion.outputs["Color"], separate_ao.inputs["Color"])
    links.new(separate_ao.outputs["Green"], multiply_ao.inputs[2])
    links.new(multiply_ao.outputs["Color"], shader.inputs["Base Color"])
    links.new(normal.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])
    links.new(metallic.outputs["Color"], separate_metallic.inputs["Color"])
    links.new(separate_metallic.outputs["Red"], shader.inputs["Metallic"])
    links.new(metallic.outputs["Alpha"], invert_smoothness.inputs[1])
    links.new(invert_smoothness.outputs[0], shader.inputs["Roughness"])
    links.new(emission.outputs["Color"], shader.inputs["Emission Color"])
    shader.inputs["Emission Strength"].default_value = emission_strength
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    return material


def edge_key(a, b):
    return tuple(sorted((a, b)))


def audit_part(part):
    vertices = part["vertices"]
    faces = part["faces"]
    edge_counts = Counter()
    minimum_area = float("inf")
    minimum_uv_area = float("inf")
    minimum_edge = float("inf")
    for face, face_uvs in zip(faces, part["uvs"]):
        a, b, c = face
        va, vb, vc = (Vector(vertices[index]) for index in face)
        area = (vb - va).cross(vc - va).length * 0.5
        minimum_area = min(minimum_area, area)
        if area <= EPSILON:
            raise RuntimeError(f"{part['name']} has zero-area triangle")
        for start, end in ((a, b), (b, c), (c, a)):
            length = (Vector(vertices[start]) - Vector(vertices[end])).length
            minimum_edge = min(minimum_edge, length)
            if length <= EPSILON:
                raise RuntimeError(f"{part['name']} has zero-length edge")
            edge_counts[edge_key(start, end)] += 1
        uv0, uv1, uv2 = (Vector(uv) for uv in face_uvs)
        uv_area = abs((uv1.x - uv0.x) * (uv2.y - uv0.y) - (uv1.y - uv0.y) * (uv2.x - uv0.x)) * 0.5
        minimum_uv_area = min(minimum_uv_area, uv_area)
        if uv_area <= EPSILON:
            raise RuntimeError(f"{part['name']} has zero-area UV triangle")
    non_manifold = [edge for edge, count in edge_counts.items() if count != 2]
    if non_manifold:
        raise RuntimeError(f"{part['name']} has {len(non_manifold)} non-manifold edges")
    if part["volume"] <= EPSILON:
        raise RuntimeError(f"{part['name']} is not positive-volume")
    return {
        "name": part["name"],
        "region": part["region"],
        "vertices": len(vertices),
        "triangles": len(faces),
        "volume": round(part["volume"], 8),
        "minimum_triangle_area": round(minimum_area, 8),
        "minimum_uv_area": round(minimum_uv_area, 10),
        "minimum_edge_length": round(minimum_edge, 8),
        "manifold": True,
    }


def object_bounds(objects):
    points = [obj.matrix_world @ vertex.co for obj in objects for vertex in obj.data.vertices]
    minimum = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    return minimum, maximum


def audit_connections(parts):
    by_name = {part["name"]: part for part in parts}

    def y_interval(part):
        values = [vertex[1] for vertex in part["vertices"]]
        return min(values), max(values)

    def y_overlap(first, second):
        first_min, first_max = y_interval(first)
        second_min, second_max = y_interval(second)
        return min(first_max, second_max) - max(first_min, second_min)

    body = by_name["Body"]
    body_radius = max(body["parameters"]["radius0"], body["parameters"]["radius1"])
    overlaps = {
        ("Body", "Nose"): y_overlap(body, by_name["Nose"]),
        ("Body", "NozzleShell"): y_overlap(body, by_name["NozzleShell"]),
        ("Throat", "Exhaust"): y_overlap(by_name["Throat"], by_name["Exhaust"]),
    }
    for fin_name in ("FinLeft", "FinRight", "FinTop", "FinBottom"):
        overlaps[("Body", fin_name)] = body_radius - by_name[fin_name]["parameters"]["radial_inner"]

    nozzle = by_name["NozzleShell"]
    throat = by_name["Throat"]
    nozzle_rear_y = nozzle["parameters"]["y1"]
    throat_fraction = (nozzle_rear_y - throat["parameters"]["y0"]) / (
        throat["parameters"]["y1"] - throat["parameters"]["y0"]
    )
    throat_radius_at_nozzle_rear = throat["parameters"]["radius0"] + throat_fraction * (
        throat["parameters"]["radius1"] - throat["parameters"]["radius0"]
    )
    overlaps[("NozzleShell", "Throat")] = throat_radius_at_nozzle_rear - nozzle["parameters"]["inner1"]

    results = []
    for start, end, axis in CONNECTIONS:
        overlap = overlaps[(start, end)]
        if overlap + EPSILON < MIN_OVERLAP:
            raise RuntimeError(f"{start}/{end} overlap {overlap} below {MIN_OVERLAP}")
        results.append(
            {"from": start, "to": end, "axis": axis, "overlap": round(overlap, 8), "minimum": MIN_OVERLAP}
        )
    return results


def audit_objects(objects, parts):
    expected = ("RocketSurface", "RocketHot")
    if tuple(obj.name for obj in objects) != expected:
        raise RuntimeError(f"Rocket object names invalid: {[obj.name for obj in objects]}")
    triangle_count = 0
    for obj in objects:
        if obj.data.name != obj.name:
            raise RuntimeError(f"Mesh name must match object name: {obj.name}/{obj.data.name}")
        if len(obj.material_slots) != 1 or obj.material_slots[0].material.name != obj.name:
            raise RuntimeError(f"{obj.name} must have one same-named material slot")
        if len(obj.data.uv_layers) != 1 or obj.data.uv_layers[0].name != "UVMap":
            raise RuntimeError(f"{obj.name} must have UV0 only")
        if obj.location.length > EPSILON or any(abs(value) > EPSILON for value in obj.rotation_euler):
            raise RuntimeError(f"{obj.name} location/rotation invalid")
        if any(abs(value - 1.0) > EPSILON for value in obj.scale):
            raise RuntimeError(f"{obj.name} scale is not applied")
        obj.data.calc_loop_triangles()
        triangle_count += len(obj.data.loop_triangles)
        if len(obj.data.materials) != 1:
            raise RuntimeError(f"{obj.name} has unused material slots")
        if any(polygon.material_index != 0 for polygon in obj.data.polygons):
            raise RuntimeError(f"{obj.name} contains an unexpected submesh")
        for vertex in obj.data.vertices:
            if not all(math.isfinite(value) for value in vertex.co):
                raise RuntimeError(f"{obj.name} has non-finite vertex")
        for uv in obj.data.uv_layers[0].data:
            if not all(math.isfinite(value) for value in uv.uv):
                raise RuntimeError(f"{obj.name} has non-finite UV")

    if not TRIANGLE_RANGE[0] <= triangle_count <= TRIANGLE_RANGE[1]:
        raise RuntimeError(f"Rocket triangle count {triangle_count} outside {TRIANGLE_RANGE}")
    minimum, maximum = object_bounds(objects)
    expected_min = Vector((-0.72, -1.50, -0.72))
    expected_max = Vector((0.72, 1.25, 0.72))
    if (minimum - expected_min).length > 1.0e-5 or (maximum - expected_max).length > 1.0e-5:
        raise RuntimeError(f"Rocket bounds invalid: {tuple(minimum)}..{tuple(maximum)}")
    if -minimum.y <= maximum.y:
        raise RuntimeError("Blender -Y nose is not dominant forward extent")
    if maximum.y > 1.250001:
        raise RuntimeError("Exhaust extends beyond +1.25")

    component_results = [audit_part(part) for part in parts]
    connection_results = audit_connections(parts)

    # Face-level atlas containment uses component routing after the half-pixel inset.
    atlas_results = {}
    for part in parts:
        u0, v0, u1, v1 = atlas_bounds(part["region"])
        coordinates = [coordinate for triangle in part["uvs"] for coordinate in triangle]
        if any(not (u0 - EPSILON <= u <= u1 + EPSILON and v0 - EPSILON <= v <= v1 + EPSILON) for u, v in coordinates):
            raise RuntimeError(f"{part['name']} UV escaped {part['region']} safe rectangle")
        atlas_results[part["name"]] = {
            "region": part["region"],
            "safe_uv": [round(u0, 8), round(v0, 8), round(u1, 8), round(v1, 8)],
            "contained": True,
        }
    return {
        "objects": [
            {
                "name": obj.name,
                "mesh": obj.data.name,
                "vertices": len(obj.data.vertices),
                "triangles": len(obj.data.loop_triangles),
                "material_slots": [slot.material.name for slot in obj.material_slots],
                "uv_layers": [layer.name for layer in obj.data.uv_layers],
                "location": list(obj.location),
                "rotation_euler": list(obj.rotation_euler),
                "scale": list(obj.scale),
            }
            for obj in objects
        ],
        "parts": component_results,
        "atlas": atlas_results,
        "connections": connection_results,
        "bounds": {"minimum": list(minimum), "maximum": list(maximum)},
        "triangles": triangle_count,
        "triangle_range": list(TRIANGLE_RANGE),
        "forward": {"blender": "-Y", "fbx_axis_forward": "-Z", "unity_local": "+Z"},
        "origin": [0.0, 0.0, 0.0],
        "root_scale": [1.0, 1.0, 1.0],
        "uv1_required": False,
    }


def point_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def render_previews(objects):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False
    scene.render.image_settings.color_depth = "8"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.world = bpy.data.worlds.new("RocketPreviewWorld")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.025, 0.035, 0.055, 1.0)
    background.inputs["Strength"].default_value = 0.35

    camera_data = bpy.data.cameras.new("RocketPreviewCamera")
    camera = bpy.data.objects.new("RocketPreviewCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.clip_start = 0.01
    camera.data.clip_end = 100.0
    scene.camera = camera

    light_specs = (
        ("RocketPreviewKey", (-3.0, -4.0, 4.0), 850.0, 3.0, (1.0, 0.72, 0.48)),
        ("RocketPreviewFill", (4.0, -0.5, 2.0), 650.0, 3.5, (0.44, 0.68, 1.0)),
        ("RocketPreviewRim", (0.0, 4.0, 3.0), 900.0, 2.5, (1.0, 0.32, 0.12)),
    )
    for name, location, energy, size, color in light_specs:
        data = bpy.data.lights.new(name, type="AREA")
        data.energy = energy
        data.shape = "DISK"
        data.size = size
        data.color = color
        light = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(light)
        light.location = location
        point_at(light, (0.0, 0.0, 0.0))

    views = (
        ("front", Vector((0.0, -4.2, 0.15)), 1.85),
        ("rear", Vector((0.0, 4.2, 0.15)), 1.85),
        ("left", Vector((-4.2, 0.0, 0.15)), 3.25),
        ("right", Vector((4.2, 0.0, 0.15)), 3.25),
        ("top", Vector((0.0, 0.0, 4.2)), 3.25),
        ("three-quarter", Vector((3.5, -4.0, 2.7)), 3.45),
    )
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    target = Vector((0.0, -0.08, 0.0))
    for view_name, location, scale in views:
        camera.location = location
        camera.data.ortho_scale = scale
        point_at(camera, target)
        output = os.path.join(PREVIEW_DIR, f"{view_name}.png")
        scene.render.filepath = output
        bpy.ops.render.render(write_still=True)
        if not os.path.isfile(output) or os.path.getsize(output) == 0:
            raise RuntimeError(f"Preview missing or empty: {output}")
        print(f"PREVIEW {view_name}: {output} ({os.path.getsize(output)} bytes)")


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
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)

    surface_parts = [
        closed_frustum("Body", "body", -0.82, 0.68, 0.34, 0.34),
        pointed_cone("Nose", "body", -0.79, -1.50, 0.34),
        wedge_fin("FinLeft", math.pi),
        wedge_fin("FinRight", 0.0),
        wedge_fin("FinTop", math.pi * 0.5),
        wedge_fin("FinBottom", math.pi * 1.5),
        annular_nozzle("NozzleShell", "nozzle", 0.64, 0.98, 0.31, 0.23, 0.24, 0.16),
    ]
    hot_parts = [
        closed_frustum("Throat", "hot", 0.90, 1.08, 0.18, 0.15),
        closed_frustum("Exhaust", "hot", 0.98, 1.25, 0.14, 0.04),
    ]
    parts = surface_parts + hot_parts
    surface_material = create_pbr_material("RocketSurface", 0.0)
    hot_material = create_pbr_material("RocketHot", 3.0)
    objects = [
        combine_parts("RocketSurface", surface_parts, surface_material),
        combine_parts("RocketHot", hot_parts, hot_material),
    ]

    audit = audit_objects(objects, parts)
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    with open(SEMANTIC_AUDIT_PATH, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(audit, handle, indent=2, sort_keys=True)
        handle.write("\n")
    print(f"SEMANTIC_AUDIT {SEMANTIC_AUDIT_PATH} ({os.path.getsize(SEMANTIC_AUDIT_PATH)} bytes)")

    before_preview = json.dumps(audit_objects(objects, parts), sort_keys=True)
    render_previews(objects)
    after_preview = json.dumps(audit_objects(objects, parts), sort_keys=True)
    if before_preview != after_preview:
        raise RuntimeError("Preview generation mutated export objects")
    export_fbx(objects)
    print(
        "AUDIT PASS: "
        f"objects={[obj.name for obj in objects]}, triangles={audit['triangles']}, "
        f"bounds={audit['bounds']['minimum']}..{audit['bounds']['maximum']}, "
        "UV0-only, manifold positive-volume parts, overlaps and atlas containment valid"
    )


if __name__ == "__main__":
    main()
