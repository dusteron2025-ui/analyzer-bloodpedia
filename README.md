# Analyzer Bloodpedia

<img src="marca/analyzer-bloodpedia.png" width="96" align="right">

Analisador de caçada para o **BloodStone**, no estilo dos analyzers do Tibia: XP/h, loot com valor, supplies gastos, balanço, dano causado e recebido, cura e monstros mortos — em janelas pequenas que ficam por cima do jogo.

## Download
Baixe o `.zip` da [versão mais recente](../../releases/latest), extraia e abra o `AnalyzerBloodpedia.exe` (veja o `LEIA-ME.txt`).
O analyzer avisa sozinho quando sair uma versão nova.

## Extras
- **Relatório em imagem**: o botão de câmera do Hunt Analyzer gera o resumo da caçada em PNG, em 1920x1080 (vídeo) e 1080x1920 (vertical), na pasta `Relatórios` ao lado do `.exe`. As imagens só são desenhadas no clique; nada fica rodando em segundo plano.
- **Uso dos itens**: no Loot Analyzer, um selo pequeno mostra se o item é pedido em **missão diária** (azul), usado na **forja** (laranja) ou no **refino** (roxo), segundo a Bloodpedia. Passe o mouse no item para ver onde.

## Como funciona
- **Só lê** a memória do `client.exe` (`ReadProcessMemory`): canais de chat "Pilhagem" e "Servidor", XP, vida/mana e o total de cada item que o personagem carrega.
- **Não escreve** na memória, **não injeta** código, **não automatiza** nenhuma ação e **não envia** nada ao servidor do jogo.
- Conexões com a internet: a consulta da versão mais recente neste repositório (GitHub) e a **contagem de uso** (`app\src\Licenca.cs`): ao abrir, envia à Bloodpedia só um código aleatório da instalação e a versão, para sabermos quantas pessoas usam o analyzer. Nada do personagem, da conta ou do PC. Detalhes na [política de privacidade](https://www.bloodpedia.com.br/privacidade/#analyzer).
- A resposta da contagem traz uma política assinada ("livre" hoje). Ela já deixa pronta uma trava de licença para um plano pago no futuro, ligada no servidor da wiki com `ANALYZER_COBRANCA=1` e `ANALYZER_CHAVE_PRIVADA`.
- Pede administrador porque o cliente do jogo roda como administrador.

## Compilar
Usa o compilador C# que já vem no Windows (.NET Framework 4): `app\compilar.cmd`.
Para montar o pacote: `publicar.ps1` (precisa das pastas `app\dados` e `app\icones`, geradas por `ferramentas\gerar-dados.js` a partir dos arquivos do jogo; o uso dos itens vem de `ferramentas\gerar-usos.js "<pasta da wiki>"`).
