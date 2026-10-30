"""
Flor de gelo (AnimLab, elemento Gelo): gera as lâminas no Blender, uma por objeto, com o pivô na base
e o eixo local Z ao longo da lâmina (para a erupção no Unity crescer cada uma a partir do chão).

Cada lâmina é o casco convexo de anéis de pontos (base enterrada, meio, ponta descentrada), com cortes planos
perto da superfície (lascas, pontas em cinzel) e, nas grandes, placas paralelas mais finas ao lado (camadas).

Uso no Blender: exec(open(r"...\\Tools\\Blender\\ice_burst.py").read()); build()
"""
import bpy, bmesh, math, random
from mathutils import Vector, Matrix

COLLECTION = "IceBurst"


def _hull(bm):
    ret = bmesh.ops.convex_hull(bm, input=bm.verts[:])
    bmesh.ops.delete(bm, geom=list(set(ret["geom_interior"] + ret["geom_unused"])), context='VERTS')


def _slab(bm, rng, length, radius, flat, offset):
    sides = rng.randint(5, 7)
    levels = [(-0.15, 1.0), (rng.uniform(0.2, 0.45), rng.uniform(0.85, 1.0)), (rng.uniform(0.55, 0.8), rng.uniform(0.4, 0.65))]
    tmp = bmesh.new()
    for (t, rf) in levels:
        for k in range(sides):
            a = (k + rng.uniform(-0.25, 0.25)) / sides * 2 * math.pi
            r = radius * rf * rng.uniform(0.85, 1.1)
            tmp.verts.new(Vector((math.cos(a) * r, math.sin(a) * r * flat, t * length)))
    tmp.verts.new(Vector((rng.uniform(-.3, .3) * radius, rng.uniform(-.3, .3) * radius * flat, length)))
    _hull(tmp)
    # Lascas: cortes quase de lado (a ponta fica afiada na maioria); às vezes um cinzel na ponta.
    for c in range(rng.randint(2, 5)):
        ang = rng.uniform(0, 2 * math.pi)
        up = rng.uniform(0.6, 1.4) if rng.random() < 0.2 else rng.uniform(0.0, 0.45)
        n = Vector((math.cos(ang), math.sin(ang), up)).normalized()
        top = max(v.co.dot(n) for v in tmp.verts)
        d = top - rng.uniform(0.15, 0.45) * radius
        bmesh.ops.bisect_plane(tmp, geom=tmp.verts[:] + tmp.edges[:] + tmp.faces[:], plane_co=n * d, plane_no=n, clear_outer=True)
        _hull(tmp)
    bmesh.ops.dissolve_limit(tmp, angle_limit=math.radians(1.5), verts=tmp.verts[:], edges=tmp.edges[:])
    me = bpy.data.meshes.new("tmp")
    tmp.to_mesh(me)
    tmp.free()
    me.transform(Matrix.Translation(offset))
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)


def _shard(col, rng, index, base, axis, length, radius, flat, layers=1):
    bm = bmesh.new()
    _slab(bm, rng, length, radius, flat, Vector((0, 0, 0)))
    for L in range(1, layers):
        side = (1 if L % 2 else -1) * radius * flat * rng.uniform(0.9, 1.4)
        _slab(bm, rng, length * rng.uniform(0.55, 0.85), radius * rng.uniform(0.6, 0.85), flat * rng.uniform(0.6, 0.9),
              Vector((rng.uniform(-0.2, 0.2) * radius, side, 0)))
    me = bpy.data.meshes.new("Ice_%03d" % index)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(me.name, me)
    col.objects.link(ob)
    rot = Vector((0, 0, 1)).rotation_difference(axis.normalized())
    ob.matrix_world = Matrix.Translation(base) @ rot.to_matrix().to_4x4() @ Matrix.Rotation(rng.uniform(0, 6.283), 4, 'Z')
    ob["length"] = length
    ob["ring"] = base.length  # distância ao centro (atraso da erupção)
    return ob


def _axis(outd, side, lean, twist):
    return (Vector((0, 0, 1)) * math.cos(lean) + outd * math.sin(lean) + side * twist).normalized()


def build(seed=7, radius=3.2, height=5.0, blades=11, core=22, chips=60):
    col = bpy.data.collections.get(COLLECTION)
    if col is None:
        col = bpy.data.collections.new(COLLECTION)
        bpy.context.scene.collection.children.link(col)
    for o in list(col.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    rng = random.Random(seed)
    n = 0
    for i in range(blades):
        ang = (i + rng.uniform(-0.3, 0.3)) / blades * 2 * math.pi
        outd = Vector((math.cos(ang), math.sin(ang), 0)); side = Vector((-outd.y, outd.x, 0))
        L = height * (rng.uniform(0.85, 1.0) if i % 3 == 0 else rng.uniform(0.5, 0.8))
        n += 1
        _shard(col, rng, n, outd * rng.uniform(0.6, 1.1), _axis(outd, side, math.radians(rng.uniform(16, 40)), rng.uniform(-.15, .15)),
               L, L * rng.uniform(0.08, 0.11), rng.uniform(0.45, 0.75), layers=rng.randint(2, 3))
    for i in range(core):
        ang = rng.uniform(0, 2 * math.pi)
        outd = Vector((math.cos(ang), math.sin(ang), 0)); side = Vector((-outd.y, outd.x, 0))
        L = height * rng.uniform(0.22, 0.5)
        n += 1
        _shard(col, rng, n, outd * rng.uniform(0.5, 1.5), _axis(outd, side, math.radians(rng.uniform(22, 55)), rng.uniform(-.35, .35)),
               L, L * rng.uniform(0.12, 0.19), rng.uniform(0.5, 0.85), layers=rng.randint(1, 2))
    for i in range(chips):
        ang = rng.uniform(0, 2 * math.pi)
        d01 = math.sqrt(rng.uniform(0.08, 1))
        outd = Vector((math.cos(ang), math.sin(ang), 0)); side = Vector((-outd.y, outd.x, 0))
        lean = math.radians((28 + 44 * d01) * rng.uniform(0.8, 1.15))
        L = (height * 0.42 - height * 0.32 * d01) * rng.uniform(0.6, 1.2)
        n += 1
        _shard(col, rng, n, outd * (0.55 + d01 * (radius - 0.55)), _axis(outd, side, lean, rng.uniform(-.3, .3)),
               L, L * rng.uniform(0.11, 0.18), rng.uniform(0.5, 0.9))
    return col
