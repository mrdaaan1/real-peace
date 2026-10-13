using UnityEngine;

namespace RealPeace
{
    public enum UnitKind { Riflemen, MachineGun, AntiTank, Tank, Mortar, Sniper, Sappers }

    public enum ShotSound { Rifle, Smg, MachineGun, AtRifle, Cannon, Rocket, Mortar, Flame }

    /// <summary>Оружие: дальность, урон, скорострельность, меткость, бронепробитие, подавление.</summary>
    public class Weapon
    {
        public string Name;
        public float Range = 35f;
        public float Damage = 30f;
        /// <summary>Пауза между очередями/выстрелами, с.</summary>
        public float Cooldown = 2f;
        public int Burst = 1;
        public float BurstInterval = 0.1f;
        /// <summary>Шанс попасть вплотную и на предельной дальности.</summary>
        public float AccNear = 0.6f, AccFar = 0.2f;
        /// <summary>Бронепробитие, мм. 0 — по технике бесполезно.</summary>
        public float Penetration;
        /// <summary>Радиус разрыва, м (снаряды, гранаты). 0 — пуля.</summary>
        public float Splash;
        /// <summary>Подавление пехоты за выстрел по отряду.</summary>
        public float Suppression = 0.01f;
        /// <summary>Стреляет только с места после развёртывания (станковый пулемёт).</summary>
        public float SetupTime;
        public bool VsInfantry = true;
        public bool VsVehicles;
        public bool Tracer;
        public ShotSound Sound;
        /// <summary>Навесной огонь (миномёт): стреляет по замеченным своими целям без прямой видимости.</summary>
        public bool Indirect;
        public float MinRange;
        /// <summary>Огнемёт: бьёт струёй по площади, укрытия не спасают.</summary>
        public bool Flame;
        /// <summary>Модель оружия из Weapons.bytes (если не задана — по звуку и стороне).</summary>
        public string Model;

        public AudioClip Clip
        {
            get
            {
                switch (Sound)
                {
                    case ShotSound.Smg: return SoundFactory.Smg;
                    case ShotSound.MachineGun: return SoundFactory.MachineGun;
                    case ShotSound.AtRifle: return SoundFactory.AtRifle;
                    case ShotSound.Cannon: return SoundFactory.Cannon;
                    case ShotSound.Rocket: return SoundFactory.Grenade;
                    case ShotSound.Mortar: return SoundFactory.MortarShot;
                    case ShotSound.Flame: return SoundFactory.Flame;
                    default: return SoundFactory.Rifle;
                }
            }
        }
    }

    /// <summary>Описание типа юнита для каждой стороны: название, цена, состав, оружие, броня.</summary>
    public class UnitDef
    {
        public UnitKind Kind;
        public Faction Side;
        public string Name;
        public string Short;
        public string Description;
        public int Cost;
        public float BuildTime;

        // Пехота
        public int Soldiers;
        public Weapon[] Weapons; // по номеру бойца (последнее оружие — для остальных)
        public float SoldierHp = 62f;
        public bool HasAtGrenades;

        // Техника
        public bool Vehicle;
        public float Hp;
        public float ArmorFront, ArmorSide, ArmorRear;
        public Weapon GunAP, GunHE, Coax;
        public float TurretSpeed = 30f;

        public float Speed = 3.6f;
        public float Sight = 38f;

        public Weapon WeaponFor(int soldier) => Weapons[Mathf.Min(soldier, Weapons.Length - 1)];
    }

    /// <summary>
    /// Весь баланс юнитов. Стороны симметричны по силе, отличаются названиями, оружием и характером:
    /// советская пехота многочисленнее, немецкий пулемёт скорострельнее, Pz.IV лучше пробивает, Т-34 быстрее.
    /// </summary>
    public static class UnitDefs
    {
        static readonly Weapon Mosin = new Weapon { Name = "Винтовка Мосина", Range = 36f, Damage = 30f, Cooldown = 2.4f, AccNear = 0.6f, AccFar = 0.22f, Suppression = 0.012f, Sound = ShotSound.Rifle };
        static readonly Weapon Kar98 = new Weapon { Name = "Kar98k", Range = 36f, Damage = 30f, Cooldown = 2.3f, AccNear = 0.62f, AccFar = 0.23f, Suppression = 0.012f, Sound = ShotSound.Rifle };
        static readonly Weapon Ppsh = new Weapon { Name = "ППШ-41", Range = 22f, Damage = 11f, Cooldown = 1.6f, Burst = 6, BurstInterval = 0.07f, AccNear = 0.5f, AccFar = 0.12f, Suppression = 0.01f, Sound = ShotSound.Smg };
        static readonly Weapon Mp40 = new Weapon { Name = "MP 40", Range = 22f, Damage = 11f, Cooldown = 1.6f, Burst = 5, BurstInterval = 0.09f, AccNear = 0.52f, AccFar = 0.12f, Suppression = 0.01f, Sound = ShotSound.Smg };
        static readonly Weapon Maxim = new Weapon { Name = "Пулемёт Максим", Range = 44f, Damage = 12f, Cooldown = 1.3f, Burst = 8, BurstInterval = 0.1f, AccNear = 0.42f, AccFar = 0.16f, Suppression = 0.035f, SetupTime = 2.5f, Tracer = true, Sound = ShotSound.MachineGun };
        static readonly Weapon Mg42 = new Weapon { Name = "MG 42", Range = 44f, Damage = 11f, Cooldown = 1.3f, Burst = 10, BurstInterval = 0.06f, AccNear = 0.4f, AccFar = 0.15f, Suppression = 0.035f, SetupTime = 2f, Tracer = true, Sound = ShotSound.MachineGun };
        static readonly Weapon Ptrd = new Weapon { Name = "ПТРД-41", Range = 38f, Damage = 55f, Cooldown = 3.2f, AccNear = 0.7f, AccFar = 0.4f, Penetration = 45f, VsInfantry = false, VsVehicles = true, Tracer = true, Sound = ShotSound.AtRifle };
        static readonly Weapon Schreck = new Weapon { Name = "Панцершрек", Range = 30f, Damage = 160f, Cooldown = 7f, AccNear = 0.65f, AccFar = 0.3f, Penetration = 150f, Splash = 1.5f, VsInfantry = false, VsVehicles = true, Sound = ShotSound.Rocket };

        static readonly Weapon MosinSniper = new Weapon { Name = "Снайперская винтовка Мосина с ПУ", Range = 72f, Damage = 95f, Cooldown = 4.6f, AccNear = 0.92f, AccFar = 0.72f, Suppression = 0.06f, Sound = ShotSound.Rifle, Model = "MosinSniper" };
        static readonly Weapon Kar98Sniper = new Weapon { Name = "Kar98k с ZF 39", Range = 72f, Damage = 95f, Cooldown = 4.4f, AccNear = 0.92f, AccFar = 0.72f, Suppression = 0.06f, Sound = ShotSound.Rifle, Model = "Kar98Sniper" };
        static readonly Weapon Bm37 = new Weapon { Name = "82-мм миномёт БМ-37", Range = 100f, MinRange = 18f, Damage = 70f, Cooldown = 4.8f, AccNear = 0.5f, AccFar = 0.3f, Splash = 4.5f, Suppression = 0.3f, SetupTime = 3f, Indirect = true, Sound = ShotSound.Mortar, Model = "Mortar" };
        static readonly Weapon Grw34 = new Weapon { Name = "8-cm Granatwerfer 34", Range = 100f, MinRange = 18f, Damage = 70f, Cooldown = 4.6f, AccNear = 0.52f, AccFar = 0.3f, Splash = 4.5f, Suppression = 0.3f, SetupTime = 3f, Indirect = true, Sound = ShotSound.Mortar, Model = "Mortar" };
        static readonly Weapon Roks3 = new Weapon { Name = "Огнемёт РОКС-3", Range = 17f, Damage = 20f, Cooldown = 3.2f, Burst = 8, BurstInterval = 0.1f, AccNear = 0.9f, AccFar = 0.7f, Suppression = 0.06f, Flame = true, Sound = ShotSound.Flame, Model = "Roks3" };
        static readonly Weapon Fmw41 = new Weapon { Name = "Flammenwerfer 41", Range = 18f, Damage = 20f, Cooldown = 3.2f, Burst = 8, BurstInterval = 0.1f, AccNear = 0.9f, AccFar = 0.7f, Suppression = 0.06f, Flame = true, Sound = ShotSound.Flame, Model = "Fmw41" };

        public static readonly Weapon AtGrenade = new Weapon { Name = "Противотанковая граната", Range = 14f, Damage = 120f, Cooldown = 28f, AccNear = 0.75f, AccFar = 0.45f, Penetration = 60f, Splash = 2f, VsInfantry = false, VsVehicles = true, Sound = ShotSound.Rocket };

        static readonly Weapon F34AP = new Weapon { Name = "76-мм Ф-34 (бронебойный)", Range = 55f, Damage = 160f, Cooldown = 5.5f, AccNear = 0.8f, AccFar = 0.45f, Penetration = 85f, VsInfantry = false, VsVehicles = true, Sound = ShotSound.Cannon };
        static readonly Weapon F34HE = new Weapon { Name = "76-мм Ф-34 (осколочный)", Range = 55f, Damage = 85f, Cooldown = 5.5f, AccNear = 0.6f, AccFar = 0.3f, Splash = 4.5f, Suppression = 0.25f, Sound = ShotSound.Cannon };
        static readonly Weapon Kwk40AP = new Weapon { Name = "7,5-см KwK 40 (бронебойный)", Range = 58f, Damage = 160f, Cooldown = 5.8f, AccNear = 0.82f, AccFar = 0.48f, Penetration = 105f, VsInfantry = false, VsVehicles = true, Sound = ShotSound.Cannon };
        static readonly Weapon Kwk40HE = new Weapon { Name = "7,5-см KwK 40 (осколочный)", Range = 58f, Damage = 80f, Cooldown = 5.8f, AccNear = 0.6f, AccFar = 0.3f, Splash = 4.2f, Suppression = 0.25f, Sound = ShotSound.Cannon };
        static readonly Weapon Dt = new Weapon { Name = "Пулемёт ДТ", Range = 36f, Damage = 11f, Cooldown = 1.4f, Burst = 6, BurstInterval = 0.09f, AccNear = 0.4f, AccFar = 0.14f, Suppression = 0.025f, Tracer = true, Sound = ShotSound.MachineGun };
        static readonly Weapon Mg34 = new Weapon { Name = "MG 34", Range = 36f, Damage = 11f, Cooldown = 1.4f, Burst = 7, BurstInterval = 0.07f, AccNear = 0.4f, AccFar = 0.14f, Suppression = 0.025f, Tracer = true, Sound = ShotSound.MachineGun };

        public static UnitDef Get(Faction side, UnitKind kind) => side == Faction.USSR ? Ussr(kind) : Germany(kind);

        static UnitDef Ussr(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Riflemen:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.USSR, Name = "Стрелковое отделение", Short = "Стрелки", Cost = 200, BuildTime = 8f,
                        Description = "6 бойцов: винтовки Мосина и ППШ. Захватывают точки, бросают противотанковые гранаты.",
                        Soldiers = 6, Weapons = new[] { Ppsh, Mosin }, HasAtGrenades = true, Speed = 3.7f,
                    };
                case UnitKind.MachineGun:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.USSR, Name = "Пулемётный расчёт «Максим»", Short = "Пулемёт", Cost = 240, BuildTime = 10f,
                        Description = "Станковый пулемёт: прижимает пехоту к земле. Стреляет только с места.",
                        Soldiers = 3, Weapons = new[] { Maxim, Mosin }, Speed = 3.1f,
                    };
                case UnitKind.AntiTank:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.USSR, Name = "Расчёт ПТРД", Short = "ПТР", Cost = 220, BuildTime = 10f,
                        Description = "Противотанковое ружьё: бьёт технику в борт и корму. Против пехоты — винтовка.",
                        Soldiers = 2, Weapons = new[] { Ptrd, Mosin }, Speed = 3.4f,
                    };
                case UnitKind.Mortar:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.USSR, Name = "Миномётный расчёт БМ-37", Short = "Миномёт", Cost = 260, BuildTime = 12f,
                        Description = "82-мм миномёт: навесной огонь до 100 м по целям, которые видят свои. Выкуривает пехоту из укрытий. Стреляет только с места.",
                        Soldiers = 3, Weapons = new[] { Bm37, Mosin }, Speed = 3.1f,
                    };
                case UnitKind.Sniper:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.USSR, Name = "Снайпер", Short = "Снайпер", Cost = 220, BuildTime = 10f,
                        Description = "Бьёт на 70 м и почти всегда насмерть. Неподвижного почти не видно. Против танков бесполезен.",
                        Soldiers = 1, Weapons = new[] { MosinSniper }, Speed = 3.7f, Sight = 55f,
                    };
                case UnitKind.Sappers:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.USSR, Name = "Штурмовые инженеры-сапёры", Short = "Сапёры", Cost = 280, BuildTime = 10f,
                        Description = "ШИСБр: огнемёт РОКС-3 и ППШ. Выжигают пехоту из домов и окопов — укрытия от огня не спасают.",
                        Soldiers = 5, Weapons = new[] { Ppsh, Roks3, Ppsh }, SoldierHp = 75f, HasAtGrenades = true, Speed = 3.7f,
                    };
                default:
                    return new UnitDef
                    {
                        Kind = UnitKind.Tank, Side = Faction.USSR, Name = "Т-34-76", Short = "Т-34", Cost = 600, BuildTime = 20f,
                        Description = "Средний танк: быстрый, наклонная броня. 76-мм пушка и пулемёт ДТ.",
                        Vehicle = true, Hp = 650f, ArmorFront = 90f, ArmorSide = 45f, ArmorRear = 40f,
                        GunAP = F34AP, GunHE = F34HE, Coax = Dt, TurretSpeed = 34f, Speed = 6.6f, Sight = 40f,
                    };
            }
        }

        static UnitDef Germany(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Riflemen:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.Germany, Name = "Пехотное отделение", Short = "Пехота", Cost = 200, BuildTime = 8f,
                        Description = "5 солдат: карабины Kar98k и MP 40. Захватывают точки, бросают противотанковые гранаты.",
                        Soldiers = 5, Weapons = new[] { Mp40, Kar98 }, SoldierHp = 70f, HasAtGrenades = true, Speed = 3.6f,
                    };
                case UnitKind.MachineGun:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.Germany, Name = "Пулемётный расчёт MG 42", Short = "MG 42", Cost = 240, BuildTime = 10f,
                        Description = "Самый скорострельный пулемёт войны: прижимает пехоту к земле. Стреляет только с места.",
                        Soldiers = 3, Weapons = new[] { Mg42, Kar98 }, Speed = 3.3f,
                    };
                case UnitKind.AntiTank:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.Germany, Name = "Расчёт «Панцершрек»", Short = "Шрек", Cost = 240, BuildTime = 10f,
                        Description = "Реактивный гранатомёт: пробивает любой танк, но долго перезаряжается и бьёт недалеко.",
                        Soldiers = 2, Weapons = new[] { Schreck, Kar98 }, Speed = 3.4f,
                    };
                case UnitKind.Mortar:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.Germany, Name = "Миномётный расчёт GrW 34", Short = "Миномёт", Cost = 260, BuildTime = 12f,
                        Description = "8-см миномёт: навесной огонь до 100 м по целям, которые видят свои. Выкуривает пехоту из укрытий. Стреляет только с места.",
                        Soldiers = 3, Weapons = new[] { Grw34, Kar98 }, Speed = 3.2f,
                    };
                case UnitKind.Sniper:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.Germany, Name = "Снайпер (Scharfschütze)", Short = "Снайпер", Cost = 220, BuildTime = 10f,
                        Description = "Бьёт на 70 м и почти всегда насмерть. Неподвижного почти не видно. Против танков бесполезен.",
                        Soldiers = 1, Weapons = new[] { Kar98Sniper }, Speed = 3.6f, Sight = 55f,
                    };
                case UnitKind.Sappers:
                    return new UnitDef
                    {
                        Kind = kind, Side = Faction.Germany, Name = "Штурмпионеры", Short = "Пионеры", Cost = 280, BuildTime = 10f,
                        Description = "Огнемёт Flammenwerfer 41 и MP 40. Выжигают пехоту из домов и окопов — укрытия от огня не спасают.",
                        Soldiers = 4, Weapons = new[] { Mp40, Fmw41, Mp40 }, SoldierHp = 85f, HasAtGrenades = true, Speed = 3.6f,
                    };
                default:
                    return new UnitDef
                    {
                        Kind = UnitKind.Tank, Side = Faction.Germany, Name = "Pz.Kpfw. IV Ausf. H", Short = "Pz.IV", Cost = 600, BuildTime = 20f,
                        Description = "Средний танк: длинноствольная 75-мм пушка, отлично пробивает броню. Медленнее Т-34.",
                        Vehicle = true, Hp = 620f, ArmorFront = 80f, ArmorSide = 35f, ArmorRear = 25f,
                        GunAP = Kwk40AP, GunHE = Kwk40HE, Coax = Mg34, TurretSpeed = 26f, Speed = 5.6f, Sight = 40f,
                    };
            }
        }
    }
}
