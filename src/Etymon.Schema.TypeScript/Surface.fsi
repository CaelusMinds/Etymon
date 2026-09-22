
namespace FSharp



namespace Etymon
    
    /// <summary>
    /// TypeScript declarations generated from a schema.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Types only. No fetch client, no runtime validator, no opinion about axios
    /// against fetch or ESM against CommonJS. Those are choices that age badly and
    /// that every frontend has already made differently; the part with a single
    /// right answer is the shape of the data, and that is what this emits.
    /// </para>
    /// <para>
    /// It emits from the schema directly rather than going through an OpenAPI
    /// document, which is one fewer representation to lose information in.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module TypeScript =
        
        /// TypeScript keeps `undefined` for "absent" and `null` for "present and
        /// empty", exactly as JSON Schema does. Etymon's optional and nullable map
        /// onto them one for one, which is why the two were kept apart in the schema
        /// type in the first place.
        val private typeOf: info: SchemaInfo -> string
        
        val private parenthesise: info: SchemaInfo -> string
        
        /// An enumeration becomes a union of literals rather than a bare string, so
        /// that a typo is a compile error in the frontend as well.
        val private literalUnion: info: SchemaInfo -> string option
        
        /// <summary>
        /// The warning a number needs, when TypeScript cannot hold what the schema
        /// promises.
        /// </summary>
        /// <remarks>
        /// TypeScript has one numeric type, and <c>JSON.parse</c> produces IEEE-754
        /// doubles from it. An <c>int64</c> past 2^53, or a <c>decimal</c> with more
        /// significant digits than a double holds, arrives in the frontend quietly
        /// rounded. Etymon cannot fix that — the value really is a JSON number, and
        /// emitting <c>string</c> would misdescribe the payload — but it refuses to
        /// let the generated type imply a precision the wire format does not have.
        /// </remarks>
        val private precisionNote: info: SchemaInfo -> string option
        
        val private comment:
          indent: string ->
            text: string option -> builder: System.Text.StringBuilder -> unit
        
        val private emitObject:
          name: string ->
            fields: FieldInfo list -> builder: System.Text.StringBuilder -> unit
        
        val private emitUnion:
          name: string ->
            tag: string ->
            cases: (string * SchemaInfo) list ->
            builder: System.Text.StringBuilder -> unit
        
        /// <summary>
        /// Declarations for every named type a schema reaches, ready to write to a
        /// <c>.d.ts</c> file.
        /// </summary>
        /// <remarks>
        /// Types are emitted in name order, so the same schema always produces the
        /// same file and a change to it shows up as a reviewable diff rather than
        /// noise.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// TypeScript.emit [ Schema.info personSchema ]
        /// // export interface Address { readonly street: string; ... }
        /// // export interface Person { readonly name: string; readonly email?: string; ... }
        /// </code></example>
        val emit: schemas: SchemaInfo list -> string
        
        /// <summary>Declarations for one schema.</summary>
        /// <example><code lang="fsharp">
        /// TypeScript.emitOne personSchema
        /// </code></example>
        val emitOne: schema: Schema<'T> -> string

