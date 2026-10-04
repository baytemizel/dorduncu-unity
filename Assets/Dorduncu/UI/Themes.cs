// Table themes (same catalog as the original client): one free, five premium (premium subscription or one-time purchase).
namespace Dorduncu.UI
{
    public sealed class Theme
    {
        public string Id, Name, Table, Tea;
        public bool Free;
    }

    public static class Themes
    {
        public static readonly Theme[] All =
        {
            new Theme { Id = "kahvehane", Name = "Kahvehane Kilimi", Table = "table1", Tea = "tea_kahvehane", Free = true },
            new Theme { Id = "ev", Name = "Ev Akşamı", Table = "table2", Tea = "tea_ev" },
            new Theme { Id = "cinar", Name = "Çınar Altı", Table = "table3", Tea = "tea_cinar" },
            new Theme { Id = "bogaz", Name = "Boğaz Terası", Table = "table4", Tea = "tea_bogaz" },
            new Theme { Id = "kis", Name = "Kış Köşkü", Table = "table5", Tea = "tea_kis" },
            new Theme { Id = "ege", Name = "Ege Kıyısı", Table = "table6", Tea = "tea_ege" },
        };
    }
}
