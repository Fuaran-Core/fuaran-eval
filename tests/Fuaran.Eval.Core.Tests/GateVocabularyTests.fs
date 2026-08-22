/// DECISIONS D3 says this substrate carries no gate, and CONTRIBUTING repeats
/// it. A rule stated in two documents and checked in none is a rule that lasts
/// until the first convenient exception, so it is checked here.
///
/// The check reads the library's own SOURCES rather than its compiled surface,
/// because the thing being forbidden is not a public type — it is a private
/// helper that decides whether an emission is valid, which would never appear in
/// a public-API assertion and is exactly how a second definition of "valid"
/// would arrive.
module Fuaran.Eval.Core.Tests.GateVocabularyTests

open System.IO
open System.Text.RegularExpressions
open Expecto

/// Walk up from the test assembly to the repository root. The suite must not
/// depend on the working directory it was launched from, and it must FAIL rather
/// than skip when it cannot find the sources: a source sweep that silently
/// examines nothing is a vacuous green, which is worse than no check.
let private sourceDir () =
    let rec up (dir: DirectoryInfo | null) =
        match dir with
        | null ->
            failwith "could not locate src/Fuaran.Eval.Core from the test assembly — the sweep would examine nothing"
        | d ->
            let candidate = Path.Combine(d.FullName, "src", "Fuaran.Eval.Core")

            if Directory.Exists candidate then
                candidate
            else
                up d.Parent

    let here =
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)

    up (DirectoryInfo(nonNull here))

let private sources () =
    Directory.GetFiles(sourceDir (), "*.fs")
    |> Array.map (fun p -> Path.GetFileName p, File.ReadAllText p)

/// Comments describe the boundary and must be allowed to name it; the ban is on
/// code. Stripping `///` and `//` lines is crude and deliberately so — a
/// violation hidden by being phrased as a comment is not executable.
let private code (text: string) =
    text.Split('\n')
    |> Array.filter (fun l ->
        let t = l.TrimStart()
        not (t.StartsWith "///" || t.StartsWith "//"))
    |> String.concat "\n"

[<Tests>]
let tests =
    testList
        "GateVocabulary"
        [ test "the sweep actually reads the sources" {
              // Verify the probe before trusting its verdict. A path bug here
              // would turn every assertion below into a pass over an empty set.
              let files = sources ()
              Expect.equal (Array.length files) 5 "all five modules are swept"

              Expect.isTrue
                  (files |> Array.forall (fun (_, t) -> t.Length > 500))
                  "each source was actually read, not just listed"
          }

          test "no module validates, decodes or judges an emission" {
              // The vocabulary a gate needs. `decode` is the sharpest of these:
              // a substrate that decoded emitted content would be deciding what
              // counts as well-formed, which is the domain's call.
              let banned =
                  [ @"\bvalidate\w*\s*\("
                    @"\bdecodeNode\b"
                    @"\bPreEmitValidate\b"
                    @"\bgateNodeJson\b" ]

              let hits =
                  [ for name, text in sources () do
                        for pattern in banned do
                            for m in Regex.Matches(code text, pattern) do
                                yield $"{name}: {m.Value}" ]

              Expect.isEmpty hits "a gate belongs to the domain being evaluated, never to this substrate (DECISIONS D3)"
          }

          test "nothing here starts a process or opens a socket" {
              let banned =
                  [ @"System\.Diagnostics\.Process"; @"HttpClient"; @"WebRequest"; @"Socket\b" ]

              let hits =
                  [ for name, text in sources () do
                        for pattern in banned do
                            if Regex.IsMatch(code text, pattern) then
                                yield $"{name}: {pattern}" ]

              Expect.isEmpty
                  hits
                  "the census reads files it is handed a path to; that is the whole of its outside world"
          }

          test "the library takes no dependency beyond FSharp.Core and the framework" {
              let proj = File.ReadAllText(Path.Combine(sourceDir (), "Fuaran.Eval.Core.fsproj"))

              let refs =
                  Regex.Matches(proj, @"PackageReference Include=""([^""]+)""")
                  |> Seq.map (fun m -> m.Groups[1].Value)
                  |> Seq.toList

              Expect.equal refs [ "FSharp.Core" ] "DECISIONS D2 — the dependency list is defended, not incidental"
              Expect.isFalse (proj.Contains "ProjectReference") "the substrate is the root of the graph"
          } ]
