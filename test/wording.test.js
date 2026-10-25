'use strict';

// Ревью 23.10 (Claude, установленная 1.2.4): «1 файлов», «2 файлов»; путь сети в шапке «ПК\Work\\»
// вместо «\\ПК\Work»; системная строка меню Electron по-английски. Законы - здесь.

const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { loadRenderer } = require('./helpers/renderer-harness');

const api = loadRenderer();

// Эталон по правилу русского языка, написанный иначе, чем plural: по последней цифре и «-надцать».
function expected(n) {
  const s = String(Math.abs(n));
  if (s.length >= 2 && s[s.length - 2] === '1') return 'файлов';
  const last = s[s.length - 1];
  if (last === '1') return 'файл';
  if (last === '2' || last === '3' || last === '4') return 'файла';
  return 'файлов';
}

test('склонение «файл / файла / файлов» верно на 0..1000 и на больших числах', () => {
  const ns = [];
  for (let n = 0; n <= 1000; n += 1) ns.push(n);
  let seed = 7;
  for (let i = 0; i < 500; i += 1) { seed = (seed * 48271) % 2147483647; ns.push(seed); }
  for (const n of ns) assert.strictEqual(api.plural(n, ...api.FILES), expected(n), String(n));
  assert.strictEqual(api.count(1, api.FILES), '1 файл');
  assert.strictEqual(api.count(22, api.FILES), '22 файла');
  assert.strictEqual(api.count(111, api.FILES), '111 файлов');
  assert.strictEqual(api.count(1021, api.OBJECTS), `${(1021).toLocaleString('ru-RU')} объект`);
});

test('путь в шапке панели не теряет порядок символов при rtl: метки слева направо по краям', () => {
  for (const p of ['\\\\ALEXALOYSHA\\Work', 'C:\\Work', '\\\\nas\\share\\', 'D:\\']) {
    const t = api.pathLabel(p);
    assert.strictEqual(t, `\u200E${p}\u200E`, p);
    assert.strictEqual(t.replace(/\u200E/g, ''), p, 'кроме меток - ровно путь');
  }
  assert.strictEqual(api.pathLabel(''), 'путь не выбран');
});

test('в тексте окна нет «N файлов» без склонения: числа с существительным идут через count()', () => {
  const src = fs.readFileSync(path.join(__dirname, '..', 'renderer', 'renderer.js'), 'utf8');
  const bad = src.split('\n').filter((l) => /\}\s+(файл|файла|файлов|объект|объекта|объектов|папок)\b/.test(l));
  assert.deepStrictEqual(bad, [], 'голое число перед существительным');
});

test('main.js убирает системную строку меню Electron до создания окна', () => {
  const src = fs.readFileSync(path.join(__dirname, '..', 'main.js'), 'utf8');
  const menu = src.indexOf('Menu.setApplicationMenu(null)');
  assert.ok(menu > 0, 'Menu.setApplicationMenu(null) есть');
  assert.ok(menu < src.indexOf('new BrowserWindow('), 'и стоит до создания окна');
});
