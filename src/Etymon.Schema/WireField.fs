namespace Etymon

open System
open System.Collections.Generic
open System.Text.Json

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
/// <strong>What a field here says, in all three places.</strong> It is written
/// as <c>"x": null</c> when there is nothing, never as an absent key, because
/// that is what <c>System.Text.Json</c> writes and what every client has been
/// reading. It is described as required with a nullable type, so a generated
/// client expects <c>null</c> rather than <c>undefined</c>, and the
/// compatibility snapshot records a key that is always there. It reads an
/// absent key and a <c>null</c> the same way, because in practice callers mean
/// the same thing by both. It is built on <c>Schema.present</c>;
/// <c>Schema.optional</c> is the different statement, that a key may be missing.
/// </para>
/// <para>
/// The rule that makes adoption mechanical: <strong>a field the record declares
/// nullable is present-and-nullable in the Schema; a field it declares
/// non-nullable is <c>Schema.required</c>.</strong> Take the record at its word.
/// Without that rule the first question on every type is "should this be
/// required?", and the answer drifts across a codebase.
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

    /// Reads an absent or null key as a fallback rather than refusing it, with
    /// the field still described as required. A collection is always written,
    /// as empty when there is nothing, so required is what the wire carries; and
    /// a body that omits it, or writes null, means the same thing by it.
    let private lenient (fallback: 'A) (name: string) (part: ObjectPart<'T, 'A>) : ObjectPart<'T, 'A> =
        { part with
            ReadFields =
                fun mode path element ->
                    match element.TryGetProperty name with
                    | true, property when property.ValueKind <> JsonValueKind.Null -> part.ReadFields mode path element
                    | _ -> Ok fallback
        }

    let private toDictionary (map: Map<string, 'V>) =
        let result = Dictionary<string, 'V>()

        for KeyValue(key, v) in map do
            result[key] <- v

        result

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
    /// <para>
    /// It means the same thing as <c>Schema.nullable</c>: a value that may be
    /// <c>null</c>. The difference is only the shape it binds.
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
        Schema.present
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
    /// The constraint applies to the value when there is one. A <c>null</c> is
    /// not a rule violation; say so with <c>Schema.required</c> if it should be.
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
        Schema.present name Schema.int (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;int64&gt;</c>.</summary>
    let int64 (name: string) (get: 'T -> Nullable<int64>) : ObjectPart<'T, Nullable<int64>> =
        Schema.present name Schema.int64 (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;decimal&gt;</c>. Use this for money rather than float.</summary>
    let decimal (name: string) (get: 'T -> Nullable<decimal>) : ObjectPart<'T, Nullable<decimal>> =
        Schema.present name Schema.decimal (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;float&gt;</c>.</summary>
    let float (name: string) (get: 'T -> Nullable<float>) : ObjectPart<'T, Nullable<float>> =
        Schema.present name Schema.float (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;bool&gt;</c>.</summary>
    let bool (name: string) (get: 'T -> Nullable<bool>) : ObjectPart<'T, Nullable<bool>> =
        Schema.present name Schema.bool (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;Guid&gt;</c>.</summary>
    let guid (name: string) (get: 'T -> Nullable<Guid>) : ObjectPart<'T, Nullable<Guid>> =
        Schema.present name Schema.guid (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;DateOnly&gt;</c>.</summary>
    let dateOnly (name: string) (get: 'T -> Nullable<DateOnly>) : ObjectPart<'T, Nullable<DateOnly>> =
        Schema.present name Schema.dateOnly (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;DateTimeOffset&gt;</c>.</summary>
    let dateTimeOffset (name: string) (get: 'T -> Nullable<DateTimeOffset>) : ObjectPart<'T, Nullable<DateTimeOffset>> =
        Schema.present name Schema.dateTimeOffset (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    // ---- collections ---------------------------------------------------------
    //
    // Reach for WireField because a field is nullable, never because it is a
    // collection. These describe what they write: an array that is always there,
    // empty when there is nothing.

    /// <summary>
    /// A nullable array, read as a list and handed back as an array.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A null array and an absent key are both read as no items, and the value
    /// bound is an empty array rather than null, so it assigns straight into a
    /// nullable field and no call site needs a guard. Empty is what is written
    /// for null, and the field is described as required, because that is what
    /// the wire carries.
    /// </para>
    /// <para>
    /// Where the difference between absent and empty genuinely carries meaning,
    /// say so with <c>Schema.optional</c> and a nullable element type.
    /// </para>
    /// </remarks>
    /// <example><code lang="fsharp">
    /// WireField.array "lines" lineSchema (fun r -> r.Lines)
    /// </code></example>
    let array (name: string) (element: Schema<'F>) (get: 'T -> 'F[] | null) : ObjectPart<'T, 'F[]> =
        Schema.required
            name
            (Schema.list element)
            (fun value ->
                match get value with
                | null -> []
                | items -> List.ofArray items
            )
        |> lenient [] name
        |> rebound List.toArray

    /// <summary>
    /// A nullable dictionary of uniform values, read as a map and handed back as
    /// a dictionary.
    /// </summary>
    /// <remarks>
    /// Takes <c>IDictionary</c> rather than <c>Dictionary</c>, so a record
    /// holding either satisfies it. Null and absent are both no entries, for the
    /// same reason as <c>array</c>, and it is described as required for the same
    /// reason too.
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
        Schema.required
            name
            (Schema.map value)
            (fun item ->
                match get item with
                | null -> Map.empty
                | pairs -> pairs |> Seq.map (fun pair -> pair.Key, pair.Value) |> Map.ofSeq
            )
        |> lenient Map.empty name
        |> rebound toDictionary

    // ---- collections that are always there -----------------------------------

    /// <summary>
    /// An array the record declares non-nullable, read as a list and handed back
    /// as an array.
    /// </summary>
    /// <remarks>
    /// The counterpart to <c>array</c> for a field that is not <c>| null</c>.
    /// Both are described as required, because both always write an array; the
    /// difference is that this one refuses an absent key rather than reading it
    /// as empty. A non-nullable field that a deserialiser would have left null is
    /// the defect this declines to paper over.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// WireField.requiredArray "roles" Schema.string (fun p -> p.Roles)
    /// </code></example>
    let requiredArray (name: string) (element: Schema<'F>) (get: 'T -> 'F[]) : ObjectPart<'T, 'F[]> =
        Schema.required name (Schema.list element) (fun value -> List.ofArray (get value))
        |> rebound List.toArray

    /// <summary>
    /// A dictionary the record declares non-nullable, read as a map and handed
    /// back as a dictionary.
    /// </summary>
    /// <remarks>
    /// The counterpart to <c>dictionary</c>, with the same difference as
    /// <c>requiredArray</c>: an absent key is refused. Takes <c>IDictionary</c>,
    /// so a record holding either kind satisfies it.
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
        |> rebound toDictionary
