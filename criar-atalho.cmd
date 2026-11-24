@echo off
rem Cria um atalho do Analyzer Bloodpedia na área de trabalho
powershell -NoProfile -Command "$d=[Environment]::GetFolderPath('Desktop'); $s=(New-Object -ComObject WScript.Shell).CreateShortcut(\"$d\Analyzer Bloodpedia.lnk\"); $s.TargetPath='%~dp0AnalyzerBloodpedia.exe'; $s.WorkingDirectory='%~dp0'; $s.IconLocation='%~dp0AnalyzerBloodpedia.exe,0'; $s.Save()"
echo Atalho criado na area de trabalho.
pause
