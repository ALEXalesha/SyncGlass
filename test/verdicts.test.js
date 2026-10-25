'use strict';

// Алексей 25.10: после «Выполнить» SyncGlass сверял те же сотни тысяч перемещений второй раз,
// и копирование началось через полтора часа. Законы кеша вердиктов сверки (src/plan.js).

const { test } = require('node:test');
const assert = require('node:assert');
const fsp = require('node:fs').promises;
const os = require('node:os');
const path = require('node:path');

const { confirmMoves, clearMoveVerdicts } = require('../src/plan');

async function pair(content) {
  const src = await fsp.mkdtemp(path.join(os.tmpdir(), 'sg-verdict-src-'));
  const dst = await fsp.mkdtemp(path.join(os.tmpdir(), 'sg-verdict-dst-'));
  await fsp.writeFile(path.join(src, 'new.bin'), content);
  await fsp.writeFile(path.join(dst, 'old.bin'), content);
  return { src, dst };
}
const entry = (p, size, mtimeMs) => ({ path: p, size, mtimeMs });
function planOf(size, mt = 1000) {
  const added = entry('new.bin', size, mt), gone = entry('old.bin', size, mt);
  return { moves: [{ from: 'old.bin', to: 'new.bin', path: 'new.bin', size, gone, added }], copy: [], trash: [] };
}

test('вердикт сверки берётся из кеша: второй план из тех же данных не читает файлы заново', async () => {
  clearMoveVerdicts();
  const { src, dst } = await pair(Buffer.alloc(300000, 7));
  const steps = [];
  const p1 = planOf(300000);
  await confirmMoves(src, dst, p1, (d, t) => steps.push([d, t]));
  assert.strictEqual(p1.moves.length, 1, 'одинаковые файлы - перемещение');
  // файл источника пропал: живая сверка сказала бы «нет», а вердикт из кеша - прежний
  await fsp.rm(path.join(src, 'new.bin'));
  const p2 = planOf(300000), steps2 = [];
  await confirmMoves(src, dst, p2, (d, t) => steps2.push([d, t]));
  assert.strictEqual(p2.moves.length, 1, 'второй раз - без чтения файлов');
  assert.deepStrictEqual(steps2[0], [1, 1], 'прогресс сразу показывает всё сверенным');
  // сброс кеша (как при «Обновить») - снова живая сверка
  clearMoveVerdicts();
  const p3 = planOf(300000);
  await confirmMoves(src, dst, p3);
  assert.strictEqual(p3.moves.length, 0, 'после сброса - читает и видит пропажу');
  assert.strictEqual(p3.copy.length, 1); assert.strictEqual(p3.trash.length, 1);
});

test('другие размер или дата в скане - новый ключ и новая сверка; просроченный вердикт не используется', async () => {
  clearMoveVerdicts();
  const { src, dst } = await pair(Buffer.from('одинаково'));
  const n = Buffer.byteLength('одинаково');
  await confirmMoves(src, dst, planOf(n, 1000));
  await fsp.writeFile(path.join(src, 'new.bin'), Buffer.from('ИНАЧЕ!!!!'));
  const changed = planOf(n, 2000);                       // скан увидел новую дату - вердикт старой пары не подходит
  await confirmMoves(src, dst, changed);
  assert.strictEqual(changed.moves.length, 0, 'изменённый файл сверен заново');
  // тот же ключ, но вердикт старше срока - сверка заново
  let t = 0;
  const clock = () => t;
  clearMoveVerdicts();
  await fsp.writeFile(path.join(src, 'new.bin'), Buffer.from('одинаково'));
  await confirmMoves(src, dst, planOf(n, 3000), null, clock);
  await fsp.rm(path.join(src, 'new.bin'));
  t = 15 * 60 * 1000 + 1;
  const late = planOf(n, 3000);
  await confirmMoves(src, dst, late, null, clock);
  assert.strictEqual(late.moves.length, 0, 'просроченный вердикт - живая сверка');
});
