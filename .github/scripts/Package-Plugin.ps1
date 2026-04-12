param(
    [Parameter(Mandatory = $true)]
    [string]$PluginName,

    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"

$publishDir = Join-Path $OutputRoot $PluginName
$zipPath = Join-Path $OutputRoot "$PluginName.zip"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

dotnet publish $ProjectPath -c Release -o $publishDir

$pluginDll = Join-Path $publishDir "$PluginName.dll"
if (-not (Test-Path $pluginDll)) {
    throw "Expected plugin assembly was not found: $pluginDll"
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host "Created package: $zipPath"
