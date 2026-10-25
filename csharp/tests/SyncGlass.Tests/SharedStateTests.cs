using System.Text.RegularExpressions;

namespace SyncGlass.Tests;

// Ревизия GitHub 25.10: CI 1.3.0 краснел на ContractTests, потому что MillionTests снимал общую на процесс
// галочку «Без мусора» (static FsOps), а xUnit гонял оба класса параллельно. Закон: тестовый файл, который
// снимает галочку, держит свои классы в коллекции с DisableParallelization = true.
public class SharedStateTests
{
    private static readonly string TestsDir = Path.Combine(
        Path.GetDirectoryName(Fixtures.Dir)!, "csharp", "tests", "SyncGlass.Tests");

    private static HashSet<string> SerialCollections(IEnumerable<string> sources) =>
        sources.SelectMany(s => Regex.Matches(s,
                @"\[CollectionDefinition\(""([^""]+)"",\s*DisableParallelization\s*=\s*true\)\]")
            .Select(m => m.Groups[1].Value))
            .ToHashSet();

    [Fact]
    public void снимающие_галочку_без_мусора_идут_без_параллельности()
    {
        var files = Directory.GetFiles(TestsDir, "*.cs")
            .Where(f => Path.GetFileName(f) != "SharedStateTests.cs") // здесь вызов только в примере
            .ToArray();
        var sources = files.ToDictionary(f => f, File.ReadAllText);
        var serial = SerialCollections(sources.Values);
        var touching = sources.Where(kv => kv.Value.Contains("SetSkipJunk(false)")).ToList();
        Assert.NotEmpty(touching); // закон не должен молча проверять пустоту

        foreach (var (file, src) in touching)
        {
            var classes = Regex.Matches(src, @"(?:\[Collection\(""([^""]+)""\)\]\s*)?public\s+(?:sealed\s+)?class\s+(\w+)");
            foreach (Match c in classes)
            {
                if (c.Groups[2].Value.EndsWith("Collection")) continue; // сами определения коллекций
                Assert.True(c.Groups[1].Success && serial.Contains(c.Groups[1].Value),
                    $"{Path.GetFileName(file)}: класс {c.Groups[2].Value} снимает общую галочку FsOps, " +
                    "но идёт параллельно с другими тестами");
            }
        }
    }

    [Fact]
    public void закон_ловит_класс_без_коллекции()
    {
        const string bad = "public sealed class X { void T() { b.SetSkipJunk(false); } }";
        var classes = Regex.Matches(bad, @"(?:\[Collection\(""([^""]+)""\)\]\s*)?public\s+(?:sealed\s+)?class\s+(\w+)");
        Assert.Single(classes);
        Assert.False(classes[0].Groups[1].Success);
    }
}
