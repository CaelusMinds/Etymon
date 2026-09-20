module Etymon.Core.Tests.CheckTests

open Expecto
open Etymon

let private accepts (check: ConstraintCheck<'T>) (value: 'T) = check.Satisfies value

/// Builds a table test: every value in `good` must pass and every one in `bad` must fail.
let private table name (check: ConstraintCheck<'T>) (good: 'T list) (bad: 'T list) =
    testList
        name
        [
            for value in good ->
                test $"accepts %A{value}" { Expect.isTrue (accepts check value) $"%A{value} should be accepted" }
            for value in bad ->
                test $"rejects %A{value}" { Expect.isFalse (accepts check value) $"%A{value} should be rejected" }
        ]

let tests =
    testList
        "Check"
        [
            testList
                "length"
                [
                    table
                        "length 3..5"
                        (Check.length (Some 3) (Some 5))
                        [ "abc"; "abcd"; "abcde" ]
                        [ "ab"; "abcdef"; "" ]
                    table "minLength 1" (Check.minLength 1) [ "a"; "ab" ] [ "" ]
                    table "maxLength 2" (Check.maxLength 2) [ ""; "a"; "ab" ] [ "abc" ]
                    table "exactLength 5" (Check.exactLength 5) [ "12345" ] [ "1234"; "123456" ]
                    table "nonEmpty" Check.nonEmpty [ "a"; " " ] [ "" ]

                    test "an unbounded length accepts anything" {
                        Expect.isTrue (accepts (Check.length None None) "") "no bounds, no rejection"
                    }

                    test "the constraint is carried as data" {
                        Expect.equal
                            (Check.length (Some 3) (Some 5)).Constraint
                            (Constraint.Length(Some 3, Some 5))
                            "downstream packages read this, not the predicate"
                    }
                ]

            testList
                "pattern"
                [
                    table "a five-digit zip" (Check.pattern @"^\d{5}$") [ "12345" ] [ "1234"; "123456"; "abcde"; "" ]

                    test "patterns are not anchored for you" {
                        // Deliberate: the pattern carried into a generated schema is exactly
                        // the one that was written.
                        Expect.isTrue (accepts (Check.pattern @"\d") "a1b") "an unanchored pattern matches anywhere"
                    }

                    test "the regex is carried verbatim" {
                        Expect.equal
                            (Check.pattern @"^\d{5}$").Constraint
                            (Constraint.Pattern @"^\d{5}$")
                            "the exact pattern reaches the schema"
                    }
                ]

            testList
                "formats"
                [
                    table
                        "email"
                        (Check.format Format.Email)
                        [
                            "someone@example.com"
                            "first.last@sub.example.co.uk"
                            "a+tag@example.com"
                            "x@e.io"
                        ]
                        [
                            ""
                            "nope"
                            "@example.com"
                            "someone@"
                            "someone@localhost"
                            "a b@example.com"
                            ".leading@example.com"
                            "trailing.@example.com"
                            "double..dot@example.com"
                        ]

                    table
                        "uuid"
                        (Check.format Format.Uuid)
                        [ "f81d4fae-7dec-11d0-a765-00a0c91e6bf6" ]
                        [
                            ""
                            "not-a-uuid"
                            "f81d4fae7dec11d0a76500a0c91e6bf6"
                            "{f81d4fae-7dec-11d0-a765-00a0c91e6bf6}"
                        ]

                    table
                        "date-time"
                        (Check.format Format.DateTime)
                        [
                            "2026-09-20T14:30:00Z"
                            "2026-09-20T14:30:00+01:00"
                            "2026-09-20T14:30:00.123Z"
                        ]
                        [ ""; "2026-09-20"; "2026-09-20 14:30:00Z"; "20/09/2026" ]

                    table
                        "date"
                        (Check.format Format.Date)
                        [ "2026-09-20"; "2000-02-29" ]
                        [ ""; "2026-9-20"; "20/09/2026"; "2026-02-30" ]

                    table
                        "time"
                        (Check.format Format.Time)
                        [ "14:30:00"; "00:00:00"; "14:30:00.5" ]
                        [ ""; "14:30"; "25:00:00" ]

                    table
                        "duration"
                        (Check.format Format.Duration)
                        [ "P3D"; "PT4H"; "P3DT4H"; "P1Y2M3DT4H5M6S"; "PT0.5S"; "P2W"; "-P1D" ]
                        [ ""; "P"; "3D"; "PT"; "P3X"; "PT1H1Y"; "P1D2D"; "PT0.5H"; "PTS" ]

                    // "/relative" and "C:\\x" are the platform-dependent ones:
                    // Uri.TryCreate(UriKind.Absolute) accepts a rooted path as an
                    // implicit file: URI on whichever platform owns that shape.
                    // Both must be rejected everywhere, or the same payload
                    // validates on one machine and not another.
                    table
                        "uri"
                        (Check.format Format.Uri)
                        [
                            "https://example.com"
                            "https://example.com/a?b=c"
                            "file:///tmp/x"
                            "urn:isbn:0451450523"
                            "mailto:ada@example.com"
                        ]
                        [
                            ""
                            "/relative"
                            "not a uri"
                            "C:\\Windows"
                            "//example.com/a"
                            "://missing-scheme"
                        ]

                    table
                        "hostname"
                        (Check.format Format.Hostname)
                        [ "example.com"; "sub.example.co.uk"; "localhost"; "a-b.example" ]
                        [ ""; "-leading.example"; "trailing-.example"; "has space" ]

                    table
                        "ipv4"
                        (Check.format Format.Ipv4)
                        [ "127.0.0.1"; "0.0.0.0"; "255.255.255.255" ]
                        [ ""; "1.2.3"; "256.0.0.1"; "::1" ]

                    table "ipv6" (Check.format Format.Ipv6) [ "::1"; "2001:db8::1" ] [ ""; "127.0.0.1"; "nonsense" ]

                    test "an unknown format is documentation, not enforcement" {
                        let check = Check.format (Format.Custom "iban")
                        Expect.isTrue (accepts check "anything at all") "Etymon cannot check it, so it does not"

                        Expect.equal
                            check.Constraint
                            (Constraint.HasFormat(Format.Custom "iban"))
                            "but it still reaches generated documentation"
                    }
                ]

            testList
                "ranges"
                [
                    table "intRange 0..130" (Check.intRange (Some 0) (Some 130)) [ 0; 65; 130 ] [ -1; 131 ]
                    table "intRange with no ceiling" (Check.intRange (Some 0) None) [ 0; 1_000_000 ] [ -1 ]
                    table "int64Range" (Check.int64Range (Some 0L) (Some 10L)) [ 0L; 10L ] [ -1L; 11L ]
                    table "decimalRange" (Check.decimalRange (Some 0m) (Some 1m)) [ 0m; 0.5m; 1m ] [ -0.1m; 1.1m ]
                    table "greaterThan 0" (Check.greaterThan 0m) [ 0.01m; 5m ] [ 0m; -1m ]
                    table "lessThan 1" (Check.lessThan 1m) [ 0.99m; -5m ] [ 1m; 2m ]
                    table "floatRange 0..1" (Check.floatRange (Some 0.0) (Some 1.0)) [ 0.0; 0.5; 1.0 ] [ -0.1; 1.1 ]

                    test "NaN satisfies no bound" {
                        Expect.isFalse
                            (accepts (Check.floatRange (Some 0.0) (Some 1.0)) nan)
                            "NaN is unordered, so it cannot be in range"
                    }

                    test "infinity is ordered normally" {
                        Expect.isFalse
                            (accepts (Check.floatRange None (Some 1.0)) infinity)
                            "infinity exceeds any ceiling"

                        Expect.isTrue
                            (accepts (Check.floatRange None (Some 1.0)) (-infinity))
                            "and is below any ceiling"
                    }

                    test "exclusivity reaches the constraint data" {
                        Expect.equal
                            (Check.greaterThan 0m).Constraint
                            (Constraint.Range(Some(Num.Dec 0m), None, true, false))
                            "a schema needs to know the bound is exclusive"
                    }
                ]

            testList
                "collections"
                [
                    test "itemCount bounds a list" {
                        let check = Check.itemCount (Some 1) (Some 3)
                        Expect.isFalse (accepts check ([]: int list)) "empty is below the floor"
                        Expect.isTrue (accepts check [ 1 ]) "one is in range"
                        Expect.isTrue (accepts check [ 1; 2; 3 ]) "three is in range"
                        Expect.isFalse (accepts check [ 1; 2; 3; 4 ]) "four is above the ceiling"
                    }

                    test "distinct rejects duplicates" {
                        Expect.isTrue (accepts Check.distinct [ 1; 2; 3 ]) "all different"
                        Expect.isFalse (accepts Check.distinct [ 1; 2; 1 ]) "a repeat is a duplicate"
                        Expect.isTrue (accepts Check.distinct ([]: int list)) "nothing can repeat"
                    }
                ]

            testList
                "oneOf"
                [
                    table "oneOf" (Check.oneOf [ "red"; "green" ]) [ "red"; "green" ] [ ""; "blue"; "Red" ]

                    test "the permitted values reach the constraint data" {
                        Expect.equal
                            (Check.oneOf [ "red"; "green" ]).Constraint
                            (Constraint.OneOf [ "red"; "green" ])
                            "a schema renders these as an enum"
                    }
                ]

            testList
                "opaque"
                [
                    test "an opaque rule is enforced" {
                        let check = Check.opaque "even" "must be even" (fun n -> n % 2 = 0)
                        Expect.isTrue (accepts check 4) "4 is even"
                        Expect.isFalse (accepts check 5) "5 is not"
                    }

                    test "an opaque rule describes itself" {
                        let check = Check.opaque "even" "must be even" (fun n -> n % 2 = 0)

                        Expect.equal
                            check.Constraint
                            (Constraint.Opaque("even", "must be even"))
                            "the code and text survive"

                        Expect.equal
                            (Constraint.describe check.Constraint)
                            "must be even"
                            "describe uses the given text"
                    }
                ]

            testList
                "running a check"
                [
                    test "apply returns the value unchanged when it passes" {
                        Expect.equal
                            (Check.apply (Check.minLength 1) Path.root "a")
                            (Ok "a")
                            "a passing check is identity"
                    }

                    test "apply reports the constraint and its description" {
                        let v = Check.apply (Check.minLength 3) (Path.root |> Path.field "name") "ab"

                        match Validation.errorList v with
                        | [ e ] ->
                            Expect.equal (Path.toString e.Path) "name" "the error knows where it was"
                            Expect.equal e.Message "must be at least 3 characters" "the message comes from describe"

                            Expect.equal
                                e.Reason
                                (ErrorReason.ConstraintFailed(Constraint.Length(Some 3, None)))
                                "and the machine-readable reason carries the constraint itself"
                        | other -> failtestf "expected exactly one error, got %A" other
                    }

                    test "toError builds the same error apply would" {
                        let check = Check.minLength 3
                        let viaToError = Check.toError check Path.root

                        match Validation.errorList (Check.apply check Path.root "") with
                        | [ viaApply ] -> Expect.equal viaApply viaToError "the two routes must agree"
                        | other -> failtestf "expected exactly one error, got %A" other
                    }
                ]
        ]
