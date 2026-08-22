/// The provenance vocabulary an eval harness stamps onto everything it
/// publishes: the citable-block fields (cohort id, harness commit, prompt hash)
/// joined to the gate vocabulary (the gate identity that produced a label, and
/// the wire `decoder_version` that is only ONE of those gates).
///
/// The distinction these types exist to keep: **a stored pass/fail label is a
/// claim about one gate**, and once a suite gates comparator arms too — a
/// bundler, a type-checker, a build — stamping those with the wire decoder's
/// version attributes a label to a toolchain that never saw the emission. So a
/// stamp carries a LIST of gate identities and a separately-named decoder
/// version, rather than one string that a reader must guess the referent of.
module Fuaran.Eval.Core.EvalProvenance

/// The identity of one gate that judged an emission: which gate, and which
/// version of it. `Version` is opaque on purpose — a content hash, a tool
/// version, a corpus stamp; the harness that owns the gate decides, and the
/// only property this layer relies on is that it changes when the gate does.
type GateIdentity =
    { Gate: string
      Version: string }

    /// The single-token form a sidecar row or a result field stores.
    member this.AsStamp: string =
        if this.Version = "" then
            this.Gate
        else
            $"{this.Gate}:{this.Version}"

/// What a published figure carries so it can travel. Every field is allowed to
/// be empty and empty means UNSTAMPED, never "fine": the fix for a misattributed
/// label is to refuse at the chokepoint that turns stored labels into a
/// published number, and a refusal needs to be able to tell "no stamp" from "a
/// foreign stamp".
type ProvenanceStamp =
    {
        /// The cohort the figure is over. A cohort id names a SET; resolving it
        /// to one is the harness's job, not this record's.
        CohortId: string
        /// The commit of the harness that bought the cells.
        HarnessSha: string
        /// The modal system-prompt hash across the cohort ("" when unstamped).
        PromptSha256: string
        /// Every gate that produced a label in this cohort — one per condition,
        /// not one per cohort. See the module doc.
        Gates: GateIdentity list
        /// The WIRE decoder version specifically (`decoder_version` on a stored
        /// cell). Named separately from `Gates` because a comparator arm's label
        /// did not come from it, and reading it as though it had is the exact
        /// misattribution this record exists to prevent.
        DecoderVersion: string
    }

    /// A stamp is complete when a reader can reconstruct what produced every
    /// number in the block: which cells, which harness, which prompt, which
    /// gate. Anything less is quotable only as advisory.
    member this.IsComplete: bool =
        this.CohortId <> ""
        && this.HarnessSha <> ""
        && this.PromptSha256 <> ""
        && not (List.isEmpty this.Gates)

/// The honest zero. Distinct from a stamp whose fields happen to be blank only
/// in that it is written down deliberately at a site that has nothing to stamp.
let unstamped: ProvenanceStamp =
    { CohortId = ""
      HarnessSha = ""
      PromptSha256 = ""
      Gates = []
      DecoderVersion = "" }
