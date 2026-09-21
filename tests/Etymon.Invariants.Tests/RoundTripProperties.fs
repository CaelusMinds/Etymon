/// Properties every schema must satisfy, checked against values Etymon itself
/// generated.
///
/// The example-based tests elsewhere check the cases somebody thought of. These
/// check the cases nobody thought of: `Generate.valid` produces values a schema
/// accepts, and each property below must then hold for all of them. When one
/// fails, FsCheck shrinks to the smallest input that still breaks it, which is
/// usually the bug stated more clearly than a person would have stated it.
///
/// Using the suite's own generators against the suite's own codecs is not
/// circular so long as the properties are independent of how either is written:
/// "encoding then decoding gives back what you started with" is a claim about
/// the pair, and a generator cannot make it true by construction.
module Etymon.Invariants.Tests.RoundTripProperties

open Expecto
open FsCheck
open FsCheck.FSharp
open Etymon
open Etymon.Invariants.Tests.Domain

/// Enough cases to be worth running, few enough to keep the suite quick.
let private config =
    { FsCheckConfig.defaultConfig with
        maxTest = 200
    }

// ---------------------------------------------------------------------------
// The properties, written once and applied to every schema.
// ---------------------------------------------------------------------------

/// Encode, decode, and get back what you started with.
let private roundTrips (schema: Schema<'T>) (value: 'T) =
    match Schema.fromJson schema (Schema.toJson schema value) with
    | Ok decoded -> decoded = value
    | Error errors -> failwithf "a generated value did not decode: %s" (ValidationErrors.format errors)

/// Encoding is a function of the value alone: same value, same bytes, every
/// time. Anything else makes a golden test flaky and a cache key wrong.
let private encodesDeterministically (schema: Schema<'T>) (value: 'T) =
    Schema.toJson schema value = Schema.toJson schema value

/// The string and the bytes are two views of one encoding, not two encoders.
let private textAndBytesAgree (schema: Schema<'T>) (value: 'T) =
    let fromBytes = System.Text.Encoding.UTF8.GetString(Schema.toJsonUtf8 schema value)

    Schema.toJson schema value = fromBytes

/// Indenting changes the whitespace and nothing else.
let private indentingPreservesTheValue (schema: Schema<'T>) (value: 'T) =
    match Schema.fromJson schema (Schema.toJsonIndented schema value) with
    | Ok decoded -> decoded = value
    | Error _ -> false

/// Decoding twice from the same text gives the same value. Guards against a
/// decoder that consumed something it should not have.
let private decodingIsRepeatable (schema: Schema<'T>) (value: 'T) =
    let json = Schema.toJson schema value
    Schema.fromJson schema json = Schema.fromJson schema json

/// Every property, for one schema, as a list of tests.
let private propertiesOf (name: string) (schema: Schema<'T>) (gen: Gen<'T>) =
    let arb = Arb.fromGen gen

    [
        testPropertyWithConfig
            config
            $"%s{name}: encoding then decoding is the identity"
            (fun () -> Prop.forAll arb (roundTrips schema))

        testPropertyWithConfig
            config
            $"%s{name}: encoding is deterministic"
            (fun () -> Prop.forAll arb (encodesDeterministically schema))

        testPropertyWithConfig
            config
            $"%s{name}: text and bytes are the same encoding"
            (fun () -> Prop.forAll arb (textAndBytesAgree schema))

        testPropertyWithConfig
            config
            $"%s{name}: indenting does not change the value"
            (fun () -> Prop.forAll arb (indentingPreservesTheValue schema))

        testPropertyWithConfig
            config
            $"%s{name}: decoding is repeatable"
            (fun () -> Prop.forAll arb (decodingIsRepeatable schema))
    ]

/// A generator for a self-referential type, bounded the way FsCheck bounds
/// anything recursive: the size shrinks on the way down, and at zero the node
/// has no children.
module private Trees =

    let rec private ofSize size =
        gen {
            let! value = Gen.choose (-1000, 1000)

            let! children =
                if size <= 0 then
                    Gen.constant []
                else
                    // Divided rather than decremented, so breadth and depth
                    // cannot multiply into something enormous.
                    Gen.listOfLength (min size 3) (ofSize (size / 3))

            return { Value = value; Children = children }
        }

    let generator = Gen.sized ofSize

// ---------------------------------------------------------------------------
// The shapes. One of each kind the combinators can build.
// ---------------------------------------------------------------------------

let tests =
    testList
        "round trips"
        [
            yield! propertiesOf "a record with constrained fields" Contact.schema (Generate.valid Contact.schema)

            yield!
                propertiesOf "a record with dates and an optional field" Booking.schema (Generate.valid Booking.schema)

            yield! propertiesOf "a primitive" Schema.int (Generate.valid Schema.int)

            yield!
                propertiesOf
                    "a constrained string"
                    (Schema.string |> Schema.constrain (Check.length (Some 1) (Some 20)))
                    (Generate.valid (Schema.string |> Schema.constrain (Check.length (Some 1) (Some 20))))

            yield! propertiesOf "a list" (Schema.list Schema.int) (Generate.valid (Schema.list Schema.int))

            yield!
                propertiesOf
                    "a list of records"
                    (Schema.list Contact.schema)
                    (Generate.valid (Schema.list Contact.schema))

            yield! propertiesOf "a map" (Schema.map Schema.int) (Generate.valid (Schema.map Schema.int))

            yield!
                propertiesOf
                    "a nullable"
                    (Schema.nullable Schema.string)
                    (Generate.valid (Schema.nullable Schema.string))

            yield!
                propertiesOf
                    "a nullable inside a list"
                    (Schema.list (Schema.nullable Schema.string))
                    (Generate.valid (Schema.list (Schema.nullable Schema.string)))

            // Generate.valid refuses a recursive schema, because generating one
            // would not terminate and Etymon carries no depth budget. Its error
            // says to write the generator by hand; this is that, and it doubles
            // as a check that the advice works.
            yield! propertiesOf "a recursive type, generated by hand" Tree.schema Trees.generator

            testList
                "what the generators themselves promise"
                [
                    testPropertyWithConfig
                        config
                        "a valid value is accepted by the schema that made it"
                        (fun () ->
                            Prop.forAll
                                (Arb.fromGen (Generate.valid Contact.schema))
                                (fun contact ->
                                    // Generate.valid raises rather than thinning its
                                    // sample, so this is the belt to that braces: the
                                    // value is re-read through the schema as JSON would
                                    // arrive.
                                    Schema.fromJson Contact.schema (Schema.toJson Contact.schema contact)
                                    |> Validation.isOk
                                )
                        )

                    testPropertyWithConfig
                        config
                        "an invalid case really is rejected"
                        (fun () ->
                            Prop.forAll
                                (Arb.fromGen (Generate.invalid Contact.schema))
                                (fun case ->
                                    // The other half: a generator of counter-examples is
                                    // worthless if the counter-examples pass.
                                    Schema.fromJson Contact.schema case.Json |> Validation.isError
                                )
                        )
                ]
        ]
