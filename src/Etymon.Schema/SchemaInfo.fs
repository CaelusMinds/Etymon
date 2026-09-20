namespace Etymon

open System.Text.Json.Nodes

/// The kind of a scalar. Named rather than reflected, so that every consumer of a
/// schema agrees on what a value is without asking the type system.
[<RequireQualifiedAccess>]
type PrimKind =
    /// A JSON string.
    | String
    /// A 32-bit signed integer.
    | Int
    /// A 64-bit signed integer.
    | Int64
    /// A double-precision number.
    | Float
    /// An exact decimal, carried on the wire as a JSON number.
    | Decimal
    /// A JSON boolean.
    | Bool
    /// A UUID, carried as a string.
    | Guid
    /// An RFC 3339 timestamp, carried as a string.
    | DateTimeOffset
    /// An ISO 8601 date, carried as a string.
    | DateOnly
    /// An ISO 8601 wall-clock time, carried as a string.
    | TimeOnly
    /// A duration, carried as a string.
    | TimeSpan
    /// Binary data, carried as base64.
    | Bytes
    /// Arbitrary JSON, passed through untouched.
    | Raw

/// <summary>
/// Everything a schema says about a value that is not its shape: what it is
/// called, what it means, what restricts it, and what it looks like.
/// </summary>
[<NoComparison>]
type Meta =
    {
        /// The name this schema is known by, which becomes a reusable component
        /// in generated documentation and a table name in a generated migration.
        Name: string option
        /// Prose describing what the value means.
        Description: string option
        /// The rules the value must satisfy, drawn from the one vocabulary in
        /// <c>Etymon.Core</c> so that every derivation target reads the same list.
        Constraints: Constraint list
        /// Examples, already encoded.
        Examples: JsonNode list
        /// Whether the value is a secret. Redacted wherever Etymon renders it.
        Sensitive: bool
    }

/// Working with <see cref="T:Etymon.Meta"/>.
[<RequireQualifiedAccess>]
module Meta =

    /// <summary>Metadata that says nothing.</summary>
    /// <example><code lang="fsharp">
    /// Meta.empty.Constraints // []
    /// </code></example>
    let empty =
        {
            Name = None
            Description = None
            Constraints = []
            Examples = []
            Sensitive = false
        }

    /// <summary>
    /// Combines two layers of metadata, with the outer one winning where both say
    /// something. Constraints accumulate rather than replace, because each one is
    /// a separate promise about the value.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Meta.merge inner outer // outer.Description wins; constraints are concatenated
    /// </code></example>
    let merge (inner: Meta) (outer: Meta) =
        {
            Name = Option.orElse inner.Name outer.Name
            Description = Option.orElse inner.Description outer.Description
            Constraints = inner.Constraints @ outer.Constraints
            Examples = inner.Examples @ outer.Examples
            Sensitive = inner.Sensitive || outer.Sensitive
        }

/// <summary>
/// The shape of a value, with the types erased.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of <c>Schema&lt;'T&gt;</c> that every other package reads.
/// <c>Etymon.Schema.OpenApi</c> turns it into JSON Schema, <c>Etymon.Migrations</c>
/// into a table, <c>Etymon.Contracts.FsCheck</c> into a generator — each of them a
/// pure function of this tree and nothing else.
/// </para>
/// <para>
/// Keeping the description separate from the typed codec is what stops the two
/// drifting: there is no way to change how a value encodes without changing what
/// this says about it, because they are built from the same combinator call.
/// </para>
/// </remarks>
[<NoComparison>]
type SchemaInfo =
    /// A scalar.
    | SPrim of PrimKind
    /// A value that may be JSON <c>null</c>. Distinct from an optional field,
    /// which may be absent entirely — JSON Schema, OpenAPI and TypeScript all
    /// treat those differently, so Etymon does too.
    | SNullable of SchemaInfo
    /// An ordered collection.
    | SList of SchemaInfo
    /// An object used as a string-keyed map of uniform values.
    | SMap of SchemaInfo
    /// An object with known fields.
    | SObject of name: string * fields: FieldInfo list
    /// A discriminated union, distinguished by the value of a tag field.
    | SUnion of name: string * tag: string * cases: (string * SchemaInfo) list
    /// A reference to a named schema, which is how recursion is represented.
    | SRef of name: string
    /// A schema with metadata attached.
    | SAnnotated of SchemaInfo * Meta

/// One field of an object.
and [<NoComparison>] FieldInfo =
    {
        /// The key as it appears in JSON.
        Name: string
        /// The shape of the value.
        Schema: SchemaInfo
        /// Whether the key must be present. Distinct from whether the value may
        /// be <c>null</c>.
        Required: bool
        /// Prose describing what the field means.
        Description: string option
        /// The value used when the key is absent, already encoded.
        Default: JsonNode option
        /// Whether the field is a secret.
        Sensitive: bool
    }

/// Inspecting a <see cref="T:Etymon.SchemaInfo"/>.
[<RequireQualifiedAccess>]
module SchemaInfo =

    /// <summary>
    /// The metadata attached to a schema, flattened through any number of
    /// annotation layers.
    /// </summary>
    /// <example><code lang="fsharp">
    /// SchemaInfo.meta info |> fun m -> m.Constraints
    /// </code></example>
    let rec meta (info: SchemaInfo) =
        match info with
        | SAnnotated(inner, m) -> Meta.merge (meta inner) m
        | _ -> Meta.empty

    /// <summary>The schema with its annotation layers removed.</summary>
    /// <example><code lang="fsharp">
    /// SchemaInfo.strip (SAnnotated(SPrim PrimKind.String, Meta.empty)) // SPrim String
    /// </code></example>
    let rec strip (info: SchemaInfo) =
        match info with
        | SAnnotated(inner, _) -> strip inner
        | other -> other

    /// <summary>
    /// The name this schema is known by, if it has one. A named schema becomes a
    /// reusable component rather than being inlined everywhere it appears.
    /// </summary>
    /// <example><code lang="fsharp">
    /// SchemaInfo.name personInfo // Some "Person"
    /// </code></example>
    let name (info: SchemaInfo) =
        match strip info with
        | SObject(n, _) -> Some n
        | SUnion(n, _, _) -> Some n
        | SRef n -> Some n
        | _ -> (meta info).Name

    /// <summary>The constraints attached to a schema, in the order they were added.</summary>
    /// <example><code lang="fsharp">
    /// SchemaInfo.constraints emailInfo // [ Length(Some 3, Some 254); HasFormat Email ]
    /// </code></example>
    let constraints (info: SchemaInfo) = (meta info).Constraints

    /// <summary>Whether the value may be JSON <c>null</c>.</summary>
    /// <example><code lang="fsharp">
    /// SchemaInfo.isNullable (SNullable(SPrim PrimKind.String)) // true
    /// </code></example>
    let isNullable (info: SchemaInfo) =
        match strip info with
        | SNullable _ -> true
        | _ -> false

    /// <summary>
    /// Every named schema reachable from this one, keyed by name. This is what
    /// lets a generator emit each object once and reference it thereafter, and
    /// what makes a recursive schema expressible at all.
    /// </summary>
    /// <example><code lang="fsharp">
    /// SchemaInfo.definitions personInfo |> Map.keys // seq [ "Address"; "Person" ]
    /// </code></example>
    let definitions (info: SchemaInfo) =
        let mutable found = Map.empty

        let rec walk current =
            match current with
            | SAnnotated(inner, _) -> walk inner
            | SNullable inner -> walk inner
            | SList inner -> walk inner
            | SMap inner -> walk inner
            | SPrim _
            | SRef _ -> ()
            | SObject(n, fields) ->
                if not (Map.containsKey n found) then
                    // Recorded before descending, so that a schema containing
                    // itself terminates instead of recurring forever.
                    found <- Map.add n current found

                    for field in fields do
                        walk field.Schema
            | SUnion(n, _, cases) ->
                if not (Map.containsKey n found) then
                    found <- Map.add n current found

                    for _, case in cases do
                        walk case

        walk info
        found
