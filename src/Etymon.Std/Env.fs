namespace Etymon

open System

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
    let tryGet (name: string) =
        Environment.GetEnvironmentVariable name |> Str.toOption

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
    let tryGetAllowEmpty (name: string) =
        Environment.GetEnvironmentVariable name |> Str.toOptionAllowEmpty

    /// <summary>The value of a variable, or a fallback.</summary>
    /// <example><code lang="fsharp">
    /// Env.getOr "Production" "ASPNETCORE_ENVIRONMENT" // "Development" if set
    /// </code></example>
    let getOr (fallback: string) (name: string) =
        tryGet name |> Option.defaultValue fallback

    /// <summary>
    /// The value of a variable, parsed. <c>None</c> when the variable is absent
    /// or the text does not parse.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Env.tryParse Parse.int "MAX_RETRIES" // Some 3
    /// Env.tryParse Parse.int "PATH" // None -- set, but not a number
    /// </code></example>
    let tryParse (parser: string -> 'T option) (name: string) = tryGet name |> Option.bind parser

    /// <summary>The value of a variable parsed as an <c>int</c>.</summary>
    /// <example><code lang="fsharp">
    /// Env.tryGetInt "PORT" // Some 8080
    /// </code></example>
    let tryGetInt (name: string) = tryParse Parse.int name

    /// <summary>
    /// The value of a variable parsed as a <c>bool</c>, accepting the forms that
    /// occur in practice: <c>1</c>, <c>yes</c>, <c>on</c> and their opposites.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Env.tryGetBool "CI" // Some true when CI=1
    /// </code></example>
    let tryGetBool (name: string) = tryParse Parse.bool name

    /// <summary>
    /// Whether a variable is set to something truthy. The usual shape of a
    /// feature switch: absent means off.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Env.isEnabled "ETYMON_VERBOSE" // false when unset
    /// </code></example>
    let isEnabled (name: string) =
        tryGetBool name |> Option.defaultValue false

    /// <summary>Whether a variable is set to anything non-blank.</summary>
    /// <example><code lang="fsharp">
    /// Env.isSet "NUGET_API_KEY" // true
    /// </code></example>
    let isSet (name: string) = (tryGet name).IsSome

    /// <summary>
    /// Every environment variable, in a case-insensitive dictionary so that
    /// lookups behave the same on every platform.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Env.all () |> Dict.tryFind "path" // Some "..." on any platform
    /// </code></example>
    let all () =
        Environment.GetEnvironmentVariables()
        |> Seq.cast<Collections.DictionaryEntry>
        |> Seq.map (fun entry -> string entry.Key, Str.orEmpty (string entry.Value))
        |> List.ofSeq
        |> Dict.ofListIgnoreCase
