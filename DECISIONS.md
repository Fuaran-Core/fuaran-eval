# Decisions

Build-time decisions and the reasons for them. Newest at the bottom. A decision is recorded here
when a later reader would otherwise have to guess whether something was a choice or an accident.

---

## D1 — The eval substrate is its own repository

**2026-08-22.**

These five modules were source files inside the project that first needed them. That arrangement
has a failure mode which is invisible while it is happening: the line between "the harness" and
"the parts of the harness any harness would want" is whatever the two happen to agree on today, it
is never written down, and it moves a little with every change. Nothing breaks, so nothing
complains — until the day a second domain wants a harness and discovers there was never a boundary
at all, only a habit.

A package boundary makes the line a real object. It has a name, a version, and a rule: **a change
to the public surface advances the version in the same commit that makes it.** That rule is
enforceable in a way "please keep the generic parts generic" is not.

The move was mechanically cheap, and that is the argument for doing it *now* rather than when the
second consumer arrives. The library holds **zero project references** and compiles against
`FSharp.Core` and the framework alone, so it is the root of the dependency graph and nothing had to
be untangled to lift it out. That property was not free; it was arranged in advance by a preceding
partition pass, and this extraction is the return on it.

## D2 — The dependency list is a defended property, not a coincidence

**2026-08-22.**

`FSharp.Core` and the framework. No project references, no substrate packages, nothing else.

It is tempting to read that as "this library happens not to need much yet". It is the other way
round: the point of a substrate is that a harness can adopt it without inheriting the other
assumptions its existing consumers hold, and every dependency added here is one an adopter must
satisfy before it can begin. The census reads JSON with the in-box reader rather than a nicer one
for exactly this reason.

**Do not add a dependency to save twenty lines.** If a change genuinely needs an effect, take it as
a function value the caller supplies.

## D3 — The substrate carries no gate, and this is the load-bearing boundary

**2026-08-22.**

A gate decides whether an emission is valid. That decision belongs to the domain whose language is
being emitted: its own shipped validators *are* its gate, and there is one definition of "valid" in
a domain, never a second weaker one shipped by a library that has never seen the language.

So what this library models is the **naming** of a gate — `GateIdentity`, and the list of them a
`ProvenanceStamp` carries — and never the judging. `EvalProvenance`'s whole argument is that a
stored label is a claim about *one* gate and must say which; a substrate that also supplied a gate
would be inviting every adopter to hold two answers to the same question.

The same line explains an asymmetry a reader might otherwise take for an oversight: `IEvalProvider`
is a seam the substrate defines and a domain fills, while the gate is not a seam here at all. A
seam implies the substrate orchestrates the thing behind it. It does not orchestrate gates; it
records which one spoke.

`GateVocabularyTests` reads the sources and fails on the vocabulary a validator would need, so this
is a checked property rather than a preference stated in a document.

## D4 — A clean copy, with the origin recorded, rather than preserved history

**2026-08-22.**

The sibling extractions this repository is modelled on preserved their sources' commit history
through the move. This one did not, and the reason is specific rather than a shortcut.

These five files were **created hours earlier** by the partition pass that made the extraction
possible — carved out of three much older modules whose history stays with them, because those
modules do not move. So the history available to preserve was a single commit, and it was not the
history a reader would actually want: the interesting past of `ProvenanceStamp` is inside the
provenance work that predates the file it now lives in, and no rewriting of this repository's log
could carry that across.

The files were therefore copied in whole from the partition at commit `32a6d8c5`, with two classes
of edit and no others:

1. **Doc comments were rewritten** where they referenced the partition pass, the numbering of a
   planning system, or the files and types of the project they were lifted from. The *semantics* of
   every comment are preserved; what changed is that they no longer point at things a reader of this
   repository cannot look up. See D5.
2. **Nothing else.** No type, member, signature, literal or printed string moved. That is checked
   rather than asserted: the first consumer's census and re-gate outputs are byte-identical across
   the repin, which is a stronger statement than a diff, because it covers the strings the
   compiler never sees.

**The printed strings are byte-pinned and are not a tidy-up target.** Some of them carry vocabulary
that reads as domain-specific — a tier letter in a section heading, a document path in a hint line.
Rewording them is a behaviour change to every consumer's output, not a cleanup, and would break the
equivalence this move rests on. They are noted as a genuine seam gap in D7.

## D5 — The licence is deliberately unset

**2026-08-22.**

There is no `LICENSE` file and no `PackageLicenseExpression` in the build properties. This is not an
oversight and should not be "fixed" by inference.

The repository is written **to a public standard** — no reference to any private counterpart in any
file, contribution documentation that assumes an outside reader, a README that stands on its own —
precisely so that choosing a licence later is a one-line act rather than a rewrite. But choosing it
is a decision with consequences that outlive the code, and a decision like that should be *taken*,
explicitly, by someone entitled to take it. It should not arrive as a side effect of a file-move
because the template had a slot for it.

Until then: all rights reserved, and the standard the code is written to is a promise about
*effort*, not about terms.

`gates/check-publication-boundary.ps1` enforces the standard over the tracked file set on every run
and prints the residue it knowingly carries even when green, because a standard nobody checks decays
one convenient reference at a time and by the time anyone looks the tidy-up is a rewrite.

## D6 — The feed path reaches outside the repository, knowingly

**2026-08-22.**

`nuget.config` declares a folder source at `..\..\local-nuget-feed`. Nothing in this repository can
enforce that path, and a fresh clone does not satisfy it.

It is the one concession to the development loop that packs this library and consumes it locally,
and it is listed by the publication-boundary gate on every run rather than excused quietly. A
publication replaces it with a real feed; until then the honest statement is that this repository
builds standalone and *packs* into somewhere it does not own.

## D7 — Two seam gaps the extraction found, recorded rather than fixed

**2026-08-22.**

The move surfaced two places where the substrate is less generic than its types claim. Both are
recorded here rather than repaired, because repairing either changes observable output and would
have to be a versioned change with its consumers moving in step.

1. **Report headings and hint lines carry domain vocabulary.** `DemandCensus.printReport` prints a
   tier letter in one section heading, names two diagnostic track kinds in another, and points at
   two document paths in its drafting and coverage hints. Those are the first adopting domain's
   conventions baked into the engine's output. The clean shape is a small record of report labels
   supplied alongside `DomainCensusSeam` — deliberately not `DomainCensusSeam` itself, since a new
   required field there is a breaking change for every adopter, and this one is presentational.

2. **The provider seam has one binding and therefore one shape.** `EvalCompletion`'s six usage
   counters are the arithmetic every cost model needs, which is why they are here rather than beside
   the binding. But they were chosen against one family of providers, and a second binding is the
   only thing that can tell a general shape from a well-generalised specific one. Until that binding
   exists, treat the record as provisional: adding a counter is additive and cheap, and renaming one
   is not.

Neither is a defect in the partition. They are the residue any first extraction carries, and naming
them is what stops the next reader mistaking them for design.

## D8 — Membership of the shared substrate version cohort is an open question

**2026-08-22, surfaced and deliberately not decided.**

The packages this library sits beside move as a system: when one cuts a new version, every other
member that pins it is raised to that version in the same sweep. Whether `Fuaran.Eval.*` joins that
set is a genuine question with arguments on both sides — it is substrate, which argues for; it is
consumed by evaluation harnesses that adopt deliberately and may reasonably lag, which argues
against — and it is not a question this extraction is entitled to answer by acting.

**Surfaced to the operator on 2026-08-22. The default pending that decision is: NOT a member.** No
membership list was edited in either direction. Recorded here so that a later reader finds a
decision that was deferred rather than an omission, and so that the default is a choice with a date
on it rather than the state nobody looked at.
