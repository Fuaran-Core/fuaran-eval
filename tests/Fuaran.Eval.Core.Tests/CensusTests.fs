module Fuaran.Eval.Core.Tests.CensusTests

open Expecto
open Fuaran.Eval.Core.DemandSidecar
open Fuaran.Eval.Core.DemandSeams
open Fuaran.Eval.Core.DemandCensus

let private seam = minimal "mine"

let private cell taskShort family verdicts =
    { FileName = $"2026-08-22T00-00-00__{taskShort}.json"
      TaskId = $"task-{taskShort}"
      TaskShort = taskShort
      Provider = $"{family}-model"
      Family = family
      Judged = true
      ParsePassed = Some true
      ParseReason = ""
      Notes = ""
      TypedCriterionVerdicts = Some verdicts }

let private parseFail taskShort family reason =
    { cell taskShort family [] with
        ParsePassed = Some false
        ParseReason = reason }

[<Tests>]
let tests =
    testList
        "Census"
        [ testList
              "isStrong — keep the disjunction"
              [ test "a repeated single-family cluster is strong on count" {
                    let c =
                        { Kind = JudgeCluster("040", "c3")
                          Count = 3
                          Families = [ "claude" ]
                          Detail = ""
                          SampleFiles = []
                          BaitInduced = false
                          ProbeCorpus = false }

                    Expect.isTrue (isStrong 3 c) "count alone reaches the threshold"
                }

                test "a cross-family cluster is strong BELOW the count threshold" {
                    // The arm that is easy to drop and carries the signal a raw
                    // count cannot: two unrelated families reaching for the same
                    // shape is evidence about the vocabulary, not about a vendor.
                    let c =
                        { Kind = JudgeCluster("040", "c3")
                          Count = 2
                          Families = [ "claude"; "gpt" ]
                          Detail = ""
                          SampleFiles = []
                          BaitInduced = false
                          ProbeCorpus = false }

                    Expect.isTrue (isStrong 5 c) "two families outrank the count threshold"
                }

                test "a lone single-family sighting is not strong" {
                    let c =
                        { Kind = JudgeCluster("040", "c3")
                          Count = 1
                          Families = [ "claude" ]
                          Detail = ""
                          SampleFiles = []
                          BaitInduced = false
                          ProbeCorpus = false }

                    Expect.isFalse (isStrong 3 c) "one sighting is log-and-watch, not ship"
                } ]

          testList
              "clustering"
              [ test "non-YES criteria cluster by task and criterion" {
                    let cells =
                        [ cell "040" "claude" [ "c1", "YES"; "c3", "NO" ]
                          cell "040" "gpt" [ "c3", "PARTIAL" ]
                          cell "041" "claude" [ "c3", "NO" ] ]

                    let clusters = buildClusters seam cells

                    let c040 = clusters |> List.find (fun c -> c.Kind = JudgeCluster("040", "c3"))

                    Expect.equal c040.Count 2 "both 040/c3 non-YES verdicts land in one cluster"
                    Expect.equal c040.Families [ "claude"; "gpt" ] "the families are collected and sorted"

                    Expect.isFalse
                        (clusters |> List.exists (fun c -> c.Kind = JudgeCluster("040", "c1")))
                        "a YES verdict is not a cluster"
                }

                test "an UNPARSED cell's criteria are decode noise, not judge demand" {
                    let cells =
                        [ { cell "040" "claude" [ "c3", "NO" ] with
                              ParsePassed = Some false } ]

                    Expect.isEmpty
                        (buildClusters seam cells
                         |> List.filter (fun c -> c.Kind = JudgeCluster("040", "c3")))
                        "an unparsed cell contributes to the parse bucket only"
                }

                test "parse failures cluster by reason class, token and slot" {
                    let cells =
                        [ parseFail "040" "claude" "UnknownKind at root.columns[0].kind.$type: unknown 'Sparkline'"
                          parseFail "041" "gpt" "UnknownKind at root.columns[3].kind.$type: unknown 'Sparkline'" ]

                    let clusters = buildClusters seam cells
                    Expect.equal (List.length clusters) 1 "the per-emission index is erased, so the two share a cluster"
                    Expect.equal clusters[0].Count 2 "both contribute"
                }

                test "the same token at two positions is TWO demands" {
                    let cells =
                        [ parseFail "040" "claude" "UnknownKind at root.columns[0].kind.$type: unknown 'Sparkline'"
                          parseFail "041" "gpt" "UnknownKind at root.children[0].kind.$type: unknown 'Sparkline'" ]

                    Expect.equal
                        (List.length (buildClusters seam cells))
                        2
                        "slot is part of the cluster key, so a grid cell and a node are not one demand"
                } ]

          testList
              "intake matching"
              [ test "a row naming the task covers a judge cluster" {
                    let cluster =
                        { Kind = JudgeCluster("040", "c3")
                          Count = 2
                          Families = []
                          Detail = ""
                          SampleFiles = []
                          BaitInduced = false
                          ProbeCorpus = false }

                    let row =
                        { Date = "2026-08-22"
                          Source = "040 census"
                          Intent = "wants a thing"
                          Disposition = "Open" }

                    Expect.isTrue (rowMatches cluster row) "the task number is the judge-cluster key"
                }

                test "a row pinning a DIFFERENT slot does not cover the cluster" {
                    // A token match against a row that pins another position is
                    // a cluster-key collision, not coverage — the failure mode
                    // that makes a census read as covered while the demand is
                    // unlogged.
                    let cluster =
                        { Kind = ParseCluster("UnknownKind", "Sparkline", "columns[].kind.$type")
                          Count = 2
                          Families = []
                          Detail = ""
                          SampleFiles = []
                          BaitInduced = false
                          ProbeCorpus = false }

                    let row =
                        { Date = "2026-08-22"
                          Source = "census"
                          Intent = "Sparkline at children[N].kind.$type"
                          Disposition = "Open" }

                    Expect.isFalse (rowMatches cluster row) "the positions disagree, so the row does not cover it"
                }

                test "a position-less row still covers on the token alone" {
                    let cluster =
                        { Kind = ParseCluster("UnknownKind", "Sparkline", "columns[].kind.$type")
                          Count = 2
                          Families = []
                          Detail = ""
                          SampleFiles = []
                          BaitInduced = false
                          ProbeCorpus = false }

                    let row =
                        { Date = "2026-08-22"
                          Source = "census"
                          Intent = "models want Sparkline"
                          Disposition = "Open" }

                    Expect.isTrue (rowMatches cluster row) "a row that names no position is not contradicted by one"
                }

                test "a cluster with no row buckets as New" {
                    let cluster =
                        { Kind = JudgeCluster("099", "c1")
                          Count = 2
                          Families = []
                          Detail = ""
                          SampleFiles = []
                          BaitInduced = false
                          ProbeCorpus = false }

                    Expect.equal (bucketOf [] cluster) New "no row means intake is owed"
                } ]

          testList
              "criterion pass counts"
              [ test "recordedCount 0 means unavailable, not zero" {
                    // The distinction the denominator rests on: a slice nothing
                    // recorded has no pass rate, and reporting 0/0 as 0% would
                    // manufacture a failure.
                    let cells =
                        [ { cell "040" "claude" [] with
                              TypedCriterionVerdicts = None } ]

                    let yes, recorded = criterionPassCount cells "040" "c3"
                    Expect.equal recorded 0 "nothing recorded the criterion"
                    Expect.equal yes 0 "and so nothing passed it either — the pair must be read together"
                }

                test "only typed verdicts supply a denominator" {
                    let cells =
                        [ cell "040" "claude" [ "c3", "YES" ]
                          cell "040" "gpt" [ "c3", "NO" ]
                          { cell "040" "gemini" [] with
                              TypedCriterionVerdicts = None } ]

                    let yes, recorded = criterionPassCount cells "040" "c3"
                    Expect.equal (yes, recorded) (1, 2) "the untyped cell is absent from both halves"
                } ]

          testList
              "catalog coverage"
              [ test "coverage over an empty open population is undefined, not 0%" {
                    let cov = catalogCoverage []
                    Expect.isNone cov.Percent "a ratio over nothing is not zero"
                }

                test "a bold Open row still counts as open" {
                    // Authors emphasise the leading token, and not stripping it
                    // silently excludes exactly the loudest rows from the
                    // denominator.
                    let rows =
                        [ { Date = "d"
                            Source = "s"
                            Intent = "i"
                            Disposition = "**Open — classify by falsifier" }
                          { Date = "d"
                            Source = "s"
                            Intent = "i cat:some.thing"
                            Disposition = "Open" } ]

                    let cov = catalogCoverage rows
                    Expect.equal cov.OpenRows 2 "both rows are open"
                    Expect.equal cov.OpenTokened 1 "only one carries a token"
                    Expect.equal cov.Percent (Some 50.0) "the ratio is over both"
                } ]

          testList
              "the sidecar keeps the delta"
              [ test "a flipped row is visible as a disagreement" {
                    let r =
                        { File = "f"
                          Task = "040"
                          Provider = "claude"
                          Recorded = Some true
                          Fresh = false
                          Reason = "UnknownKind"
                          Decoder = "0.31.0" }

                    Expect.isTrue r.Flipped "recorded and fresh disagree"
                }

                test "a cell with NO recorded label is stable, never a flip" {
                    // Collapsing None into false would manufacture a flip out of
                    // a cell that never carried a claim.
                    let r =
                        { File = "f"
                          Task = "040"
                          Provider = "claude"
                          Recorded = None
                          Fresh = false
                          Reason = ""
                          Decoder = "0.31.0" }

                    Expect.isFalse r.Flipped "nothing was claimed, so nothing was invalidated"
                } ]

          testList
              "the minimal seam"
              [ test "minimal switches the exclusions OFF, which is why it is not a default" {
                    let s = minimal "mine"
                    Expect.isFalse (s.IsAdversarialTierTask "bait-anything") "no adversarial marker"
                    Expect.isFalse (s.IsProbeCorpusTask "probe-anything") "no probe marker"
                    Expect.isTrue (s.IsOwnLanguageCondition "mine") "own condition matches"
                    Expect.isFalse (s.IsOwnLanguageCondition "theirs") "a comparator arm does not"
                } ] ]
