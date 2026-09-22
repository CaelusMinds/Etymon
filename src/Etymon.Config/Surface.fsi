
namespace FSharp



namespace Etymon
    
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
    type ConfigEntries = Map<string list,System.Text.Json.Nodes.JsonNode>
    
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
          Read: (unit -> Result<ConfigEntries,string>)
        }
    
    /// Building <see cref="T:Etymon.ConfigSource"/> values.
    [<RequireQualifiedAccess>]
    module Source =
        
        /// The separator between path segments in a flat key. Two underscores,
        /// matching what Docker, Kubernetes and Microsoft.Extensions.Configuration
        /// all already use, so an existing deployment needs no changes.
        [<Literal>]
        val DefaultSeparator: string = "__"
        
        /// Keys are compared case-insensitively throughout: an environment variable
        /// is conventionally SHOUTED and a JSON field is not, and nobody should have
        /// to think about that.
        val internal normalise: path: string list -> string list
        
        /// Reduces a JSON tree to its leaves. An array is a leaf: configuration
        /// almost never wants to merge two lists element by element, and pretending
        /// otherwise produces results nobody predicted.
        val internal flatten:
          prefix: string list ->
            node: System.Text.Json.Nodes.JsonNode -> ConfigEntries
        
        val private entriesFromJson:
          text: string ->
            Result<Map<string list,System.Text.Json.Nodes.JsonNode>,string>
        
        /// <summary>
        /// Values already in hand, keyed by flat path. Useful in tests and for
        /// defaults supplied in code.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Source.inMemory "defaults" [ "database__host", "localhost"; "retries", "3" ]
        /// </code></example>
        val inMemory:
          name: string -> values: (string * string) list -> ConfigSource
        
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
        val environment: prefix: string -> ConfigSource
        
        /// <summary>JSON held in a string, for a source that is not a file.</summary>
        /// <example><code lang="fsharp">
        /// Source.jsonText "embedded defaults" """{"retries":3}"""
        /// </code></example>
        val jsonText: name: string -> text: string -> ConfigSource
        
        /// <summary>
        /// A JSON file that must exist. A missing file is reported like any other
        /// configuration problem, alongside the rest, rather than throwing.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Source.jsonFile "appsettings.json"
        /// </code></example>
        val jsonFile: path: string -> ConfigSource
        
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
        val optionalJsonFile: path: string -> ConfigSource
        
        /// <summary>
        /// A source of your own: user secrets, a key vault, a command line, a
        /// database.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Source.custom "key vault" (fun () ->
        ///     vault.GetAll() |> Seq.map toEntry |> Map.ofSeq |> Ok)
        /// </code></example>
        val custom:
          name: string ->
            read: (unit -> Result<ConfigEntries,string>) -> ConfigSource

namespace Etymon
    
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
        
        /// Every leaf the schema wants, with the path to it and what it is.
        /// Walking the schema rather than the sources is what lets the loader say
        /// "database.host was not found" instead of waiting for a decoder to notice.
        val private expectedLeaves:
          prefix: string list ->
            info: SchemaInfo -> (string list * SchemaInfo) list
        
        /// A readable name for what a leaf wants, for "expected a number" messages.
        /// "a string", "an integer". Getting the article right matters more than it
        /// looks: these messages are read by people under time pressure, and a
        /// library that writes "a integer" reads as one that was not finished.
        val private withArticle: noun: string -> string
        
        val private expectedKind: info: SchemaInfo -> string
        
        /// Whether a leaf is marked sensitive, either on the field or on its schema.
        val private sensitiveLeaves:
          prefix: string list -> info: SchemaInfo -> Set<string list>
        
        /// What every source holds, plus whichever of them could not be read at all.
        val private readAll:
          sources: ConfigSource list ->
            (string * ConfigEntries) list * (string * string) list
        
        /// The last source that holds a path wins, which is why sources are given in
        /// increasing order of precedence.
        val private resolve:
          loaded: (string * ConfigEntries) list ->
            path: string list ->
            (string * System.Text.Json.Nodes.JsonNode) option
        
        /// Rebuilds the nested shape the schema expects from the flat values found.
        val private rebuild:
          found: (string list * System.Text.Json.Nodes.JsonNode) list ->
            System.Text.Json.Nodes.JsonObject
        
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
        val load:
          schema: Schema<'T> -> sources: ConfigSource list -> Validation<'T>
        
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
        val report: errors: ValidationErrors -> string
        
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
        val settings:
          schema: Schema<'T> -> sources: ConfigSource list -> ConfigSetting list
        
        /// <summary>
        /// The same as <see cref="M:Etymon.Config.settings"/>, rendered as a table.
        /// </summary>
        /// <example><code lang="fsharp">
        /// printfn "%s" (Config.explain schema sources)
        /// </code></example>
        val explain: schema: Schema<'T> -> sources: ConfigSource list -> string

