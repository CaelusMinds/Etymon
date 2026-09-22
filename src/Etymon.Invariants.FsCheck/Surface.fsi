
namespace FSharp



namespace Etymon
    
    /// <summary>
    /// Why a generator could not produce a value.
    /// </summary>
    /// <remarks>
    /// Raised rather than returned. A generator that cannot satisfy a schema is a
    /// mistake in the schema or the generator, not an outcome a property test can
    /// meaningfully handle — and silently producing fewer cases, which is what
    /// filtering normally does, hides the mistake until the day the test was needed.
    /// </remarks>
    exception GenerationFailed of constraintName: string * attempts: int *
                                  message: string
    
    /// <summary>
    /// Generates JSON that satisfies a <see cref="T:Etymon.SchemaInfo"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Values are generated as JSON and then decoded through the schema, rather than
    /// being built directly. That reuses the decoder — already the most heavily
    /// tested code in the suite — instead of writing a second construction path that
    /// could disagree with it. A generated value is valid by the same definition of
    /// valid that production code uses.
    /// </para>
    /// <para>
    /// Constraints are satisfied constructively wherever Etymon understands them
    /// well enough: a length bound picks a string of that length, a range picks a
    /// number inside it, an enumeration picks a member. Where it cannot — a regular
    /// expression, or an <c>Opaque</c> predicate — it generates and filters, and
    /// raises <see cref="T:Etymon.GenerationFailed"/> if the budget runs out rather
    /// than quietly yielding a thinner sample.
    /// </para>
    /// </remarks>
    [<RequireQualifiedAccess>]
    module JsonGen =
        
        /// How many times to retry a constraint that can only be satisfied by
        /// filtering. Generous, because failing is much worse than being slow, and
        /// still finite, because hanging is worse than either.
        [<Literal>]
        val private FilterAttempts: int = 300
        
        val private node: value: string -> System.Text.Json.Nodes.JsonNode
        
        val private numberNode: value: int64 -> System.Text.Json.Nodes.JsonNode
        
        val private decimalNode:
          value: decimal -> System.Text.Json.Nodes.JsonNode
        
        val private floatNode: value: float -> System.Text.Json.Nodes.JsonNode
        
        val private boolNode: value: bool -> System.Text.Json.Nodes.JsonNode
        
        val private lengthBounds: constraints: Constraint list -> int * int
        
        val private itemBounds: constraints: Constraint list -> int * int
        
        val private mustBeDistinct: constraints: Constraint list -> bool
        
        val private enumValues:
          constraints: Constraint list -> string list option
        
        val private formatOf: constraints: Constraint list -> Format option
        
        val private rangeOf:
          constraints: Constraint list ->
            (Num option * Num option * bool * bool) option
        
        val private letters: FsCheck.Gen<char>
        
        val private word:
          shortest: int -> longest: int -> FsCheck.Gen<System.String>
        
        val private formatGen: format: Format -> FsCheck.Gen<string> option
        
        /// Applies the constraints that can only be checked, never constructed.
        /// Raises rather than thinning the sample, so that an impossible constraint
        /// is a loud failure at the moment it happens.
        val private filterBy:
          constraints: Constraint list ->
            satisfies: (Constraint -> string -> bool) ->
            source: FsCheck.Gen<string> -> FsCheck.Gen<string>
        
        val private stringGen:
          constraints: Constraint list ->
            FsCheck.Gen<System.Text.Json.Nodes.JsonNode>
        
        val private intGen:
          constraints: Constraint list ->
            floor: int64 ->
            ceiling: int64 -> FsCheck.Gen<System.Text.Json.Nodes.JsonNode>
        
        val private decimalGen:
          constraints: Constraint list ->
            FsCheck.Gen<System.Text.Json.Nodes.JsonNode>
        
        /// <summary>
        /// A generator of JSON satisfying a schema description.
        /// </summary>
        /// <example><code lang="fsharp">
        /// JsonGen.forInfo (Schema.info personSchema) |> Gen.sample 1
        /// </code></example>
        val forInfo:
          info: SchemaInfo -> FsCheck.Gen<System.Text.Json.Nodes.JsonNode>

namespace Etymon
    
    /// <summary>
    /// A value that a schema ought to reject, together with what is wrong with it
    /// and where the error should be reported.
    /// </summary>
    /// <remarks>
    /// Negative testing normally checks only that something failed, which passes
    /// just as well when the decoder failed for the wrong reason. Carrying the
    /// expected path lets a test assert that the error landed where it should.
    /// </remarks>
    type InvalidCase =
        {
          
          /// What was done to the value to make it invalid.
          Description: string
          
          /// The resulting JSON.
          Json: string
          
          /// Where the resulting error should be reported, rendered as a path.
          ExpectedPath: string
          
          /// The reason the decoder should give.
          ExpectedReason: ErrorReason
        }
    
    /// <summary>
    /// Generators of values that a schema accepts, and of values it should not.
    /// </summary>
    /// <remarks>
    /// Valid values are generated as JSON and decoded through the schema itself,
    /// rather than constructed directly. That reuses the decoder instead of adding a
    /// second construction path that could disagree with it — a generated value is
    /// valid by exactly the definition production code uses.
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Generate =
        
        [<Literal>]
        val private InvariantAttempts: int = 500
        
        /// <summary>
        /// Values the schema accepts.
        /// </summary>
        /// <remarks>
        /// Raises <see cref="T:Etymon.GenerationFailed"/> if a generated value does
        /// not decode. That would mean the generator and the decoder disagree about
        /// what the schema means, which is a bug worth stopping for rather than a
        /// case to discard.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Generate.valid personSchema |> Gen.sample 10
        /// </code></example>
        val valid: schema: Schema<'T> -> FsCheck.Gen<'T>
        
        /// <summary>Values the schema accepts, as an FsCheck arbitrary.</summary>
        /// <example><code lang="fsharp">
        /// testPropertyWithConfig config "round-trips" (Prop.forAll (Generate.arbitrary personSchema) check)
        /// </code></example>
        val arbitrary: schema: Schema<'T> -> FsCheck.Arbitrary<'T>
        
        /// <summary>
        /// Values the schema accepts <em>and</em> which satisfy the type's
        /// invariants.
        /// </summary>
        /// <remarks>
        /// Cross-field rules can rarely be satisfied constructively, so this filters.
        /// If the budget runs out it raises rather than yielding a thinner sample:
        /// a property test that quietly ran on twelve cases instead of a hundred has
        /// told you nothing, and told you it confidently.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Generate.validSatisfying bookingInvariants bookingSchema |> Gen.sample 10
        /// </code></example>
        val validSatisfying:
          invariants: Invariants<'T> -> schema: Schema<'T> -> FsCheck.Gen<'T>
        
        /// <summary>Values satisfying both the schema and the invariants, as an arbitrary.</summary>
        /// <example><code lang="fsharp">
        /// Generate.arbitrarySatisfying bookingInvariants bookingSchema
        /// </code></example>
        val arbitrarySatisfying:
          invariants: Invariants<'T> ->
            schema: Schema<'T> -> FsCheck.Arbitrary<'T>
        
        /// The fields of an object schema, when it is one.
        val private fieldsOf: info: SchemaInfo -> FieldInfo list
        
        /// A JSON value of a definitely different type, for provoking a mismatch.
        val private wrongTypeFor:
          info: SchemaInfo -> System.Text.Json.Nodes.JsonNode * string
        
        /// <summary>
        /// Values the schema should reject, each saying what is wrong and where the
        /// error should land.
        /// </summary>
        /// <remarks>
        /// Two corruptions are produced: a required field removed, and a field given
        /// a value of the wrong type. Both are chosen so that the expected error is
        /// unambiguous, which is what makes the assertion worth making.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// Generate.invalid personSchema |> Gen.sample 5
        /// </code></example>
        val invalid: schema: Schema<'T> -> FsCheck.Gen<InvalidCase>

namespace Etymon
    
    /// <summary>
    /// Ready-made property checks for a schema and its invariants.
    /// </summary>
    /// <remarks>
    /// These are the properties worth asserting about every schema, written once so
    /// that a consumer does not have to think of them. Each returns a
    /// <c>Property</c>, so it drops into whichever runner is already in use.
    /// </remarks>
    [<RequireQualifiedAccess>]
    module Properties =
        
        /// <summary>
        /// Encoding then decoding returns the value that went in.
        /// </summary>
        /// <remarks>
        /// The property every codec must satisfy, and the one that catches a
        /// normalising refinement, a lossy number format or an option that writes
        /// itself back differently from how it read.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// testProperty "Person round-trips" (fun () -> Properties.roundTrips Person.schema)
        /// </code></example>
        val roundTrips: schema: Schema<'T> -> FsCheck.Property when 'T: equality
        
        /// <summary>
        /// Every value the schema accepts satisfies the type's invariants.
        /// </summary>
        /// <remarks>
        /// A failure here means the schema is looser than the type: something the
        /// decoder lets through is not a legal value of the type, which is exactly
        /// the gap a constructor is supposed to close.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// testProperty "generated bookings are legal" (fun () ->
        ///     Properties.generatedValuesSatisfy Booking.invariants Booking.schema)
        /// </code></example>
        val generatedValuesSatisfy:
          invariants: Invariants<'T> -> schema: Schema<'T> -> FsCheck.Property
        
        /// <summary>
        /// Values the schema should reject are rejected, with the error in the right
        /// place.
        /// </summary>
        /// <remarks>
        /// Asserting only that decoding failed would pass just as well when it
        /// failed for the wrong reason, so this checks the path too.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// testProperty "bad input is rejected where it is bad" (fun () ->
        ///     Properties.invalidIsRejected Person.schema)
        /// </code></example>
        val invalidIsRejected: schema: Schema<'T> -> FsCheck.Property
        
        /// <summary>
        /// Decoding the same input twice gives the same answer, and encoding the
        /// same value twice gives the same bytes.
        /// </summary>
        /// <remarks>
        /// Worth asserting because a schema that iterates a dictionary or depends on
        /// the clock will pass every other test and then produce a snapshot diff
        /// nobody can explain.
        /// </remarks>
        /// <example><code lang="fsharp">
        /// testProperty "encoding is stable" (fun () -> Properties.isDeterministic Person.schema)
        /// </code></example>
        val isDeterministic: schema: Schema<'T> -> FsCheck.Property
        
        /// <summary>
        /// Every property worth asserting about a schema on its own, as one check.
        /// </summary>
        /// <example><code lang="fsharp">
        /// testProperty "Person behaves" (fun () -> Properties.all Person.schema)
        /// </code></example>
        val all: schema: Schema<'T> -> FsCheck.Property when 'T: equality

