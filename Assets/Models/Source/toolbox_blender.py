import bpy, bmesh, math, sys
from mathutils import Vector

bpy.ops.wm.read_factory_settings(use_empty=True)

W, D, H, LID = 0.56, 0.30, 0.17, 0.07

def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1)
    return m

SHELL = mat("Shell", (0.62, 0.36, 0.12))
TRIM = mat("Trim", (0.16, 0.15, 0.14))
METAL = mat("Metal", (0.55, 0.55, 0.52))
INNER = mat("Inner", (0.09, 0.08, 0.08))

def box(bm, center, size, material_index, bevel=0.0, segments=1):
    r = bmesh.ops.create_cube(bm, size=1.0)
    verts = r["verts"]
    for v in verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])) + Vector(center)
    faces = {f for v in verts for f in v.link_faces}
    for f in faces:
        f.material_index = material_index
    if bevel > 0:
        edges = list({e for v in verts for e in v.link_edges})
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=segments, affect='EDGES', profile=0.5)
    return faces

def obj(name, build, mats, origin=(0, 0, 0)):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    build(bm)
    bmesh.ops.translate(bm, verts=bm.verts, vec=-Vector(origin))
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    for m in mats:
        me.materials.append(m)
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    o.location = origin
    return o

def tray(bm, z0, height, open_top, wall=0.014):
    # Hollow shell: a slab plus four walls, so the open side shows the inside.
    slab_z = z0 + (wall / 2 if open_top else height - wall / 2)
    box(bm, (0, 0, slab_z), (W, D, wall), 0, bevel=0.006)
    wz = z0 + height / 2
    for y in (-1, 1):
        box(bm, (0, y * (D / 2 - wall / 2), wz), (W, wall, height), 0, bevel=0.006)
    for x in (-1, 1):
        box(bm, (x * (W / 2 - wall / 2), 0, wz), (wall, D - wall * 2, height), 0, bevel=0.006)
    # Foam lining
    foam = height * (0.45 if open_top else 0.35)
    fz = z0 + wall + foam / 2 if open_top else z0 + height - wall - foam / 2
    box(bm, (0, 0, fz), (W - wall * 2, D - wall * 2, foam), 3)

def frame(bm, z, h, band=0.02):
    for y in (-1, 1):
        box(bm, (0, y * (D / 2 - band / 2 + 0.004), z), (W + 0.008, band, h), 1)
    for x in (-1, 1):
        box(bm, (x * (W / 2 - band / 2 + 0.004), 0, z), (band, D - band * 2 + 0.008, h), 1)

def base(bm):
    tray(bm, 0, H, True)
    # Rim band and corner guards
    frame(bm, H - 0.012, 0.018)
    for x in (-1, 1):
        for y in (-1, 1):
            box(bm, (x * (W / 2 - 0.02), y * (D / 2 - 0.02), 0.03), (0.055, 0.055, 0.06), 1, bevel=0.01)
    # Feet
    for x in (-1, 1):
        box(bm, (x * (W / 2 - 0.07), 0, -0.006), (0.06, D - 0.06, 0.014), 1)
    # Latch plates
    for x in (-1, 1):
        box(bm, (x * 0.17, -D / 2 - 0.006, H - 0.035), (0.05, 0.012, 0.05), 2, bevel=0.004)
    # Handle posts
    for x in (-1, 1):
        box(bm, (x * 0.09, -D / 2 - 0.012, H - 0.06), (0.03, 0.02, 0.03), 1)
    # Hinge knuckles at the back
    for x in (-0.2, 0, 0.2):
        box(bm, (x, D / 2 + 0.008, H), (0.06, 0.016, 0.022), 2)
    # Divider and tool cradles in the foam
    box(bm, (0.16, 0, H * 0.5), (0.012, D - 0.03, H * 0.7), 1)
    for y in (-0.075, 0.0, 0.075):
        box(bm, (-0.06, y, H * 0.52), (0.36, 0.05, 0.012), 3)

def handle(bm):
    z = H - 0.06
    box(bm, (0, -D / 2 - 0.045, z), (0.2, 0.026, 0.026), 1, bevel=0.006)
    for x in (-1, 1):
        box(bm, (x * 0.09, -D / 2 - 0.03, z), (0.022, 0.03, 0.022), 1)

def lid(bm):
    tray(bm, H, LID, False)
    frame(bm, H + 0.012, 0.018)
    # Ribs on the top
    for y in (-0.08, 0.0, 0.08):
        box(bm, (0, y, H + LID + 0.003), (W - 0.1, 0.03, 0.008), 0, bevel=0.003)
    # Latch hooks
    for x in (-1, 1):
        box(bm, (x * 0.17, -D / 2 - 0.008, H + 0.012), (0.04, 0.012, 0.04), 2)
    # Corner guards
    for x in (-1, 1):
        for y in (-1, 1):
            box(bm, (x * (W / 2 - 0.02), y * (D / 2 - 0.02), H + LID - 0.02), (0.055, 0.055, 0.045), 1, bevel=0.01)

body = obj("Toolbox", lambda bm: (base(bm), handle(bm)), [SHELL, TRIM, METAL, INNER])
hinge = (0, D / 2 + 0.008, H)
top = obj("Lid", lid, [SHELL, TRIM, METAL, INNER], origin=hinge)
top.parent = body
top.location = Vector(hinge)

out = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.save_as_mainfile(filepath=out.replace(".fbx", ".blend"))
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='FACE',
                         use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False)
tris = sum(len(p.vertices) - 2 for o in bpy.data.objects if o.type == 'MESH' for p in o.data.polygons)
print("TRIS", tris)

bpy.context.view_layer.update()
scene = bpy.context.scene
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
scene.collection.objects.link(cam)
cam.location = (0.85, -1.15, 0.95)
cam.rotation_euler = (math.radians(50), 0, math.radians(37))
scene.camera = cam
top.rotation_euler = (math.radians(-70), 0, 0)
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
sun.rotation_euler = (math.radians(40), math.radians(20), math.radians(30))
sun.data.energy = 3
scene.collection.objects.link(sun)
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'MATERIAL'
scene.render.resolution_x, scene.render.resolution_y = 900, 700
scene.render.filepath = out.replace(".fbx", "_preview.png")
bpy.ops.render.render(write_still=True)
