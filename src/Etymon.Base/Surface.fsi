
namespace FSharp



namespace Etymon
    
    /// <summary>
    /// String operations that return <c>option</c> rather than <c>null</c>, and that
    /// say which comparison they are doing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named <c>Str</c> rather than <c>String</c> on purpose: a module called
    /// <c>String</c> would shadow FSharp.Core's, and <c>String.concat</c> would stop
    /// resolving for anyone who opened <c>Etymon</c>. A short name is a small price
    /// for not breaking the standard library.
    /// </para>
    /// <para>
    /// Every comparison here is ordinal unless its name says otherwise. Culture-aware
    /// comparison is for sorting text a person will read, and is almost never what
    /// you want when matching an identifier, a header name or a config key.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Str =
        
        /// <summary>
        /// Turns a null or whitespace-only string into <c>None</c>. The usual first
        /// step when a value came from somewhere that cannot be trusted to distinguish
        /// "absent" from "empty".
        /// </summary>
        /// <example><code lang="fsharp">
        /// Str.toOption "  " // None
        /// Str.toOption " hi " // Some " hi "
        /// </code></example>
        val toOption: value: string -> string option
        
        /// <summary>
        /// Turns a null string into <c>None</c>, keeping the empty string as a value.
        /// Use this when empty genuinely means something different from absent.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Str.toOptionAllowEmpty "" // Some ""
        /// Str.toOptionAllowEmpty null // None
        /// </code></example>
        val toOptionAllowEmpty: value: string -> string option
        
        /// <summary>Replaces a null string with the empty one.</summary>
        /// <example><code lang="fsharp">
        /// Str.orEmpty null // ""
        /// </code></example>
        val orEmpty: value: string -> string
        
        /// <summary>True when the string is null or has no characters.</summary>
        /// <example><code lang="fsharp">
        /// Str.isEmpty "" // true
        /// Str.isEmpty " " // false
        /// </code></example>
        val isEmpty: value: string -> bool
        
        /// <summary>True when the string is null, empty, or only whitespace.</summary>
        /// <example><code lang="fsharp">
        /// Str.isBlank "  \t " // true
        /// </code></example>
        val isBlank: value: string -> bool
        
        /// <summary>Trims surrounding whitespace, treating null as empty.</summary>
        /// <example><code lang="fsharp">
        /// Str.trim "  hi  " // "hi"
        /// Str.trim null // ""
        /// </code></example>
        val trim: value: string -> string
        
        /// <summary>Lower-cases using the invariant culture.</summary>
        /// <example><code lang="fsharp">
        /// Str.toLower "ABC" // "abc"
        /// </code></example>
        val toLower: value: string -> string
        
        /// <summary>Upper-cases using the invariant culture.</summary>
        /// <example><code lang="fsharp">
        /// Str.toUpper "abc" // "ABC"
        /// </code></example>
        val toUpper: value: string -> string
        
        /// <summary>Ordinal equality, ignoring case. The right default for identifiers.</summary>
        /// <example><code lang="fsharp">
        /// Str.equalsIgnoreCase "Content-Type" "content-type" // true
        /// </code></example>
        val equalsIgnoreCase: left: string -> right: string -> bool
        
        /// <summary>Ordinal <c>startsWith</c>.</summary>
        /// <example><code lang="fsharp">
        /// Str.startsWith "Etymon." "Etymon.Core" // true
        /// </code></example>
        val startsWith: prefix: string -> value: string -> bool
        
        /// <summary>Ordinal <c>endsWith</c>.</summary>
        /// <example><code lang="fsharp">
        /// Str.endsWith ".fs" "Parse.fs" // true
        /// </code></example>
        val endsWith: suffix: string -> value: string -> bool
        
        /// <summary>Ordinal <c>contains</c>.</summary>
        /// <example><code lang="fsharp">
        /// Str.contains "@" "a@b.com" // true
        /// </code></example>
        val contains: needle: string -> value: string -> bool
        
        /// <summary>
        /// Splits on a separator, discarding empty entries and trimming each part.
        /// The shape a comma-separated config value almost always wants.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Str.split ',' "a, b ,, c" // [ "a"; "b"; "c" ]
        /// </code></example>
        val split: separator: char -> value: string -> string list
        
        /// <summary>Splits on a separator, keeping every part exactly as it was.</summary>
        /// <example><code lang="fsharp">
        /// Str.splitRaw ',' "a,,b" // [ "a"; ""; "b" ]
        /// </code></example>
        val splitRaw: separator: char -> value: string -> string list
        
        /// <summary>
        /// Splits once at the first occurrence of a separator, into what came before
        /// and what came after. <c>None</c> when the separator is absent.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Str.splitFirst '=' "KEY=a=b" // Some ("KEY", "a=b")
        /// Str.splitFirst '=' "KEY" // None
        /// </code></example>
        val splitFirst:
          separator: char -> value: string -> (string * string) option
        
        /// <summary>Joins parts with a separator.</summary>
        /// <example><code lang="fsharp">
        /// Str.join ", " [ "a"; "b" ] // "a, b"
        /// </code></example>
        val join: separator: string -> parts: string seq -> string
        
        /// <summary>
        /// Shortens a string to at most <paramref name="maxLength"/> characters,
        /// ending with an ellipsis when it had to cut. The ellipsis counts toward the
        /// limit, so the result is never longer than asked for.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Str.truncate 8 "a long sentence" // "a long …"
        /// Str.truncate 8 "short" // "short"
        /// </code></example>
        val truncate: maxLength: int -> value: string -> string
        
        /// <summary>Removes a prefix if it is there, and does nothing if it is not.</summary>
        /// <example><code lang="fsharp">
        /// Str.stripPrefix "Etymon." "Etymon.Core" // "Core"
        /// Str.stripPrefix "Etymon." "FsCheck" // "FsCheck"
        /// </code></example>
        val stripPrefix: prefix: string -> value: string -> string
        
        /// <summary>Removes a suffix if it is there, and does nothing if it is not.</summary>
        /// <example><code lang="fsharp">
        /// Str.stripSuffix ".fs" "Parse.fs" // "Parse"
        /// </code></example>
        val stripSuffix: suffix: string -> value: string -> string

namespace Etymon
    
    /// <summary>
    /// Lookups into .NET dictionaries that return <c>option</c>.
    /// </summary>
    /// <remarks>
    /// F# gives <c>Map</c> a <c>tryFind</c> and gives the base library dictionaries nothing, so
    /// every codebase grows its own <c>TryGetValue</c> wrapper. These are those,
    /// written once, over the interfaces rather than the concrete types so that they
    /// work on a <c>Dictionary</c>, a <c>ConcurrentDictionary</c>, a
    /// <c>IReadOnlyDictionary</c> and anything else that implements them.
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Dict =
        
        /// <summary>Looks up a key, returning <c>None</c> when it is absent.</summary>
        /// <example><code lang="fsharp">
        /// let headers = Dictionary&lt;string, string&gt;()
        /// headers["accept"] &lt;- "application/json"
        /// Dict.tryFind "accept" headers // Some "application/json"
        /// Dict.tryFind "missing" headers // None
        /// </code></example>
        val tryFind:
          key: 'K ->
            source: System.Collections.Generic.IDictionary<'K,'V> -> 'V option
        
        /// <summary>Looks up a key in a read-only dictionary.</summary>
        /// <example><code lang="fsharp">
        /// Dict.tryFindReadOnly "accept" (headers :> IReadOnlyDictionary&lt;_, _&gt;)
        /// </code></example>
        val tryFindReadOnly:
          key: 'K ->
            source: System.Collections.Generic.IReadOnlyDictionary<'K,'V> ->
            'V option
        
        /// <summary>The value for a key, or a fallback when it is absent.</summary>
        /// <example><code lang="fsharp">
        /// Dict.findOr "application/json" "accept" headers
        /// </code></example>
        val findOr:
          fallback: 'V ->
            key: 'K ->
            source: System.Collections.Generic.IDictionary<'K,'V> -> 'V
        
        /// <summary>
        /// Looks up a key and transforms the value, in one step. <c>None</c> when the
        /// key is absent or the transformation rejects the value.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Dict.tryPick Parse.int "retries" settings // None if absent or not a number
        /// </code></example>
        val tryPick:
          chooser: ('V -> 'U option) ->
            key: 'K ->
            source: System.Collections.Generic.IDictionary<'K,'V> -> 'U option
        
        /// <summary>Whether a key is present.</summary>
        /// <example><code lang="fsharp">
        /// Dict.containsKey "accept" headers // true
        /// </code></example>
        val containsKey:
          key: 'K ->
            source: System.Collections.Generic.IDictionary<'K,'V> -> bool
        
        /// <summary>The keys, as a list.</summary>
        /// <example><code lang="fsharp">
        /// Dict.keys headers // [ "accept" ]
        /// </code></example>
        val keys:
          source: System.Collections.Generic.IDictionary<'K,'V> -> 'K list
        
        /// <summary>The values, as a list.</summary>
        /// <example><code lang="fsharp">
        /// Dict.values headers // [ "application/json" ]
        /// </code></example>
        val values:
          source: System.Collections.Generic.IDictionary<'K,'V> -> 'V list
        
        /// <summary>
        /// A mutable <c>Dictionary</c> from key-value pairs. Useful at the boundary
        /// where a .NET API wants one and you have F# data.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Dict.ofList [ "accept", "application/json" ]
        /// </code></example>
        val ofList:
          pairs: ('K * 'V) list -> System.Collections.Generic.Dictionary<'K,'V>
            when 'K: equality
        
        /// <summary>
        /// A case-insensitive string-keyed <c>Dictionary</c>. What HTTP headers,
        /// environment variables and config keys almost always want.
        /// </summary>
        /// <example><code lang="fsharp">
        /// let headers = Dict.ofListIgnoreCase [ "Content-Type", "application/json" ]
        /// Dict.tryFind "content-type" headers // Some "application/json"
        /// </code></example>
        val ofListIgnoreCase:
          pairs: (string * 'V) list ->
            System.Collections.Generic.Dictionary<string,'V>
        
        /// <summary>An F# <c>Map</c> from any dictionary.</summary>
        /// <example><code lang="fsharp">
        /// Dict.toMap headers // Map&lt;string, string&gt;
        /// </code></example>
        val toMap:
          source: System.Collections.Generic.IDictionary<'K,'V> -> Map<'K,'V>
            when 'K: comparison

namespace Etymon
    
    /// <summary>
    /// Environment variables, as <c>option</c> rather than <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Environment.GetEnvironmentVariable</c> returns <c>null</c> for an unset
    /// variable and <c>""</c> for one that is set but empty, and almost no caller
    /// wants to tell those apart — an empty connection string is as absent as a
    /// missing one. So the default here treats blank as absent, and
    /// <see cref="M:Etymon.Env.tryGetAllowEmpty"/> is there for the rare case that
    /// genuinely cares.
    /// </para>
    /// <para>
    /// Variable names are matched exactly as the platform does: case-insensitively
    /// on Windows, case-sensitively elsewhere. Etymon does not paper over that,
    /// because a name that works in one place and not the other is better found
    /// immediately than hidden.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Env =
        
        /// <summary>
        /// The value of a variable, or <c>None</c> when it is unset, empty or
        /// whitespace.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Env.tryGet "PATH" // Some "..."
        /// Env.tryGet "NOT_SET_ANYWHERE" // None
        /// </code></example>
        val tryGet: name: string -> string option
        
        /// <summary>
        /// The value of a variable, distinguishing "set but empty" from "not set" as
        /// far as the platform allows.
        /// </summary>
        /// <remarks>
        /// Windows cannot hold a variable whose value is the empty string: setting one
        /// removes it. So on Windows this can only ever agree with
        /// <see cref="M:Etymon.Env.tryGet"/>. On Linux and macOS the distinction is
        /// real, and this is how to see it.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Env.tryGetAllowEmpty "SET_TO_EMPTY" // Some "" where the platform can store it
        /// </code></example>
        val tryGetAllowEmpty: name: string -> string option
        
        /// <summary>The value of a variable, or a fallback.</summary>
        /// <example><code lang="fsharp">
        /// Env.getOr "Production" "ASPNETCORE_ENVIRONMENT" // "Development" if set
        /// </code></example>
        val getOr: fallback: string -> name: string -> string
        
        /// <summary>
        /// The value of a variable, parsed. <c>None</c> when the variable is absent
        /// or the text does not parse.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Env.tryParse Parse.int "MAX_RETRIES" // Some 3
        /// Env.tryParse Parse.int "PATH" // None -- set, but not a number
        /// </code></example>
        val tryParse: parser: (string -> 'T option) -> name: string -> 'T option
        
        /// <summary>The value of a variable parsed as an <c>int</c>.</summary>
        /// <example><code lang="fsharp">
        /// Env.tryGetInt "PORT" // Some 8080
        /// </code></example>
        val tryGetInt: name: string -> int option
        
        /// <summary>
        /// The value of a variable parsed as a <c>bool</c>, accepting the forms that
        /// occur in practice: <c>1</c>, <c>yes</c>, <c>on</c> and their opposites.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Env.tryGetBool "CI" // Some true when CI=1
        /// </code></example>
        val tryGetBool: name: string -> bool option
        
        /// <summary>
        /// Whether a variable is set to something truthy. The usual shape of a
        /// feature switch: absent means off.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Env.isEnabled "ETYMON_VERBOSE" // false when unset
        /// </code></example>
        val isEnabled: name: string -> bool
        
        /// <summary>Whether a variable is set to anything non-blank.</summary>
        /// <example><code lang="fsharp">
        /// Env.isSet "NUGET_API_KEY" // true
        /// </code></example>
        val isSet: name: string -> bool
        
        /// <summary>
        /// Every environment variable, in a case-insensitive dictionary so that
        /// lookups behave the same on every platform.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Env.all () |> Dict.tryFind "path" // Some "..." on any platform
        /// </code></example>
        val all: unit -> System.Collections.Generic.Dictionary<string,string>

namespace Etymon
    
    /// <summary>
    /// Why a file operation did not work.
    /// </summary>
    /// <remarks>
    /// The distinction that matters is between the cases a caller can reasonably act
    /// on — the file is not there, we are not allowed, someone else has it — and
    /// everything else, which is <c>IoFailure</c>. Branching on a message is not
    /// something a caller should ever have to do, so each case that can be named is
    /// named.
    /// </remarks>
    [<RequireQualifiedAccess>]
    type FileError =
        
        /// The file does not exist.
        | NotFound of path: string
        
        /// A directory on the path does not exist.
        | DirectoryNotFound of path: string
        
        /// The operating system refused: permissions, or a read-only file.
        | AccessDenied of path: string
        
        /// <summary>Another process has the file open in a conflicting mode.</summary>
        /// <remarks>
        /// Reported the same way on every platform. Windows enforces this in the
        /// kernel; on Unix .NET emulates it with an advisory lock, which holds
        /// against other .NET processes but not against a process that ignores the
        /// advisory lock entirely.
        /// </remarks>
        | InUse of path: string
        
        /// The path is malformed, or longer than the platform allows.
        | InvalidPath of path: string
        
        /// The bytes were not valid in the expected text encoding.
        | NotValidText of path: string
        
        /// Anything else the operating system reported.
        | IoFailure of path: string * message: string
    
    /// Working with <see cref="T:Etymon.FileError"/>.
    [<RequireQualifiedAccess>]
    module FileError =
        
        /// <summary>The path the failure was about.</summary>
        /// <example><code lang="fsharp">
        /// FileError.path (FileError.NotFound "a.txt") // "a.txt"
        /// </code></example>
        val path: error: FileError -> string
        
        /// <summary>A sentence describing the failure, ready to log or show.</summary>
        /// <example><code lang="fsharp">
        /// FileError.describe (FileError.AccessDenied "C:\\x") // "access to C:\x was denied"
        /// </code></example>
        val describe: error: FileError -> string
    
    [<AutoOpen>]
    module internal FileErrorMapping =
        
        /// Maps the exception the base library throws onto the case a caller can act
        /// on. Kept in one place so that every operation classifies failures the same
        /// way -- the alternative is each function inventing its own taxonomy.
        val classify: path: string -> exn: exn -> FileError
        
        val attempt:
          path: string -> operation: (unit -> 'T) -> Result<'T,FileError>
    
    /// <summary>
    /// File operations that return <c>Result</c> instead of throwing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A missing file, a locked file and a permission refusal are all ordinary
    /// outcomes of asking the filesystem for something, not bugs — so they are
    /// values. Genuine bugs, such as passing a null path, still throw.
    /// </para>
    /// <para>
    /// This module shadows <c>System.IO.File</c> for anyone who opens
    /// <c>System.IO</c> after <c>Etymon</c>. If you need both, qualify:
    /// <c>Etymon.File.tryReadAllText</c>.
    /// </para>
    /// <para>
    /// Text is UTF-8 throughout, without a byte order mark on write, because a BOM
    /// is a recurring source of trouble for anything that later parses the file.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module File =
        
        val private utf8NoBom: System.Text.UTF8Encoding
        
        /// <summary>Whether the file exists. Never throws.</summary>
        /// <example><code lang="fsharp">
        /// File.exists "appsettings.json" // true
        /// </code></example>
        val exists: path: string -> bool
        
        /// <summary>Reads a whole file as UTF-8 text.</summary>
        /// <example><code lang="fsharp">
        /// match File.tryReadAllText "appsettings.json" with
        /// | Ok contents -> ...
        /// | Error e -> eprintfn "%s" (FileError.describe e)
        /// </code></example>
        val tryReadAllText: path: string -> Result<string,FileError>
        
        /// <summary>Reads a whole file as UTF-8 lines.</summary>
        /// <example><code lang="fsharp">
        /// File.tryReadAllLines ".gitignore" // Ok [| "bin/"; "obj/" |]
        /// </code></example>
        val tryReadAllLines: path: string -> Result<string array,FileError>
        
        /// <summary>Reads a whole file as bytes.</summary>
        /// <example><code lang="fsharp">
        /// File.tryReadAllBytes "icon.png" // Ok [| ... |]
        /// </code></example>
        val tryReadAllBytes: path: string -> Result<byte array,FileError>
        
        /// <summary>
        /// Writes UTF-8 text, replacing the file if it is already there. Creates the
        /// containing directory if it is missing, since not doing so only ever
        /// produces a failure the caller would have to handle by creating it.
        /// </summary>
        /// <example><code lang="fsharp">
        /// File.tryWriteAllText "out/report.md" contents // Ok ()
        /// </code></example>
        val tryWriteAllText:
          path: string -> contents: string -> Result<unit,FileError>
        
        /// <summary>Writes bytes, replacing the file if it is already there.</summary>
        /// <example><code lang="fsharp">
        /// File.tryWriteAllBytes "out/icon.png" bytes // Ok ()
        /// </code></example>
        val tryWriteAllBytes:
          path: string -> contents: byte array -> Result<unit,FileError>
        
        /// <summary>Appends UTF-8 text, creating the file if it is not there.</summary>
        /// <example><code lang="fsharp">
        /// File.tryAppendText "log.txt" "started\n" // Ok ()
        /// </code></example>
        val tryAppendText:
          path: string -> contents: string -> Result<unit,FileError>
        
        /// <summary>
        /// Deletes the file. Succeeds when it was already absent, because the
        /// caller's intent — that it not be there — has been met either way.
        /// </summary>
        /// <example><code lang="fsharp">
        /// File.tryDelete "temp.txt" // Ok () even if it never existed
        /// </code></example>
        val tryDelete: path: string -> Result<unit,FileError>
        
        /// <summary>Copies a file, refusing to replace an existing destination.</summary>
        /// <example><code lang="fsharp">
        /// File.tryCopy "a.txt" "b.txt" // Error (IoFailure ...) if b.txt exists
        /// </code></example>
        val tryCopy:
          source: string -> destination: string -> Result<unit,FileError>
        
        /// <summary>Copies a file, replacing the destination if it is there.</summary>
        /// <example><code lang="fsharp">
        /// File.tryCopyOver "a.txt" "b.txt" // Ok ()
        /// </code></example>
        val tryCopyOver:
          source: string -> destination: string -> Result<unit,FileError>
        
        /// <summary>Moves a file, replacing the destination if it is there.</summary>
        /// <example><code lang="fsharp">
        /// File.tryMove "draft.md" "final.md" // Ok ()
        /// </code></example>
        val tryMove:
          source: string -> destination: string -> Result<unit,FileError>
    
    /// <summary>
    /// Directory operations that return <c>Result</c> instead of throwing.
    /// </summary>
    /// <remarks>
    /// Named <c>Dir</c> rather than <c>Directory</c> so that it does not shadow
    /// <c>System.IO.Directory</c> for anyone who has both open.
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Dir =
        
        /// <summary>Whether the directory exists. Never throws.</summary>
        /// <example><code lang="fsharp">
        /// Dir.exists "src" // true
        /// </code></example>
        val exists: path: string -> bool
        
        /// <summary>
        /// Creates the directory and any missing parents. Succeeds when it is already
        /// there.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Dir.tryCreate "out/reports" // Ok ()
        /// </code></example>
        val tryCreate: path: string -> Result<unit,FileError>
        
        /// <summary>
        /// The files directly inside a directory, matching a pattern. Sorted, so that
        /// two runs over the same directory produce the same order — which is what
        /// makes anything built from the result reproducible.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Dir.tryListFiles "*.fs" "src/Etymon.Core" // Ok [ "...\\Check.fs"; ... ]
        /// </code></example>
        val tryListFiles:
          pattern: string -> path: string -> Result<string list,FileError>
        
        /// <summary>The files inside a directory and all of its subdirectories, sorted.</summary>
        /// <example><code lang="fsharp">
        /// Dir.tryListFilesRecursive "*.fs" "src" // Ok [ ... ]
        /// </code></example>
        val tryListFilesRecursive:
          pattern: string -> path: string -> Result<string list,FileError>
        
        /// <summary>The immediate subdirectories, sorted.</summary>
        /// <example><code lang="fsharp">
        /// Dir.tryListDirectories "src" // Ok [ "src\\Etymon.Core"; "src\\Etymon.Base" ]
        /// </code></example>
        val tryListDirectories: path: string -> Result<string list,FileError>
        
        /// <summary>
        /// Deletes a directory and everything in it. Succeeds when it was already
        /// absent.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Dir.tryDelete "out" // Ok ()
        /// </code></example>
        val tryDelete: path: string -> Result<unit,FileError>

