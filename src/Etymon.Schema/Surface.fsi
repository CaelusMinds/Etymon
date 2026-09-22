
namespace FSharp



namespace Etymon
    
    /// The kind of a scalar. Named rather than reflected, so that every consumer of a
    /// schema agrees on what a value is without asking the type system.
    [<RequireQualifiedAccess>]
    type PrimKind =
        
        /// A JSON string.
        | String
        
        /// A 32-bit signed integer.
        | Int
        
        /// A 64-bit signed integer.
        | Int64
        
        /// A double-precision number.
        | Float
        
        /// An exact decimal, carried on the wire as a JSON number.
        | Decimal
        
        /// A JSON boolean.
        | Bool
        
        /// A UUID, carried as a string.
        | Guid
        
        /// An RFC 3339 timestamp, carried as a string.
        | DateTimeOffset
        
        /// An ISO 8601 date, carried as a string.
        | DateOnly
        
        /// An ISO 8601 wall-clock time, carried as a string.
        | TimeOnly
        
        /// A duration, carried as a string.
        | TimeSpan
        
        /// Binary data, carried as base64.
        | Bytes
        
        /// Arbitrary JSON, passed through untouched.
        | Raw
    
    /// <summary>
    /// Everything a schema says about a value that is not its shape: what it is
    /// called, what it means, what restricts it, and what it looks like.
    /// </summary>
    [<NoComparison>]
    type Meta =
        {
          
          /// The name this schema is known by, which becomes a reusable component
          /// in generated documentation and a table name in a generated migration.
          Name: string option
          
          /// Prose describing what the value means.
          Description: string option
          
          /// The rules the value must satisfy, drawn from the one vocabulary in
          /// <c>Etymon.Core</c> so that every derivation target reads the same list.
          Constraints: Constraint list
          
          /// Examples, already encoded.
          Examples: System.Text.Json.Nodes.JsonNode list
          
          /// Whether the value is a secret. Redacted wherever Etymon renders it.
          Sensitive: bool
        }
    
    /// Working with <see cref="T:Etymon.Meta"/>.
    [<RequireQualifiedAccess>]
    module Meta =
        
        /// <summary>Metadata that says nothing.</summary>
        /// <example><code lang="fsharp">
        /// Meta.empty.Constraints // []
        /// </code></example>
        val empty: Meta
        
        /// <summary>
        /// Combines two layers of metadata, with the outer one winning where both say
        /// something. Constraints accumulate rather than replace, because each one is
        /// a separate promise about the value.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Meta.merge inner outer // outer.Description wins; constraints are concatenated
        /// </code></example>
        val merge: inner: Meta -> outer: Meta -> Meta
    
    /// <summary>
    /// The shape of a value, with the types erased.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the half of <c>Schema&lt;'T&gt;</c> that every other package reads.
    /// <c>Etymon.Schema.OpenApi</c> turns it into JSON Schema, <c>Etymon.Schema.Sql</c>
    /// into a table, <c>Etymon.Invariants.FsCheck</c> into a generator — each of them a
    /// pure function of this tree and nothing else.
    /// </para>
    /// <para>
    /// Keeping the description separate from the typed codec is what stops the two
    /// drifting: there is no way to change how a value encodes without changing what
    /// this says about it, because they are built from the same combinator call.
    /// </para>
    /// </remarks>
    [<NoComparison>]
    type SchemaInfo =
        
        /// A scalar.
        | SPrim of PrimKind
        
        /// A value that may be JSON <c>null</c>. Distinct from an optional field,
        /// which may be absent entirely — JSON Schema, OpenAPI and TypeScript all
        /// treat those differently, so Etymon does too.
        | SNullable of SchemaInfo
        
        /// An ordered collection.
        | SList of SchemaInfo
        
        /// An object used as a string-keyed map of uniform values.
        | SMap of SchemaInfo
        
        /// An object with known fields.
        | SObject of name: string * fields: FieldInfo list
        
        /// A discriminated union, distinguished by the value of a tag field.
        | SUnion of
          name: string * tag: string * cases: (string * SchemaInfo) list
        
        /// A reference to a named schema, which is how recursion is represented.
        | SRef of name: string
        
        /// A schema with metadata attached.
        | SAnnotated of SchemaInfo * Meta
    
    /// One field of an object.
    and [<NoComparison>] FieldInfo =
        {
          
          /// The key as it appears in JSON.
          Name: string
          
          /// The shape of the value.
          Schema: SchemaInfo
          
          /// Whether the key must be present. Distinct from whether the value may
          /// be <c>null</c>.
          Required: bool
          
          /// Prose describing what the field means.
          Description: string option
          
          /// The value used when the key is absent, already encoded.
          /// A JSON <c>null</c> default is <c>Some null</c>: <c>System.Text.Json.Nodes</c>
          /// has no other value for it, and a reader must not dereference it.
          Default: System.Text.Json.Nodes.JsonNode option
          
          /// Whether the field is a secret.
          Sensitive: bool
        }
    
    /// Inspecting a <see cref="T:Etymon.SchemaInfo"/>.
    [<RequireQualifiedAccess>]
    module SchemaInfo =
        
        /// <summary>
        /// The metadata attached to a schema, flattened through any number of
        /// annotation layers.
        /// </summary>
        /// <example><code lang="fsharp">
        /// SchemaInfo.meta info |> fun m -> m.Constraints
        /// </code></example>
        val meta: info: SchemaInfo -> Meta
        
        /// <summary>The schema with its annotation layers removed.</summary>
        /// <example><code lang="fsharp">
        /// SchemaInfo.strip (SAnnotated(SPrim PrimKind.String, Meta.empty)) // SPrim String
        /// </code></example>
        val strip: info: SchemaInfo -> SchemaInfo
        
        /// <summary>
        /// The name this schema is known by, if it has one. A named schema becomes a
        /// reusable component rather than being inlined everywhere it appears.
        /// </summary>
        /// <example><code lang="fsharp">
        /// SchemaInfo.name personInfo // Some "Person"
        /// </code></example>
        val name: info: SchemaInfo -> string option
        
        /// <summary>The constraints attached to a schema, in the order they were added.</summary>
        /// <example><code lang="fsharp">
        /// SchemaInfo.constraints emailInfo // [ Length(Some 3, Some 254); HasFormat Email ]
        /// </code></example>
        val constraints: info: SchemaInfo -> Constraint list
        
        /// <summary>Whether the value may be JSON <c>null</c>.</summary>
        /// <example><code lang="fsharp">
        /// SchemaInfo.isNullable (SNullable(SPrim PrimKind.String)) // true
        /// </code></example>
        val isNullable: info: SchemaInfo -> bool
        
        /// <summary>
        /// Every named schema reachable from this one, keyed by name. This is what
        /// lets a generator emit each object once and reference it thereafter, and
        /// what makes a recursive schema expressible at all.
        /// </summary>
        /// <example><code lang="fsharp">
        /// SchemaInfo.definitions personInfo |> Map.keys // seq [ "Address"; "Person" ]
        /// </code></example>
        val definitions: info: SchemaInfo -> Map<string,SchemaInfo>

namespace Etymon
    
    /// <summary>
    /// How strictly a decoder reads a value whose JSON type is not the expected one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Strict</c> is for JSON that arrived as JSON: a string where a number was
    /// expected is a mistake by whoever sent it, and saying so is more useful than
    /// guessing.
    /// </para>
    /// <para>
    /// <c>Coercing</c> is for the places where everything is a string whether it wants
    /// to be or not — environment variables, query strings, form posts. There,
    /// <c>"8080"</c> really is the number, and refusing it is pedantry.
    /// <c>Etymon.Config</c> reads in this mode.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    type DecodeMode =
        
        /// The JSON type must match what the schema expects.
        | Strict
        
        /// A string may stand in for a number, a boolean or a date.
        | Coercing
    
    /// <summary>
    /// One value describing how a <typeparamref name="'T"/> is encoded, decoded,
    /// validated and documented.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A schema is a typed codec paired with an untyped description of itself. The
    /// codec is what runs; the description is what every other package in the suite
    /// reads. Because both come out of the same combinator call, there is no way to
    /// change one without changing the other.
    /// </para>
    /// <para>
    /// Encoding writes straight to a <c>Utf8JsonWriter</c> and cannot fail, because a
    /// value of type <typeparamref name="'T"/> already is whatever its type says.
    /// Decoding reads a <c>JsonElement</c> and accumulates every error it finds,
    /// which a forward-only reader cannot do.
    /// </para>
    /// </remarks>
    [<NoEquality; NoComparison>]
    type Schema<'T> =
        {
          
          /// Writes a value. Total.
          Write: (System.Text.Json.Utf8JsonWriter -> 'T -> unit)
          
          /// Reads a value, collecting every reason it could not be read.
          Read:
            (DecodeMode ->
               Path -> System.Text.Json.JsonElement -> Validation<'T>)
          
          /// What this schema says about the shape it describes.
          Info: SchemaInfo
        }
    
    /// <summary>
    /// One field of an object being described, or several of them combined.
    /// </summary>
    /// <remarks>
    /// Each part carries the getter that pulls its field out of the whole value,
    /// which is what lets one declaration serve encoding and decoding at once.
    /// Combining parts concatenates their descriptions and their writers, and
    /// combines their readers so that errors from every field accumulate.
    /// </remarks>
    [<NoEquality; NoComparison>]
    type ObjectPart<'T,'A> =
        {
          
          /// The fields described so far.
          Fields: FieldInfo list
          
          /// Writes those fields as properties of the object being written.
          WriteFields: (System.Text.Json.Utf8JsonWriter -> 'T -> unit)
          
          /// Reads those fields, accumulating errors across all of them.
          ReadFields:
            (DecodeMode ->
               Path -> System.Text.Json.JsonElement -> Validation<'A>)
        }
    
    /// <summary>One case of a discriminated union.</summary>
    [<NoEquality; NoComparison>]
    type CaseSchema<'T> =
        {
          
          /// The value of the tag field that identifies this case.
          Tag: string
          
          /// The shape of the case's payload, if it has one.
          Payload: SchemaInfo option
          
          /// Writes the payload when the value is this case, and reports whether it was.
          TryWrite: (System.Text.Json.Utf8JsonWriter -> string -> 'T -> bool)
          
          /// Reads a value of this case.
          Read:
            (DecodeMode ->
               Path -> System.Text.Json.JsonElement -> Validation<'T>)
        }
    
    /// Shared machinery for reading JSON. Internal: consumers use the combinators in
    /// <c>Schema</c> rather than these.
    module internal Codec =
        
        /// The name a JSON value would go by in an error message.
        val kindName: element: System.Text.Json.JsonElement -> string
        
        val mismatch:
          expected: string ->
            path: Path ->
            element: System.Text.Json.JsonElement -> Validation<'T>
        
        val rejected:
          code: string -> message: string -> path: Path -> Validation<'T>
        
        val missing: path: Path -> Validation<'T>
        
        /// The text of a JSON value when it is a string, or -- only in coercing mode
        /// -- the literal text of a number or boolean. The single place that decides
        /// what "a string may stand in for it" means.
        val coercibleText:
          mode: DecodeMode ->
            element: System.Text.Json.JsonElement -> string option
        
        /// Writes a value to UTF-8 bytes using a schema's writer and the given options.
        /// <summary>
        /// Writes a value and hands back the buffer it was written into.
        /// </summary>
        /// <remarks>
        /// An <c>ArrayBufferWriter</c> rather than a <c>MemoryStream</c>, because
        /// <c>Utf8JsonWriter</c> takes an <c>IBufferWriter</c> directly. Going
        /// through a stream meant the stream's buffer, the writer's buffer, and a
        /// copy of the whole payload out of <c>ToArray</c> before anything could
        /// look at it. The caller decides what to copy, once.
        /// </remarks>
        val writeToBuffer:
          options: System.Text.Json.JsonWriterOptions ->
            write: (System.Text.Json.Utf8JsonWriter -> 'T -> unit) ->
            value: 'T -> System.Buffers.ArrayBufferWriter<byte>
        
        val toUtf8With:
          options: System.Text.Json.JsonWriterOptions ->
            write: (System.Text.Json.Utf8JsonWriter -> 'T -> unit) ->
            value: 'T -> byte array
        
        /// The writer options Etymon uses unless told otherwise.
        ///
        /// The encoder is System.Text.Json's default, which escapes characters that
        /// are dangerous inside HTML -- '+' becomes +, '&' becomes &. That
        /// is valid JSON and round-trips exactly, but it surprises people reading the
        /// output. `Schema.toJsonWith` exists for anyone who would rather have
        /// JavaScriptEncoder.UnsafeRelaxedJsonEscaping and is not embedding the
        /// result in a web page.
        val defaultOptions: System.Text.Json.JsonWriterOptions
        
        val indentedOptions: System.Text.Json.JsonWriterOptions
        
        /// Writes a value to UTF-8 bytes using a schema's writer.
        val toUtf8:
          write: (System.Text.Json.Utf8JsonWriter -> 'T -> unit) ->
            indented: bool -> value: 'T -> byte array
        
        /// Writes a value straight to a string, without the byte array in between.
        val toText:
          options: System.Text.Json.JsonWriterOptions ->
            write: (System.Text.Json.Utf8JsonWriter -> 'T -> unit) ->
            value: 'T -> string

namespace Etymon
    
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
    type ObjectBuilder<'T> =
        
        new: name: string -> ObjectBuilder<'T>
        
        /// Turns the accumulated fields into a schema, using the body of the
        /// expression to assemble the value.
        member
          BindReturn: part: ObjectPart<'T,'A> * assemble: ('A -> 'T) ->
                        Schema<'T>
        
        /// Combines two field declarations, concatenating their descriptions and
        /// collecting errors from both.
        member
          MergeSources: left: ObjectPart<'T,'A> * right: ObjectPart<'T,'B> ->
                          ObjectPart<'T,('A * 'B)>
        
        /// An object with no fields at all.
        member Return: value: 'T -> Schema<'T>
    
    /// Building and running <see cref="T:Etymon.Schema`1"/> values.
    [<RequireQualifiedAccess>]
    module Schema =
        
        val private inv: System.Globalization.CultureInfo
        
        val private prim:
          kind: PrimKind ->
            write: (System.Text.Json.Utf8JsonWriter -> 'T -> unit) ->
            read: (DecodeMode ->
                     Path -> System.Text.Json.JsonElement -> Validation<'T>) ->
            Schema<'T>
        
        /// <summary>A JSON string.</summary>
        /// <example><code lang="fsharp">
        /// Schema.toJson Schema.string "hi" // "\"hi\""
        /// </code></example>
        val string: Schema<string>
        
        val private numeric:
          kind: PrimKind ->
            write: (System.Text.Json.Utf8JsonWriter -> 'T -> unit) ->
            ofElement: (System.Text.Json.JsonElement -> 'T option) ->
            parse: (string | null -> 'T option) -> Schema<'T>
        
        /// <summary>A 32-bit integer.</summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJson Schema.int "42" // Ok 42
        /// </code></example>
        val int: Schema<int>
        
        /// <summary>A 64-bit integer.</summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJson Schema.int64 "9007199254740993" // Ok 9007199254740993L
        /// </code></example>
        val int64: Schema<int64>
        
        /// <summary>
        /// A double-precision number. NaN and the infinities are rejected on the way
        /// in and cannot be written, because JSON has no way to express them.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJson Schema.float "1.5" // Ok 1.5
        /// </code></example>
        val float: Schema<float>
        
        /// <summary>An exact decimal. Use this for money rather than <c>float</c>.</summary>
        /// <example><code lang="fsharp">
        /// Schema.toJson Schema.decimal 1.50m // "1.50"
        /// </code></example>
        val decimal: Schema<decimal>
        
        /// <summary>A boolean.</summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJson Schema.bool "true" // Ok true
        /// </code></example>
        val bool: Schema<bool>
        
        val private stringly:
          kind: PrimKind ->
            render: ('T -> string) ->
            parse: (string -> 'T option) -> expected: string -> Schema<'T>
        
        /// <summary>A UUID, carried as a canonical 8-4-4-4-12 string.</summary>
        /// <example><code lang="fsharp">
        /// Schema.toJson Schema.guid Guid.Empty // "\"00000000-0000-0000-0000-000000000000\""
        /// </code></example>
        val guid: Schema<System.Guid>
        
        /// <summary>An RFC 3339 timestamp.</summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJson Schema.dateTimeOffset "\"2026-09-20T14:30:00Z\"" // Ok ...
        /// </code></example>
        val dateTimeOffset: Schema<System.DateTimeOffset>
        
        /// <summary>An ISO 8601 date.</summary>
        /// <example><code lang="fsharp">
        /// Schema.toJson Schema.dateOnly (DateOnly(2026, 9, 20)) // "\"2026-09-20\""
        /// </code></example>
        val dateOnly: Schema<System.DateOnly>
        
        /// <summary>An ISO 8601 wall-clock time.</summary>
        /// <example><code lang="fsharp">
        /// Schema.toJson Schema.timeOnly (TimeOnly(14, 30)) // "\"14:30:00\""
        /// </code></example>
        val timeOnly: Schema<System.TimeOnly>
        
        /// <summary>A duration, in .NET's <c>[d.]hh:mm:ss</c> form.</summary>
        /// <example><code lang="fsharp">
        /// Schema.toJson Schema.timeSpan (TimeSpan.FromHours 1.0) // "\"01:00:00\""
        /// </code></example>
        val timeSpan: Schema<System.TimeSpan>
        
        /// <summary>Binary data, carried as base64.</summary>
        /// <example><code lang="fsharp">
        /// Schema.toJson Schema.bytes [| 1uy; 2uy |] // "\"AQI=\""
        /// </code></example>
        val bytes: Schema<byte array>
        
        /// <summary>
        /// Arbitrary JSON, passed through untouched. The escape hatch for a payload
        /// whose shape genuinely is not known ahead of time.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJson Schema.raw """{"anything":1}""" // Ok (a JsonNode)
        /// </code></example>
        val raw: Schema<System.Text.Json.Nodes.JsonNode>
        
        val private annotate:
          f: (Meta -> Meta) -> schema: Schema<'T> -> Schema<'T>
        
        /// <summary>
        /// Gives a schema a name, making it a reusable component in generated output
        /// rather than something inlined at every use.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.string |> Schema.named "Slug"
        /// </code></example>
        val named: name: string -> schema: Schema<'T> -> Schema<'T>
        
        /// <summary>Describes, in prose, what a value means.</summary>
        /// <example><code lang="fsharp">
        /// Schema.int |> Schema.describe "Age in whole years"
        /// </code></example>
        val describe: description: string -> schema: Schema<'T> -> Schema<'T>
        
        /// <summary>
        /// Marks a value as a secret. Etymon redacts it wherever Etymon renders it;
        /// it cannot stop your own code from printing it.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.string |> Schema.sensitive
        /// </code></example>
        val sensitive: schema: Schema<'T> -> Schema<'T>
        
        /// <summary>
        /// Adds a rule. It is enforced on decode and carried, as data, into
        /// everything derived from the schema.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.int |> Schema.constrain (Check.intRange (Some 0) (Some 130))
        /// </code></example>
        val constrain:
          check: ConstraintCheck<'T> -> schema: Schema<'T> -> Schema<'T>
        
        /// <summary>What a schema says about the shape it describes.</summary>
        /// <example><code lang="fsharp">
        /// Schema.info Schema.int // SPrim PrimKind.Int
        /// </code></example>
        val info: schema: Schema<'T> -> SchemaInfo
        
        /// <summary>Writes a value onto an existing writer.</summary>
        /// <example><code lang="fsharp">
        /// Schema.writeJson Schema.int writer 42
        /// </code></example>
        val writeJson:
          schema: Schema<'T> ->
            writer: System.Text.Json.Utf8JsonWriter -> value: 'T -> unit
        
        /// <summary>Encodes a value as UTF-8 bytes, without going through a string.</summary>
        /// <example><code lang="fsharp">
        /// Schema.toJsonUtf8 Schema.int 42 // [| 52uy; 50uy |]
        /// </code></example>
        val toJsonUtf8: schema: Schema<'T> -> value: 'T -> byte array
        
        /// <summary>Encodes a value as compact JSON.</summary>
        /// <example><code lang="fsharp">
        /// Schema.toJson personSchema person // """{"name":"Ada","age":36}"""
        /// </code></example>
        val toJson: schema: Schema<'T> -> value: 'T -> string
        
        /// <summary>Encodes a value as indented JSON, for something a person will read.</summary>
        /// <example><code lang="fsharp">
        /// Schema.toJsonIndented personSchema person
        /// </code></example>
        val toJsonIndented: schema: Schema<'T> -> value: 'T -> string
        
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
        val toJsonWith:
          options: System.Text.Json.JsonWriterOptions ->
            schema: Schema<'T> -> value: 'T -> string
        
        /// <summary>Decodes an already-parsed element, in a given mode.</summary>
        /// <example><code lang="fsharp">
        /// Schema.decodeWith DecodeMode.Coercing Schema.int element // accepts "42"
        /// </code></example>
        val decodeWith:
          mode: DecodeMode ->
            schema: Schema<'T> ->
            element: System.Text.Json.JsonElement -> Validation<'T>
        
        /// <summary>Decodes an already-parsed element, strictly.</summary>
        /// <example><code lang="fsharp">
        /// Schema.decode Schema.int element
        /// </code></example>
        val decode:
          schema: Schema<'T> ->
            element: System.Text.Json.JsonElement -> Validation<'T>
        
        /// <summary>Parses and decodes, in a given mode.</summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJsonWith DecodeMode.Coercing Schema.int "\"42\"" // Ok 42
        /// </code></example>
        val fromJsonWith:
          mode: DecodeMode ->
            schema: Schema<'T> -> json: string -> Validation<'T>
        
        /// <summary>
        /// Parses and decodes, strictly, reporting every problem at once.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJson personSchema """{"age":-3}"""
        /// // Error, both of:
        /// //   name: is required
        /// //   age: must be between 0 and 130
        /// </code></example>
        val fromJson: schema: Schema<'T> -> json: string -> Validation<'T>
        
        /// <summary>Parses and decodes UTF-8 bytes, strictly.</summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJsonUtf8 personSchema payload
        /// </code></example>
        val fromJsonUtf8:
          schema: Schema<'T> -> json: byte array -> Validation<'T>
        
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
        val validate: schema: Schema<'T> -> value: 'T -> Validation<'T>
        
        /// <summary>
        /// Adds an example, encoded with the schema itself so that it cannot be an
        /// example of something the schema would reject.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.int |> Schema.example 42
        /// </code></example>
        val example: value: 'T -> schema: Schema<'T> -> Schema<'T>
        
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
        val convert:
          forward: ('A -> 'B) ->
            backward: ('B -> 'A) -> schema: Schema<'A> -> Schema<'B>
        
        /// <summary>
        /// Lifts a constrained type from <c>Etymon.Core</c> into a schema. The
        /// refinement's rules become the schema's constraints, so that JSON Schema,
        /// SQL and generators all read the same declaration instead of restating it.
        /// </summary>
        /// <example><code lang="fsharp">
        /// let emailSchema = Schema.string |> Schema.refine Email.refinement
        /// Schema.fromJson emailSchema "\"nope\"" // Error [ "must be a valid email" ]
        /// </code></example>
        val refine:
          refinement: Refinement<'A,'B> -> schema: Schema<'A> -> Schema<'B>
        
        /// <summary>
        /// A value that may be JSON <c>null</c>. Distinct from an optional field,
        /// which may be absent entirely: JSON Schema, OpenAPI and TypeScript all treat
        /// those as different things, so Etymon does too.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJson (Schema.nullable Schema.string) "null" // Ok None
        /// </code></example>
        val nullable: schema: Schema<'T> -> Schema<'T option>
        
        /// <summary>
        /// An ordered collection. Every element is read, so a list with three bad
        /// entries reports three errors, each carrying its index.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJson (Schema.list Schema.int) "[1,\"x\",\"y\"]" |> Validation.errorCount // 2
        /// </code></example>
        val list: schema: Schema<'T> -> Schema<'T list>
        
        /// <summary>An ordered collection, as an array.</summary>
        /// <example><code lang="fsharp">
        /// Schema.toJson (Schema.array Schema.int) [| 1; 2 |] // "[1,2]"
        /// </code></example>
        val array: schema: Schema<'T> -> Schema<'T array>
        
        /// <summary>
        /// A JSON object used as a string-keyed map of uniform values. Keys are
        /// written in order, so encoding the same map twice produces identical bytes.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.fromJson (Schema.map Schema.int) """{"a":1}""" // Ok (Map [ "a", 1 ])
        /// </code></example>
        val map: schema: Schema<'V> -> Schema<Map<string,'V>>
        
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
        val object: name: string -> ObjectBuilder<'T>
        
        /// <summary>A field that must be present.</summary>
        /// <example><code lang="fsharp">
        /// Schema.required "name" Schema.string (fun p -> p.Name)
        /// </code></example>
        val required:
          name: string ->
            schema: Schema<'F> -> get: ('T -> 'F) -> ObjectPart<'T,'F>
        
        /// <summary>
        /// A field that may be absent. An explicit <c>null</c> is read as absent too,
        /// because in practice callers mean the same thing by both; use
        /// <see cref="M:Etymon.Schema.nullable"/> on a required field when the
        /// difference matters.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.optional "email" Schema.string (fun p -> p.Email)
        /// </code></example>
        val optional:
          name: string ->
            schema: Schema<'F> ->
            get: ('T -> 'F option) -> ObjectPart<'T,'F option>
        
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
        val present:
          name: string ->
            schema: Schema<'F> ->
            get: ('T -> 'F option) -> ObjectPart<'T,'F option>
        
        /// <summary>
        /// A field that may be absent, standing in for a value when it is. The
        /// default is always written back out, so that a round trip is stable.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.defaulted "retries" Schema.int 3 (fun c -> c.Retries)
        /// </code></example>
        val defaulted:
          name: string ->
            schema: Schema<'F> ->
            fallback: 'F -> get: ('T -> 'F) -> ObjectPart<'T,'F>
        
        /// <summary>Describes what a field means, in prose.</summary>
        /// <example><code lang="fsharp">
        /// Schema.required "zip" Schema.string (fun a -> a.Zip) |> Schema.doc "US postal code"
        /// </code></example>
        val doc:
          description: string -> part: ObjectPart<'T,'A> -> ObjectPart<'T,'A>
        
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
        val case:
          tag: string ->
            payload: Schema<'C> ->
            construct: ('C -> 'T) ->
            destruct: ('T -> 'C voption) -> CaseSchema<'T>
        
        /// <summary>One case of a union with no payload, written as the tag alone.</summary>
        /// <example><code lang="fsharp">
        /// Schema.caseUnit "unknown" Unknown (function Unknown -> true | _ -> false)
        /// </code></example>
        val caseUnit:
          tag: string -> value: 'T -> isCase: ('T -> bool) -> CaseSchema<'T>
        
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
        val union:
          name: string ->
            tagField: string -> cases: CaseSchema<'T> list -> Schema<'T>
        
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
        val recursive:
          name: string -> build: (Schema<'T> -> Schema<'T>) -> Schema<'T>
        
        /// <summary>
        /// Every named schema reachable from this one. What a generator needs in order
        /// to emit each object once and refer to it thereafter.
        /// </summary>
        /// <example><code lang="fsharp">
        /// Schema.definitions personSchema |> Map.keys // seq [ "Address"; "Person" ]
        /// </code></example>
        val definitions: schema: Schema<'T> -> Map<string,SchemaInfo>

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
        val numSchema: Schema<Num>
        
        /// A format is carried by its JSON Schema name, which is the spelling every
        /// other part of the suite already uses for it.
        /// A named format, such as email or uuid.
        val formatSchema: Schema<Format>
        
        /// Constraints are written to the snapshot because they are part of the shape
        /// the database is in: a CHECK that exists is something a later diff has to
        /// be able to see. An earlier draft left them out on the grounds that the
        /// schema is the source of truth, which confused "where the rule is declared"
        /// with "what the database currently has".
        /// One constraint, tagged by which rule it is.
        val schema: Schema<Constraint>

namespace Etymon
    
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
        val private rebound:
          convert: ('A -> 'B) -> part: ObjectPart<'T,'A> -> ObjectPart<'T,'B>
        
        /// Reads an absent or null key as a fallback rather than refusing it, with
        /// the field still described as required. A collection is always written,
        /// as empty when there is nothing, so required is what the wire carries; and
        /// a body that omits it, or writes null, means the same thing by it.
        val private lenient:
          fallback: 'A ->
            name: string -> part: ObjectPart<'T,'A> -> ObjectPart<'T,'A>
        
        val private toDictionary:
          map: Map<string,'V> ->
            System.Collections.Generic.Dictionary<string,'V>
        
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
        val nullable<'T,'F when 'F: not struct and 'F: not null> :
          name: string ->
            schema: Schema<'F> ->
            get: ('T -> 'F | null) -> ObjectPart<'T,('F | null)>
            when 'F: not struct and 'F: not null
        
        /// <summary>A nullable string, read and handed back as one.</summary>
        /// <example><code lang="fsharp">
        /// WireField.text "billNumber" (fun r -> r.BillNumber)
        /// </code></example>
        val text:
          name: string ->
            get: ('T -> string | null) -> ObjectPart<'T,(string | null)>
        
        /// <summary>A nullable string with rules of its own.</summary>
        /// <remarks>
        /// The constraint applies to the value when there is one. A <c>null</c> is
        /// not a rule violation; say so with <c>Schema.required</c> if it should be.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// WireField.constrainedText "role" (Check.oneOf [ "admin"; "member" ]) (fun r -> r.Role)
        /// </code></example>
        val constrainedText:
          name: string ->
            check: ConstraintCheck<string> ->
            get: ('T -> string | null) -> ObjectPart<'T,(string | null)>
        
        /// <summary>A <c>Nullable&lt;int&gt;</c>.</summary>
        val int:
          name: string ->
            get: ('T -> System.Nullable<int>) ->
            ObjectPart<'T,System.Nullable<int>>
        
        /// <summary>A <c>Nullable&lt;int64&gt;</c>.</summary>
        val int64:
          name: string ->
            get: ('T -> System.Nullable<int64>) ->
            ObjectPart<'T,System.Nullable<int64>>
        
        /// <summary>A <c>Nullable&lt;decimal&gt;</c>. Use this for money rather than float.</summary>
        val decimal:
          name: string ->
            get: ('T -> System.Nullable<decimal>) ->
            ObjectPart<'T,System.Nullable<decimal>>
        
        /// <summary>A <c>Nullable&lt;float&gt;</c>.</summary>
        val float:
          name: string ->
            get: ('T -> System.Nullable<float>) ->
            ObjectPart<'T,System.Nullable<float>>
        
        /// <summary>A <c>Nullable&lt;bool&gt;</c>.</summary>
        val bool:
          name: string ->
            get: ('T -> System.Nullable<bool>) ->
            ObjectPart<'T,System.Nullable<bool>>
        
        /// <summary>A <c>Nullable&lt;Guid&gt;</c>.</summary>
        val guid:
          name: string ->
            get: ('T -> System.Nullable<System.Guid>) ->
            ObjectPart<'T,System.Nullable<System.Guid>>
        
        /// <summary>A <c>Nullable&lt;DateOnly&gt;</c>.</summary>
        val dateOnly:
          name: string ->
            get: ('T -> System.Nullable<System.DateOnly>) ->
            ObjectPart<'T,System.Nullable<System.DateOnly>>
        
        /// <summary>A <c>Nullable&lt;DateTimeOffset&gt;</c>.</summary>
        val dateTimeOffset:
          name: string ->
            get: ('T -> System.Nullable<System.DateTimeOffset>) ->
            ObjectPart<'T,System.Nullable<System.DateTimeOffset>>
        
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
        val array:
          name: string ->
            element: Schema<'F> ->
            get: ('T -> 'F array | null) -> ObjectPart<'T,'F array>
        
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
        val dictionary:
          name: string ->
            value: Schema<'V> ->
            get: ('T -> System.Collections.Generic.IDictionary<string,'V> | null) ->
            ObjectPart<'T,System.Collections.Generic.Dictionary<string,'V>>
        
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
        val requiredArray:
          name: string ->
            element: Schema<'F> ->
            get: ('T -> 'F array) -> ObjectPart<'T,'F array>
        
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
        val requiredDictionary:
          name: string ->
            value: Schema<'V> ->
            get: ('T -> System.Collections.Generic.IDictionary<string,'V>) ->
            ObjectPart<'T,System.Collections.Generic.Dictionary<string,'V>>

