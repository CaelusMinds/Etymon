namespace Etymon

open System.Text.Json.Nodes
open FsCheck
open FsCheck.FSharp

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
    let private InvariantAttempts = 500

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
    let valid (schema: Schema<'T>) : Gen<'T> =
        JsonGen.forInfo schema.Info
        |> Gen.map (fun json ->
            let text = if isNull json then "null" else json.ToJsonString()

            match Schema.fromJson schema text with
            | Ok value -> value
            | Error errors ->
                raise (
                    GenerationFailed(
                        "decode",
                        1,
                        $"The generator produced JSON the schema rejects, which means the two disagree about what the schema means.\nJSON: %s{text}\nErrors: %s{ValidationErrors.format errors}"
                    )
                )
        )

    /// <summary>Values the schema accepts, as an FsCheck arbitrary.</summary>
    /// <example><code lang="fsharp">
    /// testPropertyWithConfig config "round-trips" (Prop.forAll (Generate.arbitrary personSchema) check)
    /// </code></example>
    let arbitrary (schema: Schema<'T>) : Arbitrary<'T> = Arb.fromGen (valid schema)

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
    let validSatisfying (invariants: Invariants<'T>) (schema: Schema<'T>) : Gen<'T> =
        let source = valid schema

        if List.isEmpty invariants.Rules then
            source
        else
            gen {
                let! candidates = Gen.listOfLength InvariantAttempts source

                match
                    candidates
                    |> List.tryFind (fun c -> Validation.isOk (Invariants.check invariants c))
                with
                | Some value -> return value
                | None ->
                    let names = invariants.Rules |> List.map (fun r -> r.Name) |> String.concat ", "

                    return
                        raise (
                            GenerationFailed(
                                names,
                                InvariantAttempts,
                                $"Could not generate a value of %s{invariants.TypeName} satisfying its invariants (%s{names}) in %d{InvariantAttempts} attempts. Cross-field rules usually cannot be satisfied by chance; supply a generator that constructs valid values directly."
                            )
                        )
            }

    /// <summary>Values satisfying both the schema and the invariants, as an arbitrary.</summary>
    /// <example><code lang="fsharp">
    /// Generate.arbitrarySatisfying bookingInvariants bookingSchema
    /// </code></example>
    let arbitrarySatisfying (invariants: Invariants<'T>) (schema: Schema<'T>) : Arbitrary<'T> =
        Arb.fromGen (validSatisfying invariants schema)

    // ---- values that should be rejected --------------------------------------

    /// The fields of an object schema, when it is one.
    let private fieldsOf (info: SchemaInfo) =
        match SchemaInfo.strip info with
        | SObject(_, fields) -> fields
        | _ -> []

    /// A JSON value of a definitely different type, for provoking a mismatch.
    let private wrongTypeFor (info: SchemaInfo) : JsonNode * string =
        match SchemaInfo.strip info with
        | SPrim PrimKind.String -> JsonValue.Create 1 :> JsonNode, "number"
        | SPrim PrimKind.Bool -> JsonValue.Create "not a bool" :> JsonNode, "string"
        | SList _
        | SObject _
        | SMap _
        | SUnion _ -> JsonValue.Create 1 :> JsonNode, "number"
        | _ -> JsonValue.Create "not a number" :> JsonNode, "string"

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
    let invalid (schema: Schema<'T>) : Gen<InvalidCase> =
        let fields = fieldsOf schema.Info

        if List.isEmpty fields then
            // Nothing structural to corrupt, so provoke a top-level mismatch.
            let wrong, actual = wrongTypeFor schema.Info

            Gen.constant
                {
                    Description = "a value of the wrong type"
                    Json = wrong.ToJsonString()
                    ExpectedPath = ""
                    ExpectedReason = ErrorReason.TypeMismatch("", actual)
                }
        else
            let required = fields |> List.filter (fun f -> f.Required)

            gen {
                let! baseJson = JsonGen.forInfo schema.Info

                let asObject () =
                    JsonNode.Parse(baseJson.ToJsonString()) :?> JsonObject

                let removeRequired =
                    match required with
                    | [] -> []
                    | _ ->
                        [
                            for field in required ->
                                gen {
                                    let object = asObject ()
                                    object.Remove field.Name |> ignore

                                    return
                                        {
                                            Description = $"the required field '%s{field.Name}' removed"
                                            Json = object.ToJsonString()
                                            ExpectedPath = field.Name
                                            ExpectedReason = ErrorReason.Missing
                                        }
                                }
                        ]

                let wrongType =
                    [
                        for field in fields ->
                            gen {
                                let object = asObject ()
                                let wrong, actual = wrongTypeFor field.Schema
                                object[field.Name] <- wrong

                                return
                                    {
                                        Description = $"the field '%s{field.Name}' given a %s{actual}"
                                        Json = object.ToJsonString()
                                        ExpectedPath = field.Name
                                        ExpectedReason = ErrorReason.TypeMismatch("", actual)
                                    }
                            }
                    ]

                return! Gen.oneof (removeRequired @ wrongType)
            }
