# Fuaran.Eval

The generic substrate an evaluation harness sits on: **what a published figure carries so it can
travel**, **what a harness needs from a model provider**, and **the census that turns a cohort's
failures into demand evidence**. It is a pure library — no host, no process, no clock it was not
given — and it holds no gate.

```
dotnet add package Fuaran.Eval.Core
```

## What is here

| Module | What it is |
|---|---|
| `Fuaran.Eval.Core.EvalProvenance` | the provenance-stamp shapes — cohort id, harness commit, prompt hash, the gate identities that produced the labels, and the wire decoder version named separately from them |
| `Fuaran.Eval.Core.EvalProvider` | the provider seam — `EvalMessage` / `EvalCompletion` / `IEvalProvider`, plus the fake-replay posture that refuses on a miss rather than generating |
| `Fuaran.Eval.Core.DemandSidecar` | the demand-loop data shapes — the cell slice a census reads, the intake-ledger row, the re-gate sidecar row, and the cluster |
| `Fuaran.Eval.Core.DemandSeams` | `DomainCensusSeam` — everything the census needs from a domain, and by construction everything about the census that is *not* transferable |
| `Fuaran.Eval.Core.DemandCensus` | the census engine, generic over that seam: cluster the failures, diff them against the intake ledger, and fail the run on a repeated cluster nobody wrote down |

## The one idea worth reading before the code

**A stored pass/fail label is a claim about one gate.** A harness that gates several arms — a
decoder, a bundler, a type-checker, a build — and stamps them all with one version attributes a
label to a toolchain that never saw the emission. So `ProvenanceStamp` carries a *list* of gate
identities and a separately-named decoder version, and every field is allowed to be empty because
empty means **unstamped**, never "fine". A refusal has to be able to tell "no stamp" from "a
foreign stamp".

The same instinct runs through the rest. `EvalProvider.replay` fails on a miss rather than
inventing a completion, because a fake that silently generates turns a replay run into an
unlabelled live run — indistinguishable from the real thing in the result file, and the one failure
a replay harness must not be able to have. `RegateRow` carries both the recorded label and the
fresh one, because the *delta* is the evidence and a shape that stored only the correction would
destroy the proof that anything moved.

## What is deliberately absent

**There is no gate here.** A gate is the thing that decides whether an emission is valid, and that
decision belongs to the domain whose language is being emitted — its own shipped validators *are*
its gate. A substrate that shipped a second, weaker definition of "valid" would give every adopting
domain two, which is worse than none. What this library models is the *naming* of a gate
(`GateIdentity`), never the judging.

**There is no domain vocabulary.** No tier letters, no condition names, no corpus layout. Where the
census needs one — a task-id grammar, the own-language predicate, the criterion-verdict source, an
adversarial-tier marker, a probe-corpus marker — it takes it as a field of `DomainCensusSeam`. A
domain with no such structure supplies `DemandSeams.minimal` and loses only the exclusions it does
not have.

## Standing it up in a new domain

```fsharp
open Fuaran.Eval.Core

let seam: DemandSeams.DomainCensusSeam =
    { DemandSeams.minimal "my_language" with
        TaskShortOf = fun id -> id.Split('-') |> Array.last
        IsAdversarialTierTask = fun id -> id.StartsWith "bait-" }

let report = DemandCensus.runCensus seam [ "results" ] None "docs/DEMAND-LOG.md" None
exit (DemandCensus.printReport seam 2 false report)
```

`runCensus` reads stored result files and nothing else — it spends no provider tokens and opens no
network surface, which is what lets it run on every change rather than at cohort close-out.

## Building

```
pwsh ./run.ps1
```

Tool restore, Fantomas, the publication-boundary sweep, build, and the full suite. See
[CONTRIBUTING.md](CONTRIBUTING.md); the decisions and their reasons are in
[DECISIONS.md](DECISIONS.md).
