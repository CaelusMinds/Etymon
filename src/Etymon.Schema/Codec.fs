namespace Etymon

open System
open System.Text.Json

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
        Write: Utf8JsonWriter -> 'T -> unit
        /// Reads a value, collecting every reason it could not be read.
        Read: DecodeMode -> Path -> JsonElement -> Validation<'T>
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
type ObjectPart<'T, 'A> =
    {
        /// The fields described so far.
        Fields: FieldInfo list
        /// Writes those fields as properties of the object being written.
        WriteFields: Utf8JsonWriter -> 'T -> unit
        /// Reads those fields, accumulating errors across all of them.
        ReadFields: DecodeMode -> Path -> JsonElement -> Validation<'A>
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
        TryWrite: Utf8JsonWriter -> string -> 'T -> bool
        /// Reads a value of this case.
        Read: DecodeMode -> Path -> JsonElement -> Validation<'T>
    }

/// Shared machinery for reading JSON. Internal: consumers use the combinators in
/// <c>Schema</c> rather than these.
module internal Codec =

    /// The name a JSON value would go by in an error message.
    let kindName (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.String -> "string"
        | JsonValueKind.Number -> "number"
        | JsonValueKind.True
        | JsonValueKind.False -> "boolean"
        | JsonValueKind.Array -> "array"
        | JsonValueKind.Object -> "object"
        | JsonValueKind.Null -> "null"
        | _ -> "nothing"

    let mismatch (expected: string) (path: Path) (element: JsonElement) : Validation<'T> =
        let actual = kindName element

        Validation.error
            path
            (ErrorReason.TypeMismatch(expected, actual))
            $"must be a %s{expected}, but was a %s{actual}"

    let rejected (code: string) (message: string) (path: Path) : Validation<'T> =
        Validation.error path (ErrorReason.Rejected code) message

    let missing (path: Path) : Validation<'T> =
        Validation.error path ErrorReason.Missing "is required"

    /// The text of a JSON value when it is a string, or -- only in coercing mode
    /// -- the literal text of a number or boolean. The single place that decides
    /// what "a string may stand in for it" means.
    let coercibleText (mode: DecodeMode) (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.String -> Some(element.GetString())
        | JsonValueKind.Number
        | JsonValueKind.True
        | JsonValueKind.False when mode = DecodeMode.Coercing -> Some(element.GetRawText())
        | _ -> None

    /// Writes a value to UTF-8 bytes using a schema's writer and the given options.
    let toUtf8With (options: JsonWriterOptions) (write: Utf8JsonWriter -> 'T -> unit) (value: 'T) =
        use stream = new IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream, options)
        write writer value
        writer.Flush()
        stream.ToArray()

    /// The writer options Etymon uses unless told otherwise.
    ///
    /// The encoder is System.Text.Json's default, which escapes characters that
    /// are dangerous inside HTML -- '+' becomes +, '&' becomes &. That
    /// is valid JSON and round-trips exactly, but it surprises people reading the
    /// output. `Schema.toJsonWith` exists for anyone who would rather have
    /// JavaScriptEncoder.UnsafeRelaxedJsonEscaping and is not embedding the
    /// result in a web page.
    let defaultOptions = JsonWriterOptions(Indented = false, SkipValidation = false)

    let indentedOptions = JsonWriterOptions(Indented = true, SkipValidation = false)

    /// Writes a value to UTF-8 bytes using a schema's writer.
    let toUtf8 (write: Utf8JsonWriter -> 'T -> unit) (indented: bool) (value: 'T) =
        toUtf8With (if indented then indentedOptions else defaultOptions) write value
