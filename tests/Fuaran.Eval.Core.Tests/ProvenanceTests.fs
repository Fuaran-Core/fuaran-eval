module Fuaran.Eval.Core.Tests.ProvenanceTests

open Expecto
open Fuaran.Eval.Core.EvalProvenance

[<Tests>]
let tests =
    testList
        "Provenance"
        [ test "a gate identity with a version stamps as gate:version" {
              let g =
                  { Gate = "wire-decode"
                    Version = "0.31.0" }

              Expect.equal g.AsStamp "wire-decode:0.31.0" "the single-token form joins on a colon"
          }

          test "a versionless gate stamps as the bare gate name" {
              // Not "wire-decode:" — a trailing separator reads as an empty
              // version rather than as no version, and the two are different
              // claims.
              let g = { Gate = "wire-decode"; Version = "" }
              Expect.equal g.AsStamp "wire-decode" "no version means no separator"
          }

          test "the honest zero is not complete" {
              Expect.isFalse unstamped.IsComplete "an unstamped stamp must never read as complete"
          }

          test "a stamp missing only its gates is not complete" {
              // The field most likely to be forgotten, because the other three
              // are single strings a caller naturally has to hand and this one
              // is a list that defaults to empty without complaint.
              let s =
                  { unstamped with
                      CohortId = "since-2026-08-01"
                      HarnessSha = "32a6d8c5"
                      PromptSha256 = "ab12" }

              Expect.isFalse s.IsComplete "an empty gate list leaves the label unattributed"
          }

          test "a fully-stamped figure is complete" {
              let s =
                  { CohortId = "since-2026-08-01"
                    HarnessSha = "32a6d8c5"
                    PromptSha256 = "ab12"
                    Gates =
                      [ { Gate = "wire-decode"
                          Version = "0.31.0" } ]
                    DecoderVersion = "0.31.0" }

              Expect.isTrue s.IsComplete "every field a reader needs is present"
          }

          test "the decoder version alone does not make a stamp complete" {
              // The misattribution the record exists to prevent: a suite that
              // gates several arms and records only the wire decoder's version
              // has said nothing about the arms the decoder never saw.
              let s =
                  { unstamped with
                      CohortId = "c"
                      HarnessSha = "h"
                      PromptSha256 = "p"
                      DecoderVersion = "0.31.0" }

              Expect.isFalse s.IsComplete "DecoderVersion is not a member of Gates and must not stand in for one"
          } ]
