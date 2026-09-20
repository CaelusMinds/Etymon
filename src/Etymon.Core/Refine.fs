namespace Etymon

open System

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
type Refinement<'Raw, 'T> =
    {
        /// What the refined type is called, in error messages and generated schemas.
        Name: string
        /// The rules this refinement enforces, as inspectable data.
        Constraints: Constraint list
        /// Normalises and converts a raw value. Failing here stops the checks, because
        /// there is no value left to check.
        Convert: Path -> 'Raw -> Validation<'T>
        /// The checks to run once a value exists. Every one runs; their errors accumulate.
        Checks: (Path -> 'T -> ValidationError list) list
        /// Recovers the raw value. Total, because a refined value is already valid.
        Extract: 'T -> 'Raw
    }

/// Building <see cref="T:Etymon.Refinement`2"/> values and running them.
[<RequireQualifiedAccess>]
module Refine =

    /// <summary>A refinement that accepts anything, ready to have rules added to it.</summary>
    /// <example><code lang="fsharp">
    /// Refine.identity "Anything" : Refinement&lt;int, int&gt;
    /// </code></example>
    let identity (name: string) : Refinement<'T, 'T> =
        {
            Name = name
            Constraints = []
            Convert = fun _ value -> Ok value
            Checks = []
            Extract = id
        }

    /// <summary>Starts a refinement of <c>string</c>.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Email" |> Refine.format Format.Email
    /// </code></example>
    let ofString (name: string) : Refinement<string, string> = identity name

    /// <summary>Starts a refinement of <c>int</c>.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofInt "Age" |> Refine.intRange (Some 0) (Some 130)
    /// </code></example>
    let ofInt (name: string) : Refinement<int, int> = identity name

    /// <summary>Starts a refinement of <c>int64</c>.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofInt64 "Bytes" |> Refine.int64Range (Some 0L) None
    /// </code></example>
    let ofInt64 (name: string) : Refinement<int64, int64> = identity name

    /// <summary>Starts a refinement of <c>decimal</c>.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofDecimal "Price" |> Refine.greaterThan 0m
    /// </code></example>
    let ofDecimal (name: string) : Refinement<decimal, decimal> = identity name

    /// <summary>Starts a refinement of <c>float</c>.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofFloat "Probability" |> Refine.floatRange (Some 0.0) (Some 1.0)
    /// </code></example>
    let ofFloat (name: string) : Refinement<float, float> = identity name

    /// <summary>Starts a refinement of a list.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofList "Tags" |> Refine.itemCount (Some 1) (Some 10)
    /// </code></example>
    let ofList (name: string) : Refinement<'T list, 'T list> = identity name

    /// <summary>Renames a refinement.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Text" |> Refine.rename "Slug"
    /// </code></example>
    let rename (name: string) (refinement: Refinement<'Raw, 'T>) = { refinement with Name = name }

    /// <summary>
    /// Adds a rule. Rules added this way all run, so a value breaking several of them
    /// reports several errors rather than just the first.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Zip" |> Refine.check (Check.exactLength 5) |> Refine.check (Check.pattern @"^\d+$")
    /// // "abc" reports both the length and the pattern failure
    /// </code></example>
    let check (c: ConstraintCheck<'T>) (refinement: Refinement<'Raw, 'T>) =
        let run path value =
            if c.Satisfies value then [] else [ Check.toError c path ]

        { refinement with
            Constraints = refinement.Constraints @ [ c.Constraint ]
            Checks = refinement.Checks @ [ run ]
        }

    /// <summary>
    /// Normalises a value before any rule runs, wherever in the pipeline it appears.
    /// Use it for trimming and case-folding, not for anything that can fail.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Code" |> Refine.normalise (fun s -> s.Replace("-", ""))
    /// </code></example>
    let normalise (f: 'T -> 'T) (refinement: Refinement<'Raw, 'T>) =
        { refinement with
            Convert = fun path raw -> refinement.Convert path raw |> Validation.map f
        }

    /// <summary>Trims leading and trailing whitespace before any rule runs.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Name" |> Refine.trimmed |> Refine.nonEmpty
    /// // "   " is trimmed to "" and then rejected as empty
    /// </code></example>
    let trimmed (refinement: Refinement<'Raw, string>) =
        normalise (fun (s: string) -> s.Trim()) refinement

    /// <summary>Lower-cases using the invariant culture before any rule runs.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Email" |> Refine.lowercased
    /// </code></example>
    let lowercased (refinement: Refinement<'Raw, string>) =
        normalise (fun (s: string) -> s.ToLowerInvariant()) refinement

    /// <summary>Upper-cases using the invariant culture before any rule runs.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "CountryCode" |> Refine.uppercased
    /// </code></example>
    let uppercased (refinement: Refinement<'Raw, string>) =
        normalise (fun (s: string) -> s.ToUpperInvariant()) refinement

    /// <summary>
    /// Changes the type a refinement produces, typically by wrapping it in a single-case
    /// union so that the constrained type is distinct from its representation.
    /// </summary>
    /// <example><code lang="fsharp">
    /// type Email = private Email of string
    /// Refine.ofString "Email"
    /// |> Refine.format Format.Email
    /// |> Refine.wrap Email (fun (Email s) -> s)
    /// </code></example>
    let wrap (construct: 'T -> 'U) (destruct: 'U -> 'T) (refinement: Refinement<'Raw, 'T>) : Refinement<'Raw, 'U> =
        {
            Name = refinement.Name
            Constraints = refinement.Constraints
            Convert = fun path raw -> refinement.Convert path raw |> Validation.map construct
            Checks =
                refinement.Checks
                |> List.map (fun run -> fun path value -> run path (destruct value))
            Extract = destruct >> refinement.Extract
        }

    // ---- string rules --------------------------------------------------------

    /// <summary>Requires at least one character.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "NonEmpty" |> Refine.nonEmpty
    /// </code></example>
    let nonEmpty (refinement: Refinement<'Raw, string>) = check Check.nonEmpty refinement

    /// <summary>Bounds the number of characters, inclusive.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Email" |> Refine.length (Some 3) (Some 254)
    /// </code></example>
    let length (min: int option) (max: int option) (refinement: Refinement<'Raw, string>) =
        check (Check.length min max) refinement

    /// <summary>Requires at least this many characters.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Password" |> Refine.minLength 12
    /// </code></example>
    let minLength (min: int) (refinement: Refinement<'Raw, string>) = check (Check.minLength min) refinement

    /// <summary>Requires at most this many characters.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Summary" |> Refine.maxLength 280
    /// </code></example>
    let maxLength (max: int) (refinement: Refinement<'Raw, string>) = check (Check.maxLength max) refinement

    /// <summary>Requires exactly this many characters.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Zip" |> Refine.exactLength 5
    /// </code></example>
    let exactLength (n: int) (refinement: Refinement<'Raw, string>) = check (Check.exactLength n) refinement

    /// <summary>Requires the string to match a regular expression.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Zip" |> Refine.pattern @"^\d{5}$"
    /// </code></example>
    let pattern (regex: string) (refinement: Refinement<'Raw, string>) = check (Check.pattern regex) refinement

    /// <summary>Requires the string to be in a named format.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Email" |> Refine.format Format.Email
    /// </code></example>
    let format (f: Format) (refinement: Refinement<'Raw, string>) = check (Check.format f) refinement

    /// <summary>Requires one of an explicit set of values.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofString "Colour" |> Refine.oneOf [ "red"; "green"; "blue" ]
    /// </code></example>
    let oneOf (values: string list) (refinement: Refinement<'Raw, string>) = check (Check.oneOf values) refinement

    // ---- numeric rules -------------------------------------------------------

    /// <summary>Bounds an <c>int</c>, inclusive.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofInt "Age" |> Refine.intRange (Some 0) (Some 130)
    /// </code></example>
    let intRange (min: int option) (max: int option) (refinement: Refinement<'Raw, int>) =
        check (Check.intRange min max) refinement

    /// <summary>Bounds an <c>int64</c>, inclusive.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofInt64 "Bytes" |> Refine.int64Range (Some 0L) None
    /// </code></example>
    let int64Range (min: int64 option) (max: int64 option) (refinement: Refinement<'Raw, int64>) =
        check (Check.int64Range min max) refinement

    /// <summary>Bounds a <c>decimal</c>, inclusive.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofDecimal "Discount" |> Refine.decimalRange (Some 0m) (Some 1m)
    /// </code></example>
    let decimalRange (min: decimal option) (max: decimal option) (refinement: Refinement<'Raw, decimal>) =
        check (Check.decimalRange min max) refinement

    /// <summary>Bounds a <c>float</c>, inclusive.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofFloat "Probability" |> Refine.floatRange (Some 0.0) (Some 1.0)
    /// </code></example>
    let floatRange (min: float option) (max: float option) (refinement: Refinement<'Raw, float>) =
        check (Check.floatRange min max) refinement

    /// <summary>Requires a decimal strictly above a floor.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofDecimal "Price" |> Refine.greaterThan 0m // zero is rejected
    /// </code></example>
    let greaterThan (min: decimal) (refinement: Refinement<'Raw, decimal>) =
        check (Check.greaterThan min) refinement

    /// <summary>Requires a decimal strictly below a ceiling.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofDecimal "Ratio" |> Refine.lessThan 1m
    /// </code></example>
    let lessThan (max: decimal) (refinement: Refinement<'Raw, decimal>) = check (Check.lessThan max) refinement

    // ---- collection rules ----------------------------------------------------

    /// <summary>Bounds the number of elements, inclusive.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofList "Tags" |> Refine.itemCount (Some 1) (Some 10)
    /// </code></example>
    let itemCount (min: int option) (max: int option) (refinement: Refinement<'Raw, 'T list>) =
        check (Check.itemCount min max) refinement

    /// <summary>Requires every element to differ.</summary>
    /// <example><code lang="fsharp">
    /// Refine.ofList "Tags" |> Refine.distinct
    /// </code></example>
    let distinct (refinement: Refinement<'Raw, 'T list>) = check Check.distinct refinement

    // ---- escape hatch --------------------------------------------------------

    /// <summary>
    /// Adds a rule Etymon cannot inspect. It is enforced and documented, but never
    /// derived into a schema keyword, a SQL constraint or a generator.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Refine.ofInt "Even" |> Refine.satisfies "even" "must be even" (fun n -> n % 2 = 0)
    /// </code></example>
    let satisfies (code: string) (description: string) (predicate: 'T -> bool) (refinement: Refinement<'Raw, 'T>) =
        check (Check.opaque code description predicate) refinement

    // ---- running -------------------------------------------------------------

    /// <summary>
    /// Builds a refined value at a given path, so that errors read relative to the
    /// enclosing value.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Refine.createAt (Path.root |> Path.field "email") Email.refinement "nope"
    /// // Error [ "email: must be a valid email" ]
    /// </code></example>
    let createAt (path: Path) (refinement: Refinement<'Raw, 'T>) (raw: 'Raw) : Validation<'T> =
        match refinement.Convert path raw with
        | Error e -> Error e
        | Ok value ->
            match refinement.Checks |> List.collect (fun run -> run path value) with
            | [] -> Ok value
            | first :: rest -> Error(ValidationErrors.ofHeadTail first rest)

    /// <summary>Builds a refined value, reporting every rule it breaks.</summary>
    /// <example><code lang="fsharp">
    /// Email.create "nope" // Error [ "must be a valid email" ]
    /// </code></example>
    let create (refinement: Refinement<'Raw, 'T>) (raw: 'Raw) : Validation<'T> = createAt Path.root refinement raw

    /// <summary>Recovers the raw value from a refined one. Always succeeds.</summary>
    /// <example><code lang="fsharp">
    /// Refine.extract Email.refinement myEmail // "someone@example.com"
    /// </code></example>
    let extract (refinement: Refinement<'Raw, 'T>) (value: 'T) = refinement.Extract value

    /// <summary>
    /// Checks an already-built value against the refinement's rules, without converting.
    /// Useful when a value arrived by another route and you want to know it is still valid.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Refine.validate Age.refinement 200 |> Validation.errorCount // 1
    /// </code></example>
    let validate (refinement: Refinement<'Raw, 'T>) (value: 'T) : Validation<'T> =
        match refinement.Checks |> List.collect (fun run -> run Path.root value) with
        | [] -> Ok value
        | first :: rest -> Error(ValidationErrors.ofHeadTail first rest)
