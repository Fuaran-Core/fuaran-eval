/// The stored result cell — one file per (task × condition × provider × run) —
/// and the writer and reader that own its bytes.
///
/// **What this closes.** The demand-census engine reads stored cells by field
/// name: `condition`, `task_id`, `provider`, `judged`, `parse_passed`,
/// `parse_reason`, `notes`, `criterion_verdicts`. Those names were an implicit
/// contract with a typed holder at NEITHER end — the reader parsed them out of
/// raw JSON, and every writing harness spelled them from scratch by reading the
/// reader's source. That arrangement has one failure mode and it is silent: a
/// harness that spells `parse_reason` as `reason` produces cells the census
/// loads and reports as clean, because an absent field and a well-formed
/// negative look identical to a tolerant loader.
///
/// So the ENVELOPE is the substrate's, and this module is where it lives.
///
/// **What stays the domain's.** Everything the envelope does not name. Two
/// slots carry it:
///
/// - `Verdict` is a TYPE PARAMETER, the same posture the provider seam takes:
///   it is the only construct that carries a domain's judgement without naming
///   what was judged. A harness supplies a pair of functions to move its own
///   verdict shape in and out; a harness with no verdict instantiates at `unit`.
/// - `Extra` carries the members a domain adds beyond the envelope, as values
///   rather than as names this library would have to know.
///
/// **Absent is not false, anywhere in here.** `ParsePassed` is an option because
/// a case with nothing to decode — a refusal that correctly emitted nothing —
/// has no parse label, and recording `false` there manufactures a failure the
/// model never had. The writer OMITS the member in that case, which is what the
/// census reads as `None`.
///
/// **The reader is total.** It answers `Result`, never an exception, and its
/// failures are typed: a caller distinguishes "this file is not JSON" from
/// "this file is a cell missing its condition" from "the domain could not read
/// its own verdict", and each of those wants a different response.
///
/// **It requires the identity and label members and not the provenance ones**,
/// which is this library's own doctrine applied to itself: a `ProvenanceStamp`
/// field is allowed to be empty and empty means UNSTAMPED, so a cell that never
/// carried a cohort id is unstamped rather than unreadable. A cell missing
/// `condition` or `parse_reason` is a different thing entirely — a
/// differently-spelled cell — and that is the defect this module exists to
/// catch, so those are refused.
module Fuaran.Eval.Core.EvalCell

open Fuaran.Eval.Core.EvalJson
open Fuaran.Eval.Core.EvalProvenance

/// What one named gate said about one cell's emission.
///
/// `Passed` is an option for the same reason `ParsePassed` is: a gate that did
/// not run on this cell has no verdict, and a gate that ran and refused has
/// `Some false`. Collapsing the two loses the only distinction that matters
/// when a cohort is re-gated later.
type GateOutcome =
    { Gate: GateIdentity
      Passed: bool option
      Reason: string }

/// One stored result cell.
///
/// `ParsePassed` / `ParseReason` are the PRIMARY label — the one the census
/// keys off, written flat because that is the contract a mixed-age results
/// directory is read under. `Gates` is the fuller record: every gate that
/// judged this cell, named. They are separate fields rather than one derived
/// from the other, because a harness with a single gate should not have to
/// construct a list to record a label, and a harness with three should not have
/// to decide which one the flat field means.
type EvalCell<'Verdict> =
    {
        TaskId: string
        /// The arm this cell belongs to. The census's own-language seam keys on
        /// it, so a comparator arm's failures are not counted as demand on the
        /// domain under evaluation.
        Condition: string
        Provider: string
        ProviderModelId: string
        RunIndex: int
        /// False when no judge ran. A cell excluded from the judged denominator
        /// is not a failed cell, and the census relies on the difference.
        Judged: bool
        Success: bool
        /// The label the census keys off. `None` = nothing was gated here.
        ParsePassed: bool option
        /// The gate error — code, path, message — when `ParsePassed` is
        /// `Some false`; empty otherwise. The parse-cluster key.
        ParseReason: string
        /// Every gate that judged this cell. Empty is legitimate and means
        /// exactly that: nothing named a gate, so a later reader must not
        /// attribute the label to one.
        Gates: GateOutcome list
        /// The domain's own judgement, in the domain's own shape.
        Verdict: 'Verdict
        Notes: string
        RawOutput: string
        /// The provenance the cell carries so a figure computed over it can
        /// travel. Flattened onto the cell rather than nested, so a reader of
        /// one file can reconstruct what produced its labels without knowing
        /// the cohort it came from.
        Stamp: ProvenanceStamp
        /// The members this domain adds beyond the envelope, in write order.
        Extra: (string * EvalValue) list
    }

/// Why a cell could not be read. Each case is a different problem with a
/// different answer, which is the whole reason they are not one string.
type CellReadError =
    /// The bytes are not a JSON document, or the root is not an object. Carries
    /// the parser's message.
    | NotJson of message: string
    /// A member the envelope requires is absent. A mixed-age directory can
    /// legitimately produce this, which is why the census has its own tolerant
    /// loader and this reader does not pretend to be one.
    | MissingMember of name: string
    /// The member is present with the wrong JSON kind.
    | MemberTypeMismatch of name: string * expected: string
    /// The domain's verdict decoder refused. Carries its message unchanged —
    /// this layer has nothing to add to a judgement it cannot read.
    | VerdictUnreadable of message: string

    member this.Message: string =
        match this with
        | NotJson m -> $"not a JSON object: {m}"
        | MissingMember name -> $"required member '{name}' is absent"
        | MemberTypeMismatch(name, expected) -> $"member '{name}' is not {expected}"
        | VerdictUnreadable m -> $"the domain could not read its verdict: {m}"

// ─── The envelope's member names ──────────────────────────────────────────

[<Literal>]
let private mTaskId = "task_id"

[<Literal>]
let private mCondition = "condition"

[<Literal>]
let private mProvider = "provider"

[<Literal>]
let private mProviderModelId = "provider_model_id"

[<Literal>]
let private mRunIndex = "run_index"

[<Literal>]
let private mJudged = "judged"

[<Literal>]
let private mSuccess = "success"

[<Literal>]
let private mParsePassed = "parse_passed"

[<Literal>]
let private mParseReason = "parse_reason"

[<Literal>]
let private mGateOutcomes = "gate_outcomes"

[<Literal>]
let private mNotes = "notes"

[<Literal>]
let private mCohort = "cohort"

[<Literal>]
let private mHarnessSha = "harness_sha"

[<Literal>]
let private mPromptSha = "system_prompt_sha256"

[<Literal>]
let private mDecoderVersion = "decoder_version"

[<Literal>]
let private mGates = "gates"

[<Literal>]
let private mRawOutput = "raw_output"

/// Every member the envelope owns. A reader subtracts these to find what the
/// domain added; a writer emits them in this ORDER, which is what makes the
/// bytes deterministic. Order is part of the contract, so this list is read
/// left to right by both halves and is not sorted anywhere.
let envelopeMembers: string list =
    [ mTaskId
      mCondition
      mProvider
      mProviderModelId
      mRunIndex
      mJudged
      mSuccess
      mParsePassed
      mParseReason
      mGateOutcomes
      mNotes
      mCohort
      mHarnessSha
      mPromptSha
      mDecoderVersion
      mGates
      mRawOutput ]

// ─── Writing ──────────────────────────────────────────────────────────────

let private gateOutcomeValue (outcome: GateOutcome) : EvalValue =
    JObj
        [ "gate", JStr outcome.Gate.Gate
          "version", JStr outcome.Gate.Version
          "passed",
          (match outcome.Passed with
           | Some p -> JBool p
           | None -> JNull)
          "reason", JStr outcome.Reason ]

/// The cell's members, in canonical order, ready to render or to splice.
///
/// `encodeVerdict` returns the domain's own members; they are placed between
/// the gate block and `notes` — inside the envelope rather than after it, so a
/// domain's judgement reads beside the labels it explains rather than trailing
/// the raw output.
let members (encodeVerdict: 'Verdict -> (string * EvalValue) list) (cell: EvalCell<'Verdict>) =
    [ yield mTaskId, JStr cell.TaskId
      yield mCondition, JStr cell.Condition
      yield mProvider, JStr cell.Provider
      yield mProviderModelId, JStr cell.ProviderModelId
      yield mRunIndex, JInt(int64 cell.RunIndex)
      yield mJudged, JBool cell.Judged
      yield mSuccess, JBool cell.Success

      // OMITTED, not null, when there is no label. An absent member is what the
      // census reads as "this cell was not gated"; a null would be a second
      // spelling of the same thing, and two spellings is how a contract rots.
      match cell.ParsePassed with
      | Some passed -> yield mParsePassed, JBool passed
      | None -> ()

      yield mParseReason, JStr cell.ParseReason

      // Likewise omitted when empty: a harness that names no gate should not
      // write an empty array that reads as "gated by nothing".
      match cell.Gates with
      | [] -> ()
      | gates -> yield mGateOutcomes, JArr(gates |> List.map gateOutcomeValue)

      yield! encodeVerdict cell.Verdict

      yield mNotes, JStr cell.Notes
      yield mCohort, JStr cell.Stamp.CohortId
      yield mHarnessSha, JStr cell.Stamp.HarnessSha
      yield mPromptSha, JStr cell.Stamp.PromptSha256
      yield mDecoderVersion, JStr cell.Stamp.DecoderVersion
      yield mGates, JArr(cell.Stamp.Gates |> List.map (fun g -> JStr g.AsStamp))
      yield mRawOutput, JStr cell.RawOutput
      yield! cell.Extra ]

/// The cell as the bytes it is stored as: one indented JSON document.
let toJson (encodeVerdict: 'Verdict -> (string * EvalValue) list) (cell: EvalCell<'Verdict>) : string =
    renderObject true (members encodeVerdict cell)

// ─── Reading ──────────────────────────────────────────────────────────────

let private requireString name ms =
    match tryMember name ms with
    | Some(JStr s) -> Ok s
    | Some _ -> Error(MemberTypeMismatch(name, "a string"))
    | None -> Error(MissingMember name)

let private requireBool name ms =
    match tryMember name ms with
    | Some(JBool b) -> Ok b
    | Some _ -> Error(MemberTypeMismatch(name, "a boolean"))
    | None -> Error(MissingMember name)

let private requireInt name ms =
    match tryMember name ms with
    | Some(JInt n) -> Ok(int n)
    | Some _ -> Error(MemberTypeMismatch(name, "an integer"))
    | None -> Error(MissingMember name)

let private readGateOutcomes (ms: (string * EvalValue) list) : Result<GateOutcome list, CellReadError> =
    match tryMember mGateOutcomes ms with
    | None -> Ok []
    | Some(JArr items) ->
        let rec loop acc remaining =
            match remaining with
            | [] -> Ok(List.rev acc)
            | JObj fields :: rest ->
                let outcome =
                    { Gate =
                        { Gate = tryString "gate" fields |> Option.defaultValue ""
                          Version = tryString "version" fields |> Option.defaultValue "" }
                      Passed = tryBool "passed" fields
                      Reason = tryString "reason" fields |> Option.defaultValue "" }

                loop (outcome :: acc) rest
            | _ -> Error(MemberTypeMismatch(mGateOutcomes, "an array of objects"))

        loop [] items
    | Some _ -> Error(MemberTypeMismatch(mGateOutcomes, "an array"))

let private readStampGates (ms: (string * EvalValue) list) : Result<GateIdentity list, CellReadError> =
    match tryMember mGates ms with
    | None -> Ok []
    | Some(JArr items) ->
        // The stored form is the single-token stamp `gate:version`, which is
        // what `GateIdentity.AsStamp` writes. Splitting on the FIRST colon is
        // the inverse: a version may itself carry colons, a gate name may not
        // by construction of the stamp, and an unversioned gate round-trips
        // through the no-colon branch.
        let split (token: string) =
            match token.IndexOf ':' with
            | -1 -> { Gate = token; Version = "" }
            | i ->
                { Gate = token.Substring(0, i)
                  Version = token.Substring(i + 1) }

        let rec loop acc remaining =
            match remaining with
            | [] -> Ok(List.rev acc)
            | JStr token :: rest -> loop (split token :: acc) rest
            | _ -> Error(MemberTypeMismatch(mGates, "an array of stamp strings"))

        loop [] items
    | Some _ -> Error(MemberTypeMismatch(mGates, "an array"))

/// Read a stored cell.
///
/// `decodeVerdict` is handed every member the envelope does not own, and
/// answers with the domain's verdict AND the members it did not consume. That
/// asymmetric-looking signature is what makes the round trip exact: whatever
/// the domain leaves behind becomes `Extra`, so writing the result again
/// reproduces the members in the order they arrived.
let ofMembers
    (decodeVerdict: (string * EvalValue) list -> Result<'Verdict * (string * EvalValue) list, string>)
    (ms: (string * EvalValue) list)
    : Result<EvalCell<'Verdict>, CellReadError> =
    result {
        let! taskId = requireString mTaskId ms
        let! condition = requireString mCondition ms
        let! provider = requireString mProvider ms
        let! providerModelId = requireString mProviderModelId ms
        let! runIndex = requireInt mRunIndex ms
        let! judged = requireBool mJudged ms
        let! success = requireBool mSuccess ms
        let! parseReason = requireString mParseReason ms
        let! gateOutcomes = readGateOutcomes ms
        let! notes = requireString mNotes ms
        let! stampGates = readStampGates ms
        let! rawOutput = requireString mRawOutput ms

        // The provenance block is READ AS UNSTAMPED WHEN ABSENT, and that is not
        // laxity — it is this library's own doctrine applied to its own reader.
        // Every field of a `ProvenanceStamp` is allowed to be empty, and empty
        // means UNSTAMPED, never "fine". A cell that never carried a cohort id
        // is unstamped for that field; refusing to READ it would make the
        // reader unusable on exactly the corpora whose provenance is the thing
        // under investigation. The identity and label members above stay
        // REQUIRED, because a cell missing one of those is not an unstamped
        // cell — it is a differently-spelled one, which is the defect this
        // module exists to catch.
        let stamped name =
            match tryMember name ms with
            | Some(JStr s) -> Ok s
            | None -> Ok ""
            | Some _ -> Error(MemberTypeMismatch(name, "a string"))

        let! cohortId = stamped mCohort
        let! harnessSha = stamped mHarnessSha
        let! promptSha = stamped mPromptSha
        let! decoderVersion = stamped mDecoderVersion

        let parsePassed =
            match tryMember mParsePassed ms with
            | Some(JBool b) -> Some b
            | _ -> None

        let! verdict, leftover =
            match decodeVerdict (except envelopeMembers ms) with
            | Ok pair -> Ok pair
            | Error message -> Error(VerdictUnreadable message)

        return
            { TaskId = taskId
              Condition = condition
              Provider = provider
              ProviderModelId = providerModelId
              RunIndex = runIndex
              Judged = judged
              Success = success
              ParsePassed = parsePassed
              ParseReason = parseReason
              Gates = gateOutcomes
              Verdict = verdict
              Notes = notes
              RawOutput = rawOutput
              Stamp =
                { CohortId = cohortId
                  HarnessSha = harnessSha
                  PromptSha256 = promptSha
                  Gates = stampGates
                  DecoderVersion = decoderVersion }
              Extra = leftover }
    }

/// Read a stored cell from its bytes.
let ofJson
    (decodeVerdict: (string * EvalValue) list -> Result<'Verdict * (string * EvalValue) list, string>)
    (json: string)
    : Result<EvalCell<'Verdict>, CellReadError> =
    match parseObject json with
    | Error message -> Error(NotJson message)
    | Ok ms -> ofMembers decodeVerdict ms

/// The verdict pair for a domain that records none. Supplied rather than left
/// to each caller because writing it wrong — consuming the leftover members
/// instead of passing them on — silently empties `Extra`.
let noVerdict
    : (unit -> (string * EvalValue) list) *
      ((string * EvalValue) list -> Result<unit * (string * EvalValue) list, string>) =
    (fun () -> []), (fun leftover -> Ok((), leftover))
