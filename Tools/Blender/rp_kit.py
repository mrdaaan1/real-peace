# Набор для моделей Real Peace в Blender: примитивы (выпуклые оболочки, коробки, цилиндры), фаски,
# сглаживание по углу, гусеницы по контуру катков и выгрузка в формат RPM1 для Unity.
#
# Координаты в Blender: X — вправо, Y — вперёд, Z — вверх. В Unity: (x, y, z) = (bx, bz, by);
# такая замена осей — отражение, поэтому порядок вершин треугольников переворачивается.
#
# Формат RPM1 (gzip): "RPM1", число частей; у части: имя, индекс родителя (-1 — корень), опорная точка
# относительно родителя (Unity), вершины, нормали, подсетки (имя материала + индексы).
# Части модели (Hull, Turret, Gun, Wheel_L0…) — то, что двигается в игре. Каждый объект Blender
# помечен свойством "part": при выгрузке все объекты одной части склеиваются в одну сетку.

import bpy, bmesh, math, struct, gzip
from mathutils import Vector, Matrix

PARTS = {}      # имя -> (родитель, мировая опорная точка)
PART_ORDER = []
_mats = {}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()
    PARTS.clear()
    PART_ORDER.clear()


def part(name, parent, origin):
    PARTS[name] = (parent, Vector(origin))
    PART_ORDER.append(name)


_mats = {}


def mat(name):
    if name not in _mats or _mats[name].name not in bpy.data.materials:
        _mats[name] = bpy.data.materials.new(name)
    return _mats[name]


def _finish(bm, name, prt, materials, bevel, smooth_angle, location=(0, 0, 0)):
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    ob.location = location
    for m in materials:
        me.materials.append(mat(m))
    ob["part"] = prt
    if bevel > 0:
        mod = ob.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(30)
        mod.harden_normals = False
    for p in me.polygons:
        p.use_smooth = True
    ob["smooth_angle"] = smooth_angle
    return ob


def hull(name, prt, points, material="paint", bevel=0.02, smooth_angle=30):
    """Выпуклая оболочка точек — бронелисты под любыми углами."""
    bm = bmesh.new()
    verts = [bm.verts.new(Vector(p)) for p in points]
    res = bmesh.ops.convex_hull(bm, input=verts)
    junk = set()
    for key in ("geom_interior", "geom_unused"):
        junk.update(g for g in res[key] if isinstance(g, bmesh.types.BMVert))
    junk = [v for v in junk if v.is_valid and not v.link_faces]
    if junk:
        bmesh.ops.delete(bm, geom=junk, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # Слить треугольники одной плоскости в многоугольники — чище фаски
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(1), verts=bm.verts, edges=bm.edges)
    return _finish(bm, name, prt, [material], bevel, smooth_angle)


def box(name, prt, center, size, material="paint", bevel=0.015, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    R = Matrix.Rotation(math.radians(rot[2]), 4, "Z") @ Matrix.Rotation(math.radians(rot[1]), 4, "Y") @ Matrix.Rotation(math.radians(rot[0]), 4, "X")
    bmesh.ops.transform(bm, matrix=R, verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return _finish(bm, name, prt, [material], bevel, 30)


def cyl(name, prt, center, radius, depth, axis="X", segments=20, material="paint", bevel=0.01, radius2=None, smooth_angle=40):
    """Цилиндр (или усечённый конус, если задан radius2) вдоль оси X/Y/Z."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=radius,
                          radius2=radius if radius2 is None else radius2, depth=depth)
    if axis == "X":
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, "Y"), verts=bm.verts)
    elif axis == "Y":
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-90), 3, "X"), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return _finish(bm, name, prt, [material], bevel, smooth_angle)


def sphere(name, prt, center, radius, material="paint", scale=(1, 1, 1)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=radius)
    bmesh.ops.scale(bm, vec=Vector(scale), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return _finish(bm, name, prt, [material], 0, 80)


def star(name, prt, center, normal, up, radius, material="marking_red"):
    """Плоская пятиконечная звезда на поверхности (центр, нормаль наружу, «верх»)."""
    n = Vector(normal).normalized()
    u = (Vector(up) - n * Vector(up).dot(n)).normalized()
    r = n.cross(u).normalized()
    bm = bmesh.new()
    c = bm.verts.new(Vector(center) + n * 0.004)
    ring = []
    for i in range(10):
        a = math.pi / 2 + i * math.pi / 5
        rr = radius if i % 2 == 0 else radius * 0.4
        ring.append(bm.verts.new(Vector(center) + n * 0.004 + (r * math.cos(a) + u * math.sin(a)) * rr))
    for i in range(10):
        f = bm.faces.new((c, ring[i], ring[(i + 1) % 10]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        if f.normal.dot(n) < 0:
            f.normal_flip()
    return _finish(bm, name, prt, [material], 0, 10)


def plate(name, prt, corners, thickness, material="paint", bevel=0.006):
    """Пластина по четырём углам (экраны, крышки люков) толщиной thickness."""
    a, b, c, d = [Vector(p) for p in corners]
    n = (b - a).cross(d - a).normalized() * thickness * 0.5
    pts = [a + n, b + n, c + n, d + n, a - n, b - n, c - n, d - n]
    return hull(name, prt, pts, material, bevel)


# ---------- Колёса и гусеницы ----------

def road_wheel(prt, center, radius, width, dual=False, rim="paint", style="dish"):
    """Опорный каток: резиновый бандаж, диск со ступицей. dual — сдвоенный (с промежутком для гребня трака)."""
    cx, cy, cz = center
    offsets = [-width * 0.75, width * 0.75] if dual else [0.0]
    for i, dx in enumerate(offsets):
        w = width if not dual else width * 0.9
        cyl(prt + "_tire%d" % i, prt, (cx + dx, cy, cz), radius, w, "X", 28, "rubber", 0.02)
        side = 1 if dx >= 0 else -1
        cyl(prt + "_disc%d" % i, prt, (cx + dx + side * w * 0.12, cy, cz), radius * 0.86, w * 0.9, "X", 28, rim, 0.01)
        # Выпуклая ступица и болты
        cyl(prt + "_hub%d" % i, prt, (cx + dx + side * w * 0.5, cy, cz), radius * 0.28, w * 0.35, "X", 16, rim, 0.01, radius2=radius * 0.18)
        if style == "dish":
            for k in range(8):
                a = k * math.pi / 4
                cyl(prt + "_hole%d_%d" % (i, k), prt, (cx + dx + side * w * 0.47, cy + math.cos(a) * radius * 0.55, cz + math.sin(a) * radius * 0.55),
                    radius * 0.11, 0.02, "X", 10, "dark", 0)


def track_path(circles, samples=72):
    """Контур гусеницы: выпуклая оболочка окружностей (каток/ленивец/ведущее — (y, z, r)) в плоскости YZ."""
    pts = []
    for (y, z, r) in circles:
        for i in range(samples):
            a = 2 * math.pi * i / samples
            pts.append((y + math.cos(a) * r, z + math.sin(a) * r))
    pts = sorted(set(pts))

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]  # против часовой стрелки


def resample(loop, pitch):
    """Точки через равный шаг по замкнутому контуру: (точка, касательная)."""
    segs = []
    total = 0.0
    for i in range(len(loop)):
        a, b = Vector(loop[i]), Vector(loop[(i + 1) % len(loop)])
        L = (b - a).length
        segs.append((a, b, L))
        total += L
    n = max(8, int(round(total / pitch)))
    step = total / n
    out = []
    d = 0.0
    si = 0
    acc = 0.0
    for k in range(n):
        target = k * step
        while acc + segs[si][2] < target and si < len(segs) - 1:
            acc += segs[si][2]
            si += 1
        a, b, L = segs[si]
        t = (target - acc) / L if L > 0 else 0
        p = a.lerp(b, t)
        tang = (b - a).normalized()
        out.append((p, tang))
    return out, step


def track(prt, x, width, circles, pitch, thickness=0.05, horn=True, grouser=True, material="track"):
    """Гусеница из отдельных траков по контуру катков. x — середина гусеницы."""
    loop = track_path(circles)
    links, step = resample(loop, pitch)
    bm = bmesh.new()
    cy = sum(c[0] for c in circles) / len(circles)
    cz = sum(c[1] for c in circles) / len(circles)

    def add_box(center, ax_t, ax_n, half):
        # half = (вдоль X, вдоль касательной, вдоль нормали)
        ex = Vector((1, 0, 0)) * half[0]
        et = Vector((0, ax_t.x, ax_t.y)) * half[1]
        en = Vector((0, ax_n.x, ax_n.y)) * half[2]
        vs = []
        for sx in (-1, 1):
            for st in (-1, 1):
                for sn in (-1, 1):
                    vs.append(bm.verts.new(center + ex * sx + et * st + en * sn))
        idx = lambda sx, st, sn: vs[(sx > 0) * 4 + (st > 0) * 2 + (sn > 0)]
        faces = [
            (idx(-1, -1, -1), idx(-1, 1, -1), idx(-1, 1, 1), idx(-1, -1, 1)),
            (idx(1, -1, -1), idx(1, -1, 1), idx(1, 1, 1), idx(1, 1, -1)),
            (idx(-1, -1, -1), idx(-1, -1, 1), idx(1, -1, 1), idx(1, -1, -1)),
            (idx(-1, 1, -1), idx(1, 1, -1), idx(1, 1, 1), idx(-1, 1, 1)),
            (idx(-1, -1, -1), idx(1, -1, -1), idx(1, 1, -1), idx(-1, 1, -1)),
            (idx(-1, -1, 1), idx(-1, 1, 1), idx(1, 1, 1), idx(1, -1, 1)),
        ]
        for f in faces:
            try:
                bm.faces.new(f)
            except ValueError:
                pass

    for i, (p, t) in enumerate(links):
        # Нормаль наружу контура (контур против часовой — поворот касательной по часовой)
        n = Vector((t.y, -t.x))
        if (Vector((p.x - cy, p.y - cz))).dot(n) < 0:
            n = -n
        c = Vector((x, p.x, p.y)) + Vector((0, n.x, n.y)) * (thickness * 0.5)
        add_box(c, t, n, (width * 0.5, step * 0.44, thickness * 0.5))
        if grouser:
            add_box(c + Vector((0, n.x, n.y)) * thickness * 0.6, t, n, (width * 0.46, step * 0.1, thickness * 0.25))
        if horn and i % 2 == 0:
            add_box(c - Vector((0, n.x, n.y)) * thickness * 1.2, t, n, (0.03, step * 0.3, thickness * 0.9))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _finish(bm, prt + "_links_%s" % ("L" if x < 0 else "R"), prt, [material], 0, 20)


# ---------- Выгрузка ----------

def _smooth(ob):
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    try:
        bpy.ops.object.shade_auto_smooth(angle=math.radians(ob.get("smooth_angle", 30)))
    except Exception as e:
        print("auto smooth:", e)


def export(path):
    for ob in list(bpy.data.objects):
        if ob.type == "MESH":
            _smooth(ob)
    dg = bpy.context.evaluated_depsgraph_get()

    data = {name: {} for name in PART_ORDER}  # part -> material -> (verts, normals, idx, dedupe)
    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        prt = ob.get("part")
        if prt not in data:
            print("Объект без части:", ob.name, prt)
            continue
        origin = PARTS[prt][1]
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        me.calc_loop_triangles()
        M = ob.matrix_world
        N = M.to_3x3().inverted().transposed()
        cn = me.corner_normals
        for tri in me.loop_triangles:
            mname = me.materials[tri.material_index].name if me.materials else "paint"
            bucket = data[prt].setdefault(mname, ([], [], [], {}))
            verts, norms, idx, dd = bucket
            corner = []
            for li in tri.loops:
                v = me.vertices[me.loops[li].vertex_index].co
                p = M @ v - origin
                n = (N @ cn[li].vector).normalized()
                key = (round(p.x, 4), round(p.y, 4), round(p.z, 4), round(n.x, 3), round(n.y, 3), round(n.z, 3))
                if key not in dd:
                    dd[key] = len(verts)
                    verts.append((p.x, p.z, p.y))  # в Unity
                    norms.append((n.x, n.z, n.y))
                corner.append(dd[key])
            idx.extend((corner[0], corner[2], corner[1]))  # отражение осей — переворачиваем обход
        ev.to_mesh_clear()

    out = bytearray()
    out += b"RPM1"
    out += struct.pack("<i", len(PART_ORDER))

    def s(t):
        b = t.encode("utf-8")
        return struct.pack("<i", len(b)) + b
    total = 0
    for name in PART_ORDER:
        parent, origin = PARTS[name]
        pi = PART_ORDER.index(parent) if parent else -1
        po = PARTS[parent][1] if parent else Vector((0, 0, 0))
        local = origin - po
        out += s(name) + struct.pack("<i", pi) + struct.pack("<3f", local.x, local.z, local.y)
        # Общий список вершин части по всем материалам
        allv, alln, subs = [], [], []
        for mname, (verts, norms, idx, _) in data[name].items():
            base = len(allv)
            allv += verts
            alln += norms
            subs.append((mname, [i + base for i in idx]))
        total += len(allv)
        out += struct.pack("<i", len(allv))
        for v in allv:
            out += struct.pack("<3f", *v)
        for n in alln:
            out += struct.pack("<3f", *n)
        out += struct.pack("<i", len(subs))
        for mname, idx in subs:
            out += s(mname) + struct.pack("<i", len(idx)) + struct.pack("<%di" % len(idx), *idx)
    with gzip.open(path, "wb") as f:
        f.write(bytes(out))
    print("Выгружено %s: %d частей, %d вершин" % (path, len(PART_ORDER), total))


def save_blend(path):
    bpy.ops.wm.save_as_mainfile(filepath=path)
