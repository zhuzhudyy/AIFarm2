"""Original, texture-free storybook villagers for AIFarm.

Run with Blender 5.2:
    blender --background --factory-startup --python Tools/Art/build_residents.py
    blender --background --factory-startup --python Tools/Art/build_residents.py -- --preview PATH

All dimensions are metres. Blender characters face -Y, with feet on Z=0.
The FBXs use Unity's Y-up conversion and contain four named limb pivots.
No downloaded geometry, textures, fonts, or third-party assets are used.
"""

import argparse
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector


ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PARSER = argparse.ArgumentParser()
PARSER.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
PARSER.add_argument("--preview", type=Path)
OPTIONS = PARSER.parse_args(ARGS)
REPO = OPTIONS.repo.resolve()
EXPORT = REPO / "My project/Assets/AIFarm/Art/Models/Residents"
SOURCE = REPO / "ArtSource/Residents.blend"
EXPORT.mkdir(parents=True, exist_ok=True)
SOURCE.parent.mkdir(parents=True, exist_ok=True)

# Deliberately shared across residents so Unity can share material assets.
PALETTE = {
    "SkinPeach": (0.91, 0.63, 0.44, 1),
    "SkinHoney": (0.68, 0.40, 0.25, 1),
    "SkinRose": (0.97, 0.72, 0.57, 1),
    "Blush": (0.86, 0.30, 0.24, 1),
    "Eye": (0.055, 0.036, 0.040, 1),
    "Ivory": (0.96, 0.88, 0.70, 1),
    "White": (1.0, 0.985, 0.91, 1),
    "HairChestnut": (0.23, 0.105, 0.053, 1),
    "HairInk": (0.082, 0.077, 0.12, 1),
    "HairCopper": (0.49, 0.205, 0.070, 1),
    "HairHighlight": (0.37, 0.185, 0.083, 1),
    "Mint": (0.42, 0.70, 0.54, 1),
    "Pine": (0.16, 0.36, 0.25, 1),
    "MintLight": (0.66, 0.84, 0.65, 1),
    "Straw": (0.84, 0.60, 0.28, 1),
    "StrawLight": (0.97, 0.77, 0.40, 1),
    "Terracotta": (0.69, 0.28, 0.16, 1),
    "ClayLight": (0.84, 0.43, 0.26, 1),
    "Mustard": (0.92, 0.62, 0.18, 1),
    "MustardLight": (0.99, 0.81, 0.34, 1),
    "Indigo": (0.20, 0.28, 0.43, 1),
    "IndigoLight": (0.36, 0.47, 0.63, 1),
    "Lavender": (0.65, 0.59, 0.73, 1),
    "Leather": (0.30, 0.17, 0.11, 1),
    "LeatherLight": (0.47, 0.29, 0.15, 1),
    "Sole": (0.13, 0.11, 0.105, 1),
    "Brass": (0.88, 0.65, 0.30, 1),
    "Iron": (0.28, 0.32, 0.31, 1),
    "WaterCan": (0.32, 0.56, 0.57, 1),
    "Petal": (0.97, 0.70, 0.58, 1),
}
MATERIALS = {}
CURRENT_ROOT = None
CURRENT_NAME = ""


def material(key):
    if key not in MATERIALS:
        mat = bpy.data.materials.new("Art_Char_" + key)
        mat.diffuse_color = PALETTE[key]
        mat.use_nodes = True
        shader = mat.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = PALETTE[key]
        shader.inputs["Roughness"].default_value = 0.77 if key != "Eye" else 0.32
        if key in ("Brass", "Iron"):
            shader.inputs["Metallic"].default_value = 0.25
            shader.inputs["Roughness"].default_value = 0.52
        MATERIALS[key] = mat
    return MATERIALS[key]


def finish(obj, name, mat, parent=None, smooth=True):
    obj.name = CURRENT_NAME + "_" + name
    if mat:
        obj.data.materials.append(material(mat))
    if obj.type == "MESH" and smooth:
        for poly in obj.data.polygons:
            poly.use_smooth = True
    if parent is None:
        parent = CURRENT_ROOT
    if parent:
        matrix = obj.matrix_world.copy()
        obj.parent = parent
        obj.matrix_world = matrix
    return obj


def applied(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


def ellipsoid(name, pos, scale, mat, parent=None, rot=None, segments=20, rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=pos)
    obj = bpy.context.object
    obj.scale = scale
    if rot:
        obj.rotation_euler = rot
    applied(obj)
    return finish(obj, name, mat, parent)


def rounded_box(name, pos, size, mat, radius=0.045, parent=None, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = bpy.context.object
    obj.scale = size
    if rot:
        obj.rotation_euler = rot
    applied(obj)
    mod = obj.modifiers.new("Soft hand-shaped edges", "BEVEL")
    mod.width = radius
    mod.segments = 3
    bpy.ops.object.modifier_apply(modifier=mod.name)
    mod = obj.modifiers.new("Weighted corner normals", "WEIGHTED_NORMAL")
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(obj, name, mat, parent)


def tube(name, points, radius, mat, parent=None, closed=False, radii=None):
    curve = bpy.data.curves.new(CURRENT_NAME + "_" + name, "CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 6
    curve.bevel_depth = radius
    curve.bevel_resolution = 2
    spline = curve.splines.new("POLY" if closed else "BEZIER")
    if closed:
        spline.points.add(len(points) - 1)
        for p, co in zip(spline.points, points):
            p.co = (*co, 1)
    else:
        spline.bezier_points.add(len(points) - 1)
        for i, (p, co) in enumerate(zip(spline.bezier_points, points)):
            p.co = co
            p.handle_left_type = "AUTO"
            p.handle_right_type = "AUTO"
            if radii:
                p.radius = radii[i]
    spline.use_cyclic_u = closed
    obj = bpy.data.objects.new(CURRENT_NAME + "_" + name, curve)
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target="MESH")
    return finish(bpy.context.object, name, mat, parent)


def ring(name, center, rx, ry, radius, mat, plane="XY", parent=None, n=32):
    x, y, z = center
    points = []
    for i in range(n):
        a = math.tau * i / n
        if plane == "XY":
            points.append((x + rx * math.cos(a), y + ry * math.sin(a), z))
        elif plane == "XZ":
            points.append((x + rx * math.cos(a), y, z + ry * math.sin(a)))
        else:
            points.append((x, y + rx * math.cos(a), z + ry * math.sin(a)))
    return tube(name, points, radius, mat, parent, closed=True)


def profile_mesh(name, profile, mat, n=32, center=(0, 0, 0), ripple=0):
    """Closed elliptical lathe: profile contains (z, x radius, y radius)."""
    cx, cy, cz = center
    vertices = []
    for z, rx, ry in profile:
        for i in range(n):
            a = math.tau * i / n
            pleat = 1 + ripple * math.cos(a * 10)
            vertices.append((cx + rx * math.cos(a) * pleat,
                             cy + ry * math.sin(a) * pleat, cz + z))
    faces = []
    for row in range(len(profile) - 1):
        for i in range(n):
            j = (i + 1) % n
            faces.append((row * n + i, row * n + j,
                          (row + 1) * n + j, (row + 1) * n + i))
    faces.extend([tuple(reversed(range(n))), tuple((len(profile) - 1) * n + i for i in range(n))])
    mesh = bpy.data.meshes.new(CURRENT_NAME + "_" + name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(CURRENT_NAME + "_" + name, mesh)
    bpy.context.collection.objects.link(obj)
    return finish(obj, name, mat)


def pivot(name, pos):
    obj = bpy.data.objects.new(CURRENT_NAME + "_" + name, None)
    bpy.context.collection.objects.link(obj)
    obj.parent = CURRENT_ROOT
    obj.location = pos
    obj["unity_limb_pivot"] = name
    return obj


def button(name, pos, mat="Brass", radius=0.028, parent=None):
    return ellipsoid(name, pos, (radius, 0.017, radius), mat, parent, segments=16, rings=8)


def make_face(skin, hair, broad=False, glasses=False, moustache=False):
    width = 0.405 if broad else 0.39
    ellipsoid("Head", (0, 0, 1.765), (width, 0.325, 0.425), skin, segments=32, rings=20)
    ellipsoid("Neck", (0, 0, 1.405), (0.135, 0.13, 0.145), skin)
    for side in (-1, 1):
        ellipsoid("Ear", (side * (width - 0.004), -0.008, 1.75), (0.082, 0.085, 0.12), skin)
        ellipsoid("EarInset", (side * (width + 0.024), -0.073, 1.75), (0.035, 0.02, 0.064), "Blush")
        x = side * 0.142
        # The entire face projects toward Blender -Y (Unity forward after import).
        ellipsoid("Eye", (x, -0.309, 1.798), (0.052, 0.027, 0.07), "Eye")
        ellipsoid("EyeShine", (x - 0.012, -0.334, 1.82), (0.016, 0.009, 0.021), "White", segments=12, rings=8)
        ellipsoid("EyeShineSmall", (x + 0.013, -0.334, 1.773), (0.007, 0.006, 0.009), "White", segments=12, rings=8)
        ellipsoid("Cheek", (side * 0.24, -0.266, 1.69), (0.068, 0.015, 0.041), "Blush")
        tube("Eyebrow", [(x - 0.05, -0.288, 1.90), (x, -0.303, 1.914), (x + 0.048, -0.29, 1.902)], 0.015, hair, radii=[0.35, 1, 0.35])
        if not broad:
            # Three tiny freckles per cheek remain visible at close portrait distance.
            for j in range(3):
                button("Freckle", (side * (0.21 + j * 0.023), -0.284 + j * 0.012, 1.696 + (j % 2) * 0.014), "HairCopper", 0.005)
    ellipsoid("Nose", (0, -0.341, 1.73), (0.060, 0.066, 0.048), skin)
    tube("Smile", [(-0.077, -0.29, 1.642), (-0.035, -0.316, 1.62), (0.019, -0.318, 1.615), (0.077, -0.29, 1.646)], 0.009, "HairChestnut", radii=[0.5, 1, 1, 0.5])
    if moustache:
        for side in (-1, 1):
            tube("Moustache", [(0, -0.37, 1.681), (side * 0.049, -0.359, 1.663), (side * 0.102, -0.321, 1.674)], 0.028, hair, radii=[0.75, 1, 0.25])
    if glasses:
        for side in (-1, 1):
            ring("RoundSpectacle", (side * 0.147, -0.35, 1.80), 0.102, 0.096, 0.014, "Brass", "XZ", n=24)
            tube("GlassesTemple", [(side * 0.249, -0.348, 1.81), (side * 0.36, -0.22, 1.82), (side * 0.407, -0.002, 1.815)], 0.012, "Brass")
        tube("SpectacleBridge", [(-0.045, -0.35, 1.81), (0, -0.378, 1.825), (0.045, -0.35, 1.81)], 0.012, "Brass")


def hair_cap(mat, style):
    # Explicit sculpted cap leaves a clean facial oval, wraps around the back,
    # and has a deliberately irregular hairline instead of a complete sphere.
    n, rows = 40, 10
    vertices = [(0, 0, 2.225)]
    for row in range(1, rows + 1):
        t = row / rows
        for i in range(n):
            a = math.tau * i / n
            front = max(0.0, -math.sin(a))
            max_phi = 1.95 - front * 0.86 + 0.06 * math.sin(a * 5)
            phi = t * max_phi
            vertices.append((0.431 * math.sin(phi) * math.cos(a),
                             0.367 * math.sin(phi) * math.sin(a),
                             1.774 + 0.451 * math.cos(phi)))
    faces = []
    for i in range(n):
        faces.append((0, 1 + i, 1 + (i + 1) % n))
    for row in range(rows - 1):
        a = 1 + row * n
        b = a + n
        for i in range(n):
            j = (i + 1) % n
            faces.append((a + i, b + i, b + j, a + j))
    mesh = bpy.data.meshes.new(CURRENT_NAME + "_HairCap")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(CURRENT_NAME + "_HairCap", mesh)
    bpy.context.collection.objects.link(obj)
    finish(obj, "SculptedHairCap", mat)
    # Thick tapering locks form the hair volume and handmade silhouette.
    if style == "swept":
        locks = [((-0.25, -0.12, 2.10), (-0.20, -0.30, 2.03), (-0.30, -0.28, 1.88)),
                 ((0.13, -0.11, 2.18), (0.03, -0.29, 2.09), (-0.14, -0.32, 1.98)),
                 ((0.29, -0.02, 2.13), (0.25, -0.25, 2.04), (0.14, -0.32, 1.96))]
    else:
        locks = [((-0.25, -0.13, 2.08), (-0.24, -0.28, 1.995), (-0.31, -0.25, 1.91)),
                 ((-0.07, -0.17, 2.15), (-0.10, -0.32, 2.04), (-0.15, -0.33, 1.972)),
                 ((0.14, -0.13, 2.13), (0.13, -0.31, 2.04), (0.025, -0.337, 1.966)),
                 ((0.28, -0.08, 2.08), (0.28, -0.25, 1.98), (0.31, -0.22, 1.89))]
    for i, points in enumerate(locks):
        tube("FringeLock", points, 0.072, mat, radii=[0.65, 1, 0.05])
    for side in (-1, 1):
        tube("SideLock", [(side * 0.34, 0.02, 1.99), (side * 0.394, -0.026, 1.84), (side * 0.354, -0.023, 1.67)], 0.057, mat, radii=[1, 1, 0.1])


def legs(trouser, boot="Leather", shorts=False):
    for side, label in ((-1, "Left"), (1, "Right")):
        x = side * 0.163
        group = pivot(label + "Leg", (x, 0.014, 0.87))
        ellipsoid(label + "TrouserLeg", (x, 0.02, 0.58), (0.137, 0.149, 0.317), trouser, group)
        if shorts:
            ellipsoid(label + "Sock", (x, -0.004, 0.36), (0.109, 0.111, 0.196), "Ivory", group)
            ring(label + "SockRib", (x, 0, 0.43), 0.113, 0.11, 0.016, "MustardLight", parent=group, n=20)
        rounded_box(label + "BootSole", (x, -0.063, 0.052), (0.282, 0.391, 0.101), "Sole", 0.047, group)
        ellipsoid(label + "BootToe", (x, -0.081, 0.145), (0.145, 0.207, 0.118), boot, group)
        rounded_box(label + "BootShaft", (x, 0.016, 0.207), (0.254, 0.254, 0.256), boot, 0.071, group)
        ring(label + "BootCuff", (x, 0.019, 0.309), 0.128, 0.126, 0.021, "LeatherLight", parent=group, n=24)
        tube(label + "ToeStitch", [(x - 0.092, -0.212, 0.172), (x, -0.257, 0.177), (x + 0.092, -0.212, 0.172)], 0.006, "Straw", group)


def arms(sleeve, skin, cuff="Ivory"):
    result = {}
    for side, label in ((-1, "Left"), (1, "Right")):
        group = pivot(label + "Arm", (side * 0.292, 0, 1.285))
        result[label] = group
        # Separate sleeve, forearm, cuff and thumb define a human silhouette.
        ellipsoid(label + "Sleeve", (side * 0.339, 0, 1.178), (0.131, 0.151, 0.214), sleeve, group, rot=(0, side * -0.20, 0))
        ellipsoid(label + "Forearm", (side * 0.389, -0.012, 1.005), (0.089, 0.104, 0.16), skin, group, rot=(0, side * -0.12, 0))
        ring(label + "RolledCuff", (side * 0.377, -0.003, 1.06), 0.109, 0.115, 0.028, cuff, parent=group, n=24)
        ellipsoid(label + "Hand", (side * 0.40, -0.026, 0.883), (0.088, 0.077, 0.108), skin, group)
        ellipsoid(label + "Thumb", (side * 0.332, -0.063, 0.914), (0.044, 0.039, 0.07), skin, group, rot=(0, side * 0.38, 0))
    return result


def torso(mat):
    profile_mesh("TailoredTorso", [(0.775, 0.225, 0.178), (0.80, 0.252, 0.19),
                 (0.96, 0.249, 0.19), (1.16, 0.275, 0.195),
                 (1.29, 0.313, 0.195), (1.36, 0.24, 0.165), (1.39, 0.125, 0.125)], mat)


def collar(mat="Ivory"):
    for side in (-1, 1):
        rounded_box("FoldedCollar", (side * 0.092, -0.155, 1.33), (0.15, 0.053, 0.137), mat, 0.022, rot=(0, side * -0.42, 0))


def seam(name, points, mat="Ivory", radius=0.005):
    return tube(name, points, radius, mat)


def satchel(side, mat, strap):
    x = side * 0.295
    tube("CrossbodyStrap", [(-side * 0.205, -0.131, 1.35), (-side * 0.1, -0.209, 1.19), (side * 0.10, -0.223, 0.96), (x, -0.04, 0.816)], 0.028, strap)
    rounded_box("SatchelBody", (x, -0.012, 0.778), (0.26, 0.18, 0.25), mat, 0.047)
    rounded_box("SatchelFlap", (x, -0.115, 0.83), (0.258, 0.042, 0.146), mat, 0.025)
    rounded_box("SatchelClasp", (x, -0.144, 0.79), (0.039, 0.021, 0.052), "Brass", 0.009)
    seam("SatchelStitch", [(x - 0.10, -0.140, 0.87), (x - 0.10, -0.141, 0.77), (x + 0.10, -0.141, 0.77), (x + 0.10, -0.140, 0.87)], "Straw", 0.005)


def straw_hat():
    # Continuous curved brim with an upturned edge and a woven crown.
    profile_mesh("StrawHatBrim", [(2.055, 0.37, 0.31), (2.04, 0.49, 0.414),
                 (2.065, 0.603, 0.498), (2.088, 0.621, 0.511),
                 (2.108, 0.604, 0.50), (2.09, 0.482, 0.407), (2.105, 0.345, 0.30)], "Straw")
    profile_mesh("StrawHatCrown", [(2.095, 0.343, 0.295), (2.17, 0.343, 0.295),
                 (2.285, 0.286, 0.248), (2.327, 0.243, 0.205), (2.34, 0.18, 0.157)], "StrawLight")
    profile_mesh("HatRibbon", [(2.105, 0.349, 0.302), (2.158, 0.349, 0.302), (2.17, 0.342, 0.294)], "Pine")
    for i, (r, h) in enumerate(((0.39, 2.093), (0.45, 2.084), (0.51, 2.089), (0.57, 2.103), (0.611, 2.112))):
        ring("WovenBrimLine", (0, 0, h), r, r * 0.825, 0.0065, "StrawLight", n=40)
    for i in range(16):
        a = math.tau * i / 16
        tube("CrownWeave", [(0.345 * math.cos(a), 0.297 * math.sin(a), 2.173),
                           (0.286 * math.cos(a), 0.249 * math.sin(a), 2.283),
                           (0.211 * math.cos(a), 0.182 * math.sin(a), 2.337)], 0.004, "Straw")
    # Small peach blossom tucked into the hatband.
    for i in range(5):
        a = math.tau * i / 5
        ellipsoid("HatFlowerPetal", (-0.295 + math.cos(a) * 0.05, -0.216, 2.159 + math.sin(a) * 0.05), (0.04, 0.017, 0.047), "Petal", rot=(0, -a, 0), segments=16, rings=8)
    button("HatFlowerHeart", (-0.295, -0.24, 2.159), "Mustard", 0.025)


def make_yaya():
    legs("Pine")
    torso("Ivory")
    arms("MintLight", "SkinPeach", "Ivory")
    collar()
    # Overalls have a shaped bib, back panel and independent shoulder straps.
    profile_mesh("OverallsWaist", [(0.77, 0.248, 0.203), (0.90, 0.270, 0.214), (1.035, 0.272, 0.218)], "Pine")
    rounded_box("OverallsBib", (0, -0.198, 1.111), (0.353, 0.049, 0.273), "Pine", 0.034)
    for side in (-1, 1):
        tube("OverallShoulderStrap", [(side * 0.15, -0.232, 1.19), (side * 0.183, -0.18, 1.34), (side * 0.195, 0.01, 1.376), (side * 0.176, 0.176, 1.22)], 0.034, "Mint")
        button("OverallButton", (side * 0.146, -0.233, 1.20))
    rounded_box("BibPocket", (0, -0.23, 1.096), (0.176, 0.026, 0.126), "Mint", 0.024)
    seam("PocketHem", [(-0.07, -0.247, 1.142), (0, -0.249, 1.14), (0.07, -0.247, 1.142)], "MintLight")
    # Tiny leaf embroidery is geometry, visible without a texture.
    tube("PocketStem", [(0, -0.25, 1.061), (0.006, -0.251, 1.108)], 0.005, "Ivory")
    ellipsoid("EmbroideredLeaf", (-0.015, -0.252, 1.092), (0.025, 0.006, 0.012), "Ivory", rot=(0, 0.45, 0), segments=12, rings=8)
    ellipsoid("EmbroideredLeaf", (0.02, -0.252, 1.105), (0.021, 0.006, 0.011), "Ivory", rot=(0, -0.45, 0), segments=12, rings=8)
    make_face("SkinPeach", "HairChestnut")
    hair_cap("HairChestnut", "soft")
    for side in (-1, 1):
        ellipsoid("ShortPigtail", (side * 0.336, 0.201, 1.603), (0.117, 0.15, 0.18), "HairChestnut", rot=(0, side * 0.40, 0))
        ellipsoid("PigtailRibbon", (side * 0.338, 0.105, 1.68), (0.061, 0.042, 0.034), "Mint")
    # The straw hat replaces the crown silhouette; tuck hidden upper locks under
    # its brim so no brown hair fragments emerge through the woven surface.
    for obj in CURRENT_ROOT.children:
        if obj.type == "MESH" and any(part in obj.name for part in ("HairCap", "FringeLock", "SideLock")):
            for vertex in obj.data.vertices:
                vertex.co.z = min(vertex.co.z, 2.043)
    straw_hat()


def make_amu():
    legs("Indigo")
    torso("MintLight")
    arms("MintLight", "SkinHoney", "Ivory")
    collar()
    # Tapered leather apron sits over the shirt and wraps at the hips.
    profile_mesh("ApronWrap", [(0.74, 0.28, 0.211), (0.83, 0.279, 0.214), (1.02, 0.251, 0.21)], "Terracotta")
    rounded_box("ApronBib", (0, -0.201, 1.112), (0.343, 0.048, 0.26), "Terracotta", 0.03)
    tube("ApronNeckLoop", [(-0.15, -0.227, 1.21), (-0.152, -0.159, 1.36), (0, 0.015, 1.434), (0.152, -0.159, 1.36), (0.15, -0.227, 1.21)], 0.026, "Leather")
    for side in (-1, 1):
        button("ApronRivet", (side * 0.143, -0.233, 1.212), radius=0.018)
    rounded_box("ApronPocket", (-0.016, -0.226, 0.94), (0.308, 0.047, 0.153), "ClayLight", 0.023)
    seam("ApronPocketHem", [(-0.153, -0.253, 1.002), (-0.02, -0.255, 1.002), (0.123, -0.253, 1.002)], "Straw")
    seam("ApronPocketDivider", [(0.025, -0.254, 0.89), (0.025, -0.254, 0.998)], "Straw")
    rounded_box("ToolBelt", (0, 0, 0.87), (0.554, 0.447, 0.066), "Leather", 0.05)
    rounded_box("ToolBeltBuckle", (0.04, -0.244, 0.872), (0.079, 0.026, 0.07), "Brass", 0.008)
    rounded_box("BuckleInset", (0.04, -0.260, 0.872), (0.046, 0.012, 0.039), "Leather", 0.004)
    # Carpenter's wooden mallet tucked into the hip loop.
    rounded_box("MalletHandle", (-0.30, -0.019, 0.75), (0.046, 0.049, 0.33), "LeatherLight", 0.014, rot=(0, -0.19, 0))
    rounded_box("MalletHead", (-0.33, -0.019, 0.927), (0.19, 0.106, 0.115), "Straw", 0.022)
    ring("MalletLoop", (-0.286, -0.013, 0.86), 0.066, 0.067, 0.016, "Leather", n=20)
    make_face("SkinHoney", "HairChestnut", broad=True, moustache=True)
    hair_cap("HairChestnut", "swept")
    # Open face, thick swept locks, sideburns and an ear pencil distinguish him.
    for side in (-1, 1):
        tube("Sideburn", [(side * 0.375, 0, 1.92), (side * 0.397, -0.025, 1.77), (side * 0.351, -0.046, 1.65)], 0.052, "HairChestnut", radii=[1, 0.7, 0.12])
    tube("Pencil", [(0.394, 0.002, 1.73), (0.415, 0.053, 1.97)], 0.018, "Mustard")
    ellipsoid("PencilEraser", (0.415, 0.053, 1.97), (0.021, 0.021, 0.034), "Petal", segments=12, rings=8)
    # Two subtle warm strokes on the front hair mass.
    tube("HairContour", [(0.22, -0.235, 2.087), (0.09, -0.305, 2.045), (0.015, -0.32, 2.004)], 0.008, "HairHighlight", radii=[0.1, 1, 0.1])


def make_xiaosui():
    legs("Mustard", shorts=True)
    torso("Ivory")
    arms("Ivory", "SkinRose", "MustardLight")
    collar("MustardLight")
    profile_mesh("PleatedDress", [(0.55, 0.353, 0.249), (0.575, 0.374, 0.266),
                 (0.63, 0.367, 0.258), (0.82, 0.309, 0.222),
                 (1.06, 0.280, 0.218), (1.22, 0.301, 0.222), (1.285, 0.292, 0.215)], "Mustard", n=60, ripple=0.025)
    ring("DressHemPiping", (0, 0, 0.584), 0.371, 0.262, 0.016, "MustardLight", n=40)
    for side in (-1, 1):
        tube("PinaforeStrap", [(side * 0.15, -0.196, 1.205), (side * 0.19, -0.136, 1.355), (side * 0.19, 0.08, 1.368), (side * 0.17, 0.182, 1.18)], 0.039, "Mustard")
        button("DressButton", (side * 0.15, -0.211, 1.22), "Ivory", 0.025)
    rounded_box("DressPocket", (-0.126, -0.224, 0.93), (0.15, 0.029, 0.127), "MustardLight", 0.027)
    ring("WaistRibbon", (0, 0, 1.012), 0.295, 0.231, 0.018, "Ivory", n=32)
    for side in (-1, 1):
        ellipsoid("WaistBow", (0.287 + side * 0.027, -0.088, 1.018), (0.046, 0.038, 0.028), "Ivory", rot=(0, side * 0.4, 0), segments=16, rings=8)
    satchel(1, "LeatherLight", "Leather")
    make_face("SkinRose", "HairCopper")
    hair_cap("HairCopper", "soft")
    # Alternating overlapping lobes form thick braids, finished with fabric bows.
    for side in (-1, 1):
        for i in range(6):
            z = 1.73 - i * 0.071
            x = side * (0.365 + 0.025 * math.sin(i * 1.1))
            y = 0.095 - i * 0.017
            ellipsoid("BraidedLock", (x + side * (0.022 if i % 2 else -0.022), y, z), (0.073 - i * 0.004, 0.074 - i * 0.005, 0.080), "HairCopper", rot=(0, side * (0.3 if i % 2 else -0.3), 0), segments=16, rings=10)
        for s in (-1, 1):
            ellipsoid("BraidBowLoop", (side * 0.355 + s * 0.039, -0.026, 1.343), (0.053, 0.034, 0.028), "Mint", rot=(0, s * 0.38, 0), segments=16, rings=8)
        button("BraidBowKnot", (side * 0.355, -0.057, 1.343), "Pine", 0.023)
    # Fabric headband follows the scalp, topped with a tiny leaf-shaped tie.
    tube("Headband", [(-0.405, 0.02, 1.90), (-0.315, -0.017, 2.10), (0, 0.012, 2.215), (0.315, -0.017, 2.10), (0.405, 0.02, 1.90)], 0.024, "Mint")
    ellipsoid("HeadbandTie", (0.22, -0.02, 2.189), (0.097, 0.055, 0.035), "Mint", rot=(0, -0.5, 0), segments=16, rings=8)


def make_momo():
    legs("Indigo", boot="LeatherLight")
    torso("Indigo")
    arms("Indigo", "SkinPeach", "IndigoLight")
    collar("Ivory")
    # Coat opening, hem, pocket and brass buttons give the bookish outfit depth.
    profile_mesh("CoatHem", [(0.746, 0.27, 0.209), (0.77, 0.28, 0.212), (0.95, 0.265, 0.207)], "Indigo")
    seam("CoatPlacket", [(0, -0.211, 0.768), (0, -0.216, 1.08), (0, -0.201, 1.28)], "IndigoLight", 0.012)
    for z in (0.86, 1.0, 1.135):
        button("CoatButton", (-0.049, -0.222, z), "Brass", 0.017)
    rounded_box("CoatPocket", (-0.163, -0.186, 0.94), (0.131, 0.031, 0.094), "IndigoLight", 0.022)
    # Soft scarf knot and two hanging tapered folds.
    ring("ScarfWrap", (0, 0, 1.363), 0.18, 0.16, 0.045, "Lavender", n=24)
    ellipsoid("ScarfKnot", (0.044, -0.177, 1.347), (0.061, 0.047, 0.054), "Lavender")
    tube("ScarfTail", [(0.066, -0.188, 1.345), (0.089, -0.231, 1.22), (0.108, -0.236, 1.132)], 0.044, "Lavender", radii=[0.8, 1, 0.6])
    satchel(1, "Leather", "LeatherLight")
    # A book protrudes from the side satchel: pages, cover, raised spine and band.
    rounded_box("BookPages", (0.311, 0.013, 0.934), (0.164, 0.076, 0.215), "Ivory", 0.009, rot=(0, -0.13, 0))
    for y in (-0.033, 0.057):
        rounded_box("BookCover", (0.311, y, 0.934), (0.187, 0.018, 0.23), "Terracotta", 0.007, rot=(0, -0.13, 0))
    rounded_box("BookSpine", (0.225, 0.013, 0.924), (0.025, 0.10, 0.23), "Terracotta", 0.009, rot=(0, -0.13, 0))
    make_face("SkinPeach", "HairInk", glasses=True)
    hair_cap("HairInk", "swept")
    # Beret is flattened, asymmetric, with a rolled band and a small stem.
    ellipsoid("SlouchedBeret", (-0.059, 0.017, 2.177), (0.438, 0.347, 0.167), "Indigo", rot=(0, -0.13, -0.06), segments=32, rings=16)
    ring("BeretBand", (0, 0.009, 2.097), 0.371, 0.312, 0.028, "IndigoLight", n=32)
    tube("BeretStem", [(-0.10, 0.018, 2.312), (-0.115, 0.021, 2.36), (-0.094, 0.024, 2.384)], 0.028, "Indigo", radii=[1, 0.8, 0.55])
    button("BeretPin", (0.24, -0.256, 2.146), "Brass", 0.028)


def descendants(root):
    return [root] + list(root.children_recursive)


def consolidate_meshes(root):
    """Five renderers per resident, keeping the four movable limb groups."""
    groups = [root] + [obj for obj in root.children if "unity_limb_pivot" in obj]
    for group in groups:
        meshes = [obj for obj in group.children if obj.type == "MESH"]
        if not meshes:
            continue
        bpy.ops.object.select_all(action="DESELECT")
        for obj in meshes:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.join()
        obj = bpy.context.object
        obj.name = root.name + "_" + (group.get("unity_limb_pivot", "Body")) + "Mesh"
        # Body and limb mesh origins coincide with their owning pivot.
        bpy.context.scene.cursor.location = group.matrix_world.translation
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")


def export_resident(root):
    objects = descendants(root)
    renamed = []
    for obj in objects:
        if "unity_limb_pivot" in obj:
            renamed.append((obj, obj.name))
            obj.name = obj["unity_limb_pivot"]
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = EXPORT / (root.name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
                             object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y",
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             bake_space_transform=False, add_leaf_bones=False,
                             bake_anim=False, use_mesh_modifiers=True,
                             mesh_smooth_type="FACE", use_custom_props=True,
                             path_mode="AUTO")
    for obj, previous in renamed:
        obj.name = previous
    meshes = [o for o in objects if o.type == "MESH"]
    triangles = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
    return {"name": root.name, "meshObjects": len(meshes), "triangles": triangles,
            "heightMeters": round(max((o.matrix_world @ Vector(c)).z for o in meshes for c in o.bound_box), 3),
            "limbPivots": ["LeftLeg", "RightLeg", "LeftArm", "RightArm"],
            "file": str(path.relative_to(REPO)).replace("\\", "/")}


def setup_lineup(roots):
    for i, root in enumerate(roots):
        root.location = ((i - 1.5) * 1.54, 0, 0)
        root.rotation_euler.z = (-0.035, 0.025, -0.025, 0.035)[i]
    global CURRENT_ROOT, CURRENT_NAME
    CURRENT_ROOT, CURRENT_NAME = None, "Studio"
    rounded_box("DisplayPlinth", (0, 0, -0.11), (6.7, 1.55, 0.22), "Ivory", 0.18)
    world = bpy.data.worlds.new("Warm linen studio")
    bpy.context.scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.53, 0.65, 0.65, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.42
    for name, position, power, size, color in [
            ("Large warm key", (-4, -5, 7), 1150, 5, (1, 0.87, 0.69)),
            ("Cool soft fill", (5, -2, 4), 850, 4, (0.76, 0.88, 1)),
            ("Golden rim", (1, 4, 6), 1250, 3, (1, 0.8, 0.52))]:
        bpy.ops.object.light_add(type="AREA", location=position)
        light = bpy.context.object
        light.name = name
        light.data.energy = power
        light.data.shape = "DISK"
        light.data.size = size
        light.data.color = color
        light.rotation_euler = (Vector((0, 0, 1.2)) - light.location).to_track_quat("-Z", "Y").to_euler()
    bpy.ops.object.camera_add(location=(3.2, -12, 5.2))
    camera = bpy.context.object
    camera.name = "Villager lineup camera"
    camera.rotation_euler = (Vector((0, 0, 1.18)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 7.0
    bpy.context.scene.camera = camera
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1800
    scene.render.resolution_y = 980
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX"
    for area in bpy.context.screen.areas if bpy.context.screen else []:
        if area.type == "VIEW_3D":
            area.spaces.active.region_3d.view_perspective = "CAMERA"


def main():
    global CURRENT_ROOT, CURRENT_NAME
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    roots, manifest = [], []
    for name, builder in (("Yaya", make_yaya), ("Amu", make_amu),
                          ("Xiaosui", make_xiaosui), ("Momo", make_momo)):
        CURRENT_NAME = name
        CURRENT_ROOT = bpy.data.objects.new(name, None)
        bpy.context.collection.objects.link(CURRENT_ROOT)
        CURRENT_ROOT["art_source"] = "Original procedural Blender model; Tools/Art/build_residents.py"
        CURRENT_ROOT["forward"] = "Blender -Y / Unity forward"
        builder()
        bpy.context.view_layer.update()
        consolidate_meshes(CURRENT_ROOT)
        manifest.append(export_resident(CURRENT_ROOT))
        roots.append(CURRENT_ROOT)
    palette_document = {"materials": [{"name": "Art_Char_" + name, "color": list(value),
                                      "metallic": 0.25 if name in ("Brass", "Iron") else 0,
                                      "smoothness": 0.68 if name == "Eye" else 0.23}
                                     for name, value in PALETTE.items()],
                        "models": manifest,
                        "source": "ArtSource/Residents.blend",
                        "generator": "Tools/Art/build_residents.py"}
    (EXPORT / "Residents.palette.json").write_text(json.dumps(palette_document, ensure_ascii=False, indent=2), encoding="utf-8")
    setup_lineup(roots)
    # Avoid local .blend1 backups on reproducible regeneration.
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
    if OPTIONS.preview:
        bpy.context.scene.render.filepath = str(OPTIONS.preview.resolve())
        bpy.ops.render.render(write_still=True)
    print("RESIDENT_MODEL_MANIFEST=" + json.dumps(manifest))


if __name__ == "__main__":
    main()
