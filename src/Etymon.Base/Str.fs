namespace Etymon

open System

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
    let toOption (value: string) =
        if String.IsNullOrWhiteSpace value then None else Some value

    /// <summary>
    /// Turns a null string into <c>None</c>, keeping the empty string as a value.
    /// Use this when empty genuinely means something different from absent.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Str.toOptionAllowEmpty "" // Some ""
    /// Str.toOptionAllowEmpty null // None
    /// </code></example>
    let toOptionAllowEmpty (value: string) =
        if isNull value then None else Some value

    /// <summary>Replaces a null string with the empty one.</summary>
    /// <example><code lang="fsharp">
    /// Str.orEmpty null // ""
    /// </code></example>
    let orEmpty (value: string) = if isNull value then "" else value

    /// <summary>True when the string is null or has no characters.</summary>
    /// <example><code lang="fsharp">
    /// Str.isEmpty "" // true
    /// Str.isEmpty " " // false
    /// </code></example>
    let isEmpty (value: string) = String.IsNullOrEmpty value

    /// <summary>True when the string is null, empty, or only whitespace.</summary>
    /// <example><code lang="fsharp">
    /// Str.isBlank "  \t " // true
    /// </code></example>
    let isBlank (value: string) = String.IsNullOrWhiteSpace value

    /// <summary>Trims surrounding whitespace, treating null as empty.</summary>
    /// <example><code lang="fsharp">
    /// Str.trim "  hi  " // "hi"
    /// Str.trim null // ""
    /// </code></example>
    let trim (value: string) = (orEmpty value).Trim()

    /// <summary>Lower-cases using the invariant culture.</summary>
    /// <example><code lang="fsharp">
    /// Str.toLower "ABC" // "abc"
    /// </code></example>
    let toLower (value: string) = (orEmpty value).ToLowerInvariant()

    /// <summary>Upper-cases using the invariant culture.</summary>
    /// <example><code lang="fsharp">
    /// Str.toUpper "abc" // "ABC"
    /// </code></example>
    let toUpper (value: string) = (orEmpty value).ToUpperInvariant()

    // ---- comparison ----------------------------------------------------------

    /// <summary>Ordinal equality, ignoring case. The right default for identifiers.</summary>
    /// <example><code lang="fsharp">
    /// Str.equalsIgnoreCase "Content-Type" "content-type" // true
    /// </code></example>
    let equalsIgnoreCase (left: string) (right: string) =
        String.Equals(left, right, StringComparison.OrdinalIgnoreCase)

    /// <summary>Ordinal <c>startsWith</c>.</summary>
    /// <example><code lang="fsharp">
    /// Str.startsWith "Etymon." "Etymon.Core" // true
    /// </code></example>
    let startsWith (prefix: string) (value: string) =
        not (isNull value) && value.StartsWith(prefix, StringComparison.Ordinal)

    /// <summary>Ordinal <c>endsWith</c>.</summary>
    /// <example><code lang="fsharp">
    /// Str.endsWith ".fs" "Parse.fs" // true
    /// </code></example>
    let endsWith (suffix: string) (value: string) =
        not (isNull value) && value.EndsWith(suffix, StringComparison.Ordinal)

    /// <summary>Ordinal <c>contains</c>.</summary>
    /// <example><code lang="fsharp">
    /// Str.contains "@" "a@b.com" // true
    /// </code></example>
    let contains (needle: string) (value: string) =
        not (isNull value) && value.Contains(needle, StringComparison.Ordinal)

    // ---- slicing -------------------------------------------------------------

    /// <summary>
    /// Splits on a separator, discarding empty entries and trimming each part.
    /// The shape a comma-separated config value almost always wants.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Str.split ',' "a, b ,, c" // [ "a"; "b"; "c" ]
    /// </code></example>
    let split (separator: char) (value: string) =
        (orEmpty value).Split(separator, StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> List.ofArray

    /// <summary>Splits on a separator, keeping every part exactly as it was.</summary>
    /// <example><code lang="fsharp">
    /// Str.splitRaw ',' "a,,b" // [ "a"; ""; "b" ]
    /// </code></example>
    let splitRaw (separator: char) (value: string) =
        (orEmpty value).Split(separator) |> List.ofArray

    /// <summary>
    /// Splits once at the first occurrence of a separator, into what came before
    /// and what came after. <c>None</c> when the separator is absent.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Str.splitFirst '=' "KEY=a=b" // Some ("KEY", "a=b")
    /// Str.splitFirst '=' "KEY" // None
    /// </code></example>
    let splitFirst (separator: char) (value: string) =
        match orEmpty value with
        | text ->
            match text.IndexOf separator with
            | -1 -> None
            | at -> Some(text.Substring(0, at), text.Substring(at + 1))

    /// <summary>Joins parts with a separator.</summary>
    /// <example><code lang="fsharp">
    /// Str.join ", " [ "a"; "b" ] // "a, b"
    /// </code></example>
    let join (separator: string) (parts: string seq) = String.Join(separator, parts)

    /// <summary>
    /// Shortens a string to at most <paramref name="maxLength"/> characters,
    /// ending with an ellipsis when it had to cut. The ellipsis counts toward the
    /// limit, so the result is never longer than asked for.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Str.truncate 8 "a long sentence" // "a long …"
    /// Str.truncate 8 "short" // "short"
    /// </code></example>
    let truncate (maxLength: int) (value: string) =
        let text = orEmpty value

        if maxLength <= 0 then ""
        elif text.Length <= maxLength then text
        elif maxLength = 1 then "…"
        else text.Substring(0, maxLength - 1).TrimEnd() + "…"

    /// <summary>Removes a prefix if it is there, and does nothing if it is not.</summary>
    /// <example><code lang="fsharp">
    /// Str.stripPrefix "Etymon." "Etymon.Core" // "Core"
    /// Str.stripPrefix "Etymon." "FsCheck" // "FsCheck"
    /// </code></example>
    let stripPrefix (prefix: string) (value: string) =
        let text = orEmpty value

        if startsWith prefix text then
            text.Substring(prefix.Length)
        else
            text

    /// <summary>Removes a suffix if it is there, and does nothing if it is not.</summary>
    /// <example><code lang="fsharp">
    /// Str.stripSuffix ".fs" "Parse.fs" // "Parse"
    /// </code></example>
    let stripSuffix (suffix: string) (value: string) =
        let text = orEmpty value

        if endsWith suffix text && suffix.Length > 0 then
            text.Substring(0, text.Length - suffix.Length)
        else
            text
