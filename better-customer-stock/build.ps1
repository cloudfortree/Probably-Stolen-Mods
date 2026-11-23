[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$gamePath = (Resolve-Path -LiteralPath $GameDir).ProviderPath
Push-Location -LiteralPath $PSScriptRoot
try {
    dotnet build Mod.csproj --configuration $Configuration "-p:GameDir=$gamePath"
    if ($LASTEXITCODE -ne 0) { throw 'Better Customer Stock compilation failed.' }
} finally {
    Pop-Location
}
