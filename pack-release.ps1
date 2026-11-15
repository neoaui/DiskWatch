param(
    [Parameter(Mandatory = $true)]
    [string]$Destination,
    [string]$Version = '0.1.0'
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

if (-not (Test-Path -LiteralPath $Destination)) {
    New-Item -ItemType Directory -Path $Destination | Out-Null
}

function Resolve-DotNet {
    if ($env:DOTNET_ROOT) {
        $p = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
        if (Test-Path -LiteralPath $p) { return (Resolve-Path -LiteralPath $p).Path }
    }
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source -and (Test-Path -LiteralPath $cmd.Source)) { return $cmd.Source }
    $pf = Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $pf) { return (Resolve-Path -LiteralPath $pf).Path }
    throw '找不到 dotnet。请安装 .NET 10 SDK，或设置环境变量 DOTNET_ROOT。'
}

$dotnetExe = Resolve-DotNet
$env:DOTNET_ROOT = Split-Path -Parent $dotnetExe
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'work\cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot 'work\nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $PSScriptRoot 'work\nuget-http'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$staging = Join-Path $Destination 'staging'
$appOut = Join-Path $staging 'app'
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Path $appOut | Out-Null

# PathMap cannot contain a semicolon; MSBuild splits /p on it.
$pathMap = "$($PSScriptRoot)=/_/"

& $dotnetExe publish 'src\DiskWatch.App\DiskWatch.App.csproj' -c Release -r win-x64 --self-contained true -o $appOut --nologo `
    '-p:DiskWatchFramework=net10.0' `
    '-p:PublishTrimmed=false' `
    '-p:PublishSingleFile=false' `
    '-p:DebugType=none' `
    '-p:DebugSymbols=false' `
    '-p:ContinuousIntegrationBuild=true' `
    "-p:PathMap=$pathMap" `
    "-p:Version=$Version" `
    '-p:AssemblyVersion=0.1.0.0' `
    "-p:FileVersion=$Version.0" `
    "-p:InformationalVersion=$Version" `
    '-p:Authors=neoaui' `
    '-p:Company=neoaui' `
    '-p:Product=DiskWatch' `
    '-p:AssemblyTitle=DiskWatch' `
    '-p:Copyright=Copyright (C) 2026 neoaui'
if ($LASTEXITCODE -ne 0) { throw "publish failed: $LASTEXITCODE" }

Get-ChildItem -LiteralPath $appOut -Recurse -File | Where-Object { $_.Extension -in '.pdb','.xml' } | Remove-Item -Force
if (Test-Path -LiteralPath (Join-Path $appOut 'Directory.Build.props')) {
    Remove-Item -LiteralPath (Join-Path $appOut 'Directory.Build.props') -Force
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "找不到 csc：$csc" }
$launcher = Join-Path $staging 'DiskWatch.exe'
& $csc /nologo /target:winexe /platform:x64 /optimize+ `
    "/win32icon:$PSScriptRoot\assets\DiskWatch.ico" `
    "/win32manifest:$PSScriptRoot\src\ReleaseLauncher.manifest" `
    /reference:System.Windows.Forms.dll `
    /out:$launcher `
    "$PSScriptRoot\src\ReleaseLauncher.cs" `
    "$PSScriptRoot\src\LaunchPolicy.cs"
if ($LASTEXITCODE -ne 0) { throw "launcher build failed: $LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $staging 'LICENSE')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\packaging\README-使用说明.txt') -Destination (Join-Path $staging 'README-使用说明.txt')

$zipName = "DiskWatch-v$Version-win-x64.zip"
$zipPath = Join-Path $Destination $zipName
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$shaPath = Join-Path $Destination "$zipName.sha256.txt"
$utf8 = New-Object System.Text.UTF8Encoding $false
[IO.File]::WriteAllText($shaPath, "$hash  $zipName`r`n", $utf8)

Write-Host "ZIP $zipPath"
Write-Host "SHA256 $hash"
Write-Host "SIZE $((Get-Item $zipPath).Length) bytes"