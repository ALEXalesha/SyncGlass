using System.Text.RegularExpressions;
using SyncGlass.Ui;

namespace SyncGlass.Tests;

// Перенос test/wording.test.js. Ревью 23.10: «1 файлов», «2 файлов» - склонение по числу общей функцией.
public class WordingTests
{
    // Эталон по правилу языка, написанный иначе, чем Format.Plural: по цифрам записи.
    private static string Expected(long n)
    {
        var s = Math.Abs(n).ToString();
        if (s.Length >= 2 && s[^2] == '1') return "файлов";
        return s[^1] switch { '1' => "файл", '2' or '3' or '4' => "файла", _ => "файлов" };
    }

    [Fact]
    public void склонение_файл_файла_файлов_верно_на_0_1000_и_больших_числах()
    {
        var ns = Enumerable.Range(0, 1001).Select(i => (long)i).ToList();
        long seed = 7;
        for (var i = 0; i < 500; i++) { seed = seed * 48271 % 2147483647; ns.Add(seed); }
        foreach (var n in ns) Assert.Equal(Expected(n), Format.Plural(n, Format.Files[0], Format.Files[1], Format.Files[2]));
        Assert.Equal("1 файл", Format.Count(1, Format.Files));
        Assert.Equal("22 файла", Format.Count(22, Format.Files));
        Assert.Equal("111 файлов", Format.Count(111, Format.Files));
        Assert.Equal(Format.FmtNum(1021) + " объект", Format.Count(1021, Format.Objects));
    }

    [Fact]
    public void в_текстах_окна_нет_голого_числа_перед_существительным()
    {
        var dir = Path.Combine(Path.GetDirectoryName(Fixtures.Dir)!, "csharp", "src", "SyncGlass.Ui");
        var bad = Directory.EnumerateFiles(dir, "*.cs")
            .SelectMany(f => File.ReadAllLines(f).Select(l => (f, l)))
            .Where(x => Regex.IsMatch(x.l, @"\}\s+(файл|файла|файлов|объект|объекта|объектов|папок)\b"))
            .Select(x => Path.GetFileName(x.f) + ": " + x.l.Trim())
            .ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }
}
