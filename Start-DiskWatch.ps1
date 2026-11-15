$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

function Resolve-DotNet {
    if ($env:DOTNET_ROOT) {
        $p = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
        if (Test-Path -LiteralPath $p) { return (Resolve-Path -LiteralPath $p).Path }
    }
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source -and (Test-Path -LiteralPath $cmd.Source)) { return $cmd.Source }
    $pf = Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $pf) { return (Resolve-Path -LiteralPath $pf).Path }
    $bundled = Join-Path $PSScriptRoot 'runtime\dotnet.exe'
    if (Test-Path -LiteralPath $bundled) { return (Resolve-Path -LiteralPath $bundled).Path }
    return $null
}

Add-Type -AssemblyName System.Windows.Forms
$dotnetExe = Resolve-DotNet
$dll = @(
    (Join-Path $PSScriptRoot 'release\net10\DiskWatch.App.dll'),
    (Join-Path $PSScriptRoot 'DiskWatch.App.dll'),
    (Join-Path $PSScriptRoot 'app\DiskWatch.App.dll')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $dotnetExe -or -not $dll) {
    [System.Windows.MessageBox]::Show('找不到 .NET 运行时或 DiskWatch 程序文件。请先安装 .NET 10 运行时，或运行 build.ps1 -Publish。', 'DiskWatch2') | Out-Null
    exit 1
}

$env:DOTNET_ROOT = Split-Path -Parent $dotnetExe
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'work\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TEMP = Join-Path $PSScriptRoot 'work\temp'
$env:TMP = $env:TEMP
Start-Process -FilePath $dotnetExe -ArgumentList @((Get-Item -LiteralPath $dll).FullName) -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
