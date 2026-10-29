param([Parameter(Mandatory = $true)][string]$IsoPath, [switch]$Visible)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$paths = Get-Content -LiteralPath (Join-Path $workspace 'analysis\stage3-environment\paths.json') -Raw | ConvertFrom-Json
$source = (Resolve-Path -LiteralPath $IsoPath).Path
if ([IO.Path]::GetExtension($source) -ine '.iso') { throw 'Expected an ISO file.' }
if (Get-Process -Name PPSSPPWindows64 -ErrorAction SilentlyContinue) {
    throw 'A PPSSPP process is already running. Close it before launching the research instance.'
}
$runtime = Join-Path $workspace 'analysis\stage3-runtime'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
$arguments = @('--windowed', ('--log=' + '"' + (Join-Path $runtime 'ppsspp.log') + '"'), ('"' + $source + '"'))
$style = if ($Visible) { 'Normal' } else { 'Hidden' }
$process = Start-Process -FilePath $paths.ppsspp -ArgumentList $arguments -WorkingDirectory (Split-Path $paths.ppsspp) -WindowStyle $style -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $runtime 'process-id.txt')
Write-Output "Research PPSSPP started with process ID $($process.Id)."
