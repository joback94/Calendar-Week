# Builds WeekNumber.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
Set-Location $PSScriptRoot
& $csc /nologo /target:winexe /optimize+ /out:WeekNumber.exe /win32manifest:app.manifest `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll WeekNumber.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
