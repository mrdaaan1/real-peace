using System.Collections.Generic;
using UnityEngine;

namespace RealPeace
{
    /// <summary>
    /// Юнит, которым командуют: отряд пехоты или танк. Общая часть — приказы:
    /// маршрут из точек (Shift+ПКМ добавляет точку), движение с атакой (останавливается при виде врага),
    /// атака цели, стоп, отступление на базу. Как именно ехать и стрелять — решают Squad и Tank.
    /// </summary>
    public abstract class Unit : MonoBehaviour
    {
        static int nextId;

        public int Id { get; private set; }
        public Faction Side;
        public UnitDef Def;
        public bool Alive { get; protected set; } = true;
        public bool Selected { get; private set; }
        bool hovered;
        /// <summary>Под курсором мыши (подсветка кольцами).</summary>
        public bool Hovered
        {
            get => hovered;
            set
            {
                if (hovered == value) return;
                hovered = value;
                ApplyHighlight();
            }
        }

        /// <summary>Видит ли юнит сторона (индекс — Faction). Обновляет Battle.</summary>
        public readonly bool[] SpottedBy = new bool[2];
        /// <summary>Выстрелы выдают позицию на несколько секунд.</summary>
        public float RevealTimer;
        public bool VisibleToPlayer { get; private set; } = true;

        public readonly List<Vector3> Waypoints = new List<Vector3>();
        public bool AttackMove { get; protected set; }
        public Unit ForcedTarget { get; protected set; }
        public bool Retreating { get; protected set; }
        /// <summary>Номер группы (Ctrl+цифра), 0 — нет.</summary>
        public int Group;

        bool moveIssued;
        bool holding; // при движении с атакой остановились на бой
        float chaseTimer;

        public abstract Vector3 Position { get; }
        public abstract float Health01 { get; }
        public abstract bool Moving { get; }
        public virtual bool IsVehicle => false;
        public virtual bool CanCapture => false;
        /// <summary>Дальность основного оружия — для приказа «атаковать».</summary>
        public abstract float AttackRange { get; }
        /// <summary>Строка состояния для интерфейса: «Подавлен», «Отступает»…</summary>
        public abstract string Status { get; }
        /// <summary>Точки юнита для выделения мышью (солдаты отряда или центр танка).</summary>
        public abstract IEnumerable<Vector3> PickPoints { get; }
        public abstract float PickRadius { get; }

        /// <summary>Насколько трудно заметить: в лесу и тяжёлом укрытии — ближе.</summary>
        public virtual float Concealment => MapLayout.InForest(Position.x, Position.z) ? 0.65f : 1f;

        protected abstract void MoveTo(Vector3 destination, Vector3 facing);
        protected abstract void Halt();
        protected abstract bool ReachedDestination();
        /// <summary>Есть ли враг, по которому можно стрелять (для движения с атакой).</summary>
        protected abstract bool HasEnemyInRange();
        protected abstract void ApplyHighlight();
        protected abstract void ApplyVisible(bool visible);

        protected virtual void Awake() => Id = ++nextId;

        public Vector3 CurrentDestination => Waypoints.Count > 0 ? Waypoints[0] : Position;

        // ---------- Приказы ----------

        public void OrderMove(Vector3 destination, bool queue, bool attackMove)
        {
            if (!Alive) return;
            if (Retreating && !queue) Retreating = false;
            if (!queue)
            {
                Waypoints.Clear();
                ForcedTarget = null;
            }
            Waypoints.Add(MapLayout.ClampToMap(destination, 3f));
            AttackMove = attackMove;
            if (!queue || Waypoints.Count == 1) moveIssued = false;
            holding = false;
        }

        public void OrderAttack(Unit target)
        {
            if (!Alive || target == null || target.Side == Side) return;
            Waypoints.Clear();
            Retreating = false;
            ForcedTarget = target;
            AttackMove = false;
            chaseTimer = 0f;
            holding = false;
        }

        public virtual void OrderStop()
        {
            Waypoints.Clear();
            ForcedTarget = null;
            AttackMove = false;
            Retreating = false;
            holding = false;
            Halt();
        }

        public virtual void OrderRetreat()
        {
            if (!Alive) return;
            Waypoints.Clear();
            ForcedTarget = null;
            AttackMove = false;
            var b = MapLayout.Base(Side) + new Vector3(Random.Range(-10f, 10f), 0f, Random.Range(-6f, 6f));
            Waypoints.Add(b);
            Retreating = true;
            moveIssued = false;
            holding = false;
        }

        public void SetSelected(bool value)
        {
            Selected = value;
            ApplyHighlight();
        }

        public void SetVisibleToPlayer(bool value)
        {
            if (VisibleToPlayer == value) return;
            VisibleToPlayer = value;
            ApplyVisible(value);
            if (!value && Hovered) Hovered = false;
        }

        // ---------- Исполнение приказов ----------

        protected virtual void Update()
        {
            if (!Alive) return;
            float dt = Time.deltaTime;
            if (RevealTimer > 0f) RevealTimer -= dt;

            // Атака цели: подойти на дальность выстрела и с линией видимости, потом стоять и стрелять
            if (ForcedTarget != null)
            {
                if (!ForcedTarget.Alive || !ForcedTarget.SpottedBy[(int)Side])
                {
                    ForcedTarget = null;
                    Halt();
                }
                else
                {
                    float d = Vector3.Distance(Position, ForcedTarget.Position);
                    bool canShoot = d < AttackRange * 0.92f && Combat.LineOfSight(Position, ForcedTarget.Position);
                    if (canShoot)
                    {
                        if (Moving) Halt();
                    }
                    else
                    {
                        chaseTimer -= dt;
                        if (chaseTimer <= 0f)
                        {
                            chaseTimer = 1f;
                            var to = ForcedTarget.Position;
                            var dir = (to - Position).normalized;
                            MoveTo(to - dir * Mathf.Min(d * 0.5f, AttackRange * 0.6f), dir);
                        }
                    }
                }
                return;
            }

            if (Waypoints.Count == 0) return;

            if (AttackMove && HasEnemyInRange())
            {
                if (!holding)
                {
                    holding = true;
                    Halt();
                }
                return;
            }
            if (holding)
            {
                holding = false;
                moveIssued = false;
            }

            if (!moveIssued)
            {
                var from = Position;
                var dest = Waypoints[0];
                var dir = dest - from;
                dir.y = 0f;
                MoveTo(dest, dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward);
                moveIssued = true;
                return;
            }

            if (ReachedDestination())
            {
                Waypoints.RemoveAt(0);
                moveIssued = false;
                if (Waypoints.Count == 0)
                {
                    AttackMove = false;
                    if (Retreating) OnRetreatArrived();
                }
            }
        }

        protected virtual void OnRetreatArrived() => Retreating = false;

        /// <summary>Юнит погиб: снять выделение, сообщить бою.</summary>
        protected void Die()
        {
            if (!Alive) return;
            Alive = false;
            Waypoints.Clear();
            ForcedTarget = null;
            SetSelected(false);
            if (Battle.I != null) Battle.I.OnUnitDestroyed(this);
        }

        /// <summary>Ближайший замеченный враг в радиусе, по которому есть линия видимости (с фильтром).</summary>
        protected Unit FindEnemy(Vector3 from, float range, System.Func<Unit, bool> filter)
        {
            if (Battle.I == null) return null;
            Unit best = null;
            float bestD = range;
            foreach (var u in Battle.I.Units)
            {
                if (!u.Alive || u.Side == Side || !u.SpottedBy[(int)Side]) continue;
                if (filter != null && !filter(u)) continue;
                float d = Vector3.Distance(from, u.Position);
                if (d >= bestD) continue;
                if (!Combat.LineOfSight(from, u.Position)) continue;
                best = u;
                bestD = d;
            }
            return best;
        }
    }
}
