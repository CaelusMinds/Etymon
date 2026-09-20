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
[<CustomEquality; NoComparison; StructuredFormatDisplay("{Display}")>]
type Secret<'T when 'T: equality> =
    private
    | Secret of 'T

    /// Always <c>&lt;redacted&gt;</c>.
    override _.ToString() = "<redacted>"

    /// Used by F# <c>%A</c> formatting. Always <c>&lt;redacted&gt;</c>.
    member _.Display = "<redacted>"

    /// Two secrets are equal when the values they hide are equal.
    override this.Equals(other: obj) =
        match other with
        | :? Secret<'T> as that ->
            let (Secret mine) = this
            let (Secret theirs) = that
            mine = theirs
        | _ -> false

    /// Hashes the hidden value, so secrets work as dictionary keys.
    override this.GetHashCode() =
        let (Secret value) = this
        hash value

/// Building and unwrapping <see cref="T:Etymon.Secret`1"/>.
[<RequireQualifiedAccess>]
module Secret =

    /// <summary>Hides a value.</summary>
    /// <example><code lang="fsharp">
    /// let key = Secret.create "sk-live-123"
    /// printfn "%A" key // prints &lt;redacted&gt;
    /// </code></example>
    let create (value: 'T) = Secret value

    /// <summary>
    /// Unhides a value. Every place a secret leaves Etymon goes through this one
    /// function, so auditing is a search for <c>Secret.reveal</c>.
    /// </summary>
    /// <example><code lang="fsharp">
    /// let connect (cs: Secret&lt;string&gt;) = new SqlConnection(Secret.reveal cs)
    /// </code></example>
    let reveal (Secret value) = value

    /// <summary>Transforms the hidden value without exposing it to the caller.</summary>
    /// <example><code lang="fsharp">
    /// Secret.create "  key  " |> Secret.map (fun s -> s.Trim())
    /// </code></example>
    let map (f: 'T -> 'U) (Secret value) : Secret<'U> = Secret(f value)
