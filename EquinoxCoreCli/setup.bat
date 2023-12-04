for %%I in (.) do set CurrDirName=%%~nxI
mklink /J "%AppData%\MedievalEngineers\Mods\%CurrDirName%" "%~dp0\Content\"
echo "Registered mod %CurrDirName% in MedievalEngineers\Mods"
PAUSE