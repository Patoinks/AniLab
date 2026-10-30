"""
Frame-alvo da explosão de gelo (lookdev no Blender/Cycles), pela folha ICE BURST e pela crítica:
poucos picos-herói largos e sólidos (5) com faces grandes e limpas, 5 médios entre eles, uma base controlada
de cristais pequenos a apoiar cada herói, placa de gelo no chão, fraturas internas gráficas (geometria),
contraste forte azul fundo / azul claro por face, arestas muito luminosas e pouca névoa.

Uso no Blender: exec(open(r"...\\Tools\\Blender\\ice_hero.py", encoding="utf-8").read()); build_scene(); render()
"""
import bpy, bmesh, math, random
from mathutils import Vector, Matrix

COL = "IceHero"
OUT = r"C:\Users\Martim\AnimLab\Logs\cap\ice_target.png"


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


def _obj(name, bm, col, mat=None, matrix=Matrix()):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.transform(matrix)
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(name, me)
    col.objects.link(ob)
    if mat:
        me.materials.append(mat)
    return ob


def gem(rng, height, radius, sides=6, flat=0.85, shear=0.0):
    """Cristal-herói: base larga, corpo de prisma, ombro e ponta piramidal comprida; faces grandes e planas.
    shear inclina a ponta para +X (afim: as faces continuam planas)."""
    bm = bmesh.new()
    rings = [(-0.05, 1.0), (0.14, rng.uniform(1.04, 1.1)), (0.4, rng.uniform(0.9, 0.97)), (0.6, rng.uniform(0.72, 0.8))]
    twist = rng.uniform(0, 1)
    for t, rf in rings:
        twist += rng.uniform(0.0, 0.08)
        for k in range(sides):
            a = (k + twist + rng.uniform(-0.12, 0.12)) / sides * 2 * math.pi
            r = radius * rf * rng.uniform(0.92, 1.08)
            bm.verts.new(Vector((math.cos(a) * r, math.sin(a) * r * flat, t * height)))
    bm.verts.new(Vector((rng.uniform(-0.08, 0.08) * radius, rng.uniform(-0.08, 0.08) * radius, height)))
    _hull(bm)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(2.5), verts=bm.verts[:], edges=bm.edges[:])
    for v in bm.verts:
        v.co.x += shear * max(v.co.z, 0.0)
    return bm


def _place(direction_deg, dist, lean_deg, spin_deg):
    a = math.radians(direction_deg)
    outd = Vector((math.cos(a), math.sin(a), 0))
    axis = Vector((-outd.y, outd.x, 0))
    # A ponta (shear para +X local) fica virada para fora: roda-se +X para a direção de saída.
    return (Matrix.Translation(outd * dist) @ Matrix.Rotation(math.radians(lean_deg), 4, axis)
            @ Matrix.Rotation(a + math.radians(spin_deg), 4, 'Z'))


def _chamfer(ob):
    """Chanfro real nas arestas (apanha a luz como uma aresta de cristal; nada de linhas emissivas)."""
    mod = ob.modifiers.new("chanfro", 'BEVEL')
    mod.width = 0.022
    mod.segments = 2
    mod.limit_method = 'ANGLE'
    mod.angle_limit = math.radians(25)


def _face_random(ob, rng):
    attr = ob.data.attributes.new("face_rand", 'FLOAT', 'FACE')
    for i in range(len(ob.data.polygons)):
        attr.data[i].value = rng.random()


def _cracks(rng, hero, col, mat, count, height, radius, matrix):
    """Planos de fratura dentro do herói: lajes finas recortadas pelo próprio cristal (Boolean intersect)."""
    shell = hero.copy()
    shell.data = hero.data.copy()
    col.objects.link(shell)
    shell.matrix_world = matrix @ Matrix.Diagonal((0.93, 0.93, 0.97, 1.0))
    for i in range(count):
        bm = bmesh.new()
        w, h = radius * rng.uniform(0.6, 1.2), height * rng.uniform(0.2, 0.45)
        pts = []
        for k in range(rng.randint(5, 8)):
            ang = k / 7 * 2 * math.pi + rng.uniform(-0.3, 0.3)
            pts.append((math.cos(ang) * w * rng.uniform(0.6, 1.0), math.sin(ang) * h * rng.uniform(0.6, 1.0)))
        for x, z in pts:
            bm.verts.new(Vector((x, -0.004, z)))
            bm.verts.new(Vector((x, 0.004, z)))
        _hull(bm)
        m = (matrix @ Matrix.Translation(Vector((rng.uniform(-0.3, 0.3) * radius, rng.uniform(-0.3, 0.3) * radius, height * rng.uniform(0.25, 0.5))))
             @ Matrix.Rotation(rng.uniform(0, math.pi), 4, 'Z') @ Matrix.Rotation(rng.uniform(-0.5, 0.5), 4, 'X'))
        slab = _obj(hero.name + "_crack%d" % i, bm, col, mat, m)
        mod = slab.modifiers.new("clip", 'BOOLEAN')
        mod.operation = 'INTERSECT'
        mod.object = shell
        mod.solver = 'EXACT'
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(slab.evaluated_get(dg))
        slab.modifiers.remove(mod)
        old = slab.data
        slab.data = me
        bpy.data.meshes.remove(old)
        slab.matrix_world = Matrix()
    bpy.data.meshes.remove(shell.data)


def materials():
    def new(name):
        m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        m.use_nodes = True
        m.node_tree.nodes.clear()
        return m, m.node_tree

    # Gelo a sério (sem cores lisas por face nem arestas de néon): vidro azul com micro-fraturas no relevo,
    # rugosidade irregular, película fina (arco-íris), veios de geada esticados ao longo do pico, interior turvo
    # (volume com densidade de ruído) e um brilho interior fraco. As arestas são chanfros reais na geometria.
    ice, nt = new("HeroIce")
    N = nt.nodes.new; L = nt.links.new
    out = N("ShaderNodeOutputMaterial")
    tc = N("ShaderNodeTexCoord")
    mp = N("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (2.5, 2.5, 2.5)
    L(tc.outputs["Object"], mp.inputs["Vector"])
    vor = N("ShaderNodeTexVoronoi"); vor.feature = 'DISTANCE_TO_EDGE'; vor.inputs["Scale"].default_value = 1.6
    L(mp.outputs["Vector"], vor.inputs["Vector"])
    crackle = N("ShaderNodeMapRange"); crackle.inputs[1].default_value = 0.0; crackle.inputs[2].default_value = 0.04
    crackle.inputs[3].default_value = 1.0; crackle.inputs[4].default_value = 0.0
    L(vor.outputs["Distance"], crackle.inputs[0])
    fine = N("ShaderNodeTexNoise"); fine.inputs["Scale"].default_value = 14; fine.inputs["Detail"].default_value = 8
    L(mp.outputs["Vector"], fine.inputs["Vector"])
    height = N("ShaderNodeMath"); height.operation = 'MULTIPLY_ADD'; height.inputs[1].default_value = 0.7
    L(crackle.outputs[0], height.inputs[0]); L(fine.outputs["Fac"], height.inputs[2])
    bump = N("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.22; bump.inputs["Distance"].default_value = 0.02
    L(height.outputs[0], bump.inputs["Height"])
    cloud = N("ShaderNodeTexNoise"); cloud.inputs["Scale"].default_value = 1.2; cloud.inputs["Detail"].default_value = 6
    L(mp.outputs["Vector"], cloud.inputs["Vector"])
    rough = N("ShaderNodeMapRange"); rough.inputs[1].default_value = 0.35; rough.inputs[2].default_value = 0.75
    rough.inputs[3].default_value = 0.02; rough.inputs[4].default_value = 0.2
    L(cloud.outputs["Fac"], rough.inputs[0])
    film = N("ShaderNodeMapRange"); film.inputs[3].default_value = 250; film.inputs[4].default_value = 700
    L(fine.outputs["Fac"], film.inputs[0])
    bsdf = N("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = (0.4, 0.7, 1.0, 1)
    bsdf.inputs["Transmission Weight"].default_value = 0.92
    bsdf.inputs["IOR"].default_value = 1.31
    bsdf.inputs["Thin Film IOR"].default_value = 1.33
    bsdf.inputs["Emission Color"].default_value = (0.1, 0.35, 1.0, 1)
    bsdf.inputs["Emission Strength"].default_value = 0.25
    L(rough.outputs[0], bsdf.inputs["Roughness"])
    L(film.outputs[0], bsdf.inputs["Thin Film Thickness"])
    L(bump.outputs["Normal"], bsdf.inputs["Normal"])
    # Veios de geada: ruído esticado ao longo do pico (Z local), branco e baço.
    smp = N("ShaderNodeMapping"); smp.inputs["Scale"].default_value = (7.0, 7.0, 0.3)
    L(tc.outputs["Object"], smp.inputs["Vector"])
    streak = N("ShaderNodeTexNoise"); streak.inputs["Scale"].default_value = 2.2; streak.inputs["Detail"].default_value = 5
    L(smp.outputs["Vector"], streak.inputs["Vector"])
    smask = N("ShaderNodeMapRange"); smask.inputs[1].default_value = 0.64; smask.inputs[2].default_value = 0.72
    smask.inputs[4].default_value = 0.5
    L(streak.outputs["Fac"], smask.inputs[0])
    frostb = N("ShaderNodeBsdfPrincipled")
    frostb.inputs["Base Color"].default_value = (0.75, 0.88, 1.0, 1)
    frostb.inputs["Roughness"].default_value = 0.55
    frostb.inputs["Subsurface Weight"].default_value = 0.6
    frostb.inputs["Emission Color"].default_value = (0.4, 0.65, 1.0, 1)
    frostb.inputs["Emission Strength"].default_value = 0.12
    L(bump.outputs["Normal"], frostb.inputs["Normal"])
    mix = N("ShaderNodeMixShader")
    L(smask.outputs[0], mix.inputs[0]); L(bsdf.outputs[0], mix.inputs[1]); L(frostb.outputs[0], mix.inputs[2])
    geo = N("ShaderNodeNewGeometry")
    bev = N("ShaderNodeBevel"); bev.inputs["Radius"].default_value = 0.03; bev.samples = 8
    bdot = N("ShaderNodeVectorMath"); bdot.operation = 'DOT_PRODUCT'
    L(bev.outputs[0], bdot.inputs[0]); L(geo.outputs["Normal"], bdot.inputs[1])
    emask = N("ShaderNodeMapRange"); emask.inputs[1].default_value = 0.99; emask.inputs[2].default_value = 0.88
    L(bdot.outputs["Value"], emask.inputs[0])
    breakup = N("ShaderNodeTexNoise"); breakup.inputs["Scale"].default_value = 3.0; breakup.inputs["Detail"].default_value = 3
    L(tc.outputs["Object"], breakup.inputs["Vector"])
    bk = N("ShaderNodeMapRange"); bk.inputs[1].default_value = 0.4; bk.inputs[2].default_value = 0.65
    L(breakup.outputs["Fac"], bk.inputs[0])
    estr = N("ShaderNodeMath"); estr.operation = 'MULTIPLY'
    L(emask.outputs[0], estr.inputs[0]); L(bk.outputs[0], estr.inputs[1])
    eem = N("ShaderNodeEmission"); eem.inputs["Color"].default_value = (0.7, 0.88, 1.0, 1); eem.inputs["Strength"].default_value = 2.5
    eadd = N("ShaderNodeMixShader")
    L(estr.outputs[0], eadd.inputs[0]); L(mix.outputs[0], eadd.inputs[1])
    eaddsum = N("ShaderNodeAddShader")
    L(mix.outputs[0], eaddsum.inputs[0]); L(eem.outputs[0], eaddsum.inputs[1])
    L(eaddsum.outputs[0], eadd.inputs[2])
    L(eadd.outputs[0], out.inputs["Surface"])
    # Interior: a absorção dá o azul fundo (mais espesso = mais escuro); um pouco de dispersão só nas nuvens.
    absorb = N("ShaderNodeVolumeAbsorption")
    absorb.inputs["Color"].default_value = (0.05, 0.25, 0.9, 1); absorb.inputs["Density"].default_value = 4.0
    scat = N("ShaderNodeVolumeScatter"); scat.inputs["Color"].default_value = (0.6, 0.8, 1.0, 1)
    dens = N("ShaderNodeMapRange"); dens.inputs[1].default_value = 0.5; dens.inputs[2].default_value = 0.8
    dens.inputs[3].default_value = 0.0; dens.inputs[4].default_value = 0.5
    L(cloud.outputs["Fac"], dens.inputs[0])
    L(dens.outputs[0], scat.inputs["Density"])
    addv = N("ShaderNodeAddShader")
    L(absorb.outputs[0], addv.inputs[0]); L(scat.outputs[0], addv.inputs[1])
    L(addv.outputs[0], out.inputs["Volume"])

    # Fraturas: planos brancos-azulados com um toque de arco-íris pelo ângulo, meio transparentes.
    crack, nt = new("HeroCrack")
    N = nt.nodes.new; L = nt.links.new
    out = N("ShaderNodeOutputMaterial")
    lw = N("ShaderNodeLayerWeight"); lw.inputs[0].default_value = 0.5
    graze = N("ShaderNodeMapRange"); graze.inputs[1].default_value = 0.55; graze.inputs[2].default_value = 0.95
    hue = N("ShaderNodeValToRGB")
    cols = [(0.0, (0.9, 0.95, 1.0)), (0.3, (0.55, 0.9, 1.0)), (0.5, (0.75, 0.6, 1.0)), (0.7, (1.0, 0.75, 0.85)), (1.0, (0.85, 1.0, 0.9))]
    hue.color_ramp.elements[0].position, hue.color_ramp.elements[0].color = cols[0][0], (*cols[0][1], 1)
    hue.color_ramp.elements[1].position, hue.color_ramp.elements[1].color = cols[-1][0], (*cols[-1][1], 1)
    for p, c in cols[1:-1]:
        e = hue.color_ramp.elements.new(p); e.color = (*c, 1)
    L(lw.outputs["Facing"], hue.inputs[0])
    em = N("ShaderNodeEmission"); em.inputs["Strength"].default_value = 5.0
    L(hue.outputs[0], em.inputs["Color"])
    tr = N("ShaderNodeBsdfTransparent")
    mix = N("ShaderNodeMixShader")
    L(lw.outputs["Fresnel"], graze.inputs[0]); L(graze.outputs[0], mix.inputs[0])
    L(tr.outputs[0], mix.inputs[1]); L(em.outputs[0], mix.inputs[2])
    L(mix.outputs[0], out.inputs["Surface"])

    # Placa de gelo no chão: geada branca-azulada, pouco brilho.
    frost, nt = new("HeroFrost")
    b = nt.nodes.new("ShaderNodeBsdfPrincipled"); o = nt.nodes.new("ShaderNodeOutputMaterial")
    b.inputs["Base Color"].default_value = (0.08, 0.2, 0.42, 1); b.inputs["Roughness"].default_value = 0.1
    b.inputs["Emission Color"].default_value = (0.2, 0.45, 1.0, 1); b.inputs["Emission Strength"].default_value = 0.12
    b.inputs["Transmission Weight"].default_value = 0.3
    nt.links.new(b.outputs[0], o.inputs["Surface"])

    floor, nt = new("HeroFloor")
    b = nt.nodes.new("ShaderNodeBsdfPrincipled"); o = nt.nodes.new("ShaderNodeOutputMaterial")
    b.inputs["Base Color"].default_value = (0.008, 0.011, 0.018, 1); b.inputs["Roughness"].default_value = 0.3
    nt.links.new(b.outputs[0], o.inputs["Surface"])

    human, nt = new("HeroHuman")
    b = nt.nodes.new("ShaderNodeBsdfPrincipled"); o = nt.nodes.new("ShaderNodeOutputMaterial")
    b.inputs["Base Color"].default_value = (0.1, 0.1, 0.11, 1); b.inputs["Roughness"].default_value = 0.6
    nt.links.new(b.outputs[0], o.inputs["Surface"])
    return ice, crack, frost, floor, human


def build_scene(seed=3):
    col = _collection(COL)
    for c in ("IceSpikes", "IceBurstPreview"):
        if c in bpy.data.collections:
            bpy.data.collections[c].hide_render = True
            bpy.data.collections[c].hide_viewport = True
    for n in ("Human", "Floor", "CoreLight"):
        o = bpy.data.objects.get(n)
        if o:
            o.hide_render = True; o.hide_viewport = True
    ice, crack, frost, floor, human = materials()
    rng = random.Random(seed)

    # 5 heróis em estrela (o mais alto atrás), 5 médios entre eles.
    heroes = [(100, 4.7, 24), (172, 3.9, 44), (244, 3.4, 52), (316, 3.7, 50), (28, 4.2, 40)]
    for i, (ang, h, lean) in enumerate(heroes):
        r = h * rng.uniform(0.17, 0.2)
        m = _place(ang + rng.uniform(-8, 8), rng.uniform(1.1, 1.5), lean + rng.uniform(-4, 4), 0)
        bm = gem(rng, h, r, sides=rng.choice((5, 6)), flat=rng.uniform(0.75, 0.9), shear=rng.uniform(0.08, 0.2))
        ob = _obj("Hero_%d" % i, bm, col, ice)
        _cracks(rng, ob, col, crack, rng.randint(3, 5), h, r, m)  # recortadas pela forma local do herói
        ob.data.transform(m)
        _chamfer(ob)
        # Base de apoio: 3-4 cristais pequenos no lado de fora do pé do herói.
        for k in range(rng.randint(3, 4)):
            sh = h * rng.uniform(0.14, 0.26)
            sm = _place(ang + rng.uniform(-25, 25), rng.uniform(1.9, 2.6), rng.uniform(40, 65), rng.uniform(-40, 40))
            sb = _obj("Base_%d_%d" % (i, k), gem(rng, sh, sh * rng.uniform(0.25, 0.32), sides=5, shear=0.05), col, ice)
            sb.data.transform(sm)
            _chamfer(sb)
    for i, (ang, h, lean) in enumerate(heroes):
        mh = h * rng.uniform(0.42, 0.55)
        m = _place(ang + 36 + rng.uniform(-6, 6), rng.uniform(2.3, 2.8), rng.uniform(55, 68), 0)
        ob = _obj("Mid_%d" % i, gem(rng, mh, mh * rng.uniform(0.2, 0.24), sides=5, shear=rng.uniform(0.1, 0.2)), col, ice)
        ob.data.transform(m)
        _chamfer(ob)

    # Placa de gelo recortada no chão (contorno irregular em estrela).
    bm = bmesh.new()
    ring = []
    n = 44
    for k in range(n):
        a = k / n * 2 * math.pi
        r = 2.9 * (1.0 + 0.3 * (1 if k % 2 else -0.3) * rng.uniform(0.3, 1.0))
        ring.append(bm.verts.new(Vector((math.cos(a) * r, math.sin(a) * r, 0.02))))
    bm.faces.new(ring)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    _obj("FrostPlate", bm, col, frost)

    # Chão e figura humana de escala no centro.
    bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
    fl = bpy.context.active_object; fl.name = "HeroFloor"; fl.data.materials.append(floor)
    for c in list(fl.users_collection): c.objects.unlink(fl)
    col.objects.link(fl)
    parts = [("Head", 'UV_SPHERE', (0, 0, 1.65), (0.11, 0.11, 0.12)), ("Torso", 'CYLINDER', (0, 0, 1.2), (0.18, 0.12, 0.3)),
             ("LegL", 'CYLINDER', (0.09, 0, 0.45), (0.07, 0.07, 0.45)), ("LegR", 'CYLINDER', (-0.09, 0, 0.45), (0.07, 0.07, 0.45)),
             ("ArmL", 'CYLINDER', (0.27, 0, 1.15), (0.05, 0.05, 0.32)), ("ArmR", 'CYLINDER', (-0.27, 0, 1.15), (0.05, 0.05, 0.32))]
    for name, kind, loc, scl in parts:
        if kind == 'UV_SPHERE':
            bpy.ops.mesh.primitive_uv_sphere_add(radius=1, location=loc)
        else:
            bpy.ops.mesh.primitive_cylinder_add(radius=1, depth=2, location=loc)
        o = bpy.context.active_object; o.name = "Man_" + name; o.scale = scl
        o.data.materials.append(human)
        for c in list(o.users_collection): c.objects.unlink(o)
        col.objects.link(o)

    # Luz: o gelo é a fonte (luzes ciano dentro do cacho) + contraluz frio.
    for name, loc, energy, color in [("IceCore", (0, 0, 0.8), 500, (0.35, 0.65, 1.0)), ("IceCore2", (0, 0, 2.6), 250, (0.5, 0.8, 1.0))]:
        ld = bpy.data.lights.new(name, 'POINT'); ld.energy = energy; ld.color = color; ld.shadow_soft_size = 1.0
        lo = bpy.data.objects.new(name, ld); col.objects.link(lo); lo.location = loc
    ld = bpy.data.lights.new("Rim", 'AREA'); ld.energy = 1800; ld.size = 8; ld.color = (0.55, 0.75, 1.0)
    lo = bpy.data.objects.new("Rim", ld); col.objects.link(lo); lo.location = (1, 7, 11)
    lo.rotation_euler = (Vector((0, 0, 1.5)) - lo.location).to_track_quat('-Z', 'Y').to_euler()
    # Caixas de luz grandes fora de cena: as facetas refletem-nas (sem isto o vidro não tem o que mostrar).
    for name, loc, size, energy, color in [("SoftL", (-9, -3, 6), 6, 900, (0.8, 0.9, 1.0)), ("SoftR", (9, -1, 4), 5, 700, (0.4, 0.65, 1.0)),
                                           ("SoftTop", (0, 0, 12), 4, 150, (0.7, 0.85, 1.0))]:
        ld = bpy.data.lights.new(name, 'AREA'); ld.energy = energy; ld.size = size; ld.color = color
        lo = bpy.data.objects.new(name, ld); col.objects.link(lo); lo.location = loc
        lo.rotation_euler = (Vector((0, 0, 1.5)) - lo.location).to_track_quat('-Z', 'Y').to_euler()
    moon = bpy.data.objects.get("Light")
    if moon:
        moon.hide_render = True

    sc = bpy.context.scene
    w = sc.world.node_tree.nodes["Background"]
    w.inputs[0].default_value = (0.006, 0.01, 0.02, 1); w.inputs[1].default_value = 1.0
    cam = bpy.data.objects["Camera"]
    cam.data.type = 'PERSP'; cam.data.lens = 34
    cam.location = Vector((1.8, -14.5, 7.0))
    cam.rotation_euler = (Vector((0, 0, 1.4)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.camera = cam
    return col


def render(samples=96, path=OUT):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'AgX - Punchy'
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    _bloom(path)


def _bloom(path):
    """Brilho como o da folha (o Cycles não tem bloom): realces desfocados somados por cima, com ffmpeg."""
    import subprocess
    glow = path.replace(".png", "_glow.png")
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", path, "-filter_complex",
                    "[0]split[a][b];[b]curves=all='0/0 0.55/0 1/1',gblur=sigma=18[g];[a][g]blend=all_mode=screen",
                    glow], check=True)
