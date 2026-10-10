using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Летящий снаряд, который видно: граната (по дуге) и реактивная граната «Панцершрека» (прямо, с дымным следом).
    /// Долетел — вызывает onArrive (там и решается урон).
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        Vector3 from, to;
        float time, duration, arc;
        System.Action onArrive;
        float trailTimer;
        bool smokeTrail;

        public static void Launch(Vector3 from, Vector3 to, float duration, float arc, bool rocket, System.Action onArrive)
        {
            var parent = Battle.I != null ? Battle.I.transform : null;
            var go = rocket
                ? Shapes.Part(Shapes.LowCylinder, parent, from, new Vector3(0.1f, 0.5f, 0.1f), Shapes.Hex("#3b3d33"), name: "Rocket")
                : Shapes.Part(Shapes.LowSphere, parent, from, new Vector3(0.14f, 0.14f, 0.14f), Shapes.Hex("#3a3f2e"), name: "Grenade");
            var p = go.AddComponent<Projectile>();
            p.from = from;
            p.to = to;
            p.duration = duration;
            p.arc = arc;
            p.onArrive = onArrive;
            p.smokeTrail = rocket;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            time += dt;
            float k = Mathf.Clamp01(time / duration);
            var p = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * arc;
            var dir = p - transform.position;
            transform.position = p;
            if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);
            if (smokeTrail)
            {
                trailTimer -= dt;
                if (trailTimer <= 0f)
                {
                    trailTimer = 0.03f;
                    Effects.Dust(p, 0.35f);
                }
            }
            if (k >= 1f)
            {
                onArrive?.Invoke();
                Destroy(gameObject);
            }
        }
    }
}
