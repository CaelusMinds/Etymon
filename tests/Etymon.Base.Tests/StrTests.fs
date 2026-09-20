module Etymon.Base.Tests.StrTests

open Expecto
open Etymon

let tests =
    testList
        "Str"
        [
            testList
                "absence"
                [
                    test "toOption treats blank as absent" {
                        Expect.equal (Str.toOption null) None "null is absent"
                        Expect.equal (Str.toOption "") None "empty is absent"
                        Expect.equal (Str.toOption "  \t ") None "whitespace is absent"
                        Expect.equal (Str.toOption " hi ") (Some " hi ") "content is kept exactly as it was"
                    }

                    test "toOptionAllowEmpty keeps empty as a value" {
                        Expect.equal (Str.toOptionAllowEmpty null) None "null is still absent"
                        Expect.equal (Str.toOptionAllowEmpty "") (Some "") "empty is a value here"
                        Expect.equal (Str.toOptionAllowEmpty " ") (Some " ") "so is whitespace"
                    }

                    test "orEmpty replaces null" {
                        Expect.equal (Str.orEmpty null) "" "null becomes empty"
                        Expect.equal (Str.orEmpty "hi") "hi" "anything else is untouched"
                    }

                    test "isEmpty and isBlank differ on whitespace" {
                        Expect.isTrue (Str.isEmpty "") "empty is empty"
                        Expect.isFalse (Str.isEmpty " ") "a space is not empty"
                        Expect.isTrue (Str.isBlank " ") "but it is blank"
                        Expect.isTrue (Str.isBlank null) "and null is blank"
                    }
                ]

            testList
                "normalisation"
                [
                    test "trim handles null" {
                        Expect.equal (Str.trim "  hi  ") "hi" "surrounding whitespace goes"
                        Expect.equal (Str.trim null) "" "null does not throw"
                    }

                    test "case folding is invariant" {
                        Expect.equal (Str.toLower "ABC") "abc" "down"
                        Expect.equal (Str.toUpper "abc") "ABC" "up"
                        Expect.equal (Str.toLower null) "" "null does not throw"
                    }
                ]

            testList
                "comparison"
                [
                    test "equalsIgnoreCase is ordinal" {
                        Expect.isTrue (Str.equalsIgnoreCase "Content-Type" "content-type") "case is ignored"
                        Expect.isTrue (Str.equalsIgnoreCase null null) "two nulls are equal"
                        Expect.isFalse (Str.equalsIgnoreCase null "") "null is not empty"
                        Expect.isFalse (Str.equalsIgnoreCase "a" "b") "different is different"
                    }

                    test "startsWith, endsWith and contains tolerate null" {
                        Expect.isTrue (Str.startsWith "Etymon." "Etymon.Core") "prefix"
                        Expect.isFalse (Str.startsWith "Etymon." null) "null has no prefix"
                        Expect.isTrue (Str.endsWith ".fs" "Parse.fs") "suffix"
                        Expect.isFalse (Str.endsWith ".fs" null) "null has no suffix"
                        Expect.isTrue (Str.contains "@" "a@b.com") "contains"
                        Expect.isFalse (Str.contains "@" null) "null contains nothing"
                    }

                    test "comparison is case sensitive unless the name says otherwise" {
                        Expect.isFalse (Str.startsWith "etymon." "Etymon.Core") "startsWith is ordinal"
                        Expect.isFalse (Str.contains "A" "abc") "so is contains"
                    }
                ]

            testList
                "splitting"
                [
                    test "split trims and drops empties" {
                        Expect.equal (Str.split ',' "a, b ,, c") [ "a"; "b"; "c" ] "the shape a config value wants"
                        Expect.equal (Str.split ',' "") [] "empty splits to nothing"
                        Expect.equal (Str.split ',' null) [] "null splits to nothing"
                    }

                    test "splitRaw keeps every part" {
                        Expect.equal (Str.splitRaw ',' "a,,b") [ "a"; ""; "b" ] "empties are preserved"
                        Expect.equal (Str.splitRaw ',' " a ") [ " a " ] "whitespace is preserved"
                    }

                    test "splitFirst splits once at the first separator" {
                        Expect.equal
                            (Str.splitFirst '=' "KEY=a=b")
                            (Some("KEY", "a=b"))
                            "only the first separator counts"

                        Expect.equal
                            (Str.splitFirst '=' "KEY=")
                            (Some("KEY", ""))
                            "a trailing separator gives an empty tail"

                        Expect.equal
                            (Str.splitFirst '=' "=VALUE")
                            (Some("", "VALUE"))
                            "a leading one gives an empty head"

                        Expect.equal (Str.splitFirst '=' "KEY") None "no separator, no split"
                    }

                    test "join" {
                        Expect.equal (Str.join ", " [ "a"; "b" ]) "a, b" "parts are joined"
                        Expect.equal (Str.join ", " []) "" "nothing joins to nothing"
                    }
                ]

            testList
                "truncate"
                [
                    test "leaves a short string alone" {
                        Expect.equal (Str.truncate 8 "short") "short" "nothing to cut"
                    }

                    test "never exceeds the requested length" {
                        let result = Str.truncate 8 "a long sentence"
                        Expect.isLessThanOrEqual result.Length 8 "the ellipsis counts toward the limit"
                        Expect.stringEnds result "…" "and it signals that something was cut"
                    }

                    test "handles the degenerate lengths" {
                        Expect.equal (Str.truncate 0 "abc") "" "zero gives nothing"
                        Expect.equal (Str.truncate -1 "abc") "" "negative gives nothing"
                        Expect.equal (Str.truncate 1 "abc") "…" "one gives just the ellipsis"
                        Expect.equal (Str.truncate 3 "abc") "abc" "exactly the length is not truncated"
                    }

                    testProperty "truncate never returns more characters than asked for"
                    <| fun (text: string) (length: byte) ->
                        let limit = int length % 40
                        (Str.truncate limit text).Length <= max limit 0
                ]

            testList
                "stripping"
                [
                    test "stripPrefix removes only what is there" {
                        Expect.equal (Str.stripPrefix "Etymon." "Etymon.Core") "Core" "prefix removed"
                        Expect.equal (Str.stripPrefix "Etymon." "FsCheck") "FsCheck" "absent prefix changes nothing"
                        Expect.equal (Str.stripPrefix "" "abc") "abc" "an empty prefix changes nothing"
                    }

                    test "stripSuffix removes only what is there" {
                        Expect.equal (Str.stripSuffix ".fs" "Parse.fs") "Parse" "suffix removed"
                        Expect.equal (Str.stripSuffix ".fs" "Parse.fsi") "Parse.fsi" "absent suffix changes nothing"
                        Expect.equal (Str.stripSuffix "" "abc") "abc" "an empty suffix changes nothing"
                    }

                    testProperty "stripping a prefix that is present shortens by exactly its length"
                    <| fun (prefix: string) (rest: string) ->
                        let prefix = Str.orEmpty prefix
                        let rest = Str.orEmpty rest
                        Str.stripPrefix prefix (prefix + rest) = rest
                ]

            testList
                "null safety"
                [
                    testProperty "no Str function throws on any input"
                    <| fun (text: string) (other: string) ->
                        Str.trim text |> ignore
                        Str.toLower text |> ignore
                        Str.toUpper text |> ignore
                        Str.split ',' text |> ignore
                        Str.splitFirst '=' text |> ignore
                        Str.truncate 5 text |> ignore
                        Str.stripPrefix (Str.orEmpty other) text |> ignore
                        Str.stripSuffix (Str.orEmpty other) text |> ignore
                        Str.contains (Str.orEmpty other) text |> ignore
                        true
                ]
        ]
