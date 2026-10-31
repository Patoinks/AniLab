"""Training dummy based on the generated dummy_concept_art.png.
Meters, Z up, front -Y, mesh pivots and root at ground origin.
Run in Blender: exec(compile(open(PATH,encoding='utf-8').read(),PATH,'exec'),
{'__file__':PATH,'__name__':'__main__'}).
Rebuilds only data tagged training_dummy_v1/v2. Preserves other scenes.
Produces .blend, three renders, deterministic side-by-side comparison, metrics.
No Unity edits or export. The concept includes a sword; the model does not.
"""
import json
import math
import random
import numpy as np
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
BLEND = ROOT / 'Tools/Blender/TrainingDummy.blend'
CAP = ROOT / 'Logs/cap'
OWNER = 'training_dummy_v2'


def owned(data):
    data['generator'] = OWNER
    return data


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def material(name, color, roughness=0.8):
    mat = owned(bpy.data.materials.new('TD_' + name))
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Roughness'].default_value = roughness
    return mat


def emission(name, color):
    mat = material(name, color)
    nodes = mat.node_tree.nodes
    nodes.clear()
    out = nodes.new('ShaderNodeOutputMaterial')
    shader = nodes.new('ShaderNodeEmission')
    shader.inputs[0].default_value = (*color, 1)
    mat.node_tree.links.new(shader.outputs[0], out.inputs['Surface'])
    return mat


def move_to(obj, collection, mat=None):
    owned(obj)
    for col in list(obj.users_collection):
        col.objects.unlink(obj)
    collection.objects.link(obj)
    if obj.data:
        owned(obj.data)
    if mat:
        obj.data.materials.append(mat)
    return obj


def mesh(name, vertices, faces, mat, col):
    data = owned(bpy.data.meshes.new(name + '_Mesh'))
    data.from_pydata(vertices, [], faces)
    data.update()
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(data)
    bm.free()
    obj = owned(bpy.data.objects.new(name, data))
    col.objects.link(obj)
    data.materials.append(mat)
    return obj


def box(name, loc, scale, mat, col, bevel=0.015):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    move_to(obj, col, mat)
    if bevel:
        mod = obj.modifiers.new('Soft_cut_edges', 'BEVEL')
        mod.width, mod.segments = min(bevel, min(scale)*0.40), 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
        mod = obj.modifiers.new('Weighted_normals', 'WEIGHTED_NORMAL')
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def loft(name, profile, mat, col, sides=12, squircle=False):
    verts = []
    for z, rx, ry, cx in profile:
        for i in range(sides):
            a = math.tau * i / sides
            x, y = math.cos(a), math.sin(a)
            if squircle:
                x = math.copysign(abs(x) ** 0.55, x)
                y = math.copysign(abs(y) ** 0.55, y)
            verts.append((cx + rx*x, ry*y, z))
    faces = [tuple(reversed(range(sides)))]
    for j in range(len(profile)-1):
        for i in range(sides):
            a, b = j*sides+i, j*sides+(i+1)%sides
            faces.append((a, b, b+sides, a+sides))
    faces.append(tuple((len(profile)-1)*sides+i for i in range(sides)))
    return mesh(name, verts, faces, mat, col)


def tube(name, points, radius, mat, col, sides=6, closed=False):
    points = [Vector(p) for p in points]
    verts = []
    for i, p in enumerate(points):
        prev = points[i-1] if i or closed else p
        nxt = points[(i+1)%len(points)] if i+1<len(points) or closed else p
        tangent = (nxt-prev).normalized()
        ref = Vector((0, 0, 1)) if abs(tangent.z)<0.9 else Vector((0, 1, 0))
        u = tangent.cross(ref).normalized()
        v = tangent.cross(u).normalized()
        for k in range(sides):
            a = math.tau*k/sides
            verts.append(p + radius*(u*math.cos(a)+v*math.sin(a)))
    faces = []
    for j in range(len(points) if closed else len(points)-1):
        for k in range(sides):
            a, b = j*sides+k, j*sides+(k+1)%sides
            c, d = ((j+1)%len(points))*sides+(k+1)%sides, ((j+1)%len(points))*sides+k
            faces.append((a,b,c,d))
    if not closed:
        faces += [tuple(reversed(range(sides))), tuple((len(points)-1)*sides+k for k in range(sides))]
    obj = mesh(name, verts, faces, mat, col)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def ellipse(name, center, rx, ry, radius, mat, col, plane='XY', segments=24):
    cx, cy, cz = center
    points = []
    for i in range(segments):
        a = math.tau*i/segments
        if plane == 'XY':
            points.append((cx+rx*math.cos(a), cy+ry*math.sin(a), cz))
        else:
            points.append((cx, cy+rx*math.cos(a), cz+ry*math.sin(a)))
    return tube(name, points, radius, mat, col, closed=True)


def paint_ring(name, x, y, z, outer, inner, mat, col):
    verts = [(x+r*math.cos(i*math.tau/32),y,z+r*math.sin(i*math.tau/32))
             for r in (outer,inner) for i in range(32)]
    faces = [(i,(i+1)%32,(i+1)%32+32,i+32) for i in range(32)]
    return mesh(name,verts,faces,mat,col)



def clean_generated():
    for obj in list(bpy.data.objects):
        if obj.get('generator') in ('training_dummy_v1', OWNER):
            bpy.data.objects.remove(obj, do_unlink=True)
    for group in (bpy.data.scenes, bpy.data.collections, bpy.data.meshes,
                  bpy.data.curves, bpy.data.cameras, bpy.data.lights,
                  bpy.data.materials, bpy.data.worlds, bpy.data.images):
        for data in list(group):
            if data.get('generator') in ('training_dummy_v1', OWNER) and (isinstance(data,bpy.types.Scene) or data.users == 0):
                group.remove(data)


def srgb(rgb):
    return tuple(v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4 for v in rgb)


def palette(name, rgb):
    return material(name, srgb(tuple(v/255 for v in rgb)))


def patch_surface(name, coords, mat, col):
    return mesh(name, coords, [tuple(range(len(coords)))], mat, col)


def spike(name, a, b, width, mat, col):
    a,b = Vector(a),Vector(b)
    direction=(b-a).normalized()
    u=direction.cross(Vector((0,1,0)))
    if u.length < .01:
        u=direction.cross(Vector((1,0,0)))
    u.normalize()
    v=direction.cross(u)
    mid=a.lerp(b,.58)+u*width*.5
    verts=[a+width*.8*u,a+width*.4*v,a-width*.8*u,
           mid+width*u,mid+width*.5*v,mid-width*u,b]
    return mesh(name,verts,[(0,1,4,3),(1,2,5,4),(2,0,3,5),
                           (3,4,6),(4,5,6),(5,3,6),(2,1,0)],mat,col)


def make_grip(col, wood, end):
    # Four squared wooden fingers around a vertical unobstructed grip socket.
    cx,cy=-.585,-.018
    for k in range(4):
        z=1.18+k*.045
        outer=[(-.070,-.080),(.070,-.080),(.070,.080),(-.070,.080)]
        inner=[(-.023,-.025),(.023,-.025),(.023,.025),(-.023,.025)]
        verts=[(cx+x,cy+y,h) for h in (z,z+.039) for ring in (outer,inner) for x,y in ring]
        faces=[]
        for i in range(4):
            j=(i+1)%4
            faces.extend([(i,j,j+8,i+8),(i+4,i+12,j+12,j+4),
                          (i+8,j+8,j+12,i+12),(i,i+4,j+4,j)])
        obj=mesh('Hand_R_Wooden_Finger_%02d'%k,verts,faces,wood,col)
        obj.data.materials.append(end)
        for p in obj.data.polygons:
            if p.index%4==2:p.material_index=1
    return (cx,cy,1.267)


def build_dummy():
    clean_generated()
    rng=random.Random(31)
    scene=owned(bpy.data.scenes.new('TrainingDummy_Model'))
    bpy.context.window.scene=scene
    scene.unit_settings.system='METRIC'
    scene.unit_settings.scale_length=1
    col=owned(bpy.data.collections.new('TrainingDummy'))
    scene.collection.children.link(col)
    wood=palette('Wood_Umber',(88,65,45))
    woodlight=palette('Wood_Cut',(126,94,62))
    wooddark=palette('Wood_Cracks',(52,39,29))
    straw=palette('Straw_Ochre',(184,133,58))
    strawlight=palette('Straw_Gold',(205,160,82))
    strawdark=palette('Straw_Shadow',(143,98,38))
    sack=palette('Burlap_Tan',(170,140,101))
    sacklight=palette('Burlap_Light',(184,155,115))
    sackdark=palette('Burlap_Shade',(158,127,89))
    rope=palette('Hemp_Rope',(139,105,64))
    ropelight=palette('Hemp_Twist',(176,137,85))
    thread=palette('Thread_Umber',(57,39,25))
    red=palette('Target_BrickRed',(139,67,49))
    patchmat=palette('Patch_LeatherBrown',(125,81,55))
    iron=palette('Iron_Nail',(62,60,53))
    root=owned(bpy.data.objects.new('Dummy_Root',None))
    col.objects.link(root)
    root.empty_display_size=.16
    root['front']='-Y'
    root['reference']='//../../Logs/cap/dummy_concept_art.png'
    root['class']='Training dummy - wood and burlap prop'
    # Both crossed planks meet the ground. Central overlap is hidden by the post.
    for name,angle in [('Base_Cross_A',math.pi/4),('Base_Cross_B',-math.pi/4)]:
        obj=box(name,(0,0,.0575),(1.17,.18,.115),wood,col,.020)
        obj.rotation_euler.z=angle
        for side in (-1,1):
            x,y=side*.32*math.cos(angle),side*.32*math.sin(angle)
            nail=box(name+'_Nail_'+str(side),(x,y,.118),(.039,.039,.015),iron,col,.005)
            nail.rotation_euler.z=angle
        for side in (-1,1):
            for j in range(3):
                x0=side*(.14+j*.025);x1=side*(.53-j*.035)
                off=-.057+j*.047
                def rot(x,y,z):
                    return (x*math.cos(angle)-y*math.sin(angle),x*math.sin(angle)+y*math.cos(angle),z)
                patch_surface(name+'_Grain_%s_%s'%(side,j),
                    [rot(x0,off,.116),rot(x1,off+.007,.116),rot(x1-.035*side,off+.015,.116)],
                    woodlight if j%2 else wooddark,col)
    post=box('Post_Central',(0,.015,.79),(.216,.210,1.47),wood,col,.018)
    for j in range(7):
        x=-.087+j*.028
        z=.14+rng.random()*.12
        patch_surface('Post_Front_Grain_%02d'%j,
            [(x,-.091,z),(x+.010,-.091,.55),(x+.004,-.091,.85),(x-.008,-.091,.48)],
            woodlight if j%3 else wooddark,col)
    # Angular, solid crossbar rather than anatomical limbs.
    arm=tube('Arms_Wooden_Crossbar',[(-.54,.005,1.29),(.54,.005,1.29)],.085,wood,col,sides=8)
    for p in arm.data.polygons:p.use_smooth=False
    for side in (-1,1):
        x=side*.542
        tube('Arm_Endgrain_'+str(side),[(x,.005,1.29),(x+side*.004,.005,1.29)],.073,woodlight,col,sides=8)
        for z in (1.26,1.31):
            patch_surface('Arm_Grain_'+str(side)+str(z),
                [(side*.29,-.078,z),(side*.51,-.078,z+.009),(side*.50,-.079,z+.017)],wooddark,col)
        for j in range(2):
            ellipse('Wrist_Rope_'+str(side)+'_'+str(j),(side*(.464+j*.026),.005,1.29),
                    .092,.092,.012,rope,col,plane='YZ',segments=16)
    bodyprofile=[(.77,.230,.158,0),(.83,.285,.185,0),(.94,.265,.186,0),
                 (1.005,.239,.173,0),(1.09,.278,.198,0),(1.32,.285,.194,0),
                 (1.425,.260,.173,0),(1.465,.175,.125,0)]
    headprofile=[(1.535,.095,.085,0),(1.57,.154,.127,0),
                 (1.63,.196,.155,-.005),(1.77,.218,.162,-.006),
                 (1.885,.213,.158,-.008),(1.935,.181,.133,-.009),
                 (1.95,.125,.085,-.005)]
    for name,profile in [('Torso_Burlap',bodyprofile),('Head_Burlap_Sack',headprofile)]:
        obj=loft(name,profile,sack,col,sides=20,squircle=True)
        obj.data.materials.append(sacklight);obj.data.materials.append(sackdark)
        for v in obj.data.vertices:
            if name=='Torso_Burlap' and v.co.z<.79:
                v.co.z+=rng.uniform(-.024,.024)
            if .78<v.co.z<1.94:
                v.co.x+=rng.uniform(-.005,.005)
                v.co.y+=rng.uniform(-.004,.004)
                v.co.z+=rng.uniform(-.006,.006)
            if name=='Head_Burlap_Sack' and 1.62<v.co.z<1.91:
                # Soften the rectangular corners while preserving the broad face.
                v.co.y*=1.03
        for p in obj.data.polygons:
            r=rng.random()
            p.material_index=1 if r<.12 else (2 if r>.89 else 0)
    # Ragged collar flaps around the tied sack.
    for i in range(16):
        a=math.tau*i/16;b=a+math.tau/16
        r=.20+rng.uniform(-.025,.025)
        patch_surface('Neck_Ragged_Collar_%02d'%i,
            [(math.cos(a)*.10,math.sin(a)*.083,1.54),
             (math.cos(a)*r,math.sin(a)*r*.72,1.455+rng.uniform(-.018,.012)),
             (math.cos(b)*r,math.sin(b)*r*.72,1.46+rng.uniform(-.01,.014)),
             (math.cos(b)*.10,math.sin(b)*.083,1.54)],sacklight if i%3 else sackdark,col)
    # Dense but deliberately broad straw tips: six triangles per blade.
    for i in range(64):
        a=math.tau*i/64+rng.uniform(-.035,.035)
        r=.24+rng.uniform(-.018,.018)
        a0=(math.cos(a)*r,math.sin(a)*.16,.85+rng.uniform(-.025,.02))
        a1=(math.cos(a+.07)*(r+rng.uniform(-.005,.08)),math.sin(a+.07)*(.19+rng.uniform(0,.045)),.72+rng.uniform(-.065,.055))
        spike('Straw_Hem_%02d'%i,a0,a1,rng.uniform(.010,.020),[straw,strawlight,strawdark][i%3],col)
    for side in (-1,1):
        for i in range(27):
            a=math.tau*i/27+rng.uniform(-.08,.08)
            z=1.30+math.sin(a)*.12
            a0=(side*.245,math.cos(a)*.145,z)
            a1=(side*(.29+rng.random()*.085),math.cos(a)*(.16+rng.random()*.035),z+math.sin(a)*rng.uniform(.02,.085)+rng.uniform(-.025,.025))
            spike('Straw_Shoulder_%s_%02d'%(side,i),a0,a1,rng.uniform(.009,.018),[strawlight,straw,strawdark][i%3],col)
    # Squared rope loops conform to the squarish fabric volume.
    for zone,z,rx,ry in [('Waist_A',.994,.249,.184),('Waist_B',1.018,.249,.184),
                         ('Neck_A',1.524,.125,.104),('Neck_B',1.545,.124,.104)]:
        n=32
        pts=[]
        for i in range(n):
            a=math.tau*i/n
            pts.append((rx*math.copysign(abs(math.cos(a))**.55,math.cos(a)),
                        ry*math.copysign(abs(math.sin(a))**.55,math.sin(a)),z))
        tube('Rope_'+zone,pts,.012,rope,col,sides=5,closed=True)
        # One fine helix strand makes the broad rope read as twisted hemp.
        pts2=[]
        for i in range(64):
            a=math.tau*i/64
            x=rx*math.copysign(abs(math.cos(a))**.55,math.cos(a))
            y=ry*math.copysign(abs(math.sin(a))**.55,math.sin(a))
            pts2.append((x+math.cos(a)*.010*math.cos(a*16),y+math.sin(a)*.010*math.cos(a*16),z+.010*math.sin(a*16)))
        tube('Rope_Twist_'+zone,pts2,.0035,ropelight,col,sides=3,closed=True)
    # Flat painted pigment in the fabric material, never hovering decal geometry.
    for fabric in (sack,sacklight,sackdark):
        nodes=fabric.node_tree.nodes;links=fabric.node_tree.links
        geom=nodes.new('ShaderNodeTexCoord');geom.object=root
        xyz=nodes.new('ShaderNodeSeparateXYZ')
        links.new(geom.outputs['Object'],xyz.inputs[0])
        def calc(op,a,b=0):
            n=nodes.new('ShaderNodeMath');n.operation=op
            for i,value in enumerate((a,b)):
                if isinstance(value,(int,float)):n.inputs[i].default_value=value
                else:links.new(value,n.inputs[i])
            return n.outputs[0]
        x2=calc('MULTIPLY',xyz.outputs['X'],xyz.outputs['X'])
        dz=calc('SUBTRACT',xyz.outputs['Z'],1.19)
        radius=calc('SQRT',calc('ADD',x2,calc('MULTIPLY',dz,dz)))
        ring=calc('MULTIPLY',calc('LESS_THAN',radius,.169),calc('GREATER_THAN',radius,.122))
        mask=calc('MULTIPLY',calc('MAXIMUM',ring,calc('LESS_THAN',radius,.059)),
                  calc('LESS_THAN',xyz.outputs['Y'],-.10))
        mix=nodes.new('ShaderNodeMixRGB')
        mix.inputs[1].default_value=fabric.diffuse_color
        mix.inputs[2].default_value=red.diffuse_color
        links.new(mask,mix.inputs[0])
        links.new(mix.outputs[0],nodes.get('Principled BSDF').inputs['Base Color'])
    # Sewn face on the large sack. Front-facing detail follows its convex contour.
    def face_y(x,z):
        return -.164*max(.01,1-(abs(x+.006)/.22)**(2/.55))**(.55/2)-.010
    for side,x,z in [('R',-.100,1.755),('L',.087,1.751)]:
        for j,sign in enumerate((-1,1)):
            a=(x-.033,face_y(x-.033,z-.033),z-.033)
            b=(x+.033,face_y(x+.033,z+.033*sign),z+.033*sign)
            if sign<0:a=(x-.033,face_y(x-.033,z+.033),z+.033)
            tube('Eye_'+side+'_Stitch_'+str(j),[a,b],.010,thread,col,sides=5)
    mouth=[]
    for i in range(9):
        x=-.122+i*.029
        z=1.628+.045*((x+.006)/.122)**2
        mouth.append((x,face_y(x,z)-.001,z))
    tube('Face_Sewn_Smile',mouth,.0055,thread,col,sides=5)
    for i in range(7):
        x=-.107+i*.034
        z=1.628+.045*((x+.006)/.122)**2
        tube('Mouth_Cross_Stitch_%02d'%i,[(x-.003,face_y(x,z)-.005,z-.012),
             (x+.004,face_y(x,z)-.005,z+.013)],.0045,thread,col,sides=4)
    # Brown patches, with visible light stitches crossing the edges.
    for name,x,z,w,h,yy in [('Head',.140,1.867,.102,.126,-.161),
                            ('Hem',.160,.891,.115,.133,-.188)]:
        coords=[(x-w/2,yy,z-h/2),(x+w/2,yy+.014,z-h/2+.006),
                (x+w/2-.008,yy+.008,z+h/2),(x-w/2-.010,yy,z+h/2-.009)]
        patch_surface('Patch_'+name,coords,patchmat,col)
        for side in (-1,1):
            for j in range(4):
                xx=x+side*w/2;zz=z-h*.36+j*h*.24
                tube('Patch_'+name+'_Edge_'+str(side)+'_'+str(j),
                     [(xx-.009,yy-.007,zz-.004),(xx+.009,yy-.007,zz+.004)],.0038,ropelight,col,sides=4)
        for side in (-1,1):
            for j in range(3):
                xx=x-w*.32+j*w*.32;zz=z+side*h/2
                tube('Patch_'+name+'_Top_'+str(side)+'_'+str(j),
                     [(xx,yy-.007,zz-.009),(xx+.004,yy-.007,zz+.009)],.0038,ropelight,col,sides=4)
    # Large side seam, legible in profile.
    tube('Head_Side_Seam',[(.128,-.131,1.57),(.196,-.102,1.66),
         (.211,-.099,1.80),(.204,-.101,1.91)],.0035,thread,col,sides=4)
    for z in (1.64,1.69,1.74,1.79,1.84):
        tube('Head_Side_Stitch_'+str(z),[(.197,-.120,z-.004),(.215,-.077,z+.005)],.003,ropelight,col,sides=4)
    handpos=make_grip(col,wood,woodlight)
    socket=owned(bpy.data.objects.new('Hand_R',None));col.objects.link(socket)
    socket.location=handpos
    socket.empty_display_type='ARROWS';socket.empty_display_size=.12
    socket['purpose']='Centro da pega; lamina +Z; direita anatomica = -X'
    socket['opening_m']=[.046,.05]
    for obj in col.objects:
        if obj!=root:obj.parent=root
    bpy.context.view_layer.update()
    for obj in col.objects:
        if obj.type=='MESH':
            obj.data.transform(obj.matrix_world)
            obj.matrix_world=Matrix.Identity(4)
    return scene,col,root

def validate(col, root):
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    points, tris = [],0
    for obj in col.objects:
        if obj.type != 'MESH':
            continue
        evaluated = obj.evaluated_get(dg)
        data = evaluated.to_mesh()
        data.calc_loop_triangles()
        tris += len(data.loop_triangles)
        points.extend(obj.matrix_world@v.co for v in data.vertices)
        assert all(t.area>1e-11 for t in data.loop_triangles),obj.name
        evaluated.to_mesh_clear()
        assert obj.location.length < 1e-6, obj.name
    bottom,top = min(p.z for p in points),max(p.z for p in points)
    hand = bpy.data.objects['Hand_R']
    metrics = {'height_m':round(top-bottom,6),'z_min_m':round(bottom,6),
               'z_max_m':round(top,6),'triangles':tris,
               'Hand_R_world_m':[round(v,6) for v in hand.matrix_world.translation],
               'Hand_R_rotation_degrees':[0,0,0],'blade_axis':'+Z','front':'-Y',
               'pivot_world_m':list(root.matrix_world.translation),
               'mesh_parts':sum(o.type=='MESH' for o in col.objects)}
    assert 1.9 <= top-bottom <= 2.0,metrics
    assert abs(bottom)<1e-6 and tris<8000,metrics
    root['metrics_json'] = json.dumps(metrics)
    CAP.mkdir(parents=True,exist_ok=True)
    (CAP/'dummy_metrics.json').write_text(json.dumps(metrics,indent=2),encoding='utf-8')
    return metrics


def point_at(obj,target):
    obj.rotation_euler = (Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()



def configure_scene(scene):
    scene.render.engine='CYCLES'
    scene.cycles.samples=32
    scene.cycles.use_denoising=True
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.image_settings.color_mode='RGB'
    scene.view_settings.view_transform='Standard'
    scene.view_settings.look='None'
    scene.view_settings.exposure=0
    scene.view_settings.gamma=1
    world=owned(bpy.data.worlds.new('TD_Studio_World'))
    world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.56,.53,.48,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.7
    scene.world=world


def setup_studio(scene):
    configure_scene(scene)
    studio=owned(bpy.data.collections.new('TrainingDummy_Studio'))
    scene.collection.children.link(studio)
    mat=palette('Studio_Neutral',(192,186,174))
    shader=mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Emission Color'].default_value=(*srgb((192/255,186/255,174/255)),1)
    shader.inputs['Emission Strength'].default_value=.65
    mesh('Studio_Floor',[(-200,-200,-.001),(200,-200,-.001),(200,200,-.001),(-200,200,-.001)],
         [(0,1,2,3)],mat,studio)
    for name,loc,power,size in [('Key',(-3,-4,6),450,4),('Fill',(4,-1,4),200,5)]:
        data=owned(bpy.data.lights.new('TD_'+name,'AREA'))
        data.energy=power;data.size=size
        obj=owned(bpy.data.objects.new('TD_'+name,data));studio.objects.link(obj)
        obj.location=loc;point_at(obj,(0,0,1.1))
    camera=owned(bpy.data.objects.new('TD_Camera',owned(bpy.data.cameras.new('TD_Camera'))))
    studio.objects.link(camera)
    camera.data.type='ORTHO';camera.data.ortho_scale=2.52
    scene.camera=camera
    textdata=owned(bpy.data.curves.new('TD_View_Label','FONT'))
    textdata.align_x='CENTER';textdata.size=.052
    label=owned(bpy.data.objects.new('TD_View_Label',textdata))
    studio.objects.link(label)
    label.parent=camera;label.location=(0,-1.12,-3)
    textdata.materials.append(emission('View_Label_Ink',(.09,.075,.06)))
    return camera


def camera_view(camera,angle):
    a=math.radians(angle)
    camera.location=(-.035+6*math.sin(a),-6*math.cos(a),.94+6*math.tan(math.radians(9)))
    point_at(camera,(-.035,0,.94))
    camera.data.ortho_scale=2.52


def render_panels(scene,camera):
    scene.render.resolution_x,scene.render.resolution_y=512,1024
    for name,angle in [('front',0),('three_quarter',25),('profile',90)]:
        bpy.data.objects['TD_View_Label'].data.body={'front':'FRONT','three_quarter':'3/4','profile':'SIDE'}[name]
        camera_view(camera,angle)
        scene.render.filepath=str(CAP/('dummy_'+name+'.png'))
        bpy.ops.render.render(write_still=True)
    camera_view(camera,25)
    bpy.data.objects['TD_View_Label'].data.body='3/4'


def build_comparison():
    # Mechanical layout only: original generated art remains unchanged on the left.
    ref=owned(bpy.data.images.load(str(CAP/'dummy_concept_art.png'),check_existing=False))
    ref.name='TD_Concept_Art_Reference';ref.pack();ref.use_fake_user=True
    assert tuple(ref.size)==(1536,1024),tuple(ref.size)
    canvas=np.ones((1024,3072,4),dtype=np.float32)
    buf=np.empty(1536*1024*4,dtype=np.float32)
    ref.pixels.foreach_get(buf)
    canvas[:,:1536]=buf.reshape(1024,1536,4)
    for i,name in enumerate(('front','three_quarter','profile')):
        img=owned(bpy.data.images.load(str(CAP/('dummy_'+name+'.png')),check_existing=False))
        buf=np.empty(512*1024*4,dtype=np.float32)
        img.pixels.foreach_get(buf)
        canvas[:,1536+i*512:1536+(i+1)*512]=buf.reshape(1024,512,4)
    out=owned(bpy.data.images.new('TD_Concept_versus_Model',width=3072,height=1024,alpha=False))
    out.pixels.foreach_set(canvas.ravel())
    out.filepath_raw=str(CAP/'dummy_compare.png');out.file_format='PNG';out.save()
    return str(CAP/'dummy_compare.png')


def save_model(scene,root):
    bpy.context.window.scene=scene
    activate(root)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_perspective='CAMERA'
                area.spaces.active.shading.type='MATERIAL'
    text=bpy.data.texts.get('training_dummy.py') or bpy.data.texts.new('training_dummy.py')
    text.clear();text.write(Path(__file__).read_text(encoding='utf-8'))
    BLEND.parent.mkdir(parents=True,exist_ok=True)
    old_versions=bpy.context.preferences.filepaths.save_version
    bpy.context.preferences.filepaths.save_version=0
    try:
        bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    finally:
        bpy.context.preferences.filepaths.save_version=old_versions


def main(render=True):
    scene,col,root=build_dummy()
    metrics=validate(col,root)
    metrics['concept']='Logs/cap/dummy_concept_art.png'
    metrics['view_transform']='Standard'
    metrics['sword_mesh']=False
    camera=setup_studio(scene)
    if render:
        render_panels(scene,camera)
        build_comparison()
    else:
        camera_view(camera,25)
    save_model(scene,root)
    (CAP/'dummy_metrics.json').write_text(json.dumps(metrics,indent=2),encoding='utf-8')
    return metrics


if __name__=='__main__':
    main()
