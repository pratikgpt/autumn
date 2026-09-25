# Builds Autumn.exe with the C# compiler that ships inside Windows (.NET Framework 4.x). No SDK needed.
param([string]$Out = "$PSScriptRoot\Autumn.exe", [string]$Target = "winexe")
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$icon = "$PSScriptRoot\src\autumn.ico"
$iconArg = if (Test-Path $icon) { "/win32icon:$icon" } else { "" }
& $csc /nologo /target:$Target /optimize+ /unsafe /out:$Out $iconArg `
    /r:System.Drawing.dll /r:System.Windows.Forms.dll "$PSScriptRoot\src\*.cs"
exit $LASTEXITCODE
