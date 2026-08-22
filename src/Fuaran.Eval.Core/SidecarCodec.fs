/// The canonical bytes of the demand loop's sidecar artefacts: the re-gate
/// manifest row, the intake-ledger row, and the census cluster.
///
/// **What this closes.** `DemandSidecar` types all three, including the
/// reasoning for why a re-gate row carries BOTH the recorded and the fresh
/// label. It then left every harness to invent the JSONL, so one documented
/// artefact acquired one spelling per harness — differing in member names,
/// member order, and which of the derived facts were written down at all. None
/// of that fails anywhere, because each harness reads only its own manifest,
/// which is exactly why it goes unnoticed until someone tries to read two.
///
/// **The row is a fixed core plus carried extras.** The seven members below are
/// the row: they are what the loop's documentation specifies and what any
/// reader of any harness's manifest can rely on. A harness that records more —
/// the arm a cell belongs to, a second stored label it wants beside the gate's
/// — passes those as extras and they are written after the core, in the order
/// given. That is a deliberate ordering choice: a reader keying on member names
/// is unaffected by it, and a reader eyeballing a manifest sees the same seven
/// columns first in every harness's file.
///
/// **`flipped` is written even though it is derived.** `Recorded <> Fresh` is
/// the finding the whole artefact exists to surface, and a manifest is read by
/// eye and by line-oriented tools at least as often as it is parsed. The
/// decoder ignores the member and recomputes, so a hand-edited manifest cannot
/// assert a flip that its own labels do not support.
module Fuaran.Eval.Core.SidecarCodec

open Fuaran.Eval.Core.EvalJson
open Fuaran.Eval.Core.DemandSidecar

/// Why a sidecar line could not be read.
type SidecarReadError =
    | NotJson of message: string
    | MissingMember of name: string
    | MemberTypeMismatch of name: string * expected: string

    member this.Message: string =
        match this with
        | NotJson m -> $"not a JSON object: {m}"
        | MissingMember name -> $"required member '{name}' is absent"
        | MemberTypeMismatch(name, expected) -> $"member '{name}' is not {expected}"

// ─── The re-gate manifest row ─────────────────────────────────────────────

/// The members the re-gate row owns, in write order.
let regateMembers: string list =
    [ "file"
      "task"
      "provider"
      "recorded"
      "fresh"
      "reason"
      "decoder"
      "flipped" ]

/// One manifest line. Compact — a manifest is one line per cell and is read
/// line-wise, so indenting it would break every tool that assumes that.
let encodeRegateRow (extra: (string * EvalValue) list) (row: RegateRow) : string =
    renderObject
        false
        [ yield "file", JStr row.File
          yield "task", JStr row.Task
          yield "provider", JStr row.Provider

          // NULL, not omitted, and this is the opposite choice from the cell's
          // `parse_passed`. A manifest row is a claim about a specific cell's
          // label, so "this cell carried no label" is a positive finding the row
          // must state; a stored cell with no label is simply a cell the gate
          // did not reach, and stating it there would be noise on every file.
          yield
              "recorded",
              (match row.Recorded with
               | Some recorded -> JBool recorded
               | None -> JNull)

          yield "fresh", JBool row.Fresh
          yield "reason", JStr row.Reason
          yield "decoder", JStr row.Decoder
          yield "flipped", JBool row.Flipped
          yield! extra ]

/// Read one manifest line, answering the row and whatever the harness carried
/// beyond the core — the same shape as the cell reader, and for the same
/// reason: what a decoder does not consume is what a re-encode must reproduce.
let decodeRegateRow (line: string) : Result<RegateRow * (string * EvalValue) list, SidecarReadError> =
    match parseObject line with
    | Error message -> Error(NotJson message)
    | Ok ms ->
        let requireString name =
            match tryMember name ms with
            | Some(JStr s) -> Ok s
            | Some _ -> Error(MemberTypeMismatch(name, "a string"))
            | None -> Error(MissingMember name)

        result {
            let! file = requireString "file"
            let! task = requireString "task"
            let! provider = requireString "provider"
            let! reason = requireString "reason"
            let! decoder = requireString "decoder"

            let! fresh =
                match tryMember "fresh" ms with
                | Some(JBool b) -> Ok b
                | Some _ -> Error(MemberTypeMismatch("fresh", "a boolean"))
                | None -> Error(MissingMember "fresh")

            // A null or absent `recorded` is `None` — the cell carried no
            // stored label, so there is nothing a gate move could have
            // invalidated.
            let recorded =
                match tryMember "recorded" ms with
                | Some(JBool b) -> Some b
                | _ -> None

            return
                { File = file
                  Task = task
                  Provider = provider
                  Recorded = recorded
                  Fresh = fresh
                  Reason = reason
                  Decoder = decoder },
                except regateMembers ms
        }

// ─── The intake-ledger row ────────────────────────────────────────────────

/// The intake ledger's OWN form is a markdown table, and that is not an
/// accident to be tidied away: it is the artefact a person edits in the same
/// pass that they decide something, and the moment intake needs a form, gaps go
/// back to being everyone else's error.
///
/// This codec is therefore an EXPORT, not a replacement. It exists so a ledger
/// can be handed to something that is not a person — a drift check, a second
/// harness, an index — without that consumer parsing markdown.
let logRowMembers: string list = [ "date"; "source"; "intent"; "disposition" ]

let encodeLogRow (extra: (string * EvalValue) list) (row: LogRow) : string =
    renderObject
        false
        [ yield "date", JStr row.Date
          yield "source", JStr row.Source
          yield "intent", JStr row.Intent
          yield "disposition", JStr row.Disposition
          yield! extra ]

let decodeLogRow (line: string) : Result<LogRow * (string * EvalValue) list, SidecarReadError> =
    match parseObject line with
    | Error message -> Error(NotJson message)
    | Ok ms ->
        let requireString name =
            match tryMember name ms with
            | Some(JStr s) -> Ok s
            | Some _ -> Error(MemberTypeMismatch(name, "a string"))
            | None -> Error(MissingMember name)

        result {
            let! date = requireString "date"
            let! source = requireString "source"
            let! intent = requireString "intent"
            let! disposition = requireString "disposition"

            return
                { Date = date
                  Source = source
                  Intent = intent
                  Disposition = disposition },
                except logRowMembers ms
        }

// ─── The census cluster ───────────────────────────────────────────────────

/// A cluster's own artefact is a printed report, which is the right shape for
/// the reader it is written for. This codec is the machine-readable form of the
/// same finding, for the case the report cannot serve: comparing one cohort's
/// clusters against the next one's, which is stage 5's whole question and is
/// not something anyone should be doing by diffing rendered text.
///
/// `kind` discriminates, and the two shapes carry different keys — a judge
/// cluster is (task, criterion), a parse cluster is (reason class, token,
/// slot). Flattening them into one key set would lose which fields are the
/// cluster's identity, and the identity is what a cross-cohort comparison joins
/// on.
let clusterMembers: string list =
    [ "kind"
      "task"
      "criterion"
      "reason_class"
      "token"
      "slot"
      "count"
      "families"
      "detail"
      "sample_files"
      "bait_induced"
      "probe_corpus" ]

let encodeCluster (extra: (string * EvalValue) list) (cluster: Cluster) : string =
    renderObject
        false
        [ match cluster.Kind with
          | JudgeCluster(task, criterion) ->
              yield "kind", JStr "judge"
              yield "task", JStr task
              yield "criterion", JStr criterion
          | ParseCluster(reasonClass, token, slot) ->
              yield "kind", JStr "parse"
              yield "reason_class", JStr reasonClass
              yield "token", JStr token
              yield "slot", JStr slot

          yield "count", JInt(int64 cluster.Count)
          yield "families", JArr(cluster.Families |> List.map JStr)
          yield "detail", JStr cluster.Detail
          yield "sample_files", JArr(cluster.SampleFiles |> List.map JStr)
          yield "bait_induced", JBool cluster.BaitInduced
          yield "probe_corpus", JBool cluster.ProbeCorpus
          yield! extra ]

let decodeCluster (line: string) : Result<Cluster * (string * EvalValue) list, SidecarReadError> =
    match parseObject line with
    | Error message -> Error(NotJson message)
    | Ok ms ->
        let requireString name =
            match tryMember name ms with
            | Some(JStr s) -> Ok s
            | Some _ -> Error(MemberTypeMismatch(name, "a string"))
            | None -> Error(MissingMember name)

        let strings name =
            match tryMember name ms with
            | Some(JArr items) ->
                Ok
                    [ for item in items do
                          match item with
                          | JStr s -> yield s
                          | _ -> () ]
            | Some _ -> Error(MemberTypeMismatch(name, "an array of strings"))
            | None -> Error(MissingMember name)

        let flag name =
            match tryMember name ms with
            | Some(JBool b) -> Ok b
            | Some _ -> Error(MemberTypeMismatch(name, "a boolean"))
            | None -> Error(MissingMember name)

        result {
            let! kind = requireString "kind"

            let! clusterKind =
                match kind with
                | "judge" ->
                    result {
                        let! task = requireString "task"
                        let! criterion = requireString "criterion"
                        return JudgeCluster(task, criterion)
                    }
                | "parse" ->
                    result {
                        let! reasonClass = requireString "reason_class"
                        let! token = requireString "token"
                        let! slot = requireString "slot"
                        return ParseCluster(reasonClass, token, slot)
                    }
                | _ -> Error(MemberTypeMismatch("kind", "either \"judge\" or \"parse\""))

            let! count =
                match tryMember "count" ms with
                | Some(JInt n) -> Ok(int n)
                | Some _ -> Error(MemberTypeMismatch("count", "an integer"))
                | None -> Error(MissingMember "count")

            let! families = strings "families"
            let! detail = requireString "detail"
            let! sampleFiles = strings "sample_files"
            let! baitInduced = flag "bait_induced"
            let! probeCorpus = flag "probe_corpus"

            return
                { Kind = clusterKind
                  Count = count
                  Families = families
                  Detail = detail
                  SampleFiles = sampleFiles
                  BaitInduced = baitInduced
                  ProbeCorpus = probeCorpus },
                except clusterMembers ms
        }
