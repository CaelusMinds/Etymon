namespace Etymon

open System
open System.Text

/// <summary>
/// TypeScript declarations generated from a schema.
/// </summary>
/// <remarks>
/// <para>
/// Types only. No fetch client, no runtime validator, no opinion about axios
/// against fetch or ESM against CommonJS. Those are choices that age badly and
/// that every frontend has already made differently; the part with a single
/// right answer is the shape of the data, and that is what this emits.
/// </para>
/// <para>
/// It emits from the schema directly rather than going through an OpenAPI
/// document, which is one fewer representation to lose information in.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module TypeScript =

    /// TypeScript keeps `undefined` for "absent" and `null` for "present and
    /// empty", exactly as JSON Schema does. Etymon's optional and nullable map
    /// onto them one for one, which is why the two were kept apart in the schema
    /// type in the first place.
    let rec private typeOf (info: SchemaInfo) : string =
        match SchemaInfo.strip info with
        | SPrim PrimKind.String
        | SPrim PrimKind.Guid
        | SPrim PrimKind.DateTimeOffset
        | SPrim PrimKind.DateOnly
        | SPrim PrimKind.TimeOnly
        | SPrim PrimKind.TimeSpan
        | SPrim PrimKind.Bytes -> "string"
        | SPrim PrimKind.Int
        | SPrim PrimKind.Int64
        | SPrim PrimKind.Float
        | SPrim PrimKind.Decimal -> "number"
        | SPrim PrimKind.Bool -> "boolean"
        | SPrim PrimKind.Raw -> "unknown"
        | SNullable inner -> typeOf inner + " | null"
        | SList inner -> "readonly " + parenthesise inner + "[]"
        | SMap inner -> "{ readonly [key: string]: " + typeOf inner + " }"
        | SObject(name, _) -> name
        | SUnion(name, _, _) -> name
        | SRef name -> name
        | SAnnotated _ -> "unknown"

    and private parenthesise (info: SchemaInfo) =
        let rendered = typeOf info

        if rendered.Contains " | " || rendered.Contains " " then
            "(" + rendered + ")"
        else
            rendered

    /// An enumeration becomes a union of literals rather than a bare string, so
    /// that a typo is a compile error in the frontend as well.
    let private literalUnion (info: SchemaInfo) =
        SchemaInfo.constraints info
        |> List.tryPick (
            function
            | Constraint.OneOf values when not (List.isEmpty values) ->
                Some(values |> List.map (fun v -> "\"" + v + "\"") |> String.concat " | ")
            | _ -> None
        )

    /// <summary>
    /// The warning a number needs, when TypeScript cannot hold what the schema
    /// promises.
    /// </summary>
    /// <remarks>
    /// TypeScript has one numeric type, and <c>JSON.parse</c> produces IEEE-754
    /// doubles from it. An <c>int64</c> past 2^53, or a <c>decimal</c> with more
    /// significant digits than a double holds, arrives in the frontend quietly
    /// rounded. Etymon cannot fix that — the value really is a JSON number, and
    /// emitting <c>string</c> would misdescribe the payload — but it refuses to
    /// let the generated type imply a precision the wire format does not have.
    /// </remarks>
    let rec private precisionNote (info: SchemaInfo) =
        match SchemaInfo.strip info with
        | SPrim PrimKind.Int64 ->
            Some "Encoded as a JSON number: values beyond 2^53 lose precision when JavaScript parses them."
        | SPrim PrimKind.Decimal ->
            Some "Encoded as a JSON number: JavaScript parses it as a double, so exactness is not preserved."
        | SNullable inner
        | SList inner
        | SMap inner -> precisionNote inner
        | _ -> None

    let private comment (indent: string) (text: string option) (builder: StringBuilder) =
        match text with
        | Some description ->
            builder.Append(indent).Append("/** ").Append(description).AppendLine(" */")
            |> ignore
        | None -> ()

    let private emitObject (name: string) (fields: FieldInfo list) (builder: StringBuilder) =
        builder.Append("export interface ").Append(name).AppendLine(" {") |> ignore

        for field in fields do
            // A field that was documented and also needs a precision warning gets
            // both: dropping either one would be the generated type keeping
            // something back.
            let description =
                match field.Description, precisionNote field.Schema with
                | Some prose, Some note -> Some(prose + " " + note)
                | Some prose, None -> Some prose
                | None, note -> note

            comment "  " description builder

            let rendered =
                match literalUnion field.Schema with
                | Some union -> union
                | None -> typeOf field.Schema

            // `?:` for a key that may be absent; `| null` for one that may be
            // present and empty. Conflating them is the commonest way a
            // generated type lies about its payload.
            let marker = if field.Required then ": " else "?: "

            builder.Append("  readonly ").Append(field.Name).Append(marker).Append(rendered).AppendLine(";")
            |> ignore

        builder.AppendLine("}") |> ignore

    let private emitUnion (name: string) (tag: string) (cases: (string * SchemaInfo) list) (builder: StringBuilder) =
        builder.Append("export type ").Append(name).AppendLine(" =") |> ignore

        let rendered =
            cases
            |> List.map (fun (caseTag, payload) ->
                let body =
                    match SchemaInfo.strip payload with
                    // A case with no payload is the tag alone, matching how
                    // Etymon writes it on the wire.
                    | SPrim PrimKind.Raw -> ""
                    | _ -> "; readonly value: " + typeOf payload

                "  | { readonly " + tag + ": \"" + caseTag + "\"" + body + " }"
            )

        // The semicolon closes the last case rather than sitting on a line of its
        // own, which is where somebody writing this by hand would put it.
        let lastIndex = List.length rendered - 1

        rendered
        |> List.iteri (fun i line -> builder.AppendLine(if i = lastIndex then line + ";" else line) |> ignore)

    /// <summary>
    /// Declarations for every named type a schema reaches, ready to write to a
    /// <c>.d.ts</c> file.
    /// </summary>
    /// <remarks>
    /// Types are emitted in name order, so the same schema always produces the
    /// same file and a change to it shows up as a reviewable diff rather than
    /// noise.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// TypeScript.emit [ Schema.info personSchema ]
    /// // export interface Address { readonly street: string; ... }
    /// // export interface Person { readonly name: string; readonly email?: string; ... }
    /// </code></example>
    let emit (schemas: SchemaInfo list) =
        let definitions =
            schemas
            |> List.map SchemaInfo.definitions
            |> List.fold (fun acc m -> Map.fold (fun inner k v -> Map.add k v inner) acc m) Map.empty

        let builder = StringBuilder()

        builder.AppendLine("// Generated by Etymon. Do not edit by hand.").AppendLine()
        |> ignore

        let mutable first = true

        for KeyValue(name, info) in definitions do
            if not first then
                builder.AppendLine() |> ignore

            first <- false

            let meta = SchemaInfo.meta info
            comment "" meta.Description builder

            match SchemaInfo.strip info with
            | SObject(_, fields) -> emitObject name fields builder
            | SUnion(_, tag, cases) -> emitUnion name tag cases builder
            | other ->
                builder.Append("export type ").Append(name).Append(" = ").Append(typeOf other).AppendLine(";")
                |> ignore

        // StringBuilder.AppendLine writes Environment.NewLine, so the file would
        // otherwise be CRLF on Windows and LF everywhere else. A generated .d.ts
        // is checked in and reviewed, and one that changes line endings with the
        // machine that produced it churns in every cross-platform repository.
        builder.ToString().Replace("\r\n", "\n").TrimEnd() + "\n"

    /// <summary>Declarations for one schema.</summary>
    /// <example><code lang="fsharp">
    /// TypeScript.emitOne personSchema
    /// </code></example>
    let emitOne (schema: Schema<'T>) = emit [ schema.Info ]
