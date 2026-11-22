# Runs the workspace-local Godot 4.7.2 .NET editor against the Godot game host (TagForceSpecial.PC).
# Install: copy the Godot_v4.7.2-stable_mono_win64 folder to ./Godot-win-x64 (ignored).
# ./godot.ps1                 opens the editor
# ./godot.ps1 --headless ...  any other Godot arguments, e.g. --headless --quit-after 5
# Game switches go after ++ (PowerShell drops a bare --): ./godot.ps1 ++ --game --data-root <dir>
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GodotArgs)

$root = $PSScriptRoot
$console = Join-Path $root 'Godot-win-x64/Godot_v4.7.2-stable_mono_win64_console.exe'
$editor = Join-Path $root 'Godot-win-x64/Godot_v4.7.2-stable_mono_win64.exe'
$project = Join-Path $root 'TagForceSpecial.PC'
if (-not (Test-Path $editor)) { throw "Godot is not installed at $editor" }

if (-not $GodotArgs) {
    Start-Process $editor -ArgumentList @('--editor', '--path', "`"$project`"")
    return
}
& $console --path $project @GodotArgs
exit $LASTEXITCODE
