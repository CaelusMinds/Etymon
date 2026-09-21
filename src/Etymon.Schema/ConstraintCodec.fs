namespace Etymon

/// <summary>
/// The <see cref="T:Etymon.Constraint"/> vocabulary, as a <c>Schema</c>.
/// </summary>
/// <remarks>
/// <para>
/// Constraints are data, and more than one package needs to write them to a
/// file and read them back: the table snapshot in <c>Etymon.Schema.Sql</c> and
/// the event snapshot in <c>Etymon.Schema.Compatibility</c> both record the rules a
/// value must satisfy. Two hand-written copies of one format is the drift this
/// suite exists to prevent, so there is one definition and both use it.
/// </para>
/// <para>
/// Public so a consumer recording constraints in a file of its own can do the
/// same rather than inventing a third spelling.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module ConstraintCodec =

    /// A numeric bound, tagged by the kind of number it holds.
    let numSchema: Schema<Num> =
        Schema.union
            "Num"
            "kind"
            [
                Schema.case
                    "int"
                    Schema.int64
                    Num.Int
                    (function
                    | Num.Int v -> ValueSome v
                    | _ -> ValueNone
                    )
                Schema.case
                    "decimal"
                    Schema.decimal
                    Num.Dec
                    (function
                    | Num.Dec v -> ValueSome v
                    | _ -> ValueNone
                    )
                Schema.case
                    "float"
                    Schema.float
                    Num.Float
                    (function
                    | Num.Float v -> ValueSome v
                    | _ -> ValueNone
                    )
            ]

    /// A format is carried by its JSON Schema name, which is the spelling every
    /// other part of the suite already uses for it.
    /// A named format, such as email or uuid.
    let formatSchema: Schema<Format> =
        let byName =
            [
                Format.Email
                Format.Uuid
                Format.DateTime
                Format.Date
                Format.Time
                Format.Duration
                Format.Uri
                Format.UriReference
                Format.Hostname
                Format.Ipv4
                Format.Ipv6
            ]
            |> List.map (fun f -> Format.name f, f)

        Schema.string
        |> Schema.convert
            (fun name ->
                byName
                |> List.tryFind (fun (known, _) -> known = name)
                |> Option.map snd
                |> Option.defaultValue (Format.Custom name)
            )
            Format.name

    /// Constraints are written to the snapshot because they are part of the shape
    /// the database is in: a CHECK that exists is something a later diff has to
    /// be able to see. An earlier draft left them out on the grounds that the
    /// schema is the source of truth, which confused "where the rule is declared"
    /// with "what the database currently has".
    /// One constraint, tagged by which rule it is.
    let schema: Schema<Constraint> =
        let optionalInt = Schema.nullable Schema.int
        let optionalNum = Schema.nullable numSchema

        Schema.union
            "Constraint"
            "kind"
            [
                Schema.case
                    "length"
                    (Schema.object "Length" {
                        let! min = Schema.required "min" optionalInt fst
                        and! max = Schema.required "max" optionalInt snd
                        return (min, max)
                    })
                    Constraint.Length
                    (function
                    | Constraint.Length(min, max) -> ValueSome(min, max)
                    | _ -> ValueNone
                    )

                Schema.case
                    "range"
                    (Schema.object "Range" {
                        let! min = Schema.required "min" optionalNum (fun (a, _, _, _) -> a)
                        and! max = Schema.required "max" optionalNum (fun (_, b, _, _) -> b)

                        and! exclusiveMin =
                            Schema.required "exclusiveMin" Schema.bool (fun (_, _, c, _) -> c)

                        and! exclusiveMax =
                            Schema.required "exclusiveMax" Schema.bool (fun (_, _, _, d) -> d)

                        return (min, max, exclusiveMin, exclusiveMax)
                    })
                    Constraint.Range
                    (function
                    | Constraint.Range(a, b, c, d) -> ValueSome(a, b, c, d)
                    | _ -> ValueNone
                    )

                Schema.case
                    "pattern"
                    Schema.string
                    Constraint.Pattern
                    (function
                    | Constraint.Pattern v -> ValueSome v
                    | _ -> ValueNone
                    )

                Schema.case
                    "format"
                    formatSchema
                    Constraint.HasFormat
                    (function
                    | Constraint.HasFormat v -> ValueSome v
                    | _ -> ValueNone
                    )

                Schema.case
                    "oneOf"
                    (Schema.list Schema.string)
                    Constraint.OneOf
                    (function
                    | Constraint.OneOf v -> ValueSome v
                    | _ -> ValueNone
                    )

                Schema.case
                    "items"
                    (Schema.object "Items" {
                        let! min = Schema.required "min" optionalInt (fun (a, _, _) -> a)
                        and! max = Schema.required "max" optionalInt (fun (_, b, _) -> b)
                        and! unique = Schema.required "unique" Schema.bool (fun (_, _, c) -> c)
                        return (min, max, unique)
                    })
                    Constraint.Items
                    (function
                    | Constraint.Items(a, b, c) -> ValueSome(a, b, c)
                    | _ -> ValueNone
                    )

                Schema.case
                    "opaque"
                    (Schema.object "Opaque" {
                        let! code = Schema.required "code" Schema.string fst
                        and! description = Schema.required "description" Schema.string snd
                        return (code, description)
                    })
                    Constraint.Opaque
                    (function
                    | Constraint.Opaque(a, b) -> ValueSome(a, b)
                    | _ -> ValueNone
                    )
            ]
