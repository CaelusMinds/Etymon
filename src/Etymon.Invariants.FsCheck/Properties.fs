namespace Etymon

open FsCheck
open FsCheck.FSharp

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
    let roundTrips (schema: Schema<'T>) : Property =
        Prop.forAll
            (Generate.arbitrary schema)
            (fun value ->
                match Schema.fromJson schema (Schema.toJson schema value) with
                | Ok decoded -> decoded = value
                | Error errors ->
                    failwithf
                        "A generated value did not survive a round trip.\nValue: %A\nJSON: %s\nErrors: %s"
                        value
                        (Schema.toJson schema value)
                        (ValidationErrors.format errors)
            )

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
    let generatedValuesSatisfy (invariants: Invariants<'T>) (schema: Schema<'T>) : Property =
        Prop.forAll
            (Generate.arbitrarySatisfying invariants schema)
            (fun value -> Validation.isOk (Invariants.check invariants value))

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
    let invalidIsRejected (schema: Schema<'T>) : Property =
        Prop.forAll
            (Arb.fromGen (Generate.invalid schema))
            (fun case ->
                match Schema.fromJson schema case.Json with
                | Ok _ -> failwithf "A value with %s was accepted.\nJSON: %s" case.Description case.Json
                | Error errors ->
                    let paths =
                        errors |> ValidationErrors.toList |> List.map (fun e -> Path.toString e.Path)

                    if List.contains case.ExpectedPath paths then
                        true
                    else
                        failwithf
                            "A value with %s was rejected, but not at '%s'.\nJSON: %s\nErrors were at: %s"
                            case.Description
                            case.ExpectedPath
                            case.Json
                            (String.concat ", " paths)
            )

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
    let isDeterministic (schema: Schema<'T>) : Property =
        Prop.forAll (Generate.arbitrary schema) (fun value -> Schema.toJson schema value = Schema.toJson schema value)

    /// <summary>
    /// Every property worth asserting about a schema on its own, as one check.
    /// </summary>
    /// <example><code lang="fsharp">
    /// testProperty "Person behaves" (fun () -> Properties.all Person.schema)
    /// </code></example>
    let all (schema: Schema<'T>) : Property =
        roundTrips schema .&. isDeterministic schema .&. invalidIsRejected schema
