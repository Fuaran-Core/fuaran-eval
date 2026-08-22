/// **The demand-census engine — stage 1 of the demand loop, generic over the
/// domain seam.** Clusters judge-fails by (task × criterion) and parse-fails by
/// gate reason-class over stored result cells, then diffs the clusters against
/// the intake ledger: a repeated (or cross-family) cluster with NO log row is
/// an intake violation and fails the run — the mechanical teeth behind "intake
/// before classification". Reads stored cells only; spends no provider tokens.
///
/// It also carries the ledger ↔ capability-catalog drift check: a log row
/// citing `cat:<id>` must agree with the catalog entry's lifecycle
/// (log-says-Open vs catalog-says-promoted is drift, and vice versa).
///
/// Every domain-coupled site is a `DomainCensusSeam` field; everything else is
/// here. A domain supplies the seam, and nothing in this module knows what kind
/// of artefact was emitted.
module Fuaran.Eval.Core.DemandCensus

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions

open Fuaran.Eval.Core.DemandSidecar
open Fuaran.Eval.Core.DemandSeams

// ─── Cell loading ─────────────────────────────────────────────────────────

/// The provider-family prefix feeding the cross-family threshold in
/// `isStrong`. A cluster two unrelated families reach for independently is
/// evidence about the vocabulary's shape rather than one vendor's quirks, which
/// is why it earns intake at a count a single-family cluster would not.
let private familyOf (provider: string) : string =
    match provider.Split('-') with
    | [||] -> provider
    | parts -> parts[0]

let private tryStr (el: JsonElement) (name: string) : string option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String ->
        match v.GetString() with
        | null -> None
        | s -> Some s
    | _ -> None

let private tryBool (el: JsonElement) (name: string) : bool option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.True -> Some true
    | true, v when v.ValueKind = JsonValueKind.False -> Some false
    | _ -> None

/// Load census cells from a results directory. Only the domain's OWN arm
/// participates (seam 2) — the demand log is demand on this domain's language.
/// `since` filters on the file-name run stamp (the `<stamp>__…` prefix,
/// ordinal compare — stamps are ISO-sortable).
///
/// Two properties here are worth porting deliberately: the tolerant per-file
/// parse (an unreadable stray file is warned and skipped, not fatal — a census
/// over a mixed-age results directory must survive unknown providers and future
/// DTO fields), and the ordinal string compare on the file-name stamp, which is
/// why nothing here parses a date.
let loadCells (seam: DomainCensusSeam) (dir: string) (since: string option) : CensusCell list =
    if not (Directory.Exists dir) then
        failwith $"results directory not found: {dir}"

    Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
    |> Seq.filter (fun path ->
        let name = string (Path.GetFileName path)

        not (name.StartsWith "_")
        && name.Contains "__"
        && (match since with
            | Some s -> String.CompareOrdinal(name.Split("__")[0], s) >= 0
            | None -> true))
    |> Seq.choose (fun path ->
        try
            use doc = JsonDocument.Parse(File.ReadAllText path)
            let root = doc.RootElement

            match tryStr root "condition" with
            | Some condition when seam.IsOwnLanguageCondition condition ->
                let taskId = tryStr root "task_id" |> Option.defaultValue "?"
                let provider = tryStr root "provider" |> Option.defaultValue "?"

                Some
                    { FileName = string (Path.GetFileName path)
                      TaskId = taskId
                      TaskShort = seam.TaskShortOf taskId
                      Provider = provider
                      Family = familyOf provider
                      // Back-compat: absent `judged` was a real-judge run.
                      Judged = tryBool root "judged" |> Option.defaultValue true
                      ParsePassed = tryBool root "parse_passed"
                      ParseReason = tryStr root "parse_reason" |> Option.defaultValue ""
                      Notes = tryStr root "notes" |> Option.defaultValue ""
                      // Absent / null reads as None ("not recorded"), never as
                      // an empty verdict set.
                      TypedCriterionVerdicts =
                        match root.TryGetProperty "criterion_verdicts" with
                        | true, arr when arr.ValueKind = JsonValueKind.Array ->
                            [ for el in arr.EnumerateArray() do
                                  match tryStr el "id", tryStr el "verdict" with
                                  | Some id, Some v -> yield id, v
                                  | _ -> () ]
                            |> Some
                        | _ -> None }
            | _ -> None
        with _ ->
            // A malformed stray file must not kill the census.
            eprintfn $"warn: skipping unreadable {path}"
            None)
    |> Seq.toList

// ─── Clustering ───────────────────────────────────────────────────────────

/// A criterion-level figure from stored cells alone: for one (taskShort,
/// criterionId), the YES count over all judged+parsed cells that RECORD the
/// criterion (typed field only — a prose fallback cannot supply a denominator).
/// Returns (yesCount, recordedCount); `recordedCount = 0` means the figure is
/// honestly unavailable for that slice, not zero.
let criterionPassCount (cells: CensusCell list) (taskShort: string) (criterionId: string) : int * int =
    let recorded =
        cells
        |> List.filter (fun c -> c.Judged && c.ParsePassed = Some true && c.TaskShort = taskShort)
        |> List.choose (fun c ->
            match c.TypedCriterionVerdicts with
            | Some vs -> vs |> List.tryFind (fun (id, _) -> id = criterionId)
            | None -> None)

    let yes = recorded |> List.filter (fun (_, v) -> v = "YES") |> List.length
    yes, List.length recorded

/// Erase array indices from a path or a path fragment quoted in a log row
/// (`columns[3]` → `columns[]`), so positions compare across emissions.
///
/// This and `slotOfPath` are what stop clusters shattering across per-emission
/// indices while still telling two same-named leaves apart. Port the idea; tune
/// the segment count.
let private normalizeSlot (s: string) : string = Regex.Replace(s, @"\[[^\]]*\]", "[]")

/// The position discriminator for a parse failure: the last three segments of
/// the gate error path, indices erased — enough to tell `columns[].kind.$type`
/// (a grid-cell slot) from `children[].kind.$type` (a node slot) without
/// shattering clusters across per-emission indices. "" for a root-level reason.
let private slotOfPath (path: string) : string =
    let segments =
        (normalizeSlot path).Split('.') |> Array.filter (fun s -> s <> "" && s <> "$")

    segments |> Array.skip (max 0 (segments.Length - 3)) |> String.concat "."

/// Both the `{Code} at {Path}: {Message}` format and the decode-error codes
/// come from the shared tree substrate, so this transfers across domains
/// unchanged.
///
/// **Inherited hazard, stated where it bites.** A decoder stops at the FIRST
/// error, so `reason` names one offender per emission and every later instance
/// is invisible — non-randomly, since an emission repeating the same near-miss
/// contributes once. That is fine for CLUSTERING (a log row names one offender
/// too) and wrong for COUNTING: a falsifier decided on first-error counts is
/// decided on an undercount. Keep the two uses separate; a census built on the
/// stored reason string will conflate them by default.
let private parseReasonClass (reason: string) : string * string * string =
    let cls =
        let head = reason.Split(' ') |> Array.tryHead |> Option.defaultValue reason
        head.TrimEnd(':')

    let token =
        // Prefer the quoted offender ('Title'); else a named missing field
        // ("missing field: columns") — both are what a log row would name.
        let quoted = Regex.Match(reason, @"'([^']+)'")
        let missing = Regex.Match(reason, @"missing (?:required )?field:?\s+'?(\w+)'?")

        if missing.Success then missing.Groups[1].Value
        elif quoted.Success then quoted.Groups[1].Value
        else ""

    let slot =
        // The gate records "{Code} at {Path}: {Message}" — the path has no
        // spaces or colons, so a lazy match up to the first colon is exact.
        let m = Regex.Match(reason, @"^\S+ at (\S+?):")
        if m.Success then slotOfPath m.Groups[1].Value else ""

    cls, token, slot

/// Build the failure clusters for a cohort of cells. Judge clusters come from
/// judged, parsed cells' non-YES criteria (an unparsed cell's all-NO criteria
/// are decode noise — they land in the parse bucket instead).
let buildClusters (seam: DomainCensusSeam) (cells: CensusCell list) : Cluster list =
    let judge =
        cells
        |> List.filter (fun c -> c.Judged && c.ParsePassed = Some true)
        |> List.collect (fun c ->
            // Seam 3. The clusters are built from non-YES verdicts, which every
            // source carries, so clustering is identical across a domain's
            // typed / legacy boundary.
            seam.CriterionVerdictsOf c
            |> List.filter (fun (_, v) -> v <> "YES")
            |> List.map (fun (crit, verdict) -> (c.TaskShort, crit), (verdict, c.Family, c.FileName, c.TaskId)))
        |> List.groupBy fst
        |> List.map (fun ((task, crit), hits) ->
            let verdicts = hits |> List.map (fun (_, (v, _, _, _)) -> v)

            let detail =
                verdicts
                |> List.countBy id
                |> List.sortBy fst
                |> List.map (fun (v, n) -> $"{n}×{v}")
                |> String.concat " "

            { Kind = JudgeCluster(task, crit)
              Count = List.length hits
              Families = hits |> List.map (fun (_, (_, f, _, _)) -> f) |> List.distinct |> List.sort
              Detail = detail
              SampleFiles = hits |> List.map (fun (_, (_, _, file, _)) -> file) |> List.truncate 3
              BaitInduced = false
              ProbeCorpus = hits |> List.forall (fun (_, (_, _, _, tid)) -> seam.IsProbeCorpusTask tid) })

    let parse =
        cells
        |> List.filter (fun c -> c.ParsePassed = Some false)
        |> List.map (fun c ->
            let cls, token, slot = parseReasonClass c.ParseReason
            (cls, token, slot), c)
        |> List.groupBy fst
        |> List.map (fun ((cls, token, slot), hits) ->
            let sampleReason =
                let r = (snd hits[0]).ParseReason
                if r.Length > 160 then r.Substring(0, 160) + "…" else r

            { Kind = ParseCluster(cls, token, slot)
              Count = List.length hits
              Families = hits |> List.map (fun (_, c) -> c.Family) |> List.distinct |> List.sort
              Detail = sampleReason
              SampleFiles = hits |> List.map (fun (_, c) -> c.FileName) |> List.truncate 3
              // Seam 4.
              BaitInduced = hits |> List.forall (fun (_, c) -> seam.IsAdversarialTierTask c.TaskId)
              ProbeCorpus = hits |> List.forall (fun (_, c) -> seam.IsProbeCorpusTask c.TaskId) })

    (judge @ parse) |> List.sortByDescending (fun c -> c.Count)

/// The repeat/cross-family threshold — a cluster this strong demands an
/// intake row before anyone classifies it (demand-loop stage 2).
///
/// Keep the DISJUNCTION when porting. The cross-family arm is the half that is
/// easy to drop and the half that carries the signal a raw count cannot: two
/// unrelated families reaching for the same shape is evidence about the
/// vocabulary, not about a vendor.
let isStrong (minCount: int) (c: Cluster) : bool =
    c.Count >= minCount || List.length c.Families >= 2

// ─── Demand-log parsing + matching ────────────────────────────────────────

/// Parse the demand log's table (| Date | Source | Token or intent |
/// Disposition |). Tolerant: any 4+-column pipe row past the header.
let loadDemandLog (path: string) : LogRow list =
    if not (File.Exists path) then
        failwith $"demand log not found: {path}"

    File.ReadAllLines path
    |> Array.filter (fun l -> l.TrimStart().StartsWith "|")
    |> Array.choose (fun l ->
        let cols = l.Split('|') |> Array.map (fun c -> c.Trim())

        if cols.Length >= 5 && cols[1] <> "Date" && not (cols[1].StartsWith "---") then
            Some
                { Date = cols[1]
                  Source = cols[2]
                  Intent = cols[3]
                  Disposition = cols[4] }
        else
            None)
    |> Array.toList

/// Wire-path fragments a log row names (e.g. `columns[N].kind.$type`). Only a
/// bracket- or `$type`-bearing dotted chain counts as position-naming — plain
/// dotted names (file names, property paths like `kind.subtext`) deliberately
/// do not, so position-less rows keep matching on the token alone.
let private rowSlotFragments (row: LogRow) : string list =
    Regex.Matches($"{row.Text} {row.Disposition}", @"[\w$]+(?:\[[^\]\s]*\])?(?:\.[\w$]+(?:\[[^\]\s]*\])?)+")
    |> Seq.map _.Value
    |> Seq.filter (fun f -> f.Contains '[' || f.Contains "$type")
    |> Seq.map normalizeSlot
    |> Seq.toList

/// A row-quoted fragment and a cluster slot agree when their trailing segments
/// coincide over the shorter of the two — `columns[N].kind.$type` covers
/// `columns[].kind.$type`, and refuses `children[].kind.$type`.
let private slotCompatible (slot: string) (fragment: string) : bool =
    let a = fragment.Split('.')
    let b = slot.Split('.')
    let n = min a.Length b.Length
    n > 0 && Array.forall2 (=) (a[a.Length - n ..]) (b[b.Length - n ..])

/// Does a log row cover a cluster? Judge clusters match on the task number
/// plus, when the row names criteria (`c3`), the criterion id; parse clusters
/// match on the offending token (or reason class when token-less) — and when
/// BOTH the cluster and the row name a wire position, the positions must
/// agree: a token match against a row that pins a different slot is a
/// cluster-key collision, not coverage.
let rowMatches (cluster: Cluster) (row: LogRow) : bool =
    match cluster.Kind with
    | JudgeCluster(task, crit) ->
        let taskMatch = row.Text.Contains task

        let critMatch =
            let rowCrits =
                Regex.Matches(row.Text, @"\bc(\d{1,2})\b")
                |> Seq.map (fun m -> m.Value)
                |> Seq.toList

            List.isEmpty rowCrits || List.contains crit rowCrits

        taskMatch && critMatch
    | ParseCluster(cls, token, slot) ->
        let tokenMatch =
            if token.Length >= 3 then
                row.Text.Contains(token, StringComparison.OrdinalIgnoreCase)
            else
                row.Text.Contains(cls, StringComparison.OrdinalIgnoreCase)

        if not tokenMatch then
            false
        elif slot = "" then
            true
        else
            match rowSlotFragments row with
            | [] -> true // position-less row — the token match stands
            | fragments -> fragments |> List.exists (slotCompatible slot)

let private isWatchRow (row: LogRow) : bool =
    row.Disposition.Contains("demand-gate", StringComparison.OrdinalIgnoreCase)
    || row.Disposition.Contains("watch", StringComparison.OrdinalIgnoreCase)

type Bucket =
    /// A log row exists — show its disposition.
    | Known of LogRow
    /// A log row exists AND its disposition demand-gates on a future cohort —
    /// this cohort is (or may be) that gate; resurface it.
    | WatchExpired of LogRow
    /// No log row — the intake-before-classification candidates.
    | New

let bucketOf (log: LogRow list) (cluster: Cluster) : Bucket =
    // A row that names the criterion explicitly ("040/c3") outranks a row
    // that merely mentions the task, and a row that pins the wire position
    // (`columns[N].kind.$type`) outranks one that only names the token — the
    // specific disposition wins over a generic same-token/same-task watch row.
    let specificity (row: LogRow) =
        match cluster.Kind with
        | JudgeCluster(_, crit) when Regex.IsMatch(row.Text, $@"\b{crit}\b") -> 1
        | ParseCluster(_, _, slot) when slot <> "" && rowSlotFragments row |> List.exists (slotCompatible slot) -> 1
        | _ -> 0

    match log |> List.filter (rowMatches cluster) with
    | [] -> New
    | rows ->
        let top =
            rows
            |> List.groupBy specificity
            |> List.sortByDescending fst
            |> List.head
            |> snd

        match top |> List.tryFind isWatchRow with
        | Some w -> WatchExpired w
        | None -> Known(List.last top) // latest row wins (log is append-only)

// ─── Demand-log ↔ capability-catalog drift ────────────────────────────────

type CatalogDrift =
    { RowSource: string
      CatalogId: string
      Problem: string }

let private catIdsOf (row: LogRow) : string list =
    Regex.Matches($"{row.Text} {row.Disposition}", @"cat:([A-Za-z0-9._\-]+)")
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Seq.toList

let private catalogLifecycle (catalogDir: string) (id: string) : string option =
    let shard = Path.Combine(catalogDir, $"{id}.md")

    if not (File.Exists shard) then
        None
    else
        let m = Regex.Match(File.ReadAllText shard, @"\*\*Lifecycle:\*\*\s*(\w+)")
        if m.Success then Some(m.Groups[1].Value) else Some "?"

/// How much of the OPEN population `driftCheck` can actually see.
///
/// The drift check below is exact but scoped: it examines only rows citing a
/// `cat:<id>`. A row with no token cannot drift, so the check is silent about
/// it — and the untokened rows are precisely the ones that go stale, because
/// nothing links them to the supply side. Measured on a real corpus of 54 open
/// rows: 52 carried no token, so the check covered 4% of the population it reads
/// as clean — which is how three shipped capabilities kept demand rows reading
/// Open and manufactured the premise of two pieces of planned work.
///
/// Reported as ONE LINE, deliberately. Listing the untokened rows would print a
/// 52-line wall every run, and a ratio in front of the operator every run is
/// what changes behaviour. The remedy is authored tokens; nothing here infers
/// one from prose.
type CatalogCoverage =
    { OpenRows: int
      OpenTokened: int }

    /// `None` when there are no open rows at all — a ratio over an empty
    /// population is not 0%, it is undefined, and printing "0%" there would
    /// report a problem that does not exist.
    member this.Percent: float option =
        if this.OpenRows = 0 then
            None
        else
            Some(100.0 * float this.OpenTokened / float this.OpenRows)

/// The leading token owns a row's state, and authors routinely emphasise it
/// (`**Open — …`, `**Shipped — …`). The table parser preserves markdown, so
/// strip leading emphasis markers before reading the state — a bold Open row
/// is still open. Not counting it silently excludes exactly the loudest rows
/// from the coverage denominator: the undercount class, one level down.
let private stripLeadingEmphasis (s: string) = s.TrimStart().TrimStart('*', '_')

let private isOpenRow (row: LogRow) =
    (stripLeadingEmphasis row.Disposition).StartsWith("Open", StringComparison.OrdinalIgnoreCase)

/// The coverage measurement. Counts rows, never guesses links.
let catalogCoverage (log: LogRow list) : CatalogCoverage =
    let openRows = log |> List.filter isOpenRow

    { OpenRows = List.length openRows
      OpenTokened =
        openRows
        |> List.filter (fun r -> not (List.isEmpty (catIdsOf r)))
        |> List.length }

/// Cross-check every log row citing `cat:<id>` against the catalog store:
/// an Open row's entry must still be a Candidate (not silently promoted), a
/// Shipped row's entry must exist and be past Candidate.
let driftCheck (catalogDir: string) (log: LogRow list) : CatalogDrift list =
    log
    |> List.collect (fun row ->
        catIdsOf row
        |> List.choose (fun id ->
            let isOpen = isOpenRow row

            // An Open row is open even when its prose mentions a partial that
            // "shipped" — the leading token owns the row's state.
            let isShipped =
                not isOpen
                && row.Disposition.Contains("Shipped", StringComparison.OrdinalIgnoreCase)

            match catalogLifecycle catalogDir id with
            | None ->
                Some
                    { RowSource = row.Source
                      CatalogId = id
                      Problem = "log cites the id but no catalog entry exists (run `catalog demand`)" }
            | Some lifecycle when isOpen && (lifecycle = "Experimental" || lifecycle = "Stable") ->
                Some
                    { RowSource = row.Source
                      CatalogId = id
                      Problem = $"log says Open but catalog lifecycle is {lifecycle} — flip the row's disposition" }
            | Some "Candidate" when isShipped ->
                Some
                    { RowSource = row.Source
                      CatalogId = id
                      Problem = "log says Shipped but catalog lifecycle is still Candidate — promote at ship time" }
            | Some _ -> None))

// ─── Report ───────────────────────────────────────────────────────────────

let private familiesStr (c: Cluster) = String.concat "," c.Families

/// Drafted intake row for a new cluster (--markdown). The subcommand drafts
/// the row; the operator owns the intent naming and the disposition — intake
/// stays one table row, no ceremony.
let draftRow (today: string) (c: Cluster) : string =
    match c.Kind with
    | JudgeCluster(task, crit) ->
        $"| {today} | {task}/{crit} census ×{c.Count} ({familiesStr c}) | (operator: name the intent — {c.Detail}) | Open — classify by falsifier (teach → shakedown) |"
    | ParseCluster(_, _, _) ->
        // c.Label carries the slot (`… @ columns[].kind.$type`), so a drafted
        // row is position-naming by construction — future rows disambiguate.
        $"| {today} | parse census ×{c.Count} ({familiesStr c}) | {c.Label} — {c.Detail} | Open — classify by falsifier (coercion / alias / teaching) |"

type CensusReport =
    {
        Cells: int
        Clusters: (Cluster * Bucket) list
        Drift: CatalogDrift list
        /// `None` when no catalog was supplied: the coverage of a check that
        /// did not run is not 0%, it is not applicable.
        Coverage: CatalogCoverage option
    }

let runCensus
    (seam: DomainCensusSeam)
    (dirs: string list)
    (since: string option)
    (logPath: string)
    (catalogDir: string option)
    : CensusReport =
    let cells = dirs |> List.collect (fun d -> loadCells seam d since)
    let log = loadDemandLog logPath
    let clusters = buildClusters seam cells |> List.map (fun c -> c, bucketOf log c)

    { Cells = List.length cells
      Clusters = clusters
      Drift = catalogDir |> Option.map (fun d -> driftCheck d log) |> Option.defaultValue []
      Coverage = catalogDir |> Option.map (fun _ -> catalogCoverage log) }

/// Print the report; returns the exit code (1 = intake violation or drift).
let printReport (seam: DomainCensusSeam) (minCount: int) (markdown: bool) (report: CensusReport) : int =
    let strong = isStrong minCount

    let inBucket b =
        report.Clusters
        |> List.filter (fun (c, _) -> not c.BaitInduced && not c.ProbeCorpus)
        |> List.filter (fun (_, bucket) ->
            match bucket, b with
            | Known _, "known"
            | WatchExpired _, "watch"
            | New, "new" -> true
            | _ -> false)

    printfn $"demand-census: {report.Cells} {seam.OwnLanguageLabel} cells\n"

    // Bait-induced clusters report first, under their own heading, and never
    // enter intake or gate arithmetic: an adversarial-tier parse failure is the
    // induced failure WORKING, not organic demand.
    let bait = report.Clusters |> List.filter (fun (c, _) -> c.BaitInduced)

    if not (List.isEmpty bait) then
        printfn
            $"── BAIT-INDUCED (Tier-C parse — the induced failure working; excluded from demand evidence) — {List.length bait} ──"

        for c, _ in bait do
            printfn $"  {c.Label}: ×{c.Count} ({familiesStr c})"

        printfn ""

    // Probe-corpus clusters mirror the bait treatment: a diagnostic track
    // provokes its failures by design, so its clusters are readings of the
    // probe, never demand.
    let probe =
        report.Clusters
        |> List.filter (fun (c, _) -> c.ProbeCorpus && not c.BaitInduced)

    if not (List.isEmpty probe) then
        printfn
            $"── PROBE-CORPUS (prior-alignment / custom-sentinel diagnostics — the probe working; excluded from demand evidence) — {List.length probe} ──"

        for c, _ in probe do
            printfn $"  {c.Label}: ×{c.Count} ({familiesStr c})"

        printfn ""

    let newOnes = inBucket "new"
    let strongNew = newOnes |> List.filter (fst >> strong)

    printfn $"── NEW (no demand-log row) — {List.length newOnes} cluster(s), {List.length strongNew} at threshold ──"

    for c, _ in newOnes do
        let flag = if strong c then "  ⚠ INTAKE REQUIRED" else ""
        let samples = String.concat ", " c.SampleFiles
        printfn $"  {c.Label}: ×{c.Count} ({familiesStr c}) {c.Detail}{flag}"
        printfn $"      e.g. {samples}"

    if markdown && not (List.isEmpty newOnes) then
        let today = DateTime.UtcNow.ToString "yyyy-MM-dd"
        printfn "\n  drafted intake rows (paste into docs/CAPABILITY-DEMAND-LOG.md):"

        for c, _ in newOnes do
            printfn $"  {draftRow today c}"

    let watch = inBucket "watch"
    printfn $"\n── WATCH-EXPIRED (row demand-gates a future cohort; the cluster is back) — {List.length watch} ──"

    for c, bucket in watch do
        match bucket with
        | WatchExpired row ->
            printfn $"  {c.Label}: ×{c.Count} ({familiesStr c}) — row [{row.Source}] says: {row.Disposition}"
        | _ -> ()

    let known = inBucket "known"
    printfn $"\n── KNOWN (logged) — {List.length known} ──"

    for c, bucket in known do
        match bucket with
        | Known row -> printfn $"  {c.Label}: ×{c.Count} ({familiesStr c}) — {row.Disposition}"
        | _ -> ()

    // The coverage line prints whenever a catalog was supplied, INCLUDING when
    // the drift list is empty. An empty drift report over a 4% sample is the
    // misreading this exists to prevent: it looks like "no drift" and means
    // "almost nothing was examined".
    match report.Coverage with
    | Some cov ->
        let pct =
            match cov.Percent with
            | Some p -> $"%.0f{p}%%"
            | None -> "n/a"

        printfn ""

        printfn
            $"catalog linkage: {cov.OpenTokened}/{cov.OpenRows} open row(s) carry a cat:<id> ({pct} covered by the drift check)"

        if cov.OpenTokened < cov.OpenRows then
            printfn
                "  an untokened row cannot drift, so the check above is silent about it - author the tokens (tools/demand-log-sync.md)"
    | None -> ()

    if not (List.isEmpty report.Drift) then
        printfn $"\n── CATALOG DRIFT — {List.length report.Drift} ──"

        for d in report.Drift do
            printfn $"  cat:{d.CatalogId} (row: {d.RowSource}): {d.Problem}"

    let violations = List.length strongNew + List.length report.Drift

    if violations > 0 then
        printfn
            $"\nFAIL: {List.length strongNew} unlogged repeated/cross-family cluster(s) + {List.length report.Drift} catalog drift(s) — intake before classification."

        1
    else
        printfn "\nOK: every repeated cluster has a demand-log row; no catalog drift."
        0
