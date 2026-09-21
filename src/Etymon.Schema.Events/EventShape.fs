namespace Etymon

open System

/// <summary>
/// The type of one field in an event, reduced to what compatibility depends on.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately coarser than <see cref="T:Etymon.SchemaInfo"/>. Two events are
/// compatible or not because of the JSON they hold, so this records the JSON
/// shape and the rules, and forgets everything a description carries that a
/// stored byte does not — prose, examples, which .NET type it came from.
/// </para>
/// <para>
/// The coarseness is what makes the snapshot stable: renaming an F# record or
/// adding a doc comment must not read as a change to the events already
/// written.
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

/// <summary>One field of an event: what it is called, what it holds, and whether it
/// has to be there.</summary>
[<NoComparison>]
type EventField =
    {
        /// The key as it appears in the stored JSON.
        Name: string
        /// What the key holds.
        Type: FieldType
        /// Whether the key must be present. Distinct from whether its value may
        /// be null, which <see cref="T:Etymon.FieldType"/> carries.
        Required: bool
        /// The rules the value must satisfy, in the vocabulary from
        /// <c>Etymon.Core</c>. Narrowing one of these breaks reading of events
        /// already written, which is why they are recorded rather than dropped.
        Constraints: Constraint list
    }

/// <summary>
/// The field structure of one event type at one version.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from the event <em>type</em>, which is the name, and from an event
/// <em>instance</em>, which is a row. A shape is what a reader must be able to
/// cope with.
/// </para>
/// <para>
/// In an event-sourced system the tables barely change: the event table has the
/// columns it will always have. What changes is what is written inside the
/// payload, and no migration tool sees it, because no column moved. This is the
/// description of that invisible thing.
/// </para>
/// </remarks>
[<NoComparison>]
type EventShape =
    {
        /// The event type name, as stored.
        Name: string
        /// Which version of this event type the shape describes.
        Version: int
        /// Its fields, in declaration order.
        Fields: EventField list
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
type EventSnapshot =
    {
        /// The shapes, in a stable order.
        Shapes: EventShape list
    }

/// Building shapes from a <c>Schema</c>, and snapshots from shapes.
[<RequireQualifiedAccess>]
module EventShape =

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
    /// The shape of an event, derived from the <c>Schema</c> its codec is built
    /// from.
    /// </summary>
    /// <remarks>
    /// Derive this from the same value the codec uses. The codec is what wrote
    /// the bytes; a shape derived from anything else describes a hope rather
    /// than the log.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// EventShape.ofSchema "InvoiceRaised" 2 invoiceRaisedSchema
    /// </code></example>
    let ofSchema (name: string) (version: int) (schema: Schema<'T>) : EventShape =
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
                // An event that is not an object has no fields to compare, and
                // every change to it is a change to the whole payload. Saying so
                // is better than pretending there is structure to diff.
                []

        {
            Name = name
            Version = version
            Fields = fields
        }

    /// <summary>The field of this shape with a given name, if it has one.</summary>
    let tryField (name: string) (shape: EventShape) =
        shape.Fields
        |> List.tryFind (fun f -> String.Equals(f.Name, name, StringComparison.Ordinal))

/// Building and reading <see cref="T:Etymon.EventSnapshot"/> values.
[<RequireQualifiedAccess>]
module EventSnapshot =

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
    /// EventSnapshot.of' [ raisedV1; raisedV2; settledV1 ]
    /// </code></example>
    let of' (shapes: EventShape list) : EventSnapshot =
        {
            Shapes = shapes |> List.sortBy (fun s -> s.Name, s.Version)
        }

    /// <summary>Every version recorded for one event type, in order.</summary>
    let versionsOf (name: string) (snapshot: EventSnapshot) =
        snapshot.Shapes
        |> List.filter (fun s -> String.Equals(s.Name, name, StringComparison.Ordinal))
        |> List.map (fun s -> s.Version)
        |> List.sort

    /// <summary>Every event type in the snapshot, without repeats.</summary>
    let names (snapshot: EventSnapshot) =
        snapshot.Shapes |> List.map (fun s -> s.Name) |> List.distinct |> List.sort

    /// <summary>One shape, by name and version.</summary>
    let tryShape (name: string) (version: int) (snapshot: EventSnapshot) =
        snapshot.Shapes
        |> List.tryFind (fun s -> String.Equals(s.Name, name, StringComparison.Ordinal) && s.Version = version)

    /// <summary>The highest version recorded for an event type.</summary>
    let tryLatest (name: string) (snapshot: EventSnapshot) =
        match versionsOf name snapshot with
        | [] -> None
        | versions -> tryShape name (List.max versions) snapshot
