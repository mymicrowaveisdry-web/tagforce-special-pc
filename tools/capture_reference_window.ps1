param([int]$ResearchProcessId, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw 'Choose a new capture filename to preserve existing evidence.' }
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public class Stage4DCapture {
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
}
'@
[Stage4DCapture]::SetProcessDPIAware() | Out-Null
$researchProcess = Get-Process -Id $ResearchProcessId
$workspace = Split-Path $PSScriptRoot -Parent
$pathsFile = Join-Path $workspace 'analysis/stage3-environment/paths.json'
$expectedExecutable = (Get-Content $pathsFile -Raw | ConvertFrom-Json).ppsspp
if ($researchProcess.Path -ine $expectedExecutable) { throw 'Only the isolated research PPSSPP process can be captured.' }
$analysisRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'analysis')) + [IO.Path]::DirectorySeparatorChar
if (-not [IO.Path]::GetFullPath($OutputPath).StartsWith($analysisRoot,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Captures must stay under the local ignored analysis directory.'
}
$windowHandle = $researchProcess.MainWindowHandle
if ($windowHandle -eq 0) { throw 'Research window is unavailable.' }
if ([Stage4DCapture]::IsIconic($windowHandle)) { throw 'Research window is minimized; restore it before capture.' }
$windowRect = New-Object Stage4DCapture+Rect
if (-not [Stage4DCapture]::GetWindowRect($windowHandle,[ref]$windowRect)) { throw 'Window bounds unavailable.' }
$bitmap = New-Object Drawing.Bitmap ($windowRect.Right-$windowRect.Left),($windowRect.Bottom-$windowRect.Top)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$device = $graphics.GetHdc()
try { if (-not [Stage4DCapture]::PrintWindow($windowHandle,$device,2)) { throw 'Window capture failed.' } }
finally { $graphics.ReleaseHdc($device) }
try { $bitmap.Save($OutputPath,[Drawing.Imaging.ImageFormat]::Png) }
finally { $graphics.Dispose(); $bitmap.Dispose() }
