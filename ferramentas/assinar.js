'use strict';
// Assina o pacote de uma versão do Analyzer Bloodpedia para a atualização automática.
// O analyzer (1.0.7 em diante) só instala uma atualização se o arquivo AnalyzerBloodpedia.zip.sig da release
// tiver uma assinatura válida desta chave para a mensagem:
//   AnalyzerBloodpedia|<versão>|<sha-256 do zip em hexadecimal>
// Assim, mesmo quem tomar a conta do GitHub não consegue mandar um .exe falso para os usuários: falta a chave.
//
// A chave PRIVADA fica só neste PC: %USERPROFILE%\.bloodpedia\analyzer-atualizacao-privada.pem
// (nunca no repositório, no Vercel ou no Discord). Faça uma cópia de segurança offline: sem ela, as próximas versões
// não se instalam sozinhas e cada pessoa teria de baixar à mão.
// Uso: node assinar.js <versão> <arquivo.zip>    → grava <arquivo.zip>.sig (texto em base64)
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const os = require('os');

const [versao, zip] = process.argv.slice(2);
if (!versao || !zip) { console.error('uso: node assinar.js <versão> <arquivo.zip>'); process.exit(1); }
if (!/^\d+(\.\d+){1,3}$/.test(versao)) { console.error('versão inválida: ' + versao); process.exit(1); }
const arqChave = process.env.ANALYZER_CHAVE_ATUALIZACAO || path.join(os.homedir(), '.bloodpedia', 'analyzer-atualizacao-privada.pem');
if (!fs.existsSync(arqChave)) { console.error('chave de atualização não encontrada: ' + arqChave); process.exit(1); }

const sha = crypto.createHash('sha256').update(fs.readFileSync(zip)).digest('hex');
const msg = Buffer.from(`AnalyzerBloodpedia|${versao}|${sha}`, 'utf8');
const chave = crypto.createPrivateKey(fs.readFileSync(arqChave));
const sig = crypto.sign('sha256', msg, chave).toString('base64');
// confere antes de gravar (pega chave trocada ou arquivo corrompido)
if (!crypto.verify('sha256', msg, crypto.createPublicKey(chave), Buffer.from(sig, 'base64'))) { console.error('a assinatura não conferiu'); process.exit(1); }
fs.writeFileSync(zip + '.sig', sig);
console.log(`assinado: ${path.basename(zip)} v${versao} sha256=${sha.slice(0, 16)}… → ${path.basename(zip)}.sig`);
