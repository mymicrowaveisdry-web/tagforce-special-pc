param(
    [ValidateSet('inspect', 'run', 'validate', 'test')][string]$Command = 'run',
    [string]$ModulePath,
    [string]$OutputDirectory = 'analysis/native/card-attack-docker',
    [switch]$Build
)
$ErrorActionPreference = 'Stop'
$comparisonRoot = Split-Path $PSScriptRoot -Parent
$comparisonImage = 'tagforce-pspdev:local'
if ($Build) {
    & docker build --tag $comparisonImage --file (Join-Path $PSScriptRoot 'Dockerfile') $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$comparisonOutput = [IO.Path]::GetFullPath((Join-Path $comparisonRoot $OutputDirectory))
$comparisonPrivateRoot = [IO.Path]::GetFullPath((Join-Path $comparisonRoot 'analysis')) + [IO.Path]::DirectorySeparatorChar
if (-not $comparisonOutput.StartsWith($comparisonPrivateRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must stay under the ignored analysis directory.'
}
New-Item -ItemType Directory -Path $comparisonOutput -Force | Out-Null
$comparisonArgs = @('run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL',
    '--security-opt', 'no-new-privileges', '--tmpfs', '/tmp:rw,nosuid,size=64m',
    '--mount', ('type=bind,source=' + $PSScriptRoot + ',target=/source/native,readonly'),
    '--mount', ('type=bind,source=' + $comparisonOutput + ',target=/source/analysis/native/card-attack'),
    '--workdir', '/source')
if ($Command -eq 'test') {
    $comparisonArgs += @($comparisonImage, 'python3', '-m', 'unittest', 'discover', '-s', 'native', '-p', 'test_*.py')
} else {
    if ([string]::IsNullOrWhiteSpace($ModulePath)) { throw 'ModulePath is required for inspect, run and validate.' }
    $comparisonModule = (Resolve-Path -LiteralPath $ModulePath).Path
    if (-not (Test-Path -LiteralPath $comparisonModule -PathType Leaf)) { throw 'ModulePath must name a file.' }
    $comparisonArgs += @('--mount', ('type=bind,source=' + $comparisonModule + ',target=/inputs/EBOOT.BIN,readonly'),
        $comparisonImage, 'python3', 'native/compile_compare.py', $Command,
        '--module', '/inputs/EBOOT.BIN', '--out', 'analysis/native/card-attack', '--compiler', 'psp-gcc')
}
& docker @comparisonArgs
exit $LASTEXITCODE
