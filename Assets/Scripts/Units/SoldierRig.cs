using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    public enum SoldierPose { Stand, Crouch, Prone, Dead }

    /// <summary>
    /// Солдат из примитивов: ноги, туловище с ремнём, руки, голова, каска своей стороны, оружие.
    /// Позы: идёт (ноги и руки качаются), целится, пригнулся, лежит, убит (падает навзничь).
    /// Каркас тот же, что у человечка из «Симулятора очереди» (HumanRig), опора — между ступнями, вперёд — +Z.
    /// </summary>
    public class SoldierRig : MonoBehaviour
    {
        public Transform body, legL, legR, armL, armR, head, gun, muzzle;
        public Renderer[] Renderers { get; private set; }

        float phase;
        float crouch, prone, dead, aim;
        float flinch;
        Vector3 gunReadyPos, gunAimPos, gunProneAimPos;
        Quaternion gunReadyRot;
        Quaternion gunAimRot = Quaternion.identity;
        Vector3 deadTilt;

        public static SoldierRig Build(Transform parent, Faction side, Weapon weapon, int seed)
        {
            var model = ModelLibrary.Load("Soldier_" + side);
            var weapons = ModelLibrary.Load("Weapons");
            if (model != null && weapons != null) return BuildFromModel(parent, side, weapon, seed, model, weapons);

            var root = new GameObject("Soldier").transform;
            root.SetParent(parent, false);
            var rig = root.gameObject.AddComponent<SoldierRig>();
            var rnd = new System.Random(seed);
            Color[] skins = { Shapes.Hex("#e0ac85"), Shapes.Hex("#c9926b"), Shapes.Hex("#d9a27c") };
            var skin = skins[rnd.Next(skins.Length)];
            var tunic = Factions.Tunic(side);
            var pants = Factions.Trousers(side);
            var boots = Factions.Boots(side);

            rig.body = Shapes.Group("Body", root);
            var brush = Shapes.Brush(Surf.Fabric);
            rig.legL = Limb(rig.body, "LegL", new Vector3(-0.11f, 0.9f, 0f), new Vector3(0.17f, 0.86f, 0.2f), pants, boots, true);
            rig.legR = Limb(rig.body, "LegR", new Vector3(0.11f, 0.9f, 0f), new Vector3(0.17f, 0.86f, 0.2f), pants, boots, true);
            Shapes.Box(rig.body, new Vector3(0f, 1.2f, 0f), new Vector3(0.44f, 0.6f, 0.26f), tunic, name: "Torso");
            Shapes.Box(rig.body, new Vector3(0f, 0.95f, 0f), new Vector3(0.46f, 0.07f, 0.28f), Factions.Gear(side), name: "Belt").GetComponent<Renderer>().sharedMaterial = Shapes.Mat(Factions.Gear(side), Surf.Leather);
            // Подсумки и вещмешок/ранец
            Shapes.Box(rig.body, new Vector3(-0.12f, 0.98f, 0.15f), new Vector3(0.12f, 0.1f, 0.06f), Factions.Gear(side));
            Shapes.Box(rig.body, new Vector3(0.12f, 0.98f, 0.15f), new Vector3(0.12f, 0.1f, 0.06f), Factions.Gear(side));
            Shapes.Box(rig.body, new Vector3(0f, 1.2f, -0.18f), new Vector3(0.32f, 0.36f, 0.12f), side == Faction.USSR ? Shapes.Hex("#6f6343") : Shapes.Hex("#4c4a40"), name: "Pack");
            rig.armL = Limb(rig.body, "ArmL", new Vector3(-0.29f, 1.46f, 0f), new Vector3(0.12f, 0.6f, 0.14f), tunic, skin, false);
            rig.armR = Limb(rig.body, "ArmR", new Vector3(0.29f, 1.46f, 0f), new Vector3(0.12f, 0.6f, 0.14f), tunic, skin, false);
            brush.Dispose();
            brush = Shapes.Brush(Surf.Skin);
            Shapes.Part(Shapes.LowCylinder, rig.body, new Vector3(0f, 1.55f, 0f), new Vector3(0.11f, 0.1f, 0.11f), skin, name: "Neck");
            rig.head = Shapes.Group("Head", rig.body, new Vector3(0f, 1.7f, 0f));
            Shapes.Part(Shapes.LowSphere, rig.head, Vector3.zero, new Vector3(0.23f, 0.26f, 0.24f), skin);
            // Лицо: брови, глаза, нос — с камеры стратегии мелочь, но на крупном плане голова не «яйцо»
            Shapes.Box(rig.head, new Vector3(0f, -0.01f, 0.12f), new Vector3(0.04f, 0.06f, 0.04f), skin, name: "Nose");
            Shapes.Box(rig.head, new Vector3(0.05f, 0.03f, 0.11f), new Vector3(0.035f, 0.02f, 0.01f), Shapes.Hex("#1d1813"), name: "Eye");
            Shapes.Box(rig.head, new Vector3(-0.05f, 0.03f, 0.11f), new Vector3(0.035f, 0.02f, 0.01f), Shapes.Hex("#1d1813"), name: "Eye");
            brush.Dispose();
            brush = Shapes.Brush(Surf.Paint);
            var helmet = Factions.Helmet(side);
            if (side == Faction.Germany)
            {
                // Штальхельм: купол и характерный расширяющийся назад «козырёк»
                Shapes.Part(Shapes.LowSphere, rig.head, new Vector3(0f, 0.06f, -0.01f), new Vector3(0.3f, 0.25f, 0.31f), helmet, name: "Helmet");
                Shapes.Part(Shapes.LowCylinder, rig.head, new Vector3(0f, 0.0f, -0.02f), new Vector3(0.33f, 0.07f, 0.34f), helmet, new Vector3(-8f, 0f, 0f), "HelmetSkirt");
            }
            else
            {
                // СШ-40: круглая каска с небольшим полем
                Shapes.Part(Shapes.LowSphere, rig.head, new Vector3(0f, 0.07f, 0f), new Vector3(0.29f, 0.24f, 0.3f), helmet, name: "Helmet");
                Shapes.Part(Shapes.LowCylinder, rig.head, new Vector3(0f, 0.035f, 0f), new Vector3(0.31f, 0.03f, 0.32f), helmet, name: "HelmetRim");
            }

            brush.Dispose();
            rig.BuildGun(weapon, side);
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            foreach (var r in rig.Renderers) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return rig;
        }

        /// <summary>Солдат из модели Blender: части фигуры и оружие по типу и стороне.</summary>
        static SoldierRig BuildFromModel(Transform parent, Faction side, Weapon weapon, int seed, ModelLibrary.Model model, ModelLibrary.Model weapons)
        {
            var root = new GameObject("Soldier").transform;
            root.SetParent(parent, false);
            var rig = root.gameObject.AddComponent<SoldierRig>();
            var rnd = new System.Random(seed);
            Color[] skins = { Shapes.Hex("#d8a684"), Shapes.Hex("#c48e6a"), Shapes.Hex("#e0b090") };
            var skin = skins[rnd.Next(skins.Length)];
            // Форма у каждого чуть своего оттенка — выгоревшая, стираная
            float fade = 0.92f + (float)rnd.NextDouble() * 0.16f;
            Material Body(string part, string slot)
            {
                switch (slot)
                {
                    case "tunic": return Mats.Get(Shapes.Shade(Factions.Tunic(side), fade), Surf.Fabric);
                    case "trousers": return Mats.Get(Shapes.Shade(Factions.Trousers(side), fade), Surf.Fabric);
                    case "collar": return Mats.Get(Shapes.Hex("#3e4a3c"), Surf.Fabric);
                    case "coat": return Mats.Get(Shapes.Hex("#6e6656"), Surf.Fabric);
                    case "skin": return Mats.Get(skin, Surf.Skin);
                    case "boots": return Mats.Get(Factions.Boots(side), Surf.Leather);
                    case "gear": return Mats.Get(Factions.Gear(side), Surf.Leather);
                    case "pack": return Mats.Get(side == Faction.USSR ? Shapes.Hex("#6f6343") : Shapes.Hex("#5a5646"), Surf.Burlap);
                    case "helmet": return Mats.Get(Factions.Helmet(side), Surf.Paint);
                    case "metal": return Mats.Get(Shapes.Hex("#3a3b3a"), Surf.Metal);
                    case "hair": return Mats.Get(Shapes.Hex("#3a2a1c"), Surf.Paint);
                    default: return Mats.Get(Shapes.Hex("#141414"), Surf.Paint);
                }
            }
            var parts = ModelLibrary.Build(model, root, Body);
            rig.body = parts["Body"];
            rig.legL = parts["LegL"];
            rig.legR = parts["LegR"];
            rig.armL = parts["ArmL"];
            rig.armR = parts["ArmR"];
            rig.head = parts["Head"];

            string gunName;
            switch (weapon.Sound)
            {
                case ShotSound.Smg: gunName = side == Faction.USSR ? "Ppsh" : "Mp40"; break;
                case ShotSound.MachineGun: gunName = side == Faction.USSR ? "Maxim" : "Mg42"; break;
                case ShotSound.AtRifle: gunName = "Ptrd"; break;
                case ShotSound.Rocket: gunName = "Schreck"; break;
                default: gunName = side == Faction.USSR ? "Mosin" : "Kar98"; break;
            }
            if (!string.IsNullOrEmpty(weapon.Model)) gunName = weapon.Model;
            Material Gun(string part, string slot)
            {
                switch (slot)
                {
                    case "wood": return Mats.Get(Shapes.Hex("#5e3e24"), Surf.Wood);
                    case "metal": return Mats.Get(Shapes.Hex("#2c2d2e"), Surf.Metal);
                    case "bakelite": return Mats.Get(Shapes.Hex("#3a2418"), Surf.Paint);
                    case "paint": return Mats.Get(side == Faction.USSR ? Shapes.Hex("#46523a") : Shapes.Hex("#545750"), Surf.Paint);
                    case "pack": return Mats.Get(Shapes.Hex("#6f6343"), Surf.Burlap);
                    default: return Mats.Get(Shapes.Hex("#101010"), Surf.Paint);
                }
            }
            rig.gun = Shapes.Group("Gun", rig.body);
            var gunParts = ModelLibrary.BuildPart(weapons, gunName, rig.gun, Gun);
            rig.muzzle = gunParts.TryGetValue(gunName + "_Muzzle", out var mz) ? mz : rig.gun;
            rig.gunReadyPos = new Vector3(0.14f, 1.12f, 0.02f);
            rig.gunReadyRot = Quaternion.Euler(-25f, 0f, 0f);
            rig.gunAimPos = new Vector3(0.13f, 1.4f, 0.0f);
            if (weapon.Sound == ShotSound.Rocket) { rig.gunAimPos = new Vector3(0.18f, 1.5f, -0.1f); rig.gunReadyPos = new Vector3(0.18f, 1.45f, -0.2f); rig.gunReadyRot = Quaternion.identity; }
            if (weapon.Sound == ShotSound.MachineGun || weapon.Sound == ShotSound.AtRifle) { rig.gunReadyPos = new Vector3(0.16f, 1.05f, -0.1f); rig.gunReadyRot = Quaternion.Euler(-35f, 0f, 0f); }
            if (weapon.Indirect)
            {
                // Миномёт: на марше — за спиной стволом вниз, в бою — на земле перед расчётом, ствол вверх
                rig.gunReadyPos = new Vector3(0.05f, 1.45f, -0.22f);
                rig.gunReadyRot = Quaternion.Euler(170f, 0f, 0f);
                rig.gunAimPos = new Vector3(0f, 0.45f, 0.85f);
                rig.gunAimRot = Quaternion.Euler(-58f, 0f, 0f);
            }
            if (weapon.Flame)
            {
                // Баллоны огнемёта за спиной
                var pack = Shapes.Group("FlamePack", rig.body, new Vector3(0f, 1.22f, -0.24f));
                ModelLibrary.BuildPart(weapons, gunName + "_Pack", pack, Gun);
            }
            rig.gunProneAimPos = rig.gunAimPos;
            rig.gun.localPosition = rig.gunReadyPos;
            rig.gun.localRotation = rig.gunReadyRot;
            rig.Renderers = root.GetComponentsInChildren<Renderer>();
            foreach (var r in rig.Renderers) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return rig;
        }

        static Transform Limb(Transform parent, string name, Vector3 joint, Vector3 size, Color color, Color end, bool leg)
        {
            var pivot = Shapes.Group(name, parent, joint);
            Shapes.Box(pivot, new Vector3(0f, -size.y / 2f, 0f), size, color).GetComponent<Renderer>().sharedMaterial = Shapes.Mat(color, Surf.Fabric);
            var e = Shapes.Box(pivot, new Vector3(0f, -size.y + (leg ? 0.04f : -0.04f), leg ? 0.05f : 0f),
                leg ? new Vector3(size.x + 0.02f, 0.1f, size.z + 0.1f) : new Vector3(size.x, 0.1f, size.z), end);
            e.GetComponent<Renderer>().sharedMaterial = Shapes.Mat(end, leg ? Surf.Leather : Surf.Skin);
            if (leg) // голенище сапога
                Shapes.Box(pivot, new Vector3(0f, -size.y * 0.8f, 0f), new Vector3(size.x + 0.03f, size.y * 0.38f, size.z + 0.03f), end)
                    .GetComponent<Renderer>().sharedMaterial = Shapes.Mat(end, Surf.Leather);
            return pivot;
        }

        /// <summary>Оружие: винтовка, ППШ/MP 40, пулемёт, ПТРД, «Панцершрек». Держится у плеча.</summary>
        void BuildGun(Weapon w, Faction side)
        {
            var wood = Shapes.Hex("#5a3c22");
            var steel = Shapes.Hex("#2a2b2d");
            gun = Shapes.Group("Gun", body);
            using var metal = Shapes.Brush(Surf.Metal);
            float length = 1.1f;
            switch (w.Sound)
            {
                case ShotSound.Smg:
                    length = 0.8f;
                    Shapes.Box(gun, new Vector3(0f, 0f, 0.1f), new Vector3(0.07f, 0.1f, 0.65f), side == Faction.USSR ? wood : steel);
                    Shapes.Box(gun, new Vector3(0f, -0.1f, 0.15f), new Vector3(0.05f, 0.18f, 0.05f), steel);
                    if (side == Faction.USSR) Shapes.Part(Shapes.LowCylinder, gun, new Vector3(0f, -0.08f, 0.12f), new Vector3(0.16f, 0.06f, 0.16f), steel, new Vector3(0f, 0f, 90f)); // диск ППШ
                    break;
                case ShotSound.MachineGun:
                    length = 1.25f;
                    Shapes.Box(gun, new Vector3(0f, 0f, 0.2f), new Vector3(0.12f, 0.14f, 0.9f), steel);
                    Shapes.Box(gun, new Vector3(0f, 0f, 0.8f), new Vector3(0.04f, 0.04f, 0.5f), steel);
                    if (side == Faction.USSR) Shapes.Part(Shapes.LowCylinder, gun, new Vector3(0f, 0f, 0.45f), new Vector3(0.14f, 0.5f, 0.14f), Shapes.Hex("#3d4a2a"), new Vector3(90f, 0f, 0f)); // кожух «Максима»
                    break;
                case ShotSound.AtRifle:
                    length = 1.9f;
                    Shapes.Box(gun, new Vector3(0f, 0f, 0.1f), new Vector3(0.08f, 0.1f, 0.8f), wood);
                    Shapes.Box(gun, new Vector3(0f, 0.02f, 0.95f), new Vector3(0.05f, 0.05f, 1.1f), steel);
                    break;
                case ShotSound.Rocket:
                    length = 1.5f;
                    Shapes.Part(Shapes.LowCylinder, gun, new Vector3(0f, 0.05f, 0.35f), new Vector3(0.12f, 1.5f, 0.12f), Shapes.Hex("#4a4d40"), new Vector3(90f, 0f, 0f));
                    Shapes.Box(gun, new Vector3(0f, 0.1f, 0.6f), new Vector3(0.3f, 0.25f, 0.03f), Shapes.Hex("#4a4d40")); // щиток
                    break;
                default:
                    Shapes.Box(gun, new Vector3(0f, 0f, 0.1f), new Vector3(0.06f, 0.09f, 0.75f), wood);
                    Shapes.Box(gun, new Vector3(0f, 0.02f, 0.62f), new Vector3(0.035f, 0.035f, 0.45f), steel);
                    break;
            }
            // Деревянные части (ложа, приклад) — дерево, остальное — металл
            foreach (var r in gun.GetComponentsInChildren<Renderer>())
                if (r.sharedMaterial.GetColor("_BaseColor") == wood) r.sharedMaterial = Shapes.Mat(wood, Surf.Wood);
            muzzle = Shapes.Group("Muzzle", gun, new Vector3(0f, 0.02f, length - 0.2f));
            gunReadyPos = new Vector3(0.12f, 1.12f, 0.28f);
            gunReadyRot = Quaternion.Euler(-25f, 0f, 0f);
            gunAimPos = new Vector3(0.12f, 1.42f, 0.22f);
            gunProneAimPos = gunAimPos;
            if (w.Sound == ShotSound.Rocket) { gunAimPos = new Vector3(0.18f, 1.52f, 0.05f); gunReadyPos = new Vector3(0.18f, 1.45f, -0.1f); gunReadyRot = Quaternion.identity; }
            gun.localPosition = gunReadyPos;
            gun.localRotation = gunReadyRot;
        }

        /// <summary>Анимация за кадр: скорость ходьбы, нужная поза, целится ли.</summary>
        public void Animate(float speed, float dt, SoldierPose pose, bool aiming)
        {
            dead = Mathf.MoveTowards(dead, pose == SoldierPose.Dead ? 1f : 0f, dt * 2.5f);
            prone = Mathf.MoveTowards(prone, pose == SoldierPose.Prone ? 1f : 0f, dt * 3f);
            crouch = Mathf.MoveTowards(crouch, pose == SoldierPose.Crouch ? 1f : 0f, dt * 4f);
            aim = Mathf.MoveTowards(aim, aiming && pose != SoldierPose.Dead ? 1f : 0f, dt * 5f);
            flinch = Mathf.MoveTowards(flinch, 0f, dt * 4f);

            if (dead > 0.001f)
            {
                // Падает навзничь, немного вбок; оружие выпадает из рук
                body.localRotation = Quaternion.Euler(-86f * dead + deadTilt.x * dead, deadTilt.y * dead, deadTilt.z * dead);
                body.localPosition = new Vector3(0f, 0.14f * dead, 0f);
                legL.localRotation = Quaternion.Euler(-10f * dead, 0, -8f * dead);
                legR.localRotation = Quaternion.Euler(-25f * dead, 0, 8f * dead);
                armL.localRotation = Quaternion.Euler(-150f * dead, 0, -40f * dead);
                armR.localRotation = Quaternion.Euler(-120f * dead, 0, 45f * dead);
                gun.localPosition = Vector3.Lerp(gunReadyPos, new Vector3(0.5f, 0.1f, 0.6f), dead);
                return;
            }

            float swing = 0f;
            if (speed > 0.1f)
            {
                phase += dt * (3.6f + speed * 2.3f);
                swing = Mathf.Sin(phase) * Mathf.Clamp(speed * 11f, 0f, 40f);
            }
            else phase = 0f;

            // Корпус: пригнулся — ниже и наклонён, лежит — повёрнут на 85° лицом вниз
            float lean = crouch * 18f + prone * 85f - flinch * 10f;
            body.localRotation = Quaternion.Euler(lean, 0f, 0f);
            body.localPosition = new Vector3(0f, -crouch * 0.42f + prone * 0.18f, -prone * 0.6f);

            float legBend = crouch * 70f;
            legL.localRotation = Quaternion.Euler(swing * (1f - prone) - legBend, 0, 0);
            legR.localRotation = Quaternion.Euler(-swing * (1f - prone) - legBend * 0.4f, 0, 0);
            if (prone > 0.01f)
            {
                // Лёжа ноги вытянуты вдоль корпуса, чуть врозь
                legL.localRotation = Quaternion.Slerp(legL.localRotation, Quaternion.Euler(0f, 0f, -6f), prone);
                legR.localRotation = Quaternion.Slerp(legR.localRotation, Quaternion.Euler(0f, 0f, 6f), prone);
            }

            // Руки: с оружием наготове или у плеча (целится)
            var readyL = Quaternion.Euler(-48f + swing * 0.2f, 0f, 18f);
            var readyR = Quaternion.Euler(-38f - swing * 0.2f, 0f, -6f);
            var aimL = Quaternion.Euler(-82f, 0f, 22f);
            var aimR = Quaternion.Euler(-70f, 0f, -18f);
            armL.localRotation = Quaternion.Slerp(readyL, aimL, aim);
            armR.localRotation = Quaternion.Slerp(readyR, aimR, aim);
            if (prone > 0.01f)
            {
                // Лёжа руки вытянуты вперёд (относительно корпуса — вверх)
                armL.localRotation = Quaternion.Slerp(armL.localRotation, Quaternion.Euler(-160f, 0f, 14f), prone);
                armR.localRotation = Quaternion.Slerp(armR.localRotation, Quaternion.Euler(-150f, 0f, -14f), prone);
            }

            // Оружие: у бедра или у плеча; лёжа — корпус повёрнут, оружие поворачиваем обратно вперёд
            gun.localPosition = Vector3.Lerp(gunReadyPos, gunAimPos, aim);
            var gunRot = Quaternion.Slerp(gunReadyRot, gunAimRot, aim);
            gun.localRotation = Quaternion.Euler(-lean, 0f, 0f) * gunRot;
            if (prone > 0.01f) gun.localPosition = Vector3.Lerp(gun.localPosition, new Vector3(0.1f, 1.75f, 0.25f), prone);

            head.localRotation = Quaternion.Euler(-prone * 70f, 0f, 0f);
        }

        public void Flinch() => flinch = 1f;

        public void SetDeadTilt() => deadTilt = new Vector3(Random.Range(-6f, 6f), Random.Range(-40f, 40f), Random.Range(-15f, 15f));
    }
}
