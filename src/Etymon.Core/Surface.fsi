
namespace FSharp



namespace Etymon
    
    /// <summary>
    /// Parsing that returns <c>option</c> instead of a bool and an out parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every function here is culture-explicit. The plain names use the invariant
    /// culture, because a value that arrived over a wire, out of a config file or off
    /// a command line has no culture and should not acquire the machine's by accident
    /// — that is the bug where a price of <c>1.5</c> becomes <c>15</c> on a German
    /// server. The <c>In</c> suffix takes a culture for text a person typed.
    /// </para>
    /// <para>
    /// Failure is always <c>None</c>, never an exception and never a reason: for a
    /// parse there is only ever one reason, and it is "that is not one of those".
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Parse =
        
        val private inv: System.Globalization.CultureInfo
        
        val private toOption: succeeded: bool * value: 'T -> 'T option
        
        val private invariantDecimalStyles: System.Globalization.NumberStyles
        
        val private invariantFloatStyles: System.Globalization.NumberStyles
        
        /// <summary>Parses an <c>int</c> using the invariant culture.</summary>
        /// <example><code lang="fsharp">
        /// Parse.int "42" // Some 42
        /// Parse.int "4.2" // None
        /// </code></example>
        val int: value: string -> int option
        
        /// <summary>Parses an <c>int</c> using a given culture.</summary>
        /// <example><code lang="fsharp">
        /// Parse.intIn (CultureInfo.GetCultureInfo "de-DE") "1.234" // Some 1234
        /// </code></example>
        val intIn:
          culture: System.Globalization.CultureInfo ->
            value: string -> int option
        
        /// <summary>Parses an <c>int64</c> using the invariant culture.</summary>
        /// <example><code lang="fsharp">
        /// Parse.int64 "9007199254740993" // Some 9007199254740993L
        /// </code></example>
        val int64: value: string -> int64 option
        
        /// <summary>Parses an <c>int64</c> using a given culture.</summary>
        /// <example><code lang="fsharp">
        /// Parse.int64In CultureInfo.CurrentCulture "1,000" // Some 1000L in en-US
        /// </code></example>
        val int64In:
          culture: System.Globalization.CultureInfo ->
            value: string -> int64 option
        
        /// <summary>Parses a <c>byte</c> using the invariant culture.</summary>
        /// <example><code lang="fsharp">
        /// Parse.byte "255" // Some 255uy
        /// Parse.byte "256" // None
        /// </code></example>
        val byte: value: string -> byte option
        
        /// <summary>
        /// Parses a <c>byte</c> in a given culture, accepting that culture's grouping.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.byteIn (CultureInfo "de-DE") "255" // Some 255uy
        /// </code></example>
        val byteIn:
          culture: System.Globalization.CultureInfo ->
            value: string -> byte option
        
        /// <summary>
        /// Parses a <c>decimal</c> using the invariant culture. Prefer this to
        /// <see cref="M:Etymon.Parse.float"/> for money: a decimal keeps its scale and
        /// does not round in binary.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.decimal "1.50" // Some 1.50m
        /// Parse.decimal "1,50" // None -- that is a comma, not a decimal point
        /// </code></example>
        val decimal: value: string -> decimal option
        
        /// <summary>Parses a <c>decimal</c> using a given culture.</summary>
        /// <example><code lang="fsharp">
        /// Parse.decimalIn (CultureInfo.GetCultureInfo "de-DE") "1,50" // Some 1.50m
        /// </code></example>
        val decimalIn:
          culture: System.Globalization.CultureInfo ->
            value: string -> decimal option
        
        /// <summary>
        /// Parses a <c>float</c> using the invariant culture. Accepts exponents;
        /// rejects <c>NaN</c> and the infinities, which are almost never what a text
        /// field meant.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.float "1.5e3" // Some 1500.0
        /// Parse.float "NaN" // None
        /// </code></example>
        val float: value: string -> float option
        
        /// <summary>Parses a <c>float</c> using a given culture.</summary>
        /// <example><code lang="fsharp">
        /// Parse.floatIn (CultureInfo.GetCultureInfo "fr-FR") "1,5" // Some 1.5
        /// </code></example>
        val floatIn:
          culture: System.Globalization.CultureInfo ->
            value: string -> float option
        
        /// <summary>
        /// Parses a <c>bool</c> from the forms that actually occur in configuration
        /// and query strings, not only the two that <c>Boolean.TryParse</c> accepts:
        /// <c>true</c>/<c>false</c>, <c>yes</c>/<c>no</c>, <c>on</c>/<c>off</c>,
        /// <c>1</c>/<c>0</c>. Case and surrounding whitespace are ignored.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.bool "YES" // Some true
        /// Parse.bool "0" // Some false
        /// Parse.bool "maybe" // None
        /// </code></example>
        val bool: value: string -> bool option
        
        /// <summary>
        /// Parses a <c>Guid</c> in any of the formats <c>Guid.TryParse</c> accepts,
        /// including braced and hyphenless.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.guid "f81d4fae-7dec-11d0-a765-00a0c91e6bf6" // Some ...
        /// </code></example>
        val guid: value: string -> System.Guid option
        
        /// <summary>
        /// Parses a <c>Guid</c> in canonical 8-4-4-4-12 form only. Use this at a
        /// boundary where the shape is part of the contract.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.guidExact "{f81d4fae-7dec-11d0-a765-00a0c91e6bf6}" // None -- braced
        /// </code></example>
        val guidExact: value: string -> System.Guid option
        
        /// <summary>
        /// Parses a <c>DateTimeOffset</c> from an RFC 3339 / ISO 8601 timestamp,
        /// which is the only form a wire value should be in.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.dateTimeOffset "2026-09-20T14:30:00Z" // Some ...
        /// Parse.dateTimeOffset "20/09/2026" // None
        /// </code></example>
        val dateTimeOffset: value: string -> System.DateTimeOffset option
        
        /// <summary>
        /// Parses a <c>DateTimeOffset</c> using a given culture and the formats that
        /// culture considers ordinary. For text a person typed.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.dateTimeOffsetIn (CultureInfo.GetCultureInfo "en-GB") "20/09/2026" // Some ...
        /// </code></example>
        val dateTimeOffsetIn:
          culture: System.Globalization.CultureInfo ->
            value: string -> System.DateTimeOffset option
        
        /// <summary>
        /// Parses a <c>DateTime</c> from an RFC 3339 timestamp, preserving the offset
        /// information as <c>Utc</c> or <c>Local</c> rather than silently dropping it.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.dateTime "2026-09-20T14:30:00Z" // Some (Kind = Utc)
        /// </code></example>
        val dateTime: value: string -> System.DateTime option
        
        /// <summary>Parses a <c>DateTime</c> using a given culture.</summary>
        /// <example><code lang="fsharp">
        /// Parse.dateTimeIn (CultureInfo.GetCultureInfo "en-GB") "20/09/2026 14:30" // Some ...
        /// </code></example>
        val dateTimeIn:
          culture: System.Globalization.CultureInfo ->
            value: string -> System.DateTime option
        
        /// <summary>Parses a <c>DateOnly</c> in ISO 8601 <c>yyyy-MM-dd</c> form.</summary>
        /// <example><code lang="fsharp">
        /// Parse.dateOnly "2026-09-20" // Some ...
        /// Parse.dateOnly "2026-9-20" // None -- ISO 8601 pads
        /// </code></example>
        val dateOnly: value: string -> System.DateOnly option
        
        /// <summary>Parses a <c>DateOnly</c> using a given culture.</summary>
        /// <example><code lang="fsharp">
        /// Parse.dateOnlyIn (CultureInfo.GetCultureInfo "en-GB") "20/09/2026" // Some ...
        /// </code></example>
        val dateOnlyIn:
          culture: System.Globalization.CultureInfo ->
            value: string -> System.DateOnly option
        
        /// <summary>Parses a <c>TimeOnly</c> in ISO 8601 <c>HH:mm:ss</c> form.</summary>
        /// <example><code lang="fsharp">
        /// Parse.timeOnly "14:30:00" // Some ...
        /// </code></example>
        val timeOnly: value: string -> System.TimeOnly option
        
        /// <summary>
        /// Parses a <c>TimeOnly</c> the way a person in a given culture would write
        /// one, which may be a twelve-hour clock.
        /// </summary>
        /// <remarks>
        /// Not restricted to the ISO forms, because the whole point of taking a
        /// culture is to accept what somebody typed rather than what a wire format
        /// would have sent.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Parse.timeOnlyIn (CultureInfo "en-US") "2:30 PM" // Some 14:30
        /// </code></example>
        val timeOnlyIn:
          culture: System.Globalization.CultureInfo ->
            value: string -> System.TimeOnly option
        
        /// <summary>
        /// Parses a <c>TimeSpan</c> in .NET's <c>[d.]hh:mm:ss[.fffffff]</c> form using
        /// the invariant culture.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.timeSpan "1.02:03:04" // Some (1 day, 2 hours, 3 minutes, 4 seconds)
        /// </code></example>
        val timeSpan: value: string -> System.TimeSpan option
        
        /// <summary>
        /// Parses a <c>TimeSpan</c> in a given culture, which decides the separator
        /// between the seconds and the fraction.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.timeSpanIn (CultureInfo "de-DE") "1.02:03:04,5"
        /// </code></example>
        val timeSpanIn:
          culture: System.Globalization.CultureInfo ->
            value: string -> System.TimeSpan option
        
        /// <summary>
        /// Parses an enum by name, ignoring case, and rejects a numeric value that
        /// does not correspond to a declared member. <c>Enum.TryParse</c> accepts any
        /// number at all, which is how an undefined enum value gets into a domain.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Parse.enum&lt;DayOfWeek&gt; "monday" // Some DayOfWeek.Monday
        /// Parse.enum&lt;DayOfWeek&gt; "99" // None
        /// </code></example>
        val enum:
          value: string -> 'T option
            when 'T: struct and 'T :> System.Enum and 'T: (new: unit -> 'T)

namespace Etymon
    
    /// One step into a value: a named field, or a position in a collection.
    [<RequireQualifiedAccess>]
    type PathSegment =
        
        /// A named field, such as the <c>zip</c> in <c>address.zip</c>.
        | Field of name: string
        
        /// A zero-based position, such as the <c>3</c> in <c>items[3]</c>.
        | Index of position: int
    
    /// <summary>
    /// A location inside a value. Every <see cref="T:Etymon.ValidationError"/> carries one,
    /// so a caller can say not just <em>what</em> was wrong but <em>where</em>.
    /// </summary>
    /// <example>
    /// <code lang="fsharp">
    /// Path.root |> Path.field "address" |> Path.field "zip" |> Path.toString
    /// // "address.zip"
    /// </code>
    /// </example>
    [<StructuredFormatDisplay ("{Display}"); Struct>]
    type Path =
        private Path of PathSegment list
        
        /// Renders the path in dotted form, for example <c>items[3].name</c>.
        override ToString: unit -> string
        
        /// Used by F#'s <c>%A</c> formatting.
        member Display: string
    
    /// Building and rendering <see cref="T:Etymon.Path"/> values.
    [<RequireQualifiedAccess>]
    module Path =
        
        /// The empty path: the value itself, with no steps taken into it.
        val root: Path
        
        /// <summary>Steps into a named field.</summary>
        /// <example><code lang="fsharp">
        /// Path.root |> Path.field "address" |> Path.field "zip" |> Path.toString // "address.zip"
        /// </code></example>
        val field: name: string -> Path -> Path
        
        /// <summary>Steps into a zero-based position in a collection.</summary>
        /// <example><code lang="fsharp">
        /// Path.root |> Path.field "items" |> Path.index 3 |> Path.toString // "items[3]"
        /// </code></example>
        val index: position: int -> Path -> Path
        
        /// <summary>Steps into an arbitrary segment.</summary>
        /// <example><code lang="fsharp">
        /// Path.root |> Path.push (PathSegment.Field "name") |> Path.toString // "name"
        /// </code></example>
        val push: segment: PathSegment -> Path -> Path
        
        /// <summary>The segments of a path, outermost first.</summary>
        /// <example><code lang="fsharp">
        /// Path.root |> Path.field "a" |> Path.index 0 |> Path.segments
        /// // [ PathSegment.Field "a"; PathSegment.Index 0 ]
        /// </code></example>
        val segments: Path -> PathSegment list
        
        /// <summary>Builds a path from segments given outermost first.</summary>
        /// <example><code lang="fsharp">
        /// Path.ofSegments [ PathSegment.Field "a"; PathSegment.Index 0 ] |> Path.toString // "a[0]"
        /// </code></example>
        val ofSegments: outermostFirst: PathSegment list -> Path
        
        /// <summary>True when the path points at the value itself.</summary>
        /// <example><code lang="fsharp">
        /// Path.isRoot Path.root // true
        /// </code></example>
        val isRoot: Path -> bool
        
        /// <summary>How many steps the path takes.</summary>
        /// <example><code lang="fsharp">
        /// Path.root |> Path.field "a" |> Path.index 0 |> Path.depth // 2
        /// </code></example>
        val depth: Path -> int
        
        /// <summary>The path with its last step removed, or <c>None</c> at the root.</summary>
        /// <example><code lang="fsharp">
        /// Path.root |> Path.field "a" |> Path.field "b" |> Path.parent |> Option.map Path.toString
        /// // Some "a"
        /// </code></example>
        val parent: Path -> Path option
        
        /// <summary>
        /// Places <paramref name="inner"/> underneath <paramref name="outer"/>. Used when a
        /// nested decoder reports errors relative to its own root.
        /// </summary>
        /// <example><code lang="fsharp">
        /// let outer = Path.root |> Path.field "address"
        /// let inner = Path.root |> Path.field "zip"
        /// Path.combine outer inner |> Path.toString // "address.zip"
        /// </code></example>
        val combine: outer: Path -> inner: Path -> Path
        
        /// <summary>Renders the path in dotted form. The root renders as the empty string.</summary>
        /// <example><code lang="fsharp">
        /// Path.root |> Path.field "items" |> Path.index 3 |> Path.field "name" |> Path.toString
        /// // "items[3].name"
        /// </code></example>
        val toString: path: Path -> string
        
        /// <summary>
        /// Renders the path, substituting <paramref name="rootLabel"/> for the root so that
        /// a top-level error reads as something rather than nothing.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Path.toStringOr "(value)" Path.root // "(value)"
        /// </code></example>
        val toStringOr: rootLabel: string -> path: Path -> string

namespace Etymon
    
    /// A number appearing in a constraint. Constraints are data that outlives the type
    /// they were written against, so they carry their own numeric representation rather
    /// than being generic.
    [<RequireQualifiedAccess>]
    type Num =
        
        /// An integral value.
        | Int of value: int64
        
        /// An exact decimal value.
        | Dec of value: decimal
        
        /// A binary floating-point value.
        | Float of value: float
    
    /// Working with <see cref="T:Etymon.Num"/>.
    [<RequireQualifiedAccess>]
    module Num =
        
        val private isNaN: n: Num -> bool
        
        val private asFloat: n: Num -> float
        
        val private tryAsDecimal: n: Num -> decimal voption
        
        /// <summary>
        /// Orders two numbers, promoting to a representation that can hold both.
        /// Returns <c>ValueNone</c> when either side is NaN, which is unordered.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Num.compare (Num.Int 1L) (Num.Dec 1.5m) // ValueSome -1
        /// </code></example>
        val compare: left: Num -> right: Num -> int voption
        
        /// <summary>Renders a number the way a human-readable message should show it.</summary>
        /// <example><code lang="fsharp">
        /// Num.toString (Num.Dec 1.50m) // "1.50"
        /// </code></example>
        val toString: n: Num -> string
    
    /// A named string format, matching the <c>format</c> vocabulary of JSON Schema.
    [<RequireQualifiedAccess>]
    type Format =
        
        /// An RFC 5322 mailbox that also has a dotted host.
        | Email
        
        /// A canonical 8-4-4-4-12 UUID.
        | Uuid
        
        /// An RFC 3339 timestamp, for example <c>2026-09-20T14:30:00Z</c>.
        | DateTime
        
        /// An ISO 8601 calendar date, for example <c>2026-09-20</c>.
        | Date
        
        /// An ISO 8601 wall-clock time, for example <c>14:30:00</c>.
        | Time
        
        /// An ISO 8601 duration, for example <c>P3DT4H</c>.
        | Duration
        
        /// An absolute URI.
        | Uri
        
        /// A URI or a relative reference.
        | UriReference
        
        /// An RFC 1123 host name.
        | Hostname
        
        /// A dotted-quad IPv4 address.
        | Ipv4
        
        /// An IPv6 address.
        | Ipv6
        
        /// A format Etymon does not know how to check. Carried into generated
        /// documentation, but never enforced.
        | Custom of name: string
    
    /// Working with <see cref="T:Etymon.Format"/>.
    [<RequireQualifiedAccess>]
    module Format =
        
        /// <summary>The JSON Schema <c>format</c> keyword for this format.</summary>
        /// <example><code lang="fsharp">
        /// Format.name Format.DateTime // "date-time"
        /// </code></example>
        val name: format: Format -> string
    
    /// <summary>
    /// A restriction on a value, expressed as data.
    /// </summary>
    /// <remarks>
    /// This is the single constraint vocabulary for the whole suite. <c>Etymon.Schema</c>
    /// attaches these to fields; <c>Etymon.Schema.OpenApi</c>, <c>Etymon.Schema.Sql</c> and
    /// <c>Etymon.Invariants.FsCheck</c> each read them without ever running the check that
    /// produced them. Anything expressible here can be derived into a JSON Schema keyword,
    /// a SQL CHECK clause, or a generator; anything that cannot is <c>Opaque</c>, and is
    /// documented but never derived.
    /// </remarks>
    [<RequireQualifiedAccess>]
    type Constraint =
        
        /// Bounds on the number of characters in a string, inclusive.
        | Length of min: int option * max: int option
        
        /// Bounds on a numeric value.
        | Range of
          min: Num option * max: Num option * exclusiveMin: bool *
          exclusiveMax: bool
        
        /// A .NET regular expression the whole value must match.
        | Pattern of regex: string
        
        /// A named string format.
        | HasFormat of format: Format
        
        /// An explicit set of permitted values.
        | OneOf of values: string list
        
        /// Bounds on the number of elements in a collection, and whether they must differ.
        | Items of min: int option * max: int option * unique: bool
        
        /// A rule Etymon cannot inspect. Carried into documentation and error messages,
        /// but never turned into a schema keyword, a SQL constraint or a generator.
        | Opaque of code: string * description: string
    
    /// Working with <see cref="T:Etymon.Constraint"/>.
    [<RequireQualifiedAccess>]
    module Constraint =
        
        val private plural: count: int -> singular: string -> string
        
        /// <summary>
        /// A human-readable statement of what the constraint requires, phrased so that it
        /// reads after a field name: "age must be at least 0".
        /// </summary>
        /// <example><code lang="fsharp">
        /// Constraint.describe (Constraint.Length(Some 3, Some 254))
        /// // "must be between 3 and 254 characters"
        /// </code></example>
        val describe: c: Constraint -> string

namespace Etymon
    
    /// Why a value was rejected, in a form a program can branch on. The human-readable
    /// wording lives separately on <see cref="T:Etymon.ValidationError"/>, so that
    /// callers can re-word an error without having to parse it.
    [<RequireQualifiedAccess>]
    type ErrorReason =
        
        /// A required value was not present at all.
        | Missing
        
        /// A value was present but of the wrong shape.
        | TypeMismatch of expected: string * actual: string
        
        /// A value was of the right shape but broke a constraint.
        | ConstraintFailed of failed: Constraint
        
        /// A value was rejected by a rule identified by a stable code.
        | Rejected of code: string
    
    /// A single thing that was wrong, and where.
    [<StructuredFormatDisplay ("{Display}")>]
    type ValidationError =
        {
          
          /// Where in the value the problem was found.
          Path: Path
          
          /// Why it was rejected, in machine-readable form.
          Reason: ErrorReason
          
          /// What to tell a human. Phrased to read after the path.
          Message: string
        }
        
        /// Renders as <c>path: message</c>, or just the message at the root.
        override ToString: unit -> string
        
        /// Used by F# <c>%A</c> formatting.
        member Display: string
    
    /// <summary>
    /// One or more validation errors. Never empty: if you are holding one of these,
    /// something was wrong.
    /// </summary>
    /// <remarks>
    /// Etymon accumulates by default. A decode that finds four bad fields produces four
    /// errors, not the first one. The only operations that short-circuit are the ones
    /// that genuinely cannot continue, such as binding on a value that was never produced.
    /// </remarks>
    [<StructuredFormatDisplay ("{Display}"); CustomEquality; NoComparison>]
    type ValidationErrors =
        private | Single of ValidationError
                | Join of left: ValidationErrors * right: ValidationErrors
        
        override Equals: other: obj -> bool
        
        override GetHashCode: unit -> int
        
        /// <summary>
        /// Every error, in the order they were found.
        /// </summary>
        /// <remarks>
        /// Iterative rather than recursive: a long run of joins is a deep tree, and
        /// a deep tree is exactly what arrives when the input was large.
        /// </remarks>
        member ToList: unit -> ValidationError list
        
        /// Renders every error, one per line.
        override ToString: unit -> string
        
        /// Used by F# <c>%A</c> formatting.
        member Display: string
    
    /// Building and inspecting <see cref="T:Etymon.ValidationErrors"/>.
    [<RequireQualifiedAccess>]
    module ValidationErrors =
        
        /// <summary>A collection holding exactly one error.</summary>
        /// <example><code lang="fsharp">
        /// ValidationErrors.one { Path = Path.root; Reason = ErrorReason.Missing; Message = "is required" }
        /// </code></example>
        val one: error: ValidationError -> ValidationErrors
        
        /// <summary>Builds one error from its parts.</summary>
        /// <example><code lang="fsharp">
        /// ValidationErrors.create (Path.root |> Path.field "name") ErrorReason.Missing "is required"
        /// </code></example>
        val create:
          path: Path ->
            reason: ErrorReason -> message: string -> ValidationErrors
        
        /// <summary>
        /// Builds from a list, or returns <c>None</c> when the list is empty. Emptiness is
        /// not an error condition, so it is not representable.
        /// </summary>
        /// <example><code lang="fsharp">
        /// ValidationErrors.ofList [] // None
        /// </code></example>
        val ofList: errors: ValidationError list -> ValidationErrors option
        
        /// <summary>
        /// Builds from a first error and any that followed, so that non-emptiness holds by
        /// construction rather than by checking.
        /// </summary>
        /// <example><code lang="fsharp">
        /// match found with
        /// | [] -> Ok value
        /// | first :: rest -> Error(ValidationErrors.ofHeadTail first rest)
        /// </code></example>
        val ofHeadTail:
          first: ValidationError ->
            rest: ValidationError list -> ValidationErrors
        
        /// <summary>Every error, in the order they were found.</summary>
        /// <example><code lang="fsharp">
        /// errs |> ValidationErrors.toList |> List.map (fun e -> e.Message)
        /// </code></example>
        val toList: errors: ValidationErrors -> ValidationError list
        
        /// <summary>The first error found.</summary>
        /// <example><code lang="fsharp">
        /// (ValidationErrors.one e |> ValidationErrors.head).Message
        /// </code></example>
        val head: errors: ValidationErrors -> ValidationError
        
        /// <summary>How many things were wrong.</summary>
        /// <example><code lang="fsharp">
        /// ValidationErrors.count errs // 4
        /// </code></example>
        val count: errors: ValidationErrors -> int
        
        /// <summary>
        /// Joins two collections, keeping the left-hand errors first. This is how
        /// accumulation happens.
        /// </summary>
        /// <example><code lang="fsharp">
        /// ValidationErrors.append nameErrors ageErrors |> ValidationErrors.count // 2
        /// </code></example>
        val append:
          left: ValidationErrors -> right: ValidationErrors -> ValidationErrors
        
        /// <summary>Rewrites every error.</summary>
        /// <example><code lang="fsharp">
        /// errs |> ValidationErrors.map (fun e -> { e with Message = e.Message.ToUpperInvariant() })
        /// </code></example>
        val map:
          f: (ValidationError -> ValidationError) ->
            errors: ValidationErrors -> ValidationErrors
        
        /// <summary>
        /// Re-roots every error one step deeper, so that a nested decoder's errors read
        /// relative to the outer value.
        /// </summary>
        /// <example><code lang="fsharp">
        /// // an error at "zip" becomes an error at "address.zip"
        /// errs |> ValidationErrors.under (PathSegment.Field "address")
        /// </code></example>
        val under:
          segment: PathSegment -> errors: ValidationErrors -> ValidationErrors
        
        /// <summary>Re-roots every error under a named field.</summary>
        /// <example><code lang="fsharp">
        /// errs |> ValidationErrors.underField "address"
        /// </code></example>
        val underField:
          name: string -> errors: ValidationErrors -> ValidationErrors
        
        /// <summary>Re-roots every error under a collection position.</summary>
        /// <example><code lang="fsharp">
        /// errs |> ValidationErrors.underIndex 3
        /// </code></example>
        val underIndex:
          position: int -> errors: ValidationErrors -> ValidationErrors
        
        /// <summary>Every error, one per line, ready to log or show.</summary>
        /// <example><code lang="fsharp">
        /// printfn "%s" (ValidationErrors.format errs)
        /// // name: is required
        /// // age: must be at least 0
        /// </code></example>
        val format: errors: ValidationErrors -> string

namespace Etymon
    
    /// <summary>
    /// The result of validating something: the value, or every reason it was rejected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an abbreviation for <c>Result&lt;'T, ValidationErrors&gt;</c>, not a distinct
    /// type. You can pattern-match it with <c>Ok</c> and <c>Error</c>, and it interoperates
    /// with the <c>Result</c> module and with other libraries for free.
    /// </para>
    /// <para>
    /// The trade-off: <see cref="M:Etymon.Validation.apply"/> accumulates errors, whereas an
    /// <c>apply</c> from a general-purpose <c>Result</c> library short-circuits on the first
    /// failure. Reach for the <c>Validation</c> module when you want every error, and for
    /// <c>Validation.bind</c> only when a later step genuinely needs an earlier step's value.
    /// </para>
    /// </remarks>
    type Validation<'T> = Result<'T,ValidationErrors>
    
    /// Combinators over <see cref="T:Etymon.Validation`1"/>. Everything here accumulates
    /// errors except <c>bind</c>, which cannot.
    [<RequireQualifiedAccess>]
    module Validation =
        
        /// <summary>A successful validation.</summary>
        /// <example><code lang="fsharp">
        /// Validation.ok 42 // Ok 42
        /// </code></example>
        val ok: value: 'T -> Validation<'T>
        
        /// <summary>A failed validation carrying an existing error collection.</summary>
        /// <example><code lang="fsharp">
        /// Validation.errors existing : Validation&lt;int&gt;
        /// </code></example>
        val errors: e: ValidationErrors -> Validation<'T>
        
        /// <summary>A failed validation carrying exactly one error.</summary>
        /// <example><code lang="fsharp">
        /// Validation.error (Path.root |> Path.field "age") ErrorReason.Missing "is required"
        /// </code></example>
        val error:
          path: Path -> reason: ErrorReason -> message: string -> Validation<'T>
        
        /// <summary>True when the value passed.</summary>
        /// <example><code lang="fsharp">
        /// Validation.isOk (Validation.ok 1) // true
        /// </code></example>
        val isOk: v: Validation<'T> -> bool
        
        /// <summary>True when the value was rejected.</summary>
        /// <example><code lang="fsharp">
        /// Validation.isError (Validation.ok 1) // false
        /// </code></example>
        val isError: v: Validation<'T> -> bool
        
        /// <summary>Transforms a value that passed, leaving errors untouched.</summary>
        /// <example><code lang="fsharp">
        /// Validation.ok 2 |> Validation.map ((*) 10) // Ok 20
        /// </code></example>
        val map: f: ('T -> 'U) -> v: Validation<'T> -> Validation<'U>
        
        /// <summary>Rewrites the errors, leaving a passing value untouched.</summary>
        /// <example><code lang="fsharp">
        /// v |> Validation.mapErrors (ValidationErrors.underField "address")
        /// </code></example>
        val mapErrors:
          f: (ValidationErrors -> ValidationErrors) ->
            v: Validation<'T> -> Validation<'T>
        
        /// <summary>
        /// Runs a second validation that needs the first one's value. This is the one
        /// combinator that cannot accumulate: if the first step failed there is no value
        /// to give the second.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Validation.ok "42" |> Validation.bind parseAge
        /// </code></example>
        val bind:
          f: ('T -> Validation<'U>) -> v: Validation<'T> -> Validation<'U>
        
        /// <summary>
        /// Applies a validated function to a validated argument, collecting the errors of
        /// both sides. This is the primitive every other accumulating combinator is built on.
        /// </summary>
        /// <example><code lang="fsharp">
        /// // two bad fields produce two errors, not one
        /// Validation.apply (Validation.map (+) badA) badB |> Validation.errorCount // 2
        /// </code></example>
        val apply:
          f: Validation<('T -> 'U)> -> v: Validation<'T> -> Validation<'U>
        
        /// <summary>Combines two validations, collecting errors from both.</summary>
        /// <example><code lang="fsharp">
        /// Validation.map2 (fun a b -> a + b) (Validation.ok 1) (Validation.ok 2) // Ok 3
        /// </code></example>
        val map2:
          f: ('A -> 'B -> 'C) ->
            a: Validation<'A> -> b: Validation<'B> -> Validation<'C>
        
        /// <summary>Combines three validations, collecting errors from all of them.</summary>
        /// <example><code lang="fsharp">
        /// Validation.map3 (fun a b c -> a + b + c) (Validation.ok 1) (Validation.ok 2) (Validation.ok 3)
        /// // Ok 6
        /// </code></example>
        val map3:
          f: ('A -> 'B -> 'C -> 'D) ->
            a: Validation<'A> ->
            b: Validation<'B> -> c: Validation<'C> -> Validation<'D>
        
        /// <summary>Pairs two validations, collecting errors from both.</summary>
        /// <example><code lang="fsharp">
        /// Validation.zip (Validation.ok 1) (Validation.ok "a") // Ok (1, "a")
        /// </code></example>
        val zip: a: Validation<'A> -> b: Validation<'B> -> Validation<'A * 'B>
        
        /// <summary>
        /// Validates every element, collecting errors from all of them. Error paths are left
        /// alone; use <see cref="M:Etymon.Validation.traverseIndexed"/> when they should carry
        /// the element position.
        /// </summary>
        /// <example><code lang="fsharp">
        /// [ "1"; "x"; "y" ] |> Validation.traverse parseInt |> Validation.errorCount // 2
        /// </code></example>
        val traverse:
          f: ('T -> Validation<'U>) -> items: 'T list -> Validation<'U list>
        
        /// <summary>
        /// Validates every element, collecting errors from all of them and re-rooting each
        /// element's errors under its position.
        /// </summary>
        /// <example><code lang="fsharp">
        /// [ "1"; "x" ] |> Validation.traverseIndexed (fun _ s -> parseInt s)
        /// // Error [ "[1]: must be a number" ]
        /// </code></example>
        val traverseIndexed:
          f: (int -> 'T -> Validation<'U>) ->
            items: 'T list -> Validation<'U list>
        
        /// <summary>Collects a list of validations into a validation of a list.</summary>
        /// <example><code lang="fsharp">
        /// Validation.sequence [ Validation.ok 1; Validation.ok 2 ] // Ok [ 1; 2 ]
        /// </code></example>
        val sequence: items: Validation<'T> list -> Validation<'T list>
        
        /// <summary>Re-roots every error one step deeper.</summary>
        /// <example><code lang="fsharp">
        /// v |> Validation.under (PathSegment.Field "address")
        /// </code></example>
        val under: segment: PathSegment -> v: Validation<'T> -> Validation<'T>
        
        /// <summary>Re-roots every error under a named field.</summary>
        /// <example><code lang="fsharp">
        /// decodeZip json |> Validation.underField "address" // errors read "address.zip: ..."
        /// </code></example>
        val underField: name: string -> v: Validation<'T> -> Validation<'T>
        
        /// <summary>Re-roots every error under a collection position.</summary>
        /// <example><code lang="fsharp">
        /// decodeItem json |> Validation.underIndex 3 // errors read "[3]: ..."
        /// </code></example>
        val underIndex: position: int -> v: Validation<'T> -> Validation<'T>
        
        /// <summary>Turns an absent value into a validation failure.</summary>
        /// <example><code lang="fsharp">
        /// None |> Validation.ofOption (Path.root |> Path.field "name") ErrorReason.Missing "is required"
        /// </code></example>
        val ofOption:
          path: Path ->
            reason: ErrorReason ->
            message: string -> value: 'T option -> Validation<'T>
        
        /// <summary>Lifts a plain <c>Result</c> by describing how its error becomes a validation error.</summary>
        /// <example><code lang="fsharp">
        /// Error "nope" |> Validation.ofResult (fun m -> { Path = Path.root; Reason = ErrorReason.Rejected "nope"; Message = m })
        /// </code></example>
        val ofResult:
          toError: ('E -> ValidationError) ->
            result: Result<'T,'E> -> Validation<'T>
        
        /// <summary>The value, or a fallback if it was rejected.</summary>
        /// <example><code lang="fsharp">
        /// Validation.defaultValue 0 (Validation.error Path.root ErrorReason.Missing "x") // 0
        /// </code></example>
        val defaultValue: fallback: 'T -> v: Validation<'T> -> 'T
        
        /// <summary>The value, or a fallback computed from the errors.</summary>
        /// <example><code lang="fsharp">
        /// v |> Validation.defaultWith (fun errs -> failwith (ValidationErrors.format errs))
        /// </code></example>
        val defaultWith:
          fallback: (ValidationErrors -> 'T) -> v: Validation<'T> -> 'T
        
        /// <summary>How many things were wrong; zero when the value passed.</summary>
        /// <example><code lang="fsharp">
        /// Validation.errorCount (Validation.ok 1) // 0
        /// </code></example>
        val errorCount: v: Validation<'T> -> int
        
        /// <summary>Every error, or an empty list when the value passed.</summary>
        /// <example><code lang="fsharp">
        /// Validation.errorList v |> List.map (fun e -> Path.toString e.Path)
        /// </code></example>
        val errorList: v: Validation<'T> -> ValidationError list
    
    /// <summary>
    /// The computation expression for <see cref="T:Etymon.Validation`1"/>.
    /// </summary>
    /// <remarks>
    /// Use <c>let!</c> with <c>and!</c> to validate independent things and collect every
    /// error. A plain sequence of <c>let!</c> bindings uses <c>bind</c> instead and will
    /// stop at the first failure, which is almost never what you want here.
    /// </remarks>
    type ValidationBuilder =
        
        new: unit -> ValidationBuilder
        
        /// Sequential binding. Short-circuits, because the second step needs the first value.
        member
          Bind: v: Validation<'T> * f: ('T -> Validation<'U>) -> Validation<'U>
        
        /// The final mapping step of an applicative chain.
        member BindReturn: v: Validation<'T> * f: ('T -> 'U) -> Validation<'U>
        
        /// Combines two independent validations, collecting errors from both. This is what
        /// makes <c>and!</c> accumulate.
        member
          MergeSources: a: Validation<'A> * b: Validation<'B> ->
                          Validation<'A * 'B>
        
        /// Wraps a plain value.
        member Return: value: 'T -> Validation<'T>
        
        /// Passes a validation through unchanged.
        member ReturnFrom: v: Validation<'T> -> Validation<'T>
        
        /// Identity on sources.
        member Source: v: Validation<'T> -> Validation<'T>
    
    /// Exposes the <c>validation { ... }</c> computation expression.
    [<AutoOpen>]
    module ValidationBuilderExtensions =
        
        /// <summary>
        /// Validates several things at once, collecting every error.
        /// </summary>
        /// <example><code lang="fsharp">
        /// validation {
        ///     let! name = NonEmpty.create raw.Name |> Validation.underField "name"
        ///     and! age = Age.create raw.Age |> Validation.underField "age"
        ///     return { Name = name; Age = age }
        /// }
        /// // a bad name AND a bad age produce two errors
        /// </code></example>
        val validation: ValidationBuilder

namespace Etymon
    
    /// <summary>
    /// A <see cref="T:Etymon.Constraint"/> paired with the test that decides it.
    /// </summary>
    /// <remarks>
    /// The <c>Constraint</c> is data and the <c>Satisfies</c> function is behaviour. Every
    /// package downstream of Core reads the data and ignores the behaviour:
    /// <c>Etymon.Schema.OpenApi</c> turns it into a JSON Schema keyword,
    /// <c>Etymon.Schema.Sql</c> into a SQL constraint, <c>Etymon.Invariants.FsCheck</c> into a
    /// generator. Keeping them in one value is what stops the two drifting apart.
    /// </remarks>
    [<NoEquality; NoComparison>]
    type ConstraintCheck<'T> =
        {
          
          /// What this check requires, as inspectable data.
          Constraint: Constraint
          
          /// Whether a value meets it.
          Satisfies: ('T -> bool)
          
          /// <summary>
          /// A stable code to report instead of the constraint, when one is
          /// wanted.
          /// </summary>
          /// <remarks>
          /// For an API that already publishes error codes its callers branch on.
          /// The constraint is still carried, so the OpenAPI document, the SQL and
          /// the generators are unaffected — only the reported reason changes.
          /// </remarks>
          Code: string option
          
          /// Wording to use instead of the constraint's own description.
          Message: string option
        }
    
    /// Ready-made <see cref="T:Etymon.ConstraintCheck`1"/> values, and the means to run them.
    [<RequireQualifiedAccess>]
    module Check =
        
        val private regexCache:
          System.Collections.Concurrent.ConcurrentDictionary<string,
                                                             System.Text.RegularExpressions.Regex>
        
        val private regexFor:
          pattern: string -> System.Text.RegularExpressions.Regex
        
        val private hostnameRegex: System.Text.RegularExpressions.Regex
        
        val private isHostname: value: string -> bool
        
        val private isEmail: value: string -> bool
        
        val private isUuid: value: string -> bool
        
        val private isRfc3339: value: string -> bool
        
        val private isDate: value: string -> bool
        
        val private isTime: value: string -> bool
        
        val private isDuration: value: string -> bool
        
        /// <summary>
        /// An absolute URI: a scheme, then the rest.
        /// </summary>
        /// <remarks>
        /// <c>Uri.TryCreate</c> with <c>UriKind.Absolute</c> is not enough on its
        /// own, because on Unix it accepts a rooted file path — <c>/relative</c>
        /// parses as an implicit <c>file:</c> URI, and the same string is rejected
        /// on Windows. A validator whose answer depends on the operating system is
        /// worse than no validator, so the scheme is required explicitly and the
        /// implicit-file case is excluded.
        /// </remarks>
        val private isAbsoluteUri: value: string -> bool
        
        val private isUriReference: value: string -> bool
        
        val private isIp:
          family: System.Net.Sockets.AddressFamily -> value: string -> bool
        
        val private satisfiesFormat: format: Format -> value: string -> bool
        
        /// <summary>Pairs a constraint with an arbitrary test. Prefer the named helpers below.</summary>
        /// <example><code lang="fsharp">
        /// Check.make (Constraint.Pattern "^A") (fun (s: string) -> s.StartsWith "A")
        /// </code></example>
        val make:
          c: Constraint -> satisfies: ('T -> bool) -> ConstraintCheck<'T>
        
        /// <summary>
        /// Reports a stable code when this check fails, instead of the constraint
        /// itself.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For adopting Etymon behind an API whose callers already branch on error
        /// codes. Without this, moving a rule from hand-written code into a schema
        /// changes what the API returns, which makes the change visible to every
        /// integrator — and that is enough of a reason not to make it.
        /// </para>
        /// <para>
        /// The constraint is still carried, so the OpenAPI document, the generated
        /// SQL and the generators see exactly what they saw before. Only
        /// <c>ValidationError.Reason</c> changes, from <c>ConstraintFailed</c> to
        /// <c>Rejected</c>.
        /// </para>
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Check.minLength 1 |> Check.withCode "bill_has_no_lines"
        /// </code></example>
        val withCode:
          code: string -> check: ConstraintCheck<'T> -> ConstraintCheck<'T>
        
        /// <summary>Replaces the wording a failed check reports.</summary>
        /// <remarks>
        /// The constraint's own description says what the rule is; this says what it
        /// means here. "must be at least 1 item" is correct and tells a caller
        /// nothing about bills.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Check.minLength 1 |> Check.saying "A bill needs at least one line."
        /// </code></example>
        val saying:
          message: string -> check: ConstraintCheck<'T> -> ConstraintCheck<'T>
        
        /// <summary>Bounds the number of characters in a string, inclusive.</summary>
        /// <example><code lang="fsharp">
        /// Check.length (Some 3) (Some 254) // "ab" fails, "abc" passes
        /// </code></example>
        val length:
          min: int option -> max: int option -> ConstraintCheck<string>
        
        /// <summary>Requires at least this many characters.</summary>
        /// <example><code lang="fsharp">
        /// Check.minLength 1 // rejects ""
        /// </code></example>
        val minLength: min: int -> ConstraintCheck<string>
        
        /// <summary>Requires at most this many characters.</summary>
        /// <example><code lang="fsharp">
        /// Check.maxLength 140 // rejects a 141-character string
        /// </code></example>
        val maxLength: max: int -> ConstraintCheck<string>
        
        /// <summary>Requires exactly this many characters.</summary>
        /// <example><code lang="fsharp">
        /// Check.exactLength 5 // a US zip code
        /// </code></example>
        val exactLength: n: int -> ConstraintCheck<string>
        
        /// <summary>Requires a string with at least one character.</summary>
        /// <example><code lang="fsharp">
        /// Check.nonEmpty.Satisfies "" // false
        /// </code></example>
        val nonEmpty: ConstraintCheck<string>
        
        /// <summary>
        /// Requires the whole string to match a .NET regular expression. Anchor it yourself;
        /// Etymon does not add anchors, so that the pattern carried into generated schemas is
        /// exactly the one you wrote.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Check.pattern @"^\d{5}$" // a US zip code
        /// </code></example>
        val pattern: regex: string -> ConstraintCheck<string>
        
        /// <summary>Requires a string in a named format.</summary>
        /// <example><code lang="fsharp">
        /// Check.format Format.Email // "a@b" fails, "a@b.com" passes
        /// </code></example>
        val format: f: Format -> ConstraintCheck<string>
        
        /// <summary>Requires one of an explicit set of values.</summary>
        /// <example><code lang="fsharp">
        /// Check.oneOf [ "red"; "green"; "blue" ]
        /// </code></example>
        val oneOf: values: string list -> ConstraintCheck<string>
        
        val private numRange:
          toNum: ('T -> Num) ->
            min: 'T option ->
            max: 'T option ->
            exclusiveMin: bool -> exclusiveMax: bool -> ConstraintCheck<'T>
        
        /// <summary>Bounds an <c>int</c>, inclusive at both ends.</summary>
        /// <example><code lang="fsharp">
        /// Check.intRange (Some 0) (Some 130) // an age
        /// </code></example>
        val intRange: min: int option -> max: int option -> ConstraintCheck<int>
        
        /// <summary>Bounds an <c>int64</c>, inclusive at both ends.</summary>
        /// <example><code lang="fsharp">
        /// Check.int64Range (Some 0L) None
        /// </code></example>
        val int64Range:
          min: int64 option -> max: int64 option -> ConstraintCheck<int64>
        
        /// <summary>Bounds a <c>decimal</c>, inclusive at both ends.</summary>
        /// <example><code lang="fsharp">
        /// Check.decimalRange (Some 0m) None // a price
        /// </code></example>
        val decimalRange:
          min: decimal option -> max: decimal option -> ConstraintCheck<decimal>
        
        /// <summary>Bounds a <c>float</c>, inclusive at both ends. NaN never satisfies a bound.</summary>
        /// <example><code lang="fsharp">
        /// Check.floatRange (Some 0.0) (Some 1.0) // a probability
        /// </code></example>
        val floatRange:
          min: float option -> max: float option -> ConstraintCheck<float>
        
        /// <summary>Requires a number strictly above a floor.</summary>
        /// <example><code lang="fsharp">
        /// Check.greaterThan 0m // a positive price, zero excluded
        /// </code></example>
        val greaterThan: min: decimal -> ConstraintCheck<decimal>
        
        /// <summary>Requires a number strictly below a ceiling.</summary>
        /// <example><code lang="fsharp">
        /// Check.lessThan 1.0m
        /// </code></example>
        val lessThan: max: decimal -> ConstraintCheck<decimal>
        
        /// <summary>Bounds the number of elements in a list, inclusive.</summary>
        /// <example><code lang="fsharp">
        /// Check.itemCount (Some 1) None // a list that may not be empty
        /// </code></example>
        val itemCount:
          min: int option -> max: int option -> ConstraintCheck<'T list>
        
        /// <summary>Requires every element of a list to differ.</summary>
        /// <example><code lang="fsharp">
        /// Check.distinct.Satisfies [ 1; 1 ] // false
        /// </code></example>
        val distinct<'T when 'T: equality> :
          ConstraintCheck<'T list> when 'T: equality
        
        /// <summary>
        /// A rule Etymon cannot inspect. It is enforced, carried into error messages and
        /// shown in generated documentation, but never turned into a schema keyword, a SQL
        /// constraint or a generator.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Check.opaque "even" "must be even" (fun n -> n % 2 = 0)
        /// </code></example>
        val opaque:
          code: string ->
            description: string ->
            satisfies: ('T -> bool) -> ConstraintCheck<'T>
        
        /// <summary>The error a failed check produces at a given path.</summary>
        /// <example><code lang="fsharp">
        /// (Check.toError (Check.minLength 1) Path.root).Message // "must be at least 1 character"
        /// </code></example>
        val toError: check: ConstraintCheck<'T> -> path: Path -> ValidationError
        
        /// <summary>Runs a check, returning the value unchanged or the error it produced.</summary>
        /// <example><code lang="fsharp">
        /// Check.apply (Check.minLength 1) Path.root "" |> Validation.errorCount // 1
        /// </code></example>
        val apply:
          check: ConstraintCheck<'T> ->
            path: Path -> value: 'T -> Validation<'T>

namespace Etymon
    
    /// <summary>
    /// A value that must not be printed: a password, a connection string, an API key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Redaction that only works inside one library's error messages is theatre — the
    /// moment somebody writes <c>printfn "%A" config</c> the secret is in the log. So the
    /// redaction lives on the type: <c>ToString</c>, <c>%O</c>, <c>%A</c>, <c>string</c> and
    /// most structured loggers all render <c>&lt;redacted&gt;</c>.
    /// </para>
    /// <para>
    /// Getting the value back out is <see cref="M:Etymon.Secret.reveal"/>, which is a single
    /// greppable token — so "where does this secret escape?" is a search, not an audit.
    /// </para>
    /// <para>
    /// Two secrets compare equal when their values do, so records containing one stay usable
    /// in tests. They are deliberately <em>not</em> ordered: sorting secrets is meaningless,
    /// and a comparison that walks the value is a side channel. The practical consequence is
    /// that a record with a <c>Secret</c> field needs <c>[&lt;NoComparison&gt;]</c>, and that a
    /// secret can be a hash key but never a <c>Map</c> key.
    /// </para>
    /// <para>
    /// Serialising a <c>Secret</c> is deliberately not defined here; <c>Etymon.Schema</c>
    /// decides what a sensitive field does on the wire.
    /// </para>
    /// </remarks>
    [<CustomEquality; NoComparison; StructuredFormatDisplay ("{Display}")>]
    type Secret<'T when 'T: equality> =
        private | Secret of 'T
        
        /// Two secrets are equal when the values they hide are equal.
        override Equals: other: obj -> bool
        
        /// Hashes the hidden value, so secrets work as dictionary keys.
        override GetHashCode: unit -> int
        
        /// Always <c>&lt;redacted&gt;</c>.
        override ToString: unit -> string
        
        /// Used by F# <c>%A</c> formatting. Always <c>&lt;redacted&gt;</c>.
        member Display: string
    
    /// Building and unwrapping <see cref="T:Etymon.Secret`1"/>.
    [<RequireQualifiedAccess>]
    module Secret =
        
        /// <summary>Hides a value.</summary>
        /// <example><code lang="fsharp">
        /// let key = Secret.create "sk-live-123"
        /// printfn "%A" key // prints &lt;redacted&gt;
        /// </code></example>
        val create: value: 'T -> Secret<'T> when 'T: equality
        
        /// <summary>
        /// Unhides a value. Every place a secret leaves Etymon goes through this one
        /// function, so auditing is a search for <c>Secret.reveal</c>.
        /// </summary>
        /// <example><code lang="fsharp">
        /// let connect (cs: Secret&lt;string&gt;) = new SqlConnection(Secret.reveal cs)
        /// </code></example>
        val reveal: Secret<'a> -> 'a when 'a: equality
        
        /// <summary>Transforms the hidden value without exposing it to the caller.</summary>
        /// <example><code lang="fsharp">
        /// Secret.create "  key  " |> Secret.map (fun s -> s.Trim())
        /// </code></example>
        val map:
          f: ('T -> 'U) -> Secret<'T> -> Secret<'U>
            when 'T: equality and 'U: equality

namespace Etymon
    
    /// <summary>
    /// How to build a constrained type from a raw value, and how to get the raw value back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A refinement is the single source of truth about what a type is. It carries its
    /// constraints as data, so <c>Etymon.Schema</c> can lift it into a codec and everything
    /// downstream — JSON Schema, OpenAPI, SQL, generators — reads the same rules rather than
    /// restating them.
    /// </para>
    /// <para>
    /// Conversions run first and can fail outright; checks run afterwards and all of them
    /// run, so a value that breaks three rules reports three errors.
    /// </para>
    /// </remarks>
    [<NoEquality; NoComparison>]
    type Refinement<'Raw,'T> =
        {
          
          /// What the refined type is called, in error messages and generated schemas.
          Name: string
          
          /// The rules this refinement enforces, as inspectable data.
          Constraints: Constraint list
          
          /// Normalises and converts a raw value. Failing here stops the checks, because
          /// there is no value left to check.
          Convert: (Path -> 'Raw -> Validation<'T>)
          
          /// The checks to run once a value exists. Every one runs; their errors accumulate.
          Checks: (Path -> 'T -> ValidationError list) list
          
          /// Recovers the raw value. Total, because a refined value is already valid.
          Extract: ('T -> 'Raw)
        }
    
    /// Building <see cref="T:Etymon.Refinement`2"/> values and running them.
    [<RequireQualifiedAccess>]
    module Refine =
        
        /// <summary>A refinement that accepts anything, ready to have rules added to it.</summary>
        /// <example><code lang="fsharp">
        /// Refine.identity "Anything" : Refinement&lt;int, int&gt;
        /// </code></example>
        val identity: name: string -> Refinement<'T,'T>
        
        /// <summary>Starts a refinement of <c>string</c>.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Email" |> Refine.format Format.Email
        /// </code></example>
        val ofString: name: string -> Refinement<string,string>
        
        /// <summary>Starts a refinement of <c>int</c>.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofInt "Age" |> Refine.intRange (Some 0) (Some 130)
        /// </code></example>
        val ofInt: name: string -> Refinement<int,int>
        
        /// <summary>Starts a refinement of <c>int64</c>.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofInt64 "Bytes" |> Refine.int64Range (Some 0L) None
        /// </code></example>
        val ofInt64: name: string -> Refinement<int64,int64>
        
        /// <summary>Starts a refinement of <c>decimal</c>.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofDecimal "Price" |> Refine.greaterThan 0m
        /// </code></example>
        val ofDecimal: name: string -> Refinement<decimal,decimal>
        
        /// <summary>Starts a refinement of <c>float</c>.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofFloat "Probability" |> Refine.floatRange (Some 0.0) (Some 1.0)
        /// </code></example>
        val ofFloat: name: string -> Refinement<float,float>
        
        /// <summary>Starts a refinement of a list.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofList "Tags" |> Refine.itemCount (Some 1) (Some 10)
        /// </code></example>
        val ofList: name: string -> Refinement<'T list,'T list>
        
        /// <summary>Renames a refinement.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Text" |> Refine.rename "Slug"
        /// </code></example>
        val rename:
          name: string -> refinement: Refinement<'Raw,'T> -> Refinement<'Raw,'T>
        
        /// <summary>
        /// Adds a rule. Rules added this way all run, so a value breaking several of them
        /// reports several errors rather than just the first.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Zip" |> Refine.check (Check.exactLength 5) |> Refine.check (Check.pattern @"^\d+$")
        /// // "abc" reports both the length and the pattern failure
        /// </code></example>
        val check:
          c: ConstraintCheck<'T> ->
            refinement: Refinement<'Raw,'T> -> Refinement<'Raw,'T>
        
        /// <summary>
        /// Normalises a value before any rule runs, wherever in the pipeline it appears.
        /// Use it for trimming and case-folding, not for anything that can fail.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Code" |> Refine.normalise (fun s -> s.Replace("-", ""))
        /// </code></example>
        val normalise:
          f: ('T -> 'T) ->
            refinement: Refinement<'Raw,'T> -> Refinement<'Raw,'T>
        
        /// <summary>Trims leading and trailing whitespace before any rule runs.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Name" |> Refine.trimmed |> Refine.nonEmpty
        /// // "   " is trimmed to "" and then rejected as empty
        /// </code></example>
        val trimmed:
          refinement: Refinement<'Raw,string> -> Refinement<'Raw,string>
        
        /// <summary>Lower-cases using the invariant culture before any rule runs.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Email" |> Refine.lowercased
        /// </code></example>
        val lowercased:
          refinement: Refinement<'Raw,string> -> Refinement<'Raw,string>
        
        /// <summary>Upper-cases using the invariant culture before any rule runs.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "CountryCode" |> Refine.uppercased
        /// </code></example>
        val uppercased:
          refinement: Refinement<'Raw,string> -> Refinement<'Raw,string>
        
        val private afterChecks:
          refinement: Refinement<'Raw,'T> ->
            convert: (Path -> 'T -> Validation<'U>) ->
            path: Path -> raw: 'Raw -> Result<'U,ValidationErrors>
        
        /// <summary>
        /// Changes the type a refinement produces, typically by wrapping it in a single-case
        /// union so that the constrained type is distinct from its representation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The rules added before <c>wrap</c> run before it. A rule can therefore
        /// guard the conversion that follows it -- a check that a code is known,
        /// then a wrap that looks it up -- and a value that breaks the rule is
        /// refused before the conversion ever sees it. For a conversion that can
        /// refuse on its own, use <c>parse</c>.
        /// </para>
        /// <para>
        /// Those rules also stay in <c>Checks</c>, mapped back through
        /// <c>destruct</c>, because <c>validate</c> runs the checks alone on a value
        /// that already exists. A rule that passed before the conversion passes
        /// again afterwards; the cost is a second evaluation of a predicate on the
        /// happy path.
        /// </para>
        /// </remarks>
        /// <example><code lang="fsharp">
        /// type Email = private Email of string
        /// Refine.ofString "Email"
        /// |> Refine.format Format.Email
        /// |> Refine.wrap Email (fun (Email s) -> s)
        /// </code></example>
        val wrap:
          construct: ('T -> 'U) ->
            destruct: ('U -> 'T) ->
            refinement: Refinement<'Raw,'T> -> Refinement<'Raw,'U>
        
        /// <summary>
        /// Changes the type a refinement produces by a conversion that can refuse.
        /// </summary>
        /// <remarks>
        /// The counterpart to <c>wrap</c> for a parse that is partial: a code that
        /// may not be one of the known ones, a string that may not be a date. A
        /// refusal is a validation error at the value's path carrying the code and
        /// description given here, and the rule is published in the refinement's
        /// constraints like any other. The rules added before it run first.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Role"
        /// |> Refine.parse "known_role" "is not a role" Role.tryParse Role.render
        /// </code></example>
        val parse:
          code: string ->
            description: string ->
            construct: ('T -> 'U option) ->
            destruct: ('U -> 'T) ->
            refinement: Refinement<'Raw,'T> -> Refinement<'Raw,'U>
        
        /// <summary>Requires at least one character.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "NonEmpty" |> Refine.nonEmpty
        /// </code></example>
        val nonEmpty:
          refinement: Refinement<'Raw,string> -> Refinement<'Raw,string>
        
        /// <summary>Bounds the number of characters, inclusive.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Email" |> Refine.length (Some 3) (Some 254)
        /// </code></example>
        val length:
          min: int option ->
            max: int option ->
            refinement: Refinement<'Raw,string> -> Refinement<'Raw,string>
        
        /// <summary>Requires at least this many characters.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Password" |> Refine.minLength 12
        /// </code></example>
        val minLength:
          min: int ->
            refinement: Refinement<'Raw,string> -> Refinement<'Raw,string>
        
        /// <summary>Requires at most this many characters.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Summary" |> Refine.maxLength 280
        /// </code></example>
        val maxLength:
          max: int ->
            refinement: Refinement<'Raw,string> -> Refinement<'Raw,string>
        
        /// <summary>Requires exactly this many characters.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Zip" |> Refine.exactLength 5
        /// </code></example>
        val exactLength:
          n: int ->
            refinement: Refinement<'Raw,string> -> Refinement<'Raw,string>
        
        /// <summary>Requires the string to match a regular expression.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Zip" |> Refine.pattern @"^\d{5}$"
        /// </code></example>
        val pattern:
          regex: string ->
            refinement: Refinement<'Raw,string> -> Refinement<'Raw,string>
        
        /// <summary>Requires the string to be in a named format.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Email" |> Refine.format Format.Email
        /// </code></example>
        val format:
          f: Format ->
            refinement: Refinement<'Raw,string> -> Refinement<'Raw,string>
        
        /// <summary>Requires one of an explicit set of values.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofString "Colour" |> Refine.oneOf [ "red"; "green"; "blue" ]
        /// </code></example>
        val oneOf:
          values: string list ->
            refinement: Refinement<'Raw,string> -> Refinement<'Raw,string>
        
        /// <summary>Bounds an <c>int</c>, inclusive.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofInt "Age" |> Refine.intRange (Some 0) (Some 130)
        /// </code></example>
        val intRange:
          min: int option ->
            max: int option ->
            refinement: Refinement<'Raw,int> -> Refinement<'Raw,int>
        
        /// <summary>Bounds an <c>int64</c>, inclusive.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofInt64 "Bytes" |> Refine.int64Range (Some 0L) None
        /// </code></example>
        val int64Range:
          min: int64 option ->
            max: int64 option ->
            refinement: Refinement<'Raw,int64> -> Refinement<'Raw,int64>
        
        /// <summary>Bounds a <c>decimal</c>, inclusive.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofDecimal "Discount" |> Refine.decimalRange (Some 0m) (Some 1m)
        /// </code></example>
        val decimalRange:
          min: decimal option ->
            max: decimal option ->
            refinement: Refinement<'Raw,decimal> -> Refinement<'Raw,decimal>
        
        /// <summary>Bounds a <c>float</c>, inclusive.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofFloat "Probability" |> Refine.floatRange (Some 0.0) (Some 1.0)
        /// </code></example>
        val floatRange:
          min: float option ->
            max: float option ->
            refinement: Refinement<'Raw,float> -> Refinement<'Raw,float>
        
        /// <summary>Requires a decimal strictly above a floor.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofDecimal "Price" |> Refine.greaterThan 0m // zero is rejected
        /// </code></example>
        val greaterThan:
          min: decimal ->
            refinement: Refinement<'Raw,decimal> -> Refinement<'Raw,decimal>
        
        /// <summary>Requires a decimal strictly below a ceiling.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofDecimal "Ratio" |> Refine.lessThan 1m
        /// </code></example>
        val lessThan:
          max: decimal ->
            refinement: Refinement<'Raw,decimal> -> Refinement<'Raw,decimal>
        
        /// <summary>Bounds the number of elements, inclusive.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofList "Tags" |> Refine.itemCount (Some 1) (Some 10)
        /// </code></example>
        val itemCount:
          min: int option ->
            max: int option ->
            refinement: Refinement<'Raw,'T list> -> Refinement<'Raw,'T list>
        
        /// <summary>Requires every element to differ.</summary>
        /// <example><code lang="fsharp">
        /// Refine.ofList "Tags" |> Refine.distinct
        /// </code></example>
        val distinct:
          refinement: Refinement<'Raw,'T list> -> Refinement<'Raw,'T list>
            when 'T: equality
        
        /// <summary>
        /// Adds a rule Etymon cannot inspect. It is enforced and documented, but never
        /// derived into a schema keyword, a SQL constraint or a generator.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Refine.ofInt "Even" |> Refine.satisfies "even" "must be even" (fun n -> n % 2 = 0)
        /// </code></example>
        val satisfies:
          code: string ->
            description: string ->
            predicate: ('T -> bool) ->
            refinement: Refinement<'Raw,'T> -> Refinement<'Raw,'T>
        
        /// <summary>
        /// Builds a refined value at a given path, so that errors read relative to the
        /// enclosing value.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Refine.createAt (Path.root |> Path.field "email") Email.refinement "nope"
        /// // Error [ "email: must be a valid email" ]
        /// </code></example>
        val createAt:
          path: Path ->
            refinement: Refinement<'Raw,'T> -> raw: 'Raw -> Validation<'T>
        
        /// <summary>Builds a refined value, reporting every rule it breaks.</summary>
        /// <example><code lang="fsharp">
        /// Email.create "nope" // Error [ "must be a valid email" ]
        /// </code></example>
        val create:
          refinement: Refinement<'Raw,'T> -> raw: 'Raw -> Validation<'T>
        
        /// <summary>Recovers the raw value from a refined one. Always succeeds.</summary>
        /// <example><code lang="fsharp">
        /// Refine.extract Email.refinement myEmail // "someone@example.com"
        /// </code></example>
        val extract: refinement: Refinement<'Raw,'T> -> value: 'T -> 'Raw
        
        /// <summary>
        /// Checks an already-built value against the refinement's rules, without converting.
        /// Useful when a value arrived by another route and you want to know it is still valid.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Refine.validate Age.refinement 200 |> Validation.errorCount // 1
        /// </code></example>
        val validate:
          refinement: Refinement<'Raw,'T> -> value: 'T -> Validation<'T>

