# Builds the plugin and makes the zip to unpack in <ComicRack profile>\Scripts\
# Usage: .\package.ps1 [-ComicRackBin <path to ComicRack\bin\Release\net48>\]
param([string]$ComicRackBin = "")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$arguments = @("build", "-c", "Release", "-p:ReleaseBuild=true") # a release has no build stamp in its version
if ($ComicRackBin) { $arguments += "-p:ComicRackBin=$ComicRackBin" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "The build failed" }

$version = ([xml](Get-Content ComicVineAI.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$stage = Join-Path $PSScriptRoot "dist\stage"
$folder = Join-Path $stage "Comic Vine AI"
if (Test-Path "dist") { Remove-Item "dist" -Recurse -Force }
New-Item -ItemType Directory -Path $folder | Out-Null
Copy-Item "bin\Release\net48\ComicVineAI.dll" $folder
@"
Comic Vine AI $version

Unpack the folder "Comic Vine AI" into the Scripts folder of your ComicRack profile
(the Scripts folder next to ComicDb.xml), with ComicRack closed, then start ComicRack.
The plugin needs the ComicRack Community Edition build with .NET plugin support.
https://github.com/Fableton/comicrack-comicvine-ai
"@ | Set-Content (Join-Path $folder "INSTALL.txt") -Encoding UTF8

$zip = Join-Path $PSScriptRoot "dist\ComicVineAI-v$version.zip"
Compress-Archive -Path $folder -DestinationPath $zip
Remove-Item $stage -Recurse -Force
Write-Host "Created $zip"
