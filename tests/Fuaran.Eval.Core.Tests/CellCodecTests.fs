/// The cell and sidecar codecs, checked in BOTH directions.
///
/// One direction on its own proves less than it looks. `read (write x) = x`
/// passes for a writer that drops a member the reader also ignores; `write
/// (read j) = j` passes for a codec that cannot represent anything the sample
/// document does not contain. Together they pin the bytes: the record survives
/// a trip through the file, and the file survives a trip through the record.
module Fuaran.Eval.Core.Tests.CellCodecTests

open Expecto

open Fuaran.Eval.Core.EvalJson
open Fuaran.Eval.Core.EvalProvenance
open Fuaran.Eval.Core.EvalCell
open Fuaran.Eval.Core.DemandSidecar
open Fuaran.Eval.Core.SidecarCodec

// ─── A domain's verdict, standing in for a real one ───────────────────────
//
// Deliberately not `unit`: the type parameter's whole claim is that a domain
// keeps its own discrimination through the seam, and a test that instantiates
// at nothing tests the claim it was cheapest to test.

type CriterionVerdict = { Id: string; Verdict: string }

let private encodeVerdicts (verdicts: CriterionVerdict list) =
    [ "criterion_verdicts", JArr [ for v in verdicts -> JObj [ "id", JStr v.Id; "verdict", JStr v.Verdict ] ] ]

let private decodeVerdicts (members: (string * EvalValue) list) =
    match tryMember "criterion_verdicts" members with
    | Some(JArr items) ->
        let verdicts =
            [ for item in items do
                  match item with
                  | JObj fields ->
                      match tryString "id" fields, tryString "verdict" fields with
                      | Some id, Some verdict -> yield { Id = id; Verdict = verdict }
                      | _ -> ()
                  | _ -> () ]

        Ok(verdicts, except [ "criterion_verdicts" ] members)
    | _ -> Error "no criterion_verdicts member"

let private sample: EvalCell<CriterionVerdict list> =
    { TaskId = "tier-a-040-ticket-triage"
      Condition = "reference_domain"
      Provider = "vendor-a"
      ProviderModelId = "vendor-a-model-1"
      RunIndex = 2
      Judged = true
      Success = false
      ParsePassed = Some false
      ParseReason = "E_UNKNOWN_KIND at $.children[0].kind: 'accordion'"
      Gates =
        [ { Gate =
              { Gate = "wire-decoder"
                Version = "0.4.1" }
            Passed = Some false
            Reason = "E_UNKNOWN_KIND at $.children[0].kind: 'accordion'" }
          { Gate = { Gate = "bundler"; Version = "2.0.0" }
            Passed = None
            Reason = "" } ]
      Verdict = [ { Id = "c1"; Verdict = "YES" }; { Id = "c2"; Verdict = "PARTIAL" } ]
      Notes = "quoted \"detail\" with a\nnewline and a } brace"
      RawOutput = "{\"kind\":\"accordion\"}"
      Stamp =
        { CohortId = "cohort-20260822"
          HarnessSha = "0123456789abcdef0123456789abcdef01234567"
          PromptSha256 = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210"
          Gates =
            [ { Gate = "wire-decoder"
                Version = "0.4.1" }
              { Gate = "bundler"; Version = "2.0.0" } ]
          DecoderVersion = "0.4.1" }
      Extra = [ "case_kind", JStr "author"; "refusal_class", JStr ""; "attempts", JInt 3L ] }

[<Tests>]
let cellTests =
    testList
        "EvalCell"
        [ test "a cell survives a round trip through its bytes" {
              let json = toJson encodeVerdicts sample

              match ofJson decodeVerdicts json with
              | Error e -> failtestf "the reader refused its own writer's output: %s" e.Message
              | Ok back -> Expect.equal back sample "record → bytes → record"
          }

          test "the bytes survive a round trip through the record" {
              let json = toJson encodeVerdicts sample

              match ofJson decodeVerdicts json with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok back -> Expect.equal (toJson encodeVerdicts back) json "bytes → record → bytes"
          }

          test "the envelope's member order is fixed and the domain's members sit inside it" {
              let names = members encodeVerdicts sample |> List.map fst

              Expect.equal
                  names
                  [ "task_id"
                    "condition"
                    "provider"
                    "provider_model_id"
                    "run_index"
                    "judged"
                    "success"
                    "parse_passed"
                    "parse_reason"
                    "gate_outcomes"
                    "criterion_verdicts"
                    "notes"
                    "cohort"
                    "harness_sha"
                    "system_prompt_sha256"
                    "decoder_version"
                    "gates"
                    "raw_output"
                    "case_kind"
                    "refusal_class"
                    "attempts" ]
                  "the canonical order — a deterministic writer is the point"
          }

          test "no label OMITS parse_passed rather than writing false" {
              // The distinction the census reads. `false` would manufacture a
              // parse failure on a case that emitted nothing to parse.
              let cell = { sample with ParsePassed = None }
              let json = toJson encodeVerdicts cell

              Expect.isFalse (json.Contains "\"parse_passed\"") "absent, not false"

              match ofJson decodeVerdicts json with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok back -> Expect.equal back.ParsePassed None "and it reads back as None"
          }

          test "a harness that names no gate writes no gate_outcomes member" {
              let cell = { sample with Gates = [] }
              let json = toJson encodeVerdicts cell

              Expect.isFalse (json.Contains "\"gate_outcomes\"") "absent, not an empty array"

              match ofJson decodeVerdicts json with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok back -> Expect.equal back.Gates [] "and it reads back as no gates"
          }

          test "the stamp's gate list round-trips through its single-token form" {
              let json = toJson encodeVerdicts sample
              Expect.stringContains json "\"wire-decoder:0.4.1\"" "stored as the stamp token"

              match ofJson decodeVerdicts json with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok back -> Expect.equal back.Stamp.Gates sample.Stamp.Gates "split back on the first colon"
          }

          test "an unversioned gate round-trips too" {
              let cell =
                  { sample with
                      Stamp =
                          { sample.Stamp with
                              Gates = [ { Gate = "hand-review"; Version = "" } ] } }

              match ofJson decodeVerdicts (toJson encodeVerdicts cell) with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok back -> Expect.equal back.Stamp.Gates cell.Stamp.Gates "no colon, no version"
          }

          test "a reason carrying quotes, braces and newlines survives" {
              // The rows that matter most are the ones a hand-escaped writer
              // corrupts, so this is checked on the values rather than trusted.
              match ofJson decodeVerdicts (toJson encodeVerdicts sample) with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok back ->
                  Expect.equal back.Notes sample.Notes "notes"
                  Expect.equal back.ParseReason sample.ParseReason "parse reason"
                  Expect.equal back.RawOutput sample.RawOutput "raw output"
          }

          test "members the envelope does not own are carried, in order" {
              match ofJson decodeVerdicts (toJson encodeVerdicts sample) with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok back ->
                  Expect.equal
                      (back.Extra |> List.map fst)
                      [ "case_kind"; "refusal_class"; "attempts" ]
                      "the domain's own members, unnamed by this library"
          }

          test "the reader is total and its failures are typed" {
              match ofJson decodeVerdicts "not json at all" with
              | Error(CellReadError.NotJson _) -> ()
              | other -> failtestf "malformed bytes must answer Error, never throw; got %A" other

              let noCondition =
                  toJson encodeVerdicts sample |> fun j -> j.Replace("\"condition\"", "\"cond\"")

              match ofJson decodeVerdicts noCondition with
              | Error(CellReadError.MissingMember "condition") -> ()
              | other -> failtestf "expected a MissingMember for condition, got %A" other

              let wrongType =
                  toJson encodeVerdicts sample
                  |> fun j -> j.Replace("\"run_index\": 2", "\"run_index\": \"2\"")

              match ofJson decodeVerdicts wrongType with
              | Error(CellReadError.MemberTypeMismatch("run_index", _)) -> ()
              | other -> failtestf "expected a MemberTypeMismatch for run_index, got %A" other
          }

          test "a domain that cannot read its own verdict is told so, distinctly" {
              let stripped =
                  toJson encodeVerdicts sample
                  |> fun j -> j.Replace("\"criterion_verdicts\"", "\"verdicts\"")

              match ofJson decodeVerdicts stripped with
              | Error(VerdictUnreadable _) -> ()
              | other -> failtestf "expected a VerdictUnreadable, got %A" other
          }

          test "a domain with no verdict keeps every unnamed member as an extra" {
              let encode, decode = noVerdict

              let cell: EvalCell<unit> =
                  { TaskId = "t"
                    Condition = "c"
                    Provider = "p"
                    ProviderModelId = "p-1"
                    RunIndex = 0
                    Judged = false
                    Success = false
                    ParsePassed = None
                    ParseReason = ""
                    Gates = []
                    Verdict = ()
                    Notes = ""
                    RawOutput = ""
                    Stamp = unstamped
                    Extra = [ "domain_field", JStr "kept" ] }

              match ofJson decode (toJson encode cell) with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok back -> Expect.equal back cell "nothing consumed, nothing lost"
          } ]

// ─── The sidecar ──────────────────────────────────────────────────────────

let private row: RegateRow =
    { File = "20260822T101500Z__tier-a-040__reference_domain__vendor-a__r0.json"
      Task = "tier-a-040-ticket-triage"
      Provider = "vendor-a-model-1"
      Recorded = Some true
      Fresh = false
      Reason = "E_UNKNOWN_KIND at $.children[0].kind: 'accordion'"
      Decoder = "wire-decoder/0.4.1" }

[<Tests>]
let sidecarTests =
    testList
        "SidecarCodec"
        [ test "a re-gate row survives a round trip through its line" {
              match decodeRegateRow (encodeRegateRow [] row) with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok(back, extra) ->
                  Expect.equal back row "record → line → record"
                  Expect.isEmpty extra "nothing carried that was not passed"
          }

          test "the line survives a round trip through the record" {
              let line = encodeRegateRow [ "condition", JStr "reference_domain" ] row

              match decodeRegateRow line with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok(back, extra) -> Expect.equal (encodeRegateRow extra back) line "line → record → line"
          }

          test "the core seven come first, in the documented order" {
              let line =
                  encodeRegateRow [ "condition", JStr "x"; "judged_success", JBool true ] row

              match parseObject line with
              | Error e -> failtestf "unreadable: %s" e
              | Ok members ->
                  Expect.equal
                      (members |> List.map fst)
                      [ "file"
                        "task"
                        "provider"
                        "recorded"
                        "fresh"
                        "reason"
                        "decoder"
                        "flipped"
                        "condition"
                        "judged_success" ]
                      "carried extras follow the core, in the order given"
          }

          test "a line is one line" {
              let line = encodeRegateRow [] row
              Expect.isFalse (line.Contains "\n") "a manifest is read line-wise, so the row must not be indented"
          }

          test "no stored label writes NULL, not an omission" {
              // The opposite choice from the cell, deliberately: a manifest row
              // is a claim about a specific cell, so "carried no label" is a
              // finding the row must state.
              let line = encodeRegateRow [] { row with Recorded = None }
              Expect.stringContains line "\"recorded\":null" "stated, not implied"

              match decodeRegateRow line with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok(back, _) ->
                  Expect.equal back.Recorded None "reads back as no label"
                  Expect.isFalse back.Flipped "and a row with no label cannot have flipped"
          }

          test "flipped is written but never believed" {
              Expect.stringContains (encodeRegateRow [] row) "\"flipped\":true" "the finding is visible in the bytes"

              // A hand-edited manifest cannot assert a flip its labels deny.
              let lying =
                  (encodeRegateRow [] { row with Fresh = true }).Replace("\"flipped\":false", "\"flipped\":true")

              match decodeRegateRow lying with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok(back, _) -> Expect.isFalse back.Flipped "recomputed from recorded and fresh"
          }

          test "the re-gate reader is total and its failures are typed" {
              match decodeRegateRow "}{" with
              | Error(NotJson _) -> ()
              | other -> failtestf "expected NotJson, got %A" other

              match decodeRegateRow "{\"file\":\"f\"}" with
              | Error(MissingMember _) -> ()
              | other -> failtestf "expected MissingMember, got %A" other

              match
                  decodeRegateRow
                      "{\"file\":1,\"task\":\"t\",\"provider\":\"p\",\"fresh\":true,\"reason\":\"\",\"decoder\":\"d\"}"
              with
              | Error(MemberTypeMismatch("file", _)) -> ()
              | other -> failtestf "expected MemberTypeMismatch, got %A" other
          }

          test "an intake-ledger row survives a round trip" {
              let logRow: LogRow =
                  { Date = "2026-08-22"
                    Source = "eval cohort"
                    Intent = "express a substring test"
                    Disposition = "Open — language gap" }

              match decodeLogRow (encodeLogRow [ "cat", JStr "cat:demand-loop" ] logRow) with
              | Error e -> failtestf "read failed: %s" e.Message
              | Ok(back, extra) ->
                  Expect.equal back logRow "record → line → record"
                  Expect.equal extra [ "cat", JStr "cat:demand-loop" ] "the citation is carried, not parsed"
          }

          test "both cluster shapes survive a round trip, keeping their identity" {
              let judge =
                  { Kind = JudgeCluster("040", "c3")
                    Count = 4
                    Families = [ "vendor-a"; "vendor-b" ]
                    Detail = "3xNO 1xPARTIAL"
                    SampleFiles = [ "a.json"; "b.json" ]
                    BaitInduced = false
                    ProbeCorpus = false }

              let parse =
                  { Kind = ParseCluster("E_UNKNOWN_KIND", "accordion", "children[].kind")
                    Count = 9
                    Families = [ "vendor-a" ]
                    Detail = "E_UNKNOWN_KIND at $.children[0].kind"
                    SampleFiles = [ "c.json" ]
                    BaitInduced = false
                    ProbeCorpus = true }

              for cluster in [ judge; parse ] do
                  match decodeCluster (encodeCluster [] cluster) with
                  | Error e -> failtestf "read failed: %s" e.Message
                  | Ok(back, extra) ->
                      Expect.equal back cluster "record → line → record"
                      Expect.isEmpty extra "the cluster owns all its members"
          }

          test "a cluster of an unknown kind is refused rather than guessed" {
              match decodeCluster "{\"kind\":\"other\",\"count\":1}" with
              | Error(MemberTypeMismatch("kind", _)) -> ()
              | other -> failtestf "expected a refusal on kind, got %A" other
          } ]
