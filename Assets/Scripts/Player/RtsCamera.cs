using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Камера стратегии: смотрит на точку земли. WASD/стрелки/край экрана — сдвиг, Q/E или средняя кнопка — поворот,
    /// колесо — приближение (вблизи камера опускается к горизонту — видно бой «как в кино», издали — карта сверху).
    /// Работает и на паузе (неигровое время). Не уходит под землю и за край карты.
    /// </summary>
    public class RtsCamera : MonoBehaviour
    {
        public Camera Cam { get; private set; }

        Vector3 focus, focusTarget;
        float yaw, yawTarget;
        float distance = 70f, distanceTarget = 70f;
        const float MinDistance = 14f, MaxDistance = 170f;

        /// <summary>Облёт карты в главном меню.</summary>
        public bool Showcase;

        public static RtsCamera Create(Transform parent)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.4f;
            cam.farClipPlane = 1500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            go.AddComponent<AudioListener>();
            Atmosphere.SetupCamera(cam);
            var rig = go.AddComponent<RtsCamera>();
            rig.Cam = cam;
            return rig;
        }

        public void FocusOn(Vector3 p, bool instant = false)
        {
            focusTarget = MapLayout.ClampToMap(p, 5f);
            if (instant) focus = focusTarget;
        }

        public void SetView(Vector3 p, float yawDeg, float dist, bool instant)
        {
            FocusOn(p, instant);
            yawTarget = yawDeg;
            distanceTarget = Mathf.Clamp(dist, MinDistance, MaxDistance);
            if (instant)
            {
                yaw = yawTarget;
                distance = distanceTarget;
                Apply();
            }
        }

        public Vector3 Focus => focus;
        public float Yaw => yaw;

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            if (Showcase)
            {
                yawTarget += dt * 4f;
            }
            else
            {
                // Сдвиг: клавиши и край экрана, быстрее при большем отдалении
                var pan = GameInput.Pan;
                var m = GameInput.MousePosition;
                const float edge = 3f;
                if (Application.isFocused && !GameInput.Blocked && m.x >= 0 && m.y >= 0 && m.x <= Screen.width && m.y <= Screen.height)
                {
                    if (m.x < edge) pan.x = -1f;
                    else if (m.x > Screen.width - edge) pan.x = 1f;
                    if (m.y < edge) pan.y = -1f;
                    else if (m.y > Screen.height - edge) pan.y = 1f;
                }
                float speed = (12f + distance * 0.9f) * (GameInput.FastPan ? 2f : 1f);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                focusTarget += rot * new Vector3(pan.x, 0f, pan.y) * speed * dt;
                focusTarget = MapLayout.ClampToMap(focusTarget, 5f);

                yawTarget += GameInput.Rotate * 90f * dt;
                if (GameInput.MiddleHeld) yawTarget += GameInput.MouseDelta.x * 3f;

                float zoom = GameInput.Zoom;
                if (Mathf.Abs(zoom) > 0.01f && !Hud.MouseOverUi)
                    distanceTarget = Mathf.Clamp(distanceTarget * Mathf.Pow(0.88f, zoom), MinDistance, MaxDistance);
            }

            float k = 1f - Mathf.Exp(-dt * 10f);
            focus = Vector3.Lerp(focus, focusTarget, k);
            yaw = Mathf.LerpAngle(yaw, yawTarget, k);
            distance = Mathf.Lerp(distance, distanceTarget, k);
            Apply();
        }

        void Apply()
        {
            focus.y = MapLayout.WalkHeight(focus.x, focus.z);
            // Вблизи — взгляд под 32°, издали — почти сверху
            float t = Mathf.InverseLerp(MinDistance, MaxDistance, distance);
            float pitch = Mathf.Lerp(32f, 62f, Mathf.SmoothStep(0f, 1f, t));
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var pos = focus - rot * Vector3.forward * distance;
            float ground = MapLayout.WalkHeight(pos.x, pos.z) + 3f;
            if (pos.y < ground) pos.y = ground;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(focus + Vector3.up * 1f - pos);
        }
    }
}
