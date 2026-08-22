/// **Everything the census needs from a domain, as fields.** The domain-coupled
/// sites in the census engine were once marked with comments; an annotation a
/// reader has to notice is not a boundary, so each became a field here. A domain
/// supplies this record, the engine takes it, and a seam that is missed is a
/// compile error rather than a comment nobody read.
///
/// A witness RECORD rather than an interface: the seams are independent
/// functions with no shared state, so an interface would buy only ceremony.
module Fuaran.Eval.Core.DemandSeams

open Fuaran.Eval.Core.DemandSidecar

/// Everything the demand census needs from a domain — and, by construction,
/// everything about the census that is NOT transferable. Anything absent from
/// this record transfers unchanged.
type DomainCensusSeam =
    {
        // ─── SEAM 1 — the corpus's task-id grammar ────────────────────────
        /// The short task id the judge-cluster key is built from. A corpus with
        /// structured ids might read a tier letter plus a number out of
        /// `tier-a-040-ticket-triage`; a domain substitutes its own corpus's
        /// grammar, and returning the id verbatim is a legitimate implementation
        /// for a corpus with no structure to exploit.
        TaskShortOf: string -> string

        // ─── SEAM 2 — the own-language condition ───────────────────────────
        /// Does this result cell's `condition` name THIS domain's own arm?
        /// A comparator arm's failures are not demand on our language, so they
        /// never reach intake.
        IsOwnLanguageCondition: string -> bool

        /// How the census names those cells when it reports ("… N <label>
        /// cells"). The other half of seam 2: the predicate decides membership,
        /// this decides what the operator sees it called.
        OwnLanguageLabel: string

        // ─── SEAM 3 — the criterion-verdict source ─────────────────────────
        /// The cell's per-criterion verdicts, from the domain's own judge
        /// record. The ADVICE rather than the description: **record the typed
        /// field from the first cohort and return it here directly.** A domain
        /// whose typed field arrives late will need a prose fallback, and such a
        /// fallback is partial by construction — non-YES verdicts only, so it
        /// cannot supply a denominator, which is why criterion-level pass rates
        /// are honestly unavailable on cells written before the field existed.
        CriterionVerdictsOf: CensusCell -> (string * string) list

        // ─── SEAM 4 — the adversarial-tier marker ──────────────────────────
        /// Is this task from the bait / adversarial tier? A parse cluster whose
        /// every contributing cell is one was deliberately provoked, so it is
        /// reported under its own heading — never intake, never gate evidence.
        /// Judge-criterion clusters on the same tier still intake normally:
        /// they grade the RECOVERED state, not the provocation.
        IsAdversarialTierTask: string -> bool

        // ─── SEAM 5 — the probe-corpus marker ──────────────────────────────
        /// Is this task from a diagnostic PROBE corpus — a track whose failures
        /// are the probe working rather than demand?
        ///
        /// The last seam to be found, and worth noting as a class rather than an
        /// item: it names a corpus's track prefixes for exactly the reason seam 4
        /// names its tier letters, so a domain that grows a new kind of
        /// deliberately-provoked failure will find it needs a sixth. A domain
        /// with no diagnostic tracks supplies `fun _ -> false` and loses nothing.
        IsProbeCorpusTask: string -> bool
    }

/// The four places the census's printed report says something only a domain can
/// say. Supplied ALONGSIDE `DomainCensusSeam` rather than as fields of it,
/// deliberately: a new required field on the seam is a breaking change for every
/// adopter, and these are presentational — a domain that wants the generic
/// wording should not have to restate the whole census contract to decline them.
///
/// The generic set below names nothing. It is the honest default in a way a
/// pre-filled one is not: the first adopting domain's tier letters and document
/// paths were once printed by the engine itself, which read as the engine's own
/// vocabulary and was invisible to everyone except the second adopter.
type ReportLabels =
    {
        /// Why an adversarial-tier cluster is being shown but excluded, rendered
        /// as `── BAIT-INDUCED (<aside>; excluded from demand evidence) — N ──`.
        /// A domain that names its adversarial tier says so here.
        AdversarialAside: string

        /// The same, for the diagnostic-probe clusters:
        /// `── PROBE-CORPUS (<aside>; excluded from demand evidence) — N ──`. A
        /// domain that runs named diagnostic tracks names them here.
        ProbeCorpusAside: string

        /// What to do with the drafted intake rows, rendered as
        /// `drafted intake rows (<hint>):`. A domain that keeps its intake ledger
        /// at a known path points at it here.
        IntakeDraftHint: string

        /// What to do about an open ledger row carrying no catalog token, printed
        /// after the sentence explaining why the drift check cannot see it. A
        /// domain with tooling for it points at that tooling here.
        UntokenedRowHint: string
    }

/// Report labels that name nothing outside the census itself — the wording for a
/// domain that has no tier vocabulary, no named diagnostic tracks, and no
/// document to point a reader at. Unlike `minimal` this IS a safe default: a
/// domain that adopts it loses only specificity, never an exclusion.
let genericLabels: ReportLabels =
    { AdversarialAside = "the induced failure working"
      ProbeCorpusAside = "the probe working"
      IntakeDraftHint = "paste into the intake ledger"
      UntokenedRowHint = "author the tokens" }

/// A seam for a domain with no corpus structure to exploit — every predicate
/// off, every id verbatim, typed verdicts only. Useful as a starting point and
/// as the shape a test fixture wants; NOT a default, because a domain that
/// adopts it silently has switched its adversarial-tier exclusion off.
let minimal (ownCondition: string) : DomainCensusSeam =
    { TaskShortOf = id
      IsOwnLanguageCondition = fun c -> c = ownCondition
      OwnLanguageLabel = ownCondition
      CriterionVerdictsOf = fun cell -> cell.TypedCriterionVerdicts |> Option.defaultValue []
      IsAdversarialTierTask = fun _ -> false
      IsProbeCorpusTask = fun _ -> false }
