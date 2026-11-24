'use strict';
// Gera os dados do analyzer a partir da pasta do jogo (rodar de novo quando o jogo atualizar):
//   app/dados/itens.tsv     nome_pt <TAB> id <TAB> categoria <TAB> nome_en
//   app/dados/monstros.tsv  nome <TAB> lookId
//   app/icones/itens/<id>.png e app/icones/monstros/<lookId>.png
// Uso: node gerar-dados.js "<pasta do BloodStone>"
const fs = require('fs');
const path = require('path');
const ESTUDO = path.join(__dirname, '..', '..', 'bloodstone-study');
const lib = require(path.join(ESTUDO, 'research', 'tools', 'outfit-lib.js'));
const png = require(path.join(ESTUDO, 'research', 'tools', 'png.js'));
const { inflateFrom } = require(path.join(ESTUDO, 'tools', 'lib.js'));

const JOGO = process.argv[2];
if (!JOGO) { console.error('uso: node gerar-dados.js "<pasta do BloodStone>"'); process.exit(1); }
const GD = path.join(JOGO, 'gamedata');
const APP = path.join(__dirname, '..', 'app');
const DIR_IT = path.join(APP, 'icones', 'itens'), DIR_MO = path.join(APP, 'icones', 'monstros');
fs.mkdirSync(DIR_IT, { recursive: true }); fs.mkdirSync(DIR_MO, { recursive: true }); fs.mkdirSync(path.join(APP, 'dados'), { recursive: true });

// ---- textos (bloodstone.msg): blocos "id\npt\nen" ----
const msgs = new Map();
for (const blk of inflateFrom(fs.readFileSync(path.join(GD, 'bloodstone.msg'))).data.toString('utf8').split(/\n\s*\n/)) {
  const l = blk.split('\n'); const id = Number(l[0].trim()); if (!Number.isInteger(id) || l.length < 2) continue;
  const v = l.slice(1).filter(x => x.length); msgs.set(id, { pt: (v[0] || '').trim(), en: (v[1] || '').trim() });
}

// ---- itens (.dat): nome = msg id na posição 44; sprites do 1º quadro no fim do registro ----
const D = lib.loadDat(path.join(GD, 'bloodstone.dat'));
const S = lib.openSpr(path.join(GD, 'bloodstone.spr'));
function spritesDoRegistro(rec) {
  const L = rec[9], A = rec[10], cel = L * A; if (!cel || L > 4 || A > 4) return null;
  const tam = 4 + 4 * cel;
  for (let F = 1; F <= 64; F++) {
    const ini = rec.length - F * tam; if (ini - 1 < 48) break;
    if (rec[ini - 1] !== F) continue;
    const ids = []; for (let k = 0; k < cel; k++) ids.push(rec.readUInt32BE(ini + 4 + k * 4));
    if (ids.every(v => v < 900000) && ids.some(v => v)) return { L, A, ids };
  }
  return null;
}
function montar(s) {
  const w = s.L * 64, h = s.A * 64, c = Buffer.alloc(w * h * 4);
  s.ids.forEach((sid, k) => { if (!sid) return; const im = lib.sprRgba(S, sid); if (im) png.blit(c, w, h, im.rgba, im.w, im.h, (k % s.L) * 64, (k / s.L | 0) * 64); });
  return { w, h, rgba: c };
}
function visiveis(im) { let n = 0; for (let i = 3; i < im.rgba.length; i += 4) if (im.rgba[i] >= 128) n++; return n; }
// recorta a área visível e centraliza num quadrado (o ícone fica legível mesmo em itens grandes)
function quadrado(im) {
  let x0 = im.w, y0 = im.h, x1 = -1, y1 = -1;
  for (let y = 0; y < im.h; y++) for (let x = 0; x < im.w; x++) if (im.rgba[(y * im.w + x) * 4 + 3] >= 16) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
  if (x1 < 0) return im;
  const lado = Math.max(x1 - x0 + 1, y1 - y0 + 1, 32), o = Buffer.alloc(lado * lado * 4);
  const dx = ((lado - (x1 - x0 + 1)) / 2 | 0) - x0, dy = ((lado - (y1 - y0 + 1)) / 2 | 0) - y0;
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) im.rgba.copy(o, ((y + dy) * lado + x + dx) * 4, (y * im.w + x) * 4, (y * im.w + x) * 4 + 4);
  return { w: lado, h: lado, rgba: o };
}
const CRIATURA = 0x54;
const linhas = [], vistos = new Set();
let comIcone = 0;
for (let id = 1; id <= D.itemCount; id++) {
  const rec = lib.record(D, id); if (!rec || rec.length < 48) continue;
  if (rec[8] === CRIATURA || rec[8] === 0) continue;
  const m = msgs.get(rec.readUInt32BE(44)); if (!m || !m.pt) continue;
  const s = spritesDoRegistro(rec); let temIcone = false;
  if (s) { const im = montar(s); if (visiveis(im) >= 20) { const q = quadrado(im); fs.writeFileSync(path.join(DIR_IT, id + '.png'), png.encode(q.w, q.h, q.rgba)); temIcone = true; comIcone++; } }
  linhas.push([m.pt, id, String.fromCharCode(rec[8]), m.en, temIcone ? 1 : 0].join('\t'));
  vistos.add(m.pt);
}
fs.writeFileSync(path.join(APP, 'dados', 'itens.tsv'), linhas.join('\n'));

// ---- monstros (.mob): nome (UTF-16BE com tamanho em bytes) e lookId; ícone = direção 2 (frente), quadro 0 ----
const mob = inflateFrom(fs.readFileSync(path.join(GD, 'bloodstone.mob'))).data;
const mons = []; let ini = 0;
for (let i = 0; i + 2 <= mob.length; i++) {
  const n = mob.readUInt16BE(i); if (n < 2 || n > 80 || n % 2 || i + 2 + n > mob.length) continue;
  let ok = true, s = ''; for (let j = 0; j < n / 2; j++) { const c = mob.readUInt16BE(i + 2 + j * 2); if (c < 0x20 || c > 0x24f) { ok = false; break; } s += String.fromCharCode(c); }
  if (!ok) continue;
  const hdr = mob.subarray(ini, i); if (hdr.length >= 8) mons.push({ nome: s, look: hdr.readUInt32BE(4) });
  ini = i + 2 + n; i = ini - 1;
}
const mlin = []; let mIcone = 0;
for (const m of mons) {
  mlin.push(m.nome + '\t' + m.look);
  const arq = path.join(DIR_MO, m.look + '.png'); if (fs.existsSync(arq)) continue;
  const rec = lib.record(D, m.look); if (!rec) continue;
  const g = lib.parseOutfit(rec); const fr = (g.A[2] || g.A[3] || g.A[1] || {})[0]; if (!fr) continue;
  const im = lib.assemble(S, fr); if (visiveis(im) < 20) continue;
  const q = quadrado(im); fs.writeFileSync(arq, png.encode(q.w, q.h, q.rgba)); mIcone++;
}
fs.writeFileSync(path.join(APP, 'dados', 'monstros.tsv'), mlin.join('\n'));
console.log(JSON.stringify({ textos: msgs.size, itensComNome: linhas.length, nomesUnicos: vistos.size, itensComIcone: comIcone, monstros: mons.length, monstrosComIcone: mIcone }));

// ---- wiki (opcional, 2º argumento): imagens das criaturas e preços de NPC ----
// Comercio.json: tipo "compra" = o NPC compra de você (valor do loot); "venda" = o NPC vende (custo de supply).
const WIKI = process.argv[3];
if (WIKI) {
  const imgs = new Map();
  (function r(d) { for (const n of fs.readdirSync(d)) { const p = path.join(d, n); if (fs.statSync(p).isDirectory()) r(p); else imgs.set(n.toLowerCase(), p); } })(path.join(WIKI, 'public', 'w', 'images'));
  const DIR_CR = path.join(APP, 'icones', 'criaturas'); fs.mkdirSync(DIR_CR, { recursive: true });
  const cri = require(path.join(WIKI, 'data', 'raw', 'cargo', 'Criaturas.json')).rows;
  const clin = []; let cImg = 0;
  for (const c of cri) {
    const nome = (c.Name || c.page || '').trim(); if (!nome) continue;
    const base = nome.replace(/ /g, '_').toLowerCase(); let arq = '';
    for (const ext of ['.gif', '.png', '.webp']) if (imgs.has(base + ext)) { arq = imgs.get(base + ext); break; }
    let destino = '';
    if (arq && !arq.endsWith('.webp')) { destino = nome.replace(/[\/:*?"<>|]/g, '') + path.extname(arq); fs.copyFileSync(arq, path.join(DIR_CR, destino)); cImg++; }
    clin.push([nome, c.XP || '', c.PontosVida || '', c.IsBoss || '', destino].join('\t'));
  }
  // as animações da wiki (.gif) também cobrem criaturas que não estão na tabela; o analyzer procura pelo nome
  for (const [n, p] of imgs) if (n.endsWith('.gif')) { const d = path.join(DIR_CR, path.basename(p).replace(/_/g, ' ')); if (!fs.existsSync(d)) { fs.copyFileSync(p, d); cImg++; } }
  fs.writeFileSync(path.join(APP, 'dados', 'criaturas.tsv'), clin.join('\n'));
  const com = require(path.join(WIKI, 'data', 'raw', 'cargo', 'Comercio.json')).rows;
  const pr = new Map();
  for (const r of com) {
    const v = Number(r.preco); if (!r.item || !(v > 0)) continue;
    const e = pr.get(r.item) || { loot: 0, custo: 0 };
    if (r.tipo === 'compra') e.loot = Math.max(e.loot, v);
    else if (r.tipo === 'venda') e.custo = e.custo ? Math.min(e.custo, v) : v;
    pr.set(r.item, e);
  }
  fs.writeFileSync(path.join(APP, 'dados', 'precos.tsv'), [...pr].map(([k, e]) => [k, e.loot, e.custo].join('\t')).join('\n'));
  console.log(JSON.stringify({ criaturas: clin.length, criaturasComImagem: cImg, itensComPreco: pr.size }));
  // para que serve cada item (missão diária, forja, refino): dados/usos.tsv
  require('./gerar-usos.js')(WIKI, APP);
}

// ---- lista de supplies (dados/supplies.json): poções/elixires/tônicos/infusões (categoria U), flechas e alguns consumíveis ----
// Itens já na lista são mantidos (o usuário pode acrescentar outros à mão).
{
  const arq = path.join(APP, 'dados', 'supplies.json');
  const set = new Set(fs.existsSync(arq) ? JSON.parse(fs.readFileSync(arq, 'utf8')) : []);
  const fora = /Fertiliz|Fertilidade|Vitamínico|^Poção de (Banmont|Clawtab|Danmori|Dasmankor|Donncaster|Ely|Oakendor|Porto Rackham|Shellshire)|^Poção (VIla|Vila) |^Poção do Lar|^Poção da Passagem/i;
  for (const l of linhas) {
    const [n, , cat] = l.split('\t');
    if (cat === 'U' && !fora.test(n)) set.add(n);
    if (cat === 'O' && /^Flecha/.test(n)) set.add(n);
    if (/^(Curativo|Runa (Ar|Fogo|Terra|Água)|Pergaminho Arcano|Pergaminho de Treino|Barreira de Gelo)$/.test(n)) set.add(n);
  }
  fs.writeFileSync(arq, JSON.stringify([...set].sort((a, b) => a.localeCompare(b, 'pt')), null, 2));
  console.log(JSON.stringify({ supplies: set.size }));
}
