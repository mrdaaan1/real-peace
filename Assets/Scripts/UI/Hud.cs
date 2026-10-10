using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Интерфейс на IMGUI. В бою: очки победы и точки (сверху), живая сила и сообщения (слева), миникарта,
    /// панель выделенных, приказы, подкрепления (снизу), полоски здоровья над юнитами, подсказка укрытия у курсора,
    /// пауза, итог боя, справка (F1), меню (Esc). Вне боя — главное меню с выбором стороны.
    /// Всё рисуется в виртуальном разрешении высотой 1080 и масштабируется под экран.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        /// <summary>Курсор над интерфейсом — клики не уходят в мир.</summary>
        public static bool MouseOverUi { get; private set; }

        public GameBootstrap Game;
        public RtsCamera Rig;
        public bool ShowHelp;
        public bool ShowMenu;

        readonly List<Rect> uiRects = new List<Rect>();
        float scale = 1f;
        float vw, vh;
        GUIStyle panel, label, small, title, big, button, buttonOn, center, bar;
        static Texture2D white;
        bool minimapDrag;
        bool pausedBeforeMenu;

        // Главное меню
        Faction chosenSide = Faction.USSR;

        void Update()
        {
            // Курсор над прямоугольниками интерфейса прошлого кадра
            var m = Input.mousePosition;
            var v = new Vector2(m.x / scale, (Screen.height - m.y) / scale);
            bool over = false;
            foreach (var r in uiRects) if (r.Contains(v)) { over = true; break; }
            MouseOverUi = over || ShowMenu || Game.InMenu || (Battle.I != null && Battle.I.Over);
            GameInput.Blocked = ShowMenu || Game.InMenu;

            if (GameInput.HelpPressed) ShowHelp = !ShowHelp;
            if (GameInput.MenuPressed && !Game.InMenu && Battle.I != null && !Battle.I.Over)
            {
                ShowMenu = !ShowMenu;
                if (ShowMenu)
                {
                    pausedBeforeMenu = Battle.I.Paused;
                    Battle.I.SetPaused(true);
                }
                else Battle.I.SetPaused(pausedBeforeMenu);
            }
        }

        // ---------- Стили ----------

        static Texture2D White
        {
            get
            {
                if (white != null) return white;
                white = new Texture2D(1, 1) { name = "White" };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
                return white;
            }
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void Styles()
        {
            if (panel != null) return;
            panel = new GUIStyle { normal = { background = Solid(new Color(0.07f, 0.075f, 0.065f, 0.82f)) }, padding = new RectOffset(10, 10, 8, 8), border = new RectOffset(1, 1, 1, 1) };
            label = new GUIStyle { fontSize = 18, normal = { textColor = Shapes.Hex("#e8e2cf") }, richText = true, wordWrap = true };
            small = new GUIStyle(label) { fontSize = 14, normal = { textColor = Shapes.Hex("#bdb6a2") } };
            title = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold };
            big = new GUIStyle(label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            bar = new GUIStyle { normal = { background = White } };
            button = new GUIStyle
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                richText = true,
                padding = new RectOffset(8, 8, 6, 6),
                normal = { background = Solid(new Color(0.2f, 0.21f, 0.17f, 0.95f)), textColor = Shapes.Hex("#e8e2cf") },
                hover = { background = Solid(new Color(0.32f, 0.33f, 0.24f, 0.95f)), textColor = Color.white },
                active = { background = Solid(new Color(0.45f, 0.42f, 0.26f, 1f)), textColor = Color.white },
            };
            buttonOn = new GUIStyle(button) { normal = { background = Solid(new Color(0.5f, 0.38f, 0.16f, 0.95f)), textColor = Color.white } };
        }

        Rect Ui(Rect r)
        {
            uiRects.Add(r);
            return r;
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = old;
        }

        void Frame(Rect r, Color c, float w = 2f)
        {
            Fill(new Rect(r.x, r.y, r.width, w), c);
            Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.x, r.y, w, r.height), c);
            Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        // ---------- Отрисовка ----------

        void OnGUI()
        {
            Styles();
            scale = Screen.height / 1080f;
            vw = Screen.width / scale;
            vh = 1080f;
            if (Event.current.type == EventType.Layout) uiRects.Clear();
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            if (Game.InMenu)
            {
                MainMenu();
                return;
            }
            var battle = Battle.I;
            if (battle == null) return;

            UnitOverlays(battle);
            CursorHint(battle);
            DragRect();
            TopBar(battle);
            LeftInfo(battle);
            Minimap(battle);
            SelectionPanel(battle);
            Reinforcements(battle);
            if (battle.Paused && !ShowMenu && !battle.Over)
                GUI.Label(new Rect(vw / 2f - 400f, 118f, 800f, 40f), "<b>ПАУЗА</b> — осмотритесь и раздайте приказы.  Пробел — продолжить", center);
            if (ShowHelp) Help();
            if (battle.Over) EndScreen(battle);
            else if (ShowMenu) PauseMenu(battle);
        }

        // ---------- Верхняя панель: очки и точки ----------

        void TopBar(Battle battle)
        {
            var r = Ui(new Rect(vw / 2f - 380f, 10f, 760f, 100f));
            GUI.Box(r, GUIContent.none, panel);
            float max = GameSettings.I.startTickets;
            for (int i = 0; i < 2; i++)
            {
                var side = (Faction)i;
                bool left = side == Faction.USSR;
                var br = new Rect(left ? r.x + 14f : r.xMax - 254f, r.y + 38f, 240f, 16f);
                Fill(br, new Color(0f, 0f, 0f, 0.6f));
                float k = battle.Tickets[i] / max;
                Fill(left ? new Rect(br.xMax - br.width * k, br.y, br.width * k, br.height) : new Rect(br.x, br.y, br.width * k, br.height), Factions.Ui(side));
                string you = side == battle.PlayerSide ? "  <color=#c8f0b0>(вы)</color>" : "";
                var nameStyle = new GUIStyle(label) { alignment = left ? TextAnchor.UpperLeft : TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(br.x, r.y + 8f, br.width, 26f), Factions.Name(side) + you, nameStyle);
                GUI.Label(new Rect(br.x, br.yMax + 4f, br.width, 24f), Mathf.CeilToInt(battle.Tickets[i]).ToString(), new GUIStyle(small) { alignment = left ? TextAnchor.UpperRight : TextAnchor.UpperLeft });
            }
            // Точки
            float px = r.center.x - battle.Points.Length * 40f;
            for (int i = 0; i < battle.Points.Length; i++)
            {
                var p = battle.Points[i];
                var pr = new Rect(px + i * 80f + 12f, r.y + 14f, 56f, 56f);
                Fill(pr, new Color(0f, 0f, 0f, 0.55f));
                float c = p.Control;
                var side = c > 0f ? Faction.Germany : Faction.USSR;
                float k = Mathf.Abs(c);
                Fill(new Rect(pr.x, pr.yMax - pr.height * k, pr.width, pr.height * k), Factions.Ui(side) * new Color(1f, 1f, 1f, p.Owner.HasValue ? 1f : 0.55f));
                Frame(pr, p.Contested ? Shapes.Hex("#ffcf4a") : p.Owner.HasValue ? Color.white : new Color(1f, 1f, 1f, 0.4f), p.Contested ? 3f : 2f);
                GUI.Label(pr, "<b>" + (i + 1) + "</b>", new GUIStyle(center) { fontSize = 24 });
                GUI.Label(new Rect(pr.x - 20f, pr.yMax + 2f, pr.width + 40f, 20f), p.Title, new GUIStyle(small) { alignment = TextAnchor.UpperCenter, fontSize = 13, wordWrap = false });
                if (GUI.Button(pr, GUIContent.none, GUIStyle.none)) Rig.FocusOn(p.Position);
            }
            // Время и скорость
            int t = Mathf.FloorToInt(battle.Elapsed);
            GUI.Label(new Rect(r.x, r.yMax + 2f, r.width, 22f), $"{t / 60:00}:{t % 60:00}   ×{battle.Speed:0.#}" + (battle.Paused ? "   ⏸" : ""), new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
        }

        void LeftInfo(Battle battle)
        {
            var side = battle.PlayerSide;
            var r = Ui(new Rect(10f, 10f, 330f, 70f));
            GUI.Box(r, GUIContent.none, panel);
            int owned = 0;
            foreach (var p in battle.Points) if (p.Owner == side) owned++;
            float income = GameSettings.I.baseIncome + GameSettings.I.incomePerPoint * owned;
            GUI.Label(new Rect(r.x + 12f, r.y + 8f, r.width, 26f), $"Живая сила: <b>{Mathf.FloorToInt(battle.Manpower[(int)side])}</b>  <color=#9fd08a>+{income:0.#}/с</color>", label);
            GUI.Label(new Rect(r.x + 12f, r.y + 36f, r.width, 26f), $"Юнитов: {battle.CountUnits(side)}/{GameSettings.I.unitCap}    Точек: {owned}/{battle.Points.Length}", small);

            float y = r.yMax + 8f;
            foreach (var n in battle.Notes)
            {
                float age = Time.unscaledTime - n.Time;
                if (age > 9f) continue;
                var c = n.Color;
                c.a = Mathf.Clamp01(9f - age);
                var st = new GUIStyle(small) { normal = { textColor = c }, fontSize = 15 };
                Fill(new Rect(10f, y, 420f, 24f), new Color(0f, 0f, 0f, 0.45f * c.a));
                GUI.Label(new Rect(18f, y + 2f, 410f, 22f), n.Text, st);
                y += 26f;
            }
        }

        // ---------- Миникарта ----------

        void Minimap(Battle battle)
        {
            const float size = 240f;
            var r = Ui(new Rect(10f, vh - size - 10f, size, size));
            Fill(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), new Color(0.07f, 0.075f, 0.065f, 0.9f));
            if (MapBuilder.MapTexture != null) GUI.DrawTexture(r, MapBuilder.MapTexture);
            Vector2 ToMap(Vector3 w) => new Vector2(r.x + (w.x / MapLayout.Size + 0.5f) * r.width, r.y + (0.5f - w.z / MapLayout.Size) * r.height);

            foreach (var p in battle.Points)
            {
                var mp = ToMap(p.Position);
                Fill(new Rect(mp.x - 7f, mp.y - 7f, 14f, 14f), Color.black);
                Fill(new Rect(mp.x - 5f, mp.y - 5f, 10f, 10f), p.Owner.HasValue ? Factions.Ui(p.Owner.Value) : Color.white);
            }
            foreach (var u in battle.Units)
            {
                if (!u.Alive || !u.VisibleToPlayer) continue;
                var mp = ToMap(u.Position);
                float s = u.IsVehicle ? 7f : 5f;
                var col = u.Side == battle.PlayerSide ? (u.Selected ? Shapes.Hex("#b8ff9a") : Shapes.Hex("#58c84a")) : Shapes.Hex("#ff4a3a");
                Fill(new Rect(mp.x - s / 2f - 1f, mp.y - s / 2f - 1f, s + 2f, s + 2f), Color.black);
                Fill(new Rect(mp.x - s / 2f, mp.y - s / 2f, s, s), col);
            }
            // Рамка того, что видит камера (примерно)
            var f = ToMap(Rig.Focus);
            Frame(new Rect(f.x - 18f, f.y - 12f, 36f, 24f), new Color(1f, 1f, 1f, 0.8f), 1.5f);

            var e = Event.current;
            bool inside = r.Contains(e.mousePosition);
            Vector3 World(Vector2 m) => new Vector3(((m.x - r.x) / r.width - 0.5f) * MapLayout.Size, 0f, (0.5f - (m.y - r.y) / r.height) * MapLayout.Size);
            if (e.type == EventType.MouseDown && inside && e.button == 0) { minimapDrag = true; e.Use(); }
            if (e.type == EventType.MouseUp && e.button == 0) minimapDrag = false;
            if (minimapDrag && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown || e.type == EventType.Repaint))
                Rig.FocusOn(World(new Vector2(Mathf.Clamp(e.mousePosition.x, r.x, r.xMax), Mathf.Clamp(e.mousePosition.y, r.y, r.yMax))));
            if (e.type == EventType.MouseDown && inside && e.button == 1 && PlayerCommander.I != null)
            {
                PlayerCommander.I.IssueMove(MapLayout.Ground(World(e.mousePosition)), GameInput.Shift, GameInput.AttackMoveHeld || GameInput.Ctrl);
                e.Use();
            }
        }

        // ---------- Выделенные юниты и приказы ----------

        void SelectionPanel(Battle battle)
        {
            var pc = PlayerCommander.I;
            if (pc == null || pc.Selection.Count == 0) return;
            var r = Ui(new Rect(270f, vh - 170f, 640f, 160f));
            GUI.Box(r, GUIContent.none, panel);
            if (pc.Selection.Count == 1)
            {
                var u = pc.Selection[0];
                GUI.Label(new Rect(r.x + 14f, r.y + 10f, 420f, 28f), u.Def.Name, title);
                GUI.Label(new Rect(r.x + 14f, r.y + 40f, 420f, 22f), "<color=#d8c890>" + u.Status + "</color>", label);
                HealthBar(new Rect(r.x + 14f, r.y + 68f, 300f, 12f), u.Health01, true);
                string info;
                if (u is Squad sq)
                {
                    info = $"Бойцов: {sq.Soldiers.Count}/{u.Def.Soldiers}    Оружие: ";
                    var names = new List<string>();
                    foreach (var w in u.Def.Weapons) if (!names.Contains(w.Name)) names.Add(w.Name);
                    info += string.Join(", ", names);
                    if (sq.Suppression > 0.05f)
                    {
                        GUI.Label(new Rect(r.x + 330f, r.y + 62f, 120f, 22f), "Подавление", small);
                        Fill(new Rect(r.x + 420f, r.y + 68f, 120f, 10f), new Color(0f, 0f, 0f, 0.6f));
                        Fill(new Rect(r.x + 420f, r.y + 68f, 120f * sq.Suppression, 10f), Shapes.Hex("#ffcf4a"));
                    }
                    if (sq.Def.HasAtGrenades)
                        info += sq.GrenadeCooldown > 0f ? $"\nПТ-граната: через {Mathf.CeilToInt(sq.GrenadeCooldown)} с" : "\nПТ-граната готова (вплотную к танку)";
                }
                else
                {
                    var t = (Tank)u;
                    info = $"Броня: лоб {u.Def.ArmorFront} / борт {u.Def.ArmorSide} / корма {u.Def.ArmorRear} мм\n{u.Def.GunAP.Name.Replace(" (бронебойный)", "")}, {u.Def.Coax.Name}";
                    if (t.EngineDamaged) info += "\n<color=#ffb070>Двигатель повреждён — отведите на базу (R)</color>";
                }
                GUI.Label(new Rect(r.x + 14f, r.y + 88f, 440f, 70f), info, small);
            }
            else
            {
                // Карточки нескольких юнитов
                int i = 0;
                foreach (var u in pc.Selection.ToArray())
                {
                    var cr = new Rect(r.x + 10f + (i % 6) * 72f, r.y + 10f + (i / 6) * 72f, 66f, 66f);
                    if (cr.yMax > r.yMax) break;
                    if (GUI.Button(cr, $"<size=13>{u.Def.Short}</size>\n<size=11>{(u is Squad s ? s.Soldiers.Count + " бойц." : Mathf.RoundToInt(u.Health01 * 100f) + "%")}</size>", button))
                    {
                        if (Event.current.shift) pc.Remove(u); else pc.SelectOnly(u);
                    }
                    HealthBar(new Rect(cr.x + 4f, cr.yMax - 9f, cr.width - 8f, 5f), u.Health01, true);
                    i++;
                }
            }
            // Приказы
            float bx = r.xMax - 170f;
            if (GUI.Button(new Rect(bx, r.y + 12f, 156f, 40f), "Стоп  <size=13>(X)</size>", button))
                foreach (var u in pc.Selection) u.OrderStop();
            if (GUI.Button(new Rect(bx, r.y + 58f, 156f, 40f), "Отступить  <size=13>(R)</size>", button))
                foreach (var u in pc.Selection) u.OrderRetreat();
            GUI.Label(new Rect(bx, r.y + 104f, 160f, 54f), "ПКМ — идти/атаковать\nShift — маршрут, F/Ctrl — с атакой", new GUIStyle(small) { fontSize = 12 });
        }

        void Reinforcements(Battle battle)
        {
            var side = battle.PlayerSide;
            var r = Ui(new Rect(vw - 330f, vh - 290f, 320f, 280f));
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 12f, r.y + 8f, 300f, 26f), "Подкрепления", title);
            for (int i = 0; i < 4; i++)
            {
                var kind = (UnitKind)i;
                var def = UnitDefs.Get(side, kind);
                bool afford = battle.Manpower[(int)side] >= def.Cost;
                var br = new Rect(r.x + 10f, r.y + 42f + i * 46f, 300f, 42f);
                var content = new GUIContent($"<b>{def.Name}</b>  <size=13>F{5 + i}</size>\n<size=13><color={(afford ? "#c8e8a8" : "#d08070")}>{def.Cost} ж/с</color></size>", def.Description);
                if (GUI.Button(br, content, button) && PlayerCommander.I != null) PlayerCommander.I.Buy(kind);
            }
            // Очередь
            float y = r.y + 230f;
            foreach (var q in battle.Queue)
            {
                if (q.Side != side) continue;
                var def = UnitDefs.Get(side, q.Kind);
                GUI.Label(new Rect(r.x + 12f, y - 2f, 160f, 20f), def.Short, small);
                Fill(new Rect(r.x + 100f, y + 4f, 200f, 8f), new Color(0f, 0f, 0f, 0.6f));
                Fill(new Rect(r.x + 100f, y + 4f, 200f * (1f - q.Left / q.Total), 8f), Shapes.Hex("#c8a050"));
                y += 18f;
                if (y > r.yMax - 6f) break;
            }
            // Подсказка к кнопке
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                var tr = new Rect(r.x - 330f, r.y + 40f, 320f, 90f);
                GUI.Box(tr, GUIContent.none, panel);
                GUI.Label(new Rect(tr.x + 10f, tr.y + 8f, tr.width - 20f, tr.height - 10f), GUI.tooltip, small);
            }
        }

        // ---------- Над юнитами ----------

        void HealthBar(Rect r, float k, bool own)
        {
            Fill(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(0f, 0f, 0f, 0.7f));
            var c = own ? Color.Lerp(Shapes.Hex("#d85a3a"), Shapes.Hex("#6ad04a"), k) : Shapes.Hex("#e04a3a");
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(k), r.height), c);
        }

        void UnitOverlays(Battle battle)
        {
            var cam = Rig.Cam;
            foreach (var u in battle.Units)
            {
                if (!u.Alive || !u.VisibleToPlayer) continue;
                var head = u.Position + Vector3.up * (u.IsVehicle ? 3.6f : 2.4f);
                var sp = cam.WorldToScreenPoint(head);
                if (sp.z <= 0f || sp.z > 220f) continue;
                var p = new Vector2(sp.x / scale, (Screen.height - sp.y) / scale);
                bool own = u.Side == battle.PlayerSide;
                float w = u.Selected || u.Hovered ? 54f : 40f;
                if (u is Squad sq)
                {
                    // Пипсы бойцов
                    int n = u.Def.Soldiers;
                    float pw = Mathf.Min(9f, (w + 10f) / n - 2f);
                    float x0 = p.x - (n * (pw + 2f)) / 2f;
                    for (int i = 0; i < n; i++)
                    {
                        var pr = new Rect(x0 + i * (pw + 2f), p.y, pw, 6f);
                        Fill(new Rect(pr.x - 1f, pr.y - 1f, pr.width + 2f, pr.height + 2f), new Color(0f, 0f, 0f, 0.7f));
                        if (i < sq.Soldiers.Count)
                        {
                            float k = sq.Soldiers[i].Hp / sq.Soldiers[i].MaxHp;
                            Fill(pr, own ? Color.Lerp(Shapes.Hex("#d85a3a"), Shapes.Hex("#6ad04a"), k) : Shapes.Hex("#e04a3a"));
                        }
                    }
                    if (sq.Suppression > 0.1f)
                        Fill(new Rect(p.x - w / 2f, p.y + 8f, w * sq.Suppression, 3f), sq.Pinned ? Shapes.Hex("#ff7a3a") : Shapes.Hex("#ffcf4a"));
                }
                else HealthBar(new Rect(p.x - w / 2f, p.y, w, 6f), u.Health01, own);
                if (u.Group > 0 && own)
                    GUI.Label(new Rect(p.x - w / 2f - 16f, p.y - 7f, 16f, 18f), "<b>" + u.Group + "</b>", new GUIStyle(small) { fontSize = 13 });
                if (u.Selected || u.Hovered)
                    GUI.Label(new Rect(p.x - 100f, p.y - 24f, 200f, 20f), u.Def.Short + (u.Status != "Ожидает приказа" ? " · " + u.Status : ""),
                        new GUIStyle(small) { alignment = TextAnchor.UpperCenter, fontSize = 13, normal = { textColor = own ? Shapes.Hex("#d8f0c8") : Shapes.Hex("#ffc0b0") } });
            }
        }

        void CursorHint(Battle battle)
        {
            var pc = PlayerCommander.I;
            if (pc == null || pc.Selection.Count == 0 || !pc.CursorGround.HasValue || MouseOverUi) return;
            var cover = CoverMap.At(pc.CursorGround.Value);
            if (cover == Cover.None && pc.HoverUnit == null) return;
            var m = Event.current.mousePosition;
            string text = pc.HoverUnit != null && pc.HoverUnit.Side != battle.PlayerSide ? "<color=#ff8a7a>Атаковать</color>"
                : cover == Cover.Heavy ? "<color=#8ad86a>■ Тяжёлое укрытие</color>" : cover == Cover.Light ? "<color=#e8d070>■ Лёгкое укрытие</color>" : "";
            if (text.Length > 0) GUI.Label(new Rect(m.x + 18f, m.y + 14f, 220f, 22f), text, new GUIStyle(small) { fontSize = 15 });
        }

        void DragRect()
        {
            var pc = PlayerCommander.I;
            if (pc == null || !pc.Dragging) return;
            var r = pc.DragRect;
            var gr = new Rect(r.x / scale, (Screen.height - r.yMax) / scale, r.width / scale, r.height / scale);
            Fill(gr, new Color(0.5f, 1f, 0.4f, 0.12f));
            Frame(gr, new Color(0.6f, 1f, 0.5f, 0.8f), 1.5f);
        }

        // ---------- Окна ----------

        void Help()
        {
            var r = Ui(new Rect(vw / 2f - 330f, 140f, 660f, 520f));
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20f, r.y + 14f, 600f, 30f), "Управление", title);
            GUI.Label(new Rect(r.x + 20f, r.y + 54f, 620f, 460f),
                "<b>Камера:</b> WASD / стрелки / край экрана — сдвиг, Q/E или средняя кнопка — поворот, колесо — приближение, H — к базе\n" +
                "<b>Выделение:</b> ЛКМ, рамка, Shift — добавить, двойной клик — все такие же, Ctrl+A — все\n" +
                "<b>Приказы:</b> ПКМ по земле — идти, по врагу — атаковать; Shift+ПКМ — точка маршрута;\n   F+ПКМ или Ctrl+ПКМ — движение с атакой (остановятся при встрече с врагом)\n" +
                "<b>X</b> — стоп, <b>R</b> — отступить на базу (там отряд пополняется, танк чинится)\n" +
                "<b>Группы:</b> Ctrl+1…9 — запомнить, 1…9 — выбрать, дважды — камера к группе\n" +
                "<b>Пробел</b> — пауза (приказы можно отдавать на паузе), <b>+ / −</b> — скорость\n" +
                "<b>F5–F8</b> — подкрепления, <b>Esc</b> — меню\n\n" +
                "<b>Советы:</b> пехота в укрытии (лес, изгороди, стены, воронки) получает меньше попаданий. Пулемёт прижимает пехоту — " +
                "обходите его с фланга. Броня танка в лоб толще всего: заходите в борт и корму. Пехота вплотную бросает гранату в танк. " +
                "Захватывает точки только пехота.", label);
        }

        void PauseMenu(Battle battle)
        {
            var r = Ui(new Rect(vw / 2f - 180f, vh / 2f - 170f, 360f, 340f));
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x, r.y + 16f, r.width, 34f), "Меню", new GUIStyle(title) { alignment = TextAnchor.UpperCenter });
            if (GUI.Button(new Rect(r.x + 30f, r.y + 70f, 300f, 48f), "Продолжить", button)) { ShowMenu = false; battle.SetPaused(false); }
            if (GUI.Button(new Rect(r.x + 30f, r.y + 128f, 300f, 48f), "Начать бой заново", button)) { ShowMenu = false; Game.StartBattle(battle.PlayerSide); }
            if (GUI.Button(new Rect(r.x + 30f, r.y + 186f, 300f, 48f), "Главное меню", button)) { ShowMenu = false; Game.ToMenu(); }
            if (GUI.Button(new Rect(r.x + 30f, r.y + 244f, 300f, 48f), "Выйти из игры", button)) Game.Quit();
        }

        void EndScreen(Battle battle)
        {
            Fill(new Rect(0f, 0f, vw, vh), new Color(0f, 0f, 0f, 0.45f));
            bool win = battle.Winner == battle.PlayerSide;
            GUI.Label(new Rect(0f, vh / 2f - 200f, vw, 90f), win ? "ПОБЕДА" : "ПОРАЖЕНИЕ", new GUIStyle(big) { normal = { textColor = win ? Shapes.Hex("#e8d690") : Shapes.Hex("#d86a5a") } });
            var me = (int)battle.PlayerSide;
            int t = Mathf.FloorToInt(battle.Elapsed);
            GUI.Label(new Rect(0f, vh / 2f - 100f, vw, 90f),
                $"Время боя {t / 60}:{t % 60:00}\nУничтожено врагов: {battle.Kills[me]}    Наши потери: {battle.Losses[me]}", new GUIStyle(center) { fontSize = 22 });
            var r = Ui(new Rect(vw / 2f - 160f, vh / 2f + 10f, 320f, 120f));
            if (GUI.Button(new Rect(r.x, r.y, r.width, 50f), "Ещё раз", button)) Game.StartBattle(battle.PlayerSide);
            if (GUI.Button(new Rect(r.x, r.y + 60f, r.width, 50f), "Главное меню", button)) Game.ToMenu();
        }

        // ---------- Главное меню ----------

        void MainMenu()
        {
            Fill(new Rect(0f, 0f, vw, vh), new Color(0f, 0f, 0f, 0.25f));
            Fill(new Rect(0f, 0f, 760f, vh), new Color(0.05f, 0.05f, 0.045f, 0.78f));
            GUI.Label(new Rect(60f, 70f, 680f, 70f), "<b>REAL PEACE</b>", new GUIStyle(label) { fontSize = 60, wordWrap = false });
            GUI.Label(new Rect(64f, 148f, 680f, 30f), "Битва за Токмачку · осень 1943 · Запорожье", new GUIStyle(label) { fontSize = 22, normal = { textColor = Shapes.Hex("#c8b88a") } });

            GUI.Label(new Rect(64f, 220f, 600f, 30f), "Выберите сторону", title);
            SideCard(new Rect(64f, 262f, 300f, 250f), Faction.USSR,
                "Красная армия наступает на Токмачку и рвёт «Линию Вотан». Отделения по 6 бойцов, «Максим», ПТРД и быстрый Т-34-76.");
            SideCard(new Rect(384f, 262f, 300f, 250f), Faction.Germany,
                "Вермахт удерживает Токмачку на «Линии Вотан». Отделения по 5 солдат, скорострельный MG 42, «Панцершрек» и Pz.IV.");

            var s = GameSettings.I;
            GUI.Label(new Rect(64f, 540f, 600f, 30f), "Сложность", title);
            string[] diff = { "Лёгкая", "Обычная", "Тяжёлая" };
            for (int i = 0; i < 3; i++)
                if (GUI.Button(new Rect(64f + i * 210f, 578f, 200f, 44f), diff[i], (int)s.difficulty == i ? buttonOn : button))
                    s.difficulty = (Difficulty)i;
            if (GUI.Button(new Rect(64f, 636f, 410f, 44f), "Туман войны: " + (s.fogOfWar ? "включён" : "выключен"), s.fogOfWar ? buttonOn : button))
                s.fogOfWar = !s.fogOfWar;

            if (GUI.Button(new Rect(64f, 712f, 620f, 70f), $"<size=26><b>В бой за {Factions.Name(chosenSide)}</b></size>", buttonOn))
                Game.StartBattle(chosenSide);
            GUI.enabled = false;
            GUI.Button(new Rect(64f, 796f, 620f, 50f), "Кампания: Курская дуга — скоро", button);
            GUI.enabled = true;
            if (GUI.Button(new Rect(64f, 860f, 300f, 44f), "Выход", button)) Game.Quit();
            GUI.Label(new Rect(64f, vh - 50f, 680f, 30f), "F1 в бою — управление и советы", small);
        }

        void SideCard(Rect r, Faction side, string text)
        {
            bool on = chosenSide == side;
            if (GUI.Button(r, GUIContent.none, on ? buttonOn : button)) chosenSide = side;
            Fill(new Rect(r.x, r.y, r.width, 8f), Factions.Ui(side));
            GUI.Label(new Rect(r.x + 16f, r.y + 20f, r.width - 32f, 34f), "<b>" + Factions.Name(side) + "</b>", new GUIStyle(title) { fontSize = 28 });
            GUI.Label(new Rect(r.x + 16f, r.y + 58f, r.width - 32f, 24f), Factions.ArmyName(side), new GUIStyle(small) { fontSize = 16 });
            GUI.Label(new Rect(r.x + 16f, r.y + 92f, r.width - 32f, 150f), text, new GUIStyle(label) { fontSize = 16 });
        }
    }
}
