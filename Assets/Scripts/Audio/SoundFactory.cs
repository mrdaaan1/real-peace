using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Звуки боя синтезируются кодом (как в «Симуляторе очереди»): выстрелы винтовки, ППШ/MP 40, пулемёта,
    /// пушки, разрывы, рикошет, гул мотора танка, щелчки интерфейса. Потом можно заменить записями.
    /// </summary>
    public static class SoundFactory
    {
        const int Rate = 44100;

        static AudioClip rifle, smg, mg, cannon, explosion, grenade, ricochet, engine, click, capture, atRifle;

        static AudioClip Create(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Выстрел: резкий хлопок шума + низкое «тело» + короткое эхо.</summary>
        static AudioClip Shot(string name, int seed, float length, float crackDecay, float bodyHz, float bodyDecay, float echo)
        {
            int n = (int)(Rate * length);
            var data = new float[n];
            var rnd = new System.Random(seed);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float noise = (float)rnd.NextDouble() * 2f - 1f;
                lp = Mathf.Lerp(lp, noise, 0.12f);
                float crack = noise * Mathf.Exp(-t * crackDecay);
                float body = Mathf.Sin(2 * Mathf.PI * (bodyHz - bodyHz * 0.5f * Mathf.Min(1f, t * 8f)) * t) * Mathf.Exp(-t * bodyDecay);
                float tail = lp * echo * Mathf.Exp(-t * 6f);
                data[i] = Mathf.Clamp(crack * 0.75f + body * 0.6f + tail, -1f, 1f);
            }
            return Create(name, data);
        }

        public static AudioClip Rifle => rifle != null ? rifle : (rifle = Shot("Rifle", 11, 0.7f, 70f, 140f, 18f, 1.6f));
        public static AudioClip Smg => smg != null ? smg : (smg = Shot("Smg", 12, 0.25f, 110f, 180f, 30f, 0.8f));
        public static AudioClip MachineGun => mg != null ? mg : (mg = Shot("MG", 13, 0.3f, 90f, 160f, 26f, 1.1f));
        public static AudioClip AtRifle => atRifle != null ? atRifle : (atRifle = Shot("AtRifle", 14, 0.9f, 45f, 90f, 10f, 2.2f));

        /// <summary>Пушка: глубокий удар с долгим раскатом.</summary>
        public static AudioClip Cannon
        {
            get
            {
                if (cannon != null) return cannon;
                int n = (int)(Rate * 1.8f);
                var data = new float[n];
                var rnd = new System.Random(21);
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Rate;
                    float noise = (float)rnd.NextDouble() * 2f - 1f;
                    lp = Mathf.Lerp(lp, noise, 0.05f);
                    float crack = noise * Mathf.Exp(-t * 45f);
                    float boom = Mathf.Sin(2 * Mathf.PI * (70f - 30f * Mathf.Min(t, 1f)) * t) * Mathf.Exp(-t * 4f);
                    float rumble = lp * 3f * Mathf.Exp(-t * 2.2f);
                    data[i] = Mathf.Clamp(crack + boom + rumble, -1f, 1f);
                }
                cannon = Create("Cannon", data);
                return cannon;
            }
        }

        /// <summary>Разрыв снаряда: хлопок, низкий грохот, потрескивание.</summary>
        public static AudioClip Explosion => explosion != null ? explosion : (explosion = Blast("Explosion", 51, 2.6f, 1f));
        public static AudioClip Grenade => grenade != null ? grenade : (grenade = Blast("Grenade", 52, 1.4f, 1.6f));

        static AudioClip Blast(string name, int seed, float length, float pitch)
        {
            int n = (int)(Rate * length);
            var data = new float[n];
            var rnd = new System.Random(seed);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float noise = (float)rnd.NextDouble() * 2f - 1f;
                lp = Mathf.Lerp(lp, noise, 0.04f * pitch);
                lp2 = Mathf.Lerp(lp2, noise, 0.35f);
                float crack = noise * Mathf.Exp(-t * 30f);
                float boom = Mathf.Sin(2 * Mathf.PI * (48f * pitch - 20f * Mathf.Min(t, 1f)) * t) * Mathf.Exp(-t * 2.4f * pitch);
                float rumble = lp * 3.2f * Mathf.Exp(-t * 1.3f * pitch);
                float crackle = lp2 * (rnd.NextDouble() < 0.02 ? 1f : 0.15f) * Mathf.Exp(-t * 1.6f);
                data[i] = Mathf.Clamp(crack * 0.9f + boom * 0.9f + rumble + crackle * 0.4f, -1f, 1f);
            }
            return Create(name, data);
        }

        /// <summary>Рикошет снаряда от брони: звонкий «дзынь» с падающим тоном.</summary>
        public static AudioClip Ricochet
        {
            get
            {
                if (ricochet != null) return ricochet;
                int n = (int)(Rate * 0.5f);
                var data = new float[n];
                var rnd = new System.Random(31);
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Rate;
                    float f = 2400f - 1500f * t;
                    float ping = Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 7f);
                    float clank = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 80f);
                    data[i] = Mathf.Clamp(ping * 0.5f + clank * 0.6f, -1f, 1f);
                }
                ricochet = Create("Ricochet", data);
                return ricochet;
            }
        }

        /// <summary>Гул танкового мотора — зацикленный, 1 с.</summary>
        public static AudioClip Engine
        {
            get
            {
                if (engine != null) return engine;
                int n = Rate;
                var data = new float[n];
                var rnd = new System.Random(41);
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Rate;
                    float noise = (float)rnd.NextDouble() * 2f - 1f;
                    lp = Mathf.Lerp(lp, noise, 0.08f);
                    // 30 «тактов» в секунду — ровно целое число за цикл, чтобы не щёлкало на стыке
                    float firing = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * 30f * t)), 6f);
                    float hum = Mathf.Sin(2 * Mathf.PI * 60f * t) * 0.4f + Mathf.Sin(2 * Mathf.PI * 120f * t) * 0.2f;
                    data[i] = Mathf.Clamp((firing * 0.6f + hum * 0.4f) * 0.5f + lp * 0.6f, -1f, 1f);
                }
                engine = Create("Engine", data);
                return engine;
            }
        }

        /// <summary>Щелчок интерфейса.</summary>
        public static AudioClip Click
        {
            get
            {
                if (click != null) return click;
                int n = (int)(Rate * 0.05f);
                var data = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Rate;
                    data[i] = Mathf.Sin(2 * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 120f) * 0.4f;
                }
                click = Create("Click", data);
                return click;
            }
        }

        /// <summary>Точка захвачена: короткий сигнал горна из трёх нот.</summary>
        public static AudioClip Capture
        {
            get
            {
                if (capture != null) return capture;
                float[] notes = { 392f, 523f, 659f };
                int per = (int)(Rate * 0.18f);
                var data = new float[per * notes.Length + Rate / 4];
                for (int k = 0; k < notes.Length; k++)
                    for (int i = 0; i < per + (k == notes.Length - 1 ? Rate / 4 : 0); i++)
                    {
                        float t = (float)i / Rate;
                        float len = k == notes.Length - 1 ? 0.43f : 0.18f;
                        float env = Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01((len - t) / 0.05f);
                        float f = notes[k];
                        float s = Mathf.Sin(2 * Mathf.PI * f * t) + 0.5f * Mathf.Sin(4 * Mathf.PI * f * t) + 0.25f * Mathf.Sin(6 * Mathf.PI * f * t);
                        data[k * per + i] += s * 0.22f * env;
                    }
                capture = Create("Capture", data);
                return capture;
            }
        }
    }

    /// <summary>
    /// Пул 3D-источников звука: выстрелов в бою много, своё AudioSource на каждый — дорого.
    /// Источник ставится в точку выстрела, играет и возвращается в пул.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        static Sfx I;
        AudioSource[] sources;
        int next;

        public static void Init(Transform world)
        {
            var go = new GameObject("Sfx");
            go.transform.SetParent(world, false);
            I = go.AddComponent<Sfx>();
            I.sources = new AudioSource[40];
            for (int i = 0; i < I.sources.Length; i++)
            {
                var child = new GameObject("Src");
                child.transform.SetParent(go.transform, false);
                var s = child.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 15f;
                s.maxDistance = 260f;
                s.dopplerLevel = 0f;
                I.sources[i] = s;
            }
        }

        public static void Play(AudioClip clip, Vector3 at, float volume = 1f, float pitch = 1f)
        {
            if (I == null || clip == null) return;
            // Ищем свободный источник; если все заняты — перебиваем самый старый
            AudioSource src = null;
            for (int k = 0; k < I.sources.Length; k++)
            {
                var s = I.sources[(I.next + k) % I.sources.Length];
                if (!s.isPlaying) { src = s; I.next = (I.next + k + 1) % I.sources.Length; break; }
            }
            if (src == null)
            {
                src = I.sources[I.next];
                I.next = (I.next + 1) % I.sources.Length;
            }
            src.transform.position = at;
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch * Mathf.Clamp(Time.timeScale, 0.5f, 1.5f);
            src.Play();
        }

        /// <summary>Звук интерфейса (без 3D).</summary>
        public static void Ui(AudioClip clip, float volume = 0.6f)
        {
            if (I == null) return;
            var cam = Camera.main;
            Play(clip, cam != null ? cam.transform.position : Vector3.zero, volume);
        }
    }
}
