"""Exporta o boneco de treino (TrainingDummy.blend, do Astra) para o Unity.

Corre em batch:
  "C:/Program Files/Blender Foundation/Blender 5.1/blender.exe" -b Tools/Blender/TrainingDummy.blend --python Tools/Blender/export_dummy.py

- Junta as peças todas numa malha só (uma chamada de desenho no Unity).
- A cor de cada material vai para as cores dos vértices (sRGB, por canto); alfa = 1 no tecido de saco,
  onde o shader do Unity pinta o alvo do peito (no Blender era pintado no próprio material).
- Mantém o Empty Hand_R (onde encaixa a espada).
- FBX em Assets/KonoSuba/Models/TrainingDummy/TrainingDummy.fbx, escala 1, Y para cima.
"""
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/KonoSuba/Models/TrainingDummy/TrainingDummy.fbx"
FABRIC = ("Burlap_Tan", "Burlap_Light", "Burlap_Shade")


def to_srgb(c):
    return c * 12.92 if c <= 0.0031308 else 1.055 * (c ** (1 / 2.4)) - 0.055


def main():
    col = bpy.data.collections["TrainingDummy"]
    meshes = [o for o in col.all_objects if o.type == 'MESH' and o.visible_get()]
    hand = bpy.data.objects["Hand_R"]

    bpy.ops.object.select_all(action='DESELECT')
    copies = []
    for o in meshes:
        c = o.copy()
        c.data = o.data.copy()
        bpy.context.scene.collection.objects.link(c)
        c.parent = None
        c.matrix_world = o.matrix_world
        copies.append(c)

    # Cores dos vértices a partir dos materiais (antes de juntar, cada peça sabe os seus).
    for c in copies:
        me = c.data
        attr = me.color_attributes.new("Col", 'FLOAT_COLOR', 'CORNER')
        for poly in me.polygons:
            mat = c.material_slots[poly.material_index].material if c.material_slots else None
            rgb = [to_srgb(v) for v in mat.diffuse_color[:3]] if mat else [1, 1, 1]
            fabric = 1.0 if mat and any(mat.name.endswith(f) for f in FABRIC) else 0.0
            for li in poly.loop_indices:
                attr.data[li].color = (rgb[0], rgb[1], rgb[2], fabric)

    for c in copies:
        c.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = "TrainingDummy"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    body.data.color_attributes.active_color_name = "Col"
    # Um material só: a cor vem dos vértices.
    body.data.materials.clear()
    body.data.materials.append(bpy.data.materials.new("TrainingDummy"))

    hand.name = "Hand_R_src"  # (senão o novo ficava "Hand_R.001"; o .blend não é gravado)
    marker = bpy.data.objects.new("Hand_R", None)
    bpy.context.scene.collection.objects.link(marker)
    marker.matrix_world = hand.matrix_world.copy()
    marker.parent = body
    marker.matrix_parent_inverse = body.matrix_world.inverted()

    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True)
    marker.select_set(True)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(OUT), use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
        axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', colors_type='LINEAR',
        add_leaf_bones=False, use_mesh_modifiers=True)
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    print(f"EXPORT_DUMMY ok {OUT} tris={tris} hand={tuple(round(v, 3) for v in marker.matrix_world.translation)}")


main()
