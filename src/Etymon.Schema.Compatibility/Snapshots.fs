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
    // 2 added elementConstraints on every field. A version-1 file still reads;
    // its fields simply have none recorded.
    let FormatVersion = 2

    let private fieldTypeSchema: Schema<FieldType> =
        Schema.recursive
            "FieldType"
            (fun self ->
                Schema.union
                    "FieldType"
                    "kind"
                    [
                        Schema.case
                            "scalar"
                            Schema.string
                            FieldType.Scalar
                            (function
                            | FieldType.Scalar k -> ValueSome k
                            | _ -> ValueNone
                            )

                        Schema.case
                            "sequence"
                            self
                            FieldType.Sequence
                            (function
                            | FieldType.Sequence e -> ValueSome e
                            | _ -> ValueNone
                            )

                        Schema.case
                            "mapping"
                            self
                            FieldType.Mapping
                            (function
                            | FieldType.Mapping v -> ValueSome v
                            | _ -> ValueNone
                            )

                        Schema.case
                            "nullable"
                            self
                            FieldType.Nullable
                            (function
                            | FieldType.Nullable i -> ValueSome i
                            | _ -> ValueNone
                            )

                        Schema.case
                            "nested"
                            Schema.string
                            FieldType.Nested
                            (function
                            | FieldType.Nested n -> ValueSome n
                            | _ -> ValueNone
                            )

                        Schema.case
                            "choice"
                            (Schema.object "Choice" {
                                let! name = Schema.required "name" Schema.string fst
                                and! cases = Schema.required "cases" (Schema.list Schema.string) snd
                                return (name, cases)
                            })
                            FieldType.Choice
                            (function
                            | FieldType.Choice(n, cs) -> ValueSome(n, cs)
                            | _ -> ValueNone
                            )

                        Schema.caseUnit
                            "unknown"
                            FieldType.Unknown
                            (function
                            | FieldType.Unknown -> true
                            | _ -> false
                            )
                    ]
            )

    let private fieldSchema: Schema<ShapeField> =
        Schema.object "ShapeField" {
            let! name = Schema.required "name" Schema.string (fun f -> f.Name)
            and! fieldType = Schema.required "type" fieldTypeSchema (fun f -> f.Type)
            and! required = Schema.required "required" Schema.bool (fun f -> f.Required)

            and! constraints =
                Schema.required "constraints" (Schema.list ConstraintCodec.schema) (fun f -> f.Constraints)

            // Optional, not defaulted. A snapshot written before this key existed
            // never recorded element rules, and reading that as "recorded none"
            // made the first diff after the upgrade a false narrowing on every
            // list field with item rules. Absent reads as None; a written
            // snapshot always carries the key.
            and! elementConstraints =
                Schema.optional
                    "elementConstraints"
                    (Schema.list ConstraintCodec.schema)
                    (fun f -> f.ElementConstraints)

            return
                {
                    Name = name
                    Type = fieldType
                    Required = required
                    Constraints = constraints
                    ElementConstraints = elementConstraints
                }
        }

    let private shapeSchema: Schema<Shape> =
        Schema.object "Shape" {
            let! name = Schema.required "name" Schema.string (fun s -> s.Name)
            and! version = Schema.required "version" Schema.int (fun s -> s.Version)
            and! fields = Schema.required "fields" (Schema.list fieldSchema) (fun s -> s.Fields)

            return
                {
                    Name = name
                    Version = version
                    Fields = fields
                }
        }

    /// <summary>
    /// The snapshot file format, as a <c>Schema</c>.
    /// </summary>
    /// <remarks>
    /// Public so that the format is documentable by the same machinery that
    /// documents everything else: <c>OpenApi.toJsonSchemaText ShapeSnapshots.schema</c>
    /// prints it.
    /// </remarks>
    let schema: Schema<ShapeSnapshot> =
        Schema.object "ShapeSnapshot" {
            let! _ =
                Schema.defaulted "formatVersion" Schema.int FormatVersion (fun _ -> FormatVersion)

            and! shapes = Schema.required "shapes" (Schema.list shapeSchema) (fun s -> s.Shapes)
            return { Shapes = shapes }
        }

    /// <summary>The snapshot as JSON, indented, ready to commit.</summary>
    /// <remarks>
    /// Shapes are written in name and version order, so the same set of shapes
    /// always produces the same file and a real change is not buried in
    /// reordering noise.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// File.WriteAllText("events.snapshot.json", ShapeSnapshots.toJson snapshot)
    /// </code></example>
    let toJson (snapshot: ShapeSnapshot) =
        Schema.toJsonIndented schema (ShapeSnapshot.of' snapshot.Shapes)

    /// <summary>A snapshot read back, or every reason the file could not be read.</summary>
    /// <example><code lang="fsharp">
    /// match ShapeSnapshots.fromJson text with
    /// | Ok snapshot -> ...
    /// | Error errors -> eprintfn "%s" (ValidationErrors.format errors)
    /// // shapes[2].fields[0].name: is required
    /// </code></example>
    let fromJson (text: string) : Validation<ShapeSnapshot> = Schema.fromJson schema text
