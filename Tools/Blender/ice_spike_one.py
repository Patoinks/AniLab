"""
Um pico de gelo bom (alvo: o pico alto da vista lateral da folha mestra, Logs/cap/ref_ice_master.webp).

Forma: lâmina comprida (~3 m, base ~0.6 m), facetas longas e direitas da base quase à ponta, ponta em cinzel
assimétrica, inclinado ~25°. Material: gelo claro e luminoso (azul no meio das faces, branco junto às arestas),
rede de veios brancos na superfície (fissuras tipo geada), arestas a brilhar, fraturas internas longitudinais
que brilham quando vistas de lado, glow.

Uso no Blender (depois de ice_hero.py, que tem as funções comuns):
exec(open(r"...\\ice_hero.py", encoding="utf-8").read()); exec(open(r"...\\ice_spike_one.py", encoding="utf-8").read())
build_one(); render_views()
"""
import bpy, bmesh, math, random, subprocess
from mathutils import Vector, Matrix

ONE = "IceSpikeOne"
CAP = r"C:\Users\Martim\AnimLab\Logs\cap"


def _slab(rng, height, radius, flat, sides, center_x, tip_x, rings):
    """Laje convexa: cada anel encolhe na direção de tip_x (esse bordo fica comprido e direito)."""
    bm = bmesh.new()
    base_angles = [(k + rng.uniform(-0.1, 0.1)) / sides * 2 * math.pi for k in range(sides)]
    radii = [rng.uniform(0.92, 1.08) for k in range(sides)]
    for t, rf in rings:
        cx = center_x + (tip_x - center_x) * (1.0 - rf)
        for a, rr in zip(base_angles, radii):
            r = radius * rf * rr * rng.uniform(0.98, 1.02)
            bm.verts.new(Vector((cx + math.cos(a) * r, math.sin(a) * r * flat, t * height)))
    bm.verts.new(Vector((tip_x, 0.0, height)))
    _hull(bm)
    return bm


def blade(rng, height=3.4, radius=0.46, flat=0.5, sides=6):
    """Lâmina larga da folha: face da frente larga, bordo de trás (-X) comprido e direito até à ponta,
    bordo da frente (+X) inclinado, com facetas em triângulo perto da ponta."""
    rings = [(-0.05, 1.0), (0.25, rng.uniform(0.95, 1.0)), (0.5, rng.uniform(0.78, 0.84)), (0.72, rng.uniform(0.5, 0.56)),
             (0.88, rng.uniform(0.2, 0.25))]
    bm = _slab(rng, height, radius, flat, sides, 0.0, -radius * 0.75, rings)
    for nrm, depth in (((1.0, 0.45, 0.55), 0.1), ((1.0, -0.4, 0.4), 0.12), ((0.6, 0.2, 1.0), 0.06)):
        n = Vector(nrm).normalized()
        top = max(v.co.dot(n) for v in bm.verts)
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=n * (top - radius * depth), plane_no=n, clear_outer=True)
        _hull(bm)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(2.0), verts=bm.verts[:], edges=bm.edges[:])
    return bm


def blade_step(rng, height=3.4, radius=0.46):
    """Segunda camada encostada à frente da lâmina, que acaba a ~60%: o degrau do lado direito da folha."""
    h = height * rng.uniform(0.5, 0.56)
    rings = [(-0.05, 1.0), (0.4, rng.uniform(0.88, 0.94)), (0.75, rng.uniform(0.45, 0.55))]
    bm = _slab(rng, h, radius * 0.7, 0.45, 5, radius * 0.95, radius * 0.55, rings)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(2.0), verts=bm.verts[:], edges=bm.edges[:])
    return bm


def spike_material():
    """Gelo mágico estilizado (modelo unlit, igual ao que o shader do Unity vai fazer):
    nuvens azul fundo / ciano por dentro, três camadas de fissuras brancas a profundidades diferentes (paralaxe),
    facetas pintadas claro/escuro por uma luz fixa, rim claro de lado, arestas (chanfro) a brilhar."""
    m = bpy.data.materials.get("SpikeIce") or bpy.data.materials.new("SpikeIce")
    m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    N = nt.nodes.new; L = nt.links.new
    out = N("ShaderNodeOutputMaterial")
    tc = N("ShaderNodeTexCoord")
    geo = N("ShaderNodeNewGeometry")
    # Coordenadas com paralaxe: cada camada desloca-se pela direção de vista (parece estar dentro do gelo).
    def layer_coords(depth):
        sc = N("ShaderNodeVectorMath"); sc.operation = 'SCALE'; sc.inputs["Scale"].default_value = -depth
        L(geo.outputs["Incoming"], sc.inputs[0])
        add = N("ShaderNodeVectorMath"); add.operation = 'ADD'
        L(tc.outputs["Object"], add.inputs[0]); L(sc.outputs[0], add.inputs[1])
        warp = N("ShaderNodeTexNoise"); warp.inputs["Scale"].default_value = 1.4; warp.inputs["Detail"].default_value = 4
        L(add.outputs[0], warp.inputs["Vector"])
        mix = N("ShaderNodeMix"); mix.data_type = 'VECTOR'; mix.inputs[0].default_value = 0.1
        L(add.outputs[0], mix.inputs[4]); L(warp.outputs["Color"], mix.inputs[5])
        return mix.outputs[1]

    def cells(vec, scale, line_w, halo_w, line_k, halo_k):
        """Rede de fissuras como na folha (vista de perto): células irregulares esticadas ao longo do pico,
        com o miolo azul fundo, bordas leitosas largas e uma linha branca fina no meio da borda.
        Devolve (brilho das fissuras, miolo 0..1)."""
        st = N("ShaderNodeVectorMath"); st.operation = 'MULTIPLY'; st.inputs[1].default_value = (1.0, 1.0, 0.35)
        L(vec, st.inputs[0])
        v = N("ShaderNodeTexVoronoi"); v.feature = 'DISTANCE_TO_EDGE'; v.inputs["Scale"].default_value = scale
        L(st.outputs[0], v.inputs["Vector"])
        line = N("ShaderNodeMapRange"); line.inputs[2].default_value = line_w; line.inputs[3].default_value = line_k; line.inputs[4].default_value = 0.0
        L(v.outputs["Distance"], line.inputs[0])
        halo = N("ShaderNodeMapRange"); halo.inputs[2].default_value = halo_w; halo.inputs[3].default_value = halo_k; halo.inputs[4].default_value = 0.0
        halo.interpolation_type = 'SMOOTHSTEP'
        L(v.outputs["Distance"], halo.inputs[0])
        both = N("ShaderNodeMath"); both.operation = 'ADD'
        L(line.outputs[0], both.inputs[0]); L(halo.outputs[0], both.inputs[1])
        core = N("ShaderNodeMapRange"); core.inputs[1].default_value = halo_w * 0.6; core.inputs[2].default_value = halo_w * 2.5
        L(v.outputs["Distance"], core.inputs[0])
        return both.outputs[0], core.outputs[0]

    c0 = layer_coords(0.0); c1 = layer_coords(0.12); c2 = layer_coords(0.3)
    big, core_big = cells(c0, 1.6, 0.006, 0.085, 0.6, 0.38)
    small, core_small = cells(c1, 3.8, 0.004, 0.045, 0.3, 0.18)
    deep, core_deep = cells(c2, 1.1, 0.012, 0.08, 0.2, 0.12)
    # Estrias compridas ao longo do pico (as linhas paralelas da folha).
    sm = N("ShaderNodeVectorMath"); sm.operation = 'MULTIPLY'; sm.inputs[1].default_value = (26.0, 26.0, 1.2)
    L(c0, sm.inputs[0])
    sn = N("ShaderNodeTexNoise"); sn.inputs["Scale"].default_value = 1.0; sn.inputs["Detail"].default_value = 3
    L(sm.outputs[0], sn.inputs["Vector"])
    stri = N("ShaderNodeMapRange"); stri.inputs[1].default_value = 0.6; stri.inputs[2].default_value = 0.7; stri.inputs[4].default_value = 0.3
    L(sn.outputs["Fac"], stri.inputs[0])
    acc = big
    for k in (small, deep, stri.outputs[0]):
        ad = N("ShaderNodeMath"); ad.operation = 'ADD'
        L(acc, ad.inputs[0]); L(k, ad.inputs[1]); acc = ad.outputs[0]
    ksum2 = N("ShaderNodeMath"); ksum2.operation = 'MINIMUM'; ksum2.inputs[1].default_value = 1.4
    L(acc, ksum2.inputs[0])
    # Miolo das células (camadas grande e pequena): escurece o tom para azul fundo.
    cores = N("ShaderNodeMath"); cores.operation = 'MULTIPLY'
    L(core_big, cores.inputs[0]); L(core_small, cores.inputs[1])
    # Nuvens por dentro (camada do meio).
    cloud = N("ShaderNodeTexNoise"); cloud.inputs["Scale"].default_value = 1.6; cloud.inputs["Detail"].default_value = 6
    cloud.inputs["Roughness"].default_value = 0.6
    L(c1, cloud.inputs["Vector"])
    ramp = N("ShaderNodeValToRGB"); e = ramp.color_ramp.elements
    e[0].position = 0.3; e[0].color = (0.005, 0.08, 0.5, 1)
    e[1].position = 0.85; e[1].color = (0.62, 0.9, 1.0, 1)
    mid = e.new(0.5); mid.color = (0.15, 0.58, 1.0, 1)
    tone = N("ShaderNodeMath"); tone.operation = 'MULTIPLY_ADD'; tone.inputs[1].default_value = 0.75
    # Facetas pintadas: uma luz fixa decide se a faceta é clara ou escura (como na folha).
    light = N("ShaderNodeVectorMath"); light.operation = 'DOT_PRODUCT'
    light.inputs[1].default_value = (-0.45, -0.55, 0.7)
    L(geo.outputs["Normal"], light.inputs[0])
    facet01 = N("ShaderNodeMapRange"); facet01.inputs[1].default_value = -0.5; facet01.inputs[2].default_value = 0.8
    L(light.outputs["Value"], facet01.inputs[0])
    fhalf = N("ShaderNodeMath"); fhalf.operation = 'MULTIPLY'; fhalf.inputs[1].default_value = 0.32
    L(facet01.outputs[0], fhalf.inputs[0])
    L(cloud.outputs["Fac"], tone.inputs[0]); L(fhalf.outputs[0], tone.inputs[2])
    dark = N("ShaderNodeMath"); dark.operation = 'MULTIPLY_ADD'; dark.inputs[1].default_value = -0.45
    L(cores.outputs[0], dark.inputs[0]); L(tone.outputs[0], dark.inputs[2])
    L(dark.outputs[0], ramp.inputs[0])
    facet = N("ShaderNodeMapRange"); facet.inputs[1].default_value = -0.6; facet.inputs[2].default_value = 0.9
    facet.inputs[3].default_value = 0.65; facet.inputs[4].default_value = 1.15
    L(light.outputs["Value"], facet.inputs[0])
    sep = N("ShaderNodeSeparateXYZ"); L(tc.outputs["Object"], sep.inputs[0])
    vgrad = N("ShaderNodeMapRange"); vgrad.inputs[1].default_value = 0.0; vgrad.inputs[2].default_value = 3.3
    vgrad.inputs[3].default_value = 0.65; vgrad.inputs[4].default_value = 1.35
    L(sep.outputs["Z"], vgrad.inputs[0])
    fv = N("ShaderNodeMath"); fv.operation = 'MULTIPLY'
    L(facet.outputs[0], fv.inputs[0]); L(vgrad.outputs[0], fv.inputs[1])
    # Manchas leitosas (gelo turvo) a puxar para branco-azulado.
    milk = N("ShaderNodeTexNoise"); milk.inputs["Scale"].default_value = 2.3; milk.inputs["Detail"].default_value = 5
    L(c1, milk.inputs["Vector"])
    mm = N("ShaderNodeMapRange"); mm.inputs[1].default_value = 0.55; mm.inputs[2].default_value = 0.75
    mm.inputs[4].default_value = 0.35
    L(milk.outputs["Fac"], mm.inputs[0])
    milky = N("ShaderNodeMix"); milky.data_type = 'RGBA'; milky.inputs[7].default_value = (0.82, 0.94, 1.0, 1)
    L(mm.outputs[0], milky.inputs[0]); L(ramp.outputs[0], milky.inputs[6])
    shaded = N("ShaderNodeVectorMath"); shaded.operation = 'SCALE'
    L(milky.outputs[2], shaded.inputs[0]); L(fv.outputs[0], shaded.inputs["Scale"])
    # Rim claro de lado.
    lw = N("ShaderNodeLayerWeight"); lw.inputs["Blend"].default_value = 0.25
    rim = N("ShaderNodeVectorMath"); rim.operation = 'SCALE'; rim.inputs[0].default_value = (0.35, 0.75, 1.0)
    L(lw.outputs["Fresnel"], rim.inputs["Scale"])
    # Fissuras e arestas brancas (acima de 1 para o glow).
    kcol = N("ShaderNodeVectorMath"); kcol.operation = 'SCALE'; kcol.inputs[0].default_value = (1.1, 1.35, 1.6)
    L(ksum2.outputs[0], kcol.inputs["Scale"])
    bev = N("ShaderNodeBevel"); bev.inputs["Radius"].default_value = 0.012; bev.samples = 8
    bdot = N("ShaderNodeVectorMath"); bdot.operation = 'DOT_PRODUCT'
    L(bev.outputs[0], bdot.inputs[0]); L(geo.outputs["Normal"], bdot.inputs[1])
    edge = N("ShaderNodeMapRange"); edge.inputs[1].default_value = 0.997; edge.inputs[2].default_value = 0.94
    edge.inputs[4].default_value = 4.0
    L(bdot.outputs["Value"], edge.inputs[0])
    ecol = N("ShaderNodeVectorMath"); ecol.operation = 'SCALE'; ecol.inputs[0].default_value = (0.8, 0.95, 1.0)
    L(edge.outputs[0], ecol.inputs["Scale"])
    total = None
    for vec in (shaded.outputs[0], rim.outputs[0], kcol.outputs[0], ecol.outputs[0]):
        if total is None:
            total = vec
            continue
        a = N("ShaderNodeVectorMath"); a.operation = 'ADD'
        L(total, a.inputs[0]); L(vec, a.inputs[1]); total = a.outputs[0]
    em = N("ShaderNodeEmission"); em.inputs["Strength"].default_value = 1.0
    L(total, em.inputs["Color"])
    tr = N("ShaderNodeBsdfTransparent")
    mix = N("ShaderNodeMixShader"); mix.inputs[0].default_value = 0.12
    L(em.outputs[0], mix.inputs[1]); L(tr.outputs[0], mix.inputs[2])
    L(mix.outputs[0], out.inputs["Surface"])
    return m


def build_one(seed=21):
    col = _collection(ONE)
    cube = bpy.data.objects.get("Cube")  # cubo da cena inicial do Blender (render em batch)
    if cube:
        cube.hide_render = True
    for c in ("IceHero", "IceSpikes", "IceBurstPreview"):
        if c in bpy.data.collections:
            bpy.data.collections[c].hide_render = True
            bpy.data.collections[c].hide_viewport = True
    ice, crack, frost, floor, human = materials()
    mat = spike_material()
    rng = random.Random(seed)
    m = Matrix.Rotation(math.radians(30), 4, 'Y')
    ob = _obj("Spike", blade(rng), col, mat)
    step = _obj("SpikeStep", blade_step(rng), col, mat)
    mod = ob.modifiers.new("uniao", 'BOOLEAN'); mod.operation = 'UNION'; mod.object = step; mod.solver = 'EXACT'
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob.modifiers.remove(mod)
    old = ob.data; ob.data = me; bpy.data.meshes.remove(old)
    bpy.data.objects.remove(step, do_unlink=True)
    for poly in ob.data.polygons:
        poly.use_smooth = False
    # Fraturas internas compridas, ao longo do pico (recortadas pela forma, como no frame-alvo).
    # (as placas internas de fratura ficavam como tábuas brancas: as fissuras do material chegam)
    ob.data.transform(m)
    for o in col.objects:
        if "_crack" in o.name:
            o.data.transform(m)
    _chamfer(ob)
    ob.modifiers["chanfro"].width = 0.012

    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
    fl = bpy.context.active_object; fl.name = "OneFloor"; fl.data.materials.append(floor)
    for c in list(fl.users_collection): c.objects.unlink(fl)
    col.objects.link(fl)
    for name, kind, loc, scl in [("Head", 'UV_SPHERE', (-1.4, 0.6, 1.65), (0.11, 0.11, 0.12)), ("Body", 'CYLINDER', (-1.4, 0.6, 0.8), (0.17, 0.12, 0.75))]:
        if kind == 'UV_SPHERE':
            bpy.ops.mesh.primitive_uv_sphere_add(radius=1, location=loc)
        else:
            bpy.ops.mesh.primitive_cylinder_add(radius=1, depth=2, location=loc)
        o = bpy.context.active_object; o.name = "OneMan_" + name; o.scale = scl; o.data.materials.append(human)
        for c in list(o.users_collection): c.objects.unlink(o)
        col.objects.link(o)
    for name, loc, size, energy, color in [("OneKeyL", (-5, -4, 5), 4, 500, (0.8, 0.9, 1.0)), ("OneKeyR", (5, -2, 3), 4, 400, (0.4, 0.65, 1.0)),
                                           ("OneRim", (1, 6, 6), 5, 700, (0.55, 0.75, 1.0))]:
        ld = bpy.data.lights.new(name, 'AREA'); ld.energy = energy; ld.size = size; ld.color = color
        lo = bpy.data.objects.new(name, ld); col.objects.link(lo); lo.location = loc
        lo.rotation_euler = (Vector((0.3, 0, 1.3)) - lo.location).to_track_quat('-Z', 'Y').to_euler()
    ld = bpy.data.lights.new("OneBase", 'POINT'); ld.energy = 0; ld.color = (0.35, 0.65, 1.0)
    lo = bpy.data.objects.new("OneBase", ld); col.objects.link(lo); lo.location = (0.4, -0.5, 0.25)
    return col


def _cracks_long(rng, hero, col, mat, count):
    """Fraturas internas compridas (ao longo do eixo do pico), recortadas pelo próprio pico."""
    shell = hero.copy(); shell.data = hero.data.copy(); col.objects.link(shell)
    shell.scale = (0.9, 0.9, 0.97)
    for i in range(count):
        bm = bmesh.new()
        w, h = rng.uniform(0.12, 0.3), rng.uniform(0.8, 2.0)
        for k in range(7):
            a = k / 7 * 2 * math.pi + rng.uniform(-0.3, 0.3)
            x, z = math.cos(a) * w * rng.uniform(0.6, 1.0), math.sin(a) * h * 0.5 * rng.uniform(0.7, 1.0)
            bm.verts.new(Vector((x, -0.003, z))); bm.verts.new(Vector((x, 0.003, z)))
        _hull(bm)
        z0 = rng.uniform(0.6, 1.8)
        mm = (Matrix.Translation(Vector((0.35 * 0.32 * z0 / 3.0 + rng.uniform(-0.08, 0.08), rng.uniform(-0.06, 0.06), z0)))
              @ Matrix.Rotation(rng.uniform(-0.25, 0.25), 4, 'Y') @ Matrix.Rotation(rng.uniform(0, math.pi), 4, 'Z'))
        slab = _obj(hero.name + "_crack%d" % i, bm, col, mat, mm)
        mod = slab.modifiers.new("clip", 'BOOLEAN'); mod.operation = 'INTERSECT'; mod.object = shell; mod.solver = 'EXACT'
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(slab.evaluated_get(dg))
        slab.modifiers.remove(mod)
        old = slab.data; slab.data = me; bpy.data.meshes.remove(old)
        slab.matrix_world = Matrix()
    bpy.data.meshes.remove(shell.data)


def render_views(samples=64):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.samples = samples; sc.cycles.use_denoising = True
    sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'
    sc.render.resolution_x, sc.render.resolution_y = 600, 800
    w = sc.world.node_tree.nodes["Background"]; w.inputs[0].default_value = (0.006, 0.01, 0.02, 1)
    cam = bpy.data.objects["Camera"]; cam.data.type = 'PERSP'; cam.data.lens = 50
    sc.camera = cam
    paths = []
    for i, ang in enumerate((-90, -30, 60)):
        a = math.radians(ang)
        cam.location = Vector((math.cos(a) * 6.5 + 0.4, math.sin(a) * 6.5, 1.9))
        cam.rotation_euler = (Vector((0.5, 0, 1.4)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        p = CAP + r"\spike_one_%d.png" % i
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        paths.append(p)
    cam.location = Vector((1.6, -3.2, 1.6))
    cam.rotation_euler = (Vector((0.7, 0, 1.45)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.resolution_x, sc.render.resolution_y = 800, 800
    close = CAP + r"\spike_one_close.png"
    sc.render.filepath = close
    bpy.ops.render.render(write_still=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", CAP + r"\ref_ice_master.webp", "-i", close, "-filter_complex",
                    "[0]crop=150:180:1190:330,scale=800:-1:flags=lanczos,crop=800:800[r];[r][1]hstack",
                    CAP + r"\spike_close_compare.png"], check=True)
    ref = CAP + r"\ref_spike_tall.png"
    args = ["ffmpeg", "-v", "error", "-y", "-i", ref] + sum([["-i", p] for p in paths], [])
    f = "[0]scale=-1:800[r];" + "".join("[%d]curves=all='0/0 1/1'[v%d];" % (i + 1, i + 1) for i in range(3))
    f += "[r][v1][v2][v3]hstack=4[s];[s]split[a][b];[b]curves=all='0/0 0.42/0 1/1',gblur=sigma=22[g];[a][g]blend=all_mode=screen"
    subprocess.run(args + ["-filter_complex", f, CAP + r"\spike_one_compare.png"], check=True)
