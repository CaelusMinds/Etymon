namespace Etymon

open System
open System.Text.Json
open System.Text.Json.Nodes

/// <summary>
/// Which dialect a generated schema is written in.
/// </summary>
/// <remarks>
/// OpenAPI 3.1 is a superset of JSON Schema 2020-12, so the two outputs differ in
/// only two places: where reusable schemas live (<c>#/$defs</c> against
/// <c>#/components/schemas</c>), and whether the document declares a
/// <c>$schema</c>.
/// </remarks>
[<RequireQualifiedAccess>]
type SchemaDialect =
    /// A standalone JSON Schema 2020-12 document, with reusable schemas in <c>$defs</c>.
    | JsonSchema2020
    /// OpenAPI 3.1 component schemas, referenced under <c>#/components/schemas</c>.
    | OpenApi31

/// <summary>
/// Generates JSON Schema 2020-12 and OpenAPI 3.1 from a <see cref="T:Etymon.Schema`1"/>.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a pure function of <see cref="T:Etymon.SchemaInfo"/> — the
/// untyped half of a schema. Nothing reads the codec, so a generated document
/// cannot describe something different from what the codec actually does.
/// </para>
/// <para>
/// Constraints that Etymon can express as keywords become keywords. A constraint
/// it cannot — <c>Opaque</c>, the escape hatch for a rule written as an arbitrary
/// predicate — is appended to the description instead, so that a reader is told
/// about a rule the schema cannot enforce rather than being quietly misled into
/// thinking there isn't one.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module OpenApi =

    let private refPrefix (dialect: SchemaDialect) =
        match dialect with
        | SchemaDialect.JsonSchema2020 -> "#/$defs/"
        | SchemaDialect.OpenApi31 -> "#/components/schemas/"

    // One overload per type, rather than one generic helper. JsonValue.Create<T>
    // builds a JsonValueCustomized, which on net8.0 cannot be written out without
    // a TypeInfoResolver -- a failure that does not occur on net10.0, and so would
    // have shipped unnoticed had the suite not been multi-targeted.
    [<AbstractClass; Sealed>]
    type private Node =
        static member Of(value: string) : JsonNode = JsonValue.Create value
        static member Of(value: int) : JsonNode = JsonValue.Create value
        static member Of(value: int64) : JsonNode = JsonValue.Create value
        static member Of(value: decimal) : JsonNode = JsonValue.Create value
        static member Of(value: float) : JsonNode = JsonValue.Create value
        static member Of(value: bool) : JsonNode = JsonValue.Create value

    let private numToNode (n: Num) : JsonNode =
        match n with
        | Num.Int v -> Node.Of v
        | Num.Dec v -> Node.Of v
        | Num.Float v -> Node.Of v

    /// The scalar keywords for a primitive. `format` follows the JSON Schema
    /// vocabulary, and the `int32`/`int64`/`double` formats follow OpenAPI's,
    /// which readers of both expect.
    let private primKeywords (kind: PrimKind) =
        match kind with
        | PrimKind.String -> [ "type", Node.Of "string" ]
        | PrimKind.Int -> [ "type", Node.Of "integer"; "format", Node.Of "int32" ]
        | PrimKind.Int64 -> [ "type", Node.Of "integer"; "format", Node.Of "int64" ]
        | PrimKind.Float -> [ "type", Node.Of "number"; "format", Node.Of "double" ]
        | PrimKind.Decimal -> [ "type", Node.Of "number" ]
        | PrimKind.Bool -> [ "type", Node.Of "boolean" ]
        | PrimKind.Guid -> [ "type", Node.Of "string"; "format", Node.Of "uuid" ]
        | PrimKind.DateTimeOffset -> [ "type", Node.Of "string"; "format", Node.Of "date-time" ]
        | PrimKind.DateOnly -> [ "type", Node.Of "string"; "format", Node.Of "date" ]
        | PrimKind.TimeOnly -> [ "type", Node.Of "string"; "format", Node.Of "time" ]
        | PrimKind.TimeSpan -> [ "type", Node.Of "string"; "format", Node.Of "duration" ]
        | PrimKind.Bytes -> [ "type", Node.Of "string"; "contentEncoding", Node.Of "base64" ]
        // Deliberately empty: "any JSON at all" is the absence of constraints.
        | PrimKind.Raw -> []

    /// Turns a constraint into schema keywords, or into prose when it cannot be
    /// a keyword. Returning both is what keeps the promise that an unrepresentable
    /// rule is documented rather than silently dropped.
    let private constraintKeywords (c: Constraint) : (string * JsonNode) list * string option =
        match c with
        | Constraint.Length(min, max) ->
            let keywords =
                [
                    match min with
                    | Some v -> "minLength", Node.Of v
                    | None -> ()
                    match max with
                    | Some v -> "maxLength", Node.Of v
                    | None -> ()
                ]

            keywords, None

        | Constraint.Range(min, max, exclusiveMin, exclusiveMax) ->
            let keywords =
                [
                    match min with
                    | Some v -> (if exclusiveMin then "exclusiveMinimum" else "minimum"), numToNode v
                    | None -> ()
                    match max with
                    | Some v -> (if exclusiveMax then "exclusiveMaximum" else "maximum"), numToNode v
                    | None -> ()
                ]

            keywords, None

        | Constraint.Pattern regex -> [ "pattern", Node.Of regex ], None
        | Constraint.HasFormat format -> [ "format", Node.Of(Format.name format) ], None

        | Constraint.OneOf values ->
            let array = JsonArray()

            for value in values do
                array.Add(Node.Of value)

            [ "enum", array :> JsonNode ], None

        | Constraint.Items(min, max, unique) ->
            let keywords =
                [
                    match min with
                    | Some v -> "minItems", Node.Of v
                    | None -> ()
                    match max with
                    | Some v -> "maxItems", Node.Of v
                    | None -> ()
                    if unique then
                        "uniqueItems", Node.Of true
                ]

            keywords, None

        // The escape hatch. There is no keyword for "satisfies this F# predicate",
        // so it is stated in prose and never pretended to be enforceable.
        | Constraint.Opaque(_, description) -> [], Some description

    let rec private render (dialect: SchemaDialect) (inlineNamed: bool) (info: SchemaInfo) : JsonObject =
        let meta = SchemaInfo.meta info
        let stripped = SchemaInfo.strip info

        // A named object or union becomes a reference, so that it is written once
        // and pointed at thereafter. The one exception is when we are rendering
        // the definition itself.
        let asReference =
            if inlineNamed then
                None
            else
                match stripped with
                | SObject(name, _)
                | SUnion(name, _, _)
                | SRef name -> Some name
                | _ -> None

        match asReference with
        | Some name ->
            let result = JsonObject()
            result.Add("$ref", Node.Of(refPrefix dialect + name))
            result
        | None ->

        let result = JsonObject()

        let add key value =
            if not (result.ContainsKey key) then
                result.Add(key, value)

        match stripped with
        | SPrim kind ->
            for key, value in primKeywords kind do
                add key value

        | SNullable inner ->
            let rendered = render dialect false inner

            // In 2020-12 and OpenAPI 3.1 a nullable value is a union of types. A
            // reference cannot carry a type keyword, so it needs anyOf instead.
            if rendered.ContainsKey "$ref" then
                let choices = JsonArray()
                choices.Add(rendered)
                let nullChoice = JsonObject()
                nullChoice.Add("type", Node.Of "null")
                choices.Add(nullChoice)
                add "anyOf" (choices :> JsonNode)
            else
                match rendered["type"] with
                | null ->
                    for KeyValue(key, value) in rendered do
                        add key (value.DeepClone())
                | existing ->
                    let types = JsonArray()
                    types.Add(Node.Of(existing.GetValue<string>()))
                    types.Add(Node.Of "null")
                    rendered.Remove "type" |> ignore
                    add "type" (types :> JsonNode)

                    for KeyValue(key, value) in rendered do
                        add key (value.DeepClone())

        | SList inner ->
            add "type" (Node.Of "array")
            add "items" (render dialect false inner :> JsonNode)

        | SMap inner ->
            add "type" (Node.Of "object")
            add "additionalProperties" (render dialect false inner :> JsonNode)

        | SObject(_, fields) ->
            add "type" (Node.Of "object")

            let properties = JsonObject()

            for field in fields do
                let rendered = render dialect false field.Schema

                match field.Description with
                | Some description when not (rendered.ContainsKey "description") ->
                    rendered.Add("description", Node.Of description)
                | _ -> ()

                match field.Default with
                | Some value when not (rendered.ContainsKey "default") -> rendered.Add("default", value.DeepClone())
                | _ -> ()

                if field.Sensitive && not (rendered.ContainsKey "writeOnly") then
                    rendered.Add("writeOnly", Node.Of true)

                properties.Add(field.Name, rendered)

            add "properties" (properties :> JsonNode)

            let required = fields |> List.filter (fun f -> f.Required)

            if not (List.isEmpty required) then
                let names = JsonArray()

                for field in required do
                    names.Add(Node.Of field.Name)

                add "required" (names :> JsonNode)

        // Deliberately no "additionalProperties": false. Etymon's decoder ignores
        // keys it does not know about, so claiming otherwise would describe a
        // stricter contract than the code actually enforces.

        | SUnion(_, tag, cases) ->
            let choices = JsonArray()

            for caseTag, casePayload in cases do
                let choice = JsonObject()
                choice.Add("type", Node.Of "object")

                let properties = JsonObject()
                let tagSchema = JsonObject()
                tagSchema.Add("const", Node.Of caseTag)
                properties.Add(tag, tagSchema)

                let requiredNames = JsonArray()
                requiredNames.Add(Node.Of tag)

                // A case carrying no payload is written as the tag alone, so its
                // schema must not require a value.
                match SchemaInfo.strip casePayload with
                | SPrim PrimKind.Raw -> ()
                | _ ->
                    properties.Add("value", render dialect false casePayload)
                    requiredNames.Add(Node.Of "value")

                choice.Add("properties", properties)
                choice.Add("required", requiredNames)
                choices.Add(choice)

            add "oneOf" (choices :> JsonNode)

        | SRef name -> add "$ref" (Node.Of(refPrefix dialect + name))
        | SAnnotated _ -> failwith "unreachable: annotations are stripped before this point"

        // ---- metadata, applied over the shape --------------------------------

        let mutable opaqueNotes = []

        for c in meta.Constraints do
            let keywords, note = constraintKeywords c

            for key, value in keywords do
                add key value

            match note with
            | Some text -> opaqueNotes <- opaqueNotes @ [ text ]
            | None -> ()

        let description =
            match meta.Description, opaqueNotes with
            | None, [] -> None
            | Some text, [] -> Some text
            | None, notes -> Some(String.Join(". ", notes))
            | Some text, notes -> Some(text + ". " + String.Join(". ", notes))

        match description with
        | Some text -> add "description" (Node.Of text)
        | None -> ()

        match meta.Examples with
        | [] -> ()
        | examples ->
            let array = JsonArray()

            for value in examples do
                array.Add(value.DeepClone())

            add "examples" (array :> JsonNode)

        if meta.Sensitive then
            add "writeOnly" (Node.Of true)

        result

    /// <summary>
    /// The reusable component schemas for every named type reachable from this
    /// one, keyed by name. This is what goes under <c>components/schemas</c> in an
    /// OpenAPI document.
    /// </summary>
    /// <example><code lang="fsharp">
    /// OpenApi.toComponents personSchema
    /// // { "Address": { ... }, "Person": { ... } }
    /// </code></example>
    let toComponents (schema: Schema<'T>) : JsonObject =
        let result = JsonObject()

        // Sorted by name so that the same schema always produces byte-identical
        // output, which is what makes a snapshot test meaningful.
        for KeyValue(name, info) in SchemaInfo.definitions schema.Info do
            result.Add(name, render SchemaDialect.OpenApi31 true info)

        result

    /// <summary>
    /// The pair needed to place a schema in an OpenAPI document: its named types
    /// for <c>components/schemas</c>, and a root already addressed at them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>toComponents</c> gives the components and does not say which of them is
    /// the root, so a caller cannot write the reference that points at it.
    /// <c>toJsonSchema</c> gives a root, addressed at <c>#/$defs/</c>, which is
    /// the wrong address space for an OpenAPI document. Assembling one from many
    /// schemas therefore meant a lift-and-re-address pass in every consumer.
    /// </para>
    /// <para>
    /// That pass has a trap in it. The natural implementation reads <c>$ref</c>
    /// at the <em>root</em> of the standalone document and rewrites it, which is
    /// right for an operation returning one object and silently wrong for one
    /// returning a list: an array carries its <c>$ref</c> one level down, under
    /// <c>items</c>, so the root has none, the lift finds nothing, and the
    /// response is described as an empty object. Nothing fails. The document is
    /// still valid OpenAPI, and a test asserting "this path is described" is
    /// satisfied by a description that says nothing.
    /// </para>
    /// <para>
    /// <c>Root</c> is whatever the schema is — a reference, an array of
    /// references, a nullable one, or an inline object when there is nothing
    /// named to reference.
    /// </para>
    /// </remarks>
    /// <example><code lang="fsharp">
    /// let listing = OpenApi.toComponentsWithRoot accountListSchema
    /// // listing.Root       = { "type": "array", "items": { "$ref": "#/components/schemas/Account" } }
    /// // listing.Components = { "Account": { ... } }
    /// </code></example>
    let toComponentsWithRoot
        (schema: Schema<'T>)
        : {|
              Root: JsonObject
              Components: JsonObject
          |}
        =
        let definitions = SchemaInfo.definitions schema.Info

        {|
            // Inline only when there is nothing named to point at. Otherwise the
            // root is a reference, and the dialect decides its address -- which
            // is the whole reason this does not need a rewriting pass.
            Root = render SchemaDialect.OpenApi31 (Map.isEmpty definitions) schema.Info
            Components = toComponents schema
        |}

    /// <summary>
    /// A standalone JSON Schema 2020-12 document. Named types are written once
    /// under <c>$defs</c> and referenced, so a recursive type is expressible.
    /// </summary>
    /// <example><code lang="fsharp">
    /// OpenApi.toJsonSchema personSchema
    /// // { "$schema": "...", "$ref": "#/$defs/Person", "$defs": { ... } }
    /// </code></example>
    let toJsonSchema (schema: Schema<'T>) : JsonObject =
        let dialect = SchemaDialect.JsonSchema2020
        let definitions = SchemaInfo.definitions schema.Info

        let body = render dialect (Map.isEmpty definitions) schema.Info

        // Built fresh so that "$schema" comes first, which is where every reader
        // expects it and where a snapshot will keep it.
        let root = JsonObject()
        root.Add("$schema", Node.Of "https://json-schema.org/draft/2020-12/schema")

        for KeyValue(key, value) in body do
            root.Add(key, value.DeepClone())

        if not (Map.isEmpty definitions) then
            let defs = JsonObject()

            for KeyValue(name, info) in definitions do
                defs.Add(name, render dialect true info)

            root.Add("$defs", defs)

        root

    // Relaxed escaping rather than the HTML-safe default. A generated schema is a
    // document people read and paste into tooling, and the default turns the "+" in
    // a regex like ^\d+$ into \u002B -- valid, parses back identically, and awful to
    // look at. The escaping only matters when JSON is injected into HTML without
    // escaping, which is not what happens to an OpenAPI document.
    let private writerOptions =
        JsonSerializerOptions(
            WriteIndented = true,
            Encoder = Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        )

    /// <summary>
    /// A JSON Schema 2020-12 document, rendered as indented text. The form a
    /// snapshot test compares and a person reads.
    /// </summary>
    /// <example><code lang="fsharp">
    /// printfn "%s" (OpenApi.toJsonSchemaText personSchema)
    /// </code></example>
    let toJsonSchemaText (schema: Schema<'T>) =
        (toJsonSchema schema).ToJsonString writerOptions

    /// <summary>Component schemas, rendered as indented text.</summary>
    /// <example><code lang="fsharp">
    /// printfn "%s" (OpenApi.toComponentsText personSchema)
    /// </code></example>
    let toComponentsText (schema: Schema<'T>) =
        (toComponents schema).ToJsonString writerOptions
