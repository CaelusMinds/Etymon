
namespace FSharp



namespace Etymon
    
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
        
        val private refPrefix: dialect: SchemaDialect -> string
        
        [<AbstractClass; Sealed; Class>]
        type private Node =
            
            static member Of: value: bool -> System.Text.Json.Nodes.JsonNode
            
            static member Of: value: float -> System.Text.Json.Nodes.JsonNode
            
            static member Of: value: decimal -> System.Text.Json.Nodes.JsonNode
            
            static member Of: value: int64 -> System.Text.Json.Nodes.JsonNode
            
            static member Of: value: int -> System.Text.Json.Nodes.JsonNode
            
            static member Of: value: string -> System.Text.Json.Nodes.JsonNode
        
        val private numToNode: n: Num -> System.Text.Json.Nodes.JsonNode
        
        /// The scalar keywords for a primitive. `format` follows the JSON Schema
        /// vocabulary, and the `int32`/`int64`/`double` formats follow OpenAPI's,
        /// which readers of both expect.
        val private primKeywords:
          kind: PrimKind -> (string * System.Text.Json.Nodes.JsonNode) list
        
        /// Turns a constraint into schema keywords, or into prose when it cannot be
        /// a keyword. Returning both is what keeps the promise that an unrepresentable
        /// rule is documented rather than silently dropped.
        val private constraintKeywords:
          c: Constraint ->
            (string * System.Text.Json.Nodes.JsonNode) list * string option
        
        val private render:
          dialect: SchemaDialect ->
            inlineNamed: bool ->
            info: SchemaInfo -> System.Text.Json.Nodes.JsonObject
        
        /// <summary>
        /// The reusable component schemas for every named type reachable from this
        /// one, keyed by name. This is what goes under <c>components/schemas</c> in an
        /// OpenAPI document.
        /// </summary>
        /// <example><code lang="fsharp">
        /// OpenApi.toComponents personSchema
        /// // { "Address": { ... }, "Person": { ... } }
        /// </code></example>
        val toComponents:
          schema: Schema<'T> -> System.Text.Json.Nodes.JsonObject
        
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
        val toComponentsWithRoot:
          schema: Schema<'T> ->
            {| Components: System.Text.Json.Nodes.JsonObject;
               Root: System.Text.Json.Nodes.JsonObject |}
        
        /// <summary>
        /// A standalone JSON Schema 2020-12 document. Named types are written once
        /// under <c>$defs</c> and referenced, so a recursive type is expressible.
        /// </summary>
        /// <example><code lang="fsharp">
        /// OpenApi.toJsonSchema personSchema
        /// // { "$schema": "...", "$ref": "#/$defs/Person", "$defs": { ... } }
        /// </code></example>
        val toJsonSchema:
          schema: Schema<'T> -> System.Text.Json.Nodes.JsonObject
        
        val private writerOptions: System.Text.Json.JsonSerializerOptions
        
        /// <summary>
        /// A JSON Schema 2020-12 document, rendered as indented text. The form a
        /// snapshot test compares and a person reads.
        /// </summary>
        /// <example><code lang="fsharp">
        /// printfn "%s" (OpenApi.toJsonSchemaText personSchema)
        /// </code></example>
        val toJsonSchemaText: schema: Schema<'T> -> string
        
        /// <summary>Component schemas, rendered as indented text.</summary>
        /// <example><code lang="fsharp">
        /// printfn "%s" (OpenApi.toComponentsText personSchema)
        /// </code></example>
        val toComponentsText: schema: Schema<'T> -> string

