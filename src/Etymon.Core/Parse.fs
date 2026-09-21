namespace Etymon

open System
open System.Globalization

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

    let private inv = CultureInfo.InvariantCulture

    let private toOption (succeeded: bool, value: 'T) = if succeeded then Some value else None

    // NumberStyles.Number and NumberStyles.Float both include AllowThousands,
    // which is exactly wrong for a value that has no culture: under the invariant
    // culture the group separator is a comma, so "1,5" parses happily as 15. That
    // is the silent order-of-magnitude bug this module exists to prevent, so the
    // invariant parsers spell their styles out and leave grouping to the
    // culture-aware `In` variants, where a human typed the thousands separator on
    // purpose.
    let private invariantDecimalStyles =
        NumberStyles.AllowLeadingWhite
        ||| NumberStyles.AllowTrailingWhite
        ||| NumberStyles.AllowLeadingSign
        ||| NumberStyles.AllowDecimalPoint

    let private invariantFloatStyles =
        invariantDecimalStyles ||| NumberStyles.AllowExponent

    // ---- integers ------------------------------------------------------------

    /// <summary>Parses an <c>int</c> using the invariant culture.</summary>
    /// <example><code lang="fsharp">
    /// Parse.int "42" // Some 42
    /// Parse.int "4.2" // None
    /// </code></example>
    let int (value: string) =
        Int32.TryParse(value, NumberStyles.Integer, inv) |> toOption

    /// <summary>Parses an <c>int</c> using a given culture.</summary>
    /// <example><code lang="fsharp">
    /// Parse.intIn (CultureInfo.GetCultureInfo "de-DE") "1.234" // Some 1234
    /// </code></example>
    let intIn (culture: CultureInfo) (value: string) =
        Int32.TryParse(value, NumberStyles.Integer ||| NumberStyles.AllowThousands, culture)
        |> toOption

    /// <summary>Parses an <c>int64</c> using the invariant culture.</summary>
    /// <example><code lang="fsharp">
    /// Parse.int64 "9007199254740993" // Some 9007199254740993L
    /// </code></example>
    let int64 (value: string) =
        Int64.TryParse(value, NumberStyles.Integer, inv) |> toOption

    /// <summary>Parses an <c>int64</c> using a given culture.</summary>
    /// <example><code lang="fsharp">
    /// Parse.int64In CultureInfo.CurrentCulture "1,000" // Some 1000L in en-US
    /// </code></example>
    let int64In (culture: CultureInfo) (value: string) =
        Int64.TryParse(value, NumberStyles.Integer ||| NumberStyles.AllowThousands, culture)
        |> toOption

    /// <summary>Parses a <c>byte</c> using the invariant culture.</summary>
    /// <example><code lang="fsharp">
    /// Parse.byte "255" // Some 255uy
    /// Parse.byte "256" // None
    /// </code></example>
    let byte (value: string) =
        Byte.TryParse(value, NumberStyles.Integer, inv) |> toOption

    /// <summary>
    /// Parses a <c>byte</c> in a given culture, accepting that culture's grouping.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Parse.byteIn (CultureInfo "de-DE") "255" // Some 255uy
    /// </code></example>
    let byteIn (culture: CultureInfo) (value: string) =
        Byte.TryParse(value, NumberStyles.Integer ||| NumberStyles.AllowThousands, culture)
        |> toOption

    // ---- reals ---------------------------------------------------------------

    /// <summary>
    /// Parses a <c>decimal</c> using the invariant culture. Prefer this to
    /// <see cref="M:Etymon.Parse.float"/> for money: a decimal keeps its scale and
    /// does not round in binary.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Parse.decimal "1.50" // Some 1.50m
    /// Parse.decimal "1,50" // None -- that is a comma, not a decimal point
    /// </code></example>
    let decimal (value: string) =
        Decimal.TryParse(value, invariantDecimalStyles, inv) |> toOption

    /// <summary>Parses a <c>decimal</c> using a given culture.</summary>
    /// <example><code lang="fsharp">
    /// Parse.decimalIn (CultureInfo.GetCultureInfo "de-DE") "1,50" // Some 1.50m
    /// </code></example>
    let decimalIn (culture: CultureInfo) (value: string) =
        Decimal.TryParse(value, NumberStyles.Number, culture) |> toOption

    /// <summary>
    /// Parses a <c>float</c> using the invariant culture. Accepts exponents;
    /// rejects <c>NaN</c> and the infinities, which are almost never what a text
    /// field meant.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Parse.float "1.5e3" // Some 1500.0
    /// Parse.float "NaN" // None
    /// </code></example>
    let float (value: string) =
        match Double.TryParse(value, invariantFloatStyles, inv) with
        | true, parsed when Double.IsFinite parsed -> Some parsed
        | _ -> None

    /// <summary>Parses a <c>float</c> using a given culture.</summary>
    /// <example><code lang="fsharp">
    /// Parse.floatIn (CultureInfo.GetCultureInfo "fr-FR") "1,5" // Some 1.5
    /// </code></example>
    let floatIn (culture: CultureInfo) (value: string) =
        match Double.TryParse(value, NumberStyles.Float ||| NumberStyles.AllowThousands, culture) with
        | true, parsed when Double.IsFinite parsed -> Some parsed
        | _ -> None

    // ---- booleans ------------------------------------------------------------

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
    let bool (value: string) =
        if isNull value then
            None
        else
            match value.Trim().ToLowerInvariant() with
            | "true"
            | "yes"
            | "y"
            | "on"
            | "1" -> Some true
            | "false"
            | "no"
            | "n"
            | "off"
            | "0" -> Some false
            | _ -> None

    // ---- identifiers ---------------------------------------------------------

    /// <summary>
    /// Parses a <c>Guid</c> in any of the formats <c>Guid.TryParse</c> accepts,
    /// including braced and hyphenless.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Parse.guid "f81d4fae-7dec-11d0-a765-00a0c91e6bf6" // Some ...
    /// </code></example>
    let guid (value: string) = Guid.TryParse value |> toOption

    /// <summary>
    /// Parses a <c>Guid</c> in canonical 8-4-4-4-12 form only. Use this at a
    /// boundary where the shape is part of the contract.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Parse.guidExact "{f81d4fae-7dec-11d0-a765-00a0c91e6bf6}" // None -- braced
    /// </code></example>
    let guidExact (value: string) =
        Guid.TryParseExact(value, "D") |> toOption

    // ---- dates and times -----------------------------------------------------

    /// <summary>
    /// Parses a <c>DateTimeOffset</c> from an RFC 3339 / ISO 8601 timestamp,
    /// which is the only form a wire value should be in.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Parse.dateTimeOffset "2026-09-20T14:30:00Z" // Some ...
    /// Parse.dateTimeOffset "20/09/2026" // None
    /// </code></example>
    let dateTimeOffset (value: string) =
        let formats = [| "yyyy-MM-ddTHH:mm:ssK"; "yyyy-MM-ddTHH:mm:ss.FFFFFFFK" |]

        DateTimeOffset.TryParseExact(value, formats, inv, DateTimeStyles.None)
        |> toOption

    /// <summary>
    /// Parses a <c>DateTimeOffset</c> using a given culture and the formats that
    /// culture considers ordinary. For text a person typed.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Parse.dateTimeOffsetIn (CultureInfo.GetCultureInfo "en-GB") "20/09/2026" // Some ...
    /// </code></example>
    let dateTimeOffsetIn (culture: CultureInfo) (value: string) =
        DateTimeOffset.TryParse(value, culture, DateTimeStyles.None) |> toOption

    /// <summary>
    /// Parses a <c>DateTime</c> from an RFC 3339 timestamp, preserving the offset
    /// information as <c>Utc</c> or <c>Local</c> rather than silently dropping it.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Parse.dateTime "2026-09-20T14:30:00Z" // Some (Kind = Utc)
    /// </code></example>
    let dateTime (value: string) =
        let formats = [| "yyyy-MM-ddTHH:mm:ssK"; "yyyy-MM-ddTHH:mm:ss.FFFFFFFK" |]

        DateTime.TryParseExact(value, formats, inv, DateTimeStyles.RoundtripKind)
        |> toOption

    /// <summary>Parses a <c>DateTime</c> using a given culture.</summary>
    /// <example><code lang="fsharp">
    /// Parse.dateTimeIn (CultureInfo.GetCultureInfo "en-GB") "20/09/2026 14:30" // Some ...
    /// </code></example>
    let dateTimeIn (culture: CultureInfo) (value: string) =
        DateTime.TryParse(value, culture, DateTimeStyles.None) |> toOption

    /// <summary>Parses a <c>DateOnly</c> in ISO 8601 <c>yyyy-MM-dd</c> form.</summary>
    /// <example><code lang="fsharp">
    /// Parse.dateOnly "2026-09-20" // Some ...
    /// Parse.dateOnly "2026-9-20" // None -- ISO 8601 pads
    /// </code></example>
    let dateOnly (value: string) =
        DateOnly.TryParseExact(value, "yyyy-MM-dd", inv, DateTimeStyles.None)
        |> toOption

    /// <summary>Parses a <c>DateOnly</c> using a given culture.</summary>
    /// <example><code lang="fsharp">
    /// Parse.dateOnlyIn (CultureInfo.GetCultureInfo "en-GB") "20/09/2026" // Some ...
    /// </code></example>
    let dateOnlyIn (culture: CultureInfo) (value: string) =
        DateOnly.TryParse(value, culture, DateTimeStyles.None) |> toOption

    /// <summary>Parses a <c>TimeOnly</c> in ISO 8601 <c>HH:mm:ss</c> form.</summary>
    /// <example><code lang="fsharp">
    /// Parse.timeOnly "14:30:00" // Some ...
    /// </code></example>
    let timeOnly (value: string) =
        let formats = [| "HH:mm:ss"; "HH:mm:ss.FFFFFFF"; "HH:mm" |]
        TimeOnly.TryParseExact(value, formats, inv, DateTimeStyles.None) |> toOption

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
    let timeOnlyIn (culture: CultureInfo) (value: string) =
        TimeOnly.TryParse(value, culture, DateTimeStyles.None) |> toOption

    /// <summary>
    /// Parses a <c>TimeSpan</c> in .NET's <c>[d.]hh:mm:ss[.fffffff]</c> form using
    /// the invariant culture.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Parse.timeSpan "1.02:03:04" // Some (1 day, 2 hours, 3 minutes, 4 seconds)
    /// </code></example>
    let timeSpan (value: string) =
        TimeSpan.TryParse(value, inv) |> toOption

    /// <summary>
    /// Parses a <c>TimeSpan</c> in a given culture, which decides the separator
    /// between the seconds and the fraction.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Parse.timeSpanIn (CultureInfo "de-DE") "1.02:03:04,5"
    /// </code></example>
    let timeSpanIn (culture: CultureInfo) (value: string) =
        TimeSpan.TryParse(value, culture) |> toOption

    // ---- enums ---------------------------------------------------------------

    /// <summary>
    /// Parses an enum by name, ignoring case, and rejects a numeric value that
    /// does not correspond to a declared member. <c>Enum.TryParse</c> accepts any
    /// number at all, which is how an undefined enum value gets into a domain.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Parse.enum&lt;DayOfWeek&gt; "monday" // Some DayOfWeek.Monday
    /// Parse.enum&lt;DayOfWeek&gt; "99" // None
    /// </code></example>
    let enum<'T when 'T: struct and 'T :> Enum and 'T: (new: unit -> 'T)> (value: string) =
        match Enum.TryParse<'T>(value, true) with
        | true, parsed when Enum.IsDefined(typeof<'T>, parsed) -> Some parsed
        | _ -> None
