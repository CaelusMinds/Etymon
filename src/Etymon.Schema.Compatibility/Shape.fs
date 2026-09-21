namespace Etymon

open System

/// <summary>
/// The type of one field, reduced to what compatibility depends on.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately coarser than <see cref="T:Etymon.SchemaInfo"/>. Two payloads are
/// compatible or not because of the JSON they hold, so this records the JSON
/// shape and the rules, and forgets everything a description carries that a
/// stored byte does not — prose, examples, which .NET type it came from.
/// </para>
/// <para>
/// The coarseness is what makes the snapshot stable: renaming an F# record or
/// adding a doc comment must not read as a change to what was already
/// written or already sent.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
type FieldType =
    /// A JSON string, number, boolean or null.
    | Scalar of kind: string
    /// A JSON array of a single element type.
    | Sequence of element: FieldType
    /// A JSON object used as a map of uniform values.
    | Mapping of value: FieldType
    /// A JSON value that may be null, wrapping the type it holds when it is not.
    | Nullable of inner: FieldType
    /// A nested object, by the name it is recorded under.
    | Nested of name: string
    /// A tagged union, by name, with the case tags it admits.
    | Choice of name: string * cases: string list
    /// Arbitrary JSON, whose shape this cannot reason about.
    | Unknown

/// <summary>One field: what it is called, what it holds, and whether it has to be
/// there.</summary>
[<NoComparison>]
type ShapeField =
    {
        /// The key as it appears in the stored JSON.
        Name: string
        /// What the key holds.
        Type: FieldType
        /// Whether the key must be present. Distinct from whether its value may
        /// be null, which <see cref="T:Etymon.FieldType"/> carries.
        Required: bool
        /// The rules the value must satisfy, in the vocabulary from
        /// <c>Etymon.Core</c>. Narrowing one of these breaks reading of payloads
        /// already written or already sent, which is why they are recorded
        /// rather than dropped.
        Constraints: Constraint list
    }

/// <summary>
/// The field structure of one named thing at one version.
/// </summary>
/// <remarks>
/// <para>
/// An event type, a request body, a response body: the machinery does not care
/// which, and the policies in <c>Events</c> and <c>Wire</c> are what make it
/// mean something. Distinct from the <em>type</em>, which is the name, and from
/// an <em>instance</em>, which is a row or a payload. A shape is what a reader must be able to
/// cope with.
/// </para>
/// <para>
/// This is the description of the thing no migration tool sees, because it is
/// not in a column: the shape inside the payload.
/// </para>
/// </remarks>
[<NoComparison>]
type Shape =
    {
        /// The name this shape is recorded under.
        Name: string
        /// Which version of that name the shape describes.
        Version: int
        /// Its fields, in declaration order.
        Fields: ShapeField list
    }

/// <summary>
/// Every shape an application can write, recorded at a point in time.
/// </summary>
/// <remarks>
/// Serialised to JSON and committed to the repository, for the same reason the
/// table snapshot is: a change that exists as a file in a pull request is one a
/// colleague can object to before it reaches a log that cannot be rewritten.
/// </remarks>
[<NoComparison>]
type ShapeSnapshot =
    {
        /// The shapes, in a stable order.
        Shapes: Shape list
    }

/// Building shapes from a <c>Schema</c>, and snapshots from shapes.
[<RequireQualifiedAccess>]
module Shape =

    let private scalarName (kind: PrimKind) =
        match kind with
        | PrimKind.String -> "string"
        | PrimKind.Guid -> "uuid"
        | PrimKind.DateTimeOffset -> "date-time"
        | PrimKind.DateOnly -> "date"
        | PrimKind.TimeOnly -> "time"
        | PrimKind.TimeSpan -> "duration"
        | PrimKind.Bytes -> "base64"
        | PrimKind.Int -> "int32"
        | PrimKind.Int64 -> "int64"
        | PrimKind.Float -> "double"
        | PrimKind.Decimal -> "decimal"
        | PrimKind.Bool -> "boolean"
        | PrimKind.Raw -> "json"

    /// <summary>What a described value looks like once stored.</summary>
    /// <remarks>
    /// Named types are recorded by name rather than expanded, so that a shape
    /// stays readable and a nested type's own changes are reported against the
    /// nested type.
    /// </remarks>
    let rec private typeOf (info: SchemaInfo) : FieldType =
        match SchemaInfo.strip info with
        | SPrim PrimKind.Raw -> FieldType.Unknown
        | SPrim kind -> FieldType.Scalar(scalarName kind)
        | SNullable inner -> FieldType.Nullable(typeOf inner)
        | SList inner -> FieldType.Sequence(typeOf inner)
        | SMap inner -> FieldType.Mapping(typeOf inner)
        | SObject(name, _) -> FieldType.Nested name
        | SUnion(name, _, cases) -> FieldType.Choice(name, cases |> List.map fst)
        | SRef name -> FieldType.Nested name
        | SAnnotated _ -> FieldType.Unknown

    /// <summary>
    /// The shape of a named thing, derived from the <c>Schema</c> that describes
    /// it.
    /// </summary>
    /// <remarks>
    /// Derive this from the same value that does the work -- the codec for an
    /// event, the request or response schema for a wire contract. A shape
    /// derived from anything else describes a hope rather than what is actually
    /// written or sent.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Shape.ofSchema "InvoiceRaised" 2 invoiceRaisedSchema
    /// </code></example>
    let ofSchema (name: string) (version: int) (schema: Schema<'T>) : Shape =
        let fields =
            match SchemaInfo.strip schema.Info with
            | SObject(_, fields) ->
                fields
                |> List.map (fun field ->
                    {
                        Name = field.Name
                        Type = typeOf field.Schema
                        Required = field.Required
                        Constraints = SchemaInfo.constraints field.Schema
                    }
                )
            | _ ->
                // Something that is not an object has no fields to compare, and
                // every change to it is a change to the whole payload. Saying so
                // is better than pretending there is structure to diff.
                []

        {
            Name = name
            Version = version
            Fields = fields
        }

    /// <summary>The field of this shape with a given name, if it has one.</summary>
    let tryField (name: string) (shape: Shape) =
        shape.Fields
        |> List.tryFind (fun f -> String.Equals(f.Name, name, StringComparison.Ordinal))

/// Building and reading <see cref="T:Etymon.ShapeSnapshot"/> values.
[<RequireQualifiedAccess>]
module ShapeSnapshot =

    /// <summary>
    /// A snapshot of the given shapes, ordered by name and version so that the
    /// same set always produces the same file.
    /// </summary>
    /// <remarks>
    /// A snapshot whose bytes depend on the order somebody listed things in is
    /// a snapshot that produces spurious diffs, and a spurious diff is how a
    /// real one stops being read.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// ShapeSnapshot.of' [ raisedV1; raisedV2; settledV1 ]
    /// </code></example>
    let of' (shapes: Shape list) : ShapeSnapshot =
        {
            Shapes = shapes |> List.sortBy (fun s -> s.Name, s.Version)
        }

    /// <summary>Every version recorded under one name, in order.</summary>
    let versionsOf (name: string) (snapshot: ShapeSnapshot) =
        snapshot.Shapes
        |> List.filter (fun s -> String.Equals(s.Name, name, StringComparison.Ordinal))
        |> List.map (fun s -> s.Version)
        |> List.sort

    /// <summary>Every name in the snapshot, without repeats.</summary>
    let names (snapshot: ShapeSnapshot) =
        snapshot.Shapes |> List.map (fun s -> s.Name) |> List.distinct |> List.sort

    /// <summary>One shape, by name and version.</summary>
    let tryShape (name: string) (version: int) (snapshot: ShapeSnapshot) =
        snapshot.Shapes
        |> List.tryFind (fun s -> String.Equals(s.Name, name, StringComparison.Ordinal) && s.Version = version)

    /// <summary>The highest version recorded under a name.</summary>
    let tryLatest (name: string) (snapshot: ShapeSnapshot) =
        match versionsOf name snapshot with
        | [] -> None
        | versions -> tryShape name (List.max versions) snapshot
