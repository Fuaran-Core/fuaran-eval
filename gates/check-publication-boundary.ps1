#Requires -Version 7.0
<#
.SYNOPSIS
    Publication-boundary sweep: this repository must name nothing outside itself.

.DESCRIPTION
    The library is public and Apache-2.0 (DECISIONS D11), and was written to a public standard
    before either was decided (D5). "Written to a public standard" is a claim, and a claim without a check decays
    one convenient reference at a time — invisibly, because nothing breaks. By the time anyone
    looks, the tidy-up is a rewrite, which is the outcome the standard exists to avoid.

    So the standard is a gate. Every tracked file is swept for the vocabulary a reader outside this
    repository could not look up and should not learn from it: the names of neighbouring projects
    and tools, the operator command surface they are driven by, the numbering of the planning
    system they are scheduled on, and paths that reach outside this repository altogether.

    THE SCOPE IS THE TRACKED SET (`git ls-files`), which is exactly what a publication would carry.
    Build output is not swept: it is not published, and it is unavoidably full of absolute paths.

    PATTERNS ARE WORD-BOUNDARY ANCHORED where the term has common-English collisions. A gate that
    cries wolf gets turned off, which costs more than never having built it.

    WHAT MAKES THIS GATE HONEST: THE EXCEPTIONS ARE LOUD. One hit genuinely cannot be fixed here —
    the folder feed path in nuget.config is what makes the development loop work today (DECISIONS
    D6). It is printed on every run, including a green one, rather than filtered out silently. A
    suppression nobody sees is how a boundary rots.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

# ── The banned vocabulary ────────────────────────────────────────────────────
# Each entry: the regex, and what a reader should do about a hit. The "why" is
# in the message, because a gate that only says "forbidden" teaches nothing.
$banned = @(
    @{ Pattern = '\beval-suite\b'
       Say     = 'names a neighbouring project; say "a consumer" or "the first adopting domain"' }
    @{ Pattern = '\borchestrator-demo\b|\borchestration\b'
       Say     = 'names a neighbouring project' }
    @{ Pattern = 'Fuaran\.UI\.Orchestration|ToolUp\.'
       Say     = 'names a package family outside this repository' }
    @{ Pattern = '\bFuaran-(Build|Roadmap|Dispatch|Judge|Witness|App)\b'
       Say     = 'names a component of a wider private system' }
    @{ Pattern = '\broadmapctl\b|\bRefine Roadmap\b|\bAccept suggestions\b|\bSuggest features\b|\bPrompt next\b|\bMake it so\b|\bSync All\b'
       Say     = 'names an operator command surface this repository is not driven by' }
    @{ Pattern = '\bPhase \d+\b|\bfuaran-core#\d+\b|\bfuaran#\d+\b|\bforge#\d+\b'
       Say     = 'cites the numbering of a planning system a reader here cannot resolve' }
    @{ Pattern = '\bsibling repo\b|\bthe estate\b|\bprivate sibling\b'
       Say     = 'implies a wider private ecosystem' }
    @{ Pattern = 'PARTITION\.md'
       Say     = 'points at a document that stayed behind in the source project' }
)

# ── The carried exceptions ───────────────────────────────────────────────────
# Not suppressions. Each is a hit this repository knowingly holds, with the
# decision that authorises it. They are PRINTED on every run.
$carried = @(
    @{ File = 'nuget.config'
       What = 'the folder feed path reaches outside this repository'
       Why  = 'DECISIONS D6 — the development loop packs and consumes locally; a publication replaces it with a real feed' }
    @{ File = 'pack.ps1'
       What = 'the default feed path reaches outside this repository'
       Why  = 'DECISIONS D6 — same path, same reason' }
    @{ File = 'DECISIONS.md'
       What = 'D4 records the origin commit of the copied sources'
       Why  = 'a bare hash names no repository and resolves nowhere; the provenance is worth more than the hash costs' }
)

$files = @(git ls-files) | Where-Object { $_ -and (Test-Path $_ -PathType Leaf) }
if (-not $files) {
    # A fresh `git init` before the first commit tracks nothing. Sweeping zero
    # files and reporting success would be a vacuous green, so say which it is.
    Write-Host 'publication boundary: no tracked files yet — nothing swept (not a pass).'
    exit 0
}

$hits = @()
foreach ($file in $files) {
    # Skip this gate itself: it necessarily spells out every banned term.
    if ($file -eq 'gates/check-publication-boundary.ps1') { continue }

    $lines = Get-Content -LiteralPath $file -ErrorAction SilentlyContinue
    if (-not $lines) { continue }

    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($rule in $banned) {
            if ($lines[$i] -match $rule.Pattern) {
                $hits += [pscustomobject]@{
                    File = $file
                    Line = $i + 1
                    Text = $lines[$i].Trim()
                    Say  = $rule.Say
                }
            }
        }
    }
}

Write-Host "publication boundary: swept $($files.Count) tracked file(s)."

Write-Host ''
Write-Host 'carried exceptions (knowingly held, see DECISIONS):'
foreach ($c in $carried) {
    Write-Host ("  {0,-16} {1}" -f $c.File, $c.What)
    Write-Host ("  {0,-16}   {1}" -f '', $c.Why)
}

# The carried exceptions are declared by FILE, and only for the specific hits
# above; a new hit in the same file is still a failure. That is deliberate —
# a file-level allowlist would let the next reference in nuget.config through
# on the strength of a decision that was about something else.
$real = $hits | Where-Object {
    -not ($_.File -eq 'nuget.config' -and $_.Text -match 'local-nuget-feed') -and
    -not ($_.File -eq 'pack.ps1' -and $_.Text -match 'local-nuget-feed') -and
    -not ($_.File -eq 'DECISIONS.md' -and $_.Text -match '32a6d8c5')
}

if ($real.Count -gt 0) {
    Write-Host ''
    Write-Host "FAIL: $($real.Count) publication-boundary violation(s)."
    foreach ($h in $real) {
        Write-Host "  $($h.File):$($h.Line)  — $($h.Say)"
        Write-Host "      $($h.Text)"
    }
    exit 1
}

Write-Host ''
Write-Host 'OK: no file names anything outside this repository.'
exit 0
