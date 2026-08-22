/// The demand loop's data shapes, typed once: the cell slice the census reads,
/// the intake-ledger row, the re-gate (near-miss) sidecar row, and the cluster
/// the census produces.
///
/// One rule is a property of these types rather than of any code that writes
/// them: **the sidecar is a sidecar, never an in-place edit.**
/// A `RegateRow` carries `Recorded` AND `Fresh` because the DELTA is the
/// evidence; a shape that stored only the corrected label would destroy the
/// only proof that the labels moved.
module Fuaran.Eval.Core.DemandSidecar

// ─── The cell slice ───────────────────────────────────────────────────────

/// The slice of a stored result cell the census needs. Parsed directly from
/// JSON (not via the harness's full result reader) so unknown providers /
/// future DTO fields never kill a census pass over a mixed-age results
/// directory.
type CensusCell =
    {
        FileName: string
        TaskId: string
        /// Short task number, e.g. "040" from "tier-a-040-ticket-triage" —
        /// produced by the domain's task-id seam.
        TaskShort: string
        Provider: string
        /// Coarse model family ("claude" / "gpt" / "gemini" / …) — the
        /// cross-family demand-strength signal.
        Family: string
        Judged: bool
        ParsePassed: bool option
        ParseReason: string
        Notes: string
        /// The typed per-criterion verdicts when the cell carries them; `None`
        /// on cells written before the field existed, where a domain's prose
        /// fallback (if it has one) is the only partial source.
        ///
        /// A new domain records this from the first cohort and needs no
        /// fallback — see the template's advice on the criterion-verdict seam.
        TypedCriterionVerdicts: (string * string) list option
    }

// ─── The intake ledger ────────────────────────────────────────────────────

/// One row of the demand log — stage 2's entire artefact. Four columns, and
/// the cheapness is load-bearing: the moment intake needs a form, gaps go back
/// to being everyone else's error.
type LogRow =
    { Date: string
      Source: string
      Intent: string
      Disposition: string }

    member this.Text = $"{this.Source} {this.Intent}"

// ─── The re-gate sidecar ──────────────────────────────────────────────────

/// One line of a re-gate manifest: what a cell's label SAID at capture time,
/// what it says under the gate running now, and which gate that was.
///
/// `Recorded` is an option because a cell may carry no stored label at all —
/// nothing a gate move could invalidate, so such a row is stable by
/// definition, and collapsing that case into `false` would manufacture a flip.
type RegateRow =
    {
        /// The cell's identity; also carries the run stamp (`<stamp>__…`,
        /// ISO-sortable), which is how a cohort is selected — an ordinal string
        /// compare IS the date filter, so nothing parses a date.
        File: string
        Task: string
        Provider: string
        /// The label as stored at capture time.
        Recorded: bool option
        /// The label recomputed now. `Recorded <> Fresh` is the stale-label
        /// hazard, made visible.
        Fresh: bool
        /// The gate error — code, path, message — the parse-cluster key.
        Reason: string
        /// The identity of the gate that produced `Fresh`. Without it,
        /// `Recorded` is a claim about an unnamed decoder.
        Decoder: string
    }

    /// The row disagrees with itself: the stored label does not survive the
    /// current gate. A cell in this state is left UNSTAMPED rather than
    /// repaired — the honest state is "unverified".
    member this.Flipped: bool =
        match this.Recorded with
        | Some recorded -> recorded <> this.Fresh
        | None -> false

// ─── The census cluster ───────────────────────────────────────────────────

type ClusterKind =
    /// (taskShort, criterionId) — a rubric criterion repeatedly non-YES.
    | JudgeCluster of task: string * criterion: string
    /// (reasonClass, offendingToken, slot) — a gate reason repeatedly hit.
    /// `slot` is the normalised wire position, "" when the reason carries no
    /// path past the root. It is part of the cluster key because the same
    /// (code, token) at two positions is two demands.
    | ParseCluster of reasonClass: string * token: string * slot: string

type Cluster =
    {
        Kind: ClusterKind
        Count: int
        Families: string list
        /// e.g. "3×NO 2×PARTIAL" for judge clusters; sample reason for parse.
        Detail: string
        SampleFiles: string list
        /// True when EVERY contributing cell is an adversarial-tier task and
        /// the cluster is parse-layer: the induced failure working as designed,
        /// never organic demand evidence. Reported under its own heading and
        /// excluded from intake requirements and demand gates. Judge-criterion
        /// clusters are never bait-induced (they grade the RECOVERED state).
        BaitInduced: bool
        /// True when EVERY contributing cell belongs to a probe corpus:
        /// diagnostic tracks whose failures are the probe WORKING. Unlike bait,
        /// judge clusters are excluded too — these corpora have no
        /// recovered-state half to grade. Reported under their own heading;
        /// never intake, never gate evidence.
        ProbeCorpus: bool
    }

    member this.Label =
        match this.Kind with
        | JudgeCluster(task, crit) -> $"{task}/{crit}"
        | ParseCluster(cls, tok, slot) ->
            let head = if tok = "" then cls else $"{cls} '{tok}'"
            if slot = "" then head else $"{head} @ {slot}"
