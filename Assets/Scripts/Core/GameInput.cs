using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Все клавиши и мышь в одном месте (старый Input Manager — так настроен проект).
    /// Пауза в стратегии — игровая (Time.timeScale = 0), ввод при ней работает: на паузе раздаём приказы.
    /// </summary>
    public static class GameInput
    {
        /// <summary>Пока открыто меню, игра не получает нажатий.</summary>
        public static bool Blocked;

        // Камера
        public static Vector2 Pan => Blocked ? Vector2.zero : new Vector2(
            (Key(KeyCode.D) || Key(KeyCode.RightArrow) ? 1f : 0f) - (Key(KeyCode.A) || Key(KeyCode.LeftArrow) ? 1f : 0f),
            (Key(KeyCode.W) || Key(KeyCode.UpArrow) ? 1f : 0f) - (Key(KeyCode.S) || Key(KeyCode.DownArrow) ? 1f : 0f));
        public static float Rotate => Blocked ? 0f : (Key(KeyCode.E) ? 1f : 0f) - (Key(KeyCode.Q) ? 1f : 0f);
        public static float Zoom => Blocked ? 0f : Input.mouseScrollDelta.y;
        public static bool FastPan => Key(KeyCode.LeftShift) || Key(KeyCode.RightShift);

        // Модификаторы
        public static bool Shift => Key(KeyCode.LeftShift) || Key(KeyCode.RightShift);
        public static bool Ctrl => Key(KeyCode.LeftControl) || Key(KeyCode.RightControl) || Key(KeyCode.LeftCommand) || Key(KeyCode.RightCommand);

        // Мышь
        public static Vector3 MousePosition => Input.mousePosition;
        public static Vector2 MouseDelta => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        public static bool LeftDown => !Blocked && Input.GetMouseButtonDown(0);
        public static bool LeftHeld => !Blocked && Input.GetMouseButton(0);
        public static bool LeftUp => Input.GetMouseButtonUp(0);
        public static bool RightDown => !Blocked && Input.GetMouseButtonDown(1);
        public static bool MiddleHeld => !Blocked && Input.GetMouseButton(2);

        // Приказы
        public static bool PausePressed => Down(KeyCode.Space);
        public static bool MenuPressed => Input.GetKeyDown(KeyCode.Escape);
        public static bool StopPressed => Down(KeyCode.X);
        public static bool RetreatPressed => Down(KeyCode.R);
        public static bool AttackMoveHeld => Key(KeyCode.F);
        public static bool HelpPressed => Down(KeyCode.F1);
        public static bool SpeedUpPressed => Down(KeyCode.Equals) || Down(KeyCode.KeypadPlus);
        public static bool SpeedDownPressed => Down(KeyCode.Minus) || Down(KeyCode.KeypadMinus);
        public static bool SelectAllPressed => Ctrl && Down(KeyCode.A);
        public static bool FocusPressed => Down(KeyCode.Home) || Down(KeyCode.H);

        /// <summary>Вызов подкреплений: F5–F8 (буквы заняты камерой и приказами).</summary>
        public static int BuyPressed =>
            Down(KeyCode.F5) ? 1 : Down(KeyCode.F6) ? 2 : Down(KeyCode.F7) ? 3 : Down(KeyCode.F8) ? 4 : 0;

        /// <summary>Группы: Ctrl+1…9 — запомнить, 1…9 — выбрать. Возвращает номер или 0.</summary>
        public static int GroupPressed
        {
            get
            {
                if (Blocked) return 0;
                for (int i = 1; i <= 9; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha0 + i)) return i;
                return 0;
            }
        }

        static bool Key(KeyCode k) => !Blocked && Input.GetKey(k);
        static bool Down(KeyCode k) => !Blocked && Input.GetKeyDown(k);
    }
}
