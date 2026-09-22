namespace Etymon

open System
open System.Globalization
open System.Text
open System.Text.Json
open System.Text.Json.Nodes

/// <summary>
/// The builder behind <c>Schema.object</c>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately applicative and not monadic: it provides <c>let!</c> with
/// <c>and!</c>, and no <c>Bind</c>. That is not an omission. A field whose
/// decoding depended on another field's <em>value</em> could not be described
/// statically, and static description is the entire point — so the type system
/// forbids it rather than leaving it to a convention.
/// </para>
/// <para>
/// The practical consequence is that every field's errors accumulate, because
/// nothing can short-circuit.
/// </para>
/// </remarks>
[<Sealed>]
type ObjectBuilder<'T>(name: string) =

    /// Combines two field declarations, concatenating their descriptions and
    /// collecting errors from both.
    member _.MergeSources(left: ObjectPart<'T, 'A>, right: ObjectPart<'T, 'B>) : ObjectPart<'T, 'A * 'B> =
        {
            Fields = left.Fields @ right.Fields
            WriteFields =
                fun writer value ->
                    left.WriteFields writer value
                    right.WriteFields writer value
            ReadFields =
                fun mode path element ->
                    Validation.zip (left.ReadFields mode path element) (right.ReadFields mode path element)
        }

    /// Turns the accumulated fields into a schema, using the body of the
    /// expression to assemble the value.
    member _.BindReturn(part: ObjectPart<'T, 'A>, assemble: 'A -> 'T) : Schema<'T> =
        {
            Write =
                fun writer value ->
                    writer.WriteStartObject()
                    part.WriteFields writer value
                    writer.WriteEndObject()
            Read =
                fun mode path element ->
                    match element.ValueKind with
                    | JsonValueKind.Object -> part.ReadFields mode path element |> Validation.map assemble
                    | _ -> Codec.mismatch "object" path element
            Info = SObject(name, part.Fields)
        }

    /// An object with no fields at all.
    member _.Return(value: 'T) : Schema<'T> =
        {
            Write =
                fun writer _ ->
                    writer.WriteStartObject()
                    writer.WriteEndObject()
            Read =
                fun _ path element ->
                    match element.ValueKind with
                    | JsonValueKind.Object -> Ok value
                    | _ -> Codec.mismatch "object" path element
            Info = SObject(name, [])
        }

/// Building and running <see cref="T:Etymon.Schema`1"/> values.
[<RequireQualifiedAccess>]
module Schema =

    let private inv = CultureInfo.InvariantCulture

    // ---- primitives ----------------------------------------------------------

    let private prim kind write read : Schema<'T> =
        {
            Write = write
            Read = read
            Info = SPrim kind
        }

    /// <summary>A JSON string.</summary>
    /// <example><code lang="fsharp">
    /// Schema.toJson Schema.string "hi" // "\"hi\""
    /// </code></example>
    let string: Schema<string> =
        prim
            PrimKind.String
            (fun w v -> w.WriteStringValue v)
            (fun mode path element ->
                match Codec.coercibleText mode element with
                | Some text -> Ok text
                | None -> Codec.mismatch "string" path element
            )

    let private numeric kind (write: Utf8JsonWriter -> 'T -> unit) (ofElement: JsonElement -> 'T option) parse =
        prim
            kind
            write
            (fun mode path element ->
                match element.ValueKind with
                | JsonValueKind.Number ->
                    match ofElement element with
                    | Some value -> Ok value
                    | None -> Codec.rejected "out-of-range" "is outside the range this field can hold" path
                | JsonValueKind.String when mode = DecodeMode.Coercing ->
                    match parse (element.GetString()) with
                    | Some value -> Ok value
                    | None -> Codec.mismatch "number" path element
                | _ -> Codec.mismatch "number" path element
            )

    /// <summary>A 32-bit integer.</summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJson Schema.int "42" // Ok 42
    /// </code></example>
    let int: Schema<int> =
        numeric
            PrimKind.Int
            (fun w v -> w.WriteNumberValue v)
            (fun e ->
                match e.TryGetInt32() with
                | true, v -> Some v
                | false, _ -> None
            )
            Parse.int

    /// <summary>A 64-bit integer.</summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJson Schema.int64 "9007199254740993" // Ok 9007199254740993L
    /// </code></example>
    let int64: Schema<int64> =
        numeric
            PrimKind.Int64
            (fun w v -> w.WriteNumberValue v)
            (fun e ->
                match e.TryGetInt64() with
                | true, v -> Some v
                | false, _ -> None
            )
            Parse.int64

    /// <summary>
    /// A double-precision number. NaN and the infinities are rejected on the way
    /// in and cannot be written, because JSON has no way to express them.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJson Schema.float "1.5" // Ok 1.5
    /// </code></example>
    let float: Schema<float> =
        numeric
            PrimKind.Float
            (fun w v -> w.WriteNumberValue v)
            (fun e ->
                match e.TryGetDouble() with
                | true, v when Double.IsFinite v -> Some v
                | _ -> None
            )
            Parse.float

    /// <summary>An exact decimal. Use this for money rather than <c>float</c>.</summary>
    /// <example><code lang="fsharp">
    /// Schema.toJson Schema.decimal 1.50m // "1.50"
    /// </code></example>
    let decimal: Schema<decimal> =
        numeric
            PrimKind.Decimal
            (fun w v -> w.WriteNumberValue v)
            (fun e ->
                match e.TryGetDecimal() with
                | true, v -> Some v
                | false, _ -> None
            )
            Parse.decimal

    /// <summary>A boolean.</summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJson Schema.bool "true" // Ok true
    /// </code></example>
    let bool: Schema<bool> =
        prim
            PrimKind.Bool
            (fun w v -> w.WriteBooleanValue v)
            (fun mode path element ->
                match element.ValueKind with
                | JsonValueKind.True -> Ok true
                | JsonValueKind.False -> Ok false
                | JsonValueKind.String when mode = DecodeMode.Coercing ->
                    match Parse.bool (element.GetString()) with
                    | Some value -> Ok value
                    | None -> Codec.mismatch "boolean" path element
                | _ -> Codec.mismatch "boolean" path element
            )

    // A scalar carried as a string: written by rendering, read by parsing.
    //
    // A plain comment rather than a doc comment. The compiler puts a documented
    // private member into the XML, where a reader of the package takes it for
    // something they can call. The public way to carry a coded value is
    // Schema.string |> Schema.constrain (Check.oneOf codes) |> Schema.convert,
    // which also publishes the code set as an enum.
    let private stringly kind (render: 'T -> string) (parse: string -> 'T option) (expected: string) : Schema<'T> =
        prim
            kind
            (fun w v -> w.WriteStringValue(render v))
            (fun mode path element ->
                match Codec.coercibleText mode element with
                | Some text ->
                    match parse text with
                    | Some value -> Ok value
                    | None -> Codec.rejected expected $"must be a valid %s{expected}" path
                | None -> Codec.mismatch "string" path element
            )

    /// <summary>A UUID, carried as a canonical 8-4-4-4-12 string.</summary>
    /// <example><code lang="fsharp">
    /// Schema.toJson Schema.guid Guid.Empty // "\"00000000-0000-0000-0000-000000000000\""
    /// </code></example>
    let guid: Schema<Guid> =
        stringly PrimKind.Guid (fun (v: Guid) -> v.ToString "D") Parse.guid "uuid"

    /// <summary>An RFC 3339 timestamp.</summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJson Schema.dateTimeOffset "\"2026-09-20T14:30:00Z\"" // Ok ...
    /// </code></example>
    let dateTimeOffset: Schema<DateTimeOffset> =
        // UTC is rendered "…Z" rather than "…+00:00". Both are valid RFC 3339, but
        // Z is the canonical spelling, it is what every other system emits, and it
        // avoids a '+' -- which the default JSON encoder escapes to +,
        // producing valid but startling output.
        let render (v: DateTimeOffset) =
            if v.Offset = TimeSpan.Zero then
                v.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", inv) + "Z"
            else
                v.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFK", inv)

        stringly PrimKind.DateTimeOffset render Parse.dateTimeOffset "date-time"

    /// <summary>An ISO 8601 date.</summary>
    /// <example><code lang="fsharp">
    /// Schema.toJson Schema.dateOnly (DateOnly(2026, 9, 20)) // "\"2026-09-20\""
    /// </code></example>
    let dateOnly: Schema<DateOnly> =
        stringly PrimKind.DateOnly (fun (v: DateOnly) -> v.ToString("yyyy-MM-dd", inv)) Parse.dateOnly "date"

    /// <summary>An ISO 8601 wall-clock time.</summary>
    /// <example><code lang="fsharp">
    /// Schema.toJson Schema.timeOnly (TimeOnly(14, 30)) // "\"14:30:00\""
    /// </code></example>
    let timeOnly: Schema<TimeOnly> =
        stringly PrimKind.TimeOnly (fun (v: TimeOnly) -> v.ToString("HH:mm:ss.FFFFFFF", inv)) Parse.timeOnly "time"

    /// <summary>A duration, in .NET's <c>[d.]hh:mm:ss</c> form.</summary>
    /// <example><code lang="fsharp">
    /// Schema.toJson Schema.timeSpan (TimeSpan.FromHours 1.0) // "\"01:00:00\""
    /// </code></example>
    let timeSpan: Schema<TimeSpan> =
        stringly PrimKind.TimeSpan (fun (v: TimeSpan) -> v.ToString("c", inv)) Parse.timeSpan "duration"

    /// <summary>Binary data, carried as base64.</summary>
    /// <example><code lang="fsharp">
    /// Schema.toJson Schema.bytes [| 1uy; 2uy |] // "\"AQI=\""
    /// </code></example>
    let bytes: Schema<byte[]> =
        prim
            PrimKind.Bytes
            (fun w (v: byte[]) -> w.WriteBase64StringValue(ReadOnlySpan<byte>(v)))
            (fun _ path element ->
                match element.ValueKind with
                | JsonValueKind.String ->
                    match element.TryGetBytesFromBase64() with
                    | true, value -> Ok value
                    | false, _ -> Codec.rejected "base64" "must be valid base64" path
                | _ -> Codec.mismatch "string" path element
            )

    /// <summary>
    /// Arbitrary JSON, passed through untouched. The escape hatch for a payload
    /// whose shape genuinely is not known ahead of time.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJson Schema.raw """{"anything":1}""" // Ok (a JsonNode)
    /// </code></example>
    let raw: Schema<JsonNode> =
        prim
            PrimKind.Raw
            // ReferenceEquals rather than isNull: JsonNode.Parse can hand back
            // a null at run time even though the type does not admit one, and
            // writing it as JSON null is the only sensible reading. Saying so
            // through the type would mean Schema<JsonNode | null>, which every
            // caller would then have to carry.
            (fun w (v: JsonNode) ->
                if obj.ReferenceEquals(v, null) then
                    w.WriteNullValue()
                else
                    v.WriteTo w
            )
            (fun _ _ element -> Ok(JsonNode.Parse(element.GetRawText())))

    // ---- metadata ------------------------------------------------------------

    let private annotate (f: Meta -> Meta) (schema: Schema<'T>) =
        let updated =
            match schema.Info with
            | SAnnotated(inner, m) -> SAnnotated(inner, f m)
            | other -> SAnnotated(other, f Meta.empty)

        { schema with Info = updated }

    /// <summary>
    /// Gives a schema a name, making it a reusable component in generated output
    /// rather than something inlined at every use.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.string |> Schema.named "Slug"
    /// </code></example>
    let named (name: string) (schema: Schema<'T>) =
        annotate (fun m -> { m with Name = Some name }) schema

    /// <summary>Describes, in prose, what a value means.</summary>
    /// <example><code lang="fsharp">
    /// Schema.int |> Schema.describe "Age in whole years"
    /// </code></example>
    let describe (description: string) (schema: Schema<'T>) =
        annotate
            (fun m ->
                { m with
                    Description = Some description
                }
            )
            schema

    /// <summary>
    /// Marks a value as a secret. Etymon redacts it wherever Etymon renders it;
    /// it cannot stop your own code from printing it.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.string |> Schema.sensitive
    /// </code></example>
    let sensitive (schema: Schema<'T>) =
        annotate (fun m -> { m with Sensitive = true }) schema

    /// <summary>
    /// Adds a rule. It is enforced on decode and carried, as data, into
    /// everything derived from the schema.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.int |> Schema.constrain (Check.intRange (Some 0) (Some 130))
    /// </code></example>
    let constrain (check: ConstraintCheck<'T>) (schema: Schema<'T>) =
        let constrained =
            { schema with
                Read =
                    fun mode path element -> schema.Read mode path element |> Validation.bind (Check.apply check path)
            }

        annotate
            (fun m ->
                { m with
                    Constraints = m.Constraints @ [ check.Constraint ]
                }
            )
            constrained

    // ---- running -------------------------------------------------------------

    /// <summary>What a schema says about the shape it describes.</summary>
    /// <example><code lang="fsharp">
    /// Schema.info Schema.int // SPrim PrimKind.Int
    /// </code></example>
    let info (schema: Schema<'T>) = schema.Info

    /// <summary>Writes a value onto an existing writer.</summary>
    /// <example><code lang="fsharp">
    /// Schema.writeJson Schema.int writer 42
    /// </code></example>
    let writeJson (schema: Schema<'T>) (writer: Utf8JsonWriter) (value: 'T) = schema.Write writer value

    /// <summary>Encodes a value as UTF-8 bytes, without going through a string.</summary>
    /// <example><code lang="fsharp">
    /// Schema.toJsonUtf8 Schema.int 42 // [| 52uy; 50uy |]
    /// </code></example>
    let toJsonUtf8 (schema: Schema<'T>) (value: 'T) = Codec.toUtf8 schema.Write false value

    /// <summary>Encodes a value as compact JSON.</summary>
    /// <example><code lang="fsharp">
    /// Schema.toJson personSchema person // """{"name":"Ada","age":36}"""
    /// </code></example>
    let toJson (schema: Schema<'T>) (value: 'T) =
        Codec.toText Codec.defaultOptions schema.Write value

    /// <summary>Encodes a value as indented JSON, for something a person will read.</summary>
    /// <example><code lang="fsharp">
    /// Schema.toJsonIndented personSchema person
    /// </code></example>
    let toJsonIndented (schema: Schema<'T>) (value: 'T) =
        Codec.toText Codec.indentedOptions schema.Write value

    /// <summary>
    /// Encodes a value with writer options of your choosing.
    /// </summary>
    /// <remarks>
    /// The default encoder escapes characters that are dangerous inside HTML, so
    /// a <c>+</c> in a timestamp offset comes out as <c>+</c>. That is valid
    /// JSON and reads back identically, but if the output is for a person and is
    /// never going into a web page, <c>JavaScriptEncoder.UnsafeRelaxedJsonEscaping</c>
    /// is easier to look at.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// let relaxed = JsonWriterOptions(Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
    /// Schema.toJsonWith relaxed personSchema person
    /// </code></example>
    let toJsonWith (options: JsonWriterOptions) (schema: Schema<'T>) (value: 'T) =
        Codec.toText options schema.Write value

    /// <summary>Decodes an already-parsed element, in a given mode.</summary>
    /// <example><code lang="fsharp">
    /// Schema.decodeWith DecodeMode.Coercing Schema.int element // accepts "42"
    /// </code></example>
    let decodeWith (mode: DecodeMode) (schema: Schema<'T>) (element: JsonElement) = schema.Read mode Path.root element

    /// <summary>Decodes an already-parsed element, strictly.</summary>
    /// <example><code lang="fsharp">
    /// Schema.decode Schema.int element
    /// </code></example>
    let decode (schema: Schema<'T>) (element: JsonElement) =
        decodeWith DecodeMode.Strict schema element

    /// <summary>Parses and decodes, in a given mode.</summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJsonWith DecodeMode.Coercing Schema.int "\"42\"" // Ok 42
    /// </code></example>
    let fromJsonWith (mode: DecodeMode) (schema: Schema<'T>) (json: string) : Validation<'T> =
        try
            use document = JsonDocument.Parse json
            decodeWith mode schema document.RootElement
        with :? JsonException as e ->
            // Malformed JSON is one failure, not a list of them: there is nothing
            // to accumulate when the text could not be read at all.
            Validation.error Path.root (ErrorReason.Rejected "malformed-json") e.Message

    /// <summary>
    /// Parses and decodes, strictly, reporting every problem at once.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJson personSchema """{"age":-3}"""
    /// // Error, both of:
    /// //   name: is required
    /// //   age: must be between 0 and 130
    /// </code></example>
    let fromJson (schema: Schema<'T>) (json: string) =
        fromJsonWith DecodeMode.Strict schema json

    /// <summary>Parses and decodes UTF-8 bytes, strictly.</summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJsonUtf8 personSchema payload
    /// </code></example>
    let fromJsonUtf8 (schema: Schema<'T>) (json: byte[]) : Validation<'T> =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory<byte>(json))
            decode schema document.RootElement
        with :? JsonException as e ->
            Validation.error Path.root (ErrorReason.Rejected "malformed-json") e.Message

    /// <summary>
    /// Checks a value against the schema's rules without any JSON changing hands.
    /// </summary>
    /// <remarks>
    /// Implemented by encoding and decoding again, which is not free but is
    /// guaranteed to apply exactly the rules a real decode would — a separate
    /// validation path would be a second place for the rules to live, and would
    /// eventually disagree.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Schema.validate ageSchema 200 |> Validation.errorCount // 1
    /// </code></example>
    let validate (schema: Schema<'T>) (value: 'T) : Validation<'T> =
        fromJsonUtf8 schema (toJsonUtf8 schema value)

    /// <summary>
    /// Adds an example, encoded with the schema itself so that it cannot be an
    /// example of something the schema would reject.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.int |> Schema.example 42
    /// </code></example>
    let example (value: 'T) (schema: Schema<'T>) =
        let encoded = JsonNode.Parse(toJson schema value)

        annotate
            (fun m ->
                { m with
                    Examples = m.Examples @ [ encoded ]
                }
            )
            schema

    // ---- transformation ------------------------------------------------------

    /// <summary>
    /// Changes the type a schema describes, where the change cannot fail and goes
    /// both ways.
    /// </summary>
    /// <remarks>
    /// Called <c>convert</c> rather than <c>bimap</c>, which it was: in every
    /// other library <c>bimap</c> maps the two sides of a two-parameter type,
    /// such as the success and the failure of a result. This maps one type to
    /// another and back, which is a different thing wearing a familiar name.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Schema.string |> Schema.convert Secret.create Secret.reveal
    /// </code></example>
    let convert (forward: 'A -> 'B) (backward: 'B -> 'A) (schema: Schema<'A>) : Schema<'B> =
        {
            Write = fun w v -> schema.Write w (backward v)
            Read = fun mode path element -> schema.Read mode path element |> Validation.map forward
            Info = schema.Info
        }

    /// <summary>
    /// Lifts a constrained type from <c>Etymon.Core</c> into a schema. The
    /// refinement's rules become the schema's constraints, so that JSON Schema,
    /// SQL and generators all read the same declaration instead of restating it.
    /// </summary>
    /// <example><code lang="fsharp">
    /// let emailSchema = Schema.string |> Schema.refine Email.refinement
    /// Schema.fromJson emailSchema "\"nope\"" // Error [ "must be a valid email" ]
    /// </code></example>
    let refine (refinement: Refinement<'A, 'B>) (schema: Schema<'A>) : Schema<'B> =
        let refined =
            {
                Write = fun w v -> schema.Write w (refinement.Extract v)
                Read =
                    fun mode path element ->
                        schema.Read mode path element
                        |> Validation.bind (fun value -> Refine.createAt path refinement value)
                Info = schema.Info
            }

        annotate
            (fun m ->
                { m with
                    Name = Option.orElse (Some refinement.Name) m.Name
                    Constraints = m.Constraints @ refinement.Constraints
                }
            )
            refined

    // ---- structure -----------------------------------------------------------

    /// <summary>
    /// A value that may be JSON <c>null</c>. Distinct from an optional field,
    /// which may be absent entirely: JSON Schema, OpenAPI and TypeScript all treat
    /// those as different things, so Etymon does too.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJson (Schema.nullable Schema.string) "null" // Ok None
    /// </code></example>
    let nullable (schema: Schema<'T>) : Schema<'T option> =
        {
            Write =
                fun w v ->
                    match v with
                    | Some value -> schema.Write w value
                    | None -> w.WriteNullValue()
            Read =
                fun mode path element ->
                    match element.ValueKind with
                    | JsonValueKind.Null -> Ok None
                    | _ -> schema.Read mode path element |> Validation.map Some
            Info = SNullable schema.Info
        }

    /// <summary>
    /// An ordered collection. Every element is read, so a list with three bad
    /// entries reports three errors, each carrying its index.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJson (Schema.list Schema.int) "[1,\"x\",\"y\"]" |> Validation.errorCount // 2
    /// </code></example>
    let list (schema: Schema<'T>) : Schema<'T list> =
        {
            Write =
                fun w items ->
                    w.WriteStartArray()

                    for item in items do
                        schema.Write w item

                    w.WriteEndArray()
            Read =
                fun mode path element ->
                    match element.ValueKind with
                    | JsonValueKind.Array ->
                        element.EnumerateArray()
                        |> Seq.toList
                        |> List.mapi (fun i item ->
                            schema.Read mode Path.root item
                            |> Validation.mapErrors (ValidationErrors.underIndex i)
                        )
                        |> Validation.sequence
                    | _ -> Codec.mismatch "array" path element
            Info = SList schema.Info
        }

    /// <summary>An ordered collection, as an array.</summary>
    /// <example><code lang="fsharp">
    /// Schema.toJson (Schema.array Schema.int) [| 1; 2 |] // "[1,2]"
    /// </code></example>
    let array (schema: Schema<'T>) : Schema<'T[]> =
        list schema |> convert List.toArray Array.toList

    /// <summary>
    /// A JSON object used as a string-keyed map of uniform values. Keys are
    /// written in order, so encoding the same map twice produces identical bytes.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.fromJson (Schema.map Schema.int) """{"a":1}""" // Ok (Map [ "a", 1 ])
    /// </code></example>
    let map (schema: Schema<'V>) : Schema<Map<string, 'V>> =
        {
            Write =
                fun w items ->
                    w.WriteStartObject()

                    for KeyValue(key, value) in items do
                        w.WritePropertyName key
                        schema.Write w value

                    w.WriteEndObject()
            Read =
                fun mode path element ->
                    match element.ValueKind with
                    | JsonValueKind.Object ->
                        element.EnumerateObject()
                        |> Seq.toList
                        |> List.map (fun property ->
                            schema.Read mode Path.root property.Value
                            |> Validation.mapErrors (ValidationErrors.underField property.Name)
                            |> Validation.map (fun value -> property.Name, value)
                        )
                        |> Validation.sequence
                        |> Validation.map Map.ofList
                    | _ -> Codec.mismatch "object" path element
            Info = SMap schema.Info
        }

    // ---- objects -------------------------------------------------------------

    /// <summary>
    /// Starts describing an object. Use it as a computation expression, declaring
    /// each field with <c>let!</c> and <c>and!</c>, and assembling the value in
    /// the <c>return</c>.
    /// </summary>
    /// <example><code lang="fsharp">
    /// let personSchema =
    ///     Schema.object "Person" {
    ///         let! name = Schema.required "name" Schema.string (fun p -> p.Name)
    ///         and! age = Schema.required "age" Schema.int (fun p -> p.Age)
    ///         and! email = Schema.optional "email" Schema.string (fun p -> p.Email)
    ///         return { Name = name; Age = age; Email = email }
    ///     }
    /// </code></example>
    let object<'T> (name: string) = ObjectBuilder<'T>(name)

    /// <summary>A field that must be present.</summary>
    /// <example><code lang="fsharp">
    /// Schema.required "name" Schema.string (fun p -> p.Name)
    /// </code></example>
    let required (name: string) (schema: Schema<'F>) (get: 'T -> 'F) : ObjectPart<'T, 'F> =
        {
            Fields =
                [
                    {
                        Name = name
                        Schema = schema.Info
                        Required = true
                        Description = None
                        Default = None
                        Sensitive = (SchemaInfo.meta schema.Info).Sensitive
                    }
                ]
            WriteFields =
                fun writer value ->
                    writer.WritePropertyName name
                    schema.Write writer (get value)
            ReadFields =
                fun mode _ element ->
                    match element.TryGetProperty name with
                    | true, property ->
                        // Matched rather than piped through mapErrors: the pipe
                        // needs a partial application, and a closure per field is
                        // the allocation this whole arrangement exists to avoid.
                        // Ok is returned as it came, not rebuilt.
                        let result = schema.Read mode Path.root property

                        match result with
                        | Ok _ -> result
                        | Error errors -> Error(ValidationErrors.underField name errors)
                    | false, _ -> Codec.missing (Path.field name Path.root)
        }

    /// <summary>
    /// A field that may be absent. An explicit <c>null</c> is read as absent too,
    /// because in practice callers mean the same thing by both; use
    /// <see cref="M:Etymon.Schema.nullable"/> on a required field when the
    /// difference matters.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.optional "email" Schema.string (fun p -> p.Email)
    /// </code></example>
    let optional (name: string) (schema: Schema<'F>) (get: 'T -> 'F option) : ObjectPart<'T, 'F option> =
        {
            Fields =
                [
                    {
                        Name = name
                        Schema = schema.Info
                        Required = false
                        Description = None
                        Default = None
                        Sensitive = (SchemaInfo.meta schema.Info).Sensitive
                    }
                ]
            WriteFields =
                fun writer value ->
                    match get value with
                    | Some present ->
                        writer.WritePropertyName name
                        schema.Write writer present
                    | None -> ()
            ReadFields =
                fun mode _ element ->
                    match element.TryGetProperty name with
                    | true, property when property.ValueKind = JsonValueKind.Null -> Ok None
                    | true, property ->
                        match schema.Read mode Path.root property with
                        | Ok value -> Ok(Some value)
                        | Error errors -> Error(ValidationErrors.underField name errors)
                    | false, _ -> Ok None
        }

    /// <summary>
    /// A field that is always present and may be <c>null</c>: written as
    /// <c>"x": null</c> when there is nothing, described as required with a
    /// nullable type, and read leniently, so that an absent key and a
    /// <c>null</c> both arrive as <c>None</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what <c>System.Text.Json</c> writes for a nullable field, and
    /// therefore what every client of a service built on it has been reading.
    /// Describing such a field as <c>optional</c> tells a generated client to
    /// expect <c>undefined</c> and hands it <c>null</c>, and tells the
    /// compatibility snapshot the key might not be there, so that removing it
    /// later looks safe when it is not.
    /// </para>
    /// <para>
    /// Three fields, three statements. <c>required</c>: the key is there and
    /// the value is not null. <c>present</c>: the key is there and the value
    /// may be null. <c>optional</c>: the key may be missing. A missing key is
    /// read as <c>None</c> here rather than refused, because in practice a
    /// caller means the same thing by both; where the difference matters,
    /// <c>required</c> with <c>Schema.nullable</c> refuses the absent key.
    /// </para>
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Schema.present "note" Schema.string (fun r -> r.Note) // r.Note : string option
    /// </code></example>
    let present (name: string) (schema: Schema<'F>) (get: 'T -> 'F option) : ObjectPart<'T, 'F option> =
        let orNull = nullable schema

        {
            Fields =
                [
                    {
                        Name = name
                        Schema = orNull.Info
                        Required = true
                        Description = None
                        Default = None
                        // From the inner schema: the nullable wrapper carries no
                        // metadata of its own, and a sensitive value is no less
                        // sensitive for being allowed to be null.
                        Sensitive = (SchemaInfo.meta schema.Info).Sensitive
                    }
                ]
            WriteFields =
                fun writer value ->
                    writer.WritePropertyName name
                    orNull.Write writer (get value)
            ReadFields =
                fun mode _ element ->
                    match element.TryGetProperty name with
                    | true, property ->
                        match orNull.Read mode Path.root property with
                        | Ok value -> Ok value
                        | Error errors -> Error(ValidationErrors.underField name errors)
                    | false, _ -> Ok None
        }

    /// <summary>
    /// A field that may be absent, standing in for a value when it is. The
    /// default is always written back out, so that a round trip is stable.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.defaulted "retries" Schema.int 3 (fun c -> c.Retries)
    /// </code></example>
    let defaulted (name: string) (schema: Schema<'F>) (fallback: 'F) (get: 'T -> 'F) : ObjectPart<'T, 'F> =
        {
            Fields =
                [
                    {
                        Name = name
                        Schema = schema.Info
                        Required = false
                        Description = None
                        Default = Some(JsonNode.Parse(toJson schema fallback))
                        Sensitive = (SchemaInfo.meta schema.Info).Sensitive
                    }
                ]
            WriteFields =
                fun writer value ->
                    writer.WritePropertyName name
                    schema.Write writer (get value)
            ReadFields =
                fun mode _ element ->
                    match element.TryGetProperty name with
                    | true, property when property.ValueKind = JsonValueKind.Null -> Ok fallback
                    | true, property ->
                        let result = schema.Read mode Path.root property

                        match result with
                        | Ok _ -> result
                        | Error errors -> Error(ValidationErrors.underField name errors)
                    | false, _ -> Ok fallback
        }

    /// <summary>Describes what a field means, in prose.</summary>
    /// <example><code lang="fsharp">
    /// Schema.required "zip" Schema.string (fun a -> a.Zip) |> Schema.doc "US postal code"
    /// </code></example>
    let doc (description: string) (part: ObjectPart<'T, 'A>) =
        { part with
            Fields =
                part.Fields
                |> List.mapi (fun i field ->
                    if i = List.length part.Fields - 1 then
                        { field with
                            Description = Some description
                        }
                    else
                        field
                )
        }

    // ---- unions --------------------------------------------------------------

    /// <summary>
    /// One case of a union, with a payload.
    /// </summary>
    /// <remarks>
    /// Cases are written adjacently tagged — <c>{ "kind": "circle", "value": … }</c>
    /// — rather than by merging the payload's fields into the outer object. That
    /// works whatever shape the payload has, including a bare number or a list,
    /// and it means the tag can never collide with a payload field.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// Schema.case "circle" Schema.float Circle (function Circle r -> ValueSome r | _ -> ValueNone)
    /// </code></example>
    let case (tag: string) (payload: Schema<'C>) (construct: 'C -> 'T) (destruct: 'T -> 'C voption) : CaseSchema<'T> =
        {
            Tag = tag
            Payload = Some payload.Info
            TryWrite =
                fun writer tagField value ->
                    match destruct value with
                    | ValueSome inner ->
                        writer.WriteString(tagField, tag)
                        writer.WritePropertyName "value"
                        payload.Write writer inner
                        true
                    | ValueNone -> false
            Read =
                fun mode _ element ->
                    match element.TryGetProperty "value" with
                    | true, property ->
                        payload.Read mode Path.root property
                        |> Validation.mapErrors (ValidationErrors.underField "value")
                        |> Validation.map construct
                    | false, _ -> Codec.missing (Path.field "value" Path.root)
        }

    /// <summary>One case of a union with no payload, written as the tag alone.</summary>
    /// <example><code lang="fsharp">
    /// Schema.caseUnit "unknown" Unknown (function Unknown -> true | _ -> false)
    /// </code></example>
    let caseUnit (tag: string) (value: 'T) (isCase: 'T -> bool) : CaseSchema<'T> =
        {
            Tag = tag
            Payload = None
            TryWrite =
                fun writer tagField candidate ->
                    if isCase candidate then
                        writer.WriteString(tagField, tag)
                        true
                    else
                        false
            Read = fun _ _ _ -> Ok value
        }

    /// <summary>
    /// A discriminated union, distinguished by the value of a tag field.
    /// </summary>
    /// <remarks>
    /// Encoding raises if no case matches the value. That is deliberate: it means
    /// the destructors do not cover the type, which is a bug in the schema rather
    /// than an expected outcome, and no caller could sensibly handle it.
    /// </remarks>
    /// <example><code lang="fsharp">
    /// let shapeSchema =
    ///     Schema.union "Shape" "kind" [
    ///         Schema.case "circle" Schema.float Circle (function Circle r -> ValueSome r | _ -> ValueNone)
    ///         Schema.case "square" Schema.float Square (function Square s -> ValueSome s | _ -> ValueNone)
    ///     ]
    /// Schema.toJson shapeSchema (Circle 1.0) // """{"kind":"circle","value":1}"""
    /// </code></example>
    let union (name: string) (tagField: string) (cases: CaseSchema<'T> list) : Schema<'T> =
        let tags = cases |> List.map (fun c -> c.Tag)

        {
            Write =
                fun writer value ->
                    writer.WriteStartObject()

                    if not (cases |> List.exists (fun c -> c.TryWrite writer tagField value)) then
                        failwith
                            $"the union schema %s{name} has no case matching this value; its destructors do not cover the type"

                    writer.WriteEndObject()
            Read =
                fun mode path element ->
                    match element.ValueKind with
                    | JsonValueKind.Object ->
                        // Built here rather than on the way in: every path below
                        // is relative, and this one is only ever used to report.
                        let tagPath = Path.field tagField Path.root

                        match element.TryGetProperty tagField with
                        | true, tagElement when tagElement.ValueKind = JsonValueKind.String ->
                            let tag = tagElement.GetString()

                            match cases |> List.tryFind (fun c -> c.Tag = tag) with
                            | Some matched -> matched.Read mode path element
                            | None ->
                                Codec.rejected "unknown-case" ("must be one of: " + String.Join(", ", tags)) tagPath
                        | true, tagElement -> Codec.mismatch "string" tagPath tagElement
                        | false, _ -> Codec.missing tagPath
                    | _ -> Codec.mismatch "object" path element
            Info =
                SUnion(
                    name,
                    tagField,
                    cases
                    |> List.map (fun c -> c.Tag, c.Payload |> Option.defaultValue (SPrim PrimKind.Raw))
                )
        }

    // ---- recursion -----------------------------------------------------------

    /// <summary>
    /// A schema that refers to itself. Without this, building a self-referential
    /// schema overflows the stack while it is being constructed, rather than
    /// failing in a way that says what is wrong.
    /// </summary>
    /// <example><code lang="fsharp">
    /// let treeSchema =
    ///     Schema.recursive "Tree" (fun self ->
    ///         Schema.object "Tree" {
    ///             let! value = Schema.required "value" Schema.int (fun t -> t.Value)
    ///             and! children = Schema.required "children" (Schema.list self) (fun t -> t.Children)
    ///             return { Value = value; Children = children }
    ///         })
    /// </code></example>
    let recursive (name: string) (build: Schema<'T> -> Schema<'T>) : Schema<'T> =
        let mutable resolved: Schema<'T> option = None

        let useResolved (f: Schema<'T> -> 'R) =
            match resolved with
            | Some schema -> f schema
            | None -> failwith $"the recursive schema %s{name} was used before it had finished being built"

        let placeholder =
            {
                Write = fun writer value -> useResolved (fun s -> s.Write writer value)
                Read = fun mode path element -> useResolved (fun s -> s.Read mode path element)
                Info = SRef name
            }

        let built = build placeholder
        resolved <- Some built
        built |> named name

    /// <summary>
    /// Every named schema reachable from this one. What a generator needs in order
    /// to emit each object once and refer to it thereafter.
    /// </summary>
    /// <example><code lang="fsharp">
    /// Schema.definitions personSchema |> Map.keys // seq [ "Address"; "Person" ]
    /// </code></example>
    let definitions (schema: Schema<'T>) = SchemaInfo.definitions schema.Info
