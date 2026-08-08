"""Generate the low-poly rocket visual used by Unity."""

import math
import os

import bpy


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUTPUT_PATH = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Models", "LowPolyRocket.fbx")


def add_cylinder(name, vertices, radius, depth, y):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        end_fill_type="NGON",
        location=(0.0, y, 0.0),
        rotation=(math.radians(90.0), 0.0, 0.0),
    )
    bpy.context.object.name = name
    return bpy.context.object


def add_fin(name, location, scale):
    bpy.ops.mesh.primitive_cube_add(location=location)
    fin = bpy.context.object
    fin.name = name
    fin.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return fin


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)

    parts = [add_cylinder("Body", 12, 0.32, 1.6, 0.0)]

    bpy.ops.mesh.primitive_cone_add(
        vertices=12,
        radius1=0.32,
        radius2=0.0,
        depth=0.7,
        end_fill_type="NGON",
        location=(0.0, -1.15, 0.0),
        rotation=(math.radians(90.0), 0.0, 0.0),
    )
    bpy.context.object.name = "Nose"
    parts.append(bpy.context.object)

    parts.extend(
        [
            add_fin("FinLeft", (-0.43, 0.55, 0.0), (0.28, 0.32, 0.06)),
            add_fin("FinRight", (0.43, 0.55, 0.0), (0.28, 0.32, 0.06)),
            add_fin("FinBottom", (0.0, 0.55, -0.43), (0.06, 0.32, 0.28)),
            add_fin("FinTop", (0.0, 0.55, 0.43), (0.06, 0.32, 0.28)),
        ]
    )

    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()

    rocket = bpy.context.object
    rocket.name = "RocketVisual"
    rocket.data.name = "RocketVisualMesh"
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    for polygon in rocket.data.polygons:
        polygon.use_smooth = False

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
    print(f"Generated {OUTPUT_PATH}: {len(rocket.data.vertices)} vertices, {len(rocket.data.polygons)} polygons")


if __name__ == "__main__":
    main()
