"""
Kit de picos de gelo (AnimLab, folha de assets do Gelo): malhas para o Unity montar a explosão.

Fila 1 - Spike_P*: cachos com um pico grande curvado (garra), 1-3 picos menores encostados e uma base larga
          feita de muitas lascas pequenas. Facetas triangulares irregulares (low-poly).
Fila 2 - Shard_*: lascas soltas (cunha, agulha, lâmina plana, bloco, estilhaço comprido).
Fila 3 - Ground_*: placas de gelo rasas para o chão (anel com o centro livre, monte, estrela).

Cada peça é um objeto com o pivô no centro da base; nos picos o eixo Z é "para cima" e a curva vai para +X
(no Unity roda-se para a curva apontar para fora do centro da explosão).
Os picos são varridos ao longo de uma curva (anéis de vértices torcidos e irregulares), e as peças de um
cacho são unidas por Boolean exato; no fim tudo é triangulado (as arestas de cada triângulo são as linhas
brancas do material).

Uso no Blender: exec(open(r"...\\Tools\\Blender\\ice_spikes.py", encoding="utf-8").read()); build_kit()
"""
import bpy, bmesh, math, random
from mathutils import Vector, Matrix

KIT = "IceSpikes"


def _collection(name):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(col)
    for o in list(col.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    return col


def _hull(bm):
    ret = bmesh.ops.convex_hull(bm, input=bm.verts[:])
    bmesh.ops.delete(bm, geom=list(set(ret["geom_interior"] + ret["geom_unused"])), context='VERTS')


def _to_object(name, bm, col, matrix=Matrix()):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.transform(matrix)
    ob = bpy.data.objects.new(name, me)
    col.objects.link(ob)
    return ob


def _horn(rng, height, radius, bend, sides=6, rings=6, flat=0.85, side_wobble=0.06):
    """Pico varrido ao longo de uma curva: sobe em Z e curva para +X (bend = desvio da ponta / altura)."""
    sign = rng.choice((-1, 1))

    def spine(t):
        return Vector((bend * height * t * t, side_wobble * height * math.sin(math.pi * t) * sign, t * height))

    bm = bmesh.new()
    ring_verts = []
    twist = rng.uniform(0, 1)
    for t in [-0.06] + [0.02 + i * (0.93 - 0.02) / (rings - 1) for i in range(rings)]:
        # Cada anel sai um pouco do eixo: a silhueta ganha quebras em vez de ser um tubo liso.
        p = spine(t) + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)) * radius * 0.12 * (1.0 - max(t, 0.0))
        tan = (spine(t + 0.01) - spine(t - 0.01)).normalized()
        nrm = Vector((0, 1, 0)).cross(tan).normalized()
        side = tan.cross(nrm).normalized()
        # Perfil: base larga, afina sempre, um pouco convexo; a ponta é muito afiada.
        r = radius * max(0.0, 1.04 - max(t, 0.0)) ** 0.95
        twist += rng.uniform(0.1, 0.4)
        ring = []
        for k in range(sides):
            a = (k + twist + rng.uniform(-0.18, 0.18)) / sides * 2 * math.pi
            rr = r * rng.uniform(0.7, 1.25)
            ring.append(bm.verts.new(p + nrm * (math.cos(a) * rr) + side * (math.sin(a) * rr * flat)))
        ring_verts.append(ring)
    tip = bm.verts.new(spine(1.0) + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)) * radius * 0.05)
    base = bm.verts.new(spine(-0.06))
    for a, b in zip(ring_verts[:-1], ring_verts[1:]):
        for k in range(sides):
            bm.faces.new((a[k], a[(k + 1) % sides], b[(k + 1) % sides], b[k]))
    last = ring_verts[-1]
    for k in range(sides):
        bm.faces.new((last[k], last[(k + 1) % sides], tip))
        bm.faces.new((ring_verts[0][(k + 1) % sides], ring_verts[0][k], base))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='SHORT_EDGE', ngon_method='BEAUTY')
    return bm


def _crystal_bits(rng, size, elong):
    """Lasca convexa irregular (pontos ao acaso esticados em Z), para a franja da base."""
    bm = bmesh.new()
    for i in range(rng.randint(7, 11)):
        bm.verts.new(Vector((rng.uniform(-1, 1), rng.uniform(-0.8, 0.8), rng.uniform(0.0, 1.0) * elong)) * size)
    bm.verts.new(Vector((rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2), elong * 1.25)) * size)
    _hull(bm)
    return bm


def _union(main, pieces):
    """Une as peças ao objeto principal (Boolean exato)."""
    if not pieces:
        return
    tmp = bpy.data.collections.new("_ice_tmp")
    bpy.context.scene.collection.children.link(tmp)
    for p in pieces:
        for c in p.users_collection:
            c.objects.unlink(p)
        tmp.objects.link(p)
    mod = main.modifiers.new("union", 'BOOLEAN')
    mod.operation = 'UNION'
    mod.operand_type = 'COLLECTION'
    mod.collection = tmp
    mod.solver = 'EXACT'
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(main.evaluated_get(dg))
    main.modifiers.remove(mod)
    old = main.data
    main.data = me
    bpy.data.meshes.remove(old)
    for p in pieces:
        bpy.data.meshes.remove(p.data)
    bpy.data.collections.remove(tmp)


def _finish(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    # Corta o que ficou abaixo do chão (as peças vêm meio enterradas) e fecha o fundo.
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, -0.02), plane_no=(0, 0, -1), clear_outer=True)
    edges = [e for e in bm.edges if e.is_boundary]
    if edges:
        bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='SHORT_EDGE', ngon_method='BEAUTY')
    bm.to_mesh(ob.data)
    bm.free()
    for p in ob.data.polygons:
        p.use_smooth = False


def _lean(direction_angle, lean_deg):
    axis = Vector((-math.sin(direction_angle), math.cos(direction_angle), 0))
    return Matrix.Rotation(math.radians(lean_deg), 4, axis)


def spike_cluster(name, col, seed, height, radius, bend, side_spikes):
    rng = random.Random(seed)
    main = _to_object(name, _horn(rng, height, radius, bend, sides=rng.randint(5, 6), rings=7), col)
    pieces = []
    # Picos menores encostados ao grande (de lado e atrás; a frente da curva fica limpa).
    for i in range(side_spikes):
        ang = math.pi + rng.uniform(-2.2, 2.2)
        h = height * rng.uniform(0.25, 0.5)
        h = height * rng.uniform(0.3, 0.55)
        bm = _horn(rng, h, radius * rng.uniform(0.5, 0.7), bend * rng.uniform(0.3, 1.2), sides=5, rings=5)
        m = (Matrix.Translation(Vector((math.cos(ang), math.sin(ang), 0)) * radius * rng.uniform(0.35, 0.6))
             @ _lean(ang, rng.uniform(12, 35)) @ Matrix.Rotation(ang, 4, 'Z'))
        pieces.append(_to_object(name + "_s%d" % i, bm, col, m))
    # Franja da base: muitas lascas pequenas inclinadas para fora.
    for i in range(rng.randint(9, 14)):
        ang = rng.uniform(0, 2 * math.pi)
        size = radius * rng.uniform(0.22, 0.42)
        bm = _crystal_bits(rng, size, rng.uniform(1.0, 2.2))
        m = (Matrix.Translation(Vector((math.cos(ang), math.sin(ang), 0)) * radius * rng.uniform(0.5, 0.95) + Vector((0, 0, -0.3 * size)))
             @ _lean(ang, rng.uniform(20, 50)) @ Matrix.Rotation(rng.uniform(0, 6.28), 4, 'Z'))
        pieces.append(_to_object(name + "_f%d" % i, bm, col, m))
    _union(main, pieces)
    _finish(main)
    main["kind"] = "spike"
    return main


def shard(name, col, seed, kind, size):
    rng = random.Random(seed)
    if kind == "needle":
        bm = _horn(rng, size, size * rng.uniform(0.16, 0.22), rng.uniform(0.0, 0.25), sides=5, rings=4)
    else:
        sx, sy, sz, n = {"wedge": (1.0, 0.6, 0.9, 10), "blade": (1.6, 0.18, 0.5, 9),
                         "block": (0.7, 0.65, 0.75, 12), "sliver": (2.4, 0.3, 0.16, 9)}[kind]
        bm = bmesh.new()
        for i in range(n):
            bm.verts.new(Vector((rng.uniform(-1, 1) * sx, rng.uniform(-1, 1) * sy, rng.uniform(0, 1) * sz)) * size * 0.5)
        if kind != "block":
            bm.verts.new(Vector((sx * 1.3, rng.uniform(-0.1, 0.1) * sy, sz * 0.4)) * size * 0.5)
        _hull(bm)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
    ob = _to_object(name, bm, col)
    for p in ob.data.polygons:
        p.use_smooth = False
    ob["kind"] = "shard"
    return ob


def ground_plate(name, col, seed, kind, radius):
    """Placas rasas: lâminas finas quase deitadas e sobrepostas (anel com o centro livre, monte, estrela)."""
    rng = random.Random(seed)
    main = None
    pieces = []
    count = {"ring": 34, "mound": 26, "star": 16}[kind]
    for i in range(count):
        ang = (i + rng.uniform(-0.4, 0.4)) / count * 2 * math.pi if kind != "mound" else rng.uniform(0, 2 * math.pi)
        if kind == "ring":
            dist, length, tilt = radius * rng.uniform(0.5, 0.75), radius * rng.uniform(0.45, 0.7), rng.uniform(8, 28)
        elif kind == "mound":
            dist, length, tilt = radius * rng.uniform(0.0, 0.55), radius * rng.uniform(0.3, 0.55), rng.uniform(4, 22)
        else:
            dist, length, tilt = radius * rng.uniform(0.05, 0.25), radius * rng.uniform(0.6, 1.0), rng.uniform(2, 10)
        bm = bmesh.new()
        width = length * rng.uniform(0.3, 0.5)
        for k in range(rng.randint(7, 10)):
            u = rng.uniform(0, 1)
            bm.verts.new(Vector((u * length, rng.uniform(-1, 1) * width * (1.0 - 0.8 * u), rng.uniform(0, 1) * length * 0.08)))
        bm.verts.new(Vector((length * 1.1, 0, length * 0.02)))
        _hull(bm)
        m = (Matrix.Rotation(ang, 4, 'Z') @ Matrix.Translation(Vector((dist - length * 0.25, 0, -0.04)))
             @ Matrix.Rotation(math.radians(-tilt), 4, 'Y'))
        ob = _to_object(name if main is None else name + "_%d" % i, bm, col, m)
        if main is None:
            main = ob
        else:
            pieces.append(ob)
    _union(main, pieces)
    _finish(main)
    main["kind"] = "ground"
    return main


def build_kit():
    col = _collection(KIT)
    kit = []
    # Fila 1: (altura, raio da base, curva, n.º de picos encostados), do mais direito à garra mais curva.
    for i, (h, r, bend, s) in enumerate([(4.4, 0.62, 0.22, 2), (4.6, 0.55, 0.05, 3), (4.2, 0.6, 0.55, 2),
                                         (3.9, 0.66, 0.42, 3), (3.4, 0.7, 0.5, 2)]):
        kit.append(spike_cluster("Spike_P%d" % (i + 1), col, 100 + i, h, r, bend, s))
    # Fila 2
    for i, (kind, size) in enumerate([("needle", 0.9), ("needle", 0.5), ("wedge", 0.7), ("blade", 0.8), ("blade", 0.65),
                                      ("block", 0.7), ("needle", 0.8), ("wedge", 0.9), ("needle", 0.6), ("block", 0.45),
                                      ("sliver", 0.9)]):
        kit.append(shard("Shard_%02d" % (i + 1), col, 300 + i, kind, size))
    # Fila 3
    for i, (kind, r) in enumerate([("ring", 2.2), ("mound", 1.8), ("star", 2.0)]):
        kit.append(ground_plate("Ground_%s" % kind.capitalize(), col, 400 + i, kind, r))
    return kit
