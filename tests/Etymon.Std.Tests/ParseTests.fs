module Etymon.Std.Tests.ParseTests

open System
open System.Globalization
open Expecto
open Etymon

let private de = CultureInfo.GetCultureInfo "de-DE"
let private enUS = CultureInfo.GetCultureInfo "en-US"
let private frFR = CultureInfo.GetCultureInfo "fr-FR"

/// Every value in `good` must parse to its expected result; every one in `bad`
/// must return None.
let private table name (parse: string -> 'T option) (good: (string * 'T) list) (bad: string list) =
    testList
        name
        [
            for input, expected in good ->
                test $"parses %A{input}" { Expect.equal (parse input) (Some expected) $"%s{input} should parse" }
            for input in bad ->
                test $"rejects %A{input}" { Expect.equal (parse input) None $"%s{input} should not parse" }
        ]

let tests =
    testList
        "Parse"
        [
            testList
                "integers"
                [
                    table
                        "int"
                        Parse.int
                        [ "0", 0; "42", 42; "-7", -7; "2147483647", Int32.MaxValue ]
                        [ ""; " "; "4.2"; "abc"; "2147483648"; "1,000"; "0x10" ]

                    test "int does not accept thousands separators by default" {
                        // A wire value has no culture, so a comma is a mistake rather
                        // than a grouping character.
                        Expect.equal (Parse.int "1,000") None "invariant parsing is strict"
                    }

                    test "intIn accepts the culture's grouping" {
                        Expect.equal (Parse.intIn enUS "1,000") (Some 1000) "a comma groups in en-US"
                        Expect.equal (Parse.intIn de "1.000") (Some 1000) "a dot groups in de-DE"
                    }

                    table "int64" Parse.int64 [ "9007199254740993", 9007199254740993L ] [ ""; "abc"; "1.5" ]
                    table "byte" Parse.byte [ "0", 0uy; "255", 255uy ] [ "256"; "-1"; "" ]
                ]

            testList
                "reals"
                [
                    table
                        "decimal"
                        Parse.decimal
                        [ "1.5", 1.5m; "0", 0m; "-2.25", -2.25m ]
                        [ ""; "1,5"; "abc"; "1.5e3" ]

                    test "decimal keeps its scale" {
                        // 1.50m and 1.5m are equal but render differently; money cares.
                        Expect.equal (Parse.decimal "1.50" |> Option.map string) (Some "1.50") "trailing zero survives"
                    }

                    test "decimalIn reads a comma as the decimal point where that is the convention" {
                        Expect.equal (Parse.decimalIn de "1,50") (Some 1.50m) "de-DE uses a comma"

                        Expect.equal
                            (Parse.decimal "1,50")
                            None
                            "and the invariant parser refuses it, rather than reading 150"
                    }

                    table
                        "float"
                        Parse.float
                        [ "1.5", 1.5; "1.5e3", 1500.0; "-0.25", -0.25 ]
                        [ ""; "abc"; "NaN"; "Infinity"; "-Infinity" ]

                    test "float rejects the non-finite values" {
                        // A text field almost never meant NaN, and letting one through
                        // poisons every arithmetic result downstream.
                        Expect.equal (Parse.float "NaN") None "NaN is refused"
                        Expect.equal (Parse.float "Infinity") None "infinity is refused"
                    }

                    test "floatIn honours the culture" {
                        Expect.equal (Parse.floatIn frFR "1,5") (Some 1.5) "fr-FR uses a comma"
                    }
                ]

            testList
                "bool"
                [
                    table
                        "bool"
                        Parse.bool
                        [
                            "true", true
                            "True", true
                            "TRUE", true
                            "yes", true
                            "y", true
                            "on", true
                            "1", true
                            "  true  ", true
                            "false", false
                            "no", false
                            "n", false
                            "off", false
                            "0", false
                        ]
                        [ ""; " "; "maybe"; "2"; "-1"; "t" ]

                    test "null is rejected rather than throwing" {
                        Expect.equal (Parse.bool null) None "a null string is simply not a bool"
                    }
                ]

            testList
                "identifiers"
                [
                    test "guid accepts the formats Guid.TryParse does" {
                        Expect.isSome (Parse.guid "f81d4fae-7dec-11d0-a765-00a0c91e6bf6") "hyphenated"
                        Expect.isSome (Parse.guid "{f81d4fae-7dec-11d0-a765-00a0c91e6bf6}") "braced"
                        Expect.isSome (Parse.guid "f81d4fae7dec11d0a76500a0c91e6bf6") "hyphenless"
                        Expect.equal (Parse.guid "nope") None "and refuses nonsense"
                    }

                    test "guidExact accepts only the canonical form" {
                        Expect.isSome (Parse.guidExact "f81d4fae-7dec-11d0-a765-00a0c91e6bf6") "canonical"
                        Expect.equal (Parse.guidExact "{f81d4fae-7dec-11d0-a765-00a0c91e6bf6}") None "braced is refused"
                        Expect.equal (Parse.guidExact "f81d4fae7dec11d0a76500a0c91e6bf6") None "hyphenless is refused"
                    }
                ]

            testList
                "dates and times"
                [
                    table
                        "dateTimeOffset"
                        (Parse.dateTimeOffset >> Option.map (fun d -> d.ToUnixTimeSeconds()))
                        [
                            "2026-09-20T14:30:00Z",
                            DateTimeOffset(2026, 9, 20, 14, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds()
                        ]
                        [ ""; "2026-09-20"; "20/09/2026"; "2026-09-20 14:30:00Z" ]

                    test "dateTimeOffset keeps the offset it was given" {
                        match Parse.dateTimeOffset "2026-09-20T14:30:00+02:00" with
                        | Some parsed ->
                            Expect.equal parsed.Offset (TimeSpan.FromHours 2.0) "the offset is not discarded"
                        | None -> failtest "should have parsed"
                    }

                    test "dateTimeOffsetIn reads a culture's ordinary format" {
                        Expect.isSome
                            (Parse.dateTimeOffsetIn (CultureInfo.GetCultureInfo "en-GB") "20/09/2026")
                            "day-first is ordinary in en-GB"
                    }

                    test "dateTime marks a Z timestamp as UTC" {
                        match Parse.dateTime "2026-09-20T14:30:00Z" with
                        | Some parsed -> Expect.equal parsed.Kind DateTimeKind.Utc "Z means UTC, and that must survive"
                        | None -> failtest "should have parsed"
                    }

                    table
                        "dateOnly"
                        Parse.dateOnly
                        [ "2026-09-20", DateOnly(2026, 9, 20); "2000-02-29", DateOnly(2000, 2, 29) ]
                        [ ""; "2026-9-20"; "20/09/2026"; "2026-02-30"; "2026-13-01" ]

                    table
                        "timeOnly"
                        Parse.timeOnly
                        [
                            "14:30:00", TimeOnly(14, 30, 0)
                            "14:30", TimeOnly(14, 30)
                            "00:00:00", TimeOnly(0, 0)
                        ]
                        [ ""; "25:00:00"; "2:30 PM" ]

                    table
                        "timeSpan"
                        Parse.timeSpan
                        [ "01:02:03", TimeSpan(1, 2, 3); "1.02:03:04", TimeSpan(1, 2, 3, 4) ]
                        [ ""; "abc"; "1h" ]
                ]

            testList
                "enums"
                [
                    test "parses a member by name, ignoring case" {
                        Expect.equal (Parse.enum<DayOfWeek> "monday") (Some DayOfWeek.Monday) "case is ignored"
                        Expect.equal (Parse.enum<DayOfWeek> "Monday") (Some DayOfWeek.Monday) "exact case also works"
                    }

                    test "rejects a number with no corresponding member" {
                        // Enum.TryParse happily returns (DayOfWeek)99. That is how an
                        // undefined value gets into a domain and is never noticed.
                        Expect.equal (Parse.enum<DayOfWeek> "99") None "99 is not a day of the week"
                    }

                    test "accepts a number that does correspond to a member" {
                        Expect.equal (Parse.enum<DayOfWeek> "1") (Some DayOfWeek.Monday) "1 is Monday"
                    }

                    test "rejects a name that is not a member" {
                        Expect.equal (Parse.enum<DayOfWeek> "Someday") None "not a member"
                    }
                ]

            testList
                "properties"
                [
                    testProperty "int round-trips through its own rendering"
                    <| fun (value: int) -> Parse.int (string value) = Some value

                    testProperty "int64 round-trips through its own rendering"
                    <| fun (value: int64) -> Parse.int64 (string value) = Some value

                    testProperty "decimal round-trips through invariant rendering"
                    <| fun (value: decimal) -> Parse.decimal (value.ToString CultureInfo.InvariantCulture) = Some value

                    testProperty "a finite float round-trips through round-trip formatting"
                    <| fun (value: float) ->
                        if Double.IsFinite value then
                            Parse.float (value.ToString("R", CultureInfo.InvariantCulture)) = Some value
                        else
                            Parse.float (value.ToString("R", CultureInfo.InvariantCulture)) = None

                    testProperty "a guid round-trips"
                    <| fun () ->
                        let value = Guid.NewGuid()
                        Parse.guid (string value) = Some value

                    testProperty "parsing never throws"
                    <| fun (text: string) ->
                        // Whatever FsCheck produces, including null and control
                        // characters, a parser returns rather than raises.
                        Parse.int text |> ignore
                        Parse.decimal text |> ignore
                        Parse.float text |> ignore
                        Parse.bool text |> ignore
                        Parse.guid text |> ignore
                        Parse.dateOnly text |> ignore
                        Parse.timeSpan text |> ignore
                        true
                ]
        ]
