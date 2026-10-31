"""Duas espadas de prototipo, em metros; origem na mao, lamina +Z, gume X.

Uso: executar run_swords.py em Blender --background --factory-startup.
So escreve ProtoSwords.fbx, Swords.blend e swords_preview.png. As medidas
e verificacoes sao impressas no stdout e guardadas nas propriedades dos objetos.
As copias para a folha vivem numa cena separada e nunca entram no FBX.
"""
import json
import math
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
FBX = ROOT / "Assets/KonoSuba/Models/Swords/ProtoSwords.fbx"
BLEND = ROOT / "Tools/Blender/Swords.blend"
PREVIEW = ROOT / "Logs/cap/swords_preview.png"
NAMES = ("Sword_Player", "Sword_Dummy")


def material(name, color, metallic, roughness):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return mat


def active(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def mesh_part(name, vertices, faces, mat, smooth=False):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    bm.free()
    for poly in mesh.polygons:
        poly.use_smooth = smooth
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def loft(name, rings, tip, mat, smooth=False):
    """Aneis XY em alturas Z; fundo fechado e topo fechado ou uma ponta unica."""
    count = len(rings[0])
    verts = [point for ring in rings for point in ring]
    faces = [tuple(reversed(range(count)))]
    for row in range(len(rings) - 1):
        a, b = row * count, (row + 1) * count
        for i in range(count):
            j = (i + 1) % count
            faces.append((a + i, a + j, b + j, b + i))
    last = (len(rings) - 1) * count
    if tip is None:
        faces.append(tuple(last + i for i in range(count)))
    else:
        end = len(verts)
        verts.append(tip)
        faces.extend((last + i, last + (i + 1) % count, end)
                     for i in range(count))
    return mesh_part(name, verts, faces, mat, smooth)


def round_part(name, profile, mat, sides=12):
    rings = [[(rx * math.cos(i * math.tau / sides),
               ry * math.sin(i * math.tau / sides), z)
              for i in range(sides)] for z, rx, ry in profile]
    obj = loft(name, rings, None, mat, smooth=True)
    obj.data.set_sharp_from_angle(angle=math.radians(55))
    return obj


def guard(name, width, depth, bottom, top, mat):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, (bottom + top) / 2))
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = (width, depth, top - bottom)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.data.materials.append(mat)
    bevel = obj.modifiers.new("Small bevel", 'BEVEL')
    bevel.width = 0.003
    bevel.segments = 1
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    obj.data.set_sharp_from_angle(angle=math.radians(55))
    weighted = obj.modifiers.new("Hilt normals", 'WEIGHTED_NORMAL')
    weighted.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier=weighted.name)
    return obj


def assemble(name, parts):
    for label, obj in parts:
        group = obj.vertex_groups.new(name=label)
        group.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
    active(parts[0][1])  # Blade first: stable material slot order.
    for _, obj in parts:
        obj.select_set(True)
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name + "_Mesh"
    obj.location = (0, 0, 0)
    obj.rotation_euler = (0, 0, 0)
    obj.scale = (1, 1, 1)
    blade_id = obj.vertex_groups['Blade'].index
    blade_verts = {v.index for v in obj.data.vertices
                   if any(g.group == blade_id for g in v.groups)}
    for edge in obj.data.edges:
        if all(i in blade_verts for i in edge.vertices):
            edge.use_edge_sharp = True
    obj['pivot'] = "Centro do punho: (0, 0, 0); lamina +Z; gume X"
    return obj


def group_coords(obj, name):
    index = obj.vertex_groups[name].index
    return [v.co for v in obj.data.vertices if any(g.group == index for g in v.groups)]


def extent(coords, axis):
    return max(v[axis] for v in coords) - min(v[axis] for v in coords)


def validate(obj, expected):
    mesh = obj.data
    mesh.calc_loop_triangles()
    vertices = [v.co for v in mesh.vertices]
    blade = group_coords(obj, 'Blade')
    grip = group_coords(obj, 'Grip')
    metrics = {
        'length_m': extent(vertices, 2),
        'blade_length_m': extent(blade, 2),
        'guard_width_m': extent(group_coords(obj, 'Guard'), 0),
        'grip_length_m': extent(grip, 2),
        'blade_width_m': extent(blade, 0),
        'triangles': len(mesh.loop_triangles),
        'tip_local_blender_m': list(max(blade, key=lambda v: v.z)),
    }
    assert all(abs(metrics[key] - value) < 1e-6 for key, value in expected.items()), metrics
    assert metrics['triangles'] < 600, metrics
    assert [slot.material.name for slot in obj.material_slots] == ['Blade', 'Hilt']
    assert obj.location.length < 1e-8 and obj.rotation_euler.to_matrix().is_identity
    assert tuple(obj.scale) == (1, 1, 1)
    assert abs(min(v.z for v in grip) + max(v.z for v in grip)) < 1e-8
    assert all(tri.area > 1e-10 for tri in mesh.loop_triangles)
    assert all(not poly.use_smooth for poly in mesh.polygons if poly.material_index == 0)
    assert all(poly.use_smooth for poly in mesh.polygons if poly.material_index == 1)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    assert all(edge.is_manifold for edge in bm.edges), 'Open/non-manifold mesh'
    assert all(edge.is_contiguous for edge in bm.edges), 'Inconsistent winding'
    assert bm.calc_volume(signed=True) > 0, 'Inverted mesh'
    bm.free()
    for key, value in metrics.items():
        obj[key] = value
    print('SWORD_METRICS ' + obj.name + ' ' + json.dumps(metrics), flush=True)
    return metrics


def build_swords():
    # Este gerador e intencionalmente executado numa sessao batch nova.
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.name = 'Swords'
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1
    blade = material('Blade', (0.63, 0.68, 0.73), 0.72, 0.3)
    hilt = material('Hilt', (0.065, 0.057, 0.047), 0.25, 0.52)

    rings = [[(-w, 0, z), (0, -t, z), (w, 0, z), (0, t, z)]
             for z, w, t in ((0.12, 0.033, 0.009), (0.72, 0.026, 0.007))]
    player = assemble('Sword_Player', [
        ('Blade', loft('Player_Blade', rings, (0, 0, 0.90), blade)),
        ('Guard', guard('Player_Guard', 0.22, 0.038, 0.09, 0.12, hilt)),
        ('Grip', round_part('Player_Grip', [(-0.09, 0.017, 0.014),
                                          (0.09, 0.02, 0.016)], hilt)),
        ('Pommel', round_part('Player_Pommel', [(-0.15, 0.016, 0.012),
                                              (-0.14, 0.026, 0.02),
                                              (-0.10, 0.026, 0.02),
                                              (-0.09, 0.017, 0.014)], hilt)),
    ])
    # Costas espessas -X; bevel largo e gume unico +X. Topo em falchion.
    rings = [[(left, -t, z), (shoulder, -t, z), (right, 0, z),
              (shoulder, t, z), (left, t, z)]
             for z, left, shoulder, right, t in (
                 (0.12, -0.035, 0.018, 0.045, 0.012),
                 (0.48, -0.040, 0.032, 0.080, 0.012),
                 (0.68, -0.044, 0.052, 0.105, 0.011))]
    dummy = assemble('Sword_Dummy', [
        ('Blade', loft('Dummy_Blade', rings, (0.065, 0, 0.82), blade)),
        ('Guard', guard('Dummy_Guard', 0.19, 0.048, 0.085, 0.12, hilt)),
        ('Grip', round_part('Dummy_Grip', [(-0.085, 0.022, 0.018),
                                         (0.085, 0.023, 0.019)], hilt, sides=8)),
        ('Pommel', round_part('Dummy_Pommel', [(-0.13, 0.021, 0.017),
                                             (-0.123, 0.029, 0.023),
                                             (-0.092, 0.029, 0.023),
                                             (-0.085, 0.022, 0.018)], hilt, sides=8)),
    ])
    validate(player, {'length_m': 1.05, 'blade_length_m': 0.78,
                      'guard_width_m': 0.22, 'grip_length_m': 0.18})
    validate(dummy, {'length_m': 0.95, 'blade_length_m': 0.70,
                     'guard_width_m': 0.19, 'grip_length_m': 0.17})
    return player, dummy


def export_swords():
    bpy.context.window.scene = bpy.data.scenes['Swords']
    bpy.ops.object.select_all(action='DESELECT')
    for name in NAMES:
        bpy.data.objects[name].select_set(True)
    FBX.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(FBX), use_selection=True, object_types={'MESH'},
        global_scale=1, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='OFF', use_triangles=True,
        bake_anim=False, add_leaf_bones=False, use_custom_props=False,
        path_mode='STRIP', embed_textures=False)
    validate_fbx()


def validate_fbx():
    """Ler o FBX real: eixos, unidades, transforms, malhas e normais exportados."""
    from io_scene_fbx import parse_fbx
    root, _ = parse_fbx.parse(str(FBX))

    def child(element, name):
        return next(item for item in element.elems if item.id == name)

    def properties(element):
        return {item.props[0]: item.props[4:]
                for item in child(element, b'Properties70').elems}

    settings = properties(child(root, b'GlobalSettings'))
    assert settings[b'UpAxis'] == [1] and settings[b'UpAxisSign'] == [1]
    assert abs(settings[b'UnitScaleFactor'][0] - 100) < 1e-6  # 1 unidade = 1 m.
    objects = child(root, b'Objects').elems
    models = [item for item in objects if item.id == b'Model']
    geometries = [item for item in objects if item.id == b'Geometry']
    assert len(models) == len(geometries) == 2
    assert len([item for item in objects if item.id == b'Material']) == 2
    model_names = {model.props[0]: model.props[1].split(b'\x00')[0].decode('utf-8')
                   for model in models}
    assert set(model_names.values()) == set(NAMES)
    connections = child(root, b'Connections').elems
    geometry_names = {link.props[1]: model_names[link.props[2]] for link in connections
                      if link.props[0] == b'OO' and link.props[2] in model_names}
    for model in models:
        props = properties(model)
        for key, target in ((b'Lcl Translation', (0, 0, 0)),
                            (b'Lcl Rotation', (0, 0, 0)),
                            (b'Lcl Scaling', (1, 1, 1))):
            assert all(abs(a - b) < 1e-6 for a, b in zip(props.get(key, target), target))
    for geo in geometries:
        name = geometry_names[geo.props[0]]
        obj = bpy.data.objects[name]
        values = child(geo, b'Vertices').props[0]
        points = [Vector(values[i:i + 3]) for i in range(0, len(values), 3)]
        expected_tip = obj['tip_local_blender_m']
        expected = Vector((expected_tip[0], expected_tip[2], -expected_tip[1]))
        assert (max(points, key=lambda p: p.y) - expected).length < 1e-6
        assert abs(extent(points, 1) - obj['length_m']) < 1e-6
        indices = child(geo, b'PolygonVertexIndex').props[0]
        assert len(indices) == obj['triangles'] * 3
        assert all((v < 0) == (i % 3 == 2) for i, v in enumerate(indices))
        assert child(geo, b'LayerElementNormal') is not None
        print('FBX_VERIFIED ' + name + ' tip_Y_up=' + str(list(expected)), flush=True)
    print('FBX_VERIFIED two meshes; two materials; rotation=0; scale=1; metres; +Y', flush=True)


def point_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def label(scene, text, x, z, size, mat):
    curve = bpy.data.curves.new('Label', 'FONT')
    curve.body = text
    curve.align_x = 'CENTER'
    curve.size = size
    obj = bpy.data.objects.new('Label_' + text, curve)
    scene.collection.objects.link(obj)
    obj.location = (x, -0.06, z)
    obj.rotation_euler.x = math.pi / 2
    curve.materials.append(mat)


def render_preview(samples=48):
    scene = bpy.data.scenes.new('Swords_Preview')
    bpy.context.window.scene = scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1200
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.film_transparent = False
    world = bpy.data.worlds.new('Preview_Neutral')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (0.025, 0.025, 0.025, 1)
    world.node_tree.nodes['Background'].inputs[1].default_value = 0.7
    scene.world = world
    ink = material('Preview_Label', (0.65, 0.65, 0.65), 0, 1)
    shader = ink.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Emission Color'].default_value = (0.65, 0.65, 0.65, 1)
    shader.inputs['Emission Strength'].default_value = 0.7
    for name, x_front, x_side in (('Sword_Player', -0.68, -0.28),
                                 ('Sword_Dummy', 0.28, 0.68)):
        source = bpy.data.objects[name]
        for view, x, angle in (('FRENTE', x_front, 0), ('PERFIL', x_side, math.pi / 2)):
            obj = source.copy()
            obj.name = 'Preview_' + name + '_' + view
            scene.collection.objects.link(obj)
            obj.location = (x, 0, 0)
            obj.rotation_euler.z = angle
            label(scene, view, x, -0.22, 0.025, ink)
        center = (x_front + x_side) / 2
        label(scene, name, center, 1.02, 0.038, ink)
        label(scene, '%.2f m  /  %d tris' % (source['length_m'], source['triangles']),
              center, -0.29, 0.027, ink)
    label(scene, 'PROTOTIPOS  /  ESCALA 1:1  /  VISTAS ORTOGRAFICAS', 0, 1.13, 0.025, ink)
    for name, location, power, size in (
            ('Key', (-2.4, -3, 3), 240, 2.2),
            ('Fill', (2, -1.5, 1.3), 110, 1.8),
            ('Rim', (0, 2, 2.5), 180, 2)):
        light = bpy.data.lights.new(name, 'AREA')
        light.energy, light.size = power, size
        obj = bpy.data.objects.new(name, light)
        scene.collection.objects.link(obj)
        obj.location = location
        point_at(obj, (0, 0, 0.4))
    cam = bpy.data.objects.new('Preview_Camera', bpy.data.cameras.new('Preview_Camera'))
    scene.collection.objects.link(cam)
    cam.location = (0, -5, 0.42)
    point_at(cam, (0, 0, 0.42))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.15
    scene.camera = cam
    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(PREVIEW)
    bpy.ops.render.render(write_still=True)


def save_swords():
    # Abertura direta na folha; os dois originais de transform identidade estao em Swords.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                area.spaces.active.region_3d.view_perspective = 'CAMERA'
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    assert all(path.is_file() and path.stat().st_size > 0 for path in (FBX, BLEND, PREVIEW))
    print('SWORDS_COMPLETE ' + json.dumps([str(p) for p in (FBX, BLEND, PREVIEW)]), flush=True)
