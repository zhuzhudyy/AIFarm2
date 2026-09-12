"""Build an original, texture-free carrot plant in Blender 5.2.

blender --background --factory-startup --python Tools/Art/build_crop.py
Optional arguments after --: --repo PATH --preview PATH
Ground is Blender Z=0; the carrot's lower root is intentionally buried.
"""

import argparse
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector


parser = argparse.ArgumentParser()
parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
parser.add_argument("--preview", type=Path)
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
repo = args.repo.resolve()
output = repo / "My project/Assets/AIFarm/Art/Models/Crops"
source = repo / "ArtSource/CarrotPlant.blend"
output.mkdir(parents=True, exist_ok=True)
source.parent.mkdir(parents=True, exist_ok=True)
palette = {
    "Art_Crop_CarrotOrange": [0.92, 0.32, 0.075, 1],
    "Art_Crop_CarrotRidge": [0.64, 0.20, 0.048, 1],
    "Art_Crop_CarrotLight": [1.0, 0.49, 0.12, 1],
    "Art_Crop_LeafGreen": [0.20, 0.44, 0.16, 1],
    "Art_Crop_LeafLight": [0.41, 0.64, 0.22, 1],
    "Art_Crop_LeafVein": [0.56, 0.71, 0.28, 1],
}
materials = {}
root = None


def mat(name):
    if name not in materials:
        value = bpy.data.materials.new(name)
        value.diffuse_color = palette[name]
        value.use_nodes = True
        shader = value.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = palette[name]
        shader.inputs["Roughness"].default_value = 0.76
        materials[name] = value
    return materials[name]


def mesh(name, vertices, faces, material):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.parent = root
    data.materials.append(mat(material))
    for poly in data.polygons:
        poly.use_smooth = True
    return obj


def curve(name, points, radius, material, resolution=5, radii=None):
    data = bpy.data.curves.new(name, "CURVE")
    data.dimensions = "3D"
    data.resolution_u = resolution
    data.bevel_depth = radius
    data.bevel_resolution = 0
    spline = data.splines.new("BEZIER")
    spline.bezier_points.add(len(points) - 1)
    for i, (p, co) in enumerate(zip(spline.bezier_points, points)):
        p.co = co
        p.handle_left_type = "AUTO"
        p.handle_right_type = "AUTO"
        if radii:
            p.radius = radii[i]
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target="MESH")
    obj = bpy.context.object
    obj.parent = root
    obj.data.materials.append(mat(material))
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def carrot():
    # A bent, gently ridged carrot profile, with a blunt shoulder and tapered tip.
    profile = [(-0.31, 0.004, -0.035), (-0.273, 0.017, -0.026),
               (-0.20, 0.038, -0.014), (-0.11, 0.061, -0.003),
               (-0.035, 0.076, 0.003), (0.025, 0.083, 0.003),
               (0.082, 0.080, 0.001), (0.116, 0.059, 0),
               (0.126, 0.034, 0), (0.119, 0.011, 0)]
    vertices, faces = [], []
    n = 24
    for z, radius, bend in profile:
        for j in range(n):
            angle = math.tau * j / n
            r = radius * (1 + 0.038 * math.cos(angle * 6))
            vertices.append((bend + r * math.cos(angle), r * math.sin(angle), z))
    for i in range(len(profile) - 1):
        for j in range(n):
            a, b = i * n + j, i * n + (j + 1) % n
            faces.append((a, b, b + n, a + n))
    faces.extend([tuple(reversed(range(n))), tuple((len(profile) - 1) * n + j for j in range(n))])
    pieces = [mesh("CarrotBody", vertices, faces, "Art_Crop_CarrotOrange")]
    # Short, irregular horizontal scars sit on the exposed carrot shoulder.
    for i, (z, angle, width, radius) in enumerate(((0.07, -1.45, 0.58, 0.082),
                                                (0.036, -2.4, 0.45, 0.085),
                                                (0.011, -0.72, 0.50, 0.084),
                                                (-0.07, -1.1, 0.42, 0.072),
                                                (0.045, 0.7, 0.48, 0.084))):
        points = [(radius * math.cos(angle + d), radius * math.sin(angle + d), z + math.sin(d) * 0.003)
                  for d in (-width / 2, 0, width / 2)]
        pieces.append(curve("CarrotGrowthScar", points, 0.0022, "Art_Crop_CarrotRidge", radii=[0.1, 1, 0.1]))
    pieces.append(curve("RootTip", [(-0.035, 0, -0.302), (-0.045, 0.008, -0.332), (-0.065, 0.012, -0.348)],
                        0.0045, "Art_Crop_CarrotLight", radii=[1, 0.6, 0.1]))
    return pieces


def leaflet(name, base, tip, width, light):
    base, tip = Vector(base), Vector(tip)
    direction = tip - base
    sideways = direction.cross(Vector((0, 0, 1))).normalized()
    if sideways.length < 0.1:
        sideways = Vector((1, 0, 0))
    # Narrow compound leaflets have a raised central vein and gently notched edges.
    widths = [0.02, 0.72, 1, 0.67, 0.46, 0]
    vertices, faces = [], []
    centers = []
    for i, w in enumerate(widths):
        t = i / (len(widths) - 1)
        center = base.lerp(tip, t) + Vector((0, 0, math.sin(math.pi * t) * width * 0.43))
        centers.append(center)
        for layer in (1, -1):
            ridge = Vector((0, 0, layer * 0.0018))
            vertices += [tuple(center - sideways * width * w + Vector((0, 0, -width * 0.16 * w)) + ridge),
                         tuple(center + ridge),
                         tuple(center + sideways * width * w + Vector((0, 0, -width * 0.16 * w)) + ridge)]
    for i in range(len(widths) - 1):
        for layer in (0, 3):
            a, b = i * 6 + layer, (i + 1) * 6 + layer
            for j in (0, 1):
                face = (a + j, b + j, b + j + 1, a + j + 1)
                faces.append(face if layer == 0 else tuple(reversed(face)))
        a, b = i * 6, (i + 1) * 6
        faces.extend([(a, a + 3, b + 3, b), (a + 2, b + 2, b + 5, a + 5)])
    obj = mesh(name, vertices, faces, "Art_Crop_LeafLight" if light else "Art_Crop_LeafGreen")
    vein = curve(name + "Vein", [tuple(centers[i] + Vector((0, 0, 0.0029))) for i in (0, 2, 4)],
                 0.00125, "Art_Crop_LeafVein", resolution=2, radii=[1, 0.8, 0.15])
    return [obj, vein]


def foliage():
    pieces = []
    for k in range(6):
        a = math.tau * k / 6 + 0.28
        forward = Vector((math.cos(a), math.sin(a), 0))
        sideways = Vector((-math.sin(a), math.cos(a), 0))
        reach = (0.26, 0.215, 0.255, 0.23, 0.25, 0.19)[k]
        height = (0.66, 0.77, 0.69, 0.74, 0.65, 0.8)[k]

        def point(t):
            return forward * reach * t ** 1.40 + Vector((0, 0, 0.105 + (height - 0.105) * (1 - (1 - t) ** 1.32)))

        pieces.append(curve("ArchedLeafStalk", [tuple(point(t)) for t in (0, 0.30, 0.65, 1)],
                            0.0068, "Art_Crop_LeafGreen", resolution=5, radii=[1.05, 0.8, 0.5, 0.08]))
        for j, t in enumerate((0.32, 0.46, 0.60, 0.73, 0.85)):
            base = point(t)
            scale = math.sin(math.pi * (t - 0.06))
            for side in (-1, 1):
                tip = base + sideways * side * 0.080 * scale + forward * (0.035 + 0.024 * scale) + Vector((0, 0, 0.038 * scale))
                pieces.extend(leaflet("PairedLeaflet", base, tip, 0.020 * scale, (k + j) % 3 == 0))
        pieces.extend(leaflet("TerminalLeaflet", point(0.88), point(1) + forward * 0.025,
                              0.020, k % 2 == 0))
    return pieces


def join(name, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    return obj


def main():
    global root
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1
    root = bpy.data.objects.new("CarrotPlant", None)
    bpy.context.collection.objects.link(root)
    root["ground_plane"] = "Z=0; lower orange taproot is intentionally buried"
    root["art_source"] = "Original procedural Blender model; Tools/Art/build_crop.py"
    body = join("RootCarrot", carrot())
    leaves = join("Leaves", foliage())
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    for obj in (root, body, leaves):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(output / "CarrotPlant.fbx"), use_selection=True,
                             object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
                             use_mesh_modifiers=True, mesh_smooth_type="FACE", use_custom_props=True)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in (body, leaves))
    assert tris < 8000, tris
    bounds = [o.matrix_world @ Vector(c) for o in (body, leaves) for c in o.bound_box]
    info = {"name": "CarrotPlant", "triangles": tris, "meshObjects": 2,
            "heightAboveGround": round(max(v.z for v in bounds), 3),
            "width": round(max(v.x for v in bounds) - min(v.x for v in bounds), 3)}
    (output / "Crops.palette.json").write_text(json.dumps({
        "materials": [{"name": name, "color": color, "metallic": 0, "smoothness": 0.24}
                      for name, color in palette.items()], "models": [info],
        "source": "ArtSource/CarrotPlant.blend", "generator": "Tools/Art/build_crop.py"
    }, indent=2), encoding="utf-8")
    root = None
    # A neutral studio is kept in the editable source, excluded from FBX export.
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=0.37, depth=0.12, location=(0, 0, -0.07))
    soil = bpy.context.object
    soil.name = "StudioSoilDisc_NotExported"
    soil_mat = bpy.data.materials.new("Studio soil")
    soil_mat.diffuse_color = (0.23, 0.13, 0.075, 1)
    soil_mat.use_nodes = True
    soil_shader = soil_mat.node_tree.nodes.get("Principled BSDF")
    soil_shader.inputs["Base Color"].default_value = soil_mat.diffuse_color
    soil_shader.inputs["Roughness"].default_value = 0.92
    soil.data.materials.append(soil_mat)
    mod = soil.modifiers.new("Soft edge", "BEVEL")
    mod.width, mod.segments = 0.025, 3
    for poly in soil.data.polygons:
        poly.use_smooth = True
    scene.world.color = (0.3, 0.3, 0.3)
    for loc, energy, size in (((-2, -3, 4), 350, 3), ((2, 0, 3), 230, 2)):
        bpy.ops.object.light_add(type="AREA", location=loc)
        light = bpy.context.object
        light.data.energy, light.data.size = energy, size
        light.rotation_euler = (Vector((0, 0, 0.3)) - light.location).to_track_quat("-Z", "Y").to_euler()
    bpy.ops.object.camera_add(location=(1.4, -2.4, 1.25))
    camera = bpy.context.object
    camera.rotation_euler = (Vector((0, 0, 0.35)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type, camera.data.ortho_scale = "ORTHO", 1.14
    scene.camera = camera
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 1000, 1000
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = "AgX"
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(source))
    if args.preview:
        scene.render.filepath = str(args.preview.resolve())
        bpy.ops.render.render(write_still=True)
    print("CARROT_MODEL_MANIFEST=" + json.dumps(info))


if __name__ == "__main__":
    main()
