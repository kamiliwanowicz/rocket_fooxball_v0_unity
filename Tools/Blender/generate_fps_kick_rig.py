"""Generate the original camera-space right boot kick rig used by Unity."""

import math
import os
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
BLEND_PATH = REPOSITORY_ROOT / "Tools" / "Blender" / "FpsKickRig.blend"
FBX_PATH = REPOSITORY_ROOT / "Assets" / "_Game" / "Models" / "FpsKickRig.fbx"
PREVIEW_DIR = REPOSITORY_ROOT / "Temp" / "BlenderPreviews" / "FpsKickRig"

FPS = 30
KICK_START = 1
KICK_CONTACT = 5
KICK_END = 11
KICK_SECONDS = (KICK_END - KICK_START) / FPS
CONTACT_FRACTION = (KICK_CONTACT - KICK_START) / (KICK_END - KICK_START)
MOUNT_UNITY = Vector((0.12, -0.42, 0.30))
MIN_OVERLAP = 0.005
TRIANGLE_RANGE = (500, 900)
ACTION_NAMES = {"Idle", "Kick"}

# Declared before geometry. Parts meet through closed-solid overlap along named axis.
CONNECTION_MAP = (
    ("ShinGuard", "ShinBack", "Y", MIN_OVERLAP),
    ("ShinGuard", "Cuff", "Z", MIN_OVERLAP),
    ("ShinGuard", "BootShell", "Z", MIN_OVERLAP),
    ("BootShell", "ToeCap", "Y", MIN_OVERLAP),
    ("BootShell", "Sole", "Z", MIN_OVERLAP),
    ("Sole", "Heel", "Y", MIN_OVERLAP),
)


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def make_material(name, color):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    material.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1.0)
    material.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.82
    return material


def apply_transform(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    obj.select_set(False)


def add_beveled_box(name, location, dimensions, material, bone_name, bevel=0.018, segments=2):
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    obj.scale = Vector(dimensions) * 0.5
    apply_transform(obj)
    modifier = obj.modifiers.new("EdgeChamfer", "BEVEL")
    modifier.width = bevel
    modifier.segments = segments
    modifier.limit_method = "ANGLE"
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)
    obj.data.materials.append(material)
    group = obj.vertex_groups.new(name=bone_name)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    return obj


def add_cylinder(name, location, radius, depth, vertices, material, bone_name):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        end_fill_type="NGON",
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = f"{name}Mesh"
    apply_transform(obj)
    bevel = obj.modifiers.new("EdgeChamfer", "BEVEL")
    bevel.width = 0.012
    bevel.segments = 1
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    obj.select_set(False)
    obj.data.materials.append(material)
    group = obj.vertex_groups.new(name=bone_name)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    return obj


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        Vector((min(v.x for v in corners), min(v.y for v in corners), min(v.z for v in corners))),
        Vector((max(v.x for v in corners), max(v.y for v in corners), max(v.z for v in corners))),
    )


def audit_connections(parts):
    axis_index = {"X": 0, "Y": 1, "Z": 2}
    for first_name, second_name, axis_name, required in CONNECTION_MAP:
        first_min, first_max = world_bounds(parts[first_name])
        second_min, second_max = world_bounds(parts[second_name])
        axis = axis_index[axis_name]
        overlap = min(first_max[axis], second_max[axis]) - max(first_min[axis], second_min[axis])
        require(overlap >= required - 1e-6, f"Connection {first_name}->{second_name} overlap {overlap:.6f}m < {required:.6f}m")
        print(f"CONNECTION {first_name}->{second_name} axis={axis_name} overlap={overlap:.6f}m")


def create_geometry(materials):
    red, dark, cream = materials
    parts = {}
    parts["ShinGuard"] = add_beveled_box("ShinGuard", (0.0, 0.015, -0.245), (0.205, 0.155, 0.430), red, "Shin.R", 0.025, 2)
    parts["ShinBack"] = add_beveled_box("ShinBack", (0.0, 0.080, -0.255), (0.165, 0.080, 0.360), dark, "Shin.R", 0.014, 2)
    parts["Cuff"] = add_cylinder("Cuff", (0.0, 0.015, -0.055), 0.126, 0.090, 16, cream, "Shin.R")
    parts["BootShell"] = add_beveled_box("BootShell", (0.0, -0.190, -0.505), (0.255, 0.430, 0.205), red, "Foot.R", 0.035, 2)
    parts["ToeCap"] = add_beveled_box("ToeCap", (0.0, -0.405, -0.495), (0.270, 0.145, 0.180), cream, "Foot.R", 0.030, 2)
    parts["Sole"] = add_beveled_box("Sole", (0.0, -0.205, -0.615), (0.285, 0.455, 0.055), dark, "Foot.R", 0.012, 2)
    parts["Heel"] = add_beveled_box("Heel", (0.0, 0.005, -0.575), (0.235, 0.105, 0.145), dark, "Foot.R", 0.022, 2)
    audit_connections(parts)

    bpy.ops.object.select_all(action="DESELECT")
    for part in parts.values():
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts["BootShell"]
    bpy.ops.object.join()
    mesh = bpy.context.object
    mesh.name = "FpsKickMesh"
    mesh.data.name = "FpsKickMeshData"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for polygon in mesh.data.polygons:
        polygon.use_smooth = False

    # Stable single UV layer for Unity materials.
    while mesh.data.uv_layers:
        mesh.data.uv_layers.remove(mesh.data.uv_layers[0])
    mesh.data.uv_layers.new(name="UVMap")
    bpy.context.view_layer.objects.active = mesh
    mesh.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")
    mesh.select_set(False)
    return mesh


def create_armature(mesh):
    armature_data = bpy.data.armatures.new("FpsKickRigData")
    armature = bpy.data.objects.new("FpsKickRig", armature_data)
    bpy.context.collection.objects.link(armature)
    armature.show_in_front = True
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    root = armature.data.edit_bones.new("Root")
    root.head = (0.0, 0.0, 0.0)
    root.tail = (0.0, 0.0, 0.16)
    shin = armature.data.edit_bones.new("Shin.R")
    shin.head = (0.0, 0.0, 0.0)
    shin.tail = (0.0, 0.0, -0.47)
    shin.parent = root
    foot = armature.data.edit_bones.new("Foot.R")
    foot.head = (0.0, 0.0, -0.47)
    foot.tail = (0.0, -0.34, -0.55)
    foot.parent = shin
    foot.use_connect = True
    bpy.ops.object.mode_set(mode="OBJECT")
    armature.select_set(False)

    modifier = mesh.modifiers.new("FpsKickArmature", "ARMATURE")
    modifier.object = armature
    mesh.parent = armature
    return armature


def key_pose(armature, frame, shin_location, shin_x_degrees, foot_x_degrees):
    bpy.context.scene.frame_set(frame)
    root = armature.pose.bones["Root"]
    shin = armature.pose.bones["Shin.R"]
    foot = armature.pose.bones["Foot.R"]
    for bone in (root, shin, foot):
        bone.rotation_mode = "XYZ"
    root.location = (0.0, 0.0, 0.0)
    root.rotation_euler = (0.0, 0.0, 0.0)
    shin.location = shin_location
    shin.rotation_euler = (math.radians(shin_x_degrees), 0.0, 0.0)
    foot.location = (0.0, 0.0, 0.0)
    foot.rotation_euler = (math.radians(foot_x_degrees), 0.0, 0.0)
    for bone in (root, shin, foot):
        bone.keyframe_insert("location", frame=frame, group=bone.name)
        bone.keyframe_insert("rotation_euler", frame=frame, group=bone.name)


def create_actions(armature):
    animation_data = armature.animation_data_create()
    idle = bpy.data.actions.new("Idle")
    idle.use_fake_user = True
    idle.frame_range = (1.0, 31.0)
    animation_data.action = idle
    key_pose(armature, 1, (0.0, 0.0, 0.0), 0.0, 0.0)
    key_pose(armature, 31, (0.0, 0.0, 0.0), 0.0, 0.0)

    kick = bpy.data.actions.new("Kick")
    kick.use_fake_user = True
    kick.frame_range = (KICK_START, KICK_END)
    animation_data.action = kick
    key_pose(armature, KICK_START, (0.0, 0.0, 0.0), 0.0, 0.0)
    key_pose(armature, KICK_CONTACT, (-0.11, -0.15, -0.05), -68.0, 20.0)
    key_pose(armature, 8, (-0.05, -0.08, -0.02), -34.0, 8.0)
    key_pose(armature, KICK_END, (0.0, 0.0, 0.0), 0.0, 0.0)
    for action in (idle, kick):
        for fcurve in action.fcurves:
            for point in fcurve.keyframe_points:
                point.interpolation = "BEZIER"
                point.handle_left_type = "AUTO_CLAMPED"
                point.handle_right_type = "AUTO_CLAMPED"
    animation_data.action = idle
    bpy.context.scene.frame_set(1)
    return idle, kick


def evaluated_world_bounds(obj):
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    corners = [evaluated.matrix_world @ Vector(corner) for corner in evaluated.bound_box]
    return (
        Vector((min(v.x for v in corners), min(v.y for v in corners), min(v.z for v in corners))),
        Vector((max(v.x for v in corners), max(v.y for v in corners), max(v.z for v in corners))),
    )


def camera_contract_sample(mesh, armature, action, frame):
    armature.animation_data.action = action
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    lower, upper = evaluated_world_bounds(mesh)
    # Blender x/z map to Unity x/y. Blender -y maps to Unity +z.
    center = (lower + upper) * 0.5
    unity_center = Vector((MOUNT_UNITY.x + center.x, MOUNT_UNITY.y + center.z, MOUNT_UNITY.z - center.y))
    unity_lower = Vector((MOUNT_UNITY.x + lower.x, MOUNT_UNITY.y + lower.z, MOUNT_UNITY.z - upper.y))
    unity_upper = Vector((MOUNT_UNITY.x + upper.x, MOUNT_UNITY.y + upper.z, MOUNT_UNITY.z - lower.y))
    return lower, upper, unity_center, unity_lower, unity_upper


def audit(mesh, armature, idle, kick):
    require(mesh.name == "FpsKickMesh" and mesh.data.name == "FpsKickMeshData", "Unstable mesh names")
    require(armature.name == "FpsKickRig" and armature.data.name == "FpsKickRigData", "Unstable armature names")
    require([bone.name for bone in armature.data.bones] == ["Root", "Shin.R", "Foot.R"], "Unexpected bone hierarchy")
    require({action.name for action in bpy.data.actions} == ACTION_NAMES, "Unexpected exported action")
    require(TRIANGLE_RANGE[0] <= len(mesh.data.loop_triangles) <= TRIANGLE_RANGE[1], f"Triangle count {len(mesh.data.loop_triangles)} outside {TRIANGLE_RANGE}")
    require(len(mesh.data.uv_layers) == 1 and mesh.data.uv_layers[0].name == "UVMap", "UVMap contract failed")
    require([slot.name for slot in mesh.data.materials] == ["KickRed", "KickBlack", "KickCream"], "Material slots unstable")
    require(all(abs(value) < 1e-6 for value in mesh.rotation_euler), "Mesh rotation not applied")
    require(all(abs(value - 1.0) < 1e-6 for value in mesh.scale), "Mesh scale not applied")
    require(mesh.location.length < 1e-6, "Mesh origin not baked to rig origin")
    require(all(abs(value) < 1e-6 for value in armature.rotation_euler), "Armature rotation not applied")
    require(all(abs(value - 1.0) < 1e-6 for value in armature.scale), "Armature scale not applied")
    require(0.30 <= KICK_SECONDS <= 0.38, "Kick duration outside contract")
    require(0.35 <= CONTACT_FRACTION <= 0.45, "Kick contact outside contract")

    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    require(all(math.isfinite(component) for vertex in bm.verts for component in vertex.co), "Non-finite vertex")
    require(all(edge.calc_length() > 1e-7 for edge in bm.edges), "Zero-length edge")
    require(all(face.calc_area() > 1e-9 for face in bm.faces), "Zero-area face")
    require(not [edge for edge in bm.edges if not edge.is_manifold], "Non-manifold mesh edge")
    require(not [vertex for vertex in bm.verts if not vertex.link_faces], "Loose mesh vertex")
    bm.normal_update()
    require(all(face.normal.length > 0.999 for face in bm.faces), "Invalid face normal")
    require(bm.calc_volume(signed=True) > 0.0, "Inverted or inconsistent winding")
    bm.free()

    group_names = {group.index: group.name for group in mesh.vertex_groups}
    for vertex in mesh.data.vertices:
        total = sum(group.weight for group in vertex.groups)
        require(vertex.groups and abs(total - 1.0) < 1e-5, f"Vertex {vertex.index} weight sum {total}")
        require(len(vertex.groups) <= 4, f"Vertex {vertex.index} exceeds influence cap")
        require(all(group_names[group.group] in {"Shin.R", "Foot.R"} for group in vertex.groups), "Unknown deform group")

    root = armature.pose.bones["Root"]
    armature.animation_data.action = kick
    root_samples = []
    for frame in range(KICK_START, KICK_END + 1):
        bpy.context.scene.frame_set(frame)
        root_samples.append((root.location.copy(), root.rotation_euler.copy()))
    require(max(location.length for location, _ in root_samples) <= 0.005, "Root translation motion detected")
    require(max(max(abs(math.degrees(v)) for v in rotation) for _, rotation in root_samples) <= 0.5, "Root rotation motion detected")

    armature.animation_data.action = kick
    bpy.context.scene.frame_set(KICK_START)
    start_pose = [(bone.location.copy(), bone.rotation_euler.copy()) for bone in armature.pose.bones]
    bpy.context.scene.frame_set(KICK_END)
    end_pose = [(bone.location.copy(), bone.rotation_euler.copy()) for bone in armature.pose.bones]
    require(all((a[0] - b[0]).length < 1e-7 and (Vector(a[1]) - Vector(b[1])).length < 1e-7 for a, b in zip(start_pose, end_pose)), "Kick final pose not idle-compatible")

    idle_bounds = camera_contract_sample(mesh, armature, idle, 1)
    contact_bounds = camera_contract_sample(mesh, armature, kick, KICK_CONTACT)
    idle_center = idle_bounds[2]
    contact_center = contact_bounds[2]
    half_vertical_tangent = math.tan(math.radians(75.0 * 0.5))
    idle_screen_y = idle_center.y / (idle_center.z * half_vertical_tangent)
    contact_screen_y = contact_center.y / (contact_center.z * half_vertical_tangent)
    contact_screen_x = contact_center.x / (contact_center.z * half_vertical_tangent * (16.0 / 9.0))
    require(idle_screen_y < -1.10, f"Idle center not below view: normalized y={idle_screen_y:.3f}")
    require(abs(contact_screen_x) < 0.25 and -0.85 < contact_screen_y < -0.25, f"Contact misses lower-center: center={tuple(round(v, 3) for v in contact_center)} normalized=({contact_screen_x:.3f},{contact_screen_y:.3f})")
    require(contact_bounds[3].z > 0.03, f"Contact crosses near plane: minimum Unity z={contact_bounds[3].z:.3f}")

    armature.animation_data.action = idle
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    mesh.data.calc_loop_triangles()
    lower, upper = evaluated_world_bounds(mesh)
    print(f"AUDIT mesh={mesh.name} vertices={len(mesh.data.vertices)} triangles={len(mesh.data.loop_triangles)}")
    print(f"AUDIT bounds_min={tuple(round(v, 6) for v in lower)} bounds_max={tuple(round(v, 6) for v in upper)}")
    print(f"AUDIT actions=Idle[1,31] Kick[{KICK_START},{KICK_END}] seconds={KICK_SECONDS:.6f} contact={CONTACT_FRACTION:.2%}")
    print(f"AUDIT camera_idle_center={tuple(round(v, 6) for v in idle_center)} normalized_y={idle_screen_y:.6f}")
    print(f"AUDIT camera_contact_center={tuple(round(v, 6) for v in contact_center)} normalized=({contact_screen_x:.6f},{contact_screen_y:.6f})")


def point_camera(camera, target):
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()


def render_previews(mesh, armature, idle):
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.025, 0.022, 0.028)

    bpy.ops.object.light_add(type="AREA", location=(2.5, -3.5, 3.5))
    key = bpy.context.object
    key.name = "PreviewKey"
    key.data.energy = 850
    key.data.shape = "DISK"
    key.data.size = 4.0
    point_camera(key, (0.0, -0.15, -0.3))
    bpy.ops.object.light_add(type="AREA", location=(-2.5, 1.5, 1.0))
    fill = bpy.context.object
    fill.name = "PreviewFill"
    fill.data.energy = 500
    fill.data.size = 3.0
    point_camera(fill, (0.0, -0.15, -0.3))

    camera_data = bpy.data.cameras.new("PreviewCameraData")
    camera = bpy.data.objects.new("PreviewCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    camera.data.lens = 58
    target = Vector((0.0, -0.14, -0.31))
    views = {
        "front": (0.0, -2.2, -0.15),
        "rear": (0.0, 2.0, -0.15),
        "left": (-2.0, -0.15, -0.15),
        "right": (2.0, -0.15, -0.15),
        "top": (0.0, -0.15, 1.65),
        "three-quarter": (1.55, -1.65, 0.75),
    }
    armature.animation_data.action = idle
    scene.frame_set(1)
    for name, location in views.items():
        camera.location = location
        point_camera(camera, target)
        output = PREVIEW_DIR / f"{name}.png"
        scene.render.filepath = str(output)
        bpy.ops.render.render(write_still=True)
        require(output.exists() and output.stat().st_size > 0, f"Missing preview {output}")
        print(f"PREVIEW {name}={output} bytes={output.stat().st_size}")

    bpy.data.objects.remove(camera, do_unlink=True)
    bpy.data.objects.remove(key, do_unlink=True)
    bpy.data.objects.remove(fill, do_unlink=True)


def save_and_export(mesh, armature, idle):
    BLEND_PATH.parent.mkdir(parents=True, exist_ok=True)
    FBX_PATH.parent.mkdir(parents=True, exist_ok=True)
    armature.animation_data.action = idle
    bpy.context.scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), check_existing=False)

    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.export_scene.fbx(
        filepath=str(FBX_PATH),
        use_selection=True,
        object_types={"MESH", "ARMATURE"},
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        use_mesh_modifiers=True,
        mesh_smooth_type="OFF",
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
    )
    require(BLEND_PATH.exists() and BLEND_PATH.stat().st_size > 0, "Blend source not saved")
    require(FBX_PATH.exists() and FBX_PATH.stat().st_size > 0, "FBX not exported")
    print(f"OUTPUT blend={BLEND_PATH} bytes={BLEND_PATH.stat().st_size}")
    print(f"OUTPUT fbx={FBX_PATH} bytes={FBX_PATH.stat().st_size}")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = FPS
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    materials = (
        make_material("KickRed", (0.62, 0.018, 0.028)),
        make_material("KickBlack", (0.018, 0.012, 0.018)),
        make_material("KickCream", (0.88, 0.75, 0.54)),
    )
    mesh = create_geometry(materials)
    armature = create_armature(mesh)
    idle, kick = create_actions(armature)
    mesh.data.calc_loop_triangles()
    audit(mesh, armature, idle, kick)
    render_previews(mesh, armature, idle)
    save_and_export(mesh, armature, idle)
    print("FPS_KICK_RIG_GENERATION_SUCCEEDED")


if __name__ == "__main__":
    main()
