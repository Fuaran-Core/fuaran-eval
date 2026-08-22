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

**2026-08-22. Superseded the same day by D11 — the licence is Apache-2.0.** The record stays because
it says what the hold was *for*, and D11 is the act it was waiting on.

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

**2026-08-22. Both closed the same day, at 0.2.0 — item 1 by D10, item 2 by D9.** Kept verbatim
because it is the record of what was known before the second binding existed, and D9 is only
interesting against it.

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

## D8 — Not a member of the shared substrate version cohort

**2026-08-22, surfaced. 2026-08-22, DECIDED: not a member.**

The packages this library sits beside move as a system: when one cuts a new version, every other
member that pins it is raised to that version in the same sweep. Whether `Fuaran.Eval.*` joins that
set was a genuine question with arguments on both sides — it is substrate, which argues for; it is
consumed by evaluation harnesses that adopt deliberately and may reasonably lag, which argues
against — and it was not a question the extraction was entitled to answer by acting.

**The decision is: NOT a member.** The deciding argument is the second one, and this release is the
evidence for it: two harnesses consume this library, they are on different release rhythms, and
the version that matters to each is the one whose seam their scorer compiles against — not the one
some third package happened to cut this week. A cohort rule would oblige both to move together for
reasons neither could see in its own tree, which is the cost of cohort membership and buys nothing
here: this library takes no dependency on any cohort member (D2), so it can never be the reason one
of them is behind.

What does NOT change is the producer-side rule, which was never cohort-specific: a change to the
public surface advances `<Version>` in the same commit. That is what 0.2.0 is.

Recorded as a decision with a date on it rather than a default nobody revisited. No membership list
was edited in either direction, because the answer is the one the absence already implied — but an
absence that was checked and an absence that was overlooked look identical, and this is the
difference.

## D9 — The provider result is a type parameter, decided against two bindings

**2026-08-22, 0.2.0. This is the answer D7(2) said only a second binding could give.**

D7(2) recorded that the seam had one binding and therefore one shape, and asked the next reader to
treat `EvalCompletion` as provisional. The second binding arrived, and it disagreed in exactly the
way that could not have been predicted from the first: its provider does not answer in text. It
reduces a response to one of a small closed set of outcomes — one branch per emission kind it
recognises, plus one for "no usable emission" — and the branch *is* the result. Its scorer matches
on the case. Passing that through a seam whose completion carried `OutputText: string` would have
forced it to render the branch into a string and re-derive it on the other side, which is not an
adapter but a lossy encoding with the loss on the side that cares.

So `EvalCompletion<'Result>` and `IEvalProvider<'Result>`. A text-shaped harness instantiates the
parameter at `string` and is back where it started; a branch-shaped one instantiates it at its own
union and keeps every case. **A type parameter is the only construct that carries discrimination
without naming what is discriminated** — which is the constraint this library has to satisfy and
the reason the obvious alternatives were rejected:

- **A tagged payload (`Emitted of tag: string * body: string`) was rejected.** It carries the
  branch, but as a string the domain must re-parse and cannot exhaust over, so the first thing every
  adopter would write is a mapping back to the union it already had — a stringly-typed round trip
  through a library that never reads either end of it.
- **A union in the substrate (`Text | Structured | Failed`) was rejected harder.** Naming the branch
  set is exactly the domain vocabulary D3 keeps out, and the set would be wrong for the third
  binding for the same reason the text shape was wrong for the second.
- **The natural objection to a type parameter is that the substrate can then do nothing with a
  result.** True, and it is not a cost here: nothing in this library reads a completion, and the
  house rule for when something needs to (CONTRIBUTING: "if your change needs an effect, take it as
  a function value the caller supplies") already covers the day one does.

Three consequences worth stating, because each was a deliberate choice inside the change:

1. **The invocation key is caller-supplied.** `replay` used to derive its lookup key from the last
   message's content. That is right for a conversational harness and wrong for a case-driven one,
   whose invocations are identified by case id and whose prompts may repeat verbatim. `EvalRequest`
   now carries `InvocationKey`, and `requestKeyedOnLastMessage` writes the old derivation at the
   call site where a reader can see it being chosen.
2. **Usage is an option, and the counters moved into their own record.** The second binding reports
   no usage at all. Under a required record it would have had to pass zeros, and a cost figure over
   that cohort would have read as measured rather than as absent — the same failure `ProvenanceStamp`
   spends its whole design avoiding. `None` means unreported.
3. **The seam still does not model failure.** A "failed" case belongs to a domain's own result union
   if that domain wants one. Deciding that an emission is bad is a judgement about emitted content,
   and D3 is the standing answer to who makes those.

**The fake-replay refusal survives, and could not have failed to.** Refusing means raising, and
raising needs no value of the type it declines to produce — so a miss is still a failure even for a
domain whose union *could* express a polite one. That is checked by a test that hands `replay` a
branch-shaped result type with a refusal case in it and requires the miss to throw anyway.

## D10 — The report's four domain-worded lines are supplied, not baked

**2026-08-22, 0.2.0. Closes D7(1).**

`DemandCensus.printReport` printed a tier letter in one section heading, two diagnostic track names
in another, and two document paths in its hints. Those were the first adopting domain's conventions,
printed by the engine as if they were the engine's own — invisible to that domain, because they read
correctly there, and wrong for every other.

`ReportLabels` carries the four, supplied as an argument to `printReport`. It is deliberately NOT a
field of `DomainCensusSeam`, per the shape D7(1) named: a new required field on the seam is a
breaking change for every adopter, and these are presentational — a domain that wants the generic
wording should not have to restate the whole census contract to decline them.

`DemandSeams.genericLabels` names nothing, and unlike `DemandSeams.minimal` it *is* a safe default:
a domain adopting it loses specificity, never an exclusion. The first adopting domain now passes its
own labels and its report is byte-identical to the one the baked strings produced — which is the
check that this was a move rather than a rewording.

## D11 — The licence is Apache-2.0

**2026-08-22, superseding D5.**

D5 held the licence unset on the grounds that choosing one is a decision with consequences that
outlive the code, and should be taken explicitly rather than arrive as a side effect of a file move.
It has now been taken: **Apache-2.0**.

The repository was written to a public standard from its first commit for exactly this moment, and
the act was correspondingly small: a `LICENSE`, a `NOTICE`, and one `PackageLicenseExpression`. No
source file changed, no reference had to be scrubbed, and the publication-boundary gate that made
that true keeps running.

The reasoning is the shape of the thing. This library is a substrate: a seam, a stamp, and a census
that reads files. Its value to anyone who has it is that a harness can adopt it without inheriting
assumptions, which is a property that grows with the number of people holding it and shrinks to
nothing if nobody can. There is no version of this library whose *terms* are the interesting part.

The gate stays and the standard stays. A licence permits publication; it does not maintain the
property that makes publishing worth anything, and the day a convenient reference to something a
reader cannot look up gets in is the day this stops being a substrate anyone else can hold.

## D12 — The substrate owns the bytes of the artefacts it types

**2026-08-22, 0.3.0.**

Two gaps, recorded by the second consumer's bootstrap and closed together because they are one
species. This library **typed** a stored result cell's read-slice and typed the re-gate sidecar
row — including the reasoning for why that row carries both the recorded and the fresh label — and
then wrote neither. A type with no codec beside it is not a contract; it is a suggestion that each
adopting harness re-derives by reading the reader's source.

The failure mode is silent in both directions, which is why neither would have surfaced on its own.
A harness that spells `parse_reason` as `reason` writes cells the census loads and reports as
clean, because a tolerant loader cannot tell an absent field from a well-formed negative. Two
harnesses that each invent a sidecar spelling never notice, because each reads only its own
manifest. Nothing fails until someone tries to read both, at which point the artefacts are years
old.

**What the substrate now owns is the ENVELOPE, and only the envelope.** `EvalCell<'Verdict>` names
the identity axes, the primary gate label the census keys off, the per-gate outcomes, the
provenance stamp, and nothing a domain would recognise as its own. Two slots carry everything else:
the verdict is a **type parameter**, the same construct and the same argument as D9 — it is the
only shape that carries a domain's judgement without naming what was judged — and `Extra` carries
the members a domain adds, as values rather than as names this library would otherwise have to
learn. `EvalJson` exists for that second slot: writing members it is not allowed to name is what
owning the bytes actually requires.

**Three properties are checked rather than asserted.**

- **Round trip in BOTH directions.** `read (write x) = x` alone passes for a writer that drops a
  member the reader also ignores; `write (read j) = j` alone passes for a codec that cannot
  represent anything the sample does not contain. Together they pin the bytes. What makes the pair
  exact is the decoder's signature: a domain's verdict decoder answers with the verdict **and the
  members it did not consume**, so whatever it leaves behind is what a re-encode reproduces.
- **Absent is not false, in the direction each artefact needs.** A cell OMITS `parse_passed` when
  there is no label, because a case that emitted nothing to decode did not fail to decode. A
  manifest row writes `"recorded": null`, because a row is a claim about one specific cell and "this
  cell carried no label" is a positive finding it must state. Opposite spellings, and each is right
  for its artefact rather than an inconsistency to be tidied.
- **`flipped` is written and never believed.** It is derived from `recorded` and `fresh`, and it is
  written because it is the finding the artefact exists to surface and manifests are read by eye and
  by line-oriented tools at least as often as they are parsed. The decoder ignores the stored member
  and recomputes, so a hand-edited manifest cannot assert a flip its own labels deny.

**Two shapes the phase asked for are offered as EXPORTS, not replacements**, and the distinction is
the point. The intake ledger's own artefact is a markdown table, and that is not an accident to be
normalised away: it is the thing a person edits in the same pass they decide something, and the
moment intake needs a form, gaps go back to being everyone else's error. A cluster's own artefact is
a printed report, which is the right shape for the reader it is written for. Both codecs exist for
the consumer that is not a person — a drift check, an index, a cross-cohort comparison, which is
stage 5's whole question and not something anyone should answer by diffing rendered text.

**On the existing consumers, and why one adopted the writer and one did not.** The greenfield
consumer retired its hand-rolled writer and sidecar spelling outright. The older one adopted the
sidecar codec and the cell READER, and kept its own cell writer, deliberately: its stored cell
carries some sixty domain fields whose absent-versus-false semantics were established one at a time
over a long series of additive changes, and pushing that through a generic writer would re-encode
every one of those distinctions by hand for no gain the envelope does not already give. What it took
instead is the check that matters — its written cells are read by this library's reader, so the
envelope contract now has a typed holder at both ends. That is the whole of what the gap was.
