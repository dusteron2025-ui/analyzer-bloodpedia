@echo off
rem Compila o Analyzer Bloodpedia com o compilador C# que ja vem no Windows (.NET Framework 4).
rem Antes, monta obj\pacote.bin (dados e icones reduzidos), que vai embutido no .exe.
set NET=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%NET%\csc.exe
set WPF=%NET%\WPF
cd /d "%~dp0"
if not exist ..\ferramentas\empacotar.exe "%CSC%" -nologo -r:System.Drawing.dll -r:"%NET%\System.IO.Compression.dll" -out:..\ferramentas\empacotar.exe ..\ferramentas\Empacotar.cs
..\ferramentas\empacotar.exe . obj\pacote.bin || exit /b 1
"%CSC%" -nologo -target:winexe -platform:x86 -optimize+ -define:TESTE -out:teste-captura.exe -win32icon:icone.ico -win32manifest:src\teste.manifest ^
  -resource:src\Estilos.xaml,Estilos.xaml -resource:src\Hunt.xaml,Hunt.xaml -resource:src\Loot.xaml,Loot.xaml -resource:src\Xp.xaml,Xp.xaml -resource:src\Kills.xaml,Kills.xaml -resource:src\Recebido.xaml,Recebido.xaml -resource:src\Supplies.xaml,Supplies.xaml ^
  -resource:obj\pacote.bin,pacote.bin -resource:..\marca\analyzer-bloodpedia-sem-fundo.png,logo.png ^
  -r:"%WPF%\PresentationFramework.dll" -r:"%WPF%\PresentationCore.dll" -r:"%WPF%\WindowsBase.dll" -r:System.Xaml.dll -r:System.Xml.dll -r:"%NET%\System.Web.Extensions.dll" -r:"%NET%\System.IO.Compression.dll" -r:"%NET%\System.IO.Compression.FileSystem.dll" ^
  src\Analyzer.cs src\Extras.cs src\Pacote.cs src\Relatorio.cs src\Licenca.cs src\Grupo.cs src\Tutorial.cs
