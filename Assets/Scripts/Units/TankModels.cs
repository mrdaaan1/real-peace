using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>Детали модели танка, которые двигаются: башня, ствол (откат), дуло, гусеницы.</summary>
    public class TankParts
    {
        public Transform root, hull, turret, gun, muzzle, coaxMuzzle;
        public Transform[] wheels;
        /// <summary>Исходный поворот катка и ось его вращения (у примитивов и у моделей из Blender разные).</summary>
        public Quaternion[] wheelBase;
        public Vector3 wheelAxis = Vector3.up;
        public float gunRestZ;
    }

    /// <summary>
    /// Модели танков из примитивов и призм. Т-34-76 обр. 1943: наклонный лобовой лист, башня-«гайка», большие катки
    /// Кристи, круглые люки, красные звёзды. Pz.IV Ausf. H: угловатый корпус, мелкие катки, длинная пушка с дульным
    /// тормозом, противокумулятивные экраны, командирская башенка, балочные кресты.
    /// Начало координат — на земле в центре, вперёд — +Z.
    /// </summary>
    public static class TankModels
    {
        static readonly Color Track = Shapes.Hex("#2b2a27");
        static readonly Color Rubber = Shapes.Hex("#1f1f1e");
        static readonly Color Steel = Shapes.Hex("#3a3b38");

        public static TankParts Build(Transform parent, Faction side)
        {
            var model = ModelLibrary.Load(side == Faction.USSR ? "T34" : "PzIV");
            if (model != null) return FromModel(model, parent, side);

            var p = new TankParts();
            p.root = Shapes.Group("Model", parent);
            p.hull = Shapes.Group("Hull", p.root);
            using (Shapes.Brush(Surf.Armor))
            {
                if (side == Faction.USSR) T34(p, side); else PzIV(p, side);
            }
            foreach (var r in p.root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            p.wheelBase = new Quaternion[p.wheels.Length];
            for (int i = 0; i < p.wheels.Length; i++) p.wheelBase[i] = p.wheels[i].localRotation;
            return p;
        }

        /// <summary>Танк из модели Blender (Resources/Models): материалы по именам слотов, части — по именам.</summary>
        static TankParts FromModel(ModelLibrary.Model model, Transform parent, Faction side)
        {
            var paint = Factions.TankPaint(side);
            var dark = Factions.TankPaintDark(side);
            Material Resolve(string partName, string slot)
            {
                var m = Slot(slot);
                bool upper = partName == "Turret" || partName == "Gun";
                return upper && m.GetFloat("_Dirt") > 0.2f ? Mats.Clean(m) : m;
            }
            Material Slot(string slot)
            {
                switch (slot)
                {
                    case "paint": return side == Faction.Germany ? Mats.Camo(paint, Shapes.Hex("#56603a")) : Mats.Get(paint, Surf.Armor);
                    case "paint_dark": return Mats.Get(dark, Surf.Armor);
                    case "track": return Mats.Get(Shapes.Hex("#3a3632"), Surf.Track);
                    case "metal": return Mats.Get(Shapes.Hex("#3b3c3a"), Surf.Metal);
                    case "rubber": return Mats.Get(Shapes.Hex("#1e1e1d"), Surf.Leather);
                    case "dark": return Mats.Get(Shapes.Hex("#0e0f0f"), Surf.Paint);
                    case "marking_red": return Mats.Get(Shapes.Hex("#9c2219"), Surf.Paint);
                    case "marking_white": return Mats.Get(Shapes.Hex("#d9d6c8"), Surf.Paint);
                    case "marking_black": return Mats.Get(Shapes.Hex("#121212"), Surf.Paint);
                    default: return Mats.Get(paint, Surf.Armor);
                }
            }
            var root = Shapes.Group("Model", parent);
            var parts = ModelLibrary.Build(model, root, Resolve);
            var p = new TankParts { root = root, wheelAxis = Vector3.right };
            p.hull = parts["Hull"];
            p.turret = parts["Turret"];
            p.gun = parts["Gun"];
            p.muzzle = parts["Muzzle"];
            p.coaxMuzzle = parts["Coax"];
            p.gunRestZ = p.gun.localPosition.z;
            var wheels = new List<Transform>();
            foreach (var kv in parts) if (kv.Key.StartsWith("Wheel_")) wheels.Add(kv.Value);
            p.wheels = wheels.ToArray();
            p.wheelBase = new Quaternion[p.wheels.Length];
            for (int i = 0; i < p.wheels.Length; i++) p.wheelBase[i] = Quaternion.identity;
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return p;
        }

        static void Tracks(TankParts p, float halfTrack, float trackWidth, float length, float trackHeight, int wheels, float wheelD, Color paint)
        {
            var list = new List<Transform>();
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * halfTrack;
                using (Shapes.Brush(Surf.Track))
                    Shapes.Box(p.hull, new Vector3(x, trackHeight * 0.5f, 0f), new Vector3(trackWidth, trackHeight, length), Track, name: "Track");
                // Ленивец и ведущее колесо
                Shapes.Part(Shapes.LowCylinder, p.hull, new Vector3(x, trackHeight * 0.55f, length * 0.5f - 0.35f), new Vector3(0.6f, trackWidth + 0.04f, 0.6f), Steel, new Vector3(0f, 0f, 90f));
                Shapes.Part(Shapes.LowCylinder, p.hull, new Vector3(x, trackHeight * 0.55f, -length * 0.5f + 0.35f), new Vector3(0.66f, trackWidth + 0.04f, 0.66f), Steel, new Vector3(0f, 0f, 90f));
                for (int i = 0; i < wheels; i++)
                {
                    float z = Mathf.Lerp(-length * 0.5f + 0.9f, length * 0.5f - 0.9f, wheels == 1 ? 0.5f : (float)i / (wheels - 1));
                    var w = Shapes.Part(Shapes.LowCylinder, p.hull, new Vector3(x + s * 0.05f, wheelD * 0.5f + 0.05f, z),
                        new Vector3(wheelD, trackWidth * 0.8f, wheelD), paint, new Vector3(0f, 0f, 90f), "Wheel").transform;
                    using (Shapes.Brush(Surf.Leather))
                        Shapes.Part(Shapes.LowCylinder, w, Vector3.zero, new Vector3(1.04f, 0.6f, 1.04f), Rubber);
                    list.Add(w);
                }
            }
            p.wheels = list.ToArray();
        }

        static void T34(TankParts p, Faction side)
        {
            var paint = Factions.TankPaint(side);
            var dark = Factions.TankPaintDark(side);
            Tracks(p, 1.28f, 0.52f, 6.0f, 0.95f, 5, 0.83f, paint);
            // Нижняя часть корпуса между гусеницами
            Shapes.Box(p.hull, new Vector3(0f, 0.75f, 0f), new Vector3(2.0f, 0.5f, 5.9f), dark);
            // Верхний корпус: наклонный лоб 60°, наклонные борта над гусеницами
            var hull = Shapes.Prism(new List<Vector2>
            {
                new Vector2(0.6f, 3.25f), new Vector2(1.75f, 2.15f), new Vector2(1.75f, -2.55f), new Vector2(0.9f, -3.15f), new Vector2(0.6f, -3.15f),
            }, 1.55f, 1.25f);
            Shapes.MeshObject("UpperHull", p.hull, hull, Shapes.Mat(paint));
            // Люк механика-водителя, пулемёт в лобовом листе, фары
            Shapes.Box(p.hull, new Vector3(-0.35f, 1.42f, 2.55f), new Vector3(0.6f, 0.45f, 0.12f), dark, new Vector3(-47f, 0f, 0f));
            Shapes.Part(Shapes.LowSphere, p.hull, new Vector3(0.45f, 1.35f, 2.62f), new Vector3(0.2f, 0.2f, 0.2f), dark);
            // Бочки с топливом на корме
            for (int s = -1; s <= 1; s += 2)
                Shapes.Part(Shapes.LowCylinder, p.hull, new Vector3(s * 1.05f, 1.55f, -2.7f), new Vector3(0.4f, 0.75f, 0.4f), dark, new Vector3(90f, 0f, 0f));
            // Ящики ЗИП на бортах
            Shapes.Box(p.hull, new Vector3(1.38f, 1.25f, 0.4f), new Vector3(0.22f, 0.3f, 1.2f), dark);
            Shapes.Box(p.hull, new Vector3(-1.38f, 1.25f, -0.6f), new Vector3(0.22f, 0.3f, 1.0f), dark);

            // Башня-«гайка»
            p.turret = Shapes.Group("Turret", p.hull, new Vector3(0f, 1.75f, 0.75f));
            var tur = Shapes.Prism(new List<Vector2>
            {
                new Vector2(0f, 1.35f), new Vector2(0.82f, 1.05f), new Vector2(0.82f, -1.05f), new Vector2(0f, -1.3f),
            }, 1.12f, 0.86f);
            Shapes.MeshObject("TurretBody", p.turret, tur, Shapes.Mat(paint));
            Shapes.Box(p.turret, new Vector3(0f, 0.42f, 1.35f), new Vector3(0.75f, 0.55f, 0.3f), dark, name: "Mantlet");
            // Два круглых люка («Микки Маус»)
            Shapes.Part(Shapes.LowCylinder, p.turret, new Vector3(-0.42f, 0.87f, -0.25f), new Vector3(0.55f, 0.1f, 0.55f), dark);
            Shapes.Part(Shapes.LowCylinder, p.turret, new Vector3(0.42f, 0.87f, -0.25f), new Vector3(0.55f, 0.1f, 0.55f), dark);
            // Пушка
            p.gun = Shapes.Group("Gun", p.turret, new Vector3(0f, 0.42f, 1.45f));
            Shapes.Part(Shapes.LowCylinder, p.gun, new Vector3(0f, 0f, 1.5f), new Vector3(0.17f, 3.0f, 0.17f), Steel, new Vector3(90f, 0f, 0f), "Barrel").GetComponent<Renderer>().sharedMaterial = Shapes.Mat(Steel, Surf.Metal);
            Shapes.Part(Shapes.LowCylinder, p.gun, new Vector3(0f, 0f, 0.35f), new Vector3(0.28f, 0.7f, 0.28f), dark, new Vector3(90f, 0f, 0f));
            p.muzzle = Shapes.Group("Muzzle", p.gun, new Vector3(0f, 0f, 3.05f));
            p.gunRestZ = p.gun.localPosition.z;
            p.coaxMuzzle = Shapes.Group("Coax", p.turret, new Vector3(0.28f, 0.4f, 1.55f));
            // Красные звёзды на бортах башни
            for (int s = -1; s <= 1; s += 2)
            {
                var star = Shapes.MeshObject("Star", p.turret, Shapes.Star, Shapes.Mat(Shapes.Hex("#a3241c"), Surf.Paint),
                    new Vector3(s * 1.02f, 0.42f, 0f), new Vector3(0f, s > 0 ? 90f : -90f, 0f));
                star.transform.localScale = Vector3.one * 0.55f;
                star.transform.position += star.transform.forward * 0.02f;
            }
            // Номер на корме башни — белые полосы (тактический знак)
            Shapes.Box(p.turret, new Vector3(0f, 0.55f, -1.27f), new Vector3(0.9f, 0.1f, 0.02f), Shapes.Hex("#d8d6cc"));
        }

        static void PzIV(TankParts p, Faction side)
        {
            var paint = Factions.TankPaint(side);
            var dark = Factions.TankPaintDark(side);
            Tracks(p, 1.2f, 0.48f, 5.6f, 0.85f, 8, 0.5f, paint);
            // Возвращающие ролики
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 4; i++)
                    Shapes.Part(Shapes.LowCylinder, p.hull, new Vector3(s * 1.2f, 0.95f, -1.8f + i * 1.2f), new Vector3(0.22f, 0.36f, 0.22f), Steel, new Vector3(0f, 0f, 90f));
            var hull = Shapes.Prism(new List<Vector2>
            {
                new Vector2(0.45f, 2.85f), new Vector2(1.3f, 2.95f), new Vector2(1.95f, 2.55f), new Vector2(1.95f, -2.6f), new Vector2(1.3f, -2.85f), new Vector2(0.45f, -2.85f),
            }, 1.0f, 1.0f);
            Shapes.MeshObject("Hull", p.hull, hull, Shapes.Mat(paint));
            // Надгусеничные полки
            Shapes.Box(p.hull, new Vector3(0f, 1.38f, -0.1f), new Vector3(2.88f, 0.08f, 5.2f), paint);
            // Рубка механика с выступом
            Shapes.Box(p.hull, new Vector3(-0.45f, 1.75f, 2.45f), new Vector3(0.9f, 0.4f, 0.25f), dark);
            Shapes.Box(p.hull, new Vector3(-0.45f, 1.82f, 2.58f), new Vector3(0.35f, 0.1f, 0.04f), Shapes.Hex("#151515"));
            // Запасные траки на лобовом листе
            for (int i = 0; i < 4; i++)
                Shapes.Box(p.hull, new Vector3(-0.75f + i * 0.5f, 1.2f, 2.9f), new Vector3(0.42f, 0.12f, 0.08f), Track, new Vector3(-30f, 0f, 0f));
            // Противокумулятивные экраны (Schürzen) с балочными крестами
            for (int s = -1; s <= 1; s += 2)
            {
                Shapes.Box(p.hull, new Vector3(s * 1.62f, 1.05f, -0.2f), new Vector3(0.05f, 0.95f, 4.6f), paint, name: "Skirt");
                BalkenKreuz(p.hull, new Vector3(s * 1.66f, 1.05f, -0.6f), s);
            }

            p.turret = Shapes.Group("Turret", p.hull, new Vector3(0f, 1.95f, 0.15f));
            var tur = Shapes.Prism(new List<Vector2>
            {
                new Vector2(0f, 1.2f), new Vector2(0.85f, 1.1f), new Vector2(0.85f, -1.15f), new Vector2(0f, -1.3f),
            }, 1.05f, 0.82f);
            Shapes.MeshObject("TurretBody", p.turret, tur, Shapes.Mat(paint));
            // Командирская башенка
            Shapes.Part(Shapes.LowCylinder, p.turret, new Vector3(0f, 1.0f, -0.85f), new Vector3(0.75f, 0.32f, 0.75f), dark);
            Shapes.Box(p.turret, new Vector3(0f, 0.4f, 1.2f), new Vector3(0.95f, 0.5f, 0.2f), dark, name: "Mantlet");
            // Экраны башни
            Shapes.Box(p.turret, new Vector3(1.15f, 0.42f, -0.2f), new Vector3(0.05f, 0.75f, 1.8f), paint);
            Shapes.Box(p.turret, new Vector3(-1.15f, 0.42f, -0.2f), new Vector3(0.05f, 0.75f, 1.8f), paint);
            Shapes.Box(p.turret, new Vector3(0f, 0.42f, -1.55f), new Vector3(2.3f, 0.75f, 0.05f), paint);
            // Длинная 75-мм пушка с дульным тормозом
            p.gun = Shapes.Group("Gun", p.turret, new Vector3(0f, 0.4f, 1.3f));
            Shapes.Part(Shapes.LowCylinder, p.gun, new Vector3(0f, 0f, 1.85f), new Vector3(0.15f, 3.7f, 0.15f), Steel, new Vector3(90f, 0f, 0f), "Barrel").GetComponent<Renderer>().sharedMaterial = Shapes.Mat(Steel, Surf.Metal);
            Shapes.Box(p.gun, new Vector3(0f, 0f, 3.72f), new Vector3(0.3f, 0.22f, 0.32f), Steel, name: "MuzzleBrake");
            p.muzzle = Shapes.Group("Muzzle", p.gun, new Vector3(0f, 0f, 3.9f));
            p.gunRestZ = p.gun.localPosition.z;
            p.coaxMuzzle = Shapes.Group("Coax", p.turret, new Vector3(-0.3f, 0.4f, 1.35f));
            // Номер на экране башни — тёмные «цифры» полосками
            for (int s = -1; s <= 1; s += 2)
                Shapes.Box(p.turret, new Vector3(s * 1.18f, 0.5f, -0.1f), new Vector3(0.02f, 0.28f, 0.8f), Shapes.Hex("#2a2a26"));
        }

        /// <summary>Балочный крест: чёрный крест с белой окантовкой. s — сторона (+1 справа).</summary>
        static void BalkenKreuz(Transform parent, Vector3 at, int s)
        {
            var g = Shapes.Group("Cross", parent, at, new Vector3(0f, s > 0 ? 90f : -90f, 0f));
            var white = Shapes.Hex("#e8e6dc");
            var black = Shapes.Hex("#151515");
            Shapes.Box(g, new Vector3(0f, 0f, 0f), new Vector3(0.7f, 0.22f, 0.012f), white);
            Shapes.Box(g, new Vector3(0f, 0f, 0f), new Vector3(0.22f, 0.7f, 0.012f), white);
            Shapes.Box(g, new Vector3(0f, 0f, 0.008f), new Vector3(0.6f, 0.12f, 0.012f), black);
            Shapes.Box(g, new Vector3(0f, 0f, 0.008f), new Vector3(0.12f, 0.6f, 0.012f), black);
        }
    }
}
