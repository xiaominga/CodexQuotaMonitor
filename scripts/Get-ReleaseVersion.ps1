param([string]$Tag)

$ErrorActionPreference = 'Stop'
[xml]$properties = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "Version must be a canonical major.minor.patch number: $version"
}
$parts = $version.Split('.')
if ([decimal]$parts[0] -gt 255 -or [decimal]$parts[1] -gt 255 -or [decimal]$parts[2] -gt 65535) {
    throw "Version exceeds Windows Installer limits (255.255.65535): $version"
}
if ($PSBoundParameters.ContainsKey('Tag') -and $Tag -cne "v$version") {
    throw "Tag '$Tag' must match Directory.Build.props version 'v$version'."
}
Write-Output $version
