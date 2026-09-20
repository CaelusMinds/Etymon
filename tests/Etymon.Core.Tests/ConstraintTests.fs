module Etymon.Core.Tests.ConstraintTests

open Expecto
open Etymon

let private describes (c: Constraint) (expected: string) =
    test $"describes %A{c}" { Expect.equal (Constraint.describe c) expected "the wording is part of the public API" }

let tests =
    testList
        "Constraint"
        [
            testList
                "describe"
                [
                    describes (Constraint.Length(Some 3, Some 254)) "must be between 3 and 254 characters"
                    describes (Constraint.Length(Some 5, Some 5)) "must be exactly 5 characters"
                    describes (Constraint.Length(Some 1, Some 1)) "must be exactly 1 character"
                    describes (Constraint.Length(Some 1, None)) "must be at least 1 character"
                    describes (Constraint.Length(Some 3, None)) "must be at least 3 characters"
                    describes (Constraint.Length(None, Some 1)) "must be at most 1 character"
                    describes (Constraint.Length(None, None)) "must be a string"

                    describes
                        (Constraint.Range(Some(Num.Int 0L), Some(Num.Int 130L), false, false))
                        "must be between 0 and 130"
                    describes (Constraint.Range(Some(Num.Int 0L), None, true, false)) "must be greater than 0"
                    describes (Constraint.Range(Some(Num.Int 0L), None, false, false)) "must be at least 0"
                    describes (Constraint.Range(None, Some(Num.Int 10L), false, true)) "must be less than 10"
                    describes (Constraint.Range(None, Some(Num.Int 10L), false, false)) "must be at most 10"
                    describes (Constraint.Range(None, None, false, false)) "must be a number"

                    describes (Constraint.Pattern @"^\d{5}$") @"must match ^\d{5}$"
                    describes (Constraint.HasFormat Format.Email) "must be a valid email"
                    describes (Constraint.HasFormat Format.DateTime) "must be a valid date-time"
                    describes (Constraint.HasFormat(Format.Custom "iban")) "must be a valid iban"
                    describes (Constraint.OneOf [ "red"; "green"; "blue" ]) "must be one of: red, green, blue"

                    describes (Constraint.Items(Some 1, Some 10, false)) "must contain between 1 and 10 items"
                    describes (Constraint.Items(Some 2, Some 2, false)) "must contain exactly 2 items"
                    describes (Constraint.Items(Some 1, Some 1, true)) "must contain exactly 1 item with no duplicates"
                    describes (Constraint.Items(Some 1, None, false)) "must contain at least 1 item"
                    describes (Constraint.Items(None, Some 5, false)) "must contain at most 5 items"
                    describes (Constraint.Items(None, None, true)) "must be a collection with no duplicates"
                    describes (Constraint.Items(None, None, false)) "must be a collection"

                    describes (Constraint.Opaque("even", "must be even")) "must be even"

                    test "a bounded range shows both words when exclusive" {
                        Expect.equal
                            (Constraint.describe (Constraint.Range(Some(Num.Int 0L), Some(Num.Int 1L), true, true)))
                            "must be greater than 0 and less than 1"
                            "both ends state their strictness"
                    }

                    testProperty "describe always says something"
                    <| fun (min: int option) (max: int option) ->
                        Constraint.describe (Constraint.Length(min, max)) <> ""
                ]

            testList
                "Num"
                [
                    test "integers order as integers" {
                        Expect.equal (Num.compare (Num.Int 1L) (Num.Int 2L)) (ValueSome -1) "1 is below 2"
                        Expect.equal (Num.compare (Num.Int 2L) (Num.Int 2L)) (ValueSome 0) "2 equals 2"
                    }

                    test "mixed representations promote" {
                        Expect.equal (Num.compare (Num.Int 1L) (Num.Dec 1.5m)) (ValueSome -1) "1 is below 1.5"
                        Expect.equal (Num.compare (Num.Dec 1m) (Num.Float 1.0)) (ValueSome 0) "1m equals 1.0"
                        Expect.equal (Num.compare (Num.Float 2.5) (Num.Int 2L)) (ValueSome 1) "2.5 is above 2"
                    }

                    test "NaN is unordered" {
                        Expect.equal (Num.compare (Num.Float nan) (Num.Int 0L)) ValueNone "NaN compares to nothing"
                        Expect.equal (Num.compare (Num.Int 0L) (Num.Float nan)) ValueNone "in either direction"
                        Expect.equal (Num.compare (Num.Float nan) (Num.Float nan)) ValueNone "not even to itself"
                    }

                    test "values too large for decimal fall back to float" {
                        Expect.equal (Num.compare (Num.Float 1e30) (Num.Int 0L)) (ValueSome 1) "still ordered correctly"

                        Expect.equal
                            (Num.compare (Num.Float infinity) (Num.Float 1e30))
                            (ValueSome 1)
                            "infinity is largest"
                    }

                    test "rendering is culture-invariant" {
                        Expect.equal (Num.toString (Num.Int 42L)) "42" "integers render plainly"
                        Expect.equal (Num.toString (Num.Dec 1.50m)) "1.50" "decimals keep their scale"
                        Expect.equal (Num.toString (Num.Float 0.5)) "0.5" "floats use a dot, never a comma"
                    }
                ]

            testList
                "Format"
                [
                    test "names match the JSON Schema vocabulary" {
                        Expect.equal (Format.name Format.Email) "email" "email"
                        Expect.equal (Format.name Format.Uuid) "uuid" "uuid"
                        Expect.equal (Format.name Format.DateTime) "date-time" "hyphenated, as the spec has it"
                        Expect.equal (Format.name Format.UriReference) "uri-reference" "hyphenated too"
                        Expect.equal (Format.name (Format.Custom "iban")) "iban" "a custom format uses its own name"
                    }
                ]
        ]
