@echo off
rem Compila o Analyzer Bloodpedia com o compilador C# que j?? vem no Windows (.NET Framework 4).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF
cd /d "%~dp0"
"%CSC%" -nologo -target:winexe -platform:x86 -optimize+ -out:teste-captura.exe -win32icon:icone.ico -win32manifest:src\app.manifest ^
  -resource:src\Estilos.xaml,Estilos.xaml -resource:src\Hunt.xaml,Hunt.xaml -resource:src\Loot.xaml,Loot.xaml -resource:src\Xp.xaml,Xp.xaml -resource:src\Kills.xaml,Kills.xaml -resource:src\Recebido.xaml,Recebido.xaml -resource:src\Supplies.xaml,Supplies.xaml ^
  -r:"%WPF%\PresentationFramework.dll" -r:"%WPF%\PresentationCore.dll" -r:"%WPF%\WindowsBase.dll" -r:System.Xaml.dll -r:System.Xml.dll ^
  src\Analyzer.cs src\Extras.cs
