# Analyzer Bloodpedia

Responda sempre em português do Brasil.

## Compilar e publicar
- Compilar: `app\compilar.cmd` (C# do .NET Framework 4 que já vem no Windows).
- Versão: `Versao = "..."` em `app\src\Extras.cs`. Notas da versão em `dist\notas-X.Y.Z.md`.
- Pacote: `powershell -ExecutionPolicy Bypass -File publicar.ps1`, depois `gh release create vX.Y.Z dist\AnalyzerBloodpedia-vX.Y.Z.zip dist\AnalyzerBloodpedia.zip --notes-file dist\notas-X.Y.Z.md`.
- Offsets de memória do jogo: `app\offsets.ini` / classe `Offsets` em `app\src\Analyzer.cs`.

## Anunciar versão nova no Discord: use o BOT, nunca o navegador
O servidor Discord "Bloodpedia" tem um bot próprio em `C:\Users\eduar\OneDrive\Imagens\BLOODSTONE\bloodpedia-bot`.
Depois do `gh release create`, anuncie assim (uma mudança curta por argumento):

```
cd "C:\Users\eduar\OneDrive\Imagens\BLOODSTONE\bloodpedia-bot"
npm run anunciar -- X.Y.Z "mudança 1" "mudança 2"
```

- Posta em #📊・analyzer marcando o cargo 📊 Avisos do Analyzer (não @everyone) com o link de download.
- `--teste` no fim posta em #🤖・bot-logs sem marcar ninguém, para conferir antes.
- Não abra o Discord no navegador nem peça login: o bot já tem acesso. Não leia nem mostre o `.env` do bot (token).
- Mostre o texto ao usuário e espere o OK antes do anúncio real.

## Invasões
O bot também lê `%LOCALAPPDATA%\BloodstoneHunt\mensagens.log` (que o Analyzer grava, canal Servidor) para avisar invasões no Discord. Não mude o formato das linhas `[Servidor]` desse log sem ajustar `src/invasoes.js` do bot.
