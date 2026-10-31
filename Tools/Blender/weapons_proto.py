"""Armas protótipo dos estilos de espada: foice (Reaper), montante (Greatsword) e adaga (Daggers, uma em cada mão).

Uso:
  "C:/Program Files/Blender Foundation/Blender 5.1/blender.exe" -b --factory-startup --python Tools/Blender/weapons_proto.py

Convenção igual à das espadas (swords_proto.py): origem na mão, cabo/lâmina em +Z, dois material slots
(Blade, Hilt). No Unity (FBX, Y para cima) o +Z vira +Y local e o -X do Blender vira +X: a lâmina da
foice sai para -X aqui para, no Unity, apontar para +X (o lado do gume, que lidera o golpe).
Escreve Assets/KonoSuba/Models/Weapons/Weapons.fbx e Logs/cap/weapons_preview.png.
"""
import math
from pathlib import Path

import bpy
import bmesh

ROOT = Path(__file__).resolve().parents[2]
FBX = ROOT / "Assets/KonoSuba/Models/Weapons/Weapons.fbx"
PREVIEW = ROOT / "Logs/cap/weapons_preview.png"


def material(name, color, metallic, roughness):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return mat


def mesh_object(name, verts, faces, mats, face_mats):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    for m in mats:
        mesh.materials.append(m)
    for poly, mi in zip(mesh.polygons, face_mats):
        poly.material_index = mi
        poly.use_smooth = False
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


class Builder:
    """Junta peças numa malha só, com o material de cada face."""

    def __init__(self):
        self.verts, self.faces, self.mats = [], [], []

    def add(self, verts, faces, mat):
        base = len(self.verts)
        self.verts += verts
        self.faces += [tuple(base + i for i in f) for f in faces]
        self.mats += [mat] * len(faces)

    def cylinder(self, r, z0, z1, mat, sides=8, r1=None):
        r1 = r if r1 is None else r1
        v = []
        for z, rr in ((z0, r), (z1, r1)):
            for i in range(sides):
                a = i * math.tau / sides
                v.append((rr * math.cos(a), rr * math.sin(a), z))
        f = [tuple(reversed(range(sides))), tuple(range(sides, 2 * sides))]
        for i in range(sides):
            j = (i + 1) % sides
            f.append((i, j, sides + j, sides + i))
        self.add(v, f, mat)

    def box(self, cx, cy, cz, sx, sy, sz, mat):
        hx, hy, hz = sx / 2, sy / 2, sz / 2
        v = [(cx + x, cy + y, cz + z) for x in (-hx, hx) for y in (-hy, hy) for z in (-hz, hz)]
        f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        self.add(v, f, mat)


def scythe(blade, hilt):
    b = Builder()
    # Cabo comprido (a mão fica a um terço de baixo), com um anel de metal onde encaixa a lâmina.
    b.cylinder(0.022, -0.55, 1.28, 1, sides=8)
    b.cylinder(0.03, -0.6, -0.55, 1, sides=8)
    b.cylinder(0.034, 1.16, 1.3, 0, sides=8)
    b.cylinder(0.028, 0.32, 0.4, 1, sides=8, r1=0.03)  # pega da segunda mão
    # Lâmina em crescente: lombo em cima, gume em baixo (secção em triângulo), a cair para a ponta.
    n = 16
    verts, faces = [], []
    for i in range(n + 1):
        t = i / n
        x = -0.04 - 0.76 * t
        z = 1.24 - 0.30 * t ** 1.7
        w = 0.15 * (1 - t) ** 0.7 + 0.004
        th = 0.008 * (1 - t) + 0.001
        verts += [(x, -th, z), (x, th, z), (x + 0.05 * w, 0.0, z - w)]
    for i in range(n):
        a, c = 3 * i, 3 * (i + 1)
        faces += [(a, c, c + 2, a + 2), (a + 1, a + 2, c + 2, c + 1), (a, a + 1, c + 1, c)]
    faces.append((0, 2, 1))
    last = 3 * n
    faces.append((last, last + 1, last + 2))
    b.add(verts, faces, 0)
    return b


def greatsword(blade, hilt):
    b = Builder()
    # Pega comprida para as duas mãos (a origem é a mão de cima), pomo, guarda larga e lâmina larga.
    b.cylinder(0.024, -0.34, 0.06, 1, sides=8)
    b.cylinder(0.04, -0.42, -0.34, 1, sides=8, r1=0.03)
    b.box(0.0, 0.0, 0.1, 0.38, 0.05, 0.05, 1)
    rings = []
    length, base = 1.3, 0.13
    for z, half in ((base, 0.075), (base + 0.15, 0.08), (base + length * 0.75, 0.07)):
        rings.append([(half, 0, z), (0, 0.016, z), (-half, 0, z), (0, -0.016, z)])
    verts = [p for r in rings for p in r]
    faces = [(3, 2, 1, 0)]
    for r in range(len(rings) - 1):
        a, c = r * 4, (r + 1) * 4
        for i in range(4):
            j = (i + 1) % 4
            faces.append((a + i, a + j, c + j, c + i))
    tip = len(verts)
    verts.append((0, 0, base + length))
    last = (len(rings) - 1) * 4
    faces += [(last + i, last + (i + 1) % 4, tip) for i in range(4)]
    b.add(verts, faces, 0)
    return b


def dagger(blade, hilt):
    b = Builder()
    # Adaga curta: lâmina em folha (losango) de 0.29 m, guarda pequena, punho curto e pomo.
    b.cylinder(0.017, -0.1, 0.03, 1, sides=8)
    b.cylinder(0.024, -0.13, -0.1, 1, sides=8, r1=0.018)
    b.box(0.0, 0.0, 0.045, 0.11, 0.03, 0.025, 1)
    rings = []
    for z, half in ((0.06, 0.022), (0.13, 0.03), (0.27, 0.02)):
        rings.append([(half, 0, z), (0, 0.007, z), (-half, 0, z), (0, -0.007, z)])
    verts = [p for r in rings for p in r]
    faces = [(3, 2, 1, 0)]
    for r in range(len(rings) - 1):
        a, c = r * 4, (r + 1) * 4
        for i in range(4):
            j = (i + 1) % 4
            faces.append((a + i, a + j, c + j, c + i))
    tip = len(verts)
    verts.append((0, 0, 0.35))
    last = (len(rings) - 1) * 4
    faces += [(last + i, last + (i + 1) % 4, tip) for i in range(4)]
    b.add(verts, faces, 0)
    return b


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    blade = material("Blade", (0.78, 0.8, 0.84), 0.85, 0.3)
    hilt = material("Hilt", (0.16, 0.12, 0.1), 0.1, 0.7)
    objs = []
    for name, build in (("Scythe", scythe), ("Greatsword", greatsword), ("Dagger", dagger)):
        b = build(blade, hilt)
        objs.append(mesh_object(name, b.verts, b.faces, [blade, hilt], b.mats))

    FBX.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=str(FBX), use_selection=True, object_types={'MESH'},
        global_scale=1, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='OFF', use_triangles=True,
        bake_anim=False, add_leaf_bones=False, use_custom_props=False,
        path_mode='STRIP', embed_textures=False)
    for o in objs:
        print(f"WEAPON {o.name} dims={tuple(round(d, 3) for d in o.dimensions)} tris={sum(len(p.vertices) - 2 for p in o.data.polygons)}")

    # Pré-visualização lado a lado.
    objs[0].location.x = -0.5
    objs[1].location.x = 0.6
    objs[2].location.x = 1.3
    objs[2].location.z = 0.4
    scene = bpy.context.scene
    world = bpy.data.worlds.new("W")
    world.color = (0.05, 0.055, 0.065)
    scene.world = world
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    cam.location = (0.05, -4.2, 0.45)
    cam.rotation_euler = (math.radians(90), 0, 0)
    cam.data.lens = 50
    scene.camera = cam
    for loc, energy in (((-2, -3, 3), 400), ((3, 1, 2), 200)):
        light = bpy.data.objects.new("L", bpy.data.lights.new("L", 'AREA'))
        light.data.energy = energy
        light.data.size = 3
        light.location = loc
        light.rotation_euler = ((cam.location - light.location).to_track_quat('-Z', 'Y').to_euler()
                                if False else (math.radians(60), 0, math.radians(-40)))
        scene.collection.objects.link(light)
    scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    scene.view_settings.view_transform = 'Standard'
    scene.render.resolution_x, scene.render.resolution_y = 1200, 900
    scene.render.filepath = str(PREVIEW)
    bpy.ops.render.render(write_still=True)
    print("WEAPONS ok", FBX)


main()
