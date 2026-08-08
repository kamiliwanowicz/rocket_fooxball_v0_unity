"""Generate Rocket Fooxball's original low-poly armored footballer."""

import math
import os

import bpy
from mathutils import Vector


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BLEND_PATH = os.path.join(REPOSITORY_ROOT, "Tools", "Blender", "LowPolyCharacter.blend")
FBX_PATH = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models", "LowPolyCharacter.fbx")
PREVIEW_DIR = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "LowPolyCharacter")
MESH_NAMES = ("CharacterBody", "CharacterArmor", "CharacterHead", "CharacterEye")
ACTION_NAMES = ("Idle", "Kick")
TARGET_BOUNDS = (0.75, 0.45, 1.75)
MIN_OVERLAP = 0.005

# Authored before geometry. Each pair names joined solids and required AABB overlap.
CONNECTION_MAP = (
    ("Pelvis", "Torso", "Z", MIN_OVERLAP),
    ("Torso", "Neck", "Z", MIN_OVERLAP),
    ("Neck", "Helmet", "Z", MIN_OVERLAP),
    ("Pelvis", "Thigh.R", "Z", MIN_OVERLAP),
    ("Thigh.R", "Shin.R", "Z", MIN_OVERLAP),
    ("Shin.R", "Boot.R", "Z", MIN_OVERLAP),
    ("Pelvis", "Thigh.L", "Z", MIN_OVERLAP),
    ("Thigh.L", "Shin.L", "Z", MIN_OVERLAP),
    ("Shin.L", "Boot.L", "Z", MIN_OVERLAP),
    ("Torso", "UpperArm.R", "X", MIN_OVERLAP),
    ("UpperArm.R", "Forearm.R", "Z", MIN_OVERLAP),
    ("Torso", "UpperArm.L", "X", MIN_OVERLAP),
    ("UpperArm.L", "Forearm.L", "Z", MIN_OVERLAP),
)


def make_material(name, color):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    return material


def add_uv_part(name, collection, bone, location, scale, material, bounds):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=12,
        ring_count=8,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name + "Mesh"
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    group = obj.vertex_groups.new(name=bone)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    if obj.data.uv_layers:
        obj.data.uv_layers.active.name = "UVMap"
    bounds[name] = world_bounds(obj)
    collection.append(obj)
    return obj


def add_box_part(name, collection, bone, location, scale, material, bounds, bevel=0.025):
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name + "Mesh"
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0.0:
        modifier = obj.modifiers.new("FacetedBevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.data.materials.append(material)
    group = obj.vertex_groups.new(name=bone)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    if obj.data.uv_layers:
        obj.data.uv_layers.active.name = "UVMap"
    bounds[name] = world_bounds(obj)
    collection.append(obj)
    return obj


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    low = Vector((min(v.x for v in corners), min(v.y for v in corners), min(v.z for v in corners)))
    high = Vector((max(v.x for v in corners), max(v.y for v in corners), max(v.z for v in corners)))
    return low, high


def join_parts(parts, object_name):
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = object_name
    obj.data.name = object_name + "Mesh"
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


def make_rig():
    bpy.ops.object.armature_add(enter_editmode=True, location=(0.0, 0.0, 0.0))
    rig = bpy.context.object
    rig.name = "CharacterRig"
    rig.data.name = "CharacterRig"
    edit = rig.data.edit_bones
    edit.remove(edit[0])

    def bone(name, head, tail, parent=None):
        item = edit.new(name)
        item.head = head
        item.tail = tail
        if parent:
            item.parent = edit[parent]
        return item

    bone("Root", (0, 0, 0.00), (0, 0, 0.10))
    bone("Pelvis", (0, 0, 0.78), (0, 0, 0.98), "Root")
    bone("Spine", (0, 0, 0.94), (0, 0, 1.22), "Pelvis")
    bone("Chest", (0, 0, 1.18), (0, 0, 1.42), "Spine")
    bone("Neck", (0, 0, 1.39), (0, 0, 1.50), "Chest")
    bone("Head", (0, 0, 1.48), (0, 0, 1.70), "Neck")
    bone("Thigh.R", (-0.16, 0, 0.86), (-0.16, 0, 0.52), "Pelvis")
    bone("Shin.R", (-0.16, 0, 0.52), (-0.16, 0, 0.16), "Thigh.R")
    bone("Foot.R", (-0.16, 0, 0.16), (-0.16, -0.20, 0.08), "Shin.R")
    bone("Thigh.L", (0.16, 0, 0.86), (0.16, 0, 0.52), "Pelvis")
    bone("Shin.L", (0.16, 0, 0.52), (0.16, 0, 0.16), "Thigh.L")
    bone("Foot.L", (0.16, 0, 0.16), (0.16, -0.20, 0.08), "Shin.L")
    bone("UpperArm.R", (-0.25, 0, 1.35), (-0.31, 0, 1.05), "Chest")
    bone("Forearm.R", (-0.31, 0, 1.05), (-0.29, -0.03, 0.82), "UpperArm.R")
    bone("Hand.R", (-0.29, -0.03, 0.82), (-0.29, -0.08, 0.72), "Forearm.R")
    bone("UpperArm.L", (0.25, 0, 1.35), (0.31, 0, 1.05), "Chest")
    bone("Forearm.L", (0.31, 0, 1.05), (0.29, -0.03, 0.82), "UpperArm.L")
    bone("Hand.L", (0.29, -0.03, 0.82), (0.29, -0.08, 0.72), "Forearm.L")
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def attach_mesh(obj, rig):
    modifier = obj.modifiers.new("CharacterSkin", "ARMATURE")
    modifier.object = rig
    obj.parent = rig


def key_rotation(action, rig, bone_name, frame, rotation):
    rig.animation_data.action = action
    pose = rig.pose.bones[bone_name]
    pose.rotation_mode = "XYZ"
    pose.rotation_euler = rotation
    pose.keyframe_insert("rotation_euler", frame=frame, group=bone_name)


def make_actions(rig):
    rig.animation_data_create()
    idle = bpy.data.actions.new("Idle")
    idle.use_fake_user = True
    idle.frame_range
    for frame in (1, 30):
        key_rotation(idle, rig, "Chest", frame, (0.0, 0.0, 0.0))
        key_rotation(idle, rig, "Thigh.R", frame, (0.0, 0.0, 0.0))
        key_rotation(idle, rig, "Shin.R", frame, (0.0, 0.0, 0.0))
        key_rotation(idle, rig, "Foot.R", frame, (0.0, 0.0, 0.0))

    kick = bpy.data.actions.new("Kick")
    kick.use_fake_user = True
    for frame, thigh, shin, foot in (
        (1, (0, 0, 0), (0, 0, 0), (0, 0, 0)),
        (3, (math.radians(-22), 0, 0), (math.radians(38), 0, 0), (math.radians(-10), 0, 0)),
        (5, (math.radians(67), 0, 0), (math.radians(-42), 0, 0), (math.radians(18), 0, 0)),
        (8, (math.radians(22), 0, 0), (math.radians(-8), 0, 0), (0, 0, 0)),
        (12, (0, 0, 0), (0, 0, 0), (0, 0, 0)),
    ):
        key_rotation(kick, rig, "Thigh.R", frame, thigh)
        key_rotation(kick, rig, "Shin.R", frame, shin)
        key_rotation(kick, rig, "Foot.R", frame, foot)

    rig.animation_data.action = idle
    for action in (idle, kick):
        track = rig.animation_data.nla_tracks.new()
        track.name = action.name
        strip = track.strips.new(action.name, int(action.frame_range[0]), action)
        strip.action_frame_start = action.frame_range[0]
        strip.action_frame_end = action.frame_range[1]
        track.mute = True
    return idle, kick


def build_character():
    red = make_material("CharacterRed", (0.56, 0.025, 0.035))
    black = make_material("CharacterBlack", (0.018, 0.014, 0.018))
    cream = make_material("CharacterCream", (0.78, 0.67, 0.50))
    eye_red = make_material("CharacterEyeRed", (0.96, 0.04, 0.02))
    body, armor, head, eye, bounds = [], [], [], [], {}

    add_uv_part("Pelvis", body, "Pelvis", (0, 0.01, 0.85), (0.23, 0.15, 0.19), black, bounds)
    add_uv_part("Torso", body, "Spine", (0, 0.015, 1.18), (0.255, 0.17, 0.31), cream, bounds)
    add_uv_part("Neck", body, "Neck", (0, 0, 1.47), (0.09, 0.085, 0.10), black, bounds)
    for side, x in (("R", -0.16), ("L", 0.16)):
        add_uv_part(f"Thigh.{side}", body, f"Thigh.{side}", (x, 0.015, 0.67), (0.115, 0.115, 0.235), cream, bounds)
        add_uv_part(f"Shin.{side}", body, f"Shin.{side}", (x, 0.005, 0.30), (0.10, 0.10, 0.20), black, bounds)
        add_box_part(f"Boot.{side}", body, f"Foot.{side}", (x, -0.055, 0.075), (0.12, 0.19, 0.075), black, bounds, 0.025)
    for side, x in (("R", -0.290), ("L", 0.290)):
        add_uv_part(f"UpperArm.{side}", body, f"UpperArm.{side}", (x, 0.005, 1.23), (0.085, 0.09, 0.205), cream, bounds)
        add_uv_part(f"Forearm.{side}", body, f"Forearm.{side}", (x, -0.005, 0.94), (0.075, 0.085, 0.17), black, bounds)
        add_uv_part(f"Hand.{side}", body, f"Hand.{side}", (x, -0.04, 0.77), (0.075, 0.075, 0.09), cream, bounds)

    add_uv_part("ChestPlate", armor, "Chest", (0, -0.145, 1.26), (0.245, 0.065, 0.245), red, bounds)
    add_uv_part("BackPlate", armor, "Chest", (0, 0.145, 1.25), (0.235, 0.055, 0.23), black, bounds)
    add_box_part("ChestChevron", armor, "Chest", (0, -0.202, 1.30), (0.12, 0.025, 0.075), black, bounds, 0.015)
    for side, x in (("R", -0.285), ("L", 0.285)):
        add_uv_part(f"Shoulder.{side}", armor, f"UpperArm.{side}", (x, -0.005, 1.37), (0.09, 0.12, 0.12), red, bounds)
        add_uv_part(f"Knee.{side}", armor, f"Shin.{side}", (x * 0.56, -0.085, 0.46), (0.105, 0.05, 0.10), red, bounds)
        add_box_part(f"ShinPlate.{side}", armor, f"Shin.{side}", (x * 0.56, -0.082, 0.285), (0.075, 0.035, 0.11), red, bounds, 0.015)

    add_uv_part("Helmet", head, "Head", (0, 0.015, 1.595), (0.155, 0.145, 0.155), red, bounds)
    add_uv_part("FaceMask", head, "Head", (0, -0.125, 1.565), (0.125, 0.055, 0.115), black, bounds)
    add_box_part("Brow", head, "Head", (0, -0.188, 1.625), (0.125, 0.025, 0.035), cream, bounds, 0.012)
    add_box_part("EyeLens", eye, "Head", (0, -0.191, 1.575), (0.078, 0.018, 0.042), eye_red, bounds, 0.010)
    add_box_part("EyePupil", eye, "Head", (0, -0.210, 1.575), (0.021, 0.009, 0.021), black, bounds, 0.006)

    audit_connections(bounds)
    meshes = [join_parts(body, "CharacterBody"), join_parts(armor, "CharacterArmor"), join_parts(head, "CharacterHead"), join_parts(eye, "CharacterEye")]
    rig = make_rig()
    for mesh in meshes:
        attach_mesh(mesh, rig)
    actions = make_actions(rig)
    return meshes, rig, actions


def audit_connections(bounds):
    axis_index = {"X": 0, "Y": 1, "Z": 2}
    for first, second, axis, required in CONNECTION_MAP:
        low_a, high_a = bounds[first]
        low_b, high_b = bounds[second]
        overlap = min(high_a[axis_index[axis]], high_b[axis_index[axis]]) - max(low_a[axis_index[axis]], low_b[axis_index[axis]])
        if overlap + 1e-6 < required:
            raise RuntimeError(f"Connection {first}->{second} overlap {overlap:.6f}m below {required:.6f}m on {axis}")


def audit(meshes, rig, actions):
    all_points = []
    total_vertices = 0
    total_triangles = 0
    bone_names = {bone.name for bone in rig.data.bones}
    if rig.name != "CharacterRig" or len(bone_names) != len(rig.data.bones):
        raise RuntimeError("Armature name/bone names invalid")
    required = {"Root", "Pelvis", "Thigh.R", "Shin.R", "Foot.R"}
    if not required.issubset(bone_names):
        raise RuntimeError("Required rig chain missing")
    if rig.data.bones["Pelvis"].parent.name != "Root" or rig.data.bones["Shin.R"].parent.name != "Thigh.R" or rig.data.bones["Foot.R"].parent.name != "Shin.R":
        raise RuntimeError("Required rig hierarchy invalid")

    for obj in meshes:
        if obj.name not in MESH_NAMES or any(abs(v) > 1e-6 for v in obj.rotation_euler) or any(abs(v - 1.0) > 1e-6 for v in obj.scale):
            raise RuntimeError(f"Transform/name audit failed: {obj.name}")
        if len(obj.data.uv_layers) != 1 or obj.data.uv_layers[0].name != "UVMap":
            raise RuntimeError(f"UVMap audit failed: {obj.name}")
        obj.data.calc_loop_triangles()
        total_vertices += len(obj.data.vertices)
        total_triangles += len(obj.data.loop_triangles)
        for vertex in obj.data.vertices:
            point = obj.matrix_world @ vertex.co
            if not all(math.isfinite(value) for value in point):
                raise RuntimeError(f"Non-finite vertex: {obj.name}")
            all_points.append(point)
            weights = [(group.group, group.weight) for group in vertex.groups if group.weight > 1e-7]
            if not weights or len(weights) > 4 or abs(sum(weight for _, weight in weights) - 1.0) > 1e-5:
                raise RuntimeError(f"Weight audit failed: {obj.name} vertex {vertex.index}")
        for edge in obj.data.edges:
            if (obj.data.vertices[edge.vertices[0]].co - obj.data.vertices[edge.vertices[1]].co).length <= 1e-7:
                raise RuntimeError(f"Zero-length edge: {obj.name}")
        for polygon in obj.data.polygons:
            if polygon.area <= 1e-10:
                raise RuntimeError(f"Zero-area face: {obj.name}")
        for edge in obj.data.edges:
            linked_faces = sum(1 for polygon in obj.data.polygons if edge.key in polygon.edge_keys)
            if linked_faces != 2:
                raise RuntimeError(f"Non-manifold edge: {obj.name}")

    low = Vector((min(p.x for p in all_points), min(p.y for p in all_points), min(p.z for p in all_points)))
    high = Vector((max(p.x for p in all_points), max(p.y for p in all_points), max(p.z for p in all_points)))
    dimensions = high - low
    if low.z < -1e-5 or low.z > 0.005 or high.z > TARGET_BOUNDS[2] + 0.005:
        raise RuntimeError(f"Ground/origin bounds invalid: {tuple(low)} to {tuple(high)}")
    if dimensions.x > TARGET_BOUNDS[0] + 0.005 or dimensions.y > TARGET_BOUNDS[1] + 0.005 or dimensions.z > TARGET_BOUNDS[2] + 0.005:
        raise RuntimeError(f"Character exceeds target bounds: {tuple(dimensions)}")
    if not 2000 <= total_triangles <= 4000:
        raise RuntimeError(f"Triangle budget invalid: {total_triangles}")
    if tuple(action.name for action in actions) != ACTION_NAMES or {action.name for action in bpy.data.actions} != set(ACTION_NAMES):
        raise RuntimeError("Declared action audit failed")
    kick = bpy.data.actions["Kick"]
    duration = (kick.frame_range[1] - kick.frame_range[0]) / 30.0
    contact_ratio = (5.0 - kick.frame_range[0]) / (kick.frame_range[1] - kick.frame_range[0])
    if not 0.30 <= duration <= 0.38 or not 0.35 <= contact_ratio <= 0.45:
        raise RuntimeError(f"Kick timing invalid: {duration:.3f}s contact {contact_ratio:.3f}")
    for action in actions:
        for curve in action.fcurves:
            if curve.data_path in {'pose.bones["Root"].location', 'pose.bones["Pelvis"].location', 'pose.bones["Root"].rotation_euler', 'pose.bones["Pelvis"].rotation_euler'}:
                raise RuntimeError("Root or pelvis motion authored")
    for bone_name in ("Thigh.R", "Shin.R", "Foot.R"):
        curves = [curve for curve in kick.fcurves if f'pose.bones["{bone_name}"]' in curve.data_path]
        for curve in curves:
            if abs(curve.evaluate(1.0) - curve.evaluate(12.0)) > 1e-7:
                raise RuntimeError(f"Kick final pose mismatch: {bone_name}")
    print(f"AUDIT bounds min={tuple(round(v, 4) for v in low)} max={tuple(round(v, 4) for v in high)} dimensions={tuple(round(v, 4) for v in dimensions)}")
    print(f"AUDIT vertices={total_vertices} triangles={total_triangles} actions=Idle(loop),Kick(non-loop) duration={duration:.3f}s contact={contact_ratio:.3f}")
    print("AUDIT weights=normalized max_influences=1 unweighted=0 connections=13 overlap>=0.005m")
    return low, high, total_vertices, total_triangles


def render_previews(meshes, rig, low, high):
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.035, 0.04, 0.05)
    scene.frame_set(1)
    rig.animation_data.action = bpy.data.actions["Idle"]

    bpy.ops.object.light_add(type="AREA", location=(2.5, -3.5, 4.0))
    key = bpy.context.object
    key.name = "PreviewKey"
    key.data.energy = 900
    key.data.shape = "DISK"
    key.data.size = 4.0
    bpy.ops.object.light_add(type="AREA", location=(-3.0, 1.5, 2.0))
    fill = bpy.context.object
    fill.name = "PreviewFill"
    fill.data.energy = 550
    fill.data.size = 3.0
    bpy.ops.object.camera_add()
    camera = bpy.context.object
    camera.name = "PreviewCamera"
    camera.data.lens = 58
    scene.camera = camera
    target = Vector((0.0, 0.0, (low.z + high.z) * 0.5))
    views = {
        "front": (0.0, -3.4, 0.95),
        "rear": (0.0, 3.4, 0.95),
        "left": (-3.4, 0.0, 0.95),
        "right": (3.4, 0.0, 0.95),
        "top": (0.0, 0.0, 4.0),
        "three-quarter": (2.5, -2.8, 1.65),
    }
    for name, location in views.items():
        camera.location = location
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(PREVIEW_DIR, name + ".png")
        bpy.ops.render.render(write_still=True)
        if not os.path.isfile(scene.render.filepath) or os.path.getsize(scene.render.filepath) == 0:
            raise RuntimeError(f"Preview render failed: {scene.render.filepath}")
    bpy.data.objects.remove(camera, do_unlink=True)
    bpy.data.objects.remove(key, do_unlink=True)
    bpy.data.objects.remove(fill, do_unlink=True)


def save_and_export(meshes, rig):
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)
    rig.animation_data.action = bpy.data.actions["Idle"]
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in (*meshes, rig):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=True,
        object_types={"MESH", "ARMATURE"},
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
    )
    if not os.path.isfile(FBX_PATH) or os.path.getsize(FBX_PATH) == 0:
        raise RuntimeError("FBX export missing or empty")
    print(f"OUTPUT blend={BLEND_PATH} bytes={os.path.getsize(BLEND_PATH)}")
    print(f"OUTPUT fbx={FBX_PATH} bytes={os.path.getsize(FBX_PATH)}")
    print(f"OUTPUT previews={PREVIEW_DIR} count=6")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.scene.render.fps = 30
    meshes, rig, actions = build_character()
    low, high, _, _ = audit(meshes, rig, actions)
    render_previews(meshes, rig, low, high)
    save_and_export(meshes, rig)


if __name__ == "__main__":
    main()
