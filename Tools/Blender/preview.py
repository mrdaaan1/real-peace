# Превью модели: открыть .blend, поставить свет и камеру, отрендерить несколько ракурсов (Workbench).
# Blender --background <файл.blend> --python preview.py -- <префикс png>
import bpy, sys, math
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]
prefix = argv[0]
dist = float(argv[1]) if len(argv) > 1 else 11.0
tz = float(argv[2]) if len(argv) > 2 else 1.1
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "MATERIAL"
scene.display.shading.show_cavity = True
scene.display.shading.show_shadows = True
scene.render.resolution_x, scene.render.resolution_y = 1280, 800
colors = {"paint": (0.3, 0.36, 0.18, 1), "paint_dark": (0.22, 0.27, 0.13, 1), "track": (0.12, 0.12, 0.11, 1), "metal": (0.2, 0.2, 0.2, 1),
          "rubber": (0.06, 0.06, 0.06, 1), "dark": (0.03, 0.03, 0.03, 1), "marking_red": (0.6, 0.05, 0.03, 1), "marking_white": (0.9, 0.9, 0.85, 1), "marking_black": (0, 0, 0, 1), "tunic": (0.45, 0.42, 0.25, 1), "trousers": (0.38, 0.36, 0.22, 1), "skin": (0.8, 0.6, 0.45, 1), "boots": (0.1, 0.08, 0.06, 1), "helmet": (0.27, 0.32, 0.18, 1), "gear": (0.3, 0.24, 0.15, 1), "pack": (0.4, 0.36, 0.24, 1), "coat": (0.4, 0.37, 0.3, 1), "collar": (0.2, 0.27, 0.2, 1), "hair": (0.15, 0.1, 0.05, 1), "wood": (0.35, 0.2, 0.1, 1), "bakelite": (0.2, 0.12, 0.08, 1)}
for m in bpy.data.materials:
    m.diffuse_color = colors.get(m.name, (0.5, 0.5, 0.5, 1))
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.lens = 50
for i, (az, el, d) in enumerate([(35, 12, dist), (160, 15, dist), (90, 3, dist), (0, 5, dist)]):
    a, e = math.radians(az), math.radians(el)
    target = Vector((0, 0, tz))
    cam.location = target + Vector((math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e))) * d
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = "%s_%d.png" % (prefix, i)
    bpy.ops.render.render(write_still=True)
