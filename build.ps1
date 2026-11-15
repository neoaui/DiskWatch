param([switch]$DevelopmentSdk9, [switch]$Test, [switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'work\cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot 'work\nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $PSScriptRoot 'work\nuget-http'
$env:TEMP = Join-Path $PSScriptRoot 'work\temp'
$env:TMP = $env:TEMP
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'

function Resolve-DotNet {
    if ($DevelopmentSdk9) {
        $p = Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe'
        if (-not (Test-Path -LiteralPath $p)) { throw "找不到 .NET SDK 9：$p" }
        return (Resolve-Path -LiteralPath $p).Path
    }
    if ($env:DOTNET_ROOT) {
        $p = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
        if (Test-Path -LiteralPath $p) { return (Resolve-Path -LiteralPath $p).Path }
    }
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source -and (Test-Path -LiteralPath $cmd.Source)) { return $cmd.Source }
    $pf = Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $pf) { return (Resolve-Path -LiteralPath $pf).Path }
    throw '找不到 dotnet。请安装 .NET 10 SDK，或设置环境变量 DOTNET_ROOT 指向 SDK 目录。'
}

$target = if ($DevelopmentSdk9) { 'net9.0' } else { 'net10.0' }
$dotnetExe = Resolve-DotNet
$env:DOTNET_ROOT = Split-Path -Parent $dotnetExe
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
& $dotnetExe build src\DiskWatch.App\DiskWatch.App.csproj -c Release "-p:DiskWatchFramework=$target" --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
if ($Test) {
    & $dotnetExe run --project tests\DiskWatch.Tests\DiskWatch.Tests.csproj -c Release "-p:DiskWatchFramework=$target"
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $LASTEXITCODE" }
}
if ($Publish) {
    $destination = if ($DevelopmentSdk9) { 'release\development-net9' } else { 'release\net10' }
    & $dotnetExe publish src\DiskWatch.App\DiskWatch.App.csproj -c Release "-p:DiskWatchFramework=$target" -o $destination --self-contained false --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
    if (-not $DevelopmentSdk9) {
        & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /win32icon:assets\DiskWatch.ico /reference:System.Windows.Forms.dll /out:DiskWatch2.exe src\Launcher.cs src\LaunchPolicy.cs
        if ($LASTEXITCODE -ne 0) { throw "Launcher build failed: $LASTEXITCODE" }
    }
}
