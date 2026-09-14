param(
    [string]$Version,
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($PSScriptRoot)
$rootPrefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$sourceVersion = & (Join-Path $root 'scripts\Get-ReleaseVersion.ps1')
if ($PSBoundParameters.ContainsKey('Version') -and $Version -cne $sourceVersion) {
    throw "Requested version '$Version' must match Directory.Build.props version '$sourceVersion'."
}
$Version = $sourceVersion
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
if (-not $OutputRoot.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputRoot must be a directory inside the project: $OutputRoot"
}
$appProject = Join-Path $root 'src\CodexQuotaMonitor.Wpf\CodexQuotaMonitor.Wpf.csproj'
$installerProject = Join-Path $root 'installer\CodexQuotaMonitor.Installer.wixproj'
$appOutput = Join-Path $OutputRoot 'win-x64-self-contained'
$installerOutput = Join-Path $OutputRoot 'installer'
$releaseOutput = Join-Path $OutputRoot 'release'

function Remove-ProjectDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the project: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

foreach ($path in @($appOutput, $installerOutput, $releaseOutput)) {
    Remove-ProjectDirectory -Path $path
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

try {
    $publishArgs = @(
        'publish', $appProject,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:Version=$Version",
        '-o', $appOutput
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Application publish failed with exit code $LASTEXITCODE."
    }

    $installerArgs = @(
        'build', $installerProject,
        '-c', 'Release',
        "-p:ProductVersion=$Version",
        "-p:PublishDir=$appOutput\",
        '-o', $installerOutput
    )
    & dotnet @installerArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed with exit code $LASTEXITCODE."
    }

    $target = Join-Path $installerOutput "CodexQuotaMonitor-Setup-$Version.msi"
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "Installer output was not created at the expected path: $target"
    }

    Get-ChildItem -LiteralPath $installerOutput -Filter '*.wixpdb' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}
finally {
    foreach ($path in @(
        (Join-Path $root 'src\CodexQuotaMonitor.Wpf\bin'),
        (Join-Path $root 'src\CodexQuotaMonitor.Wpf\obj'),
        (Join-Path $root 'installer\bin'),
        (Join-Path $root 'installer\obj')
    )) {
        Remove-ProjectDirectory -Path $path
    }
}

Write-Output "Application: $(Join-Path $appOutput 'CodexQuotaMonitor.Wpf.exe')"
Write-Output "Installer:   $target"

Copy-Item -LiteralPath (Join-Path $appOutput 'CodexQuotaMonitor.Wpf.exe') -Destination $releaseOutput
Copy-Item -LiteralPath $target -Destination $releaseOutput
$checksums = foreach ($file in Get-ChildItem -LiteralPath $releaseOutput -File | Sort-Object Name) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
$checksums | Set-Content -LiteralPath (Join-Path $releaseOutput 'SHA256SUMS') -Encoding ascii
& (Join-Path $root 'scripts\Test-ReleasePackage.ps1') -Path $releaseOutput -Version $Version
Write-Output "Release assets: $releaseOutput"
