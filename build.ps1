# Builds WeekNumber.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
# GitHub sets the version (APP_VERSION, e.g. 1.0.12); local builds are 0.0.0 and never update themselves.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$version = if ($env:APP_VERSION -match '^\d+(\.\d+){1,3}$') { $env:APP_VERSION } else { '0.0.0' }
$parts = ($version.Split('.') + @('0', '0', '0'))[0..3] -join '.'
$versionCs = Join-Path $env:TEMP 'WeekNumber.Version.cs'
[IO.File]::WriteAllText($versionCs, "[assembly: System.Reflection.AssemblyVersion(""$parts"")]`r`n" +
    "[assembly: System.Reflection.AssemblyFileVersion(""$parts"")]`r`n" +
    "[assembly: System.Reflection.AssemblyInformationalVersion(""$version"")]`r`n" +
    "namespace AppUpdate { static class BuildInfo { public const string Version = ""$version""; } }`r`n",
    (New-Object Text.UTF8Encoding($false)))

$out = Join-Path $here 'WeekNumber.exe'
& $csc /nologo /codepage:65001 /target:winexe /optimize+ "/out:$out" "/win32manifest:$(Join-Path $here 'app.manifest')" `
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    (Join-Path $here 'WeekNumber.cs') (Join-Path $here 'Updater.cs') $versionCs
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "Built $out, version $version"
