/// The small JSON value this library writes its artefacts with, and the
/// deterministic renderer that turns members into bytes.
///
/// **Why it exists at all.** The artefacts a harness stores — a result cell, a
/// re-gate manifest line — are a contract between a writer and every later
/// reader, and until now this library typed those artefacts without owning
/// their BYTES. A type with no codec beside it is an invitation: each adopting
/// harness spells the same documented artefact its own way, the spellings
/// diverge in field names and member order, and nothing fails, because each
/// harness reads only what it wrote.
///
/// Owning the bytes means the substrate must be able to write members it is not
/// allowed to NAME: a domain's own fields are the domain's business, and a
/// substrate that enumerated them would be back to carrying domain vocabulary.
/// `EvalValue` is what those members are handed over as — enough structure to
/// round-trip anything JSON can hold, and no opinion about what any of it means.
///
/// Rendering goes through `Utf8JsonWriter` rather than string building, for the
/// reason a manifest of gate errors makes obvious: a gate reason carries the
/// offending path and can hold quotes, braces and newlines, so a hand-escaped
/// artefact corrupts exactly the rows that matter most.
module Fuaran.Eval.Core.EvalJson

open System.IO
open System.Text
open System.Text.Json

/// The `Result` builder both codecs read with. A reader that must check
/// fifteen members answers on the FIRST failure, and spelling that as nested
/// matches buries the shape of the record being built under the plumbing.
type ResultBuilder() =
    member _.Bind(r, f) = Result.bind f r
    member _.Return v = Ok v
    member _.ReturnFrom(r: Result<_, _>) = r

let result = ResultBuilder()

/// A JSON value. `JObj` carries an ORDERED member list rather than a map,
/// because member order is part of the bytes a deterministic writer promises
/// and a map would silently decide it.
type EvalValue =
    | JNull
    | JBool of bool
    | JInt of int64
    | JFloat of float
    | JStr of string
    | JArr of EvalValue list
    | JObj of (string * EvalValue) list

let rec private writeValue (writer: Utf8JsonWriter) (value: EvalValue) : unit =
    match value with
    | JNull -> writer.WriteNullValue()
    | JBool b -> writer.WriteBooleanValue b
    | JInt n -> writer.WriteNumberValue n
    | JFloat f -> writer.WriteNumberValue f
    | JStr s -> writer.WriteStringValue s
    | JArr items ->
        writer.WriteStartArray()

        for item in items do
            writeValue writer item

        writer.WriteEndArray()
    | JObj members ->
        writer.WriteStartObject()

        for name, member' in members do
            writer.WritePropertyName name
            writeValue writer member'

        writer.WriteEndObject()

/// Render members as one JSON object. `indented` picks the two shapes this
/// library's artefacts take: an indented document per stored cell, and one
/// compact line per manifest row.
///
/// Deterministic by construction: members are written in the order given, and
/// nothing here sorts, dedupes or re-cases a name.
let renderObject (indented: bool) (members: (string * EvalValue) list) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = indented))
    writeValue writer (JObj members)
    writer.Flush()
    Encoding.UTF8.GetString(stream.ToArray())

/// The inverse: an element as an `EvalValue`, member order preserved.
///
/// `Undefined` reads as null rather than raising. It is not a value a parsed
/// document produces, and a total reader that throws on an impossible case is a
/// total reader with a hole in it.
let rec ofElement (element: JsonElement) : EvalValue =
    match element.ValueKind with
    | JsonValueKind.True -> JBool true
    | JsonValueKind.False -> JBool false
    | JsonValueKind.String ->
        match element.GetString() with
        | null -> JStr ""
        | s -> JStr s
    | JsonValueKind.Number ->
        match element.TryGetInt64() with
        | true, n -> JInt n
        | _ -> JFloat(element.GetDouble())
    | JsonValueKind.Array -> JArr [ for item in element.EnumerateArray() -> ofElement item ]
    | JsonValueKind.Object ->
        JObj [ for property in element.EnumerateObject() -> property.Name, ofElement property.Value ]
    | _ -> JNull

/// Parse one JSON document into its top-level members, in document order.
/// `Error` carries the parser's own message: a caller that cannot read a file
/// wants to know why, and this layer has nothing better to say than the parser.
let parseObject (json: string) : Result<(string * EvalValue) list, string> =
    try
        use document = JsonDocument.Parse json

        match ofElement document.RootElement with
        | JObj members -> Ok members
        | _ -> Error "the document's root is not a JSON object"
    with :? JsonException as ex ->
        Error ex.Message

// ─── Member accessors ─────────────────────────────────────────────────────
//
// Deliberately option-returning rather than defaulting. A reader that cannot
// tell "absent" from "empty" is the failure `ProvenanceStamp` spends its whole
// design avoiding, one layer down: absent means UNKNOWN, and only the caller
// knows whether unknown is fatal here.

let tryMember (name: string) (members: (string * EvalValue) list) : EvalValue option =
    members |> List.tryPick (fun (n, v) -> if n = name then Some v else None)

let tryString (name: string) (members: (string * EvalValue) list) : string option =
    match tryMember name members with
    | Some(JStr s) -> Some s
    | _ -> None

let tryBool (name: string) (members: (string * EvalValue) list) : bool option =
    match tryMember name members with
    | Some(JBool b) -> Some b
    | _ -> None

let tryInt (name: string) (members: (string * EvalValue) list) : int option =
    match tryMember name members with
    | Some(JInt n) -> Some(int n)
    | _ -> None

/// The members NOT named in `names`, in their original order — what a reader
/// hands on to a domain, or keeps as the cell's carried extras.
let except (names: string list) (members: (string * EvalValue) list) : (string * EvalValue) list =
    let taken = Set.ofList names
    members |> List.filter (fun (n, _) -> not (taken.Contains n))
