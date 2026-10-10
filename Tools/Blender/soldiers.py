# Солдаты и стрелковое оружие для Real Peace. Фигура — отдельные части (туловище, ноги, руки, голова),
# как у каркаса SoldierRig в игре: опорные точки — тазобедренные и плечевые суставы, шея.
# Запуск: Blender --background --factory-startup --python soldiers.py -- <Resources/Models> [<папка .blend>]
# Выход: Soldier_USSR.bytes, Soldier_Germany.bytes, Weapons.bytes (формат RPM1, см. rp_kit.py).

import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rp_kit as K
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
BLEND = argv[1] if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)


def ring(cx, cy, z, rx, ry, n=12, phase=0.0):
    return [(cx + math.cos(phase + 2 * math.pi * i / n) * rx, cy + math.sin(phase + 2 * math.pi * i / n) * ry, z) for i in range(n)]


def rings(prt, name, sections, material, smooth=70, cx=0.0, cy=0.0, n=12):
    """Тело вращения по сечениям (z, rx, ry[, dy]) — выпуклая оболочка колец."""
    pts = []
    for s in sections:
        dy = s[3] if len(s) > 3 else 0.0
        pts += ring(cx, cy + dy, s[0], s[1], s[2], n)
    return K.hull(name, prt, pts, material, 0, smooth)


def tube(prt, name, a, b, r, material, seg=8):
    """Цилиндр между двумя точками (лямки, ремни, скатка)."""
    a, b = Vector(a), Vector(b)
    d = b - a
    up = Vector((0, 0, 1)) if abs(d.normalized().z) < 0.9 else Vector((1, 0, 0))
    u = d.cross(up).normalized() * r
    v = d.cross(u).normalized() * r
    pts = []
    for k in range(seg):
        ang = 2 * math.pi * k / seg
        off = u * math.cos(ang) + v * math.sin(ang)
        pts.append(tuple(a + off))
        pts.append(tuple(b + off))
    return K.hull(name, prt, pts, material, 0, 60)


# =====================================================================
#   Фигура
# =====================================================================

def soldier(side):
    K.reset()
    sov = side == "USSR"
    K.part("Body", None, (0, 0, 0))
    K.part("LegL", "Body", (-0.11, 0, 0.9))
    K.part("LegR", "Body", (0.11, 0, 0.9))
    K.part("ArmL", "Body", (-0.235, 0, 1.44))
    K.part("ArmR", "Body", (0.235, 0, 1.44))
    K.part("Head", "Body", (0, 0, 1.7))

    # --- Туловище ---
    if sov:
        # Гимнастёрка навыпуск до бёдер, стоячий воротник
        rings("Body", "Tunic", [(0.8, 0.2, 0.14), (0.9, 0.195, 0.135), (1.05, 0.18, 0.125), (1.28, 0.205, 0.14), (1.44, 0.225, 0.125), (1.52, 0.13, 0.09)], "tunic")
        rings("Body", "Collar", [(1.5, 0.075, 0.07), (1.57, 0.07, 0.065)], "tunic")
    else:
        # Китель с четырьмя карманами
        rings("Body", "Tunic", [(0.78, 0.205, 0.145), (0.9, 0.195, 0.135), (1.05, 0.18, 0.125), (1.28, 0.205, 0.14), (1.44, 0.225, 0.125), (1.52, 0.13, 0.09)], "tunic")
        rings("Body", "Collar", [(1.5, 0.078, 0.072), (1.58, 0.072, 0.068)], "collar")
        for s in (-1, 1):
            K.box("ChestPocket", "Body", (s * 0.1, 0.135, 1.3), (0.1, 0.02, 0.11), "tunic", 0.006)
            K.box("HipPocket", "Body", (s * 0.12, 0.14, 0.86), (0.12, 0.02, 0.13), "tunic", 0.006)
    # Ремень с бляхой
    rings("Body", "Belt", [(0.96, 0.2, 0.14), (1.02, 0.2, 0.14)], "gear", 30)
    K.box("Buckle", "Body", (0, 0.142, 0.99), (0.06, 0.012, 0.05), "metal", 0.003)
    # Погоны
    for s in (-1, 1):
        K.box("Strap", "Body", (s * 0.15, 0, 1.47), (0.11, 0.05, 0.015), "tunic", 0.004, (0, s * 12, 0))

    if sov:
        # Подсумки на ремне, вещмешок, скатка шинели через левое плечо
        for s in (-1, 1):
            K.box("Pouch", "Body", (s * 0.1, 0.15, 0.97), (0.09, 0.05, 0.08), "gear", 0.008)
        rings("Body", "Veshmeshok", [(0.95, 0.1, 0.06, -0.19), (1.1, 0.14, 0.08, -0.2), (1.28, 0.12, 0.07, -0.19), (1.36, 0.05, 0.04, -0.17)], "pack")
        for sx in (-1, 1):
            tube("Body", "PackStrap%d" % sx, (sx * 0.12, 0.1, 1.46), (sx * 0.1, -0.17, 1.3), 0.012, "pack")
        pts = []
        for i in range(17):
            a = 2 * math.pi * i / 16
            # Эллипс в наклонной плоскости: с левого плеча к правому бедру
            c = Vector((0, 0, 1.16))
            u = Vector((0.22, 0, 0.3))
            v = Vector((0, 0.16, 0))
            pts.append(c + u * math.cos(a) + v * math.sin(a))
        for i in range(16):
            tube("Body", "Skatka%d" % i, pts[i], pts[i + 1], 0.055, "coat")
    else:
        # Y-образные ремни, котелок, сухарная сумка, футляр противогаза на спине, лопатка
        for s in (-1, 1):
            tube("Body", "YStrapF%d" % s, (s * 0.09, 0.13, 1.0), (s * 0.1, 0.1, 1.48), 0.012, "gear")
            tube("Body", "YStrapB%d" % s, (s * 0.1, -0.1, 1.48), (0, -0.14, 1.2), 0.012, "gear")
            K.box("Pouch", "Body", (s * 0.11, 0.15, 0.97), (0.08, 0.05, 0.07), "gear", 0.008)
        tube("Body", "YStrapLow", (0, -0.14, 1.2), (0, -0.14, 0.99), 0.012, "gear")
        K.cyl("GasMask", "Body", (0.06, -0.2, 0.85), 0.055, 0.28, "Z", 14, "helmet", 0.006)
        for k in range(4):
            K.cyl("GasMaskRib", "Body", (0.06, -0.2, 0.75 + k * 0.07), 0.058, 0.012, "Z", 14, "helmet", 0)
        K.box("BreadBag", "Body", (0.17, -0.06, 0.87), (0.05, 0.18, 0.14), "pack", 0.012)
        K.cyl("MessKit", "Body", (0.19, -0.13, 0.98), 0.06, 0.09, "X", 12, "helmet", 0.006)
        K.box("Shovel", "Body", (-0.19, -0.05, 0.85), (0.03, 0.12, 0.22), "metal", 0.005)

    # --- Ноги: брюки (у советских — шаровары, расширены на бёдрах), высокие сапоги ---
    for s, name in ((-1, "LegL"), (1, "LegR")):
        x = s * 0.11
        thigh = 0.11 if sov else 0.1
        rings(name, name + "_Trousers", [(0.97, 0.1, 0.1), (0.82, thigh, thigh + 0.005), (0.62, 0.085, 0.085), (0.5, 0.072, 0.072)], "trousers", 70, x)
        rings(name, name + "_Boot", [(0.47, 0.07, 0.072), (0.3, 0.062, 0.066), (0.12, 0.056, 0.062), (0.04, 0.058, 0.07)], "boots", 70, x)
        K.hull(name + "_Foot", name, [(x + dx, y, z) for dx in (-0.05, 0.05) for (y, z) in ((-0.06, 0.0), (-0.06, 0.1), (0.17, 0.0), (0.16, 0.06), (0.05, 0.11))], "boots", 0.01, 60)

    # --- Руки: рукав с манжетой, кисть ---
    for s, name in ((-1, "ArmL"), (1, "ArmR")):
        x = s * 0.235
        K.sphere(name + "_Shoulder", name, (x, 0, 1.44), 0.075, "tunic", (1, 1, 1.05))
        rings(name, name + "_Sleeve", [(1.47, 0.068, 0.07), (1.36, 0.064, 0.066), (1.12, 0.054, 0.058), (0.95, 0.048, 0.05)], "tunic", 70, x)
        rings(name, name + "_Cuff", [(0.97, 0.054, 0.056), (0.93, 0.052, 0.054)], "tunic", 40, x)
        rings(name, name + "_Hand", [(0.93, 0.038, 0.045), (0.87, 0.042, 0.05), (0.82, 0.03, 0.04)], "skin", 70, x)

    # --- Голова: лицо, уши, каска ---
    rings("Head", "Neck", [(1.5, 0.055, 0.055), (1.64, 0.05, 0.052)], "skin")
    K.hull("Face", "Head", ring(0, 0.005, 1.6, 0.055, 0.06, 10) + ring(0, 0.01, 1.65, 0.08, 0.09, 12) + ring(0, 0.0, 1.73, 0.088, 0.1, 12)
           + ring(0, -0.01, 1.8, 0.075, 0.09, 12) + [(0, -0.01, 1.83)], "skin", 0, 70)
    K.hull("Nose", "Head", [(0, 0.105, 1.69), (0.015, 0.1, 1.72), (-0.015, 0.1, 1.72), (0, 0.118, 1.67), (0.012, 0.09, 1.66), (-0.012, 0.09, 1.66)], "skin", 0, 40)
    for s in (-1, 1):
        K.box("Eye", "Head", (s * 0.035, 0.093, 1.725), (0.025, 0.006, 0.012), "dark", 0)
        K.box("Brow", "Head", (s * 0.036, 0.095, 1.745), (0.035, 0.01, 0.01), "hair", 0)
        K.sphere("Ear", "Head", (s * 0.088, -0.005, 1.71), 0.022, "skin", (0.5, 1, 1.4))
    if sov:
        # СШ-40: круглый купол с небольшим полем и характерным «козырьком»
        K.hull("Helmet", "Head", ring(0, -0.01, 1.72, 0.145, 0.155, 20) + ring(0, -0.01, 1.745, 0.142, 0.152, 20)
               + ring(0, -0.01, 1.81, 0.125, 0.135, 16) + ring(0, -0.01, 1.86, 0.08, 0.09, 12) + [(0, -0.01, 1.885)], "helmet", 0, 70)
        K.hull("HelmetBrim", "Head", ring(0, -0.005, 1.715, 0.155, 0.168, 24) + ring(0, -0.005, 1.725, 0.15, 0.162, 24), "helmet", 0, 30)
    else:
        # Штальхельм: купол, опущенные назад и по бокам поля с отгибом наружу
        pts = ring(0, -0.015, 1.81, 0.13, 0.14, 16) + ring(0, -0.015, 1.86, 0.085, 0.095, 12) + [(0, -0.015, 1.885)]
        for i in range(24):
            a = 2 * math.pi * i / 24
            # Перед (a≈π/2, +Y) — выше, затылок и бока ниже
            front = max(0.0, math.sin(a))
            z = 1.735 - (1 - front) * 0.085
            r = 0.17 + (1 - front) * 0.015
            pts.append((math.cos(a) * r * 0.95, -0.015 + math.sin(a) * r, z))
        K.hull("Helmet", "Head", pts, "helmet", 0, 50)

    name = "Soldier_" + side
    K.export(os.path.join(OUT, name + ".bytes"))
    if BLEND:
        K.save_blend(os.path.join(BLEND, name.lower() + ".blend"))


# =====================================================================
#   Оружие (начало координат — затыльник приклада, ствол вперёд по +Y)
# =====================================================================

def weapons():
    K.reset()
    W = {}

    def weapon(name, muzzle_y, muzzle_z=0.02):
        K.part(name, None, (0, 0, 0))
        K.part(name + "_Muzzle", name, (0, muzzle_y, muzzle_z))

    def stock(prt, y0, y1, h_butt, h_wrist, w=0.042):
        pts = []
        for (y, h, zc) in ((y0, h_butt, -0.05), (y0 + 0.05, h_butt, -0.05), (y0 + 0.3, h_wrist, -0.01), (y1, 0.05, 0.0)):
            for sx in (-1, 1):
                pts += [(sx * w * 0.5, y, zc - h * 0.5), (sx * w * 0.5, y, zc + h * 0.5)]
        K.hull(prt + "_Stock", prt, pts, "wood", 0.004, 40)

    # Винтовка Мосина обр. 1891/30 с примкнутым игольчатым штыком
    weapon("Mosin", 1.66)
    stock("Mosin", 0.0, 0.98, 0.13, 0.045)
    K.cyl("Mosin_Barrel", "Mosin", (0, 0.86, 0.025), 0.012, 0.76, "Y", 10, "metal", 0)
    K.cyl("Mosin_Receiver", "Mosin", (0, 0.42, 0.03), 0.018, 0.2, "Y", 10, "metal", 0.002)
    K.cyl("Mosin_Bolt", "Mosin", (0.04, 0.4, 0.03), 0.006, 0.07, "X", 6, "metal", 0)
    K.sphere("Mosin_BoltKnob", "Mosin", (0.075, 0.4, 0.03), 0.012, "metal")
    K.cyl("Mosin_Bayonet", "Mosin", (0.012, 1.45, 0.02), 0.006, 0.43, "Y", 6, "metal", 0, radius2=0.002)
    for y in (0.62, 0.9):
        K.cyl("Mosin_Band", "Mosin", (0, y, 0.015), 0.024, 0.02, "Y", 10, "metal", 0)

    # Kar98k
    weapon("Kar98", 1.12)
    stock("Kar98", 0.0, 0.92, 0.12, 0.045)
    K.cyl("Kar98_Barrel", "Kar98", (0, 0.8, 0.025), 0.012, 0.64, "Y", 10, "metal", 0)
    K.cyl("Kar98_Receiver", "Kar98", (0, 0.42, 0.03), 0.018, 0.2, "Y", 10, "metal", 0.002)
    K.cyl("Kar98_Bolt", "Kar98", (0.03, 0.36, 0.0), 0.006, 0.08, "X", 6, "metal", 0)
    K.sphere("Kar98_BoltKnob", "Kar98", (0.07, 0.36, -0.01), 0.013, "metal")

    # ППШ-41: деревянное ложе, кожух с окнами, дисковый магазин
    weapon("Ppsh", 0.86)
    stock("Ppsh", 0.0, 0.45, 0.12, 0.05, 0.04)
    K.cyl("Ppsh_Receiver", "Ppsh", (0, 0.45, 0.03), 0.028, 0.22, "Y", 12, "metal", 0.003)
    K.cyl("Ppsh_Jacket", "Ppsh", (0, 0.68, 0.035), 0.022, 0.3, "Y", 12, "metal", 0.002)
    for k in range(5):
        K.box("Ppsh_Slot", "Ppsh", (0.021, 0.58 + k * 0.05, 0.035), (0.004, 0.025, 0.012), "dark", 0)
    K.cyl("Ppsh_Drum", "Ppsh", (0, 0.43, -0.07), 0.1, 0.06, "X", 20, "metal", 0.004)
    K.box("Ppsh_Comp", "Ppsh", (0, 0.84, 0.04), (0.03, 0.04, 0.04), "metal", 0.003)

    # MP 40: металлический складной приклад, ствольная коробка, прямой магазин, рукоятка
    weapon("Mp40", 0.82)
    K.box("Mp40_StockTop", "Mp40", (0, 0.13, 0.02), (0.012, 0.26, 0.015), "metal", 0.002)
    K.box("Mp40_StockBot", "Mp40", (0, 0.13, -0.07), (0.012, 0.26, 0.015), "metal", 0.002)
    K.box("Mp40_Butt", "Mp40", (0, 0.0, -0.025), (0.04, 0.02, 0.11), "metal", 0.003)
    K.cyl("Mp40_Receiver", "Mp40", (0, 0.42, 0.02), 0.022, 0.34, "Y", 12, "metal", 0.002)
    K.cyl("Mp40_Barrel", "Mp40", (0, 0.7, 0.02), 0.012, 0.22, "Y", 10, "metal", 0)
    K.box("Mp40_Grip", "Mp40", (0, 0.33, -0.07), (0.03, 0.05, 0.11), "bakelite", 0.006, (-15, 0, 0))
    K.box("Mp40_Mag", "Mp40", (0, 0.47, -0.13), (0.025, 0.035, 0.22), "metal", 0.003)

    # «Максим» обр. 1910: кожух водяного охлаждения, щиток
    weapon("Maxim", 1.1)
    K.cyl("Maxim_Jacket", "Maxim", (0, 0.55, 0.03), 0.065, 0.62, "Y", 16, "paint", 0.006)
    for k in range(5):
        K.cyl("Maxim_Rib", "Maxim", (0, 0.3 + k * 0.12, 0.03), 0.069, 0.02, "Y", 16, "paint", 0)
    K.box("Maxim_Body", "Maxim", (0, 0.12, 0.02), (0.1, 0.24, 0.12), "paint", 0.006)
    K.cyl("Maxim_Barrel", "Maxim", (0, 0.95, 0.03), 0.015, 0.2, "Y", 8, "metal", 0)
    K.box("Maxim_Handles", "Maxim", (0, -0.02, 0.02), (0.14, 0.03, 0.03), "metal", 0.003)
    K.plate("Maxim_Shield", "Maxim", [(-0.24, 0.3, -0.18), (0.24, 0.3, -0.18), (0.22, 0.3, 0.28), (-0.22, 0.3, 0.28)], 0.008, "paint", 0.003)
    K.box("Maxim_Belt", "Maxim", (0.09, 0.08, 0.0), (0.06, 0.02, 0.12), "pack", 0.004)

    # MG 42: перфорированный кожух ствола, приклад, сошки
    weapon("Mg42", 1.2)
    K.hull("Mg42_Stock", "Mg42", [(sx * 0.02, y, z) for sx in (-1, 1) for (y, z) in ((0, -0.07), (0, 0.04), (0.25, -0.01), (0.25, 0.04))], "bakelite", 0.004)
    K.box("Mg42_Receiver", "Mg42", (0, 0.4, 0.02), (0.045, 0.32, 0.07), "metal", 0.004)
    K.box("Mg42_Jacket", "Mg42", (0, 0.82, 0.03), (0.04, 0.55, 0.045), "metal", 0.004)
    for k in range(6):
        K.box("Mg42_Hole", "Mg42", (0.021, 0.64 + k * 0.07, 0.03), (0.004, 0.035, 0.02), "dark", 0)
    K.box("Mg42_Grip", "Mg42", (0, 0.32, -0.06), (0.03, 0.05, 0.1), "bakelite", 0.005, (-15, 0, 0))
    for s in (-1, 1):
        tube("Mg42", "Mg42_Bipod%d" % s, (0, 0.95, 0.0), (s * 0.12, 0.98, -0.3), 0.008, "metal", 6)
    K.box("Mg42_Belt", "Mg42", (-0.06, 0.4, 0.0), (0.06, 0.06, 0.03), "metal", 0.003)

    # ПТРД-41: очень длинный ствол с дульным тормозом, сошки
    weapon("Ptrd", 2.0)
    stock("Ptrd", 0.0, 0.55, 0.13, 0.05, 0.05)
    K.cyl("Ptrd_Barrel", "Ptrd", (0, 1.2, 0.03), 0.022, 1.4, "Y", 12, "metal", 0.002, radius2=0.018)
    K.cyl("Ptrd_Brake", "Ptrd", (0, 1.93, 0.03), 0.035, 0.1, "Y", 12, "metal", 0.004)
    K.cyl("Ptrd_Receiver", "Ptrd", (0, 0.55, 0.03), 0.035, 0.22, "Y", 12, "metal", 0.003)
    for s in (-1, 1):
        tube("Ptrd", "Ptrd_Bipod%d" % s, (0, 1.0, 0.02), (s * 0.15, 1.05, -0.3), 0.01, "metal", 6)

    # «Панцершрек»: труба, щиток с окошком, кольцо-защита сзади
    weapon("Schreck", 1.25, 0.0)
    K.cyl("Schreck_Tube", "Schreck", (0, 0.42, 0.0), 0.046, 1.64, "Y", 16, "paint", 0.004)
    K.cyl("Schreck_Ring", "Schreck", (0, -0.38, 0.0), 0.055, 0.03, "Y", 16, "metal", 0.002)
    K.plate("Schreck_Shield", "Schreck", [(-0.2, 0.62, -0.12), (0.12, 0.62, -0.12), (0.12, 0.62, 0.22), (-0.2, 0.62, 0.22)], 0.006, "paint", 0.003)
    K.box("Schreck_Window", "Schreck", (-0.12, 0.618, 0.14), (0.06, 0.008, 0.05), "dark", 0)
    K.box("Schreck_Grip", "Schreck", (0, 0.2, -0.09), (0.025, 0.04, 0.09), "wood", 0.004)

    K.export(os.path.join(OUT, "Weapons.bytes"))
    if BLEND:
        K.save_blend(os.path.join(BLEND, "weapons.blend"))


soldier("USSR")
soldier("Germany")
weapons()
