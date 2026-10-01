# fuaran-eval — "drop into the repository, run one command, the thing works".
# Stage-0 shape (library + tests only):
#   tool restore -> Fantomas -> publication boundary -> build -> Expecto runner.
#
#   pwsh ./run.ps1                 full pass
#   pwsh ./run.ps1 -SkipFormat     skip the Fantomas pass
#   pwsh ./run.ps1 -SkipBuild      skip the build (implies a prior build)
#   pwsh ./run.ps1 -SkipTests      skip the Expecto suite
#
# The publication-boundary sweep runs with the ordinary gate rather than beside it, and there is
# deliberately NO switch to skip it. This repository is public and Apache-2.0 (DECISIONS D11), and
# was written to that standard before either was decided (D5); a standard nobody checks decays
# one convenient reference at a time until the tidy-up is a rewrite. It is fast, it needs no
# build, and it prints the residue it knowingly carries on every run — including a green one.
#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $SkipFormat,
    [switch] $SkipBuild,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not $SkipFormat) {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet fantomas --check src tests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& (Join-Path $PSScriptRoot 'gates/check-publication-boundary.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipBuild) {
    dotnet build Fuaran.Eval.slnx --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SkipTests) {
    # The built test assembly is invoked DIRECTLY rather than through `dotnet run --project`,
    # which has been observed to hang before the suite starts.
    $testDll = Join-Path $PSScriptRoot 'tests/Fuaran.Eval.Core.Tests/bin/Debug/net10.0/Fuaran.Eval.Core.Tests.dll'
    if (-not (Test-Path $testDll)) {
        Write-Error "test assembly not found at $testDll — run without -SkipBuild first"
        exit 1
    }
    dotnet $testDll --summary
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
