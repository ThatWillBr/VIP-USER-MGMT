param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\VIP1132\VIP1132.csproj'
$videoHostProject = Join-Path $root 'installer\VideoHost\InstallerVideoHost.csproj'
$projectXml = [xml](Get-Content -Raw -LiteralPath $project)
$appVersion = $projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $appVersion) { throw "Could not read Version from $project" }
$localDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet-sdk\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$buildRoot = Join-Path $env:LOCALAPPDATA 'VIP1132Build'
$publishDir = Join-Path $buildRoot 'publish'
$portableBuild = Join-Path $buildRoot 'portable'
$dist = Join-Path $root 'dist'
$portableDist = Join-Path $dist 'VIP1132-portable'
$portableZip = Join-Path $dist "VIP1132-Portable-$appVersion.zip"

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Write-Host 'Restoring and compiling...'
& $dotnet restore $project
& $dotnet build $project -c Release --no-restore
& $dotnet restore $videoHostProject
& $dotnet build $videoHostProject -c Release --no-restore
$videoHostExe = Join-Path $buildRoot 'bin\InstallerVideoHost\Release\net48\VIP1132.InstallerVisual.exe'
if (-not (Test-Path -LiteralPath $videoHostExe)) { throw "Installer video host was not built: $videoHostExe" }

Write-Host 'Publishing self-contained installer payload...'
& $dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true -p:DebugType=None -p:DebugSymbols=false -o $publishDir
Get-ChildItem -LiteralPath $publishDir -Filter '*.pdb' -File -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host 'Publishing small portable build (requires .NET 8 Desktop Runtime)...'
& $dotnet publish $project -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $portableBuild
Get-ChildItem -LiteralPath $portableBuild -Filter '*.pdb' -File -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

New-Item -ItemType Directory -Force -Path $dist | Out-Null
if (Test-Path -LiteralPath $portableDist) {
    $resolvedPortable = [IO.Path]::GetFullPath($portableDist)
    $resolvedDist = [IO.Path]::GetFullPath($dist)
    if (-not $resolvedPortable.StartsWith($resolvedDist, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace a portable folder outside dist: $resolvedPortable"
    }
    Remove-Item -LiteralPath $resolvedPortable -Recurse -Force
}
Copy-Item -LiteralPath $portableBuild -Destination $portableDist -Recurse
Compress-Archive -Path (Join-Path $portableDist '*') -DestinationPath $portableZip -Force

if (-not $SkipInstaller) {
    $isccCandidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 is required to build the installer.' }
    Write-Host 'Compiling installer...'
    & $iscc "/DPublishDir=$publishDir" "/DDistDir=$dist" "/DVideoHost=$videoHostExe" `
        "/DSetupVideo=$(Join-Path $root 'assets\setup-loop.mp4')" (Join-Path $root 'installer\VIP1132.iss')
    Get-ChildItem -LiteralPath $dist -Filter '*.tmp' -File -ErrorAction SilentlyContinue | Remove-Item -Force
}

Write-Host "Build complete: $dist"
