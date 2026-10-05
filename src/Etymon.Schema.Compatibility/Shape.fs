namespace Etymon

open System
open System.Text.Json.Nodes

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
    /// A tagged union, by name, with the case tags it admits and the wire shape
    /// the union is written in, as text: "adjacent:kind:value" or
    /// "internal:type". A snapshot older than format 4 never recorded the shape,
    /// so None reads as not recorded and is never compared.
    | Choice of name: string * cases: string list * encoding: string option
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
        /// The rules on each element of a sequence, or each value of a mapping.
        /// A code set is more often a list of codes than a single one -- roles,
        /// permissions, grants -- and narrowing it is the same break as narrowing
        /// a scalar's rules, so it is recorded the same way. One level: the
        /// elements of a list of lists are not reached.
        ///
        /// <c>None</c> means the snapshot this came from predates the key and
        /// never recorded them, which is not the same as recording none:
        /// nothing can be compared against it, so nothing is. <c>Some []</c>
        /// is recorded, and empty.
        ElementConstraints: Constraint list option
        /// The field's default, as the encoded JSON text, where the field has
        /// one. The outer option is whether the writer recorded a default at
        /// all: a snapshot written before the key existed reads as None and is
        /// never compared, as element rules are. The inner option is whether
        /// the field has a default; a JSON null default is Some "null".
        Default: string option option
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

    /// The tag key of a union, whatever the union's shape.
    let private tagOf (shape: UnionShape) =
        match shape with
        | UnionShape.AdjacentTag(tag, _)
        | UnionShape.InternalTag tag -> tag

    /// A union's wire shape as the text the snapshot records, so a change of
    /// shape is a change of what was written.
    let private encodingOf (shape: UnionShape) =
        match shape with
        | UnionShape.AdjacentTag(tag, payloadKey) -> $"adjacent:%s{tag}:%s{payloadKey}"
        | UnionShape.InternalTag tag -> $"internal:%s{tag}"

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
        | SUnion(name, shape, cases) -> FieldType.Choice(name, cases |> List.map fst, Some(encodingOf shape))
        | SRef name -> FieldType.Nested name
        | SAnnotated _ -> FieldType.Unknown

    /// The constraints on what a collection holds, looking through a nullable
    /// wrapper so that a list that may itself be null still reports them.
    let rec private elementConstraints (info: SchemaInfo) : Constraint list =
        match SchemaInfo.strip info with
        | SNullable inner -> elementConstraints inner
        | SList inner
        | SMap inner -> SchemaInfo.constraints inner
        | _ -> []

    /// A default as the text a snapshot records: the encoded JSON, with a JSON
    /// null default spelled "null" because JsonNode has no other value for it.
    let private defaultText (node: JsonNode option) : string option =
        match node with
        | None -> None
        | Some null -> Some "null"
        | Some node -> Some(node.ToJsonString())

    let private fieldOf (field: FieldInfo) : ShapeField =
        {
            Name = field.Name
            Type = typeOf field.Schema
            Required = field.Required
            Constraints = SchemaInfo.constraints field.Schema
            ElementConstraints = Some(elementConstraints field.Schema)
            Default = Some(defaultText field.Default)
        }

    /// The fields of a payload. An object's fields are the object's fields. A
    /// union is one field, named after the tag key, holding the union's cases.
    /// Anything else is one field named "$", the document root, holding the
    /// payload's type and rules; a payload with no fields to compare used to
    /// record none, and every change to the payload then passed unseen.
    let private fieldsOf (info: SchemaInfo) : ShapeField list =
        match SchemaInfo.strip info with
        | SObject(_, fields) -> fields |> List.map fieldOf
        | SUnion(_, shape, _) ->
            [
                {
                    Name = tagOf shape
                    Type = typeOf info
                    Required = true
                    Constraints = []
                    ElementConstraints = Some []
                    Default = Some None
                }
            ]
        | other ->
            [
                {
                    Name = "$"
                    Type = typeOf other
                    Required = true
                    Constraints = SchemaInfo.constraints info
                    ElementConstraints = Some(elementConstraints info)
                    Default = Some None
                }
            ]

    let private shapeOf (name: string) (version: int) (info: SchemaInfo) : Shape =
        {
            Name = name
            Version = version
            Fields = fieldsOf info
        }

    /// <summary>
    /// The shape of a thing under a name you choose, rather than the one its
    /// <c>Schema</c> carries.
    /// </summary>
    /// <remarks>
    /// For the rare shape whose Schema is unnamed, and for a deliberate rename
    /// where the snapshot must keep the old entry. Prefer <c>Shape.ofSchema</c>,
    /// which cannot disagree with the Schema because it does not get the chance.
    /// </remarks>
    /// <remarks>
    /// Derive this from the same value that does the work -- the codec for an
    /// event, the request or response schema for a wire contract. A shape
    /// derived from anything else describes a hope rather than what is actually
    /// written or sent.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Shape.ofSchemaNamed "LegacyInvoice" 2 invoiceRaisedSchema
    /// </code></example>
    let ofSchemaNamed (name: string) (version: int) (schema: Schema<'T>) : Shape = shapeOf name version schema.Info

    /// <summary>
    /// The shape of a named thing, derived from the <c>Schema</c> that describes
    /// it — including its name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derive this from the same value that does the work -- the codec for an
    /// event, the request or response schema for a wire contract. A shape
    /// derived from anything else describes a hope rather than what is actually
    /// written or sent.
    /// </para>
    /// <para>
    /// The name comes from the <c>Schema</c> rather than from the caller. A
    /// snapshot matches its entries by name, so a shape recorded under a name
    /// its <c>Schema</c> does not carry diffs cleanly against itself forever and
    /// is never compared with the thing it actually describes. The failure is
    /// silence, which is the failure this package exists to remove -- and a
    /// hand-written list of sixty shapes is exactly where a copy-paste keeps the
    /// wrong name.
    /// </para>
    /// <para>
    /// The version stays a parameter, and should: it is not something a
    /// <c>Schema</c> knows.
    /// </para>
    /// </remarks>
    /// <exception cref="System.ArgumentException">
    /// The schema has no name to take. Name it with <c>Schema.object</c>, or say
    /// the name deliberately with <c>Shape.ofSchemaNamed</c>.
    /// </exception>
    /// <example><code lang="fsharp">
    /// Shape.ofSchema 2 invoiceRaisedSchema
    /// </code></example>
    let ofSchema (version: int) (schema: Schema<'T>) : Shape =
        match SchemaInfo.name schema.Info with
        | Some name -> ofSchemaNamed name version schema
        | None ->
            invalidArg
                "schema"
                "This schema has no name, so a shape cannot take one from it. Name it with Schema.object, or give the name deliberately with Shape.ofSchemaNamed."

    /// <summary>
    /// The shape of a named thing and every shape reachable from it: the root
    /// first, then each nested object and each union case payload, in name
    /// order, all at the given version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Shape.ofSchema</c> records a nested object by name and a union by its
    /// tags, so a change inside either is compared only if the harness lists
    /// the nested shape by hand, and a nested shape the harness does not list
    /// is a shape no check covers. This walks the <c>Schema</c> instead, so the
    /// snapshot carries every shape the payload can hold.
    /// </para>
    /// <para>
    /// A nested object or union is recorded under the object's or union's own
    /// name, once. A union case whose payload is an object or a union is
    /// recorded under the payload's own name; a case whose payload is anything
    /// else recordable is recorded under <c>Union.tag</c> with one field named
    /// <c>$</c>; a case with no payload records nothing. The walk stops at a
    /// reference, which is how recursion is broken, and at a raw payload; a
    /// reference to a recorded union resolves to the union's cases, as the
    /// resolved schema records them, so the same bytes carry one field type.
    /// </para>
    /// </remarks>
    /// <exception cref="System.ArgumentException">
    /// The schema has no name, or two different definitions share one name. A
    /// snapshot matches by name, so one name describes one shape. Name a list
    /// or scalar root with <c>Schema.named</c>.
    /// </exception>
    /// <example><code lang="fsharp">
    /// ShapeSnapshot.of' (codecs |> List.collect (Shape.ofSchemaDeep 1))
    /// </code></example>
    let ofSchemaDeep (version: int) (schema: Schema<'T>) : Shape list =
        let rootName =
            match SchemaInfo.name schema.Info with
            | Some name -> name
            | None ->
                invalidArg
                    "schema"
                    "This schema has no name, so a shape cannot take one from it. Name it with Schema.object, or name a list or scalar root with Schema.named."

        let mutable found: Map<string, Shape> = Map.empty
        // The unions recorded, by name, with the Choice type a field holding
        // the union carries; a reference to the union resolves to the same.
        let mutable unions: Map<string, FieldType> = Map.empty

        // Record a shape under a name; the first definition wins, a second
        // identical one is the same shape reached again, and a different one is
        // refused. Returns whether the name was new, so children are walked once.
        let record (name: string) (info: SchemaInfo) =
            let shape = shapeOf name version info

            match Map.tryFind name found with
            | Some existing when existing.Fields = shape.Fields -> false
            | Some _ ->
                invalidArg
                    "schema"
                    $"Two different definitions share the name '%s{name}'. A snapshot matches by name, so one name describes one shape; name one of the two differently."
            | None ->
                found <- Map.add name shape found
                true

        let rec walk (info: SchemaInfo) =
            match SchemaInfo.strip info with
            | SObject(name, fields) ->
                if record name info then
                    fields |> List.iter (fun f -> walk f.Schema)
            | SUnion(name, _, cases) ->
                // Recorded under the union's own name, as an object is, so a
                // reference back to the union from a case payload resolves.
                if record name info then
                    unions <- Map.add name (typeOf info) unions

                    cases
                    |> List.iter (fun (tag, payload) ->
                        match payload with
                        | None -> ()
                        | Some payload ->
                            match SchemaInfo.strip payload with
                            | SPrim PrimKind.Raw -> ()
                            | SObject _
                            | SUnion _ -> walk payload
                            | other ->
                                if record $"%s{name}.%s{tag}" payload then
                                    walk other
                    )
            | SNullable inner
            | SList inner
            | SMap inner -> walk inner
            | SPrim _
            | SRef _
            | SAnnotated _ -> ()

        match SchemaInfo.strip schema.Info with
        | SObject _
        | SUnion _ -> walk schema.Info
        | _ ->
            record rootName schema.Info |> ignore
            walk schema.Info

        // A reference is recorded as Nested name because the walk cannot see
        // through a reference; once every union is recorded, a reference to a
        // union becomes the union's Choice, which is what a field holding the
        // resolved union records.
        let rec resolve (fieldType: FieldType) =
            match fieldType with
            | FieldType.Nested name ->
                match Map.tryFind name unions with
                | Some choice -> choice
                | None -> fieldType
            | FieldType.Sequence inner -> FieldType.Sequence(resolve inner)
            | FieldType.Mapping inner -> FieldType.Mapping(resolve inner)
            | FieldType.Nullable inner -> FieldType.Nullable(resolve inner)
            | FieldType.Scalar _
            | FieldType.Choice _
            | FieldType.Unknown -> fieldType

        let resolved (shape: Shape) =
            { shape with
                Fields = shape.Fields |> List.map (fun f -> { f with Type = resolve f.Type })
            }

        let root = resolved (Map.find rootName found)

        let rest =
            found
            |> Map.toList
            |> List.filter (fun (name, _) -> name <> rootName)
            |> List.map (snd >> resolved)
            |> List.sortBy (fun s -> s.Name)

        root :: rest

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
        // Several roots reaching one nested object give the object once per
        // root. Identical copies collapse; two different shapes under one name
        // and version would be compared against each other forever, so the
        // pair is refused with both names in hand.
        let distinct =
            shapes
            |> List.groupBy (fun s -> s.Name, s.Version)
            |> List.map (fun ((name, version), group) ->
                match List.distinct group with
                | [ one ] -> one
                | _ ->
                    invalidArg
                        "shapes"
                        $"'%s{name}' v%d{version} is given twice with different fields. A snapshot matches by name and version, so one pair describes one shape."
            )

        {
            Shapes = distinct |> List.sortBy (fun s -> s.Name, s.Version)
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

    /// <summary>
    /// The committed snapshot with every current shape added, or replacing the
    /// committed shape of the same name and version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The regeneration primitive. A harness derives the current snapshot from
    /// the codecs, so the current snapshot carries only the versions the code
    /// declares today; the committed file is where the stored versions live. A
    /// file regenerated from the current snapshot alone forgets every version
    /// the code stopped declaring, and with the version goes the evidence that
    /// events at the version exist.
    /// </para>
    /// <para>
    /// A version present only in the committed snapshot therefore stays. A
    /// shape present in both is taken from the current snapshot, so an in-place
    /// change the matrix reports as safe (an optional field added, say) is
    /// recorded as the shape is now.
    /// </para>
    /// </remarks>
    /// <example><code lang="fsharp">
    /// File.WriteAllText(path, ShapeSnapshots.toJson (ShapeSnapshot.extend committed current))
    /// </code></example>
    let extend (committed: ShapeSnapshot) (current: ShapeSnapshot) : ShapeSnapshot =
        let kept =
            committed.Shapes
            |> List.filter (fun c -> (tryShape c.Name c.Version current).IsNone)

        of' (kept @ current.Shapes)
