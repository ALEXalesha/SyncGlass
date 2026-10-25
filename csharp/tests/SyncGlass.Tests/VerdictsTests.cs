using SyncGlass.Core;

namespace SyncGlass.Tests;

// Перенос test/verdicts.test.js. Алексей 25.10: после «Выполнить» SyncGlass сверял те же сотни тысяч
// перемещений второй раз, и копирование началось через полтора часа. Законы кеша вердиктов сверки.
// Кеш общий на процесс - тесты этого класса идут по одному (коллекция), ключи у всех разные (свои папки).
[Collection("verdicts")]
public class VerdictsTests
{
    private static (TempDir Src, TempDir Dst) Pair(byte[] content)
    {
        var src = new TempDir();
        var dst = new TempDir();
        src.WriteBytes("new.bin", content);
        dst.WriteBytes("old.bin", content);
        return (src, dst);
    }

    private static SyncPlan PlanOf(long size, double mt = 1000)
    {
        var added = new FileEntry("new.bin", size, mt);
        var gone = new FileEntry("old.bin", size, mt);
        return new SyncPlan { Moves = { new Move("old.bin", "new.bin", "new.bin", size, gone, added) } };
    }

    [Fact]
    public async Task вердикт_из_кеша_второй_план_не_читает_файлы_заново()
    {
        Plan.ClearMoveVerdicts();
        var (src, dst) = Pair(Enumerable.Repeat((byte)7, 300000).ToArray());
        using var _s = src; using var _d = dst;
        var p1 = PlanOf(300000);
        await Plan.ConfirmMoves(src.Root, dst.Root, p1);
        Assert.Single(p1.Moves);
        // файл источника пропал: живая сверка сказала бы «нет», а вердикт из кеша - прежний
        File.Delete(src.P("new.bin"));
        var p2 = PlanOf(300000);
        var steps = new List<(int, int)>();
        await Plan.ConfirmMoves(src.Root, dst.Root, p2, (d, t) => steps.Add((d, t)));
        Assert.Single(p2.Moves);
        Assert.Equal((1, 1), steps[0]);              // прогресс сразу показывает всё сверенным
        // сброс кеша (как при «Обновить») - снова живая сверка
        Plan.ClearMoveVerdicts();
        var p3 = PlanOf(300000);
        await Plan.ConfirmMoves(src.Root, dst.Root, p3);
        Assert.Empty(p3.Moves);
        Assert.Single(p3.Copy);
        Assert.Single(p3.Trash);
    }

    [Fact]
    public async Task другая_дата_в_скане_новый_ключ_просроченный_вердикт_не_используется()
    {
        Plan.ClearMoveVerdicts();
        var same = System.Text.Encoding.UTF8.GetBytes("одинаково");
        var (src, dst) = Pair(same);
        using var _s = src; using var _d = dst;
        await Plan.ConfirmMoves(src.Root, dst.Root, PlanOf(same.Length, 1000));
        src.WriteBytes("new.bin", System.Text.Encoding.UTF8.GetBytes("ИНАЧЕ!!!!"));
        var changed = PlanOf(same.Length, 2000);           // скан увидел новую дату - вердикт старой пары не подходит
        await Plan.ConfirmMoves(src.Root, dst.Root, changed);
        Assert.Empty(changed.Moves);

        // тот же ключ, но вердикт старше срока - сверка заново
        var t = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        Plan.ClearMoveVerdicts();
        src.WriteBytes("new.bin", same);
        await Plan.ConfirmMoves(src.Root, dst.Root, PlanOf(same.Length, 3000), null, () => t);
        File.Delete(src.P("new.bin"));
        var fresh = PlanOf(same.Length, 3000);
        await Plan.ConfirmMoves(src.Root, dst.Root, fresh, null, () => t + Plan.VerdictTtl - TimeSpan.FromSeconds(1));
        Assert.Single(fresh.Moves);                        // ещё в сроке - из кеша
        var late = PlanOf(same.Length, 3000);
        await Plan.ConfirmMoves(src.Root, dst.Root, late, null, () => t + Plan.VerdictTtl + TimeSpan.FromSeconds(1));
        Assert.Empty(late.Moves);                          // просрочен - сверено заново, файла нет
    }

    [Fact]
    public void сверка_идёт_по_128_разом_как_в_plan_js()
    {
        Assert.Equal(128, Plan.ConfirmConcurrency);
    }
}
