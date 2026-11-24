# Monta o pacote do Analyzer Bloodpedia para distribuir: compila e gera dist\AnalyzerBloodpedia-v<versão>.zip
# Uso: powershell -ExecutionPolicy Bypass -File publicar.ps1
# Depois: gh release create vX.Y.Z dist\AnalyzerBloodpedia-vX.Y.Z.zip dist\AnalyzerBloodpedia.zip --notes "..."
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path
$app = Join-Path $raiz 'app'
$versao = ([regex]::Match((Get-Content (Join-Path $app 'src\Extras.cs') -Raw), 'Versao = "([^"]+)"')).Groups[1].Value
cmd /c "`"$app\compilar.cmd`""
if ($LASTEXITCODE -ne 0) { throw 'falha ao compilar' }
$tmp = Join-Path $raiz 'dist\AnalyzerBloodpedia'
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $tmp | Out-Null
# dados e ícones vão embutidos no .exe (obj\pacote.bin): o zip leva só o programa e as instruções
Copy-Item "$app\AnalyzerBloodpedia.exe" $tmp
Copy-Item (Join-Path $raiz 'LEIA-ME.txt') $tmp
Copy-Item (Join-Path $raiz 'criar-atalho.cmd') $tmp
$zip = Join-Path $raiz "dist\AnalyzerBloodpedia-v$versao.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path $tmp -DestinationPath $zip
# cópia com nome fixo: o link .../releases/latest/download/AnalyzerBloodpedia.zip sempre baixa a versão mais nova
Copy-Item $zip (Join-Path $raiz "dist\AnalyzerBloodpedia.zip") -Force
"Pacote: $zip ($([math]::Round((Get-Item $zip).Length/1MB,1)) MB)"
