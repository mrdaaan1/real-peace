using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Точка контроля: флагшток, полотнище цвета владельца, светящееся кольцо радиуса захвата.
    /// Захватывает пехота: сначала спускает чужой флаг до нейтрального, потом поднимает свой.
    /// Контроль — от −1 (СССР) до +1 (Германия); несколько отрядов захватывают быстрее; при споре — стоит.
    /// </summary>
    public class VictoryPoint : MonoBehaviour
    {
        public int Index;
        public string Title;
        /// <summary>−1 — полностью СССР, +1 — Германия, 0 — нейтральная.</summary>
        public float Control { get; private set; }
        public Faction? Owner { get; private set; }
        public bool Contested { get; private set; }
        /// <summary>Кто сейчас захватывает (для интерфейса).</summary>
        public Faction? Capturing { get; private set; }
        public Vector3 Position => transform.position;

        Transform flag;
        Renderer cloth, ring;
        float flagTop;

        public static VictoryPoint Create(int index, Transform parent)
        {
            var go = new GameObject("Point_" + MapLayout.PointNames[index]);
            go.transform.SetParent(parent, false);
            go.transform.position = MapLayout.Point(index);
            var vp = go.AddComponent<VictoryPoint>();
            vp.Index = index;
            vp.Title = MapLayout.PointNames[index];
            vp.Build();
            return vp;
        }

        void Build()
        {
            using (Shapes.Brush(Surf.Wood))
                Shapes.Part(Shapes.LowCylinder, transform, new Vector3(0f, 4f, 0f), new Vector3(0.14f, 8f, 0.14f), Shapes.Hex("#6b5a46"), name: "Pole");
            using (Shapes.Brush(Surf.Stone))
                Shapes.Part(Shapes.LowCylinder, transform, new Vector3(0f, 0.15f, 0f), new Vector3(1.2f, 0.3f, 1.2f), Shapes.Hex("#77726a"));
            flagTop = 7.6f;
            flag = Shapes.Group("Flag", transform, new Vector3(0f, flagTop, 0f));
            var c = Shapes.Box(flag, new Vector3(0.85f, -0.5f, 0f), new Vector3(1.6f, 1f, 0.03f), Color.white, name: "Cloth");
            cloth = c.GetComponent<Renderer>();
            var r = Shapes.MeshObject("Ring", transform, Shapes.Ring(0.97f, 1f, 64), Mats.Glow(Color.white, 1f), new Vector3(0f, 0.25f, 0f));
            r.transform.localScale = Vector3.one * (GameSettings.I != null ? GameSettings.I.captureRadius : 11f);
            r.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring = r.GetComponent<Renderer>();
            Refresh();
        }

        public void Tick(float dt, int ussr, int germany)
        {
            Contested = ussr > 0 && germany > 0;
            Capturing = null;
            if (Contested || (ussr == 0 && germany == 0)) { Animate(dt); return; }
            int n = Mathf.Max(ussr, germany);
            float dir = germany > 0 ? 1f : -1f;
            float rate = 2f / GameSettings.I.captureSeconds * (1f + 0.35f * (n - 1));
            float target = dir;
            if (Mathf.Abs(Control - target) > 0.0001f) Capturing = germany > 0 ? Faction.Germany : Faction.USSR;
            float before = Control;
            Control = Mathf.MoveTowards(Control, target, rate * dt);

            // Нейтрализация: прошли через ноль — владелец снят
            if (Owner.HasValue && Mathf.Sign(Control) != (Owner == Faction.Germany ? 1f : -1f) || Mathf.Abs(Control) < 0.001f)
            {
                if (Owner.HasValue)
                {
                    var lost = Owner.Value;
                    Owner = null;
                    if (Battle.I != null) Battle.I.Message($"Точка «{Title}» потеряна: {Factions.Name(lost)}", lost);
                }
            }
            if (Mathf.Abs(Control) >= 0.999f && !Owner.HasValue)
            {
                Owner = Control > 0f ? Faction.Germany : Faction.USSR;
                Sfx.Ui(SoundFactory.Capture, 0.5f);
                if (Battle.I != null) Battle.I.Message($"Точка «{Title}» захвачена: {Factions.Name(Owner.Value)}", Owner.Value);
            }
            if (Mathf.Abs(before - Control) > 0f) Refresh();
            Animate(dt);
        }

        void Animate(float dt)
        {
            // Флаг колышется
            if (flag != null)
                flag.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 2.3f + Index) * 12f, Mathf.Sin(Time.time * 3.1f) * 2f);
        }

        void Refresh()
        {
            float k = Mathf.Abs(Control);
            var side = Control > 0f ? Faction.Germany : Faction.USSR;
            var col = k < 0.01f ? Shapes.Hex("#d8d4c8") : Color.Lerp(Shapes.Hex("#d8d4c8"), FlagColor(side), Mathf.Clamp01(k * 1.5f));
            cloth.sharedMaterial = Shapes.Mat(col, Surf.Fabric);
            // Флаг поднимается по мере захвата
            flag.localPosition = new Vector3(0f, Mathf.Lerp(2.2f, flagTop, k < 0.01f ? 1f : k), 0f);
            ring.sharedMaterial = Mats.Glow(Owner.HasValue ? Factions.Ui(Owner.Value) : Color.Lerp(Color.white, Factions.Ui(side), k), 1.2f);
        }

        static Color FlagColor(Faction f) => f == Faction.USSR ? Shapes.Hex("#a3241c") : Shapes.Hex("#5a5e55");
    }
}
