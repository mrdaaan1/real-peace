using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Правила боя: линия видимости, шанс попадания (дистанция, движение, укрытие, подавление),
    /// броня танков (лоб/борт/корма, шанс пробития), разрывы с уроном по площади.
    /// </summary>
    public static class Combat
    {
        public const float EyeHeight = 1.5f;

        /// <summary>Статистика для автотеста: выстрелы и попадания по сторонам.</summary>
        public static readonly int[] Shots = new int[2], Hits = new int[2];

        /// <summary>Видно ли из точки from точку to (земля, дома, стволы деревьев заслоняют).</summary>
        public static bool LineOfSight(Vector3 from, Vector3 to)
        {
            var a = from + Vector3.up * EyeHeight;
            var b = to + Vector3.up * EyeHeight;
            return !Physics.Linecast(a, b, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Шанс попадания по пехотинцу или танку.</summary>
        public static float HitChance(Weapon w, float distance, bool shooterMoving, Cover targetCover, bool targetVehicle,
            float shooterSuppression, float sideMultiplier)
        {
            float k = Mathf.Clamp01(distance / Mathf.Max(1f, w.Range));
            float p = Mathf.Lerp(w.AccNear, w.AccFar, k);
            if (shooterMoving) p *= 0.5f;
            if (targetVehicle) p = Mathf.Min(0.95f, p * 1.4f);
            else p *= CoverMap.HitMultiplier(targetCover);
            p *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(shooterSuppression));
            return Mathf.Clamp01(p * sideMultiplier);
        }

        /// <summary>
        /// Попадание по танку: с какой стороны пришёл снаряд, пробил ли броню.
        /// Возвращает урон (непробитие — царапина и рикошет).
        /// </summary>
        public static float ArmorDamage(Tank tank, Vector3 from, Weapon w, out bool penetrated)
        {
            var toShooter = from - tank.Position;
            toShooter.y = 0f;
            float facing = Vector3.Dot(tank.transform.forward, toShooter.normalized);
            float armor = facing > 0.55f ? tank.Def.ArmorFront : facing < -0.55f ? tank.Def.ArmorRear : tank.Def.ArmorSide;
            float ratio = w.Penetration / Mathf.Max(1f, armor);
            float chance = Mathf.Clamp(0.1f + 0.9f * (ratio - 0.5f) / 0.8f, 0.05f, 0.95f);
            penetrated = w.Penetration > 0f && Random.value < chance;
            return penetrated ? w.Damage * Random.Range(0.85f, 1.15f) : w.Damage * 0.04f;
        }

        /// <summary>Разрыв: урон пехоте по площади (с учётом укрытий), лёгкий урон технике, подавление.</summary>
        public static void Explode(Vector3 at, Weapon w, Faction shooterSide, Unit shooter)
        {
            Effects.Blast(at, w.Damage > 100f ? 0.8f : 0.6f);
            if (Battle.I == null) return;
            foreach (var u in Battle.I.Units.ToArray())
            {
                if (!u.Alive) continue;
                if (u is Squad sq)
                {
                    bool near = false;
                    foreach (var s in sq.Soldiers.ToArray())
                    {
                        if (!s.Alive) continue;
                        float d = Vector3.Distance(s.Position, at);
                        if (d > w.Splash * 2f) continue;
                        near = true;
                        if (d > w.Splash) continue;
                        float dmg = w.Damage * (1f - d / w.Splash * 0.6f) * CoverMap.HitMultiplier(CoverMap.At(s.Position));
                        if (sq.Side == shooterSide) dmg *= 0.5f; // по своим — тоже бывает
                        s.TakeDamage(dmg, shooter);
                    }
                    if (near) sq.Suppress(w.Suppression > 0f ? w.Suppression : 0.15f);
                }
                else if (u is Tank t && Vector3.Distance(t.Position, at) < w.Splash)
                {
                    t.TakeDamage(w.Damage * 0.08f, shooter);
                }
            }
        }
    }
}
