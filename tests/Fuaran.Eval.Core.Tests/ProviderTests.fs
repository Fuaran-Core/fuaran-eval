module Fuaran.Eval.Core.Tests.ProviderTests

open Expecto
open Fuaran.Eval.Core.EvalProvider

let private completion text =
    { OutputText = text
      InputTokens = 10
      OutputTokens = 20
      ReasoningOutputTokens = 5
      CacheCreationInputTokens = 0
      CacheReadInputTokens = 0
      WallClockMs = 42 }

[<Tests>]
let tests =
    testList
        "Provider"
        [ test "ofFuncs presents a function pair through the seam" {
              let p =
                  ofFuncs "acme" "acme-1" (fun _ msgs ->
                      async { return completion (msgs |> List.map _.Content |> String.concat "|") })

              Expect.equal p.ProviderKey "acme" "the provider key is carried verbatim"
              Expect.equal p.ModelId "acme-1" "the model id is carried verbatim"

              let r =
                  p.Invoke("sys", [ { Role = "user"; Content = "a" }; { Role = "user"; Content = "b" } ])
                  |> Async.RunSynchronously

              Expect.equal r.OutputText "a|b" "the messages reach the function in order"
          }

          test "replay serves a recorded completion keyed by the last message" {
              let p = replay "acme" "acme-1" (Map [ "ask", completion "answer" ])

              let r =
                  p.Invoke("sys", [ { Role = "user"; Content = "ask" } ])
                  |> Async.RunSynchronously

              Expect.equal r.OutputText "answer" "the recorded completion is served"
          }

          test "replay FAILS on a miss rather than generating" {
              // The whole posture. A fake that invents a completion turns a
              // replay run into an unlabelled live run — indistinguishable from
              // the real thing in the result file, which is the one failure a
              // replay harness must not be able to have.
              let p = replay "acme" "acme-1" (Map [ "ask", completion "answer" ])

              Expect.throws
                  (fun () ->
                      p.Invoke(
                          "sys",
                          [ { Role = "user"
                              Content = "unrecorded" } ]
                      )
                      |> Async.RunSynchronously
                      |> ignore)
                  "an unrecorded invocation must refuse, never fabricate"
          }

          test "replay keyed on the LAST message, not the first" {
              let p = replay "acme" "acme-1" (Map [ "second", completion "right" ])

              let r =
                  p.Invoke("sys", [ { Role = "user"; Content = "first" }; { Role = "user"; Content = "second" } ])
                  |> Async.RunSynchronously

              Expect.equal r.OutputText "right" "a multi-turn replay keys on the turn being asked"
          }

          test "notWired refuses rather than returning an empty completion" {
              let p = notWired "acme"
              Expect.stringContains p.ModelId "not-yet-wired" "the model id says what it is"

              Expect.throws
                  (fun () -> p.Invoke("sys", []) |> Async.RunSynchronously |> ignore)
                  "a declared-but-unwired provider must refuse"
          }

          test "the reasoning share is carried separately from billed output" {
              // The compactness measurand is OutputTokens - ReasoningOutputTokens
              // while cost uses OutputTokens; collapsing the two loses one of
              // them, and which one is lost depends on who reads it.
              let c = completion "x"
              Expect.equal (c.OutputTokens - c.ReasoningOutputTokens) 15 "the text emission size is derivable"
          } ]
