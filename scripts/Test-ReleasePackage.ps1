param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = 'Stop'
$exeName = 'CodexQuotaMonitor.Wpf.exe'
$msiName = "CodexQuotaMonitor-Setup-$Version.msi"
$expectedNames = @($exeName, $msiName, 'SHA256SUMS') | Sort-Object
$actualNames = @(Get-ChildItem -LiteralPath $Path -Force | Select-Object -ExpandProperty Name | Sort-Object)
if (Compare-Object $expectedNames $actualNames) { throw 'Release must contain exactly the EXE, MSI and SHA256SUMS.' }

$exe = Join-Path $Path $exeName
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if ($fileVersion.FileVersion -ne "$Version.0" -or $fileVersion.ProductVersion.Split('+')[0] -ne $Version) {
    throw "EXE version does not match $Version."
}

# Read MSI metadata without installing it or invoking any installer custom action.
$installer = $database = $view = $record = $null
try {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase((Join-Path (Resolve-Path -LiteralPath $Path).Path $msiName), 0)
    foreach ($entry in @(
        @{ Name = 'ProductVersion'; Value = $Version },
        @{ Name = 'UpgradeCode'; Value = '{F5D76B33-171C-4C7C-9B73-4C0F4D9D4A95}' }
    )) {
        $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$($entry.Name)'")
        $view.Execute()
        $record = $view.Fetch()
        if ($null -eq $record -or $record.StringData(1) -ne $entry.Value) {
            throw "MSI $($entry.Name) does not match $($entry.Value)."
        }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        $record = $null
        $view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        $view = $null
    }
}
finally {
    foreach ($item in @($record, $view, $database, $installer)) {
        if ($null -ne $item) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($item) }
    }
}

$expectedHashes = foreach ($name in @($exeName, $msiName) | Sort-Object) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $Path $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
$actualHashes = @(Get-Content -LiteralPath (Join-Path $Path 'SHA256SUMS'))
if (Compare-Object $expectedHashes $actualHashes) { throw 'SHA256SUMS does not match the release payloads.' }
Write-Output "Verified EXE/MSI version $Version, MSI upgrade identity and SHA256SUMS."
