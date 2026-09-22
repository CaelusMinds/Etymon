namespace Etymon

open System
open System.Collections.Generic

/// <summary>
/// Fields of a request body, bound to a record that is nullable because it came
/// off the wire.
/// </summary>
/// <remarks>
/// <para>
/// The same DTO shape needs two descriptions, one for each direction.
/// <c>WireField</c> is what you <em>write</em>: every field present, null when
/// there is nothing, described as required with a nullable type, because that
/// is what <c>System.Text.Json</c> writes and what every client has been
/// reading. This module is what you <em>accept</em>: a request body is read,
/// not written, and read leniently — absent and null are the same thing — so
/// the honest description is optional and nullable. Naming both means neither
/// is the other's misuse.
/// </para>
/// <para>
/// Same members, same signatures, same crossing in both directions. A request
/// file uses this module, a response file uses <c>WireField</c>, and a DTO
/// shared by both is described twice, once each way. For a scalar the two
/// descriptions differ in one word, Required. A collection differs in one more:
/// going out it is never written as null, so it is a plain array; coming in it
/// may arrive as null, so it admits one.
/// </para>
/// <para>
/// Built on <c>Schema.defaulted</c> with a null fallback, so the document says
/// <c>"default": null</c>: absent means null, which is what the reader does.
/// The client package encodes with the same schema, so a request it sends
/// carries the key with null rather than omitting it — within the contract this
/// describes. Collections write as empty and default to empty, as in
/// <c>WireField</c>, because a caller reading "lines" should find a list.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module RequestField =

    /// Rebinds what a field produces, so the expression hands back the shape the
    /// record wants rather than the shape the decoder worked in.
    let private rebound (convert: 'A -> 'B) (part: ObjectPart<'T, 'A>) : ObjectPart<'T, 'B> =
        {
            Fields = part.Fields
            WriteFields = part.WriteFields
            ReadFields = fun mode path element -> part.ReadFields mode path element |> Validation.map convert
        }

    let private toDictionary (map: Map<string, 'V>) =
        let result = Dictionary<string, 'V>()

        for KeyValue(key, v) in map do
            result[key] <- v

        result

    // ---- any schema at all ---------------------------------------------------

    /// <summary>
    /// A nullable field of any <c>Schema</c>, optional and nullable in the
    /// description. Everything below is a convenient specialisation of this.
    /// </summary>
    /// <example><code lang="fsharp">
    /// RequestField.nullable "cadence" cadenceSchema (fun r -> r.Cadence)
    /// </code></example>
    let nullable<'T, 'F when 'F: not struct and 'F: not null>
        (name: string)
        (schema: Schema<'F>)
        (get: 'T -> 'F | null)
        : ObjectPart<'T, 'F | null>
        =
        Schema.defaulted
            name
            (Schema.nullable schema)
            None
            (fun value ->
                // Not Option.ofObj: at the pinned FSharp.Core its constraint is
                // `'T : null`, which an F# record does not satisfy. Boxing and
                // unboxing a reference is a no-op that needs no constraint.
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
    /// RequestField.text "billNumber" (fun r -> r.BillNumber)
    /// </code></example>
    let text (name: string) (get: 'T -> string | null) : ObjectPart<'T, string | null> = nullable name Schema.string get

    /// <summary>A nullable string with rules of its own.</summary>
    /// <remarks>
    /// The constraint applies to the value when there is one. Absent and
    /// <c>null</c> are not rule violations; say so with <c>Schema.required</c>
    /// if they should be.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// RequestField.constrainedText "role" (Check.oneOf [ "admin"; "member" ]) (fun r -> r.Role)
    /// </code></example>
    let constrainedText
        (name: string)
        (check: ConstraintCheck<string>)
        (get: 'T -> string | null)
        : ObjectPart<'T, string | null>
        =
        nullable name (Schema.string |> Schema.constrain check) get

    // ---- value types ---------------------------------------------------------

    /// <summary>A <c>Nullable&lt;int&gt;</c>.</summary>
    let int (name: string) (get: 'T -> Nullable<int>) : ObjectPart<'T, Nullable<int>> =
        Schema.defaulted name (Schema.nullable Schema.int) None (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;int64&gt;</c>.</summary>
    let int64 (name: string) (get: 'T -> Nullable<int64>) : ObjectPart<'T, Nullable<int64>> =
        Schema.defaulted name (Schema.nullable Schema.int64) None (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;decimal&gt;</c>. Use this for money rather than float.</summary>
    let decimal (name: string) (get: 'T -> Nullable<decimal>) : ObjectPart<'T, Nullable<decimal>> =
        Schema.defaulted name (Schema.nullable Schema.decimal) None (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;float&gt;</c>.</summary>
    let float (name: string) (get: 'T -> Nullable<float>) : ObjectPart<'T, Nullable<float>> =
        Schema.defaulted name (Schema.nullable Schema.float) None (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;bool&gt;</c>.</summary>
    let bool (name: string) (get: 'T -> Nullable<bool>) : ObjectPart<'T, Nullable<bool>> =
        Schema.defaulted name (Schema.nullable Schema.bool) None (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;Guid&gt;</c>.</summary>
    let guid (name: string) (get: 'T -> Nullable<Guid>) : ObjectPart<'T, Nullable<Guid>> =
        Schema.defaulted name (Schema.nullable Schema.guid) None (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;DateOnly&gt;</c>.</summary>
    let dateOnly (name: string) (get: 'T -> Nullable<DateOnly>) : ObjectPart<'T, Nullable<DateOnly>> =
        Schema.defaulted name (Schema.nullable Schema.dateOnly) None (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    /// <summary>A <c>Nullable&lt;DateTimeOffset&gt;</c>.</summary>
    let dateTimeOffset (name: string) (get: 'T -> Nullable<DateTimeOffset>) : ObjectPart<'T, Nullable<DateTimeOffset>> =
        Schema.defaulted name (Schema.nullable Schema.dateTimeOffset) None (fun value -> Option.ofNullable (get value))
        |> rebound Option.toNullable

    // ---- collections ---------------------------------------------------------

    /// <summary>
    /// A nullable array, read as a list and handed back as an array. Optional
    /// in the description, admitting null, defaulting to empty.
    /// </summary>
    /// <example><code lang="fsharp">
    /// RequestField.array "lines" lineSchema (fun r -> r.Lines)
    /// </code></example>
    let array (name: string) (element: Schema<'F>) (get: 'T -> 'F[] | null) : ObjectPart<'T, 'F[]> =
        Schema.defaulted
            name
            (Schema.nullable (Schema.list element))
            (Some [])
            (fun value ->
                match get value with
                | null -> Some []
                | items -> Some(List.ofArray items)
            )
        |> rebound (Option.defaultValue [] >> List.toArray)

    /// <summary>
    /// A nullable dictionary of uniform values, read as a map and handed back as
    /// a dictionary. Optional in the description, admitting null, defaulting to
    /// empty.
    /// </summary>
    /// <example><code lang="fsharp">
    /// RequestField.dictionary "dimensions" Schema.string (fun r -> r.Dimensions)
    /// </code></example>
    let dictionary
        (name: string)
        (value: Schema<'V>)
        (get: 'T -> IDictionary<string, 'V> | null)
        : ObjectPart<'T, Dictionary<string, 'V>>
        =
        Schema.defaulted
            name
            (Schema.nullable (Schema.map value))
            (Some Map.empty)
            (fun item ->
                match get item with
                | null -> Some Map.empty
                | pairs -> Some(pairs |> Seq.map (fun pair -> pair.Key, pair.Value) |> Map.ofSeq)
            )
        |> rebound (Option.defaultValue Map.empty >> toDictionary)

    // ---- collections that are always there -----------------------------------
    //
    // Required is required in both directions. These are WireField's, so that a
    // request file needs one module.

    /// <summary>An array the record declares non-nullable; see <c>WireField.requiredArray</c>.</summary>
    let requiredArray (name: string) (element: Schema<'F>) (get: 'T -> 'F[]) : ObjectPart<'T, 'F[]> =
        WireField.requiredArray name element get

    /// <summary>A dictionary the record declares non-nullable; see <c>WireField.requiredDictionary</c>.</summary>
    let requiredDictionary
        (name: string)
        (value: Schema<'V>)
        (get: 'T -> IDictionary<string, 'V>)
        : ObjectPart<'T, Dictionary<string, 'V>>
        =
        WireField.requiredDictionary name value get
