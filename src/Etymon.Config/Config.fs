namespace Etymon

open System
open System.Text
open System.Text.Json.Nodes

/// <summary>
/// Where one setting's value came from, and what it was.
/// </summary>
/// <remarks>
/// The thing an operator actually wants at startup: not just what the
/// configuration is, but which of six overlapping sources won. A value that is
/// right for the wrong reason breaks the next time the sources change.
/// </remarks>
type ConfigSetting =
    {
        /// The setting's path, as it appears in the schema.
        Path: string
        /// The value in use, redacted when the schema marks the field sensitive.
        Value: string
        /// The source that supplied it, or <c>None</c> when nothing did and a
        /// default or absence applies.
        Source: string option
        /// Whether the schema marks this field sensitive.
        Sensitive: bool
    }

/// <summary>
/// Loads a configuration record through a <see cref="T:Etymon.Schema`1"/>.
/// </summary>
/// <remarks>
/// <para>
/// Configuration is the place where every value arrives as a string whether it
/// wants to be one or not, so decoding happens in
/// <see cref="T:Etymon.DecodeMode"/>'s coercing mode: <c>"8080"</c> really is the
/// port. Coercion never makes nonsense acceptable — <c>"eighty"</c> is still not
/// a number.
/// </para>
/// <para>
/// Every problem is reported at once, with the path, what was expected, and
/// either the source that supplied the bad value or the list of sources that
/// were searched for a missing one. Starting up, failing on the first missing
/// setting, being restarted, and failing on the second is a slow way to learn
/// something that could have been said in one breath.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module Config =

    // ---- what the schema expects ---------------------------------------------

    /// Every leaf the schema wants, with the path to it and what it is.
    /// Walking the schema rather than the sources is what lets the loader say
    /// "database.host was not found" instead of waiting for a decoder to notice.
    let rec private expectedLeaves (prefix: string list) (info: SchemaInfo) : (string list * SchemaInfo) list =
        match SchemaInfo.strip info with
        | SObject(_, fields) ->
            fields
            |> List.collect (fun field -> expectedLeaves (prefix @ [ field.Name ]) field.Schema)
        | SNullable inner -> expectedLeaves prefix inner
        | _ -> [ prefix, info ]

    /// A readable name for what a leaf wants, for "expected a number" messages.
    /// "a string", "an integer". Getting the article right matters more than it
    /// looks: these messages are read by people under time pressure, and a
    /// library that writes "a integer" reads as one that was not finished.
    let private withArticle (noun: string) =
        if noun.Length > 0 && "aeiou".Contains(noun[0]) then
            "an " + noun
        else
            "a " + noun

    let private expectedKind (info: SchemaInfo) =
        let constraints = SchemaInfo.constraints info

        let fromFormat =
            constraints
            |> List.tryPick (
                function
                | Constraint.HasFormat format -> Some(Format.name format)
                | _ -> None
            )

        match fromFormat with
        | Some format -> format
        | None ->
            match SchemaInfo.strip info with
            | SPrim PrimKind.String -> "string"
            | SPrim PrimKind.Int
            | SPrim PrimKind.Int64 -> "integer"
            | SPrim PrimKind.Float
            | SPrim PrimKind.Decimal -> "number"
            | SPrim PrimKind.Bool -> "boolean"
            | SPrim PrimKind.Guid -> "uuid"
            | SPrim PrimKind.DateTimeOffset -> "date-time"
            | SPrim PrimKind.DateOnly -> "date"
            | SPrim PrimKind.TimeOnly -> "time"
            | SPrim PrimKind.TimeSpan -> "duration"
            | SPrim PrimKind.Bytes -> "base64"
            | SList _ -> "array"
            | SMap _
            | SObject _ -> "object"
            | SUnion _ -> "tagged object"
            | _ -> "value"

    /// Whether a leaf is marked sensitive, either on the field or on its schema.
    let rec private sensitiveLeaves (prefix: string list) (info: SchemaInfo) : Set<string list> =
        match SchemaInfo.strip info with
        | SObject(_, fields) ->
            fields
            |> List.fold
                (fun acc field ->
                    let childPath = prefix @ [ field.Name ]

                    let acc =
                        if field.Sensitive || (SchemaInfo.meta field.Schema).Sensitive then
                            Set.add (Source.normalise childPath) acc
                        else
                            acc

                    Set.union acc (sensitiveLeaves childPath field.Schema)
                )
                Set.empty
        | SNullable inner -> sensitiveLeaves prefix inner
        | _ -> Set.empty

    // ---- gathering -----------------------------------------------------------

    /// What every source holds, plus whichever of them could not be read at all.
    let private readAll (sources: ConfigSource list) =
        sources
        |> List.map (fun source ->
            match source.Read() with
            | Ok entries -> Choice1Of2(source.Name, entries)
            | Error message -> Choice2Of2(source.Name, message)
        )
        |> List.partition (
            function
            | Choice1Of2 _ -> true
            | Choice2Of2 _ -> false
        )
        |> fun (loaded, failed) ->
            loaded
            |> List.choose (
                function
                | Choice1Of2 pair -> Some pair
                | Choice2Of2 _ -> None
            ),
            failed
            |> List.choose (
                function
                | Choice2Of2 pair -> Some pair
                | Choice1Of2 _ -> None
            )

    /// The last source that holds a path wins, which is why sources are given in
    /// increasing order of precedence.
    let private resolve (loaded: (string * ConfigEntries) list) (path: string list) =
        let key = Source.normalise path

        loaded
        |> List.fold
            (fun found (sourceName, entries) ->
                match Map.tryFind key entries with
                | Some value -> Some(sourceName, value)
                | None -> found
            )
            None

    /// Rebuilds the nested shape the schema expects from the flat values found.
    let private rebuild (found: (string list * JsonNode) list) =
        let root = JsonObject()

        for path, value in found do
            let rec place (current: JsonObject) (segments: string list) =
                match segments with
                | [] -> ()
                | [ (last: string) ] -> current[last] <- value.DeepClone()
                | (head: string) :: rest ->
                    let child =
                        match current[head] with
                        | :? JsonObject as existing -> existing
                        | _ ->
                            let created = JsonObject()
                            current[head] <- created
                            created

                    place child rest

            place root path

        root

    // ---- loading -------------------------------------------------------------

    /// <summary>
    /// Loads a configuration record, reporting every problem at once.
    /// </summary>
    /// <remarks>
    /// Sources are given in increasing order of precedence: the last one that
    /// holds a setting wins. The conventional order is file, then environment
    /// overlay, then environment variables.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Config.load settingsSchema [
    ///     Source.jsonFile "appsettings.json"
    ///     Source.optionalJsonFile $"appsettings.{envName}.json"
    ///     Source.environment "APP"
    /// ]
    /// // Error, all at once:
    /// //   database.host: is required (searched: appsettings.json, environment)
    /// //   database.port: expected an integer, but environment gave a string
    /// </code></example>
    let load (schema: Schema<'T>) (sources: ConfigSource list) : Validation<'T> =
        let loaded, unreadable = readAll sources

        // A source that could not be read is a configuration problem in its own
        // right, and reporting it alongside the rest is more use than failing
        // before the others have been looked at.
        let sourceErrors =
            unreadable
            |> List.map (fun (name, message) ->
                {
                    Path = Path.root
                    Reason = ErrorReason.Rejected "unreadable-source"
                    Message = $"the source '%s{name}' could not be read: %s{message}"
                }
            )

        let expected = expectedLeaves [] schema.Info
        let sensitive = sensitiveLeaves [] schema.Info
        let searched = loaded |> List.map fst

        let found =
            expected
            |> List.choose (fun (path, _) -> resolve loaded path |> Option.map (fun (_, value) -> path, value))

        let provenance =
            expected
            |> List.choose (fun (path, _) ->
                resolve loaded path
                |> Option.map (fun (sourceName, _) -> Source.normalise path, sourceName)
            )
            |> Map.ofList

        let document = rebuild found

        match Schema.fromJsonWith DecodeMode.Coercing schema (document.ToJsonString()) with
        | Ok value when List.isEmpty sourceErrors -> Ok value
        | Ok _ -> Error(ValidationErrors.ofHeadTail (List.head sourceErrors) (List.tail sourceErrors))
        | Error errors ->
            // Each decode error gets the one thing the decoder could not know:
            // where the value came from, or where it was looked for.
            let enriched =
                errors
                |> ValidationErrors.toList
                |> List.map (fun error ->
                    let key =
                        Source.normalise (
                            Path.segments error.Path
                            |> List.choose (
                                function
                                | PathSegment.Field name -> Some name
                                | PathSegment.Index _ -> None
                            )
                        )

                    let expectedFor =
                        expected
                        |> List.tryFind (fun (path, _) -> Source.normalise path = key)
                        |> Option.map (snd >> expectedKind)

                    let context =
                        match Map.tryFind key provenance, error.Reason with
                        | Some source, _ -> $" (from %s{source})"
                        | None, ErrorReason.Missing ->
                            match searched with
                            | [] -> " (no sources were readable)"
                            | names -> " (searched: " + String.Join(", ", names) + ")"
                        | None, _ -> ""

                    let expectation =
                        match expectedFor, error.Reason with
                        | Some kind, ErrorReason.TypeMismatch _ -> "expected " + withArticle kind
                        | Some kind, ErrorReason.Missing -> "is required, and must be " + withArticle kind
                        | _ -> error.Message

                    let redactionNote =
                        if Set.contains key sensitive then
                            ". The value is not shown because this setting is sensitive."
                        else
                            ""

                    { error with
                        Message = expectation + context + redactionNote
                    }
                )

            Error(
                ValidationErrors.ofHeadTail (List.head (sourceErrors @ enriched)) (List.tail (sourceErrors @ enriched))
            )

    /// <summary>
    /// A startup-ready report of everything that is wrong, one problem per line.
    /// </summary>
    /// <example><code lang="fsharp">
    /// match Config.load schema sources with
    /// | Ok settings -> run settings
    /// | Error problems ->
    ///     eprintfn "%s" (Config.report problems)
    ///     exit 1
    /// </code></example>
    let report (errors: ValidationErrors) =
        let lines =
            errors
            |> ValidationErrors.toList
            |> List.map (fun e ->
                let where = Path.toStringOr "(root)" e.Path
                $"  %s{where}: %s{e.Message}"
            )

        let count = ValidationErrors.count errors

        let heading =
            if count = 1 then
                "1 configuration problem:"
            else
                $"%d{count} configuration problems:"

        heading + Environment.NewLine + String.Join(Environment.NewLine, lines)

    /// <summary>
    /// Every setting the schema expects, the value in use, and which source
    /// supplied it. Sensitive settings are redacted.
    /// </summary>
    /// <remarks>
    /// Worth printing at startup. The commonest configuration bug is not a value
    /// being wrong but the wrong source winning, and that is invisible until
    /// something says which one did.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// for setting in Config.settings schema sources do
    ///     printfn "%-28s %-20s %s" setting.Path setting.Value (defaultArg setting.Source "(default)")
    /// // database.host  db.internal    appsettings.json
    /// // database.password  &lt;redacted&gt;  environment
    /// </code></example>
    let settings (schema: Schema<'T>) (sources: ConfigSource list) : ConfigSetting list =
        let loaded, _ = readAll sources
        let sensitive = sensitiveLeaves [] schema.Info

        expectedLeaves [] schema.Info
        |> List.map (fun (path, _) ->
            let key = Source.normalise path
            let isSensitive = Set.contains key sensitive
            let resolved = resolve loaded path

            {
                Path = String.Join(".", path)
                Value =
                    match resolved with
                    | None -> "(not set)"
                    | Some _ when isSensitive -> "<redacted>"
                    | Some(_, value) ->
                        match value with
                        | null -> "null"
                        | node -> node.ToJsonString().Trim '"'
                Source = resolved |> Option.map fst
                Sensitive = isSensitive
            }
        )

    /// <summary>
    /// The same as <see cref="M:Etymon.Config.settings"/>, rendered as a table.
    /// </summary>
    /// <example><code lang="fsharp">
    /// printfn "%s" (Config.explain schema sources)
    /// </code></example>
    let explain (schema: Schema<'T>) (sources: ConfigSource list) =
        let rows = settings schema sources

        if List.isEmpty rows then
            "no settings"
        else
            let pathWidth = rows |> List.map (fun r -> r.Path.Length) |> List.max
            let valueWidth = rows |> List.map (fun r -> r.Value.Length) |> List.max
            let builder = StringBuilder()

            for row in rows do
                builder
                    .Append(row.Path.PadRight pathWidth)
                    .Append("  ")
                    .Append(row.Value.PadRight valueWidth)
                    .Append("  ")
                    .AppendLine(defaultArg row.Source "(not set)")
                |> ignore

            builder.ToString().TrimEnd()
