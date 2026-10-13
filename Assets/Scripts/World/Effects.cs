using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Эффекты боя на системах частиц (дым с турбулентностью, огонь, пыль, комья земли, искры, вспышки)
    /// плюс короткие вспышки света от выстрелов и разрывов. Несколько общих систем частиц на весь бой —
    /// эффекты «выбрасываются» в нужную точку (Emit), поэтому тысячи выстрелов не создают объектов.
    /// Трассеры и снаряды — светящиеся чёрточки (подхватываются bloom).
    /// </summary>
    public class Effects : MonoBehaviour
    {
        static Effects I;
        Transform root;
        ParticleSystem smoke, darkSmoke, fire, dust, dirt, sparks, flash;

        class Streak
        {
            public Transform t;
            public Vector3 from, to;
            public float age, life;
        }

        class FlashLight
        {
            public Light light;
            public float age, life, intensity;
        }

        readonly List<Streak> streaks = new List<Streak>();
        readonly Stack<Streak> streakPool = new Stack<Streak>();
        readonly List<FlashLight> lights = new List<FlashLight>();
        static Mesh cube;

        public static void Init(Transform world)
        {
            var go = new GameObject("Effects");
            go.transform.SetParent(world, false);
            I = go.AddComponent<Effects>();
            I.root = go.transform;
            I.Build();
        }

        // ---------- Системы частиц ----------

        void Build()
        {
            var smokeTex = TextureFactory.SmokePuff;
            smoke = System("Smoke", Mats.Particle("SmokeMat", smokeTex, new Color(0.62f, 0.6f, 0.56f, 1f), false, 0f, 1f), 2000, -0.05f, 0.6f, true);
            darkSmoke = System("DarkSmoke", Mats.Particle("DarkSmokeMat", smokeTex, new Color(0.16f, 0.15f, 0.14f, 1f), false, 0f, 1f), 1500, -0.08f, 0.5f, true);
            dust = System("Dust", Mats.Particle("DustMat", smokeTex, new Color(0.55f, 0.48f, 0.38f, 1f), false, 0f, 1f), 1500, 0.05f, 1.5f, true);
            fire = System("Fire", Mats.Particle("FireMat", TextureFactory.Fire, new Color(1f, 0.75f, 0.45f, 1f), true, 4f, 0f), 800, -0.1f, 1.2f, false);
            flash = System("Flash", Mats.Particle("FlashMat", TextureFactory.Flash, new Color(1f, 0.85f, 0.6f, 1f), true, 6f, 0f), 300, 0f, 0f, false);
            sparks = System("Sparks", Mats.Particle("SparkMat", TextureFactory.Flash, new Color(1f, 0.7f, 0.35f, 1f), true, 5f, 0f), 600, 1f, 0.2f, false);
            dirt = Chunks("Dirt", Mats.Get(Shapes.Hex("#4b3e2e"), Surf.Soil), 600);

            // Дым тает и растёт, огонь — сжимается и гаснет
            Fade(smoke, 0.05f, 0.85f, 1f, 2.6f);
            Fade(darkSmoke, 0.05f, 0.8f, 1f, 2.8f);
            Fade(dust, 0.05f, 0.7f, 1f, 2.2f);
            Fade(fire, 0.0f, 0.4f, 1f, 0.4f);
            Fade(flash, 0f, 0.1f, 1f, 0.6f);
            Fade(sparks, 0f, 0.6f, 1f, 0.3f);
            foreach (var ps in new[] { smoke, darkSmoke, dust })
            {
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.35f;
                noise.scrollSpeed = 0.3f;
                noise.quality = ParticleSystemNoiseQuality.Medium;
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            }
            // Искры — вытянутые по скорости
            var sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.velocityScale = 0.04f;
            sr.lengthScale = 1f;

            for (int i = 0; i < 8; i++)
            {
                var lgo = new GameObject("FlashLight");
                lgo.transform.SetParent(root, false);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = Shapes.Hex("#ffb35a");
                l.shadows = LightShadows.None;
                l.enabled = false;
                lights.Add(new FlashLight { light = l });
            }
        }

        ParticleSystem System(string name, Material mat, int max, float gravity, float drag, bool lit)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            main.scalingMode = ParticleSystemScalingMode.Local;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            if (drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.drag = drag;
                limit.multiplyDragByParticleSize = false;
                limit.multiplyDragByParticleVelocity = true;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = lit;
            r.minParticleSize = 0f;
            r.maxParticleSize = 4f;
            ps.Play();
            return ps;
        }

        /// <summary>Комья земли — маленькие сетки, падают под тяжестью и катятся.</summary>
        ParticleSystem Chunks(string name, Material mat, int max)
        {
            var ps = System(name, mat, max, 1.6f, 0.3f, true);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = Shapes.LowSphere;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.alignment = ParticleSystemRenderSpace.World;
            var main = ps.main;
            main.startRotation3D = true;
            var col = ps.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.dampen = 0.6f;
            col.bounce = 0.2f;
            col.lifetimeLoss = 0.1f;
            col.quality = ParticleSystemCollisionQuality.Low;
            return ps;
        }

        static void Fade(ParticleSystem ps, float fadeIn, float fadeOutStart, float startSize, float endSize)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f), new GradientAlphaKey(1f, Mathf.Max(0.01f, fadeIn)), new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, startSize, 1f, endSize));
        }

        static void Emit(ParticleSystem ps, Vector3 pos, Vector3 velocity, float size, float life, Color color, float rotation = 0f)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = velocity,
                startSize = size,
                startLifetime = life,
                startColor = color,
                rotation = rotation,
                applyShapeToPosition = false,
            };
            ps.Emit(p, 1);
        }

        void Light(Vector3 at, float intensity, float range, float life)
        {
            FlashLight best = null;
            foreach (var l in lights)
                if (!l.light.enabled) { best = l; break; }
            if (best == null)
            {
                best = lights[0];
                foreach (var l in lights) if (l.age / l.life > best.age / best.life) best = l;
            }
            best.light.enabled = true;
            best.light.transform.position = at;
            best.light.range = range;
            best.intensity = intensity;
            best.light.intensity = intensity;
            best.age = 0f;
            best.life = life;
        }

        // ---------- Публичные эффекты ----------

        static Mesh Cube
        {
            get
            {
                if (cube != null) return cube;
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = probe.GetComponent<MeshFilter>().sharedMesh;
                Destroy(probe);
                return cube;
            }
        }

        Streak GetStreak(Color color, float width, float length, float intensity)
        {
            Streak s;
            if (streakPool.Count > 0)
            {
                s = streakPool.Pop();
                s.t.gameObject.SetActive(true);
            }
            else
            {
                var go = Shapes.MeshObject("Tracer", root, Cube, Mats.Glow(color, intensity));
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s = new Streak { t = go.transform };
            }
            s.t.GetComponent<MeshRenderer>().sharedMaterial = Mats.Glow(color, intensity);
            s.t.localScale = new Vector3(width, width, length);
            s.age = 0f;
            streaks.Add(s);
            return s;
        }

        /// <summary>Трассер пули: светящаяся чёрточка летит от ствола к цели.</summary>
        public static void Tracer(Vector3 from, Vector3 to, Color color)
        {
            if (I == null) return;
            var s = I.GetStreak(color, 0.035f, 1.8f, 6f);
            s.from = from;
            s.to = to;
            s.life = Mathf.Max(0.05f, Vector3.Distance(from, to) / 300f);
            s.t.rotation = Quaternion.LookRotation(to - from);
            s.t.position = from;
        }

        /// <summary>Снаряд пушки — яркая короткая чёрточка.</summary>
        public static void Shell(Vector3 from, Vector3 to)
        {
            if (I == null) return;
            var s = I.GetStreak(Shapes.Hex("#ffd27a"), 0.12f, 2.4f, 10f);
            s.from = from;
            s.to = to;
            s.life = Mathf.Max(0.06f, Vector3.Distance(from, to) / 380f);
            s.t.rotation = Quaternion.LookRotation(to - from);
            s.t.position = from;
        }

        /// <summary>Вспышка у дула: звезда огня, облачко дыма, для крупного калибра — свет.</summary>
        public static void MuzzleFlash(Vector3 at, float size)
        {
            if (I == null) return;
            Emit(I.flash, at, Vector3.zero, size * 2.2f, 0.06f, Color.white, Random.Range(0f, 360f));
            Emit(I.smoke, at, Random.insideUnitSphere * 0.3f + Vector3.up * 0.3f, size * 1.5f, 1.2f + size, new Color(1f, 1f, 1f, 0.35f), Random.Range(0f, 6f));
            if (size > 0.6f)
            {
                I.Light(at, 6f, 18f, 0.12f);
                for (int i = 0; i < 4; i++)
                    Emit(I.smoke, at + Random.insideUnitSphere * 0.5f, Random.insideUnitSphere * 1.5f, 1.8f, 2.5f, new Color(1f, 1f, 1f, 0.5f), Random.Range(0f, 6f));
            }
        }

        /// <summary>Фонтанчик пыли от промаха или выхлоп пыли от выстрела пушки.</summary>
        public static void Dust(Vector3 at, float size = 0.5f)
        {
            if (I == null) return;
            Emit(I.dust, at, Vector3.up * Random.Range(0.6f, 1.6f) + Random.insideUnitSphere * 0.4f, size * 1.4f, 0.9f + size, new Color(1f, 1f, 1f, 0.7f), Random.Range(0f, 6f));
            if (size < 0.8f)
                for (int i = 0; i < 2; i++)
                    Emit(I.dirt, at + Vector3.up * 0.05f, new Vector3(Random.Range(-1f, 1f), Random.Range(2f, 4f), Random.Range(-1f, 1f)), 0.05f, 1.2f, Color.white);
        }

        /// <summary>Струя огнемёта: языки пламени и чёрный дым вдоль линии, вспышка света.</summary>
        public static void Flame(Vector3 from, Vector3 to)
        {
            if (I == null) return;
            var d = to - from;
            float len = d.magnitude;
            var dir = d / Mathf.Max(0.01f, len);
            for (int i = 0; i < 7; i++)
            {
                float t = (i + Random.value) / 7f;
                var p = from + d * t + Random.insideUnitSphere * 0.25f * t + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.6f;
                Emit(I.fire, p, dir * Random.Range(4f, 9f) + Vector3.up * 1.2f, Mathf.Lerp(0.5f, 1.8f, t), Random.Range(0.25f, 0.5f), Color.white, Random.Range(0f, 6f));
            }
            Emit(I.darkSmoke, to + Vector3.up * 0.8f, Vector3.up * 1.5f + Random.insideUnitSphere * 0.5f, 2f, 3f, new Color(1f, 1f, 1f, 0.6f), Random.Range(0f, 6f));
            I.Light(to + Vector3.up, 4f, 12f, 0.15f);
        }

        /// <summary>Искры от рикошета по броне.</summary>
        public static void Sparks(Vector3 at, Vector3 normal)
        {
            if (I == null) return;
            for (int i = 0; i < 14; i++)
            {
                var v = (normal + Random.insideUnitSphere * 0.9f).normalized * Random.Range(6f, 14f);
                Emit(I.sparks, at, v, 0.09f, Random.Range(0.15f, 0.4f), Color.white);
            }
            Emit(I.flash, at, Vector3.zero, 0.9f, 0.05f, Color.white);
        }

        /// <summary>
        /// Разрыв снаряда: огненный шар, столб земли и комья, дымное облако, вспышка света, воронка (если на земле), грохот.
        /// scale 1 — 75-мм снаряд, 0.5 — граната, больше 1 — взрыв боеукладки.
        /// </summary>
        public static void Blast(Vector3 at, float scale, bool crater = true)
        {
            if (I == null) return;
            I.Light(at + Vector3.up * scale, 10f * scale, 30f * scale, 0.25f);
            Emit(I.flash, at + Vector3.up * 0.5f * scale, Vector3.zero, 5f * scale, 0.09f, Color.white);
            for (int i = 0; i < 8; i++)
            {
                var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) + 0.3f;
                Emit(I.fire, at + Vector3.up * 0.4f * scale + Random.insideUnitSphere * 0.4f * scale, dir.normalized * Random.Range(2f, 6f) * scale,
                    Random.Range(1.4f, 2.6f) * scale, Random.Range(0.25f, 0.5f), Color.white, Random.Range(0f, 6f));
            }
            // Столб земли и дыма
            for (int i = 0; i < 10; i++)
            {
                var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 3f + 0.8f;
                Emit(I.dust, at + Random.insideUnitSphere * 0.5f * scale, dir.normalized * Random.Range(3f, 9f) * scale,
                    Random.Range(1.5f, 2.8f) * scale, Random.Range(1.8f, 3f), Color.white, Random.Range(0f, 6f));
            }
            for (int i = 0; i < 6; i++)
                Emit(I.darkSmoke, at + Vector3.up * scale + Random.insideUnitSphere * scale, new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(1f, 2.5f), Random.Range(-0.6f, 0.6f)),
                    Random.Range(2.5f, 4f) * scale, Random.Range(3f, 5f), new Color(1f, 1f, 1f, 0.8f), Random.Range(0f, 6f));
            int chunks = Mathf.RoundToInt(16 * scale);
            for (int i = 0; i < chunks; i++)
            {
                var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 2f + 0.5f;
                Emit(I.dirt, at + Vector3.up * 0.2f, dir.normalized * Random.Range(4f, 11f) * scale, Random.Range(0.08f, 0.25f) * scale, Random.Range(2f, 3.5f), Color.white, Random.Range(0f, 360f));
            }
            if (crater && scale >= 0.45f)
            {
                Crater(at, 1.2f * scale + 0.4f, I.root);
                CoverMap.MarkCircle(new Vector2(at.x, at.z), 1.3f * scale, Cover.Light); // в воронке можно укрыться
            }
            Sfx.Play(scale > 0.7f ? SoundFactory.Explosion : SoundFactory.Grenade, at, Mathf.Clamp01(0.6f + scale * 0.4f), Random.Range(0.9f, 1.1f));
        }

        /// <summary>Непрерывный дым и огонь над подбитым танком или горящим домом.</summary>
        public static Burner Burning(Transform parent, Vector3 local, float strength, bool withFire)
        {
            var go = new GameObject("Burning");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var b = go.AddComponent<Burner>();
            b.strength = strength;
            b.fire = withFire;
            return b;
        }

        /// <summary>Воронка: выбитая земля с валиком по краю (кладётся на рельеф).</summary>
        public static void Crater(Vector3 at, float radius, Transform parent)
        {
            var g = Shapes.Group("Crater", parent, at + Vector3.up * 0.03f, new Vector3(0f, Random.Range(0f, 360f), 0f));
            var pit = Shapes.MeshObject("Pit", g, Shapes.Disc, Mats.Get(Shapes.Hex("#2f2820"), Surf.Soil));
            pit.transform.localScale = new Vector3(radius * 2f, 1f, radius * 1.9f);
            var rim = Shapes.Part(Shapes.LowSphere, g, new Vector3(0f, -0.05f, 0f), new Vector3(radius * 2.6f, 0.35f, radius * 2.5f), Shapes.Hex("#4e4232"));
            rim.GetComponent<MeshRenderer>().sharedMaterial = Mats.Get(Shapes.Hex("#4e4232"), Surf.Soil);
            pit.transform.localPosition = Vector3.up * 0.12f;
            pit.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---------- Обновление ----------

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            for (int i = streaks.Count - 1; i >= 0; i--)
            {
                var s = streaks[i];
                s.age += dt;
                float k = s.age / s.life;
                if (k >= 1f)
                {
                    s.t.gameObject.SetActive(false);
                    streakPool.Push(s);
                    streaks.RemoveAt(i);
                    continue;
                }
                s.t.position = Vector3.Lerp(s.from, s.to, k);
            }
            foreach (var l in lights)
            {
                if (!l.light.enabled) continue;
                l.age += dt;
                float k = l.age / l.life;
                if (k >= 1f) { l.light.enabled = false; continue; }
                l.light.intensity = l.intensity * (1f - k) * (1f - k);
            }
        }

        // Для Burner: доступ к общим системам
        internal static void EmitFire(Vector3 at, float strength)
        {
            if (I == null) return;
            Emit(I.fire, at + Random.insideUnitSphere * 0.4f * strength, Vector3.up * Random.Range(1f, 2.5f) + Random.insideUnitSphere * 0.3f,
                Random.Range(0.8f, 1.5f) * strength, Random.Range(0.4f, 0.8f), Color.white, Random.Range(0f, 6f));
        }

        internal static void EmitSmoke(Vector3 at, float strength, bool dark)
        {
            if (I == null) return;
            Emit(dark ? I.darkSmoke : I.smoke, at + Random.insideUnitSphere * 0.3f, new Vector3(Random.Range(0.3f, 0.9f), Random.Range(1.5f, 2.5f), Random.Range(-0.3f, 0.3f)),
                Random.Range(1.2f, 2f) * strength, Random.Range(5f, 8f), new Color(1f, 1f, 1f, 0.75f), Random.Range(0f, 6f));
        }
    }

    /// <summary>Горение: огонь и столб дыма из точки (подбитый танк, повреждённый мотор).</summary>
    public class Burner : MonoBehaviour
    {
        public float strength = 1f;
        public bool fire = true;
        float smokeTimer, fireTimer;
        Light glow;

        void Start()
        {
            if (!fire) return;
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = Shapes.Hex("#ff8a3a");
            glow.range = 9f * strength;
            glow.shadows = LightShadows.None;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || strength <= 0.01f) return;
            smokeTimer -= dt;
            if (smokeTimer <= 0f)
            {
                smokeTimer = Mathf.Lerp(0.5f, 0.12f, Mathf.Clamp01(strength));
                Effects.EmitSmoke(transform.position, strength, fire);
            }
            if (fire)
            {
                fireTimer -= dt;
                if (fireTimer <= 0f)
                {
                    fireTimer = 0.08f;
                    Effects.EmitFire(transform.position, strength);
                }
                if (glow != null) glow.intensity = (1.5f + Mathf.PerlinNoise(Time.time * 8f, 0f) * 1.5f) * strength;
            }
        }
    }
}
