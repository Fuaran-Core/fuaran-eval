# Contributing

Thank you for looking. This document says what the project expects, so that a change you spend an
afternoon on is not sent back for something you had no way to know.

## The short version

```
pwsh ./run.ps1
```

That is tool restore, Fantomas, the publication-boundary sweep, the build, and the full test suite.
If it is green, your change passes the same gate every other change passes. Run it before you open
a pull request.

## Ground rules

**F# 10 / .NET 10.** The SDK version is pinned in `global.json`. Do not lower the target framework
or pin `LangVersion` below the implicit default.

**Fantomas formats every F# file.** Run `dotnet fantomas src tests` — or just `run.ps1` — before
committing. Format files *inside the repository*: a copy formatted at a temporary path misses the
repository's `.editorconfig` and produces a diff the next in-repo pass immediately re-dirties.

**No new dependencies.** This library depends on `FSharp.Core` and the framework, and holds no
project references at all. That is a property worth defending rather than a coincidence: it is what
lets a harness adopt the substrate without inheriting anything else the substrate's other consumers
happen to believe. A dependency that arrives to save twenty lines is a bad trade here.

**No gate.** Deciding whether an emission is valid belongs to the domain being evaluated — see
DECISIONS D3. A pull request that adds a validator, a decoder, or any judgement about emitted
content is out of scope however small it looks, and `GateVocabularyTests` reads the sources and says
so.

**Nothing here starts a process, opens a socket, or reads a clock it was not given.** The census
reads files it is handed a path to; that is the whole of its outside world. If your change needs an
effect, take it as a function value the caller supplies.

## The publication standard

This project is written as if it were public: **no file here names another project, product,
repository or tool, the command surface any of them are driven by, or the numbering of any planning
system.** It is licensed Apache-2.0 (DECISIONS D11); the standard predates the licence, and holding
it is what made choosing one a one-line act rather than a rewrite. It stays, because a licence
permits publication and does nothing to keep the thing worth publishing.

By contributing you agree that your contribution is licensed under the same terms.

`gates/check-publication-boundary.ps1` enforces it over the tracked file set on every run, and
prints the residue it knowingly carries even when it is green. If your change trips it, the fix is
almost always to say the thing generically — the substrate has no need to name anyone.

## A change to the public surface advances the version

`Directory.Build.props` carries `<Version>`, and the rule attached to it is strict: **a change to
any public type, member or signature advances that number in the same commit that makes it.** A
folder feed keys packages by id and version, so repacking an unchanged version swaps the contract
under every pinned consumer while their extracted caches keep serving the old bits — green
everywhere, and wrong.

Adding a required field to a record is a breaking change: every consumer that constructs it stops
compiling. `DomainCensusSeam` is the record this bites hardest, because a new seam field is exactly
how the census grows.

## Tests

Expecto, in `tests/Fuaran.Eval.Core.Tests`. Prefer a test that would have caught the bug over a test
that describes the fix. The suite is fast and offline by construction; keep it that way.
