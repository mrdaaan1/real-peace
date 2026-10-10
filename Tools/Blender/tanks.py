# Модели танков для Real Peace: Т-34-76 обр. 1943 (башня-«гайка» УЗТМ) и Pz.Kpfw. IV Ausf. H.
# Размеры по справочным данным, округлены. Запуск:
#   Blender --background --factory-startup --python tanks.py -- <папка Resources/Models> [<папка для .blend>]
# Выход: T34.bytes и PzIV.bytes (формат RPM1, см. rp_kit.py), по желанию — .blend для просмотра.

import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rp_kit as K

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
BLEND = argv[1] if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)


def mirror(points):
    """Точки с x>0 и их зеркальные копии."""
    out = []
    for (x, y, z) in points:
        out.append((x, y, z))
        if abs(x) > 1e-6:
            out.append((-x, y, z))
    return out


# =====================================================================
#   Т-34-76 обр. 1943
# =====================================================================

def t34():
    K.reset()
    K.part("Hull", None, (0, 0, 0))
    ring = (0, 0.62, 1.62)
    K.part("Turret", "Hull", ring)
    K.part("Gun", "Turret", (0, ring[1] + 1.36, ring[2] + 0.43))
    K.part("Muzzle", "Gun", (0, ring[1] + 1.36 + 3.12, ring[2] + 0.43))
    K.part("Coax", "Turret", (0.3, ring[1] + 1.45, ring[2] + 0.4))

    # --- Корпус ---
    # Нижняя часть между гусеницами
    K.hull("LowerHull", "Hull", mirror([
        (0.95, 2.62, 0.40), (0.95, 3.02, 0.92), (0.95, 2.84, 1.07),
        (0.95, -2.95, 1.07), (0.95, -2.92, 0.46), (0.95, -2.5, 0.40)]), "paint", 0.02)
    # Верхняя часть с надгусеничными нишами: лоб 60°, борта 40°, корма
    K.hull("UpperHull", "Hull", mirror([
        (1.47, 2.84, 1.05), (1.12, 2.05, 1.62), (1.12, -2.36, 1.62),
        (1.47, -2.82, 1.05), (1.20, -2.97, 1.12), (1.0, 3.0, 0.98)]), "paint", 0.025)
    # Нижний лобовой лист и буксирные крюки
    for s in (-1, 1):
        K.cyl("Hook", "Hull", (s * 0.55, 2.98, 0.7), 0.06, 0.09, "X", 10, "paint")
    # Люк механика-водителя с двумя перископами (левая сторона лба)
    gl = lambda y: 1.05 + (2.84 - y) * (1.62 - 1.05) / (2.84 - 2.05)  # высота лба в точке y
    K.plate("DriverHatch", "Hull", [(-0.72, 2.62, gl(2.62) + 0.03), (-0.08, 2.62, gl(2.62) + 0.03),
                                     (-0.08, 2.22, gl(2.22) + 0.03), (-0.72, 2.22, gl(2.22) + 0.03)], 0.05, "paint_dark")
    for x in (-0.6, -0.2):
        K.box("Periscope", "Hull", (x, 2.27, gl(2.27) + 0.08), (0.12, 0.06, 0.06), "dark", 0.005, (-30, 0, 0))
    # Курсовой пулемёт ДТ в шаровой установке (справа)
    K.sphere("MgBall", "Hull", (0.55, 2.45, gl(2.45) + 0.02), 0.13, "paint_dark", (1, 0.8, 1))
    K.cyl("MgBarrel", "Hull", (0.55, 2.62, gl(2.45) + 0.06), 0.02, 0.35, "Y", 8, "metal", 0)
    # Фары и звуковой сигнал
    K.cyl("Headlight", "Hull", (-0.95, 2.75, 1.18), 0.09, 0.12, "Y", 14, "paint_dark")
    # Поручни на бортах и ящики ЗИП на надгусеничных полках
    for s in (-1, 1):
        K.cyl("Rail", "Hull", (s * 1.18, -0.6, 1.66), 0.015, 2.6, "Y", 6, "paint", 0)
        K.box("ToolBox", "Hull", (s * 1.33, 0.9 if s > 0 else -1.4, 1.2), (0.26, 0.9, 0.24), "paint", 0.012)
        # Бочки с топливом на бортах кормы (по две)
        for y in (-1.75, -2.45):
            K.cyl("FuelDrum", "Hull", (s * 1.33, y, 1.36), 0.18, 0.62, "Y", 18, "paint", 0.012)
            for k in (-0.22, 0.22):
                K.cyl("DrumBand", "Hull", (s * 1.33, y + k, 1.36), 0.19, 0.03, "Y", 18, "paint_dark", 0)
        # Кронштейны крыльев
        K.box("Fender", "Hull", (s * 1.25, 2.72, 1.05), (0.52, 0.5, 0.03), "paint", 0.006, (8, 0, 0))
    # Корма: моторное отделение, жалюзи, выхлопные трубы
    K.box("EngineDeck", "Hull", (0, -1.4, 1.64), (1.6, 1.3, 0.05), "paint_dark", 0.01)
    for i in range(7):
        K.box("Louver", "Hull", (0, -1.95 + i * 0.18, 1.67), (1.5, 0.05, 0.02), "dark", 0.004)
    K.box("RearHatch", "Hull", (0, -2.7, 1.32), (1.0, 0.04, 0.5), "paint", 0.01, (-48, 0, 0))
    for s in (-1, 1):
        K.cyl("Exhaust", "Hull", (s * 0.62, -2.98, 1.0), 0.07, 0.25, "Y", 12, "metal", 0.005)
    # Запасные траки на надгусеничной полке справа
    for i in range(3):
        K.box("SpareLink", "Hull", (1.3, 2.25 - i * 0.18, 1.12), (0.48, 0.15, 0.05), "track", 0.005)

    # --- Ходовая: 5 сдвоенных катков Кристи, ленивец спереди, ведущее колесо сзади ---
    wheel_r = 0.415
    ys = [1.75, 0.83, -0.09, -1.01, -1.93]
    circles = []
    for i, y in enumerate(ys):
        circles.append((y, wheel_r + 0.02, wheel_r))
    circles.append((2.62, 0.56, 0.32))     # ленивец
    circles.append((-2.72, 0.6, 0.35))     # ведущее колесо
    for side, s in (("L", -1), ("R", 1)):
        x = s * 1.25
        for i, y in enumerate(ys):
            name = "Wheel_%s%d" % (side, i)
            K.part(name, "Hull", (x, y, wheel_r + 0.02))
            K.road_wheel(name, (x, y, wheel_r + 0.02), wheel_r, 0.14, dual=True)
        idl = "Wheel_%sI" % side
        K.part(idl, "Hull", (x, 2.62, 0.56))
        K.road_wheel(idl, (x, 2.62, 0.56), 0.32, 0.14, dual=True, style="plain")
        spr = "Wheel_%sS" % side
        K.part(spr, "Hull", (x, -2.72, 0.6))
        K.cyl(spr + "_body", spr, (x, -2.72, 0.6), 0.3, 0.32, "X", 24, "paint", 0.01)
        for k in range(6):  # ролики ведущего колеса
            a = k * math.pi / 3
            K.cyl(spr + "_roller%d" % k, spr, (x, -2.72 + math.cos(a) * 0.3, 0.6 + math.sin(a) * 0.3), 0.06, 0.36, "X", 10, "metal", 0)
        K.track("Hull", x, 0.5, [(c[0], c[1], c[2] + 0.005) for c in circles], 0.172, 0.05, horn=True)
        # Кривошипы подвески под катками
        for y in ys:
            K.box("Crank", "Hull", (s * 0.98, y + 0.15, 0.55), (0.12, 0.35, 0.12), "paint_dark", 0.01, (25, 0, 0))

    # --- Башня-«гайка» ---
    tz = ring[2]
    ty = ring[1]
    base = [(0.56, 1.22), (1.13, 0.36), (1.06, -0.96), (0.6, -1.36)]
    pts = []
    for (x, y) in base:
        pts.append((x, ty + y, tz + 0.02))
        pts.append((x * 0.8, ty + y * 0.86 - 0.04, tz + 0.79))
    K.hull("TurretBody", "Turret", mirror(pts), "paint", 0.03, 25)
    # Погон
    K.cyl("TurretRing", "Turret", (0, ty, tz + 0.03), 0.9, 0.08, "Z", 32, "paint_dark", 0.01)
    # Маска пушки
    K.hull("Mantlet", "Gun", mirror([
        (0.42, ty + 1.2, tz + 0.14), (0.42, ty + 1.2, tz + 0.72), (0.36, ty + 1.52, tz + 0.2), (0.36, ty + 1.52, tz + 0.66)]), "paint", 0.03)
    K.cyl("BarrelBase", "Gun", (0, ty + 1.62, tz + 0.43), 0.12, 0.28, "Y", 18, "paint", 0.01)
    K.cyl("Barrel", "Gun", (0, ty + 3.05, tz + 0.43), 0.085, 2.6, "Y", 18, "metal", 0.005, radius2=0.075)
    K.cyl("MuzzleRing", "Gun", (0, ty + 4.38, tz + 0.43), 0.088, 0.07, "Y", 18, "metal", 0.004)
    K.cyl("CoaxBarrel", "Turret", (0.3, ty + 1.4, tz + 0.4), 0.02, 0.2, "Y", 8, "metal", 0)
    # Два круглых люка («Микки Маус»), перископы, вентилятор
    for s in (-1, 1):
        K.cyl("Hatch", "Turret", (s * 0.42, ty - 0.3, tz + 0.83), 0.28, 0.07, "Z", 24, "paint", 0.012)
        K.cyl("HatchHinge", "Turret", (s * 0.42, ty - 0.58, tz + 0.84), 0.04, 0.2, "X", 8, "paint_dark", 0)
        K.box("Periscope", "Turret", (s * 0.42, ty + 0.25, tz + 0.82), (0.1, 0.16, 0.1), "dark", 0.005)
        # Поручни на бортах башни
        K.cyl("TurretRail", "Turret", (s * 0.98, ty - 0.3, tz + 0.55), 0.015, 1.0, "Y", 6, "paint", 0)
        # Красная звезда на борту башни (по плоскости борта)
        fx, fy = 1.13, 0.36
        rx, ry = 1.06, -0.96
        p0 = (s * fx, ty + fy, tz + 0.02)
        p1 = (s * rx, ty + ry, tz + 0.02)
        p2 = (s * fx * 0.8, ty + fy * 0.86 - 0.04, tz + 0.79)
        from mathutils import Vector
        a, b, c = Vector(p0), Vector(p1), Vector(p2)
        n = (b - a).cross(c - a).normalized()
        if n.x * s < 0:
            n = -n
        center = (a + b) * 0.5 + (c - a) * 0.45 + (Vector(p1) - Vector(p0)) * 0.0
        K.star("Star", "Turret", center + n * 0.012, n, (0, 0, 1), 0.2)
        # Белая полоса — опознавательный знак
        K.box("Marking", "Turret", (s * 1.0, ty - 0.35, tz + 0.66), (0.02, 0.9, 0.06), "marking_white", 0.0, (0, s * 3, 0))
    K.cyl("Fan", "Turret", (0, ty - 0.85, tz + 0.82), 0.14, 0.06, "Z", 16, "paint_dark", 0.01)

    K.export(os.path.join(OUT, "T34.bytes"))
    if BLEND:
        K.save_blend(os.path.join(BLEND, "t34.blend"))


# =====================================================================
#   Pz.Kpfw. IV Ausf. H
# =====================================================================

def pz4():
    K.reset()
    K.part("Hull", None, (0, 0, 0))
    ring = (0, 0.15, 1.92)
    K.part("Turret", "Hull", ring)
    K.part("Gun", "Turret", (0, ring[1] + 1.12, ring[2] + 0.4))
    K.part("Muzzle", "Gun", (0, ring[1] + 1.12 + 3.85, ring[2] + 0.4))
    K.part("Coax", "Turret", (-0.3, ring[1] + 1.2, ring[2] + 0.38))

    # --- Корпус ---
    K.hull("LowerHull", "Hull", mirror([
        (1.0, 2.6, 0.42), (1.0, 2.98, 0.86), (1.0, 2.98, 1.2),
        (1.0, -2.78, 1.2), (1.0, -2.85, 0.6), (1.0, -2.5, 0.42)]), "paint", 0.02)
    # Короткий верхний лобовой лист
    K.hull("Glacis", "Hull", mirror([(1.0, 2.98, 1.18), (1.0, 2.36, 1.32), (1.0, 2.36, 1.18)]), "paint", 0.015)
    # Надстройка шире корпуса — нависает над гусеницами
    K.hull("Superstructure", "Hull", mirror([
        (1.42, 2.36, 1.2), (1.42, 2.36, 1.92), (1.42, -2.0, 1.92), (1.42, -2.0, 1.2),
        (1.0, -2.78, 1.2), (1.0, -2.6, 1.86)]), "paint", 0.02)
    # Крылья над гусеницами спереди и сзади
    for s in (-1, 1):
        K.box("FenderF", "Hull", (s * 1.2, 2.72, 1.2), (0.46, 0.75, 0.03), "paint", 0.006)
        K.box("FenderR", "Hull", (s * 1.2, -2.45, 1.2), (0.46, 0.8, 0.03), "paint", 0.006)
    # Лоб рубки: смотровой прибор водителя (Fahrersehklappe), курсовой пулемёт
    K.box("DriverVisor", "Hull", (-0.5, 2.4, 1.66), (0.45, 0.08, 0.22), "paint", 0.012)
    K.box("VisorSlit", "Hull", (-0.5, 2.45, 1.68), (0.26, 0.02, 0.04), "dark", 0)
    K.sphere("MgBall", "Hull", (0.55, 2.4, 1.6), 0.11, "paint_dark", (1, 0.7, 1))
    K.cyl("MgBarrel", "Hull", (0.55, 2.55, 1.6), 0.018, 0.3, "Y", 8, "metal", 0)
    # Запасные траки на лобовом листе и запасные катки
    for i in range(5):
        K.box("SpareLink", "Hull", (-0.8 + i * 0.4, 2.99, 1.02), (0.36, 0.05, 0.12), "track", 0.004)
    for s in (-1, 1):
        K.cyl("SpareWheel", "Hull", (s * 0.75, 2.75, 1.3), 0.23, 0.08, "X", 18, "rubber", 0.01)
    # Люки водителя и радиста, моторные решётки
    for s in (-1, 1):
        K.box("CrewHatch", "Hull", (s * 0.55, 1.75, 1.94), (0.55, 0.5, 0.04), "paint", 0.01)
    K.box("EngineDeck", "Hull", (0, -1.55, 1.93), (2.0, 1.6, 0.03), "paint_dark", 0.008)
    for i in range(6):
        K.box("Grille", "Hull", (0, -2.05 + i * 0.2, 1.95), (1.8, 0.06, 0.02), "dark", 0.003)
    # Глушитель (Auspufftopf) на корме
    K.cyl("Muffler", "Hull", (0, -2.95, 0.95), 0.18, 1.0, "X", 18, "metal", 0.01)
    K.cyl("Exhaust", "Hull", (0.35, -2.95, 1.2), 0.05, 0.3, "Z", 10, "metal", 0)
    # Инструмент на крыльях: домкрат, лопата, канистры
    for s in (-1, 1):
        K.cyl("Jerrycan", "Hull", (s * 1.2, -2.2, 1.35), 0.12, 0.3, "Z", 4, "paint_dark", 0.01)
        K.box("Shovel", "Hull", (s * 1.25, 0.2, 1.23), (0.08, 1.1, 0.04), "paint_dark", 0.004)

    # --- Ходовая: 8 катков в 4 тележках, 4 поддерживающих ролика, ведущее колесо спереди ---
    wheel_r = 0.235
    ys = [2.02, 1.5, 0.9, 0.38, -0.25, -0.77, -1.37, -1.89]
    circles = [(y, wheel_r + 0.01, wheel_r) for y in ys]
    circles.append((2.66, 0.62, 0.34))   # ведущее
    circles.append((-2.62, 0.6, 0.3))    # ленивец
    rollers = [-1.6, -0.55, 0.55, 1.6]
    for y in rollers:
        circles.append((y, 0.98, 0.12))
    for side, s in (("L", -1), ("R", 1)):
        x = s * 1.2
        for i, y in enumerate(ys):
            name = "Wheel_%s%d" % (side, i)
            K.part(name, "Hull", (x, y, wheel_r + 0.01))
            K.road_wheel(name, (x, y, wheel_r + 0.01), wheel_r, 0.1, dual=True, style="plain")
        # Тележки с листовыми рессорами
        for b in range(4):
            yc = (ys[b * 2] + ys[b * 2 + 1]) / 2
            K.box("Bogie", "Hull", (s * 0.98, yc, 0.38), (0.16, 0.65, 0.22), "paint", 0.015)
            K.box("Spring", "Hull", (s * 0.98, yc, 0.56), (0.14, 0.55, 0.06), "paint_dark", 0.01)
        for y in rollers:
            K.cyl("Roller", "Hull", (x, y, 0.98), 0.12, 0.24, "X", 14, "rubber", 0.01)
        spr = "Wheel_%sS" % side
        K.part(spr, "Hull", (x, 2.66, 0.62))
        K.cyl(spr + "_body", spr, (x, 2.66, 0.62), 0.3, 0.3, "X", 24, "paint", 0.01)
        for k in range(10):  # зубья
            a = k * math.pi / 5
            K.box(spr + "_tooth%d" % k, spr, (x, 2.66 + math.cos(a) * 0.33, 0.62 + math.sin(a) * 0.33), (0.18, 0.08, 0.08), "metal", 0.005, (math.degrees(a), 0, 0))
        idl = "Wheel_%sI" % side
        K.part(idl, "Hull", (x, -2.62, 0.6))
        K.cyl(idl + "_body", idl, (x, -2.62, 0.6), 0.3, 0.3, "X", 24, "paint", 0.01)
        for k in range(6):
            a = k * math.pi / 3
            K.cyl(idl + "_spoke%d" % k, idl, (x + s * 0.14, -2.62 + math.cos(a) * 0.17, 0.6 + math.sin(a) * 0.17), 0.05, 0.04, "X", 8, "dark", 0)
        K.track("Hull", x, 0.4, [(c[0], c[1], c[2] + 0.004) for c in circles], 0.12, 0.045, horn=True)

        # Противокумулятивные экраны (Schürzen): 5 листов на кронштейнах, слегка наклонены наружу
        for p in range(5):
            y0 = -2.35 + p * 0.95
            y1 = y0 + 0.9
            K.plate("Skirt%d" % p, "Hull", [(s * 1.6, y0, 1.42), (s * 1.6, y1, 1.42), (s * 1.66, y1, 0.55), (s * 1.66, y0, 0.55)], 0.008, "paint", 0.003)
        K.cyl("SkirtRail", "Hull", (s * 1.58, 0.0, 1.47), 0.02, 4.8, "Y", 6, "paint_dark", 0)
        # Балочный крест на экране
        cx, cz, yy = s * 1.675, 1.0, -0.2
        K.box("CrossWhiteH", "Hull", (cx, yy, cz), (0.012, 0.62, 0.2), "marking_white", 0)
        K.box("CrossWhiteV", "Hull", (cx, yy, cz), (0.012, 0.2, 0.62), "marking_white", 0)
        K.box("CrossBlackH", "Hull", (cx + s * 0.006, yy, cz), (0.012, 0.52, 0.11), "marking_black", 0)
        K.box("CrossBlackV", "Hull", (cx + s * 0.006, yy, cz), (0.012, 0.11, 0.52), "marking_black", 0)

    # --- Башня ---
    tz, ty = ring[2], ring[1]
    base = [(0.78, 1.02), (1.0, 0.6), (0.96, -1.02), (0.7, -1.32)]
    pts = []
    for (x, y) in base:
        pts.append((x, ty + y, tz + 0.02))
        pts.append((x * 0.82, ty + y * 0.9, tz + 0.82))
    K.hull("TurretBody", "Turret", mirror(pts), "paint", 0.025, 25)
    K.cyl("TurretRing", "Turret", (0, ty, tz + 0.03), 0.86, 0.08, "Z", 32, "paint_dark", 0.01)
    # Командирская башенка с приборами наблюдения
    K.cyl("Cupola", "Turret", (0, ty - 0.88, tz + 0.95), 0.36, 0.3, "Z", 24, "paint", 0.015)
    K.cyl("CupolaTop", "Turret", (0, ty - 0.88, tz + 1.12), 0.3, 0.05, "Z", 24, "paint_dark", 0.01)
    for k in range(5):
        a = math.pi * 0.25 + k * math.pi * 0.35
        K.box("Vision", "Turret", (math.cos(a) * 0.36, ty - 0.88 + math.sin(a) * 0.36, tz + 0.98), (0.12, 0.04, 0.05), "dark", 0, (0, 0, math.degrees(a) + 90))
    # Боковые люки
    for s in (-1, 1):
        K.plate("SideHatch", "Turret", [(s * 0.92, ty + 0.15, tz + 0.15), (s * 0.92, ty - 0.35, tz + 0.15),
                                         (s * 0.79, ty - 0.35, tz + 0.72), (s * 0.79, ty + 0.15, tz + 0.72)], 0.04, "paint", 0.008)
    # Маска и длинная пушка KwK 40 L/48 с двухкамерным дульным тормозом
    K.hull("Mantlet", "Gun", mirror([
        (0.5, ty + 1.0, tz + 0.16), (0.5, ty + 1.0, tz + 0.66), (0.5, ty + 1.24, tz + 0.18), (0.5, ty + 1.24, tz + 0.64)]), "paint", 0.03)
    K.cyl("BarrelSleeve", "Gun", (0, ty + 1.45, tz + 0.4), 0.11, 0.4, "Y", 18, "paint", 0.01)
    K.cyl("Barrel", "Gun", (0, ty + 3.15, tz + 0.4), 0.075, 3.1, "Y", 18, "metal", 0.005, radius2=0.068)
    K.cyl("Brake1", "Gun", (0, ty + 4.78, tz + 0.4), 0.13, 0.16, "Y", 18, "metal", 0.01)
    K.cyl("Brake2", "Gun", (0, ty + 4.98, tz + 0.4), 0.13, 0.16, "Y", 18, "metal", 0.01)
    K.cyl("CoaxBarrel", "Turret", (-0.3, ty + 1.15, tz + 0.38), 0.02, 0.2, "Y", 8, "metal", 0)
    # Экраны башни на кронштейнах (с дверцами)
    shield = [(-1.28, 0.72), (-1.32, -1.1), (-0.95, -1.62), (0.95, -1.62), (1.32, -1.1), (1.28, 0.72)]
    for i in range(len(shield) - 1):
        (x0, y0), (x1, y1) = shield[i], shield[i + 1]
        K.plate("TurretSkirt%d" % i, "Turret", [(x0, ty + y0, tz + 0.82), (x1, ty + y1, tz + 0.82), (x1, ty + y1, tz + 0.05), (x0, ty + y0, tz + 0.05)], 0.008, "paint", 0.003)
    for s in (-1, 1):
        # Тактический номер на экране башни — тёмные «цифры»
        for d in range(3):
            K.box("Digit", "Turret", (s * 1.315, ty + 0.2 - d * 0.22, tz + 0.45), (0.012, 0.13, 0.22), "marking_black", 0)
    K.cyl("Antenna", "Hull", (1.1, -2.2, 3.0), 0.008, 2.2, "Z", 6, "metal", 0)

    K.export(os.path.join(OUT, "PzIV.bytes"))
    if BLEND:
        K.save_blend(os.path.join(BLEND, "pz4.blend"))


t34()
pz4()
