param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$releasesFolderName  = "VCS-SRS-$Version"
$releasesFolder      = $releasesFolderName
$releasesArchiveName = ".\VCS-SRS-$Version.zip"

# dotnet CLI build paths (SRS-Lua-Wrapper.vcxproj and Installer.csproj require VS and are excluded)
$clientBin   = ".\DCS-SR-Client\bin\x64\Release\net8.0-windows10.0.19041.0"
$installBuild = ".\install-build"

# Restore
dotnet restore DCS-SimpleRadioStandalone.slnf
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet restore failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Build
dotnet build DCS-SimpleRadioStandalone.slnf `
    --configuration Release `
    --no-restore `
    -p:Platform=x64 `
    -p:SourceLinkCreate=false `
    -p:Version=$Version `
    -v:q
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}
Write-Host "dotnet build completed with no errors"

# Assemble install-build
New-Item -ItemType Directory -Path $installBuild -Force | Out-Null
New-Item -ItemType Directory -Path "$installBuild\AudioEffects" -Force | Out-Null
New-Item -ItemType Directory -Path "$installBuild\Scripts" -Force | Out-Null

# Client runtime (exe, managed dll, config, native audio dlls, data files)
$clientFiles = @(
    "VCS-RadioClient.exe",
    "VCS-RadioClient.dll",
    "VCS-RadioClient.deps.json",
    "VCS-RadioClient.runtimeconfig.json",
    "VCS-RadioClient.dll.config",
    "opus.dll",
    "speexdsp.dll",
    "WebRtcVad.dll",
    "awacs-radios.json",
    "vngd-channels.txt",
    "NLog.config"
)
foreach ($f in $clientFiles) {
    $src = "$clientBin\$f"
    if (Test-Path $src) {
        Copy-Item $src "$installBuild\$f" -Force
    } else {
        Write-Warning "Expected file not found: $src"
    }
}

# AudioEffects (source assets, not build outputs)
Copy-Item ".\DCS-SR-Client\AudioEffects\*" "$installBuild\AudioEffects" -Recurse -Force

# DCS Lua scripts
Copy-Item ".\Scripts\*" "$installBuild\Scripts" -Recurse -Force

# srs.dll from C++ build - requires Visual Studio with C++ workload.
# Build SRS-Lua-Wrapper separately and ensure x64\Release\srs.dll exists before packaging.
$srsDll = ".\x64\Release\srs.dll"
if (Test-Path $srsDll) {
    New-Item -ItemType Directory -Path "$installBuild\Scripts\DCS-SRS\bin" -Force | Out-Null
    Copy-Item $srsDll "$installBuild\Scripts\DCS-SRS\bin\srs.dll" -Force
    Write-Host "Included srs.dll"
} else {
    Write-Warning "srs.dll not found at '$srsDll' - Lua wrapper will be missing from release"
}

# Remove git metadata
Get-ChildItem $installBuild -Recurse | Where-Object { $_.Name -like "*.git*" } | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "Assembled install-build"

# Copy into release folder
New-Item -ItemType Directory -Path $releasesFolder -Force | Out-Null
Copy-Item -Path "$installBuild\*" -Destination $releasesFolder -Recurse -Force
Write-Host "Copied to release folder: $releasesFolder"

# Final archive
tar -acf $releasesArchiveName $releasesFolderName
if ($LASTEXITCODE -ne 0) {
    Write-Error "Error creating archive (exit code $LASTEXITCODE)"
    exit $LASTEXITCODE
}

Write-Host "Finished: $releasesArchiveName"
