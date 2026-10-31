"""
Explosão de gelo montada pela folha mestra (Logs/cap/ref_ice_master.webp): baixa e larga (~3 m no pico mais alto,
7-8 m de diâmetro), picos em leque (poucos altos no centro, muitos médios e baixos quase deitados na borda),
chão coberto de placas partidas, algumas lascas no ar e um núcleo luminoso.

Os picos são o pico aprovado (ice_spike_one.py: blade + blade_step + material SpikeIce), em 5 variantes partilhadas
por instâncias com escala/rotação diferentes (a escala de objeto mantém a textura proporcional ao pico).

Uso (batch): blender -b --factory-startup --python Tools/Blender/run_burst.py
"""
import bpy, bmesh, math, random, subprocess
from mathutils import Vector, Matrix

BURST = "IceBurstMaster"


def _bake_spike(seed, name, col, mat, broad_up=False):
    """Malha de um pico aprovado (com degrau, inclinação de 30° e chanfro aplicados)."""
    rng = random.Random(seed)
    ob = _obj(name, blade(rng), col, mat)
    step = _obj(name + "_step", blade_step(rng), col, mat)
    mod = ob.modifiers.new("uniao", 'BOOLEAN'); mod.operation = 'UNION'; mod.object = step; mod.solver = 'EXACT'
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob.modifiers.remove(mod)
    old = ob.data; ob.data = me; bpy.data.meshes.remove(old)
    bpy.data.objects.remove(step, do_unlink=True)
    if broad_up:
        ob.data.transform(Matrix.Rotation(math.radians(90), 4, 'Z'))
    ob.data.transform(Matrix.Rotation(math.radians(30), 4, 'Y'))
    _chamfer(ob)
    ob.modifiers["chanfro"].width = 0.012
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob.modifiers.clear()
    old = ob.data; ob.data = me; bpy.data.meshes.remove(old)
    for poly in ob.data.polygons:
        poly.use_smooth = False
    me = ob.data
    bpy.data.objects.remove(ob, do_unlink=True)
    me.name = name
    return me


def _place(col, name, mesh, angle_deg, dist, extra_lean, scale, z=0.0, spin=0.0):
    """A ponta do pico (inclinada para +X local) fica virada para fora; extra_lean inclina mais (ou menos)."""
    a = math.radians(angle_deg)
    outd = Vector((math.cos(a), math.sin(a), 0))
    tangent = Vector((-outd.y, outd.x, 0))
    ob = bpy.data.objects.new(name, mesh)
    col.objects.link(ob)
    ob.matrix_world = (Matrix.Translation(outd * dist + Vector((0, 0, z))) @ Matrix.Rotation(math.radians(extra_lean), 4, tangent)
                       @ Matrix.Rotation(a + math.radians(spin), 4, 'Z') @ Matrix.Diagonal((scale, scale, scale, 1)))
    return ob


def _plate(rng, size, thick):
    bm = bmesh.new()
    for i in range(rng.randint(7, 10)):
        a = rng.uniform(0, 2 * math.pi)
        r = size * rng.uniform(0.35, 1.0)
        bm.verts.new(Vector((math.cos(a) * r * rng.uniform(1.0, 1.8), math.sin(a) * r, rng.uniform(0, thick))))
    _hull(bm)
    return bm


def _floor_glow_material():
    m = bpy.data.materials.get("BurstGlow") or bpy.data.materials.new("BurstGlow")
    m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    N = nt.nodes.new; L = nt.links.new
    out = N("ShaderNodeOutputMaterial")
    tc = N("ShaderNodeTexCoord")
    grad = N("ShaderNodeTexGradient"); grad.gradient_type = 'QUADRATIC_SPHERE'
    L(tc.outputs["Object"], grad.inputs["Vector"])
    pw = N("ShaderNodeMath"); pw.operation = 'POWER'; pw.inputs[1].default_value = 2.2
    L(grad.outputs["Fac"], pw.inputs[0])
    em = N("ShaderNodeEmission"); em.inputs["Color"].default_value = (0.12, 0.45, 1.0, 1)
    st = N("ShaderNodeMath"); st.operation = 'MULTIPLY'; st.inputs[1].default_value = 3.5
    L(pw.outputs[0], st.inputs[0]); L(st.outputs[0], em.inputs["Strength"])
    tr = N("ShaderNodeBsdfTransparent")
    inv = N("ShaderNodeMath"); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
    L(pw.outputs[0], inv.inputs[1])
    mix = N("ShaderNodeMixShader")
    L(inv.outputs[0], mix.inputs[0]); L(em.outputs[0], mix.inputs[1]); L(tr.outputs[0], mix.inputs[2])
    L(mix.outputs[0], out.inputs["Surface"])
    return m


def _mist_material():
    """Névoa fria: volume azul-claro com densidade de ruído, que cai com a altura e para a borda."""
    m = bpy.data.materials.get("BurstMist") or bpy.data.materials.new("BurstMist")
    m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    N = nt.nodes.new; L = nt.links.new
    out = N("ShaderNodeOutputMaterial")
    tc = N("ShaderNodeTexCoord")
    noise = N("ShaderNodeTexNoise"); noise.inputs["Scale"].default_value = 3.4; noise.inputs["Detail"].default_value = 6
    noise.inputs["Roughness"].default_value = 0.6
    L(tc.outputs["Object"], noise.inputs["Vector"])
    nmr = N("ShaderNodeMapRange"); nmr.inputs[1].default_value = 0.47; nmr.inputs[2].default_value = 0.72
    L(noise.outputs["Fac"], nmr.inputs[0])
    sep = N("ShaderNodeSeparateXYZ"); L(tc.outputs["Object"], sep.inputs[0])
    hz = N("ShaderNodeMapRange"); hz.inputs[1].default_value = -1.0; hz.inputs[2].default_value = 0.85
    hz.inputs[3].default_value = 1.0; hz.inputs[4].default_value = 0.0; hz.interpolation_type = 'SMOOTHSTEP'
    L(sep.outputs["Z"], hz.inputs[0])
    rad = N("ShaderNodeVectorMath"); rad.operation = 'LENGTH'
    xy = N("ShaderNodeCombineXYZ"); L(sep.outputs["X"], xy.inputs[0]); L(sep.outputs["Y"], xy.inputs[1])
    L(xy.outputs[0], rad.inputs[0])
    rr = N("ShaderNodeMapRange"); rr.inputs[1].default_value = 0.4; rr.inputs[2].default_value = 1.0
    rr.inputs[3].default_value = 1.0; rr.inputs[4].default_value = 0.0; rr.interpolation_type = 'SMOOTHSTEP'
    L(rad.outputs["Value"], rr.inputs[0])
    d1 = N("ShaderNodeMath"); d1.operation = 'MULTIPLY'; L(nmr.outputs[0], d1.inputs[0]); L(hz.outputs[0], d1.inputs[1])
    d2 = N("ShaderNodeMath"); d2.operation = 'MULTIPLY'; L(d1.outputs[0], d2.inputs[0]); L(rr.outputs[0], d2.inputs[1])
    d3 = N("ShaderNodeMath"); d3.operation = 'MULTIPLY'; d3.inputs[1].default_value = 6.0; L(d2.outputs[0], d3.inputs[0])
    vol = N("ShaderNodeVolumePrincipled")
    vol.inputs["Color"].default_value = (0.55, 0.78, 1.0, 1)
    vol.inputs["Emission Color"].default_value = (0.15, 0.45, 1.0, 1)
    L(d3.outputs[0], vol.inputs["Density"])
    es = N("ShaderNodeMath"); es.operation = 'MULTIPLY'; es.inputs[1].default_value = 2.5
    L(d2.outputs[0], es.inputs[0]); L(es.outputs[0], vol.inputs["Emission Strength"])
    L(vol.outputs[0], out.inputs["Volume"])
    return m


def _add_mist_and_dust(col, rng):
    # Névoa: cilindro baixo (o material escala com o objeto: coordenadas -1..1).
    bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=1, depth=2, location=(0, 0, 0.6))
    mist = bpy.context.active_object; mist.name = "BurstMist"; mist.scale = (4.2, 4.2, 0.75)
    mist.data.materials.append(_mist_material())
    for c in list(mist.users_collection): c.objects.unlink(mist)
    col.objects.link(mist)
    # Pó de gelo e faíscas: estilhaços minúsculos luminosos, densos junto à base, alguns no alto.
    dm = bpy.data.materials.get("BurstDust") or bpy.data.materials.new("BurstDust")
    dm.use_nodes = True; dn = dm.node_tree; dn.nodes.clear()
    e = dn.nodes.new("ShaderNodeEmission"); e.inputs["Color"].default_value = (0.6, 0.85, 1.0, 1); e.inputs["Strength"].default_value = 3.0
    o = dn.nodes.new("ShaderNodeOutputMaterial"); dn.links.new(e.outputs[0], o.inputs["Surface"])
    bm = bmesh.new()
    for i in range(320):
        a = rng.uniform(0, 2 * math.pi)
        d = 3.8 * rng.uniform(0.05, 1.0) ** 0.7
        z = min(rng.expovariate(1.6), 3.2)
        size = rng.uniform(0.008, 0.03) * (1.6 if rng.random() < 0.1 else 1.0)
        c = Vector((math.cos(a) * d, math.sin(a) * d, z))
        verts = [bm.verts.new(c + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * size) for k in range(4)]
        try:
            bm.faces.new(verts[:3]); bm.faces.new((verts[0], verts[2], verts[3]))
        except ValueError:
            pass
    dust = _obj("BurstDust", bm, col, dm)
    return mist, dust


def build_burst(seed=5):
    col = _collection(BURST)
    cube = bpy.data.objects.get("Cube")
    if cube:
        cube.hide_render = True
    for c in ("IceHero", "IceSpikes", "IceBurstPreview", ONE):
        if c in bpy.data.collections:
            bpy.data.collections[c].hide_render = True
            bpy.data.collections[c].hide_viewport = True
    ice, crack, frost, floor, human = materials()
    mat = spike_material()
    meshes = [_bake_spike(21 + i * 7, "BurstSpike%d" % i, col, mat, broad_up=(i % 2 == 0)) for i in range(5)]
    rng = random.Random(seed)

    # Picos: 3 altos perto do centro (quase direitos), 7 médios, 10 baixos quase deitados na borda.
    # (a malha já vem inclinada 30°: extra_lean negativo endireita, positivo deita)
    n = 0
    for i, ang in enumerate((100, 215, 330)):
        n += 1
        _place(col, "Tall%d" % i, meshes[i % 5], ang + rng.uniform(-15, 15), rng.uniform(0.4, 0.8),
               rng.uniform(-8, 6), rng.uniform(0.82, 0.95))
    for i in range(10):
        ang = i / 10 * 360 + rng.uniform(-14, 14) + 20
        _place(col, "Mid%d" % i, meshes[(i + 1) % 5], ang, rng.uniform(0.9, 1.9), rng.uniform(5, 25), rng.uniform(0.45, 0.7),
               spin=rng.uniform(-15, 15))
    for i in range(16):
        ang = i / 16 * 360 + rng.uniform(-10, 10)
        _place(col, "Low%d" % i, meshes[(i + 2) % 5], ang, rng.uniform(1.8, 3.3), rng.uniform(25, 50), rng.uniform(0.22, 0.42),
               z=-0.05, spin=rng.uniform(-20, 20))
    # Crosta de gelo no centro: placas grandes e grossas sobrepostas (a massa da base).
    for i in range(16):
        d = 1.6 * math.sqrt(rng.uniform(0.0, 1.0))
        a = rng.uniform(0, 2 * math.pi)
        size = rng.uniform(0.4, 0.8)
        ob = _obj("Crust%d" % i, _plate(rng, size, size * 0.35), col, mat)
        ob.matrix_world = (Matrix.Translation(Vector((math.cos(a) * d, math.sin(a) * d, -0.05)))
                           @ Matrix.Rotation(math.radians(rng.uniform(5, 25)), 4, Vector((-math.sin(a), math.cos(a), 0)))
                           @ Matrix.Rotation(rng.uniform(0, 6.28), 4, 'Z'))

    # Placas partidas no chão (mais densas perto do centro) e lascas no ar.
    for i in range(90):
        d = 3.9 * rng.uniform(0.05, 1.0) ** 0.8
        a = rng.uniform(0, 2 * math.pi)
        size = rng.uniform(0.12, 0.45) * (1.2 - 0.5 * d / 3.9)
        ob = _obj("Plate%d" % i, _plate(rng, size, size * 0.12), col, mat)
        ob.matrix_world = (Matrix.Translation(Vector((math.cos(a) * d, math.sin(a) * d, 0.0)))
                           @ Matrix.Rotation(math.radians(rng.uniform(0, 18)), 4, Vector((-math.sin(a), math.cos(a), 0)))
                           @ Matrix.Rotation(rng.uniform(0, 6.28), 4, 'Z'))
    for i in range(12):
        d = rng.uniform(1.2, 3.6)
        a = rng.uniform(0, 2 * math.pi)
        size = rng.uniform(0.07, 0.18)
        ob = _obj("Chunk%d" % i, _plate(rng, size, size * 0.6), col, mat)
        ob.matrix_world = (Matrix.Translation(Vector((math.cos(a) * d, math.sin(a) * d, rng.uniform(0.6, 2.6))))
                           @ Matrix.Rotation(rng.uniform(0, 6.28), 4, Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 1)).normalized()))

    # Núcleo luminoso: brilho no chão (gradiente radial) + uma esfera de energia baixa.
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 0, 0.01))
    gl = bpy.context.active_object; gl.name = "BurstFloorGlow"; gl.scale = (7.5, 7.5, 1)
    gl.data.materials.append(_floor_glow_material())

    _add_mist_and_dust(col, rng)
    bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
    fl = bpy.context.active_object; fl.name = "BurstFloor"
    fm = floor.copy(); fm.name = "BurstFloorMat"
    fm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.45
    fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.004, 0.006, 0.01, 1)
    fl.data.materials.append(fm)
    for ob in (gl, fl):
        for c in list(ob.users_collection): c.objects.unlink(ob)
        col.objects.link(ob)
    for name, loc, size, energy, color in [("BKeyL", (-6, -5, 10), 5, 110, (0.8, 0.9, 1.0)), ("BRim", (1, 7, 7), 6, 700, (0.55, 0.75, 1.0))]:
        ld = bpy.data.lights.new(name, 'AREA'); ld.energy = energy; ld.size = size; ld.color = color
        lo = bpy.data.objects.new(name, ld); col.objects.link(lo); lo.location = loc
        lo.rotation_euler = (Vector((0, 0, 1)) - lo.location).to_track_quat('-Z', 'Y').to_euler()
    return col


def render_burst(samples=48):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.samples = samples; sc.cycles.use_denoising = True
    sc.cycles.volume_step_rate = 2.0
    sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'
    w = sc.world.node_tree.nodes["Background"]; w.inputs[0].default_value = (0.006, 0.01, 0.02, 1)
    cam = bpy.data.objects["Camera"]; sc.camera = cam
    views = {
        "side": ((0.0, -11.5, 1.9), (0, 0, 1.0), 'PERSP', 38, (1280, 720)),
        "top": ((0.0, -0.01, 16.0), (0, 0, 0), 'ORTHO', 9.5, (900, 900)),
        "threequarter": ((5.5, -9.5, 6.5), (0, 0, 0.8), 'PERSP', 32, (1280, 720)),
    }
    out = {}
    for name, (loc, look, kind, lens, res) in views.items():
        cam.data.type = kind
        if kind == 'ORTHO':
            cam.data.ortho_scale = lens
        else:
            cam.data.lens = lens
        cam.location = Vector(loc)
        cam.rotation_euler = (Vector(look) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.resolution_x, sc.render.resolution_y = res
        p = CAP + r"\burst_%s.png" % name
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        out[name] = p
    glow = "format=gbrp,split[a][b];[b]curves=all='0/0 0.55/0 1/1',gblur=sigma=14[g];[a][g]blend=all_mode=screen:all_opacity=0.6"
    ref = CAP + r"\ref_ice_master.webp"
    # Lateral: vista lateral da folha | a nossa.  Topo: vista de topo da folha | a nossa.
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", ref, "-i", out["side"], "-filter_complex",
                    "[0]crop=760:520:776:180,scale=-1:720[r];[1]" + glow + "[v];[r][v]hstack", CAP + r"\burst_compare_side.png"], check=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", ref, "-i", out["top"], "-filter_complex",
                    "[0]crop=770:700:0:0,scale=-1:900[r];[1]" + glow + "[v];[r][v]hstack", CAP + r"\burst_compare_top.png"], check=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", out["threequarter"], "-vf", glow, CAP + r"\burst_threequarter_glow.png"], check=True)
