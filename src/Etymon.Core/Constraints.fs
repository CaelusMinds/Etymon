namespace Etymon

open System
open System.Globalization

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

    let private isNaN n =
        match n with
        | Num.Float v -> Double.IsNaN v
        | Num.Int _
        | Num.Dec _ -> false

    let private asFloat n =
        match n with
        | Num.Int v -> float v
        | Num.Dec v -> float v
        | Num.Float v -> v

    let private tryAsDecimal n =
        match n with
        | Num.Int v -> ValueSome(decimal v)
        | Num.Dec v -> ValueSome v
        | Num.Float v ->
            if Double.IsFinite v && abs v < 7.9e28 then
                ValueSome(decimal v)
            else
                ValueNone

    /// <summary>
    /// Orders two numbers, promoting to a representation that can hold both.
    /// Returns <c>ValueNone</c> when either side is NaN, which is unordered.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Num.compare (Num.Int 1L) (Num.Dec 1.5m) // ValueSome -1
    /// </code></example>
    let compare (left: Num) (right: Num) : int voption =
        if isNaN left || isNaN right then
            ValueNone
        else
            match left, right with
            | Num.Int a, Num.Int b -> ValueSome(Operators.compare a b)
            | _ ->
                match tryAsDecimal left, tryAsDecimal right with
                | ValueSome a, ValueSome b -> ValueSome(Operators.compare a b)
                | _ -> ValueSome(Operators.compare (asFloat left) (asFloat right))

    /// <summary>Renders a number the way a human-readable message should show it.</summary>
    /// <example><code lang="fsharp">
    /// Num.toString (Num.Dec 1.50m) // "1.50"
    /// </code></example>
    let toString (n: Num) =
        match n with
        | Num.Int v -> v.ToString(CultureInfo.InvariantCulture)
        | Num.Dec v -> v.ToString(CultureInfo.InvariantCulture)
        | Num.Float v -> v.ToString("R", CultureInfo.InvariantCulture)

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
    let name (format: Format) =
        match format with
        | Format.Email -> "email"
        | Format.Uuid -> "uuid"
        | Format.DateTime -> "date-time"
        | Format.Date -> "date"
        | Format.Time -> "time"
        | Format.Duration -> "duration"
        | Format.Uri -> "uri"
        | Format.UriReference -> "uri-reference"
        | Format.Hostname -> "hostname"
        | Format.Ipv4 -> "ipv4"
        | Format.Ipv6 -> "ipv6"
        | Format.Custom n -> n

/// <summary>
/// A restriction on a value, expressed as data.
/// </summary>
/// <remarks>
/// This is the single constraint vocabulary for the whole suite. <c>Etymon.Schema</c>
/// attaches these to fields; <c>Etymon.Schema.OpenApi</c>, <c>Etymon.Migrations</c> and
/// <c>Etymon.Contracts.FsCheck</c> each read them without ever running the check that
/// produced them. Anything expressible here can be derived into a JSON Schema keyword,
/// a SQL CHECK clause, or a generator; anything that cannot is <c>Opaque</c>, and is
/// documented but never derived.
/// </remarks>
[<RequireQualifiedAccess>]
type Constraint =
    /// Bounds on the number of characters in a string, inclusive.
    | Length of min: int option * max: int option
    /// Bounds on a numeric value.
    | Range of min: Num option * max: Num option * exclusiveMin: bool * exclusiveMax: bool
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

    let private plural count singular =
        if count = 1 then
            $"1 %s{singular}"
        else
            $"%d{count} %s{singular}s"

    /// <summary>
    /// A human-readable statement of what the constraint requires, phrased so that it
    /// reads after a field name: "age must be at least 0".
    /// </summary>
    /// <example><code lang="fsharp">
    /// Constraint.describe (Constraint.Length(Some 3, Some 254))
    /// // "must be between 3 and 254 characters"
    /// </code></example>
    let describe (c: Constraint) =
        match c with
        | Constraint.Length(Some lo, Some hi) when lo = hi -> "must be exactly " + plural lo "character"
        | Constraint.Length(Some lo, Some hi) -> $"must be between %d{lo} and %d{hi} characters"
        | Constraint.Length(Some lo, None) -> "must be at least " + plural lo "character"
        | Constraint.Length(None, Some hi) -> "must be at most " + plural hi "character"
        | Constraint.Length(None, None) -> "must be a string"

        | Constraint.Range(Some lo, Some hi, false, false) ->
            $"must be between %s{Num.toString lo} and %s{Num.toString hi}"
        | Constraint.Range(Some lo, Some hi, exclusiveMin, exclusiveMax) ->
            let loWord = if exclusiveMin then "greater than" else "at least"
            let hiWord = if exclusiveMax then "less than" else "at most"
            $"must be %s{loWord} %s{Num.toString lo} and %s{hiWord} %s{Num.toString hi}"
        | Constraint.Range(Some lo, None, true, _) -> $"must be greater than %s{Num.toString lo}"
        | Constraint.Range(Some lo, None, false, _) -> $"must be at least %s{Num.toString lo}"
        | Constraint.Range(None, Some hi, _, true) -> $"must be less than %s{Num.toString hi}"
        | Constraint.Range(None, Some hi, _, false) -> $"must be at most %s{Num.toString hi}"
        | Constraint.Range(None, None, _, _) -> "must be a number"

        | Constraint.Pattern regex -> $"must match %s{regex}"
        | Constraint.HasFormat format -> "must be a valid " + Format.name format
        | Constraint.OneOf values -> "must be one of: " + String.Join(", ", values)

        | Constraint.Items(Some lo, Some hi, unique) when lo = hi ->
            let suffix = if unique then " with no duplicates" else ""
            "must contain exactly " + plural lo "item" + suffix
        | Constraint.Items(lo, hi, unique) ->
            let bound =
                match lo, hi with
                | Some lo, Some hi -> $"must contain between %d{lo} and %d{hi} items"
                | Some lo, None -> "must contain at least " + plural lo "item"
                | None, Some hi -> "must contain at most " + plural hi "item"
                | None, None -> "must be a collection"

            if unique then bound + " with no duplicates" else bound

        | Constraint.Opaque(_, description) -> description
