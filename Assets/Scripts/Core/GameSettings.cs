using UnityEngine;

namespace RealPeace
{
    public enum Difficulty { Easy, Normal, Hard }

    /// <summary>
    /// Баланс боя в одном месте. Висит на объекте "Game" в сцене, значения можно крутить в инспекторе.
    /// </summary>
    public class GameSettings : MonoBehaviour
    {
        public static GameSettings I { get; private set; }

        [Header("Очки победы и живая сила")]
        public int startTickets = 500;
        [Tooltip("Сколько очков в секунду теряет сторона за каждую точку, которой у неё меньше, чем у врага")]
        public float bleedPerPointPerSecond = 0.9f;
        [Tooltip("Очки победы за каждого погибшего бойца и за подбитый танк")]
        public float ticketsPerSoldier = 1.5f;
        public float ticketsPerTank = 15f;
        public int startManpower = 300;
        [Tooltip("Живая сила в секунду без точек")]
        public float baseIncome = 3f;
        [Tooltip("Добавка за каждую удерживаемую точку, в секунду")]
        public float incomePerPoint = 2.5f;
        [Tooltip("Максимум юнитов у стороны")]
        public int unitCap = 12;

        [Header("Захват точек")]
        public float captureRadius = 11f;
        [Tooltip("Секунд на захват одним отрядом (с нейтральной до своей)")]
        public float captureSeconds = 12f;

        [Header("Туман войны")]
        public bool fogOfWar = true;

        [Header("ИИ")]
        public Difficulty difficulty = Difficulty.Normal;

        [Tooltip("Версия значений по умолчанию (для миграции старой сцены)")]
        public int settingsVersion;
        const int CurrentVersion = 2;

        void Awake()
        {
            I = this;
            // Сцена хранит значения, сохранённые при её создании. Поменяв значения по умолчанию в коде,
            // повышаем CurrentVersion и переносим их сюда.
            if (settingsVersion < 2)
            {
                bleedPerPointPerSecond = 0.9f;
            }
            settingsVersion = CurrentVersion;
        }

        /// <summary>Множитель дохода ИИ по сложности.</summary>
        public float AiIncomeMultiplier => difficulty == Difficulty.Easy ? 0.7f : difficulty == Difficulty.Hard ? 1.35f : 1f;
        /// <summary>Множитель меткости ИИ по сложности.</summary>
        public float AiAccuracyMultiplier => difficulty == Difficulty.Easy ? 0.8f : difficulty == Difficulty.Hard ? 1.1f : 1f;
    }
}
