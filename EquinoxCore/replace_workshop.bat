set mod=1794489239
set steam=C:\Program Files (x86)\Steam
(robocopy /mir /njh /ndl /nfl /ns "%~dp0Content" "%steam%\steamapps\workshop\content\333950\%mod%") ^& IF %ERRORLEVEL% LSS 8 SET ERRORLEVEL = 0
(robocopy /mir /njh /ndl /nfl /ns "%~dp0Content" "%steam%\steamapps\common\Medieval Engineers Dedicated Server\Content\Workshop\content\333950\%mod%") ^& IF %ERRORLEVEL% LSS 8 SET ERRORLEVEL = 0
