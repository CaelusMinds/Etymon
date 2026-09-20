namespace Etymon

open System
open System.Text.Json
open System.Text.Json.Nodes

/// <summary>
/// A flat view of one configuration source: every leaf it holds, keyed by the
/// path to that leaf.
/// </summary>
/// <remarks>
/// Flattening first is what lets a JSON file and a set of environment variables
/// be compared on equal terms. A nested object in a file and
/// <c>DATABASE__HOST</c> in the environment both reduce to the same key, so the
/// rest of the loader never has to care which kind of source it is reading.
/// </remarks>
type ConfigEntries = Map<string list, JsonNode>

/// <summary>
/// Somewhere configuration values come from.
/// </summary>
/// <remarks>
/// Deliberately one function. Adding user secrets, a key vault or a command line
/// means writing a <c>Read</c> that produces entries; nothing else in the loader
/// needs to know that the source exists.
/// </remarks>
[<NoEquality; NoComparison>]
type ConfigSource =
    {
        /// What to call this source when reporting where a value came from, or
        /// where one was looked for. Shown to whoever has to fix the problem, so
        /// it should be something they can act on: a file path, "environment".
        Name: string
        /// Reads every value the source holds. Failing to read is itself a
        /// configuration error, reported like any other.
        Read: unit -> Result<ConfigEntries, string>
    }

/// Building <see cref="T:Etymon.ConfigSource"/> values.
[<RequireQualifiedAccess>]
module Source =

    /// The separator between path segments in a flat key. Two underscores,
    /// matching what Docker, Kubernetes and Microsoft.Extensions.Configuration
    /// all already use, so an existing deployment needs no changes.
    [<Literal>]
    let DefaultSeparator = "__"

    /// Keys are compared case-insensitively throughout: an environment variable
    /// is conventionally SHOUTED and a JSON field is not, and nobody should have
    /// to think about that.
    let internal normalise (path: string list) =
        path |> List.map (fun s -> s.ToLowerInvariant())

    /// Reduces a JSON tree to its leaves. An array is a leaf: configuration
    /// almost never wants to merge two lists element by element, and pretending
    /// otherwise produces results nobody predicted.
    let rec internal flatten (prefix: string list) (node: JsonNode) : ConfigEntries =
        match node with
        | :? JsonObject as object ->
            object
            |> Seq.fold
                (fun acc pair ->
                    let child = flatten (prefix @ [ pair.Key ]) pair.Value

                    child |> Map.fold (fun inner key value -> Map.add key value inner) acc
                )
                Map.empty
        | _ -> Map.ofList [ normalise prefix, node ]

    let private entriesFromJson (text: string) =
        try
            match JsonNode.Parse text with
            | null -> Ok Map.empty
            | node -> Ok(flatten [] node)
        with :? JsonException as e ->
            Error e.Message

    /// <summary>
    /// Values already in hand, keyed by flat path. Useful in tests and for
    /// defaults supplied in code.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Source.inMemory "defaults" [ "database__host", "localhost"; "retries", "3" ]
    /// </code></example>
    let inMemory (name: string) (values: (string * string) list) : ConfigSource =
        {
            Name = name
            Read =
                fun () ->
                    values
                    |> List.map (fun (key, value) ->
                        normalise (List.ofArray (key.Split(DefaultSeparator, StringSplitOptions.None))),
                        (JsonValue.Create value :> JsonNode)
                    )
                    |> Map.ofList
                    |> Ok
        }

    /// <summary>
    /// Environment variables, with <c>__</c> separating path segments.
    /// </summary>
    /// <remarks>
    /// Only variables starting with the prefix are read, and the prefix is
    /// stripped. Without one, every variable on the machine becomes a candidate
    /// configuration key, and <c>PATH</c> starts meaning something.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// // APP__DATABASE__HOST=db.internal  ->  database.host
    /// Source.environment "APP"
    /// </code></example>
    let environment (prefix: string) : ConfigSource =
        {
            Name = "environment"
            Read =
                fun () ->
                    let prefixWithSeparator =
                        if String.IsNullOrEmpty prefix then
                            ""
                        else
                            prefix + DefaultSeparator

                    Env.all ()
                    |> Seq.filter (fun pair ->
                        String.IsNullOrEmpty prefixWithSeparator
                        || pair.Key.StartsWith(prefixWithSeparator, StringComparison.OrdinalIgnoreCase)
                    )
                    |> Seq.map (fun pair ->
                        let withoutPrefix = pair.Key.Substring prefixWithSeparator.Length

                        normalise (List.ofArray (withoutPrefix.Split(DefaultSeparator, StringSplitOptions.None))),
                        (JsonValue.Create pair.Value :> JsonNode)
                    )
                    |> Map.ofSeq
                    |> Ok
        }

    /// <summary>JSON held in a string, for a source that is not a file.</summary>
    /// <example><code lang="fsharp">
    /// Source.jsonText "embedded defaults" """{"retries":3}"""
    /// </code></example>
    let jsonText (name: string) (text: string) : ConfigSource =
        {
            Name = name
            Read = fun () -> entriesFromJson text
        }

    /// <summary>
    /// A JSON file that must exist. A missing file is reported like any other
    /// configuration problem, alongside the rest, rather than throwing.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Source.jsonFile "appsettings.json"
    /// </code></example>
    let jsonFile (path: string) : ConfigSource =
        {
            Name = path
            Read =
                fun () ->
                    match File.tryReadAllText path with
                    | Ok text -> entriesFromJson text
                    | Error error -> Error(FileError.describe error)
        }

    /// <summary>
    /// A JSON file that may not exist. Absence is not a problem; anything else
    /// wrong with it still is.
    /// </summary>
    /// <remarks>
    /// The shape an environment overlay wants: <c>appsettings.json</c> required,
    /// <c>appsettings.Production.json</c> optional. A file that exists but does
    /// not parse is still an error, because silently ignoring it is how a
    /// deployment runs for a week on defaults nobody meant.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Source.optionalJsonFile $"appsettings.{environmentName}.json"
    /// </code></example>
    let optionalJsonFile (path: string) : ConfigSource =
        {
            Name = path
            Read =
                fun () ->
                    match File.tryReadAllText path with
                    | Ok text -> entriesFromJson text
                    | Error(FileError.NotFound _)
                    | Error(FileError.DirectoryNotFound _) -> Ok Map.empty
                    | Error error -> Error(FileError.describe error)
        }

    /// <summary>
    /// A source of your own: user secrets, a key vault, a command line, a
    /// database.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Source.custom "key vault" (fun () ->
    ///     vault.GetAll() |> Seq.map toEntry |> Map.ofSeq |> Ok)
    /// </code></example>
    let custom (name: string) (read: unit -> Result<ConfigEntries, string>) : ConfigSource =
        { Name = name; Read = read }
