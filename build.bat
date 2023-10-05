@echo off
nuget restore
msbuild /p:Configuration=Release /p:Platform=x64 /p:SourceLinkCreate=false

set presetsFolder=%1
set version=%2

set clientReleasesFolder=.\SRS-Client
set releasesFolder=.

mkdir %clientReleaseFolder%
mkdir %releaseFolder%

DEL /F .\install-build\DCS-SR-ExternalAudio.exe .\install-build\Installer.exe .\install-build\SRS-AutoUpdater.exe .\install-build\SR-Server.exe
echo Removed not needed executables

set clientArchiveName=Vanguard-SRS-Client-%version%.zip

dir

tar.exe -a -c -f %clientArchiveName% .\install-build
MOVE /Y .\%clientArchiveName%  %clientReleasesFolder%\%clientArchiveName%
echo Copied client-archive to client Release folder

set clientFolderName=Vanguard-SRS-Client-%version%

XCOPY .\install-build\ %clientReleasesFolder%\%clientFolderName%\ /Y /q /e
echo Copied client to client Release folder

set clientFolder=%clientReleasesFolder%\%clientFolderName%\

set "releasesFolderName=Vanguard-SRS-%version%"
set "presetsFolderName=SRS-Radio-Presets"
set "presetsFolder=%presetsFolder%\"

mkdir "%releasesFolder%\%releasesFolderName%\"
echo Created Release Folder

XCOPY "%clientFolder%" "%releasesFolder%\%releasesFolderName%\%clientFolderName%\" /Y /q /e
echo Copied Client Folder to Release

XCOPY %presetsFolder% %releasesFolder%\%releasesFolderName%\%presetsFolderName%\ /q /e /k /h /i /y 
echo Copied Presets to Release

set "releasesArchiveName=%releasesFolder%\Vanguard-SRS-%version%.zip"
tar.exe -a -c -f "%releasesArchiveName%" "%releasesFolder%\%releasesFolderName%"
echo Finished