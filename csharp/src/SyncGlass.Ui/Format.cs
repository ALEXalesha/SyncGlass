using System.Globalization;

namespace SyncGlass.Ui;

/// <summary>Утилиты вывода из renderer.js: размеры, числа с разрядами, даты.</summary>
public static class Format
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ", "ПБ"];

    // Без ограничения сверху шкала кончалась бы раньше числа («2 undefined» у JS).
    // Дробная часть - с точкой, как toFixed.
    public static string FormatSize(long? bytes)
    {
        if (bytes is not { } b) return "";
        if (b == 0) return "0 Б";
        var i = Math.Min(Units.Length - 1, (int)Math.Floor(Math.Log(b) / Math.Log(1024)));
        var v = b / Math.Pow(1024, i);
        return v.ToString(v < 10 && i > 0 ? "F1" : "F0", CultureInfo.InvariantCulture) + " " + Units[i];
    }

    // Разряды как у toLocaleString('ru-RU'): неразрывный пробел.
    public static string FmtNum(long n) => n.ToString("N0", Ru);

    // Склонение по числу, как plural в renderer.js (ревью 23.10: «1 файлов», «2 файлов»).
    public static string Plural(long n, string one, string few, string many)
    {
        var a = Math.Abs(n) % 100;
        var b = a % 10;
        if (a > 10 && a < 20) return many;
        if (b == 1) return one;
        return b is >= 2 and <= 4 ? few : many;
    }

    public static readonly string[] Files = ["файл", "файла", "файлов"];
    public static readonly string[] Objects = ["объект", "объекта", "объектов"];

    // «1 файл», «2 файла», «5 файлов» - число с разрядами и слово в нужной форме.
    public static string Count(long n, string[] forms) => $"{FmtNum(n)} {Plural(n, forms[0], forms[1], forms[2])}";

    // Счётчик в плашке предпросмотра: плашка узкая, четыре в ряд, а число бывает
    // шестизначным - шрифт мельчает по длине записи (классы как в styles.css).
    public static string NumSize(string text) => text.Length switch
    {
        > 11 => "num-xs",
        > 7 => "num-s",
        > 5 => "num-m",
        _ => "",
    };

    // Дата записи истории: как new Date(iso).toLocaleString('ru-RU').
    public static string FmtDateTime(string? iso)
    {
        if (iso != null && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t))
            return t.ToLocalTime().ToString("dd.MM.yyyy, HH:mm:ss", CultureInfo.InvariantCulture);
        return iso ?? "";
    }
}
