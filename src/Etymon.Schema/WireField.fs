namespace Etymon

open System

/// <summary>
/// Fields of a record that is nullable because it came off the wire.
/// </summary>
/// <remarks>
/// <para>
/// A record deserialised by <c>System.Text.Json</c> is nullable throughout —
/// <c>string | null</c>, <c>Nullable&lt;int&gt;</c>, <c>'a[] | null</c> — because a
/// decoder that cannot refuse has to be able to put something in every field.
/// A <c>Schema</c> speaks in <c>option</c>. So every field crosses, and without
/// this the crossing is an <c>Option.ofObj</c> at each one.
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

    /// <summary>A nullable string.</summary>
    /// <example><code lang="fsharp">
    /// WireField.text "billNumber" (fun r -> r.BillNumber)
    /// </code></example>
    let text (name: string) (get: 'T -> string) =
        Schema.optional name Schema.string (fun value -> Option.ofObj (get value))

    /// <summary>A nullable string with rules of its own.</summary>
    /// <example><code lang="fsharp">
    /// WireField.constrainedText "role" (Check.oneOf [ "admin"; "member" ]) (fun r -> r.Role)
    /// </code></example>
    let constrainedText (name: string) (check: ConstraintCheck<string>) (get: 'T -> string) =
        Schema.optional name (Schema.string |> Schema.constrain check) (fun value -> Option.ofObj (get value))

    /// <summary>A <c>Nullable&lt;int&gt;</c>.</summary>
    let int (name: string) (get: 'T -> Nullable<int>) =
        Schema.optional name Schema.int (fun value -> Option.ofNullable (get value))

    /// <summary>A <c>Nullable&lt;int64&gt;</c>.</summary>
    let int64 (name: string) (get: 'T -> Nullable<int64>) =
        Schema.optional name Schema.int64 (fun value -> Option.ofNullable (get value))

    /// <summary>A <c>Nullable&lt;decimal&gt;</c>. Use this for money rather than float.</summary>
    let decimal (name: string) (get: 'T -> Nullable<decimal>) =
        Schema.optional name Schema.decimal (fun value -> Option.ofNullable (get value))

    /// <summary>A <c>Nullable&lt;float&gt;</c>.</summary>
    let float (name: string) (get: 'T -> Nullable<float>) =
        Schema.optional name Schema.float (fun value -> Option.ofNullable (get value))

    /// <summary>A <c>Nullable&lt;bool&gt;</c>.</summary>
    let bool (name: string) (get: 'T -> Nullable<bool>) =
        Schema.optional name Schema.bool (fun value -> Option.ofNullable (get value))

    /// <summary>A <c>Nullable&lt;Guid&gt;</c>.</summary>
    let guid (name: string) (get: 'T -> Nullable<Guid>) =
        Schema.optional name Schema.guid (fun value -> Option.ofNullable (get value))

    /// <summary>A <c>Nullable&lt;DateOnly&gt;</c>.</summary>
    let dateOnly (name: string) (get: 'T -> Nullable<DateOnly>) =
        Schema.optional name Schema.dateOnly (fun value -> Option.ofNullable (get value))

    /// <summary>A <c>Nullable&lt;DateTimeOffset&gt;</c>.</summary>
    let dateTimeOffset (name: string) (get: 'T -> Nullable<DateTimeOffset>) =
        Schema.optional name Schema.dateTimeOffset (fun value -> Option.ofNullable (get value))

    /// <summary>
    /// A nullable array, read as a list.
    /// </summary>
    /// <remarks>
    /// A null array and an absent key are both read as no items, because that is
    /// what a caller means by either. Where the difference matters, say so with
    /// <c>Schema.required</c> and a nullable element type.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// WireField.array "lines" lineSchema (fun r -> r.Lines)
    /// </code></example>
    let array (name: string) (element: Schema<'F>) (get: 'T -> 'F[]) =
        Schema.defaulted
            name
            (Schema.list element)
            []
            (fun value ->
                match get value with
                | null -> []
                | items -> List.ofArray items
            )

    /// <summary>A nullable dictionary of uniform values, read as a map.</summary>
    let dictionary (name: string) (value: Schema<'V>) (get: 'T -> Collections.Generic.IDictionary<string, 'V>) =
        Schema.defaulted
            name
            (Schema.map value)
            Map.empty
            (fun item ->
                match get item with
                | null -> Map.empty
                | pairs -> pairs |> Seq.map (fun pair -> pair.Key, pair.Value) |> Map.ofSeq
            )
