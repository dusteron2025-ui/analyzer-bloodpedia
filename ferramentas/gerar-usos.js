'use strict';
// Gera app/dados/usos.tsv a partir da wiki (src/generated/loot-items.json): para que serve cada item além de vender no NPC.
//   nome <TAB> diárias <TAB> forja <TAB> refino
// Cada coluna: "Nome|quantidade" separados por ";" (diária: nome da missão; forja/refino: arma/equipamento que usa o item).
// Uso: node gerar-usos.js "<pasta da wiki>"   (o gerar-dados.js também chama isto quando recebe a pasta da wiki)
const fs = require('fs');
const path = require('path');

function gerarUsos(wiki, app) {
  const arq = path.join(wiki, 'src', 'generated', 'loot-items.json');
  if (!fs.existsSync(arq)) { console.error('sem ' + arq + ' (rode o build da wiki antes)'); return null; }
  const itens = JSON.parse(fs.readFileSync(arq, 'utf8'));
  const limpa = s => String(s || '').replace(/[\t\r\n;|]/g, ' ').trim();
  const col = (lista, nome, qtd) => (lista || []).map(x => limpa(x[nome]) + (x[qtd] ? '|' + x[qtd] : '')).filter(x => x && x[0] !== '|').join(';');
  const linhas = [];
  let d = 0, f = 0, r = 0;
  for (const it of itens) {
    if (!it.name) continue;
    const di = col(it.diarias, 'n', 'q'), fo = col(it.forja, 'n', 'q'), re = col(it.refino, 'n', 'q');
    if (!di && !fo && !re) continue;
    if (di) d++; if (fo) f++; if (re) r++;
    linhas.push([limpa(it.name), di, fo, re].join('\t'));
  }
  fs.mkdirSync(path.join(app, 'dados'), { recursive: true });
  fs.writeFileSync(path.join(app, 'dados', 'usos.tsv'), linhas.join('\n'));
  const res = { itensComUso: linhas.length, diarias: d, forja: f, refino: r };
  console.log(JSON.stringify(res));
  return res;
}
module.exports = gerarUsos;

if (require.main === module) {
  const wiki = process.argv[2];
  if (!wiki) { console.error('uso: node gerar-usos.js "<pasta da wiki>"'); process.exit(1); }
  gerarUsos(wiki, path.join(__dirname, '..', 'app'));
}
