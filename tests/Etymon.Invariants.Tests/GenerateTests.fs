module Etymon.Invariants.Tests.GenerateTests

open Expecto
open FsCheck
open FsCheck.FSharp
open Etymon
open Etymon.Invariants.Tests.Domain

let private sample count generator =
    Gen.sample count generator |> List.ofArray

let tests =
    testList
        "Generate"
        [
            testList
                "valid values"
                [
                    test "generated values decode, which is how they are known to be valid" {
                        // If the generator and the decoder disagreed, Generate.valid
                        // would raise rather than hand back a value -- so simply
                        // sampling is the assertion.
                        let contacts = sample 50 (Generate.valid Contact.schema)
                        Expect.equal (List.length contacts) 50 "fifty values, none rejected"
                    }

                    test "constraints are respected, not merely filtered for" {
                        let contacts = sample 50 (Generate.valid Contact.schema)

                        Expect.all
                            contacts
                            (fun c -> c.Email.Contains "@" && c.Email.Contains ".")
                            "the email format is constructed, not stumbled upon"

                        Expect.all contacts (fun c -> List.length c.Tags <= 3) "the item bound holds"
                    }

                    test "a length constraint is satisfied exactly" {
                        let schema = Schema.string |> Schema.constrain (Check.length (Some 6) (Some 6))

                        Expect.all (sample 30 (Generate.valid schema)) (fun s -> s.Length = 6) "every value"
                    }

                    test "a numeric range is satisfied" {
                        let schema = Schema.int |> Schema.constrain (Check.intRange (Some 10) (Some 20))
                        Expect.all (sample 40 (Generate.valid schema)) (fun n -> n >= 10 && n <= 20) "every value"
                    }

                    test "an exclusive bound is respected" {
                        let schema =
                            Schema.int
                            |> Schema.constrain (
                                Check.make (Constraint.Range(Some(Num.Int 0L), None, true, false)) (fun n -> n > 0)
                            )

                        Expect.all (sample 40 (Generate.valid schema)) (fun n -> n > 0) "zero is excluded"
                    }

                    test "an enumeration only yields its members" {
                        let schema =
                            Schema.string |> Schema.constrain (Check.oneOf [ "red"; "green"; "blue" ])

                        Expect.all
                            (sample 30 (Generate.valid schema))
                            (fun s -> List.contains s [ "red"; "green"; "blue" ])
                            "every value is a member"
                    }

                    test "optional fields are sometimes present and sometimes not" {
                        let contacts = sample 60 (Generate.valid Contact.schema)
                        let present = contacts |> List.filter (fun c -> c.Nickname.IsSome) |> List.length

                        Expect.isGreaterThan present 0 "some are present"
                        Expect.isLessThan present 60 "and some are not, or the optionality is never exercised"
                    }

                    test "a union yields every case" {
                        let schema =
                            Schema.union
                                "Choice"
                                "kind"
                                [
                                    Schema.case
                                        "a"
                                        Schema.int
                                        Choice1Of2
                                        (function
                                        | Choice1Of2 v -> ValueSome v
                                        | _ -> ValueNone
                                        )
                                    Schema.case
                                        "b"
                                        Schema.string
                                        Choice2Of2
                                        (function
                                        | Choice2Of2 v -> ValueSome v
                                        | _ -> ValueNone
                                        )
                                ]

                        let values = sample 40 (Generate.valid schema)

                        Expect.isTrue
                            (values
                             |> List.exists (
                                 function
                                 | Choice1Of2 _ -> true
                                 | _ -> false
                             ))
                            "case a"

                        Expect.isTrue
                            (values
                             |> List.exists (
                                 function
                                 | Choice2Of2 _ -> true
                                 | _ -> false
                             ))
                            "case b"
                    }
                ]

            testList
                "failing loudly"
                [
                    test "an impossible range raises rather than yielding nothing" {
                        // A generator that quietly produced fewer cases would let a
                        // property test pass having checked almost nothing.
                        let schema =
                            Schema.int
                            |> Schema.constrain (
                                Check.make
                                    (Constraint.Range(Some(Num.Int 10L), Some(Num.Int 5L), false, false))
                                    (fun _ -> false)
                            )

                        Expect.throws
                            (fun () -> sample 1 (Generate.valid schema) |> ignore)
                            "an unsatisfiable constraint is a mistake, not a discard"
                    }

                    test "a recursive schema says why it cannot be generated" {
                        Expect.throws
                            (fun () -> sample 1 (Generate.valid Tree.schema) |> ignore)
                            "a generator for a recursive type would not terminate"
                    }

                    test "an unsatisfiable set of invariants raises" {
                        let impossible =
                            Invariants.forType "Contact" [ Invariant.create "never" "can never hold" (fun _ -> false) ]

                        Expect.throws
                            (fun () -> sample 1 (Generate.validSatisfying impossible Contact.schema) |> ignore)
                            "better a loud failure than a thin sample"
                    }
                ]

            testList
                "values that should be rejected"
                [
                    test "a required field is removed, and the error is expected at that field" {
                        let cases = sample 40 (Generate.invalid Contact.schema)

                        let removals =
                            cases |> List.filter (fun c -> c.ExpectedReason = ErrorReason.Missing)

                        Expect.isGreaterThan (List.length removals) 0 "some cases remove a required field"

                        Expect.all removals (fun c -> c.ExpectedPath <> "") "and each says where the error should land"
                    }

                    test "every generated invalid case really is rejected" {
                        for case in sample 40 (Generate.invalid Contact.schema) do
                            let result = Schema.fromJson Contact.schema case.Json

                            Expect.isTrue
                                (Validation.isError result)
                                $"%s{case.Description} should have been rejected: %s{case.Json}"
                    }

                    test "and rejected at the expected path" {
                        for case in sample 40 (Generate.invalid Contact.schema) do
                            let paths =
                                Schema.fromJson Contact.schema case.Json
                                |> Validation.errorList
                                |> List.map (fun e -> Path.toString e.Path)

                            Expect.contains paths case.ExpectedPath $"%s{case.Description}"
                    }
                ]

            testList
                "invariant-aware generation"
                [
                    test "generated bookings satisfy their cross-field rules" {
                        let bookings =
                            sample 20 (Generate.validSatisfying Booking.invariants Booking.schema)

                        Expect.all
                            bookings
                            (fun b -> Validation.isOk (Invariants.check Booking.invariants b))
                            "every one is a legal booking, not merely a decodable one"
                    }

                    test "without the invariants, some generated values are illegal" {
                        // Worth asserting: it is what makes validSatisfying necessary
                        // rather than decorative.
                        let bookings = sample 60 (Generate.valid Booking.schema)

                        let illegal =
                            bookings
                            |> List.filter (fun b -> Validation.isError (Invariants.check Booking.invariants b))

                        Expect.isGreaterThan
                            (List.length illegal)
                            0
                            "the schema alone is looser than the type, which is the gap invariants close"
                    }
                ]

            testList
                "ready-made properties"
                [
                    testProperty "round-trips" (fun () -> Properties.roundTrips Contact.schema)
                    testProperty "encoding is deterministic" (fun () -> Properties.isDeterministic Contact.schema)
                    testProperty
                        "invalid input is rejected where it is invalid"
                        (fun () -> Properties.invalidIsRejected Contact.schema)
                    testProperty "everything at once" (fun () -> Properties.all Contact.schema)

                    testProperty
                        "generated bookings satisfy their invariants"
                        (fun () -> Properties.generatedValuesSatisfy Booking.invariants Booking.schema)
                ]
        ]
