using SyncGlass.Core;
using SyncGlass.Core.Main;

namespace SyncGlass.Tests;

// Перенос test/million.test.js (24.10, «зависло на 1 030 009»): сверка перемещений говорит
// о себе и отменяется; «Без мусора» - мусор не видит ни одна дорога, на приёмнике он цел,
// план с мусором и галочкой равен плану того же дерева без него; без галочки - как раньше.
public sealed class MillionTests : IDisposable
{
    private readonly TempDir _userData = new();
    private readonly Backend _b;

    public MillionTests() => _b = new Backend(_userData.Root);

    public void Dispose()
    {
        _b.SetSkipJunk(true);
        _userData.Dispose();
    }

    // Progress<T> шлёт в пул потоков и может прийти после ответа - здесь нужен сразу.
    private sealed class Now<T>(Action<T> on) : IProgress<T>
    {
        public void Report(T value) => on(value);
    }

    private static SyncArgs Args(TempDir local, TempDir network, params string[] folders)
        => new(local.Root, network.Root, folders, [], "toNetwork");

    // Перенесли папку: на приёмнике файлы в Старое/, на источнике те же в Новое/, даты те же.
    private static void MovedTree(TempDir local, TempDir network, int n)
    {
        var t = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        for (var i = 0; i < n; i++)
        {
            var body = $"файл {i} " + new string('x', i % 50);
            network.Write($"Старое/d{i % 7}/f{i}.txt", body, t);
            local.Write($"Новое/d{i % 7}/f{i}.txt", body, t);
        }
    }

    [Fact]
    public async Task сверка_перемещений_шлёт_прогресс_до_done_равно_total_и_досылает_итог_скана()
    {
        using var local = new TempDir();
        using var network = new TempDir();
        const int n = 300;
        MovedTree(local, network, n);
        var scans = new List<int>();
        var moves = new List<(int Done, int Total)>();
        var r = await _b.Preview(Args(local, network, "Новое", "Старое"),
            new Now<int>(s => { lock (scans) scans.Add(s); }), new Now<(int, int)>(m => { lock (moves) moves.Add(m); }));
        Assert.Equal(n, r.Totals!.Move);
        Assert.True(moves.Count >= 2, "у сверки есть свой прогресс");
        Assert.Equal((n, n), moves[^1]);
        Assert.Equal(2 * n, scans[^1]); // итог скана досылается целиком
    }

    [Fact]
    public async Task отмена_предпросмотра_останавливает_и_сверку_перемещений()
    {
        using var local = new TempDir();
        using var network = new TempDir();
        MovedTree(local, network, 300);
        var cancelled = false;
        var r = await _b.Preview(Args(local, network, "Новое", "Старое"), null,
            new Now<(int, int)>(_ => { if (!cancelled) { cancelled = true; _b.CancelPreview(); } }));
        Assert.True(cancelled, "до сверки дошли");
        Assert.True(r.Aborted);
    }

    private static readonly string[] Junk = ["node_modules", ".venv", "__pycache__", ".pytest_cache", ".hypothesis", ".ruff_cache", ".mypy_cache"];

    // Псевдослучайное дерево с мусором на обеих сторонах и его же копия без мусорных папок
    // (родители мусора остаются: папка, где лежал только node_modules, на диске есть).
    private static void JunkTree(int seed, TempDir local, TempDir network, TempDir cleanLocal, TempDir cleanNetwork)
    {
        var rnd = new Random(seed);
        foreach (var (side, root, clean) in new[] { ("local", local, cleanLocal), ("network", network, cleanNetwork) })
            for (var i = 0; i < 25; i++)
            {
                var parts = new List<string> { "проект" };
                var depth = 1 + rnd.Next(3);
                for (var d = 1; d < depth; d++) parts.Add($"п{rnd.Next(3)}");
                var junk = rnd.NextDouble() < 0.4;
                if (junk)
                {
                    clean.Mkdir(string.Join('/', parts));
                    parts.Add(Junk[rnd.Next(Junk.Length)]);
                    parts.Add($"глубже{rnd.Next(2)}");
                }
                var rel = string.Join('/', parts.Append($"{side}-{i}-{rnd.Next(4)}.txt"));
                root.Write(rel, $"{side}{i}");
                if (!junk) clean.Write(rel, $"{side}{i}");
            }
    }

    [Fact]
    public async Task без_мусора_план_с_мусорными_папками_равен_плану_дерева_без_них_на_обеих_дорогах()
    {
        _b.SetSkipJunk(true);
        for (var seed = 1; seed <= 8; seed++)
        {
            using var local = new TempDir();
            using var network = new TempDir();
            using var cl = new TempDir();
            using var cn = new TempDir();
            JunkTree(seed, local, network, cl, cn);
            var ожидаем = await _b.Preview(Args(cl, cn, "проект"));
            var живой = await _b.Preview(Args(local, network, "проект"));
            Assert.True(ожидаем.Totals == живой.Totals, $"зерно {seed}: живой скан видит мусор");
            await _b.StartCrawl(local.Root, network.Root, true, false, new RecordingSink());
            var поИндексу = await _b.Preview(Args(local, network, "проект"));
            Assert.True(ожидаем.Totals == поИндексу.Totals, $"зерно {seed}: индекс обхода видит мусор");
        }
    }

    [Fact]
    public async Task без_мусора_дерево_его_не_показывает_а_синхронизация_не_трогает_мусор_приёмника()
    {
        _b.SetSkipJunk(true);
        using var local = new TempDir();
        using var network = new TempDir();
        local.Write("проект/main.js", "код");
        local.Write("проект/node_modules/pkg/index.js", "зависимость");
        network.Write("проект/.venv/Lib/site.py", "своё окружение ноутбука");
        var list = await _b.ListFolders(local.Root, network.Root, "проект", true, false);
        Assert.DoesNotContain(list.Items, i => Junk.Contains(i.Name, StringComparer.OrdinalIgnoreCase));
        var res = await _b.Sync(Args(local, network, "проект"));
        Assert.Null(res.Error);
        Assert.True(File.Exists(network.P("проект/main.js")));
        Assert.False(Directory.Exists(network.P("проект/node_modules")), "мусор источника не скопирован");
        Assert.True(File.Exists(network.P("проект/.venv/Lib/site.py")), "мусор приёмника не удалён");
    }

    [Fact]
    public async Task с_выключенной_галочкой_мусор_синхронизируется_как_обычные_папки()
    {
        using var local = new TempDir();
        using var network = new TempDir();
        local.Write("проект/node_modules/pkg/index.js", "зависимость");
        network.Write("проект/__pycache__/old.pyc", "старое");
        _b.SetSkipJunk(true);
        // Сначала с галочкой: сканы ложатся в кеш, переключение обязано его сбросить.
        Assert.Equal(0, (await _b.Preview(Args(local, network, "проект"))).Totals!.Total);
        _b.SetSkipJunk(false);
        var p = await _b.Preview(Args(local, network, "проект"));
        Assert.Equal(1, p.Totals!.Copy);
        Assert.Equal(1, p.Totals.Trash);
        var list = await _b.ListFolders(local.Root, network.Root, "проект", true, false);
        Assert.Contains(list.Items, i => i.Name == "node_modules");
    }
}
