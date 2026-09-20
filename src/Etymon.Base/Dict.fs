namespace Etymon

open System.Collections.Generic

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
    let tryFind (key: 'K) (source: IDictionary<'K, 'V>) =
        match source.TryGetValue key with
        | true, value -> Some value
        | false, _ -> None

    /// <summary>Looks up a key in a read-only dictionary.</summary>
    /// <example><code lang="fsharp">
    /// Dict.tryFindReadOnly "accept" (headers :> IReadOnlyDictionary&lt;_, _&gt;)
    /// </code></example>
    let tryFindReadOnly (key: 'K) (source: IReadOnlyDictionary<'K, 'V>) =
        match source.TryGetValue key with
        | true, value -> Some value
        | false, _ -> None

    /// <summary>The value for a key, or a fallback when it is absent.</summary>
    /// <example><code lang="fsharp">
    /// Dict.findOr "application/json" "accept" headers
    /// </code></example>
    let findOr (fallback: 'V) (key: 'K) (source: IDictionary<'K, 'V>) =
        match source.TryGetValue key with
        | true, value -> value
        | false, _ -> fallback

    /// <summary>
    /// Looks up a key and transforms the value, in one step. <c>None</c> when the
    /// key is absent or the transformation rejects the value.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Dict.tryPick Parse.int "retries" settings // None if absent or not a number
    /// </code></example>
    let tryPick (chooser: 'V -> 'U option) (key: 'K) (source: IDictionary<'K, 'V>) =
        match source.TryGetValue key with
        | true, value -> chooser value
        | false, _ -> None

    /// <summary>Whether a key is present.</summary>
    /// <example><code lang="fsharp">
    /// Dict.containsKey "accept" headers // true
    /// </code></example>
    let containsKey (key: 'K) (source: IDictionary<'K, 'V>) = source.ContainsKey key

    /// <summary>The keys, as a list.</summary>
    /// <example><code lang="fsharp">
    /// Dict.keys headers // [ "accept" ]
    /// </code></example>
    let keys (source: IDictionary<'K, 'V>) = List.ofSeq source.Keys

    /// <summary>The values, as a list.</summary>
    /// <example><code lang="fsharp">
    /// Dict.values headers // [ "application/json" ]
    /// </code></example>
    let values (source: IDictionary<'K, 'V>) = List.ofSeq source.Values

    /// <summary>
    /// A mutable <c>Dictionary</c> from key-value pairs. Useful at the boundary
    /// where a .NET API wants one and you have F# data.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Dict.ofList [ "accept", "application/json" ]
    /// </code></example>
    let ofList (pairs: ('K * 'V) list) =
        let result = Dictionary<'K, 'V>()

        for key, value in pairs do
            result[key] <- value

        result

    /// <summary>
    /// A case-insensitive string-keyed <c>Dictionary</c>. What HTTP headers,
    /// environment variables and config keys almost always want.
    /// </summary>
    /// <example><code lang="fsharp">
    /// let headers = Dict.ofListIgnoreCase [ "Content-Type", "application/json" ]
    /// Dict.tryFind "content-type" headers // Some "application/json"
    /// </code></example>
    let ofListIgnoreCase (pairs: (string * 'V) list) =
        let result = Dictionary<string, 'V>(System.StringComparer.OrdinalIgnoreCase)

        for key, value in pairs do
            result[key] <- value

        result

    /// <summary>An F# <c>Map</c> from any dictionary.</summary>
    /// <example><code lang="fsharp">
    /// Dict.toMap headers // Map&lt;string, string&gt;
    /// </code></example>
    let toMap (source: IDictionary<'K, 'V>) =
        source |> Seq.map (fun pair -> pair.Key, pair.Value) |> Map.ofSeq
