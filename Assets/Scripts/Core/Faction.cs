using UnityEngine;

namespace RealPeace
{
    public enum Faction { USSR, Germany }

    /// <summary>Всё, что отличает стороны внешне: названия, цвета формы и техники, цвета интерфейса.</summary>
    public static class Factions
    {
        public static Faction Other(this Faction f) => f == Faction.USSR ? Faction.Germany : Faction.USSR;

        public static string Name(Faction f) => f == Faction.USSR ? "СССР" : "Германия";
        public static string ArmyName(Faction f) => f == Faction.USSR ? "Красная армия" : "Вермахт";

        /// <summary>СССР наступает с юга (−Z), Германия — с севера (+Z).</summary>
        public static float NorthSign(Faction f) => f == Faction.USSR ? -1f : 1f;

        // Форма: советская — хаки (гимнастёрка), немецкая — полевой серый (фельдграу)
        public static Color Tunic(Faction f) => f == Faction.USSR ? Shapes.Hex("#7d7448") : Shapes.Hex("#5d6253");
        public static Color Trousers(Faction f) => f == Faction.USSR ? Shapes.Hex("#6b6440") : Shapes.Hex("#4f5347");
        public static Color Helmet(Faction f) => f == Faction.USSR ? Shapes.Hex("#4e5a34") : Shapes.Hex("#4a4d48");
        public static Color Boots(Faction f) => f == Faction.USSR ? Shapes.Hex("#2b2219") : Shapes.Hex("#1d1a17");
        public static Color Gear(Faction f) => f == Faction.USSR ? Shapes.Hex("#5a4a30") : Shapes.Hex("#2a2622");

        // Техника: советский защитный зелёный (4БО) и немецкий тёмно-жёлтый (Dunkelgelb)
        public static Color TankPaint(Faction f) => f == Faction.USSR ? Shapes.Hex("#4c5534") : Shapes.Hex("#a08c5b");
        public static Color TankPaintDark(Faction f) => f == Faction.USSR ? Shapes.Hex("#3b4824") : Shapes.Hex("#6e6a45");

        /// <summary>Цвет стороны в интерфейсе (флажки, полоски, точки на миникарте).</summary>
        public static Color Ui(Faction f) => f == Faction.USSR ? Shapes.Hex("#d23a2e") : Shapes.Hex("#8a9aa8");
    }
}
