namespace Etymon

open System
open System.Collections.Concurrent
open System.Globalization
open System.Net
open System.Net.Sockets
open System.Text.RegularExpressions

/// <summary>
/// A <see cref="T:Etymon.Constraint"/> paired with the test that decides it.
/// </summary>
/// <remarks>
/// The <c>Constraint</c> is data and the <c>Satisfies</c> function is behaviour. Every
/// package downstream of Core reads the data and ignores the behaviour:
/// <c>Etymon.Schema.OpenApi</c> turns it into a JSON Schema keyword,
/// <c>Etymon.Migrations</c> into a SQL constraint, <c>Etymon.Invariants.FsCheck</c> into a
/// generator. Keeping them in one value is what stops the two drifting apart.
/// </remarks>
[<NoEquality; NoComparison>]
type ConstraintCheck<'T> =
    {
        /// What this check requires, as inspectable data.
        Constraint: Constraint
        /// Whether a value meets it.
        Satisfies: 'T -> bool
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

    // Cached because a refinement is built once and run many times. Given a
    // one-second timeout because patterns usually come from whoever defined the
    // type rather than from Etymon, and a pathological pattern should fail the
    // value rather than hang the process.
    //
    // Worth knowing if you are counting bytes for a WebAssembly payload:
    // System.Text.RegularExpressions (~330 KB trimmed) is retained by any F#
    // application, with or without Etymon -- FSharp.Core's printf and structural
    // formatting reach it. Measured with a hello-world control, so moving these
    // bindings around to try to shed it achieves nothing.
    let private regexCache = ConcurrentDictionary<string, Regex>()

    let private regexFor (pattern: string) =
        regexCache.GetOrAdd(pattern, (fun p -> Regex(p, RegexOptions.CultureInvariant, TimeSpan.FromSeconds 1.0)))

    // ---- format checks -------------------------------------------------------

    let private hostnameRegex =
        Regex(
            @"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds 1.0
        )

    let private isHostname (value: string) =
        value.Length >= 1 && value.Length <= 253 && hostnameRegex.IsMatch value

    // Deliberately hand-written rather than delegating to System.Net.Mail.MailAddress.
    // Core has to be able to go all the way down into a WebAssembly payload, and an
    // assembly reference for one predicate is not a trade worth making. This accepts
    // the addresses that actually occur and rejects the shapes that actually break
    // things; it does not implement RFC 5322 in full, and no validator should.
    let private isEmail (value: string) =
        if String.IsNullOrEmpty value || value.Length > 254 then
            false
        else
            let at = value.LastIndexOf '@'

            if at <= 0 || at = value.Length - 1 then
                false
            else
                let local = value.Substring(0, at)
                let domain = value.Substring(at + 1)

                let localIsSane =
                    local.Length <= 64
                    && not (local.StartsWith '.')
                    && not (local.EndsWith '.')
                    && not (local.Contains "..")
                    && local
                       |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || "!#$%&'*+/=?^_`{|}~-.".Contains c)

                localIsSane && domain.Contains '.' && isHostname domain

    let private isUuid (value: string) =
        match Guid.TryParseExact(value, "D") with
        | true, _ -> true
        | false, _ -> false

    let private isRfc3339 (value: string) =
        let formats = [| "yyyy-MM-ddTHH:mm:ssK"; "yyyy-MM-ddTHH:mm:ss.FFFFFFFK" |]

        match DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, _ -> true
        | false, _ -> false

    let private isDate (value: string) =
        match DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, _ -> true
        | false, _ -> false

    let private isTime (value: string) =
        let formats = [| "HH:mm:ss"; "HH:mm:ss.FFFFFFF" |]

        match TimeOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, _ -> true
        | false, _ -> false

    // Hand-written for the same reason as isEmail: the only ISO 8601 duration parser in
    // the base library is System.Xml.XmlConvert, and System.Private.Xml is megabytes of
    // payload to validate a string like "P3DT4H".
    //
    // Accepts an optional sign, then P, then components in descending order (Y M W D,
    // then T and H M S), each a run of digits with an optional fraction on seconds.
    // At least one component is required, and a T must be followed by one.
    let private isDuration (value: string) =
        let n = value.Length
        let start = if n > 0 && value[0] = '-' then 1 else 0

        if start >= n || value[start] <> 'P' then
            false
        else
            let dateUnits = [| 'Y'; 'M'; 'W'; 'D' |]
            let timeUnits = [| 'H'; 'M'; 'S' |]

            let mutable i = start + 1
            let mutable ok = true
            let mutable components = 0
            let mutable timeComponents = 0
            let mutable inTime = false
            let mutable nextUnit = 0

            while ok && i < n do
                if value[i] = 'T' then
                    if inTime then
                        ok <- false
                    else
                        inTime <- true
                        nextUnit <- 0
                        i <- i + 1
                else
                    let digitsStart = i

                    while i < n && Char.IsAsciiDigit value[i] do
                        i <- i + 1

                    if i = digitsStart then
                        ok <- false
                    else
                        let mutable hadFraction = false

                        if i < n && (value[i] = '.' || value[i] = ',') then
                            hadFraction <- true
                            i <- i + 1
                            let fractionStart = i

                            while i < n && Char.IsAsciiDigit value[i] do
                                i <- i + 1

                            if i = fractionStart then
                                ok <- false

                        if ok then
                            if i >= n then
                                ok <- false
                            else
                                let unit = value[i]
                                let alphabet = if inTime then timeUnits else dateUnits

                                match Array.IndexOf(alphabet, unit) with
                                | -1 -> ok <- false
                                | position when position < nextUnit -> ok <- false
                                | position ->
                                    // A fraction is only meaningful on the smallest unit.
                                    if hadFraction && not (inTime && unit = 'S') then
                                        ok <- false

                                    nextUnit <- position + 1
                                    components <- components + 1

                                    if inTime then
                                        timeComponents <- timeComponents + 1

                                    i <- i + 1

            ok && components > 0 && (not inTime || timeComponents > 0)

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
    let private isAbsoluteUri (value: string) =
        match Uri.TryCreate(value, UriKind.Absolute) with
        | true, uri ->
            // The scheme the parser settled on must be the scheme the string
            // actually names. That is what separates a URI from a file path the
            // parser was willing to adopt: "/relative" names none and becomes
            // file on Unix, and "C:\Windows" names "C" -- a syntactically legal
            // scheme -- but becomes file on Windows. Both are rejected, so the
            // same value validates the same way on every machine.
            let delimiter = value.IndexOf ':'

            delimiter > 0
            && String.Equals(value.Substring(0, delimiter), uri.Scheme, StringComparison.OrdinalIgnoreCase)
        | false, _ -> false

    let private isUriReference (value: string) =
        Uri.TryCreate(value, UriKind.RelativeOrAbsolute) |> fst

    let private isIp (family: AddressFamily) (value: string) =
        match IPAddress.TryParse value with
        | true, address ->
            address.AddressFamily = family
            // IPAddress.TryParse accepts shorthand such as "1.2.3"; JSON Schema ipv4
            // means a dotted quad.
            && (family <> AddressFamily.InterNetwork || value.Split('.').Length = 4)
        | false, _ -> false

    let private satisfiesFormat (format: Format) (value: string) =
        match format with
        | Format.Email -> isEmail value
        | Format.Uuid -> isUuid value
        | Format.DateTime -> isRfc3339 value
        | Format.Date -> isDate value
        | Format.Time -> isTime value
        | Format.Duration -> isDuration value
        | Format.Uri -> isAbsoluteUri value
        | Format.UriReference -> isUriReference value
        | Format.Hostname -> isHostname value
        | Format.Ipv4 -> isIp AddressFamily.InterNetwork value
        | Format.Ipv6 -> isIp AddressFamily.InterNetworkV6 value
        // A format Etymon does not understand is documentation, not enforcement.
        | Format.Custom _ -> true

    // ---- construction --------------------------------------------------------

    /// <summary>Pairs a constraint with an arbitrary test. Prefer the named helpers below.</summary>
    /// <example><code lang="fsharp">
    /// Check.make (Constraint.Pattern "^A") (fun (s: string) -> s.StartsWith "A")
    /// </code></example>
    let make (c: Constraint) (satisfies: 'T -> bool) : ConstraintCheck<'T> =
        {
            Constraint = c
            Satisfies = satisfies
            Code = None
            Message = None
        }

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
    let withCode (code: string) (check: ConstraintCheck<'T>) = { check with Code = Some code }

    /// <summary>Replaces the wording a failed check reports.</summary>
    /// <remarks>
    /// The constraint's own description says what the rule is; this says what it
    /// means here. "must be at least 1 item" is correct and tells a caller
    /// nothing about bills.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Check.minLength 1 |> Check.saying "A bill needs at least one line."
    /// </code></example>
    let saying (message: string) (check: ConstraintCheck<'T>) = { check with Message = Some message }

    /// <summary>Bounds the number of characters in a string, inclusive.</summary>
    /// <example><code lang="fsharp">
    /// Check.length (Some 3) (Some 254) // "ab" fails, "abc" passes
    /// </code></example>
    let length (min: int option) (max: int option) : ConstraintCheck<string> =
        make
            (Constraint.Length(min, max))
            (fun value ->
                let n = value.Length
                let atLeast = min |> Option.forall (fun lo -> n >= lo)
                let atMost = max |> Option.forall (fun hi -> n <= hi)
                atLeast && atMost
            )

    /// <summary>Requires at least this many characters.</summary>
    /// <example><code lang="fsharp">
    /// Check.minLength 1 // rejects ""
    /// </code></example>
    let minLength (min: int) : ConstraintCheck<string> = length (Some min) None

    /// <summary>Requires at most this many characters.</summary>
    /// <example><code lang="fsharp">
    /// Check.maxLength 140 // rejects a 141-character string
    /// </code></example>
    let maxLength (max: int) : ConstraintCheck<string> = length None (Some max)

    /// <summary>Requires exactly this many characters.</summary>
    /// <example><code lang="fsharp">
    /// Check.exactLength 5 // a US zip code
    /// </code></example>
    let exactLength (n: int) : ConstraintCheck<string> = length (Some n) (Some n)

    /// <summary>Requires a string with at least one character.</summary>
    /// <example><code lang="fsharp">
    /// Check.nonEmpty.Satisfies "" // false
    /// </code></example>
    let nonEmpty: ConstraintCheck<string> = minLength 1

    /// <summary>
    /// Requires the whole string to match a .NET regular expression. Anchor it yourself;
    /// Etymon does not add anchors, so that the pattern carried into generated schemas is
    /// exactly the one you wrote.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Check.pattern @"^\d{5}$" // a US zip code
    /// </code></example>
    let pattern (regex: string) : ConstraintCheck<string> =
        let compiled = regexFor regex

        make
            (Constraint.Pattern regex)
            (fun value ->
                try
                    compiled.IsMatch value
                with :? RegexMatchTimeoutException ->
                    false
            )

    /// <summary>Requires a string in a named format.</summary>
    /// <example><code lang="fsharp">
    /// Check.format Format.Email // "a@b" fails, "a@b.com" passes
    /// </code></example>
    let format (f: Format) : ConstraintCheck<string> =
        make (Constraint.HasFormat f) (satisfiesFormat f)

    /// <summary>Requires one of an explicit set of values.</summary>
    /// <example><code lang="fsharp">
    /// Check.oneOf [ "red"; "green"; "blue" ]
    /// </code></example>
    let oneOf (values: string list) : ConstraintCheck<string> =
        let set = Set.ofList values
        make (Constraint.OneOf values) (fun value -> Set.contains value set)

    let private numRange toNum min max exclusiveMin exclusiveMax : ConstraintCheck<'T> =
        let constraintData =
            Constraint.Range(Option.map toNum min, Option.map toNum max, exclusiveMin, exclusiveMax)

        make
            constraintData
            (fun value ->
                let n = toNum value

                let aboveFloor =
                    min
                    |> Option.forall (fun lo ->
                        match Num.compare n (toNum lo) with
                        | ValueSome c -> if exclusiveMin then c > 0 else c >= 0
                        | ValueNone -> false
                    )

                let belowCeiling =
                    max
                    |> Option.forall (fun hi ->
                        match Num.compare n (toNum hi) with
                        | ValueSome c -> if exclusiveMax then c < 0 else c <= 0
                        | ValueNone -> false
                    )

                aboveFloor && belowCeiling
            )

    /// <summary>Bounds an <c>int</c>, inclusive at both ends.</summary>
    /// <example><code lang="fsharp">
    /// Check.intRange (Some 0) (Some 130) // an age
    /// </code></example>
    let intRange (min: int option) (max: int option) : ConstraintCheck<int> =
        numRange (fun (v: int) -> Num.Int(int64 v)) min max false false

    /// <summary>Bounds an <c>int64</c>, inclusive at both ends.</summary>
    /// <example><code lang="fsharp">
    /// Check.int64Range (Some 0L) None
    /// </code></example>
    let int64Range (min: int64 option) (max: int64 option) : ConstraintCheck<int64> =
        numRange Num.Int min max false false

    /// <summary>Bounds a <c>decimal</c>, inclusive at both ends.</summary>
    /// <example><code lang="fsharp">
    /// Check.decimalRange (Some 0m) None // a price
    /// </code></example>
    let decimalRange (min: decimal option) (max: decimal option) : ConstraintCheck<decimal> =
        numRange Num.Dec min max false false

    /// <summary>Bounds a <c>float</c>, inclusive at both ends. NaN never satisfies a bound.</summary>
    /// <example><code lang="fsharp">
    /// Check.floatRange (Some 0.0) (Some 1.0) // a probability
    /// </code></example>
    let floatRange (min: float option) (max: float option) : ConstraintCheck<float> =
        numRange Num.Float min max false false

    /// <summary>Requires a number strictly above a floor.</summary>
    /// <example><code lang="fsharp">
    /// Check.greaterThan 0m // a positive price, zero excluded
    /// </code></example>
    let greaterThan (min: decimal) : ConstraintCheck<decimal> =
        numRange Num.Dec (Some min) None true false

    /// <summary>Requires a number strictly below a ceiling.</summary>
    /// <example><code lang="fsharp">
    /// Check.lessThan 1.0m
    /// </code></example>
    let lessThan (max: decimal) : ConstraintCheck<decimal> =
        numRange Num.Dec None (Some max) false true

    /// <summary>Bounds the number of elements in a list, inclusive.</summary>
    /// <example><code lang="fsharp">
    /// Check.itemCount (Some 1) None // a list that may not be empty
    /// </code></example>
    let itemCount (min: int option) (max: int option) : ConstraintCheck<'T list> =
        make
            (Constraint.Items(min, max, false))
            (fun items ->
                let n = List.length items
                let atLeast = min |> Option.forall (fun lo -> n >= lo)
                let atMost = max |> Option.forall (fun hi -> n <= hi)
                atLeast && atMost
            )

    /// <summary>Requires every element of a list to differ.</summary>
    /// <example><code lang="fsharp">
    /// Check.distinct.Satisfies [ 1; 1 ] // false
    /// </code></example>
    let distinct<'T when 'T: equality> : ConstraintCheck<'T list> =
        make (Constraint.Items(None, None, true)) (fun items -> List.length (List.distinct items) = List.length items)

    /// <summary>
    /// A rule Etymon cannot inspect. It is enforced, carried into error messages and
    /// shown in generated documentation, but never turned into a schema keyword, a SQL
    /// constraint or a generator.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Check.opaque "even" "must be even" (fun n -> n % 2 = 0)
    /// </code></example>
    let opaque (code: string) (description: string) (satisfies: 'T -> bool) : ConstraintCheck<'T> =
        make (Constraint.Opaque(code, description)) satisfies

    // ---- running -------------------------------------------------------------

    /// <summary>The error a failed check produces at a given path.</summary>
    /// <example><code lang="fsharp">
    /// (Check.toError (Check.minLength 1) Path.root).Message // "must be at least 1 character"
    /// </code></example>
    let toError (check: ConstraintCheck<'T>) (path: Path) : ValidationError =
        {
            Path = path
            Reason =
                match check.Code with
                | Some code -> ErrorReason.Rejected code
                | None -> ErrorReason.ConstraintFailed check.Constraint
            Message =
                match check.Message with
                | Some message -> message
                | None -> Constraint.describe check.Constraint
        }

    /// <summary>Runs a check, returning the value unchanged or the error it produced.</summary>
    /// <example><code lang="fsharp">
    /// Check.apply (Check.minLength 1) Path.root "" |> Validation.errorCount // 1
    /// </code></example>
    let apply (check: ConstraintCheck<'T>) (path: Path) (value: 'T) : Validation<'T> =
        if check.Satisfies value then
            Ok value
        else
            Error(ValidationErrors.one (toError check path))
