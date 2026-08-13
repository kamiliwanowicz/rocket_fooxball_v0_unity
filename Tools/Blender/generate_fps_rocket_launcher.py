"""Generate the original low-poly first-person rocket launcher visual."""

import math
import os

import bmesh
import bpy
from mathutils import Vector


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUTPUT_PATH = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models", "FpsRocketLauncher.fbx")
PREVIEW_DIR = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "FpsRocketLauncher")

TARGET_MIN = Vector((-0.16, -0.55, -0.14))
TARGET_MAX = Vector((0.16, 0.20, 0.11))
HARD_ENVELOPE = Vector((0.40, 0.90, 0.35))  # Blender X width, Y length, Z height.
MIN_OVERLAP = 0.005
TRIANGLE_MIN = 700
TRIANGLE_MAX = 1500

# Multipart connection map declared before geometry. Each joint requires at least
# MIN_OVERLAP world-space AABB contact on every axis. Rails derive Y spans from
# measured neighbour endpoints rather than guessed lengths.
CONNECTION_MAP = (
    ("Grip", "RearBlock"),
    ("RearBlock", "Core"),
    ("Core", "MuzzleHousing"),
    ("MuzzleHousing", "MuzzleCollar"),
    ("MuzzleHousing", "MuzzleInset"),
    ("Core", "TopSpine"),
    ("Core", "RailLeft"),
    ("Core", "RailRight"),
    ("MuzzleHousing", "RailLeft"),
    ("MuzzleHousing", "RailRight"),
    ("TopSpine", "AccentSpine"),
    ("RailLeft", "AccentLeft"),
    ("RailRight", "AccentRight"),
    ("MuzzleCollar", "AccentMuzzle"),
)

MATERIAL_SPECS = {
    "WeaponMetal": (0.38, 0.055, 0.045, 1.0),
    "WeaponDark": (0.018, 0.012, 0.014, 1.0),
    "WeaponAccent": (0.82, 0.70, 0.48, 1.0),
}


def make_material(name, color):
    material = bpy.data.materials.new(name=name)
    material.diffuse_color = color
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Roughness"].default_value = 0.72
    bsdf.inputs["Metallic"].default_value = 0.12 if name == "WeaponMetal" else 0.0
    return material


def apply_bevel(obj, width=0.008, segments=1):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    modifier = obj.modifiers.new(name="HardEdgeBevel", type="BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)


def add_box(name, center, dimensions, bevel=0.008, rotation_x=0.0):
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=center, rotation=(rotation_x, 0.0, 0.0))
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    obj.scale = Vector(dimensions) * 0.5
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    apply_bevel(obj, min(bevel, min(dimensions) * 0.22))
    return obj


def add_tapered_box(name, y_front, y_rear, front_width, rear_width, z_bottom, z_top, bevel=0.008):
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
    faces = [
        (0, 3, 2, 1),
        (4, 5, 6, 7),
        (0, 1, 5, 4),
        (3, 7, 6, 2),
        (0, 4, 7, 3),
        (1, 2, 6, 5),
    ]
    mesh = bpy.data.meshes.new(f"{name}Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    apply_bevel(obj, bevel)
    return obj


def add_cylinder(name, radius, z_radius, depth, y, vertices=16, bevel=0.004):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        end_fill_type="NGON",
        location=(0.0, y, 0.015),
        rotation=(math.radians(90.0), 0.0, 0.0),
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.scale.z = z_radius / radius
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    apply_bevel(obj, bevel, segments=1)
    return obj


def add_elliptical_torus(name, major_radius, minor_radius, z_scale, y):
    bpy.ops.mesh.primitive_torus_add(
        align="WORLD",
        major_segments=16,
        minor_segments=4,
        location=(0.0, y, 0.015),
        rotation=(math.radians(90.0), 0.0, 0.0),
        major_radius=major_radius,
        minor_radius=minor_radius,
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.scale.z = z_scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return obj


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        Vector(tuple(min(corner[i] for corner in corners) for i in range(3))),
        Vector(tuple(max(corner[i] for corner in corners) for i in range(3))),
    )


def add_measured_rail(name, x, z, depth, neighbour_a, neighbour_b):
    bounds_a = world_bounds(neighbour_a)
    bounds_b = world_bounds(neighbour_b)
    y_rear = bounds_a[1].y - 0.010
    y_front = bounds_b[0].y + 0.010
    measured_depth = y_rear - y_front
    if measured_depth <= 0.0:
        raise RuntimeError(f"Invalid measured span for {name}: {measured_depth:.6f}")
    return add_box(name, (x, (y_front + y_rear) * 0.5, z), (0.040, measured_depth, depth), 0.005)


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
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR", center="MEDIAN")
    for polygon in result.data.polygons:
        polygon.use_smooth = False
    bpy.context.view_layer.objects.active = result
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    result.data.uv_layers.active.name = "UVMap"
    return result


def create_geometry(materials):
    parts = {}
    parts["Grip"] = add_box("Grip", (0.0, 0.100, -0.055), (0.095, 0.190, 0.130), 0.009, math.radians(-8.0))
    parts["RearBlock"] = add_tapered_box("RearBlock", -0.030, 0.200, 0.205, 0.165, -0.070, 0.075, 0.010)
    parts["Core"] = add_tapered_box("Core", -0.385, 0.015, 0.255, 0.190, -0.075, 0.085, 0.010)
    parts["MuzzleHousing"] = add_cylinder("MuzzleHousing", 0.160, 0.105, 0.175, -0.4625, vertices=16, bevel=0.006)
    parts["MuzzleCollar"] = add_elliptical_torus("MuzzleCollar", 0.124, 0.018, 0.676, -0.5325)
    parts["MuzzleInset"] = add_cylinder("MuzzleInset", 0.105, 0.070, 0.018, -0.553, vertices=16, bevel=0.002)
    parts["TopSpine"] = add_box("TopSpine", (0.0, -0.175, 0.087), (0.090, 0.405, 0.046), 0.006)
    parts["RailLeft"] = add_measured_rail("RailLeft", -0.126, 0.012, 0.055, parts["Core"], parts["MuzzleHousing"])
    parts["RailRight"] = add_measured_rail("RailRight", 0.126, 0.012, 0.055, parts["Core"], parts["MuzzleHousing"])
    parts["AccentSpine"] = add_box("AccentSpine", (0.0, -0.172, 0.106), (0.056, 0.315, 0.020), 0.004)
    parts["AccentLeft"] = add_box("AccentLeft", (-0.143, -0.270, 0.012), (0.034, 0.205, 0.032), 0.004)
    parts["AccentRight"] = add_box("AccentRight", (0.143, -0.270, 0.012), (0.034, 0.205, 0.032), 0.004)
    parts["AccentMuzzle"] = add_elliptical_torus("AccentMuzzle", 0.110, 0.020, 0.67, -0.540)

    part_bounds = {name: world_bounds(obj) for name, obj in parts.items()}
    exported = [
        assign_and_join(
            [parts[name] for name in ("RearBlock", "Core", "MuzzleHousing", "MuzzleCollar", "TopSpine", "RailLeft", "RailRight")],
            "WeaponMetal",
            materials["WeaponMetal"],
        ),
        assign_and_join([parts[name] for name in ("Grip", "MuzzleInset")], "WeaponDark", materials["WeaponDark"]),
        assign_and_join(
            [parts[name] for name in ("AccentSpine", "AccentLeft", "AccentRight", "AccentMuzzle")],
            "WeaponAccent",
            materials["WeaponAccent"],
        ),
    ]
    return exported, part_bounds


def audit_connections(part_bounds):
    for first, second in CONNECTION_MAP:
        first_min, first_max = part_bounds[first]
        second_min, second_max = part_bounds[second]
        overlaps = [min(first_max[i], second_max[i]) - max(first_min[i], second_min[i]) for i in range(3)]
        if min(overlaps) < MIN_OVERLAP - 1e-6:
            raise RuntimeError(f"Connection {first}->{second} overlap failed: {tuple(round(v, 6) for v in overlaps)}")
        print(f"AUDIT connection {first}->{second}: overlap={tuple(round(v, 6) for v in overlaps)}")


def audit_mesh(obj):
    if obj.type != "MESH":
        raise RuntimeError(f"Export object {obj.name} is not a mesh")
    if any(abs(value) > 1e-6 for value in obj.rotation_euler) or any(abs(value - 1.0) > 1e-6 for value in obj.scale):
        raise RuntimeError(f"Unapplied transform on {obj.name}")
    if tuple(slot.material.name for slot in obj.material_slots) != (obj.name,):
        raise RuntimeError(f"Material contract failed on {obj.name}")
    if len(obj.data.uv_layers) != 1 or obj.data.uv_layers[0].name != "UVMap":
        raise RuntimeError(f"UVMap contract failed on {obj.name}")
    if any(not math.isfinite(component) for vertex in obj.data.vertices for component in vertex.co):
        raise RuntimeError(f"Non-finite vertex on {obj.name}")

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    zero_edges = sum(1 for edge in bm.edges if edge.calc_length() <= 1e-8)
    zero_faces = sum(1 for face in bm.faces if face.calc_area() <= 1e-10)
    non_manifold = sum(1 for edge in bm.edges if not edge.is_manifold)
    loose_vertices = sum(1 for vertex in bm.verts if not vertex.link_edges)
    signed_volume = bm.calc_volume(signed=True)
    bm.free()
    if zero_edges or zero_faces or non_manifold or loose_vertices:
        raise RuntimeError(
            f"Mesh topology failed on {obj.name}: zero_edges={zero_edges}, zero_faces={zero_faces}, "
            f"non_manifold={non_manifold}, loose_vertices={loose_vertices}"
        )
    if not math.isfinite(signed_volume) or signed_volume <= 1e-9:
        raise RuntimeError(f"Normals/winding failed on {obj.name}: signed_volume={signed_volume}")

    loop_triangles = len(obj.data.loop_triangles)
    if loop_triangles == 0:
        obj.data.calc_loop_triangles()
        loop_triangles = len(obj.data.loop_triangles)
    print(
        f"AUDIT mesh {obj.name}: vertices={len(obj.data.vertices)}, triangles={loop_triangles}, "
        f"manifold=yes, normals=outward, UVMap=yes, transform=applied"
    )
    return len(obj.data.vertices), loop_triangles


def combined_bounds(objects):
    bounds = [world_bounds(obj) for obj in objects]
    minimum = Vector(tuple(min(pair[0][i] for pair in bounds) for i in range(3)))
    maximum = Vector(tuple(max(pair[1][i] for pair in bounds) for i in range(3)))
    return minimum, maximum


def audit_asset(objects, part_bounds):
    if tuple(obj.name for obj in objects) != ("WeaponMetal", "WeaponDark", "WeaponAccent"):
        raise RuntimeError("Stable export object names failed")
    if len({obj.data.name for obj in objects}) != len(objects):
        raise RuntimeError("Mesh data names are not unique")
    audit_connections(part_bounds)
    counts = [audit_mesh(obj) for obj in objects]
    total_vertices = sum(count[0] for count in counts)
    total_triangles = sum(count[1] for count in counts)

    minimum, maximum = combined_bounds(objects)
    dimensions = maximum - minimum
    if any(dimensions[i] > HARD_ENVELOPE[i] + 1e-6 for i in range(3)):
        raise RuntimeError(f"Hard envelope failed: dimensions={tuple(dimensions)}")
    target_dimensions = TARGET_MAX - TARGET_MIN
    if any(abs(dimensions[i] - target_dimensions[i]) > 0.025 for i in range(3)):
        raise RuntimeError(f"Target dimensions failed: dimensions={tuple(dimensions)}")
    grip_min, grip_max = part_bounds["Grip"]
    if any(not grip_min[i] - 1e-6 <= 0.0 <= grip_max[i] + 1e-6 for i in range(3)):
        raise RuntimeError(f"Origin is outside grip/mount: {tuple(grip_min)} to {tuple(grip_max)}")

    print(f"AUDIT total: vertices={total_vertices}, triangles={total_triangles}")
    print(f"AUDIT bounds min={tuple(round(v, 6) for v in minimum)}, max={tuple(round(v, 6) for v in maximum)}")
    print(f"AUDIT dimensions W/H/L={dimensions.x:.6f}/{dimensions.z:.6f}/{dimensions.y:.6f} m")
    print("AUDIT origin=(0,0,0) inside Grip; forward=Blender -Y; static mesh only")
    return minimum, maximum, total_vertices, total_triangles


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
    if scene.world is None:
        scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.025, 0.028, 0.035)

    center = (minimum + maximum) * 0.5
    camera_data = bpy.data.cameras.new("PreviewCamera")
    camera = bpy.data.objects.new("PreviewCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    camera.data.lens = 58.0

    for name, location, energy, size in (
        ("Key", (1.8, -1.8, 2.0), 900.0, 2.5),
        ("Fill", (-1.8, 0.8, 1.1), 500.0, 2.0),
        ("Rim", (0.8, 1.7, 1.5), 700.0, 1.5),
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
    distance = 1.45
    views = {
        "front": center + Vector((0.0, -distance, 0.02)),
        "rear": center + Vector((0.0, distance, 0.02)),
        "left": center + Vector((-distance, 0.0, 0.02)),
        "right": center + Vector((distance, 0.0, 0.02)),
        "top": center + Vector((0.0, 0.0, distance)),
        "three-quarter": center + Vector((0.95, -1.10, 0.72)),
    }
    for view_name, location in views.items():
        camera.location = location
        point_camera(camera, center)
        output = os.path.join(PREVIEW_DIR, f"{view_name}.png")
        scene.render.filepath = output
        bpy.ops.render.render(write_still=True)
        if not os.path.isfile(output) or os.path.getsize(output) == 0:
            raise RuntimeError(f"Preview render missing or empty: {output}")
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
        raise RuntimeError(f"FBX export missing or empty: {OUTPUT_PATH}")
    print(f"OUTPUT {OUTPUT_PATH} ({os.path.getsize(OUTPUT_PATH)} bytes)")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)

    materials = {name: make_material(name, color) for name, color in MATERIAL_SPECS.items()}
    objects, part_bounds = create_geometry(materials)
    minimum, maximum, _, _ = audit_asset(objects, part_bounds)
    render_previews(objects, minimum, maximum)
    export_fbx(objects)
    print("RESULT FpsRocketLauncher generation succeeded")


if __name__ == "__main__":
    main()
