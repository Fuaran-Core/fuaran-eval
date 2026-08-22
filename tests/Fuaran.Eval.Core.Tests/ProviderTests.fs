module Fuaran.Eval.Core.Tests.ProviderTests

open Expecto
open Fuaran.Eval.Core.EvalProvider

let private usage =
    { InputTokens = 10
      OutputTokens = 20
      ReasoningOutputTokens = 5
      CacheCreationInputTokens = 0
      CacheReadInputTokens = 0
      WallClockMs = 42 }

let private completion result = { Result = result; Usage = Some usage }

/// A branch-shaped result — the second binding's shape, in miniature. The
/// discrimination IS the result: a scorer reads which case came back, and a seam
/// that flattened this to text would hand it a string it would have to re-parse.
type private Emission =
    | EmittedDocument of json: string
    | EmittedOp of json: string
    | Refused of reason: string

[<Tests>]
let tests =
    testList
        "Provider"
        [ test "ofFuncs presents a function pair through the seam" {
              let p: ITextEvalProvider =
                  ofFuncs "acme" "acme-1" (fun req ->
                      async { return completion (req.Messages |> List.map _.Content |> String.concat "|") })

              Expect.equal p.ProviderKey "acme" "the provider key is carried verbatim"
              Expect.equal p.ModelId "acme-1" "the model id is carried verbatim"

              let r =
                  p.Invoke(
                      requestKeyedOnLastMessage
                          "sys"
                          [ { Role = "user"; Content = "a" }; { Role = "user"; Content = "b" } ]
                  )
                  |> Async.RunSynchronously

              Expect.equal r.Result "a|b" "the messages reach the function in order"
          }

          test "the system prompt reaches the provider on the request" {
              let p: ITextEvalProvider =
                  ofFuncs "acme" "acme-1" (fun req -> async { return completion req.SystemPrompt })

              let r = p.Invoke(request "k" "the system prompt" []) |> Async.RunSynchronously

              Expect.equal r.Result "the system prompt" "the system prompt is carried verbatim"
          }

          test "a branch-shaped result survives the seam undiminished" {
              // The finding the second binding produced. The provider answers in
              // cases; the seam carries the case, not a rendering of it.
              let p: IEvalProvider<Emission> =
                  ofFuncs "acme" "acme-1" (fun req ->
                      async {
                          return
                              completion (
                                  match req.InvocationKey with
                                  | "make-doc" -> EmittedDocument "{\"doc\":1}"
                                  | "edit-doc" -> EmittedOp "{\"op\":1}"
                                  | other -> Refused $"no branch for '{other}'"
                              )
                      })

              let branchFor key =
                  p.Invoke(request key "sys" []) |> Async.RunSynchronously |> _.Result

              Expect.equal
                  (branchFor "make-doc")
                  (EmittedDocument "{\"doc\":1}")
                  "the document branch arrives as itself"

              Expect.equal (branchFor "edit-doc") (EmittedOp "{\"op\":1}") "the op branch arrives as itself"
              Expect.equal (branchFor "nope") (Refused "no branch for 'nope'") "the refusal branch arrives as itself"
          }

          test "replay serves a recorded completion keyed by the caller's key" {
              let p = replay "acme" "acme-1" (Map [ "case-7", completion "answer" ])

              let r =
                  p.Invoke(
                      request
                          "case-7"
                          "sys"
                          [ { Role = "user"
                              Content = "anything at all" } ]
                  )
                  |> Async.RunSynchronously

              Expect.equal r.Result "answer" "the recorded completion is served"
          }

          test "replay FAILS on a miss rather than generating" {
              // The whole posture. A fake that invents a completion turns a
              // replay run into an unlabelled live run — indistinguishable from
              // the real thing in the result file, which is the one failure a
              // replay harness must not be able to have.
              let p = replay "acme" "acme-1" (Map [ "ask", completion "answer" ])

              Expect.throws
                  (fun () -> p.Invoke(request "unrecorded" "sys" []) |> Async.RunSynchronously |> ignore)
                  "an unrecorded invocation must refuse, never fabricate"
          }

          test "replay refuses a branch-shaped miss too, with no case to hide in" {
              // The refusal survives the result becoming a type parameter, and a
              // domain whose union HAS a failure case does not get a polite
              // answer to a miss: the seam raises rather than choosing one.
              let p = replay "acme" "acme-1" (Map [ "known", completion (EmittedDocument "{}") ])

              Expect.throws
                  (fun () -> p.Invoke(request "unknown" "sys" []) |> Async.RunSynchronously |> ignore)
                  "a miss refuses even when the result type could express a refusal"
          }

          test "the conversational key is the LAST message, and it is the caller's choice" {
              // The derivation the seam used to perform internally is now written
              // at the call site — same behaviour, and a case-driven caller can
              // key on something else without the seam knowing.
              let req =
                  requestKeyedOnLastMessage
                      "sys"
                      [ { Role = "user"; Content = "first" }; { Role = "user"; Content = "second" } ]

              Expect.equal req.InvocationKey "second" "a multi-turn replay keys on the turn being asked"

              let p = replay "acme" "acme-1" (Map [ "second", completion "right" ])
              Expect.equal (p.Invoke(req) |> Async.RunSynchronously).Result "right" "and the replay finds it"
          }

          test "notWired refuses rather than returning an empty completion" {
              let p: ITextEvalProvider = notWired "acme"
              Expect.stringContains p.ModelId "not-yet-wired" "the model id says what it is"

              Expect.throws
                  (fun () -> p.Invoke(request "k" "sys" []) |> Async.RunSynchronously |> ignore)
                  "a declared-but-unwired provider must refuse"
          }

          test "unreported usage is None, not a row of zeros" {
              // A provider that reports no counters — the second binding is one —
              // must be distinguishable from one that ran free, or a cohort's cost
              // figure reads as measured when it was invented.
              let p: ITextEvalProvider =
                  ofFuncs "acme" "acme-1" (fun _ -> async { return { Result = "x"; Usage = None } })

              let r = p.Invoke(request "k" "sys" []) |> Async.RunSynchronously
              Expect.isNone r.Usage "no usage payload is absent, never zero"
          }

          test "the reasoning share is carried separately from billed output" {
              // The compactness measurand is OutputTokens - ReasoningOutputTokens
              // while cost uses OutputTokens; collapsing the two loses one of
              // them, and which one is lost depends on who reads it.
              Expect.equal (usage.OutputTokens - usage.ReasoningOutputTokens) 15 "the text emission size is derivable"
          } ]
