param(
    [string]$RevitYear = "2025",
    [string]$Configuration = "Release",
    [string]$SourcePath,
    [switch]$AllUsers
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot

if (-not $SourcePath) {
    $preferredPackage = Join-Path $projectRoot "bin\OfficeHidraulica"
    $versionedBuildOutput = Join-Path $projectRoot "bin\$Configuration\$RevitYear"
    $buildOutput = Join-Path $projectRoot "bin\$Configuration"

    if (Test-Path (Join-Path $versionedBuildOutput "RevitSlopePlugin.dll")) {
        $SourcePath = $versionedBuildOutput
    }
    elseif (Test-Path (Join-Path $buildOutput "RevitSlopePlugin.dll")) {
        $SourcePath = $buildOutput
    }
    elseif (Test-Path (Join-Path $preferredPackage "RevitSlopePlugin.dll")) {
        $SourcePath = $preferredPackage
    }
}

$sourceDll = Join-Path $SourcePath "RevitSlopePlugin.dll"
if (-not (Test-Path $sourceDll)) {
    throw "RevitSlopePlugin.dll was not found in '$SourcePath'. Build the project first or pass -SourcePath."
}

if ($AllUsers) {
    $installRoot = Join-Path $env:ProgramData "OfficeHidraulica\RevitSlopePlugin\$RevitYear"
    $addinDir = Join-Path $env:ProgramData "Autodesk\Revit\Addins\$RevitYear"
}
else {
    $installRoot = Join-Path $env:LOCALAPPDATA "OfficeHidraulica\RevitSlopePlugin\$RevitYear"
    $addinDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
}

New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
New-Item -ItemType Directory -Force -Path $addinDir | Out-Null

try {
    Copy-Item -Path (Join-Path $SourcePath "*") -Destination $installRoot -Recurse -Force
}
catch [System.IO.IOException] {
    throw "Nao foi possivel atualizar os arquivos em '$installRoot'. Feche o Revit $RevitYear e execute a instalacao novamente."
}

Get-ChildItem -Path $installRoot -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

$installedDll = Join-Path $installRoot "RevitSlopePlugin.dll"
$addinPath = Join-Path $addinDir "OfficeHidraulica.addin"

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Office - Hidraulica</Name>
    <FullClassName>RevitSlopePlugin.App</FullClassName>
    <Assembly>$installedDll</Assembly>
    <AddInId>CBC0EDAA-8D19-4A25-8BDC-74B4FB29EDA5</AddInId>
    <Description>Aplica inclinacao automatica em redes hidraulicas de esgoto sanitario.</Description>
    <VendorId>RevitSlopePlugin</VendorId>
    <VendorDescription>Plugin para inclinacao automatica de redes de esgoto sanitario</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -Path $addinPath -Value $manifest -Encoding UTF8

Write-Host "Installed plugin files to: $installRoot"
Write-Host "Installed Revit manifest to: $addinPath"
Write-Host "Restart Revit $RevitYear to load the add-in."
