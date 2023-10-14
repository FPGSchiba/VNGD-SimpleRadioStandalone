@echo off

::Command line arguments
set presetsFolder=%1
set version=%2

:::::::::  File structure settings :::::::::::::::::

::Release
set "releasesFolderName=Vanguard-SRS-%version%"
set "releasesFolder=%releasesFolderName%\"

::Client Release
set clientReleasesFolder=.\SRS-Client
set clientArchiveName=Vanguard-SRS-Client-%version%.zip
set clientFolder=%clientReleasesFolder%\%clientFolderName%\

::Presets
set "presetsFolderName=SRS-Radio-Presets"
set "presetsFolder=%presetsFolder%\"

::Final Release Archive
set "releasesArchiveName=.\Vanguard-SRS-%version%.zip"

::::::::::: /File structure settings ::::::::::::::::


:: Build

:: Set to quiet, redirected to NUL for batch file testing - change if needed

nuget restore
msbuild -v:q /p:Configuration=Release /p:Platform=x64 /p:SourceLinkCreate=false /p:Version=%version% > NUL
if %errorlevel% neq 0 then goto builderror
echo msbuild completed with no error level



:::: Create File Structure  
::
::   .\Vanguard-SRS-%version%\presets        -- Preset manager, etc
::   .\Vanguard-SRS-%version%\radio-client   -- SRS Client
::   

mkdir %releasesFolder%
mkdir %releasesFolder%\%clientReleasesFolder%
mkdir %releasesFolder%\%presetsFolder%
::mkdir %releasesFolderName%
echo Created Release Folders

::Cleanup uneeded files
DEL /F .\install-build\DCS-SR-ExternalAudio.exe .\install-build\Installer.exe .\install-build\SRS-AutoUpdater.exe .\install-build\SR-Server.exe
echo Removed unneeded files


:: Move the build into the client fold

XCOPY .\install-build\ "%releasesFolder%\%clientReleasesFolder%" /Y /q /e
echo Copied Client to Release 


XCOPY %presetsFolder% %releasesFolder%\%presetsFolder%\ /q /e /k /h /i /y 
echo Copied Presets to Release


:: Final release archive
tar.exe -a -c -f "%releasesArchiveName%" "%releasesFolderName%"
if %errorlevel% neq 0 then goto tarerror

::Cleanup - when I know what to clean up


echo Finished


goto end

::: Error handling

:msbuilderror

Echo msbuild exited with error level %errorlevel%
goto end

:tarerror

echo Error creating Archive: tar.exe


:end