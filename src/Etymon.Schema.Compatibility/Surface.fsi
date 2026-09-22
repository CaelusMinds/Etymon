
namespace FSharp



namespace Etymon
    
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
          
          /// The rules on each element of a sequence, or each value of a mapping.
          /// A code set is more often a list of codes than a single one -- roles,
          /// permissions, grants -- and narrowing it is the same break as narrowing
          /// a scalar's rules, so it is recorded the same way. One level: the
          /// elements of a list of lists are not reached.
          ElementConstraints: Constraint list
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
        
        val private scalarName: kind: PrimKind -> string
        
        /// <summary>What a described value looks like once stored.</summary>
        /// <remarks>
        /// Named types are recorded by name rather than expanded, so that a shape
        /// stays readable and a nested type's own changes are reported against the
        /// nested type.
        /// </remarks>
        val private typeOf: info: SchemaInfo -> FieldType
        
        /// The constraints on what a collection holds, looking through a nullable
        /// wrapper so that a list that may itself be null still reports them.
        val private elementConstraints: info: SchemaInfo -> Constraint list
        
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
        val ofSchemaNamed:
          name: string -> version: int -> schema: Schema<'T> -> Shape
        
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
        val ofSchema: version: int -> schema: Schema<'T> -> Shape
        
        /// <summary>The field of this shape with a given name, if it has one.</summary>
        val tryField: name: string -> shape: Shape -> ShapeField option
    
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
        val of': shapes: Shape list -> ShapeSnapshot
        
        /// <summary>Every version recorded under one name, in order.</summary>
        val versionsOf: name: string -> snapshot: ShapeSnapshot -> int list
        
        /// <summary>Every name in the snapshot, without repeats.</summary>
        val names: snapshot: ShapeSnapshot -> string list
        
        /// <summary>One shape, by name and version.</summary>
        val tryShape:
          name: string ->
            version: int -> snapshot: ShapeSnapshot -> Shape option
        
        /// <summary>The highest version recorded under a name.</summary>
        val tryLatest: name: string -> snapshot: ShapeSnapshot -> Shape option

namespace Etymon
    
    /// <summary>
    /// Reading and writing an <see cref="T:Etymon.ShapeSnapshot"/> as JSON.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The format is described by an Etymon <c>Schema</c> rather than written by
    /// hand, which is the suite eating its own cooking: the snapshot round-trips
    /// through the same codec everything else uses, and a malformed file reports
    /// <c>shapes[2].fields[0].name: is required</c> instead of a stack trace.
    /// </para>
    /// <para>
    /// <strong>Where the file belongs, in a consuming application: beside the
    /// codecs, not beside the SQL migrations.</strong> This is the non-obvious part
    /// and filing it with the migrations is the mistake a reader will make. The
    /// codec is what wrote the bytes; the snapshot describes those bytes; the two
    /// change together and should be reviewed in the same diff. The migrations
    /// describe the tables, which in an event-sourced system barely change at all.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module ShapeSnapshots =
        
        /// The format version of the snapshot file itself, so that a future change
        /// to the format can be recognised rather than guessed at.
        [<Literal>]
        val FormatVersion: int = 1
        
        val private fieldTypeSchema: Schema<FieldType>
        
        val private fieldSchema: Schema<ShapeField>
        
        val private shapeSchema: Schema<Shape>
        
        /// <summary>
        /// The snapshot file format, as a <c>Schema</c>.
        /// </summary>
        /// <remarks>
        /// Public so that the format is documentable by the same machinery that
        /// documents everything else: <c>OpenApi.toJsonSchemaText ShapeSnapshots.schema</c>
        /// prints it.
        /// </remarks>
        val schema: Schema<ShapeSnapshot>
        
        /// <summary>The snapshot as JSON, indented, ready to commit.</summary>
        /// <remarks>
        /// Shapes are written in name and version order, so the same set of shapes
        /// always produces the same file and a real change is not buried in
        /// reordering noise.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// File.WriteAllText("events.snapshot.json", ShapeSnapshots.toJson snapshot)
        /// </code></example>
        val toJson: snapshot: ShapeSnapshot -> string
        
        /// <summary>A snapshot read back, or every reason the file could not be read.</summary>
        /// <example><code lang="fsharp">
        /// match ShapeSnapshots.fromJson text with
        /// | Ok snapshot -> ...
        /// | Error errors -> eprintfn "%s" (ValidationErrors.format errors)
        /// // shapes[2].fields[0].name: is required
        /// </code></example>
        val fromJson: text: string -> Validation<ShapeSnapshot>

namespace Etymon
    
    /// <summary>
    /// Which direction a compatibility verdict is about.
    /// </summary>
    /// <remarks>
    /// Both directions matter and they fail on different changes, so neither is
    /// "compatibility" on its own. Prose in this package never says "compatible"
    /// unqualified, and neither should yours.
    /// </remarks>
    [<RequireQualifiedAccess>]
    type Direction =
        
        /// New code reading what was already written. The one you always need:
        /// losing it makes history unreadable.
        | Backward
        
        /// Already-deployed code reading what newer code writes. Matters during
        /// a rollout, when two versions run against one log.
        | Forward
    
    /// Working with <see cref="T:Etymon.Direction"/>.
    [<RequireQualifiedAccess>]
    module Direction =
        
        /// <summary>What this direction means, as a phrase that fits in a sentence.</summary>
        val describe: direction: Direction -> string
    
    /// <summary>One thing that changed between two versions of an event shape.</summary>
    [<NoComparison>]
    type ShapeChange =
        {
          
          /// The name of the shape the change is about.
          Shape: string
          
          /// The field, where the change is about one.
          Field: string option
          
          /// What changed, in a sentence.
          Description: string
          
          /// The directions this change breaks, and why.
          Breaks: (Direction * string) list
        }
    
    /// <summary>
    /// A rename that the author has declared, because it cannot be detected.
    /// </summary>
    /// <remarks>
    /// A removal plus an addition is indistinguishable from a rename, and the
    /// difference decides whether shapes already written can be read at all --
    /// stored events for one policy, bodies already sent for the other. This
    /// package refuses to guess, so a rename is something you say.
    /// </remarks>
    [<NoComparison>]
    type Rename =
        {
          
          /// The name of the shape the rename happened in.
          Shape: string
          
          /// What the field used to be called.
          From: string
          
          /// What it is called now.
          To: string
        }
    
    /// Comparing two event snapshots.
    [<RequireQualifiedAccess>]
    module Compatibility =
        
        val private renamedTo:
          event: string -> from: string -> renames: Rename list -> string option
        
        val private renamedFrom:
          event: string -> to': string -> renames: Rename list -> bool
        
        val private change:
          event: string ->
            field: string option ->
            description: string ->
            breaks: (Direction * string) list -> ShapeChange
        
        /// Whether the second set of rules admits everything the first does.
        ///
        /// Compared as sets rather than interpreted: this package knows that the
        /// rules differ, not what the difference permits. Narrowing and widening
        /// are therefore both reported, in the direction each could break.
        val private constraintsDiffer:
          before: Constraint list -> after: Constraint list -> bool
        
        val private compareFields:
          event: string ->
            renames: Rename list ->
            before: Shape -> after: Shape -> ShapeChange list
        
        /// <summary>
        /// Every change between two versions of one event shape, with the direction
        /// each breaks.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Compatibility.betweenShapes [] raisedV1 raisedV2
        /// </code></example>
        val betweenShapes:
          renames: Rename list ->
            before: Shape -> after: Shape -> ShapeChange list
        
        /// <summary>
        /// Every change between the latest shape of each event type in two
        /// snapshots.
        /// </summary>
        /// <remarks>
        /// Pure: two snapshots in, verdicts out. It never opens a connection,
        /// because the question "is this change safe against events already
        /// written?" is answerable from the shapes alone, and a package that needed
        /// a database to answer it could not run in a build.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Compatibility.between [] committedSnapshot currentSnapshot
        /// </code></example>
        val between:
          renames: Rename list ->
            before: ShapeSnapshot -> after: ShapeSnapshot -> ShapeChange list
        
        /// <summary>Only the changes that break a given direction.</summary>
        /// <example><code lang="fsharp">
        /// changes |> Compatibility.breaking Direction.Backward
        /// </code></example>
        val breaking:
          direction: Direction -> changes: ShapeChange list -> ShapeChange list
        
        /// <summary>
        /// The changes as prose, one paragraph each, ready to print in a build.
        /// </summary>
        /// <remarks>
        /// Full sentences rather than codes. The verdict a developer reads at
        /// eleven at night is the entire product here, and "EVT0007" is not a
        /// verdict.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// printfn "%s" (Compatibility.report changes)
        /// </code></example>
        val report: changes: ShapeChange list -> string

namespace Etymon
    
    /// <summary>
    /// A declaration that one version of an event can be read into another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The function itself is not here, and cannot be: "what due date should a 2024
    /// invoice get?" is a business judgement, not a derivation. What is checked is
    /// that a declaration exists for every gap, so that no stored version is left
    /// with nothing able to read it.
    /// </para>
    /// <para>
    /// Note the direction. An upcaster transforms <em>on read</em>. Old events are
    /// never rewritten, which is why this is a permanent property of the code and
    /// not a one-time change to data — and why it is not a migration.
    /// </para>
    /// </remarks>
    [<NoComparison>]
    type Upcaster =
        {
          
          /// The name of the shape it applies to.
          Shape: string
          
          /// The version it reads.
          Reads: int
          
          /// The version it produces.
          Produces: int
        }
    
    /// <summary>Something that must be settled before a change is safe to ship.</summary>
    [<NoComparison>]
    type Unresolved =
        {
          
          /// The name of the shape it is about.
          Shape: string
          
          /// What is unresolved, and what to do about it, in full sentences.
          Message: string
        }
    
    /// <summary>
    /// The two checks a versioned-shape policy has to make, without the vocabulary
    /// of any one policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Events and request bodies both carry versions, and both can be changed in
    /// ways that cannot be settled by looking at the shapes alone. The computation
    /// is identical: a removal plus an addition is indistinguishable from a rename
    /// whichever kind of shape it is, and version reachability is graph walking
    /// either way.
    /// </para>
    /// <para>
    /// What differs is the noun. An event's old shapes are <em>stored</em>; a
    /// request body's were <em>already sent</em>. The check states the fact and the
    /// policy supplies the noun, which is the same layering the compatibility
    /// matrix uses -- and for the same reason, because the alternative is a wire
    /// verdict that talks about event stores.
    /// </para>
    /// </remarks>
    module internal Detect =
        
        /// Versions no chain of upcasters brings to the current one. `oldShapes`
        /// names what the policy keeps: "stored shapes", "bodies already sent".
        val strandedVersions:
          oldShapes: string ->
            upcasters: Upcaster list ->
            snapshot: ShapeSnapshot -> Unresolved list
        
        /// Removals and additions in one version that might be a rename. `decides`
        /// names what the ambiguity decides, which is the policy's to say.
        val renameAmbiguities:
          decides: string ->
            renames: Rename list ->
            before: ShapeSnapshot -> after: ShapeSnapshot -> Unresolved list
        
        /// The items as prose, one paragraph each. Shared because the rendering is
        /// not a policy decision -- only the words being rendered are.
        val report: problems: Unresolved list -> string

namespace Etymon
    
    /// <summary>
    /// The event-sourcing policy over the shared compatibility matrix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The matrix in <c>Compatibility</c> knows nothing about events. This module is
    /// what makes it mean something for an event log: the direction that matters is
    /// backward, the fix is an upcaster, and the events already written are never
    /// rewritten.
    /// </para>
    /// <para>
    /// The last of those is not a default. Migrating an event store means upcasting,
    /// or rarely copy-forward. A tool offering to fix up the old rows would be
    /// offering to destroy the only irreplaceable thing in the system.
    /// </para>
    /// <para>
    /// Its sibling is <c>Wire</c>, which applies a different policy to the same
    /// matrix because a DTO's old shape sits in somebody else's code rather than in
    /// your database.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Events =
        
        /// <summary>
        /// Every stored version that no chain of upcasters can bring to the current
        /// one.
        /// </summary>
        /// <remarks>
        /// Pure graph reachability. For each version present in the snapshot, follow
        /// the declared upcasters and see whether the current version is reached.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Events.unreachable upcasters snapshot
        /// // "'InvoiceRaised' has stored shapes at v1 and v2, and the current shape
        /// //  is v3. Upcasters exist for v2 -> v3. Nothing reads v1."
        /// </code></example>
        val unreachable:
          upcasters: Upcaster list -> snapshot: ShapeSnapshot -> Unresolved list
        
        /// <summary>
        /// Field removals and additions in the same version that might be a rename,
        /// and must be declared one way or the other.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A rename is indistinguishable from a removal plus an addition, and the
        /// difference decides whether stored events can be read. This package's
        /// stated principle is that ambiguity is an error rather than a guess, so
        /// it refuses.
        /// </para>
        /// <para>
        /// Declare the rename, or confirm the two changes are separate by listing
        /// neither — in which case the compatibility report will describe both, as
        /// it should.
        /// </para>
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Events.undeclaredRenames [] previous current
        /// </code></example>
        val undeclaredRenames:
          renames: Rename list ->
            before: ShapeSnapshot -> after: ShapeSnapshot -> Unresolved list
        
        /// <summary>Everything unresolved about a change, as one report.</summary>
        /// <example><code lang="fsharp">
        /// match Events.check upcasters renames previous current with
        /// | [] -> ()
        /// | problems -> failwith (Events.report problems)
        /// </code></example>
        val check:
          upcasters: Upcaster list ->
            renames: Rename list ->
            before: ShapeSnapshot -> after: ShapeSnapshot -> Unresolved list
        
        /// <summary>The unresolved items as prose, one per paragraph.</summary>
        val report: problems: Unresolved list -> string

namespace Etymon
    
    /// <summary>
    /// Which side of a wire contract a shape is on, which decides what can be done
    /// about a break.
    /// </summary>
    /// <remarks>
    /// This is the whole reason wire contracts are a separate policy rather than a
    /// rerun of the event one. An event's old shape sits in your database and you
    /// own the reader, so an upcaster can always rescue it. A DTO's old shape sits
    /// in somebody else's code, and whether it can be rescued depends entirely on
    /// which way it travels.
    /// </remarks>
    [<RequireQualifiedAccess>]
    type WireSide =
        
        /// A body clients send and you read. Old clients send old shapes, so the
        /// direction that matters is backward — and an upcaster can take the old
        /// body and produce the current one.
        | Request
        
        /// A body you send and clients read. You write new shapes and their code
        /// reads them, so the direction that matters is forward — and there is no
        /// upcaster to write, because the code that would run it is not code you
        /// ship.
        | Response
    
    /// <summary>What a wire change means, and for whom.</summary>
    [<NoComparison>]
    type WireVerdict =
        {
          
          /// The shape the verdict is about.
          Contract: string
          
          /// Which side of the wire it is on.
          Side: WireSide
          
          /// What changed, in a sentence.
          Change: string
          
          /// What it means for the people it affects, in sentences.
          Consequence: string
          
          /// Whether anything the author ships can fix it. False for every
          /// response break, because the code that breaks is the client's.
          Fixable: bool
        }
    
    /// <summary>
    /// The wire-contract policy over the shared compatibility matrix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same matrix that <c>Events</c> reads, with a different policy on top,
    /// because a DTO's old shape is not yours to fix.
    /// </para>
    /// <para>
    /// Verdicts name the <em>audience</em> rather than the direction. "Forward" and
    /// "backward" are the two words people reliably get the wrong way round, and a
    /// verdict read at eleven at night is the entire product here.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Wire =
        
        val private audience: side: WireSide -> string
        
        /// The direction that matters for a side. Requests are read by you, so the
        /// risk is to what has already been sent; responses are read by them, so
        /// the risk is to code already deployed elsewhere.
        val private directionFor: side: WireSide -> Direction
        
        val private verdictFor:
          side: WireSide -> change: ShapeChange -> WireVerdict list
        
        /// <summary>
        /// What changing a request body does to clients that have already been
        /// written.
        /// </summary>
        /// <remarks>
        /// Backward compatibility, and the event policy's answer applies almost
        /// unchanged: bodies already in the wild carry the old shape, and an
        /// upcaster can rescue them.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Wire.requests renames previousSnapshot currentSnapshot
        /// </code></example>
        val requests:
          renames: Rename list ->
            before: ShapeSnapshot -> after: ShapeSnapshot -> WireVerdict list
        
        /// <summary>
        /// What changing a response body does to clients that have already been
        /// written.
        /// </summary>
        /// <remarks>
        /// Forward compatibility, and there is no fix. This package offers no hook
        /// for one, because a hook that cannot help is worse than an honest refusal:
        /// it suggests the problem has been handled.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Wire.responses renames previousSnapshot currentSnapshot
        /// </code></example>
        val responses:
          renames: Rename list ->
            before: ShapeSnapshot -> after: ShapeSnapshot -> WireVerdict list
        
        /// <summary>
        /// Only the verdicts nothing you ship can fix, which are the ones that have
        /// to be decided before the change goes out.
        /// </summary>
        /// <example><code lang="fsharp">
        /// match Wire.responses [] previous current |> Wire.unfixable with
        /// | [] -> ()
        /// | breaks -> failwith (Wire.report breaks)
        /// </code></example>
        val unfixable: verdicts: WireVerdict list -> WireVerdict list
        
        /// <summary>
        /// Renames and upcaster gaps on the request side, which must be settled the
        /// same way they are for events.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Request bodies have versions and upcasters for the same reason events do.
        /// Response bodies have neither, which is why this takes only the request
        /// snapshot.
        /// </para>
        /// <para>
        /// The checks are the same as the event ones and the wording is not. An
        /// event's old shapes sit in your store; a request body's were already sent
        /// by somebody's client. Telling a reader looking at an HTTP body that the
        /// question is whether stored events can be read points them at a system
        /// they do not have.
        /// </para>
        /// </remarks>
        /// <example><code lang="fsharp">
        /// match Wire.unresolvedRequests upcasters renames previous current with
        /// | [] -> ()
        /// | problems -> failwith (Wire.reportUnresolved problems)
        /// </code></example>
        val unresolvedRequests:
          upcasters: Upcaster list ->
            renames: Rename list ->
            before: ShapeSnapshot -> after: ShapeSnapshot -> Unresolved list
        
        /// <summary>The unresolved request items as prose, one paragraph each.</summary>
        /// <remarks>
        /// Here so that printing a wire outcome never goes through <c>Events</c>.
        /// Reaching across for the renderer is how the event wording arrived in wire
        /// verdicts in the first place.
        /// </remarks>
        val reportUnresolved: problems: Unresolved list -> string
        
        /// <summary>The verdicts as prose, one paragraph each.</summary>
        /// <remarks>
        /// Named for the audience, not the direction, and it says plainly where
        /// nothing can be done.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// printfn "%s" (Wire.report verdicts)
        /// </code></example>
        val report: verdicts: WireVerdict list -> string

