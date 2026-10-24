'use strict';

// «Повисло на 1 030 009» (Алексей, 24.10.2026). Предпросмотр дерева после переноса
// папок: скан закончился, а дальше сверка кандидатов в перемещения - по два чтения на
// пару, на приёмнике по сети - шла молча десятки минут, счётчик стоял, отмена не
// доходила. Проба на 200 тысячах перемещений: 231 с тишины. Плюс миллион файлов
// node_modules/.venv/кешей, которые синхронизировать незачем.
//
// Законы:
// 1. Сверка перемещений говорит о себе: фаза 'moves' с done/total, последняя - done = total,
//    промежутки между сообщениями короткие при любом числе пар; итог скана досылается.
// 2. Отмена предпросмотра доходит и до сверки: ответ { aborted }, сверка не дочитывает все пары.
// 3. «Без мусора»: мусорные папки не видит ни одна дорога (живой скан, индекс обхода, дерево),
//    на приёмнике они не удаляются; план с мусором и галочкой = план того же дерева без мусора;
//    без галочки мусор синхронизируется как обычные папки.

const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const fsp = fs.promises;
const path = require('node:path');

const { loadMain, tmpDir, writeFile } = require('./helpers/main-harness');

const ready = loadMain();

// Дерево «перенесли папку»: на приёмнике файлы в Старое/, на источнике те же файлы в Новое/.
// Даты одинаковые - иначе это не кандидаты в перемещения.
async function movedTree(n) {
  const local = await tmpDir();
  const network = await tmpDir();
  const t = new Date('2026-01-02T03:04:05Z');
  for (let i = 0; i < n; i++) {
    const body = `файл ${i} ` + 'x'.repeat(i % 50);
    await writeFile(network, `Старое/d${i % 7}/f${i}.txt`, body);
    await writeFile(local, `Новое/d${i % 7}/f${i}.txt`, body);
    await fsp.utimes(path.join(network, `Старое/d${i % 7}/f${i}.txt`), t, t);
    await fsp.utimes(path.join(local, `Новое/d${i % 7}/f${i}.txt`), t, t);
  }
  return { local, network };
}

// Медленная сеть: каждое открытие файла на сверку ждёт ms.
function slowOpen(ms) {
  const real = fsp.open;
  let opened = 0;
  fsp.open = async (...a) => {
    opened += 1;
    await new Promise((r) => setTimeout(r, ms));
    return real(...a);
  };
  return { restore: () => { fsp.open = real; }, count: () => opened };
}

test('сверка перемещений шлёт прогресс до done = total, без долгой тишины', async () => {
  const { call, sent } = await ready;
  const N = 400;
  const { local, network } = await movedTree(N);
  const from = sent.length;
  const stamps = [];
  const realPush = sent.push.bind(sent);
  sent.push = (m) => { if (m.ch === 'preview-progress') stamps.push(Date.now()); return realPush(m); };
  const slow = slowOpen(20);
  let r;
  try {
    r = await call('preview', { localPath: local, networkPath: network, folders: ['Новое', 'Старое'], excludes: [], direction: 'toNetwork' });
  } finally {
    slow.restore();
    sent.push = realPush;
  }
  assert.strictEqual(r.totals.move, N, 'все пары - перемещения');
  const moves = sent.slice(from).filter((m) => m.ch === 'preview-progress' && m.payload.phase === 'moves');
  assert.ok(moves.length >= 2, 'у сверки есть свой прогресс');
  const last = moves[moves.length - 1].payload;
  assert.strictEqual(last.done, N);
  assert.strictEqual(last.total, N);
  assert.strictEqual(last.scanned, 2 * N, 'итог скана досылается целиком');
  let gap = 0;
  for (let i = 1; i < stamps.length; i++) gap = Math.max(gap, stamps[i] - stamps[i - 1]);
  // 400 пар x 2 открытия x 20 мс при 16 одновременно - 1 с тишины в старом коде.
  assert.ok(gap < 600, `тишина между сообщениями ${gap} мс`);
});

test('отмена предпросмотра останавливает и сверку перемещений', async () => {
  const { call, sent } = await ready;
  const N = 400;
  const { local, network } = await movedTree(N);
  const realPush = sent.push.bind(sent);
  let cancelled = false;
  sent.push = (m) => {
    if (!cancelled && m.ch === 'preview-progress' && m.payload.phase === 'moves') {
      cancelled = true;
      call('cancel-preview');
    }
    return realPush(m);
  };
  const slow = slowOpen(20);
  let r;
  try {
    r = await call('preview', { localPath: local, networkPath: network, folders: ['Новое', 'Старое'], excludes: [], direction: 'toNetwork' });
  } finally {
    slow.restore();
    sent.push = realPush;
  }
  assert.ok(cancelled, 'до сверки дошли');
  assert.deepStrictEqual(r, { aborted: true });
  assert.ok(slow.count() < N, `после отмены сверка не дочитала все пары (${slow.count()} открытий)`);
});

// ---- «Без мусора» ----

const JUNK = ['node_modules', '.venv', '__pycache__', '.pytest_cache', '.hypothesis', '.ruff_cache', '.mypy_cache'];

// Псевдослучайное дерево с зерном: обычные файлы и мусорные папки на разной глубине
// на обеих сторонах (на приёмнике - свой мусор, которого нет на источнике).
function rng(seed) {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 2 ** 32);
}
async function junkTree(seed) {
  const r = rng(seed);
  const local = await tmpDir();
  const network = await tmpDir();
  const clean = { local: await tmpDir(), network: await tmpDir() };
  for (const [side, root] of [['local', local], ['network', network]]) {
    for (let i = 0; i < 25; i++) {
      const depth = 1 + Math.floor(r() * 3);
      const parts = ['проект'];
      for (let d = 1; d < depth; d++) parts.push(`п${Math.floor(r() * 3)}`);
      const junk = r() < 0.4;
      // Чистое дерево - то же дерево без мусорных папок, но с их родителями:
      // папка, в которой лежал только node_modules, на диске всё равно есть.
      if (junk) await fsp.mkdir(path.join(clean[side], ...parts), { recursive: true });
      if (junk) parts.push(JUNK[Math.floor(r() * JUNK.length)], `глубже${Math.floor(r() * 2)}`);
      const rel = [...parts, `${side}-${i}-${Math.floor(r() * 4)}.txt`].join('/');
      const body = `${side}${i}`;
      await writeFile(root, rel, body);
      if (!junk) await writeFile(clean[side], rel, body);
    }
  }
  return { local, network, clean };
}

const args = (local, network) => ({ localPath: local, networkPath: network, folders: ['проект'], excludes: [], direction: 'toNetwork' });

test('без мусора: план с мусорными папками равен плану того же дерева без них (обе дороги)', async () => {
  const { call } = await ready;
  await call('set-skip-junk', true);
  for (let seed = 1; seed <= 12; seed++) {
    const t = await junkTree(seed);
    const ожидаем = await call('preview', args(t.clean.local, t.clean.network));
    const живой = await call('preview', args(t.local, t.network));
    assert.deepStrictEqual(живой.totals, ожидаем.totals, `зерно ${seed}: живой скан видит мусор`);
    await call('start-crawl', { localPath: t.local, networkPath: t.network, noLimit: true });
    const поИндексу = await call('preview', args(t.local, t.network));
    assert.deepStrictEqual(поИндексу.totals, ожидаем.totals, `зерно ${seed}: индекс обхода видит мусор`);
  }
});

test('без мусора: дерево его не показывает, синхронизация не трогает мусор приёмника', async () => {
  const { call } = await ready;
  await call('set-skip-junk', true);
  const local = await tmpDir();
  const network = await tmpDir();
  await writeFile(local, 'проект/main.js', 'код');
  await writeFile(local, 'проект/node_modules/pkg/index.js', 'зависимость');
  await writeFile(network, 'проект/.venv/Lib/site.py', 'своё окружение ноутбука');
  const list = await call('list-folders', { localPath: local, networkPath: network, relPath: 'проект' });
  assert.ok(!list.items.some((i) => JUNK.includes(i.name.toLowerCase())), 'мусор в дереве не показывается');
  const res = await call('sync', args(local, network));
  assert.ok(!res.error, res.error);
  assert.ok(fs.existsSync(path.join(network, 'проект/main.js')), 'обычный файл скопирован');
  assert.ok(!fs.existsSync(path.join(network, 'проект/node_modules')), 'мусор источника не скопирован');
  assert.ok(fs.existsSync(path.join(network, 'проект/.venv/Lib/site.py')), 'мусор приёмника не удалён');
});

test('с выключенной галочкой мусор синхронизируется как обычные папки', async () => {
  const { call } = await ready;
  const local = await tmpDir();
  const network = await tmpDir();
  await writeFile(local, 'проект/node_modules/pkg/index.js', 'зависимость');
  await writeFile(network, 'проект/__pycache__/old.pyc', 'старое');
  // Сначала с галочкой: сканы этого дерева ложатся в кеш. Переключение обязано его
  // сбросить, иначе план «со всем» строился бы по скану «без мусора».
  const без = await call('preview', args(local, network));
  assert.strictEqual(без.totals.total, 0);
  await call('set-skip-junk', false);
  try {
    const p = await call('preview', args(local, network));
    assert.strictEqual(p.totals.copy, 1, 'node_modules копируется');
    assert.strictEqual(p.totals.trash, 1, 'лишний __pycache__ приёмника удаляется');
    const list = await call('list-folders', { localPath: local, networkPath: network, relPath: 'проект' });
    assert.ok(list.items.some((i) => i.name === 'node_modules'), 'и показывается в дереве');
  } finally {
    await call('set-skip-junk', true);
  }
});
