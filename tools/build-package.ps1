param(
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"

if ($Version.StartsWith("v")) {
    $Version = $Version.Substring(1)
}

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Project = Join-Path $Root "src\Jellyfin.Plugin.Ddys\Jellyfin.Plugin.Ddys.csproj"
$PackageDir = Join-Path $Root "package\ddys-jellyfin"
$ReleaseDirPath = Join-Path $Root "..\..\releases"
New-Item -ItemType Directory -Force -Path $ReleaseDirPath | Out-Null
$ReleaseDir = (Resolve-Path $ReleaseDirPath).Path
$Zip = Join-Path $ReleaseDir ("ddys-jellyfin-v{0}.zip" -f $Version)

$workspaceRoot = (Resolve-Path (Join-Path $Root "..\..\..")).Path
$localDotnet = Join-Path $workspaceRoot ".dotnet-sdk-9\dotnet.exe"
if (Test-Path -LiteralPath $localDotnet -PathType Leaf) {
    $dotnet = $localDotnet
} else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        throw "dotnet SDK 9 is required to build the Jellyfin plugin package."
    }

    $dotnet = $dotnetCommand.Source
}

$sdkList = & $dotnet --list-sdks
if (-not ($sdkList -match "^9\.")) {
    throw "dotnet SDK 9 is required to build the Jellyfin plugin package."
}

function Assert-InRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Base
    )

    $full = [System.IO.Path]::GetFullPath($Path)
    $baseFull = [System.IO.Path]::GetFullPath($Base).TrimEnd("\") + "\"
    if (-not $full.StartsWith($baseFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside project root: $full"
    }
}

Assert-InRoot -Path $PackageDir -Base $Root
if (Test-Path -LiteralPath $PackageDir) {
    Remove-Item -LiteralPath $PackageDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null
New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

& $dotnet publish $Project -c Release -o $PackageDir -p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Get-ChildItem -LiteralPath $PackageDir -File |
    Where-Object { $_.Name -notin @("Jellyfin.Plugin.Ddys.dll", "Jellyfin.Plugin.Ddys.pdb") } |
    Remove-Item -Force

$metaVersion = if ($Version -match "^\d+\.\d+\.\d+$") { "$Version.0" } else { $Version }
$meta = [ordered]@{
    name = "低端影视 DDYS"
    guid = "1bb6d203-7ff2-40c1-a0b6-7f8355120b61"
    version = $metaVersion
    targetAbi = "10.11.0.0"
    framework = "net9.0"
    overview = "低端影视 API 的官方 Jellyfin Server 频道插件"
    category = "Channels"
    owner = "ddysiodev"
    artifacts = @("Jellyfin.Plugin.Ddys.dll")
}
$meta | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PackageDir "meta.json") -Encoding UTF8

Copy-Item -LiteralPath (Join-Path $Root "README.md") -Destination (Join-Path $PackageDir "README.md") -Force
Copy-Item -LiteralPath (Join-Path $Root "LICENSE") -Destination (Join-Path $PackageDir "LICENSE") -Force

if (Test-Path -LiteralPath $Zip) {
    Remove-Item -LiteralPath $Zip -Force
}

Compress-Archive -Path (Join-Path $PackageDir "*") -DestinationPath $Zip -Force
$Hash = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash

[pscustomobject]@{
    ok = $true
    package = $Zip
    sha256 = $Hash
} | ConvertTo-Json -Depth 3
