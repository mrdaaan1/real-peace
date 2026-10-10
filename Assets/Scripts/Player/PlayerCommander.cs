using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Управление игрока: ЛКМ — выделить (рамкой, кликом, двойной клик — все такие же на экране, Shift — добавить),
    /// ПКМ — идти/атаковать (Shift — добавить точку маршрута, F или Ctrl — движение с атакой), X — стоп,
    /// R — отступить, Ctrl+1…9 — запомнить группу, 1…9 — выбрать (дважды — камера к ней), Ctrl+A — все,
    /// Пробел — пауза, +/− — скорость, F5–F8 — подкрепления. Линии маршрутов и маркеры приказов видны всегда.
    /// </summary>
    public class PlayerCommander : MonoBehaviour
    {
        public static PlayerCommander I { get; private set; }
        public readonly List<Unit> Selection = new List<Unit>();
        public Unit HoverUnit { get; private set; }
        public bool Dragging { get; private set; }
        public Rect DragRect { get; private set; }
        public Vector3? CursorGround { get; private set; }

        RtsCamera rig;
        Vector2 dragStart;
        bool leftPressed;
        float lastClickTime;
        Unit lastClickUnit;
        int lastGroup;
        float lastGroupTime;

        class Marker
        {
            public Transform t;
            public float age;
        }

        readonly List<Marker> markers = new List<Marker>();
        readonly List<LineRenderer> lines = new List<LineRenderer>();

        public static PlayerCommander Create(Transform parent, RtsCamera rig)
        {
            var go = new GameObject("PlayerCommander");
            go.transform.SetParent(parent, false);
            var pc = go.AddComponent<PlayerCommander>();
            pc.rig = rig;
            I = pc;
            return pc;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }

        Faction Side => Battle.I.PlayerSide;

        void Update()
        {
            var battle = Battle.I;
            if (battle == null) return;
            Selection.RemoveAll(u => u == null || !u.Alive);

            if (GameInput.PausePressed && !battle.Over) battle.SetPaused(!battle.Paused);
            if (GameInput.SpeedUpPressed) battle.ChangeSpeed(1);
            if (GameInput.SpeedDownPressed) battle.ChangeSpeed(-1);

            UpdateHover();
            UpdateSelectionInput();
            UpdateOrders();
            UpdateKeys();
            UpdateMarkers();
            UpdateLines();
        }

        // ---------- Поиск юнита под курсором ----------

        Unit PickUnit(Vector2 screen, bool ownOnly)
        {
            var cam = rig.Cam;
            Unit best = null;
            float bestD = float.MaxValue;
            foreach (var u in Battle.I.Units)
            {
                if (!u.Alive || !u.VisibleToPlayer) continue;
                if (ownOnly && u.Side != Side) continue;
                foreach (var p in u.PickPoints)
                {
                    var sp = cam.WorldToScreenPoint(p);
                    if (sp.z <= 0f) continue;
                    var edge = cam.WorldToScreenPoint(p + cam.transform.right * u.PickRadius);
                    float radius = Mathf.Max(10f, Vector2.Distance(sp, edge));
                    float d = Vector2.Distance(sp, screen);
                    if (d < radius && d < bestD)
                    {
                        bestD = d;
                        best = u;
                    }
                }
            }
            return best;
        }

        bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            var ray = rig.Cam.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                if (hit.collider != MapBuilder.GroundCollider && hit.collider.name != "BridgeDeck")
                    point = MapLayout.Ground(point); // клик по дому — точка на земле рядом
                return true;
            }
            point = Vector3.zero;
            return false;
        }

        void UpdateHover()
        {
            Vector2 m = GameInput.MousePosition;
            var u = Hud.MouseOverUi ? null : PickUnit(m, false);
            if (HoverUnit != u)
            {
                if (HoverUnit != null) HoverUnit.Hovered = false;
                HoverUnit = u;
                if (HoverUnit != null) HoverUnit.Hovered = true;
            }
            CursorGround = !Hud.MouseOverUi && GroundPoint(m, out var g) ? g : (Vector3?)null;
        }

        // ---------- Выделение ----------

        void UpdateSelectionInput()
        {
            Vector2 m = GameInput.MousePosition;
            if (GameInput.LeftDown && !Hud.MouseOverUi)
            {
                leftPressed = true;
                dragStart = m;
            }
            if (leftPressed && GameInput.LeftHeld && Vector2.Distance(m, dragStart) > 6f)
            {
                Dragging = true;
                DragRect = Rect.MinMaxRect(Mathf.Min(dragStart.x, m.x), Mathf.Min(dragStart.y, m.y), Mathf.Max(dragStart.x, m.x), Mathf.Max(dragStart.y, m.y));
            }
            if (leftPressed && GameInput.LeftUp)
            {
                leftPressed = false;
                if (Dragging) BoxSelect(DragRect, GameInput.Shift);
                else ClickSelect(m);
                Dragging = false;
            }
        }

        void ClickSelect(Vector2 m)
        {
            var u = PickUnit(m, true);
            bool doubleClick = u != null && u == lastClickUnit && Time.unscaledTime - lastClickTime < 0.35f;
            lastClickTime = Time.unscaledTime;
            lastClickUnit = u;
            if (u == null)
            {
                if (!GameInput.Shift) ClearSelection();
                return;
            }
            if (doubleClick)
            {
                // Все юниты того же типа на экране
                foreach (var o in Battle.I.Units)
                {
                    if (!o.Alive || o.Side != Side || o.Def.Kind != u.Def.Kind) continue;
                    var sp = rig.Cam.WorldToScreenPoint(o.Position);
                    if (sp.z > 0 && sp.x >= 0 && sp.y >= 0 && sp.x <= Screen.width && sp.y <= Screen.height) Add(o);
                }
                return;
            }
            if (GameInput.Shift)
            {
                if (u.Selected) Remove(u); else Add(u);
            }
            else
            {
                ClearSelection();
                Add(u);
            }
            Sfx.Ui(SoundFactory.Click, 0.4f);
        }

        void BoxSelect(Rect r, bool add)
        {
            if (!add) ClearSelection();
            foreach (var u in Battle.I.Units)
            {
                if (!u.Alive || u.Side != Side) continue;
                foreach (var p in u.PickPoints)
                {
                    var sp = rig.Cam.WorldToScreenPoint(p);
                    if (sp.z > 0 && r.Contains(new Vector2(sp.x, sp.y))) { Add(u); break; }
                }
            }
            if (Selection.Count > 0) Sfx.Ui(SoundFactory.Click, 0.4f);
        }

        public void Add(Unit u)
        {
            if (u == null || Selection.Contains(u)) return;
            Selection.Add(u);
            u.SetSelected(true);
        }

        public void Remove(Unit u)
        {
            Selection.Remove(u);
            u.SetSelected(false);
        }

        public void ClearSelection()
        {
            foreach (var u in Selection) if (u != null) u.SetSelected(false);
            Selection.Clear();
        }

        public void SelectOnly(Unit u)
        {
            ClearSelection();
            Add(u);
        }

        // ---------- Приказы ----------

        void UpdateOrders()
        {
            if (!GameInput.RightDown || Hud.MouseOverUi || Selection.Count == 0) return;
            Vector2 m = GameInput.MousePosition;
            var target = PickUnit(m, false);
            if (target != null && target.Side != Side)
            {
                foreach (var u in Selection) u.OrderAttack(target);
                SpawnMarker(target.Position, Shapes.Hex("#ff4a3a"));
                Sfx.Ui(SoundFactory.Click, 0.5f);
                return;
            }
            if (!GroundPoint(m, out var point)) return;
            IssueMove(point, GameInput.Shift, GameInput.AttackMoveHeld || GameInput.Ctrl);
        }

        /// <summary>Приказ двигаться выделенным юнитам: строй сохраняется, если отряды рядом, иначе — линия поперёк хода.</summary>
        public void IssueMove(Vector3 point, bool queue, bool attackMove)
        {
            if (Selection.Count == 0) return;
            var center = Vector3.zero;
            foreach (var u in Selection) center += queue ? u.Waypoints.Count > 0 ? u.Waypoints[u.Waypoints.Count - 1] : u.Position : u.Position;
            center /= Selection.Count;
            var dir = point - center;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = rig.transform.forward;
            dir.Normalize();
            var right = Vector3.Cross(Vector3.up, dir);

            // Строй: линия поперёк направления движения, танки — по краям сзади
            var sorted = new List<Unit>(Selection);
            sorted.Sort((a, b) => Vector3.Dot(a.Position - center, right).CompareTo(Vector3.Dot(b.Position - center, right)));
            float spacing = 9f;
            for (int i = 0; i < sorted.Count; i++)
            {
                var u = sorted[i];
                float x = (i - (sorted.Count - 1) * 0.5f) * spacing;
                float back = u.IsVehicle ? -6f : 0f;
                var dest = point + right * x + dir * back;
                u.OrderMove(dest, queue, attackMove);
            }
            SpawnMarker(point, attackMove ? Shapes.Hex("#ffb43a") : Shapes.Hex("#7dff6a"));
            Sfx.Ui(SoundFactory.Click, 0.5f);
        }

        void UpdateKeys()
        {
            var battle = Battle.I;
            if (GameInput.StopPressed) foreach (var u in Selection) u.OrderStop();
            if (GameInput.RetreatPressed && Selection.Count > 0)
            {
                foreach (var u in Selection) u.OrderRetreat();
                battle.Message("Отступаем на базу!", Side);
            }
            if (GameInput.SelectAllPressed)
            {
                ClearSelection();
                foreach (var u in battle.Units) if (u.Alive && u.Side == Side) Add(u);
            }
            if (GameInput.FocusPressed) rig.FocusOn(MapLayout.Base(Side));

            int g = GameInput.GroupPressed;
            if (g > 0)
            {
                if (GameInput.Ctrl)
                {
                    foreach (var u in battle.Units) if (u.Side == Side && u.Group == g) u.Group = 0;
                    foreach (var u in Selection) u.Group = g;
                    battle.Message($"Группа {g}: {Selection.Count} юн.", Side);
                }
                else
                {
                    ClearSelection();
                    foreach (var u in battle.Units) if (u.Alive && u.Side == Side && u.Group == g) Add(u);
                    if (lastGroup == g && Time.unscaledTime - lastGroupTime < 0.4f && Selection.Count > 0)
                        rig.FocusOn(Selection[0].Position);
                    lastGroup = g;
                    lastGroupTime = Time.unscaledTime;
                }
            }

            int buy = GameInput.BuyPressed;
            if (buy > 0) Buy((UnitKind)(buy - 1));
        }

        public void Buy(UnitKind kind)
        {
            var battle = Battle.I;
            if (battle.Order(Side, kind, out var reason))
            {
                battle.Message($"Вызвано подкрепление: {UnitDefs.Get(Side, kind).Name}", Side);
                Sfx.Ui(SoundFactory.Click, 0.6f);
            }
            else battle.Message(reason, null);
        }

        // ---------- Маркеры и линии маршрутов ----------

        void SpawnMarker(Vector3 at, Color color)
        {
            var go = Shapes.MeshObject("Marker", transform, Shapes.Ring(0.75f, 1f, 32), Mats.Glow(color, 2.5f, true), at + Vector3.up * 0.3f);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markers.Add(new Marker { t = go.transform });
        }

        void UpdateMarkers()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = markers.Count - 1; i >= 0; i--)
            {
                var mk = markers[i];
                mk.age += dt;
                if (mk.age > 0.6f)
                {
                    Destroy(mk.t.gameObject);
                    markers.RemoveAt(i);
                    continue;
                }
                float s = Mathf.Lerp(3.5f, 0.8f, mk.age / 0.6f);
                mk.t.localScale = new Vector3(s, 1f, s);
            }
        }

        void UpdateLines()
        {
            int used = 0;
            foreach (var u in Selection)
            {
                if (u.Waypoints.Count == 0 && u.ForcedTarget == null) continue;
                var lr = GetLine(used++);
                var pts = new List<Vector3>();
                var from = u.Position;
                pts.Add(from + Vector3.up * 0.4f);
                if (u.ForcedTarget != null)
                {
                    AddSegment(pts, from, u.ForcedTarget.Position);
                    lr.sharedMaterial = Mats.Glow(Shapes.Hex("#ff4a3a"), 1.6f, true);
                }
                else
                {
                    foreach (var w in u.Waypoints)
                    {
                        AddSegment(pts, from, w);
                        from = w;
                    }
                    lr.sharedMaterial = Mats.Glow(u.AttackMove ? Shapes.Hex("#ffb43a") : u.Retreating ? Shapes.Hex("#ffe04a") : Shapes.Hex("#7dff6a"), 1.4f, true);
                }
                lr.positionCount = pts.Count;
                lr.SetPositions(pts.ToArray());
                lr.enabled = true;
            }
            for (int i = used; i < lines.Count; i++) lines[i].enabled = false;
        }

        static void AddSegment(List<Vector3> pts, Vector3 a, Vector3 b)
        {
            // Линия ложится на рельеф — точки каждые 4 м
            float len = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 4f));
            for (int i = 1; i <= n; i++)
            {
                var p = Vector3.Lerp(a, b, (float)i / n);
                pts.Add(MapLayout.Ground(p) + Vector3.up * 0.4f);
            }
        }

        LineRenderer GetLine(int i)
        {
            while (lines.Count <= i)
            {
                var go = new GameObject("PathLine");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.widthMultiplier = 0.25f;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.alignment = LineAlignment.View;
                lines.Add(lr);
            }
            return lines[i];
        }
    }
}
