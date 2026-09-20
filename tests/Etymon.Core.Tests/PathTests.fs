module Etymon.Core.Tests.PathTests

open Expecto
open Etymon

let tests =
    testList
        "Path"
        [
            test "the root renders as the empty string" {
                Expect.equal (Path.toString Path.root) "" "root has no segments to render"
            }

            test "toStringOr substitutes a label for the root" {
                Expect.equal (Path.toStringOr "(value)" Path.root) "(value)" "root should use the label"

                Expect.equal
                    (Path.root |> Path.field "a" |> Path.toStringOr "(value)")
                    "a"
                    "a non-root path ignores the label"
            }

            test "nested fields render dotted" {
                let path = Path.root |> Path.field "address" |> Path.field "zip"
                Expect.equal (Path.toString path) "address.zip" "fields join with a dot"
            }

            test "an index renders in brackets" {
                let path = Path.root |> Path.field "items" |> Path.index 3
                Expect.equal (Path.toString path) "items[3]" "indexes use brackets, not dots"
            }

            test "fields and indexes mix" {
                let path = Path.root |> Path.field "items" |> Path.index 3 |> Path.field "name"

                Expect.equal (Path.toString path) "items[3].name" "no dot between a field and its index"
            }

            test "a leading index renders without a dot" {
                let path = Path.root |> Path.index 0 |> Path.field "name"
                Expect.equal (Path.toString path) "[0].name" "a root-level index needs no separator"
            }

            test "a key that is not a bare identifier is quoted" {
                let path = Path.root |> Path.field "headers" |> Path.field "content.type"

                Expect.equal
                    (Path.toString path)
                    "headers[\"content.type\"]"
                    "a dot inside a key must not read as nesting"
            }

            test "quoting escapes quotes and backslashes" {
                let path = Path.root |> Path.field "a\"b\\c"

                Expect.equal (Path.toString path) "[\"a\\\"b\\\\c\"]" "the rendered form must be unambiguous"
            }

            test "an empty key is quoted rather than vanishing" {
                let path = Path.root |> Path.field "a" |> Path.field ""
                Expect.equal (Path.toString path) "a[\"\"]" "an empty key must still be visible"
            }

            test "hyphenated keys stay bare" {
                let path = Path.root |> Path.field "headers" |> Path.field "content-type"
                Expect.equal (Path.toString path) "headers.content-type" "hyphens are common and unambiguous"
            }

            test "segments reads outermost first" {
                let path = Path.root |> Path.field "a" |> Path.index 0 |> Path.field "b"

                Expect.equal
                    (Path.segments path)
                    [ PathSegment.Field "a"; PathSegment.Index 0; PathSegment.Field "b" ]
                    "segments should read in the same order as the rendered path"
            }

            test "ofSegments is the inverse of segments" {
                let segments = [ PathSegment.Field "a"; PathSegment.Index 0; PathSegment.Field "b" ]

                Expect.equal (Path.ofSegments segments |> Path.segments) segments "round-trip should be exact"
            }

            test "combine nests one path under another" {
                let outer = Path.root |> Path.field "address"
                let inner = Path.root |> Path.field "zip"

                Expect.equal (Path.combine outer inner |> Path.toString) "address.zip" "inner goes underneath outer"
            }

            test "combining with the root changes nothing" {
                let path = Path.root |> Path.field "a" |> Path.index 1

                Expect.equal (Path.combine Path.root path) path "the root is a left identity"
                Expect.equal (Path.combine path Path.root) path "the root is a right identity"
            }

            test "parent drops the last step" {
                let path = Path.root |> Path.field "a" |> Path.field "b"

                Expect.equal (Path.parent path |> Option.map Path.toString) (Some "a") "parent of a.b is a"
                Expect.equal (Path.parent Path.root) None "the root has no parent"
            }

            test "depth and isRoot agree" {
                Expect.isTrue (Path.isRoot Path.root) "the root is the root"
                Expect.equal (Path.depth Path.root) 0 "the root has no steps"

                let path = Path.root |> Path.field "a" |> Path.index 0
                Expect.isFalse (Path.isRoot path) "a stepped path is not the root"
                Expect.equal (Path.depth path) 2 "two steps were taken"
            }

            test "paths compare structurally" {
                let a = Path.root |> Path.field "x" |> Path.index 1
                let b = Path.root |> Path.field "x" |> Path.index 1
                Expect.equal a b "equal construction means equal value"
            }

            testProperty "segments and ofSegments round-trip"
            <| fun (names: string list) ->
                let segments = names |> List.map PathSegment.Field
                Path.ofSegments segments |> Path.segments = segments

            testProperty "depth counts the segments given"
            <| fun (count: byte) ->
                let n = int count % 32

                let path = List.fold (fun p i -> Path.index i p) Path.root [ 0 .. n - 1 ]

                Path.depth path = n
        ]
