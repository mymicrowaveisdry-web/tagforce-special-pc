# Exports the Windows game with the importer Workbench beside it: <out>/TagForceSpecial.PC.exe and <out>/workbench/.
# Needs the Godot 4.7.2 .NET editor and export templates in ./Godot-win-x64 (see godot.ps1).
# Both are built in the ExportRelease configuration so they carry the same Assets DLL, whose SHA-256 the import pins;
# imports made by a Debug Workbench or Tools build do not open in this package (import again with its Workbench).
param([string]$Output = (Join-Path $PSScriptRoot 'artifacts/godot-export'))
$ErrorActionPreference = 'Stop'
# Reused MSBuild nodes outlive the export and keep the caller's output pipe open.
$env:MSBUILDDISABLENODEREUSE = '1'

$output = [System.IO.Path]::GetFullPath($Output)
if (Test-Path $output) { throw "Choose a new export folder: $output exists." }
New-Item -ItemType Directory $output | Out-Null

$console = Join-Path $PSScriptRoot 'Godot-win-x64/Godot_v4.7.2-stable_mono_win64_console.exe'
& $console --headless --path (Join-Path $PSScriptRoot 'TagForceSpecial.PC') --export-release 'Windows Desktop' (Join-Path $output 'TagForceSpecial.PC.exe')
if ($LASTEXITCODE -ne 0) { throw "Godot export failed ($LASTEXITCODE)." }

$workbench = Join-Path $PSScriptRoot 'TagForceSpecial.Workbench/TagForceSpecial.Workbench.csproj'
dotnet build $workbench -c ExportRelease -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw "Workbench build failed ($LASTEXITCODE)." }
Copy-Item -Recurse (Join-Path $PSScriptRoot 'TagForceSpecial.Workbench/bin/ExportRelease/net10.0-windows') (Join-Path $output 'workbench')
"Exported to $output"
