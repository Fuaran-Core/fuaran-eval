# Pack Fuaran.Eval.Core into the shared local folder feed.
#
# The feed path reaches outside this repository (see nuget.config and DECISIONS D6). That is the
# one concession to the development loop; a publication replaces it with a real feed.
#
#   pwsh ./pack.ps1                 pack into ../../local-nuget-feed
#   pwsh ./pack.ps1 -Feed <path>    pack elsewhere
#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $Feed = (Join-Path $PSScriptRoot '../../local-nuget-feed'),
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Test-Path $Feed)) { New-Item -ItemType Directory -Force $Feed | Out-Null }
$Feed = (Resolve-Path $Feed).Path

dotnet pack src/Fuaran.Eval.Core/Fuaran.Eval.Core.fsproj -c $Configuration -o $Feed --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "packed to $Feed"
