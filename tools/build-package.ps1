param(
    [string]$Version = "",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$Root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$PackageJson = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $Root "package.json") | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]$PackageJson.version
}
if ($Version.StartsWith("v")) {
    $Version = $Version.Substring(1)
}

if (-not [string]::IsNullOrWhiteSpace($OutputDir)) {
    if ([System.IO.Path]::IsPathRooted($OutputDir)) {
        $ReleaseDirPath = $OutputDir
    } else {
        $ReleaseDirPath = Join-Path $Root $OutputDir
    }
} else {
    $LocalReleaseDirPath = Join-Path $Root "..\..\releases"
    if (Test-Path -LiteralPath (Join-Path $Root "..\..\scripts\github-upload-project.ps1")) {
        $ReleaseDirPath = $LocalReleaseDirPath
    } else {
        $ReleaseDirPath = Join-Path $Root "releases"
    }
}

New-Item -ItemType Directory -Force -Path $ReleaseDirPath | Out-Null
$ReleaseDir = (Resolve-Path -LiteralPath $ReleaseDirPath).Path
$Zip = Join-Path $ReleaseDir ("ddys-jellyfin-v{0}.zip" -f $Version)
$ShaFile = "$Zip.sha256"
$Project = Join-Path $Root "src\Jellyfin.Plugin.Ddys\Jellyfin.Plugin.Ddys.csproj"
$PackageDir = Join-Path $Root "package\ddys-jellyfin"
$LegacyPackageRoot = Join-Path $Root "package"

function Assert-InRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Base
    )

    $separator = [System.IO.Path]::DirectorySeparatorChar
    $full = [System.IO.Path]::GetFullPath($Path)
    $baseFull = [System.IO.Path]::GetFullPath($Base).TrimEnd([char[]]@("\", "/")) + $separator
    if (-not $full.StartsWith($baseFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside project root: $full"
    }
}

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)][string]$Base,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $separator = [System.IO.Path]::DirectorySeparatorChar
    $basePath = [System.IO.Path]::GetFullPath($Base).TrimEnd([char[]]@("\", "/")) + $separator
    $baseUri = New-Object System.Uri($basePath)
    $fileUri = New-Object System.Uri([System.IO.Path]::GetFullPath($Path))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($fileUri).ToString()).Replace("/", $separator)
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Output
    )

    if (Test-Path -LiteralPath $Output) {
        Remove-Item -LiteralPath $Output -Force
    }

    if (-not ("DdysZipCrc32" -as [type])) {
        Add-Type -TypeDefinition @"
public static class DdysZipCrc32 {
    public static uint Compute(byte[] bytes) {
        uint crc = 0xffffffffu;
        for (int i = 0; i < bytes.Length; i++) {
            uint value = (crc ^ bytes[i]) & 0xffu;
            for (int bit = 0; bit < 8; bit++) {
                value = ((value & 1u) != 0u) ? (0xedb88320u ^ (value >> 1)) : (value >> 1);
            }
            crc = (crc >> 8) ^ value;
        }
        return crc ^ 0xffffffffu;
    }
}
"@
    }

    $utf8 = [System.Text.Encoding]::UTF8
    $fixedDosTime = [uint16]0x0000
    $fixedDosDate = [uint16]0x5c21
    $generalPurposeFlagUtf8 = [uint16]0x0800
    $storedMethod = [uint16]0
    $centralEntries = New-Object System.Collections.Generic.List[object]
    $packageFiles = New-Object System.Collections.Generic.List[object]

    foreach ($file in (Get-ChildItem -LiteralPath $Source -Recurse -Force -File)) {
        $relative = (Get-RelativePathCompat -Base $Source -Path $file.FullName).Replace("\", "/")
        if ($file.Name -match "\.(log|tmp|cache|zip|tgz|sha256)$") { continue }
        [void]$packageFiles.Add([pscustomobject]@{
            File = $file
            Relative = $relative
        })
    }

    $packageFiles.Sort([System.Comparison[object]]{
        param($left, $right)
        return [System.StringComparer]::Ordinal.Compare([string]$left.Relative, [string]$right.Relative)
    })

    $stream = [System.IO.File]::Open($Output, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    $writer = $null
    try {
        $writer = [System.IO.BinaryWriter]::new($stream, $utf8, $false)
        foreach ($packageFile in $packageFiles) {
            $file = $packageFile.File
            $relative = $packageFile.Relative
            $nameBytes = $utf8.GetBytes($relative)
            $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
            if ($bytes.LongLength -gt [uint32]::MaxValue) {
                throw "File too large for deterministic ZIP32 package: $relative"
            }
            if ($nameBytes.Length -gt [uint16]::MaxValue) {
                throw "File name too long for ZIP package: $relative"
            }

            $offset = [uint32]$writer.BaseStream.Position
            $size = [uint32]$bytes.Length
            $crc = [DdysZipCrc32]::Compute($bytes)

            $writer.Write([uint32]0x04034b50)
            $writer.Write([uint16]20)
            $writer.Write($generalPurposeFlagUtf8)
            $writer.Write($storedMethod)
            $writer.Write($fixedDosTime)
            $writer.Write($fixedDosDate)
            $writer.Write([uint32]$crc)
            $writer.Write($size)
            $writer.Write($size)
            $writer.Write([uint16]$nameBytes.Length)
            $writer.Write([uint16]0)
            $writer.Write($nameBytes)
            $writer.Write($bytes)

            [void]$centralEntries.Add([pscustomobject]@{
                NameBytes = $nameBytes
                Crc = [uint32]$crc
                Size = $size
                Offset = $offset
            })
        }

        if ($centralEntries.Count -gt [uint16]::MaxValue) {
            throw "Too many files for deterministic ZIP32 package."
        }

        $centralOffset = [uint32]$writer.BaseStream.Position
        foreach ($entry in $centralEntries) {
            $writer.Write([uint32]0x02014b50)
            $writer.Write([uint16]20)
            $writer.Write([uint16]20)
            $writer.Write($generalPurposeFlagUtf8)
            $writer.Write($storedMethod)
            $writer.Write($fixedDosTime)
            $writer.Write($fixedDosDate)
            $writer.Write([uint32]$entry.Crc)
            $writer.Write([uint32]$entry.Size)
            $writer.Write([uint32]$entry.Size)
            $writer.Write([uint16]$entry.NameBytes.Length)
            $writer.Write([uint16]0)
            $writer.Write([uint16]0)
            $writer.Write([uint16]0)
            $writer.Write([uint16]0)
            $writer.Write([uint32]0)
            $writer.Write([uint32]$entry.Offset)
            $writer.Write($entry.NameBytes)
        }
        $centralSize = [uint32]($writer.BaseStream.Position - $centralOffset)

        $writer.Write([uint32]0x06054b50)
        $writer.Write([uint16]0)
        $writer.Write([uint16]0)
        $writer.Write([uint16]$centralEntries.Count)
        $writer.Write([uint16]$centralEntries.Count)
        $writer.Write($centralSize)
        $writer.Write($centralOffset)
        $writer.Write([uint16]0)
    } finally {
        if ($null -ne $writer) {
            $writer.Dispose()
        } else {
            $stream.Dispose()
        }
    }

    return $packageFiles.Count
}

$workspaceRoot = (Resolve-Path -LiteralPath (Join-Path $Root "..\..\..")).Path
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

Assert-InRoot -Path $LegacyPackageRoot -Base $Root
if (Test-Path -LiteralPath $LegacyPackageRoot) {
    Remove-Item -LiteralPath $LegacyPackageRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null

foreach ($path in @($Zip, $ShaFile)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

& $dotnet publish $Project -c Release -o $PackageDir -p:Version=$Version -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" -p:Deterministic=true -p:ContinuousIntegrationBuild=true -p:PathMap="$Root=/_/ddys-jellyfin"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Get-ChildItem -LiteralPath $PackageDir -File |
    Where-Object { $_.Name -notin @("Jellyfin.Plugin.Ddys.dll", "Jellyfin.Plugin.Ddys.pdb") } |
    Remove-Item -Force

$metaVersion = if ($Version -match "^\d+\.\d+\.\d+$") { "$Version.0" } else { $Version }
$metaJson = @"
{
  "name": "\u4f4e\u7aef\u5f71\u89c6 DDYS",
  "guid": "1bb6d203-7ff2-40c1-a0b6-7f8355120b61",
  "version": "$metaVersion",
  "targetAbi": "10.11.0.0",
  "framework": "net9.0",
  "overview": "\u4f4e\u7aef\u5f71\u89c6 API \u7684\u5b98\u65b9 Jellyfin Server \u9891\u9053\u63d2\u4ef6",
  "category": "Channels",
  "owner": "ddysiodev",
  "artifacts": [
    "Jellyfin.Plugin.Ddys.dll"
  ]
}
"@
[System.IO.File]::WriteAllText(
    (Join-Path $PackageDir "meta.json"),
    $metaJson,
    [System.Text.UTF8Encoding]::new($false)
)

Copy-Item -LiteralPath (Join-Path $Root "README.md") -Destination (Join-Path $PackageDir "README.md") -Force
Copy-Item -LiteralPath (Join-Path $Root "LICENSE") -Destination (Join-Path $PackageDir "LICENSE") -Force

$FileCount = New-DeterministicZip -Source $PackageDir -Output $Zip
$Hash = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash
[System.IO.File]::WriteAllText(
    $ShaFile,
    "$Hash  $(Split-Path -Leaf $Zip)",
    [System.Text.Encoding]::ASCII
)

[pscustomobject]@{
    ok = $true
    package = $Zip
    sha256 = $Hash
    shaFile = $ShaFile
    files = $FileCount
} | ConvertTo-Json -Depth 3
