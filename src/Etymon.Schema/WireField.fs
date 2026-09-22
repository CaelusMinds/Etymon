namespace Etymon

open System
open System.Collections.Generic

/// <summary>
/// Fields of a record that is nullable because it came off the wire.
/// </summary>
/// <remarks>
/// <para>
/// A record deserialised by <c>System.Text.Json</c> is nullable throughout —
/// <c>string | null</c>, <c>Nullable&lt;int&gt;</c>, <c>'a[] | null</c> — because a
/// decoder that cannot refuse has to be able to put something in every field.
/// A <c>Schema</c> speaks in <c>option</c>. So every field crosses, twice: once
/// reading the record, and once building it again.
/// </para>
/// <para>
/// <strong>This crosses in both directions.</strong> The getter takes the
/// nullable form, and the value bound in the expression is the nullable form
/// too — so the record is rebuilt by naming its fields, with no
/// <c>Option.toObj</c> at any of them:
/// </para>
/// <code lang="fsharp">
/// Schema.object "CreateBillRequest" {
///     let! billNumber = WireField.text "billNumber" (fun r -> r.BillNumber)
///     and! vendorId   = WireField.guid "vendorId" (fun r -> r.VendorId)
///     and! lines      = WireField.array "lines" lineSchema (fun r -> r.Lines)
///     return { BillNumber = billNumber; VendorId = vendorId; Lines = lines }
/// }
/// </code>
/// <para>
/// A crossing that only went one way would remove the smaller half of the work.
/// </para>
/// <para>
/// <strong>This is not a shorter spelling of <c>Schema.optional</c>.</strong> They
/// say different things:
/// </para>
/// <list type="bullet">
/// <item><c>Schema.optional</c> says <em>this key may be absent</em>. That is a
/// statement about the contract.</item>
/// <item><c>WireField.text</c> says <em>this field is nullable because of how it
/// arrived</em>. That is a statement about the record, and a temporary one.</item>
/// </list>
/// <para>
/// They are spelled the same today and they are not the same idea. Naming the
/// second one means that when those records stop being nullable, this module is
/// the list of everywhere it mattered.
/// </para>
/// <para>
/// The rule that makes adoption mechanical: <strong>a field the record declares
/// nullable is optional in the Schema; a field it declares non-nullable is
/// <c>Schema.required</c>.</strong> Take the record at its word. Without that
/// rule the first question on every type is "should this be required?", and the
/// answer drifts across a codebase.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module WireField =

    /// Rebinds what a field produces, so the expression hands back the shape the
    /// record wants rather than the shape the decoder worked in.
    let private rebound (convert: 'A -> 'B) (part: ObjectPart<'T, 'A>) : ObjectPart<'T, 'B> =
        {
            Fields = part.Fields
            WriteFields = part.WriteFields
            ReadFields = fun mode path element -> part.ReadFields mode path element |> Validation.map convert
        }

    // ---- any schema at all ---------------------------------------------------

    /// <summary>
    /// A nullable field of any <c>Schema</c>. Everything below is a convenient
    /// specialisation of this.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The typed members cover what a deserialiser produces most of the time.
    /// This covers the rest without the module having to know about it: a nested
    /// object that may be null, a string carrying <c>Schema.sensitive</c>, a
    /// <c>Schema.raw</c> passthrough. Without it those fall back to the
    /// <c>Option.ofObj</c> / <c>Option.toObj</c> pair this module exists to
    /// remove, at exactly the fields where going through a <c>Schema</c> matters
    /// most.
    /// </para>
    /// </remarks>
    /// <example><code lang="fsharp">
    /// WireField.nullable "cadence" cadenceSchema (fun r -> r.Cadence)
    /// WireField.nullable "password" (Schema.string |> Schema.sensitive) (fun r -> r.Password)
    /// </code></example>
    let nullable<'T, 'F when 'F: not struct and 'F: not null>
        (name: string)
        (schema: Schema<'F>)
        (get: 'T -> 'F | null)
        : ObjectPart<'T, 'F | null>
        =
        Schema.optional
            name
            schema
            (fun value ->
                // Not Option.ofObj. At the pinned FSharp.Core its constraint is
                // `'T : null`, which an F# record does not satisfy, and the
                // narrowing form arrived in FSharp.Core 9. Boxing and unboxing a
                // reference is a no-op that needs neither.
                match box (get value) with
                | null -> None
                | boxed -> Some(unbox<'F> boxed)
            )
        |> rebound (
            function
            | Some value -> value
            | None -> Unchecked.defaultof<'F | null>
        )

    // ---- reference types -----------------------------------------------------

    /// <summary>A nullable string, read and handed back as one.</summary>
    /// <example><code lang="fsharp">
    /// WireField.text "billNumber" (fun r -> r.BillNumber)
    /// </code></example>
    let text (name: string) (get: 'T -> string | null) : ObjectPart<'T, string | null> = nullable name Schema.string get

    /// <summary>A nullable string with rules of its own.</summary>
    /// <remarks>
    /// The constraint applies to the value when there is one. An absent or null
    /// key is not a rule violation; say so with <c>Schema.required</c> if it
    /// should be.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// WireField.constrainedText "role" (Check.oneOf [ "admin"; "member" ]) (fun r -> r.Role)
    /// </code></example>
    let constrainedText
        (name: string)
        (check: ConstraintCheck<string>)
        (get: 'T -> string | null)
        : ObjectPart<'T, string | null>
        =
        nullable name (Schema.string |> Schema.constrain check) get

    // ---- value types ---------------------------------------------------------
    //
    // Already nullable in the record's own vocabulary, so there is nothing to
    // annotate here -- only the crossing to option and back.

    /// <summary>A <c>Nullable&lt;int&gt;</c>.</summary>
    let int (name: string) (get: 'T -> Nullable<int>) : ObjectPart<'T, Nullable<int>> =
        Schema.optional name Schema.int (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;int64&gt;</c>.</summary>
    let int64 (name: string) (get: 'T -> Nullable<int64>) : ObjectPart<'T, Nullable<int64>> =
        Schema.optional name Schema.int64 (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;decimal&gt;</c>. Use this for money rather than float.</summary>
    let decimal (name: string) (get: 'T -> Nullable<decimal>) : ObjectPart<'T, Nullable<decimal>> =
        Schema.optional name Schema.decimal (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;float&gt;</c>.</summary>
    let float (name: string) (get: 'T -> Nullable<float>) : ObjectPart<'T, Nullable<float>> =
        Schema.optional name Schema.float (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;bool&gt;</c>.</summary>
    let bool (name: string) (get: 'T -> Nullable<bool>) : ObjectPart<'T, Nullable<bool>> =
        Schema.optional name Schema.bool (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;Guid&gt;</c>.</summary>
    let guid (name: string) (get: 'T -> Nullable<Guid>) : ObjectPart<'T, Nullable<Guid>> =
        Schema.optional name Schema.guid (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;DateOnly&gt;</c>.</summary>
    let dateOnly (name: string) (get: 'T -> Nullable<DateOnly>) : ObjectPart<'T, Nullable<DateOnly>> =
        Schema.optional name Schema.dateOnly (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;DateTimeOffset&gt;</c>.</summary>
    let dateTimeOffset (name: string) (get: 'T -> Nullable<DateTimeOffset>) : ObjectPart<'T, Nullable<DateTimeOffset>> =
        Schema.optional name Schema.dateTimeOffset (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    // ---- collections ---------------------------------------------------------

    /// <summary>
    /// A nullable array, read as a list and handed back as an array.
    /// </summary>
    /// <remarks>
    /// A null array and an absent key are both read as no items, and the value
    /// bound is an empty array rather than null, so it assigns straight into a
    /// nullable field. That removes a decision at every call site; where the
    /// difference genuinely matters, say so with <c>Schema.required</c> and a
    /// nullable element type.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// WireField.array "lines" lineSchema (fun r -> r.Lines)
    /// </code></example>
    let array (name: string) (element: Schema<'F>) (get: 'T -> 'F[] | null) : ObjectPart<'T, 'F[]> =
        Schema.defaulted
            name
            (Schema.list element)
            []
            (fun value ->
                match get value with
                | null -> []
                | items -> List.ofArray items
            )
        |> rebound List.toArray

    /// <summary>
    /// A nullable dictionary of uniform values, read as a map and handed back as
    /// a dictionary.
    /// </summary>
    /// <remarks>
    /// Takes <c>IDictionary</c> rather than <c>Dictionary</c>, so a record
    /// holding either satisfies it. Null and absent are both no entries, for the
    /// same reason as <c>array</c>.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// WireField.dictionary "dimensions" Schema.string (fun r -> r.Dimensions)
    /// </code></example>
    let dictionary
        (name: string)
        (value: Schema<'V>)
        (get: 'T -> IDictionary<string, 'V> | null)
        : ObjectPart<'T, Dictionary<string, 'V>>
        =
        Schema.defaulted
            name
            (Schema.map value)
            Map.empty
            (fun item ->
                match get item with
                | null -> Map.empty
                | pairs -> pairs |> Seq.map (fun pair -> pair.Key, pair.Value) |> Map.ofSeq
            )
        |> rebound (fun map ->
            let result = Dictionary<string, 'V>()

            for KeyValue(key, v) in map do
                result[key] <- v

            result
        )
    // ---- collections that are always there -----------------------------------
    //
    // Reach for WireField because a field is nullable, never because it is a
    // collection. A collection the record declares non-nullable is required, and
    // saying so is the whole point -- these exist so that saying so is as short
    // as not saying it.

    /// <summary>
    /// An array that is always present, read as a list and handed back as an
    /// array.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart to <c>array</c> for a field the record declares
    /// non-nullable. Use it whenever the field is not <c>| null</c>, even when
    /// the array is sometimes empty: empty is not absent.
    /// </para>
    /// <para>
    /// Getting this wrong is quiet. <c>array</c> compiles against a non-nullable
    /// field and hands back a non-null array, so nothing complains — but it
    /// describes the field as <strong>optional</strong>, which reaches the
    /// published OpenAPI document as a nullability a client must handle, and the
    /// compatibility snapshot as an optionality that makes removing the field
    /// later look safe when it is breaking.
    /// </para>
    /// </remarks>
    /// <example><code lang="fsharp">
    /// WireField.requiredArray "roles" Schema.string (fun p -> p.Roles)
    /// </code></example>
    let requiredArray (name: string) (element: Schema<'F>) (get: 'T -> 'F[]) : ObjectPart<'T, 'F[]> =
        Schema.required name (Schema.list element) (fun value -> List.ofArray (get value))
        |> rebound List.toArray

    /// <summary>
    /// A dictionary that is always present, read as a map and handed back as a
    /// dictionary.
    /// </summary>
    /// <remarks>
    /// The counterpart to <c>dictionary</c>, and the reason that one is not the
    /// only path: <c>Schema.required name (Schema.map value)</c> is correct but
    /// speaks <c>Map</c>, so a record holding a <c>Dictionary</c> has to convert
    /// in both directions by hand. The correct path should not be the
    /// inconvenient one.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// WireField.requiredDictionary "references" Schema.string (fun p -> p.References)
    /// </code></example>
    let requiredDictionary
        (name: string)
        (value: Schema<'V>)
        (get: 'T -> IDictionary<string, 'V>)
        : ObjectPart<'T, Dictionary<string, 'V>>
        =
        Schema.required
            name
            (Schema.map value)
            (fun item -> get item |> Seq.map (fun pair -> pair.Key, pair.Value) |> Map.ofSeq)
        |> rebound (fun map ->
            let result = Dictionary<string, 'V>()

            for KeyValue(key, v) in map do
                result[key] <- v

            result
        )
