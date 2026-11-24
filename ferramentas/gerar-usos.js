'use strict';
// Gera app/dados/usos.tsv a partir da wiki: para que serve cada item além de vender no NPC.
//   nome (como no jogo) <TAB> diárias <TAB> forja <TAB> refino
// Cada coluna: "Nome|quantidade" separados por ";" (diária: nome da missão; forja/refino: arma/equipamento que usa o item;
// no refino a quantidade é a soma de todos os níveis).
// Fontes (todas da wiki): receitas de forja e refino (src/generated/item-details.json), missões diárias de coleta
// (data/raw/cargo/QuestsDiarias.json) e o resumo por item (src/generated/loot-items.json).
// Os nomes da wiki às vezes têm erro de digitação ("Cajado de Sadmarath" no lugar de "Cajado de Sedmarath"):
// cada nome é ligado ao nome do jogo (app/dados/itens.tsv) por igualdade, depois sem acentos/espaços, depois por
// diferença de até 2 letras quando só um item do jogo fica tão perto. O que não liga é listado no fim.
// Uso: node gerar-usos.js "<pasta da wiki>"   (o gerar-dados.js também chama isto quando recebe a pasta da wiki)
const fs = require('fs');
const path = require('path');

const norm = s => String(s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]/g, '');
const limpa = s => String(s || '').replace(/[\t\r\n;|]/g, ' ').replace(/\s+/g, ' ').trim();

function distancia(a, b, max) {
  if (Math.abs(a.length - b.length) > max) return max + 1;
  let ant = Array.from({ length: b.length + 1 }, (_, j) => j);
  for (let i = 1; i <= a.length; i++) {
    const cur = [i]; let menor = i;
    for (let j = 1; j <= b.length; j++) {
      cur[j] = Math.min(ant[j] + 1, cur[j - 1] + 1, ant[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
      if (cur[j] < menor) menor = cur[j];
    }
    if (menor > max) return max + 1;
    ant = cur;
  }
  return ant[b.length];
}

function lerJson(arq) { return fs.existsSync(arq) ? JSON.parse(fs.readFileSync(arq, 'utf8')) : null; }

function gerarUsos(wiki, app) {
  const detalhes = lerJson(path.join(wiki, 'src', 'generated', 'item-details.json'));
  const loot = lerJson(path.join(wiki, 'src', 'generated', 'loot-items.json'));
  const diarias = lerJson(path.join(wiki, 'data', 'raw', 'cargo', 'QuestsDiarias.json'));
  if (!detalhes && !loot) { console.error('sem dados gerados da wiki em ' + wiki + ' (rode o build da wiki antes)'); return null; }

  // nomes do jogo (os mesmos que aparecem no canal de Pilhagem)
  const arqItens = path.join(app, 'dados', 'itens.tsv');
  const nomesJogo = fs.existsSync(arqItens) ? [...new Set(fs.readFileSync(arqItens, 'utf8').split('\n').map(l => l.split('\t')[0]).filter(Boolean))] : [];
  const porMinusc = new Map(nomesJogo.map(n => [n.toLowerCase(), n]));
  const porNorm = new Map(); for (const n of nomesJogo) { const k = norm(n); if (!porNorm.has(k)) porNorm.set(k, n); }
  const cache = new Map(), corrigidos = new Map(), semPar = new Set();
  function nomeDoJogo(nome) {
    nome = limpa(nome); if (!nome) return null;
    if (cache.has(nome)) return cache.get(nome);
    let r = porMinusc.get(nome.toLowerCase()) || porNorm.get(norm(nome)) || null;
    if (!r && nomesJogo.length) {
      const k = norm(nome), max = k.length >= 12 ? 2 : 1;
      let melhor = max + 1, achados = [];
      for (const [kn, n] of porNorm) { const d = distancia(k, kn, max); if (d < melhor) { melhor = d; achados = [n]; } else if (d === melhor && d <= max) achados.push(n); }
      if (melhor <= max && achados.length === 1) { r = achados[0]; corrigidos.set(nome, r); }
    }
    if (!r) { semPar.add(nome); r = nomesJogo.length ? null : nome; }
    cache.set(nome, r); return r;
  }

  // uso[nome do jogo] = { d: Map(missão->qtd), f: Map(arma->qtd), r: Map(arma->qtd somada) }
  const usos = new Map();
  function poe(item, tipo, alvo, qtd) {
    const n = nomeDoJogo(item); if (!n || !alvo) return;
    if (!usos.has(n)) usos.set(n, { d: new Map(), f: new Map(), r: new Map() });
    const m = usos.get(n)[tipo]; alvo = limpa(alvo);
    m.set(alvo, (m.get(alvo) || 0) + (Number(qtd) || 0));
  }

  // 1) receitas de forja e refino de todas as armas e equipamentos
  for (const [arma, v] of Object.entries((detalhes && detalhes.items) || {})) {
    for (const x of (v.forja && v.forja.items) || []) poe(x.name, 'f', arma, x.qty);
    for (const nv of v.refino || []) for (const x of nv.items || []) poe(x.name, 'r', arma, x.qty);
  }
  // 2) missões diárias de coleta
  for (const q of ((diarias && diarias.rows) || [])) if (q.TipoDiaria === 'Coleta' && q.NomeDoItem) poe(q.NomeDoItem, 'd', q.Nome || q.page, q.QuantidadeDiaria);
  // 3) o resumo por item da wiki (cobre o que já estava ligado antes; não duplica)
  for (const it of loot || []) {
    for (const x of it.diarias || []) { const n = nomeDoJogo(it.name); if (n && !(usos.get(n) && usos.get(n).d.has(limpa(x.n)))) poe(it.name, 'd', x.n, x.q); }
    for (const x of it.forja || []) { const n = nomeDoJogo(it.name); if (n && !(usos.get(n) && usos.get(n).f.has(limpa(x.n)))) poe(it.name, 'f', x.n, x.q); }
    for (const x of it.refino || []) { const n = nomeDoJogo(it.name); if (n && !(usos.get(n) && usos.get(n).r.has(limpa(x.n)))) poe(it.name, 'r', x.n, x.q); }
  }

  const col = m => [...m].sort((a, b) => b[1] - a[1]).map(([k, q]) => k + (q ? '|' + q : '')).join(';');
  const linhas = [...usos].sort((a, b) => a[0].localeCompare(b[0], 'pt')).map(([n, u]) => [n, col(u.d), col(u.f), col(u.r)].join('\t'));
  fs.mkdirSync(path.join(app, 'dados'), { recursive: true });
  fs.writeFileSync(path.join(app, 'dados', 'usos.tsv'), linhas.join('\n'));
  const res = { itensComUso: linhas.length, diarias: [...usos.values()].filter(u => u.d.size).length, forja: [...usos.values()].filter(u => u.f.size).length, refino: [...usos.values()].filter(u => u.r.size).length };
  console.log(JSON.stringify(res));
  if (corrigidos.size) console.log('nomes da wiki corrigidos para o nome do jogo:\n  ' + [...corrigidos].map(([a, b]) => a + ' -> ' + b).join('\n  '));
  if (semPar.size) console.log('sem item correspondente no jogo (ignorados):\n  ' + [...semPar].sort().join('\n  '));
  return res;
}
module.exports = gerarUsos;

if (require.main === module) {
  const wiki = process.argv[2];
  if (!wiki) { console.error('uso: node gerar-usos.js "<pasta da wiki>"'); process.exit(1); }
  gerarUsos(wiki, path.join(__dirname, '..', 'app'));
}
