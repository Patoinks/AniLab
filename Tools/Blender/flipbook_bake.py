# Exportador de flipbooks de VFX do AnimLab (Blender 5.x, sem interface).
#
#   blender.exe -b --factory-startup -P Tools/Blender/flipbook_bake.py -- <nome> [frames=64] [res=256] [grid=8]
#
# 1. Simula fogo + fumo com Mantaflow (domínio de gás, emissor esférico contínuo).
# 2. Renderiza os frames em Cycles, fundo transparente, câmara ortográfica:
#    RGB = intensidade (emissão do fogo, em cinzento -> gradient map no Unity), A = cobertura (fumo + fogo).
# 3. Junta os frames numa folha grid×grid (frame 0 em cima à esquerda, como o shader AnimLab/VFX lê).
# 4. Calcula os motion vectors entre frames consecutivos por fluxo ótico (Lucas-Kanade denso, numpy)
#    e grava-os numa folha RG (0,5 = parado), com a força em <nome>.json para o material.
# Saída: Assets/KonoSuba/VFX/Flipbooks/<nome>.png, Assets/KonoSuba/VFX/Motion/<nome>_MV.png e <nome>.json
# (a importação automática do Unity aplica as definições pela pasta).

import json
import math
import os
import sys
import tempfile

import bpy
import numpy as np

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
NAME = args[0] if args else "Fire_Loop_8x8"
opts = dict(a.split("=") for a in args[1:])
FRAMES = int(opts.get("frames", 64))
RES = int(opts.get("res", 256))
GRID = int(opts.get("grid", 8))
WARMUP = 36  # frames de simulação antes de começar a gravar (o fogo já está formado)

tmp = tempfile.mkdtemp(prefix="animlab_flipbook_")
print("Pasta temporária:", tmp)

# ---------------------------------------------------------------- Cena
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.frame_start = 1
scene.frame_end = WARMUP + FRAMES + 1

bpy.ops.mesh.primitive_cube_add(size=2.0, location=(0.0, 0.0, 1.0))
domain = bpy.context.active_object
domain.name = "Dominio"
mod = domain.modifiers.new("Fluid", "FLUID")
mod.fluid_type = "DOMAIN"
ds = mod.domain_settings
ds.domain_type = "GAS"
ds.resolution_max = 96
ds.cache_type = "ALL"
ds.cache_directory = os.path.join(tmp, "cache")
ds.cache_frame_start = 1
ds.cache_frame_end = scene.frame_end
ds.vorticity = 0.5
ds.use_dissolve_smoke = True
ds.dissolve_speed = 30
ds.burning_rate = 0.7
ds.flame_smoke = 0.5
ds.flame_vorticity = 0.6
ds.alpha = 0.6   # flutuação do fumo
ds.beta = 1.8    # flutuação do calor

bpy.ops.mesh.primitive_uv_sphere_add(radius=0.34, location=(0.0, 0.0, 0.22))
emitter = bpy.context.active_object
emitter.scale = (1.0, 1.0, 0.35)  # emissor largo e achatado: base da chama larga, sem bola visível
emitter.name = "Emissor"
fmod = emitter.modifiers.new("Fluid", "FLUID")
fmod.fluid_type = "FLOW"
fs = fmod.flow_settings
fs.flow_type = "BOTH"
fs.flow_behavior = "INFLOW"
fs.flow_source = "MESH"
fs.fuel_amount = 1.3
fs.temperature = 1.6
fs.surface_distance = 0.6
emitter.hide_render = True

print("A simular...")
with bpy.context.temp_override(scene=scene, active_object=domain, object=domain, selected_objects=[domain]):
    bpy.ops.fluid.bake_all()

# Material do volume: densidade e chama do Mantaflow; emissão branca (cinzento) = calor.
mat = bpy.data.materials.new("Volume")
mat.use_nodes = True
nt = mat.node_tree
nt.nodes.clear()
out = nt.nodes.new("ShaderNodeOutputMaterial")
vol = nt.nodes.new("ShaderNodeVolumePrincipled")
dens = nt.nodes.new("ShaderNodeAttribute"); dens.attribute_name = "density"
flame = nt.nodes.new("ShaderNodeAttribute"); flame.attribute_name = "flame"
mul_d = nt.nodes.new("ShaderNodeMath"); mul_d.operation = "MULTIPLY"; mul_d.inputs[1].default_value = 3.0
mul_f = nt.nodes.new("ShaderNodeMath"); mul_f.operation = "MULTIPLY"; mul_f.inputs[1].default_value = 3.0
add_d = nt.nodes.new("ShaderNodeMath"); add_d.operation = "ADD"
nt.links.new(dens.outputs["Fac"], add_d.inputs[0])
nt.links.new(flame.outputs["Fac"], add_d.inputs[1])
nt.links.new(add_d.outputs[0], mul_d.inputs[0])
nt.links.new(flame.outputs["Fac"], mul_f.inputs[0])
nt.links.new(mul_d.outputs[0], vol.inputs["Density"])
nt.links.new(mul_f.outputs[0], vol.inputs["Emission Strength"])
vol.inputs["Color"].default_value = (0.0, 0.0, 0.0, 1.0)
vol.inputs["Emission Color"].default_value = (1.0, 1.0, 1.0, 1.0)
vol.inputs["Blackbody Intensity"].default_value = 0.0
nt.links.new(vol.outputs["Volume"], out.inputs["Volume"])
domain.data.materials.append(mat)

world = bpy.data.worlds.new("Mundo")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0
scene.world = world

cam_data = bpy.data.cameras.new("Camara")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 1.7
cam = bpy.data.objects.new("Camara", cam_data)
scene.collection.objects.link(cam)
cam.location = (0.0, -6.0, 0.9)
cam.rotation_euler = (math.radians(90.0), 0.0, 0.0)
scene.camera = cam

r = scene.render
r.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.cycles.volume_bounces = 0
scene.cycles.volume_step_rate = 1.0
r.resolution_x = r.resolution_y = RES
r.resolution_percentage = 100
r.film_transparent = True
# EXR: linear e pré-multiplicado, sem a transformação de vista a cortar a emissão.
r.image_settings.file_format = "OPEN_EXR"
r.image_settings.color_mode = "RGBA"
r.image_settings.color_depth = "16"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"

# ---------------------------------------------------------------- Render
frames = []
for i in range(FRAMES):
    f = WARMUP + i
    scene.frame_set(f)
    path = os.path.join(tmp, "frame_%03d.exr" % i)
    r.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(RES, RES, 4)  # linhas de baixo para cima
    bpy.data.images.remove(img)
    frames.append(px)
    print("Frame %d/%d" % (i + 1, FRAMES))

# Intensidade: emissão pré-multiplicada normalizada pelo percentil 99,9 de todos os frames, com uma curva
# que guarda o branco para o núcleo (o gradient map dá bordas escuras e centro quente); cobertura no alpha.
peak = np.percentile(np.concatenate([f[..., :3].max(axis=2).ravel() for f in frames]), 99.9)
for f in frames:
    i = np.clip(f[..., :3].max(axis=2) / max(peak, 1e-6), 0.0, 1.0) ** 1.3
    f[..., 0] = f[..., 1] = f[..., 2] = i
    f[..., 3] = np.clip(f[..., 3], 0.0, 1.0)

# ---------------------------------------------------------------- Folha de frames
size = RES * GRID
sheet = np.zeros((size, size, 4), dtype=np.float32)
for k, px in enumerate(frames):
    col, row_from_top = k % GRID, k // GRID
    row = GRID - 1 - row_from_top
    sheet[row * RES:(row + 1) * RES, col * RES:(col + 1) * RES] = px


def save(array, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    h, w = array.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.pixels.foreach_set(np.clip(array, 0.0, 1.0).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


# ---------------------------------------------------------------- Motion vectors (Lucas-Kanade denso)
def box(a, radius):
    # Média numa janela (2r+1)² por somas acumuladas.
    p = np.pad(a, radius + 1, mode="edge")
    c = p.cumsum(0).cumsum(1)
    n = 2 * radius + 1
    s = c[n:, n:] - c[:-n, n:] - c[n:, :-n] + c[:-n, :-n]
    return s[:a.shape[0], :a.shape[1]] / (n * n)


def flow(a, b):
    a, b = box(a, 1), box(b, 1)
    ix = (np.roll(a, -1, 1) - np.roll(a, 1, 1)) * 0.5
    iy = (np.roll(a, -1, 0) - np.roll(a, 1, 0)) * 0.5  # +y = para cima (linhas de baixo para cima)
    it = b - a
    w = 5
    sxx, syy, sxy = box(ix * ix, w), box(iy * iy, w), box(ix * iy, w)
    sxt, syt = box(ix * it, w), box(iy * it, w)
    det = sxx * syy - sxy * sxy
    ok = det > 1e-7
    u = np.where(ok, (-syy * sxt + sxy * syt) / np.where(ok, det, 1.0), 0.0)
    v = np.where(ok, (sxy * sxt - sxx * syt) / np.where(ok, det, 1.0), 0.0)
    return np.clip(box(u, 3), -6, 6), np.clip(box(v, 3), -6, 6)


def signal(px):
    # Seguir a forma inteira: cobertura + intensidade.
    return px[..., 3] * 0.6 + px[..., 0] * 0.4


mv = np.zeros((size, size, 2), dtype=np.float32)
for k in range(FRAMES):
    u, v = flow(signal(frames[k]), signal(frames[(k + 1) % FRAMES]))
    col, row = k % GRID, GRID - 1 - k // GRID
    mv[row * RES:(row + 1) * RES, col * RES:(col + 1) * RES, 0] = u
    mv[row * RES:(row + 1) * RES, col * RES:(col + 1) * RES, 1] = v

mv_uv = mv / size                       # pixels da folha -> UV da folha
strength = float(max(np.abs(mv_uv).max(), 1e-6) * 1.05)
mv_img = np.zeros((size, size, 4), dtype=np.float32)
mv_img[..., 0:2] = mv_uv / strength * 0.5 + 0.5
mv_img[..., 3] = 1.0

vfx = os.path.join(PROJECT, "Assets", "KonoSuba", "VFX")
save(sheet, os.path.join(vfx, "Flipbooks", NAME + ".png"))
save(mv_img, os.path.join(vfx, "Motion", NAME + "_MV.png"))
with open(os.path.join(vfx, "Flipbooks", NAME + ".json"), "w") as f:
    json.dump({"grid": [GRID, GRID], "frames": FRAMES, "fps": 30, "motionStrength": strength, "resolution": RES}, f, indent=2)
print("Flipbook pronto:", NAME, "força dos motion vectors:", strength)
