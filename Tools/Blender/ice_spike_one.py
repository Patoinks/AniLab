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


def shard(rng, height, radius, flat, cuts):
    """Lasca irregular: casco de pontos ao acaso num volume que afina até à ponta (facetas de tamanhos
    desiguais, silhueta torta), com cortes planos ao acaso (lascas, degraus partidos). A ponta fica."""
    bm = bmesh.new()
    tip = Vector((rng.uniform(-0.25, 0.25) * radius, rng.uniform(-0.15, 0.15) * radius, height))
    for i in range(rng.randint(14, 18)):
        t = rng.random() ** 1.2
        r = radius * (1.0 - t) ** 0.7 * rng.uniform(0.6, 1.15)
        a = rng.uniform(0, 2 * math.pi)
        lean = tip * t  # o eixo vai da base até à ponta (que pode estar ao lado)
        bm.verts.new(Vector((lean.x + math.cos(a) * r, lean.y + math.sin(a) * r * flat, -0.08 * height + t * height * 1.02)))
    bm.verts.new(tip)
    _hull(bm)
    for c in range(cuts):
        ang = rng.uniform(0, 2 * math.pi)
        n = Vector((math.cos(ang), math.sin(ang), rng.uniform(-0.3, 0.9))).normalized()
        top = max(v.co.dot(n) for v in bm.verts)
        d = top - rng.uniform(0.06, 0.3) * radius
        if tip.dot(n) > d:  # este corte levava a ponta: fica para outra lasca
            continue
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=n * d, plane_no=n, clear_outer=True)
        _hull(bm)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(4.0), verts=bm.verts[:], edges=bm.edges[:])
    return bm


def spike_cluster(rng, col, mat, height=3.4, radius=0.48):
    """Pico da folha: lasca grande + 2-3 rebentos tortos a sair da base + lascas pequenas no pé, tudo fundido."""
    # Tronco controlado: face larga dominante, faixa lateral estreita, 1-2 quebras intencionais (cortes convexos).
    trunk = _slab(rng, height, radius, 0.62, 6, 0.0, -radius * 0.45,
                  [(-0.05, 1.0), (0.18, 0.9), (0.55, 0.6), (0.85, 0.25), (0.96, 0.1)])
    for nrm, depth in (((0.9, 0.3, 0.75), 0.07), ((-0.3, 1.0, 0.2), 0.1)):
        n = Vector(nrm).normalized()
        top = max(v.co.dot(n) for v in trunk.verts)
        bmesh.ops.bisect_plane(trunk, geom=trunk.verts[:] + trunk.edges[:] + trunk.faces[:], plane_co=n * (top - radius * depth), plane_no=n, clear_outer=True)
        _hull(trunk)
    bmesh.ops.dissolve_limit(trunk, angle_limit=math.radians(2.0), verts=trunk.verts[:], edges=trunk.edges[:])
    main = _obj("Spike", trunk, col, mat)
    parts = []
    # Segunda camada encostada, mais baixa: o degrau da folha (subordinada ao tronco).
    m = Matrix.Translation(Vector((radius * 0.5, radius * 0.1, 0))) @ Matrix.Rotation(0.12, 4, 'Y')
    parts.append(_obj("Layer", shard(rng, height * rng.uniform(0.48, 0.55), radius * 0.5, 0.55, 8), col, mat, m))
    for i in range(2):
        h = height * rng.uniform(0.22, 0.36)
        ang = rng.uniform(-0.9, 0.9) + (0 if i % 2 else math.pi)
        m = (Matrix.Translation(Vector((math.cos(ang), math.sin(ang) * 0.6, 0)) * radius * rng.uniform(0.3, 0.6))
             @ Matrix.Rotation(rng.uniform(-0.45, 0.45), 4, 'Y') @ Matrix.Rotation(rng.uniform(-0.3, 0.3), 4, 'X'))
        parts.append(_obj("Off%d" % i, shard(rng, h, radius * rng.uniform(0.45, 0.65), rng.uniform(0.5, 0.8), 7), col, mat, m))
    for i in range(rng.randint(4, 6)):
        h = height * rng.uniform(0.07, 0.16)
        ang = rng.uniform(0, 2 * math.pi)
        m = (Matrix.Translation(Vector((math.cos(ang), math.sin(ang) * 0.7, -0.05)) * radius * rng.uniform(0.7, 1.2))
             @ Matrix.Rotation(rng.uniform(0.4, 1.1), 4, Vector((-math.sin(ang), math.cos(ang), 0))))
        parts.append(_obj("Chip%d" % i, shard(rng, h, h * rng.uniform(0.35, 0.6), 0.8, 4), col, mat, m))
    for p in parts:
        mod = main.modifiers.new("u", 'BOOLEAN'); mod.operation = 'UNION'; mod.object = p; mod.solver = 'EXACT'
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(main.evaluated_get(dg))
    for mod in list(main.modifiers):
        main.modifiers.remove(mod)
    old = main.data; main.data = me; bpy.data.meshes.remove(old)
    for p in parts:
        bpy.data.objects.remove(p, do_unlink=True)
    # Corta abaixo do chão.
    bm = bmesh.new(); bm.from_mesh(main.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0), plane_no=(0, 0, -1), clear_outer=True)
    bm.to_mesh(main.data); bm.free()
    for poly in main.data.polygons:
        poly.use_smooth = False
    return main


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
    def layer_coords(depth, wobble=0.24):
        sc = N("ShaderNodeVectorMath"); sc.operation = 'SCALE'; sc.inputs["Scale"].default_value = -depth
        L(geo.outputs["Incoming"], sc.inputs[0])
        add = N("ShaderNodeVectorMath"); add.operation = 'ADD'
        L(tc.outputs["Object"], add.inputs[0]); L(sc.outputs[0], add.inputs[1])
        axis = N("ShaderNodeVectorRotate"); axis.rotation_type = 'Y_AXIS'
        axis.inputs["Angle"].default_value = -math.radians(30)
        L(add.outputs[0], axis.inputs["Vector"])
        warp = N("ShaderNodeTexNoise"); warp.inputs["Scale"].default_value = 1.8; warp.inputs["Detail"].default_value = 4
        L(axis.outputs["Vector"], warp.inputs["Vector"])
        centered = N("ShaderNodeVectorMath"); centered.operation = 'SUBTRACT'; centered.inputs[1].default_value = (0.5, 0.5, 0.5)
        L(warp.outputs["Color"], centered.inputs[0])
        strength = N("ShaderNodeVectorMath"); strength.operation = 'SCALE'; strength.inputs["Scale"].default_value = wobble
        L(centered.outputs[0], strength.inputs[0])
        warped = N("ShaderNodeVectorMath"); warped.operation = 'ADD'
        L(axis.outputs["Vector"], warped.inputs[0]); L(strength.outputs[0], warped.inputs[1])
        return warped.outputs[0]

    def cells(vec, scale, line_w, halo_w, line_k, halo_k):
        """Rede de fissuras como na folha (vista de perto): células irregulares esticadas ao longo do pico,
        com o miolo azul fundo, bordas leitosas largas e uma linha branca fina no meio da borda.
        Devolve (brilho das fissuras, miolo 0..1)."""
        st = N("ShaderNodeVectorMath"); st.operation = 'MULTIPLY'; st.inputs[1].default_value = (1.0, 1.0, 0.55)
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
        core = N("ShaderNodeMapRange"); core.inputs[1].default_value = halo_w * 0.12; core.inputs[2].default_value = halo_w * 1.15
        L(v.outputs["Distance"], core.inputs[0])
        return both.outputs[0], core.outputs[0]

    def fractures(vec, scale, stretch, line_w, halo_w, line_k, halo_k, keep):
        """Fraturas: células de Voronoi muito esticadas ao longo do pico (fissuras compridas), só uma parte
        visível (máscara de ruído) e com a força a variar ao longo da fissura. Devolve (brilho, miolo)."""
        st = N("ShaderNodeVectorMath"); st.operation = 'MULTIPLY'; st.inputs[1].default_value = (1.0, 1.0, stretch)
        L(vec, st.inputs[0])
        v = N("ShaderNodeTexVoronoi"); v.feature = 'DISTANCE_TO_EDGE'; v.inputs["Scale"].default_value = scale
        L(st.outputs[0], v.inputs["Vector"])
        # Largura a variar ao longo da fissura: baseWidth * lerp(0.6, 1.8, ruído).
        wn = N("ShaderNodeTexNoise"); wn.inputs["Scale"].default_value = scale * 2.2; wn.inputs["Detail"].default_value = 2
        L(vec, wn.inputs["Vector"])
        wv = N("ShaderNodeMapRange"); wv.inputs[1].default_value = 0.3; wv.inputs[2].default_value = 0.7
        wv.inputs[3].default_value = line_w * 0.6; wv.inputs[4].default_value = line_w * 1.8
        L(wn.outputs["Fac"], wv.inputs[0])
        line = N("ShaderNodeMapRange"); line.inputs[3].default_value = line_k; line.inputs[4].default_value = 0.0
        line.interpolation_type = 'SMOOTHSTEP'
        L(v.outputs["Distance"], line.inputs[0]); L(wv.outputs[0], line.inputs[2])
        # Auréola com máscara própria (umas vezes mais larga de um lado, outras quase nenhuma).
        hn = N("ShaderNodeTexNoise"); hn.inputs["Scale"].default_value = scale * 1.3; hn.inputs["Detail"].default_value = 2
        L(vec, hn.inputs["Vector"])
        hv = N("ShaderNodeMapRange"); hv.inputs[3].default_value = halo_w * 0.4; hv.inputs[4].default_value = halo_w * 1.4
        L(hn.outputs["Fac"], hv.inputs[0])
        halo = N("ShaderNodeMapRange"); halo.inputs[3].default_value = halo_k; halo.inputs[4].default_value = 0.0
        halo.interpolation_type = 'SMOOTHSTEP'
        L(v.outputs["Distance"], halo.inputs[0]); L(hv.outputs[0], halo.inputs[2])
        both = N("ShaderNodeMath"); both.operation = 'ADD'
        L(line.outputs[0], both.inputs[0]); L(halo.outputs[0], both.inputs[1])
        mn = N("ShaderNodeTexNoise"); mn.inputs["Scale"].default_value = scale * 0.9; mn.inputs["Detail"].default_value = 3
        L(vec, mn.inputs["Vector"])
        mask = N("ShaderNodeMapRange"); mask.inputs[1].default_value = keep; mask.inputs[2].default_value = keep + 0.15
        L(mn.outputs["Fac"], mask.inputs[0])
        out_ = N("ShaderNodeMath"); out_.operation = 'MULTIPLY'
        L(both.outputs[0], out_.inputs[0]); L(mask.outputs[0], out_.inputs[1])
        core = N("ShaderNodeMapRange"); core.inputs[1].default_value = halo_w * 0.3; core.inputs[2].default_value = halo_w * 2.0
        L(v.outputs["Distance"], core.inputs[0])
        # O miolo só escurece onde a fissura existe (antes escurecia mesmo com a fissura escondida).
        cm = N("ShaderNodeMath"); cm.operation = 'MULTIPLY'
        L(core.outputs[0], cm.inputs[0]); L(mask.outputs[0], cm.inputs[1])
        return out_.outputs[0], cm.outputs[0]

    c0 = layer_coords(0.0, 0.035); c1 = layer_coords(0.04, 0.055); c2 = layer_coords(0.1, 0.08)
    w1 = layer_coords(0.04)
    # Fissuras compridas ao longo do pico (as linhas da folha), algumas fraturas cruzadas soltas, e uma camada funda.
    longf, core_long = fractures(c0, 3.4, 0.35, 0.010, 0.04, 0.8, 0.20, 0.38)
    cross, core_cross = fractures(c1, 4.8, 0.65, 0.006, 0.05, 0.35, 0.2, 0.50)
    deep, core_deep = fractures(c2, 1.6, 0.25, 0.008, 0.10, 0.12, 0.12, 0.40)
    acc = longf
    for k in (cross, deep):
        ad = N("ShaderNodeMath"); ad.operation = 'ADD'
        L(acc, ad.inputs[0]); L(k, ad.inputs[1]); acc = ad.outputs[0]
    ksum2 = N("ShaderNodeMath"); ksum2.operation = 'MINIMUM'; ksum2.inputs[1].default_value = 1.4
    L(acc, ksum2.inputs[0])
    # Miolo azul fundo só onde há fraturas cruzadas (as "células" da folha aparecem aqui e ali, não em todo o lado).
    cores = N("ShaderNodeMath"); cores.operation = 'MULTIPLY'
    L(core_cross, cores.inputs[0]); cores.inputs[1].default_value = 1.0
    # Nuvens por dentro (camada do meio).
    cloud = N("ShaderNodeTexNoise"); cloud.inputs["Scale"].default_value = 1.6; cloud.inputs["Detail"].default_value = 6
    cloud.inputs["Roughness"].default_value = 0.6
    L(w1, cloud.inputs["Vector"])
    ramp = N("ShaderNodeValToRGB"); e = ramp.color_ramp.elements
    e[0].position = 0.05; e[0].color = (0.008, 0.075, 0.30, 1)
    e[1].position = 0.85; e[1].color = (0.35, 0.72, 0.92, 1)
    mid = e.new(0.5); mid.color = (0.075, 0.37, 0.70, 1)
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
    dark = N("ShaderNodeMath"); dark.operation = 'MULTIPLY_ADD'; dark.inputs[1].default_value = -0.3
    L(cores.outputs[0], dark.inputs[0]); L(tone.outputs[0], dark.inputs[2])
    L(dark.outputs[0], ramp.inputs[0])
    facet = N("ShaderNodeMapRange"); facet.inputs[1].default_value = -0.6; facet.inputs[2].default_value = 0.9
    facet.inputs[3].default_value = 0.75; facet.inputs[4].default_value = 1.2
    L(light.outputs["Value"], facet.inputs[0])
    sep = N("ShaderNodeSeparateXYZ"); L(tc.outputs["Object"], sep.inputs[0])
    vgrad = N("ShaderNodeMapRange"); vgrad.inputs[1].default_value = 0.0; vgrad.inputs[2].default_value = 3.3
    vgrad.inputs[3].default_value = 0.75; vgrad.inputs[4].default_value = 1.05
    L(sep.outputs["Z"], vgrad.inputs[0])
    fv = N("ShaderNodeMath"); fv.operation = 'MULTIPLY'
    L(facet.outputs[0], fv.inputs[0]); L(vgrad.outputs[0], fv.inputs[1])
    # Manchas leitosas (gelo turvo) a puxar para branco-azulado.
    mv = N("ShaderNodeVectorMath"); mv.operation = 'MULTIPLY'; mv.inputs[1].default_value = (3.0, 3.0, 0.5)
    L(w1, mv.inputs[0])
    milk = N("ShaderNodeTexNoise"); milk.inputs["Scale"].default_value = 1.6; milk.inputs["Detail"].default_value = 6
    L(mv.outputs[0], milk.inputs["Vector"])
    mm = N("ShaderNodeMapRange"); mm.inputs[1].default_value = 0.47; mm.inputs[2].default_value = 0.7
    mm.inputs[4].default_value = 0.32
    L(milk.outputs["Fac"], mm.inputs[0])
    milky = N("ShaderNodeMix"); milky.data_type = 'RGBA'; milky.inputs[7].default_value = (0.82, 0.94, 1.0, 1)
    L(mm.outputs[0], milky.inputs[0]); L(ramp.outputs[0], milky.inputs[6])
    grain = N("ShaderNodeTexNoise"); grain.inputs["Scale"].default_value = 24; grain.inputs["Detail"].default_value = 2
    grain.inputs["Roughness"].default_value = 0.65
    L(c0, grain.inputs["Vector"])
    gv = N("ShaderNodeMapRange"); gv.inputs[3].default_value = 0.94; gv.inputs[4].default_value = 1.06
    L(grain.outputs["Fac"], gv.inputs[0])
    fvg = N("ShaderNodeMath"); fvg.operation = 'MULTIPLY'
    L(fv.outputs[0], fvg.inputs[0]); L(gv.outputs[0], fvg.inputs[1])
    shaded = N("ShaderNodeVectorMath"); shaded.operation = 'SCALE'
    L(milky.outputs[2], shaded.inputs[0]); L(fvg.outputs[0], shaded.inputs["Scale"])
    # Rim claro de lado.
    lw = N("ShaderNodeLayerWeight"); lw.inputs["Blend"].default_value = 0.25
    rim = N("ShaderNodeVectorMath"); rim.operation = 'SCALE'; rim.inputs[0].default_value = (0.15, 0.28, 0.40)
    L(lw.outputs["Fresnel"], rim.inputs["Scale"])
    # Fissuras e arestas brancas (acima de 1 para o glow).
    kcol = N("ShaderNodeVectorMath"); kcol.operation = 'SCALE'; kcol.inputs[0].default_value = (0.58, 0.68, 0.80)
    L(ksum2.outputs[0], kcol.inputs["Scale"])
    total = None
    for vec in (shaded.outputs[0], rim.outputs[0], kcol.outputs[0]):
        if total is None:
            total = vec
            continue
        a = N("ShaderNodeVectorMath"); a.operation = 'ADD'
        L(total, a.inputs[0]); L(vec, a.inputs[1]); total = a.outputs[0]
    # O atributo das faces de chanfro pode ser exportado como cor/atributo de vertice para HLSL.
    edge_attr = N("ShaderNodeAttribute"); edge_attr.attribute_name = "ice_edge"
    en = N("ShaderNodeTexNoise"); en.inputs["Scale"].default_value = 2.5; en.inputs["Detail"].default_value = 2
    L(tc.outputs["Object"], en.inputs["Vector"])
    ev = N("ShaderNodeMapRange"); ev.inputs[1].default_value = 0.4; ev.inputs[2].default_value = 0.65
    ev.inputs[3].default_value = 0.15; ev.inputs[4].default_value = 0.6
    L(en.outputs["Fac"], ev.inputs[0])
    edge_amount = N("ShaderNodeMath"); edge_amount.operation = 'MULTIPLY'
    L(edge_attr.outputs["Fac"], edge_amount.inputs[0]); L(ev.outputs[0], edge_amount.inputs[1])
    edge_color = N("ShaderNodeMixRGB"); edge_color.blend_type = 'MIX'
    edge_color.inputs[2].default_value = (0.75, 0.95, 1.1, 1)
    L(edge_amount.outputs[0], edge_color.inputs[0]); L(total, edge_color.inputs[1])
    em = N("ShaderNodeEmission"); em.inputs["Strength"].default_value = 1.0
    L(edge_color.outputs[0], em.inputs["Color"])
    tr = N("ShaderNodeBsdfTransparent")
    mix = N("ShaderNodeMixShader"); mix.inputs[0].default_value = 0.05
    L(em.outputs[0], mix.inputs[1]); L(tr.outputs[0], mix.inputs[2])
    L(mix.outputs[0], out.inputs["Surface"])
    return m


def build_one(seed=21):
    col = _collection(ONE)
    # O cubo da cena inicial nao faz parte da comparacao do pico.
    cube = bpy.data.objects.get("Cube")
    if cube:
        cube.hide_render = True
    for c in ("IceHero", "IceSpikes", "IceBurstPreview"):
        if c in bpy.data.collections:
            bpy.data.collections[c].hide_render = True
            bpy.data.collections[c].hide_viewport = True
    ice, crack, frost, floor, human = materials()
    mat = spike_material()
    rng = random.Random(seed)
    m = Matrix.Rotation(math.radians(26), 4, 'Y')
    ob = spike_cluster(rng, col, mat)
    # Fraturas internas compridas, ao longo do pico (recortadas pela forma, como no frame-alvo).
    # (as placas internas de fratura ficavam como tábuas brancas: as fissuras do material chegam)
    ob.data.transform(m)
    for o in col.objects:
        if "_crack" in o.name:
            o.data.transform(m)
    _chamfer(ob)
    bevel = ob.modifiers["chanfro"]; bevel.width = 0.007; bevel.material = 1
    ob.data.materials.append(mat)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob.modifiers.remove(bevel)
    old = ob.data; ob.data = me; bpy.data.meshes.remove(old)
    edge_attr = me.attributes.new("ice_edge", 'FLOAT', 'CORNER')
    for poly in me.polygons:
        value = 1.0 if poly.material_index == 1 else 0.0
        for i in poly.loop_indices:
            edge_attr.data[i].value = value
        poly.material_index = 0
    me.materials.pop(index=1)

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
    cam = bpy.data.objects["Camera"]; cam.data.type = 'PERSP'; cam.data.lens = 62
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
    cam.data.lens = 50
    cam.location = Vector((1.6, -3.2, 1.6))
    cam.rotation_euler = (Vector((0.7, 0, 1.45)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.resolution_x, sc.render.resolution_y = 800, 800
    close = CAP + r"\spike_one_close.png"
    sc.render.filepath = close
    bpy.ops.render.render(write_still=True)
    glow = "curves=all='0/0 0.72/0 0.85/0.6 1/1',gblur=sigma=8"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", CAP + r"\ref_ice_master.webp", "-i", close, "-filter_complex",
                    "[0]crop=150:180:1190:330,scale=800:-1:flags=lanczos,crop=800:800[r];"
                    "[1]format=gbrp,split[a][b];[b]" + glow + "[g];[a][g]blend=all_mode=screen:all_opacity=0.2[v];[r][v]hstack",
                    CAP + r"\spike_close_compare.png"], check=True)
    ref = CAP + r"\ref_spike_tall.png"
    args = ["ffmpeg", "-v", "error", "-y", "-i", ref] + sum([["-i", p] for p in paths], [])
    f = "[0]scale=-1:800[r];" + "".join(
        "[{0}]format=gbrp,split[a{0}][b{0}];[b{0}]{1}[g{0}];[a{0}][g{0}]blend=all_mode=screen:all_opacity=0.2[v{0}];".format(i, glow)
        for i in range(1, 4))
    f += "[r][v1][v2][v3]hstack=4"
    subprocess.run(args + ["-filter_complex", f, CAP + r"\spike_one_compare.png"], check=True)
    # Sem glow, para julgar o material (recomendação da análise).
    f2 = "[0]scale=-1:800[r];[r][1][2][3]hstack=4"
    subprocess.run(args + ["-filter_complex", f2, CAP + r"\spike_one_compare_noglow.png"], check=True)
