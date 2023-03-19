@echo off
nuget restore
msbuild /p:Configuration=Release /p:Platform=x64 /p:SourceLinkCreate=false