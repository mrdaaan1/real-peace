using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RealPeace
{
    /// <summary>
    /// Точка входа. Висит на объекте "Game" в сцене Battle и при запуске строит весь мир кодом:
    /// карту, навигацию, атмосферу, камеру, эффекты, интерфейс. Потом — главное меню (камера облетает поле боя)
    /// или сразу бой. Новый бой — перезагрузка сцены (чистое поле без воронок и остовов прошлого боя).
    ///
    /// Автотест (без участия разработчика): Unity с аргументами -rpAutotest [-rpSide Germany] [-rpSeconds 150]
    /// [-rpOut папка] запускает бой ускоренно, сохраняет скриншоты и пишет статистику в лог, потом выходит.
    /// </summary>
    [RequireComponent(typeof(GameSettings))]
    public class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap I { get; private set; }

        /// <summary>Сторона, за которую начать бой сразу после перезагрузки сцены (null — главное меню).</summary>
        static Faction? pendingStart;

        public bool InMenu { get; private set; } = true;

        Transform world;
        RtsCamera rig;
        Hud hud;
        Transform battleRoot;

        const string MapKey = "RealPeace.Map";

        void Awake()
        {
            I = this;
            // Карта: аргумент автотеста или последняя выбранная в меню
            var arg = Autotest.MapArg;
            var id = arg ?? (MapId)Mathf.Clamp(PlayerPrefs.GetInt(MapKey, 0), 0, 1);
            MapLayout.Select(id);
            Application.targetFrameRate = 120;
        }

        void Start()
        {
            float t0 = Time.realtimeSinceStartup;
            BuildWorld();
            Debug.Log($"[Real Peace] Мир построен за {Time.realtimeSinceStartup - t0:0.0} с");

            if (Autotest.Enabled)
            {
                StartCoroutine(Autotest.Run(this));
                return;
            }
            if (pendingStart.HasValue)
            {
                var side = pendingStart.Value;
                pendingStart = null;
                BeginBattle(side);
            }
            else ShowMenu();
        }

        void BuildWorld()
        {
            world = new GameObject("World").transform;
            MapBuilder.Build(world);
            NavBuilder.Build();
            Atmosphere.Setup(world);
            Effects.Init(world);
            Sfx.Init(world);
            rig = RtsCamera.Create(world);
            hud = gameObject.AddComponent<Hud>();
            hud.Game = this;
            hud.Rig = rig;
        }

        void ShowMenu()
        {
            InMenu = true;
            rig.Showcase = true;
            rig.SetView(MapLayout.Point(1), 30f, 95f, true);
        }

        /// <summary>Начать бой за сторону. Если бой уже был — перезагрузить сцену и начать на чистом поле.</summary>
        public void StartBattle(Faction side)
        {
            if (Battle.I != null)
            {
                pendingStart = side;
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().name : "Battle");
                return;
            }
            BeginBattle(side);
        }

        public void BeginBattle(Faction side)
        {
            InMenu = false;
            rig.Showcase = false;
            battleRoot = new GameObject("BattleRoot").transform;
            battleRoot.SetParent(world, false);
            Battle.Create(battleRoot, side);
            PlayerCommander.Create(battleRoot, rig);
            AiCommander.Create(battleRoot, side.Other());
            // Камера — за своей базой, лицом к фронту
            var b = MapLayout.Base(side);
            rig.SetView(b + new Vector3(0f, 0f, -Factions.NorthSign(side) * 30f), side == Faction.USSR ? 0f : 180f, 75f, true);
        }

        /// <summary>Выбрать карту в меню: запоминаем и перестраиваем мир (перезагрузка сцены).</summary>
        public void SelectMap(MapId id)
        {
            PlayerPrefs.SetInt(MapKey, (int)id);
            PlayerPrefs.Save();
            pendingStart = null;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public void ToMenu()
        {
            pendingStart = null;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public RtsCamera Rig => rig;
    }

    /// <summary>Прогон боя без человека: скриншоты и статистика (см. GameBootstrap).</summary>
    public static class Autotest
    {
        static string Arg(string name, string def)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return def;
        }

        public static MapId? MapArg
        {
            get
            {
                var m = Arg("-rpMap", null);
                if (m == null) return null;
                return m == "Melitopol" ? MapId.Melitopol : MapId.Tokmachka;
            }
        }

        public static bool Enabled => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-rpAutotest") >= 0;

        public static IEnumerator Run(GameBootstrap game)
        {
            var side = Arg("-rpSide", "USSR") == "Germany" ? Faction.Germany : Faction.USSR;
            float seconds = float.Parse(Arg("-rpSeconds", "150"), System.Globalization.CultureInfo.InvariantCulture);
            string outDir = Arg("-rpOut", Path.Combine(Application.dataPath, "../Tools/out"));
            Directory.CreateDirectory(outDir);
            AudioListener.volume = 0f; // тест идёт в фоне — без грохота в колонках
            Debug.Log($"[Autotest] Сторона {side}, {seconds} с боя, скриншоты в {outDir}");

            // Скриншоты карты до боя (несколько кадров — пусть освещение и отражения успеют обновиться)
            for (int i = 0; i < 20; i++) yield return null;
            var rig = game.Rig;
            Shot(rig, outDir, "00_overview", new Vector3(-20f, 0f, 0f), 20f, 170f);
            for (int i = 0; i < MapLayout.PointXZ.Length; i++)
                Shot(rig, outDir, $"0{i + 1}_point_{i}", MapLayout.Point(i) + new Vector3(-10f, 0f, -18f), 30f + i * 50f, 48f);

            GameSettings.I.fogOfWar = false;
            game.BeginBattle(side);
            yield return null;
            Shot(rig, outDir, "06_base_units", MapLayout.Base(side) + new Vector3(0f, 0f, -Factions.NorthSign(side) * 22f), side == Faction.USSR ? 0f : 180f, 22f);
            // Сразу дадим ИИ живой силы на танки — проверим и технику
            Battle.I.Manpower[0] += 1200f;
            Battle.I.Manpower[1] += 1200f;
            // Одинаковые заказы обеим сторонам — тест не должен давать фору
            foreach (Faction f in new[] { Faction.USSR, Faction.Germany })
                foreach (var k in new[] { UnitKind.Tank, UnitKind.Sappers, UnitKind.Mortar, UnitKind.Sniper })
                    Battle.I.Order(f, k, out _);
            // Игрок-«бот»: тоже ИИ за свою сторону
            AiCommander.Create(Battle.I.transform, side);
            Battle.I.SetSpeed(3f);

            float t = 0f, nextLog = 0f, nextShot = 40f;
            bool tankShot = false, newShot = false;
            int shotN = 7;
            while (t < seconds && !Battle.I.Over)
            {
                yield return null;
                t += Time.deltaTime;
                if (t >= nextLog)
                {
                    nextLog += 10f;
                    var b = Battle.I;
                    string pts = "";
                    foreach (var p in b.Points) pts += p.Title + "=" + (p.Owner.HasValue ? Factions.Name(p.Owner.Value) : "—") + " ";
                    string State(Faction f)
                    {
                        int ret = 0, fight = 0, move = 0, pin = 0, idle = 0;
                        foreach (var u in b.Units)
                        {
                            if (u.Side != f) continue;
                            if (u.Retreating) ret++;
                            else if (u is Squad q && q.Pinned) pin++;
                            else if (u is Squad q2 && q2.InCombat) fight++;
                            else if (u.Moving) move++;
                            else idle++;
                        }
                        return $"отход {ret} бой {fight} прижаты {pin} идут {move} стоят {idle} выстр {Combat.Shots[(int)f]} попад {Combat.Hits[(int)f]}";
                    }
                    Debug.Log($"[Autotest]   СССР: {State(Faction.USSR)} | Германия: {State(Faction.Germany)}");
                    Debug.Log($"[Autotest] t={t:0} fps={1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime):0} СССР: юн {b.CountUnits(Faction.USSR)} очки {b.Tickets[0]:0} ж/с {b.Manpower[0]:0} | " +
                              $"Германия: юн {b.CountUnits(Faction.Germany)} очки {b.Tickets[1]:0} ж/с {b.Manpower[1]:0} | {pts}| потери {b.Losses[0]}/{b.Losses[1]}");
                }
                if (!newShot && t > 75f)
                    foreach (var u in Battle.I.Units)
                        if (u.Alive && (u.Def.Kind == UnitKind.Sappers || u.Def.Kind == UnitKind.Mortar))
                        {
                            newShot = true;
                            Shot(rig, outDir, "06c_" + u.Def.Kind, u.Position, Random.Range(0f, 360f), 12f);
                            break;
                        }
                if (!tankShot && t > 45f)
                    foreach (var u in Battle.I.Units)
                        if (u is Tank && u.Alive)
                        {
                            tankShot = true;
                            Shot(rig, outDir, "06b_tank_close", u.Position, Random.Range(0f, 360f), 15f);
                            break;
                        }
                if (t >= nextShot)
                {
                    nextShot += 70f;
                    // Снимаем там, где идёт бой: у юнита, который сейчас стреляет
                    Vector3 at = MapLayout.Point(1);
                    foreach (var u in Battle.I.Units)
                        if (u is Squad sq && sq.InCombat) { at = u.Position; break; }
                    Shot(rig, outDir, $"{shotN++:00}_battle_{t:0}", at, Random.Range(0f, 360f), 30f);
                }
            }
            var bb = Battle.I;
            Debug.Log($"[Autotest] Итог: {(bb.Over ? "победа " + Factions.Name(bb.Winner) : "время вышло")}, потери СССР {bb.Losses[0]}, Германия {bb.Losses[1]}");
            Shot(rig, outDir, $"{shotN:00}_final", MapLayout.Point(1), 45f, 90f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        static void Shot(RtsCamera rig, string dir, string name, Vector3 focus, float yaw, float distance)
        {
            rig.SetView(focus, yaw, distance, true);
            var cam = rig.Cam;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            if (GrassField.I != null) GrassField.I.Submit(cam);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prev;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            Debug.Log("[Autotest] Скриншот " + name);
        }
    }
}
